# Scene loading performance

Scene persistence uses the version-1 JSON contract. Stepped `SceneViewer` rendering
uses `IJSRuntime` with binary geometry chunks, while the low-level JSON renderer
entry points remain available. This retains WebAssembly, Server, and Hybrid hosting.
Mesh packing uses preallocated arrays, and compatible shaders/pipelines are shared.

Presentation frames retain prepared geometry in a cache with a 128 MiB estimated
cost budget and GPU resources in a 128 MiB cache (including retained normal data).
Active resources are pinned even when one frame exceeds the budget. Source scene
memory is separate. Missing resources upload in at most 1 MiB interop chunks; the
previous frame remains visible until the new set is ready. Device buffer limits
are checked before allocation. Device loss and disposal release cached resources.

Unchanged meshes/paths are resolved from source identity before conversion. Picking
uses lazily cached data per mesh, and borders are cached independently of line width.
Playback fits conservative cached bounds, combining object spheres without solving
an exact minimum sphere for every frame. Saved camera/grid state is preserved.
Two upcoming stride positions are prefetched with cancellation when input changes.

Autoplay schedules elapsed-time deadlines and preserves every step. A slow update
is counted as a missed deadline instead of being hidden by skipping steps. Slider
requests coalesce, and obsolete preparation yields to the latest requested value.

### Measure a saved presentation

Run `samples/LoadAndShowScene` in Release, open the JSON file, expand **Playback
measurements**, and run the check. It traverses up to 600 steps at 10 fps and twice
at 30 fps, then performs deterministic random seeks and visits the first/last steps.
Download the report. Compare distinct `Step` values and `SubmittedAtMs` timestamps,
not the canvas animation-loop count. `TotalMs` measures active update work; interop
includes waiting for the first complete-frame submission. Prefetch runs between
updates, so also examine the actual submission intervals. These are CPU submission
measurements, not GPU execution or proof of physical screen presentation.

The public `OnStepRendered` callback provides the same metrics. Browser visibility,
GPU/driver, build mode, and simultaneous workloads must be recorded separately.
Use a visible, unobscured browser for interactive desktop acceptance; isolated
headless browser runs are useful runtime checks but are reported as such.

Weighted blended transparency adds an `rgba16float` accumulation target and an
`r16float` revealage target, together approximately 10 bytes per pixel per MSAA
sample before alignment and other GPU allocations. At 4x MSAA this is about
18.3 MiB at 800 x 600 or 79 MiB at 1920 x 1080. The renderer also performs
transparent accumulation and compositing passes. It does not currently have a
sorted-transparency fallback if these targets cannot be created.

An exploratory headless Chrome comparison on an Intel adapter used 50,000
translucent line segments and 20,000 translucent triangles at 800 x 600 and 4x
MSAA. Ten GPU-completion measurements gave approximately 18.5 ms median for
the committed pre-change renderer and 16.45 ms for weighted transparency.
These measurements include frame scheduling, use a small sample, and do not
isolate GPU execution; they are not evidence of a performance improvement.
Repeat with representative scenes and visible interactive workloads before
drawing performance conclusions.

The default input allowance is 1 GiB and can be changed with `MaxSceneFileSize`.
Loading streams the JSON with progress and cancellation, then validates once before
replacing the current presentation. Specialized readers for coordinate/index triples,
colors, and double-precision source points preserve the existing JSON format while
avoiding per-value general-purpose deserialization state. Browser memory remains a
practical limit independently of the configurable file allowance.

For large CPU-heavy JSON files, the standalone sample also has a **Performance**
publish profile. It compiles .NET to native WebAssembly (AOT) during publish and
requires the `wasm-tools` workload. Ordinary `dotnet run`, including Release, uses
the interpreted runtime and is not equivalent to this published artifact.

```powershell
dotnet publish samples/LoadAndShowScene/LoadAndShowScene.csproj -c Release -p:PublishProfile=Performance
python -m http.server 5199 --bind 127.0.0.1 --directory samples/LoadAndShowScene/bin/Release/net10.0/publish/performance/wwwroot
```

Open `http://localhost:5199`. AOT trades longer publishing and a larger initial app
download for faster CPU processing. See [Microsoft's AOT build documentation](https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly-build-tools-and-aot?view=aspnetcore-10.0).

TVGL's `WebGPUPresenter` uses the sibling BugViewer project when it exists, so local
presenter runs pick up these changes. Set `-p:UseLocalBugViewer=false` to use its
NuGet dependency instead; the package is also the fallback without a sibling checkout.

## Reproduce the browser comparison

```powershell
dotnet run --project samples/SimpleStandalone/SimpleStandalone.csproj -c Release --no-launch-profile --urls http://localhost:5195
```

Open `http://localhost:5195/benchmarks` in Edge or another WebGPU browser and click
**Run comparison**. Keep the tab visible and the window unobscured throughout.
Use **Download JSON report** to save individual measurements and environment data.
Repeat the complete comparison on the same machine/browser/canvas, without a
competing build or graphics workload. A visibility change invalidates a measurement;
Windows occlusion may also throttle frame scheduling without a visibility event.
Discard timeout/interrupted runs rather than interpreting them as performance data.

The deterministic scenes are a 131,072-triangle mesh with wide indices, 512 small
meshes, a 131,072-triangle mesh expanded for per-triangle RGBA, and 64 toolpaths with
2,048 segments each. Three repetitions rotate the order of legacy JSON (original
LINQ packing/uncached mesh pipelines), cached JSON, and cached binary. Each device
initialization is followed by cold, warm, and incremental loads. Warm loads clear
and reload the scene while keeping device caches. Incremental loads replace the
last 10% of objects (at least one) and retain the others. Single-object scenes
therefore replace their entire mesh. Cold means a fresh GPU device, not a fresh
browser process/network/driver shader cache.

Measurements:

- `initMs`: browser device/pipeline initialization plus projection/configuration interop; module import is excluded.
- `generationMs`: deterministic model creation, outside the upload measurement.
- `preparationMs` / `preparationAllocatedBytes`: synchronous C# payload packing, including binary encoding when selected.
- `interopUploadMs`: caller-observed upload/removal round trips, including serialization, scheduling, and GPU resource creation.
- `frame.jsUploadMs`: browser mesh/line resource creation time, including awaited mesh pipeline creation; excludes caller serialization and binary decoding.
- `frame.cpuSubmissionMs`: time since the measurement barrier (before preparation) through the first complete-scene `queue.submit`, including frame scheduling and scene clearing.
- `frame.gpuCompletionMs`: elapsed time to queue completion for that submission. This includes CPU preparation/waiting and is **not** GPU execution time or proof of screen presentation.
- `frame.pipelineCreations`: mesh pipeline creation attempts; shared initialization pipelines are excluded. Cached cold loads should create one mesh pipeline per fixture, warm/incremental loads zero; toolpath-only loads create zero mesh pipelines.
- `frame.retainedResources`: object identities retained across the measured update. Many-mesh incremental loads retain 461 meshes; toolpaths retain 58 lines.
- `frame.jsHeapBytes`: optional Chromium JS heap snapshot, not total browser/GPU/WASM memory or peak memory. `longTaskCount`/`longestTaskMs` are optional Long Tasks API observations, not direct input-latency measurements.

The comparison exercises the renderer upload boundary directly so it can control
legacy/cached/binary behavior independently. It excludes the public component's
picking-cache, bounding-sphere, and generated-edge preparation. Verify the public
component separately on representative application scenes before claiming an
end-to-end application speedup. No per-command browser interop is introduced.

## Automated checks

```powershell
dotnet run --project tests/BugViewer.PerformanceTests/BugViewer.PerformanceTests.csproj -c Release
node --experimental-vm-modules tests/webgpu-renderer.test.mjs
dotnet build BugViewer.slnx -c Release
```

The C# executable checks randomized equivalence against pre-optimization packing,
normal/color contracts, binary values/alignment/index boundaries, and line data.
It also reports warmed native-.NET packing medians (5 warmups, 9 measured samples).
These are CPU packing measurements, not browser results.

The Node test executes the actual renderer with mocked browser/GPU endpoints and
checks pipeline reuse across 512 meshes and four color/transparency variants,
in-flight reuse, failure retry, partial-buffer cleanup, disposal during uploads,
initialization failure/device loss, binary views/bounds, incremental identities,
lines/billboards, and CPU-submission versus queue-completion timing. It does not
validate real shaders, visual output, GPU drivers, or browser performance.

## Hosting and visual acceptance

```powershell
dotnet run --project tests/ServerSmoke/ServerSmoke.csproj -c Release --no-launch-profile --urls http://localhost:5193
```

Open `http://localhost:5193/` and confirm the server-circuit upload passes, two colored
triangles, a line, and a billboard appear, and orbit/pan/zoom/ViewCube work. Use the
standalone sample for the same WebAssembly interaction checks. Restart hosts and
refresh assets after changing the renderer. Hybrid needs a separate WebView test
host; it is not covered by the server smoke fixture. Keep build, mock-test, browser
rendering, and Hybrid acceptance evidence separate.

The `/presentation` route tests the production stepped path: its first frame uploads
1,260,000 geometry bytes, crossing the 1 MiB chunk boundary. **Switch frame** changes
the active mesh; a resident revisit reports zero geometry bytes uploaded.

## Recorded validation (2026-10-27)

`printScene.json`: 338,114,058 bytes (322.5 MiB), 254 steps, seven groups.
The old 256 MiB allowance rejected it; the default 1 GiB allowance accepts it.
The **Performance** AOT publish was served locally and tested in isolated headless
Edge 156 on Windows, .NET 10.0.14, Intel `xe-2lpg`, non-fallback WebGPU adapter,
document visibility `visible`. No competing build or browser benchmark ran during
the measured passes. Two full-file browser runs opened the scene in 11.8–12.2 s
(file selection to opened status/step controls). The final run measured:

| Traversal | Submitted steps | Measured rate | Active update median | Active update p95 |
| --- | ---: | ---: | ---: | ---: |
| 10 fps | All 254 | 10.01 fps | 6.3 ms | 8.3 ms |
| 30 fps | All 254 | 30.00 fps | 15.9 ms | 16.7 ms |
| 30 fps repeat | All 254 | 30.00 fps | 14.9 ms | 16.3 ms |
| Random seeks plus last/first | 32 (29 distinct) | 58.16 updates/s | 14.1 ms | 15.2 ms |

Every recorded frame had a nonzero submission timestamp and a visible document.
These numbers measure submitted geometry updates, not the canvas's background
animation loop. Prefetch costs are included in the achieved traversal rate but
excluded from active update timings and foreground upload-byte counts.

Build mode matters: an earlier interpreted Release diagnostic on this same file
took 334 s to open and achieved 10 fps but only 15.6 fps on its 30 fps pass.
That run preceded the final span-based packing change and is not a measurement of
the final interpreted build. Do not expect ordinary Visual Studio development
launches to match the published AOT measurements above.

The live Interactive Server checks passed both the legacy mesh/line/billboard path
and chunked presentation uploads, frame switching, prefetch, and zero-byte cached
revisits, with no browser errors. Native scene/persistence tests, 90 randomized
packing-equivalence cases, renderer/file-command/plot mocks, the solution build,
and TVGL WebGPUPresenter with its local BugViewer reference passed. GPU cache
eviction/pinning, canceled staging, device loss and disposal are covered by mocks.
Browser UI checks passed in both development and AOT builds: canceling a large
open retains the prior scene, stride and editable-step controls work, rapid slider
requests settle on the final value, playback stops, Save As restores the selected
step, and single/stepped/2D presentations replace one another without errors.
The user file and temporary browser reports are not included in the repository.
Foreground desktop interaction and Hybrid runtime acceptance remain separate.

## Recorded validation (2026-10-24)

Release, Windows x64, .NET 10.0.13, native console packing benchmark, median of
9 measured samples following 5 warmups:

| Scene | Legacy packing | Preallocated packing | Legacy allocation | Preallocated allocation |
| --- | ---: | ---: | ---: | ---: |
| Large mesh | 6.29 ms | 0.94 ms | 18,944,920 B | 3,160,089 B |
| Many meshes | 4.89 ms | 0.45 ms | 10,948,835 B | 1,880,208 B |
| Triangle colors | 37.31 ms | 4.19 ms | 109,577,137 B | 17,301,816 B |

The randomized C# checks and renderer mock tests pass. The cache test creates one
mesh pipeline for 512 compatible meshes and four for the four tested color/alpha
configurations; replacement retains unchanged resources and reuses the pipeline.
The Interactive Server fixture starts and serves the page and renderer asset over
HTTP. This does not establish a successful interactive server circuit upload.

Edge loaded the benchmark after correcting the standalone sample's missing .NET
10 import map. The attempted comparison timed out while waiting for an animation
frame with the browser behind other desktop work; that incomplete run is discarded.
No valid repeated browser timing, visual acceptance, server-circuit interaction,
or Hybrid runtime result is claimed. Desktop automation was stopped at the user's
request to continue other work. At the time of this 2026-10-24 run, the binary transport remained benchmark-only;
the presentation path described above was added subsequently.
