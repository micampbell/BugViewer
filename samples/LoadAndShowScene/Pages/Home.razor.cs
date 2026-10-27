using BugViewer;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace LoadAndShowScene.Pages;

public partial class Home
{
    private const long MaxSceneBytes = 256L * 1024 * 1024;
    private SceneRequestViewer? viewer;
    private SceneFileInterop? files;
    private bool isBusy;
    private bool hasScene;
    private int fileInputVersion;
    private string sceneName = "scene.bugscene.json";
    private string status = "Choose a scene file (up to 256 MB).";
    private string? error;

    private async Task LoadScene(InputFileChangeEventArgs args)
    {
        if (viewer is null || isBusy) return;
        isBusy = true;
        error = null;
        status = $"Loading {args.File.Name}…";
        try
        {
            await using var stream = args.File.OpenReadStream(MaxSceneBytes);
            var scene = await SceneRequest.LoadAsync(stream);
            await viewer.LoadSceneAsync(scene);
            sceneName = args.File.Name;
            hasScene = true;
            status = $"{sceneName}: {scene.Kind}, {scene.Meshes.Count} meshes, {scene.Paths.Count + scene.PathBatches.Count} paths, {scene.StepGroups.Count} step groups, {scene.Steps.Count} complete steps.";
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException
            or InvalidDataException or IOException or InvalidOperationException or NotSupportedException or JSException)
        {
            error = $"Could not load the scene: {exception.Message}";
            status = hasScene ? $"Current scene: {sceneName}" : "Choose a scene file (up to 256 MB).";
        }
        finally
        {
            isBusy = false;
            fileInputVersion++;
        }
    }

    private async Task SaveScene()
    {
        if (viewer is null || isBusy) return;
        isBusy = true;
        error = null;
        try
        {
            await using var stream = new MemoryStream();
            await viewer.SaveSceneAsync(stream);
            stream.Position = 0;
            files ??= new SceneFileInterop(JS);
            await files.DownloadAsync(sceneName, stream);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException
            or InvalidDataException or IOException or InvalidOperationException or NotSupportedException or JSException)
        {
            error = $"Could not save the scene: {exception.Message}";
        }
        finally
        {
            isBusy = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (files is not null) await files.DisposeAsync();
    }
}
