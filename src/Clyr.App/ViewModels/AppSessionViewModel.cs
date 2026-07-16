using System.ComponentModel;
using System.Runtime.CompilerServices;
using Clyr.Contracts;
using Clyr.Core;
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
public sealed class ReviewPlanViewModel(AppSessionViewModel session, ICleanupPlanStore store) : PageViewModel(session)
{
    public CleanupPlan? CurrentPlan { get; private set; }
    public IReadOnlyList<CleanupCandidate> Candidates => Session.Result is null
        ? [] : CleanupCandidateFactory.FromScan(Session.Result);

    public CleanupPlan Create(IReadOnlyList<string> selectedFindingIds)
    {
        var result = Session.Result ?? throw new InvalidOperationException("Run an analysis before previewing a plan.");
        var pack = result.Classification?.RulePack ?? throw new InvalidOperationException("Verified classification is required.");
        CurrentPlan = CleanupPlanBuilder.Create(new(result.ScanId, null, result.Root + "|" + result.FileSystem,
            pack.Id, pack.Version, pack.Digest, Session.ApplicationVersion, "support-safe",
            DateTimeOffset.UtcNow, Candidates, selectedFindingIds));
        store.Save(CurrentPlan);
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
    }

    private static PlanValidationResult CurrentValidation(CleanupPlan plan) =>
        CleanupPlanValidator.Validate(plan, new(DateTimeOffset.UtcNow, plan.Binding.SourceScanId,
            plan.Binding.SourceSnapshotId, plan.Binding.DriveIdentity, plan.Binding.SourceRulePackId,
            plan.Binding.SourceRulePackVersion, plan.Binding.SourceRulePackDigest,
            CleanupPlanningConstants.CategoryRegistryVersion, CleanupPlanningConstants.ApplicationCompatibilityVersion,
            plan.Binding.PrivacyMode, System.Collections.Immutable.ImmutableDictionary<string, CleanupTarget>.Empty));
}
public sealed class DeveloperModeViewModel(AppSessionViewModel session) : PageViewModel(session);
public sealed class PrivacyViewModel(AppSessionViewModel session) : PageViewModel(session);
public sealed class LicensesViewModel(AppSessionViewModel session) : PageViewModel(session);
public sealed class AboutViewModel(AppSessionViewModel session, IApplicationVersion version, RulePackLoadResult rules) : PageViewModel(session)
{
    public string Version { get; } = version.Value;
    public string TechnicalDetails { get; } = $"Build channel: preview · implementation: 4.1 · rules: {rules.Pack?.Summary.Version ?? "unavailable"} · database schema: 2";
}
