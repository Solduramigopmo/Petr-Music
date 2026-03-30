using Microsoft.Data.Sqlite;
using Petrichor.Core.Abstractions;
using Petrichor.Core.Domain;
using Petrichor.Data.Persistence;
using System.Text.Json;

namespace Petrichor.Data.Repositories;

public sealed class SqliteTrackRepository : ITrackRepository
{
    private readonly SqliteConnectionFactory connectionFactory;

    public SqliteTrackRepository(SqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<TrackReference>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, path, COALESCE(title, filename), artist, album, COALESCE(duration, 0), genre, year
            FROM tracks
            ORDER BY artist, album, disc_number, track_number, title, filename;
            """;

        return await ReadTracksAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<TrackReference>> GetByIdsAsync(IEnumerable<long> trackIds, CancellationToken cancellationToken = default)
    {
        var ids = trackIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return Array.Empty<TrackReference>();
        }

        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        var parameterNames = new List<string>(ids.Length);
        for (var i = 0; i < ids.Length; i++)
        {
            var parameterName = $"$id{i}";
            command.Parameters.AddWithValue(parameterName, ids[i]);
            parameterNames.Add(parameterName);
        }

        command.CommandText =
            $"""
            SELECT id, path, COALESCE(title, filename), artist, album, COALESCE(duration, 0), genre, year
            FROM tracks
            WHERE id IN ({string.Join(", ", parameterNames)})
            ORDER BY artist, album, disc_number, track_number, title, filename;
            """;

        return await ReadTracksAsync(command, cancellationToken);
    }

    public async Task<TrackReference?> GetByIdAsync(long trackId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, path, COALESCE(title, filename), artist, album, COALESCE(duration, 0), genre, year
            FROM tracks
            WHERE id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", trackId);

        return await ReadSingleTrackAsync(command, cancellationToken);
    }

    public async Task<TrackReference?> GetByPathAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, path, COALESCE(title, filename), artist, album, COALESCE(duration, 0), genre, year
            FROM tracks
            WHERE path = $path
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$path", path);

        return await ReadSingleTrackAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<TrackReference>> GetByFolderPathAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT t.id, t.path, COALESCE(t.title, t.filename), t.artist, t.album, COALESCE(t.duration, 0), t.genre, t.year
            FROM tracks t
            INNER JOIN folders f ON f.id = t.folder_id
            WHERE f.path = $folderPath
            ORDER BY t.disc_number, t.track_number, t.title, t.filename;
            """;
        command.Parameters.AddWithValue("$folderPath", folderPath);

        return await ReadTracksAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<TrackReference>> GetBySmartCriteriaAsync(string smartCriteriaJson, CancellationToken cancellationToken = default)
    {
        var criteria = ParseSmartCriteria(smartCriteriaJson);

        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        var whereClauses = new List<string>();
        var parameters = new List<(string Name, object Value)>();
        var parameterIndex = 0;

        foreach (var rule in criteria.Rules)
        {
            var clause = BuildRuleClause(rule, ref parameterIndex, parameters);
            if (!string.IsNullOrWhiteSpace(clause))
            {
                whereClauses.Add(clause);
            }
        }

        var whereSql = whereClauses.Count == 0
            ? string.Empty
            : $"WHERE {string.Join(criteria.MatchAny ? " OR " : " AND ", whereClauses)}";
        var orderBySql = BuildOrderByClause(criteria.SortBy, criteria.SortDescending);
        var limitSql = criteria.Limit > 0 ? $"LIMIT {criteria.Limit}" : string.Empty;

        command.CommandText =
            $"""
            SELECT id, path, COALESCE(title, filename), artist, album, COALESCE(duration, 0), genre, year
            FROM tracks
            {whereSql}
            {orderBySql}
            {limitSql};
            """;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return await ReadTracksAsync(command, cancellationToken);
    }

    public async Task RemoveMissingTracksForFolderAsync(
        long folderId,
        IReadOnlyList<string> discoveredPaths,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        var pathParameterNames = new List<string>(discoveredPaths.Count);
        for (var i = 0; i < discoveredPaths.Count; i++)
        {
            var parameterName = $"$path{i}";
            pathParameterNames.Add(parameterName);
            command.Parameters.AddWithValue(parameterName, discoveredPaths[i]);
        }

        command.Parameters.AddWithValue("$folderId", folderId);
        command.CommandText = discoveredPaths.Count == 0
            ? "DELETE FROM tracks WHERE folder_id = $folderId;"
            : $"DELETE FROM tracks WHERE folder_id = $folderId AND path NOT IN ({string.Join(", ", pathParameterNames)});";

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertAsync(TrackReference track, long? folderId = null, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO tracks (
                id, folder_id, path, filename, title, artist, album, genre, year, duration, date_added, is_favorite, play_count, is_duplicate
            )
            VALUES (
                $id, COALESCE($folderId, 1), $path, $filename, $title, $artist, $album, $genre, $year, $duration, CURRENT_TIMESTAMP, 0, 0, 0
            )
            ON CONFLICT(path) DO UPDATE SET
                title = excluded.title,
                artist = excluded.artist,
                album = excluded.album,
                genre = excluded.genre,
                year = excluded.year,
                duration = excluded.duration,
                folder_id = COALESCE($folderId, tracks.folder_id);
            """;
        command.Parameters.AddWithValue("$id", (object?)track.TrackId ?? DBNull.Value);
        command.Parameters.AddWithValue("$folderId", (object?)folderId ?? DBNull.Value);
        command.Parameters.AddWithValue("$path", track.Path);
        command.Parameters.AddWithValue("$filename", Path.GetFileName(track.Path));
        command.Parameters.AddWithValue("$title", track.Title);
        command.Parameters.AddWithValue("$artist", (object?)track.Artist ?? DBNull.Value);
        command.Parameters.AddWithValue("$album", (object?)track.Album ?? DBNull.Value);
        command.Parameters.AddWithValue("$genre", (object?)track.Genre ?? DBNull.Value);
        command.Parameters.AddWithValue("$year", (object?)track.Year ?? DBNull.Value);
        command.Parameters.AddWithValue("$duration", track.DurationSeconds);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<TrackReference>> ReadTracksAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        var results = new List<TrackReference>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(MapTrack(reader));
        }

        return results;
    }

    private static async Task<TrackReference?> ReadSingleTrackAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapTrack(reader) : null;
    }

    private static TrackReference MapTrack(SqliteDataReader reader)
    {
        return new TrackReference(
            TrackId: reader.GetInt64(0),
            Path: reader.GetString(1),
            Title: reader.GetString(2),
            Artist: reader.IsDBNull(3) ? null : reader.GetString(3),
            Album: reader.IsDBNull(4) ? null : reader.GetString(4),
            DurationSeconds: reader.GetDouble(5),
            Genre: reader.IsDBNull(6) ? null : reader.GetString(6),
            Year: reader.IsDBNull(7) ? null : reader.GetString(7));
    }

    private static string BuildOrderByClause(string? sortBy, bool sortDescending)
    {
        var orderDirection = sortDescending ? "DESC" : "ASC";
        var normalizedSortBy = (sortBy ?? string.Empty).Trim().ToLowerInvariant();

        return normalizedSortBy switch
        {
            "artist" => $"ORDER BY COALESCE(artist, '') {orderDirection}, COALESCE(title, filename) ASC",
            "album" => $"ORDER BY COALESCE(album, '') {orderDirection}, COALESCE(title, filename) ASC",
            "duration" => $"ORDER BY COALESCE(duration, 0) {orderDirection}, COALESCE(title, filename) ASC",
            "year" => $"ORDER BY COALESCE(year, '') {orderDirection}, COALESCE(title, filename) ASC",
            "dateadded" => $"ORDER BY date_added {orderDirection}, COALESCE(title, filename) ASC",
            "playcount" => $"ORDER BY play_count {orderDirection}, COALESCE(title, filename) ASC",
            _ => $"ORDER BY COALESCE(title, filename) {orderDirection}"
        };
    }

    private static string? BuildRuleClause(
        SmartCriteriaRule rule,
        ref int parameterIndex,
        List<(string Name, object Value)> parameters)
    {
        var field = (rule.Field ?? string.Empty).Trim().ToLowerInvariant();
        var condition = (rule.Condition ?? string.Empty).Trim().ToLowerInvariant();
        var value = rule.Value ?? string.Empty;

        if (field == "anytext")
        {
            var parameterName = $"$p{parameterIndex++}";
            parameters.Add((parameterName, $"%{value}%"));
            return $"""
                    (COALESCE(title, '') LIKE {parameterName} OR
                     COALESCE(artist, '') LIKE {parameterName} OR
                     COALESCE(album, '') LIKE {parameterName})
                    """;
        }

        if (field == "hasartist")
        {
            var truthy = value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1";
            return truthy
                ? "COALESCE(TRIM(artist), '') <> ''"
                : "COALESCE(TRIM(artist), '') = ''";
        }

        var column = field switch
        {
            "title" => "COALESCE(title, filename)",
            "artist" => "COALESCE(artist, '')",
            "album" => "COALESCE(album, '')",
            "genre" => "COALESCE(genre, '')",
            "year" => "COALESCE(year, '')",
            "path" => "path",
            "duration" => "COALESCE(duration, 0)",
            "isfavorite" => "COALESCE(is_favorite, 0)",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(column))
        {
            return null;
        }

        if (field is "duration" or "isfavorite")
        {
            if (!double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var numericValue))
            {
                return null;
            }

            var parameterName = $"$p{parameterIndex++}";
            parameters.Add((parameterName, numericValue));

            return condition switch
            {
                "greaterthan" => $"{column} > {parameterName}",
                "lessthan" => $"{column} < {parameterName}",
                "equals" => $"{column} = {parameterName}",
                "notequals" => $"{column} <> {parameterName}",
                _ => $"{column} = {parameterName}"
            };
        }

        var textParameterName = $"$p{parameterIndex++}";
        return condition switch
        {
            "contains" => AddTextParameterAndBuildClause(parameters, textParameterName, column, $"%{value}%", "LIKE"),
            "startswith" => AddTextParameterAndBuildClause(parameters, textParameterName, column, $"{value}%", "LIKE"),
            "endswith" => AddTextParameterAndBuildClause(parameters, textParameterName, column, $"%{value}", "LIKE"),
            "notequals" => AddTextParameterAndBuildClause(parameters, textParameterName, column, value, "<>"),
            _ => AddTextParameterAndBuildClause(parameters, textParameterName, column, value, "=")
        };
    }

    private static string AddTextParameterAndBuildClause(
        List<(string Name, object Value)> parameters,
        string parameterName,
        string column,
        string value,
        string operation)
    {
        parameters.Add((parameterName, value));
        return $"{column} {operation} {parameterName}";
    }

    private static SmartCriteria ParseSmartCriteria(string smartCriteriaJson)
    {
        if (string.IsNullOrWhiteSpace(smartCriteriaJson))
        {
            return SmartCriteria.Empty;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<SmartCriteriaPayload>(smartCriteriaJson);
            if (payload is null)
            {
                return SmartCriteria.Empty;
            }

            var rules = payload.Rules?
                .Where(rule => !string.IsNullOrWhiteSpace(rule.Field))
                .Select(rule => new SmartCriteriaRule(rule.Field ?? string.Empty, rule.Condition ?? "equals", rule.Value ?? string.Empty))
                .ToArray() ?? Array.Empty<SmartCriteriaRule>();

            var matchMode = (payload.MatchMode ?? payload.Operator ?? "all").Trim().ToLowerInvariant();
            var sortBy = payload.SortBy?.Trim();
            var sortDescending = payload.SortDescending ?? !payload.SortAscending.GetValueOrDefault(true);
            var limit = payload.Limit.GetValueOrDefault(0);

            return new SmartCriteria(
                Rules: rules,
                MatchAny: matchMode is "any" or "or",
                SortBy: sortBy,
                SortDescending: sortDescending,
                Limit: Math.Max(0, limit));
        }
        catch
        {
            return SmartCriteria.Empty;
        }
    }

    private sealed record SmartCriteria(
        IReadOnlyList<SmartCriteriaRule> Rules,
        bool MatchAny,
        string? SortBy,
        bool SortDescending,
        int Limit)
    {
        public static SmartCriteria Empty { get; } = new(Array.Empty<SmartCriteriaRule>(), false, "title", false, 0);
    }

    private sealed record SmartCriteriaRule(string Field, string Condition, string Value);

    private sealed record SmartCriteriaPayload(
        List<SmartCriteriaPayloadRule>? Rules,
        string? MatchMode,
        string? Operator,
        string? SortBy,
        bool? SortDescending,
        bool? SortAscending,
        int? Limit);

    private sealed record SmartCriteriaPayloadRule(
        string? Field,
        string? Condition,
        string? Value);
}
