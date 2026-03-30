using System;
using System.Collections.Generic;

namespace Petrichor.Core.Domain;

public sealed record PlaybackSessionSnapshot(
    TrackReference? CurrentTrack,
    double PlaybackPositionSeconds,
    IReadOnlyList<TrackReference> Queue,
    int CurrentQueueIndex,
    QueueSource QueueSource,
    string? SourceIdentifier,
    float Volume,
    bool IsMuted,
    bool ShuffleEnabled,
    RepeatMode RepeatMode,
    bool IsQueueVisible,
    DateTimeOffset SavedAtUtc);
