using System.Numerics;

namespace BugViewer;

/// <summary>
/// Represents an orbit camera for navigating a 3D scene.
/// This camera supports rotation (orbit), zoom, and pan controls.
/// </summary>
public class OrbitCamera
{
    private readonly BugViewerOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrbitCamera"/> class.
    /// </summary>
    /// <param name="target">The initial target for the camera to orbit around.</param>
    /// <param name="options">The camera and viewer options.</param>
    internal OrbitCamera(Vector3 target, BugViewerOptions options)
    {
        _options = options;
        Target = target;
        AzimuthAngle = Math.PI / 4; // Default to 45° for the initial view
        PolarAngle = Math.PI / 6;   // Default to 30° for the initial view
        Distance = 50;
    }

    #region Properties and Fields
    /// <summary>
    /// Gets or sets the vertical orbit angle in radians (pitch).
    /// </summary>
    public double PolarAngle
    {
        get => polarAngle;
        set
        {
            polarAngle = _options.ConstrainPolar ? Math.Clamp(value, _options.MinPolar, _options.MaxPolar) : value;
            updateCamera = true;
        }
    }
    private double polarAngle;

    /// <summary>
    /// Gets or sets the horizontal orbit angle in radians (yaw).
    /// </summary>
    public double AzimuthAngle
    {
        get => azimuthAngle;
        set
        {
            var newValue = value;
            if (_options.ConstrainAzimuth)
            {
                newValue = Math.Clamp(value, _options.MinAzimuth, _options.MaxAzimuth);
            }
            else
            {
                // Wrap to [-π, π]
                while (newValue < -Math.PI) newValue += Math.Tau;
                while (newValue >= Math.PI) newValue -= Math.Tau;
            }
            azimuthAngle = newValue;
            updateCamera = true;
        }
    }
    private double azimuthAngle;

    /// <summary>
    /// Gets or sets the distance from the camera to the target (zoom level).
    /// </summary>
    public double Distance
    {
        get => distance;
        set
        {
            distance = _options.ConstrainDistance ? Math.Clamp(value, _options.MinDistance, _options.MaxDistance) : value;
            updateCamera = true;
        }
    }
    private double distance;

    /// <summary>
    /// Gets the point in 3D space that the camera orbits around.
    /// </summary>
    internal Vector3 Target
    {
        get => target;
        private set
        {
            target = value;
            updateCamera = true;
        }
    }
    private Vector3 target = Vector3.Zero;
    #endregion

    #region Camera Controls
    /// <summary>
    /// Updates the orbit angles based on pointer or mouse movement.
    /// </summary>
    /// <param name="azimuth">The horizontal movement in pixels.</param>
    /// <param name="polar">The vertical movement in pixels.</param>
    public void Orbit(double azimuth, double polar)
    {
        AzimuthAngle += azimuth * _options.OrbitSensitivity;
        PolarAngle += polar * _options.OrbitSensitivity;
    }

    /// <summary>
    /// Updates the zoom level based on the mouse wheel delta.
    /// </summary>
    /// <param name="wheelDelta">The mouse wheel delta.</param>
    public void Zoom(double wheelDelta)
    {
        var zoomFactor = 1.0 + (wheelDelta * _options.ZoomSensitivity);
        if (_options.IsProjectionCamera)
            Distance *= zoomFactor;
        else
            _options.OrthoSize *= zoomFactor;
    }

    /// <summary>
    /// Pans the camera target based on mouse movement.
    /// </summary>
    /// <param name="deltaX">The horizontal movement in pixels.</param>
    /// <param name="deltaY">The vertical movement in pixels.</param>
    /// <param name="shiftPressed">Indicates whether the Shift key is pressed for faster panning.</param>
    public void PanWithMouse(double deltaX, double deltaY, bool shiftPressed = false)
    {
        var panSpeed = _options.PanSensitivity * Distance * (shiftPressed ? _options.PanSpeedMultiplier : 1.0);
        var camMatrix = CameraMatrix;
        var right = new Vector3(camMatrix.M11, camMatrix.M12, camMatrix.M13);
        var up = new Vector3(camMatrix.M21, camMatrix.M22, camMatrix.M23);

        Target += right * (float)(-deltaX * panSpeed);
        Target += up * (float)(deltaY * panSpeed);
        updateCamera = true;
    }

    /// <summary>
    /// Pans the camera based on keyboard input.
    /// </summary>
    /// <param name="forward">The forward/backward movement.</param>
    /// <param name="right">The right/left movement.</param>
    /// <param name="up">The up/down movement.</param>
    /// <param name="shiftPressed">Indicates whether the Shift key is pressed for faster panning.</param>
    public void PanWithKeyboard(double forward, double right, double up, bool shiftPressed = false)
    {
        var panSpeed = _options.PanSensitivity * Distance * (shiftPressed ? _options.PanSpeedMultiplier : 1.0);
        var camMatrix = CameraMatrix;
        var rightVec = new Vector3(camMatrix.M11, camMatrix.M12, camMatrix.M13);
        var upVec = new Vector3(camMatrix.M21, camMatrix.M22, camMatrix.M23);
        var forwardVec = new Vector3(camMatrix.M31, camMatrix.M32, camMatrix.M33);

        Target += rightVec * (float)(right * panSpeed * 5.0);
        Target += upVec * (float)(up * panSpeed * 5.0);
        Target += forwardVec * (float)(-forward * panSpeed * 5.0);
        updateCamera = true;
    }
    #endregion

    #region Matrix Operations
    private bool updateCamera = true;
    /// <summary>
    /// Gets the view matrix for rendering.
    /// </summary>
    public Matrix4x4 ViewMatrix
    {
        get
        {
            if (updateCamera) UpdateMatrices();
            return viewMatrix;
        }
    }
    private Matrix4x4 viewMatrix;

    /// <summary>
    /// Gets the camera matrix.
    /// </summary>
    public Matrix4x4 CameraMatrix
    {
        get
        {
            if (updateCamera) UpdateMatrices();
            return cameraMatrix;
        }
    }
    private Matrix4x4 cameraMatrix;

    /// <summary>
    /// Gets the camera position in world space.
    /// </summary>
    public Vector3 Position
    {
        get
        {
            if (updateCamera) UpdateMatrices();
            return position;
        }
    }
    private Vector3 position;

    /// <summary>
    /// Converts the view matrix to a float array for JavaScript interoperability.
    /// </summary>
    /// <returns>A float array representing the view matrix.</returns>
    public float[] ConvertMatrixToJavaScript()
    {
        var m = ViewMatrix;
        return
        [
            m.M11, m.M12, m.M13, m.M14,
            m.M21, m.M22, m.M23, m.M24,
            m.M31, m.M32, m.M33, m.M34,
            m.M41, m.M42, m.M43, m.M44
        ];
    }

    /// <summary>
    /// Updates the camera and view matrices based on the current camera state.
    /// </summary>
    private void UpdateMatrices()
    {
        if (_options.ZIsUp)
            cameraMatrix = Matrix4x4.CreateTranslation(0, 0, (float)Distance) *
                           Matrix4x4.CreateRotationZ(0.5f * MathF.PI) *
                           Matrix4x4.CreateRotationY(0.5f * MathF.PI - (float)PolarAngle) *
                           Matrix4x4.CreateRotationZ(-(float)AzimuthAngle) *
                           Matrix4x4.CreateTranslation(Target);
        else // Y is up
            cameraMatrix = Matrix4x4.CreateTranslation(0, 0, (float)Distance) *
                           Matrix4x4.CreateRotationX(-(float)PolarAngle) *
                           Matrix4x4.CreateRotationY(-(float)AzimuthAngle) *
                           Matrix4x4.CreateTranslation(Target);

        Matrix4x4.Invert(cameraMatrix, out viewMatrix);
        position = new Vector3(cameraMatrix.M41, cameraMatrix.M42, cameraMatrix.M43);
        updateCamera = false;
    }

    /// <summary>
    /// Resets the camera to a default position based on the bounding sphere of the object.
    /// </summary>
    /// <param name="objectSphere">The bounding sphere of the object.</param>
    internal void Reset(Sphere objectSphere)
    {
        Target = objectSphere.Center;
        AzimuthAngle = Math.PI / 4;
        PolarAngle = Math.PI / 6;

        var radius = (1 + _options.AutoCameraSphereBuffer) * objectSphere.GetRadius();
        var angleAtCamera = Math.PI * _options.Fov / 360;
        Distance = radius / Math.Sin(angleAtCamera);
        _options.ZFar = Distance + 50 * radius;
        _options.ZNear = Math.Max(0.0001, 0.001 * radius);
    }

    /// <summary>
    /// Sets the camera to a view from a specified cardinal direction.
    /// </summary>
    /// <param name="direction">The cardinal direction to view from.</param>
    public void SetCardinalView(CardinalDirection direction)
    {
        switch (direction)
        {
            case CardinalDirection.PositiveX:
                AzimuthAngle = Math.PI / 2;
                PolarAngle = 0;
                break;
            case CardinalDirection.NegativeX:
                AzimuthAngle = -Math.PI / 2;
                PolarAngle = 0;
                break;
            case CardinalDirection.PositiveY:
                AzimuthAngle = 0;
                PolarAngle = _options.ZIsUp ? 0 : Math.PI / 2;
                break;
            case CardinalDirection.NegativeY:
                AzimuthAngle = _options.ZIsUp ? Math.PI : 0;
                PolarAngle = _options.ZIsUp ? 0 : -Math.PI / 2;
                break;
            case CardinalDirection.PositiveZ:
                AzimuthAngle = 0;
                PolarAngle = _options.ZIsUp ? Math.PI / 2 : 0;
                break;
            case CardinalDirection.NegativeZ:
                AzimuthAngle = _options.ZIsUp ? 0 : Math.PI;
                PolarAngle = _options.ZIsUp ? -Math.PI / 2 : 0;
                break;
        }
    }
    #endregion

    #region Selection Ray
    /// <summary>
    /// Creates a ray from the camera through a specified screen point.
    /// </summary>
    /// <param name="screenX">The X-coordinate on the screen.</param>
    /// <param name="screenY">The Y-coordinate on the screen.</param>
    /// <param name="screenWidth">The width of the screen.</param>
    /// <param name="screenHeight">The height of the screen.</param>
    /// <returns>A tuple containing the origin and direction of the ray.</returns>
    public (Vector3, Vector3) CreateRayFromScreenPoint(double screenX, double screenY, double screenWidth, double screenHeight)
    {
        double ndcX = (2.0 * screenX / screenWidth) - 1.0;
        double ndcY = 1.0 - (2.0 * screenY / screenHeight);

        //var nearPointNDC = new Vector4((float)ndcX, (float)ndcY, -1.0f, 1.0f);
        var farPointNDC = new Vector4((float)ndcX, (float)ndcY, 1.0f, 1.0f);

        Matrix4x4 projectionMatrix = CreateProjectionMatrix(screenWidth, screenHeight);
        Matrix4x4 viewProjectionMatrix = Matrix4x4.Multiply(ViewMatrix, projectionMatrix);

        Matrix4x4.Invert(viewProjectionMatrix, out Matrix4x4 inverseViewProjection);

        //var nearPointWorld = Vector4.Transform(nearPointNDC, inverseViewProjection);
        var farPointWorld = Vector4.Transform(farPointNDC, inverseViewProjection);

        //nearPointWorld /= nearPointWorld.W;
        farPointWorld /= farPointWorld.W;

        Vector3 origin = Position;
        var target = new Vector3(farPointWorld.X, farPointWorld.Y, farPointWorld.Z);
        var direction = Vector3.Normalize(target - origin);

        return (origin, direction);
    }

    /// <summary>
    /// Creates the projection matrix based on the current camera settings.
    /// </summary>
    /// <param name="screenWidth">The width of the screen.</param>
    /// <param name="screenHeight">The height of the screen.</param>
    /// <returns>The projection matrix.</returns>
    public Matrix4x4 CreateProjectionMatrix(double screenWidth, double screenHeight)
    {
        float aspectRatio = (float)(screenWidth / screenHeight);
        if (_options.IsProjectionCamera)
            return Matrix4x4.CreatePerspectiveFieldOfView((float)(Math.PI * _options.Fov / 180),
                aspectRatio, (float)_options.ZNear, (float)_options.ZFar);
        else
            return Matrix4x4.CreateOrthographic((float)_options.OrthoSize * 2 * aspectRatio,
                (float)_options.OrthoSize * 2, (float)_options.ZNear, (float)_options.ZFar);
    }

    /// <summary>
    /// Converts the projection matrix to a float array for JavaScript interoperability.
    /// </summary>
    /// <param name="screenWidth">The width of the screen.</param>
    /// <param name="screenHeight">The height of the screen.</param>
    /// <returns>A float array representing the projection matrix.</returns>
    public float[] ConvertProjectionMatrixToJavaScript(double screenWidth, double screenHeight)
    {
        var m = CreateProjectionMatrix(screenWidth, screenHeight);
        return
        [
            m.M11, m.M12, m.M13, m.M14,
            m.M21, m.M22, m.M23, m.M24,
            m.M31, m.M32, m.M33, m.M34,
            m.M41, m.M42, m.M43, m.M44
        ];
    }

    /// <summary>
    /// Swaps the camera's up direction between the Y-axis and Z-axis.
    /// </summary>
    internal void SwapCameraUp()
    {
        var pos = Position - Target;
        double newPolar, newAzimuth;

        if (_options.ZIsUp) // Swapping from Y-up to Z-up
        {
            newPolar = Math.Asin(pos.Y / (float)Distance);
            newAzimuth = Math.Atan2(-pos.X, pos.Z);
        }
        else // Swapping from Z-up to Y-up
        {
            newPolar = Math.Asin(pos.Z / (float)Distance);
            newAzimuth = Math.Atan2(-pos.Y, pos.X);
        }
        PolarAngle = newPolar;
        AzimuthAngle = newAzimuth;
    }
    #endregion
}
