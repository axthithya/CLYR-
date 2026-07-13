using Clyr.Persistence;
using Microsoft.Data.Sqlite;
using Xunit.Abstractions;

namespace Clyr.Persistence.Tests;

public sealed class PersistenceTests(ITestOutputHelper output)
{
    [Fact]
    public void NativeSqliteInitializesAndReportsVersion()
    {
        SqliteRuntime.Initialize();
        var database = new AppMetadataDatabase("Data Source=:memory:");
        var version = database.GetSqliteVersion();
        output.WriteLine("Native SQLite version: " + version);
        Assert.True(Version.Parse(version) >= new Version(3, 50, 2));
    }

    [Fact]
    public void MigrationsAreIdempotent()
    {
        var path = TemporaryDatabasePath();
        try
        {
            var database = new AppMetadataDatabase("Data Source=" + path);
            database.Migrate();
            database.Migrate();
            Assert.True(File.Exists(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void ConcurrentInitializationIsSafe()
    {
        Parallel.For(0, 32, _ => SqliteRuntime.Initialize());
    }

    [Fact]
    public void NewerSchemaIsRejected()
    {
        var path = TemporaryDatabasePath();
        try
        {
            var database = new AppMetadataDatabase("Data Source=" + path);
            database.Migrate();
            using (var connection = new SqliteConnection("Data Source=" + path + ";Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE SchemaInfo SET Version = 999;";
                command.ExecuteNonQuery();
            }
            Assert.Throws<InvalidOperationException>(database.Migrate);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static string TemporaryDatabasePath()
    {
        return Path.Combine(Path.GetTempPath(), "clyr-test-" + Guid.NewGuid().ToString("N") + ".db");
    }
}
