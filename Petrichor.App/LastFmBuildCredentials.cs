using System.Reflection;

namespace Petrichor.App;

internal sealed record LastFmBuildCredentials(
    string ApiKey,
    string SharedSecret)
{
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(SharedSecret);

    public static LastFmBuildCredentials Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>();

        var apiKey = metadata
            .FirstOrDefault(attribute => string.Equals(attribute.Key, "LASTFM_API_KEY", StringComparison.Ordinal))?
            .Value?
            .Trim() ?? string.Empty;

        var sharedSecret = metadata
            .FirstOrDefault(attribute => string.Equals(attribute.Key, "LASTFM_SHARED_SECRET", StringComparison.Ordinal))?
            .Value?
            .Trim() ?? string.Empty;

        return new LastFmBuildCredentials(apiKey, sharedSecret);
    }
}
