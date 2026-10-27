using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BugViewer;

internal sealed class SceneFileInterop(IJSRuntime js) : IAsyncDisposable
{
    private const string ModulePath = "./_content/BugViewer/Presentation/SceneRequestViewer.razor.js";
    private const string InitializeMethod = "initialize";
    private const string DownloadMethod = "downloadScene";
    private const string DisposeMethod = "dispose";
    private IJSObjectReference? module, shortcuts;
    private Task? initialization;

    public Task InitializeAsync(ElementReference container, ElementReference input,
        ElementReference openButton, ElementReference saveButton) =>
        initialization ??= InitializeCoreAsync(container, input, openButton, saveButton);

    private async Task InitializeCoreAsync(ElementReference container, ElementReference input,
        ElementReference openButton, ElementReference saveButton)
    {
        module = await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
        shortcuts = await module.InvokeAsync<IJSObjectReference>(InitializeMethod, container, input, openButton, saveButton);
    }

    public async Task DownloadAsync(string name, Stream stream)
    {
        if (initialization is not null) await initialization;
        using var reference = new DotNetStreamReference(stream, leaveOpen: true);
        await module!.InvokeVoidAsync(DownloadMethod, name, reference);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (initialization is not null) await initialization;
            if (shortcuts is not null)
            {
                await shortcuts.InvokeVoidAsync(DisposeMethod);
                await shortcuts.DisposeAsync();
            }
            if (module is not null) await module.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
    }
}
