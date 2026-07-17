using System.Text.Json;
using System.Text.Json.Serialization;
using Clyr.Contracts;
using Clyr.Core;
using Clyr.Rules;

namespace Clyr.Cli;

public sealed partial class CliApplication
{
    private IDriveDiscovery? driveDiscovery;
    private IScanService? scanner;
    private IScanService? nonPersistingScanner;
    private IScanService? noCheckpointScanner;
    private IScanService? noCheckpointNoHistoryScanner;
    private IScanReportExporter? exporter;

    public CliApplication(IEnvironmentInfo environment, IDemoDataService demo, RuleValidator rules,
        IPrivacyRedactor redactor, string version, IDriveDiscovery driveDiscovery, IScanService scanner,
        IScanReportExporter exporter, RulePackLoadResult? rulePack = null) : this(environment, demo, rules, redactor, version)
    {
        this.driveDiscovery = driveDiscovery;
        this.scanner = scanner;
        this.nonPersistingScanner = scanner;
        this.noCheckpointScanner = scanner;
        this.noCheckpointNoHistoryScanner = scanner;
        this.exporter = exporter;
        this.rulePack = rulePack;
    }

    /// <summary>
    /// The four persistence combinations <c>scan</c> can select between, named by exactly what each one skips.
    /// "History" means CLYR's local aggregate scan-history database; "checkpoint" means the CLYR-owned Quick
    /// Analysis continuation checkpoint (see <see cref="Clyr.Core.IScanCheckpointStore"/>). A flag that claims
    /// something will not be persisted must actually select a scanner instance that cannot write it — never a
    /// scanner that happens to skip history while silently still writing a checkpoint, or vice versa. All four
    /// properties default to the constructor's persisting <c>scanner</c> (so existing callers that never touch
    /// these flags see no behavior change); set the ones your host application can actually honor.
    /// </summary>
    public IScanService? NonPersistingScanner { set => nonPersistingScanner = value; }

    /// <summary>History is saved (<c>--no-checkpoint</c> alone); the Quick Analysis checkpoint is never written.</summary>
    public IScanService? NoCheckpointScanner { set => noCheckpointScanner = value; }

    /// <summary>Nothing is persisted at all: neither history nor a checkpoint (<c>--no-persist</c>, or
    /// <c>--no-history --no-checkpoint</c> together).</summary>
    public IScanService? NoCheckpointNoHistoryScanner { set => noCheckpointNoHistoryScanner = value; }

    private int RunPhaseTwo(IReadOnlyList<string> arguments, TextWriter output, TextWriter error)
    {
        if (driveDiscovery is null || scanner is null || exporter is null)
        { error.WriteLine("Phase 2 scanner services are unavailable."); return 3; }
        return arguments[0] == "drives" ? Drives(arguments, output, error) : Scan(arguments, output, error);
    }

    private int Drives(IReadOnlyList<string> arguments, TextWriter output, TextWriter error)
    {
        var json = arguments.Count == 2 && arguments[1] == "--json";
        if (arguments.Count > (json ? 2 : 1)) { error.WriteLine("Usage: clyr drives [--json]"); return 2; }
        var discovered = driveDiscovery!.Discover();
        if (json)
        {
            var safeDrives = discovered.Select(drive => new
            {
                drive.Root,
                drive.FileSystem,
                drive.Kind,
                drive.IsReady,
                drive.IsSystemVolume,
                drive.IsSupported,
                drive.SupportReason,
                drive.CapacityBytes,
                drive.UsedBytes,
                drive.FreeBytes
            });
            output.WriteLine(JsonSerializer.Serialize(safeDrives, JsonOptions));
            return 0;
        }
        foreach (var drive in discovered)
        {
            var capacity = drive.CapacityBytes.HasValue ? FormatBytes(drive.CapacityBytes.Value) : "unavailable";
            output.WriteLine($"{drive.Root} {drive.Kind} {drive.FileSystem} capacity={capacity} supported={drive.IsSupported} - {drive.SupportReason}");
        }
        return 0;
    }

    private int Scan(IReadOnlyList<string> arguments, TextWriter output, TextWriter error)
    {
        if (arguments.Count < 2)
        {
            error.WriteLine("Usage: clyr scan <root> [--quick|--deep] [--top N] [--json] [--output <file>] [--no-persist] [--no-history] [--no-checkpoint] [--continue]");
            return 2;
        }
        var mode = ScanMode.Quick;
        var json = false;
        var noHistory = false;
        var noCheckpoint = false;
        var continueFromCheckpoint = false;
        int? top = null;
        string? destination = null;
        for (var index = 2; index < arguments.Count; index++)
        {
            switch (arguments[index])
            {
                case "--quick": mode = ScanMode.Quick; break;
                case "--deep": mode = ScanMode.Deep; break;
                case "--json": json = true; break;
                // Persists nothing at all: neither scan history nor a Quick Analysis checkpoint. Exactly
                // equivalent to --no-history --no-checkpoint together, spelled out below.
                case "--no-persist": noHistory = true; noCheckpoint = true; break;
                // Skips only CLYR's local aggregate scan-history database; the Quick Analysis checkpoint may
                // still be written, so a bounded Quick run stays resumable even in a diagnostic/no-history run.
                case "--no-history": noHistory = true; break;
                // Skips only the Quick Analysis checkpoint; scan history is still saved normally.
                case "--no-checkpoint": noCheckpoint = true; break;
                // Resumes Quick Analysis from its own last CLYR-owned checkpoint instead of restarting at the
                // drive root; ignored (with no error) for Deep, which has no checkpoint concept.
                case "--continue": continueFromCheckpoint = true; break;
                case "--top" when index + 1 < arguments.Count && int.TryParse(arguments[++index], out var parsed) && parsed is >= 1 and <= 1000: top = parsed; break;
                case "--output" when index + 1 < arguments.Count: destination = arguments[++index]; break;
                default: error.WriteLine("Invalid scan option. Run clyr --help."); return 2;
            }
        }
        if (!ScanPathValidator.TryNormalizeRoot(arguments[1], out var root, out var validationError))
        { error.WriteLine(validationError); return 2; }

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        try
        {
            var progress = new InlineProgress<ScanProgress>(value =>
                error.WriteLine($"{value.Status}: files={value.FilesObserved} directories={value.DirectoriesObserved} logical={FormatBytes(value.LogicalBytesObserved)} skipped={value.SkippedEntries}"));
            // Each of the four (history, checkpoint) combinations below is backed by a genuinely distinct
            // IScanService instance (see Program.cs) — never a single scanner with a flag inside it — so a run
            // that claims "no history" or "no checkpoint" is structurally incapable of writing the thing it
            // claims not to write, rather than relying on every code path to remember to check a bool.
            IScanService? activeScanner = (noHistory, noCheckpoint) switch
            {
                (false, false) => scanner,
                (true, false) => nonPersistingScanner,
                (false, true) => noCheckpointScanner,
                (true, true) => noCheckpointNoHistoryScanner,
            };
            var result = activeScanner!.ScanAsync(new(root, mode, top, continueFromCheckpoint), progress, cancellation.Token).GetAwaiter().GetResult();
            var serialized = exporter!.Serialize(result);
            if (destination is not null)
            {
                var fullDestination = Path.GetFullPath(destination);
                File.WriteAllText(fullDestination, serialized);
                if (!json) output.WriteLine("Privacy-safe summary written to: " + fullDestination);
            }
            if (json) output.WriteLine(serialized);
            else WriteHumanResult(result, output, error);
            return result.Status switch { ScanStatus.Completed => 0, ScanStatus.CompletedWithWarnings or ScanStatus.Cancelled => 1, _ => 3 };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { error.WriteLine("Export failed: " + redactor.Redact(exception.Message)); return 3; }
        finally { Console.CancelKeyPress -= handler; }
    }

    private static void WriteHumanResult(ScanResult result, TextWriter output, TextWriter error)
    {
        var writer = result.Status == ScanStatus.Failed ? error : output;
        writer.WriteLine($"Status: {result.Status}");
        writer.WriteLine($"Observed logical size: {FormatBytes(result.LogicalBytesObserved)} ({result.Precision})");
        writer.WriteLine($"Coverage: {result.Coverage.FilesObserved} files, {result.Coverage.DirectoriesObserved} directories, {result.Issues.Sum(x => x.Count)} warnings");
        writer.WriteLine(result.AccountingNote);
        if (result.UnaccountedBytes is < 0)
            writer.WriteLine($"Note: observed logical bytes exceeded the drive's reported used-bytes basis by {FormatBytes(-result.UnaccountedBytes.Value)} (hard links, sparse files, or a basis/timing difference — not a reportable coverage percentage).");
        else if (result.UnaccountedBytes is { } unaccounted) writer.WriteLine($"Not observed by this scan: {FormatBytes(unaccounted)}");
        if (result.Allocation is { } allocation)
        {
            writer.WriteLine($"Allocated (on-disk): {FormatBytes(allocation.AllocatedBytesObserved)}; unique after hard-link de-duplication: {FormatBytes(allocation.UniqueAllocatedBytesObserved)}.");
            if (allocation.VisibleHardLinkEntries > 0) writer.WriteLine($"Hard-linked entries excluded from unique allocation: {allocation.VisibleHardLinkEntries} (across {allocation.UniqueFileIdentities} unique file identities).");
            if (allocation.FilesWithUnavailableAllocatedSize > 0) writer.WriteLine($"Allocated size unavailable for {allocation.FilesWithUnavailableAllocatedSize} file(s).");
            if (allocation.SparseFileCount > 0 || allocation.CompressedFileCount > 0) writer.WriteLine($"Sparse files: {allocation.SparseFileCount}; compressed files: {allocation.CompressedFileCount}.");
        }
        if (result.Classification is not null)
        {
            writer.WriteLine(result.Classification.Summary);
            writer.WriteLine($"Classified: {FormatBytes(result.Classification.Coverage.ClassifiedBytes)}; unknown observed: {FormatBytes(result.Classification.Coverage.UnknownBytes)}; inaccessible: {result.Classification.Coverage.InaccessibleEntries}; skipped: {result.Classification.Coverage.SkippedEntries}.");
            foreach (var finding in result.Classification.Findings)
                writer.WriteLine($"- {finding.Title}: {FormatBytes(finding.LogicalBytes)} [{finding.Category}; {finding.Confidence}; {finding.Status}]");
        }
        foreach (var item in result.TopLevelDirectories) writer.WriteLine($"- {item.DisplayPath}: {FormatBytes(item.LogicalBytes)}");
        if (result.FailureCode is not null) writer.WriteLine(result.FailureCode + ": " + result.FailureMessage);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) }
    };
    private static string FormatBytes(long bytes) => bytes >= 1_073_741_824 ? $"{bytes / 1_073_741_824d:F2} GiB" : bytes >= 1_048_576 ? $"{bytes / 1_048_576d:F2} MiB" : bytes >= 1024 ? $"{bytes / 1024d:F2} KiB" : bytes + " B";
    private sealed class InlineProgress<T>(Action<T> action) : IProgress<T> { public void Report(T value) => action(value); }
}
