using System.Numerics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using BugViewer;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
static bool HasActiveBadge(RenderTreeFrame[] frames) => frames.Any(frame =>
    frame.FrameType == RenderTreeFrameType.Markup && frame.MarkupContent.Contains("active-badge"));
static EventCallback<MouseEventArgs> CloseHandler(RenderTreeFrame[] frames)
{
    for (var index = 0; index < frames.Length; index++)
    {
        if (frames[index].FrameType != RenderTreeFrameType.Component
            || frames[index].ComponentType != typeof(Microsoft.FluentUI.AspNetCore.Components.FluentButton)) continue;
        var attributes = frames.Skip(index + 1).TakeWhile(frame => frame.FrameType == RenderTreeFrameType.Attribute).ToArray();
        if (attributes.Any(frame => frame.AttributeName == "Title" && Equals(frame.AttributeValue, "Close viewer")))
            return (EventCallback<MouseEventArgs>)attributes.Single(frame => frame.AttributeName == "OnClick").AttributeValue;
    }
    throw new Exception("Close button not found.");
}
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
    var controls = new TestSceneViewer(); await renderer.AttachAsync(controls);
    int closedId = -1, readyCount = 0;
    var onClose = EventCallback.Factory.Create<int>(controls, id => closedId = id);
    var onReady = EventCallback.Factory.Create(controls, () => readyCount++);
    await controls.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
    {
        [nameof(SceneViewer.ViewerId)] = 42, [nameof(SceneViewer.IsActive)] = true,
        [nameof(SceneViewer.Scenes)] = new[] { restored },
        [nameof(SceneViewer.OnClose)] = onClose, [nameof(SceneViewer.OnReady)] = onReady
    }));
    var header = controls.Markup();
    Assert(HasActiveBadge(header)
        && header.Any(frame => frame.FrameType == RenderTreeFrameType.Text && frame.TextContent == restored.Title)
        && header.Any(frame => frame.FrameType == RenderTreeFrameType.Text && frame.TextContent == restored.Heading)
        && header.Any(frame => frame.FrameType == RenderTreeFrameType.Text && frame.TextContent == restored.Subtitle),
        "3D viewer header omitted its active indicator or scene metadata.");
    await CloseHandler(header).InvokeAsync(new MouseEventArgs());
    Assert(closedId == 42, "3D close button did not pass the viewer ID.");
    await controls.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
    {
        [nameof(SceneViewer.IsActive)] = false, [nameof(SceneViewer.OnClose)] = default(EventCallback<int>)
    }));
    header = controls.Markup();
    Assert(!HasActiveBadge(header)
        && !header.Any(frame => frame.FrameType == RenderTreeFrameType.Attribute
            && frame.AttributeName == "Title" && Equals(frame.AttributeValue, "Close viewer")),
        "Inactive or non-closable 3D viewer showed the corresponding control.");

    await controls.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
    {
        [nameof(SceneViewer.IsActive)] = true, [nameof(SceneViewer.Scenes)] = new[] { plots },
        [nameof(SceneViewer.OnClose)] = onClose
    }));
    var plotFrames = controls.Markup();
    var plotIndex = Array.FindIndex(plotFrames, frame => frame.FrameType == RenderTreeFrameType.Component && frame.ComponentType == typeof(PlotViewer));
    Assert(plotIndex >= 0, "2D scene did not render a PlotViewer.");
    var plotAttributes = plotFrames.Skip(plotIndex + 1).TakeWhile(frame => frame.FrameType == RenderTreeFrameType.Attribute)
        .ToDictionary(frame => frame.AttributeName, frame => frame.AttributeValue);
    Assert(Equals(plotAttributes[nameof(PlotViewer.ViewerId)], 42)
        && Equals(plotAttributes[nameof(PlotViewer.IsActive)], true)
        && !plotAttributes.ContainsKey(nameof(PlotViewer.HideHeader)), "2D viewer header parameters were not forwarded.");
    closedId = -1;
    await ((EventCallback<int>)plotAttributes[nameof(PlotViewer.OnClose)]).InvokeAsync(42);
    await ((EventCallback)plotAttributes[nameof(PlotViewer.OnReady)]).InvokeAsync();
    Assert(closedId == 42 && readyCount == 1, "2D viewer callbacks were not forwarded.");
    var plot = new TestPlotViewer(); await renderer.AttachAsync(plot);
    await plot.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
    {
        [nameof(PlotViewer.ViewerId)] = 42, [nameof(PlotViewer.IsActive)] = true,
        [nameof(PlotViewer.Scenes)] = new[] { plots }, [nameof(PlotViewer.OnClose)] = onClose
    }));
    var plotHeader = plot.Markup();
    Assert(HasActiveBadge(plotHeader),
        "2D viewer did not show the active indicator.");
    closedId = -1;
    await CloseHandler(plotHeader).InvokeAsync(new MouseEventArgs());
    Assert(closedId == 42, "2D close button did not pass the viewer ID.");

    var gpu = new TestViewer(); await renderer.AttachAsync(gpu);
    var display = new TestSceneViewer(); await renderer.AttachAsync(display);
    await display.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string,object?>
    {
        [nameof(SceneViewer.Scenes)] = new[] { restored }, [nameof(SceneViewer.OnReady)] = onReady
    }));
    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
    typeof(SceneViewer).GetField("viewer", flags)!.SetValue(display, gpu);
    await (Task)typeof(SceneViewer).GetMethod("OnViewerReadyAsync", flags)!.Invoke(display, null)!;
    Assert(readyCount == 2, "3D viewer did not report readiness.");
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
    var parameterViewer = new TestRequestViewer(); await renderer.AttachAsync(parameterViewer);
    var sceneParameters = ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(SceneViewer.Scene)] = request });
    await parameterViewer.SetParametersAsync(sceneParameters);
    await parameterViewer.LoadSceneAsync(plots);
    await parameterViewer.SetParametersAsync(sceneParameters);
    Assert((await parameterViewer.GetSceneAsync()).RequestId == plots.RequestId,
        "A parent rerender replaced the scene opened by the viewer.");
    await parameterViewer.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(SceneViewer.Scene)] = restored }));
    Assert((await parameterViewer.GetSceneAsync()).RequestId == restored.RequestId,
        "A newly supplied scene did not replace the opened scene.");

    // Exercise the same file handler used by Open Scene and Ctrl+Alt+O, including mode changes.
    var openFile = typeof(SceneViewer).GetMethod("OpenSceneFileAsync", flags)!;
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
            && (frame.ComponentType == typeof(global::BugViewer.BugViewer) || frame.ComponentType == typeof(PlotViewer)));
        Assert(view.ComponentType == (candidate.Kind == PresentationKind.TwoDimensional ? typeof(PlotViewer) : typeof(global::BugViewer.BugViewer)),
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
        Assert(typeof(SceneViewer).GetField("fileError", flags)!.GetValue(loader) is string { Length: > 0 },
            "Failed file open did not report an error.");
    }

    var fileRuntime = new SceneFileRuntime();
    var interopType = typeof(SceneViewer).Assembly.GetType("BugViewer.SceneFileInterop")!;
    var fileInterop = Activator.CreateInstance(interopType, fileRuntime)!;
    await (Task)interopType.GetMethod("InitializeAsync")!.Invoke(fileInterop,
        [default(ElementReference), default(ElementReference), default(ElementReference), default(ElementReference)])!;
    typeof(SceneViewer).GetField("files", flags)!.SetValue(loader, fileInterop);
    Task FileCommand(string name) => (Task)typeof(SceneViewer).GetMethod(name, flags)!.Invoke(loader, null)!;
    await Open(restored, "original.bugscene.json");
    await loader.ShowSaveAsAsync();
    Assert((string)typeof(SceneViewer).GetField("saveAsName", flags)!.GetValue(loader)! == "original.bugscene.json",
        "Save As did not suggest the opened filename.");
    await FileCommand("CancelSaveAsAsync");
    Assert(fileRuntime.Downloads.Count == 0, "Canceling Save As downloaded a file.");
    foreach (var badName in new[] { " ", "../part", "part?", "part." })
    {
        typeof(SceneViewer).GetField("saveAsName", flags)!.SetValue(loader, badName);
        await FileCommand("ConfirmSaveAsAsync");
        Assert(fileRuntime.Downloads.Count == 0, "An invalid filename was downloaded.");
    }
    typeof(SceneViewer).GetField("saveAsName", flags)!.SetValue(loader, "renamed scene");
    await FileCommand("ConfirmSaveAsAsync");
    var download = fileRuntime.Downloads.Single();
    Assert(download.Name == "renamed scene.bugscene.json", "Save As ignored the chosen name or scene extension.");
    using var downloaded = new MemoryStream(download.Data);
    Assert(Json(SceneRequest.Load(downloaded)) == Json(restored), "Save As lost the complete stepped request.");
});

await renderer.Dispatcher.InvokeAsync(async () =>
{
    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
    var gpu = new TestViewer(); await renderer.AttachAsync(gpu);
    var module = new PresentationModule();
    typeof(global::BugViewer.BugViewer).GetField("_module", flags)!.SetValue(gpu, module);
    var lifecycle = typeof(global::BugViewer.BugViewer).GetField("_lifecycleState", flags)!;
    lifecycle.SetValue(gpu, Enum.Parse(lifecycle.FieldType, "Ready"));
    var display = new TestSceneViewer(); await renderer.AttachAsync(display);
    var first = new SceneRequest { Meshes = [Triangle("first")], ShowMeshBorders = false };
    var second = new SceneRequest { Meshes = [Triangle("second")], ShowMeshBorders = false };
    var timeline = new SceneRequest { Steps = [first, second], ShowMeshBorders = false };
    await display.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(SceneViewer.Scenes)] = new[] { timeline } }));
    typeof(SceneViewer).GetField("viewer", flags)!.SetValue(display, gpu);
    await (Task)typeof(SceneViewer).GetMethod("OnViewerReadyAsync", flags)!.Invoke(display, null)!;
    var meshField = typeof(global::BugViewer.BugViewer).GetField("meshes", flags)!;
    var preparedFirst = ((List<MeshData>)meshField.GetValue(gpu)!)[0];
    await display.SetStepAsync(1);
    Assert(((List<MeshData>)meshField.GetValue(gpu)!)[0].Id == "second", "Frame switch retained the previous mesh.");
    var rayArguments = new object[] { new Vector3(.2f, .2f, 5), -Vector3.UnitZ, "", -1, 0f, Vector3.Zero };
    Assert((bool)typeof(global::BugViewer.BugViewer).GetMethod("DoesRayGoThroughTriangle", flags)!.Invoke(gpu, rayArguments)!
        && (string)rayArguments[2] == "second" && (int)rayArguments[3] == 0,
        "Cached picking data did not follow the visible mesh.");
    var sphere = typeof(global::BugViewer.BugViewer).GetField("BoundingSphere", flags)!.GetValue(gpu)!;
    var center = (Vector3)sphere.GetType().GetField("Center", flags)!.GetValue(sphere)!;
    var radiusSquared = (float)sphere.GetType().GetField("RadiusSquared", flags)!.GetValue(sphere)!;
    Assert(((List<MeshData>)meshField.GetValue(gpu)!).SelectMany(mesh => mesh.Vertices)
        .All(point => Vector3.DistanceSquared(point, center) <= radiusSquared * 1.0001f),
        "Cached playback bounds do not enclose the visible geometry.");
    var uploaded = module.UploadedBytes;
    await display.SetStepAsync(0);
    Assert(ReferenceEquals(preparedFirst, ((List<MeshData>)meshField.GetValue(gpu)!)[0]), "Revisiting a step rebuilt its source mesh.");
    Assert(module.UploadedBytes == uploaded, "Revisiting a resident step uploaded geometry again.");
    Assert(module.MaximumChunk <= 1024 * 1024, "An upload exceeded the bounded interop chunk size.");
    Assert(module.Commits >= 3, "Step changes did not use complete-frame commits.");
    await gpu.ChangeMeshColorsAsync("first", [new ColorRgba(0, 0, 255)]);
    await display.SetStepAsync(0);
    Assert(module.UploadedBytes > uploaded, "Inspection color changes did not invalidate the cached upload.");
    await display.DisposeAsync();

    var restoredDisplay = new TestSceneViewer(); await renderer.AttachAsync(restoredDisplay);
    await restoredDisplay.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
    {
        [nameof(SceneViewer.Scenes)] = new[] { first with { Camera = camera, Options = options, Billboards = request.Billboards } }
    }));
    typeof(SceneViewer).GetField("viewer", flags)!.SetValue(restoredDisplay, gpu);
    module.Calls.Clear();
    await (Task)typeof(SceneViewer).GetMethod("OnViewerReadyAsync", flags)!.Invoke(restoredDisplay, null)!;
    Assert(module.Calls.Contains("updateDisplayOptions") && module.Calls.Contains("writeViewMatrix")
        && module.Labels == 1, "Saved view state and initial billboards did not reach the renderer.");
    await restoredDisplay.DisposeAsync();

    var loader = new TestRequestViewer(); await renderer.AttachAsync(loader);
    var open = typeof(SceneViewer).GetMethod("OpenSceneFileAsync", flags)!;
    await (Task)open.Invoke(loader, [new InputFileChangeEventArgs([new MemoryBrowserFile("large.json", Encoding.UTF8.GetBytes(Json(first)), 338114058)])])!;
    Assert((await loader.GetSceneAsync()).RequestId == first.RequestId, "A 322.5 MiB file is still rejected by the default allowance.");
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
sealed class TestRequestViewer : SceneViewer
{
    public RenderTreeFrame[] Markup() { using var builder = new RenderTreeBuilder(); base.BuildRenderTree(builder); return builder.GetFrames().Array.Take(builder.GetFrames().Count).ToArray(); }
    protected override void BuildRenderTree(RenderTreeBuilder builder) { }
    protected override Task OnAfterRenderAsync(bool firstRender) => Task.CompletedTask;
}
sealed class TestPlotViewer : PlotViewer
{
    public RenderTreeFrame[] Markup() { using var builder = new RenderTreeBuilder(); base.BuildRenderTree(builder); return builder.GetFrames().Array.Take(builder.GetFrames().Count).ToArray(); }
    protected override void BuildRenderTree(RenderTreeBuilder builder) { }
    protected override Task OnAfterRenderAsync(bool firstRender) => Task.CompletedTask;
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

sealed class PresentationModule : IJSObjectReference
{
    private readonly HashSet<string> meshes = [], lines = [];
    public long UploadedBytes { get; private set; }
    public int MaximumChunk { get; private set; }
    public int Commits { get; private set; }
    public int Labels { get; private set; }
    public List<string> Calls { get; } = [];
    private static string Key(object item) => (string)item.GetType().GetProperty("Key")!.GetValue(item)!;
    public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args)
    {
        Calls.Add(identifier);
        object? result = null;
        if (identifier == "preparePresentationFrame")
        {
            result = Activator.CreateInstance(typeof(T), nonPublic: true)!;
            typeof(T).GetProperty("Meshes")!.SetValue(result, ((System.Collections.IEnumerable)args![0]!).Cast<object>().Select(Key).Where(key => !meshes.Contains(key)).ToArray());
            typeof(T).GetProperty("Lines")!.SetValue(result, ((System.Collections.IEnumerable)args[1]!).Cast<object>().Select(Key).Where(key => !lines.Contains(key)).ToArray());
        }
        if (identifier == "uploadPresentationChunk")
        {
            var bytes = (byte[])args![3]!; UploadedBytes += bytes.Length; MaximumChunk = Math.Max(MaximumChunk, bytes.Length);
            if (args[0] is { } metadata)
                foreach (var (name, cache) in new[] { ("meshes", meshes), ("lines", lines) })
                    foreach (var item in (System.Collections.IEnumerable)metadata.GetType().GetProperty(name)!.GetValue(metadata)!)
                        cache.Add((string)item.GetType().GetProperty("Id")!.GetValue(item)!);
        }
        if (identifier == "commitPresentationFrame")
        {
            Commits++;
            if (args![4] is Array labels) Labels = labels.Length;
        }
        return ValueTask.FromResult(result is null ? default! : (T)result);
    }
    public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<T>(identifier, args);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
