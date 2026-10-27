using System.Numerics;

namespace BugViewer;

/// <summary>Shared conversion of presentation data to a renderable frame.</summary>
public static class SceneGeometry
{
    /// <summary>Resolves visible elements using the selected transform and the most recent null-transform barrier.</summary>
    public static IEnumerable<(T Element, SceneTransform? Transform)> ResolveStepElements<T>(
        SceneStepGroup group, IReadOnlyList<T?> elements, int stepIndex) where T : class
    {
        if (stepIndex < 0) yield break;
        var transforms = group.Transforms;
        if (transforms is null)
        {
            foreach (var element in elements)
                if (element is not null) yield return (element, null);
            yield break;
        }
        if (stepIndex >= transforms.Count || transforms[stepIndex] is null)
        {
            if (stepIndex < elements.Count && elements[stepIndex] is { } element) yield return (element, null);
            yield break;
        }
        var lowerBound = group.LastNullTransformAtOrBefore(stepIndex) + 1;
        for (var index = Math.Min(stepIndex, elements.Count - 1); index >= lowerBound; index--)
            if (elements[index] is { } element) yield return (element, transforms[stepIndex]);
    }

    /// <summary>Expands a uniform mesh color to triangle colors; other modes retain their existing color sequence.</summary>
    public static List<ColorRgba> ExpandedColors(SceneMesh mesh) => mesh.HasUniformColor
        ? Enumerable.Repeat(mesh.Colors.Count > 0 ? mesh.Colors[0] : ColorRgba.LightGray, mesh.Triangles.Count).ToList()
        : mesh.Colors.ToList();

    /// <summary>Converts a source mesh to a frame, applying positions and inverse-transpose normals.</summary>
    public static MeshData ToMeshData(SceneMesh mesh, SceneTransform? transform = null)
    {
        var normalTransform = SceneTransform.Identity;
        var valid = transform is null || transform.Value.TryNormalTransform(out normalTransform);
        return new MeshData
        {
            Id = mesh.Id,
            Vertices = mesh.Vertices.Select(v => transform?.Transform(new(v[0],v[1],v[2])) ?? new Vector3(v[0],v[1],v[2])).ToList(),
            Indices = mesh.Triangles.Select(t => (t[0],t[1],t[2])).ToList(),
            PrimitiveSurfaceNormals = mesh.PrimitiveSurfaceNormals.Select(n => valid ? normalTransform.TransformNormal(n) : Vector3.Zero).ToList(),
            HasPrimitiveSurfaces = mesh.HasPrimitiveSurfaces,
            // Face inspection updates triangle colors, including meshes whose source color is uniform.
            Colors = mesh.ColorMode == MeshColoring.PerVertex ? mesh.Colors.ToList() : ExpandedColors(mesh),
            ColorMode = mesh.ColorMode == MeshColoring.PerVertex ? MeshColoring.PerVertex : MeshColoring.PerTriangle
        };
    }
}
