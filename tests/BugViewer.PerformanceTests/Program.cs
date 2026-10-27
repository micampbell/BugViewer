using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using BugViewer;
using SimpleStandalone.Benchmarks;

var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
var random = new Random(1741);
foreach (var mode in Enum.GetValues<MeshColoring>())
{
    for (var trial = 0; trial < 30; trial++)
    {
        var mesh = new MeshData
        {
            Id = "test", ColorMode = mode,
            Vertices = Enumerable.Range(0, 17).Select(_ => new Vector3(random.NextSingle(), random.NextSingle(), random.NextSingle())).ToArray(),
            Indices = Enumerable.Range(0, 29).Select(_ => (random.Next(17), random.Next(17), random.Next(17))).ToArray(),
            PrimitiveSurfaceNormals = Enumerable.Repeat(Vector3.UnitZ, 17).ToArray(),
            Colors = Enumerable.Range(0, mode == MeshColoring.PerTriangle ? 29 : mode == MeshColoring.UniformColor ? 1 : 17)
                .Select(_ => new ColorRgba((byte)random.Next(256), 123, 210, (byte)random.Next(256))).ToArray()
        };
        var expected = JsonSerializer.Serialize(BenchmarkScenes.LegacyPack(mesh), jsonOptions);
        Assert(expected == JsonSerializer.Serialize(mesh.Pack(), jsonOptions), $"Packing changed {mode}");
        var packed = mesh.Pack();
        var binary = BinarySceneUpload.Create([packed], []);
        var metadata = binary.Meshes[0];
        void CheckFloats(float[] values, int offset)
        {
            Assert(offset % 4 == 0, "Unaligned section");
            for (var i = 0; i < values.Length; i++)
                Assert(values[i] == BinaryPrimitives.ReadSingleLittleEndian(binary.Bytes.AsSpan(offset + i * 4)), "Float mismatch");
        }
        CheckFloats(packed.Vertices, metadata.VertexOffset);
        CheckFloats(packed.Colors, metadata.ColorOffset);
        CheckFloats(packed.PrimitiveSurfaceNormals, metadata.NormalOffset);
        for (var i = 0; i < packed.Indices.Length; i++)
            Assert(packed.Indices[i] == BinaryPrimitives.ReadUInt16LittleEndian(binary.Bytes.AsSpan(metadata.IndexOffset + i * 2)), "Index mismatch");
        var noNormals = mesh with { PrimitiveSurfaceNormals = [] };
        Assert(noNormals.Pack().PrimitiveSurfaceNormals.Length == 0, "Empty normal fallback changed");
        Assert(mesh.Vertices.Count == 17 && mesh.Indices.Count == 29, "Input mutated");
    }
}
foreach (var maximum in new[] { 65535, 65536 })
{
    var upload = new MeshUpload("boundary", [1, 2, 3], [0, maximum, 1], [1, 1, 1, 1], [], true);
    var binary = BinarySceneUpload.Create([upload, upload], []);
    var mesh = binary.Meshes[0];
    Assert(mesh.Index32 == (maximum > 65535), "Index-width boundary changed");
    Assert(binary.Meshes[1].VertexOffset % 4 == 0, "Odd uint16 index count misaligned next mesh");
    Assert(maximum == (mesh.Index32 ? BinaryPrimitives.ReadUInt32LittleEndian(binary.Bytes.AsSpan(mesh.IndexOffset + 4))
        : BinaryPrimitives.ReadUInt16LittleEndian(binary.Bytes.AsSpan(mesh.IndexOffset + 2))), "Wide index mismatch");
}
var invalid = new MeshData { Id = "invalid", Vertices = [Vector3.Zero], Indices = [(0,0,0)], ColorMode = MeshColoring.PerTriangle, Colors = [] };
try { invalid.Pack(); throw new Exception("Missing colors accepted"); } catch (InvalidOperationException) { }
try { (invalid with { PrimitiveSurfaceNormals = [Vector3.Zero, Vector3.Zero] }).Pack(); throw new Exception("Invalid normals accepted"); } catch (InvalidOperationException) { }
var path = new LineData { Id = "line", Vertices = [Vector3.Zero, Vector3.One, new(2)], Colors = [ColorRgba.Red], Thicknesses = [1, 0], FadeFactors = [2] };
var line = (LineUpload)path.CreateJavascriptData();
Assert(line.Segments.Length == 12 && line.Segments[11] == 1, "Line filtering/fade changed");
var lineBinary = BinarySceneUpload.Create([], [line]);
Assert(lineBinary.Lines[0].Count == 12 && lineBinary.Lines[0].Center.SequenceEqual(line.Center), "Line metadata changed");
Assert(BinaryPrimitives.ReadSingleLittleEndian(lineBinary.Bytes.AsSpan(24)) == ColorRgba.Red.R / 255f, "Line color changed");
Console.WriteLine("PASS: 90 randomized mesh equivalence cases, absent/invalid normals, color validation, index boundaries, binary alignment, and lines.");
Console.WriteLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
foreach (var workload in BenchmarkScenes.Names.Where(name => name != "Toolpaths"))
{
    var scene = BenchmarkScenes.Create(workload);
    foreach (var legacy in new[] { true, false })
    {
        object Pack() => legacy ? scene.Meshes.Select(BenchmarkScenes.LegacyPack).ToArray()
            : scene.Meshes.Select(mesh => (object)mesh.Pack()).ToArray();
        for (var i = 0; i < 5; i++) GC.KeepAlive(Pack());
        var times = new List<double>();
        long bytes = 0;
        for (var i = 0; i < 9; i++)
        {
            var startBytes = GC.GetAllocatedBytesForCurrentThread();
            var timer = Stopwatch.StartNew();
            GC.KeepAlive(Pack());
            times.Add(timer.Elapsed.TotalMilliseconds);
            bytes += GC.GetAllocatedBytesForCurrentThread() - startBytes;
        }
        times.Sort();
        Console.WriteLine($"{workload}, {(legacy ? "legacy" : "packed")}: median {times[4]:F2} ms, allocation {bytes / 9:N0} bytes");
    }
}
