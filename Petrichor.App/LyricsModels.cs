namespace Petrichor.App;

internal enum LyricsSourceKind
{
    None,
    Lrc,
    Srt,
    Embedded,
    Online
}

internal sealed record LyricsLoadResult(
    string Text,
    LyricsSourceKind Source);
