namespace BugViewer;

/// <summary>
/// A portable 8-bit RGBA color used by BugViewer rendering data.
/// </summary>
public readonly record struct ColorRgba(byte R, byte G, byte B, byte A = byte.MaxValue)
{
    public static readonly ColorRgba White = new(byte.MaxValue, byte.MaxValue, byte.MaxValue);
}
