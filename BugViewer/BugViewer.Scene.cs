using System.Numerics;
using Microsoft.JSInterop;

namespace BugViewer;

public partial class BugViewer
{
    /// <summary>Captures view state without copying the currently rendered geometry.</summary>
    internal async Task<(BugViewerOptions Options, CameraState? Camera)> GetViewStateAsync()
    {
        await _webGpuInteropGate.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(IsTearingDown, this);
            var options = Options.Clone();
            if (!Options.ShowAxes)
            {
                options.CoordinateThickness = visibleCoordinateThickness;
                options.LineWidthX = visibleGridLineWidthX;
                options.LineWidthY = visibleGridLineWidthY;
            }
            return (options, Camera is null ? null : GetCameraState());
        }
        finally
        {
            _webGpuInteropGate.Release();
        }
    }

    /// <summary>
    /// Replaces geometry, labels, settings, and camera with an independent copy of a scene.
    /// Invalid scenes are rejected before the viewer is changed. Geometry is queued if WebGPU is not ready.
    /// </summary>
    internal async Task LoadSnapshotAsync(SceneSnapshot scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        scene.Validate();
        var snapshot = scene.Snapshot();
        await ExecuteSceneOperationAsync("load scene", async module =>
        {
            await PauseRenderingCoreAsync(module);
            try
            {
                _suppressOptionsChanged = true;
                Options.Set(snapshot.Options);
                // Bounds and generated edges must not overwrite saved grid/camera settings.
                Options.AutoResetCamera = UpdateTypes.Never;
                Options.AutoUpdateGrid = UpdateTypes.Never;
                meshes = snapshot.Meshes;
                lines = snapshot.Lines;
                billBoards = snapshot.Billboards;
                meshDisplayLines.Clear();
                ReindexMeshIndices();
                ReindexLineIndices();
                ReindexBillboardIndices();
                RebuildMeshLookups();
                objectSpheres.Clear();
                foreach (var item in meshes.Cast<AbstractObject3D>().Concat(lines))
                    objectSpheres[item] = MinimumSphere.Run(item.Vertices);
                BoundingSphere = MinimumSphere.Run(meshes.Cast<AbstractObject3D>().Concat(lines)
                    .Concat(billBoards).SelectMany(item => item.Vertices));
                _sphereRadius = _sphereCenterLength = float.NaN;
                Camera ??= new OrbitCamera(Vector3.Zero, Options);

                await ApplyMeshFaceDisplayCoreAsync(null);
                await SynchronizeMeshDisplayLinesCoreAsync(null);
                _suppressOptionsChanged = true;
                Options.AutoResetCamera = snapshot.Options.AutoResetCamera;
                Options.AutoUpdateGrid = snapshot.Options.AutoUpdateGrid;
                visibleCoordinateThickness = Options.CoordinateThickness;
                visibleGridLineWidthX = Options.LineWidthX;
                visibleGridLineWidthY = Options.LineWidthY;
                if (!Options.ShowAxes)
                {
                    Options.CoordinateThickness = 0;
                    Options.LineWidthX = 0;
                    Options.LineWidthY = 0;
                }
                if (snapshot.Camera is { } camera)
                {
                    Camera.Apply(Options.ConstrainAzimuth ? camera : camera with
                    {
                        AzimuthAngle = Math.IEEERemainder(camera.AzimuthAngle, Math.Tau)
                    });
                    Options.OrthoSize = camera.OrthographicSize;
                    Options.IsProjectionCamera = camera.IsPerspective;
                }
                else
                {
                    ResetCameraCore();
                    if (!Options.IsProjectionCamera)
                        Options.OrthoSize = Math.Max(1e-6, (1 + Options.AutoCameraSphereBuffer)
                            * SphereRadius * Math.Max(1, _canvasHeight / Math.Max(1, _canvasWidth)));
                }

                if (module is not null)
                    await UploadSceneCoreAsync(module);
            }
            finally
            {
                _suppressOptionsChanged = true;
                Options.AutoResetCamera = snapshot.Options.AutoResetCamera;
                Options.AutoUpdateGrid = snapshot.Options.AutoUpdateGrid;
                _suppressOptionsChanged = false;
                await ResumeRenderingCoreAsync(module);
            }
        });
        await InvokeAsync(StateHasChanged);
    }

    internal Task FitSceneAsync() => ExecuteSceneOperationAsync("fit scene", async module =>
    {
        _suppressOptionsChanged = true;
        try
        {
            ResetCameraCore();
            if (!Options.IsProjectionCamera)
                Options.OrthoSize = Math.Max(1e-6, (1 + Options.AutoCameraSphereBuffer)
                    * SphereRadius * Math.Max(1, _canvasHeight / Math.Max(1, _canvasWidth)));
            if (module is not null)
            {
                await WriteViewMatrixCoreAsync(module);
                await WriteProjectionMatrixCoreAsync(module);
            }
        }
        finally { _suppressOptionsChanged = false; }
    });

    // Used for both queued startup scenes and replacement scenes while holding the interop gate.
    private async Task UploadSceneCoreAsync(IJSObjectReference module)
    {
        await module.InvokeVoidAsync("updateDisplayOptions", Options.ToJavascriptOptions(Camera!.PolarAngle));
        await WriteViewMatrixCoreAsync(module);
        await WriteProjectionMatrixCoreAsync(module);

        await module.InvokeVoidAsync("clearAllMeshes");
        if (meshes.Count > 0)
            await module.InvokeVoidAsync("addMeshes", (object)meshes.Select(mesh => mesh.CreateJavascriptData()).ToArray());
        ReindexMeshIndices();
        await ApplyMeshFaceDisplayCoreAsync(module);

        await module.InvokeVoidAsync("clearAllLines");
        if (lines.Count > 0)
            await SendLinesCoreAsync(module, lines);
        ReindexLineIndices();

        await module.InvokeVoidAsync("clearAllTextBillboards");
        foreach (var billboard in billBoards)
            await module.InvokeVoidAsync("addTextBillboard", billboard.CreateJavascriptData());
        ReindexBillboardIndices();
    }
}
