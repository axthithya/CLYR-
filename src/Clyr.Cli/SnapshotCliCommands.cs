using System.Text.Json;
using System.Text.Json.Serialization;
using Clyr.Contracts;
using Clyr.Core;
using Clyr.Persistence;
using Clyr.Rules;

namespace Clyr.Cli;

public sealed partial class CliApplication
{
    private ISnapshotStore? snapshotStore;

    public CliApplication(IEnvironmentInfo environment, IDemoDataService demo, RuleValidator rules,
        IPrivacyRedactor redactor, string version, IDriveDiscovery driveDiscovery, IScanService scanner,
        IScanReportExporter exporter, RulePackLoadResult? rulePack, ISnapshotStore snapshotStore)
        : this(environment, demo, rules, redactor, version, driveDiscovery, scanner, exporter, rulePack) => this.snapshotStore = snapshotStore;

    private int Snapshots(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        if (snapshotStore is null) { error.WriteLine("Snapshot history is unavailable."); return 3; }
        try
        {
            if (args.Count == 2 && args[1] == "list") return List(output);
            if (args.Count == 3 && args[1] == "show" && Guid.TryParse(args[2], out var show)) return Show(show, output, error);
            if (args.Count == 4 && args[1] == "compare" && Guid.TryParse(args[2], out var before) && Guid.TryParse(args[3], out var after)) return Compare(before, after, output, error);
            if ((args.Count == 3 || args.Count == 4 && args[3] == "--yes") && args[1] == "delete" && Guid.TryParse(args[2], out var delete))
            { if (args.Count != 4) { error.WriteLine("Deletion requires --yes. No snapshot was deleted."); return 2; } return snapshotStore.DeleteAsync(delete).GetAwaiter().GetResult() ? 0 : 1; }
            if ((args.Count == 2 || args.Count == 3 && args[2] == "--yes") && args[1] == "clear")
            { if (args.Count != 3) { error.WriteLine("Clearing history requires --yes. Nothing was deleted."); return 2; } output.WriteLine($"Deleted {snapshotStore.ClearAsync().GetAwaiter().GetResult()} local aggregate snapshots."); return 0; }
            if (args.Count >= 2 && args[1] == "settings") return Settings(args, output, error);
            error.WriteLine("Usage: clyr snapshots list | show <id> | compare <before> <after> | delete <id> --yes | clear --yes | settings [show|set <enabled> <retention> <save-partial> <save-cancelled>]"); return 2;
        }
        catch (SnapshotStoreException exception) { error.WriteLine(exception.Code + ": " + exception.Message); return 3; }
        catch (ArgumentOutOfRangeException exception) { error.WriteLine("Invalid snapshot setting: " + exception.Message); return 2; }
    }

    private int List(TextWriter output)
    { foreach (var item in snapshotStore!.ListAsync().GetAwaiter().GetResult()) output.WriteLine($"{item.Id:D} {item.CapturedAtUtc:O} {item.State} {item.Root} {item.Mode} observed={item.LogicalBytesObserved} identity={item.IdentityQuality}"); return 0; }
    private int Show(Guid id, TextWriter output, TextWriter error)
    { var item = snapshotStore!.GetAsync(id).GetAwaiter().GetResult(); if (item is null) { error.WriteLine("Snapshot not found."); return 1; } output.WriteLine(JsonSerializer.Serialize(item, SnapshotJson)); return 0; }
    private int Compare(Guid before, Guid after, TextWriter output, TextWriter error)
    { var a = snapshotStore!.GetAsync(before).GetAwaiter().GetResult(); var b = snapshotStore.GetAsync(after).GetAwaiter().GetResult(); if (a is null || b is null) { error.WriteLine("One or both snapshots were not found."); return 1; } var report = SnapshotComparer.Compare(a, b); output.WriteLine(JsonSerializer.Serialize(report, SnapshotJson)); return report.Compatibility.Kind == SnapshotCompatibility.NotComparable ? 1 : 0; }
    private int Settings(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        if (args.Count == 2 || args.Count == 3 && args[2] == "show") { output.WriteLine(JsonSerializer.Serialize(snapshotStore!.GetSettingsAsync().GetAwaiter().GetResult(), SnapshotJson)); return 0; }
        if (args.Count == 7 && args[2] == "set" && bool.TryParse(args[3], out var enabled) && int.TryParse(args[4], out var retention) && bool.TryParse(args[5], out var partial) && bool.TryParse(args[6], out var cancelled))
        { snapshotStore!.SetSettingsAsync(new(enabled, retention, partial, cancelled)).GetAwaiter().GetResult(); output.WriteLine("History settings updated."); return 0; }
        error.WriteLine("Usage: clyr snapshots settings set <true|false> <retention 2..1000> <save-partial> <save-cancelled>"); return 2;
    }

    private static readonly JsonSerializerOptions SnapshotJson = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) } };
}
