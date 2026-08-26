using System.Numerics;

namespace BugViewer;

/// <summary>
/// Represents 3D lines with variable thickness and color for WebGPU rendering.
/// </summary>
public record TextBillboard : AbstractObject3D
{
    public required string Text { get; init; }
    public required ColorRgba BackgroundColor { get; init; }
    public required ColorRgba TextColor { get; init; }
    /// <summary>
    /// Gets the billboard half-height in world-space units.
    /// </summary>
    public float Scale { get; init; } = 0.5f;
    public Vector3 Center  => ((List<Vector3>)Vertices)[0];
    
    internal override object CreateJavascriptData()
    {
        return new
        {
            id = Id,
            text = Text,
            position = new[] { Center.X, Center.Y, Center.Z },
            backgroundColor = ColorRgba.ToJavaScript(BackgroundColor).ToArray(),
            textColor = ColorRgba.ToJavaScript(TextColor).ToArray(),
            scale = Scale
        };
    }
}
