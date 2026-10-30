using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace BugViewer;

public partial class SceneViewer
{
    /// <summary>Optional request supplied by the parent. Loading a file replaces the active request.</summary>
    [Parameter] public SceneRequest? Scene { get; set; }
    /// <summary>Enables Ctrl+Alt+O and Ctrl+Alt+S for the active viewer.</summary>
    [Parameter] public bool EnableFileShortcuts { get; set; } = true;
    /// <summary>Enables the browser open button and shortcut; programmatic loading remains available.</summary>
    [Parameter] public bool EnableOpenScene { get; set; } = true;
    /// <summary>Enables the browser save controls and shortcut; programmatic saving remains available.</summary>
    [Parameter] public bool EnableSaveScene { get; set; } = true;
    /// <summary>Displays open and save controls above the viewer.</summary>
    [Parameter] public bool ShowFileActions { get; set; }
    /// <summary>Maximum browser input file size in bytes. Defaults to 1 GiB.</summary>
    [Parameter] public long MaxSceneFileSize { get; set; } = 1024L * 1024 * 1024;
    /// <summary>Suggested Save As filename for a supplied request.</summary>
    [Parameter] public string FileName { get; set; } = "scene.bugscene.json";

    [Parameter] public bool ShowHeader { get; set; } = false;

    private readonly SceneRequest emptyRequest = new() { RequestId = Guid.Empty };
    private IReadOnlyList<SceneRequest>? loadedScenes, lastScenesParameter;
    private SceneRequest? lastSceneParameter;
    private IReadOnlyList<SceneRequest> ActiveScenes => loadedScenes ?? Scenes;
    private PlotViewer? plotViewer;
    private int generation;
    private ElementReference container, openButton, saveButton;
    private InputFile? fileInput;
    private SceneFileInterop? files;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? loading, saving;
    private int loadPercent;
    private bool isBusy, disposed, fileOperationCanCancel;
    private string? fileOperationStatus;
    private string? openedFileName, fileError;
    private ElementReference saveAsDialog, saveAsInput;
    private readonly string saveAsTitleId = $"scene-save-title-{Guid.NewGuid():N}";
    private string saveAsName = "";
    private string saveStartStep = "1", saveEndStep = "1", saveStepStride = "1";
    private string? saveAsError;
    private bool showSaveAsAfterRender;

    private void ResetRenderer()
    {
        generation++;
        ready = false;
        viewer = null;
        plotViewer = null;
        appliedScenes = [];
        displayPreferencesInitialized = false;
        prefetchCancellation?.Cancel();
    }

    private async Task OpenSceneFileAsync(InputFileChangeEventArgs args)
    {
        if (isBusy || disposed) return;
        isBusy = true;
        fileError = null;
        loading = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        loadPercent = 0;
        fileOperationCanCancel = true;
        fileOperationStatus = "Opening scene…";
        try
        {
            await RefreshFileOperationAsync();
            loading.Token.ThrowIfCancellationRequested();
            if (args.File.Size > MaxSceneFileSize)
                throw new IOException($"The file is {args.File.Size / 1048576.0:N1} MiB; this viewer allows {MaxSceneFileSize / 1048576.0:N1} MiB. Increase MaxSceneFileSize in the host to open larger scenes.");
            await using var stream = args.File.OpenReadStream(MaxSceneFileSize, loading.Token);
            using var progress = new SceneReadProgressStream(stream, bytes => InvokeAsync(() =>
            {
                loadPercent = (int)Math.Min(100, bytes * 100 / Math.Max(1, args.File.Size));
                StateHasChanged();
            }));
            var scene = await SceneRequest.LoadAsync(progress, loading.Token);
            loading.Token.ThrowIfCancellationRequested();
            if (disposed) return;
            fileOperationCanCancel = false;
            fileOperationStatus = "Displaying scene…";
            await RefreshFileOperationAsync();
            await SetValidatedSceneAsync(scene);
            openedFileName = args.File.Name;
        }
        catch (OperationCanceledException) when (disposed || loading.IsCancellationRequested) { }
        catch (Exception exception) when (IsFileException(exception))
        {
            fileError = $"Could not open the scene: {exception.Message}";
        }
        finally { loading.Dispose(); loading = null; fileOperationStatus = null; fileOperationCanCancel = false; isBusy = false; }
    }

    private void CancelFileOperation()
    {
        if (!fileOperationCanCancel) return;
        fileOperationCanCancel = false;
        fileOperationStatus = "Canceling…";
        (loading ?? saving)?.Cancel();
    }

    private async Task RefreshFileOperationAsync()
    {
        await InvokeAsync(StateHasChanged);
        await Task.Delay(1);
    }

    private Task BeginSaveAsAsync()
    {
        if (isBusy || disposed || files is null || ActiveScenes.Count == 0) return Task.CompletedTask;
        saveAsName = openedFileName ?? FileName;
        saveStartStep = "1";
        saveEndStep = Math.Max(1, StepCount).ToString();
        saveStepStride = "1";
        saveAsError = null;
        showSaveAsAfterRender = true;
        return InvokeAsync(StateHasChanged);
    }

    /// <summary>Opens the Save As dialog for the current presentation.</summary>
    public Task ShowSaveAsAsync() => BeginSaveAsAsync();

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
        int start = 1, end = 1, stride = 1;
        if (StepCount > 0 && (!int.TryParse(saveStartStep, out start) || !int.TryParse(saveEndStep, out end)
            || !int.TryParse(saveStepStride, out stride) || start < 1 || end < start || end > StepCount || stride < 1))
        {
            saveAsError = $"Enter a start and end between 1 and {StepCount}, with the start no greater than the end, and a positive stride.";
            return;
        }
        if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".bugscene", StringComparison.OrdinalIgnoreCase))
            name += ".bugscene.json";
        await files.CloseSaveAsAsync(saveAsDialog);
        await DownloadSceneAsync(name, start, end, stride);
    }

    private async Task DownloadSceneAsync(string name, int start, int end, int stride)
    {
        if (isBusy || disposed || files is null || ActiveScenes.Count == 0) return;
        isBusy = true;
        fileError = null;
        saving = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        fileOperationCanCancel = true;
        fileOperationStatus = "Preparing scene…";
        try
        {
            await RefreshFileOperationAsync();
            saving.Token.ThrowIfCancellationRequested();
            await using var stream = new MemoryStream();
            var scene = await GetSceneAsync();
            saving.Token.ThrowIfCancellationRequested();
            if (StepCount > 0)
                scene = await SceneRangeExporter.ExportAsync(scene, start - 1, end - 1, stride, saving.Token);
            fileOperationStatus = "Writing scene…";
            await RefreshFileOperationAsync();
            saving.Token.ThrowIfCancellationRequested();
            using var progress = new SceneWriteProgressStream(stream);
            await scene.SaveAsync(progress, saving.Token);
            saving.Token.ThrowIfCancellationRequested();
            stream.Position = 0;
            fileOperationCanCancel = false;
            fileOperationStatus = "Handing download to browser…";
            await RefreshFileOperationAsync();
            await files.DownloadAsync(name, stream);
            openedFileName = name;
        }
        catch (OperationCanceledException) when (disposed || saving.IsCancellationRequested) { }
        catch (Exception exception) when (IsFileException(exception))
        {
            fileError = $"Could not save the scene: {exception.Message}";
        }
        finally { saving.Dispose(); saving = null; fileOperationStatus = null; fileOperationCanCancel = false; isBusy = false; }
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

    private Task SetValidatedSceneAsync(SceneRequest scene) => InvokeAsync(async () =>
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await StopAutoplayAsync();
        if (renderLoopTask is not null) await renderLoopTask;
        ResetRenderer();
        lastScenesParameter = Scenes;
        loadedScenes = [scene];
        lastRequest = null;
        openedFileName = null;
        fileError = null;
        await OnParametersSetAsync();
        StateHasChanged();
    });

    /// <summary>Loads a request from UTF-8 JSON, leaving the stream open.</summary>
    public async Task LoadSceneAsync(Stream stream, CancellationToken cancellationToken = default) =>
        await SetValidatedSceneAsync(await SceneRequest.LoadAsync(stream, cancellationToken));

    /// <summary>Loads a request from a local file; browser hosts should use the stream overload.</summary>
    public async Task LoadSceneAsync(string path, CancellationToken cancellationToken = default) =>
        await SetValidatedSceneAsync(await SceneRequest.LoadAsync(path, cancellationToken));

    /// <summary>Captures the complete request with the current step, camera and display settings.</summary>
    public Task<SceneRequest> GetSceneAsync() => CaptureAsync();

    /// <summary>Saves the complete request, leaving the output stream open.</summary>
    public async Task SaveSceneAsync(Stream stream, CancellationToken cancellationToken = default) =>
        await (await GetSceneAsync()).SaveAsync(stream, cancellationToken);

    /// <summary>Saves the complete request to a local file.</summary>
    public async Task SaveSceneAsync(string path, CancellationToken cancellationToken = default) =>
        await (await GetSceneAsync()).SaveAsync(path, cancellationToken);
}
