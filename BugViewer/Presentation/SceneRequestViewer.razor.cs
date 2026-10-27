using Microsoft.AspNetCore.Components;

namespace BugViewer;

/// <summary>Displays a complete portable request, with 3D step playback or 2D plots.</summary>
public partial class SceneRequestViewer
{
    /// <summary>Optional request supplied by the parent. Loading through the methods replaces the active request.</summary>
    [Parameter] public SceneRequest? Scene { get; set; }
    private SceneRequest? lastParameter;
    private SceneRequest current = new();
    private IReadOnlyList<SceneRequest> requests = [];
    private SceneViewer? sceneViewer;
    private PlotViewer? plotViewer;
    private int generation;

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
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
    }

    /// <summary>Replaces the presentation, preserving all steps and source transforms.</summary>
    public async Task LoadSceneAsync(SceneRequest scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        scene.Validate();
        await InvokeAsync(() => { SetScene(scene); StateHasChanged(); });
    }

    /// <summary>Loads a request from UTF-8 JSON, leaving the stream open.</summary>
    public async Task LoadSceneAsync(Stream stream, CancellationToken cancellationToken = default) =>
        await LoadSceneAsync(await SceneRequest.LoadAsync(stream, cancellationToken));

    /// <summary>Loads a request from a local file; browser hosts should use the stream overload.</summary>
    public async Task LoadSceneAsync(string path, CancellationToken cancellationToken = default) =>
        await LoadSceneAsync(await SceneRequest.LoadAsync(path, cancellationToken));

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
}
