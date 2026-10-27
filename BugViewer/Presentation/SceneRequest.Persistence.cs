using System.Text.Json;
using System.Text.Json.Serialization;

namespace BugViewer;

public partial record SceneRequest
{
    /// <summary>Writes UTF-8 JSON at the stream's current position, leaving the stream open.</summary>
    public void Save(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Validate();
        JsonSerializer.Serialize(stream, this, SceneRequestJsonContext.Default.SceneRequest);
    }

    /// <summary>Writes UTF-8 JSON asynchronously, leaving the stream open.</summary>
    public async Task SaveAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Validate();
        await JsonSerializer.SerializeAsync(stream, this, SceneRequestJsonContext.Default.SceneRequest, cancellationToken);
    }

    /// <summary>Saves a local file, replacing an existing file only after serialization succeeds.</summary>
    public void Save(string path)
    {
        var destination = Path.GetFullPath(path);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = File.Create(temporary)) Save(stream);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    /// <summary>Saves a local file asynchronously. In a browser, use the stream overload instead.</summary>
    public async Task SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        var destination = Path.GetFullPath(path);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 65536, FileOptions.Asynchronous))
                await SaveAsync(stream, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    /// <summary>Reads and validates a scene at the stream's current position, leaving it open.</summary>
    public static SceneRequest Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var scene = JsonSerializer.Deserialize(stream, SceneRequestJsonContext.Default.SceneRequest)
            ?? throw new InvalidDataException("The file does not contain a scene.");
        scene.Validate();
        return scene;
    }

    /// <summary>Reads and validates a scene asynchronously, leaving the stream open.</summary>
    public static async Task<SceneRequest> LoadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var scene = await JsonSerializer.DeserializeAsync(stream, SceneRequestJsonContext.Default.SceneRequest, cancellationToken)
            ?? throw new InvalidDataException("The file does not contain a scene.");
        await scene.ValidateAsync(cancellationToken);
        return scene;
    }

    /// <summary>Reads a scene from a local file.</summary>
    public static SceneRequest Load(string path)
    {
        using var stream = File.OpenRead(path);
        return Load(stream);
    }

    /// <summary>Reads a local file asynchronously. In a browser, use the stream overload instead.</summary>
    public static async Task<SceneRequest> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await LoadAsync(stream, cancellationToken);
    }

}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    IncludeFields = true, IgnoreReadOnlyProperties = true, UseStringEnumConverter = true,
    DefaultBufferSize = 262144,
    Converters = [typeof(SceneFloat3Converter), typeof(SceneInt3Converter), typeof(SceneColorConverter), typeof(ScenePointConverter)])]
[JsonSerializable(typeof(SceneRequest))]
internal partial class SceneRequestJsonContext : JsonSerializerContext;
