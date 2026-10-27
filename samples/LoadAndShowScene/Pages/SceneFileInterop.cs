using Microsoft.JSInterop;

namespace LoadAndShowScene.Pages;

internal sealed class SceneFileInterop(IJSRuntime js) : IAsyncDisposable
{
    private const string ModulePath = "./Pages/Home.razor.js";
    private const string DownloadMethod = "downloadScene";
    private IJSObjectReference? module;

    public async Task DownloadAsync(string name, Stream stream)
    {
        module ??= await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
        using var reference = new DotNetStreamReference(stream, leaveOpen: true);
        await module.InvokeVoidAsync(DownloadMethod, name, reference);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (module is not null) await module.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
    }
}
