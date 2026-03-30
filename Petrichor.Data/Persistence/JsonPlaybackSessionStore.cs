using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Petrichor.Core.Abstractions;
using Petrichor.Core.Domain;

namespace Petrichor.Data.Persistence;

public sealed class JsonPlaybackSessionStore : IPlaybackSessionStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string storageFilePath;

    public JsonPlaybackSessionStore(string storageFilePath)
    {
        this.storageFilePath = storageFilePath;
    }

    public async Task SaveAsync(PlaybackSessionSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var directoryPath = Path.GetDirectoryName(storageFilePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        await using var stream = File.Create(storageFilePath);
        await JsonSerializer.SerializeAsync(stream, snapshot, SerializerOptions, cancellationToken);
    }

    public async Task<PlaybackSessionSnapshot?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(storageFilePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(storageFilePath);
        return await JsonSerializer.DeserializeAsync<PlaybackSessionSnapshot>(stream, SerializerOptions, cancellationToken);
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
