namespace Petrichor.Core.Domain;

public sealed record PlaybackUiState(
    string TrackTitle,
    string? TrackArtist,
    string? TrackAlbum,
    byte[]? ArtworkData,
    double PlaybackPositionSeconds,
    double TrackDurationSeconds,
    float Volume,
    bool IsQueueVisible);
