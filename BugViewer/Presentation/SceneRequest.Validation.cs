namespace BugViewer;

public partial record SceneRequest
{
    /// <summary>Checks the portable data before saving or displaying it.</summary>
    public void Validate() => Validate(new HashSet<SceneRequest>(ReferenceEqualityComparer.Instance));

    private void Validate(HashSet<SceneRequest> ancestors)
    {
        if (!ancestors.Add(this)) throw new InvalidDataException("Scene steps contain a cycle.");
        try
        {
            if (Format != "BugViewer.SceneRequest" || Version != 1)
                throw new InvalidDataException($"Unsupported scene format '{Format}' or version {Version}.");
            if (!Enum.IsDefined(Kind) || !Enum.IsDefined(AutoResetCamera) || !Enum.IsDefined(ShowSurfacesAs)
                || InitialStep < 0 || Meshes is null || Paths is null || PathBatches is null
                || PointSets is null || Steps is null || StepGroups is null || Billboards is null
                || Title is null || Heading is null || Subtitle is null)
                throw new InvalidDataException("Scene collections, presentation kind, or initial step are invalid.");
            var meshIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var mesh in Meshes) ValidateMesh(mesh, meshIds);
            var pathIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in Paths)
            {
                if (path is null) throw new InvalidDataException("A path is null.");
                Id(path.Id, pathIds);
                Coordinates(path.Vertices);
                if (!double.IsFinite(path.Thickness) || !float.IsFinite((float)path.Thickness))
                    throw new InvalidDataException($"Path '{path.Id}' has an invalid thickness.");
            }
            foreach (var points in PointSets)
            {
                if (points is null) throw new InvalidDataException("A point set is null.");
                Id(points.Id, pathIds);
                Coordinates(points.Points);
                if (!double.IsFinite(points.Radius) || points.Radius < 0)
                    throw new InvalidDataException($"Point set '{points.Id}' has an invalid radius.");
            }
            foreach (var batch in PathBatches) { if (batch is null) throw new InvalidDataException("A path batch is null."); Id(batch.Id, pathIds); }
            foreach (var group in StepGroups)
            {
                if (group is null || group.Meshes is null) throw new InvalidDataException("A step group is invalid.");
                foreach (var mesh in group.Meshes)
                    if (mesh is not null) ValidateMesh(mesh, meshIds);
                if (group.Transforms is not null)
                    foreach (var transform in group.Transforms)
                        if (transform is { } matrix && matrix.Values.Any(value => !double.IsFinite(value)))
                            throw new InvalidDataException("Use null for transform barriers; matrix values must be finite.");
                if (group.PathSource is not { } source) continue;
                Id(source.Id, pathIds);
                if (source.Paths is null || source.ClosePaths is null || source.Thicknesses is null || source.Colors is null
                    || source.HistoryStepLimit < 0 || source.Thicknesses.Any(value => !double.IsFinite(value) || !float.IsFinite((float)value)))
                    throw new InvalidDataException($"Step path source '{source.Id}' is invalid.");
                foreach (var path in source.Paths)
                    if (path is null || path.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y) || !double.IsFinite(point.Z)))
                        throw new InvalidDataException($"Step path source '{source.Id}' contains invalid coordinates.");
            }
            // Share validation of renderer-native batches, billboards, options and camera snapshots.
            new SceneSnapshot { Lines = PathBatches, Billboards = Billboards,
                Options = Options ?? BugViewerOptions.Default.Clone(), Camera = Camera }.Validate();
            if (Plot is { } plot)
            {
                if (plot.Traces is null) throw new InvalidDataException("Plot traces cannot be null.");
                foreach (var trace in plot.Traces)
                    if (trace is null || trace.Name is null || trace.X is null || trace.Y is null
                        || trace.X.Count != trace.Y.Count || trace.X.Concat(trace.Y).Any(value => !double.IsFinite(value))
                        || !Enum.IsDefined(trace.Type) || !Enum.IsDefined(trace.Marker))
                        throw new InvalidDataException("A plot trace has invalid coordinates or styling.");
                if (plot.Heatmap is { } heatmap && heatmap.Any(row => row is null
                    || heatmap[0] is null || row.Length != heatmap[0].Length || row.Any(value => !double.IsFinite(value))))
                    throw new InvalidDataException("The heatmap must be a rectangular array of finite values.");
            }
            if (Steps.Count > 0 && StepGroups.Count > 0)
                throw new InvalidDataException("Use either complete steps or step groups in a single request.");
            if (Kind == PresentationKind.TwoDimensional && (Meshes.Count > 0 || Paths.Count > 0 || PathBatches.Count > 0
                || StepGroups.Count > 0 || PointSets.Count > 0 || Billboards.Count > 0))
                throw new InvalidDataException("A 2D request cannot contain 3D geometry.");
            if (Kind == PresentationKind.ThreeDimensional && Plot is not null)
                throw new InvalidDataException("A 3D request cannot contain a 2D plot.");
            foreach (var step in Steps)
            {
                if (step is null || step.Kind != Kind) throw new InvalidDataException("All steps must have the request's presentation kind.");
                step.Validate(ancestors);
            }
        }
        finally { ancestors.Remove(this); }
    }

    private static void ValidateMesh(SceneMesh mesh, HashSet<string> ids)
    {
        if (mesh is null) throw new InvalidDataException("A mesh is null.");
        Id(mesh.Id, ids);
        Coordinates(mesh.Vertices);
        Coordinates(mesh.PrimitiveSurfaceNormals);
        if (mesh.Triangles is null || mesh.Colors is null || !Enum.IsDefined(mesh.ColorMode)
            || mesh.PrimitiveSurfaceNormals.Count != 0 && mesh.PrimitiveSurfaceNormals.Count != mesh.Vertices.Count)
            throw new InvalidDataException($"Mesh '{mesh.Id}' has invalid normals, triangles, or colors.");
        foreach (var triangle in mesh.Triangles)
            if (triangle is null || triangle.Length != 3 || triangle.Any(index => (uint)index >= mesh.Vertices.Count))
                throw new InvalidDataException($"Mesh '{mesh.Id}' contains an invalid triangle index.");
        var colorCount = mesh.ColorMode == MeshColoring.PerVertex ? mesh.Vertices.Count : mesh.Triangles.Count;
        if (mesh.HasUniformColor ? mesh.Colors.Count > 1 : mesh.Colors.Count != colorCount)
            throw new InvalidDataException($"Mesh '{mesh.Id}' has the wrong number of colors.");
    }

    private static void Coordinates(IEnumerable<float[]> coordinates)
    {
        if (coordinates is null || coordinates.Any(point => point is null || point.Length != 3 || point.Any(value => !float.IsFinite(value))))
            throw new InvalidDataException("Coordinates must contain three finite numbers.");
    }

    private static void Id(string id, HashSet<string> ids)
    {
        if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)) throw new InvalidDataException($"Invalid or duplicate object ID '{id}'.");
    }
}
