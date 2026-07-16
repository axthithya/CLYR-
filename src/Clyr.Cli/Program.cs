using Clyr.Cli;
using Clyr.Core;
using Clyr.Persistence;
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
var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CLYR");
var store = new SqliteSnapshotStore(Path.Combine(dataDirectory, "history.db"));
var receiptStore = new SqliteExecutionReceiptStore(Path.Combine(dataDirectory, "history.db"));
var identity = new HmacDriveIdentityProvider(new WindowsVolumeIdentitySource(),
    new FileIdentityKeyProvider(Path.Combine(dataDirectory, "identity.key")), drives);
IScanService scanner = new ScanCoordinator(new WindowsFileSystemEnumerator(), drives, new SystemClock(), packResult.Pack);
scanner = new SnapshotSavingScanService(scanner, new SnapshotFactory(identity, new ApplicationVersion("0.5.0-phase5")), store);
var application = new CliApplication(environment, new DemoDataService(), validator, new PrivacyRedactor(environment),
    "CLYR 0.5.0-phase5", drives, scanner, new ScanReportExporter(), packResult, store, new InMemoryCleanupPlanStore(),
    receiptStore);
return application.Run(args, Console.Out, Console.Error);
