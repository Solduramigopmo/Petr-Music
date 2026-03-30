using System.IO;
using System.Text.Json;
using Petrichor.Core.Domain;

namespace Petrichor.Data.Persistence;

public sealed class PlaybackStateJsonStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string storageFilePath;

    public PlaybackStateJsonStore(string storageFilePath)
    {
        this.storageFilePath = storageFilePath;
    }

    public async Task SaveAsync(PlaybackStateRecord state, CancellationToken cancellationToken = default)
    {
        var directoryPath = Path.GetDirectoryName(storageFilePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        await using var stream = File.Create(storageFilePath);
        await JsonSerializer.SerializeAsync(stream, state, SerializerOptions, cancellationToken);
    }

    public async Task<PlaybackStateRecord?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(storageFilePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(storageFilePath);
        return await JsonSerializer.DeserializeAsync<PlaybackStateRecord>(stream, SerializerOptions, cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(storageFilePath))
        {
            File.Delete(storageFilePath);
        }

        return Task.CompletedTask;
    }
}
