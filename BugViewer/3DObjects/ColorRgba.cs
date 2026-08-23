using System.ComponentModel;
using System.Globalization;

namespace BugViewer;

/// <summary>
/// A portable 8-bit RGBA color used by BugViewer rendering data.
/// </summary>
[TypeConverter(typeof(ColorRgbaTypeConverter))]
public readonly record struct ColorRgba(byte R, byte G, byte B, byte A = byte.MaxValue)
{
    public static readonly ColorRgba White = new(byte.MaxValue, byte.MaxValue, byte.MaxValue);

    public static string ToHtml(ColorRgba c)
    {
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }
    internal static IEnumerable<float> ToJavaScript(ColorRgba c, double transparency)
    {
        yield return c.R / 255f;
        yield return c.G / 255f;
        yield return c.B / 255f;
        yield return (float)transparency;
    }
    internal static IEnumerable<float> ToJavaScript(ColorRgba c)
    {
        yield return c.R / 255f;
        yield return c.G / 255f;
        yield return c.B / 255f;
        yield return c.A / 255f;
    }
}

public sealed class ColorRgbaTypeConverter : TypeConverter
{
    public override bool CanConvertFrom(
        ITypeDescriptorContext? context,
        Type sourceType)
    {
        return sourceType == typeof(string)
            || base.CanConvertFrom(context, sourceType);
    }

    public override object? ConvertFrom(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object value)
    {
        if (value is string text)
        {
            text = text.Trim();

            if (text.StartsWith('#'))
            {
                text = text[1..];
            }

            if (text.Length == 6 &&
                byte.TryParse(text[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) &&
                byte.TryParse(text[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) &&
                byte.TryParse(text[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
            {
                return new ColorRgba(r, g, b);
            }
        }

        return base.ConvertFrom(context, culture, value);
    }

    public override bool CanConvertTo(
        ITypeDescriptorContext? context,
        Type? destinationType)
    {
        return destinationType == typeof(string)
            || base.CanConvertTo(context, destinationType);
    }

    public override object? ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        if (destinationType == typeof(string) && value is ColorRgba color)
        {
            return ColorRgba.ToHtml(color);
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }
}