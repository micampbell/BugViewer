using System.Text.Json;
using System.Text.Json.Serialization;

namespace BugViewer;

// The version-1 wire format stays unchanged. These hot values occur millions of
// times in saved scenes; read them directly instead of constructing general array
// and parameterized-object deserialization state for each triangle/vertex/color.
internal sealed class SceneFloat3Converter : JsonConverter<float[]>
{
    public override float[] Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected an XYZ coordinate array.");
        var result = new float[3];
        for (var i = 0; i < 3; i++)
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.Number) throw new JsonException("Coordinates must contain three numbers.");
            result[i] = reader.GetSingle();
        }
        if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray) throw new JsonException("Coordinates must contain three numbers.");
        return result;
    }
    public override void Write(Utf8JsonWriter writer, float[] value, JsonSerializerOptions options)
    {
        Span<byte> buf = stackalloc byte[64];
        int pos = 0;
        buf[pos++] = (byte)'[';
        System.Buffers.Text.Utf8Formatter.TryFormat(value[0], buf.Slice(pos), out int w1); pos += w1;
        buf[pos++] = (byte)',';
        System.Buffers.Text.Utf8Formatter.TryFormat(value[1], buf.Slice(pos), out int w2); pos += w2;
        buf[pos++] = (byte)',';
        System.Buffers.Text.Utf8Formatter.TryFormat(value[2], buf.Slice(pos), out int w3); pos += w3;
        buf[pos++] = (byte)']';
        writer.WriteRawValue(buf.Slice(0, pos), skipInputValidation: true);
    }
}

internal sealed class SceneFloat3ListConverter : JsonConverter<List<float[]>>
{
    public override List<float[]> Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected an array of float3.");
        var result = new List<float[]>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray) return result;
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected an XYZ coordinate array.");
            var item = new float[3];
            for (var i = 0; i < 3; i++)
            {
                if (!reader.Read() || reader.TokenType != JsonTokenType.Number) throw new JsonException("Coordinates must contain three numbers.");
                item[i] = reader.GetSingle();
            }
            if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray) throw new JsonException("Coordinates must contain three numbers.");
            result.Add(item);
        }
        throw new JsonException("Incomplete float3 list.");
    }

    public override void Write(Utf8JsonWriter writer, List<float[]> list, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        if (list.Count > 0)
        {
            byte[] chunk = new byte[65536];
            int pos = 0;
            for (var i = 0; i < list.Count; i++)
            {
                if (pos > chunk.Length - 80)
                {
                    writer.WriteRawValue(chunk.AsSpan(0, pos), skipInputValidation: true);
                    pos = 0;
                }
                if (pos > 0) chunk[pos++] = (byte)',';
                chunk[pos++] = (byte)'[';
                var item = list[i];
                System.Buffers.Text.Utf8Formatter.TryFormat(item[0], chunk.AsSpan(pos), out int w1); pos += w1;
                chunk[pos++] = (byte)',';
                System.Buffers.Text.Utf8Formatter.TryFormat(item[1], chunk.AsSpan(pos), out int w2); pos += w2;
                chunk[pos++] = (byte)',';
                System.Buffers.Text.Utf8Formatter.TryFormat(item[2], chunk.AsSpan(pos), out int w3); pos += w3;
                chunk[pos++] = (byte)']';
            }
            if (pos > 0)
                writer.WriteRawValue(chunk.AsSpan(0, pos), skipInputValidation: true);
        }
        writer.WriteEndArray();
    }
}

internal sealed class SceneInt3Converter : JsonConverter<int[]>
{
    public override int[] Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected a triangle index array.");
        var result = new int[3];
        for (var i = 0; i < 3; i++)
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.Number) throw new JsonException("Triangles must contain three indices.");
            result[i] = reader.GetInt32();
        }
        if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray) throw new JsonException("Triangles must contain three indices.");
        return result;
    }
    public override void Write(Utf8JsonWriter writer, int[] value, JsonSerializerOptions options)
    {
        Span<byte> buf = stackalloc byte[48];
        int pos = 0;
        buf[pos++] = (byte)'[';
        System.Buffers.Text.Utf8Formatter.TryFormat(value[0], buf.Slice(pos), out int w1); pos += w1;
        buf[pos++] = (byte)',';
        System.Buffers.Text.Utf8Formatter.TryFormat(value[1], buf.Slice(pos), out int w2); pos += w2;
        buf[pos++] = (byte)',';
        System.Buffers.Text.Utf8Formatter.TryFormat(value[2], buf.Slice(pos), out int w3); pos += w3;
        buf[pos++] = (byte)']';
        writer.WriteRawValue(buf.Slice(0, pos), skipInputValidation: true);
    }
}

internal sealed class SceneInt3ListConverter : JsonConverter<List<int[]>>
{
    public override List<int[]> Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected an array of int3.");
        var result = new List<int[]>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray) return result;
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected a triangle index array.");
            var item = new int[3];
            for (var i = 0; i < 3; i++)
            {
                if (!reader.Read() || reader.TokenType != JsonTokenType.Number) throw new JsonException("Triangles must contain three indices.");
                item[i] = reader.GetInt32();
            }
            if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray) throw new JsonException("Triangles must contain three indices.");
            result.Add(item);
        }
        throw new JsonException("Incomplete int3 list.");
    }

    public override void Write(Utf8JsonWriter writer, List<int[]> list, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        if (list.Count > 0)
        {
            byte[] chunk = new byte[65536];
            int pos = 0;
            for (var i = 0; i < list.Count; i++)
            {
                if (pos > chunk.Length - 50)
                {
                    writer.WriteRawValue(chunk.AsSpan(0, pos), skipInputValidation: true);
                    pos = 0;
                }
                if (pos > 0) chunk[pos++] = (byte)',';
                chunk[pos++] = (byte)'[';
                var item = list[i];
                System.Buffers.Text.Utf8Formatter.TryFormat(item[0], chunk.AsSpan(pos), out int w1); pos += w1;
                chunk[pos++] = (byte)',';
                System.Buffers.Text.Utf8Formatter.TryFormat(item[1], chunk.AsSpan(pos), out int w2); pos += w2;
                chunk[pos++] = (byte)',';
                System.Buffers.Text.Utf8Formatter.TryFormat(item[2], chunk.AsSpan(pos), out int w3); pos += w3;
                chunk[pos++] = (byte)']';
            }
            if (pos > 0)
                writer.WriteRawValue(chunk.AsSpan(0, pos), skipInputValidation: true);
        }
        writer.WriteEndArray();
    }
}

internal sealed class SceneColorConverter : JsonConverter<ColorRgba>
{
    public override ColorRgba Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Expected an RGBA color.");
        byte r = 0, g = 0, b = 0, a = 255;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return new(r, g, b, a);
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("Expected a color channel.");
            var channel = reader.ValueTextEquals("r"u8) ? 0 : reader.ValueTextEquals("g"u8) ? 1
                : reader.ValueTextEquals("b"u8) ? 2 : reader.ValueTextEquals("a"u8) ? 3 : -1;
            if (!reader.Read()) throw new JsonException("Missing color channel value.");
            switch (channel)
            {
                case 0: r = reader.GetByte(); break;
                case 1: g = reader.GetByte(); break;
                case 2: b = reader.GetByte(); break;
                case 3: a = reader.GetByte(); break;
                default: reader.Skip(); break;
            }
        }
        throw new JsonException("Incomplete color.");
    }
    public override void Write(Utf8JsonWriter writer, ColorRgba value, JsonSerializerOptions options)
    {
        writer.WriteStartObject(); writer.WriteNumber("r"u8, value.R); writer.WriteNumber("g"u8, value.G);
        writer.WriteNumber("b"u8, value.B); writer.WriteNumber("a"u8, value.A); writer.WriteEndObject();
    }
}

internal sealed class ScenePointConverter : JsonConverter<ScenePoint>
{
    public override ScenePoint Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Expected a source point.");
        double x = 0, y = 0, z = 0;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return new(x, y, z);
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("Expected a coordinate name.");
            var coordinate = reader.ValueTextEquals("x"u8) ? 0 : reader.ValueTextEquals("y"u8) ? 1 : reader.ValueTextEquals("z"u8) ? 2 : -1;
            if (!reader.Read()) throw new JsonException("Missing coordinate value.");
            switch (coordinate)
            {
                case 0: x = reader.GetDouble(); break;
                case 1: y = reader.GetDouble(); break;
                case 2: z = reader.GetDouble(); break;
                default: reader.Skip(); break;
            }
        }
        throw new JsonException("Incomplete source point.");
    }
    public override void Write(Utf8JsonWriter writer, ScenePoint value, JsonSerializerOptions options)
    {
        writer.WriteStartObject(); writer.WriteNumber("x"u8, value.X); writer.WriteNumber("y"u8, value.Y);
        writer.WriteNumber("z"u8, value.Z); writer.WriteEndObject();
    }
}
