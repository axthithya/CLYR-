using System.ComponentModel;
using System.Runtime.CompilerServices;
using Clyr.Contracts;
using Clyr.Core;
using Clyr.Core.DeveloperMode;
using Clyr.Core.Execution;
using Clyr.Persistence;
using Clyr.Rules;

namespace Clyr.App.ViewModels;

public sealed class AppSessionViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IScanService scanService;
    private CancellationTokenSource? cancellation;
    private ScanMode selectedMode = ScanMode.Quick;
    private int selectedDriveIndex;
    private ScanProgress? progress;
    private ScanResult? result;
    private readonly string applicationVersion;

    public AppSessionViewModel(IScanService scanService, IDriveDiscovery drives, RulePackLoadResult rules,
        IApplicationVersion applicationVersion)
    {
        this.scanService = scanService;
        this.applicationVersion = applicationVersion.Value;
        Drives = drives.Discover();
        selectedDriveIndex = Drives.Select((drive, index) => (drive, index)).Where(item => item.drive.IsSupported)
            .OrderByDescending(item => item.drive.IsSystemVolume).Select(item => item.index).DefaultIfEmpty(-1).First();
        DetectionStatus = rules.Pack is null ? "Storage detection is unavailable. Structural results remain available." : "Storage detection database verified.";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? StateChanged;
    public IReadOnlyList<DriveSummary> Drives { get; }
    public string DetectionStatus { get; }
    public ScanProgress? Progress { get => progress; private set => Set(ref progress, value); }
    public ScanResult? Result { get => result; private set => Set(ref result, value); }
    public bool IsScanning => cancellation is not null;
    public ScanMode SelectedMode { get => selectedMode; set => Set(ref selectedMode, value); }
    public int SelectedDriveIndex { get => selectedDriveIndex; set => Set(ref selectedDriveIndex, value); }
    public DriveSummary? SelectedDrive => selectedDriveIndex >= 0 && selectedDriveIndex < Drives.Count ? Drives[selectedDriveIndex] : null;
    public string ApplicationVersion => applicationVersion;

    /// <summary>Set by another page (Developer Mode) right before navigating to Review Plan so that page can adopt
    /// an already-saved plan instead of building a new one from the current in-memory scan result.</summary>
    public CleanupPlanId? PendingReviewPlanId { get; set; }

    public async Task<ScanResult?> StartAsync()
    {
        var drive = SelectedDrive;
        if (drive is null || !drive.IsSupported || IsScanning) return null;
        cancellation = new CancellationTokenSource();
        Progress = new(ScanStatus.Preparing, TimeSpan.Zero, 0, 0, 0, 0, drive.Root, "Preparing private analysis.");
        Changed();
        try
        {
            var reporter = new Progress<ScanProgress>(value => { Progress = value; Changed(); });
            Result = await scanService.ScanAsync(new(drive.Root, SelectedMode), reporter, cancellation.Token);
            return Result;
        }
        finally
        {
            cancellation.Dispose(); cancellation = null; Changed();
        }
    }

    public void Cancel()
    {
        cancellation?.Cancel();
        if (Progress is not null) Progress = Progress with { Status = ScanStatus.Cancelling, Message = "Stopping safely… CLYR is finishing the current metadata operation." };
        Changed();
    }

    public void Dispose() => cancellation?.Dispose();
    private void Changed() { OnPropertyChanged(nameof(IsScanning)); OnPropertyChanged(nameof(SelectedDrive)); StateChanged?.Invoke(this, EventArgs.Empty); }
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; OnPropertyChanged(name); Changed(); }
    private void OnPropertyChanged(string? name) => PropertyChanged?.Invoke(this, new(name));
}

public abstract class PageViewModel(AppSessionViewModel session)
{
    public AppSessionViewModel Session { get; } = session;
    public event EventHandler<string>? NavigationRequested;
    public void Navigate(string destination) => NavigationRequested?.Invoke(this, destination);
}

public sealed class OverviewViewModel(AppSessionViewModel session) : PageViewModel(session);
public sealed class ScanViewModel(AppSessionViewModel session) : PageViewModel(session);
public sealed class ResultsViewModel(AppSessionViewModel session, IScanReportExporter exporter) : PageViewModel(session)
{
    public string? CreatePrivacySafeReport() => Session.Result is null ? null : exporter.Serialize(Session.Result);
}
public sealed class ReviewPlanViewModel : PageViewModel
{
    private static readonly System.Text.Json.JsonSerializerOptions ReceiptExportJson = new() { WriteIndented = true };
    private readonly ICleanupPlanStore store;
    private readonly IExecutionTokenService tokenService;
    private readonly IExecutionReceiptStore? receiptStore;
    private readonly IClock clock;
    private readonly ExecutionSessionId sessionId;
    private readonly string? trustedRootOverride;
    private readonly HashSet<string> attemptedPlanIds = new(StringComparer.Ordinal);

    public ReviewPlanViewModel(AppSessionViewModel session, ICleanupPlanStore store, IExecutionTokenService tokenService,
        IExecutionReceiptStore? receiptStore, IClock clock, ExecutionSessionId sessionId, string? trustedRootOverride = null)
        : base(session)
    {
        this.store = store;
        this.tokenService = tokenService;
        this.receiptStore = receiptStore;
        this.clock = clock;
        this.sessionId = sessionId;
        this.trustedRootOverride = trustedRootOverride;
    }

    public CleanupPlan? CurrentPlan { get; private set; }
    public ExecutionOutcome? LastOutcome { get; private set; }

    public IReadOnlyList<CleanupCandidate> Candidates
    {
        get
        {
            var candidates = Session.Result is null ? new List<CleanupCandidate>() : CleanupCandidateFactory.FromScan(Session.Result).ToList();
            var builtIn = ClyrOwnedTempArtifactScanner.Scan(clock, trustedRootOverride);
            if (builtIn is not null) candidates.Add(builtIn);
            return candidates;
        }
    }

    /// <summary>Loads a plan that another page (Developer Mode) already built and saved through the same
    /// integrity-checked <see cref="CleanupPlanBuilder"/> path, rather than deriving a new plan from the
    /// current in-memory scan result.</summary>
    public void AdoptPending()
    {
        if (Session.PendingReviewPlanId is not { } id) return;
        Session.PendingReviewPlanId = null;
        var plan = store.Find(id);
        if (plan is not null) { CurrentPlan = plan; LastOutcome = null; }
    }

    public CleanupPlan Create(IReadOnlyList<string> selectedFindingIds)
    {
        var result = Session.Result;
        var scanId = result?.ScanId ?? Guid.NewGuid();
        var driveIdentity = result is null ? "fixture-drive" : result.Root + "|" + result.FileSystem;
        var pack = result?.Classification?.RulePack;
        CurrentPlan = CleanupPlanBuilder.Create(new(scanId, null, driveIdentity,
            pack?.Id ?? "clyr.builtin", pack?.Version ?? "1.0.0", pack?.Digest ?? "builtin-1", Session.ApplicationVersion,
            "support-safe", DateTimeOffset.UtcNow, Candidates, selectedFindingIds));
        store.Save(CurrentPlan);
        LastOutcome = null;
        return CurrentPlan;
    }

    public string Export()
    {
        var plan = CurrentPlan ?? throw new InvalidOperationException("No dry-run plan is available.");
        return CleanupPlanReportExporter.Serialize(plan, CurrentValidation(plan));
    }

    public void Discard()
    {
        if (CurrentPlan is not null) store.Discard(CurrentPlan.Id);
        CurrentPlan = null;
        LastOutcome = null;
    }

    /// <summary>Items in the current plan that independently pass Phase 6 execution eligibility. None are selected by default.</summary>
    public IReadOnlyList<CleanupPlanItem> ExecutableItems() =>
        CurrentPlan is null ? [] : [.. CurrentPlan.Items.Where(item => ExecutionEligibilityValidator.ValidateItemForExecution(item).IsSuccess)];

    public ExecutionOutcome Execute(IReadOnlyList<string> selectedItemIds, IProgress<ExecutionItemResult>? progress, CancellationToken cancellationToken)
    {
        var plan = CurrentPlan ?? throw new InvalidOperationException("No dry-run plan is available.");
        if (!attemptedPlanIds.Add(plan.Id.ToString()))
            throw new InvalidOperationException("This plan has already been used for an execution attempt.");
        var userSid = OperatingSystem.IsWindows() ? WindowsUserIdentity.CurrentSid() : "unavailable";
        var actionIds = plan.Items.Where(item => selectedItemIds.Contains(item.ItemId, StringComparer.Ordinal))
            .Select(item => item.Action.SourceRuleId).Distinct(StringComparer.Ordinal).ToArray();
        var token = tokenService.Issue(plan, sessionId, userSid, actionIds, clock.UtcNow);
        var executor = new NonElevatedCleanupExecutor(tokenService, clock);
        var outcome = executor.Execute(plan, selectedItemIds, token, sessionId, userSid, Session.ApplicationVersion,
            trustedRootOverride, cancellationToken, progress);
        receiptStore?.SaveAsync(outcome.Receipt, cancellationToken).GetAwaiter().GetResult();
        LastOutcome = outcome;
        return outcome;
    }

    public IReadOnlyList<ExecutionReceiptSummary> ReceiptHistory() =>
        receiptStore is null ? [] : receiptStore.ListAsync().GetAwaiter().GetResult();

    public string? ExportReceipt(ExecutionId id)
    {
        var receipt = receiptStore?.GetAsync(id).GetAwaiter().GetResult();
        return receipt is null ? null : System.Text.Json.JsonSerializer.Serialize(receipt, ReceiptExportJson);
    }

    public bool DiscardReceipt(ExecutionId id) => receiptStore?.DiscardAsync(id).GetAwaiter().GetResult() ?? false;

    private static PlanValidationResult CurrentValidation(CleanupPlan plan) =>
        CleanupPlanValidator.Validate(plan, new(DateTimeOffset.UtcNow, plan.Binding.SourceScanId,
            plan.Binding.SourceSnapshotId, plan.Binding.DriveIdentity, plan.Binding.SourceRulePackId,
            plan.Binding.SourceRulePackVersion, plan.Binding.SourceRulePackDigest,
            CleanupPlanningConstants.CategoryRegistryVersion, CleanupPlanningConstants.ApplicationCompatibilityVersion,
            plan.Binding.PrivacyMode, System.Collections.Immutable.ImmutableDictionary<string, CleanupTarget>.Empty));
}
public sealed class DeveloperModeViewModel : PageViewModel
{
    private readonly ISnapshotStore snapshots;
    private readonly ICleanupPlanStore cleanupPlans;
    private readonly TrustedExecutableLocator locator;
    private readonly DeveloperToolProbeRunner probeRunner;

    public DeveloperModeViewModel(AppSessionViewModel session, ISnapshotStore snapshots, ICleanupPlanStore cleanupPlans,
        TrustedExecutableLocator locator, DeveloperToolProbeRunner probeRunner) : base(session)
    {
        this.snapshots = snapshots;
        this.cleanupPlans = cleanupPlans;
        this.locator = locator;
        this.probeRunner = probeRunner;
    }

    public IReadOnlyList<SnapshotSummary> Snapshots { get; private set; } = [];
    public Guid? SelectedSnapshotId { get; set; }
    public IReadOnlyList<DeveloperToolReport> Reports { get; private set; } = [];
    public string? StatusMessage { get; private set; }

    public async Task LoadSnapshotsAsync()
    {
        Snapshots = await snapshots.ListAsync().ConfigureAwait(false);
        if (SelectedSnapshotId is null || Snapshots.All(item => item.Id != SelectedSnapshotId))
            SelectedSnapshotId = Snapshots.OrderByDescending(item => item.CapturedAtUtc).Select(item => (Guid?)item.Id).FirstOrDefault();
    }

    public async Task DetectAsync()
    {
        if (SelectedSnapshotId is not { } id) { StatusMessage = "No local analysis is available yet. Run an analysis first."; Reports = []; return; }
        var snapshot = await snapshots.GetAsync(id).ConfigureAwait(false);
        if (snapshot is null) { StatusMessage = "The selected analysis could not be found."; Reports = []; return; }
        var classification = DeveloperToolReportBuilder.FromSnapshot(snapshot);
        Reports = await DeveloperToolRegistry.DetectAllAsync(classification, locator, probeRunner, CancellationToken.None).ConfigureAwait(false);
        StatusMessage = null;
    }

    /// <summary>Creates and saves an integrity-checked, immutable plan from a single developer finding using the
    /// same <see cref="CleanupPlanBuilder"/> path as every other cleanup candidate, then hands control to Review Plan.
    /// No file is touched here; this only records a report-only or manual-review plan for review and possible
    /// eligible execution under the existing Phase 6 boundary.</summary>
    public async Task<CleanupPlan?> CreatePlanAsync(string findingId)
    {
        if (SelectedSnapshotId is not { } id) return null;
        var snapshot = await snapshots.GetAsync(id).ConfigureAwait(false);
        if (snapshot is null) return null;
        var candidates = CleanupCandidateFactory.FromSnapshot(snapshot);
        if (candidates.All(candidate => candidate.FindingId != findingId)) return null;
        var plan = CleanupPlanBuilder.Create(new(snapshot.ScanId, snapshot.Id, snapshot.Drive.Fingerprint,
            snapshot.RulePackId, snapshot.RulePackVersion, snapshot.RulePackDigest, Session.ApplicationVersion,
            "support-safe", DateTimeOffset.UtcNow, candidates, [findingId]));
        cleanupPlans.Save(plan);
        Session.PendingReviewPlanId = plan.Id;
        return plan;
    }
}
public sealed class PrivacyViewModel(AppSessionViewModel session) : PageViewModel(session);
public sealed class LicensesViewModel(AppSessionViewModel session) : PageViewModel(session);
public sealed class AboutViewModel(AppSessionViewModel session, IApplicationVersion version, RulePackLoadResult rules) : PageViewModel(session)
{
    public string Version { get; } = version.Value;
    public string TechnicalDetails { get; } = $"Build channel: preview · implementation: 7 · rules: {rules.Pack?.Summary.Version ?? "unavailable"} · database schema: 3";
}
