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
    private ScanMode? selectedScanMode;
    private ScanMode? runningScanMode;
    private int selectedDriveIndex;
    private ScanProgress? progress;
    private ScanResult? result;
    private ScanResult? latestAttempt;
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

    /// <summary>The most recent scan attempt that reached a genuinely successful terminal state (Completed or
    /// CompletedWithWarnings) — this is what Overview, Results, and Review Plan consume. A cancelled or failed
    /// rescan attempt never overwrites this: the previous successful result remains visible and usable while,
    /// and after, a rescan that did not succeed. See <see cref="LatestAttempt"/> for the just-finished attempt
    /// regardless of outcome.</summary>
    public ScanResult? Result { get => result; private set => Set(ref result, value); }

    /// <summary>
    /// Replaces <see cref="Result"/> with an administrator-retry-enriched result — the correction for the retry
    /// workflow computing a correct, deduplicated combined figure but never actually replacing what every page
    /// reads. <paramref name="enriched"/> must share <see cref="ScanResult.ScanId"/> with the currently active
    /// <see cref="Result"/>: this is a refinement of the same completed Deep Analysis, never a substitute for a
    /// different or newer one (for example, one started while a retry was still in flight) — a mismatch is a
    /// silent no-op rather than a stale overwrite. Reuses the same <see cref="Set{T}"/>/<see cref="Changed"/>
    /// path every other session-state change already goes through, so every subscribed page (Results, Review
    /// Plan, Overview) refreshes the same way a normal scan completion already does — no page-specific forced
    /// refresh is needed.
    /// </summary>
    public void ApplyEnrichedResult(ScanResult enriched)
    {
        if (result is null || enriched.ScanId != result.ScanId) return;
        Result = enriched;
    }

    /// <summary>The most recently finished scan attempt, whatever its outcome — used by the Scan page itself to
    /// report a truthful Cancelled/Failed/Completed status for what just happened, independent of whether that
    /// attempt was successful enough to replace <see cref="Result"/>.</summary>
    public ScanResult? LatestAttempt { get => latestAttempt; private set => Set(ref latestAttempt, value); }

    public bool IsScanning => cancellation is not null;

    /// <summary>The single authoritative scan-mode selection. Null means no mode is chosen — there is
    /// deliberately no independent "QuickSelected"/"DeepSelected" boolean pair anywhere; every selection-derived
    /// fact (card checkmark, button text/enabled state, the mode actually sent to the scanner) reads this one
    /// value.</summary>
    public ScanMode? SelectedScanMode { get => selectedScanMode; set => Set(ref selectedScanMode, value); }

    /// <summary>The lifecycle state driving the Scan page's display. Computed, never independently tracked, so
    /// it can never drift from the underlying selection/progress/result facts it derives from.</summary>
    public ScanUiLifecycleState LifecycleState =>
        ScanUiLifecycle.Compute(runningScanMode ?? selectedScanMode, IsScanning, Progress?.Status, latestAttempt);

    public int SelectedDriveIndex { get => selectedDriveIndex; set => Set(ref selectedDriveIndex, value); }
    public DriveSummary? SelectedDrive => selectedDriveIndex >= 0 && selectedDriveIndex < Drives.Count ? Drives[selectedDriveIndex] : null;
    public string ApplicationVersion => applicationVersion;

    /// <summary>Set by another page (Developer Mode) right before navigating to Review Plan so that page can adopt
    /// an already-saved plan instead of building a new one from the current in-memory scan result.</summary>
    public CleanupPlanId? PendingReviewPlanId { get; set; }

    /// <summary>
    /// Starts exactly one scan attempt for <see cref="SelectedScanMode"/>. Refuses to start with no mode chosen
    /// (defect: "a scan must never start without a clear authoritative mode"), while already scanning, or with
    /// no supported drive selected. The mode is captured into <c>runningScanMode</c> for the duration of the
    /// attempt so the lifecycle display stays correct even if the user changes <see cref="SelectedScanMode"/>
    /// again immediately after this attempt finishes. Every attempt gets a fresh, independent
    /// <see cref="ScanResult"/> (never a mutated reuse of the previous one — see <c>ScanResult.ScanId</c>);
    /// <see cref="Result"/> is only replaced on a genuinely successful terminal state, so a cancelled or failed
    /// rescan can never erase a previous success.
    /// </summary>
    public async Task<ScanResult?> StartAsync(bool continueQuick = false)
    {
        var drive = SelectedDrive;
        if (drive is null || !drive.IsSupported || IsScanning || SelectedScanMode is not { } mode) return null;
        runningScanMode = mode;
        cancellation = new CancellationTokenSource();
        Progress = new(ScanStatus.Preparing, TimeSpan.Zero, 0, 0, 0, 0, drive.Root, "Preparing private analysis.");
        Changed();
        try
        {
            var reporter = new Progress<ScanProgress>(value => { Progress = value; Changed(); });
            var outcome = await scanService.ScanAsync(new(drive.Root, mode,
                ContinueFromCheckpoint: continueQuick && mode == ScanMode.Quick), reporter, cancellation.Token);
            LatestAttempt = outcome;
            if (outcome.Status is ScanStatus.Completed or ScanStatus.CompletedWithWarnings) Result = outcome;
            return outcome;
        }
        finally
        {
            cancellation.Dispose(); cancellation = null; runningScanMode = null; Changed();
        }
    }

    public void Cancel()
    {
        cancellation?.Cancel();
        if (Progress is not null) Progress = Progress with { Status = ScanStatus.Cancelling, Message = "Stopping safely… CLYR is finishing the current metadata operation." };
        Changed();
    }

    public void Dispose() => cancellation?.Dispose();
    private void Changed() { OnPropertyChanged(nameof(IsScanning)); OnPropertyChanged(nameof(SelectedDrive)); OnPropertyChanged(nameof(LifecycleState)); StateChanged?.Invoke(this, EventArgs.Empty); }
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; OnPropertyChanged(name); Changed(); }
    private void OnPropertyChanged(string? name) => PropertyChanged?.Invoke(this, new(name));
}

public abstract class PageViewModel(AppSessionViewModel session)
{
    public AppSessionViewModel Session { get; } = session;
    public event EventHandler<string>? NavigationRequested;
    public void Navigate(string destination) => NavigationRequested?.Invoke(this, destination);
}

public sealed class OverviewViewModel(AppSessionViewModel session, ISnapshotStore history) : PageViewModel(session)
{
    public IReadOnlyList<SnapshotSummary> RecentAnalyses { get; private set; } = [];

    public async Task LoadRecentAsync()
    {
        RecentAnalyses = await history.ListAsync(3).ConfigureAwait(false);
    }
}
public sealed class ScanViewModel(AppSessionViewModel session) : PageViewModel(session);
public sealed class ResultsViewModel(AppSessionViewModel session, IScanReportExporter exporter, IElevatedScanRetryService elevatedRetryService)
    : PageViewModel(session), IDisposable
{
    public string? CreatePrivacySafeReport() => Session.Result is null ? null : exporter.Serialize(Session.Result);

    /// <summary>Owns the administrator-retry action's lifecycle for whatever completed result this page is
    /// currently showing. A session-scoped value, separate from <see cref="AppSessionViewModel.Result"/> itself
    /// — a successful attempt's combined figures reach <see cref="AppSessionViewModel.Result"/> only through
    /// <see cref="ApplyAdministratorRetryResultIfPending"/>, never automatically.</summary>
    public AdministratorRetryController AdministratorRetry { get; } = new(elevatedRetryService);

    /// <summary>Guards <see cref="ApplyAdministratorRetryResultIfPending"/> against re-applying the same already-
    /// applied attempt on a later, unrelated render pass (for example, a subsequent page navigation).</summary>
    private Guid? lastAppliedReconciliationId;

    /// <summary>Re-evaluates the administrator-retry action for the page's current result. Passes
    /// <see langword="null"/> while a new scan is running so the action is never shown against what would
    /// otherwise be a stale prior result mid-rescan.</summary>
    public void RefreshAdministratorRetry() => AdministratorRetry.Evaluate(Session.IsScanning ? null : Session.Result);

    /// <summary>
    /// The Administrator Retry result-integration correction: when <see cref="AdministratorRetry"/>'s current
    /// state is a successful, not-yet-applied <see cref="AdministratorRetryPhase.Applied"/> outcome, builds the
    /// enriched result (<see cref="ElevatedScanResultEnricher.Build"/>) and replaces
    /// <see cref="AppSessionViewModel.Result"/> with it — the missing step that previously left every page
    /// showing the original, unenriched result even after a successful retry. Idempotent: calling this again for
    /// the same attempt (for example, from a later unrelated <c>Refresh()</c>) is a no-op. Safe to call from any
    /// thread — it touches only plain view-model/session state, never a WinUI control; the caller (see
    /// <c>ResultsPage.OnAdministratorRetryStateChanged</c>) is responsible for the pre-existing requirement that
    /// anything touching WinUI controls happens only after marshaling onto the UI thread.
    /// </summary>
    public void ApplyAdministratorRetryResultIfPending()
    {
        var state = AdministratorRetry.State;
        if (state.Phase != AdministratorRetryPhase.Applied || state.CombinedResult is not { } combined) return;
        if (lastAppliedReconciliationId == combined.Attempt.ReconciliationExecutionId) return;
        if (!ReferenceEquals(combined.OriginalResult, Session.Result)) return;
        lastAppliedReconciliationId = combined.Attempt.ReconciliationExecutionId;
        Session.ApplyEnrichedResult(ElevatedScanResultEnricher.Build(combined));
    }

    public void Dispose() => AdministratorRetry.Dispose();
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
    public StorageSnapshot? SelectedSnapshot { get; private set; }
    public IReadOnlyList<DeveloperToolReport> Reports { get; private set; } = [];
    public string? StatusMessage { get; private set; }

    public async Task LoadSnapshotsAsync()
    {
        Snapshots = await snapshots.ListAsync().ConfigureAwait(false);
        if (SelectedSnapshotId is null || Snapshots.All(item => item.Id != SelectedSnapshotId))
            SelectedSnapshotId = Snapshots.OrderByDescending(item => item.CapturedAtUtc).Select(item => (Guid?)item.Id).FirstOrDefault();
        SelectedSnapshot = SelectedSnapshotId is { } id
            ? await snapshots.GetAsync(id).ConfigureAwait(false)
            : null;
    }

    public async Task SelectSnapshotAsync(Guid? id)
    {
        SelectedSnapshotId = id;
        SelectedSnapshot = id is { } selectedId
            ? await snapshots.GetAsync(selectedId).ConfigureAwait(false)
            : null;
        Reports = [];
        StatusMessage = null;
    }

    public async Task DetectAsync()
    {
        if (SelectedSnapshotId is not { } id) { StatusMessage = "No local analysis is available yet. Run an analysis first."; Reports = []; return; }
        var snapshot = SelectedSnapshot?.Id == id
            ? SelectedSnapshot
            : await snapshots.GetAsync(id).ConfigureAwait(false);
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
