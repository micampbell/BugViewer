using Microsoft.FluentUI.AspNetCore.Components;
using ServerSmoke;
var builder = WebApplication.CreateBuilder(args);
// This fixture runs from build output, including when no launch profile sets
// Development. Load the RCL's source assets without requiring a publish step.
builder.WebHost.UseStaticWebAssets();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddFluentUIComponents();
var app = builder.Build();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
