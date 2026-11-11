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
    [Parameter] public string FileName { get; set; } = "scene.bugscene";

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
    private string? openedFileName, fileError, fileWarning;
    private ElementReference saveAsDialog, saveAsInput;
    private readonly string saveAsTitleId = $"scene-save-title-{Guid.NewGuid():N}";
    private string saveAsName = "";
    private string saveStartStep = "1", saveEndStep = "1", saveStepStride = "1";
    private string saveMaximumEarlierObjects = "50000";
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

    private Task OpenSceneFileAsync(InputFileChangeEventArgs args) =>
        OpenSceneStreamAsync(args.File.Name, args.File.Size,
            token => Task.FromResult(args.File.OpenReadStream(MaxSceneFileSize, token)));

    /// <summary>Opens a browser-provided stream using the same progress and validation as the Open control.</summary>
    public async Task OpenBrowserSceneAsync(string name, long size, IJSStreamReference file)
    {
        // A launch can arrive while an Open or Save operation is in progress.
        while (isBusy && !disposed) await Task.Delay(100, lifetime.Token);
        if (!disposed)
            await OpenSceneStreamAsync(name, size, token => file.OpenReadStreamAsync(MaxSceneFileSize, token).AsTask());
    }

    private async Task OpenSceneStreamAsync(string name, long size, Func<CancellationToken, Task<Stream>> openStream)
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
            if (size < 0 || size > MaxSceneFileSize)
                throw new IOException($"The file is {size / 1048576.0:N1} MiB; this viewer allows {MaxSceneFileSize / 1048576.0:N1} MiB. Increase MaxSceneFileSize in the host to open larger scenes.");
            await using var stream = await openStream(loading.Token);
            using var progress = new SceneReadProgressStream(stream, bytes => InvokeAsync(() =>
            {
                loadPercent = (int)Math.Min(100, bytes * 100 / Math.Max(1, size));
                StateHasChanged();
            }));
            var scene = await SceneRequest.LoadAsync(progress, loading.Token);
            loading.Token.ThrowIfCancellationRequested();
            if (disposed) return;
            fileOperationCanCancel = false;
            fileOperationStatus = "Displaying scene…";
            await RefreshFileOperationAsync();
            await SetValidatedSceneAsync(scene);
            openedFileName = name;
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
        saveAsName = SceneFileName(openedFileName ?? FileName);
        saveStartStep = "1";
        saveEndStep = Math.Max(1, StepCount).ToString();
        saveStepStride = "1";
        saveMaximumEarlierObjects = "50000";
        saveAsError = null;
        showSaveAsAfterRender = true;
        return InvokeAsync(StateHasChanged);
    }

    /// <summary>Opens the Save As dialog for the current presentation.</summary>
    public Task ShowSaveAsAsync() => BeginSaveAsAsync();

    private Task CancelSaveAsAsync() => files is null ? Task.CompletedTask : files.CloseSaveAsAsync(saveAsDialog);

    private Task ConfirmSaveAsAsync() => ConfirmSaveAsCoreAsync(null);

    /// <summary>Completes a save after the browser's file picker returns a destination.</summary>
    [JSInvokable]
    public async Task ConfirmPickedSceneSaveAsync(string name, string start, string end, string stride, IJSObjectReference? handle, string maximumEarlierObjects = "50000")
    {
        try
        {
            await InvokeAsync(async () =>
            {
                saveAsName = name;
                saveStartStep = start;
                saveEndStep = end;
                saveStepStride = stride;
                saveMaximumEarlierObjects = maximumEarlierObjects;
                if (handle is not null && !name.EndsWith(".bugscene", StringComparison.OrdinalIgnoreCase))
                {
                    saveAsError = "Choose a file name ending in .bugscene.";
                    StateHasChanged();
                    return;
                }
                await ConfirmSaveAsCoreAsync(handle);
                if (!disposed) StateHasChanged();
            });
        }
        finally { if (handle is not null) await handle.DisposeAsync(); }
    }

    /// <summary>Displays an error from the browser's save picker.</summary>
    [JSInvokable]
    public Task ReportSceneSaveError(string message) => InvokeAsync(() =>
    {
        saveAsError = message;
        StateHasChanged();
    });

    private static string SceneFileName(string name)
    {
        if (name.EndsWith(".bugscene", StringComparison.OrdinalIgnoreCase)) return name;
        if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) name = name[..^5];
        return name.EndsWith(".bugscene", StringComparison.OrdinalIgnoreCase) ? name : name + ".bugscene";
    }

    private async Task ConfirmSaveAsCoreAsync(IJSObjectReference? handle)
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
        if (StepCount > 0 && (!int.TryParse(saveMaximumEarlierObjects, out var maximum) || maximum < 0))
        {
            saveAsError = "Enter a nonnegative maximum number of earlier objects.";
            return;
        }
        name = SceneFileName(name);
        await files.CloseSaveAsAsync(saveAsDialog);
        await SaveSceneCoreAsync(name, start, end, stride, handle);
    }

    private Task DownloadSceneAsync(string name, int start, int end, int stride) =>
        SaveSceneCoreAsync(name, start, end, stride, null);

    private async Task SaveSceneCoreAsync(string name, int start, int end, int stride, IJSObjectReference? handle)
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
                scene = await SceneRangeExporter.ExportBoundedAsync(scene, start - 1, end - 1, stride,
                    int.Parse(saveMaximumEarlierObjects), saving.Token);
            fileOperationStatus = "Writing scene…";
            await RefreshFileOperationAsync();
            saving.Token.ThrowIfCancellationRequested();
            using var progress = new SceneWriteProgressStream(stream);
            await scene.SaveAsync(progress, saving.Token);
            saving.Token.ThrowIfCancellationRequested();
            stream.Position = 0;
            fileOperationCanCancel = false;
            fileOperationStatus = handle is null ? "Handing download to browser…" : "Saving file…";
            await RefreshFileOperationAsync();
            if (handle is null) await files.DownloadAsync(name, stream);
            else await files.WriteFileAsync(handle, stream);
            openedFileName = name;
            fileWarning = HistoryWarning(scene.HistoryOmission);
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
        fileWarning = HistoryWarning(scene.HistoryOmission);
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

    private static string? HistoryWarning(SceneHistoryOmission? omission) => omission is null ? null :
        $"Some objects from before saved step {omission.StartStep} are missing because the file was limited to {omission.MaximumEarlierObjects:N0} earlier objects.";
}
