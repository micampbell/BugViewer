using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using BugViewer;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace LoadAndShowScene.Pages;

public partial class Home : IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = default!;
    private SceneViewer? presentation;
    private ScenePlaybackMetrics? latest;
    private readonly List<Measurement> measurements = [];
    private CancellationTokenSource? checkCancellation;
    private bool checking;
    private string phase = "interactive";
    private string checkStatus = "Open a stepped scene to begin.";
    private ReportInterop? report;
    private BrowserAppInterop? browserApp;
    private DotNetObjectReference<Home>? selfReference;
    private bool updateAvailable;
    private string? launchError;
    private record Measurement(string Phase, ScenePlaybackMetrics Timing);
    private record PlaybackReport(DateTimeOffset recordedAt, List<Measurement> measurements);
    [JsonSerializable(typeof(PlaybackReport))]
    private partial class PlaybackReportContext : JsonSerializerContext;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        selfReference = DotNetObjectReference.Create(this);
        browserApp = new BrowserAppInterop(JS);
        await browserApp.InitializeAsync(selfReference);
    }

    [JSInvokable]
    public async Task OpenLaunchedFileAsync(string name, long size, IJSStreamReference file)
    {
        try
        {
            await InvokeAsync(() => { launchError = null; StateHasChanged(); });
            if (presentation is not null)
                await presentation.OpenBrowserSceneAsync(name, size, file);
        }
        finally { await file.DisposeAsync(); }
    }

    [JSInvokable]
    public Task NotifyUpdateAvailableAsync() => InvokeAsync(() => { updateAvailable = true; StateHasChanged(); });

    [JSInvokable]
    public Task ReportLaunchErrorAsync(string message) => InvokeAsync(() =>
    {
        launchError = $"Could not open the launched file: {message}";
        StateHasChanged();
    });

    private Task ApplyUpdateAsync() => browserApp?.ApplyUpdateAsync() ?? Task.CompletedTask;

    private void StepRendered(ScenePlaybackMetrics timing)
    {
        latest = timing;
        measurements.Add(new(phase, timing));
        if (measurements.Count > 2048) measurements.RemoveAt(0);
    }

    private async Task RunPlaybackCheckAsync()
    {
        if (presentation is null || presentation.StepCount < 2 || checking) return;
        checking = true;
        checkCancellation = new();
        measurements.Clear();
        try
        {
            var pass = 0;
            foreach (var fps in new[] { 10, 30, 30 })
            {
                phase = pass++ == 2 ? "30 fps repeat" : $"{fps} fps";
                checkStatus = $"Measuring {phase}…";
                var clock = Stopwatch.StartNew();
                var frames = Math.Min(presentation.StepCount, 600);
                for (var index = 0; index < frames; index++)
                {
                    checkCancellation.Token.ThrowIfCancellationRequested();
                    var wait = index * 1000.0 / fps - clock.Elapsed.TotalMilliseconds;
                    if (wait > 0) await Task.Delay(TimeSpan.FromMilliseconds(wait), checkCancellation.Token);
                    await presentation.SetStepAsync(index);
                }
            }
            phase = "random seeks";
            checkStatus = "Measuring random seeks…";
            var random = new Random(1741);
            for (var index = 0; index < 30; index++)
            {
                checkCancellation.Token.ThrowIfCancellationRequested();
                await presentation.SetStepAsync(random.Next(presentation.StepCount));
            }
            await presentation.SetStepAsync(presentation.StepCount - 1);
            await presentation.SetStepAsync(0);
            checkStatus = "Check complete. Download measurements to compare frame intervals and upload costs.";
        }
        catch (OperationCanceledException) { checkStatus = "Check canceled."; }
        catch (Exception exception) { checkStatus = $"Check failed: {exception.Message}"; }
        finally { checking = false; phase = "interactive"; checkCancellation.Dispose(); checkCancellation = null; }
    }
    private void CancelCheck() => checkCancellation?.Cancel();
    private async Task DownloadReportAsync()
    {
        report ??= new ReportInterop(JS);
        await report.DownloadAsync(JsonSerializer.Serialize(new PlaybackReport(DateTimeOffset.UtcNow, measurements),
            PlaybackReportContext.Default.PlaybackReport));
    }
    public async ValueTask DisposeAsync()
    {
        checkCancellation?.Cancel();
        if (browserApp is not null) await browserApp.DisposeAsync();
        selfReference?.Dispose();
        if (report is not null) await report.DisposeAsync();
    }

    private sealed class BrowserAppInterop(IJSRuntime js) : IAsyncDisposable
    {
        private IJSObjectReference? module;
        internal async Task InitializeAsync(DotNetObjectReference<Home> reference)
        {
            module = await js.InvokeAsync<IJSObjectReference>("import", "./Pages/Home.razor.js");
            await module.InvokeVoidAsync("initializeApp", reference);
        }
        internal async Task ApplyUpdateAsync()
        {
            if (module is not null) await module.InvokeVoidAsync("applyUpdate");
        }
        public async ValueTask DisposeAsync()
        {
            if (module is null) return;
            try { await module.InvokeVoidAsync("disposeApp"); await module.DisposeAsync(); }
            catch (JSDisconnectedException) { }
        }
    }

    private sealed class ReportInterop(IJSRuntime js) : IAsyncDisposable
    {
        private IJSObjectReference? module;
        internal async Task DownloadAsync(string json)
        {
            module ??= await js.InvokeAsync<IJSObjectReference>("import", "./Pages/Home.razor.js");
            await module.InvokeVoidAsync("download", json);
        }
        public async ValueTask DisposeAsync()
        {
            if (module is not null) await module.DisposeAsync();
        }
    }
}
