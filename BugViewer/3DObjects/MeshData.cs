using System.Numerics;
using System.Runtime.InteropServices;

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
        var vertices = AsSpan(Vertices);
        var sourceNormals = AsSpan(PrimitiveSurfaceNormals);
        var faces = AsSpan(Indices);
        if (sourceNormals.Length != 0 && sourceNormals.Length != vertices.Length)
            throw new InvalidOperationException($"Primitive surface normal count {sourceNormals.Length} does not match vertex count {vertices.Length}.");
        var colors = AsSpan(Colors as IList<ColorRgba> ?? Colors.ToArray());
        var expanded = ColorMode == MeshColoring.PerTriangle;
        if (expanded && colors.Length != faces.Length)
            throw new InvalidOperationException($"Color count {colors.Length} does not match expected per-triangle color count {faces.Length}.");

        var indexCount = checked(faces.Length * 3);
        var outputVertexCount = expanded ? indexCount : vertices.Length;
        var positions = new float[checked(outputVertexCount * 3)];
        float[] normals = sourceNormals.IsEmpty ? [] : new float[positions.Length];
        var indices = new int[indexCount];
        var packedColors = new float[checked((expanded ? indexCount : colors.Length) * 4)];
        var destinationVertices = MemoryMarshal.Cast<float, Vector3>(positions.AsSpan());
        var destinationNormals = MemoryMarshal.Cast<float, Vector3>(normals.AsSpan());
        var destinationColors = MemoryMarshal.Cast<float, Vector4>(packedColors.AsSpan());

        if (!expanded)
        {
            vertices.CopyTo(destinationVertices);
            sourceNormals.CopyTo(destinationNormals);
            for (var index = 0; index < colors.Length; index++)
            {
                var color = colors[index];
                destinationColors[index] = new(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
            }
        }
        for (var face = 0; face < faces.Length; face++)
        {
            var (a, b, c) = faces[face];
            var destination = face * 3;
            if (!expanded)
            {
                indices[destination] = a; indices[destination + 1] = b; indices[destination + 2] = c;
                continue;
            }
            // Copy complete attributes and compute the face color once, avoiding
            // repeated interface dispatch and scalar helper calls in interpreted WASM.
            destinationVertices[destination] = vertices[a];
            destinationVertices[destination + 1] = vertices[b];
            destinationVertices[destination + 2] = vertices[c];
            if (!sourceNormals.IsEmpty)
            {
                destinationNormals[destination] = sourceNormals[a];
                destinationNormals[destination + 1] = sourceNormals[b];
                destinationNormals[destination + 2] = sourceNormals[c];
            }
            var color = colors[face];
            var rgba = new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
            destinationColors[destination] = destinationColors[destination + 1] = destinationColors[destination + 2] = rgba;
            indices[destination] = destination; indices[destination + 1] = destination + 1; indices[destination + 2] = destination + 2;
        }
        return new(Id, positions, indices, packedColors, normals, ColorMode == MeshColoring.UniformColor);
    }

    private static ReadOnlySpan<T> AsSpan<T>(IList<T> values) => values switch
    {
        T[] array => array,
        List<T> list => CollectionsMarshal.AsSpan(list),
        _ => values.ToArray()
    };

}

internal sealed record MeshUpload(string Id, float[] Vertices, int[] Indices,
    float[] Colors, float[] PrimitiveSurfaceNormals, bool SingleColor);
