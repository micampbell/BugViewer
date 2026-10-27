using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components;

namespace BugViewer;

internal sealed class PlotlyInterop(IJSRuntime js) : IAsyncDisposable
{
    private const string ModulePath = "./_content/BugViewer/Presentation/PlotViewer.razor.js";
    private IJSObjectReference? module;
    private ElementReference plotElement;
    private async ValueTask<IJSObjectReference> ModuleAsync() => module ??= await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
    public async ValueTask RenderAsync(ElementReference element, PlotRequest plot, string title)
    {
        plotElement = element;
        await (await ModuleAsync()).InvokeVoidAsync("render", element, plot, title);
    }
    public async ValueTask DisposeAsync()
    {
        if (module is not null)
        {
            try { await module.InvokeVoidAsync("dispose", plotElement); await module.DisposeAsync(); }
            catch (JSDisconnectedException) { }
        }
    }
}
