using System.IO;
using System.Text.Json;

namespace Petrichor.Media.Playback;

public sealed class EqualizerProfileJsonStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string storageFilePath;

    public EqualizerProfileJsonStore(string storageFilePath)
    {
        this.storageFilePath = storageFilePath;
    }

    public async Task SaveAsync(EqualizerProfile profile, CancellationToken cancellationToken = default)
    {
        var directoryPath = Path.GetDirectoryName(storageFilePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        await using var stream = File.Create(storageFilePath);
        await JsonSerializer.SerializeAsync(stream, profile, SerializerOptions, cancellationToken);
    }

    public async Task<EqualizerProfile?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(storageFilePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(storageFilePath);
        return await JsonSerializer.DeserializeAsync<EqualizerProfile>(stream, SerializerOptions, cancellationToken);
    }
}
