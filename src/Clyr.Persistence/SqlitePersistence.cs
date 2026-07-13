using Microsoft.Data.Sqlite;

namespace Clyr.Persistence;

public static class SqliteRuntime
{
    private static readonly object Sync = new();
    private static bool initialized;

    public static void Initialize()
    {
        if (initialized) return;
        lock (Sync)
        {
            if (initialized) return;
            SQLitePCL.Batteries_V2.Init();
            initialized = true;
        }
    }
}

public sealed class AppMetadataDatabase
{
    public const int CurrentSchemaVersion = 2;
    private readonly string connectionString;

    public AppMetadataDatabase(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var builder = new SqliteConnectionStringBuilder(connectionString) { Pooling = false };
        this.connectionString = builder.ToString();
    }

    public void Migrate()
    {
        SqliteRuntime.Initialize();
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "CREATE TABLE IF NOT EXISTS SchemaInfo (Version INTEGER NOT NULL);";
        command.ExecuteNonQuery();
        command.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM SchemaInfo;";
        var version = Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        if (version > CurrentSchemaVersion) throw new InvalidOperationException("The database schema is newer than this application supports.");
        if (version == 0)
        {
            command.CommandText = "CREATE TABLE AppMetadata (Key TEXT PRIMARY KEY, Value TEXT NOT NULL); INSERT INTO SchemaInfo(Version) VALUES (1);";
            command.ExecuteNonQuery();
            version = 1;
        }
        if (version == 1)
        {
            command.CommandText = """
                CREATE TABLE Snapshot (Id TEXT PRIMARY KEY, ScanId TEXT NOT NULL UNIQUE, SchemaVersion INTEGER NOT NULL,
                    ApplicationVersion TEXT NOT NULL, CapturedAtUtc TEXT NOT NULL, Mode TEXT NOT NULL, State TEXT NOT NULL,
                    DriveFingerprint TEXT NOT NULL, IdentityQuality TEXT NOT NULL, Root TEXT NOT NULL, FileSystem TEXT NOT NULL,
                    CapacityBytes INTEGER, UsedBytes INTEGER, FreeBytes INTEGER, LogicalBytesObserved INTEGER NOT NULL,
                    ClassifiedBytes INTEGER NOT NULL, UnknownBytes INTEGER NOT NULL, UnaccountedBytes INTEGER,
                    CoverageJson TEXT NOT NULL, RulePackId TEXT NOT NULL, RulePackVersion TEXT NOT NULL, RulePackDigest TEXT NOT NULL);
                CREATE TABLE SnapshotCategory (SnapshotId TEXT NOT NULL REFERENCES Snapshot(Id) ON DELETE CASCADE,
                    Category TEXT NOT NULL, LogicalBytes INTEGER NOT NULL, FileCount INTEGER NOT NULL, Precision TEXT NOT NULL,
                    Status TEXT NOT NULL, PRIMARY KEY (SnapshotId, Category));
                CREATE TABLE SnapshotFinding (SnapshotId TEXT NOT NULL REFERENCES Snapshot(Id) ON DELETE CASCADE,
                    RuleId TEXT NOT NULL, RuleVersion TEXT NOT NULL, Category TEXT NOT NULL, Confidence TEXT NOT NULL,
                    Status TEXT NOT NULL, LogicalBytes INTEGER NOT NULL, FileCount INTEGER NOT NULL,
                    PRIMARY KEY (SnapshotId, RuleId, RuleVersion));
                CREATE TABLE SnapshotWarning (SnapshotId TEXT NOT NULL REFERENCES Snapshot(Id) ON DELETE CASCADE,
                    Ordinal INTEGER NOT NULL, Warning TEXT NOT NULL, PRIMARY KEY (SnapshotId, Ordinal));
                CREATE TABLE HistorySettings (Id INTEGER PRIMARY KEY CHECK (Id = 1), IsEnabled INTEGER NOT NULL,
                    RetentionPerDrive INTEGER NOT NULL, SavePartial INTEGER NOT NULL, SaveCancelled INTEGER NOT NULL);
                INSERT INTO HistorySettings VALUES (1, 1, 20, 1, 1);
                CREATE INDEX IX_Snapshot_DriveTime ON Snapshot(DriveFingerprint, CapturedAtUtc DESC);
                CREATE INDEX IX_Snapshot_StateTime ON Snapshot(State, CapturedAtUtc DESC);
                UPDATE SchemaInfo SET Version = 2;
                """;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public string GetSqliteVersion()
    {
        SqliteRuntime.Initialize();
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_version();";
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
