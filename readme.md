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

### Styling and host applications

`BugViewer`, `SceneViewer`, and `PlotViewer` own their control styles. Include the host
application's generated `{AssemblyName}.styles.css` link, as in the Blazor templates;
it imports the library's scoped styles automatically. Do not copy BugViewer's control
CSS into the host application. The settings, playback, and view-cube classes use the
`bugviewer-` prefix to avoid collisions with application classes.

All three components accept `Class` and `Style` on their root element. Use a wrapper
or a custom root class to set layout and the supported theme variables:

```razor
<div class="inspection-viewer" style="height:600px">
    <SceneViewer Scene="request" Class="my-viewer"
                 Style="--bugviewer-panel-width:400px;" />
</div>
```

```css
.inspection-viewer {
    --bugviewer-font-family: "Segoe UI", sans-serif;
    --bugviewer-font-size: 14px;
    --bugviewer-panel-background: #fff;
    --bugviewer-panel-foreground: #242424;
    --bugviewer-control-gap: 6px;
}
```

| Variable | Default / purpose |
| --- | --- |
| `--bugviewer-font-family` | `"Segoe UI", sans-serif` |
| `--bugviewer-font-size` | `14px` |
| `--bugviewer-line-height` | `1.4` |
| `--bugviewer-panel-background` | Fluent neutral background, falling back to white |
| `--bugviewer-panel-foreground` | Fluent neutral foreground, falling back to `#242424` |
| `--bugviewer-panel-width` | `360px` maximum settings width; constrained to the viewer |
| `--bugviewer-control-background` | Fluent neutral background for native controls |
| `--bugviewer-control-border` | Fluent neutral stroke for borders |
| `--bugviewer-control-size` | `32px` playback button and input height |
| `--bugviewer-control-gap` | `6px` spacing between playback/file controls |
| `--bugviewer-accent` | `#1769aa` for focus, active badges, and native range controls |

Variables inherit through nested viewers. They apply to BugViewer's panels and native
controls; Fluent UI controls also retain their own design-token theme. The viewer maps
its font variables to Fluent's base typography tokens. For a full Fluent palette change,
use the host's Fluent UI theme rather than overriding Fluent's internal markup.

Keep page sizing and borders on an application-owned wrapper or custom `Class`.
Treat internal classes and generated CSS scope attributes as implementation details;
avoid targeting them or using broad `!important` rules for customization. Scoped styles
protect normal embedding; they do not create a Shadow DOM boundary. Internal classes
previously named `.scene-viewer`, `.step-controls`, and `.page-container` have been
renamed, so migrate application rules that targeted them to a wrapper or theme variables.

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
await viewer.SaveSceneAsync("part.bugscene");
await viewer.LoadSceneAsync("part.bugscene");

// Persist a request directly, without creating a viewer.
request.Save("part.bugscene");
var restored = SceneRequest.Load("part.bugscene");
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
Save As prompts for a filename and step range. In browsers supporting the File System Access
API, Save then opens the system file picker so you can choose the destination and filename.
The file must end in `.bugscene`; an existing `.json` suggestion is changed to `.bugscene`.
Canceling either dialog leaves the scene unchanged. Browsers without the picker use their
normal download behavior, which may save directly to the downloads folder.
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
The [LoadAndShowScene sample](samples/LoadAndShowScene/Pages/Home.razor) is a viewing-only
app for previously saved scenes:

```powershell
dotnet run --project samples/LoadAndShowScene
```

For VS Code debugging, select **LoadAndShowScene (Blazor WebAssembly)** in Run and Debug.
The standalone WebAssembly app runs in a browser via its development server; do not
launch its output DLL as a .NET console application. Stop any existing sample server
before debugging if it is already using port 5220.

Open the displayed localhost URL in a WebGPU-capable browser. Use the Settings gear's
**Open scene** control or Ctrl+Alt+O to browse for a `.bugscene` or existing `.json` file. In a 2D scene,
the Open control appears above the plot. Scenes with no saved camera are automatically framed.
Loading replaces the current request.

### Host the standalone viewer

The sample is a static Blazor WebAssembly site: it loads scene files selected by the
user into the browser and saves through a browser download. The sample does not need
a server API for scene processing. The site assets must still be downloaded, and
the published viewer includes a service worker for offline use after the application assets have been cached. A WebGPU-capable
browser and a secure context (HTTPS, or localhost for development) are required.
Browser memory can limit the practical file size below the configured 1 GiB limit.

In desktop Edge or Chrome, install the deployed viewer as an app and allow it to handle
`.bugscene` files. If needed, choose BugViewer as the Windows default app for that extension.
Double-clicking a `.bugscene` file then launches the viewer and loads it locally. Each file
is requested in a separate app window where the browser supports multiple-client launches.
Existing `.json` scenes can still be opened through the viewer or renamed to `.bugscene`;
the JSON contents do not change. File handling is browser- and platform-dependent, and
ordinary browser Open remains available. Offline use requires an initial online visit and
successful caching of the published app. An update prompt appears when a new version is
ready; reload when convenient to activate it.

On Windows, [Install-BugViewer.cmd](scripts/Install-BugViewer.cmd) provides guided
setup. After the updated site is deployed, download and double-click this single file.
It opens the viewer in Edge, then opens Windows Default apps; follow the browser prompts
to install the PWA and allow file handling, and select BugViewer for `.bugscene` in
Windows if needed. To use Chrome, run `Install-BugViewer.cmd --chrome` in Command Prompt.
Run `Install-BugViewer.cmd --check` to check the browser and deployed manifest without
opening windows. The script cannot complete browser permissions or select the default
app on the user's behalf.
To prepare a GitHub Pages deployment:

1. In the repository's **Settings > Pages**, choose **GitHub Actions** as the build
   and deployment source. Check the visibility and availability of Pages for your
   repository before publishing; do not put private scene files in the website.
2. Push the workflow and app changes to the default branch. In **Actions**, select
   **Deploy scene viewer to GitHub Pages** and run it manually. The workflow does not
   deploy automatically on push.
3. Open the URL reported by the deployment. The application uses a relative base path,
   so both a repository subpath and a root/custom-domain site are supported.

The deployed page provides **Open scene** in Settings and the Ctrl+Alt+O shortcut.
Treat the deployed site and
browser extensions as trusted code when viewing sensitive files; the local-file
design is not a guarantee against a compromised host or browser.

Run the persistence and viewer-integration checks with:

```powershell
dotnet run --project tests/BugViewer.SceneTests
```

## Performance comparison

See [the reproducible scene-loading benchmark and validation guide](docs/performance.md).
