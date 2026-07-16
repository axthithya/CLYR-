using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Clyr.Contracts;
using Microsoft.Data.Sqlite;

namespace Clyr.Persistence;

public sealed class ExecutionReceiptStoreException(string code, string message, Exception inner) : Exception(message, inner)
{
    public string Code { get; } = code;
}

public interface IExecutionReceiptStore
{
    Task SaveAsync(ExecutionReceipt receipt, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExecutionReceiptSummary>> ListAsync(int limit = 50, CancellationToken cancellationToken = default);
    Task<ExecutionReceipt?> GetAsync(ExecutionId id, CancellationToken cancellationToken = default);
    Task<bool> DiscardAsync(ExecutionId id, CancellationToken cancellationToken = default);

    /// <summary>Marks any receipt left in an in-flight state past <paramref name="staleAfter"/> as Interrupted.
    /// This never guesses success — an abandoned "Running" row can only ever become Interrupted, never Completed.</summary>
    Task<int> ReconcileInterruptedAsync(TimeSpan staleAfter, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
}

/// <summary>
/// CLYR-owned local SQLite storage for execution receipts. Receipts are immutable once their final state is a
/// terminal one (Completed/PartiallyCompleted/Cancelled/Failed/Interrupted/UnknownOutcome/Rejected) — <see cref="SaveAsync"/>
/// upserts by execution ID so a future "started" placeholder row could be updated in place, but this store never
/// rewrites a row that already recorded a terminal state. No raw file paths and no reusable execution token or
/// authority are persisted; only the privacy-safe accounting fields defined on <see cref="ExecutionReceipt"/>.
/// </summary>
public sealed class SqliteExecutionReceiptStore : IExecutionReceiptStore
{
    private const int RetentionLimit = 200;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] TerminalStates =
    [
        nameof(ExecutionState.Completed), nameof(ExecutionState.PartiallyCompleted), nameof(ExecutionState.Cancelled),
        nameof(ExecutionState.Failed), nameof(ExecutionState.Interrupted), nameof(ExecutionState.UnknownOutcome),
        nameof(ExecutionState.Rejected)
    ];
    private readonly string connectionString;

    public SqliteExecutionReceiptStore(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath, Pooling = false }.ToString();
        new AppMetadataDatabase(connectionString).Migrate();
    }

    public async Task SaveAsync(ExecutionReceipt receipt, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using var existing = connection.CreateCommand();
            existing.Transaction = (SqliteTransaction)transaction;
            existing.CommandText = "SELECT FinalState FROM ExecutionReceipt WHERE Id=$id;";
            Add(existing, "$id", receipt.ExecutionId.ToString());
            var current = await existing.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
            if (current is not null && TerminalStates.Contains(current, StringComparer.Ordinal))
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw new ExecutionReceiptStoreException("receipt.immutable", "A terminal execution receipt cannot be overwritten.", new InvalidOperationException());
            }

            await using var upsert = connection.CreateCommand();
            upsert.Transaction = (SqliteTransaction)transaction;
            upsert.CommandText = """
                INSERT INTO ExecutionReceipt VALUES($id,$schema,$plan,$digest,$app,$pack,$drive,$start,$end,$state,
                    $cancelled,$elevated,$total,$removed,$skipped,$failed,$planned,$removedb,$skippedb,$failedb,
                    $freebefore,$freeafter,$delta,$categories,$warnings,$limitations,$privacy,$receiptdigest)
                ON CONFLICT(Id) DO UPDATE SET CompletedAtUtc=excluded.CompletedAtUtc, FinalState=excluded.FinalState,
                    Cancelled=excluded.Cancelled, TotalItems=excluded.TotalItems, RemovedCount=excluded.RemovedCount,
                    SkippedCount=excluded.SkippedCount, FailedCount=excluded.FailedCount,
                    PlannedLogicalBytes=excluded.PlannedLogicalBytes, RemovedLogicalBytes=excluded.RemovedLogicalBytes,
                    SkippedLogicalBytes=excluded.SkippedLogicalBytes, FailedLogicalBytes=excluded.FailedLogicalBytes,
                    DriveFreeBytesBefore=excluded.DriveFreeBytesBefore, DriveFreeBytesAfter=excluded.DriveFreeBytesAfter,
                    ObservedFreeSpaceDeltaBytes=excluded.ObservedFreeSpaceDeltaBytes,
                    OutcomeCategoriesJson=excluded.OutcomeCategoriesJson, WarningsJson=excluded.WarningsJson,
                    LimitationsJson=excluded.LimitationsJson, Digest=excluded.Digest;
                """;
            Bind(upsert, receipt);
            await upsert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            await using var retain = connection.CreateCommand();
            retain.Transaction = (SqliteTransaction)transaction;
            retain.CommandText = "DELETE FROM ExecutionReceipt WHERE Id IN (SELECT Id FROM ExecutionReceipt ORDER BY StartedAtUtc DESC, Id LIMIT -1 OFFSET $keep);";
            Add(retain, "$keep", RetentionLimit);
            await retain.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException exception) { throw Translate(exception); }
    }

    public async Task<IReadOnlyList<ExecutionReceiptSummary>> ListAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id,SourcePlanId,StartedAtUtc,CompletedAtUtc,FinalState,RemovedCount,SkippedCount,FailedCount,RemovedLogicalBytes FROM ExecutionReceipt ORDER BY StartedAtUtc DESC, Id LIMIT $limit;";
            Add(command, "$limit", limit);
            var result = new List<ExecutionReceiptSummary>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                result.Add(new(new ExecutionId(Guid.Parse(reader.GetString(0))), new CleanupPlanId(Guid.Parse(reader.GetString(1))),
                    ParseTime(reader.GetString(2)), reader.IsDBNull(3) ? null : ParseTime(reader.GetString(3)),
                    Enum.Parse<ExecutionState>(reader.GetString(4)), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetInt64(8)));
            return result;
        }
        catch (SqliteException exception) { throw Translate(exception); }
    }

    public async Task<ExecutionReceipt?> GetAsync(ExecutionId id, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM ExecutionReceipt WHERE Id=$id;";
            Add(command, "$id", id.ToString());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
        }
        catch (SqliteException exception) { throw Translate(exception); }
    }

    public async Task<bool> DiscardAsync(ExecutionId id, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM ExecutionReceipt WHERE Id=$id;";
            Add(command, "$id", id.ToString());
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
        }
        catch (SqliteException exception) { throw Translate(exception); }
    }

    public async Task<int> ReconcileInterruptedAsync(TimeSpan staleAfter, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE ExecutionReceipt SET FinalState='Interrupted', CompletedAtUtc=$now
                WHERE CompletedAtUtc IS NULL AND FinalState NOT IN ('Completed','PartiallyCompleted','Cancelled','Failed','Interrupted','UnknownOutcome','Rejected')
                AND StartedAtUtc <= $threshold;
                """;
            Add(command, "$now", nowUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            Add(command, "$threshold", (nowUtc - staleAfter).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException exception) { throw Translate(exception); }
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken token)
    {
        SqliteRuntime.Initialize();
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(token).ConfigureAwait(false);
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        await pragma.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        return connection;
    }

    private static void Bind(SqliteCommand command, ExecutionReceipt receipt)
    {
        Add(command, "$id", receipt.ExecutionId.ToString());
        Add(command, "$schema", receipt.SchemaVersion);
        Add(command, "$plan", receipt.SourcePlanId.ToString());
        Add(command, "$digest", receipt.SourcePlanDigest);
        Add(command, "$app", receipt.ApplicationVersion);
        Add(command, "$pack", receipt.RulePackVersion);
        Add(command, "$drive", receipt.DriveIdentityFingerprint);
        Add(command, "$start", receipt.StartedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Add(command, "$end", receipt.CompletedAtUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Add(command, "$state", receipt.FinalState.ToString());
        Add(command, "$cancelled", receipt.Cancelled ? 1 : 0);
        Add(command, "$elevated", receipt.ElevationUsed ? 1 : 0);
        Add(command, "$total", receipt.Summary.TotalItems);
        Add(command, "$removed", receipt.Summary.RemovedCount);
        Add(command, "$skipped", receipt.Summary.SkippedCount);
        Add(command, "$failed", receipt.Summary.FailedCount);
        Add(command, "$planned", receipt.Summary.PlannedLogicalBytes);
        Add(command, "$removedb", receipt.Summary.RemovedLogicalBytes);
        Add(command, "$skippedb", receipt.Summary.SkippedLogicalBytes);
        Add(command, "$failedb", receipt.Summary.FailedLogicalBytes);
        Add(command, "$freebefore", receipt.DriveFreeBytesBefore);
        Add(command, "$freeafter", receipt.DriveFreeBytesAfter);
        Add(command, "$delta", receipt.ObservedFreeSpaceDeltaBytes);
        Add(command, "$categories", JsonSerializer.Serialize(receipt.OutcomeCategories, JsonOptions));
        Add(command, "$warnings", JsonSerializer.Serialize(receipt.Warnings, JsonOptions));
        Add(command, "$limitations", JsonSerializer.Serialize(receipt.Limitations, JsonOptions));
        Add(command, "$privacy", receipt.PrivacyMode);
        Add(command, "$receiptdigest", receipt.Digest);
    }

    private static ExecutionReceipt Read(SqliteDataReader reader)
    {
        var categories = JsonSerializer.Deserialize<Dictionary<string, int>>((string)reader["OutcomeCategoriesJson"], JsonOptions) ?? [];
        var warnings = JsonSerializer.Deserialize<string[]>((string)reader["WarningsJson"], JsonOptions) ?? [];
        var limitations = JsonSerializer.Deserialize<string[]>((string)reader["LimitationsJson"], JsonOptions) ?? [];
        var summary = new ExecutionSummary(Convert.ToInt32(reader["TotalItems"], CultureInfo.InvariantCulture),
            Convert.ToInt32(reader["RemovedCount"], CultureInfo.InvariantCulture), Convert.ToInt32(reader["SkippedCount"], CultureInfo.InvariantCulture),
            Convert.ToInt32(reader["FailedCount"], CultureInfo.InvariantCulture), Convert.ToInt64(reader["PlannedLogicalBytes"], CultureInfo.InvariantCulture),
            Convert.ToInt64(reader["RemovedLogicalBytes"], CultureInfo.InvariantCulture), Convert.ToInt64(reader["SkippedLogicalBytes"], CultureInfo.InvariantCulture),
            Convert.ToInt64(reader["FailedLogicalBytes"], CultureInfo.InvariantCulture));
        return new ExecutionReceipt(Convert.ToInt32(reader["SchemaVersion"], CultureInfo.InvariantCulture),
            new ExecutionId(Guid.Parse((string)reader["Id"])), new CleanupPlanId(Guid.Parse((string)reader["SourcePlanId"])),
            (string)reader["SourcePlanDigest"], (string)reader["ApplicationVersion"], (string)reader["RulePackVersion"],
            (string)reader["DriveIdentityFingerprint"], ParseTime((string)reader["StartedAtUtc"]),
            reader["CompletedAtUtc"] is DBNull ? null : ParseTime((string)reader["CompletedAtUtc"]),
            Enum.Parse<ExecutionState>((string)reader["FinalState"]), Convert.ToInt64(reader["Cancelled"], CultureInfo.InvariantCulture) != 0,
            Convert.ToInt64(reader["ElevationUsed"], CultureInfo.InvariantCulture) != 0, summary,
            reader["DriveFreeBytesBefore"] is DBNull ? null : Convert.ToInt64(reader["DriveFreeBytesBefore"], CultureInfo.InvariantCulture),
            reader["DriveFreeBytesAfter"] is DBNull ? null : Convert.ToInt64(reader["DriveFreeBytesAfter"], CultureInfo.InvariantCulture),
            reader["ObservedFreeSpaceDeltaBytes"] is DBNull ? null : Convert.ToInt64(reader["ObservedFreeSpaceDeltaBytes"], CultureInfo.InvariantCulture),
            categories.ToImmutableDictionary(), [.. warnings], [.. limitations], (string)reader["PrivacyMode"], (string)reader["Digest"]);
    }

    private static DateTimeOffset ParseTime(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static void Add(SqliteCommand command, string name, object? value) => command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    private static ExecutionReceiptStoreException Translate(SqliteException exception) => new(
        exception.SqliteErrorCode is 11 or 26 ? "receipt.database-corrupt" : "receipt.database-error",
        exception.SqliteErrorCode is 11 or 26 ? "Execution receipt history appears corrupted; CLYR did not delete or replace it." : "Execution receipt history could not be accessed safely.",
        exception);
}
