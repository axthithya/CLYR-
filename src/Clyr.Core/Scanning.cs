using Clyr.Contracts;

namespace Clyr.Core;

[Flags]
public enum EntryTraits { None = 0, Directory = 1, ReparsePoint = 2, CloudPlaceholder = 4, Sparse = 8, Compressed = 16 }

/// <summary>
/// <paramref name="AllocatedBytes"/> is the real on-disk allocation reported by the filesystem (accounts for
/// sparse/compressed storage) — null when it could not be read, never invented. <paramref name="FileIdentity"/>
/// is a stable, volume-scoped file identity (NTFS file index) used to detect hard links so the same physical
/// content is never counted twice in unique-allocation totals — null when identity could not be read. Both are
/// read through read-only Windows metadata APIs; neither ever opens or reads file content.
/// </summary>
public sealed record FileSystemEntry(string FullPath, long LogicalBytes, EntryTraits Traits,
    long? AllocatedBytes = null, ulong? FileIdentity = null, int? HardLinkCount = null);
public interface IDriveDiscovery { IReadOnlyList<DriveSummary> Discover(); }
public interface IFileSystemEnumerator { IEnumerable<FileSystemEntry> Enumerate(string directory); }
public interface IScanService
{
    Task<ScanResult> ScanAsync(ScanRequest request, IProgress<ScanProgress>? progress, CancellationToken cancellationToken);
}

public static class ScanPathValidator
{
    public static bool TryNormalizeRoot(string? value, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) { error = "A drive root is required."; return false; }
        var candidate = value.Trim();
        if (candidate.Length != 3 || !char.IsAsciiLetter(candidate[0]) || candidate[1] != ':' || candidate[2] != '\\')
        { error = "Only an absolute drive root such as C:\\ is supported."; return false; }
        normalized = char.ToUpperInvariant(candidate[0]) + ":\\";
        return true;
    }
}

public sealed class ScanCoordinator(IFileSystemEnumerator fileSystem, IDriveDiscovery drives, IClock clock,
    IClassificationProvider? classificationProvider = null, IScanCheckpointStore? checkpoints = null,
    QuickAnalysisPolicy? quickPolicy = null) : IScanService
{
    private const int MaximumIssues = 32;
    private const int MaximumPendingDirectories = 20_000;
    private const int MaximumCheckpointDirectories = 500;
    private int active;

    /// <summary>Directory leaf names that Quick Analysis's Stage B prioritizes over everything discovered so
    /// far that isn't already known to matter — these are where the overwhelming majority of a typical Windows
    /// drive's used space actually lives.</summary>
    private static readonly string[] KnownHighValueRootNames = ["Windows", "Program Files", "Program Files (x86)", "ProgramData", "Users"];
    private static readonly string[] KnownHighValueUserSubNames = ["AppData", "Downloads", "Documents"];
    private static readonly string[] KnownDeveloperCacheNames = [".nuget", "npm-cache", "pip-cache", ".cache", "docker", "wsl", "android", ".android", ".gradle", "package cache", "yarn cache", "node_modules"];

    public async Task<ScanResult> ScanAsync(ScanRequest request, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref active, 1, 0) != 0)
            return Failed(request, "scan.overlap", "Another scan is already active.");
        try { return await Task.Run(() => Scan(request, progress, cancellationToken), CancellationToken.None).ConfigureAwait(false); }
        finally { Volatile.Write(ref active, 0); }
    }

    private ScanResult Scan(ScanRequest request, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        if (!ScanPathValidator.TryNormalizeRoot(request.Root, out var root, out var pathError))
            return Failed(request, "scan.path-invalid", pathError);
        var drive = drives.Discover().FirstOrDefault(item => string.Equals(item.Root, root, StringComparison.OrdinalIgnoreCase));
        if (drive is null) return Failed(request, "scan.drive-not-found", "The selected drive is not available.");
        if (!drive.IsReady || !drive.IsSupported) return Failed(request, "scan.drive-unsupported", drive.SupportReason);

        var policyVersion = (quickPolicy ?? QuickAnalysisPolicy.Default).PolicyVersion;
        var checkpoint = request is { Mode: ScanMode.Quick, ContinueFromCheckpoint: true } ? checkpoints?.TryLoad(root, ScanMode.Quick) : null;
        var checkpointRejectedReason = request is { Mode: ScanMode.Quick, ContinueFromCheckpoint: true } && checkpoint is null
            ? "No saved checkpoint was found for this drive; Quick Analysis started from the beginning."
            : checkpoint is not null && checkpoint.PolicyVersion != policyVersion
                ? "The saved checkpoint used an older Quick Analysis policy and was discarded; Quick Analysis started from the beginning."
                : null;
        if (checkpoint is not null && checkpoint.PolicyVersion != policyVersion) checkpoint = null;

        var sessionStarted = clock.UtcNow;
        var started = checkpoint?.OriginalStartedAtUtc ?? sessionStarted;
        var classification = classificationProvider?.Start(request with { Root = root }, drive);
        var policy = ScanPolicy.For(request, quickPolicy);
        var topFiles = new BoundedRanking(policy.TopCount);
        var topDirectories = new BoundedRanking(policy.TopCount);
        var topLevel = new BoundedRanking(policy.TopLevelCount);
        var extensions = Enum.GetValues<ExtensionFamily>().ToDictionary(item => item, _ => new ExtensionCounter());
        var issues = new Dictionary<ScanIssueKind, MutableIssue>();
        long files = checkpoint?.FilesObserved ?? 0, directories = checkpoint?.DirectoriesObserved ?? 0,
            bytes = checkpoint?.LogicalBytesObserved ?? 0, inaccessible = 0, reparse = 0, cloud = 0, changed = 0, skipped = 0;
        long allocatedBytes = 0, uniqueAllocatedBytes = 0, filesWithUnavailableAllocatedSize = 0,
            sparseFiles = 0, compressedFiles = 0, visibleHardLinkEntries = 0;
        // Volume-scoped NTFS file indices already seen this scan, used only to de-duplicate hard-linked
        // content out of the unique-allocation total. Bounded by "one entry per file this scan observes" —
        // the same order of magnitude as the scan itself, not an independent unbounded growth.
        var seenFileIdentities = new HashSet<ulong>();
        var lastProgress = DateTimeOffset.MinValue;
        var policyBoundaryHit = false;

        if (checkpointRejectedReason is not null) AddIssue(ScanIssueKind.Unsupported, "scan.checkpoint-unavailable", checkpointRejectedReason, ScanIssueSeverity.Information);
        else if (checkpoint is not null) AddIssue(ScanIssueKind.Unsupported, "scan.checkpoint-resumed", "Quick Analysis resumed from a saved checkpoint instead of restarting at the root.", ScanIssueSeverity.Information);

        progress?.Report(new(ScanStatus.Preparing, TimeSpan.Zero, files, directories, bytes, 0, RedactPath(root), "Preparing metadata-only scan."));
        try
        {
            if (request.Mode == ScanMode.Quick) RunQuick(checkpoint);
            else RunDeep();
            return cancelledResult ?? Finish(DetermineStatus());
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            CountException(exception);
            return Finish(files + directories > 0 ? ScanStatus.CompletedWithWarnings : ScanStatus.Failed,
                files + directories == 0 ? "scan.root-unavailable" : null,
                files + directories == 0 ? "The drive root could not be enumerated." : null);
        }

        // Deep Analysis: a plain iterative, stack-based depth-first walk with no depth, time, or item bound. It
        // runs until every safely accessible directory has been processed, the caller cancels, or a fatal error
        // occurs — see ScanPolicy.For.
        void RunDeep()
        {
            var stack = new Stack<DirectoryFrame>();
            try
            {
                stack.Push(Open(root, 0, null));
                while (stack.Count > 0)
                {
                    if (CheckCancellation(() => stack.Peek().Path)) return;
                    var frame = stack.Peek();
                    FileSystemEntry? entry;
                    try
                    {
                        if (!frame.Enumerator.MoveNext()) { CompleteStackFrame(stack); continue; }
                        entry = frame.Enumerator.Current;
                    }
                    catch (Exception exception) when (IsExpected(exception))
                    { CountException(exception); CompleteStackFrame(stack); continue; }
                    if (entry is null) continue;
                    if (ObserveEntry(entry, frame, out var isDirectory) && isDirectory)
                    {
                        try { stack.Push(Open(entry.FullPath, frame.Depth + 1, frame)); }
                        catch (Exception exception) when (IsExpected(exception)) { CountException(exception); }
                    }
                    ReportProgress(entry.FullPath);
                }
            }
            finally { while (stack.Count > 0) stack.Pop().Enumerator.Dispose(); }
        }

        // Quick Analysis: Stage A (root itself, opened first and always) plus Stage B/C — a single active
        // enumerator at a time, with every subdirectory it discovers placed into a bounded priority queue rather
        // than descended into immediately. Known high-value roots (Windows, Program Files, ProgramData, Users,
        // AppData/Downloads/Documents, common developer caches) are dequeued far ahead of everything else, so
        // Quick reaches the areas most likely to explain drive usage before its budget runs out — never a plain
        // alphabetical walk. Stage D is honest, bounded completion: time budget, item budget, depth policy,
        // cancellation, or true exhaustion, each recorded as a distinct diagnostic.
        void RunQuick(ScanCheckpoint? resumeFrom)
        {
            var pending = new PriorityQueue<PendingDirectory, (long NegBoost, long Order)>();
            // Directories skipped only because Quick's depth policy was reached, not because of time/item
            // pressure — these are real, known, high-priority gaps (the depth ceiling is what "Continue Quick
            // Analysis" mainly exists to work around) and are worth persisting for the next continuation even
            // when this run otherwise runs to exhaustion. Bounded for the same reason the pending queue is.
            var depthDeferred = new List<string>();
            long order = 0;
            if (resumeFrom is not null)
                // A continuation gets its own fresh depth budget starting from each resumed path (Depth 1, not
                // that path's true filesystem depth) — otherwise a directory already at the depth ceiling could
                // never be resumed into, and "Continue Quick Analysis" could never make progress past it.
                foreach (var path in resumeFrom.PendingDirectories)
                    Enqueue(new(path, 1, null, PriorityBoostFor(Path.GetFileName(path.TrimEnd('\\'))), order++));
            else
                pending.Enqueue(new(root, 0, null, long.MaxValue, order++), (long.MinValue, 0));

            DirectoryFrame? current = null;
            try
            {
                while (true)
                {
                    if (CheckCancellation(() => current?.Path ?? root)) return;
                    if (policy.TimeBudget is { } timeBudget && clock.UtcNow - sessionStarted >= timeBudget)
                    { SaveQuickCheckpoint(current, pending, depthDeferred); AddIssue(ScanIssueKind.ResourceLimit, "scan.quick-time-budget", $"Quick Analysis's {policy.TimeBudget.Value.TotalSeconds:F0}-second time budget was reached; remaining areas were not examined this run.", ScanIssueSeverity.PolicyBoundary); return; }
                    if (policy.ItemBudget is { } itemBudget && files + directories >= itemBudget)
                    { SaveQuickCheckpoint(current, pending, depthDeferred); AddIssue(ScanIssueKind.ResourceLimit, "scan.quick-item-budget", "Quick Analysis's item budget was reached; remaining areas were not examined this run.", ScanIssueSeverity.PolicyBoundary); return; }

                    if (current is null)
                    {
                        if (!pending.TryDequeue(out var next, out _))
                        {
                            // Stage D: the priority queue is exhausted. If depth-policy skips left real,
                            // known areas unexplored, that is still continuation-worthy — only a run that left
                            // nothing behind at all counts as true, final exhaustion.
                            if (depthDeferred.Count > 0) SaveQuickCheckpoint(null, pending, depthDeferred);
                            return;
                        }
                        try { current = Open(next.Path, next.Depth, next.Parent); }
                        catch (Exception exception) when (IsExpected(exception)) { CountException(exception); continue; }
                    }

                    var frame = current!;
                    FileSystemEntry? entry;
                    try
                    {
                        if (!frame.Enumerator.MoveNext()) { CompleteQueueFrame(frame); current = null; continue; }
                        entry = frame.Enumerator.Current;
                    }
                    catch (Exception exception) when (IsExpected(exception))
                    { CountException(exception); CompleteQueueFrame(frame); current = null; continue; }
                    if (entry is null) continue;

                    if (ObserveEntry(entry, frame, out var isDirectory) && isDirectory)
                    {
                        if (frame.Depth >= policy.MaximumDepth)
                        {
                            skipped++;
                            AddIssue(ScanIssueKind.ResourceLimit, "scan.depth-limit", "Quick Analysis depth policy reached; a subdirectory was not examined this run.", ScanIssueSeverity.PolicyBoundary);
                            if (depthDeferred.Count < MaximumCheckpointDirectories) depthDeferred.Add(entry.FullPath);
                        }
                        else Enqueue(new(entry.FullPath, frame.Depth + 1, frame, PriorityBoostFor(Path.GetFileName(entry.FullPath)), order++));
                    }
                    ReportProgress(entry.FullPath);
                }
            }
            finally { current?.Enumerator.Dispose(); }

            void Enqueue(PendingDirectory item)
            {
                if (pending.Count >= MaximumPendingDirectories)
                { skipped++; AddIssue(ScanIssueKind.ResourceLimit, "scan.quick-pending-capacity", "Quick Analysis's pending-directory capacity was reached; a lower-priority area was not queued this run.", ScanIssueSeverity.PolicyBoundary); return; }
                pending.Enqueue(item, (-item.Boost, item.Order));
            }
            void SaveQuickCheckpoint(DirectoryFrame? openFrame, PriorityQueue<PendingDirectory, (long, long)> queue, List<string> deferred)
            {
                policyBoundaryHit = true;
                if (checkpoints is null) return;
                var remaining = new List<string>(MaximumCheckpointDirectories);
                if (openFrame is not null) remaining.Add(openFrame.Path);
                foreach (var item in queue.UnorderedItems.OrderBy(x => x.Priority).Select(x => x.Element))
                { if (remaining.Count >= MaximumCheckpointDirectories) break; remaining.Add(item.Path); }
                foreach (var path in deferred)
                { if (remaining.Count >= MaximumCheckpointDirectories) break; remaining.Add(path); }
                checkpoints.Save(new(root, ScanMode.Quick, policyVersion, started, clock.UtcNow, files, directories, bytes, remaining));
            }
        }

        bool CheckCancellation(Func<string> currentPath)
        {
            if (!cancellationToken.IsCancellationRequested) return false;
            progress?.Report(new(ScanStatus.Cancelling, clock.UtcNow - started, files, directories, bytes,
                inaccessible + reparse + changed + skipped, RedactPath(currentPath()), "Cancellation acknowledged.",
                inaccessible, reparse, WarningCount()));
            cancelledResult = Finish(ScanStatus.Cancelled);
            return true;
        }

        bool ObserveEntry(FileSystemEntry entry, DirectoryFrame frame, out bool isDirectory)
        {
            classification?.Observe(entry);
            isDirectory = false;
            if ((entry.Traits & EntryTraits.ReparsePoint) != 0)
            { reparse++; AddIssue(ScanIssueKind.ReparseSkipped, "scan.reparse-skipped", "A reparse point was not traversed.", ScanIssueSeverity.Information); return false; }
            if ((entry.Traits & EntryTraits.Directory) != 0) { directories++; isDirectory = true; return true; }
            files++;
            var size = Math.Max(0, entry.LogicalBytes);
            frame.DirectBytes += size; frame.FileCount++; bytes += size;
            if ((entry.Traits & EntryTraits.CloudPlaceholder) != 0)
            { cloud++; AddIssue(ScanIssueKind.CloudPlaceholder, "scan.cloud-metadata-only", "Cloud placeholder counted from metadata without hydration.", ScanIssueSeverity.Information); }
            topFiles.Add(new(entry.FullPath, size, 1, MeasurementPrecision.Estimated));
            var family = ExtensionClassifier.Classify(Path.GetExtension(entry.FullPath));
            extensions[family].Bytes += size; extensions[family].Count++;
            ObserveAllocation(entry);
            return true;
        }

        // Phase 7.2.2/7.2.3: allocated-size and hard-link-aware accounting. Never invented — a file whose
        // allocated size could not be read through the read-only Windows API contributes to
        // FilesWithUnavailableAllocatedSize instead of a guessed number. A file whose NTFS identity could not
        // be read is still counted in AllocatedBytesObserved (raw sum) but cannot be de-duplicated, so it is
        // conservatively also added to UniqueAllocatedBytesObserved — undercounting hard-link savings is safer
        // than overstating them.
        void ObserveAllocation(FileSystemEntry entry)
        {
            var allocated = entry.AllocatedBytes;
            if (allocated is { } value) allocatedBytes += Math.Max(0, value);
            else filesWithUnavailableAllocatedSize++;
            if ((entry.Traits & EntryTraits.Sparse) != 0) sparseFiles++;
            if ((entry.Traits & EntryTraits.Compressed) != 0) compressedFiles++;
            if (entry.FileIdentity is { } identity)
            {
                if (seenFileIdentities.Add(identity)) { if (allocated is { } unique) uniqueAllocatedBytes += Math.Max(0, unique); }
                else visibleHardLinkEntries++;
            }
            else if (allocated is { } fallback) uniqueAllocatedBytes += Math.Max(0, fallback);
        }

        void ReportProgress(string currentPath)
        {
            var now = clock.UtcNow;
            if (now - lastProgress < TimeSpan.FromMilliseconds(250)) return;
            lastProgress = now;
            progress?.Report(new(ScanStatus.Scanning, now - started, files, directories, bytes,
                inaccessible + reparse + changed + skipped, RedactPath(currentPath), "Observing filesystem metadata.",
                inaccessible, reparse, WarningCount()));
        }

        DirectoryFrame Open(string path, int depth, DirectoryFrame? parent)
        {
            try { return new(path, depth, parent, fileSystem.Enumerate(path).GetEnumerator()); }
            catch (Exception exception) when (IsExpected(exception)) { throw new ScanOpenException(exception); }
        }
        void CompleteStackFrame(Stack<DirectoryFrame> stack)
        {
            var completed = stack.Pop(); completed.Enumerator.Dispose();
            if (stack.Count > 0) { stack.Peek().DirectBytes += completed.DirectBytes; stack.Peek().FileCount += completed.FileCount; }
            RegisterRanking(completed);
        }
        void CompleteQueueFrame(DirectoryFrame completed)
        {
            completed.Enumerator.Dispose();
            if (completed.Parent is not null) { completed.Parent.DirectBytes += completed.DirectBytes; completed.Parent.FileCount += completed.FileCount; }
            RegisterRanking(completed);
        }
        void RegisterRanking(DirectoryFrame completed)
        {
            if (completed.Depth == 1) topLevel.Add(new(completed.Path, completed.DirectBytes, completed.FileCount, MeasurementPrecision.Estimated));
            if (completed.Depth > 0) topDirectories.Add(new(completed.Path, completed.DirectBytes, completed.FileCount, MeasurementPrecision.Estimated));
        }
        void CountException(Exception exception)
        {
            var actual = exception is ScanOpenException wrapped && wrapped.InnerException is not null ? wrapped.InnerException : exception;
            if (actual is UnauthorizedAccessException) { inaccessible++; AddIssue(ScanIssueKind.AccessDenied, "scan.access-denied", "An entry was inaccessible.", ScanIssueSeverity.PermissionLimited); }
            else if (actual is FileNotFoundException or DirectoryNotFoundException) { changed++; AddIssue(ScanIssueKind.EntryChanged, "scan.entry-changed", "An entry changed during the scan.", ScanIssueSeverity.DataChanged); }
            else { skipped++; AddIssue(ScanIssueKind.Unexpected, "scan.enumeration-failed", "An entry could not be enumerated.", ScanIssueSeverity.AccessWarning); }
        }
        void AddIssue(ScanIssueKind kind, string code, string detail, ScanIssueSeverity severity)
        {
            if (issues.TryGetValue(kind, out var issue)) { issue.Count++; return; }
            if (issues.Count < MaximumIssues) issues[kind] = new(kind, code, detail, severity);
        }
        long WarningCount() => issues.Values.Where(x => x.Severity is ScanIssueSeverity.AccessWarning or ScanIssueSeverity.PermissionLimited or ScanIssueSeverity.DataChanged or ScanIssueSeverity.Fatal).Sum(x => x.Count);
        ScanStatus DetermineStatus() => issues.Count == 0 || issues.Values.All(x => x.Severity is ScanIssueSeverity.Information or ScanIssueSeverity.PolicyBoundary)
            ? ScanStatus.Completed : ScanStatus.CompletedWithWarnings;
        ScanResult Finish(ScanStatus status, string? failureCode = null, string? failureMessage = null)
        {
            var ended = clock.UtcNow;
            var observed = Math.Max(0, bytes);
            // Never silently clamped to zero (Phase 7.2.5): observed logical bytes can legitimately exceed the
            // drive's reported used-bytes basis (hard links, sparse files, or the two figures being measured at
            // slightly different instants). A negative value here is a real, meaningful signal — surfaced via
            // AccountingConsistency.LogicalExceedsDriveUsed below — not an error to be hidden by flooring it.
            long? unaccounted = drive.UsedBytes.HasValue ? drive.UsedBytes.Value - observed : null;
            var coverage = new ScanCoverage(files, directories, inaccessible, reparse, cloud, changed, skipped, false, false, false);
            var classified = classification?.Complete(coverage, unaccounted);
            // A checkpoint is only worth keeping when a policy boundary actually left work pending (saved just
            // above, in RunQuick). Genuine exhaustion (Stage D) and any Deep Analysis run both mean there is
            // nothing meaningful left for "Continue Quick Analysis" to resume, so the checkpoint is cleared.
            if (request.Mode == ScanMode.Quick && !policyBoundaryHit) checkpoints?.Clear(root, ScanMode.Quick);
            if (request.Mode == ScanMode.Deep) checkpoints?.Clear(root, ScanMode.Quick);

            var allocationConsistency = AccountingConsistency.Consistent;
            if (filesWithUnavailableAllocatedSize > 0) allocationConsistency |= AccountingConsistency.AllocatedDataIncomplete;
            if (visibleHardLinkEntries > 0) allocationConsistency |= AccountingConsistency.HardLinkAdjusted;
            if (changed > 0) allocationConsistency |= AccountingConsistency.ChangedDuringScan;
            if (unaccounted is < 0) allocationConsistency |= AccountingConsistency.LogicalExceedsDriveUsed;
            var allocation = new AllocationAccounting(allocatedBytes, uniqueAllocatedBytes, filesWithUnavailableAllocatedSize,
                sparseFiles, compressedFiles, visibleHardLinkEntries, seenFileIdentities.Count, allocationConsistency);

            var result = new ScanResult(Guid.NewGuid(), status, request.Mode, root, drive.FileSystem, started, ended, observed,
                drive.UsedBytes, unaccounted, MeasurementPrecision.Estimated,
                "Logical metadata bytes (namespace size); see Allocation for real on-disk consumption. Hard-linked " +
                "content is de-duplicated in unique allocated bytes but still counted once per visible path in logical bytes.",
                coverage,
                topLevel.Items, topDirectories.Items, topFiles.Items,
                extensions.Where(x => x.Value.Count > 0).OrderByDescending(x => x.Value.Bytes)
                    .Select(x => new ExtensionSummary(x.Key, x.Value.Bytes, x.Value.Count)).ToArray(),
                issues.Values.Select(x => new ScanIssueSummary(x.Kind, x.Code, x.Count, x.Detail, x.Severity)).ToArray(), failureCode, failureMessage,
                classified, allocation);
            progress?.Report(new(status, ended - started, files, directories, bytes, inaccessible + reparse + changed + skipped,
                RedactPath(root), TerminalMessage(status), inaccessible, reparse, WarningCount()));
            return result;
        }
    }

    private ScanResult? cancelledResult;

    private static long PriorityBoostFor(string name)
    {
        if (KnownHighValueRootNames.Contains(name, StringComparer.OrdinalIgnoreCase)) return 3;
        if (KnownHighValueUserSubNames.Contains(name, StringComparer.OrdinalIgnoreCase)) return 2;
        if (KnownDeveloperCacheNames.Contains(name, StringComparer.OrdinalIgnoreCase)) return 1;
        return 0;
    }
    private ScanResult Failed(ScanRequest request, string code, string message)
    {
        var now = clock.UtcNow;
        return new(Guid.NewGuid(), ScanStatus.Failed, request.Mode, request.Root, string.Empty, now, now, 0, null, null,
            MeasurementPrecision.Unavailable, "No accounting was performed.", new(0, 0, 0, 0, 0, 0, 0, false, false, false),
            Array.Empty<RankedPath>(), Array.Empty<RankedPath>(), Array.Empty<RankedPath>(), Array.Empty<ExtensionSummary>(),
            Array.Empty<ScanIssueSummary>(), code, message);
    }

    private static bool IsExpected(Exception exception) => exception is UnauthorizedAccessException or IOException or ScanOpenException;
    private static string RedactPath(string path) => path.Length <= 3 ? path : path[..3] + "<redacted>";
    private static string TerminalMessage(ScanStatus status) => status switch
    { ScanStatus.Completed => "Scan completed.", ScanStatus.CompletedWithWarnings => "Scan completed with coverage warnings.", ScanStatus.Cancelled => "Scan cancelled; partial observations retained.", _ => "Scan failed." };

    private sealed class DirectoryFrame(string path, int depth, DirectoryFrame? parent, IEnumerator<FileSystemEntry> enumerator)
    { public string Path { get; } = path; public int Depth { get; } = depth; public DirectoryFrame? Parent { get; } = parent; public IEnumerator<FileSystemEntry> Enumerator { get; } = enumerator; public long DirectBytes { get; set; } public long FileCount { get; set; } }
    private readonly record struct PendingDirectory(string Path, int Depth, DirectoryFrame? Parent, long Boost, long Order);
    private sealed class MutableIssue(ScanIssueKind kind, string code, string detail, ScanIssueSeverity severity)
    { public ScanIssueKind Kind { get; } = kind; public string Code { get; } = code; public string Detail { get; } = detail; public ScanIssueSeverity Severity { get; } = severity; public long Count { get; set; } = 1; }
    private sealed class ExtensionCounter { public long Bytes { get; set; } public long Count { get; set; } }
    private sealed class ScanOpenException(Exception inner) : IOException("Directory enumeration could not start.", inner);
}

public interface IScanCheckpointStore
{
    ScanCheckpoint? TryLoad(string root, ScanMode mode);
    void Save(ScanCheckpoint checkpoint);
    void Clear(string root, ScanMode mode);
}

/// <summary>Quick Analysis's documented, typed budget. Replaces the previous unconditional 8-second cutoff:
/// the target duration is a soft, displayed budget (not a promised exact completion time), configurable per
/// caller, and versioned so a checkpoint saved under an older policy is safely rejected on load rather than
/// silently reused.</summary>
public sealed record QuickAnalysisPolicy(TimeSpan TargetDuration, long ItemBudget, int PolicyVersion)
{
    public static readonly QuickAnalysisPolicy Default = new(TimeSpan.FromSeconds(30), 250_000, PolicyVersion: 2);
}

/// <summary>
/// The exact, documented bounds each mode runs under. Quick Analysis is deliberately bounded on three
/// independent axes — depth, elapsed time, and item count — so a "fast first look" stays fast even against a
/// pathologically large or deep subtree; hitting any one of them ends the scan honestly, tagged with
/// <see cref="ScanIssueSeverity.PolicyBoundary"/>, never a silent truncation and never reported as
/// <see cref="ScanStatus.CompletedWithWarnings"/> on its own (see <c>ScanCoordinator.DetermineStatus</c>). Deep
/// Analysis has no depth, time, or item bound: it runs until every safely accessible entry has been processed,
/// the caller cancels, or a fatal error occurs.
/// </summary>
internal sealed record ScanPolicy(int MaximumDepth, int TopCount, int TopLevelCount, TimeSpan? TimeBudget, long? ItemBudget)
{
    /// <summary>Quick Analysis depth bound: root-level entries plus two further levels — enough to reach the
    /// top-level contents of well-known high-value roots (Windows, ProgramData, Program Files, Users) and one
    /// level into each, without an unbounded recursive walk.</summary>
    private const int QuickMaximumDepth = 3;

    public static ScanPolicy For(ScanRequest request, QuickAnalysisPolicy? quickPolicy = null)
    {
        var defaultTop = request.Mode == ScanMode.Quick ? 25 : 100;
        var top = Math.Clamp(request.TopCount ?? defaultTop, 1, 1000);
        // Deep Analysis has no configured depth ceiling at all — int.MaxValue, not a large-but-finite number —
        // per Phase 7.2.1. It stops only on cancellation, exhaustion, or a fatal error.
        if (request.Mode != ScanMode.Quick) return new(int.MaxValue, top, 1000, null, null);
        var effective = quickPolicy ?? QuickAnalysisPolicy.Default;
        return new(QuickMaximumDepth, top, 256, effective.TargetDuration, effective.ItemBudget);
    }
}

internal sealed class BoundedRanking(int capacity)
{
    private readonly List<RankedPath> items = new(capacity);
    public IReadOnlyList<RankedPath> Items => items.OrderByDescending(x => x.LogicalBytes).ThenBy(x => x.DisplayPath, StringComparer.OrdinalIgnoreCase).ToArray();
    public void Add(RankedPath item)
    {
        if (items.Count < capacity) { items.Add(item); return; }
        var minimum = items.Select((value, index) => (value, index)).OrderBy(x => x.value.LogicalBytes).ThenByDescending(x => x.value.DisplayPath, StringComparer.OrdinalIgnoreCase).First();
        if (item.LogicalBytes > minimum.value.LogicalBytes || item.LogicalBytes == minimum.value.LogicalBytes && string.Compare(item.DisplayPath, minimum.value.DisplayPath, StringComparison.OrdinalIgnoreCase) < 0)
            items[minimum.index] = item;
    }
}

public static class ExtensionClassifier
{
    public static ExtensionFamily Classify(string extension) => extension.ToLowerInvariant() switch
    {
        "" => ExtensionFamily.NoExtension,
        ".doc" or ".docx" or ".pdf" or ".txt" or ".rtf" or ".xls" or ".xlsx" or ".ppt" or ".pptx" => ExtensionFamily.Documents,
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".heic" or ".svg" => ExtensionFamily.Images,
        ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".webm" => ExtensionFamily.Video,
        ".mp3" or ".wav" or ".flac" or ".aac" or ".m4a" or ".ogg" => ExtensionFamily.Audio,
        ".zip" or ".7z" or ".rar" or ".tar" or ".gz" or ".iso" => ExtensionFamily.Archives,
        ".exe" or ".dll" or ".msi" or ".appx" or ".msix" => ExtensionFamily.Executables,
        ".cs" or ".cpp" or ".h" or ".js" or ".ts" or ".py" or ".java" or ".rs" or ".go" or ".xaml" => ExtensionFamily.SourceCode,
        ".db" or ".sqlite" or ".json" or ".xml" or ".yaml" or ".yml" or ".csv" => ExtensionFamily.Data,
        _ => ExtensionFamily.Other
    };
}
