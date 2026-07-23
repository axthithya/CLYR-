using Clyr.App.ViewModels;
using Clyr.Contracts;
using Clyr.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Clyr.App.Pages;

public sealed partial class ResultsPage : Page
{
    /// <summary>Section 9 correction: the default contributor view always includes at least this many top
    /// categories (by observed size), regardless of their individual share — never a technical byte threshold
    /// that could behave badly on a small drive.</summary>
    private const int DefaultContributorCount = 10;

    /// <summary>Beyond <see cref="DefaultContributorCount"/>, a category is still shown by default only when it
    /// represents a practically useful share of mapped storage — tiny entries (129 B, 1.18 MiB, and similar) add
    /// no value in the primary view and are pushed behind "Show all categories" instead.</summary>
    private const double DefaultContributorMinPercentage = 0.5;

    /// <summary>An upper bound on the default view regardless of how many categories clear the percentage
    /// threshold — the default view stays a short, scannable list; everything beyond this remains one click away,
    /// never deleted from the underlying result.</summary>
    private const int MaxDefaultContributorCount = 15;

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
    private ResultsPathItem[] topLevelDirectoriesAll = [];
    private ResultsPathItem[] topLevelDirectoriesDefault = [];
    private bool topLevelDirectoriesExpanded;

    /// <summary>Section 10 correction: mirrors the contributor list's default-inclusion rule (section 9) — the
    /// top N folders by size, plus any further folder still representing a practically useful share of mapped
    /// storage, bounded by a maximum. Zero-byte entries are never included in the default view.</summary>
    private const int DefaultTopLevelFolderCount = 10;
    private const double DefaultTopLevelFolderMinPercentage = 0.5;
    private const int MaxDefaultTopLevelFolderCount = 15;

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
            ? "CLYR is still inspecting this drive. View the Scan page for live progress."
            : "The analysis stopped before finishing. Start a new analysis, or view the Scan page for what was found.";
        ProvisionalBadge.Text = stillRunning ? "In progress" : "Incomplete";
        ProvisionalBadge.Glyph = stillRunning ? "" : "";
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
        // Section 6 correction: "Completed with warnings" next to a separate "N access issues" badge said the
        // same thing twice. One neutral lifecycle status here; the access-issue badge below carries the count.
        StatusBadgeControl.Text = completedWithWarnings
            ? "Drive analysis complete"
            : quickEstimateComplete ? "Quick estimate complete" : OverviewPage.Humanize(r.Status);
        StatusBadgeControl.Glyph = completedWithWarnings ? "" : "";

        // Phase (Quick truthfulness correction): never a flat sum across every issue — genuine warnings (access
        // denied, entries changing mid-scan, enumeration failures, fatal problems) are shown separately from
        // scan-policy boundaries (Quick's time/item/pending-capacity budget being reached, an expected outcome
        // of a bounded scan, not a warning about the scan itself). Informational notes (reparse skips, cloud
        // placeholders, checkpoint resumption) are not badged at all; they remain in the limitations list below.
        var warningCount = AccessIssueCount(r);
        WarningBadgeControl.Visibility = warningCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (warningCount > 0)
        {
            // Section 6/14: name the actual warning category rather than a generic count — these are access
            // warnings (permission-limited or access-denied areas), never an unqualified "N warnings".
            // Section 15: "access issues" reads more naturally in normal UI; the more technical "access warnings"
            // phrasing remains in Developer Mode and other advanced surfaces.
            WarningBadgeControl.Text = $"{warningCount:N0} access {(warningCount == 1 ? "issue" : "issues")}";
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
        // Section 2/8: a distinct, specific state — never the generic "coverage cannot be calculated" wording —
        // for the one condition where logical (namespace) bytes legitimately exceed the drive's physical
        // used-bytes basis (hard links, sparse files, compression, filesystem-managed storage).
        var basisDiffers = summary.Consistency.HasFlag(AccountingConsistency.LogicalExceedsDriveUsed);

        // Section 4 correction: "Coverage" is now a permanent caption under the large value (never hidden) — the
        // value itself is either the real percentage or the one canonical phrase used everywhere in this app for
        // an incompatible accounting basis: "Coverage cannot be calculated". Never alternate between that and
        // "Unavailable"/"Cannot be compared directly"/"Coverage unavailable"/"Accounted percentage unavailable".
        AccountedPercentValue.Text = summary.AccountedPercentage is { } accounted ? $"{accounted:F1}%" : "Cannot be calculated";
        AccountedDescriptionText.Text = summary.AccountedPercentage is not null
            ? "of this drive's used storage was mapped by this analysis."
            : basisDiffers
                ? "Some files share storage or use compression, so logical file sizes can be larger than the physical space Windows reports."
                : "Coverage cannot be calculated for this analysis.";
        AccountedMetricLabel.Text = basisDiffers ? "Storage found (logical size)" : "Storage mapped";
        AccountedMetric.Text = OverviewPage.Format(r.LogicalBytesObserved);
        // Section 5: never a negative "amount of unobserved storage" — PresentableUnaccountedDriveBytes is null
        // (never a raw negative UnaccountedDriveBytes) exactly when the bases are incompatible.
        NotObservedMetric.Text = summary.PresentableUnaccountedDriveBytes is { } notObserved
            ? OverviewPage.FormatSigned(notObserved)
            : "Not available";
        FreeMetric.Text = drive?.FreeBytes is { } free ? OverviewPage.Format(free) : "Unavailable";
        DriveUsedMetric.Text = r.DriveUsedBytes is { } used ? OverviewPage.Format(used) : "Unavailable";

        // Section 8 correction: lead with the plain numbers, not a technical accounting-basis paragraph. The
        // complete technical explanation (hard links, sparse files, compression, filesystem-managed storage)
        // moves into "How these numbers are calculated" (see RenderQualityAndLimitations) instead of repeating
        // here — never render a proportional bar implying 276 GiB fits inside a 275 GiB drive.
        StorageVisualization.Visibility = basisDiffers ? Visibility.Collapsed : Visibility.Visible;
        AccountingBasisDiffersPanel.Visibility = basisDiffers ? Visibility.Visible : Visibility.Collapsed;
        if (basisDiffers)
        {
            AccountingBasisDiffersText.Text = "Some files share storage or use compression, so logical file sizes can be larger " +
                "than the physical space Windows reports.";
            AutomationProperties.SetName(AccountingBasisDiffersPanel, AccountingBasisDiffersText.Text);
            return;
        }

        double accountedShare = 0, notObservedShare = 0, freeShare = 0;
        if (drive?.CapacityBytes is > 0)
        {
            var capacity = (double)drive.CapacityBytes.Value;
            accountedShare = Math.Clamp(r.LogicalBytesObserved / capacity * 100, 0, 100);
            notObservedShare = summary.PresentableUnaccountedDriveBytes is { } unaccounted
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
        // Section 4 correction: Coverage is already shown once, as the large top-right value — this card no
        // longer repeats it. Only Categorized remains here.
        ClassificationHeadlineText.Text = summary.ClassificationPercentage is { } classified
            ? $"{classified:F1}% of mapped storage"
            : "Unavailable";

        // Section 10 correction: administrator retry only carries aggregate per-root byte counts, never per-file
        // classification evidence — every retried byte lands in Unknown, which can lower the categorized
        // percentage even though coverage improved. Explain that truthfully, in plain language, rather than
        // leaving it unexplained.
        var (retryApplied, uninspectedUnclassified) = RetryUninspectedUnclassifiedBytes();
        ClassificationRetryNoteText.Visibility = retryApplied && uninspectedUnclassified > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (retryApplied && uninspectedUnclassified > 0)
            ClassificationRetryNoteText.Text = "Why did the categorized percentage change? Administrator Retry found more storage, " +
                "but it did not return enough file-level details to categorize all of it. The storage is included in the total and " +
                $"remains uncategorized ({OverviewPage.Format(uninspectedUnclassified)}).";
    }

    /// <summary>Truthful, bounded estimate of how much of the currently active result's observed logical bytes
    /// came from a successfully applied administrator retry without any per-file/category evidence — never a
    /// fabricated category assignment, only a plain byte count. Additive bytes are always uninspected; a positive
    /// replacement net change is also uninspected (the replaced root grew without new classification evidence); a
    /// negative replacement net change removed bytes rather than adding unclassified ones, so it contributes
    /// nothing here.</summary>
    private (bool retryApplied, long uninspectedUnclassified) RetryUninspectedUnclassifiedBytes()
    {
        var state = ViewModel.AdministratorRetry.State;
        if (state.Phase != AdministratorRetryPhase.Applied || state.CombinedResult?.Attempt is not { } attempt) return (false, 0);
        return (true, Math.Max(0, attempt.AdditiveLogicalBytes) + Math.Max(0, attempt.ReplacementNetLogicalBytes));
    }

    private void RenderQualityAndLimitations(ScanResult r)
    {
        var summary = ScanAccounting.Summarize(r);
        // Section 10 correction: AccountingBasisDiffers gets its own neutral "Coverage unavailable" badge —
        // it must never collapse into "Limited coverage", reserved for a genuinely low but valid, comparable
        // percentage.
        var (qualityText, qualityGlyph) = summary.Quality switch
        {
            ScanQuality.Excellent => ("Excellent", ""),
            ScanQuality.Good => ("Good", ""),
            ScanQuality.AccountingBasisDiffers => ("Cannot be calculated", ""),
            _ => ("Limited", "") // Partial, Insufficient (a genuinely low, valid percentage)
        };
        QualityBadgeControl.Text = qualityText;
        QualityBadgeControl.Glyph = qualityGlyph;
        // Section 6: the normal UI no longer exposes Deep Analysis — "Run Deep Analysis for more complete
        // drive coverage" is obsolete. Accounting-basis incompatibility and remaining-inaccessible-areas stay
        // two separate concepts, never merged into one ambiguous sentence.
        QualityDescriptionText.Text = summary.Quality switch
        {
            ScanQuality.Excellent or ScanQuality.Good => "CLYR mapped nearly all of the storage Windows currently reports as used.",
            ScanQuality.AccountingBasisDiffers => "Storage found cannot be directly compared with what Windows reports as used.",
            _ => "Some areas could still not be inspected."
        };

        var rows = new List<ResultsLimitationRow>
        {
            new("Coverage vs. categorized", "Coverage is how much used storage CLYR mapped. Categorized is how much of that mapped storage matched a known type. Mapped but uncategorized storage is still real storage — it simply has no known type yet."),
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
                    $"Rank {index + 1}, {name}, {size}, {percentage:F1}% share of observed logical storage.");
            })
            .ToArray() ?? [];
        ContributorsSection.Visibility = contributors.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        var (retryApplied, uninspectedUnclassified) = RetryUninspectedUnclassifiedBytes();
        // Section 8 correction: one consistent heading in every state — never changed by coverage or by whether
        // a retry was applied.
        ContributorsHeader.Title = "What is using space?";
        // Section 8/11 correction: drop the accessibility-implementation sentence ("include exact sizes so the
        // ranking never relies on color alone") from user-facing copy — natural wording instead. After a retry
        // that added storage the classification protocol cannot categorize, say so truthfully.
        ContributorsHeader.Description = retryApplied && uninspectedUnclassified > 0
            ? "Largest categories found during this analysis. Percentages are based on the storage CLYR mapped. Some administrator-inspected storage could not be categorized and is not shown as a named category below."
            : "Largest categories found during this analysis. Percentages are based on the storage CLYR mapped.";

        RenderContributorList();
    }

    /// <summary>Section 9 correction: the default view shows the most useful categories only — the top
    /// <see cref="DefaultContributorCount"/> plus any further category still representing at least
    /// <see cref="DefaultContributorMinPercentage"/>% of mapped storage, bounded by
    /// <see cref="MaxDefaultContributorCount"/>. Every category remains available, in the exact same order, via
    /// "Show all categories" — nothing is ever removed from the underlying result.</summary>
    private void RenderContributorList()
    {
        var defaultVisible = contributors
            .Where((item, index) => index < DefaultContributorCount || item.PercentageValue >= DefaultContributorMinPercentage)
            .Take(MaxDefaultContributorCount)
            .ToArray();
        var visible = contributorsExpanded ? contributors : defaultVisible;
        ContributorList.ItemsSource = visible;
        ContributorsExpandButton.Visibility = contributors.Length > defaultVisible.Length ? Visibility.Visible : Visibility.Collapsed;
        if (contributors.Length > defaultVisible.Length)
            ContributorsExpandButton.Content = contributorsExpanded ? "Show fewer categories" : $"Show all {contributors.Length} categories";
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
            // Section 12 correction: a single natural status word/phrase replaces the old repeated
            // "Confirmed confidence · Protected or product-managed · CLYR provides explanation only" caption —
            // normal users should not need to read internal confidence terminology on every row. Category and
            // confidence remain available as a tooltip for anyone who wants the technical detail.
            var naturalStatus = NaturalFindingStatus(item.Category, item.Status);
            var technicalDetail = $"{category} - {confidence} confidence";
            return new ResultsFindingItem(item.Title, size, naturalStatus, item.Explanation.WhatItMeans, technicalDetail, item.Confidence,
                $"{item.Title}, {size}, {naturalStatus}. {item.Explanation.WhatItMeans}");
        }).ToArray() ?? [];
        FindingsSection.Visibility = findings.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Section 11 correction: administrator retry never regenerates per-file findings — say so truthfully
        // rather than letting a successful retry imply findings were newly verified.
        var (retryApplied, uninspectedUnclassified) = RetryUninspectedUnclassifiedBytes();
        FindingsProvenanceText.Visibility = retryApplied ? Visibility.Visible : Visibility.Collapsed;
        if (retryApplied)
            FindingsProvenanceText.Text = uninspectedUnclassified > 0
                ? "Findings remain based on the original Drive Analysis. Storage found by Administrator Retry without enough detail to categorize remains uncategorized."
                : "Findings remain based on the original Drive Analysis.";
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
        // Section 12/13 correction: the main list must contain only non-overlapping rows - TopLevelDirectories is
        // exactly that (depth-1 children of the drive root). LargestDirectories can include deeper, overlapping
        // descendants, which must never be summed against the top-level rows above them; those go into a
        // clearly-labelled, overlap-explained secondary view instead. Zero-byte entries add no practical value in
        // the default view, so they are hidden until "Show all folders" is used.
        var topLevelSorted = r.TopLevelDirectories.OrderByDescending(item => item.LogicalBytes).ToArray();
        topLevelDirectoriesAll = topLevelSorted.Select(item => ToPathItem(item, r, "")).ToArray();
        topLevelDirectoriesDefault = topLevelDirectoriesAll
            .Where(item => item.PercentageValue > 0)
            .Where((item, index) => index < DefaultTopLevelFolderCount || item.PercentageValue >= DefaultTopLevelFolderMinPercentage)
            .Take(MaxDefaultTopLevelFolderCount)
            .ToArray();
        RenderTopLevelDirectoryList();
        DirectoriesSection.Visibility = r.TopLevelDirectories.Count > 0 || r.LargestDirectories.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var topLevelPaths = new HashSet<string>(r.TopLevelDirectories.Select(item => NormalizedAncestryKey(item.DisplayPath)), StringComparer.OrdinalIgnoreCase);
        var nested = r.LargestDirectories.Where(item => !topLevelPaths.Contains(NormalizedAncestryKey(item.DisplayPath))).ToArray();
        NestedDirectoryList.ItemsSource = nested.Select(item => ToPathItem(item, r, "")).ToArray();
        NestedDirectoriesExpander.Visibility = nested.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        var (retryApplied, _) = RetryUninspectedUnclassifiedBytes();
        DirectoriesProvenanceText.Visibility = retryApplied ? Visibility.Visible : Visibility.Collapsed;
        if (retryApplied)
            DirectoriesProvenanceText.Text = "Top-level folder totals were refreshed by Administrator Retry where a retried area " +
                "corresponds to one of these folders. Deeper nested folders below were not individually re-scanned.";

        FileList.ItemsSource = r.LargestFiles.Select(item => ToPathItem(item, r, "")).ToArray();
        FilesSection.Visibility = r.LargestFiles.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        FilesProvenanceText.Visibility = r.LargestFiles.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        FilesProvenanceText.Text = retryApplied
            ? "Large files are not automatically safe to remove. Individual-file rankings remain based on the " +
                "original Drive Analysis. Administrator Retry updates storage accounting and directory coverage only."
            : "Large files are not automatically safe to remove.";
    }

    /// <summary>Renders the top-level folder list, hiding zero-byte entries by default (section 13) — they add
    /// no practical value in the common case and only clutter the default view. "Show all folders" reveals them
    /// without ever discarding the underlying data.</summary>
    private void RenderTopLevelDirectoryList()
    {
        DirectoryList.ItemsSource = topLevelDirectoriesExpanded
            ? topLevelDirectoriesAll
            : topLevelDirectoriesDefault;
        ShowAllFoldersButton.Visibility = topLevelDirectoriesDefault.Length < topLevelDirectoriesAll.Length ? Visibility.Visible : Visibility.Collapsed;
        ShowAllFoldersButton.Content = topLevelDirectoriesExpanded ? "Show fewer folders" : "Show all folders";
    }

    private void ToggleTopLevelDirectories(object sender, RoutedEventArgs args)
    {
        topLevelDirectoriesExpanded = !topLevelDirectoriesExpanded;
        RenderTopLevelDirectoryList();
    }

    /// <summary>Maps a finding's raw <see cref="FindingStatus"/> to one short, natural phrase a non-technical
    /// user can understand at a glance — never broadening cleanup eligibility, never marking a protected item as
    /// cleanable; this is presentation only. Full category/confidence detail remains one tooltip away.</summary>
    private static string NaturalFindingStatus(StorageCategory category, FindingStatus status) => status switch
    {
        FindingStatus.Protected => category is StorageCategory.WindowsSystemManaged or StorageCategory.WindowsUpdateServicing or StorageCategory.RestoreRecovery
            ? "Protected by Windows"
            : "Managed by another application",
        FindingStatus.Review => "Needs your review",
        FindingStatus.Unknown => "Unsupported",
        _ => "Information only"
    };

    /// <summary>The count shown as "access issues" in normal UI (and "access warnings" in technical/advanced
    /// surfaces) — genuine access-denied/permission/data-changed/fatal issues only, never Quick's expected
    /// policy-boundary budget or purely informational notes.</summary>
    private static long AccessIssueCount(ScanResult r) => r.Issues.Where(item => item.Severity is
        ScanIssueSeverity.AccessWarning or ScanIssueSeverity.PermissionLimited or ScanIssueSeverity.DataChanged or ScanIssueSeverity.Fatal)
        .Sum(item => item.Count);

    private static ResultsPathItem ToPathItem(RankedPath item, ScanResult r, string glyph)
    {
        var size = OverviewPage.Format(item.LogicalBytes);
        var name = ShortName(item.DisplayPath);
        var shortPath = ShortenPath(item.DisplayPath);
        var percentage = r.LogicalBytesObserved > 0 ? Math.Clamp(item.LogicalBytes * 100d / r.LogicalBytesObserved, 0, 100) : 0;
        return new ResultsPathItem(name, shortPath, item.DisplayPath, size, $"{percentage:F1}%", percentage, glyph,
            $"{name}, {shortPath}, {size}, {percentage:F1}% share of observed logical storage.");
    }

    /// <summary>Normalizes a Windows path for case-insensitive ancestry comparison only (trailing separator and
    /// case differences must never make the same folder appear as both a top-level and a "nested" row) — never
    /// used for identity/security decisions, only for this display-grouping choice.</summary>
    private static string NormalizedAncestryKey(string path) => path.TrimEnd('\\').ToUpperInvariant();

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
        var everAttempted = state.Phase is not (AdministratorRetryPhase.Idle or AdministratorRetryPhase.Hidden or AdministratorRetryPhase.Running);
        // Section 3 correction (confirmed real-machine defect): the card — and its terminal summary — must stay
        // visible even once no further retry remains available (IsAdministratorRetryAvailable can legitimately
        // become false after a fully-resolved retry). Collapsing on availability alone silently threw the
        // terminal summary away.
        AdministratorRetryCard.Visibility =
            state.IsAdministratorRetryAvailable || state.IsAdministratorRetryRunning || everAttempted
                ? Visibility.Visible : Visibility.Collapsed;
        // The pre-retry pitch text ("Some folders could not be fully inspected...") is redundant and stale once
        // a retry has actually concluded — the terminal InfoBar/summary explains the outcome instead.
        AdministratorRetryHeader.Visibility = everAttempted ? Visibility.Collapsed : Visibility.Visible;

        AdministratorRetryButton.Content = everAttempted ? AdministratorRetryUx.RetryAgainButtonText : AdministratorRetryUx.ButtonText;
        AutomationProperties.SetName(AdministratorRetryButton, everAttempted ? AdministratorRetryUx.RetryAgainButtonText : AdministratorRetryUx.ButtonText);
        AdministratorRetryButton.IsEnabled = state.IsAdministratorRetryAvailable && !state.IsAdministratorRetryRunning;
        // Once no further retry can ever be available (fully resolved), the button disappears entirely rather
        // than lingering, disabled, beneath the summary.
        AdministratorRetryButton.Visibility =
            state.IsAdministratorRetryAvailable || state.IsAdministratorRetryRunning ? Visibility.Visible : Visibility.Collapsed;

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

        // Phase (root-reconciliation correction): the completion wording must distinguish areas that safely
        // added previously-unobserved storage (Additive) from areas that changed and were safely replaced
        // (Replacement, whose net change may be negative) from areas that found no new evidence (Overlap) — and
        // must never call a replacement's net change "newly accounted" bytes (section 8/9).
        var showSummary = state.Phase == AdministratorRetryPhase.Applied;
        AdministratorRetrySummary.Visibility = showSummary ? Visibility.Visible : Visibility.Collapsed;
        AdministratorRetryComparison.Visibility = showSummary ? Visibility.Visible : Visibility.Collapsed;
        if (showSummary)
        {
            var attempt = state.CombinedResult?.Attempt;
            var additiveBytes = attempt?.AdditiveLogicalBytes ?? 0;
            var replacementNet = attempt?.ReplacementNetLogicalBytes ?? 0;
            var rootsAdditive = attempt?.RootsAdditive ?? 0;
            var rootsReplaced = attempt?.RootsReplaced ?? 0;
            var rootsOverlapped = attempt?.RootsOverlapped ?? 0;

            // Section 9 correction: natural language, distinguishing added/refreshed/overlap/remaining areas —
            // never describing a replacement's net change as "newly added" storage.
            var areaParts = new List<string>();
            if (rootsAdditive > 0) areaParts.Add($"{rootsAdditive} added new storage information");
            if (rootsReplaced > 0) areaParts.Add($"{rootsReplaced} refreshed existing results");
            if (rootsOverlapped > 0) areaParts.Add($"{rootsOverlapped} contained no additional information");
            if (state.RootsStillInaccessible > 0) areaParts.Add($"{state.RootsStillInaccessible} remain restricted");
            var areasText = areaParts.Count > 0 ? string.Join(", ", areaParts) + "." : "No additional areas were found.";

            // Section 3 correction: always show Files checked/Folders checked/Access issues/Coverage before→after
            // — using the actual reconciliation values, never conditionally hidden when unchanged, matching the
            // confirmed real-machine example exactly.
            var originalResult = state.CombinedResult?.OriginalResult;
            var currentResult = ViewModel.Session.Result;
            var comparisons = new List<string>();
            if (originalResult is not null && currentResult is not null)
            {
                comparisons.Add($"Files checked: {originalResult.Coverage.FilesObserved:N0} -> {currentResult.Coverage.FilesObserved:N0}.");
                comparisons.Add($"Folders checked: {originalResult.Coverage.DirectoriesObserved:N0} -> {currentResult.Coverage.DirectoriesObserved:N0}.");
                comparisons.Add($"Access issues: {AccessIssueCount(originalResult):N0} -> {AccessIssueCount(currentResult):N0}.");
            }
            AdministratorRetrySummary.Text = "CLYR safely inspected additional restricted areas. " + areasText +
                (comparisons.Count > 0 ? " " + string.Join(" ", comparisons) : string.Empty);

            // Section 7 correction: "before" is shown whenever it was ever valid, independent of whether "after"
            // is now unavailable — a retry that adds real logical-over-physical data is a success, never a
            // failure, and must never be collapsed into one blanket "Unavailable" that could as easily read as
            // "the comparison itself failed."
            var beforePercentage = state.CombinedResult is { } combined ? ScanAccounting.Summarize(combined.OriginalResult).AccountedPercentage : null;
            var afterPercentage = ViewModel.Session.Result is { } current ? ScanAccounting.Summarize(current).AccountedPercentage : null;
            var beforeText = beforePercentage is { } before ? $"{before:F1}%" : "cannot be calculated";
            var afterText = afterPercentage is { } after ? $"{after:F1}%" : "cannot be calculated";
            AdministratorRetryCoverageText.Text = $"{beforeText} -> {afterText}";
            // Never the old full-sum (additive + replacement net) figure — only genuinely additive bytes are
            // safe to describe as "newly accounted".
            AdministratorRetryNewlyAccountedText.Text = OverviewPage.Format(additiveBytes);
            AdministratorRetryAreasText.Text =
                $"{state.RootsCompleted + state.RootsStillInaccessible} attempted - {state.RootsCompleted} inspected - {state.RootsStillInaccessible} remaining";

            // Never claimed as a retry failure — the after-percentage becoming unavailable is a truthful
            // accounting-basis fact, not an error, and must say so explicitly rather than leaving it unexplained.
            var afterBasisDiffers = ViewModel.Session.Result is { } activeResult
                && ScanAccounting.Summarize(activeResult).Consistency.HasFlag(AccountingConsistency.LogicalExceedsDriveUsed);
            AdministratorRetryBasisNoteText.Visibility = afterBasisDiffers ? Visibility.Visible : Visibility.Collapsed;
            if (afterBasisDiffers)
                AdministratorRetryBasisNoteText.Text = "The retry added logical filesystem data whose accounting basis cannot be " +
                    "directly compared with physical drive usage. This is not a retry failure.";
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
    string Title, string Size, string NaturalStatus, string Explanation, string TechnicalDetail,
    FindingConfidence ConfidenceValue, string AccessibleText);

internal sealed record ResultsPathItem(
    string Name, string ShortPath, string FullPath, string Size, string Percentage, double PercentageValue, string Glyph, string AccessibleText);

internal sealed record ResultsLimitationRow(string Label, string Value);
