using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace BugViewer;

/// <summary>Displays a complete portable request, with 3D step playback or 2D plots.</summary>
public partial class SceneRequestViewer
{
    /// <summary>Optional request supplied by the parent. Loading through the methods replaces the active request.</summary>
    [Parameter] public SceneRequest? Scene { get; set; }
    /// <summary>Enables Ctrl+Alt+O and Ctrl+Alt+S for the active viewer. Enabled by default.</summary>
    [Parameter] public bool EnableFileShortcuts { get; set; } = true;
    /// <summary>Maximum browser input file size in bytes. Defaults to 1 GiB.</summary>
    [Parameter] public long MaxSceneFileSize { get; set; } = 1024L * 1024 * 1024;
    /// <summary>Optional diagnostics for each submitted 3D step.</summary>
    [Parameter] public EventCallback<ScenePlaybackMetrics> OnStepRendered { get; set; }
    /// <summary>Suggested Save As filename for a supplied request. Opened files retain their own filename.</summary>
    [Parameter] public string FileName { get; set; } = "scene.bugscene.json";
    private SceneRequest? lastParameter;
    private SceneRequest current = new();
    private IReadOnlyList<SceneRequest> requests = [];
    private SceneViewer? sceneViewer;
    private PlotViewer? plotViewer;
    private int generation;
    private ElementReference container, openButton, saveButton;
    private InputFile? fileInput;
    private SceneFileInterop? files;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? loading;
    private int loadPercent;
    private bool isBusy, disposed;
    private string? openedFileName, fileError;
    private string status = "Open a scene file to begin.";
    private ElementReference saveAsDialog, saveAsInput;
    private readonly string saveAsTitleId = $"scene-save-title-{Guid.NewGuid():N}";
    private string saveAsName = "";
    private string? saveAsError;
    private bool showSaveAsAfterRender;

    /// <summary>Number of steps in the active 3D timeline, or zero before a timeline is loaded.</summary>
    public int StepCount => sceneViewer?.StepCount ?? 0;
    /// <summary>Whether the active 3D presentation has applied its first frame.</summary>
    public bool IsReady => sceneViewer?.IsReady ?? false;

    /// <summary>Displays a zero-based step of the active 3D timeline.</summary>
    public Task SetStepAsync(int index) => sceneViewer?.SetStepAsync(index)
        ?? throw new InvalidOperationException("A 3D viewer is not ready.");

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxSceneFileSize);
        if (Scene is not null && !ReferenceEquals(Scene, lastParameter))
        {
            Scene.Validate();
            lastParameter = Scene;
            SetScene(Scene);
        }
    }

    private void SetScene(SceneRequest scene)
    {
        current = scene;
        requests = [scene];
        sceneViewer = null;
        plotViewer = null;
        generation++;
        openedFileName = null;
        fileError = null;
        status = string.IsNullOrWhiteSpace(scene.Title) ? "Scene loaded." : $"Loaded {scene.Title}.";
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && fileInput?.Element is { } input)
        {
            files = new SceneFileInterop(JS);
            await files.InitializeAsync(container, input, openButton, saveButton);
        }
        if (showSaveAsAfterRender && files is not null)
        {
            showSaveAsAfterRender = false;
            await files.ShowSaveAsAsync(saveAsDialog, saveAsInput);
        }
    }

    private async Task OpenSceneFileAsync(InputFileChangeEventArgs args)
    {
        if (isBusy || disposed) return;
        isBusy = true;
        fileError = null;
        status = $"Opening {args.File.Name}…";
        loading = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        loadPercent = 0;
        try
        {
            if (args.File.Size > MaxSceneFileSize)
                throw new IOException($"The file is {args.File.Size / 1048576.0:N1} MiB; this viewer allows {MaxSceneFileSize / 1048576.0:N1} MiB. Increase MaxSceneFileSize in the host to open larger scenes.");
            await using var stream = args.File.OpenReadStream(MaxSceneFileSize, loading.Token);
            using var progress = new SceneReadProgressStream(stream, bytes => InvokeAsync(() =>
            {
                loadPercent = (int)Math.Min(100, bytes * 100 / Math.Max(1, args.File.Size));
                status = loadPercent < 100 ? $"Opening {args.File.Name}… {loadPercent}%" : "Validating scene…";
                StateHasChanged();
            }));
            var scene = await SceneRequest.LoadAsync(progress, loading.Token);
            loading.Token.ThrowIfCancellationRequested();
            if (disposed) return;
            await SetValidatedSceneAsync(scene);
            openedFileName = args.File.Name;
            status = $"Opened {openedFileName}.";
        }
        catch (OperationCanceledException) when (disposed) { }
        catch (OperationCanceledException) when (loading.IsCancellationRequested)
        {
            status = "Opening canceled. The current scene is unchanged.";
        }
        catch (Exception exception) when (IsFileException(exception))
        {
            fileError = $"Could not open the scene: {exception.Message}";
            status = requests.Count == 0 ? "Open a scene file to begin." : "The current scene is unchanged.";
        }
        finally { loading.Dispose(); loading = null; isBusy = false; }
    }

    private void CancelOpen() => loading?.Cancel();

    private Task BeginSaveAsAsync()
    {
        if (isBusy || disposed || files is null || requests.Count == 0) return Task.CompletedTask;
        saveAsName = openedFileName ?? FileName;
        saveAsError = null;
        showSaveAsAfterRender = true;
        return InvokeAsync(StateHasChanged);
    }

    private Task CancelSaveAsAsync() => files is null ? Task.CompletedTask : files.CloseSaveAsAsync(saveAsDialog);

    private async Task ConfirmSaveAsAsync()
    {
        if (isBusy || disposed || files is null) return;
        var name = saveAsName.Trim();
        if (name.Length == 0)
        {
            saveAsError = "Enter a file name.";
            return;
        }
        if (name.IndexOfAny(['<', '>', ':', '"', '/', '\\', '|', '?', '*']) >= 0
            || name.Any(char.IsControl) || name.EndsWith('.'))
        {
            saveAsError = "Enter a file name without folders or special filename characters.";
            return;
        }
        if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".bugscene", StringComparison.OrdinalIgnoreCase))
            name += ".bugscene.json";
        await files.CloseSaveAsAsync(saveAsDialog);
        await DownloadSceneAsync(name);
    }

    private async Task DownloadSceneAsync(string name)
    {
        if (isBusy || disposed || files is null || requests.Count == 0) return;
        isBusy = true;
        fileError = null;
        try
        {
            await using var stream = new MemoryStream();
            await SaveSceneAsync(stream, lifetime.Token);
            stream.Position = 0;
            await files.DownloadAsync(name, stream);
            openedFileName = name;
            status = $"Downloaded {name}.";
        }
        catch (OperationCanceledException) when (disposed) { }
        catch (Exception exception) when (IsFileException(exception))
        {
            fileError = $"Could not save the scene: {exception.Message}";
        }
        finally { isBusy = false; }
    }

    private static bool IsFileException(Exception exception) => exception is System.Text.Json.JsonException
        or InvalidDataException or IOException or InvalidOperationException or NotSupportedException or JSException;

    /// <summary>Replaces the presentation, preserving all steps and source transforms.</summary>
    public async Task LoadSceneAsync(SceneRequest scene)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(scene);
        scene.Validate();
        await SetValidatedSceneAsync(scene);
    }

    private Task SetValidatedSceneAsync(SceneRequest scene) => InvokeAsync(() =>
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        SetScene(scene); StateHasChanged();
    });

    /// <summary>Loads a request from UTF-8 JSON, leaving the stream open.</summary>
    public async Task LoadSceneAsync(Stream stream, CancellationToken cancellationToken = default) =>
        await SetValidatedSceneAsync(await SceneRequest.LoadAsync(stream, cancellationToken));

    /// <summary>Loads a request from a local file; browser hosts should use the stream overload.</summary>
    public async Task LoadSceneAsync(string path, CancellationToken cancellationToken = default) =>
        await SetValidatedSceneAsync(await SceneRequest.LoadAsync(path, cancellationToken));

    /// <summary>Captures the complete request with the current step, camera and display settings.</summary>
    public async Task<SceneRequest> GetSceneAsync() => current.Kind == PresentationKind.TwoDimensional
        ? plotViewer?.Capture() ?? current
        : sceneViewer is null ? current : await sceneViewer.CaptureAsync();

    /// <summary>Saves the complete request, leaving the output stream open.</summary>
    public async Task SaveSceneAsync(Stream stream, CancellationToken cancellationToken = default) =>
        await (await GetSceneAsync()).SaveAsync(stream, cancellationToken);

    /// <summary>Saves the complete request to a local file.</summary>
    public async Task SaveSceneAsync(string path, CancellationToken cancellationToken = default) =>
        await (await GetSceneAsync()).SaveAsync(path, cancellationToken);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        await lifetime.CancelAsync();
        if (files is not null) await files.DisposeAsync();
        lifetime.Dispose();
    }
}
