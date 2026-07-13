using System.Globalization;
using System.Text.Json;
using Clyr.Contracts;
using Clyr.Core;
using Microsoft.Data.Sqlite;

namespace Clyr.Persistence;

public sealed class SnapshotStoreException(string code, string message, Exception inner) : Exception(message, inner)
{
    public string Code { get; } = code;
}

public sealed class SqliteSnapshotStore : ISnapshotStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string connectionString;

    public SqliteSnapshotStore(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath, Pooling = false }.ToString();
        new AppMetadataDatabase(connectionString).Migrate();
    }

    public async Task<SnapshotSaveResult> SaveAsync(StorageSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (snapshot.State is not (SnapshotState.Complete or SnapshotState.Partial or SnapshotState.Cancelled))
            return new(false, null, "snapshot.state-ineligible", "Failed or transient snapshots are not persisted.");
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            if (await ExistsAsync(connection, transaction, snapshot.ScanId, cancellationToken).ConfigureAwait(false))
            { await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false); return new(false, null, "snapshot.duplicate", "This scan session is already stored."); }
            await InsertAsync(connection, transaction, snapshot, cancellationToken).ConfigureAwait(false);
            foreach (var item in snapshot.Categories)
                await ChildAsync(connection, transaction, "INSERT INTO SnapshotCategory VALUES($id,$a,$b,$c,$d,$e);", snapshot.Id, cancellationToken,
                    item.Category, item.LogicalBytes, item.FileCount, item.Precision, item.Status).ConfigureAwait(false);
            foreach (var item in snapshot.Findings)
                await ChildAsync(connection, transaction, "INSERT INTO SnapshotFinding VALUES($id,$a,$b,$c,$d,$e,$f,$g);", snapshot.Id, cancellationToken,
                    item.RuleId, item.RuleVersion, item.Category, item.Confidence, item.Status, item.LogicalBytes, item.FileCount).ConfigureAwait(false);
            for (var index = 0; index < snapshot.Warnings.Count; index++)
                await ChildAsync(connection, transaction, "INSERT INTO SnapshotWarning VALUES($id,$a,$b);", snapshot.Id, cancellationToken, index, snapshot.Warnings[index]).ConfigureAwait(false);
            var settings = await ReadSettingsAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            await RetainAsync(connection, transaction, snapshot.Drive.Fingerprint, snapshot.Drive.Root, settings.RetentionPerDrive, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new(true, snapshot.Id, "snapshot.saved", "Aggregate snapshot saved locally.");
        }
        catch (SqliteException exception) { throw Translate(exception); }
    }

    public async Task<IReadOnlyList<SnapshotSummary>> ListAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id,CapturedAtUtc,State,DriveFingerprint,IdentityQuality,Root,FileSystem,Mode,LogicalBytesObserved,UsedBytes,UnknownBytes FROM Snapshot WHERE State IN ('Complete','Partial','Cancelled') ORDER BY CapturedAtUtc DESC,Id LIMIT $limit;";
            Add(command, "$limit", limit); var result = new List<SnapshotSummary>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(new(Guid.Parse(reader.GetString(0)),
                DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), Enum.Parse<SnapshotState>(reader.GetString(2)),
                reader.GetString(3), Enum.Parse<DriveIdentityQuality>(reader.GetString(4)), reader.GetString(5), reader.GetString(6),
                Enum.Parse<ScanMode>(reader.GetString(7)), reader.GetInt64(8), Optional(reader, 9), reader.GetInt64(10)));
            return result;
        }
        catch (SqliteException exception) { throw Translate(exception); }
    }

    public async Task<StorageSnapshot?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand(); command.CommandText = "SELECT * FROM Snapshot WHERE Id=$id AND State IN ('Complete','Partial','Cancelled');"; Add(command, "$id", id.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
            var snapshot = Read(reader); await reader.CloseAsync().ConfigureAwait(false);
            return snapshot with { Categories = await CategoriesAsync(connection, id, cancellationToken).ConfigureAwait(false), Findings = await FindingsAsync(connection, id, cancellationToken).ConfigureAwait(false), Warnings = await WarningsAsync(connection, id, cancellationToken).ConfigureAwait(false) };
        }
        catch (SqliteException exception) { throw Translate(exception); }
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    { try { await using var c = await OpenAsync(cancellationToken).ConfigureAwait(false); await using var cmd = c.CreateCommand(); cmd.CommandText = "DELETE FROM Snapshot WHERE Id=$id;"; Add(cmd, "$id", id.ToString("D")); return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0; } catch (SqliteException e) { throw Translate(e); } }

    public async Task<int> ClearAsync(CancellationToken cancellationToken = default)
    { try { await using var c = await OpenAsync(cancellationToken).ConfigureAwait(false); await using var cmd = c.CreateCommand(); cmd.CommandText = "DELETE FROM Snapshot;"; return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); } catch (SqliteException e) { throw Translate(e); } }

    public async Task<HistorySettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    { try { await using var c = await OpenAsync(cancellationToken).ConfigureAwait(false); return await ReadSettingsAsync(c, null, cancellationToken).ConfigureAwait(false); } catch (SqliteException e) { throw Translate(e); } }

    public async Task SetSettingsAsync(HistorySettings settings, CancellationToken cancellationToken = default)
    {
        if (settings.RetentionPerDrive is < 2 or > 1000) throw new ArgumentOutOfRangeException(nameof(settings), "Retention must be from 2 through 1000.");
        try { await using var c = await OpenAsync(cancellationToken).ConfigureAwait(false); await using var cmd = c.CreateCommand(); cmd.CommandText = "UPDATE HistorySettings SET IsEnabled=$a,RetentionPerDrive=$b,SavePartial=$c,SaveCancelled=$d WHERE Id=1;"; Add(cmd, "$a", settings.IsEnabled ? 1 : 0); Add(cmd, "$b", settings.RetentionPerDrive); Add(cmd, "$c", settings.SavePartial ? 1 : 0); Add(cmd, "$d", settings.SaveCancelled ? 1 : 0); await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); } catch (SqliteException e) { throw Translate(e); }
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken token)
    { SqliteRuntime.Initialize(); var c = new SqliteConnection(connectionString); await c.OpenAsync(token).ConfigureAwait(false); await using var cmd = c.CreateCommand(); cmd.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;"; await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false); return c; }
    private static async Task<bool> ExistsAsync(SqliteConnection c, System.Data.Common.DbTransaction t, Guid scan, CancellationToken token)
    { await using var cmd = c.CreateCommand(); cmd.Transaction = (SqliteTransaction)t; cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM Snapshot WHERE ScanId=$id);"; Add(cmd, "$id", scan.ToString("D")); return Convert.ToInt32(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture) != 0; }
    private static async Task InsertAsync(SqliteConnection c, System.Data.Common.DbTransaction t, StorageSnapshot s, CancellationToken token)
    { await using var cmd = c.CreateCommand(); cmd.Transaction = (SqliteTransaction)t; cmd.CommandText = "INSERT INTO Snapshot VALUES($id,$scan,$schema,$app,$time,$mode,$state,$finger,$quality,$root,$fs,$capacity,$used,$free,$observed,$classified,$unknown,$unaccounted,$coverage,$pack,$packver,$digest);"; object?[] v = [s.Id.ToString("D"), s.ScanId.ToString("D"), s.SchemaVersion, s.ApplicationVersion, s.CapturedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), s.Mode, s.State, s.Drive.Fingerprint, s.Drive.IdentityQuality, s.Drive.Root, s.Drive.FileSystem, s.Drive.CapacityBytes, s.Drive.UsedBytes, s.Drive.FreeBytes, s.LogicalBytesObserved, s.ClassifiedBytes, s.UnknownBytes, s.UnaccountedBytes, JsonSerializer.Serialize(s.Coverage, JsonOptions), s.RulePackId, s.RulePackVersion, s.RulePackDigest]; string[] n = ["$id", "$scan", "$schema", "$app", "$time", "$mode", "$state", "$finger", "$quality", "$root", "$fs", "$capacity", "$used", "$free", "$observed", "$classified", "$unknown", "$unaccounted", "$coverage", "$pack", "$packver", "$digest"]; for (var i = 0; i < n.Length; i++) Add(cmd, n[i], v[i] is Enum ? v[i]!.ToString() : v[i]); await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false); }
    private static async Task ChildAsync(SqliteConnection c, System.Data.Common.DbTransaction t, string sql, Guid id, CancellationToken token, params object[] values)
    { await using var cmd = c.CreateCommand(); cmd.Transaction = (SqliteTransaction)t; cmd.CommandText = sql; Add(cmd, "$id", id.ToString("D")); for (var i = 0; i < values.Length; i++) Add(cmd, "$" + (char)('a' + i), values[i] is Enum ? values[i].ToString() : values[i]); await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false); }
    private static async Task RetainAsync(SqliteConnection c, System.Data.Common.DbTransaction t, string fingerprint, string root, int keep, CancellationToken token)
    { await using var cmd = c.CreateCommand(); cmd.Transaction = (SqliteTransaction)t; cmd.CommandText = "DELETE FROM Snapshot WHERE Id IN (SELECT Id FROM Snapshot WHERE (DriveFingerprint<>'' AND DriveFingerprint=$drive) OR (DriveFingerprint='' AND Root=$root) ORDER BY CapturedAtUtc DESC,Id LIMIT -1 OFFSET $keep);"; Add(cmd, "$drive", fingerprint); Add(cmd, "$root", root); Add(cmd, "$keep", Math.Clamp(keep, 2, 1000)); await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false); }
    private static async Task<HistorySettings> ReadSettingsAsync(SqliteConnection c, System.Data.Common.DbTransaction? t, CancellationToken token)
    { await using var cmd = c.CreateCommand(); cmd.Transaction = (SqliteTransaction?)t; cmd.CommandText = "SELECT IsEnabled,RetentionPerDrive,SavePartial,SaveCancelled FROM HistorySettings WHERE Id=1;"; await using var r = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false); await r.ReadAsync(token).ConfigureAwait(false); return new(r.GetInt64(0) != 0, r.GetInt32(1), r.GetInt64(2) != 0, r.GetInt64(3) != 0); }
    private static StorageSnapshot Read(SqliteDataReader r) => new(Guid.Parse((string)r["Id"]), Guid.Parse((string)r["ScanId"]), Convert.ToInt32(r["SchemaVersion"], CultureInfo.InvariantCulture), (string)r["ApplicationVersion"], DateTimeOffset.Parse((string)r["CapturedAtUtc"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), Enum.Parse<ScanMode>((string)r["Mode"]), Enum.Parse<SnapshotState>((string)r["State"]), new((string)r["DriveFingerprint"], Enum.Parse<DriveIdentityQuality>((string)r["IdentityQuality"]), (string)r["Root"], (string)r["FileSystem"], DbLong(r["CapacityBytes"]), DbLong(r["UsedBytes"]), DbLong(r["FreeBytes"])), Convert.ToInt64(r["LogicalBytesObserved"], CultureInfo.InvariantCulture), Convert.ToInt64(r["ClassifiedBytes"], CultureInfo.InvariantCulture), Convert.ToInt64(r["UnknownBytes"], CultureInfo.InvariantCulture), DbLong(r["UnaccountedBytes"]), JsonSerializer.Deserialize<ScanCoverage>((string)r["CoverageJson"], JsonOptions)!, (string)r["RulePackId"], (string)r["RulePackVersion"], (string)r["RulePackDigest"], [], [], []);
    private static async Task<IReadOnlyList<SnapshotCategory>> CategoriesAsync(SqliteConnection c, Guid id, CancellationToken token)
    { var x = new List<SnapshotCategory>(); await using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT Category,LogicalBytes,FileCount,Precision,Status FROM SnapshotCategory WHERE SnapshotId=$id ORDER BY Category;"; Add(cmd, "$id", id.ToString("D")); await using var r = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false); while (await r.ReadAsync(token).ConfigureAwait(false)) x.Add(new(Enum.Parse<StorageCategory>(r.GetString(0)), r.GetInt64(1), r.GetInt64(2), Enum.Parse<MeasurementPrecision>(r.GetString(3)), Enum.Parse<FindingStatus>(r.GetString(4)))); return x; }
    private static async Task<IReadOnlyList<SnapshotFinding>> FindingsAsync(SqliteConnection c, Guid id, CancellationToken token)
    { var x = new List<SnapshotFinding>(); await using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT RuleId,RuleVersion,Category,Confidence,Status,LogicalBytes,FileCount FROM SnapshotFinding WHERE SnapshotId=$id ORDER BY RuleId;"; Add(cmd, "$id", id.ToString("D")); await using var r = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false); while (await r.ReadAsync(token).ConfigureAwait(false)) x.Add(new(r.GetString(0), r.GetString(1), Enum.Parse<StorageCategory>(r.GetString(2)), Enum.Parse<FindingConfidence>(r.GetString(3)), Enum.Parse<FindingStatus>(r.GetString(4)), r.GetInt64(5), r.GetInt64(6))); return x; }
    private static async Task<IReadOnlyList<string>> WarningsAsync(SqliteConnection c, Guid id, CancellationToken token)
    { var x = new List<string>(); await using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT Warning FROM SnapshotWarning WHERE SnapshotId=$id ORDER BY Ordinal;"; Add(cmd, "$id", id.ToString("D")); await using var r = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false); while (await r.ReadAsync(token).ConfigureAwait(false)) x.Add(r.GetString(0)); return x; }
    private static long? Optional(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt64(i); private static long? DbLong(object value) => value is DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    private static void Add(SqliteCommand command, string name, object? value) => command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    private static SnapshotStoreException Translate(SqliteException e) => new(e.SqliteErrorCode is 11 or 26 ? "snapshot.database-corrupt" : "snapshot.database-error", e.SqliteErrorCode is 11 or 26 ? "Snapshot history appears corrupted; CLYR did not delete or replace it." : "Snapshot history could not be accessed safely.", e);
}
