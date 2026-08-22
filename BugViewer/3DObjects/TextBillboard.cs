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
    public Vector3 Center  => ((List<Vector3>)Vertices)[0];
    
    internal override object CreateJavascriptData()
    {
        return new
        {
            id = Id,
            text = Text,
            position = new[] { Center.X, Center.Y, Center.Z },
            backgroundColor = ColorToJavaScript(BackgroundColor).ToArray(),
            textColor = ColorToJavaScript(TextColor).ToArray()
        };
    }
}
