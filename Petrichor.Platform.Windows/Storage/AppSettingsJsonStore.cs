using System.IO;
using System.Text.Json;

namespace Petrichor.Platform.Windows.Storage;

public sealed class AppSettingsJsonStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string storageFilePath;

    public AppSettingsJsonStore(string storageFilePath)
    {
        this.storageFilePath = storageFilePath;
    }

    public async Task<AppSettingsProfile> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(storageFilePath))
        {
            return AppSettingsProfile.Default;
        }

        await using var stream = File.OpenRead(storageFilePath);
        var loaded = await JsonSerializer.DeserializeAsync<AppSettingsProfile>(stream, SerializerOptions, cancellationToken);
        return loaded ?? AppSettingsProfile.Default;
    }

    public async Task SaveAsync(AppSettingsProfile profile, CancellationToken cancellationToken = default)
    {
        var directoryPath = Path.GetDirectoryName(storageFilePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        await using var stream = File.Create(storageFilePath);
        await JsonSerializer.SerializeAsync(stream, profile, SerializerOptions, cancellationToken);
    }
}
