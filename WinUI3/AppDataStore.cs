using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace ModFolderCopier.WinUI;

public sealed class AppDataStore
{
    private readonly string _databasePath;
    private readonly object _sync = new();
    private bool _available;

    public AppDataStore(string databasePath)
    {
        _databasePath = databasePath;
    }

    public bool IsAvailable => _available;

    public void Initialize()
    {
        lock (_sync)
        {
            try
            {
                string? directory = Path.GetDirectoryName(_databasePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using SqliteConnection connection = OpenConnection();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    PRAGMA journal_mode = WAL;
                    PRAGMA synchronous = NORMAL;
                    PRAGMA foreign_keys = ON;
                    PRAGMA busy_timeout = 3000;

                    CREATE TABLE IF NOT EXISTS CacheEntries (
                        CacheKind TEXT NOT NULL,
                        CacheKey TEXT NOT NULL,
                        Json TEXT NOT NULL,
                        CachedAtUtc TEXT NOT NULL,
                        PRIMARY KEY (CacheKind, CacheKey)
                    );

                    CREATE TABLE IF NOT EXISTS CharacterFavorites (
                        CatalogKey TEXT NOT NULL,
                        CharacterKey TEXT NOT NULL,
                        FavoritedAtUtc TEXT NOT NULL,
                        PRIMARY KEY (CatalogKey, CharacterKey)
                    );

                    CREATE TABLE IF NOT EXISTS ModFolders (
                        RepositoryId TEXT NOT NULL,
                        SourceRoot TEXT NOT NULL,
                        FirstLevelPath TEXT NOT NULL,
                        ModPath TEXT NOT NULL,
                        FolderStampUtc TEXT NOT NULL,
                        FileCount INTEGER NOT NULL,
                        TotalBytes INTEGER NOT NULL,
                        FilesJson TEXT NOT NULL,
                        IndexedAtUtc TEXT NOT NULL,
                        PRIMARY KEY (RepositoryId, ModPath)
                    );

                    CREATE INDEX IF NOT EXISTS IX_ModFolders_Repository_FirstLevel
                        ON ModFolders (RepositoryId, FirstLevelPath);
                    """;
                command.ExecuteNonQuery();
                _available = true;
            }
            catch
            {
                _available = false;
            }
        }
    }

    public CachedValue<T>? TryReadCache<T>(string kind, string key, TimeSpan? maxAge, bool allowExpired = false)
    {
        if (!_available)
        {
            return null;
        }

        lock (_sync)
        {
            try
            {
                using SqliteConnection connection = OpenConnection();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    SELECT Json, CachedAtUtc
                    FROM CacheEntries
                    WHERE CacheKind = $kind AND CacheKey = $key;
                    """;
                command.Parameters.AddWithValue("$kind", kind);
                command.Parameters.AddWithValue("$key", key);
                using SqliteDataReader reader = command.ExecuteReader();
                if (!reader.Read()
                    || !DateTimeOffset.TryParse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset cachedAt))
                {
                    return null;
                }

                bool expired = maxAge.HasValue && DateTimeOffset.UtcNow - cachedAt > maxAge.Value;
                if (expired && !allowExpired)
                {
                    return null;
                }

                T? value = JsonSerializer.Deserialize<T>(reader.GetString(0));
                return value is null ? null : new CachedValue<T>(value, cachedAt, expired);
            }
            catch
            {
                return null;
            }
        }
    }

    public void WriteCache<T>(string kind, string key, T value, DateTimeOffset? cachedAt = null)
    {
        if (!_available)
        {
            return;
        }

        lock (_sync)
        {
            try
            {
                using SqliteConnection connection = OpenConnection();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO CacheEntries (CacheKind, CacheKey, Json, CachedAtUtc)
                    VALUES ($kind, $key, $json, $cachedAt)
                    ON CONFLICT(CacheKind, CacheKey) DO UPDATE SET
                        Json = excluded.Json,
                        CachedAtUtc = excluded.CachedAtUtc;
                    """;
                command.Parameters.AddWithValue("$kind", kind);
                command.Parameters.AddWithValue("$key", key);
                command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(value));
                command.Parameters.AddWithValue("$cachedAt", (cachedAt ?? DateTimeOffset.UtcNow).ToString("O"));
                command.ExecuteNonQuery();
            }
            catch
            {
                // File caches remain available as a compatibility fallback.
            }
        }
    }

    public HashSet<string> ReadFavorites()
    {
        HashSet<string> result = new(StringComparer.OrdinalIgnoreCase);
        if (!_available)
        {
            return result;
        }

        lock (_sync)
        {
            try
            {
                using SqliteConnection connection = OpenConnection();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "SELECT CatalogKey, CharacterKey FROM CharacterFavorites;";
                using SqliteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    result.Add(BuildFavoriteKey(reader.GetString(0), reader.GetString(1)));
                }
            }
            catch
            {
            }
        }

        return result;
    }

    public void SetFavorite(string catalogKey, string characterKey, bool isFavorite)
    {
        if (!_available || string.IsNullOrWhiteSpace(catalogKey) || string.IsNullOrWhiteSpace(characterKey))
        {
            return;
        }

        lock (_sync)
        {
            try
            {
                using SqliteConnection connection = OpenConnection();
                using SqliteCommand command = connection.CreateCommand();
                if (isFavorite)
                {
                    command.CommandText = """
                        INSERT INTO CharacterFavorites (CatalogKey, CharacterKey, FavoritedAtUtc)
                        VALUES ($catalogKey, $characterKey, $favoritedAt)
                        ON CONFLICT(CatalogKey, CharacterKey) DO UPDATE SET
                            FavoritedAtUtc = excluded.FavoritedAtUtc;
                        """;
                    command.Parameters.AddWithValue("$favoritedAt", DateTimeOffset.UtcNow.ToString("O"));
                }
                else
                {
                    command.CommandText = """
                        DELETE FROM CharacterFavorites
                        WHERE CatalogKey = $catalogKey AND CharacterKey = $characterKey;
                        """;
                }

                command.Parameters.AddWithValue("$catalogKey", catalogKey);
                command.Parameters.AddWithValue("$characterKey", characterKey);
                command.ExecuteNonQuery();
            }
            catch
            {
            }
        }
    }

    public List<IndexedModFolder> ReadModIndex(string repositoryId, string sourceRoot)
    {
        List<IndexedModFolder> result = [];
        if (!_available)
        {
            return result;
        }

        lock (_sync)
        {
            try
            {
                using SqliteConnection connection = OpenConnection();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    SELECT FirstLevelPath, ModPath, FolderStampUtc, FileCount, TotalBytes, FilesJson
                    FROM ModFolders
                    WHERE RepositoryId = $repositoryId AND SourceRoot = $sourceRoot
                    ORDER BY FirstLevelPath COLLATE NOCASE, ModPath COLLATE NOCASE;
                    """;
                command.Parameters.AddWithValue("$repositoryId", repositoryId);
                command.Parameters.AddWithValue("$sourceRoot", sourceRoot);
                using SqliteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    result.Add(new IndexedModFolder
                    {
                        FirstLevelPath = reader.GetString(0),
                        ModPath = reader.GetString(1),
                        FolderStampUtc = reader.GetString(2),
                        FileCount = reader.GetInt32(3),
                        TotalBytes = reader.GetInt64(4),
                        Files = JsonSerializer.Deserialize<List<string>>(reader.GetString(5)) ?? []
                    });
                }
            }
            catch
            {
                return [];
            }
        }

        return result;
    }

    public void ReplaceModIndex(string repositoryId, string sourceRoot, IReadOnlyCollection<IndexedModFolder> folders)
    {
        if (!_available)
        {
            return;
        }

        lock (_sync)
        {
            try
            {
                using SqliteConnection connection = OpenConnection();
                using SqliteTransaction transaction = connection.BeginTransaction();
                using (SqliteCommand delete = connection.CreateCommand())
                {
                    delete.Transaction = transaction;
                    delete.CommandText = "DELETE FROM ModFolders WHERE RepositoryId = $repositoryId;";
                    delete.Parameters.AddWithValue("$repositoryId", repositoryId);
                    delete.ExecuteNonQuery();
                }

                foreach (IndexedModFolder folder in folders)
                {
                    using SqliteCommand insert = connection.CreateCommand();
                    insert.Transaction = transaction;
                    insert.CommandText = """
                        INSERT INTO ModFolders (
                            RepositoryId, SourceRoot, FirstLevelPath, ModPath, FolderStampUtc,
                            FileCount, TotalBytes, FilesJson, IndexedAtUtc)
                        VALUES (
                            $repositoryId, $sourceRoot, $firstLevelPath, $modPath, $folderStampUtc,
                            $fileCount, $totalBytes, $filesJson, $indexedAtUtc);
                        """;
                    insert.Parameters.AddWithValue("$repositoryId", repositoryId);
                    insert.Parameters.AddWithValue("$sourceRoot", sourceRoot);
                    insert.Parameters.AddWithValue("$firstLevelPath", folder.FirstLevelPath);
                    insert.Parameters.AddWithValue("$modPath", folder.ModPath);
                    insert.Parameters.AddWithValue("$folderStampUtc", folder.FolderStampUtc);
                    insert.Parameters.AddWithValue("$fileCount", folder.FileCount);
                    insert.Parameters.AddWithValue("$totalBytes", folder.TotalBytes);
                    insert.Parameters.AddWithValue("$filesJson", JsonSerializer.Serialize(folder.Files));
                    insert.Parameters.AddWithValue("$indexedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
                    insert.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            catch
            {
            }
        }
    }

    public string GetHealthSummary()
    {
        if (!_available)
        {
            return "SQLite unavailable";
        }

        lock (_sync)
        {
            try
            {
                using SqliteConnection connection = OpenConnection();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "PRAGMA quick_check;";
                string result = Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture) ?? "unknown";
                long size = File.Exists(_databasePath) ? new FileInfo(_databasePath).Length : 0;
                return $"SQLite {result}; {size} bytes";
            }
            catch (Exception ex)
            {
                return "SQLite check failed: " + ex.GetType().Name;
            }
        }
    }

    public static string BuildFavoriteKey(string catalogKey, string characterKey)
        => catalogKey.Trim() + "\u001f" + characterKey.Trim();

    private SqliteConnection OpenConnection()
    {
        SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout = 3000;";
        command.ExecuteNonQuery();
        return connection;
    }
}

public sealed record CachedValue<T>(T Value, DateTimeOffset CachedAtUtc, bool IsExpired);

public sealed class IndexedModFolder
{
    public string FirstLevelPath { get; set; } = string.Empty;

    public string ModPath { get; set; } = string.Empty;

    public string FolderStampUtc { get; set; } = string.Empty;

    public int FileCount { get; set; }

    public long TotalBytes { get; set; }

    public List<string> Files { get; set; } = [];
}
