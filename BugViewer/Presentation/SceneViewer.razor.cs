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
    private int StepCount => CurrentRequest.StepGroups.Count > 0
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
    /// <summary>Keyboard keys that request hover selections for application-specific inspection.</summary>
    [Parameter] public IReadOnlyList<string> HoverSelectionKeys { get; set; } = [];
    /// <summary>Reports changes to the active hover-selection key.</summary>
    [Parameter] public EventCallback<string?> OnHoverSelectionKeyChanged { get; set; }
    /// <summary>Allows the host to remove transient inspection visuals before replacing a frame.</summary>
    [Parameter] public EventCallback OnBeforeSceneUpdate { get; set; }
    private bool displayPreferencesInitialized;
    private readonly Dictionary<string, MeshData> renderedMeshes = [];
    private readonly Dictionary<string, LineData> renderedLines = [];
    private readonly Dictionary<string, RenderSignature> renderedMeshSignatures = [];
    private readonly Dictionary<string, RenderSignature> renderedLineSignatures = [];
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

    private async Task StepInputKeyDownAsync(KeyboardEventArgs args)
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

        autoplayCancellation = new CancellationTokenSource();
        autoplayTask = AutoplayAsync(autoplayCancellation.Token);
        await InvokeAsync(StateHasChanged);
    }

    private async Task AutoplayAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && StepCount > 1)
            {
                await Task.Delay(autoplayDelayMilliseconds, cancellationToken);
                step = (int)(((long)step + EffectiveStepStride) % StepCount);
                await RenderSceneAsync();
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

    private async Task RenderSceneCoreAsync(int stepIndex)
    {
        if (viewer is null)
            return;

        var desiredMeshes = new List<MeshData>();
        var desiredLines = new List<LineData>();
        var desiredMeshSignatures = new Dictionary<string, RenderSignature>();
        var desiredLineSignatures = new Dictionary<string, RenderSignature>();
        foreach (var scene in VisibleScenes(stepIndex))
        {
            foreach (var mesh in scene.Meshes)
            {
                desiredMeshes.Add(SceneGeometry.ToMeshData(mesh));
                desiredMeshSignatures.Add(mesh.Id, new RenderSignature(mesh, null));
            }
            foreach (var pathBatch in scene.PathBatches)
            {
                desiredLines.Add(pathBatch);
                desiredLineSignatures.Add(pathBatch.Id, new RenderSignature(pathBatch, null));
            }
            foreach (var pathBatch in ScenePathBatchBuilder.Build(scene))
            {
                desiredLines.Add(pathBatch);
                desiredLineSignatures.Add(pathBatch.Id, new RenderSignature(scene, null));
            }
            foreach (var points in scene.PointSets)
            {
                var line = ToPointGlyphs(points);
                desiredLines.Add(line);
                desiredLineSignatures.Add(line.Id, new RenderSignature(points, null));
            }
            foreach (var group in scene.StepGroups)
            {
                foreach (var (mesh, transform) in SceneGeometry.ResolveStepElements(group, group.Meshes, stepIndex))
                {
                    desiredMeshes.Add(SceneGeometry.ToMeshData(mesh, transform));
                    desiredMeshSignatures.Add(mesh.Id, new RenderSignature(mesh, transform));
                }
                if (group.PathSource is { } pathSource
                    && StepPathBatchBuilder.Build(group, pathSource, stepIndex) is { } pathBatch)
                {
                    desiredLines.Add(pathBatch);
                    var transform = group.Transforms is not null && stepIndex < group.Transforms.Count
                        && group.Transforms[stepIndex] is not null
                        ? group.Transforms[stepIndex]
                        : (SceneTransform?)null;
                    desiredLineSignatures.Add(pathBatch.Id,
                        new RenderSignature(pathSource, transform, stepIndex));
                }
            }
        }

        var desiredMeshIds = desiredMeshes.Select(mesh => mesh.Id).ToHashSet();
        var desiredLineIds = desiredLines.Select(line => line.Id).ToHashSet();
        var changedMeshIds = desiredMeshSignatures
            .Where(item => renderedMeshSignatures.TryGetValue(item.Key, out var rendered)
                && rendered != item.Value)
            .Select(item => item.Key).ToHashSet();
        var changedLineIds = desiredLineSignatures
            .Where(item => renderedLineSignatures.TryGetValue(item.Key, out var rendered)
                && rendered != item.Value)
            .Select(item => item.Key).ToHashSet();
        var meshesToRemove = renderedMeshes.Values
            .Where(mesh => !desiredMeshIds.Contains(mesh.Id) || changedMeshIds.Contains(mesh.Id)).ToList();
        var linesToRemove = renderedLines.Values
            .Where(line => !desiredLineIds.Contains(line.Id) || changedLineIds.Contains(line.Id)).ToList();
        var meshesToAdd = desiredMeshes
            .Where(mesh => !renderedMeshes.ContainsKey(mesh.Id) || changedMeshIds.Contains(mesh.Id)).ToList();
        var linesToAdd = desiredLines
            .Where(line => !renderedLines.ContainsKey(line.Id) || changedLineIds.Contains(line.Id)).ToList();

        await viewer.PauseRenderingAsync();
        try
        {
            await OnBeforeSceneUpdate.InvokeAsync();
            if (appliedScenes.Length == 0 && (CurrentRequest.Options is not null || CurrentRequest.Camera is not null))
            {
                await viewer.LoadSnapshotAsync(new SceneSnapshot
                {
                    Meshes = desiredMeshes, Lines = desiredLines,
                    Billboards = VisibleScenes(stepIndex).SelectMany(scene => scene.Billboards).ToList(),
                    Options = CurrentRequest.Options?.Clone() ?? initialOptions.Clone(), Camera = CurrentRequest.Camera
                });
            }
            else
            {
                if (meshesToRemove.Count > 0)
                    await viewer.RemoveMeshesAsync(meshesToRemove);
                if (linesToRemove.Count > 0)
                    await viewer.RemoveLinesAsync(linesToRemove);
                if (meshesToAdd.Count > 0)
                    await viewer.AddMeshesAsync(meshesToAdd);
                if (linesToAdd.Count > 0)
                    await viewer.AddLinesAsync(linesToAdd);
                await viewer.ClearAllTextBillboardsAsync();
                foreach (var label in VisibleScenes(stepIndex).SelectMany(scene => scene.Billboards))
                    await viewer.AddTextBillboardAsync(label.Id, label.Text, label.Anchor,
                        label.BackgroundColor, label.TextColor, label.Scale, label.RelativeX, label.RelativeY);
            }
            var currentScene = CurrentAtStep(stepIndex);
            if (!displayPreferencesInitialized)
            {
                // Scene requests provide the first view's defaults. After that, the user owns these
                // settings for this viewer: changing frame or scene must not overwrite their choices.
                // Apply the face mode only after meshes are present, because BugViewer otherwise cannot
                // determine whether primitive-surface normals are available.
                displayPreferencesInitialized = true;
                if (CurrentRequest.Options is null)
                {
                    viewer.Options.ShowSurfacesAs = currentScene.ShowSurfacesAs;
                    viewer.Options.ShowMeshBorders = currentScene.ShowMeshBorders;
                }
            }
        }
        finally
        {
            await viewer.ResumeRenderingAsync();
        }

        renderedMeshes.Clear();
        foreach (var mesh in desiredMeshes)
            renderedMeshes.Add(mesh.Id, mesh);
        renderedLines.Clear();
        foreach (var line in desiredLines)
            renderedLines.Add(line.Id, line);
        renderedMeshSignatures.Clear();
        foreach (var item in desiredMeshSignatures)
            renderedMeshSignatures.Add(item.Key, item.Value);
        renderedLineSignatures.Clear();
        foreach (var item in desiredLineSignatures)
            renderedLineSignatures.Add(item.Key, item.Value);
        if (appliedScenes.Length == 0 && CurrentRequest.Options is null && CurrentRequest.Camera is null)
        {
            await viewer.FitSceneAsync();
        }
        appliedScenes = Scenes.ToArray();
        renderedStep = stepIndex;
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
        foreach (var scene in VisibleScenes(step))
        {
            foreach (var mesh in scene.Meshes)
                yield return (scene, mesh);
            foreach (var group in scene.StepGroups)
                foreach (var (mesh, _) in SceneGeometry.ResolveStepElements(group, group.Meshes, step))
                    yield return (scene, mesh);
        }
    }

    // BugViewer currently exposes meshes and lines. Three crossed line segments provide a compact,
    // camera-readable diagnostic point glyph until its WebGPU point primitive is available.
    private LineData ToPointGlyphs(ScenePointSet points)
    {
        var d = viewer.PathThickness / 2;
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
        await StopAutoplayAsync();
        Task? pendingRender;
        lock (renderRequestGate)
        {
            disposing = true;
            pendingRenderStep = null;
            pendingRender = renderLoopTask;
        }
        if (pendingRender is not null)
            await pendingRender;
        await renderLock.WaitAsync();
        renderLock.Release();
        renderLock.Dispose();
    }

    private readonly record struct RenderSignature(object Source, SceneTransform? Transform, int StepIndex = -1);
}
