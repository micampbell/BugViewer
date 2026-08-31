// Imports for Blazor, FluentUI, and JS interop.
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Numerics;

namespace BugViewer
{
    /// <summary>
    /// A Blazor component for viewing 3D models using WebGPU. 
    /// It provides camera controls, customizable display options, and supports triangle selection.
    /// </summary>
    public partial class BugViewer
    {   // Partial class for the BugViewer component. The rest is in the razor file

        // Popover visibility flags.
        private bool _cameraPopover = false;
        private bool _optionsPopover = false;
        private bool _helpPopover = false;
        // Check if any popover is open.
        private bool IsAnyPopoverOpen => _cameraPopover || _optionsPopover || _helpPopover;
        // Mouse button state.
        private bool _isMouseButtonDown;

        /// <summary>
        /// Component parameters for tool orientation and alignment.
        /// </summary>
        [Parameter]
        public Orientation ToolsOrientation { get; set; }

        /// <summary>
        /// Component parameters for tool horizontal and vertical alignment.
        /// </summary>
        [Parameter]
        public HorizontalAlignment ToolsHorizontal { get; set; }

        /// <summary>
        /// Component parameters for tool vertical alignment.
        /// </summary>
        [Parameter]
        public VerticalAlignment ToolsVertical { get; set; }

        /// <summary>
        /// Component parameters for canvas width (and height below). 
        /// Accepts any valid CSS size (e.g., "100%", "800px"). 
        /// Defaults to full width and height of the viewport.
        /// </summary>
        [Parameter]
        public string Width { get; set; } = "100%";

        /// <summary>
        /// Component parameters for canvas height.
        /// </summary>
        [Parameter]
        public string Height { get; set; } = "100vh";

        // Element references for container and canvas.
        private ElementReference? _containerRef;
        private ElementReference? _canvasRef;

        /// <summary>
        /// Component parameter for display options. Setting this will 
        /// update the viewer's appearance and behavior.
        /// </summary>
        [Parameter]
        public BugViewerOptions Options
        {
            get
            {
                if (_options is null)
                {
                    _options = BugViewerOptions.Default.Clone();
                    _options.PropertyChanged += OnOptionsChanged;
                }
                return _options;
            }
            set
            {
                if (!ReferenceEquals(_options, value))
                {
                    if (_options is not null)
                        _options.PropertyChanged -= OnOptionsChanged;
                    _options = value;
                    _options.PropertyChanged += OnOptionsChanged;
                }
            }
        }
        private BugViewerOptions _options;

        #region Options Parameter Proxies
        // These parameters allow setting BugViewerOptions properties directly on the component.
        // They store values in backing fields and apply them in OnParametersSet.

        /// <summary>Light polar angle parameter.</summary>
        [Parameter]
        public double? LightPolarAngle { get; set; }

        /// <summary>Light azimuth angle parameter.</summary>
        [Parameter]
        public double? LightAzimuthAngle { get; set; }

        /// <summary>Directional light intensity parameter.</summary>
        [Parameter]
        public double? DirectionalLightIntensity { get; set; }

        /// <summary>Ambient light intensity parameter.</summary>
        [Parameter]
        public double? AmbientLight { get; set; }

        /// <summary>Camera-mounted headlamp intensity parameter.</summary>
        [Parameter]
        public double? HeadlampIntensity { get; set; }

        /// <summary>Camera-mounted headlamp focus parameter.</summary>
        [Parameter]
        public double? HeadlampFocus { get; set; }

        /// <summary>Specular power parameter.</summary>
        [Parameter]
        public double? SpecularPower { get; set; }

        /// <summary>Auto reset camera parameter.</summary>
        [Parameter]
        public UpdateTypes? AutoResetCamera { get; set; }

        /// <summary>Auto camera sphere buffer parameter.</summary>
        [Parameter]
        public double? AutoCameraSphereBuffer { get; set; }

        /// <summary>Auto update grid parameter.</summary>
        [Parameter]
        public UpdateTypes? AutoUpdateGrid { get; set; }

        /// <summary>Auto grid buffer parameter.</summary>
        [Parameter]
        public double? AutoGridBuffer { get; set; }

        /// <summary>Whether changing the theme automatically resets theme-related options.</summary>
        [Parameter]
        public bool? AutoResetOnThemeChange { get; set; }

        /// <summary>Background color at a camera polar angle of -90 degrees.</summary>
        [Parameter]
        public ColorRgba? BackgroundGradientNegativePolarColor { get; set; }

        /// <summary>First intermediate background gradient color.</summary>
        [Parameter]
        public ColorRgba? BackgroundGradientFirstIntermediatePolarColor { get; set; }

        /// <summary>Polar angle for the first intermediate background gradient color.</summary>
        [Parameter]
        public double? BackgroundGradientFirstIntermediatePolarAngle { get; set; }

        /// <summary>Second intermediate background gradient color.</summary>
        [Parameter]
        public ColorRgba? BackgroundGradientSecondIntermediatePolarColor { get; set; }

        /// <summary>Polar angle for the second intermediate background gradient color.</summary>
        [Parameter]
        public double? BackgroundGradientSecondIntermediatePolarAngle { get; set; }

        /// <summary>Background color at a camera polar angle of +90 degrees.</summary>
        [Parameter]
        public ColorRgba? BackgroundGradientPositivePolarColor { get; set; }

        /// <summary>Line color parameter.</summary>
        [Parameter]
        public ColorRgba? LineColor { get; set; }

        /// <summary>Line transparency parameter.</summary>
        [Parameter]
        public double? LineTransparency { get; set; }

        /// <summary>Base color parameter.</summary>
        [Parameter]
        public ColorRgba? BaseColor { get; set; }

        /// <summary>Base transparency parameter.</summary>
        [Parameter]
        public double? BaseTransparency { get; set; }

        /// <summary>Double click is select parameter.</summary>
        [Parameter]
        public bool? DoubleClickIsSelect { get; set; }

        /// <summary>Line width X parameter.</summary>
        [Parameter]
        public double? LineWidthX { get; set; }

        /// <summary>Line width Y parameter.</summary>
        [Parameter]
        public double? LineWidthY { get; set; }

        /// <summary>Path thickness factor parameter.</summary>
        [Parameter]
        public float? PathThicknessFactor { get; set; }

        /// <summary>
        /// Specifies how mesh surfaces are displayed. Options include showing triangles, surfaces, or none.
        /// </summary>
        [Parameter]
        public MeshFaceDisplay ShowSurfaceAs { get; set; } = MeshFaceDisplay.Triangles;

        /// <summary>
        /// Specifies whether mesh edges are displayed. When true, edges of the mesh will be visible.
        /// </summary>
        [Parameter]
        public bool ShowMeshEdges { get; set; } = false;

        /// <summary>
        /// Specifies whether mesh borders are displayed. When true, borders of the mesh will be visible.
        /// </summary>
        [Parameter]
        public bool ShowMeshBorders { get; set; } = true;

        /// <summary>
        /// Specifies whether axes are displayed in the viewer. When true, coordinate axes will be visible.
        /// </summary>
        [Parameter]
        public bool ShowAxes { get; set; } = true;



        /// <summary>Sample count parameter.</summary>
        [Parameter]
        public int? SampleCount { get; set; }

        /// <summary>Is projection camera parameter.</summary>
        [Parameter]
        public bool? IsProjectionCamera { get; set; }

        /// <summary>Field of view parameter.</summary>
        [Parameter]
        public double? Fov { get; set; }

        /// <summary>Orthographic size parameter.</summary>
        [Parameter]
        public double? OrthoSize { get; set; }

        /// <summary>Near clipping plane parameter.</summary>
        [Parameter]
        public double? ZNear { get; set; }

        /// <summary>Far clipping plane parameter.</summary>
        [Parameter]
        public double? ZFar { get; set; }

        /// <summary>Z is up parameter.</summary>
        [Parameter]
        public bool? ZIsUp { get; set; }

        /// <summary>Grid size parameter.</summary>
        [Parameter]
        public double? GridSize { get; set; }

        /// <summary>Grid spacing parameter.</summary>
        [Parameter]
        public double? GridSpacing { get; set; }

        /// <summary>Constrain polar parameter.</summary>
        [Parameter]
        public bool? ConstrainPolar { get; set; }

        /// <summary>Max polar parameter.</summary>
        [Parameter]
        public double? MaxPolar { get; set; }

        /// <summary>Min polar parameter.</summary>
        [Parameter]
        public double? MinPolar { get; set; }

        /// <summary>Constrain azimuth parameter.</summary>
        [Parameter]
        public bool? ConstrainAzimuth { get; set; }

        /// <summary>Max azimuth parameter.</summary>
        [Parameter]
        public double? MaxAzimuth { get; set; }

        /// <summary>Min azimuth parameter.</summary>
        [Parameter]
        public double? MinAzimuth { get; set; }

        /// <summary>Constrain distance parameter.</summary>
        [Parameter]
        public bool? ConstrainDistance { get; set; }

        /// <summary>Max distance parameter.</summary>
        [Parameter]
        public double? MaxDistance { get; set; }

        /// <summary>Min distance parameter.</summary>
        [Parameter]
        public double? MinDistance { get; set; }

        /// <summary>Orbit sensitivity parameter.</summary>
        [Parameter]
        public double? OrbitSensitivity { get; set; }

        /// <summary>Zoom sensitivity parameter.</summary>
        [Parameter]
        public double? ZoomSensitivity { get; set; }

        /// <summary>Pan sensitivity parameter.</summary>
        [Parameter]
        public double? PanSensitivity { get; set; }

        /// <summary>Pan speed multiplier parameter.</summary>
        [Parameter]
        public double? PanSpeedMultiplier { get; set; }

        /// <summary>Coordinate thickness parameter.</summary>
        [Parameter]
        public double? CoordinateThickness { get; set; }

        /// <summary>
        /// Applies parameter proxy values to the Options object.
        /// Called from OnParametersSet after Options is guaranteed to exist.
        /// </summary>
        private void ApplyParameterProxiesToOptions()
        {
            if (Options == null) return;

            if (LightPolarAngle.HasValue) Options.LightPolarAngle = LightPolarAngle.Value;
            if (LightAzimuthAngle.HasValue) Options.LightAzimuthAngle = LightAzimuthAngle.Value;
            if (DirectionalLightIntensity.HasValue) Options.DirectionalLightIntensity = DirectionalLightIntensity.Value;
            if (AmbientLight.HasValue) Options.AmbientLight = AmbientLight.Value;
            if (HeadlampIntensity.HasValue) Options.HeadlampIntensity = HeadlampIntensity.Value;
            if (HeadlampFocus.HasValue) Options.HeadlampFocus = HeadlampFocus.Value;
            if (SpecularPower.HasValue) Options.SpecularPower = SpecularPower.Value;
            if (AutoResetCamera.HasValue) Options.AutoResetCamera = AutoResetCamera.Value;
            if (AutoCameraSphereBuffer.HasValue) Options.AutoCameraSphereBuffer = AutoCameraSphereBuffer.Value;
            if (AutoUpdateGrid.HasValue) Options.AutoUpdateGrid = AutoUpdateGrid.Value;
            if (AutoGridBuffer.HasValue) Options.AutoGridBuffer = AutoGridBuffer.Value;
            if (BackgroundGradientNegativePolarColor.HasValue) Options.BackgroundGradientNegativePolarColor = BackgroundGradientNegativePolarColor.Value;
            if (BackgroundGradientFirstIntermediatePolarColor.HasValue) Options.BackgroundGradientFirstIntermediatePolarColor = BackgroundGradientFirstIntermediatePolarColor.Value;
            if (BackgroundGradientFirstIntermediatePolarAngle.HasValue) Options.BackgroundGradientFirstIntermediatePolarAngle = BackgroundGradientFirstIntermediatePolarAngle.Value;
            if (BackgroundGradientSecondIntermediatePolarColor.HasValue) Options.BackgroundGradientSecondIntermediatePolarColor = BackgroundGradientSecondIntermediatePolarColor.Value;
            if (BackgroundGradientSecondIntermediatePolarAngle.HasValue) Options.BackgroundGradientSecondIntermediatePolarAngle = BackgroundGradientSecondIntermediatePolarAngle.Value;
            if (BackgroundGradientPositivePolarColor.HasValue) Options.BackgroundGradientPositivePolarColor = BackgroundGradientPositivePolarColor.Value;
            if (LineColor.HasValue) Options.LineColor = LineColor.Value;
            if (LineTransparency.HasValue) Options.LineTransparency = LineTransparency.Value;
            if (BaseColor.HasValue) Options.BaseColor = BaseColor.Value;
            if (BaseTransparency.HasValue) Options.BaseTransparency = BaseTransparency.Value;
            if (DoubleClickIsSelect.HasValue) Options.DoubleClickIsSelect = DoubleClickIsSelect.Value;
            ApplyAxesParameterProxies();
            if (PathThicknessFactor.HasValue) Options.PathThicknessFactor = PathThicknessFactor.Value;
            if (SampleCount.HasValue) Options.SampleCount = SampleCount.Value;
            if (IsProjectionCamera.HasValue) Options.IsProjectionCamera = IsProjectionCamera.Value;
            if (Fov.HasValue) Options.Fov = Fov.Value;
            if (OrthoSize.HasValue) Options.OrthoSize = OrthoSize.Value;
            if (ZNear.HasValue) Options.ZNear = ZNear.Value;
            if (ZFar.HasValue) Options.ZFar = ZFar.Value;
            if (ZIsUp.HasValue) Options.ZIsUp = ZIsUp.Value;
            if (GridSize.HasValue) Options.GridSize = GridSize.Value;
            if (GridSpacing.HasValue) Options.GridSpacing = GridSpacing.Value;
            if (ConstrainPolar.HasValue) Options.ConstrainPolar = ConstrainPolar.Value;
            if (MaxPolar.HasValue) Options.MaxPolar = MaxPolar.Value;
            if (MinPolar.HasValue) Options.MinPolar = MinPolar.Value;
            if (ConstrainAzimuth.HasValue) Options.ConstrainAzimuth = ConstrainAzimuth.Value;
            if (MaxAzimuth.HasValue) Options.MaxAzimuth = MaxAzimuth.Value;
            if (MinAzimuth.HasValue) Options.MinAzimuth = MinAzimuth.Value;
            if (ConstrainDistance.HasValue) Options.ConstrainDistance = ConstrainDistance.Value;
            if (MaxDistance.HasValue) Options.MaxDistance = MaxDistance.Value;
            if (MinDistance.HasValue) Options.MinDistance = MinDistance.Value;
            if (OrbitSensitivity.HasValue) Options.OrbitSensitivity = OrbitSensitivity.Value;
            if (ZoomSensitivity.HasValue) Options.ZoomSensitivity = ZoomSensitivity.Value;
            if (PanSensitivity.HasValue) Options.PanSensitivity = PanSensitivity.Value;
            if (PanSpeedMultiplier.HasValue) Options.PanSpeedMultiplier = PanSpeedMultiplier.Value;
        }

        private void ApplyAxesParameterProxies()
        {
            if (ShowAxes)
            {
                if (CoordinateThickness.HasValue) Options.CoordinateThickness = CoordinateThickness.Value;
                if (LineWidthX.HasValue) Options.LineWidthX = LineWidthX.Value;
                if (LineWidthY.HasValue) Options.LineWidthY = LineWidthY.Value;
                return;
            }

            if (CoordinateThickness.HasValue) visibleCoordinateThickness = CoordinateThickness.Value;
            if (LineWidthX.HasValue) visibleGridLineWidthX = LineWidthX.Value;
            if (LineWidthY.HasValue) visibleGridLineWidthY = LineWidthY.Value;
        }

        #endregion

        /// <summary>
        /// Event callback that is invoked when the WebGPU canvas is ready.
        /// </summary>
        [Parameter]
        public EventCallback OnReady { get; set; }

        /// <summary>
        /// Event callback that is invoked when a triangle is selected
        /// (if DoubleClickIsSelect is true).
        /// </summary>
        [Parameter]
        public EventCallback OnTriangleSelected { get; set; }

        /// <summary>
        /// Keys that raycast the surface under the pointer while held. Configured keys are
        /// reserved for inspection and are not also used for camera movement.
        /// </summary>
        [Parameter]
        public IReadOnlyList<string> HoverSelectionKeys { get; set; } = [];

        /// <summary>
        /// Invoked when the active hover-selection key changes, including a null value
        /// when no hover-selection key remains pressed.
        /// </summary>
        [Parameter]
        public EventCallback<string?> OnHoverSelectionKeyChanged { get; set; }

        /// <summary>
        /// The camera object that manages the view matrix and 
        /// projection matrix based on user interactions.
        /// </summary>
        public OrbitCamera? Camera { get; private set; }

        private enum ViewerLifecycleState
        {
            Uninitialized,
            Initializing,
            Flushing,
            Ready,
            Failed,
            Disposing,
            Disposed
        }

        // JS interop objects. The semaphore owns access to the module and to all
        // scene state that can be mirrored to JavaScript.
        private IJSObjectReference? _module;
        private DotNetObjectReference<BugViewer>? _dotNetRef;
        private readonly SemaphoreSlim _webGpuInteropGate = new(1, 1);
        private readonly object _disposeSync = new();
        private Task? _disposeTask;
        private volatile ViewerLifecycleState _lifecycleState = ViewerLifecycleState.Uninitialized;
        private bool _suppressOptionsChanged;
        private string? _error;
        // Mouse interaction flags.
        private bool _isDragging;
        private bool _isPanning;
        // Last pointer coordinates.
        private double _lastPointerX;
        private double _lastPointerY;
        private bool _hasPointerPosition;
        private DateTime _lastHoverSelectionTime = DateTime.MinValue;
        private const double HoverSelectionIntervalMs = 50;
        private string? _activeHoverSelectionKey;

        /// <summary>
        /// A set of currently pressed keys for keyboard movement.
        /// </summary>
        public HashSet<string> PressedKeys = new();
        // Timer for keyboard movement.
        private System.Threading.Timer? _keyboardMoveTimer;
        // Bounding sphere for the scene.
        private Sphere BoundingSphere;

        /// <summary>
        /// Gets the thickness of paths in the scene, calculated as a factor of the bounding sphere radius.
        /// </summary>
        public float PathThickness => Math.Max(1e-6f, (float)Options.PathThicknessFactor * SphereRadius);

        /// <summary>
        /// Gets the radius of the bounding sphere that encompasses all objects in the scene.
        /// </summary>
        public float SphereRadius
        {
            get
            {
                if (double.IsNaN(_sphereRadius))
                    _sphereRadius = BoundingSphere.GetRadius();
                return _sphereRadius;
            }
        }
        private float _sphereRadius = float.NaN;
        /// <summary>
        /// Gets the center of the bounding sphere that encompasses all objects in the scene.
        /// </summary>
        public float SphereCenterLength
        {
            get
            {
                if (double.IsNaN(_sphereCenterLength))
                    _sphereCenterLength = BoundingSphere.Center.Length();
                return _sphereCenterLength;

            }
        }
        private float _sphereCenterLength = float.NaN;

        private Dictionary<AbstractObject3D, Sphere> objectSpheres = new();
        // Lists of 3D objects.
        private List<MeshData> meshes = new();
        private List<LineData> lines = new();
        private List<TextBillboard> billBoards = new();
        // Dictionaries for tracking sent object IDs.
        private Dictionary<string, int> sentMeshIds = [];
        private Dictionary<string, int> sentLineIds = [];
        private Dictionary<string, int> sentBBIds = [];
        private int renderPauseDepth;
        private double visibleCoordinateThickness;
        private double visibleGridLineWidthX;
        private double visibleGridLineWidthY;
        private readonly Dictionary<string, LineData> meshDisplayLines = [];
        private bool CanShowPrimitiveSurfaces => meshes.Any(mesh => mesh.HasPrimitiveSurfaces
            && mesh.PrimitiveSurfaceNormals.Count == mesh.Vertices.Count);
        private bool CanShowMeshBorders => meshes.Any(mesh => mesh.HasPrimitiveSurfaces);

        // Canvas dimensions.
        private double _canvasWidth = 800;
        private double _canvasHeight = 600;
        // Double-click detection variables.
        private DateTime _lastClickTime = DateTime.MinValue;
        private double _lastClickX;
        private double _lastClickY;
        private const double DoubleClickTimeMs = 300;
        private const double DoubleClickDistancePx = 5;

        /// <summary>
        /// Gets the time in milliseconds it took to render the
        /// latest frame, as reported by the JavaScript module.
        /// </summary>
        public double LatestFrameMs { get; private set; }

        /// <summary>
        /// Gets the name of the mesh that contains the currently selected triangle (if any).
        /// </summary>
        public string? SelectedMeshName { get; private set; } = null;

        /// <summary>
        /// Gets the index of the selected triangle within the selected mesh.
        /// </summary>
        public int SelectedTriangleInMeshIndex { get; private set; } = -1;

        /// <summary>
        /// Gets the world-space ray intersection point of the currently selected triangle.
        /// </summary>
        public Vector3 SelectedPoint { get; private set; } = Vector3.NaN;
        // Triangle intersection data.
        List<string> triangleToMesh = new();
        List<int> triangleToInMeshIndex = new();
        List<float> facePlaneDistances = new();
        List<Vector3> faceNormals = new();
        List<Vector3> bCoords = new();
        List<Vector3> uBarycentricMultipliers = new();
        List<Vector3> vBarycentricMultipliers = new();

        // MSAA sample count options.
        Option<int>? selectedIntOption;
        private List<Option<int>> _sampleCountItems = new()
    {
        new() { Value = 1, Text = "1x (No MSAA)" },
        // new() { Value = 2, Text = "2x" },  //generally not supported
        new() { Value = 4, Text = "4x MSAA" },
        new() { Value = 8, Text = "8x MSAA" }
    };
        // Handles sample count option changes.
        private void SampleCountOptionChanged(string args)
        {
            Options.SampleCount = int.Parse(args);
        }

        /// <summary>
        /// Initializes the component, setting up the camera and starting the keyboard movement timer.
        /// </summary>
        protected override void OnInitialized()
        {
            Options ??= BugViewerOptions.Default;
            Camera = new OrbitCamera(Vector3.Zero, Options);
            _keyboardMoveTimer = new System.Threading.Timer(_ => ProcessKeyboardMovement(), null, 0, 16);
        }



        // Handles key down events.
        private async Task OnKeyDown(KeyboardEventArgs e)
        {
            if (e.Key == "Escape")
            {
                _cameraPopover = false;
                _optionsPopover = false;
                _helpPopover = false;
                await ClearPressedKeysAsync();
                StateHasChanged();
                return;
            }
            if (e.Key == ",")
            {
                ShowOptionsPanel();
                await ClearPressedKeysAsync();
                return;
            }

            if (e.Key == ".")
            {
                ShowCameraPanel();
                await ClearPressedKeysAsync();
                return;
            }

            if (e.Key == "?" || (e.Key == "/"))
            {
                ShowHelpPanel();
                await ClearPressedKeysAsync();
                return;
            }
            if (e.Key == "t")
            {
                Options.ShowSurfacesAs = Options.ShowSurfacesAs switch
                {
                    MeshFaceDisplay.Triangles when CanShowPrimitiveSurfaces => MeshFaceDisplay.Surfaces,
                    MeshFaceDisplay.Triangles => MeshFaceDisplay.None,
                    MeshFaceDisplay.Surfaces => MeshFaceDisplay.None,
                    _ => MeshFaceDisplay.Triangles
                };
                return;
            }
            if (e.Key == "x")
            {
                Options.ShowAxes = !Options.ShowAxes;
                return;
            }
            if (e.Key == "m")
            {
                Options.ShowMeshEdges = !Options.ShowMeshEdges;
                return;
            }
            if (e.Key == "b")
            {
                if (CanShowMeshBorders)
                    Options.ShowMeshBorders = !Options.ShowMeshBorders;
                return;
            }
            if (IsAnyPopoverOpen) return;

            var key = e.Key.ToLowerInvariant();
            PressedKeys.Add(key);
            if (IsHoverSelectionKey(key))
            {
                await SetActiveHoverSelectionKeyAsync(key);
                if (_hasPointerPosition)
                    await SelectTriangleAtPointerAsync(_lastPointerX, _lastPointerY);
            }
        }

        // Toggles the options panel.
        private void ShowOptionsPanel()
        {
            _optionsPopover = !_optionsPopover;
            _helpPopover = false;
            _cameraPopover = false;
        }

        // Toggles the help panel.
        private void ShowHelpPanel()
        {
            _optionsPopover = false;
            _helpPopover = !_helpPopover;
            _cameraPopover = false;
        }

        // Toggles the camera panel.
        private void ShowCameraPanel()
        {
            _optionsPopover = false;
            _helpPopover = false;
            _cameraPopover = !_cameraPopover;
        }

        // Handles key up events.
        private async Task OnKeyUp(KeyboardEventArgs e)
        {
            var key = e.Key.ToLowerInvariant();
            PressedKeys.Remove(key);
            if (!string.Equals(key, _activeHoverSelectionKey, StringComparison.OrdinalIgnoreCase))
                return;

            var nextKey = HoverSelectionKeys.FirstOrDefault(candidate =>
                PressedKeys.Contains(candidate.ToLowerInvariant()));
            await SetActiveHoverSelectionKeyAsync(nextKey);
            if (nextKey is not null && _hasPointerPosition)
                await SelectTriangleAtPointerAsync(_lastPointerX, _lastPointerY);
        }

        private Task OnFocusLost(FocusEventArgs _) => ClearPressedKeysAsync();

        private async Task ClearPressedKeysAsync()
        {
            PressedKeys.Clear();
            await SetActiveHoverSelectionKeyAsync(null);
        }

        private async Task SetActiveHoverSelectionKeyAsync(string? key)
        {
            if (string.Equals(_activeHoverSelectionKey, key, StringComparison.OrdinalIgnoreCase))
                return;

            _activeHoverSelectionKey = key;
            await OnHoverSelectionKeyChanged.InvokeAsync(key);
        }

        private bool IsHoverSelectionKey(string key) =>
            HoverSelectionKeys.Any(candidate => string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase));

        private bool HasPressedHoverSelectionKey() =>
            HoverSelectionKeys.Any(candidate => PressedKeys.Contains(candidate.ToLowerInvariant()));

        private async Task OnPointerEnter(PointerEventArgs e)
        {
            _lastPointerX = e.ClientX;
            _lastPointerY = e.ClientY;
            _hasPointerPosition = true;
            if (HoverSelectionKeys.Count > 0 && _containerRef.HasValue)
                await _containerRef.Value.FocusAsync(true);
        }

        // Handles pointer down events.
        private async Task OnPointerDown(PointerEventArgs e)
        {
            if (_containerRef.HasValue)
                await _containerRef.Value.FocusAsync(true);
            var currentTime = DateTime.Now;
            var timeSinceLast = (currentTime - _lastClickTime).TotalMilliseconds;
            var dist = Math.Sqrt(Math.Pow(e.ClientX - _lastClickX, 2) + Math.Pow(e.ClientY - _lastClickY, 2));

            if (HoverSelectionKeys.Count == 0 && e.Button == 0 && timeSinceLast <= DoubleClickTimeMs && dist <= DoubleClickDistancePx)
            {
                await OnDoubleClick(e);
                return;
            }

            if (e.Button == 0)
            {
                _lastClickTime = currentTime;
                _lastClickX = e.ClientX;
                _lastClickY = e.ClientY;
                _isDragging = true;
                _isPanning = false;
                _lastPointerX = e.ClientX;
                _lastPointerY = e.ClientY;
            }
            else if (e.Button == 2)
            {
                _isPanning = true;
                _isDragging = false;
                _lastPointerX = e.ClientX;
                _lastPointerY = e.ClientY;
            }
        }

        // Handles double-click events.
        private async Task OnDoubleClick(PointerEventArgs e)
        {
            if (!Options.DoubleClickIsSelect)
            {
                ResetCamera();
                return;
            }

            await SelectTriangleAtPointerAsync(e.ClientX, e.ClientY);
        }

        private async Task SelectTriangleAtPointerAsync(double clientX, double clientY)
        {
            if (Camera is null || !OnTriangleSelected.HasDelegate)
                return;

            var selected = false;
            await ExecuteReadyInteropAsync("pointer selection", async module =>
            {
                var rect = await module.InvokeAsync<BoundingClientRect>("getBoundingClientRect", _canvasRef);
                var rx = clientX - rect.Left;
                var ry = clientY - rect.Top;
                (Vector3 anchor, Vector3 dirVector) = Camera.CreateRayFromScreenPoint(rx, ry, rect.Width, rect.Height);
                if (!DoesRayGoThroughTriangle(anchor, dirVector, out var meshName, out var meshIndex, out _, out var point))
                    return;

                SelectedMeshName = meshName;
                SelectedTriangleInMeshIndex = meshIndex;
                SelectedPoint = point;
                selected = true;
            });

            if (selected)
                await OnTriangleSelected.InvokeAsync();
        }

        // Checks if a ray intersects with any triangle in the scene.
        bool DoesRayGoThroughTriangle(Vector3 anchor, Vector3 dirVector, out string meshName, out int meshIndex, out float distance, out Vector3 point)
        {
            distance = float.MaxValue;
            meshIndex = -1;
            meshName = "";
            point = Vector3.Zero;
            for (int index = 0; index < triangleToMesh.Count; index++)
            {
                var normal = faceNormals[index];
                if (Vector3.Dot(normal, dirVector) >= 0)
                    continue; // ignore back faces
                var faceDistance = facePlaneDistances[index];
                var uBaryFactor = uBarycentricMultipliers[index];
                var vBaryFactor = vBarycentricMultipliers[index];
                var dot = Vector3.Dot(dirVector, normal);

                var anchorDistanceToPlane = faceDistance - Vector3.Dot(anchor, normal);
                var thisDistance = anchorDistanceToPlane / dot;
                if (thisDistance < 0 || thisDistance >= distance)
                    continue;
                var thisPoint = anchor + thisDistance * dirVector; // yes, it is normally '+' but in the previous line

                var wVector = thisPoint - bCoords[index];
                var u = Vector3.Dot(wVector, uBaryFactor);
                if (u <= 0 || u >= 1) continue;
                var v = Vector3.Dot(wVector, vBaryFactor);
                if (v <= 0 || v >= 1) continue;
                if (u + v > 1) continue;
                // yes intersecting!
                distance = thisDistance;
                meshName = triangleToMesh[index];
                meshIndex = triangleToInMeshIndex[index];
                point = thisPoint;
            }
            return meshIndex != -1;
        }

        // Represents the bounding client rectangle of an element.
        private class BoundingClientRect
        {
            public double Left { get; set; }
            public double Top { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
        }

        // Handles pointer move events for camera orbiting and panning.
        private async Task OnPointerMove(PointerEventArgs e)
        {
            _hasPointerPosition = true;
            if (_isDragging)
            {
                var dx = e.ClientX - _lastPointerX;
                var dy = e.ClientY - _lastPointerY;
                _lastPointerX = e.ClientX;
                _lastPointerY = e.ClientY;
                Camera.Orbit(dx, dy);

                await WriteViewMatrixAsync("camera orbit");
            }
            else if (!_isPanning)
            {
                _lastPointerX = e.ClientX;
                _lastPointerY = e.ClientY;
            }

            if (!_isDragging && !_isPanning && HasPressedHoverSelectionKey())
            {
                var now = DateTime.UtcNow;
                if ((now - _lastHoverSelectionTime).TotalMilliseconds >= HoverSelectionIntervalMs)
                {
                    _lastHoverSelectionTime = now;
                    await SelectTriangleAtPointerAsync(e.ClientX, e.ClientY);
                }
            }
            else if (_isPanning)
            {
                var dx = e.ClientX - _lastPointerX;
                var dy = e.ClientY - _lastPointerY;
                _lastPointerX = e.ClientX;
                _lastPointerY = e.ClientY;
                Camera.PanWithMouse(dx, dy, e.ShiftKey);

                await WriteViewMatrixAsync("camera pan");
            }
        }

        // Handles pointer up events to stop dragging or panning.
        private void OnPointerUp(PointerEventArgs e)
        {
            if (e.Button == 0)
            {
                _isDragging = false;
            }
            else if (e.Button == 2)
            {
                _isPanning = false;
            }
        }

        // Handles mouse wheel events for zooming.
        private async Task OnWheel(WheelEventArgs e)
        {
            Camera.Zoom(e.DeltaY);

            await WriteViewMatrixAsync("camera zoom");
        }

        // Processes keyboard input for camera movement.
        private async void ProcessKeyboardMovement()
        {
            if (PressedKeys.Count == 0 || _lifecycleState != ViewerLifecycleState.Ready)
                return;

            double forward = 0, right = 0, up = 0;
            bool shift = PressedKeys.Contains("shift");

            if (PressedKeys.Contains("w") && !IsHoverSelectionKey("w"))
            {
                forward += 1;
            }

            if (PressedKeys.Contains("s") && !IsHoverSelectionKey("s"))
            {
                forward -= 1;
            }

            if (PressedKeys.Contains("d") && !IsHoverSelectionKey("d"))
            {
                right += 1;
            }

            if (PressedKeys.Contains("a") && !IsHoverSelectionKey("a"))
            {
                right -= 1;
            }

            if (PressedKeys.Contains("q") && !IsHoverSelectionKey("q"))
            {
                up -= 1;
            }

            if (PressedKeys.Contains("e") && !IsHoverSelectionKey("e"))
            {
                up += 1;
            }

            if (forward != 0 || right != 0 || up != 0)
            {
                Camera.PanWithKeyboard(forward, right, up, shift);

                await WriteViewMatrixAsync("keyboard camera movement");
            }
        }

        private Task WriteViewMatrixAsync(string operation) =>
            ExecuteReadyInteropAsync(operation, module => WriteViewMatrixCoreAsync(module));

        private async Task WriteViewMatrixCoreAsync(IJSObjectReference module)
        {
            if (Camera is null)
                return;

            await module.InvokeVoidAsync("writeViewMatrix", Camera.ConvertMatrixToJavaScript(),
                Camera.PolarAngle, Camera.ConvertPositionToJavaScript());
        }

        private bool IsTearingDown => _lifecycleState is ViewerLifecycleState.Disposing or ViewerLifecycleState.Disposed;

        private void FailInteropLocked(string operation, Exception exception)
        {
            if (IsTearingDown)
                return;

            _lifecycleState = ViewerLifecycleState.Failed;
            _error ??= $"WebGPU {operation} failed: {exception.Message}";
            _ = InvokeAsync(StateHasChanged);
        }

        private async Task ExecuteSceneOperationAsync(
            string operation,
            Func<IJSObjectReference?, Task> operationCore)
        {
            await _webGpuInteropGate.WaitAsync();
            try
            {
                if (IsTearingDown)
                    return;

                var module = _lifecycleState == ViewerLifecycleState.Ready ? _module : null;
                await operationCore(module);
            }
            catch (JSDisconnectedException exception)
            {
                if (!IsTearingDown)
                    FailInteropLocked(operation, exception);
            }
            catch (JSException exception)
            {
                FailInteropLocked(operation, exception);
            }
            finally
            {
                _webGpuInteropGate.Release();
            }
        }

        private async Task ExecuteReadyInteropAsync(
            string operation,
            Func<IJSObjectReference, Task> operationCore)
        {
            await _webGpuInteropGate.WaitAsync();
            try
            {
                if (_lifecycleState != ViewerLifecycleState.Ready || _module is null)
                    return;

                await operationCore(_module);
            }
            catch (JSDisconnectedException exception)
            {
                if (!IsTearingDown)
                    FailInteropLocked(operation, exception);
            }
            catch (JSException exception)
            {
                FailInteropLocked(operation, exception);
            }
            finally
            {
                _webGpuInteropGate.Release();
            }
        }

        /// <summary>
        /// Imports and initializes the WebGPU module after the canvas exists.
        /// </summary>
        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!firstRender)
                return;

            if (_containerRef.HasValue)
                await _containerRef.Value.FocusAsync();

            await _webGpuInteropGate.WaitAsync();
            try
            {
                if (_lifecycleState != ViewerLifecycleState.Uninitialized)
                    return;

                _lifecycleState = ViewerLifecycleState.Initializing;
                _module = await JS.InvokeAsync<IJSObjectReference>("import", $"/_content/BugViewer/js/webgpu-canvas.js?v={DateTime.UtcNow.Ticks}");
                _dotNetRef = DotNetObjectReference.Create(this);
                await _module.InvokeVoidAsync("initGPU_Canvas", _dotNetRef, _canvasRef,
                    Options.ToJavascriptOptions(Camera.PolarAngle), Camera.ConvertMatrixToJavaScript(),
                    Camera.ConvertPositionToJavaScript());
            }
            catch (JSDisconnectedException exception)
            {
                if (!IsTearingDown)
                    FailInteropLocked("initialization", exception);
            }
            catch (JSException exception)
            {
                FailInteropLocked("initialization", exception);
            }
            finally
            {
                _webGpuInteropGate.Release();
            }
        }

        /// <summary>
        /// Called when component parameters are set or updated. This method applies any parameter proxy values to the Options object and sends updated options to the JavaScript module if it is initialized.
        /// </summary>
        /// <returns></returns>
        protected override async Task OnParametersSetAsync()
        {
            ApplyParameterProxiesToOptions();
            await SendOptionsToJavaScriptAsync();
        }

        private Task SendOptionsToJavaScriptAsync() =>
            ExecuteReadyInteropAsync("display-options update", async module =>
            {
                await module.InvokeVoidAsync("updateDisplayOptions", Options.ToJavascriptOptions(Camera.PolarAngle));
                await WriteProjectionMatrixCoreAsync(module);
            });

        /// <summary>
        /// Invoked by JavaScript to update the time it took to render the latest frame.
        /// </summary>
        /// <param name="ms"></param>
        /// <returns></returns>
        [JSInvokable]
        public Task OnFrameMsUpdate(double ms)
        {
            LatestFrameMs = ms;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Invoked by JavaScript when the WebGPU canvas is ready, marking the viewer as ready, sending any 
        /// queued meshes and options to JavaScript, and invoking the OnReady event callback.
        /// </summary>
        /// <returns></returns>
        [JSInvokable]
        public async Task OnWebGpuReady()
        {
            var invokeReady = false;
            await _webGpuInteropGate.WaitAsync();
            try
            {
                if (_lifecycleState != ViewerLifecycleState.Initializing || _module is null)
                    return;

                _lifecycleState = ViewerLifecycleState.Flushing;
                var module = _module;

                await module.InvokeVoidAsync("updateDisplayOptions", Options.ToJavascriptOptions(Camera.PolarAngle));
                await module.InvokeVoidAsync("writeViewMatrix", Camera.ConvertMatrixToJavaScript(),
                    Camera.PolarAngle, Camera.ConvertPositionToJavaScript());
                await WriteProjectionMatrixCoreAsync(module);

                await module.InvokeVoidAsync("clearAllMeshes");
                sentMeshIds = [];
                if (meshes.Count > 0)
                    await module.InvokeVoidAsync("addMeshes", (object)meshes.Select(mesh => mesh.CreateJavascriptData()).ToArray());
                ReindexSentMeshes();
                await ApplyMeshFaceDisplayCoreAsync(module);

                await module.InvokeVoidAsync("clearAllLines");
                sentLineIds = [];
                if (lines.Count > 0)
                {
                    var automaticThickness = PathThickness;
                    await module.InvokeVoidAsync("addLinesBatch",
                        (object)lines.Select(line => line.CreateJavascriptData(automaticThickness)).ToArray());
                }
                ReindexSentLines();

                await module.InvokeVoidAsync("clearAllTextBillboards");
                sentBBIds = [];
                for (var index = 0; index < billBoards.Count; index++)
                    await module.InvokeVoidAsync("addTextBillboard", billBoards[index].CreateJavascriptData());
                ReindexSentBillboards();

                if (renderPauseDepth > 0)
                    await module.InvokeVoidAsync("pauseRendering");

                _error = null;
                _lifecycleState = ViewerLifecycleState.Ready;
                invokeReady = true;
            }
            catch (JSDisconnectedException exception)
            {
                if (!IsTearingDown)
                    FailInteropLocked("queued-scene upload", exception);
            }
            catch (JSException exception)
            {
                FailInteropLocked("queued-scene upload", exception);
            }
            finally
            {
                _webGpuInteropGate.Release();
            }

            await InvokeAsync(StateHasChanged);
            if (invokeReady && OnReady.HasDelegate)
                await OnReady.InvokeAsync();
        }

        /// <summary>
        /// Invoked by JavaScript when an error occurs in the WebGPU module, updating the error state and marking the viewer as not ready.
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        [JSInvokable]
        public async Task OnWebGpuError(string message)
        {
            await _webGpuInteropGate.WaitAsync();
            try
            {
                if (!IsTearingDown)
                {
                    _lifecycleState = ViewerLifecycleState.Failed;
                    _error ??= message;
                }
            }
            finally
            {
                _webGpuInteropGate.Release();
            }

            await InvokeAsync(StateHasChanged);
        }

        /// <summary>
        /// Invoked by JavaScript when the WebGPU canvas is resized, updating the stored 
        /// canvas dimensions and sending an updated projection matrix to JavaScript to 
        /// account for the new aspect ratio.
        /// </summary>
        /// <param name="w"></param>
        /// <param name="h"></param>
        /// <returns></returns>
        [JSInvokable]
        public async Task OnCanvasResized(double w, double h)
        {
            await ExecuteSceneOperationAsync("canvas resize", async module =>
            {
                _canvasWidth = w;
                _canvasHeight = h;
                if (module is not null)
                    await WriteProjectionMatrixCoreAsync(module);
            });
        }

        private Task SendProjectionMatrixToJavaScriptAsync() =>
            ExecuteReadyInteropAsync("projection update", WriteProjectionMatrixCoreAsync);

        private async Task WriteProjectionMatrixCoreAsync(IJSObjectReference module)
        {
            if (Camera is null)
                return;

            var projection = Camera.ConvertProjectionMatrixToJavaScript(_canvasWidth, _canvasHeight);
            await module.InvokeVoidAsync("writeProjectionMatrix", projection);
        }

        /// <summary>
        /// Disposes of the component, cleaning up resources and notifying 
        /// JavaScript to dispose of the WebGPU canvas.
        /// </summary>
        /// <returns></returns>
        public ValueTask DisposeAsync()
        {
            lock (_disposeSync)
                return new ValueTask(_disposeTask ??= DisposeCoreAsync());
        }

        private async Task DisposeCoreAsync()
        {
            _lifecycleState = ViewerLifecycleState.Disposing;
            _keyboardMoveTimer?.Dispose();
            Options.PropertyChanged -= OnOptionsChanged;

            Exception? cleanupException = null;
            await _webGpuInteropGate.WaitAsync();
            try
            {
                var module = _module;
                if (module is not null)
                {
                    try
                    {
                        await module.InvokeVoidAsync("disposeWebGPU_Canvas");
                    }
                    catch (JSDisconnectedException)
                    {
                        // The browser connection may already be gone during teardown.
                    }
                    catch (Exception exception)
                    {
                        cleanupException = exception;
                    }

                    try
                    {
                        await module.DisposeAsync();
                    }
                    catch (JSDisconnectedException)
                    {
                        // The browser connection may already be gone during teardown.
                    }
                    catch (Exception exception)
                    {
                        cleanupException ??= exception;
                    }
                }
            }
            finally
            {
                _module = null;
                _dotNetRef?.Dispose();
                _dotNetRef = null;
                _lifecycleState = ViewerLifecycleState.Disposed;
                _webGpuInteropGate.Release();
            }

            if (cleanupException is not null)
                throw cleanupException;
        }

        /// <summary>
        /// Resets the camera to frame the entire scene based on the current bounding sphere. 
        /// This is called automatically based on the AutoResetCamera option when the bounding 
        /// sphere changes or when data changes, and can also be triggered manually by the user 
        /// through a double-click (if DoubleClickIsSelect is false) or through a UI button that 
        /// calls HandleCameraReset.
        /// </summary>
        public void ResetCamera()
        {
            //if (float.IsNaN(BoundingSphere.RadiusSquared) ||
            //    float.IsNaN(BoundingSphere.Center.X) ||
            //    float.IsNaN(BoundingSphere.Center.Y) ||
            //    float.IsNaN(BoundingSphere.Center.Z))
            //    return;
            //BoundingSphere = new Sphere(Vector3.Zero, 1f);
            ResetCameraCore();
            _ = WriteViewMatrixAsync("camera reset");
        }

        private void ResetCameraCore() => Camera?.Reset(BoundingSphere);

        // Handles the camera reset action.
        private Task HandleCameraReset()
        {
            ResetCamera();
            return Task.CompletedTask;
        }

        // Sets the camera to a cardinal view direction.
        private async Task HandleCameraCardinalView(CardinalDirection dir)
        {
            if (Camera is null)
            {
                return;
            }

            ResetCameraCore();
            Camera.SetCardinalView(dir);
            await WriteViewMatrixAsync("cardinal camera view");
        }

        // Handles changes to the viewer options.
        private async void OnOptionsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs? e)
        {
            if (_suppressOptionsChanged || IsTearingDown)
                return;

            if (e?.PropertyName == nameof(Options.ZIsUp))
            {
                Camera.SwapCameraUp();
                await WriteViewMatrixAsync("camera up-axis update");
            }

            if (e?.PropertyName == nameof(Options.IsProjectionCamera))
            {
                Options.AdjustCameraProjectionParameters();
            }

            if (e?.PropertyName == nameof(Options.PathThicknessFactor))
            {
                await RemoveLinesAsync(meshDisplayLines.Values.ToList());
                meshDisplayLines.Clear();
                await SynchronizeMeshDisplayLinesAsync();
            }

            if (e?.PropertyName == nameof(Options.ShowSurfacesAs))
            {
                if (Options.ShowSurfacesAs == MeshFaceDisplay.Surfaces && !CanShowPrimitiveSurfaces)
                {
                    Options.ShowSurfacesAs = MeshFaceDisplay.Triangles;
                    return;
                }

                await ApplyMeshFaceDisplayAsync();
            }

            if (e?.PropertyName == nameof(Options.ShowMeshEdges))
            {
                if (Options.ShowMeshEdges && Options.ShowMeshBorders)
                {
                    Options.ShowMeshBorders = false;
                    return;
                }

                await SynchronizeMeshDisplayLinesAsync();
            }

            if (e?.PropertyName == nameof(Options.ShowMeshBorders))
            {
                if (Options.ShowMeshBorders && !CanShowMeshBorders)
                {
                    Options.ShowMeshBorders = false;
                    return;
                }

                if (Options.ShowMeshBorders && Options.ShowMeshEdges)
                {
                    Options.ShowMeshEdges = false;
                    return;
                }

                await SynchronizeMeshDisplayLinesAsync();
            }

            if (e?.PropertyName == nameof(Options.ShowAxes))
            {
                if (Options.ShowAxes)
                {
                    Options.CoordinateThickness = visibleCoordinateThickness;
                    Options.LineWidthX = visibleGridLineWidthX;
                    Options.LineWidthY = visibleGridLineWidthY;
                }
                else
                {
                    visibleCoordinateThickness = Options.CoordinateThickness;
                    visibleGridLineWidthX = Options.LineWidthX;
                    visibleGridLineWidthY = Options.LineWidthY;
                    Options.CoordinateThickness = 0;
                    Options.LineWidthX = 0;
                    Options.LineWidthY = 0;
                }
            }

            if (!IsTearingDown)
            {
                await InvokeAsync(StateHasChanged);
                await SendOptionsToJavaScriptAsync();
            }
        }

        // Called only while the interop gate is held.
        private async Task UpdateViewerCoreAsync(IJSObjectReference? module, bool sphereChanged)
        {
            if (Camera is null)
                return;

            if ((sphereChanged && Options.AutoResetCamera == UpdateTypes.SphereChange) || Options.AutoResetCamera == UpdateTypes.OnDataChange)
            {
                ResetCameraCore();
                if (module is not null)
                    await WriteViewMatrixCoreAsync(module);
            }

            if ((sphereChanged && Options.AutoUpdateGrid == UpdateTypes.SphereChange) || Options.AutoUpdateGrid == UpdateTypes.OnDataChange)
            {
                _suppressOptionsChanged = true;
                try
                {
                    Options.GridSize = Options.AutoGridBuffer * (SphereCenterLength + SphereRadius);
                }
                finally
                {
                    _suppressOptionsChanged = false;
                }

                if (module is not null)
                    await module.InvokeVoidAsync("updateDisplayOptions", Options.ToJavascriptOptions(Camera.PolarAngle));
            }
        }

        // Updates the bounding sphere when adding an object.
        private bool UpdateSpheresAdd(AbstractObject3D obj3D)
        {
            var sphere = MinimumSphere.Run(obj3D.Vertices);
            objectSpheres[obj3D] = sphere;
            var need = !Sphere.AContainsB(BoundingSphere, sphere);

            if (need)
            {
                var newSphere = MinimumSphere.Run(objectSpheres.Keys.SelectMany(o => o.Vertices));
                need = !Sphere.IsPracticallySame(newSphere, BoundingSphere);

                if (need)
                {
                    BoundingSphere = newSphere;
                    _sphereRadius = float.NaN;
                    _sphereCenterLength = float.NaN;
                }
            }
            return need;
        }

        // Updates the bounding sphere when removing an object.
        private bool UpdateSpheresRemove(AbstractObject3D obj3D)
        {
            var sphere = objectSpheres[obj3D];
            objectSpheres.Remove(obj3D);
            var need = obj3D.Vertices.Any(v => !Sphere.OnSurface(BoundingSphere, v));

            if (need)
            {
                var newSphere = MinimumSphere.Run(objectSpheres.Keys.SelectMany(o => o.Vertices));
                need = !Sphere.IsPracticallySame(newSphere, BoundingSphere);

                if (need)
                {
                    BoundingSphere = newSphere;
                    _sphereRadius = float.NaN;
                    _sphereCenterLength = float.NaN;
                }
            }

            return need;
        }

        /// <summary>
        /// Adds a mesh to the scene. If a mesh with the same ID already exists, it will be replaced. If the WebGPU module is not ready, the mesh will be queued and sent when the module becomes ready.
        /// </summary>
        /// <param name="mesh"></param>
        /// <returns></returns>
        public async Task AddMeshAsync(MeshData mesh)
        {
            await ExecuteSceneOperationAsync($"add mesh '{mesh.Id}'", module => AddMeshCoreAsync(mesh, module));
        }

        private async Task AddMeshCoreAsync(MeshData mesh, IJSObjectReference? module)
        {
            var index = meshes.FindIndex(candidate => candidate.Id == mesh.Id);
            if (index >= 0)
            {
                if (mesh.GetHashCode() == meshes[index].GetHashCode())
                    return;
                await RemoveMeshAtCoreAsync(index, module);
            }

            meshes.Add(mesh);
            DefineMeshLookups(mesh);
            var sphereChanged = UpdateSpheresAdd(mesh);
            await UpdateViewerCoreAsync(module, sphereChanged);
            if (module is not null)
            {
                await module.InvokeVoidAsync("addMesh", mesh.CreateJavascriptData());
                ReindexSentMeshes();
            }

            await ApplyMeshFaceDisplayCoreAsync(module);
            await SynchronizeMeshDisplayLinesCoreAsync(module);
        }

        /// <summary>
        /// Adds multiple meshes to the scene in a single batch operation. If any of the meshes have IDs that already exist in the viewer, they will be 
        /// replaced. If the WebGPU module is not ready, the meshes will be queued and sent when the module becomes ready.
        /// </summary>
        /// <param name="newMeshes"></param>
        /// <returns></returns>
        public async Task AddMeshesAsync(IEnumerable<MeshData> newMeshes)
        {
            var meshList = newMeshes as IList<MeshData> ?? newMeshes.ToList();
            if (meshList.Count == 0)
                return;

            await ExecuteSceneOperationAsync($"add {meshList.Count} meshes",
                module => AddMeshesCoreAsync(meshList, module));
        }

        private async Task AddMeshesCoreAsync(IList<MeshData> meshList, IJSObjectReference? module)
        {
            var meshesToSend = new List<MeshData>(meshList.Count);
            var sphereChanged = false;
            foreach (var mesh in meshList)
            {
                var index = meshes.FindIndex(candidate => candidate.Id == mesh.Id);
                if (index >= 0)
                {
                    if (mesh.GetHashCode() == meshes[index].GetHashCode())
                        continue;
                    await RemoveMeshAtCoreAsync(index, module);
                }

                meshes.Add(mesh);
                DefineMeshLookups(mesh);
                sphereChanged |= UpdateSpheresAdd(mesh);
                meshesToSend.Add(mesh);
            }

            if (meshesToSend.Count == 0)
                return;

            await UpdateViewerCoreAsync(module, sphereChanged);
            if (module is not null)
            {
                await module.InvokeVoidAsync("addMeshes",
                    (object)meshesToSend.Select(mesh => mesh.CreateJavascriptData()).ToArray());
                ReindexSentMeshes();
            }

            await ApplyMeshFaceDisplayCoreAsync(module);
            await SynchronizeMeshDisplayLinesCoreAsync(module);
        }

        private void DefineMeshLookups(MeshData mesh)
        {
            for (int i = 0; i < mesh.Indices.Count; i++)
            {
                var tri = mesh.Indices[i];
                var a = mesh.Vertices[tri.a];
                var b = mesh.Vertices[tri.b];
                var c = mesh.Vertices[tri.c];
                var v1 = a - b;
                var v2 = c - b;
                var v1Sqd = v1.LengthSquared();
                var v2Sqd = v2.LengthSquared();
                var normal = Vector3.Normalize(Vector3.Cross(-v1, v2));
                var dist = Vector3.Dot(normal, a);
                var v1Dotv2 = Vector3.Dot(v1, v2);
                var oneOverDenom = 1 / (v1Sqd * v2Sqd - v1Dotv2 * v1Dotv2);
                var uBaryMultiplier = Vector3.Multiply(oneOverDenom,
                    Vector3.Multiply(v1Sqd, v2) - Vector3.Multiply(v1Dotv2, v1));
                var vBaryMultiplier = Vector3.Multiply(oneOverDenom,
                    Vector3.Multiply(v2Sqd, v1) - Vector3.Multiply(v1Dotv2, v2));
                triangleToMesh.Add(mesh.Id);
                triangleToInMeshIndex.Add(i);
                facePlaneDistances.Add(dist);
                faceNormals.Add(normal);
                bCoords.Add(b);
                uBarycentricMultipliers.Add(uBaryMultiplier);
                vBarycentricMultipliers.Add(vBaryMultiplier);
            }
        }

        /// <summary>
        /// Adds a line to the scene. If a line with the same ID already exists, it will be replaced.
        /// If the WebGPU module is not ready, the line will be queued and sent when the module becomes ready.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public async Task AddLinesAsync(LineData path)
        {
            await ExecuteSceneOperationAsync($"add line '{path.Id}'", module => AddLineCoreAsync(path, module));
        }

        private async Task AddLineCoreAsync(LineData path, IJSObjectReference? module)
        {
            var index = lines.FindIndex(line => line.Id == path.Id);
            if (index >= 0)
            {
                if (path.GetHashCode() == lines[index].GetHashCode())
                    return;
                await RemoveLineAtCoreAsync(index, module);
            }

            lines.Add(path);
            var sphereChanged = UpdateSpheresAdd(path);
            await UpdateViewerCoreAsync(module, sphereChanged);
            if (module is not null)
            {
                await module.InvokeVoidAsync("addLines", path.CreateJavascriptData(PathThickness));
                ReindexSentLines();
            }
        }

        /// <summary>
        /// Adds multiple lines with one JavaScript interop call. This is useful when a caller
        /// constructs a scene from many independent paths.
        /// </summary>
        public async Task AddLinesAsync(IEnumerable<LineData> newLines)
        {
            var lineList = newLines as IList<LineData> ?? newLines.ToList();
            if (lineList.Count == 0)
                return;

            await ExecuteSceneOperationAsync($"add {lineList.Count} lines",
                module => AddLinesCoreAsync(lineList, module));
        }

        private async Task AddLinesCoreAsync(IList<LineData> lineList, IJSObjectReference? module)
        {
            var linesToSend = new List<LineData>(lineList.Count);
            var sphereChanged = false;
            foreach (var line in lineList)
            {
                var existingIndex = lines.FindIndex(candidate => candidate.Id == line.Id);
                if (existingIndex >= 0)
                {
                    var existing = lines[existingIndex];
                    if (line.GetHashCode() == existing.GetHashCode())
                        continue;
                    await RemoveLineAtCoreAsync(existingIndex, module);
                }

                lines.Add(line);
                sphereChanged |= UpdateSpheresAdd(line);
                linesToSend.Add(line);
            }

            if (linesToSend.Count == 0)
                return;

            await UpdateViewerCoreAsync(module, sphereChanged);
            if (module is not null)
            {
                var automaticThickness = PathThickness;
                await module.InvokeVoidAsync("addLinesBatch",
                    (object)linesToSend.Select(line => line.CreateJavascriptData(automaticThickness)).ToArray());
                ReindexSentLines();
            }
        }

        /// <summary>Suspends WebGPU drawing until <see cref="ResumeRenderingAsync"/> is called.</summary>
        public async Task PauseRenderingAsync()
        {
            await ExecuteSceneOperationAsync("pause rendering", PauseRenderingCoreAsync);
        }

        private async Task PauseRenderingCoreAsync(IJSObjectReference? module)
        {
            if (renderPauseDepth++ == 0 && module is not null)
                await module.InvokeVoidAsync("pauseRendering");
        }

        /// <summary>Resumes WebGPU drawing after the matching pause.</summary>
        public async Task ResumeRenderingAsync()
        {
            await ExecuteSceneOperationAsync("resume rendering", ResumeRenderingCoreAsync);
        }

        private async Task ResumeRenderingCoreAsync(IJSObjectReference? module)
        {
            if (renderPauseDepth == 0 || --renderPauseDepth != 0 || module is null)
                return;

            await module.InvokeVoidAsync("resumeRendering");
        }

        private async Task ApplyMeshFaceDisplayAsync()
        {
            await ExecuteSceneOperationAsync("mesh face display update", ApplyMeshFaceDisplayCoreAsync);
        }

        private async Task ApplyMeshFaceDisplayCoreAsync(IJSObjectReference? module)
        {
            if (Options.ShowSurfacesAs == MeshFaceDisplay.Surfaces && !CanShowPrimitiveSurfaces)
            {
                _suppressOptionsChanged = true;
                try
                {
                    Options.ShowSurfacesAs = MeshFaceDisplay.Triangles;
                }
                finally
                {
                    _suppressOptionsChanged = false;
                }
            }

            if (module is not null)
            {
                await module.InvokeVoidAsync("setMeshFaceDisplay",
                    Options.ShowSurfacesAs != MeshFaceDisplay.None,
                    Options.ShowSurfacesAs == MeshFaceDisplay.Surfaces);
            }

            _ = InvokeAsync(StateHasChanged);
        }

        private async Task SynchronizeMeshDisplayLinesAsync()
        {
            await ExecuteSceneOperationAsync("mesh edge display update", SynchronizeMeshDisplayLinesCoreAsync);
        }

        private async Task SynchronizeMeshDisplayLinesCoreAsync(IJSObjectReference? module)
        {
            var desired = new Dictionary<string, LineData>();
            foreach (var mesh in meshes)
            {
                if (Options.ShowMeshEdges)
                {
                    var lineData = CreateMeshDisplayLines(mesh, false);
                    if (lineData is not null)
                        desired.Add($"__mesh-edges-{mesh.Id}", lineData);
                }
                if (Options.ShowMeshBorders && CanShowMeshBorders)
                {
                    var lineData = CreateMeshDisplayLines(mesh, true);
                    if (lineData is not null)
                        desired.Add($"__mesh-borders-{mesh.Id}", lineData);
                }
            }

            var linesToRemove = meshDisplayLines.Values
                .Where(line => !desired.ContainsKey(line.Id)).ToList();
            var linesToAdd = desired.Values
                .Where(line => !meshDisplayLines.ContainsKey(line.Id)).ToList();
            await PauseRenderingCoreAsync(module);
            try
            {
                if (linesToRemove.Count > 0)
                    await RemoveLinesCoreAsync(linesToRemove, module);
                if (linesToAdd.Count > 0)
                    await AddLinesCoreAsync(linesToAdd, module);

                meshDisplayLines.Clear();
                foreach (var line in desired)
                    meshDisplayLines.Add(line.Key, line.Value);
            }
            finally
            {
                await ResumeRenderingCoreAsync(module);
            }
        }

        private LineData? CreateMeshDisplayLines(MeshData mesh, bool bordersOnly)
        {
            var edgeCounts = new Dictionary<(int first, int second), int>();
            foreach (var (a, b, c) in mesh.Indices)
            {
                CountEdge(a, b);
                CountEdge(b, c);
                CountEdge(c, a);
            }

            // An internal edge is shared by exactly two triangles in this primitive mesh.
            // Open and non-manifold edges are both borders.
            var edges = edgeCounts.Where(pair => !bordersOnly || pair.Value != 2)
                .Select(pair => pair.Key).ToList();
            if (edges.Count == 0)
                return null;
            var vertices = new List<Vector3>(edges.Count * 2);
            foreach (var (first, second) in edges)
            {
                vertices.Add(mesh.Vertices[first]);
                vertices.Add(mesh.Vertices[second]);
            }

            return new LineData
            {
                Id = bordersOnly ? $"__mesh-borders-{mesh.Id}" : $"__mesh-edges-{mesh.Id}",
                Vertices = vertices,
                Colors = Enumerable.Repeat(ColorRgba.Black, Math.Max(0, vertices.Count - 1)),
                Thicknesses = Enumerable.Range(0, Math.Max(0, vertices.Count - 1))
                    .Select(index => index % 2 == 0 ? PathThickness : 0f),
                FadeFactors = Enumerable.Repeat(0f, Math.Max(0, vertices.Count - 1))
            };

            void CountEdge(int first, int second)
            {
                var key = first < second ? (first, second) : (second, first);
                edgeCounts.TryGetValue(key, out var count);
                edgeCounts[key] = count + 1;
            }
        }

        /// <summary>
        /// Changes the color of a mesh.
        /// </summary>
        /// <param name="mesh"></param>
        /// <param name="color"></param>
        /// <returns></returns>
        public async Task ChangeMeshColorAsync(MeshData mesh, ColorRgba color)
        {
            await ExecuteSceneOperationAsync($"change mesh color '{mesh.Id}'", async module =>
            {
                var index = meshes.FindIndex(candidate => candidate.Id == mesh.Id);
                if (index < 0 || meshes[index].ColorMode != MeshColoring.UniformColor)
                    return;

                meshes[index].Colors = [color];
                if (module is not null)
                {
                    await module.InvokeVoidAsync("changeMeshColor", new
                    {
                        index,
                        color = new[]
                        {
                            color.R / 255f,
                            color.G / 255f,
                            color.B / 255f,
                            color.A / 255f
                        }
                    });
                }
            });
        }

        /// <summary>
        /// Changes all per-triangle colors of an existing mesh without replacing its geometry.
        /// </summary>
        public async Task ChangeMeshColorsAsync(string meshId, IEnumerable<ColorRgba> colors)
        {
            var colorList = colors.ToList();
            await ExecuteSceneOperationAsync($"change mesh colors '{meshId}'", async module =>
            {
                var index = meshes.FindIndex(candidate => candidate.Id == meshId);
                if (index < 0)
                    return;

                var mesh = meshes[index];
                if (mesh.ColorMode != MeshColoring.PerTriangle)
                    throw new InvalidOperationException(
                        $"Mesh '{meshId}' was not created with per-triangle coloring.");
                if (colorList.Count != mesh.Indices.Count)
                    throw new ArgumentException("The color count must match the mesh triangle count.", nameof(colors));

                mesh.Colors = colorList;
                if (module is not null)
                {
                    var gpuColors = colorList.SelectMany(color =>
                        ColorRgba.ToJavaScript(color)
                            .Concat(ColorRgba.ToJavaScript(color))
                            .Concat(ColorRgba.ToJavaScript(color)))
                        .ToArray();
                    await module.InvokeVoidAsync("changeMeshColors", new { meshId, colors = gpuColors });
                }
            });
        }

        /// <summary>
        /// Removes a mesh from the scene.
        /// </summary>
        /// <param name="mesh"></param>
        /// <returns></returns>
        public async Task RemoveMeshAsync(MeshData mesh)
        {
            await ExecuteSceneOperationAsync($"remove mesh '{mesh.Id}'", async module =>
            {
                var index = meshes.FindIndex(candidate => candidate.Id == mesh.Id);
                if (index < 0)
                    return;

                await RemoveMeshAtCoreAsync(index, module);
                await ApplyMeshFaceDisplayCoreAsync(module);
                await SynchronizeMeshDisplayLinesCoreAsync(module);
            });
        }

        /// <summary>Removes a group of meshes without rebuilding the remaining WebGPU scene.</summary>
        public async Task RemoveMeshesAsync(IEnumerable<MeshData> meshesToRemove)
        {
            var meshList = meshesToRemove as IList<MeshData> ?? meshesToRemove.ToList();
            await ExecuteSceneOperationAsync($"remove {meshList.Count} meshes",
                module => RemoveMeshesCoreAsync(meshList, module));
        }

        private async Task RemoveMeshesCoreAsync(IEnumerable<MeshData> meshesToRemove, IJSObjectReference? module)
        {
            var ids = meshesToRemove.Select(mesh => mesh.Id).ToHashSet();
            if (ids.Count == 0)
                return;

            var indices = meshes.Select((mesh, index) => (mesh, index))
                .Where(item => ids.Contains(item.mesh.Id))
                .Select(item => item.index)
                .OrderDescending()
                .ToList();
            if (indices.Count == 0)
                return;

            var sphereChanged = false;
            foreach (var index in indices)
            {
                sphereChanged |= UpdateSpheresRemove(meshes[index]);
                meshes.RemoveAt(index);
            }
            await UpdateViewerCoreAsync(module, sphereChanged);
            if (module is not null)
            {
                await module.InvokeVoidAsync("removeMeshes", (object)ids.ToArray());
                ReindexSentMeshes();
            }
            await ApplyMeshFaceDisplayCoreAsync(module);
            await SynchronizeMeshDisplayLinesCoreAsync(module);
        }

        private async Task RemoveMeshAtCoreAsync(int index, IJSObjectReference? module)
        {
            var meshId = meshes[index].Id;
            var sphereChanged = UpdateSpheresRemove(meshes[index]);
            meshes.RemoveAt(index);
            await UpdateViewerCoreAsync(module, sphereChanged);
            if (module is not null)
            {
                await module.InvokeVoidAsync("removeMeshes", (object)new[] { meshId });
                ReindexSentMeshes();
            }
        }

        /// <summary>
        /// Clears all meshes from the scene. If the WebGPU module is not ready, it will simply clear the queued meshes so that when the module does become ready, those meshes will not be sent to JavaScript.
        /// </summary>
        /// <returns></returns>
        public async Task ClearAllMeshesAsync()
        {
            await ExecuteSceneOperationAsync("clear all meshes", async module =>
            {
                if (meshes.Count == 0)
                    return;

                await RemoveLinesCoreAsync(meshDisplayLines.Values.ToList(), module);
                meshDisplayLines.Clear();
                foreach (var mesh in meshes)
                    UpdateSpheresRemove(mesh);
                meshes.Clear();
                sentMeshIds?.Clear();
                await UpdateViewerCoreAsync(module, true);
                if (module is not null)
                    await module.InvokeVoidAsync("clearAllMeshes");
                await ApplyMeshFaceDisplayCoreAsync(module);
            });
        }

        /// <summary>
        /// Removes lines from the scene.
        /// </summary>
        /// <param name="line"></param>
        /// <returns></returns>
        public async Task RemoveLinesAsync(LineData line)
        {
            await ExecuteSceneOperationAsync($"remove line '{line.Id}'", async module =>
            {
                var index = lines.FindIndex(candidate => candidate.Id == line.Id);
                if (index >= 0)
                    await RemoveLineAtCoreAsync(index, module);
            });
        }

        /// <summary>Removes a group of lines without clearing and rebuilding the remaining lines.</summary>
        public async Task RemoveLinesAsync(IEnumerable<LineData> linesToRemove)
        {
            var lineList = linesToRemove as IList<LineData> ?? linesToRemove.ToList();
            await ExecuteSceneOperationAsync($"remove {lineList.Count} lines",
                module => RemoveLinesCoreAsync(lineList, module));
        }

        private async Task RemoveLinesCoreAsync(IEnumerable<LineData> linesToRemove, IJSObjectReference? module)
        {
            var ids = linesToRemove.Select(line => line.Id).ToHashSet();
            if (ids.Count == 0)
                return;

            var indices = lines.Select((line, index) => (line, index))
                .Where(item => ids.Contains(item.line.Id))
                .Select(item => item.index)
                .OrderDescending()
                .ToList();
            if (indices.Count == 0)
                return;

            var sphereChanged = false;
            foreach (var index in indices)
            {
                sphereChanged |= UpdateSpheresRemove(lines[index]);
                lines.RemoveAt(index);
            }
            await UpdateViewerCoreAsync(module, sphereChanged);
            if (module is not null)
            {
                await module.InvokeVoidAsync("removeLinesBatch", (object)ids.ToArray());
                ReindexSentLines();
            }
        }

        private async Task RemoveLineAtCoreAsync(int index, IJSObjectReference? module)
        {
            var lineId = lines[index].Id;
            var sphereChanged = UpdateSpheresRemove(lines[index]);
            lines.RemoveAt(index);
            await UpdateViewerCoreAsync(module, sphereChanged);
            if (module is not null)
            {
                await module.InvokeVoidAsync("removeLines", lineId);
                ReindexSentLines();
            }
        }

        /// <summary>
        /// Clears all lines from the scene. If the WebGPU module is not ready, it will simply clear the queued lines so that when the module does become ready, those lines will not be sent to JavaScript.
        /// </summary>
        /// <returns></returns>
        public async Task ClearAllLinesAsync()
        {
            await ExecuteSceneOperationAsync("clear all lines", async module =>
            {
                if (lines.Count == 0)
                    return;

                var sphereChanged = false;
                foreach (var line in lines)
                    sphereChanged |= UpdateSpheresRemove(line);
                lines.Clear();
                sentLineIds?.Clear();
                meshDisplayLines.Clear();
                await UpdateViewerCoreAsync(module, sphereChanged);
                if (module is not null)
                    await module.InvokeVoidAsync("clearAllLines");
            });
        }

        private void ReindexSentLines()
        {
            if (sentLineIds is null)
                return;

            sentLineIds.Clear();
            for (var i = 0; i < lines.Count; i++)
                sentLineIds[lines[i].Id] = i;
        }

        private void ReindexSentMeshes()
        {
            if (sentMeshIds is null)
                return;

            sentMeshIds.Clear();
            for (var i = 0; i < meshes.Count; i++)
                sentMeshIds[meshes[i].Id] = i;
        }

        private void ReindexSentBillboards()
        {
            if (sentBBIds is null)
                return;

            sentBBIds.Clear();
            for (var index = 0; index < billBoards.Count; index++)
                sentBBIds[billBoards[index].Id] = index;
        }

        /// <summary>
        /// Adds a text billboard to the scene at the specified position with the given text and colors. If a billboard with the same ID already exists, it will be replaced.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="text"></param>
        /// <param name="position"></param>
        /// <param name="backgroundColor"></param>
        /// <param name="textColor"></param>
        /// <param name="scale">The billboard half-height in world-space units.</param>
        /// <returns></returns>
        public async Task AddTextBillboardAsync(string id, string text, Vector3 position,
            ColorRgba backgroundColor, ColorRgba textColor, float scale, float relativeX, float relativeY)
        {
            var billboardData = new TextBillboard
            {
                BackgroundColor = backgroundColor,
                TextColor = textColor,
                Text = text,
                Vertices = new List<Vector3> { position },
                Id = id,
                Scale = scale,
                RelativeX = relativeX,
                RelativeY = relativeY
            };
            await ExecuteSceneOperationAsync($"add text billboard '{id}'", async module =>
            {
                var index = billBoards.FindIndex(candidate => candidate.Id == id);
                if (index >= 0)
                    await RemoveTextBillboardAtCoreAsync(index, module);

                billBoards.Add(billboardData);
                if (module is not null)
                {
                    await module.InvokeVoidAsync("addTextBillboard", billboardData.CreateJavascriptData());
                    ReindexSentBillboards();
                }
            });
        }

        /// <summary>
        /// Removes a text billboard from the scene.
        /// </summary>
        /// <param name="billBoard"></param>
        /// <returns></returns>
        public async Task RemoveTextBillboardAsync(TextBillboard billBoard)
        {
            await ExecuteSceneOperationAsync($"remove text billboard '{billBoard.Id}'", async module =>
            {
                var index = billBoards.FindIndex(candidate => candidate.Id == billBoard.Id);
                if (index >= 0)
                    await RemoveTextBillboardAtCoreAsync(index, module);
            });
        }

        private async Task RemoveTextBillboardAtCoreAsync(int index, IJSObjectReference? module)
        {
            billBoards.RemoveAt(index);
            if (module is not null)
            {
                await module.InvokeVoidAsync("removeTextBillboard", index);
                ReindexSentBillboards();
            }
        }

        /// <summary>
        /// Clears all text billboards from the scene. If the WebGPU module is not ready, it will simply clear the 
        /// queued billboards so that when the module does become ready, those billboards will not be sent to
        /// JavaScript.
        /// </summary>
        /// <returns></returns>
        public async Task ClearAllTextBillboardsAsync()
        {
            await ExecuteSceneOperationAsync("clear all text billboards", async module =>
            {
                if (billBoards.Count == 0)
                    return;

                billBoards.Clear();
                sentBBIds?.Clear();
                if (module is not null)
                    await module.InvokeVoidAsync("clearAllTextBillboards");
            });
        }
    }
}
