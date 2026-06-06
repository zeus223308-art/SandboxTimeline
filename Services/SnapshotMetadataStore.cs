using Microsoft.Data.Sqlite;

namespace SandboxTimeline;

public sealed class SnapshotMetadataStore : IDisposable
{
    private readonly string _dbPath;
    private readonly object _lock = new();

    public SnapshotMetadataStore()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SandboxTimeline");
        Directory.CreateDirectory(appData);
        _dbPath = Path.Combine(appData, "snapshots.db");
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        lock (_lock)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS Snapshots (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    SnapshotNumber INTEGER NOT NULL DEFAULT 0,
                    Label TEXT NOT NULL,
                    VolumePath TEXT NOT NULL,
                    ShadowCopyId TEXT NOT NULL,
                    ShadowDevicePath TEXT NOT NULL,
                    RegistryBackupPath TEXT NOT NULL,
                    ManifestPath TEXT NOT NULL DEFAULT '',
                    SnapshotFolder TEXT NOT NULL DEFAULT '',
                    CreatedAtUtc TEXT NOT NULL,
                    SizeBytes INTEGER NOT NULL DEFAULT 0,
                    ChangedFileCount INTEGER NOT NULL DEFAULT 0,
                    UsedUsnJournal INTEGER NOT NULL DEFAULT 0,
                    IsPremiumSnapshot INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS IX_Snapshots_CreatedAtUtc ON Snapshots(CreatedAtUtc DESC);
                """;
            command.ExecuteNonQuery();

            EnsureColumn(connection, "SnapshotNumber", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(connection, "ManifestPath", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(connection, "SnapshotFolder", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(connection, "ChangedFileCount", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(connection, "UsedUsnJournal", "INTEGER NOT NULL DEFAULT 0");
        }
    }

    private static void EnsureColumn(SqliteConnection connection, string column, string definition)
    {
        using var check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info(Snapshots);";
        using var reader = check.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE Snapshots ADD COLUMN {column} {definition};";
        try
        {
            alter.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
        }
    }

    public int GetNextSnapshotNumber()
    {
        lock (_lock)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT IFNULL(MAX(SnapshotNumber), 0) + 1 FROM Snapshots;";
            return Convert.ToInt32(command.ExecuteScalar() ?? 1);
        }
    }

    public long Insert(SnapshotInfo snapshot)
    {
        lock (_lock)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO Snapshots (
                    SnapshotNumber, Label, VolumePath, ShadowCopyId, ShadowDevicePath,
                    RegistryBackupPath, ManifestPath, SnapshotFolder, CreatedAtUtc,
                    SizeBytes, ChangedFileCount, UsedUsnJournal, IsPremiumSnapshot)
                VALUES (
                    $num, $label, $volume, $shadowId, $device, $registry, $manifest, $folder,
                    $created, $size, $changed, $usn, $premium);
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("$num", snapshot.SnapshotNumber);
            command.Parameters.AddWithValue("$label", snapshot.Label);
            command.Parameters.AddWithValue("$volume", snapshot.VolumePath);
            command.Parameters.AddWithValue("$shadowId", snapshot.ShadowCopyId);
            command.Parameters.AddWithValue("$device", snapshot.ShadowDevicePath);
            command.Parameters.AddWithValue("$registry", snapshot.RegistryBackupPath);
            command.Parameters.AddWithValue("$manifest", snapshot.ManifestPath);
            command.Parameters.AddWithValue("$folder", snapshot.SnapshotFolder);
            command.Parameters.AddWithValue("$created", snapshot.CreatedAtUtc.ToString("O"));
            command.Parameters.AddWithValue("$size", snapshot.SizeBytes);
            command.Parameters.AddWithValue("$changed", snapshot.ChangedFileCount);
            command.Parameters.AddWithValue("$usn", snapshot.UsedUsnJournal ? 1 : 0);
            command.Parameters.AddWithValue("$premium", snapshot.IsPremiumSnapshot ? 1 : 0);
            return (long)(command.ExecuteScalar() ?? 0L);
        }
    }

    public IReadOnlyList<SnapshotInfo> GetAll()
    {
        lock (_lock)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM Snapshots ORDER BY CreatedAtUtc DESC;";
            using var reader = command.ExecuteReader();
            var list = new List<SnapshotInfo>();
            while (reader.Read())
            {
                list.Add(Map(reader));
            }
            return list;
        }
    }

    public SnapshotInfo? GetLatest()
    {
        lock (_lock)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM Snapshots ORDER BY CreatedAtUtc DESC LIMIT 1;";
            using var reader = command.ExecuteReader();
            return reader.Read() ? Map(reader) : null;
        }
    }

    public SnapshotInfo? GetById(long id)
    {
        lock (_lock)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM Snapshots WHERE Id = $id;";
            command.Parameters.AddWithValue("$id", id);
            using var reader = command.ExecuteReader();
            return reader.Read() ? Map(reader) : null;
        }
    }

    public void Delete(long id)
    {
        lock (_lock)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Snapshots WHERE Id = $id;";
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }
    }

    private static SnapshotInfo Map(SqliteDataReader reader)
    {
        return new SnapshotInfo
        {
            Id = reader.GetInt64(reader.GetOrdinal("Id")),
            SnapshotNumber = TryGetInt(reader, "SnapshotNumber"),
            Label = reader.GetString(reader.GetOrdinal("Label")),
            VolumePath = reader.GetString(reader.GetOrdinal("VolumePath")),
            ShadowCopyId = reader.GetString(reader.GetOrdinal("ShadowCopyId")),
            ShadowDevicePath = reader.GetString(reader.GetOrdinal("ShadowDevicePath")),
            RegistryBackupPath = reader.GetString(reader.GetOrdinal("RegistryBackupPath")),
            ManifestPath = TryGetString(reader, "ManifestPath"),
            SnapshotFolder = TryGetString(reader, "SnapshotFolder"),
            CreatedAtUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAtUtc"))).ToUniversalTime(),
            SizeBytes = reader.GetInt64(reader.GetOrdinal("SizeBytes")),
            ChangedFileCount = TryGetInt(reader, "ChangedFileCount"),
            UsedUsnJournal = TryGetInt(reader, "UsedUsnJournal") == 1,
            IsPremiumSnapshot = reader.GetInt64(reader.GetOrdinal("IsPremiumSnapshot")) == 1
        };
    }

    private static int TryGetInt(SqliteDataReader reader, string column)
    {
        try
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? 0 : reader.GetInt32(ordinal);
        }
        catch (IndexOutOfRangeException)
        {
            return 0;
        }
    }

    private static string TryGetString(SqliteDataReader reader, string column)
    {
        try
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
        }
        catch (IndexOutOfRangeException)
        {
            return string.Empty;
        }
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        return connection;
    }

    public void Dispose()
    {
    }
}
