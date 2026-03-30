using Petrichor.Core.Domain;
using Petrichor.Data.Repositories;

namespace Petrichor.Data.Persistence;

public sealed class DevelopmentSeedService
{
    private static readonly Guid SeedPlaylistId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string SeedFolderPath = @"C:\Music\Petrichor";
    private const string SeedTrackPath = @"C:\Music\Petrichor\Sample Track.flac";

    private readonly SqliteConnectionFactory connectionFactory;
    private readonly SqliteFolderRepository folderRepository;
    private readonly SqliteTrackRepository trackRepository;
    private readonly SqlitePlaylistRepository playlistRepository;
    private readonly SqlitePlaylistTrackRepository playlistTrackRepository;

    public DevelopmentSeedService(
        SqliteConnectionFactory connectionFactory,
        SqliteFolderRepository folderRepository,
        SqliteTrackRepository trackRepository,
        SqlitePlaylistRepository playlistRepository,
        SqlitePlaylistTrackRepository playlistTrackRepository)
    {
        this.connectionFactory = connectionFactory;
        this.folderRepository = folderRepository;
        this.trackRepository = trackRepository;
        this.playlistRepository = playlistRepository;
        this.playlistTrackRepository = playlistTrackRepository;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var folder = new FolderReference(
            Id: null,
            Path: @"C:\Music\Petrichor",
            Name: "Petrichor",
            TrackCount: 1);

        await folderRepository.UpsertAsync(folder, cancellationToken);
        var savedFolder = await folderRepository.GetByPathAsync(folder.Path, cancellationToken);
        var folderId = savedFolder?.Id;

        var track = new TrackReference(
            TrackId: 1,
            Path: @"C:\Music\Petrichor\Sample Track.flac",
            Title: "Sample Track",
            Artist: "Petrichor",
            Album: "Windows Migration",
            DurationSeconds: 245);

        await trackRepository.UpsertAsync(track, folderId, cancellationToken);

        var playlist = new PlaylistReference(
            Id: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name: "Favorites",
            Type: "smart",
            IsUserEditable: false,
            IsContentEditable: false,
            TrackCount: 1,
            SortOrder: 0);

        await playlistRepository.UpsertAsync(playlist, cancellationToken);
        await playlistTrackRepository.ReplaceTracksAsync(playlist.Id, [1], cancellationToken);
    }

    public async Task RemoveSeedAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM playlist_tracks WHERE playlist_id = $playlistId;
            DELETE FROM playlists WHERE id = $playlistId;
            DELETE FROM tracks WHERE path = $trackPath;
            DELETE FROM folders WHERE path = $folderPath;
            """;
        command.Parameters.AddWithValue("$playlistId", SeedPlaylistId.ToString());
        command.Parameters.AddWithValue("$trackPath", SeedTrackPath);
        command.Parameters.AddWithValue("$folderPath", SeedFolderPath);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
