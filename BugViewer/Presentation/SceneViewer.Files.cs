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
    /// <summary>Maximum browser input file size in bytes. Defaults to 1 GiB.</summary>
    [Parameter] public long MaxSceneFileSize { get; set; } = 1024L * 1024 * 1024;
    /// <summary>Suggested Save As filename for a supplied request.</summary>
    [Parameter] public string FileName { get; set; } = "scene.bugscene.json";

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
    private CancellationTokenSource? loading;
    private int loadPercent;
    private bool isBusy, disposed;
    private string? openedFileName, fileError;
    private ElementReference saveAsDialog, saveAsInput;
    private readonly string saveAsTitleId = $"scene-save-title-{Guid.NewGuid():N}";
    private string saveAsName = "";
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
        try
        {
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
            await SetValidatedSceneAsync(scene);
            openedFileName = args.File.Name;
        }
        catch (OperationCanceledException) when (disposed || loading.IsCancellationRequested) { }
        catch (Exception exception) when (IsFileException(exception))
        {
            fileError = $"Could not open the scene: {exception.Message}";
        }
        finally { loading.Dispose(); loading = null; isBusy = false; }
    }

    private void CancelOpen() => loading?.Cancel();

    private Task BeginSaveAsAsync()
    {
        if (isBusy || disposed || files is null || ActiveScenes.Count == 0) return Task.CompletedTask;
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
        if (isBusy || disposed || files is null || ActiveScenes.Count == 0) return;
        isBusy = true;
        fileError = null;
        try
        {
            await using var stream = new MemoryStream();
            await SaveSceneAsync(stream, lifetime.Token);
            stream.Position = 0;
            await files.DownloadAsync(name, stream);
            openedFileName = name;
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
