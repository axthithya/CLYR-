using Clyr.Core;
using Clyr.Rules;

namespace Clyr.Cli;

public sealed partial class CliApplication
{
    private readonly IEnvironmentInfo environment;
    private readonly IDemoDataService demo;
    private readonly RuleValidator rules;
    private readonly IPrivacyRedactor redactor;
    private readonly string version;
    private RulePackLoadResult? rulePack;

    public CliApplication(IEnvironmentInfo environment, IDemoDataService demo, RuleValidator rules, IPrivacyRedactor redactor, string version)
    {
        this.environment = environment;
        this.demo = demo;
        this.rules = rules;
        this.redactor = redactor;
        this.version = version;
    }

    public int Run(IReadOnlyList<string> arguments, TextWriter output, TextWriter error)
    {
        var command = arguments.Count == 0 ? "--help" : arguments[0];
        if (command is "--help" or "-h" or "help") return Help(output);
        if (command == "--version") { output.WriteLine(version); return 0; }
        if (command == "doctor") return Doctor(output);
        if (command == "demo") return Demo(output);
        if (command == "rules") return Rules(arguments, output, error);
        if (command is "drives" or "scan") return RunPhaseTwo(arguments, output, error);
        if (command == "explain") return Explain(arguments, output, error);
        if (command == "snapshots") return Snapshots(arguments, output, error);
        if (command == "plan") return Plans(arguments, output, error);
        if (command == "execution") return Execution(arguments, output, error);
        error.WriteLine("Unknown command. Run clyr --help.");
        return 2;
    }

    private static int Help(TextWriter output)
    {
        output.WriteLine("CLYR - trustworthy Windows storage understanding");
        output.WriteLine("Commands: --help, --version, doctor, demo, drives [--json], scan <root> [--quick|--deep] [--top N] [--json] [--output <file>], rules list|verify|describe <id>|validate <path>, explain <report.json>, snapshots list|show|compare|delete|clear|settings, plan candidates|create|show|validate|export|discard|execute, execution status|receipt|list|export|discard-receipt");
        return 0;
    }

    private int Doctor(TextWriter output)
    {
        output.WriteLine("OS: " + environment.OperatingSystem);
        output.WriteLine("Architecture: " + environment.Architecture);
        var packStatus = rulePack?.IsValid == true ? "built-in rules verified" : "classification unavailable; structural scanning remains available";
        output.WriteLine("Status: read-only scanning and classification available; " + packStatus +
            "; guarded low-risk execution is enabled only for approved CLYR-owned temporary artifacts.");
        output.WriteLine("Cleanup requires a validated active-session plan; arbitrary paths and general cleanup are unavailable.");
        output.WriteLine("Developer Mode is not implemented yet.");
        output.WriteLine("No drives have been scanned by this command.");
        return 0;
    }

    private int Demo(TextWriter output)
    {
        output.WriteLine("Demo data — no real drives have been scanned.");
        foreach (var finding in demo.GetFindings()) output.WriteLine("- " + finding.Title + " (synthetic)");
        return 0;
    }

    private int Rules(IReadOnlyList<string> arguments, TextWriter output, TextWriter error)
    {
        if (arguments.Count == 2 && arguments[1] == "list")
        {
            if (rulePack?.Pack is null) return WritePackErrors(error);
            foreach (var rule in rulePack.Pack.Rules.OrderBy(item => item.Id, StringComparer.Ordinal))
                output.WriteLine($"{rule.Id} {rule.Version} {rule.Category} {rule.Status}");
            return 0;
        }
        if (arguments.Count == 2 && arguments[1] == "verify")
        {
            if (rulePack?.Pack is null) return WritePackErrors(error);
            output.WriteLine($"{rulePack.Pack.Summary.Id} {rulePack.Pack.Summary.Version}: verified; {rulePack.Pack.Summary.RuleCount} detection-only rules active; digest {rulePack.Pack.Summary.Digest}");
            return 0;
        }
        if (arguments.Count == 3 && arguments[1] == "describe")
        {
            var rule = rulePack?.Pack?.Rules.FirstOrDefault(item => item.Id == arguments[2]);
            if (rule is null) { error.WriteLine("Unknown built-in rule identifier."); return 1; }
            output.WriteLine($"{rule.Id} {rule.Version}: {rule.Title}");
            output.WriteLine($"Category: {rule.Category}; confidence: {rule.Confidence}; status: {rule.Status}; report-only");
            output.WriteLine(rule.Why);
            output.WriteLine(rule.Meaning);
            return 0;
        }
        if (arguments.Count == 3 && arguments[1] == "validate")
        {
            var result = rules.ValidateFile(arguments[2]);
            if (result.IsValid) { output.WriteLine("External rule is schema-valid but remains inactive and untrusted."); return 0; }
            foreach (var message in result.Errors) error.WriteLine(redactor.Redact(message));
            return 1;
        }
        error.WriteLine("Usage: clyr rules list | rules verify | rules describe <id> | rules validate <path>");
        return 2;
    }

    private int WritePackErrors(TextWriter error)
    {
        foreach (var diagnostic in rulePack?.Diagnostics ?? [new("rule.pack-unavailable", "Built-in rules are unavailable.")])
            error.WriteLine(diagnostic.Code + ": " + redactor.Redact(diagnostic.Message));
        return 1;
    }

    private static int Explain(IReadOnlyList<string> arguments, TextWriter output, TextWriter error)
    {
        if (arguments.Count != 2) { error.WriteLine("Usage: clyr explain <report.json>"); return 2; }
        try
        {
            var info = new FileInfo(arguments[1]);
            if (!info.Exists || info.Length > 8_388_608) { error.WriteLine("Report is missing or exceeds the safe size limit."); return 1; }
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(info.FullName),
                new() { MaxDepth = 32, CommentHandling = System.Text.Json.JsonCommentHandling.Disallow });
            if (!document.RootElement.TryGetProperty("schemaVersion", out var versionElement)
                || versionElement.GetInt32() != 2
                || !document.RootElement.TryGetProperty("scan", out var scan)
                || !scan.TryGetProperty("classification", out var classification))
            { error.WriteLine("A CLYR classified report schema version 2 is required."); return 1; }
            output.WriteLine(classification.GetProperty("summary").GetString());
            foreach (var finding in classification.GetProperty("findings").EnumerateArray())
            {
                output.WriteLine($"- {finding.GetProperty("title").GetString()}: {finding.GetProperty("logicalBytes").GetInt64()} bytes [{finding.GetProperty("status").GetString()}]");
                output.WriteLine("  " + finding.GetProperty("explanation").GetProperty("whatItMeans").GetString());
            }
            return 0;
        }
        catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException or InvalidOperationException)
        { error.WriteLine("Report could not be explained: " + exception.GetType().Name); return 1; }
    }
}
