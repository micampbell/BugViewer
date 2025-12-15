using System.Drawing;
using System.Numerics;

namespace BugViewer;

/// <summary>
/// Represents a 3D mesh with vertices and indices for WebGPU rendering.
/// </summary>
public record MeshData : AbstractObject3D
{
    /// <summary>Triangle indices (3 indices per triangle).</summary>
    public required IList<(int a, int b, int c)> Indices { get; init; }

    public required MeshColoring ColorMode { get; init; }
    

    internal override object CreateJavascriptData()
    {
        if (ColorMode == MeshColoring.PerTriangle)
        {
            int expectedColors = Indices.Count();
            if (Colors.Count() != expectedColors)
            {
                throw new InvalidOperationException($"Color count {Colors.Count()} does not match expected per-triangle color count {expectedColors}.");
            }
            var vertexList = Vertices as IList<Vector3> ?? Vertices.ToList();

            return new
            {
                id = Id,
                vertices = Indices.SelectMany(face => TriangleIndices(face)).SelectMany(ind => Coordinates(vertexList[ind])).ToArray(),
                indices = Enumerable.Range(0, 3 * Indices.Count()).ToArray(),
                colors = Colors.SelectMany(c =>
                      ColorToJavaScript(c).Concat(ColorToJavaScript(c)).Concat(ColorToJavaScript(c))).ToArray(),
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
                colors = Colors.SelectMany(c => ColorToJavaScript(c)).ToArray(),
                singleColor = ColorMode == MeshColoring.UniformColor
            };
        }
    }
}
