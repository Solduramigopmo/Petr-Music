namespace Petrichor.Core.Domain;

public sealed record TrackReference(
    long? TrackId,
    string Path,
    string Title,
    string? Artist,
    string? Album,
    double DurationSeconds,
    string? Genre = null,
    string? Year = null);
