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
        error.WriteLine("Unknown command. Run clyr --help.");
        return 2;
    }

    private static int Help(TextWriter output)
    {
        output.WriteLine("CLYR - trustworthy Windows storage understanding");
        output.WriteLine("Commands: --help, --version, doctor, demo, drives [--json], scan <root> [--quick|--deep] [--top N] [--json] [--output <file>], rules validate <path>");
        return 0;
    }

    private int Doctor(TextWriter output)
    {
        output.WriteLine("OS: " + environment.OperatingSystem);
        output.WriteLine("Architecture: " + environment.Architecture);
        output.WriteLine("Status: read-only Phase 2 scanner available; no drives have been scanned by this command.");
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
        if (arguments.Count != 3 || arguments[1] != "validate")
        {
            error.WriteLine("Usage: clyr rules validate <path>");
            return 2;
        }
        var result = rules.ValidateFile(arguments[2]);
        if (result.IsValid) { output.WriteLine("Rule is valid and detection-only."); return 0; }
        foreach (var message in result.Errors) error.WriteLine(redactor.Redact(message));
        return 1;
    }
}
