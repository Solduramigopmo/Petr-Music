using System.IO;
using Microsoft.Data.Sqlite;

namespace Petrichor.Data.Persistence;

public sealed class PetrichorSqliteMigrator
{
    private const int CurrentSchemaVersion = 1;

    public async Task EnsureCreatedAsync(string databaseFilePath, CancellationToken cancellationToken = default)
    {
        var directoryPath = Path.GetDirectoryName(databaseFilePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        await using var connection = new SqliteConnection($"Data Source={databaseFilePath}");
        await connection.OpenAsync(cancellationToken);

        await ExecuteBatchAsync(connection, PragmasSql, cancellationToken);
        await ExecuteBatchAsync(connection, SchemaVersionSql, cancellationToken);

        var currentVersion = await GetSchemaVersionAsync(connection, cancellationToken);
        if (currentVersion >= CurrentSchemaVersion)
        {
            return;
        }

        await using var dbTransaction = await connection.BeginTransactionAsync(cancellationToken);
        var transaction = (SqliteTransaction)dbTransaction;

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = InitialSchemaSql;
        await command.ExecuteNonQueryAsync(cancellationToken);

        var versionCommand = connection.CreateCommand();
        versionCommand.Transaction = transaction;
        versionCommand.CommandText = "UPDATE schema_info SET version = $version, updated_at_utc = CURRENT_TIMESTAMP;";
        versionCommand.Parameters.AddWithValue("$version", CurrentSchemaVersion);
        await versionCommand.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<int> GetSchemaVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT version FROM schema_info LIMIT 1;";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long version ? (int)version : 0;
    }

    private static async Task ExecuteBatchAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string PragmasSql =
        """
        PRAGMA journal_mode = WAL;
        PRAGMA synchronous = NORMAL;
        PRAGMA busy_timeout = 5000;
        PRAGMA foreign_keys = ON;
        """;

    private const string SchemaVersionSql =
        """
        CREATE TABLE IF NOT EXISTS schema_info (
            version INTEGER NOT NULL,
            updated_at_utc TEXT NOT NULL
        );

        INSERT INTO schema_info (version, updated_at_utc)
        SELECT 0, CURRENT_TIMESTAMP
        WHERE NOT EXISTS (SELECT 1 FROM schema_info);
        """;

    private const string InitialSchemaSql =
        """
        CREATE TABLE IF NOT EXISTS folders (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL,
            path TEXT NOT NULL UNIQUE,
            track_count INTEGER NOT NULL DEFAULT 0,
            date_added TEXT NOT NULL,
            date_updated TEXT NOT NULL,
            bookmark_data BLOB
        );

        CREATE TABLE IF NOT EXISTS artists (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL,
            normalized_name TEXT NOT NULL,
            sort_name TEXT,
            artwork_data BLOB,
            bio TEXT,
            bio_source TEXT,
            bio_updated_at TEXT,
            image_url TEXT,
            image_source TEXT,
            image_updated_at TEXT,
            discogs_id TEXT,
            musicbrainz_id TEXT,
            spotify_id TEXT,
            apple_music_id TEXT,
            country TEXT,
            formed_year INTEGER,
            disbanded_year INTEGER,
            genres TEXT,
            websites TEXT,
            members TEXT,
            total_tracks INTEGER NOT NULL DEFAULT 0 CHECK(total_tracks >= 0),
            total_albums INTEGER NOT NULL DEFAULT 0 CHECK(total_albums >= 0),
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE UNIQUE INDEX IF NOT EXISTS idx_artists_normalized_name_unique
        ON artists(normalized_name);

        CREATE TABLE IF NOT EXISTS albums (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            title TEXT NOT NULL,
            normalized_title TEXT NOT NULL,
            sort_title TEXT,
            artwork_data BLOB,
            release_date TEXT,
            release_year INTEGER CHECK(release_year IS NULL OR (release_year >= 1900 AND release_year <= 2100)),
            album_type TEXT,
            total_tracks INTEGER CHECK(total_tracks IS NULL OR total_tracks >= 0),
            total_discs INTEGER CHECK(total_discs IS NULL OR total_discs >= 0),
            description TEXT,
            review TEXT,
            review_source TEXT,
            cover_art_url TEXT,
            thumbnail_url TEXT,
            discogs_id TEXT,
            musicbrainz_id TEXT,
            spotify_id TEXT,
            apple_music_id TEXT,
            label TEXT,
            catalog_number TEXT,
            barcode TEXT,
            genres TEXT,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_albums_normalized_title
        ON albums(normalized_title);

        CREATE TABLE IF NOT EXISTS album_artists (
            album_id INTEGER NOT NULL,
            artist_id INTEGER NOT NULL,
            role TEXT NOT NULL DEFAULT 'primary',
            position INTEGER NOT NULL DEFAULT 0,
            PRIMARY KEY (album_id, artist_id, role),
            FOREIGN KEY (album_id) REFERENCES albums(id) ON DELETE CASCADE,
            FOREIGN KEY (artist_id) REFERENCES artists(id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS genres (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL UNIQUE
        );

        CREATE TABLE IF NOT EXISTS tracks (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            folder_id INTEGER NOT NULL,
            album_id INTEGER,
            path TEXT NOT NULL UNIQUE,
            filename TEXT NOT NULL,
            title TEXT,
            artist TEXT,
            album TEXT,
            composer TEXT,
            genre TEXT,
            year TEXT,
            duration REAL CHECK(duration >= 0),
            format TEXT,
            file_size INTEGER,
            date_added TEXT NOT NULL,
            date_modified TEXT,
            track_artwork_data BLOB,
            is_favorite INTEGER NOT NULL DEFAULT 0,
            play_count INTEGER NOT NULL DEFAULT 0,
            last_played_date TEXT,
            is_duplicate INTEGER NOT NULL DEFAULT 0,
            primary_track_id INTEGER,
            duplicate_group_id TEXT,
            album_artist TEXT,
            track_number INTEGER,
            total_tracks INTEGER,
            disc_number INTEGER,
            total_discs INTEGER,
            rating INTEGER CHECK(rating IS NULL OR (rating >= 0 AND rating <= 5)),
            compilation INTEGER DEFAULT 0,
            release_date TEXT,
            original_release_date TEXT,
            bpm INTEGER,
            media_type TEXT,
            bitrate INTEGER CHECK(bitrate IS NULL OR bitrate > 0),
            sample_rate INTEGER,
            channels INTEGER,
            codec TEXT,
            bit_depth INTEGER,
            sort_title TEXT,
            sort_artist TEXT,
            sort_album TEXT,
            sort_album_artist TEXT,
            extended_metadata TEXT,
            FOREIGN KEY (folder_id) REFERENCES folders(id) ON DELETE CASCADE,
            FOREIGN KEY (album_id) REFERENCES albums(id) ON DELETE SET NULL,
            FOREIGN KEY (primary_track_id) REFERENCES tracks(id) ON DELETE SET NULL
        );

        CREATE INDEX IF NOT EXISTS idx_tracks_folder_id ON tracks(folder_id);
        CREATE INDEX IF NOT EXISTS idx_tracks_album_id ON tracks(album_id);
        CREATE INDEX IF NOT EXISTS idx_tracks_artist ON tracks(artist);
        CREATE INDEX IF NOT EXISTS idx_tracks_album ON tracks(album);
        CREATE INDEX IF NOT EXISTS idx_tracks_composer ON tracks(composer);
        CREATE INDEX IF NOT EXISTS idx_tracks_genre ON tracks(genre);
        CREATE INDEX IF NOT EXISTS idx_tracks_year ON tracks(year);
        CREATE INDEX IF NOT EXISTS idx_tracks_album_artist ON tracks(album_artist);
        CREATE INDEX IF NOT EXISTS idx_tracks_is_favorite ON tracks(is_favorite);
        CREATE INDEX IF NOT EXISTS idx_tracks_is_duplicate ON tracks(is_duplicate);
        CREATE INDEX IF NOT EXISTS idx_tracks_duplicate_group_id ON tracks(duplicate_group_id);

        CREATE TABLE IF NOT EXISTS playlists (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            type TEXT NOT NULL,
            is_user_editable INTEGER NOT NULL,
            is_content_editable INTEGER NOT NULL,
            date_created TEXT NOT NULL,
            date_modified TEXT NOT NULL,
            cover_artwork_data BLOB,
            smart_criteria TEXT,
            sort_order INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS playlist_tracks (
            playlist_id TEXT NOT NULL,
            track_id INTEGER NOT NULL,
            position INTEGER NOT NULL,
            date_added TEXT NOT NULL,
            PRIMARY KEY (playlist_id, track_id),
            FOREIGN KEY (playlist_id) REFERENCES playlists(id) ON DELETE CASCADE,
            FOREIGN KEY (track_id) REFERENCES tracks(id) ON DELETE CASCADE
        );

        CREATE INDEX IF NOT EXISTS idx_playlist_tracks_playlist_id
        ON playlist_tracks(playlist_id);

        CREATE TABLE IF NOT EXISTS track_artists (
            track_id INTEGER NOT NULL,
            artist_id INTEGER NOT NULL,
            role TEXT NOT NULL DEFAULT 'artist',
            position INTEGER NOT NULL DEFAULT 0,
            PRIMARY KEY (track_id, artist_id, role),
            FOREIGN KEY (track_id) REFERENCES tracks(id) ON DELETE CASCADE,
            FOREIGN KEY (artist_id) REFERENCES artists(id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS track_genres (
            track_id INTEGER NOT NULL,
            genre_id INTEGER NOT NULL,
            PRIMARY KEY (track_id, genre_id),
            FOREIGN KEY (track_id) REFERENCES tracks(id) ON DELETE CASCADE,
            FOREIGN KEY (genre_id) REFERENCES genres(id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS pinned_items (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            item_type TEXT NOT NULL,
            filter_type TEXT,
            filter_value TEXT,
            entity_id TEXT,
            artist_id INTEGER,
            album_id INTEGER,
            playlist_id TEXT,
            display_name TEXT NOT NULL,
            subtitle TEXT,
            icon_name TEXT NOT NULL,
            sort_order INTEGER NOT NULL DEFAULT 0,
            date_added TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_pinned_items_sort_order
        ON pinned_items(sort_order);

        CREATE VIRTUAL TABLE IF NOT EXISTS tracks_fts USING fts5 (
            track_id UNINDEXED,
            title,
            artist,
            album,
            album_artist,
            composer,
            genre,
            year,
            tokenize = 'unicode61'
        );

        CREATE TRIGGER IF NOT EXISTS tracks_fts_insert
        AFTER INSERT ON tracks
        BEGIN
            INSERT INTO tracks_fts(rowid, track_id, title, artist, album, album_artist, composer, genre, year)
            VALUES (NEW.id, NEW.id, NEW.title, NEW.artist, NEW.album, NEW.album_artist, NEW.composer, NEW.genre, NEW.year);
        END;

        CREATE TRIGGER IF NOT EXISTS tracks_fts_update
        AFTER UPDATE ON tracks
        BEGIN
            UPDATE tracks_fts
            SET title = NEW.title,
                artist = NEW.artist,
                album = NEW.album,
                album_artist = NEW.album_artist,
                composer = NEW.composer,
                genre = NEW.genre,
                year = NEW.year
            WHERE rowid = NEW.id;
        END;

        CREATE TRIGGER IF NOT EXISTS tracks_fts_delete
        AFTER DELETE ON tracks
        BEGIN
            DELETE FROM tracks_fts WHERE rowid = OLD.id;
        END;
        """;
}
