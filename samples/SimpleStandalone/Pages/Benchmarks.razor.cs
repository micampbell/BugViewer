using System.Diagnostics;
using System.Text.Json;
using BugViewer;
using Microsoft.AspNetCore.Components;
using SimpleStandalone.Benchmarks;

namespace SimpleStandalone.Pages;

public partial class Benchmarks
{
    private ElementReference canvas;
    private BenchmarkInterop? interop;
    private bool running;
    private bool disposed;
    private string status = "Ready";
    private string report = "";
    private string summary = "";
    private readonly List<object> results = [];

    protected override void OnInitialized() => interop = new(JS);

    private Task DownloadAsync() => interop!.DownloadAsync(report).AsTask();

    private async Task RunAsync()
    {
        running = true;
        summary = "";
        results.Clear();
        try
        {
            foreach (var workload in BenchmarkScenes.Names)
            {
                var generation = Stopwatch.StartNew();
                var scene = BenchmarkScenes.Create(workload);
                var generationMs = generation.Elapsed.TotalMilliseconds;
                for (var repetition = 0; repetition < 3; repetition++)
                {
                    // Rotate order to reduce systematic JIT/thermal bias.
                    var modes = new[] { "Legacy JSON", "Cached JSON", "Cached binary" };
                    for (var offset = 0; offset < modes.Length; offset++)
                    {
                        if (disposed) return;
                        var mode = modes[(offset + repetition) % modes.Length];
                        status = $"{workload}, {mode}, repetition {repetition + 1}/3";
                        await InvokeAsync(StateHasChanged);
                        var initMs = await interop!.InitializeAsync(canvas, mode != "Legacy JSON");
                        var environment = await interop.EnvironmentAsync();
                        await MeasureAsync(workload, mode, repetition, "cold", scene, initMs, generationMs, environment);
                        await MeasureAsync(workload, mode, repetition, "warm", scene, 0, generationMs, environment);
                        await MeasureAsync(workload, mode, repetition, "incremental", scene, 0, generationMs, environment);
                        await interop.ResetAsync();
                    }
                }
            }
            var rows = JsonSerializer.SerializeToElement(results).EnumerateArray().ToArray();
            summary = "Median CPU submission / caller upload / preparation (ms)\n" + string.Join("\n",
                rows.GroupBy(row => $"{row.GetProperty("workload").GetString()} | {row.GetProperty("mode").GetString()} | {row.GetProperty("phase").GetString()}")
                    .Select(group => $"{group.Key}: {Median(group.Select(row => row.GetProperty("frame").GetProperty("cpuSubmissionMs").GetDouble())):F1} / " +
                        $"{Median(group.Select(row => row.GetProperty("interopUploadMs").GetDouble())):F1} / " +
                        $"{Median(group.Select(row => row.GetProperty("preparationMs").GetDouble())):F1}"));
            status = "Complete. Download the report for individual runs, environment, and GPU queue completion measurements.";
        }
        catch (Exception error) { status = $"Benchmark failed: {error.Message}"; }
        finally
        {
            try { if (!disposed) await interop!.ResetAsync(); }
            catch (Exception error) { status += $" Cleanup failed: {error.Message}"; }
            running = false;
            report = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    private async Task MeasureAsync(string workload, string mode, int repetition, string phase,
        BenchmarkScene original, double initMs, double generationMs, JsonElement environment)
    {
        var incremental = phase == "incremental";
        var meshes = incremental ? original.Meshes.TakeLast(Math.Max(1, original.Meshes.Length / 10)).ToArray() : original.Meshes;
        var lines = incremental ? original.Lines.TakeLast(Math.Max(1, original.Lines.Length / 10)).ToArray() : original.Lines;
        await interop!.BeginAsync();
        if (disposed) return;
        if (!incremental) await interop.ClearAsync();
        if (disposed) return;
        var total = Stopwatch.StartNew();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var preparation = Stopwatch.StartNew();
        var packedLines = lines.Select(line => (LineUpload)line.CreateJavascriptData()).ToArray();
        object[] packedMeshes = [];
        BinarySceneUpload? binary = null;
        if (mode == "Cached binary") binary = BinarySceneUpload.Create(meshes.Select(mesh => mesh.Pack()).ToArray(), packedLines);
        else packedMeshes = meshes.Select(mesh => mode == "Legacy JSON" ? BenchmarkScenes.LegacyPack(mesh) : mesh.CreateJavascriptData()).ToArray();
        var preparationMs = preparation.Elapsed.TotalMilliseconds;
        var preparationAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        var upload = Stopwatch.StartNew();
        if (incremental) await interop.RemoveAsync(meshes.Select(mesh => mesh.Id).ToArray(), lines.Select(line => line.Id).ToArray());
        if (binary is not null) await interop.UploadAsync(binary);
        else await interop.UploadAsync(packedMeshes, packedLines);
        var interopUploadMs = upload.Elapsed.TotalMilliseconds;
        if (disposed) return;
        var frame = await interop.FinishAsync();
        if (disposed) return;
        if (!frame.GetProperty("visibilityStable").GetBoolean())
            throw new InvalidOperationException("The benchmark tab was hidden during measurement. Keep it visible and rerun.");
        results.Add(new { workload, mode, repetition, phase, initMs, generationMs,
            preparationMs, preparationAllocatedBytes, interopUploadMs,
            callerGpuCompletionMs = total.Elapsed.TotalMilliseconds, frame, environment });
        report = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
        await InvokeAsync(StateHasChanged);
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted[sorted.Length / 2];
    }

    public async ValueTask DisposeAsync()
    {
        disposed = true;
        if (interop is not null) await interop.DisposeAsync();
    }
}
