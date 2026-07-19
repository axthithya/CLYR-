using Clyr.App.ViewModels;
using Clyr.Contracts;
using Clyr.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Clyr.App.Pages;

public sealed partial class ResultsPage : Page
{
    private const int InitialContributorCount = 12;

    /// <summary>Guards against enqueueing a second redundant dispatcher callback while one is already pending —
    /// <see cref="RenderAdministratorRetry"/> always reads the controller's current
    /// <see cref="AdministratorRetryController.State"/> at the time it actually runs, so a single pending callback
    /// already reflects whatever the latest state was by the time it executes.</summary>
    private bool administratorRetryRenderQueued;

    /// <summary>Ticks only while an administrator retry is running, to keep the elapsed-time display current —
    /// see <see cref="RenderAdministratorRetry"/>. Stopped on every terminal outcome and again, defensively, when
    /// the page's owning window closes (<see cref="StopElapsedTimer"/>); never runs any workflow logic itself,
    /// only reads <see cref="AdministratorRetryUiState.RunningSinceUtc"/>.</summary>
    private readonly DispatcherTimer administratorRetryElapsedTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private ResultsContributorItem[] contributors = [];
    private bool contributorsExpanded;
    private ResultsFindingItem[] findings = [];

    public ResultsPage(ResultsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        administratorRetryElapsedTimer.Tick += (_, _) => RenderAdministratorRetryElapsed();
        FindingFilter.ItemsSource = new[] { "All findings", "High confidence", "Medium confidence", "Low confidence" };
        FindingFilter.SelectedIndex = 0;
        viewModel.Session.StateChanged += (_, _) => Refresh();
        viewModel.AdministratorRetry.StateChanged += OnAdministratorRetryStateChanged;
        PageHost.LayoutModeChanged += (_, mode) => Reflow(mode);
        Refresh();
    }

    public ResultsViewModel ViewModel { get; }
    public void ResetScroll() => PageHost.ResetScroll();

    /// <summary>Stops the elapsed-time timer — called when the owning window closes, following the app's
    /// existing lifecycle pattern (see <c>MainWindow</c>'s <c>Closed</c> handler). Never cancels the retry itself;
    /// that remains <see cref="ResultsViewModel.Dispose"/>'s job.</summary>
    public void StopElapsedTimer() => administratorRetryElapsedTimer.Stop();

    /// <summary>
    /// <see cref="AdministratorRetryController.StateChanged"/> can fire from any thread — the controller awaits
    /// <c>IElevatedScanRetryService.RetryAsync</c> with <c>ConfigureAwait(false)</c>, so its continuation (and this
    /// event) runs on whatever thread-pool thread completed the underlying elevated retry, not necessarily this
    /// page's UI thread. WinUI controls may only be touched from the thread that owns them, so every render this
    /// event causes is marshaled through <see cref="FrameworkElement.DispatcherQueue"/> first.
    /// </summary>
    private void OnAdministratorRetryStateChanged(object? sender, EventArgs args)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            HandleAdministratorRetryStateChanged();
            return;
        }
        if (administratorRetryRenderQueued) return;
        administratorRetryRenderQueued = true;
        var enqueued = DispatcherQueue.TryEnqueue(() =>
        {
            administratorRetryRenderQueued = false;
            HandleAdministratorRetryStateChanged();
        });
        if (!enqueued) administratorRetryRenderQueued = false;
    }

    /// <summary>Runs only after <see cref="OnAdministratorRetryStateChanged"/> has confirmed (or arranged, via the
    /// dispatcher queue) that this is the UI thread. Applies a pending successful retry's enriched result to the
    /// active session state first — via <see cref="AppSessionViewModel.ApplyEnrichedResult"/>, which this
    /// triggers through <see cref="ResultsViewModel.ApplyAdministratorRetryResultIfPending"/> — so the resulting
    /// <see cref="AppSessionViewModel.StateChanged"/> notification (already subscribed to <see cref="Refresh"/>)
    /// refreshes every section of this page with the enriched data before the retry panel itself is rendered.</summary>
    private void HandleAdministratorRetryStateChanged()
    {
        ViewModel.ApplyAdministratorRetryResultIfPending();
        RenderAdministratorRetry();
    }

    public void Refresh()
    {
        var r = ViewModel.Session.Result;
        var session = ViewModel.Session;
        var snapshot = session.ProvisionalSnapshot;
        // Section 9 correction: provisional visibility must never be decided from "Session.Result is null" alone
        // — a previous completed result can already exist when a second analysis starts. ProvisionalSnapshot is
        // cleared at the start of every StartAsync attempt (see AppSessionViewModel), so while a scan is actively
        // running, any non-null snapshot is guaranteed to belong to *this* run, not a stale earlier one — the
        // provisional view must take visual priority over an old completed result in that case. Once the run
        // stops being active, a completed result (new or restored-previous) is authoritative again; only a
        // lingering snapshot with no completed result at all (a first run, or one that never completed) falls
        // back to the provisional view.
        var showProvisional = snapshot is not null && (session.IsScanning || r is null);

        EmptyPanel.Visibility = r is null && snapshot is null ? Visibility.Visible : Visibility.Collapsed;
        ProvisionalPanel.Visibility = showProvisional ? Visibility.Visible : Visibility.Collapsed;
        Dashboard.Visibility = r is not null ? Visibility.Visible : Visibility.Collapsed;
        ViewModel.RefreshAdministratorRetry();

        if (showProvisional) RenderProvisional(snapshot!, session.IsScanning);
        if (r is null) { Reflow(PageHost.LayoutMode); return; }

        RenderIdentity(r);
        RenderStorageHero(r);
        RenderCoverageAndClassification(r);
        RenderQualityAndLimitations(r);
        RenderContributors(r);
        RenderFindings(r);
        RenderDirectoriesAndFiles(r);
        Reflow(PageHost.LayoutMode);
    }

    /// <summary>
    /// Renders the bounded, provisional in-progress (or just-stopped) snapshot — never Review Plan, never
    /// Administrator Retry, never a final quality claim. <paramref name="stillRunning"/> distinguishes "Analysis
    /// in progress" from "Analysis stopped" wording so a cancelled provisional state can never look completed.
    /// </summary>
    private void RenderProvisional(ProgressiveScanSnapshot snapshot, bool stillRunning)
    {
        ProvisionalTitleText.Text = stillRunning ? "Analysis in progress" : "Analysis stopped";
        ProvisionalSubtitleText.Text = stillRunning
            ? "CLYR is continuing to inspect the drive. Values may change until the analysis finishes."
            : "The analysis stopped before finishing. These insights are the last available and are marked incomplete.";
        ProvisionalBadge.Text = stillRunning ? "In progress" : "Incomplete";
        ProvisionalBadge.Glyph = stillRunning ? "" : "";

        ProvisionalObservedText.Text = OverviewPage.Format(snapshot.LogicalBytesObserved);
        ProvisionalCoverageText.Text = snapshot.ProvisionalCoveragePercentage is { } coverage ? $"{coverage:F1}%" : "Unavailable";
        ProvisionalExaminedText.Text = $"{snapshot.FilesObserved:N0} files, {snapshot.DirectoriesObserved:N0} folders";
        ProvisionalElapsedText.Text = snapshot.Elapsed.ToString(@"mm\:ss", System.Globalization.CultureInfo.InvariantCulture);
        ProvisionalWarningsText.Text = snapshot.WarningCount.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
        ProvisionalLimitationsText.Text = snapshot.LimitationCount.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);

        ProvisionalContributorList.ItemsSource = snapshot.TopContributors
            .Where(item => item.LogicalBytes > 0)
            .OrderByDescending(item => item.LogicalBytes)
            .Select(item => new ResultsLimitationRow(OverviewPage.Humanize(item.Category), OverviewPage.Format(item.LogicalBytes)))
            .ToArray();
        ProvisionalDirectoryList.ItemsSource = snapshot.TopDirectories
            .OrderByDescending(item => item.LogicalBytes)
            .Take(10)
            .Select(item => new ResultsLimitationRow(item.DisplayPath, OverviewPage.Format(item.LogicalBytes)))
            .ToArray();
        ProvisionalFileList.ItemsSource = snapshot.TopFiles
            .OrderByDescending(item => item.LogicalBytes)
            .Take(10)
            .Select(item => new ResultsLimitationRow(item.DisplayPath, OverviewPage.Format(item.LogicalBytes)))
            .ToArray();
    }

    private void RenderIdentity(ScanResult r)
    {
        var duration = r.EndedAt >= r.StartedAt ? r.EndedAt - r.StartedAt : TimeSpan.Zero;
        // Phase (progressive-analysis terminology correction): the normal app's Results page only ever shows a
        // result produced by AnalyzeDriveAsync (always ScanMode.Deep internally) — never a loaded legacy record
        // — so this is always the new "Drive Analysis" terminology, never the internal strategy name.
        ScanIdentityText.Text = $"{r.Root.TrimEnd('\\')} · Drive Analysis";
        ScanTimingText.Text = $"Completed {r.EndedAt.LocalDateTime:g} - {OverviewPage.FormatDuration(duration)} - " +
            $"{r.Coverage.FilesObserved:N0} files - {r.Coverage.DirectoriesObserved:N0} folders";

        var completedWithWarnings = r.Status == ScanStatus.CompletedWithWarnings;
        // Phase (Quick truthfulness correction): a Quick scan whose coverage is materially limited must not
        // present as a plain, unqualified "Completed" success state — that reads as a confident, exhaustive
        // result when in fact large portions of the drive were never observed within Quick's bounded budget.
        var quickEstimateComplete = !completedWithWarnings && r.Mode == ScanMode.Quick
            && ScanAccounting.Summarize(r).Quality is ScanQuality.Partial or ScanQuality.Insufficient;
        StatusBadgeControl.Text = completedWithWarnings
            ? "Completed with warnings"
            : quickEstimateComplete ? "Quick estimate complete" : OverviewPage.Humanize(r.Status);
        StatusBadgeControl.Glyph = completedWithWarnings ? "" : "";

        // Phase (Quick truthfulness correction): never a flat sum across every issue — genuine warnings (access
        // denied, entries changing mid-scan, enumeration failures, fatal problems) are shown separately from
        // scan-policy boundaries (Quick's time/item/pending-capacity budget being reached, an expected outcome
        // of a bounded scan, not a warning about the scan itself). Informational notes (reparse skips, cloud
        // placeholders, checkpoint resumption) are not badged at all; they remain in the limitations list below.
        var warningCount = r.Issues.Where(item => item.Severity is
            ScanIssueSeverity.AccessWarning or ScanIssueSeverity.PermissionLimited or ScanIssueSeverity.DataChanged or ScanIssueSeverity.Fatal)
            .Sum(item => item.Count);
        WarningBadgeControl.Visibility = warningCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (warningCount > 0)
        {
            WarningBadgeControl.Text = $"{warningCount:N0} {(warningCount == 1 ? "warning" : "warnings")}";
            WarningBadgeControl.Glyph = "";
        }

        var scanLimitCount = r.Issues.Where(item => item.Severity == ScanIssueSeverity.PolicyBoundary).Sum(item => item.Count);
        ScanLimitBadgeControl.Visibility = scanLimitCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (scanLimitCount > 0)
        {
            ScanLimitBadgeControl.Text = $"{scanLimitCount:N0} scan {(scanLimitCount == 1 ? "limit" : "limits")}";
            ScanLimitBadgeControl.Glyph = "";
        }
    }

    private void RenderStorageHero(ScanResult r)
    {
        var summary = ScanAccounting.Summarize(r);
        var drive = ViewModel.Session.Drives.FirstOrDefault(item =>
            string.Equals(item.Root.TrimEnd('\\'), r.Root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));

        AccountedPercentValue.Text = summary.AccountedPercentage is { } accounted ? $"{accounted:F1}%" : "Unavailable";
        AccountedDescriptionText.Text = summary.AccountedPercentage is null
            ? "Drive coverage cannot be calculated from the available accounting basis."
            : "of this drive's used storage was observed by this analysis.";
        AccountedMetric.Text = OverviewPage.Format(r.LogicalBytesObserved);
        NotObservedMetric.Text = OverviewPage.FormatSigned(summary.UnaccountedDriveBytes);
        FreeMetric.Text = drive?.FreeBytes is { } free ? OverviewPage.Format(free) : "Unavailable";
        DriveUsedMetric.Text = r.DriveUsedBytes is { } used ? OverviewPage.Format(used) : "Unavailable";

        double accountedShare = 0, notObservedShare = 0, freeShare = 0;
        if (drive?.CapacityBytes is > 0)
        {
            var capacity = (double)drive.CapacityBytes.Value;
            accountedShare = Math.Clamp(r.LogicalBytesObserved / capacity * 100, 0, 100);
            notObservedShare = summary.UnaccountedDriveBytes is { } unaccounted
                ? Math.Clamp(Math.Max(0, unaccounted) / capacity * 100, 0, 100)
                : 0;
            freeShare = Math.Max(0, 100 - accountedShare - notObservedShare);
        }
        AccountedColumn.Width = new GridLength(Math.Max(accountedShare, 0.001), GridUnitType.Star);
        NotObservedColumn.Width = new GridLength(Math.Max(notObservedShare, 0.001), GridUnitType.Star);
        FreeColumn.Width = new GridLength(Math.Max(freeShare, 0.001), GridUnitType.Star);
        AccountedSegment.CornerRadius = accountedShare >= 99.95 ? new CornerRadius(8) : new CornerRadius(8, 0, 0, 8);
        AutomationProperties.SetName(StorageVisualization,
            $"{AccountedMetric.Text} accounted, {accountedShare:F1}%; {NotObservedMetric.Text} not observed, {notObservedShare:F1}%; " +
            $"{FreeMetric.Text} free, {freeShare:F1}%.");
    }

    private void RenderCoverageAndClassification(ScanResult r)
    {
        var summary = ScanAccounting.Summarize(r);
        CoverageHeadlineText.Text = summary.AccountedPercentage is { } accounted
            ? $"{accounted:F1}% accounted - CLYR observed {AccountedPortion(accounted)} of the drive's used storage."
            : "Accounted percentage unavailable for this scan.";
        ClassificationHeadlineText.Text = summary.ClassificationPercentage is { } classified
            ? $"{classified:F1}% classified - about {Rounded(classified)}% of the observed storage matched known categories."
            : "Classification percentage unavailable for this scan.";

        ClassifiedValueText.Text = OverviewPage.Format(r.Classification?.Coverage.ClassifiedBytes ?? 0);
        UnclassifiedValueText.Text = OverviewPage.Format(summary.UnclassifiedObservedBytes);
        UnobservedValueText.Text = OverviewPage.FormatSigned(summary.UnaccountedDriveBytes);
    }

    private static string Rounded(double percentage) => percentage switch
    {
        >= 95 => "all",
        >= 45 and <= 55 => "half",
        _ => $"{percentage:F0}"
    };

    private static string AccountedPortion(double percentage) => percentage switch
    {
        >= 90 => "nearly all",
        >= 60 => "most",
        >= 25 => "part",
        > 0 => "a small portion",
        _ => "none"
    };

    private void RenderQualityAndLimitations(ScanResult r)
    {
        var summary = ScanAccounting.Summarize(r);
        var (qualityText, qualityGlyph) = summary.Quality switch
        {
            ScanQuality.Excellent => ("Excellent coverage", ""),
            ScanQuality.Good => ("Good coverage", ""),
            ScanQuality.Partial => ("Limited coverage", ""),
            _ => ("Limited coverage", "")
        };
        QualityBadgeControl.Text = qualityText;
        QualityBadgeControl.Glyph = qualityGlyph;
        QualityDescriptionText.Text = summary.Quality switch
        {
            ScanQuality.Excellent or ScanQuality.Good => "This scan accounted for most or all of this drive's used space.",
            _ => "Run Deep Analysis for more complete drive coverage."
        };

        var rows = new List<ResultsLimitationRow>
        {
            new("Logical vs. allocated size", "Sizes reflect logical (namespace) bytes, not real on-disk allocation."),
            new("Hard links", "Content shared by multiple paths may be counted more than once in logical totals."),
            new("Filesystem-managed storage", "Some Windows-managed areas are reported but are not cleanup candidates."),
        };
        foreach (var issue in r.Issues.Where(item => item.Count > 0))
            rows.Add(new(OverviewPage.Humanize(issue.Kind), $"{issue.Count:N0} - {issue.SafeDetail}"));
        if (r.Classification is { } classification)
            foreach (var limitation in classification.Limitations)
                rows.Add(new("Classification estimate", limitation));
        LimitationRows.ItemsSource = rows;
    }

    private void RenderContributors(ScanResult r)
    {
        contributors = r.Classification?.Categories
            .Where(item => item.LogicalBytes > 0)
            .OrderByDescending(item => item.LogicalBytes)
            .Select((item, index) =>
            {
                var percentage = r.LogicalBytesObserved > 0
                    ? Math.Clamp(item.LogicalBytes * 100d / r.LogicalBytesObserved, 0, 100)
                    : 0;
                var name = OverviewPage.Humanize(item.Category);
                var size = OverviewPage.Format(item.LogicalBytes);
                return new ResultsContributorItem(index + 1, name, GlyphFor(item.Category), size, $"{percentage:F1}%", percentage,
                    $"Rank {index + 1}, {name}, {size}, {percentage:F1}% of observed storage.");
            })
            .ToArray() ?? [];
        ContributorsSection.Visibility = contributors.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ContributorsExpandButton.Visibility = contributors.Length > InitialContributorCount ? Visibility.Visible : Visibility.Collapsed;

        var quality = ScanAccounting.Summarize(r).Quality;
        var limitedCoverage = quality is ScanQuality.Partial or ScanQuality.Insufficient;
        ContributorsHeader.Title = limitedCoverage ? "Largest observed contributors" : "Why is this drive full?";
        ContributorsHeader.Description = limitedCoverage
            ? "These rankings describe only the storage observed by this analysis. Ranked contributors include exact sizes so the ranking never relies on color alone."
            : "Ranked contributors include exact sizes so the ranking never relies on color alone.";

        RenderContributorList();
    }

    private void RenderContributorList()
    {
        var visible = contributorsExpanded ? contributors : contributors.Take(InitialContributorCount).ToArray();
        ContributorList.ItemsSource = visible;
        if (contributors.Length > InitialContributorCount)
            ContributorsExpandButton.Content = contributorsExpanded ? "Show fewer" : $"Show all {contributors.Length}";
    }

    private void ToggleContributors(object sender, RoutedEventArgs args)
    {
        contributorsExpanded = !contributorsExpanded;
        RenderContributorList();
    }

    private void RenderFindings(ScanResult r)
    {
        findings = r.Classification?.Findings.OrderByDescending(item => item.LogicalBytes).Select(item =>
        {
            var size = OverviewPage.Format(item.LogicalBytes);
            var category = OverviewPage.Humanize(item.Category);
            var confidence = item.Confidence.ToString();
            var safety = OverviewPage.Humanize(item.Explanation.SafetyStatus.Length > 0 ? item.Explanation.SafetyStatus : item.Status.ToString());
            return new ResultsFindingItem(item.Title, size, category, confidence, safety, item.Explanation.WhatItMeans, item.Confidence,
                $"{item.Title}, {size}, {category}, {confidence} confidence, {safety}. {item.Explanation.WhatItMeans}");
        }).ToArray() ?? [];
        FindingsSection.Visibility = findings.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ApplyFindingFilter();
    }

    private void FindingFilterChanged(object sender, SelectionChangedEventArgs args) => ApplyFindingFilter();

    private void ApplyFindingFilter()
    {
        var filtered = FindingFilter.SelectedIndex switch
        {
            1 => findings.Where(item => item.ConfidenceValue == FindingConfidence.High).ToArray(),
            2 => findings.Where(item => item.ConfidenceValue == FindingConfidence.Medium).ToArray(),
            3 => findings.Where(item => item.ConfidenceValue == FindingConfidence.Low).ToArray(),
            _ => findings
        };
        FindingList.ItemsSource = filtered;
        NoFindingsText.Visibility = findings.Length > 0 && filtered.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderDirectoriesAndFiles(ScanResult r)
    {
        DirectoryList.ItemsSource = r.LargestDirectories.Select(item => ToPathItem(item, r, "")).ToArray();
        DirectoriesSection.Visibility = r.LargestDirectories.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        FileList.ItemsSource = r.LargestFiles.Select(item => ToPathItem(item, r, "")).ToArray();
        FilesSection.Visibility = r.LargestFiles.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static ResultsPathItem ToPathItem(RankedPath item, ScanResult r, string glyph)
    {
        var size = OverviewPage.Format(item.LogicalBytes);
        var name = ShortName(item.DisplayPath);
        var shortPath = ShortenPath(item.DisplayPath);
        var percentage = r.LogicalBytesObserved > 0 ? Math.Clamp(item.LogicalBytes * 100d / r.LogicalBytesObserved, 0, 100) : 0;
        return new ResultsPathItem(name, shortPath, item.DisplayPath, size, $"{percentage:F1}%", glyph,
            $"{name}, {shortPath}, {size}, {percentage:F1}% of observed storage.");
    }

    /// <summary>The last path segment only — the full, un-truncated path always remains available through the
    /// row's tooltip and accessible name, never discarded, only not shown as the primary line.</summary>
    private static string ShortName(string path)
    {
        var trimmed = path.TrimEnd('\\');
        var lastSeparator = trimmed.LastIndexOf('\\');
        return lastSeparator >= 0 && lastSeparator < trimmed.Length - 1 ? trimmed[(lastSeparator + 1)..] : trimmed;
    }

    /// <summary>Keeps the drive root and the final one or two segments, replacing the middle with an ellipsis
    /// marker, so a long nested path never visually dominates the row while the full path remains one tooltip
    /// or accessible-name away.</summary>
    private static string ShortenPath(string path)
    {
        var segments = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length <= 3) return path;
        var root = segments[0] + "\\";
        var tail = string.Join('\\', segments[^2..]);
        return $"{root}...\\{tail}";
    }

    private static string GlyphFor(StorageCategory category) => category switch
    {
        StorageCategory.WindowsSystemManaged or StorageCategory.WindowsUpdateServicing => "",
        StorageCategory.TemporaryFiles or StorageCategory.CrashDumpsDiagnostics or StorageCategory.Logs => "",
        StorageCategory.BrowserCache or StorageCategory.ApplicationCache or StorageCategory.DeveloperCache => "",
        StorageCategory.UserDownloads => "",
        StorageCategory.UserDocuments => "",
        StorageCategory.UserMedia => "",
        StorageCategory.ArchivesInstallers => "",
        StorageCategory.GamesLaunchers => "",
        StorageCategory.CloudSync => "",
        StorageCategory.DeveloperDependencies or StorageCategory.BuildOutput => "",
        StorageCategory.Containers or StorageCategory.VirtualMachines => "",
        StorageCategory.Wsl => "",
        StorageCategory.AndroidSdkEmulators => "",
        StorageCategory.RecycleBin => "",
        StorageCategory.RestoreRecovery => "",
        _ => ""
    };

    /// <summary>Renders <see cref="ResultsViewModel.AdministratorRetry"/>'s current state. Reads only bounded
    /// counts and pre-composed safe text off <see cref="AdministratorRetryUiState"/> — never a path, manifest,
    /// nonce, pipe name, or executable detail, none of which that type carries in the first place.</summary>
    private void RenderAdministratorRetry()
    {
        var state = ViewModel.AdministratorRetry.State;
        AdministratorRetryCard.Visibility = state.IsAdministratorRetryAvailable || state.IsAdministratorRetryRunning ? Visibility.Visible : Visibility.Collapsed;

        var everAttempted = state.Phase is not (AdministratorRetryPhase.Idle or AdministratorRetryPhase.Hidden or AdministratorRetryPhase.Running);
        AdministratorRetryButton.Content = everAttempted ? AdministratorRetryUx.RetryAgainButtonText : AdministratorRetryUx.ButtonText;
        AutomationProperties.SetName(AdministratorRetryButton, everAttempted ? AdministratorRetryUx.RetryAgainButtonText : AdministratorRetryUx.ButtonText);
        AdministratorRetryButton.IsEnabled = state.IsAdministratorRetryAvailable && !state.IsAdministratorRetryRunning;

        AdministratorRetryRunningPanel.Visibility = state.IsAdministratorRetryRunning ? Visibility.Visible : Visibility.Collapsed;
        if (state.IsAdministratorRetryRunning)
        {
            RenderAdministratorRetryElapsed();
            if (!administratorRetryElapsedTimer.IsEnabled) administratorRetryElapsedTimer.Start();
        }
        else
        {
            administratorRetryElapsedTimer.Stop();
        }

        var showInfoBar = state.Phase is not (AdministratorRetryPhase.Idle or AdministratorRetryPhase.Hidden or AdministratorRetryPhase.Running or AdministratorRetryPhase.NotEligible);
        AdministratorRetryInfoBar.IsOpen = showInfoBar;
        if (showInfoBar)
        {
            AdministratorRetryInfoBar.Title = state.AdministratorRetryTitle;
            AdministratorRetryInfoBar.Message = state.AdministratorRetryStatusText;
            AdministratorRetryInfoBar.Severity = state.Phase switch
            {
                AdministratorRetryPhase.Applied => InfoBarSeverity.Success,
                AdministratorRetryPhase.Denied or AdministratorRetryPhase.Cancelled => InfoBarSeverity.Informational,
                _ => InfoBarSeverity.Warning
            };
        }

        // Phase (Administrator Retry result-integration correction): the completion wording must distinguish
        // areas inspected, unique newly accounted bytes (never raw/overlapping retry output — state.AdditionalLogicalBytes
        // is already the reconciler's own deduplicated delta, see ElevatedScanResultReconciler), and remaining
        // inaccessible areas — and must say so truthfully, including when zero unique bytes were added.
        var showSummary = state.Phase == AdministratorRetryPhase.Applied;
        AdministratorRetrySummary.Visibility = showSummary ? Visibility.Visible : Visibility.Collapsed;
        AdministratorRetryComparison.Visibility = showSummary ? Visibility.Visible : Visibility.Collapsed;
        if (showSummary)
        {
            var addedBytes = state.AdditionalLogicalBytes ?? 0;
            var inspectedText = $"{state.RootsCompleted} restricted area{(state.RootsCompleted == 1 ? "" : "s")} were inspected.";
            var addedText = addedBytes > 0
                ? $" {OverviewPage.Format(addedBytes)} of previously unobserved storage was added to this result."
                : " No previously unobserved storage was added to this result.";
            var remainingText = state.RootsStillInaccessible > 0
                ? $" {state.RootsStillInaccessible} restricted area{(state.RootsStillInaccessible == 1 ? "" : "s")} remain unavailable."
                : " No restricted areas remain unavailable.";
            AdministratorRetrySummary.Text = inspectedText + addedText + remainingText;

            var beforePercentage = state.CombinedResult is { } combined ? ScanAccounting.Summarize(combined.OriginalResult).AccountedPercentage : null;
            var afterPercentage = ViewModel.Session.Result is { } current ? ScanAccounting.Summarize(current).AccountedPercentage : null;
            AdministratorRetryCoverageText.Text = beforePercentage is { } before && afterPercentage is { } after
                ? $"{before:F1}% -> {after:F1}%"
                : "Unavailable";
            AdministratorRetryNewlyAccountedText.Text = OverviewPage.Format(addedBytes);
            AdministratorRetryAreasText.Text =
                $"{state.RootsCompleted + state.RootsStillInaccessible} attempted - {state.RootsCompleted} inspected - {state.RootsStillInaccessible} remaining";
        }
    }

    /// <summary>Updates only the elapsed-time text from <see cref="AdministratorRetryUiState.RunningSinceUtc"/> —
    /// never starts, stops, or otherwise affects the retry workflow itself.</summary>
    private void RenderAdministratorRetryElapsed()
    {
        var state = ViewModel.AdministratorRetry.State;
        if (!state.IsAdministratorRetryRunning || state.RunningSinceUtc is not { } runningSince)
        {
            administratorRetryElapsedTimer.Stop();
            return;
        }
        var elapsed = DateTimeOffset.UtcNow - runningSince;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        AdministratorRetryElapsedText.Text =
            $"Retrying {state.ReplaceableRootCount} restricted area(s) - elapsed {elapsed:mm\\:ss} of up to {AdministratorRetryUx.SafetyLimit.TotalMinutes:N0} minutes. " +
            "CLYR is reading file and folder metadata only.";
    }

    /// <summary>Requires an explicit Continue on the confirmation dialog before ever calling
    /// <see cref="ResultsViewModel.AdministratorRetry"/>'s <c>RunAsync</c> — cancelling or dismissing the dialog
    /// (any <see cref="ContentDialogResult"/> other than <see cref="ContentDialogResult.Primary"/>) returns
    /// without starting a retry, and <see cref="AdministratorRetryController.CanStart"/> independently prevents a
    /// second concurrent attempt even if this handler were somehow re-entered. The outer <c>try/catch</c> is a
    /// last-resort safety boundary only — <see cref="AdministratorRetryController.RunAsync"/> already contains
    /// every expected operational exception itself and reports a calm terminal state instead of throwing, so this
    /// is never expected to trigger; it exists only so this <see langword="async void"/> handler can never let an
    /// unexpected non-fatal exception escape into the WinUI dispatcher, which would otherwise terminate the app.
    /// It never reimplements the coordinator's or launcher's own outcome mapping — on the rare unexpected
    /// exception it only re-renders whatever state the controller itself already settled on.</summary>
    private async void RequestAdministratorRetry(object sender, RoutedEventArgs args)
    {
        var r = ViewModel.Session.Result;
        if (r is null || !ViewModel.AdministratorRetry.CanStart(r)) return;

        try
        {
            var replaceableRootCount = ViewModel.AdministratorRetry.State.ReplaceableRootCount;
            var dialog = new ContentDialog
            {
                Title = AdministratorRetryUx.ConfirmationTitle(replaceableRootCount),
                Content = new TextBlock { Text = AdministratorRetryUx.ConfirmationBody(AdministratorRetryUx.SafetyLimit), TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = AdministratorRetryUx.ConfirmationPrimaryButtonText,
                CloseButtonText = AdministratorRetryUx.ConfirmationCloseButtonText,
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };
            AutomationProperties.SetName(dialog, "Administrator retry confirmation dialog");

            var choice = await dialog.ShowAsync();
            if (choice != ContentDialogResult.Primary) return;

            await ViewModel.AdministratorRetry.RunAsync(r);
        }
        catch (Exception exception) when (AdministratorRetryUx.IsRecoverable(exception))
        {
            RenderAdministratorRetry();
        }
    }

    private void AnalyzeDrive(object sender, RoutedEventArgs args) => ViewModel.Navigate("Scan");
    private void ViewScanProgressFromResults(object sender, RoutedEventArgs args) => ViewModel.Navigate("Scan");
    private void RunAgain(object sender, RoutedEventArgs args) => ViewModel.Navigate("Scan");
    private void ReviewActions(object sender, RoutedEventArgs args) => ViewModel.Navigate("Review Plan");

    private void Reflow(Controls.ResponsivePageWidth mode)
    {
        var narrow = mode == Controls.ResponsivePageWidth.Narrow;
        EmptyActions.Orientation = narrow ? Orientation.Vertical : Orientation.Horizontal;
        ProvisionalActions.Orientation = narrow ? Orientation.Vertical : Orientation.Horizontal;
        FinalActionArea.Orientation = narrow ? Orientation.Vertical : Orientation.Horizontal;
        FindingFilter.HorizontalAlignment = narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;

        Position(DriveUsedMetricPanel, 0, 0);
        Position(NotObservedMetricPanel, narrow ? 1 : 1, 0);
        Position(FreeMetricPanel, narrow ? 0 : 2, narrow ? 1 : 0);
        Position(DriveUsedTotalPanel, narrow ? 1 : 3, narrow ? 1 : 0);
    }

    private static void Position(FrameworkElement element, int column, int row)
    {
        Grid.SetColumn(element, column);
        Grid.SetRow(element, row);
    }
}

internal sealed record ResultsContributorItem(
    int Rank, string Name, string Glyph, string Size, string Percentage, double PercentageValue, string AccessibleText);

internal sealed record ResultsFindingItem(
    string Title, string Size, string Category, string Confidence, string SafetyStatus, string Explanation,
    FindingConfidence ConfidenceValue, string AccessibleText);

internal sealed record ResultsPathItem(
    string Name, string ShortPath, string FullPath, string Size, string Percentage, string Glyph, string AccessibleText);

internal sealed record ResultsLimitationRow(string Label, string Value);
