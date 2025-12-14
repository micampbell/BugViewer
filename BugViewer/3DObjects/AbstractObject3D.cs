using System.Drawing;
using System.Numerics;

namespace BugViewer;

/// <summary>
/// Represents a 3D mesh with vertices and indices for WebGPU rendering.
/// </summary>
public abstract record AbstractObject3D
{
    /// <summary>Vertex positions (x, y, z triplets).</summary>
    public required IEnumerable<Vector3> Vertices { get; init; }

    /// <summary>Unique identifier for this mesh instance.</summary>
    public required string Id { get; init; }

    /// <summary>
    /// Per-triangle colors (RGBA, 0-1 range).
    /// Array length should equal Indices.Length / 3 (one color per triangle).
    /// </summary>
    public IEnumerable<Color> Colors { get; set; }

    protected IEnumerable<int> TriangleIndices((int, int, int) faceIndices)
    {
        yield return faceIndices.Item1;
        yield return faceIndices.Item2;
        yield return faceIndices.Item3;
    }

    protected IEnumerable<float> Coordinates(Vector3 v)
    { yield return v.X; yield return v.Y; yield return v.Z; }

    protected IEnumerable<float> ColorToJavaScript(Color c)
    {
        yield return c.R / 255f;
        yield return c.G / 255f;
        yield return c.B / 255f;
        yield return c.A / 255f;
    }

    internal abstract object CreateJavascriptData();
}