using System.Numerics;

namespace BugViewer;

/// <summary>
/// Internal renderer frame used to validate and replace geometry, labels, settings, and camera together.
/// Public persistence uses SceneRequest so source data and playback steps are retained.
/// </summary>
internal sealed class SceneSnapshot
{
    /// <summary>Meshes, including colors and primitive surface normals.</summary>
    public List<MeshData> Meshes { get; init; } = [];

    /// <summary>User paths. Viewer-generated mesh edges are regenerated on load.</summary>
    public List<LineData> Lines { get; init; } = [];

    /// <summary>Text labels and their placement.</summary>
    public List<TextBillboard> Billboards { get; init; } = [];

    /// <summary>Display and interaction settings.</summary>
    public BugViewerOptions Options { get; init; } = BugViewerOptions.Default.Clone();

    /// <summary>The saved view, or null to fit the camera to the loaded geometry.</summary>
    public CameraState? Camera { get; init; }

    internal SceneSnapshot Snapshot() => new()
    {
        Meshes = Meshes.Select(mesh => mesh with
        {
            Vertices = mesh.Vertices.ToArray(), Indices = mesh.Indices.ToArray(),
            Colors = mesh.Colors.ToArray(), PrimitiveSurfaceNormals = mesh.PrimitiveSurfaceNormals.ToArray()
        }).ToList(),
        Lines = Lines.Select(line => line with
        {
            Vertices = line.Vertices.ToArray(), Colors = line.Colors.ToArray(),
            Thicknesses = line.Thicknesses.ToArray(), FadeFactors = line.FadeFactors.ToArray()
        }).ToList(),
        Billboards = Billboards.Select(label => label with
        {
            Vertices = label.Vertices.ToArray(), Colors = label.Colors.ToArray()
        }).ToList(),
        Options = Options.Clone(),
        Camera = Camera
    };

    internal void Validate()
    {
        if (Meshes is null || Lines is null || Billboards is null || Options is null)
            throw new InvalidDataException("Scene collections and options cannot be null.");

        ValidateObjects(Meshes);
        ValidateObjects(Lines);
        ValidateObjects(Billboards);
        foreach (var mesh in Meshes)
        {
            if (mesh.Indices is null || mesh.PrimitiveSurfaceNormals is null || !Enum.IsDefined(mesh.ColorMode))
                throw new InvalidDataException($"Mesh '{mesh.Id}' has invalid indices, normals, or color mode.");
            foreach (var (a, b, c) in mesh.Indices)
                if ((uint)a >= mesh.Vertices.Count || (uint)b >= mesh.Vertices.Count || (uint)c >= mesh.Vertices.Count)
                    throw new InvalidDataException($"Mesh '{mesh.Id}' has a triangle index outside its vertices.");
            var colorCount = mesh.ColorMode switch
            {
                MeshColoring.UniformColor => 1,
                MeshColoring.PerVertex => mesh.Vertices.Count,
                _ => mesh.Indices.Count
            };
            if (mesh.Colors.Count() != colorCount)
                throw new InvalidDataException($"Mesh '{mesh.Id}' requires {colorCount} colors for {mesh.ColorMode}.");
            if (mesh.PrimitiveSurfaceNormals.Count != 0 && mesh.PrimitiveSurfaceNormals.Count != mesh.Vertices.Count
                || mesh.PrimitiveSurfaceNormals.Any(normal => !IsFinite(normal)))
                throw new InvalidDataException($"Mesh '{mesh.Id}' has invalid primitive surface normals.");
        }
        foreach (var line in Lines)
        {
            if (line.Id.StartsWith("__mesh-edges-", StringComparison.Ordinal)
                || line.Id.StartsWith("__mesh-borders-", StringComparison.Ordinal))
                throw new InvalidDataException($"Line '{line.Id}' uses an ID reserved for generated mesh edges.");
            if (line.Thicknesses is null || line.FadeFactors is null
                || line.Thicknesses.Count() < Math.Max(0, line.Vertices.Count - 1)
                || line.Thicknesses.Any(value => !float.IsFinite(value))
                || line.FadeFactors.Any(value => !float.IsFinite(value)))
                throw new InvalidDataException($"Line '{line.Id}' has invalid thicknesses or fade factors.");
        }
        foreach (var label in Billboards)
            if (label.Vertices.Count != 1 || label.Text is null || !float.IsFinite(label.Scale) || label.Scale <= 0
                || !float.IsFinite(label.RelativeX) || !float.IsFinite(label.RelativeY))
                throw new InvalidDataException($"Billboard '{label.Id}' has invalid placement, text, or scale.");

        if (Options.MinPolar > Options.MaxPolar || Options.MinAzimuth > Options.MaxAzimuth
            || Options.MinDistance > Options.MaxDistance || Options.Fov <= 0 || Options.Fov >= 180
            || Options.OrthoSize <= 0 || Options.ZNear <= 0 || Options.ZFar <= Options.ZNear)
            throw new InvalidDataException("Scene camera options are invalid.");
        if (Camera is { } camera && (!IsFinite(camera.Target) || !double.IsFinite(camera.AzimuthAngle)
            || !double.IsFinite(camera.PolarAngle)
            || !double.IsFinite(camera.Distance) || camera.Distance <= 0
            || !double.IsFinite(camera.OrthographicSize) || camera.OrthographicSize <= 0))
            throw new InvalidDataException("The saved camera is invalid.");
    }

    private static void ValidateObjects<T>(IEnumerable<T> objects) where T : AbstractObject3D
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in objects)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id))
                throw new InvalidDataException("Scene objects must have nonempty, unique IDs within each object type.");
            if (item.Vertices is null || item.Colors is null || item.Vertices.Any(vertex => !IsFinite(vertex)))
                throw new InvalidDataException($"Object '{item.Id}' has invalid vertices or colors.");
        }
    }

    private static bool IsFinite(Vector3 vector) =>
        float.IsFinite(vector.X) && float.IsFinite(vector.Y) && float.IsFinite(vector.Z);
}

