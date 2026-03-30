using System.IO;
using System.Text.Json;

namespace Petrichor.Media.Playback;

public sealed class NamedEqualizerPresetJsonStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string storageFilePath;

    public NamedEqualizerPresetJsonStore(string storageFilePath)
    {
        this.storageFilePath = storageFilePath;
    }

    public async Task<IReadOnlyList<NamedEqualizerPreset>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(storageFilePath))
        {
            return Array.Empty<NamedEqualizerPreset>();
        }

        await using var stream = File.OpenRead(storageFilePath);
        return await JsonSerializer.DeserializeAsync<IReadOnlyList<NamedEqualizerPreset>>(stream, SerializerOptions, cancellationToken)
            ?? Array.Empty<NamedEqualizerPreset>();
    }

    public async Task SaveAsync(IReadOnlyList<NamedEqualizerPreset> presets, CancellationToken cancellationToken = default)
    {
        var directoryPath = Path.GetDirectoryName(storageFilePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        await using var stream = File.Create(storageFilePath);
        await JsonSerializer.SerializeAsync(stream, presets, SerializerOptions, cancellationToken);
    }
}
