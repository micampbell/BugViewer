using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace BugViewer;

/// <summary>
/// Options passed from C# to JavaScript for WebGPU grid rendering.
/// Matches the shape expected by webgpu-canvas.js initGridDemo/updateDisplayOptions.
/// </summary>
public class BugViewerOptions : INotifyPropertyChanged
{
    /// <summary>
    /// Event raised when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>DefaultLight configuration with sensible values for a basic grid.</summary>
    public static BugViewerOptions DefaultLight = new()
    {
        LightPolarAngle = 0.13 * Math.PI,
        LightAzimuthAngle = 0.33 * Math.PI,
        AmbientLight = 0.3,
        SpecularPower = 32.0,
        AutoResetCamera = UpdateTypes.SphereChange,
        AutoCameraSphereBuffer = 0.2,
        AutoUpdateGrid = UpdateTypes.SphereChange,
        AutoGridBuffer = 3.0,
        IsDarkTheme = false,
        ClearColor = "#f2f2ff",
        LineColor = "#d2d2d2",
        LineTransparency = 0.8f,
        BaseColor = "#000000",
        BaseTransparency = 0f,
        LineWidthX = 0.1,
        LineWidthY = 0.1,
        SampleCount = 4,
        IsProjectionCamera = true,
        Fov = 20,
        OrthoSize = 5.0,
        ZNear = 0.001,
        ZFar = 999,
        ZIsUp = true,
        GridSize = 100.0,
        GridSpacing = 5.0,
        ConstrainPolar = true,
        MaxPolar = Math.PI * 0.49,
        MinPolar = -Math.PI * 0.49,
        ConstrainAzimuth = false,
        MaxAzimuth = 0,
        MinAzimuth = 0,
        MaxDistance = 9999.0,
        MinDistance = 0.5,
        ConstrainDistance = true,
        OrbitSensitivity = 0.01,
        ZoomSensitivity = 0.005,
        PanSensitivity = 0.005,
        PanSpeedMultiplier = 3.0,
        CoordinateThickness = 1
    };
    /// <summary>DefaultLight configuration with sensible values for a basic grid.</summary>
    public static BugViewerOptions DefaultDark = new()
    {
        LightPolarAngle = 0.13 * Math.PI,
        LightAzimuthAngle = 0.33 * Math.PI,
        AmbientLight = 0.3,
        SpecularPower = 32.0,
        AutoResetCamera = UpdateTypes.SphereChange,
        AutoCameraSphereBuffer = 0.2,
        AutoUpdateGrid = UpdateTypes.SphereChange,
        AutoGridBuffer = 3.0,
        IsDarkTheme = true,
        ClearColor = "#202020",
        LineColor = "#d2d2d2",
        LineTransparency = 0.8f,
        BaseColor = "#000000",
        BaseTransparency = 0f,
        LineWidthX = 0.1,
        LineWidthY = 0.1,
        SampleCount = 4,
        IsProjectionCamera = true,
        Fov = 20,
        OrthoSize = 5.0,
        ZNear = 0.001,
        ZFar = 999,
        ZIsUp = true,
        GridSize = 100.0,
        GridSpacing = 5.0,
        ConstrainPolar = true,
        MaxPolar = Math.PI * 0.49,
        MinPolar = -Math.PI * 0.49,
        ConstrainAzimuth = false,
        MaxAzimuth = 0,
        MinAzimuth = 0,
        MaxDistance = 9999.0,
        MinDistance = 0.5,
        ConstrainDistance = true,
        OrbitSensitivity = 0.01,
        ZoomSensitivity = 0.005,
        PanSensitivity = 0.005,
        PanSpeedMultiplier = 3.0,
        CoordinateThickness = 1
    };
    public void ResetToDefault(bool isDarkTheme)
    {
        if (isDarkTheme)
            Set(DefaultDark);
        else Set(DefaultLight);
    }

    private void Set(BugViewerOptions newOptions)
    {
        AutoResetCamera = newOptions.AutoResetCamera;
        AutoUpdateGrid = newOptions.AutoUpdateGrid;
        AutoCameraSphereBuffer = newOptions.AutoCameraSphereBuffer;
        AutoGridBuffer = newOptions.AutoGridBuffer;
        IsDarkTheme = newOptions.IsDarkTheme;
        ClearColor = newOptions.ClearColor;
        LineColor = newOptions.LineColor;
        LineTransparency = newOptions.LineTransparency;
        BaseColor = newOptions.BaseColor;
        BaseTransparency = newOptions.BaseTransparency;
        LineWidthX = newOptions.LineWidthX;
        LineWidthY = newOptions.LineWidthY;
        SampleCount = newOptions.SampleCount;
        IsProjectionCamera = newOptions.IsProjectionCamera;
        Fov = newOptions.Fov;
        OrthoSize = newOptions.OrthoSize;
        ZNear = newOptions.ZNear;
        ZFar = newOptions.ZFar;
        MaxPolar = newOptions.MaxPolar;
        MinPolar = newOptions.MinPolar;
        MaxAzimuth = newOptions.MaxAzimuth;
        MinAzimuth = newOptions.MinAzimuth;
        ConstrainPolar = newOptions.ConstrainPolar;
        ConstrainAzimuth = newOptions.ConstrainAzimuth;
        MaxDistance = newOptions.MaxDistance;
        MinDistance = newOptions.MinDistance;
        ConstrainDistance = newOptions.ConstrainDistance;
        OrbitSensitivity = newOptions.OrbitSensitivity;
        ZoomSensitivity = newOptions.ZoomSensitivity;
        PanSensitivity = newOptions.PanSensitivity;
        PanSpeedMultiplier = newOptions.PanSpeedMultiplier;
        GridSize = newOptions.GridSize;
        GridSpacing = newOptions.GridSpacing;
        CoordinateThickness = newOptions.CoordinateThickness;
        LightPolarAngle = newOptions.LightPolarAngle;
        LightAzimuthAngle = newOptions.LightAzimuthAngle;
        AmbientLight = newOptions.AmbientLight;
        SpecularPower = newOptions.SpecularPower;
        ZIsUp = newOptions.ZIsUp;
    }

    private UpdateTypes _autoResetCamera;
    public UpdateTypes AutoResetCamera
    {
        get => _autoResetCamera;
        set
        {
            if (_autoResetCamera != value)
            {
                _autoResetCamera = value;
                OnPropertyChanged();
            }
        }
    }

    private UpdateTypes _autoUpdateGrid;
    public UpdateTypes AutoUpdateGrid
    {
        get => _autoUpdateGrid;
        set
        {
            if (_autoUpdateGrid != value)
            {
                _autoUpdateGrid = value;
                OnPropertyChanged();
            }
        }
    }


    private double _autoGridBuffer;
    /// <summary>
    /// The amount that the radius of the bounding sphere is increased 
    /// when automatically positioning the camera to view an object.
    /// </summary>
    public double AutoGridBuffer
    {
        get => _autoGridBuffer;
        set
        {
            if (ChangeOccurred(_autoGridBuffer, value))
            {
                _autoGridBuffer = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _isDarkTheme = true;
    /// <summary>
    /// Gets or sets a value indicating whether the application is using a light theme.
    /// </summary>
    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set
        {
            if (_isDarkTheme != value)
            {
                _isDarkTheme = value;
                OnPropertyChanged();
            }
        }
    }

    private string _clearColor;
    /// <summary>Background clear color for the rendering canvas.</summary>
    public string ClearColor
    {
        get => _clearColor;
        set
        {
            if (_clearColor != value)
            {
                _clearColor = value;
                OnPropertyChanged();
            }
        }
    }

    private double _autoCameraSphereBuffer;
    /// <summary>
    /// The amount that the radius of the bounding sphere is increased 
    /// when automatically positioning the camera to view an object.
    /// </summary>
    public double AutoCameraSphereBuffer
    {
        get => _autoCameraSphereBuffer;
        set
        {
            if (ChangeOccurred(_autoCameraSphereBuffer, value))
            {
                _autoCameraSphereBuffer = value;
                OnPropertyChanged();
            }
        }
    }

    private double _lineTransparency = 1.0;
    public double LineTransparency
    {
        get => _lineTransparency;
        set
        {
            if (ChangeOccurred(_lineTransparency, value))
            {
                _lineTransparency = value;
                OnPropertyChanged();
            }
        }
    }
    private string _lineColor;
    /// <summary>Color of grid lines.</summary>
    public string LineColor
    {
        get => _lineColor;
        set
        {
            if (_lineColor != value)
            {
                _lineColor = value;
                OnPropertyChanged();
            }
        }
    }


    private double _baseTransparency = 1.0;
    public double BaseTransparency
    {
        get => _baseTransparency;
        set
        {
            if (ChangeOccurred(_baseTransparency, value))
            {
                _baseTransparency = value;
                OnPropertyChanged();
            }
        }
    }

    private string _baseColor;
    /// <summary>Base/background color of the grid.</summary>
    public string BaseColor
    {
        get => _baseColor;
        set
        {
            if (_baseColor != value)
            {
                _baseColor = value;
                OnPropertyChanged();
            }
        }
    }

    private double _lineWidthX;
    private double _lineWidthY;

    /// <summary>Grid line width in X direction (0.0 to 1.0).</summary>
    public double LineWidthX
    {
        get => _lineWidthX;
        set
        {
            var clamp = Math.Clamp(value, 0.0, 1.0);
            if (ChangeOccurred(_lineWidthX, clamp))
            {
                _lineWidthX = clamp;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Grid line width in Y direction (0.0 to 1.0).</summary>
    public double LineWidthY
    {
        get => _lineWidthY;
        set
        {
            var clamp = Math.Clamp(value, 0.0, 1.0);
            if (ChangeOccurred(_lineWidthY, clamp))
            {
                _lineWidthY = clamp;
                OnPropertyChanged();
            }
        }
    }


    private double _gridSize;
    private double _gridSpacing;

    /// <summary>Grid size of the square.</summary>
    public double GridSize
    {
        get => _gridSize;
        set
        {
            if (ChangeOccurred(_gridSize, value))
            {
                _gridSize = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Grid spacing.</summary>
    public double GridSpacing
    {
        get => _gridSpacing;
        set
        {
            if (ChangeOccurred(_gridSpacing, value))
            {
                _gridSpacing = value;
                OnPropertyChanged();
            }
        }
    }

    private int _sampleCount;
    /// <summary>Multi-Sample Anti-Aliasing (MSAA) sample count for smoother rendering.</summary>
    public int SampleCount
    {
        get => _sampleCount; set
        {
            if (_sampleCount != value)
            {
                _sampleCount = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _isProjectionCamera;
    /// <summary>Camera projection type (Perspective or Orthographic).</summary>
    public bool IsProjectionCamera
    {
        get => _isProjectionCamera;
        set
        {
            if (_isProjectionCamera != value)
            {
                _isProjectionCamera = value;
                OnPropertyChanged();
            }
        }
    }

    private double _fov;
    /// <summary>Field of view in radians (used for Perspective projection).</summary>
    public double Fov
    {
        get => _fov;
        set
        {
            if (ChangeOccurred(_fov, value))
            {
                _fov = value;
                OnPropertyChanged();
            }
        }
    }

    private double _orthoSize;
    /// <summary>Half-height of view in world units (used for Orthographic projection).</summary>
    public double OrthoSize
    {
        get => _orthoSize;
        set
        {
            if (ChangeOccurred(value, _orthoSize))
            {
                _orthoSize = value;
                OnPropertyChanged();
            }
        }
    }

    internal void AdjustCameraProjectionParameters()
    {
        if (IsProjectionCamera) // switching to perspective
        {   // adjust the FOV so parts appear the same size
            Fov = Math.Clamp(Fov, 1.0, 120.0);
        }
        else // switching to orthographic
        {   // adjust the OrthoSize so parts appear the same size
            OrthoSize = Math.Max(OrthoSize, 0.1);
        }
    }

    private double _zNear;
    /// <summary>Near clipping plane distance.</summary>
    public double ZNear
    {
        get => _zNear;
        set
        {
            if (ChangeOccurred(_zNear, value))
            {
                _zNear = value;
                OnPropertyChanged();
            }
        }
    }
    private double _zFar;
    /// <summary>Far clipping plane distance.</summary>
    public double ZFar
    {
        get => _zFar;
        set
        {
            if (ChangeOccurred(_zFar, value))
            {
                _zFar = value;
                OnPropertyChanged();
            }
        }
    }

    // Orbit constraints
    private double _maxPolar;
    /// <summary>Maximum polar angle in radians (slightly less than 90° to avoid gimbal lock).</summary>
    public double MaxPolar
    {
        get => _maxPolar;
        set
        {
            if (ChangeOccurred(_maxPolar, value))
            {
                _maxPolar = value;
                OnPropertyChanged();
            }
        }
    }

    private double _minPolar;
    /// <summary>Minimum polar angle in radians.</summary>
    public double MinPolar
    {
        get => _minPolar;
        set
        {
            if (ChangeOccurred(_minPolar, value))
            {
                _minPolar = value;
                OnPropertyChanged();
            }
        }
    }

    private double _maxAzimuth;
    /// <summary>Maximum azimuth angle in radians.</summary>
    public double MaxAzimuth
    {
        get => _maxAzimuth;
        set
        {
            if (ChangeOccurred(_maxAzimuth, value))
            {
                _maxAzimuth = value;
                OnPropertyChanged();
            }
        }
    }

    private double _minAzimuth;
    /// <summary>Minimum azimuth angle in radians.</summary>
    public double MinAzimuth
    {
        get => _minAzimuth;
        set
        {
            if (ChangeOccurred(_minAzimuth, value))
            {
                _minAzimuth = value;
                OnPropertyChanged();
            }
        }
    }
    private bool _zIsUp;
    public bool ZIsUp
    {
        get => _zIsUp;
        set
        {
            if (_zIsUp != value)
            {
                _zIsUp = value;
                OnPropertyChanged();
            }
        }
    }


    private bool _constrainPolar;
    /// <summary>Whether to constrain the polar angle.</summary>
    public bool ConstrainPolar { get => _constrainPolar; set { _constrainPolar = value; OnPropertyChanged(); } }

    private bool _constrainAzimuth;
    /// <summary>Whether to constrain the azimuth angle.</summary>
    public bool ConstrainAzimuth { get => _constrainAzimuth; set { _constrainAzimuth = value; OnPropertyChanged(); } }

    // Distance constraints
    private double _maxDistance;
    /// <summary>Maximum camera distance from the target.</summary>
    public double MaxDistance
    {
        get => _maxDistance;
        set
        {
            if (ChangeOccurred(_maxDistance, value))
            {
                _maxDistance = value;
                OnPropertyChanged();
            }
        }
    }

    private double _minDistance;
    /// <summary>Minimum camera distance from the target.</summary>
    public double MinDistance
    {
        get => _minDistance;
        set
        {
            if (ChangeOccurred(_minDistance, value))
            {
                _minDistance = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _constrainDistance;
    /// <summary>Whether to constrain the camera distance.</summary>
    public bool ConstrainDistance { get => _constrainDistance; set { _constrainDistance = value; OnPropertyChanged(); } }

    // Sensitivity settings
    private double _orbitSensitivity;
    /// <summary>Sensitivity for orbit (rotation) controls.</summary>
    public double OrbitSensitivity
    {
        get => _orbitSensitivity;
        set
        {
            if (ChangeOccurred(_orbitSensitivity, value))
            {
                _orbitSensitivity = value;
                OnPropertyChanged();
            }
        }
    }

    private double _zoomSensitivity;
    /// <summary>Sensitivity for zoom controls (increased for better zoom response).</summary>
    public double ZoomSensitivity
    {
        get => _zoomSensitivity;
        set
        {
            if (ChangeOccurred(_zoomSensitivity, value))
            {
                _zoomSensitivity = value;
                OnPropertyChanged();
            }
        }
    }

    private double _panSensitivity;
    /// <summary>Sensitivity for pan controls.</summary>
    public double PanSensitivity
    {
        get => _panSensitivity;
        set
        {
            if (ChangeOccurred(_panSensitivity, value))
            {
                _panSensitivity = value;
                OnPropertyChanged();
            }
        }
    }

    private double _panSpeedMultiplier;
    /// <summary>Multiplier for pan speed when Shift is held.</summary>
    public double PanSpeedMultiplier
    {
        get => _panSpeedMultiplier;
        set
        {
            if (ChangeOccurred(_panSpeedMultiplier, value))
            {
                _panSpeedMultiplier = value;
                OnPropertyChanged();
            }
        }
    }

    private double _lightPolarAngle;
    /// <summary>Direction of the primary light source.</summary>
    public double LightPolarAngle
    {
        get => _lightPolarAngle;
        set
        {
            var clamp = Math.Clamp(value, 0.0, Math.PI);
            if (ChangeOccurred(_lightPolarAngle, clamp))
            {
                _lightPolarAngle = clamp;
                OnPropertyChanged();
            }
        }
    }
    private double _lightAzimuthAngle;
    /// <summary>Direction of the primary light source.</summary>
    public double LightAzimuthAngle
    {
        get => _lightAzimuthAngle;
        set
        {
            var clamp = Math.Clamp(value, 0.0, Math.Tau);
            if (ChangeOccurred(_lightAzimuthAngle, clamp))
            {
                _lightAzimuthAngle = clamp;
                OnPropertyChanged();
            }
        }
    }
    private float[] GetLightDirection()
    {
        var x = (float)(-Math.Sin(LightPolarAngle) * Math.Cos(LightAzimuthAngle));
        var y = (float)(-Math.Sin(LightPolarAngle) * Math.Sin(LightAzimuthAngle));
        var z = (float)(-Math.Cos(LightPolarAngle));

        if (ZIsUp)
            return [x, y, z];
        else return [z, x, y];
    }

    private double _ambientLight;
    /// <summary>Ambient light intensity (0.0 to 1.0).</summary>
    public double AmbientLight
    {
        get => _ambientLight;
        set
        {
            var clamp = Math.Clamp(value, 0.0, 1.0);
            if (ChangeOccurred(_ambientLight, clamp))
            {
                _ambientLight = clamp;
                OnPropertyChanged();
            }
        }
    }

    private double _specularPower;
    /// <summary>Shininess of the material (higher is sharper).</summary>
    public double SpecularPower
    {
        get => _specularPower;
        set
        {
            if (ChangeOccurred(_specularPower, value))
            {
                _specularPower = value;
                OnPropertyChanged();
            }
        }
    }


    private double _coordThick;
    /// <summary>Whether to show coordinate axes (X=red, Y=green, Z=blue).</summary>
    public double CoordinateThickness
    {
        get => _coordThick;
        set
        {
            if (ChangeOccurred(_coordThick, value))
            {
                _coordThick = value;
                OnPropertyChanged();
            }
        }
    }

    private bool ChangeOccurred(double v1, double v2)
    {
        return Math.Abs(v1 - v2) > 1e-9;
    }
    /// <summary>
    /// Converts this options object to a JS-friendly format with colors normalized to 0-1 floats.
    /// </summary>
    public object ToJavascriptOptions() => new
    {
        clearColor = ColorToJavaScript(ClearColor, 1).ToArray(),
        lineColor = ColorToJavaScript(LineColor, LineTransparency).ToArray(),
        baseColor = ColorToJavaScript(BaseColor, BaseTransparency).ToArray(),
        lineWidthX = (float)LineWidthX,
        lineWidthY = (float)LineWidthY,
        sampleCount = SampleCount,
        gridSize = (float)GridSize,
        gridSpacing = (float)GridSpacing,
        zIsUp = ZIsUp,
        coordinateThickness = CoordinateThickness,
        lightDir = GetLightDirection(),
        ambient = (float)AmbientLight,
        specularPower = (float)SpecularPower,
    };
    internal static IEnumerable<float> ColorToJavaScript(string c, double transparency)
    {
        c = c.Substring(1).Trim().ToLower();
        byte r = 0, g = 0, b = 0;
        r = Convert.ToByte(c[0..2], 16);
        g = Convert.ToByte(c[2..4], 16);
        b = Convert.ToByte(c[4..6], 16);
        yield return r / 255f;
        yield return g / 255f;
        yield return b / 255f;
        yield return (float)transparency;
    }
}