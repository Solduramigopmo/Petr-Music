using Petrichor.Core.Domain;

namespace Petrichor.Core.Playback;

public sealed record PlaybackQueueState(
    IReadOnlyList<TrackReference> Queue,
    int CurrentQueueIndex,
    QueueSource QueueSource,
    bool ShuffleEnabled,
    RepeatMode RepeatMode,
    string? SourceIdentifier);
