using System.Diagnostics;

namespace BugViewer;

internal static class SceneRangeExporter
{
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
                var transform = group.Transforms is not null && stepIndex < group.Transforms.Count
                    ? group.Transforms[stepIndex] : null;
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
