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

    internal override object CreateJavascriptData() => Pack();

    internal MeshUpload Pack()
    {
        var vertexList = Vertices;
        if (PrimitiveSurfaceNormals.Count != 0 && PrimitiveSurfaceNormals.Count != vertexList.Count)
        {
            throw new InvalidOperationException(
                $"Primitive surface normal count {PrimitiveSurfaceNormals.Count} does not match vertex count {vertexList.Count}.");
        }

        var colors = Colors as IList<ColorRgba> ?? Colors.ToArray();
        var expanded = ColorMode == MeshColoring.PerTriangle;
        if (expanded && colors.Count != Indices.Count)
            throw new InvalidOperationException($"Color count {colors.Count} does not match expected per-triangle color count {Indices.Count}.");

        var indexCount = checked(Indices.Count * 3);
        var outputVertexCount = expanded ? indexCount : vertexList.Count;
        var positions = new float[checked(outputVertexCount * 3)];
        float[] normals = PrimitiveSurfaceNormals.Count == 0 ? [] : new float[positions.Length];
        var indices = new int[indexCount];
        var packedColors = new float[checked((expanded ? indexCount : colors.Count) * 4)];

        if (!expanded)
        {
            for (var i = 0; i < vertexList.Count; i++)
            {
                WriteVector(positions, i * 3, vertexList[i]);
                if (normals.Length != 0) WriteVector(normals, i * 3, PrimitiveSurfaceNormals[i]);
            }
            for (var i = 0; i < colors.Count; i++) WriteColor(packedColors, i * 4, colors[i]);
        }

        for (var face = 0; face < Indices.Count; face++)
        {
            var (a, b, c) = Indices[face];
            for (var corner = 0; corner < 3; corner++)
            {
                var sourceIndex = corner == 0 ? a : corner == 1 ? b : c;
                var destinationIndex = face * 3 + corner;
                indices[destinationIndex] = expanded ? destinationIndex : sourceIndex;
                if (!expanded) continue;
                WriteVector(positions, destinationIndex * 3, vertexList[sourceIndex]);
                if (normals.Length != 0) WriteVector(normals, destinationIndex * 3, PrimitiveSurfaceNormals[sourceIndex]);
                WriteColor(packedColors, destinationIndex * 4, colors[face]);
            }
        }

        return new(Id, positions, indices, packedColors, normals, ColorMode == MeshColoring.UniformColor);
    }

    private static void WriteVector(float[] output, int offset, Vector3 value)
    {
        output[offset] = value.X;
        output[offset + 1] = value.Y;
        output[offset + 2] = value.Z;
    }

    private static void WriteColor(float[] output, int offset, ColorRgba value)
    {
        output[offset] = value.R / 255f;
        output[offset + 1] = value.G / 255f;
        output[offset + 2] = value.B / 255f;
        output[offset + 3] = value.A / 255f;
    }
}

internal sealed record MeshUpload(string Id, float[] Vertices, int[] Indices,
    float[] Colors, float[] PrimitiveSurfaceNormals, bool SingleColor);
