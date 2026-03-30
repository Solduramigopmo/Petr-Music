using Petrichor.Core.Domain;

namespace Petrichor.Media.Playback;

public sealed record PlaybackStatusSnapshot(
    TrackReference? CurrentTrack,
    PlaybackTransportState State,
    TimeSpan Position,
    TimeSpan? Duration,
    double Volume);
