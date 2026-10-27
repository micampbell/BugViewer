# Scene loading performance

The production viewer retains `IJSRuntime` and its JSON scene contract. Mesh packing
uses preallocated arrays. Mesh shaders, bind-group layouts, pipeline layouts, and
pipeline promises are shared per device/configuration. Each mesh keeps its own
buffers and uniform-color bind group. Failed pipeline promises are evicted; device
loss/reinitialization/disposal clears the cache and interrupted mesh uploads free
partial buffers.

The internal binary transport is **experimental** and is used only by the sample
benchmark. Do not enable it by default without repeated browser results showing a
loading improvement without material memory or input-responsiveness regressions.
It uses small metadata objects plus one little-endian byte array, aligned typed
views, float32 attributes, and uint16/uint32 indices. Retained normal arrays are
copied independently so they do not retain an entire uploaded binary scene.

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
request to continue other work. The binary transport remains benchmark-only.
