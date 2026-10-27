using System.Numerics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using BugViewer;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
static string Json(SceneRequest request)
{
    using var stream = new MemoryStream(); request.Save(stream); return Encoding.UTF8.GetString(stream.ToArray());
}
static SceneRequest RoundTrip(SceneRequest request)
{
    using var stream = new MemoryStream(); request.Save(stream); stream.Position = 0; return SceneRequest.Load(stream);
}
static SceneMesh Triangle(string id, MeshColoring mode = MeshColoring.PerTriangle) => new()
{
    Id = id, Vertices = [[0,0,0],[2,0,0],[0,2,0]], Triangles = [[2,0,1]],
    PrimitiveSurfaceNormals = [[0,0,1],[0,0,1],[0,0,1]], HasPrimitiveSurfaces = true,
    ColorMode = mode, Colors = mode == MeshColoring.PerVertex ? [ColorRgba.Red, ColorRgba.White, new(20,30,40,123)] : [new(20,30,40,123)]
};
static void Reject(SceneRequest request)
{
    try { request.Validate(); } catch (InvalidDataException) { return; }
    throw new Exception("Invalid request accepted.");
}
var options = BugViewerOptions.Default.Clone();
options.ShowEdgesAt = ShowEdgesAtOption.ShowMeshEdges;
options.GridSize = 73;
options.ZIsUp = false;
var camera = new CameraState(new(1.25f,-2.5f,3.75f), .73, .42, 30, 7, false);
var request = new SceneRequest
{
    Title = "Portable presentation", Heading = "Source data", Subtitle = "α & β", Options = options, Camera = camera,
    AutoResetCamera = UpdateTypes.Never,
    Meshes = Enum.GetValues<MeshColoring>().Select(mode => Triangle(mode.ToString(), mode)).ToList(),
    Paths = [new ScenePath { Id = "path", Vertices = [[1,2,3],[4,5,6]], Thickness = -1 }],
    PathBatches = [new LineData { Id = "batch", Vertices = [Vector3.Zero, Vector3.One, new(2)],
        Colors = [ColorRgba.Red], Thicknesses = [LineData.AutomaticThickness,0], FadeFactors = [.3f] }],
    PointSets = [new ScenePointSet { Id = "points", Points = [[2,3,4]], Radius = .2 }],
    Billboards = [new TextBillboard { Id = "label", Text = "Label α", Vertices = new[] { new Vector3(2,3,4) },
        BackgroundColor = new(20,30,40,50), TextColor = ColorRgba.White, RelativeX = .25f, RelativeY = 0, Scale = .4f }],
    StepGroups = [new SceneStepGroup
    {
        Meshes = [Triangle("step0"), null, Triangle("step2"), Triangle("step3")],
        PathSource = new SceneStepPathSource
        {
            Id = "step-paths", Paths = [new[] { new ScenePoint(1e12+.125,0,0), new ScenePoint(1e12+1.125,1,0) },
                new[] { new ScenePoint(2,0,0), new ScenePoint(3,1,0) },
                new[] { new ScenePoint(4,0,0), new ScenePoint(5,1,0) },
                new[] { new ScenePoint(6,0,0), new ScenePoint(7,1,0) }],
            ClosePaths = [true], Colors = [ColorRgba.Red, ColorRgba.White], Thicknesses = [2,3], HistoryStepLimit = 2
        },
        Transforms = [SceneTransform.Identity with { M41 = -1e12 }, null, SceneTransform.Identity, SceneTransform.Identity with { M41 = 5 }]
    }]
};
Assert(!JsonSerializer.IsReflectionEnabledByDefault, "Reflection serialization must remain disabled.");
var json = Json(request);
var restored = RoundTrip(request);
Assert(Json(restored) == json, "Full request did not round trip exactly.");
Assert(restored.Camera == camera && restored.Options!.GridSize == 73, "View state was lost.");
Assert(restored.PathBatches[0].Thicknesses.SequenceEqual(new[] {-1f,0f}), "Packed path breaks or automatic thickness were lost.");
Assert(restored.Billboards[0].Anchor == new Vector3(2,3,4), "Billboard anchor was lost.");
Assert(restored.StepGroups[0].Transforms![1] is null, "Null transform barrier was lost.");
Assert(restored.StepGroups[0].PathSource!.Paths[0].First().X == 1e12+.125, "Source path precision was lost.");
for (var step = 0; step < 4; step++)
{
    var originalGroup = request.StepGroups[0]; var loadedGroup = restored.StepGroups[0];
    Assert(originalGroup.ResolvePathIndices(step).SequenceEqual(loadedGroup.ResolvePathIndices(step)), "Path history changed after loading.");
    var before = StepPathBatchBuilder.Build(originalGroup, originalGroup.PathSource!, step)!;
    var after = StepPathBatchBuilder.Build(loadedGroup, loadedGroup.PathSource!, step)!;
    Assert(before.Vertices.SequenceEqual(after.Vertices) && before.Thicknesses.SequenceEqual(after.Thicknesses)
        && before.Colors.SequenceEqual(after.Colors), "Rendered stepped path changed after loading.");
    Assert(SceneGeometry.ResolveStepElements(originalGroup, originalGroup.Meshes, step).Select(x => x.Element.Id)
        .SequenceEqual(SceneGeometry.ResolveStepElements(loadedGroup, loadedGroup.Meshes, step).Select(x => x.Element.Id)), "Stepped mesh membership changed.");
}
Assert(StepPathBatchBuilder.Build(restored.StepGroups[0], restored.StepGroups[0].PathSource!,0)!.Vertices[0].X == .125f,
    "Transformation rounded source coordinates before subtracting a large translation.");
Assert(restored.StepGroups[0].ResolvePathIndices(3).SequenceEqual(new[] {2,3}), "History crossed a barrier.");
Assert(restored.StepGroups[0].ResolvePathIndices(1).SequenceEqual(new[] {1}), "Selected barrier did not show only the selected element.");
var scale = SceneTransform.Identity with { M11 = 2, M22 = 3, M33 = 4, M41 = 10 };
var normalMesh = Triangle("normal");
normalMesh.PrimitiveSurfaceNormals[0] = [1,1,1];
var transformed = SceneGeometry.ToMeshData(normalMesh, scale);
Assert(transformed.Vertices[1] == new Vector3(14,0,0), "Transform convention changed.");
Assert(Vector3.Distance(transformed.PrimitiveSurfaceNormals[0], Vector3.Normalize(new(.5f,1f/3,.25f))) < 1e-6,
    "Normals did not use the inverse transpose.");
Assert(SceneGeometry.ToMeshData(normalMesh, default(SceneTransform)).PrimitiveSurfaceNormals.All(n => n == Vector3.Zero), "Singular transform normal fallback failed.");

var plots = new SceneRequest
{
    Kind = PresentationKind.TwoDimensional, Title = "Plot sequence", InitialStep = 1,
    Steps = [new SceneRequest { Kind = PresentationKind.TwoDimensional, Plot = new PlotRequest { Heatmap = [[1,2],[3,4]] } },
        new SceneRequest { Kind = PresentationKind.TwoDimensional, Plot = new PlotRequest
        { Traces = [new PlotTrace { Name = "overlay only", X = [1,2,3], Y = [3,2,1], Type = ScenePlotType.Area, Marker = SceneMarker.Star, Closed = true }] } }]
};
Assert(Json(RoundTrip(plots)) == Json(plots), "2D plots, heatmaps, or overlay-only steps were lost.");
var completeSteps = new SceneRequest { Steps = [new SceneRequest { Meshes = [Triangle("first")] }, new SceneRequest()] };
Assert(RoundTrip(completeSteps).Steps[1].Meshes.Count == 0, "Empty complete steps changed.");
Reject(new SceneRequest { Version = 99 });
Reject(new SceneRequest { Format = "Other" });
Reject(new SceneRequest { Meshes = [Triangle("same"),Triangle("same")] });
Reject(new SceneRequest { StepGroups = [new SceneStepGroup { Transforms = [SceneTransform.Identity with { M11 = double.NaN }] }] });
var cycle = new SceneRequest(); cycle.Steps.Add(cycle); Reject(cycle);
foreach (var invalid in new[] { "null", "{}", "[]", "{", json + " {}" })
{
    try { using var stream = new MemoryStream(Encoding.UTF8.GetBytes(invalid)); SceneRequest.Load(stream); throw new Exception("Invalid JSON accepted."); }
    catch (Exception e) when (e is JsonException or InvalidDataException) { }
}
await using (var stream = new MemoryStream())
{
    await request.SaveAsync(stream); Assert(stream.CanWrite, "Save closed the stream."); stream.Position = 0;
    Assert(Json(await SceneRequest.LoadAsync(stream)) == json && stream.CanRead, "Async stream round trip failed.");
}
var file = Path.Combine(Path.GetTempPath(), $"bugviewer-request-{Guid.NewGuid():N}.json");
try
{
    request.Save(file); Assert(Json(SceneRequest.Load(file)) == json, "Sync file round trip failed.");
    await request.SaveAsync(file); Assert(Json(await SceneRequest.LoadAsync(file)) == json, "Async file round trip failed.");
    using var canceled = new CancellationTokenSource(); canceled.Cancel();
    try { await plots.SaveAsync(file, canceled.Token); throw new Exception("Canceled save succeeded."); } catch (OperationCanceledException) { }
    Assert(File.ReadAllText(file) == json, "Canceled save damaged the previous file.");
    try { (request with { Version = 99 }).Save(file); throw new Exception("Invalid save succeeded."); } catch (InvalidDataException) { }
    Assert(File.ReadAllText(file) == json, "Invalid save damaged the previous file.");
    Assert(!Directory.EnumerateFiles(Path.GetDirectoryName(file)!, Path.GetFileName(file)+".*.tmp").Any(), "Temporary files leaked.");
}
finally { File.Delete(file); }

using var services = new ServiceCollection().BuildServiceProvider();
await using var renderer = new TestRenderer(services);
await renderer.Dispatcher.InvokeAsync(async () =>
{
    var gpu = new TestViewer(); await renderer.AttachAsync(gpu);
    var display = new TestSceneViewer(); await renderer.AttachAsync(display);
    await display.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string,object?> { [nameof(SceneViewer.Scenes)] = new[] { restored } }));
    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
    typeof(SceneViewer).GetField("viewer", flags)!.SetValue(display, gpu);
    await (Task)typeof(SceneViewer).GetMethod("OnViewerReadyAsync", flags)!.Invoke(display, null)!;
    Assert(gpu.GetCameraState() == restored.Camera, "Camera was not restored after scene upload.");
    Assert(gpu.Options.GridSize == 73, "Geometry upload overwrote the saved grid settings.");
    await display.SetStepAsync(3);
    var captured = await display.CaptureAsync();
    Assert(captured.InitialStep == 3 && captured.StepGroups[0].PathSource!.Count == 4,
        "Capture flattened the request to a single frame.");
    Assert(captured.PathBatches.Count == 1 && captured.Paths.Count == 1, "Generated mesh edges leaked into the request.");
    Assert(captured.Meshes[0].Vertices[1].SequenceEqual(new float[] {2,0,0}), "Capture baked a frame transform into source geometry.");
    await display.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string,object?> { [nameof(SceneViewer.Scenes)] = new[] { completeSteps } }));
    await display.SetStepAsync(1);
    Assert(!display.VisibleSceneMeshes().Any(), "Empty frame retained old meshes.");
    var loader = new TestRequestViewer(); await renderer.AttachAsync(loader);
    await loader.LoadSceneAsync(restored);
    try { await loader.LoadSceneAsync(new SceneRequest { Version = 99 }); throw new Exception("Invalid load accepted."); }
    catch (InvalidDataException) { }
    Assert((await loader.GetSceneAsync()).RequestId == restored.RequestId, "Invalid load replaced the current request.");

    // Exercise the same file handler used by Open Scene and Ctrl+Alt+O, including mode changes.
    var openFile = typeof(SceneRequestViewer).GetMethod("OpenSceneFileAsync", flags)!;
    async Task Open(SceneRequest scene, string name)
    {
        var file = new MemoryBrowserFile(name, Encoding.UTF8.GetBytes(Json(scene)));
        await (Task)openFile.Invoke(loader, [new InputFileChangeEventArgs([file])])!;
    }
    var single = new SceneRequest { Title = "Single frame", Meshes = [Triangle("single")] };
    object? priorKey = null;
    foreach (var candidate in new[] { single, completeSteps, restored, plots, single })
    {
        await Open(candidate, "switch.bugscene.json");
        var frames = loader.Markup();
        var view = frames.Single(frame => frame.FrameType == RenderTreeFrameType.Component
            && (frame.ComponentType == typeof(SceneViewer) || frame.ComponentType == typeof(PlotViewer)));
        Assert(view.ComponentType == (candidate.Kind == PresentationKind.TwoDimensional ? typeof(PlotViewer) : typeof(SceneViewer)),
            "Opening a file did not select the correct rendering surface.");
        Assert(!Equals(view.ComponentKey, priorKey), "Loading retained the old renderer and playback state.");
        priorKey = view.ComponentKey;
        Assert(Json(await loader.GetSceneAsync()) == Json(candidate), "Opening changed or flattened the request.");
        if (candidate.Kind == PresentationKind.ThreeDimensional)
        {
            await display.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string,object?>
                { [nameof(SceneViewer.Scenes)] = new[] { candidate } }));
            var controlsVisible = display.Markup().Any(frame => frame.FrameType == RenderTreeFrameType.Attribute
                && frame.AttributeName == "class" && Equals(frame.AttributeValue, "step-controls"));
            Assert(controlsVisible == (candidate.Steps.Count > 1 || candidate.StepGroups.Any(group => group.Count > 1)),
                "Playback controls did not appear or disappear when switching scene types.");
        }
        await using var saved = new MemoryStream();
        await loader.SaveSceneAsync(saved);
        saved.Position = 0;
        Assert(Json(SceneRequest.Load(saved)) == Json(candidate), "Saving after switching lost source steps.");
    }
    foreach (var file in new[] { new MemoryBrowserFile("bad.json", Encoding.UTF8.GetBytes("not json")),
        new MemoryBrowserFile("oversized.json", [], long.MaxValue) })
    {
        await (Task)openFile.Invoke(loader, [new InputFileChangeEventArgs([file])])!;
        Assert((await loader.GetSceneAsync()).RequestId == single.RequestId, "Failed file open replaced the current scene.");
        Assert(typeof(SceneRequestViewer).GetField("fileError", flags)!.GetValue(loader) is string { Length: > 0 },
            "Failed file open did not report an error.");
    }

    var fileRuntime = new SceneFileRuntime();
    var interopType = typeof(SceneRequestViewer).Assembly.GetType("BugViewer.SceneFileInterop")!;
    var fileInterop = Activator.CreateInstance(interopType, fileRuntime)!;
    await (Task)interopType.GetMethod("InitializeAsync")!.Invoke(fileInterop,
        [default(ElementReference), default(ElementReference), default(ElementReference), default(ElementReference)])!;
    typeof(SceneRequestViewer).GetField("files", flags)!.SetValue(loader, fileInterop);
    Task FileCommand(string name) => (Task)typeof(SceneRequestViewer).GetMethod(name, flags)!.Invoke(loader, null)!;
    await Open(restored, "original.bugscene.json");
    await FileCommand("BeginSaveAsAsync");
    Assert((string)typeof(SceneRequestViewer).GetField("saveAsName", flags)!.GetValue(loader)! == "original.bugscene.json",
        "Save As did not suggest the opened filename.");
    await FileCommand("CancelSaveAsAsync");
    Assert(fileRuntime.Downloads.Count == 0, "Canceling Save As downloaded a file.");
    foreach (var badName in new[] { " ", "../part", "part?", "part." })
    {
        typeof(SceneRequestViewer).GetField("saveAsName", flags)!.SetValue(loader, badName);
        await FileCommand("ConfirmSaveAsAsync");
        Assert(fileRuntime.Downloads.Count == 0, "An invalid filename was downloaded.");
    }
    typeof(SceneRequestViewer).GetField("saveAsName", flags)!.SetValue(loader, "renamed scene");
    await FileCommand("ConfirmSaveAsAsync");
    var download = fileRuntime.Downloads.Single();
    Assert(download.Name == "renamed scene.bugscene.json", "Save As ignored the chosen name or scene extension.");
    using var downloaded = new MemoryStream(download.Data);
    Assert(Json(SceneRequest.Load(downloaded)) == Json(restored), "Save As lost the complete stepped request.");
});

if (args is ["--write-examples", var directory])
{
    Directory.CreateDirectory(directory);
    var example = new SceneRequest
    {
        Title = "Stepped pyramid", Subtitle = "Scrub the slider to translate the pyramid and reveal paths.",
        AutoResetCamera = UpdateTypes.Never, ShowMeshBorders = false,
        StepGroups = [new SceneStepGroup
        {
            Meshes = [new SceneMesh
            {
                Id = "pyramid", Vertices = [[-2,-2,0],[2,-2,0],[2,2,0],[-2,2,0],[0,0,3]],
                Triangles = [[0,2,1],[0,3,2],[0,1,4],[1,2,4],[2,3,4],[3,0,4]],
                Colors = [new(130,130,150),new(130,130,150),new(220,80,70),new(60,150,220),new(80,190,130),new(230,180,60)]
            }],
            PathSource = new SceneStepPathSource
            {
                Id = "toolpath", Paths = Enumerable.Range(0,4).Select(i => (IEnumerable<ScenePoint>)new[] {new ScenePoint(-3,i-2,.1),new ScenePoint(3,i-2,.1)}).ToArray(),
                Thicknesses = [.05], Colors = [ColorRgba.Red], HistoryStepLimit = 4
            },
            Transforms = Enumerable.Range(0,4).Select(i => (SceneTransform?)(SceneTransform.Identity with { M41 = i })).ToArray()
        }]
    };
    example.Save(Path.Combine(directory,"example.bugscene.json"));
    (example with { Title = "Pyramid", Subtitle = "A single scene without playback controls.",
        StepGroups = [], Meshes = [example.StepGroups[0].Meshes[0]!] }).Save(Path.Combine(directory,"single.bugscene.json"));
    plots.Save(Path.Combine(directory,"plots.bugscene.json"));
}
Console.WriteLine("PASS: portable request persistence, 3D/2D steps, precision, transform barriers, normals, validation, cancellation, full-request capture, and viewer replacement.");

sealed class TestSceneViewer : SceneViewer
{
    public RenderTreeFrame[] Markup() { using var builder = new RenderTreeBuilder(); base.BuildRenderTree(builder); return builder.GetFrames().Array.Take(builder.GetFrames().Count).ToArray(); }
    protected override void BuildRenderTree(RenderTreeBuilder builder) { }
    protected override Task OnAfterRenderAsync(bool firstRender) => Task.CompletedTask;
}
sealed class TestRequestViewer : SceneRequestViewer
{
    public RenderTreeFrame[] Markup() { using var builder = new RenderTreeBuilder(); base.BuildRenderTree(builder); return builder.GetFrames().Array.Take(builder.GetFrames().Count).ToArray(); }
    protected override void BuildRenderTree(RenderTreeBuilder builder) { }
}

sealed class MemoryBrowserFile(string name, byte[] bytes, long? size = null) : IBrowserFile
{
    public string Name => name;
    public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
    public long Size => size ?? bytes.LongLength;
    public string ContentType => "application/json";
    public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Size > maxAllowedSize) throw new IOException("File exceeds the input limit.");
        return new MemoryStream(bytes, writable: false);
    }
}
sealed class TestViewer : global::BugViewer.BugViewer
{
    protected override void BuildRenderTree(RenderTreeBuilder builder) { }
    protected override Task OnAfterRenderAsync(bool firstRender) => Task.CompletedTask;
}

sealed class TestRenderer(IServiceProvider services) : Renderer(services, NullLoggerFactory.Instance)
{
    public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
    public Task AttachAsync(IComponent component) => RenderRootComponentAsync(AssignRootComponentId(component));
    protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;
    protected override void HandleException(Exception exception) => throw new Exception("Component failure", exception);
}

sealed class RecordingModule : IJSObjectReference
{
    public List<string> Calls { get; } = [];
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
    {
        Calls.Add(identifier);
        return ValueTask.FromResult(default(TValue)!);
    }
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

sealed class SceneFileRuntime : IJSRuntime, IJSObjectReference
{
    public List<(string Name, byte[] Data)> Downloads { get; } = [];
    public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
    {
        if (identifier is "import" or "initialize") return (TValue)(object)this;
        if (identifier == "downloadScene")
        {
            using var copy = new MemoryStream();
            await ((DotNetStreamReference)args![1]!).Stream.CopyToAsync(copy);
            Downloads.Add(((string)args[0]!, copy.ToArray()));
        }
        return default!;
    }
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        => InvokeAsync<TValue>(identifier, args);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
