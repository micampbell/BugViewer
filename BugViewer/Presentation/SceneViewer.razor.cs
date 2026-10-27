using System.Diagnostics;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using NumericsVector3 = System.Numerics.Vector3;

namespace BugViewer;

/// <summary>Displays 3D requests with lazy step resolution, playback, and selection events.</summary>
public partial class SceneViewer
{
    /// <summary>Identifier displayed in the header and passed to OnClose.</summary>
    [Parameter] public int ViewerId { get; set; }
    /// <summary>Whether to show the active-view indicator.</summary>
    [Parameter] public bool IsActive { get; set; }
    /// <summary>Requests held by the presenter. The last request supplies the active timeline.</summary>
    [Parameter] public IReadOnlyList<SceneRequest> Scenes { get; set; } = [];
    /// <summary>Optional close callback; providing it displays a close button.</summary>
    [Parameter] public EventCallback<int> OnClose { get; set; }
    /// <summary>Invoked when the underlying rendering surface is available.</summary>
    [Parameter] public EventCallback OnReady { get; set; }
    /// <summary>Reports selection using portable mesh and triangle identifiers.</summary>
    [Parameter] public EventCallback<SceneTriangleSelection> OnTriangleSelected { get; set; }
    /// <summary>Optional actions displayed below the Camera accordion in the underlying viewer's settings.</summary>
    [Parameter] public RenderFragment? CameraActions { get; set; }

    /// <summary>Optional timings for each completed step, including first browser submission.</summary>
    [Parameter] public EventCallback<ScenePlaybackMetrics> OnStepRendered { get; set; }
    private readonly ScenePreparationCache preparationCache = new(128L * 1024 * 1024);
    private CancellationTokenSource? prefetchCancellation;
    private Task? prefetchTask;
    private int missedDeadlines;
    private int step;
    private bool isEditingStep;
    private string targetStep = "";
    private ElementReference stepInput;
    private ElementReference stepButton;
    private bool focusStepInput;
    private bool focusStepButton;
    private int renderedStep;
    private int autoplayDelayMilliseconds = 333;
    private int stepStride = 1;
    private int EffectiveStepStride => Math.Clamp(stepStride, 1, Math.Max(1, StepCount - 1));
    private CancellationTokenSource? autoplayCancellation;
    private Task? autoplayTask;
    private bool IsAutoplaying => autoplayCancellation is not null;
    private SceneRequest CurrentRequest => Scenes.LastOrDefault()
        ?? new SceneRequest { RequestId = Guid.Empty };
    private SceneRequest Current => CurrentRequest is { Steps.Count: > 0 } stepped
        ? stepped.Steps[Math.Clamp(step, 0, stepped.Steps.Count - 1)]
        : CurrentRequest;
    /// <summary>Number of steps in the active timeline.</summary>
    public int StepCount => CurrentRequest.StepGroups.Count > 0
        ? CurrentRequest.StepGroups.Max(group => group.Count)
        : CurrentRequest.Steps.Count;
    private string Heading => Current.Heading;
    private string Title => Current.Title.Length == 0 ? "3D Scene" : Current.Title;
    private string Subtitle => Current.Subtitle;
    private global::BugViewer.BugViewer? viewer;

    private SceneRequest[] appliedScenes = [];
    private bool ready;
    private BugViewerOptions initialOptions = BugViewerOptions.Default.Clone();
    private SceneRequest? lastRequest;
    /// <summary>Underlying renderer after its component reference has been assigned.</summary>
    public global::BugViewer.BugViewer? Viewer => viewer;
    /// <summary>Zero-based selected step.</summary>
    public int CurrentStep => step;
    /// <summary>Whether the first frame of the current presentation has been applied.</summary>
    public bool IsReady => ready && appliedScenes.Length > 0;
    /// <summary>Keyboard keys that request hover selections for application-specific inspection.</summary>
    [Parameter] public IReadOnlyList<string> HoverSelectionKeys { get; set; } = [];
    /// <summary>Reports changes to the active hover-selection key.</summary>
    [Parameter] public EventCallback<string?> OnHoverSelectionKeyChanged { get; set; }
    /// <summary>Allows the host to remove transient inspection visuals before replacing a frame.</summary>
    [Parameter] public EventCallback OnBeforeSceneUpdate { get; set; }
    private bool displayPreferencesInitialized;
    private readonly SemaphoreSlim renderLock = new(1, 1);
    private readonly object renderRequestGate = new();
    private Task? renderLoopTask;
    private int? pendingRenderStep;
    private bool disposing;

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        if (!ReferenceEquals(lastRequest, CurrentRequest))
        {
            isEditingStep = false;
            focusStepInput = false;
            focusStepButton = false;
            preparationCache.Clear();
            prefetchCancellation?.Cancel();
            lastRequest = CurrentRequest;
            step = Math.Clamp(CurrentRequest.InitialStep, 0, Math.Max(0, StepCount - 1));
            if (!ready) initialOptions = CurrentRequest.Options?.Clone() ?? BugViewerOptions.Default.Clone();
        }
        if (step >= StepCount) step = Math.Max(0, StepCount - 1);
        if (ready && !appliedScenes.SequenceEqual(Scenes)) await RenderSceneAsync();
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (focusStepInput && isEditingStep)
        {
            focusStepInput = false;
            await stepInput.FocusAsync(preventScroll: true);
        }
        else if (focusStepButton && !isEditingStep && StepCount > 1)
        {
            focusStepButton = false;
            await stepButton.FocusAsync(preventScroll: true);
        }
    }

    private async Task OnViewerReadyAsync()
    {
        ready = true;
        await RenderSceneAsync();
        await OnReady.InvokeAsync();
    }

    /// <summary>Captures the complete request and current view without flattening its steps.</summary>
    public async Task<SceneRequest> CaptureAsync()
    {
        if (viewer is null || !ready) return CurrentRequest with { InitialStep = step };
        await renderLock.WaitAsync();
        try
        {
            var state = await viewer.GetViewStateAsync();
            return CurrentRequest with { InitialStep = renderedStep, Camera = state.Camera, Options = state.Options };
        }
        finally { renderLock.Release(); }
    }

    private Task CloseAsync() => OnClose.InvokeAsync(ViewerId);

    /// <summary>Displays one step of the complete presentation.</summary>
    public async Task SetStepAsync(int index)
    {
        step = Math.Clamp(index, 0, Math.Max(0, StepCount - 1));
        if (ready) await RenderSceneAsync();
        await InvokeAsync(StateHasChanged);
    }

    private async Task BeginStepEditAsync()
    {
        await StopAutoplayAsync();
        targetStep = (step + 1).ToString();
        isEditingStep = true;
        focusStepInput = true;
    }

    private async Task ApplyTargetStepAsync()
    {
        if (!isEditingStep)
            return;

        isEditingStep = false;
        focusStepInput = false;
        if (int.TryParse(targetStep, out var requestedStep))
            await SetStepAsync(Math.Clamp(requestedStep, 1, Math.Max(1, StepCount)) - 1);
    }

    // Apply on key-up so returning focus to the counter cannot make the same
    // Enter key-down activate that button and immediately reopen the editor.
    private async Task StepInputKeyUpAsync(KeyboardEventArgs args)
    {
        if (args.Key == "Enter")
        {
            focusStepButton = true;
            await ApplyTargetStepAsync();
        }
        else if (args.Key == "Escape")
        {
            isEditingStep = false;
            focusStepInput = false;
            focusStepButton = true;
        }
    }

    private async Task PreviousStepAsync()
    {
        step = Math.Max(0, step - EffectiveStepStride);
        await RenderSceneAsync();
    }

    private async Task NextStepAsync()
    {
        step = (int)Math.Min(Math.Max(0, StepCount - 1), (long)step + EffectiveStepStride);
        await RenderSceneAsync();
    }

    private async Task ToggleAutoplayAsync()
    {
        if (autoplayCancellation is not null)
        {
            await StopAutoplayAsync();
            return;
        }

        missedDeadlines = 0;
        autoplayCancellation = new CancellationTokenSource();
        autoplayTask = AutoplayAsync(autoplayCancellation.Token);
        await InvokeAsync(StateHasChanged);
    }

    private async Task AutoplayAsync(CancellationToken cancellationToken)
    {
        try
        {
            var clock = Stopwatch.StartNew();
            double deadline = clock.Elapsed.TotalMilliseconds + autoplayDelayMilliseconds;
            while (!cancellationToken.IsCancellationRequested && StepCount > 1)
            {
                var remaining = deadline - clock.Elapsed.TotalMilliseconds;
                if (remaining > 0) await Task.Delay(TimeSpan.FromMilliseconds(remaining), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                step = (int)(((long)step + EffectiveStepStride) % StepCount);
                await RenderSceneAsync();
                deadline += autoplayDelayMilliseconds;
                if (clock.Elapsed.TotalMilliseconds > deadline)
                {
                    missedDeadlines++;
                    // Preserve every step; do not build up a catch-up queue or claim
                    // a target FPS achieved by silently skipping geometry frames.
                    deadline = clock.Elapsed.TotalMilliseconds;
                }
                if (!cancellationToken.IsCancellationRequested)
                    await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task StopAutoplayAsync()
    {
        var cancellation = autoplayCancellation;
        var task = autoplayTask;
        autoplayCancellation = null;
        autoplayTask = null;
        if (cancellation is null)
            return;

        cancellation.Cancel();
        if (task is not null)
            await task;
        cancellation.Dispose();
    }

    private SceneRequest CurrentAtStep(int stepIndex) => CurrentRequest is { Steps.Count: > 0 } stepped
        ? stepped.Steps[Math.Clamp(stepIndex, 0, stepped.Steps.Count - 1)]
        : CurrentRequest;

    private IEnumerable<SceneRequest> VisibleScenes(int stepIndex)
    {
        for (var index = 0; index < Scenes.Count - 1; index++)
            yield return Scenes[index];
        if (Scenes.Count > 0)
            yield return CurrentAtStep(stepIndex);
    }

    private Task RenderSceneAsync()
    {
        lock (renderRequestGate)
        {
            if (disposing)
                return Task.CompletedTask;
            prefetchCancellation?.Cancel();
            pendingRenderStep = step;
            renderLoopTask ??= ProcessRenderRequestsAsync();
        }
        return renderLoopTask ?? Task.CompletedTask;
    }

    private async Task ProcessRenderRequestsAsync()
    {
        await Task.Yield();
        try
        {
            while (true)
            {
                int stepToRender;
                lock (renderRequestGate)
                {
                    if (pendingRenderStep is not { } requestedStep)
                        return;
                    stepToRender = requestedStep;
                    pendingRenderStep = null;
                }

                await renderLock.WaitAsync();
                try
                {
                    await RenderSceneCoreAsync(stepToRender);
                }
                finally
                {
                    renderLock.Release();
                }
            }
        }
        finally
        {
            lock (renderRequestGate)
            {
                renderLoopTask = null;
                if (!disposing && pendingRenderStep.HasValue)
                    renderLoopTask = ProcessRenderRequestsAsync();
            }
        }
    }

    private bool IsObsolete(int index)
    {
        lock (renderRequestGate) return disposing || pendingRenderStep is { } pending && pending != index;
    }

    private sealed record PreparedFrame(List<MeshData> Meshes, List<LineData> Lines, List<TextBillboard> Labels);

    private async Task<PreparedFrame?> PrepareFrameAsync(int stepIndex, Func<bool> obsolete)
    {
        var desiredMeshes = new List<MeshData>();
        var desiredLines = new List<LineData>();
        var slice = Stopwatch.StartNew();
        foreach (var scene in VisibleScenes(stepIndex))
        {
            foreach (var mesh in scene.Meshes)
                desiredMeshes.Add(preparationCache.Get(new RenderSignature(mesh, null),
                    () => SceneGeometry.ToMeshData(mesh), ScenePreparationCache.MeshBytes));
            desiredLines.AddRange(scene.PathBatches);
            desiredLines.AddRange(preparationCache.Get(new RenderSignature(scene, null),
                () => ScenePathBatchBuilder.Build(scene), batches => batches.Sum(ScenePreparationCache.LineBytes)));
            foreach (var points in scene.PointSets)
                desiredLines.Add(preparationCache.Get((points, viewer!.PathThickness),
                    () => ToPointGlyphs(points), ScenePreparationCache.LineBytes));
            foreach (var group in scene.StepGroups)
            {
                foreach (var (mesh, transform) in SceneGeometry.ResolveStepElements(group, group.Meshes, stepIndex))
                    desiredMeshes.Add(preparationCache.Get(new RenderSignature(mesh, transform),
                        () => SceneGeometry.ToMeshData(mesh, transform), ScenePreparationCache.MeshBytes));
                if (group.PathSource is { } source)
                {
                    var index = group.Transforms is null && source.HistoryStepLimit is null ? -1 : stepIndex;
                    var line = preparationCache.Get(new RenderSignature(group, null, index),
                        () => StepPathBatchBuilder.Build(group, source, stepIndex), ScenePreparationCache.LineBytes);
                    if (line is not null) desiredLines.Add(line);
                }
                // A browser event-loop yield lets newer slider requests supersede preparation.
                if (slice.ElapsedMilliseconds >= 6)
                {
                    await Task.Delay(1);
                    slice.Restart();
                }
                if (obsolete()) return null;
            }
        }
        return new(desiredMeshes, desiredLines, VisibleScenes(stepIndex).SelectMany(scene => scene.Billboards).ToList());
    }

    private async Task RenderSceneCoreAsync(int stepIndex)
    {
        if (viewer is null) return;
        var total = Stopwatch.StartNew();
        var frame = await PrepareFrameAsync(stepIndex, () => IsObsolete(stepIndex));
        if (frame is null || IsObsolete(stepIndex)) return;
        var preparationMs = total.Elapsed.TotalMilliseconds;
        await OnBeforeSceneUpdate.InvokeAsync();
        ScenePlaybackMetrics timing;
        if (appliedScenes.Length == 0 && (CurrentRequest.Options is not null || CurrentRequest.Camera is not null))
        {
            await viewer.LoadSnapshotAsync(new SceneSnapshot
            {
                Meshes = frame.Meshes, Lines = frame.Lines, Billboards = frame.Labels,
                Options = CurrentRequest.Options?.Clone() ?? initialOptions.Clone(), Camera = CurrentRequest.Camera
            }, upload: false);
            timing = await viewer.ApplyPresentationFrameAsync(frame.Meshes, frame.Lines, frame.Labels, preserveView: true);
        }
        else
            timing = await viewer.ApplyPresentationFrameAsync(frame.Meshes, frame.Lines, frame.Labels);

        if (disposing) return;

        if (!displayPreferencesInitialized)
        {
            displayPreferencesInitialized = true;
            if (CurrentRequest.Options is null)
            {
                var currentScene = CurrentAtStep(stepIndex);
                viewer.Options.ShowSurfacesAs = currentScene.ShowSurfacesAs;
                viewer.Options.ShowMeshBorders = currentScene.ShowMeshBorders;
            }
        }
        if (appliedScenes.Length == 0 && CurrentRequest.Options is null && CurrentRequest.Camera is null)
            await viewer.FitSceneAsync();
        appliedScenes = Scenes.ToArray();
        renderedStep = stepIndex;
        if (OnStepRendered.HasDelegate)
            await OnStepRendered.InvokeAsync(timing with { Step = stepIndex, PreparationMs = preparationMs,
                TotalMs = total.Elapsed.TotalMilliseconds, MissedDeadlines = missedDeadlines });
        SchedulePrefetch(stepIndex);
    }

    private void SchedulePrefetch(int fromStep)
    {
        if (disposing) return;
        prefetchCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        prefetchCancellation = cancellation;
        var previous = prefetchTask;
        prefetchTask = PrefetchAsync(previous, fromStep, cancellation);
    }

    private async Task PrefetchAsync(Task? previous, int fromStep, CancellationTokenSource cancellation)
    {
        try
        {
            if (previous is not null) await previous;
            await Task.Delay(1, cancellation.Token);
            await renderLock.WaitAsync(cancellation.Token);
            try
            {
                var meshes = new List<MeshData>(); var lines = new List<LineData>();
                for (var offset = 1; offset <= 2 && StepCount > 1; offset++)
                {
                    var target = (int)(((long)fromStep + offset * (long)EffectiveStepStride) % StepCount);
                    var frame = await PrepareFrameAsync(target, () => cancellation.IsCancellationRequested || disposing);
                    if (frame is null || cancellation.IsCancellationRequested) return;
                    meshes.AddRange(frame.Meshes); lines.AddRange(frame.Lines);
                }
                if (meshes.Count + lines.Count > 0)
                    await viewer!.PrefetchPresentationAsync(meshes.Distinct<MeshData>(ReferenceEqualityComparer.Instance).ToList(),
                        lines.Distinct<LineData>(ReferenceEqualityComparer.Instance).ToList(), cancellation.Token);
            }
            finally { renderLock.Release(); }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(prefetchCancellation, cancellation)) prefetchCancellation = null;
            cancellation.Dispose();
        }
    }

    private async Task TriangleSelectedAsync()
    {
        if (viewer?.SelectedMeshName is not { Length: > 0 } meshId || viewer.SelectedTriangleInMeshIndex < 0) return;
        var selected = VisibleSceneMeshes().LastOrDefault(item => item.Mesh.Id == meshId);
        if (selected.Scene is not null)
            await OnTriangleSelected.InvokeAsync(new SceneTriangleSelection(selected.Scene.RequestId,
                meshId, viewer.SelectedTriangleInMeshIndex, viewer.SelectedPoint));
    }

    /// <summary>Enumerates source meshes corresponding to the selected frame.</summary>
    public IEnumerable<(SceneRequest Scene, SceneMesh Mesh)> VisibleSceneMeshes()
    {
        foreach (var scene in VisibleScenes(renderedStep))
        {
            foreach (var mesh in scene.Meshes)
                yield return (scene, mesh);
            foreach (var group in scene.StepGroups)
                foreach (var (mesh, _) in SceneGeometry.ResolveStepElements(group, group.Meshes, renderedStep))
                    yield return (scene, mesh);
        }
    }

    // BugViewer currently exposes meshes and lines. Three crossed line segments provide a compact,
    // camera-readable diagnostic point glyph until its WebGPU point primitive is available.
    private LineData ToPointGlyphs(ScenePointSet points)
    {
        var d = viewer!.PathThickness / 2;
        var vertices = new List<NumericsVector3>();
        foreach (var p in points.Points)
        {
            vertices.Add(new NumericsVector3(p[0] - d, p[1], p[2])); vertices.Add(new NumericsVector3(p[0] + d, p[1], p[2]));
            vertices.Add(new NumericsVector3(p[0], p[1] - d, p[2])); vertices.Add(new NumericsVector3(p[0], p[1] + d, p[2]));
            vertices.Add(new NumericsVector3(p[0], p[1], p[2] - d)); vertices.Add(new NumericsVector3(p[0], p[1], p[2] + d));
        }
        var count = Math.Max(0, vertices.Count - 1);
        var color = new ColorRgba(points.Color.R, points.Color.G, points.Color.B, points.Color.A);
        return new LineData
        {
            Id = points.Id,
            Vertices = vertices,
            Thicknesses = Enumerable.Repeat(viewer.PathThickness, count).ToList(),
            Colors = Enumerable.Repeat(color, count).ToList(),
            FadeFactors = Enumerable.Repeat(0f, count).ToList()
        };
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Task? pendingRender;
        lock (renderRequestGate)
        {
            if (disposing) return;
            disposing = true;
            pendingRenderStep = null;
            pendingRender = renderLoopTask;
            prefetchCancellation?.Cancel();
        }
        if (viewer is not null) await viewer.CancelPendingPresentationAsync();
        await StopAutoplayAsync();
        if (pendingRender is not null) await pendingRender;
        prefetchCancellation?.Cancel();
        if (prefetchTask is not null) await prefetchTask;
        preparationCache.Clear();
        await renderLock.WaitAsync();
        renderLock.Release();
        renderLock.Dispose();
    }

    private readonly record struct RenderSignature(object Source, SceneTransform? Transform, int StepIndex = -1);
}
