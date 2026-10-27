using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Numerics;
using Microsoft.JSInterop;

namespace BugViewer;

public partial class BugViewer
{
    private readonly ConditionalWeakTable<AbstractObject3D, PresentationResource> presentationResources = new();
    private readonly ConditionalWeakTable<MeshData, DisplayLineCache> presentationEdges = new();
    private readonly ScenePreparationCache presentationBounds = new(1024 * 1024);
    private const int UploadChunkBytes = 1024 * 1024;
    private const long PresentationGpuCacheBytes = 128L * 1024 * 1024;

    private sealed class PresentationResource
    {
        internal string Key = Guid.NewGuid().ToString("N");
        internal readonly string BoundsKey = Guid.NewGuid().ToString("N");
        internal float Thickness = float.NaN;
        internal BinarySceneUpload? Upload;
        internal Sphere? Bounds;
    }
    private sealed class DisplayLineCache
    {
        internal bool Initialized;
        internal bool Borders;
        internal LineData? Lines;
    }
    private sealed record ResourceReference(string Key, string Id);
    private sealed class MissingPresentationResources
    {
        public string[] Meshes { get; set; } = [];
        public string[] Lines { get; set; } = [];
    }
    private sealed class SubmittedPresentation
    {
        public double UploadMs { get; set; }
        public double SubmittedAtMs { get; set; }
        public double SubmissionMs { get; set; }
        public bool Visible { get; set; }
    }

    internal async Task CancelPendingPresentationAsync()
    {
        // This is deliberately outside the scene gate: a hidden browser may be
        // waiting for a frame while holding that gate. The JS operation only
        // releases the submission waiter, allowing normal disposal to continue.
        if (_module is null) return;
        try { await _module.InvokeVoidAsync("cancelPresentationWait"); }
        catch (JSDisconnectedException) { }
        catch (ObjectDisposedException) { }
    }

    // Unlike remove/add pairs, this updates CPU state once and commits a complete GPU
    // frame. Previous GPU resources remain drawable while missing resources are uploaded.
    internal async Task<ScenePlaybackMetrics> ApplyPresentationFrameAsync(
        List<MeshData> nextMeshes, List<LineData> nextLines, List<TextBillboard> nextLabels, bool preserveView = false)
    {
        var timing = new ScenePlaybackMetrics();
        await ExecuteSceneOperationAsync("apply presentation frame", async module =>
        {
            var timer = Stopwatch.StartNew();
            var geometryChanged = !meshes.SequenceEqual(nextMeshes)
                || !lines.Where(line => !meshDisplayLines.ContainsKey(line.Id)).SequenceEqual(nextLines);
            var meshesChanged = !meshes.SequenceEqual(nextMeshes);
            // Restoring saved view state updates CPU lists before their first GPU commit.
            var labelsChanged = preserveView || !billBoards.SequenceEqual(nextLabels);
            meshes = nextMeshes;
            lines = new List<LineData>(nextLines);
            billBoards = nextLabels;
            ReindexMeshIndices();
            ReindexBillboardIndices();
            if (meshesChanged) RebuildMeshLookups();
            var sphereChanged = false;
            objectSpheres.Clear();
            foreach (var item in meshes.Cast<AbstractObject3D>().Concat(lines))
            {
                var resource = presentationResources.GetOrCreateValue(item);
                resource.Bounds ??= FastPresentationBounds(item.Vertices);
                objectSpheres[item] = resource.Bounds.Value;
            }
            if (geometryChanged)
            {
                var boundsKey = string.Join('|', meshes.Cast<AbstractObject3D>().Concat(lines)
                    .Select(item => presentationResources.GetOrCreateValue(item).BoundsKey));
                var sphere = presentationBounds.Get(boundsKey,
                    () => MergePresentationBounds(objectSpheres.Values),
                    _ => 64L + boundsKey.Length * 2L);
                sphereChanged = !Sphere.IsPracticallySame(sphere, BoundingSphere);
                BoundingSphere = sphere;
                _sphereRadius = _sphereCenterLength = float.NaN;
            }
            meshDisplayLines.Clear();
            if (Options.ShowMeshEdges || Options.ShowMeshBorders && CanShowMeshBorders)
            {
                foreach (var mesh in meshes)
                {
                    var cached = presentationEdges.GetValue(mesh, _ => new DisplayLineCache());
                    if (!cached.Initialized || cached.Borders != Options.ShowMeshBorders)
                    {
                        cached.Initialized = true; cached.Borders = Options.ShowMeshBorders;
                        cached.Lines = CreateMeshDisplayLines(mesh, cached.Borders, automaticThickness: true);
                    }
                    if (cached.Lines is { } line)
                    {
                        meshDisplayLines.Add(line.Id, line); lines.Add(line);
                        objectSpheres[line] = presentationResources.GetOrCreateValue(line).Bounds
                            ??= FastPresentationBounds(line.Vertices);
                    }
                }
            }
            ReindexLineIndices();
            if (Options.ShowSurfacesAs == MeshFaceDisplay.Surfaces && !CanShowPrimitiveSurfaces)
            {
                _suppressOptionsChanged = true;
                try { Options.ShowSurfacesAs = MeshFaceDisplay.Triangles; }
                finally { _suppressOptionsChanged = false; }
            }
            var cpuMs = timer.Elapsed.TotalMilliseconds;
            if (module is null) { timing = timing with { SceneUpdateMs = cpuMs }; return; }

            var meshRefs = meshes.Select(mesh => new ResourceReference(presentationResources.GetOrCreateValue(mesh).Key, mesh.Id)).ToArray();
            var lineRefs = lines.Select(line =>
            {
                var resource = presentationResources.GetOrCreateValue(line);
                var thickness = line.Thicknesses.Any(value => value < 0) ? PathThickness : 0;
                if (resource.Thickness != thickness)
                {
                    resource.Thickness = thickness; resource.Key = Guid.NewGuid().ToString("N"); resource.Upload = null;
                }
                return new ResourceReference(resource.Key, line.Id);
            }).ToArray();
            timer.Restart();
            var missing = await module.InvokeAsync<MissingPresentationResources>("preparePresentationFrame", meshRefs, lineRefs)
                ?? new MissingPresentationResources { Meshes = meshRefs.Select(item => item.Key).ToArray(), Lines = lineRefs.Select(item => item.Key).ToArray() };
            var missingMeshes = missing.Meshes.ToHashSet();
            var missingLines = missing.Lines.ToHashSet();
            double packingMs = 0;
            long bytes = 0;
            try
            {
                for (var index = 0; index < meshes.Count; index++)
                {
                    var resource = presentationResources.GetOrCreateValue(meshes[index]);
                    if (!missingMeshes.Contains(resource.Key)) continue;
                    var packing = Stopwatch.StartNew();
                    resource.Upload ??= BinarySceneUpload.Create([meshes[index].Pack() with { Id = resource.Key }], []);
                    packingMs += packing.Elapsed.TotalMilliseconds;
                    bytes += resource.Upload.Bytes.Length;
                    await UploadPresentationResourceAsync(module, resource.Upload);
                }
                for (var index = 0; index < lines.Count; index++)
                {
                    var resource = presentationResources.GetOrCreateValue(lines[index]);
                    if (!missingLines.Contains(resource.Key)) continue;
                    var packing = Stopwatch.StartNew();
                    resource.Upload ??= BinarySceneUpload.Create([], [(LineUpload)lines[index].CreateJavascriptData(PathThickness) with { Id = resource.Key }]);
                    packingMs += packing.Elapsed.TotalMilliseconds;
                    bytes += resource.Upload.Bytes.Length;
                    await UploadPresentationResourceAsync(module, resource.Upload);
                }
                // Camera/grid policy runs once, just before the atomic geometry switch.
                if (preserveView)
                {
                    await module.InvokeVoidAsync("updateDisplayOptions", Options.ToJavascriptOptions(Camera!.PolarAngle));
                    await WriteViewMatrixCoreAsync(module);
                    await WriteProjectionMatrixCoreAsync(module);
                }
                else await UpdateViewerCoreAsync(module, sphereChanged);
                var result = await module.InvokeAsync<SubmittedPresentation>("commitPresentationFrame", meshRefs, lineRefs,
                    Options.ShowSurfacesAs != MeshFaceDisplay.None, Options.ShowSurfacesAs == MeshFaceDisplay.Surfaces,
                    labelsChanged ? nextLabels.Select(label => label.CreateJavascriptData()).ToArray() : null,
                    PresentationGpuCacheBytes, renderPauseDepth == 0);
                timing = timing with
                {
                    SceneUpdateMs = cpuMs, PackingMs = packingMs, InteropMs = timer.Elapsed.TotalMilliseconds - packingMs,
                    UploadedBytes = bytes, CacheHits = meshRefs.Length + lineRefs.Length - missingMeshes.Count - missingLines.Count,
                    BrowserUploadMs = result?.UploadMs ?? 0, SubmittedAtMs = result?.SubmittedAtMs ?? 0,
                    SubmissionMs = result?.SubmissionMs ?? 0, Visible = result?.Visible ?? false
                };
            }
            catch
            {
                // The GPU still owns the previous complete frame on upload failure.
                await module.InvokeVoidAsync("abortPresentationFrame");
                throw;
            }
        });
        return timing;
    }

    internal Task PrefetchPresentationAsync(List<MeshData> futureMeshes, List<LineData> futureLines, CancellationToken cancellationToken) =>
        ExecuteSceneOperationAsync("prefetch presentation geometry", async module =>
        {
            if (module is null) return;
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var mesh in futureMeshes)
            {
                var resource = presentationResources.GetOrCreateValue(mesh);
                resource.Bounds ??= FastPresentationBounds(mesh.Vertices);
                cancellationToken.ThrowIfCancellationRequested();
            }
            var meshRefs = futureMeshes.Select(mesh => new ResourceReference(presentationResources.GetOrCreateValue(mesh).Key, mesh.Id)).ToArray();
            var lineRefs = futureLines.Select(line =>
            {
                var resource = presentationResources.GetOrCreateValue(line);
                var thickness = line.Thicknesses.Any(value => value < 0) ? PathThickness : 0;
                if (resource.Thickness != thickness)
                {
                    resource.Thickness = thickness; resource.Key = Guid.NewGuid().ToString("N"); resource.Upload = null;
                }
                return new ResourceReference(resource.Key, line.Id);
            }).ToArray();
            var missing = await module.InvokeAsync<MissingPresentationResources>("preparePresentationFrame", meshRefs, lineRefs);
            if (missing is null) return;
            if (missing.Meshes.Length + missing.Lines.Length == 0) return;
            try
            {
                var meshKeys = missing.Meshes.ToHashSet();
                foreach (var mesh in futureMeshes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var resource = presentationResources.GetOrCreateValue(mesh);
                    if (!meshKeys.Contains(resource.Key)) continue;
                    resource.Upload ??= BinarySceneUpload.Create([mesh.Pack() with { Id = resource.Key }], []);
                    await UploadPresentationResourceAsync(module, resource.Upload, cancellationToken);
                }
                var lineKeys = missing.Lines.ToHashSet();
                foreach (var line in futureLines)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var resource = presentationResources.GetOrCreateValue(line);
                    if (!lineKeys.Contains(resource.Key)) continue;
                    resource.Upload ??= BinarySceneUpload.Create([], [((LineUpload)line.CreateJavascriptData(PathThickness)) with { Id = resource.Key }]);
                    await UploadPresentationResourceAsync(module, resource.Upload, cancellationToken);
                }
                await module.InvokeVoidAsync("finishPresentationPrefetch", PresentationGpuCacheBytes);
            }
            catch
            {
                await module.InvokeVoidAsync("abortPresentationFrame");
                throw;
            }
        });

    private static async Task UploadPresentationResourceAsync(IJSObjectReference module, BinarySceneUpload upload, CancellationToken cancellationToken = default)
    {
        if (upload.Bytes.Length == 0)
        {
            await module.InvokeVoidAsync("uploadPresentationChunk", new { meshes = upload.Meshes, lines = upload.Lines }, 0, 0, upload.Bytes);
            return;
        }
        for (var offset = 0; offset < upload.Bytes.Length; offset += UploadChunkBytes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var length = Math.Min(UploadChunkBytes, upload.Bytes.Length - offset);
            var chunk = offset == 0 && length == upload.Bytes.Length ? upload.Bytes : upload.Bytes.AsSpan(offset, length).ToArray();
            await module.InvokeVoidAsync("uploadPresentationChunk",
                offset == 0 ? new { meshes = upload.Meshes, lines = upload.Lines } : null,
                upload.Bytes.Length, offset, chunk);
        }
    }

    // Conservative bounds are sufficient for fitting/culling. The exact enclosing
    // sphere solver can repeatedly scan/reorder every point and is too expensive
    // on a playback path. Scan each source once, then combine cached spheres.
    private static Sphere FastPresentationBounds(IList<Vector3> vertices)
    {
        if (vertices.Count == 0) return new(Vector3.Zero, 5);
        if (vertices.Count == 1) return new(vertices[0], 5);
        var min = vertices[0]; var max = min;
        for (var index = 1; index < vertices.Count; index++)
        {
            min = Vector3.Min(min, vertices[index]); max = Vector3.Max(max, vertices[index]);
        }
        return new((min + max) / 2, (max - min).LengthSquared() / 4);
    }

    private static Sphere MergePresentationBounds(IEnumerable<Sphere> spheres)
    {
        Sphere? result = null;
        foreach (var sphere in spheres)
        {
            if (result is not { } current) { result = sphere; continue; }
            var delta = sphere.Center - current.Center;
            var distance = delta.Length();
            var r1 = current.GetRadius(); var r2 = sphere.GetRadius();
            if (r1 >= distance + r2) continue;
            if (r2 >= distance + r1) { result = sphere; continue; }
            var radius = (distance + r1 + r2) / 2;
            result = new(current.Center + delta * ((radius - r1) / distance), radius * radius);
        }
        return result ?? new Sphere(Vector3.Zero, 5);
    }
}
