namespace Petrichor.Data.Persistence;

public sealed record DatabaseProfile(
    string ApplicationDirectoryName,
    string DatabaseFileName,
    string ArtworkCacheDirectoryName);
