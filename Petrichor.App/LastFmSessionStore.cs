using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Petrichor.App;

internal sealed class LastFmSessionStore
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly string storageFilePath;

    public LastFmSessionStore(string storageFilePath)
    {
        this.storageFilePath = storageFilePath;
    }

    public async Task<LastFmSession?> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(storageFilePath))
            {
                return null;
            }

            var protectedBytes = await File.ReadAllBytesAsync(storageFilePath, cancellationToken);
            var bytes = ProtectedData.Unprotect(
                protectedBytes,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<LastFmSession>(bytes, JsonSerializerOptions);
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveAsync(LastFmSession session, CancellationToken cancellationToken = default)
    {
        var directoryPath = Path.GetDirectoryName(storageFilePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var jsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(session, JsonSerializerOptions));
        var protectedBytes = ProtectedData.Protect(
            jsonBytes,
            optionalEntropy: null,
            scope: DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(storageFilePath, protectedBytes, cancellationToken);
    }

    public void Delete()
    {
        try
        {
            if (File.Exists(storageFilePath))
            {
                File.Delete(storageFilePath);
            }
        }
        catch
        {
            // best effort
        }
    }
}

internal sealed record LastFmSession(
    string SessionKey,
    string Username);
