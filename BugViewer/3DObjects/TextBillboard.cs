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
    public float Scale { get; init; } = 1f;
    public Vector3 Anchor  => ((List<Vector3>)Vertices)[0];
    private float _relativeX = 0.5f;
    private float _relativeY = 0.5f;

    /// <summary>
    /// Gets the horizontal location on the billboard that is placed at <see cref="Anchor"/>.
    /// A value of 0 is the left edge, 0.5 is the center, and 1 is the right edge.
    /// </summary>
    public float RelativeX
    {
        get => _relativeX;
        init => _relativeX = Math.Clamp(value, 0f, 1f);
    }

    /// <summary>
    /// Gets the vertical location on the billboard that is placed at <see cref="Anchor"/>.
    /// A value of 0 is the bottom edge, 0.5 is the center, and 1 is the top edge.
    /// </summary>
    public float RelativeY
    {
        get => _relativeY;
        init => _relativeY = Math.Clamp(value, 0f, 1f);
    }

    internal override object CreateJavascriptData()
    {
        return new
        {
            id = Id,
            text = Text,
            position = new[] { Anchor.X, Anchor.Y, Anchor.Z },
            backgroundColor = ColorRgba.ToJavaScript(BackgroundColor).ToArray(),
            textColor = ColorRgba.ToJavaScript(TextColor).ToArray(),
            scale = Scale,
            relativeX = RelativeX,
            relativeY = RelativeY
        };
    }
}
