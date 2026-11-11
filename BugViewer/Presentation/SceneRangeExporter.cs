using System.Diagnostics;

namespace BugViewer;

internal static class SceneRangeExporter
{
    // Preserve indexed source geometry for ordinary ranged playback, instead of duplicating
    // every visible historical object in every exported frame.
    internal static async Task<SceneRequest> ExportBoundedAsync(SceneRequest source, int start, int end, int stride,
        int maximumEarlierObjects, CancellationToken cancellationToken = default)
    {
        if (maximumEarlierObjects < 0) throw new ArgumentOutOfRangeException(nameof(maximumEarlierObjects));
        var count = source.StepGroups.Count > 0 ? source.StepGroups.Max(group => group.Count) : source.Steps.Count;
        if (start < 0 || end < start || end >= count || stride < 1)
            throw new ArgumentOutOfRangeException(nameof(start));
        cancellationToken.ThrowIfCancellationRequested();
        if (start == 0 && end == count - 1 && stride == 1)
            return source with { InitialStep = 0 };
        if (start > 0 && source.Steps.Count > 0 && source.Steps.Skip(start).Take(end - start + 1).Any(frame => frame.StepGroups.Count > 0))
            throw new NotSupportedException("Limiting earlier objects in complete frames that contain step groups is not yet supported.");
        if (source.StepGroups.Count == 0 || source.Kind == PresentationKind.TwoDimensional)
            return await ExportAsync(source, start, end, stride, cancellationToken);

        var candidates = new List<(int Step, int Group, bool Path)>();
        var clock = Stopwatch.StartNew();
        for (var g = 0; g < source.StepGroups.Count; g++)
        {
            var group = source.StepGroups[g];
            var boundary = start + group.StepOffset;
            var visibleMeshes = SceneGeometry.ResolveStepElements(group, group.Meshes, start)
                .Select(item => item.Element.Id).ToHashSet(StringComparer.Ordinal);
            for (var i = 0; i < Math.Min(boundary, group.Meshes.Count); i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (group.Meshes[i] is { } mesh && visibleMeshes.Contains(mesh.Id)) candidates.Add((i - group.StepOffset, g, false));
                if (clock.ElapsedMilliseconds >= 6) { await Task.Delay(1, cancellationToken); clock.Restart(); }
            }
            if (group.PathSource is { } pathSource)
                foreach (var i in group.ResolvePathIndices(start))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (i < boundary && pathSource.Paths[i].Take(2).Count() == 2) candidates.Add((i - group.StepOffset, g, true));
                    if (clock.ElapsedMilliseconds >= 6) { await Task.Delay(1, cancellationToken); clock.Restart(); }
                }
        }
        var kept = candidates.OrderByDescending(item => item.Step).ThenBy(item => item.Group)
            .ThenBy(item => item.Path).Take(maximumEarlierObjects).ToHashSet();
        var groups = new List<SceneStepGroup>();
        for (var g = 0; g < source.StepGroups.Count; g++)
        {
            var group = source.StepGroups[g];
            var boundary = start + group.StepOffset;
            var last = end + group.StepOffset;
            // Without transforms all indexed sources are visible at every step, including later indices.
            var maxMesh = group.Transforms is null ? group.Meshes.Count : Math.Min(group.Meshes.Count, last + 1);
            var meshes = new List<SceneMesh?>(maxMesh);
            for (var i = 0; i < maxMesh; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                meshes.Add(i >= boundary || kept.Contains((i - group.StepOffset, g, false)) ? group.Meshes[i] : null);
                if (clock.ElapsedMilliseconds >= 6) { await Task.Delay(1, cancellationToken); clock.Restart(); }
            }
            SceneStepPathSource? paths = null;
            if (group.PathSource is { } p)
            {
                var maxPath = group.Transforms is null && p.HistoryStepLimit is null ? p.Count : Math.Min(p.Count, last + 1);
                var entries = new List<IEnumerable<ScenePoint>>(maxPath);
                for (var i = 0; i < maxPath; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    entries.Add(i >= boundary || kept.Contains((i - group.StepOffset, g, true)) ? p.Paths[i] : Array.Empty<ScenePoint>());
                    if (clock.ElapsedMilliseconds >= 6) { await Task.Delay(1, cancellationToken); clock.Restart(); }
                }
                paths = new SceneStepPathSource { Id = p.Id, Paths = entries, ClosePaths = p.ClosePaths,
                    Thicknesses = p.Thicknesses, Colors = p.Colors, DefaultColor = p.DefaultColor, HistoryStepLimit = p.HistoryStepLimit };
            }
            groups.Add(new SceneStepGroup { Meshes = meshes, PathSource = paths,
                Transforms = group.Transforms?.Take(Math.Min(group.Transforms.Count, last + 1)).ToArray(),
                StepOffset = boundary, DisplayStepCount = end - start + 1 });
        }
        var filtered = source with { StepGroups = groups, InitialStep = 0,
            HistoryOmission = candidates.Count > kept.Count
                ? source.HistoryOmission ?? new SceneHistoryOmission(start + 1, maximumEarlierObjects)
                : source.HistoryOmission };
        if (stride == 1) return filtered with { Version = 2 };
        var expanded = await ExportAsync(filtered, 0, end - start, stride, cancellationToken);
        return expanded.HistoryOmission is null ? expanded : expanded with { Version = 2 };
    }

    internal static async Task<SceneRequest> ExportAsync(SceneRequest source, int start, int end, int stride,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var count = source.StepGroups.Count > 0 ? source.StepGroups.Max(group => group.Count) : source.Steps.Count;
        if (start < 0 || end < start || end >= count || stride < 1)
            throw new ArgumentOutOfRangeException(nameof(start), "The selected step range or stride is invalid.");
        cancellationToken.ThrowIfCancellationRequested();
        if (start == 0 && end == count - 1 && stride == 1)
            return source with { InitialStep = 0 };

        var frames = new List<SceneRequest>();
        var clock = Stopwatch.StartNew();
        for (var first = start; ;)
        {
            var last = (int)Math.Min(end, (long)first + stride - 1);
            if (source.Kind == PresentationKind.TwoDimensional)
                frames.Add(source.Steps[last]);
            else
            {
                var accumulator = new FrameAccumulator();
                for (var index = first; index <= last; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    accumulator.Add(source.Steps.Count > 0 ? source.Steps[index] : source, index);
                    if (clock.ElapsedMilliseconds >= 6)
                    {
                        await Task.Delay(1, cancellationToken);
                        clock.Restart();
                    }
                }
                frames.Add(accumulator.Create(source.Steps.Count > 0 ? source.Steps[last] : source));
            }
            if (last == end) break;
            first = last + 1;
        }
        return source with { Steps = frames, StepGroups = [], InitialStep = 0 };
    }

    private sealed class FrameAccumulator
    {
        private readonly Dictionary<string, SceneMesh> meshes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, object> lines = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TextBillboard> billboards = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Source, int Index), ScenePath> stepPaths = [];

        internal void Add(SceneRequest frame, int stepIndex)
        {
            foreach (var mesh in frame.Meshes) meshes[mesh.Id] = mesh;
            foreach (var path in frame.Paths) AddLine(path.Id, path);
            foreach (var points in frame.PointSets) AddLine(points.Id, points);
            foreach (var batch in frame.PathBatches) AddLine(batch.Id, batch);
            foreach (var billboard in frame.Billboards) billboards[billboard.Id] = billboard;
            foreach (var group in frame.StepGroups)
            {
                foreach (var (mesh, meshTransform) in SceneGeometry.ResolveStepElements(group, group.Meshes, stepIndex))
                    meshes[mesh.Id] = meshTransform is null ? mesh : TransformMesh(mesh, meshTransform.Value);
                if (group.PathSource is not { } source) continue;
                var sourceStepIndex = stepIndex + group.StepOffset;
                var transform = group.Transforms is not null && sourceStepIndex < group.Transforms.Count
                    ? group.Transforms[sourceStepIndex] : null;
                foreach (var index in group.ResolvePathIndices(stepIndex))
                {
                    var vertices = source.Paths[index].Select(point => transform?.Transform(point)
                        ?? new System.Numerics.Vector3((float)point.X, (float)point.Y, (float)point.Z))
                        .Select(point => new[] { point.X, point.Y, point.Z }).ToList();
                    if (vertices.Count < 2) continue;
                    if (source.CloseAt(index)) vertices.Add(vertices[0]);
                    lines.Remove(source.Id);
                    stepPaths[(source.Id, index)] = new ScenePath
                    {
                        Id = source.Id,
                        Vertices = vertices,
                        Color = source.ColorAt(index),
                        Thickness = source.ThicknessAt(index)
                    };
                }
            }
        }

        private void AddLine(string id, object line)
        {
            lines[id] = line;
            foreach (var key in stepPaths.Keys.Where(key => key.Source == id).ToArray())
                stepPaths.Remove(key);
        }

        internal SceneRequest Create(SceneRequest template)
        {
            var paths = lines.Values.OfType<ScenePath>().ToList();
            var ids = new HashSet<string>(lines.Keys, StringComparer.Ordinal);
            foreach (var (key, path) in stepPaths)
            {
                var baseId = $"{key.Source}-step-{key.Index + 1}";
                var id = baseId;
                for (var suffix = 2; !ids.Add(id); suffix++) id = $"{baseId}-{suffix}";
                paths.Add(new ScenePath { Id = id, Vertices = path.Vertices, Color = path.Color, Thickness = path.Thickness });
            }
            return template with
            {
                Meshes = meshes.Values.ToList(),
                Paths = paths,
                PointSets = lines.Values.OfType<ScenePointSet>().ToList(),
                PathBatches = lines.Values.OfType<LineData>().ToList(),
                Billboards = billboards.Values.ToList(),
                HistoryOmission = null,
                Steps = [],
                StepGroups = [],
                InitialStep = 0
            };
        }

        private static SceneMesh TransformMesh(SceneMesh mesh, SceneTransform transform)
        {
            var result = SceneGeometry.ToMeshData(mesh, transform);
            return new SceneMesh
            {
                Id = result.Id,
                Vertices = result.Vertices.Select(point => new[] { point.X, point.Y, point.Z }).ToList(),
                Triangles = result.Indices.Select(triangle => new[] { triangle.a, triangle.b, triangle.c }).ToList(),
                PrimitiveSurfaceNormals = result.PrimitiveSurfaceNormals.Select(normal => new[] { normal.X, normal.Y, normal.Z }).ToList(),
                HasPrimitiveSurfaces = result.HasPrimitiveSurfaces,
                Colors = result.Colors.ToList(),
                ColorMode = result.ColorMode
            };
        }
    }
}
