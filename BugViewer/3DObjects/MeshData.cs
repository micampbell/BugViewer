using System.Numerics;

namespace BugViewer;

/// <summary>
/// Represents a 3D mesh with vertices and indices for WebGPU rendering.
/// </summary>
public record MeshData : AbstractObject3D
{
    /// <summary>Triangle indices (3 indices per triangle).</summary>
    public required IList<(int a, int b, int c)> Indices { get; init; }

    public required MeshColoring ColorMode { get; set; }

    /// <summary>
    /// Gets the normal supplied by a primitive surface for each item in <see cref="AbstractObject3D.Vertices"/>.
    /// A zero vector requests the ordinary flat triangle normal for that vertex's triangles.
    /// </summary>
    public IList<Vector3> PrimitiveSurfaceNormals { get; init; } = [];

    /// <summary>Gets whether this mesh contains faces owned by one or more primitive surfaces.</summary>
    public bool HasPrimitiveSurfaces { get; init; }

    internal override object CreateJavascriptData()
    {
        var vertexList = Vertices as IList<Vector3> ?? Vertices.ToList();
        if (PrimitiveSurfaceNormals.Count != 0 && PrimitiveSurfaceNormals.Count != vertexList.Count)
        {
            throw new InvalidOperationException(
                $"Primitive surface normal count {PrimitiveSurfaceNormals.Count} does not match vertex count {vertexList.Count}.");
        }

        if (ColorMode == MeshColoring.PerTriangle)
        {
            int expectedColors = Indices.Count();
            if (Colors.Count() != expectedColors)
            {
                throw new InvalidOperationException($"Color count {Colors.Count()} does not match expected per-triangle color count {expectedColors}.");
            }
            return new
            {
                id = Id,
                vertices = Indices.SelectMany(face => TriangleIndices(face)).SelectMany(ind => Coordinates(vertexList[ind])).ToArray(),
                indices = Enumerable.Range(0, 3 * Indices.Count()).ToArray(),
                colors = Colors.SelectMany(c =>
                      ColorRgba.ToJavaScript(c).Concat(ColorRgba.ToJavaScript(c)).Concat(ColorRgba.ToJavaScript(c))).ToArray(),
                primitiveSurfaceNormals = PrimitiveSurfaceNormals.Count == 0
                    ? []
                    : Indices.SelectMany(face => TriangleIndices(face))
                        .SelectMany(ind => Coordinates(PrimitiveSurfaceNormals[ind])).ToArray(),
                singleColor = false
            };
        }
        else
        {
            return new
            {
                id = Id,
                vertices = Vertices.SelectMany(v => Coordinates(v)).ToArray(),
                indices = Indices.SelectMany(face => TriangleIndices(face)).ToArray(),
                colors = Colors.SelectMany(c => ColorRgba.ToJavaScript(c)).ToArray(),
                primitiveSurfaceNormals = PrimitiveSurfaceNormals.SelectMany(Coordinates).ToArray(),
                singleColor = ColorMode == MeshColoring.UniformColor
            };
        }
    }
}
