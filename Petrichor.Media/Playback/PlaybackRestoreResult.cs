using Petrichor.Core.Domain;

namespace Petrichor.Media.Playback;

public sealed record PlaybackRestoreResult(
    PlaybackStateRecord? SavedState,
    TrackReference? CurrentTrack,
    IReadOnlyList<TrackReference> RestoredQueue);
