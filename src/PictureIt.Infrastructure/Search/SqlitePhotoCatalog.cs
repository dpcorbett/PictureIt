using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using PictureIt.Core.Abstractions;
using PictureIt.Core.History;
using PictureIt.Core.Model;
using PictureIt.Core.Search;

namespace PictureIt.Infrastructure.Search;

/// <summary>
/// The photo catalogue in a SQLite file. Photos are read with the editing reader (ExifTool), so the catalogue
/// sees the same values as the editor. Free text uses an FTS5 index over the descriptive fields.
/// </summary>
public sealed class SqlitePhotoCatalog : IPhotoCatalog, IDisposable
{
    private const int ReadBatchSize = 40;
    private const string DateFormat = "yyyy-MM-dd'T'HH:mm:ss";

    private static readonly EnumerationOptions Recursive = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
    };

    private readonly string _connectionString;
    private readonly IMetadataReader _reader;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public SqlitePhotoCatalog(string databasePath, IMetadataReader reader)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = true }.ToString();
        _reader = reader;
        CreateSchema();
    }

    public static string DefaultDatabasePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PictureIt", "catalog.db");

    /// <summary>Closes pooled connections, so the database file isn't held open.</summary>
    public void Dispose()
    {
        using (var connection = new SqliteConnection(_connectionString))
        {
            SqliteConnection.ClearPool(connection);
        }

        _writeLock.Dispose();
    }

    public async Task<IReadOnlyList<CatalogFolder>> GetFoldersAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var folders = new List<CatalogFolder>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT f.path, f.scanned_at,
                   (SELECT count(*) FROM photos p WHERE p.folder = f.path OR substr(p.folder, 1, length(f.path) + 1) = f.path || '\')
            FROM folders f ORDER BY f.path
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            DateTimeOffset? scanned = reader.IsDBNull(1) ? null : DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture);
            folders.Add(new CatalogFolder(reader.GetString(0), scanned, reader.GetInt32(2)));
        }

        return folders;
    }

    public async Task<string> AddFolderAsync(string folder, CancellationToken cancellationToken = default)
    {
        var path = Normalise(folder);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var roots = await RootsAsync(cancellationToken).ConfigureAwait(false);
            if (roots.FirstOrDefault(r => IsUnder(path, r)) is { } existing)
            {
                return existing;
            }

            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            // Folders inside the new one are now covered by it; their photos stay.
            foreach (var inner in roots.Where(r => IsUnder(r, path)))
            {
                await ExecuteAsync(connection, "DELETE FROM folders WHERE path = $path", cancellationToken, ("$path", inner)).ConfigureAwait(false);
            }

            await ExecuteAsync(connection, "INSERT INTO folders (path) VALUES ($path)", cancellationToken, ("$path", path)).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return path;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task RemoveFolderAsync(string folder, CancellationToken cancellationToken = default)
    {
        var path = Normalise(folder);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, "DELETE FROM folders WHERE path = $path", cancellationToken, ("$path", path)).ConfigureAwait(false);
            var others = await RootsAsync(connection, cancellationToken).ConfigureAwait(false);
            var stale = (await EntriesUnderAsync(connection, path, cancellationToken).ConfigureAwait(false))
                .Keys.Where(p => !others.Any(r => IsUnder(p, r)))
                .ToList();
            await DeleteAsync(connection, stale, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<ScanResult> ScanAsync(string folder, IProgress<WriteProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var root = Normalise(folder);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"{root} doesn't exist or can't be opened.");
        }

        var onDisk = await Task.Run(
            () => Directory.EnumerateFiles(root, "*", Recursive).Where(SupportedFormats.IsSupported).Select(p => new FileInfo(p)).ToList(),
            cancellationToken).ConfigureAwait(false);

        Dictionary<string, (long Length, DateTime Modified)> known;
        await using (var connection = await OpenAsync(cancellationToken).ConfigureAwait(false))
        {
            known = await EntriesUnderAsync(connection, root, cancellationToken).ConfigureAwait(false);
        }

        var present = onDisk.Select(f => f.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed = known.Keys.Where(p => !present.Contains(p)).ToList();
        var added = onDisk.Where(f => !known.ContainsKey(f.FullName)).ToList();
        var changed = onDisk.Where(f => known.TryGetValue(f.FullName, out var k) && (k.Length != f.Length || k.Modified != Truncate(f.LastWriteTimeUtc))).ToList();
        var toRead = added.Concat(changed).ToList();

        var failed = await ReadAndStoreAsync(toRead, progress, cancellationToken).ConfigureAwait(false);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await DeleteAsync(connection, removed, cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(
                connection,
                "UPDATE folders SET scanned_at = $at WHERE path = $path",
                cancellationToken,
                ("$at", DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture)),
                ("$path", root)).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }

        return new ScanResult(added.Count, changed.Count, removed.Count, onDisk.Count - toRead.Count, failed);
    }

    public async Task<int> RefreshFilesAsync(IReadOnlyCollection<string> filePaths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        var roots = await RootsAsync(cancellationToken).ConfigureAwait(false);
        var inside = filePaths
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(p => SupportedFormats.IsSupported(p) && roots.Any(r => IsUnder(p, r)))
            .ToList();
        if (inside.Count == 0)
        {
            return 0;
        }

        var existing = inside.Where(File.Exists).Select(p => new FileInfo(p)).ToList();
        var gone = inside.Where(p => !File.Exists(p)).ToList();
        await ReadAndStoreAsync(existing, progress: null, cancellationToken).ConfigureAwait(false);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await DeleteAsync(connection, gone, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }

        return inside.Count;
    }

    public Task<int> ApplyMovesAsync(IReadOnlyList<FileMove> moves, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(moves);

        // The old paths are gone (so they're dropped) and the new ones are read if they're inside the catalogue.
        return RefreshFilesAsync([.. moves.Select(m => m.From), .. moves.Select(m => m.To)], cancellationToken);
    }

    public async Task<IReadOnlyList<CatalogEntry>> SearchAsync(PhotoQuery query, int? limit = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var where = Where(query, command);
        command.CommandText = $"""
            SELECT p.id, {Columns} FROM photos p {where}
            ORDER BY p.date_taken IS NULL, p.date_taken DESC, p.path
            {(limit is { } n ? $"LIMIT {n.ToString(CultureInfo.InvariantCulture)}" : string.Empty)}
            """;

        var entries = new List<(long Id, CatalogEntry Entry)>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                entries.Add((reader.GetInt64(0), ReadEntry(reader, 1)));
            }
        }

        if (entries.Count == 0)
        {
            return [];
        }

        // Keywords for the photos found.
        var keywords = new Dictionary<long, List<string>>();
        await using (var keywordCommand = connection.CreateCommand())
        {
            keywordCommand.CommandText = $"SELECT k.photo_id, k.keyword FROM keywords k WHERE k.photo_id IN (SELECT p.id FROM photos p {Where(query, keywordCommand)})";
            await using var reader = await keywordCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var id = reader.GetInt64(0);
                if (!keywords.TryGetValue(id, out var list))
                {
                    keywords[id] = list = [];
                }

                list.Add(reader.GetString(1));
            }
        }

        return [.. entries.Select(e => keywords.TryGetValue(e.Id, out var k) ? e.Entry with { Keywords = k } : e.Entry)];
    }

    public async Task<int> CountAsync(PhotoQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT count(*) FROM photos p {Where(query, command)}";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    public async Task<CatalogFacets> GetFacetsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        return new CatalogFacets(
            await FacetAsync(connection, "SELECT camera, count(*) FROM photos WHERE camera IS NOT NULL GROUP BY camera COLLATE NOCASE ORDER BY count(*) DESC, camera", cancellationToken).ConfigureAwait(false),
            await FacetAsync(connection, "SELECT lens, count(*) FROM photos WHERE lens IS NOT NULL GROUP BY lens COLLATE NOCASE ORDER BY count(*) DESC, lens", cancellationToken).ConfigureAwait(false),
            await FacetAsync(connection, "SELECT keyword, count(*) FROM keywords GROUP BY keyword COLLATE NOCASE ORDER BY keyword COLLATE NOCASE", cancellationToken).ConfigureAwait(false));
    }

    private const string Columns =
        "p.path, p.length, p.modified, p.date_taken, p.camera, p.lens, p.focal, p.fnumber, p.exposure, p.iso, p.width, p.height, " +
        "p.lat, p.lon, p.rating, p.title, p.description, p.creator, p.sublocation, p.city, p.state, p.country, p.copyright";

    private static CatalogEntry ReadEntry(SqliteDataReader r, int i) => new()
    {
        FilePath = r.GetString(i),
        Length = r.GetInt64(i + 1),
        Modified = new DateTime(r.GetInt64(i + 2), DateTimeKind.Utc),
        DateTaken = r.IsDBNull(i + 3) ? null : DateTime.ParseExact(r.GetString(i + 3), DateFormat, CultureInfo.InvariantCulture),
        Camera = Text(r, i + 4),
        Lens = Text(r, i + 5),
        FocalLength = Real(r, i + 6),
        FNumber = Real(r, i + 7),
        ExposureTime = Real(r, i + 8),
        Iso = Int(r, i + 9),
        Width = Int(r, i + 10),
        Height = Int(r, i + 11),
        Latitude = Real(r, i + 12),
        Longitude = Real(r, i + 13),
        Rating = Int(r, i + 14),
        Title = Text(r, i + 15),
        Description = Text(r, i + 16),
        Creator = Text(r, i + 17),
        Sublocation = Text(r, i + 18),
        City = Text(r, i + 19),
        State = Text(r, i + 20),
        Country = Text(r, i + 21),
        Copyright = Text(r, i + 22),
    };

    private static string? Text(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private static double? Real(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDouble(i);

    private static int? Int(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt32(i);

    /// <summary>Builds the WHERE clause for a query, adding its parameters to <paramref name="command"/>.</summary>
    private static string Where(PhotoQuery q, SqliteCommand command)
    {
        var conditions = new List<string>();
        void Add(string condition, params (string Name, object Value)[] parameters)
        {
            conditions.Add(condition);
            foreach (var (name, value) in parameters)
            {
                if (!command.Parameters.Contains(name))
                {
                    command.Parameters.AddWithValue(name, value);
                }
            }
        }

        if (FtsQuery(q.Text) is { } fts)
        {
            Add("p.id IN (SELECT rowid FROM photo_text WHERE photo_text MATCH $text)", ("$text", fts));
        }

        if (!string.IsNullOrWhiteSpace(q.Folder))
        {
            var folder = Normalise(q.Folder);
            Add("(p.folder = $folder OR substr(p.folder, 1, length($folder) + 1) = $folder || '\\')", ("$folder", folder));
        }

        if (!string.IsNullOrWhiteSpace(q.Camera))
        {
            Add("p.camera = $camera", ("$camera", q.Camera.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(q.Lens))
        {
            Add("p.lens = $lens", ("$lens", q.Lens.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(q.Keyword))
        {
            Add("EXISTS (SELECT 1 FROM keywords k WHERE k.photo_id = p.id AND k.keyword = $keyword)", ("$keyword", q.Keyword.Trim()));
        }

        if (q.TakenFrom is { } from)
        {
            Add("p.date_taken >= $from", ("$from", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }

        if (q.TakenTo is { } to)
        {
            Add("p.date_taken < $to", ("$to", to.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }

        if (q.MinRating is > 0)
        {
            Add("p.rating >= $rating", ("$rating", q.MinRating.Value));
        }

        Range("p.focal", "focal", q.MinFocalLength, q.MaxFocalLength);
        Range("p.fnumber", "fnumber", q.MinFNumber, q.MaxFNumber);
        Range("p.iso", "iso", q.MinIso, q.MaxIso);
        Presence(q.Location, "p.lat IS NOT NULL");
        Presence(q.DateTaken, "p.date_taken IS NOT NULL");
        Presence(q.Copyright, "p.copyright IS NOT NULL");
        Presence(q.Keywords, "EXISTS (SELECT 1 FROM keywords k WHERE k.photo_id = p.id)");

        return conditions.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", conditions);

        void Range(string column, string name, double? min, double? max)
        {
            if (min is { } low)
            {
                Add($"{column} >= ${name}_min", ($"${name}_min", low));
            }

            if (max is { } high)
            {
                Add($"{column} <= ${name}_max", ($"${name}_max", high));
            }
        }

        void Presence(Core.Search.Presence presence, string has)
        {
            if (presence != Core.Search.Presence.Any)
            {
                conditions.Add(presence == Core.Search.Presence.Has ? has : $"NOT ({has})");
            }
        }
    }

    /// <summary>Each word becomes a quoted prefix term, so punctuation can't break the FTS syntax: <c>"big"* "ben"*</c>.</summary>
    private static string? FtsQuery(string? text)
    {
        var words = (text ?? string.Empty)
            .Split((char[])[' ', '\t', ',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Replace("\"", string.Empty, StringComparison.Ordinal))
            .Where(w => w.Length > 0)
            .Select(w => $"\"{w}\"*")
            .ToList();
        return words.Count == 0 ? null : string.Join(' ', words);
    }

    /// <summary>Reads files in batches and stores them. Unreadable files are stored by name only. Returns the failures.</summary>
    private async Task<List<string>> ReadAndStoreAsync(List<FileInfo> files, IProgress<WriteProgress>? progress, CancellationToken cancellationToken)
    {
        var failed = new List<string>();
        var done = 0;
        foreach (var batch in files.Chunk(ReadBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new WriteProgress(done, files.Count, batch[0].FullName));
            var results = await _reader.ReadManyAsync(batch.Select(f => f.FullName), cancellationToken).ConfigureAwait(false);
            var byPath = results.ToDictionary(r => r.FilePath, StringComparer.OrdinalIgnoreCase);

            var entries = new List<CatalogEntry>();
            foreach (var file in batch)
            {
                file.Refresh();
                if (!file.Exists)
                {
                    continue;
                }

                var modified = Truncate(file.LastWriteTimeUtc);
                if (byPath.TryGetValue(file.FullName, out var result) && result.Metadata is { } metadata)
                {
                    entries.Add(CatalogEntry.FromMetadata(metadata, file.Length, modified) with { FilePath = file.FullName });
                }
                else
                {
                    failed.Add($"{file.FullName}: {result?.Error ?? "couldn't be read"}");
                    entries.Add(new CatalogEntry { FilePath = file.FullName, Length = file.Length, Modified = modified });
                }
            }

            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
                await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                foreach (var entry in entries)
                {
                    await UpsertAsync(connection, entry, cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }

            done += batch.Length;
        }

        progress?.Report(new WriteProgress(files.Count, files.Count, string.Empty));
        return failed;
    }

    private static async Task UpsertAsync(SqliteConnection connection, CatalogEntry e, CancellationToken cancellationToken)
    {
        await DeleteAsync(connection, [e.FilePath], cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO photos (path, folder, length, modified, date_taken, camera, lens, focal, fnumber, exposure, iso, width, height,
                                lat, lon, rating, title, description, creator, sublocation, city, state, country, copyright)
            VALUES ($path, $folder, $length, $modified, $date_taken, $camera, $lens, $focal, $fnumber, $exposure, $iso, $width, $height,
                    $lat, $lon, $rating, $title, $description, $creator, $sublocation, $city, $state, $country, $copyright)
            RETURNING id
            """;
        void P(string name, object? value) => command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        P("$path", e.FilePath);
        P("$folder", e.Folder);
        P("$length", e.Length);
        P("$modified", e.Modified.Ticks);
        P("$date_taken", e.DateTaken?.ToString(DateFormat, CultureInfo.InvariantCulture));
        P("$camera", e.Camera);
        P("$lens", e.Lens);
        P("$focal", e.FocalLength);
        P("$fnumber", e.FNumber);
        P("$exposure", e.ExposureTime);
        P("$iso", e.Iso);
        P("$width", e.Width);
        P("$height", e.Height);
        P("$lat", e.Latitude);
        P("$lon", e.Longitude);
        P("$rating", e.Rating);
        P("$title", e.Title);
        P("$description", e.Description);
        P("$creator", e.Creator);
        P("$sublocation", e.Sublocation);
        P("$city", e.City);
        P("$state", e.State);
        P("$country", e.Country);
        P("$copyright", e.Copyright);
        var id = (long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;

        foreach (var keyword in e.Keywords.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await ExecuteAsync(connection, "INSERT INTO keywords (photo_id, keyword) VALUES ($id, $keyword)", cancellationToken, ("$id", id), ("$keyword", keyword)).ConfigureAwait(false);
        }

        var body = string.Join(
            ' ',
            new[] { Path.GetFileNameWithoutExtension(e.FilePath), e.Title, e.Description, e.Creator, e.Sublocation, e.City, e.State, e.Country, e.Camera, e.Lens }
                .Concat(e.Keywords)
                .Where(s => !string.IsNullOrWhiteSpace(s)));
        await ExecuteAsync(connection, "INSERT INTO photo_text (rowid, body) VALUES ($id, $body)", cancellationToken, ("$id", id), ("$body", body)).ConfigureAwait(false);
    }

    private static async Task DeleteAsync(SqliteConnection connection, IReadOnlyCollection<string> paths, CancellationToken cancellationToken)
    {
        foreach (var path in paths)
        {
            // Keywords cascade; the text index is keyed by the photo's id.
            await ExecuteAsync(
                connection,
                "DELETE FROM photo_text WHERE rowid IN (SELECT id FROM photos WHERE path = $path); DELETE FROM photos WHERE path = $path",
                cancellationToken,
                ("$path", path)).ConfigureAwait(false);
        }
    }

    private static async Task<Dictionary<string, (long Length, DateTime Modified)>> EntriesUnderAsync(SqliteConnection connection, string root, CancellationToken cancellationToken)
    {
        var entries = new Dictionary<string, (long, DateTime)>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT path, length, modified FROM photos WHERE folder = $root OR substr(folder, 1, length($root) + 1) = $root || '\\'";
        command.Parameters.AddWithValue("$root", root);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            entries[reader.GetString(0)] = (reader.GetInt64(1), new DateTime(reader.GetInt64(2), DateTimeKind.Utc));
        }

        return entries;
    }

    private async Task<List<string>> RootsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        return await RootsAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<List<string>> RootsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var roots = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT path FROM folders";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            roots.Add(reader.GetString(0));
        }

        return roots;
    }

    private static async Task<IReadOnlyList<FacetValue>> FacetAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        var values = new List<FacetValue>();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            values.Add(new FacetValue(reader.GetString(0), reader.GetInt32(1)));
        }

        return values;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        await pragma.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private void CreateSchema()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();

        // Text columns compare without case, like Windows paths and the way people type names.
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS folders (
                path TEXT PRIMARY KEY COLLATE NOCASE,
                scanned_at TEXT);
            CREATE TABLE IF NOT EXISTS photos (
                id INTEGER PRIMARY KEY,
                path TEXT NOT NULL UNIQUE COLLATE NOCASE,
                folder TEXT NOT NULL COLLATE NOCASE,
                length INTEGER NOT NULL,
                modified INTEGER NOT NULL,
                date_taken TEXT,
                camera TEXT COLLATE NOCASE,
                lens TEXT COLLATE NOCASE,
                focal REAL, fnumber REAL, exposure REAL, iso INTEGER, width INTEGER, height INTEGER,
                lat REAL, lon REAL, rating INTEGER,
                title TEXT, description TEXT, creator TEXT, sublocation TEXT, city TEXT, state TEXT, country TEXT, copyright TEXT);
            CREATE INDEX IF NOT EXISTS photos_folder ON photos (folder);
            CREATE INDEX IF NOT EXISTS photos_date ON photos (date_taken);
            CREATE TABLE IF NOT EXISTS keywords (
                photo_id INTEGER NOT NULL REFERENCES photos (id) ON DELETE CASCADE,
                keyword TEXT NOT NULL COLLATE NOCASE);
            CREATE INDEX IF NOT EXISTS keywords_photo ON keywords (photo_id);
            CREATE INDEX IF NOT EXISTS keywords_keyword ON keywords (keyword);
            CREATE VIRTUAL TABLE IF NOT EXISTS photo_text USING fts5 (body, tokenize = 'unicode61 remove_diacritics 2');
            """;
        command.ExecuteNonQuery();
    }

    private static string Normalise(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
    }

    /// <summary>True if <paramref name="path"/> is <paramref name="root"/> or inside it.</summary>
    private static bool IsUnder(string path, string root) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root.EndsWith('\\') ? root : root + "\\", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whole seconds: some file systems store less precision than others, which would look like a change.</summary>
    private static DateTime Truncate(DateTime utc) => new(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
}
