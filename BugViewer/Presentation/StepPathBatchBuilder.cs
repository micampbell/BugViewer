using NumericsVector3 = System.Numerics.Vector3;

namespace BugViewer;

/// <summary>Builds a line buffer from the visible portion of an indexed path timeline.</summary>
public static class StepPathBatchBuilder
{
    /// <summary>Resolves and transforms paths for a step, or returns null when no segments are visible.</summary>
    public static LineData? Build(SceneStepGroup group, SceneStepPathSource source, int stepIndex)
    {
        var transforms = group.Transforms;
        SceneTransform? transform = transforms is not null && stepIndex < transforms.Count
            && transforms[stepIndex] is not null
            ? transforms[stepIndex]
            : null;
        var vertices = new List<NumericsVector3>();
        var thicknesses = new List<float>();
        var colors = new List<ColorRgba>();
        var fadeFactors = new List<float>();

        foreach (var pathIndex in group.ResolvePathIndices(stepIndex))
        {
            var path = source.Paths[pathIndex];
            if (path is null)
                continue;
            var pathVertices = path.Select(vertex => transform?.Transform(vertex) ?? ToNumerics(vertex))
                .ToList();
            if (pathVertices.Count < 2)
                continue;
            if (source.CloseAt(pathIndex))
                pathVertices.Add(pathVertices[0]);

            var pathColor = source.ColorAt(pathIndex);
            var color = new ColorRgba(pathColor.R, pathColor.G, pathColor.B, pathColor.A);
            var sourceThickness = source.ThicknessAt(pathIndex);
            var thickness = sourceThickness < 0 ? LineData.AutomaticThickness : (float)sourceThickness;
            if (vertices.Count == 0)
                vertices.Add(pathVertices[0]);
            else
            {
                vertices.Add(pathVertices[0]);
                thicknesses.Add(0f);
                colors.Add(color);
                fadeFactors.Add(0f);
            }

            for (var vertexIndex = 1; vertexIndex < pathVertices.Count; vertexIndex++)
            {
                vertices.Add(pathVertices[vertexIndex]);
                thicknesses.Add(thickness);
                colors.Add(color);
                fadeFactors.Add(0f);
            }
        }

        if (vertices.Count < 2 || thicknesses.All(thickness => thickness == 0f))
            return null;
        return new LineData
        {
            Id = source.Id,
            Vertices = vertices,
            Thicknesses = thicknesses,
            Colors = colors,
            FadeFactors = fadeFactors
        };
    }

    private static NumericsVector3 ToNumerics(ScenePoint position)
        => new((float)position.X, (float)position.Y, (float)position.Z);
}
