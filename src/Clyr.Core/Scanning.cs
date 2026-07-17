using Clyr.Contracts;

namespace Clyr.Core;

[Flags]
public enum EntryTraits { None = 0, Directory = 1, ReparsePoint = 2, CloudPlaceholder = 4 }
public sealed record FileSystemEntry(string FullPath, long LogicalBytes, EntryTraits Traits);
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
    IClassificationProvider? classificationProvider = null) : IScanService
{
    private const int MaximumIssues = 32;
    private int active;

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

        var started = clock.UtcNow;
        var classification = classificationProvider?.Start(request with { Root = root }, drive);
        var policy = ScanPolicy.For(request);
        var topFiles = new BoundedRanking(policy.TopCount);
        var topDirectories = new BoundedRanking(policy.TopCount);
        var topLevel = new BoundedRanking(policy.TopLevelCount);
        var extensions = Enum.GetValues<ExtensionFamily>().ToDictionary(item => item, _ => new ExtensionCounter());
        var issues = new Dictionary<ScanIssueKind, MutableIssue>();
        var stack = new Stack<DirectoryFrame>();
        long files = 0, directories = 0, bytes = 0, inaccessible = 0, reparse = 0, cloud = 0, changed = 0, skipped = 0;
        var lastProgress = DateTimeOffset.MinValue;

        progress?.Report(new(ScanStatus.Preparing, TimeSpan.Zero, 0, 0, 0, 0, RedactPath(root), "Preparing metadata-only scan."));
        try
        {
            stack.Push(Open(root, 0));
            while (stack.Count > 0)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    progress?.Report(new(ScanStatus.Cancelling, clock.UtcNow - started, files, directories, bytes,
                        inaccessible + reparse + changed + skipped, RedactPath(stack.Peek().Path), "Cancellation acknowledged.",
                        inaccessible, reparse, WarningCount()));
                    return Finish(ScanStatus.Cancelled);
                }

                // Quick Analysis carries an explicit, documented time and item budget in addition to its depth
                // limit (see ScanPolicy.For) so a first look never runs unboundedly long even against a huge,
                // deeply nested tree. Deep Analysis has neither bound and runs until every safely accessible
                // entry has been processed, the caller cancels, or a fatal error occurs.
                if (policy.TimeBudget is { } timeBudget && clock.UtcNow - started >= timeBudget)
                {
                    AddIssue(ScanIssueKind.ResourceLimit, "scan.quick-time-budget", "Quick Analysis time budget reached; remaining areas were not examined.");
                    return Finish(ScanStatus.CompletedWithWarnings);
                }
                if (policy.ItemBudget is { } itemBudget && files + directories >= itemBudget)
                {
                    AddIssue(ScanIssueKind.ResourceLimit, "scan.quick-item-budget", "Quick Analysis item budget reached; remaining areas were not examined.");
                    return Finish(ScanStatus.CompletedWithWarnings);
                }

                var frame = stack.Peek();
                FileSystemEntry? entry;
                try
                {
                    if (!frame.Enumerator.MoveNext()) { CompleteDirectory(); continue; }
                    entry = frame.Enumerator.Current;
                }
                catch (Exception exception) when (IsExpected(exception))
                { CountException(exception); CompleteDirectory(); continue; }

                if (entry is null) continue;
                classification?.Observe(entry);
                if ((entry.Traits & EntryTraits.ReparsePoint) != 0)
                { reparse++; AddIssue(ScanIssueKind.ReparseSkipped, "scan.reparse-skipped", "A reparse point was not traversed."); continue; }
                if ((entry.Traits & EntryTraits.Directory) != 0)
                {
                    directories++;
                    if (frame.Depth < policy.MaximumDepth)
                    {
                        try { stack.Push(Open(entry.FullPath, frame.Depth + 1)); }
                        catch (Exception exception) when (IsExpected(exception)) { CountException(exception); }
                    }
                    else { skipped++; AddIssue(ScanIssueKind.ResourceLimit, "scan.depth-limit", "Quick Analysis depth limit reached."); }
                }
                else
                {
                    files++;
                    var size = Math.Max(0, entry.LogicalBytes);
                    frame.DirectBytes += size; frame.FileCount++; bytes += size;
                    if ((entry.Traits & EntryTraits.CloudPlaceholder) != 0)
                    { cloud++; AddIssue(ScanIssueKind.CloudPlaceholder, "scan.cloud-metadata-only", "Cloud placeholder counted from metadata without hydration."); }
                    topFiles.Add(new(entry.FullPath, size, 1, MeasurementPrecision.Estimated));
                    var family = ExtensionClassifier.Classify(Path.GetExtension(entry.FullPath));
                    extensions[family].Bytes += size; extensions[family].Count++;
                }

                var now = clock.UtcNow;
                if (now - lastProgress >= TimeSpan.FromMilliseconds(250))
                {
                    lastProgress = now;
                    progress?.Report(new(ScanStatus.Scanning, now - started, files, directories, bytes,
                        inaccessible + reparse + changed + skipped, RedactPath(entry.FullPath), "Observing filesystem metadata.",
                        inaccessible, reparse, WarningCount()));
                }
            }
            return Finish(issues.Count == 0 ? ScanStatus.Completed : ScanStatus.CompletedWithWarnings);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            CountException(exception);
            return Finish(files + directories > 0 ? ScanStatus.CompletedWithWarnings : ScanStatus.Failed,
                files + directories == 0 ? "scan.root-unavailable" : null,
                files + directories == 0 ? "The drive root could not be enumerated." : null);
        }
        finally { while (stack.Count > 0) stack.Pop().Enumerator.Dispose(); }

        DirectoryFrame Open(string path, int depth)
        {
            try { return new(path, depth, fileSystem.Enumerate(path).GetEnumerator()); }
            catch (Exception exception) when (IsExpected(exception)) { throw new ScanOpenException(exception); }
        }
        void CompleteDirectory()
        {
            var completed = stack.Pop(); completed.Enumerator.Dispose();
            var total = completed.DirectBytes;
            if (stack.Count > 0) { stack.Peek().DirectBytes += total; stack.Peek().FileCount += completed.FileCount; }
            if (completed.Depth == 1) topLevel.Add(new(completed.Path, total, completed.FileCount, MeasurementPrecision.Estimated));
            if (completed.Depth > 0) topDirectories.Add(new(completed.Path, total, completed.FileCount, MeasurementPrecision.Estimated));
        }
        void CountException(Exception exception)
        {
            var actual = exception is ScanOpenException wrapped && wrapped.InnerException is not null ? wrapped.InnerException : exception;
            if (actual is UnauthorizedAccessException) { inaccessible++; AddIssue(ScanIssueKind.AccessDenied, "scan.access-denied", "An entry was inaccessible."); }
            else if (actual is FileNotFoundException or DirectoryNotFoundException) { changed++; AddIssue(ScanIssueKind.EntryChanged, "scan.entry-changed", "An entry changed during the scan."); }
            else { skipped++; AddIssue(ScanIssueKind.Unexpected, "scan.enumeration-failed", "An entry could not be enumerated."); }
        }
        void AddIssue(ScanIssueKind kind, string code, string detail)
        {
            if (issues.TryGetValue(kind, out var issue)) { issue.Count++; return; }
            if (issues.Count < MaximumIssues) issues[kind] = new(kind, code, detail);
        }
        long WarningCount() => issues.Values.Sum(x => x.Count);
        ScanResult Finish(ScanStatus status, string? failureCode = null, string? failureMessage = null)
        {
            var ended = clock.UtcNow;
            var observed = Math.Max(0, bytes);
            long? unaccounted = drive.UsedBytes.HasValue ? Math.Max(0, drive.UsedBytes.Value - observed) : null;
            var coverage = new ScanCoverage(files, directories, inaccessible, reparse, cloud, changed, skipped, false, false, false);
            var classified = classification?.Complete(coverage, unaccounted);
            var result = new ScanResult(Guid.NewGuid(), status, request.Mode, root, drive.FileSystem, started, ended, observed,
                drive.UsedBytes, unaccounted, MeasurementPrecision.Estimated,
                "Logical metadata bytes; hard-linked content may be counted more than once. Allocated size is not measured in Phase 2.",
                coverage,
                topLevel.Items, topDirectories.Items, topFiles.Items,
                extensions.Where(x => x.Value.Count > 0).OrderByDescending(x => x.Value.Bytes)
                    .Select(x => new ExtensionSummary(x.Key, x.Value.Bytes, x.Value.Count)).ToArray(),
                issues.Values.Select(x => new ScanIssueSummary(x.Kind, x.Code, x.Count, x.Detail)).ToArray(), failureCode, failureMessage,
                classified);
            progress?.Report(new(status, ended - started, files, directories, bytes, inaccessible + reparse + changed + skipped,
                RedactPath(root), TerminalMessage(status), inaccessible, reparse, WarningCount()));
            return result;
        }
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

    private sealed class DirectoryFrame(string path, int depth, IEnumerator<FileSystemEntry> enumerator)
    { public string Path { get; } = path; public int Depth { get; } = depth; public IEnumerator<FileSystemEntry> Enumerator { get; } = enumerator; public long DirectBytes { get; set; } public long FileCount { get; set; } }
    private sealed class MutableIssue(ScanIssueKind kind, string code, string detail)
    { public ScanIssueKind Kind { get; } = kind; public string Code { get; } = code; public string Detail { get; } = detail; public long Count { get; set; } = 1; }
    private sealed class ExtensionCounter { public long Bytes { get; set; } public long Count { get; set; } }
    private sealed class ScanOpenException(Exception inner) : IOException("Directory enumeration could not start.", inner);
}

/// <summary>
/// The exact, documented bounds each mode runs under. Quick Analysis is deliberately bounded on three
/// independent axes — depth, elapsed time, and item count — so a "fast first look" stays fast even against a
/// pathologically large or deep subtree; hitting any one of them ends the scan honestly as
/// <see cref="ScanStatus.CompletedWithWarnings"/> with a diagnostic identifying which bound was hit, never a
/// silent truncation. Deep Analysis has no depth, time, or item bound: it runs until every safely accessible
/// entry has been processed, the caller cancels, or a fatal error occurs.
/// </summary>
internal sealed record ScanPolicy(int MaximumDepth, int TopCount, int TopLevelCount, TimeSpan? TimeBudget, long? ItemBudget)
{
    /// <summary>Quick Analysis depth bound: root-level entries plus two further levels — enough to reach the
    /// top-level contents of well-known high-value roots (Windows, ProgramData, Program Files, Users) and one
    /// level into each, without an unbounded recursive walk.</summary>
    private const int QuickMaximumDepth = 3;
    /// <summary>Quick Analysis stops examining new entries after this much wall-clock time has elapsed, even if
    /// the traversal is not otherwise complete.</summary>
    private static readonly TimeSpan QuickTimeBudget = TimeSpan.FromSeconds(8);
    /// <summary>Quick Analysis stops after this many files-plus-directories have been examined.</summary>
    private const long QuickItemBudget = 250_000;

    public static ScanPolicy For(ScanRequest request)
    {
        var defaultTop = request.Mode == ScanMode.Quick ? 25 : 100;
        var top = Math.Clamp(request.TopCount ?? defaultTop, 1, 1000);
        return request.Mode == ScanMode.Quick
            ? new(QuickMaximumDepth, top, 256, QuickTimeBudget, QuickItemBudget)
            : new(512, top, 1000, null, null);
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
