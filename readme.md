# BugViewer
[![Nuget](https://img.shields.io/nuget/v/BugViewer?style=plastic)](https://www.nuget.org/packages/BugViewer/)
[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/micampbell/BugViewer)

![BugViewer](https://raw.githubusercontent.com/micampbell/BugViewer/refs/heads/master/docs/logo.png)
## Why Bug?
The most appropriate name would be Blazor WebGPU Viewer. This could be abbreviated as BWG Viewer, which is fine - but awkward to say and type. So, we simply change the double-U to a single-U -> thus BugViewer.
## Description
BugViewer is a Blazor component that renders a 3D file with the new WebGPU. It includes controls to view a single part (as opposed to being a game engine). The goal is to provide a clear method to view 3D parts with minimal Javascript.

The main things that are visualized are:
- triangles/meshes
- polylines
- text billboards

## Installation
BugViewer can be installed as Nuget package: https://www.nuget.org/packages/BugViewer/

## Usage

### Simple scenario

Just add the `BugViewer` Component to your Razor page.:

```csharp
        <BugViewer @ref="viewer" Height="80%" Width="80%" />

```

The [./samples](./samples) folder contains some examples of how to setup the canvas.

### Saving and loading scenes

`BugViewer.SceneRequest` is the portable presentation contract. It stores 3D geometry,
colors, labels, complete steps or lazy step groups, transforms, and 2D plots in versioned
UTF-8 JSON. Optional viewer settings, camera, and `InitialStep` restore the saved view.
Saving a stepped request retains the whole timeline, including history limits and null
transform barriers, rather than just the displayed frame.

Use `SceneViewer` to display either kind of request:

```razor
@using BugViewer
<SceneViewer @ref="viewer" Scene="request" />

@code {
    private SceneViewer? viewer;
    private SceneRequest request = new();
}
```

```csharp
// Save or replace the contents of a SceneViewer.
await viewer.SaveSceneAsync("part.bugscene.json");
await viewer.LoadSceneAsync("part.bugscene.json");

// Persist a request directly, without creating a viewer.
request.Save("part.bugscene.json");
var restored = SceneRequest.Load("part.bugscene.json");
await viewer.LoadSceneAsync(restored);

// Keep the source request with the current step, camera, and display settings.
var current = await viewer.GetSceneAsync();
```

`SceneRequest.Load`, `Save`, `LoadAsync`, and `SaveAsync` accept either a file path
or a `Stream`. Component `LoadSceneAsync` and `SaveSceneAsync` also accept streams. Stream
overloads leave the caller's stream open and operate at its current position; asynchronous
I/O overloads accept a cancellation token. File saves replace the destination only after
serialization succeeds. Invalid scene files are rejected before changing the viewer. The
serializer uses generated metadata and works in trimmed WebAssembly builds.

`SceneMesh`, `ScenePath`, `ScenePointSet`, `PlotRequest`, and `PlotTrace` hold the portable
data. `SceneStepGroup` stores source meshes and/or a `SceneStepPathSource` plus an optional
indexed transform list. `ScenePoint` and `SceneTransform` retain double precision until
the visible frame is converted for rendering. Use `Steps` for complete frames and
`StepGroups` for shared source data; a request cannot use both. Treat published source
collections as immutable so cached step resolution stays valid.

The low-level `BugViewer` component still accepts renderer-native meshes and lines.
`SceneViewer` displays 3D playback or 2D plots and provides the component persistence API, Open/Save As buttons, and
**Ctrl+Alt+O / Ctrl+Alt+S** shortcuts. Plain Ctrl+O / Ctrl+S remain available to the browser;
AltGr input is ignored. The 3D viewer also has Open and Save As icons in **Settings**, below
the Camera accordion, visible even when the accordion is collapsed. In 2D, the icons appear above the plot.
The viewer header displays `ViewerId`, an active indicator when `IsActive` is true, and the
scene title and heading (also the subtitle for 3D). Supplying `OnClose` shows a close button
that passes `ViewerId` to the host; `OnReady` is forwarded for both 3D and 2D rendering.
Save As prompts for a filename before downloading; Cancel or Escape leaves the scene unchanged.
Names without a `.json` or `.bugscene` extension receive `.bugscene.json` automatically.
For a stepped scene, Save As also accepts a 1-based inclusive start and end and a positive stride.
The default selects all steps with stride 1. Each saved 3D step combines the elements visible or
introduced in its consecutive stride-sized group, retaining the latest version of an object; visible
history at the start of the selected range is included. The final group may contain fewer steps.
For 2D, each saved step uses the last plot in its group. The saved presentation opens at its first step.
Full-range stride-1 saves preserve source steps and transforms. Other 3D selections materialize grouped
frames using renderer-precision coordinates, so files and memory use may increase and original source
transforms and double-precision coordinates may not be retained. Programmatic `SaveSceneAsync` still
saves the complete request.
Opening a request with steps
automatically shows playback controls; opening a single scene removes them. Saving keeps
the selected timeline and current view. The keyboard commands target the focused or last
clicked request viewer (or the only one on the page), and leave editors outside it alone.
Set `EnableFileShortcuts="false"` to let the host own those keys. Set `EnableOpenScene="false"`
for a save-only viewer, or call `ShowSaveAsAsync()` from a host control. Use `MaxSceneFileSize` to
adjust the 1 GiB input allowance and `FileName` to suggest names for programmatically supplied
requests. These file controls belong to `SceneViewer`; applications using only the
low-level `BugViewer` renderer should use this presentation component for scene files.
Opening and saving display progress in the same nonmodal viewer overlay. Opening reports bytes read
and can be canceled without replacing the current scene; saving shows its current stage and can be
canceled during preparation or serialization. Once the download is handed to the browser it cannot be
canceled from the viewer. Some synchronous work may delay cancellation. Saving buffers the exported
scene and JSON in memory, and the browser may make additional copies; the input allowance is configurable,
but available browser memory still determines the practical scene size.

Stepped playback reuses prepared geometry and a bounded GPU resource cache, prefetches the
next two stride positions, and transfers missing geometry in binary chunks. A frame becomes
visible only after its resources are ready. Playback uses elapsed-time deadlines and does
not skip steps to conceal missed deadlines. Source collections must remain immutable after
publication; publish a new request when changing source geometry. Existing JSON files and
the public Load/Save methods retain the version-1 format.

`SceneViewer` exposes `OnStepRendered` with timings, transferred bytes, cache hits, and the browser submission timestamp.
For large saved scenes, use its **Performance** publish profile; setup instructions
and measured results are in [docs/performance.md](docs/performance.md).
Plotly ships with the
library and loads only when displaying a 2D request.

Opaque fragments write depth before translucent objects are drawn. Translucent
meshes, paths (including faded edges), labels, grid, and axes use weighted blended
order-independent transparency, so crossing geometry no longer depends on
object-center draw order. This method approximates rather than exactly reproduces
depth-sorted blending; overlapping colors can differ from exact results, and
very deep stacks can exceed the precision or range of the accumulation buffers.
It requires the WebGPU accumulation and revealage render targets; there is
currently no sorted-rendering fallback if those resources cannot be created.
The viewer reports WebGPU failures rather than silently switching modes.

TVGL's `WebGPUPresenter` now produces this shared contract. Its derived `PresenterRequest`
keeps blocking/hold settings and callbacks in the presenter, and source-face references
stay in a presenter-side table. Those runtime details are excluded from the file. Reloaded
requests retain geometry and playback, but do not recreate live TVGL objects or callbacks.

Browser applications use streams from `InputFile` and browser downloads instead of local file paths.
The [LoadAndShowScene sample](samples/LoadAndShowScene/Pages/Home.razor) demonstrates both,
including downloadable single 3D, stepped 3D, and 2D examples:

```powershell
dotnet run --project samples/LoadAndShowScene
```

For VS Code debugging, select **LoadAndShowScene (Blazor WebAssembly)** in Run and Debug.
The standalone WebAssembly app runs in a browser via its development server; do not
launch its output DLL as a .NET console application. Stop any existing sample server
before debugging if it is already using port 5220.

Open the displayed localhost URL in a WebGPU-capable browser. Choose **Open Scene** to browse
for a `.bugscene.json` file, then **Save As** to name and download it with the current settings and view.
Scenes with no saved camera are automatically framed. Loading replaces the current request.

Run the persistence and viewer-integration checks with:

```powershell
dotnet run --project tests/BugViewer.SceneTests
```

## Performance comparison

See [the reproducible scene-loading benchmark and validation guide](docs/performance.md).
