using System.Numerics;
using BugViewer;

namespace SimpleStandalone.Benchmarks;

internal sealed record BenchmarkScene(MeshData[] Meshes, LineData[] Lines);
internal static class BenchmarkScenes
{
    internal static readonly string[] Names = ["Large mesh", "Many meshes", "Triangle colors", "Toolpaths"];
    internal static BenchmarkScene Create(string name)
    {
        if (name == "Toolpaths")
            return new([], Enumerable.Range(0, 64).Select(path => new LineData
            {
                Id = $"path-{path}",
                Vertices = Enumerable.Range(0, 2049).Select(i => new Vector3(
                    (float)Math.Cos(i * .03) * (2 + path * .1f),
                    (float)Math.Sin(i * .03) * (2 + path * .1f), i * .005f - 5)).ToArray(),
                Colors = [new ColorRgba(60, 140, 220)],
                Thicknesses = Enumerable.Repeat(.02f, 2048).ToArray(),
                FadeFactors = []
            }).ToArray());
        var many = name == "Many meshes";
        var triangleColors = name == "Triangle colors";
        return new(Enumerable.Range(0, many ? 512 : 1)
            .Select(i => Grid($"mesh-{i}", many ? 8 : 256, i, many, triangleColors)).ToArray(), []);
    }

    private static MeshData Grid(string id, int size, int tile, bool many, bool triangleColors)
    {
        var vertices = new Vector3[(size + 1) * (size + 1)];
        for (var y = 0; y <= size; y++)
            for (var x = 0; x <= size; x++)
                vertices[y * (size + 1) + x] = new(
                    many ? (tile % 32 - 16) * .6f + x * .05f : x * 20f / size - 10,
                    many ? (tile / 32 - 8) * .6f + y * .05f : y * 20f / size - 10,
                    MathF.Sin(x * .1f) * MathF.Cos(y * .1f) * (many ? .1f : 1));
        var indices = new (int, int, int)[size * size * 2];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var a = y * (size + 1) + x;
                indices[(y * size + x) * 2] = (a, a + 1, a + size + 1);
                indices[(y * size + x) * 2 + 1] = (a + 1, a + size + 2, a + size + 1);
            }
        return new MeshData
        {
            Id = id, Vertices = vertices, Indices = indices,
            ColorMode = triangleColors ? MeshColoring.PerTriangle : MeshColoring.UniformColor,
            Colors = triangleColors ? Enumerable.Range(0, indices.Length)
                .Select(i => new ColorRgba((byte)(i % 255), 160, 220, 180)).ToArray() : [new ColorRgba(80, 160, 210)],
            PrimitiveSurfaceNormals = Enumerable.Repeat(Vector3.UnitZ, vertices.Length).ToArray()
        };
    }

    // Frozen pre-optimization mesh packing for a meaningful before/after comparison.
    internal static object LegacyPack(MeshData mesh)
    {
        static IEnumerable<int> Indices((int a, int b, int c) face) { yield return face.a; yield return face.b; yield return face.c; }
        static IEnumerable<float> Coordinates(Vector3 v) { yield return v.X; yield return v.Y; yield return v.Z; }
        var expanded = mesh.ColorMode == MeshColoring.PerTriangle;
        return new
        {
            id = mesh.Id,
            vertices = expanded ? mesh.Indices.SelectMany(Indices).SelectMany(i => Coordinates(mesh.Vertices[i])).ToArray()
                : mesh.Vertices.SelectMany(Coordinates).ToArray(),
            indices = expanded ? Enumerable.Range(0, mesh.Indices.Count * 3).ToArray()
                : mesh.Indices.SelectMany(Indices).ToArray(),
            colors = expanded ? mesh.Colors.SelectMany(c => ColorRgba.ToJavaScript(c)
                .Concat(ColorRgba.ToJavaScript(c)).Concat(ColorRgba.ToJavaScript(c))).ToArray()
                : mesh.Colors.SelectMany(c => ColorRgba.ToJavaScript(c)).ToArray(),
            primitiveSurfaceNormals = expanded ? mesh.Indices.SelectMany(Indices)
                .SelectMany(i => Coordinates(mesh.PrimitiveSurfaceNormals[i])).ToArray()
                : mesh.PrimitiveSurfaceNormals.SelectMany(Coordinates).ToArray(),
            singleColor = mesh.ColorMode == MeshColoring.UniformColor
        };
    }
}
