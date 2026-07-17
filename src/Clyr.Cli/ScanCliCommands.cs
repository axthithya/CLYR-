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
    private IScanReportExporter? exporter;

    public CliApplication(IEnvironmentInfo environment, IDemoDataService demo, RuleValidator rules,
        IPrivacyRedactor redactor, string version, IDriveDiscovery driveDiscovery, IScanService scanner,
        IScanReportExporter exporter, RulePackLoadResult? rulePack = null) : this(environment, demo, rules, redactor, version)
    {
        this.driveDiscovery = driveDiscovery;
        this.scanner = scanner;
        this.nonPersistingScanner = scanner;
        this.exporter = exporter;
        this.rulePack = rulePack;
    }

    /// <summary>
    /// A scanner that never writes to CLYR's local aggregate history, distinct from the normal persisting
    /// scanner passed to the constructor above — used only by <c>scan --no-persist</c>, e.g. for diagnostic or
    /// real-machine verification runs that must not pollute a user's actual scan history. Defaults to the same
    /// persisting scanner (so existing callers are unaffected); set this explicitly to enable true no-persist
    /// behavior.
    /// </summary>
    public IScanService? NonPersistingScanner { set => nonPersistingScanner = value; }

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
        if (arguments.Count < 2) { error.WriteLine("Usage: clyr scan <root> [--quick|--deep] [--top N] [--json] [--output <file>] [--no-persist]"); return 2; }
        var mode = ScanMode.Quick;
        var json = false;
        var noPersist = false;
        int? top = null;
        string? destination = null;
        for (var index = 2; index < arguments.Count; index++)
        {
            switch (arguments[index])
            {
                case "--quick": mode = ScanMode.Quick; break;
                case "--deep": mode = ScanMode.Deep; break;
                case "--json": json = true; break;
                case "--no-persist": noPersist = true; break;
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
            // --no-persist deliberately runs the unwrapped scanner (no SnapshotSavingScanService), so a
            // diagnostic or real-machine verification run never writes to CLYR's real local history.
            var activeScanner = noPersist ? nonPersistingScanner : scanner;
            var result = activeScanner!.ScanAsync(new(root, mode, top), progress, cancellation.Token).GetAwaiter().GetResult();
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
