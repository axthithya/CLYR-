using Clyr.Cli;
using Clyr.Core;
using Clyr.Rules;
using Clyr.Windows;

var schemaPath = Path.Combine(Environment.CurrentDirectory, "rules", "schemas", "rule.schema.json");
if (!File.Exists(schemaPath))
{
    Console.Error.WriteLine("Rule schema was not found. Run CLYR from the repository root.");
    return 3;
}
var validator = new RuleValidator(File.ReadAllText(schemaPath));
var environment = new WindowsEnvironmentInfo();
var drives = new WindowsDriveDiscovery();
var packResult = BuiltInRulePackLoader.Load(Path.Combine(Environment.CurrentDirectory, "rules", "builtin"));
var scanner = new ScanCoordinator(new WindowsFileSystemEnumerator(), drives, new SystemClock(), packResult.Pack);
var application = new CliApplication(environment, new DemoDataService(), validator, new PrivacyRedactor(environment),
    "CLYR 0.3.0-phase3", drives, scanner, new ScanReportExporter(), packResult);
return application.Run(args, Console.Out, Console.Error);
