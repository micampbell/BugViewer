using System.Text.Json.Serialization;

namespace BugViewer;

/// <summary>Selects the rendering surface.</summary>
public enum PresentationKind
{
    /// <summary>Meshes, paths, and labels.</summary>
    ThreeDimensional,
    /// <summary>2D traces and heatmaps.</summary>
    TwoDimensional,
}

/// <summary>A portable 3D or 2D presentation, including its source data, playback, and optional view state.</summary>
public partial record SceneRequest
{
    /// <summary>Identifies the request across presentation and selection events.</summary>
    public Guid RequestId { get; init; } = Guid.NewGuid();
    /// <summary>File format identifier. Use the default value when creating requests.</summary>
    [JsonRequired] public string Format { get; init; } = "BugViewer.SceneRequest";
    /// <summary>File schema version. Currently 1.</summary>
    [JsonRequired] public int Version { get; init; } = 1;
    /// <summary>Saved display settings, or null to use viewer defaults.</summary>
    public BugViewerOptions? Options { get; init; }
    /// <summary>Saved 3D camera, or null to frame the geometry automatically.</summary>
    public CameraState? Camera { get; init; }
    /// <summary>Zero-based step to display when the request is opened.</summary>
    public int InitialStep { get; init; }
    /// <summary>Text labels in the 3D scene.</summary>
    public List<TextBillboard> Billboards { get; init; } = [];
    /// <summary>Selects the 3D renderer or 2D plot viewer.</summary>
    public PresentationKind Kind { get; init; } = PresentationKind.ThreeDimensional;
    /// <summary>Additional heading displayed above the view.</summary>
    public string Heading { get; init; } = "";
    /// <summary>Presentation title.</summary>
    public string Title { get; init; } = "";
    /// <summary>Additional description displayed with the 3D view.</summary>
    public string Subtitle { get; init; } = "";
    /// <summary>Meshes displayed in this frame.</summary>
    public List<SceneMesh> Meshes { get; init; } = [];
    /// <summary>Independent paths that are batched for rendering.</summary>
    public List<ScenePath> Paths { get; init; } = [];
    /// <summary>Prebatched paths with per-segment colors, thicknesses, and fades.</summary>
    public List<LineData> PathBatches { get; init; } = [];
    /// <summary>Diagnostic point sets displayed as crossed line glyphs.</summary>
    public List<ScenePointSet> PointSets { get; init; } = [];
    /// <summary>Plot data for a 2D frame.</summary>
    public PlotRequest? Plot { get; init; }
    /// <summary>Complete frames. Use either this collection or StepGroups.</summary>
    public List<SceneRequest> Steps { get; init; } = [];
    /// <summary>Shared source geometry resolved lazily for each step. Treat source collections as immutable after publication.</summary>
    public List<SceneStepGroup> StepGroups { get; init; } = [];
    /// <summary>Controls camera refitting when geometry changes.</summary>
    public UpdateTypes AutoResetCamera { get; init; } = UpdateTypes.SphereChange;
    /// <summary>Initial border visibility when no saved Options are provided.</summary>
    public bool ShowMeshBorders { get; init; } = true;
    /// <summary>Initial mesh face display when no saved Options are provided.</summary>
    public MeshFaceDisplay ShowSurfacesAs { get; init; } = MeshFaceDisplay.Surfaces;
}

/// <summary>Indexed mesh and path sources with optional transforms and null-transform history barriers.</summary>
public sealed class SceneStepGroup
{
    /// <summary>Source meshes by step. Null entries preserve empty step slots.</summary>
    public List<SceneMesh?> Meshes { get; init; } = [];
    /// <summary>Indexed source paths and their history settings.</summary>
    public SceneStepPathSource? PathSource { get; init; }
    /// <summary>Per-step transforms. A null list displays all sources; a null entry displays only that step and starts a history barrier.</summary>
    public IReadOnlyList<SceneTransform?>? Transforms { get; init; }
    /// <summary>Number of steps across all sources and transforms.</summary>
    public int Count => Math.Max(Math.Max(Meshes.Count, PathSource?.Count ?? 0), Transforms?.Count ?? 0);

    private readonly List<int> nullTransformIndices = [];
    private int transformsInspectedThrough = -1;

    internal int LastNullTransformAtOrBefore(int stepIndex)
    {
        if (Transforms is null)
            return -1;
        var inspectionEnd = Math.Min(stepIndex, Transforms.Count - 1);
        for (var index = transformsInspectedThrough + 1; index <= inspectionEnd; index++)
            if (Transforms[index] is null)
                nullTransformIndices.Add(index);
        transformsInspectedThrough = Math.Max(transformsInspectedThrough, inspectionEnd);
        var location = nullTransformIndices.BinarySearch(stepIndex);
        if (location >= 0)
            return nullTransformIndices[location];
        location = ~location;
        return location == 0 ? -1 : nullTransformIndices[location - 1];
    }

    /// <summary>Returns source path indices visible at a step, respecting history limits and transform barriers.</summary>
    public IEnumerable<int> ResolvePathIndices(int stepIndex)
    {
        if (PathSource is not { } source || source.HistoryStepLimit == 0
            || source.Count == 0 || stepIndex < 0)
            yield break;

        if (Transforms is null)
        {
            var endIndex = source.HistoryStepLimit.HasValue
                ? Math.Min(stepIndex, source.Count - 1)
                : source.Count - 1;
            var startIndex = source.HistoryStepLimit.HasValue
                ? Math.Max(0, endIndex - source.HistoryStepLimit.Value + 1)
                : 0;
            for (var index = startIndex; index <= endIndex; index++)
                yield return index;
            yield break;
        }

        if (stepIndex >= Transforms.Count || Transforms[stepIndex] is null)
        {
            if (stepIndex < source.Count)
                yield return stepIndex;
            yield break;
        }

        var lowerBound = source.HistoryStepLimit.HasValue
            ? Math.Max(0, stepIndex - source.HistoryStepLimit.Value + 1)
            : LastNullTransformAtOrBefore(stepIndex) + 1;
        if (source.HistoryStepLimit.HasValue)
        {
            for (var index = stepIndex; index >= lowerBound; index--)
                if (Transforms[index] is null)
                {
                    lowerBound = index + 1;
                    break;
                }
        }
        for (var index = Math.Max(0, lowerBound); index <= Math.Min(stepIndex, source.Count - 1); index++)
            yield return index;
    }
}

/// <summary>Indexed double-precision paths whose coordinates are transformed only when resolving a visible step.</summary>
public sealed class SceneStepPathSource
{
    /// <summary>Stable object identifier, unique among objects of the same kind in a frame.</summary>
    public required string Id { get; init; }
    /// <summary>Source points by step. Empty paths preserve step positions.</summary>
    public required IReadOnlyList<IEnumerable<ScenePoint>> Paths { get; init; }
    /// <summary>Closure flags by step; the final value is reused for remaining steps.</summary>
    public IReadOnlyList<bool> ClosePaths { get; init; } = [];
    /// <summary>Widths by step; negative values request automatic width and the final value is reused.</summary>
    public IReadOnlyList<double> Thicknesses { get; init; } = [];
    /// <summary>Colors by step; the final value is reused for remaining steps.</summary>
    public IReadOnlyList<ColorRgba> Colors { get; init; } = [];
    /// <summary>Color used when the per-step color list is empty.</summary>
    public ColorRgba DefaultColor { get; init; } = ColorRgba.Black;
    /// <summary>Maximum retained path steps, zero for none, or null for unlimited history within a transform barrier.</summary>
    public int? HistoryStepLimit { get; init; }
    /// <summary>Number of indexed source paths.</summary>
    public int Count => Paths.Count;

    /// <summary>Gets the closure flag, falling back to the final flag or false.</summary>
    public bool CloseAt(int index) => ValueAt(ClosePaths, index, false);
    /// <summary>Gets the width, falling back to the final width or automatic sizing.</summary>
    public double ThicknessAt(int index) => ValueAt(Thicknesses, index, -1.0);
    /// <summary>Gets the color, falling back to the final color or DefaultColor.</summary>
    public ColorRgba ColorAt(int index) => ValueAt(Colors, index, DefaultColor);

    private static T ValueAt<T>(IReadOnlyList<T> values, int index, T defaultValue)
        => values.Count == 0 ? defaultValue : values[Math.Min(index, values.Count - 1)];
}

/// <summary>Portable indexed triangle mesh without references to application-specific geometry objects.</summary>
public sealed class SceneMesh
{
    /// <summary>Stable object identifier, unique among objects of the same kind in a frame.</summary>
    public required string Id { get; init; }
    /// <summary>XYZ coordinate triples.</summary>
    public List<float[]> Vertices { get; init; } = [];
    /// <summary>Zero-based vertex-index triples defining triangles.</summary>
    public List<int[]> Triangles { get; init; } = [];
    /// <summary>Optional XYZ surface normal triples, one per vertex.</summary>
    public List<float[]> PrimitiveSurfaceNormals { get; init; } = [];
    /// <summary>Whether primitive-surface display is available.</summary>
    public bool HasPrimitiveSurfaces { get; init; }
    /// <summary>Colors indexed according to ColorMode; uniform meshes may omit a color to use light gray.</summary>
    public List<ColorRgba> Colors { get; init; } = [];
    /// <summary>Specifies whether colors are uniform, per triangle, or per vertex.</summary>
    public MeshColoring ColorMode { get; init; } = MeshColoring.PerTriangle;
    /// <summary>Convenience alias for ColorMode. Only ColorMode is persisted.</summary>
    [JsonIgnore]
    public bool HasUniformColor
    {
        get => ColorMode == MeshColoring.UniformColor;
        init => ColorMode = value ? MeshColoring.UniformColor : MeshColoring.PerTriangle;
    }
}

/// <summary>A selected triangle and world-space hit point, independent of a source geometry library.</summary>
public readonly record struct SceneTriangleSelection(Guid RequestId, string MeshId, int TriangleIndex, System.Numerics.Vector3 Point);

/// <summary>One independently styled polyline.</summary>
public sealed class ScenePath
{
    /// <summary>Stable object identifier, unique among objects of the same kind in a frame.</summary>
    public required string Id { get; init; }
    /// <summary>XYZ coordinate triples.</summary>
    public List<float[]> Vertices { get; init; } = [];
    /// <summary>Path width; a negative value requests automatic sizing.</summary>
    public double Thickness { get; init; } = -1;
    /// <summary>Object color including opacity.</summary>
    public ColorRgba Color { get; init; } = ColorRgba.Black;
}

/// <summary>A collection of diagnostic points.</summary>
public sealed class ScenePointSet
{
    /// <summary>Stable object identifier, unique among objects of the same kind in a frame.</summary>
    public required string Id { get; init; }
    /// <summary>XYZ coordinate triples.</summary>
    public List<float[]> Points { get; init; } = [];
    /// <summary>Requested point radius; the current line-glyph renderer uses the viewer path scale.</summary>
    public double Radius { get; init; } = 1;
    /// <summary>Object color including opacity.</summary>
    public ColorRgba Color { get; init; } = ColorRgba.Red;
}

/// <summary>Trace and optional heatmap data for a 2D view.</summary>
public sealed class PlotRequest
{
    /// <summary>Traces drawn together, including overlays on the heatmap.</summary>
    public List<PlotTrace> Traces { get; init; } = [];
    /// <summary>Optional rectangular grid of heatmap values.</summary>
    public double[][]? Heatmap { get; init; }
    /// <summary>Records the producer's normalization preference; supply already normalized heatmap values when required.</summary>
    public bool NormalizeHeatmap { get; init; }
}

/// <summary>One named 2D trace with coordinates and styling.</summary>
public sealed class PlotTrace
{
    /// <summary>Trace name used in the legend.</summary>
    public required string Name { get; init; }
    /// <summary>Horizontal coordinates, paired with Y.</summary>
    public List<double> X { get; init; } = [];
    /// <summary>Vertical coordinates, paired with X.</summary>
    public List<double> Y { get; init; } = [];
    /// <summary>Trace rendering style.</summary>
    public ScenePlotType Type { get; init; } = ScenePlotType.Line;
    /// <summary>Whether to append the first point when rendering the trace.</summary>
    public bool Closed { get; init; }
    /// <summary>Point marker style; None suppresses markers.</summary>
    public SceneMarker Marker { get; init; } = SceneMarker.Circle;
    /// <summary>Object color including opacity.</summary>
    public ColorRgba Color { get; init; } = ColorRgba.Black;
}

/// <summary>Rendering style of a 2D trace.</summary>
public enum ScenePlotType
{
    /// <summary>Connected line segments.</summary>
    Line,
    /// <summary>Point markers.</summary>
    Scatter,
    /// <summary>Bars.</summary>
    Bar,
    /// <summary>Heatmap style; grid values are provided through PlotRequest.Heatmap.</summary>
    Heatmap,
    /// <summary>A line filled to zero.</summary>
    Area,
}
/// <summary>Marker symbol for a 2D trace.</summary>
public enum SceneMarker
{
    /// <summary>No markers.</summary>
    None,
    /// <summary>Circular marker.</summary>
    Circle,
    /// <summary>Square marker.</summary>
    Square,
    /// <summary>Diamond marker.</summary>
    Diamond,
    /// <summary>Upward triangle.</summary>
    Triangle,
    /// <summary>Diagonal cross.</summary>
    Cross,
    /// <summary>Plus sign.</summary>
    Plus,
    /// <summary>Star marker.</summary>
    Star,
}

/// <summary>Double-precision source coordinates, converted to floats only for rendering.</summary>
public readonly record struct ScenePoint(double X, double Y, double Z);
