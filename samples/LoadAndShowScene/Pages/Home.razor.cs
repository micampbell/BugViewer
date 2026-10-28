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
    private record Measurement(string Phase, ScenePlaybackMetrics Timing);
    private record PlaybackReport(DateTimeOffset recordedAt, List<Measurement> measurements);
    [JsonSerializable(typeof(PlaybackReport))]
    private partial class PlaybackReportContext : JsonSerializerContext;

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
        if (report is not null) await report.DisposeAsync();
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
