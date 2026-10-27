using System.Numerics;
using System.Diagnostics;
using System.Text.Json;
using BugViewer;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace SimpleStandalone.Benchmarks;

internal sealed class BenchmarkInterop(IJSRuntime js) : IAsyncDisposable
{
    private const string ModulePath = "./_content/BugViewer/js/webgpu-canvas.js?benchmark";
    private IJSObjectReference? module;
    private DotNetObjectReference<BenchmarkInterop>? reference;
    private readonly BugViewerOptions options = new();
    private OrbitCamera? camera;
    internal async Task<double> InitializeAsync(ElementReference canvas, bool cache)
    {
        module ??= await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
        reference ??= DotNetObjectReference.Create(this);
        camera = new OrbitCamera(Vector3.Zero, options);
        var timer = Stopwatch.StartNew();
        await module.InvokeVoidAsync("initGPU_Canvas", reference, canvas, null, null,
            options.ToJavascriptOptions(camera.PolarAngle), camera.ConvertMatrixToJavaScript(),
            camera.ConvertPositionToJavaScript());
        await module.InvokeVoidAsync("writeProjectionMatrix", camera.ConvertProjectionMatrixToJavaScript(960, 600));
        await module.InvokeVoidAsync("configureBenchmark", cache);
        return timer.Elapsed.TotalMilliseconds;
    }

    internal ValueTask BeginAsync() => module!.InvokeVoidAsync("beginSceneMeasurement");
    internal ValueTask ClearAsync() => ClearCoreAsync();
    private async ValueTask ClearCoreAsync()
    {
        await module!.InvokeVoidAsync("clearAllMeshes");
        await module!.InvokeVoidAsync("clearAllLines");
    }
    internal async Task RemoveAsync(string[] meshes, string[] lines)
    {
        if (meshes.Length != 0) await module!.InvokeVoidAsync("removeMeshes", (object)meshes);
        if (lines.Length != 0) await module!.InvokeVoidAsync("removeLinesBatch", (object)lines);
    }
    internal async Task UploadAsync(object[] meshes, LineUpload[] lines)
    {
        if (meshes.Length != 0) await module!.InvokeVoidAsync("addMeshes", (object)meshes);
        if (lines.Length != 0) await module!.InvokeVoidAsync("addLinesBatch", (object)lines);
    }
    internal ValueTask UploadAsync(BinarySceneUpload scene) => module!.InvokeVoidAsync("addSceneBinary",
        new { meshes = scene.Meshes, lines = scene.Lines }, scene.Bytes);
    internal ValueTask<JsonElement> FinishAsync() => module!.InvokeAsync<JsonElement>("finishSceneMeasurement");
    internal ValueTask<JsonElement> EnvironmentAsync() => module!.InvokeAsync<JsonElement>("getBenchmarkEnvironment");
    internal ValueTask DownloadAsync(string report) => module!.InvokeVoidAsync("downloadBenchmarkReport", report);
    internal ValueTask ResetAsync() => module!.InvokeVoidAsync("disposeWebGPU_Canvas");
    [JSInvokable] public Task OnCanvasResized(double width, double height) =>
        module is null || camera is null ? Task.CompletedTask : module.InvokeVoidAsync("writeProjectionMatrix",
            camera.ConvertProjectionMatrixToJavaScript(width, height)).AsTask();
    [JSInvokable] public void OnWebGpuReady() { }
    [JSInvokable] public void OnFrameMsUpdate(double milliseconds) { }
    [JSInvokable] public void OnWebGpuError(string message) => Console.Error.WriteLine(message);
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (module is not null)
            {
                await ResetAsync();
                await module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException) { }
        finally { reference?.Dispose(); }
    }
}
