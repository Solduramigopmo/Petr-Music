using Petrichor.Core.Domain;

namespace Petrichor.Core.Playback;

public sealed class PlaybackQueueController
{
    private readonly List<TrackReference> queue = [];

    public IReadOnlyList<TrackReference> Queue => queue;
    public int CurrentQueueIndex { get; private set; } = -1;
    public QueueSource QueueSource { get; private set; } = QueueSource.Library;
    public string? SourceIdentifier { get; private set; }
    public bool ShuffleEnabled { get; private set; }
    public RepeatMode RepeatMode { get; private set; } = RepeatMode.Off;

    public TrackReference? CurrentTrack =>
        CurrentQueueIndex >= 0 && CurrentQueueIndex < queue.Count ? queue[CurrentQueueIndex] : null;

    public void Restore(PlaybackQueueState state)
    {
        queue.Clear();
        queue.AddRange(state.Queue);
        CurrentQueueIndex = state.Queue.Count == 0 ? -1 : Math.Clamp(state.CurrentQueueIndex, 0, state.Queue.Count - 1);
        QueueSource = state.QueueSource;
        ShuffleEnabled = state.ShuffleEnabled;
        RepeatMode = state.RepeatMode;
        SourceIdentifier = state.SourceIdentifier;
    }

    public PlaybackQueueState CaptureState()
    {
        return new PlaybackQueueState(
            Queue: queue.ToArray(),
            CurrentQueueIndex: CurrentQueueIndex,
            QueueSource: QueueSource,
            ShuffleEnabled: ShuffleEnabled,
            RepeatMode: RepeatMode,
            SourceIdentifier: SourceIdentifier);
    }

    public void SetQueue(IEnumerable<TrackReference> tracks, QueueSource source, string? sourceIdentifier = null, TrackReference? startTrack = null)
    {
        queue.Clear();
        queue.AddRange(tracks);
        QueueSource = source;
        SourceIdentifier = sourceIdentifier;

        if (queue.Count == 0)
        {
            CurrentQueueIndex = -1;
            return;
        }

        if (startTrack is not null)
        {
            var trackIndex = queue.FindIndex(track => Matches(track, startTrack));
            CurrentQueueIndex = trackIndex >= 0 ? trackIndex : 0;
        }
        else
        {
            CurrentQueueIndex = 0;
        }
    }

    public void Clear()
    {
        queue.Clear();
        CurrentQueueIndex = -1;
        SourceIdentifier = null;
    }

    public void ToggleShuffle()
    {
        ShuffleEnabled = !ShuffleEnabled;
        if (ShuffleEnabled)
        {
            ShuffleCurrentQueue();
        }
    }

    public void ToggleRepeatMode()
    {
        RepeatMode = RepeatMode switch
        {
            RepeatMode.Off => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            _ => RepeatMode.Off
        };
    }

    public TrackReference? PlayFromQueue(int index)
    {
        if (index < 0 || index >= queue.Count)
        {
            return null;
        }

        CurrentQueueIndex = index;
        return queue[index];
    }

    public TrackReference? GetNextTrack()
    {
        if (queue.Count == 0)
        {
            return null;
        }

        int nextIndex;

        switch (RepeatMode)
        {
            case RepeatMode.One:
                nextIndex = Math.Max(CurrentQueueIndex, 0);
                break;
            case RepeatMode.All:
                nextIndex = CurrentQueueIndex < 0 ? 0 : (CurrentQueueIndex + 1) % queue.Count;
                break;
            default:
                nextIndex = CurrentQueueIndex + 1;
                if (nextIndex >= queue.Count)
                {
                    return null;
                }
                break;
        }

        CurrentQueueIndex = nextIndex;
        return queue[nextIndex];
    }

    public TrackReference? GetPreviousTrack(bool restartThresholdReached)
    {
        if (queue.Count == 0)
        {
            return null;
        }

        if (restartThresholdReached && CurrentQueueIndex >= 0 && CurrentQueueIndex < queue.Count)
        {
            return queue[CurrentQueueIndex];
        }

        int previousIndex;

        switch (RepeatMode)
        {
            case RepeatMode.One:
                previousIndex = Math.Max(CurrentQueueIndex, 0);
                break;
            case RepeatMode.All:
                previousIndex = CurrentQueueIndex > 0 ? CurrentQueueIndex - 1 : queue.Count - 1;
                break;
            default:
                previousIndex = CurrentQueueIndex - 1;
                if (previousIndex < 0)
                {
                    return CurrentQueueIndex >= 0 && CurrentQueueIndex < queue.Count ? queue[CurrentQueueIndex] : null;
                }
                break;
        }

        CurrentQueueIndex = previousIndex;
        return queue[previousIndex];
    }

    public TrackReference? HandleTrackCompletion()
    {
        return RepeatMode switch
        {
            RepeatMode.One => CurrentTrack,
            RepeatMode.All => GetNextTrack(),
            _ => CurrentQueueIndex + 1 < queue.Count ? GetNextTrack() : null
        };
    }

    public void AddToQueue(TrackReference track)
    {
        if (queue.Count == 0)
        {
            queue.Add(track);
            CurrentQueueIndex = 0;
            return;
        }

        if (queue.Any(existing => Matches(existing, track)))
        {
            return;
        }

        queue.Add(track);
    }

    public void PlayNext(TrackReference track)
    {
        if (queue.Count == 0 || CurrentQueueIndex < 0)
        {
            queue.Clear();
            queue.Add(track);
            CurrentQueueIndex = 0;
            return;
        }

        var insertIndex = CurrentQueueIndex + 1;
        var existingIndex = queue.FindIndex(existing => Matches(existing, track));
        if (existingIndex >= 0)
        {
            queue.RemoveAt(existingIndex);
            if (existingIndex <= CurrentQueueIndex)
            {
                CurrentQueueIndex -= 1;
            }
        }

        queue.Insert(Math.Min(insertIndex, queue.Count), track);
    }

    public void RemoveFromQueue(int index)
    {
        if (index < 0 || index >= queue.Count || index == CurrentQueueIndex)
        {
            return;
        }

        queue.RemoveAt(index);
        if (index < CurrentQueueIndex)
        {
            CurrentQueueIndex -= 1;
        }
    }

    public void MoveInQueue(int sourceIndex, int destinationIndex)
    {
        if (sourceIndex < 0 || sourceIndex >= queue.Count || destinationIndex < 0 || destinationIndex >= queue.Count || sourceIndex == destinationIndex)
        {
            return;
        }

        var track = queue[sourceIndex];
        queue.RemoveAt(sourceIndex);
        queue.Insert(destinationIndex, track);

        if (sourceIndex == CurrentQueueIndex)
        {
            CurrentQueueIndex = destinationIndex;
        }
        else if (sourceIndex < CurrentQueueIndex && destinationIndex >= CurrentQueueIndex)
        {
            CurrentQueueIndex -= 1;
        }
        else if (sourceIndex > CurrentQueueIndex && destinationIndex <= CurrentQueueIndex)
        {
            CurrentQueueIndex += 1;
        }
    }

    private void ShuffleCurrentQueue()
    {
        if (queue.Count == 0)
        {
            return;
        }

        if (CurrentTrack is { } currentTrack)
        {
            var tracksToShuffle = queue.Where(track => !Matches(track, currentTrack)).ToList();
            ShuffleInPlace(tracksToShuffle);

            queue.Clear();
            queue.Add(currentTrack);
            queue.AddRange(tracksToShuffle);
            CurrentQueueIndex = 0;
            return;
        }

        ShuffleInPlace(queue);
        CurrentQueueIndex = 0;
    }

    private static void ShuffleInPlace<T>(IList<T> items)
    {
        for (var index = items.Count - 1; index > 0; index--)
        {
            var swapIndex = Random.Shared.Next(index + 1);
            (items[index], items[swapIndex]) = (items[swapIndex], items[index]);
        }
    }

    private static bool Matches(TrackReference left, TrackReference right)
    {
        if (left.TrackId.HasValue && right.TrackId.HasValue)
        {
            return left.TrackId.Value == right.TrackId.Value;
        }

        return string.Equals(left.Path, right.Path, StringComparison.OrdinalIgnoreCase);
    }
}
