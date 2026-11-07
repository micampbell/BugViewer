using Microsoft.AspNetCore.Components;

namespace BugViewer;

/// <summary>Displays 2D requests and their complete-frame timeline using the bundled Plotly renderer.</summary>
public partial class PlotViewer
{
    /// <summary>Identifier displayed in the header and passed to OnClose.</summary>
    [Parameter] public int ViewerId { get; set; }
    /// <summary>Whether to show the active-view indicator.</summary>
    [Parameter] public bool IsActive { get; set; }
    /// <summary>Additional CSS classes on the plot root for application layout and theming.</summary>
    [Parameter] public string? Class { get; set; }
    /// <summary>Inline root styles, including --bugviewer-* theme variables.</summary>
    [Parameter] public string? Style { get; set; }
    /// <summary>Requests held by the presenter. The last request supplies the active timeline.</summary>
    [Parameter] public IReadOnlyList<SceneRequest> Scenes { get; set; } = [];
    /// <summary>Optional close callback; providing it displays a close button.</summary>
    [Parameter] public EventCallback<int> OnClose { get; set; }
    /// <summary>Invoked when the underlying rendering surface is available.</summary>
    [Parameter] public EventCallback OnReady { get; set; }
    /// <summary>Hides the presentation header when embedded in another viewer.</summary>
    [Parameter] public bool HideHeader { get; set; }
    private ElementReference plotElement;
    private PlotlyInterop? plotly;
    private bool dataChanged = true;
    private int step;
    private SceneRequest? lastRequest;
    private CancellationTokenSource? autoplayCancellation;
    private Task? autoplayTask;
    /// <summary>Returns the active request with the currently selected step.</summary>
    public SceneRequest Capture() => (Scenes.LastOrDefault() ?? new SceneRequest()) with { InitialStep = step };
    /// <summary>Displays a zero-based step of the plot timeline.</summary>
    public async Task SetStepAsync(int index)
    {
        step = Math.Clamp(index, 0, Math.Max(0, StepCount - 1));
        await RenderCurrentAsync();
        await InvokeAsync(StateHasChanged);
    }
    private SceneRequest Current => Scenes.LastOrDefault() is { Steps.Count: > 0 } stepped ? stepped.Steps[Math.Clamp(step, 0, stepped.Steps.Count - 1)] : Scenes.LastOrDefault() ?? new SceneRequest { RequestId = Guid.Empty, Kind = PresentationKind.TwoDimensional };
    private int StepCount => Scenes.LastOrDefault()?.Steps.Count ?? 0;
    private string Title => Current.Title.Length == 0 ? "2D Scene" : Current.Title;
    private string Heading => Current.Heading;
    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        dataChanged = true;
        var request = Scenes.LastOrDefault();
        if (!ReferenceEquals(lastRequest, request))
        {
            await StopAutoplayAsync();
            lastRequest = request;
            step = request?.InitialStep ?? 0;
        }
        step = Math.Clamp(step, 0, Math.Max(0, StepCount - 1));
    }
    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender) { plotly = new PlotlyInterop(JS); await OnReady.InvokeAsync(); }
        if (dataChanged) await RenderCurrentAsync();
    }
    private async Task RenderCurrentAsync()
    {
        dataChanged = false;
        if (plotly is not null) await plotly.RenderAsync(plotElement, Current.Plot ?? new PlotRequest(), Title);
    }
    private async Task PreviousStep() { step = Math.Max(0, step - 1); await RenderCurrentAsync(); }
    private async Task NextStep() { step = Math.Min(StepCount - 1, step + 1); await RenderCurrentAsync(); }
    private async Task ToggleAutoplayAsync()
    {
        if (autoplayCancellation is not null)
        {
            await StopAutoplayAsync();
            return;
        }

        autoplayCancellation = new CancellationTokenSource();
        autoplayTask = AutoplayAsync(autoplayCancellation.Token);
    }

    private async Task AutoplayAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && StepCount > 1)
            {
                await Task.Delay(333, token);
                token.ThrowIfCancellationRequested();
                step = (step + 1) % StepCount;
                await RenderCurrentAsync();
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private async Task StopAutoplayAsync()
    {
        var cancellation = autoplayCancellation;
        var task = autoplayTask;
        autoplayCancellation = null;
        autoplayTask = null;
        if (cancellation is null) return;
        cancellation.Cancel();
        if (task is not null) await task;
        cancellation.Dispose();
    }
    private Task CloseAsync() => OnClose.InvokeAsync(ViewerId);
    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAutoplayAsync();
        if (plotly is not null) await plotly.DisposeAsync();
    }
}
