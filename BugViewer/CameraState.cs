using System.Numerics;

namespace BugViewer;

/// <summary>A serializable snapshot of the viewer camera used to synchronize multiple viewers.</summary>
public readonly record struct CameraState(
    Vector3 Target,
    double AzimuthAngle,
    double PolarAngle,
    double Distance,
    double OrthographicSize,
    bool IsPerspective);
