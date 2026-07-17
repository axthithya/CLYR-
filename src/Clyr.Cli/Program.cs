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
var applicationVersion = ApplicationVersion.Current;
var checkpointStore = new FileScanCheckpointStore(Path.Combine(dataDirectory, "checkpoints"), new SystemClock());
var snapshotFactory = new SnapshotFactory(identity, applicationVersion);

// Four genuinely distinct scanner instances — one per (history, checkpoint) combination — rather than a single
// scanner gated by a runtime flag, so "this run persists nothing" is a structural fact (the instance used has
// no checkpoint store and is never wrapped in SnapshotSavingScanService), not a promise a flag could break.
IScanService withCheckpoint = new ScanCoordinator(new WindowsFileSystemEnumerator(), drives, new SystemClock(), packResult.Pack, checkpointStore);
IScanService withoutCheckpoint = new ScanCoordinator(new WindowsFileSystemEnumerator(), drives, new SystemClock(), packResult.Pack, checkpoints: null);
IScanService historyAndCheckpoint = new SnapshotSavingScanService(withCheckpoint, snapshotFactory, store);
IScanService historyOnly = new SnapshotSavingScanService(withoutCheckpoint, snapshotFactory, store);

var application = new CliApplication(environment, new DemoDataService(), validator, new PrivacyRedactor(environment),
    "CLYR " + applicationVersion.Value, drives, historyAndCheckpoint, new ScanReportExporter(), packResult, store, new InMemoryCleanupPlanStore(),
    receiptStore)
{
    NonPersistingScanner = withCheckpoint,             // --no-history: checkpoint still available
    NoCheckpointScanner = historyOnly,                 // --no-checkpoint: history still saved
    NoCheckpointNoHistoryScanner = withoutCheckpoint    // --no-persist / --no-history --no-checkpoint: nothing saved
};
return application.Run(args, Console.Out, Console.Error);
