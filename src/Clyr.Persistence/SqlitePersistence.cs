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
    public const int CurrentSchemaVersion = 1;
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
