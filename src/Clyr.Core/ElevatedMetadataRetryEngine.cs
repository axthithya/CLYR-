using System.Collections.Immutable;
using Clyr.Contracts;

namespace Clyr.Core;

/// <summary>
/// The pure, in-process read-only metadata retry engine for
/// <see cref="ElevatedScanOperation.RetryPermissionLimitedRoots"/>. Given an already-typed request and a trusted
/// <see cref="IFileSystemEnumerator"/> (the same read-only abstraction the ordinary scanner uses — see
/// <see cref="Scanning.cs"/> — reused unmodified rather than broadened with any mutation capability), this
/// independently re-validates the request with <see cref="ElevatedScanRetryValidator"/>, enumerates only the
/// roots the validator accepted, and produces a bounded <see cref="ElevatedScanRetryResponse"/>. This class has
/// no knowledge of named pipes, processes, UAC, or UI — <c>ElevatedScanIpcTransport</c> (Phase 7.2.6B) is not
/// wired to it yet; that connection is a later phase's work.
/// </summary>
public sealed class ElevatedMetadataRetryEngine(IFileSystemEnumerator fileSystem, IClock clock)
{
    public Task<ElevatedScanRetryResponse> RetryAsync(ElevatedScanRetryRequest request, CancellationToken cancellationToken)
    {
        var startedAtUtc = clock.UtcNow;
        var validation = ElevatedScanRetryValidator.Validate(request, startedAtUtc);
        if (!validation.IsValid)
        {
            // No provider method is ever called for an invalid request — validation happens entirely before
            // this branch, and enumeration never starts.
            var rejected = new Accumulator(DiagnosticCap(request));
            rejected.RecordDiagnostic($"validation.{validation.Outcome}", ScanIssueSeverity.PolicyBoundary, "request");
            return Task.FromResult(Terminal(request, ElevatedScanRetryOutcome.ValidationRejected, startedAtUtc, clock.UtcNow, rejected, 0, 0, []));
        }
        // Enumeration is blocking, read-only filesystem I/O — offloaded the same way ScanCoordinator offloads
        // its own traversal, so the caller's async context is never blocked on disk access.
        return Task.Run(() => Retry(request, startedAtUtc, cancellationToken), CancellationToken.None);
    }

    private ElevatedScanRetryResponse Retry(ElevatedScanRetryRequest request, DateTimeOffset startedAtUtc, CancellationToken cancellationToken)
    {
        var accumulator = new Accumulator(DiagnosticCap(request));
        var rootResults = ImmutableArray.CreateBuilder<ElevatedRootRetryResult>(request.PermissionLimitedRoots.Length);
        var rootsCompleted = 0;
        var rootsStillInaccessible = 0;
        var cancelled = false;
        try
        {
            // Deterministic order: exactly the request's own root order, never re-sorted or re-grouped.
            foreach (var root in request.PermissionLimitedRoots)
            {
                if (cancellationToken.IsCancellationRequested) { cancelled = true; break; }
                var (completed, rootAccumulator) = EnumerateRoot(root, accumulator, cancellationToken, out var cancelledDuringRoot);
                if (cancelledDuringRoot)
                {
                    rootResults.Add(BuildRootResult(root, ElevatedRootRetryOutcome.Cancelled, rootAccumulator));
                    cancelled = true;
                    break;
                }
                rootResults.Add(BuildRootResult(root, completed ? ElevatedRootRetryOutcome.Completed : ElevatedRootRetryOutcome.StillInaccessible, rootAccumulator));
                if (completed) rootsCompleted++; else rootsStillInaccessible++;
            }
        }
        catch (Exception exception) when (!IsExpected(exception))
        {
            // An unexpected (not access-denied / not an ordinary I/O condition) provider failure never escapes
            // as an unhandled exception and never carries the exception's own message or stack trace forward —
            // only a fixed, bounded diagnostic code does. There is no retry loop here; one fatal failure ends
            // the whole retry attempt as ElevatedScanRetryOutcome.Failed.
            accumulator.RecordDiagnostic("engine.unexpected-provider-failure", ScanIssueSeverity.Fatal, "engine");
            return Terminal(request, ElevatedScanRetryOutcome.Failed, startedAtUtc, clock.UtcNow, accumulator, rootsCompleted, rootsStillInaccessible, rootResults.ToImmutable());
        }

        var outcome = cancelled ? ElevatedScanRetryOutcome.Cancelled
            : rootsStillInaccessible == 0 ? ElevatedScanRetryOutcome.Completed
            : ElevatedScanRetryOutcome.PartiallyCompleted;
        return Terminal(request, outcome, startedAtUtc, clock.UtcNow, accumulator, rootsCompleted, rootsStillInaccessible, rootResults.ToImmutable());
    }

    /// <summary>Enumerates exactly one already-validated root, iteratively (a plain stack of enumerators — no
    /// recursion, so this is stack-safe regardless of directory depth) and without retaining any per-file
    /// inventory (only the shared and per-root <see cref="Accumulator"/>'s running totals grow). Returns
    /// <see langword="true"/> only if the root's own subtree was walked to exhaustion without cancellation; a
    /// root that could not even be opened, or whose enumeration hit an ordinary access/IO condition partway
    /// through, still returns <see langword="false"/> (recorded as "still inaccessible") rather than throwing —
    /// this is what lets the caller continue on to the next root. Every observed entry is fed to both
    /// <paramref name="sharedAccumulator"/> (the whole-attempt aggregate) and a fresh, root-scoped accumulator
    /// returned alongside the result — each keeps its own independent hard-link identity set, so
    /// "unique within this root" never depends on what any other root happened to contain.</summary>
    private (bool Completed, Accumulator RootAccumulator) EnumerateRoot(PermissionLimitedRoot root, Accumulator sharedAccumulator,
        CancellationToken cancellationToken, out bool cancelledDuringRoot)
    {
        cancelledDuringRoot = false;
        var label = SafeRootLabel(root);
        var rootAccumulator = new Accumulator(int.MaxValue); // per-root diagnostics are not surfaced individually; only its counters are read.
        IEnumerator<FileSystemEntry> Open(string path) => fileSystem.Enumerate(path).GetEnumerator();

        IEnumerator<FileSystemEntry> rootEnumerator;
        try { rootEnumerator = Open(root.NormalizedRootPath); }
        catch (Exception exception) when (IsExpected(exception))
        {
            sharedAccumulator.RecordDiagnostic(ReasonCodeFor(exception), SeverityFor(exception), label);
            return (false, rootAccumulator);
        }

        var stack = new Stack<IEnumerator<FileSystemEntry>>();
        stack.Push(rootEnumerator);
        var rootFullyOpened = true;
        try
        {
            while (stack.Count > 0)
            {
                if (cancellationToken.IsCancellationRequested) { cancelledDuringRoot = true; return (false, rootAccumulator); }

                FileSystemEntry? entry;
                try
                {
                    if (!stack.Peek().MoveNext()) { stack.Pop().Dispose(); continue; }
                    entry = stack.Peek().Current;
                }
                catch (Exception exception) when (IsExpected(exception))
                {
                    sharedAccumulator.RecordDiagnostic(ReasonCodeFor(exception), SeverityFor(exception), label);
                    rootFullyOpened = false;
                    stack.Pop().Dispose();
                    continue;
                }
                if (entry is null) continue;

                // Reparse points (junctions, symlinks, mount points) are recorded and skipped — never passed to
                // Open — which is both the "never follow a reparse-point target" rule and the loop-prevention
                // rule: a self-referencing directory structure exposed only through a reparse point can never be
                // descended into in the first place.
                if ((entry.Traits & EntryTraits.ReparsePoint) != 0)
                {
                    sharedAccumulator.RecordDiagnostic("root.reparse-point-skipped", ScanIssueSeverity.Information, label);
                    continue;
                }
                if ((entry.Traits & EntryTraits.Directory) != 0)
                {
                    sharedAccumulator.DirectoriesExamined++;
                    rootAccumulator.DirectoriesExamined++;
                    try { stack.Push(Open(entry.FullPath)); }
                    catch (Exception exception) when (IsExpected(exception))
                    {
                        sharedAccumulator.RecordDiagnostic(ReasonCodeFor(exception), SeverityFor(exception), label);
                        rootFullyOpened = false;
                    }
                    continue;
                }
                sharedAccumulator.RecordFile(entry);
                rootAccumulator.RecordFile(entry);
            }
        }
        finally { while (stack.Count > 0) stack.Pop().Dispose(); }
        return (rootFullyOpened, rootAccumulator);
    }

    private static ElevatedRootRetryResult BuildRootResult(PermissionLimitedRoot root, ElevatedRootRetryOutcome outcome, Accumulator rootAccumulator) =>
        new(ElevatedScanManifestBuilder.NormalizePath(root.NormalizedRootPath), root.StableRootIdentifier, outcome,
            rootAccumulator.FilesExamined, rootAccumulator.DirectoriesExamined, rootAccumulator.LogicalBytesObserved,
            rootAccumulator.AllocatedBytesObserved, rootAccumulator.UniqueAllocatedBytesObserved, rootAccumulator.HardLinkEntriesDetected,
            rootAccumulator.AllocationUnavailableCount, rootAccumulator.SparseFileCount, rootAccumulator.CompressedFileCount);

    private static int DiagnosticCap(ElevatedScanRetryRequest request) =>
        Math.Clamp(request.MaximumDiagnosticCount, 1, ElevatedScanRetryProtocol.MaxDiagnosticCount);

    private static ElevatedScanRetryResponse Terminal(ElevatedScanRetryRequest request, ElevatedScanRetryOutcome outcome,
        DateTimeOffset startedAtUtc, DateTimeOffset completedAtUtc, Accumulator accumulator, int rootsCompleted, int rootsStillInaccessible,
        ImmutableArray<ElevatedRootRetryResult> rootResults) =>
        new(request.ProtocolVersion, request.Nonce, outcome, startedAtUtc, completedAtUtc,
            request.PermissionLimitedRoots.Length, rootsCompleted, rootsStillInaccessible,
            accumulator.FilesExamined, accumulator.DirectoriesExamined, accumulator.LogicalBytesObserved,
            accumulator.AllocatedBytesObserved, accumulator.UniqueAllocatedBytesObserved, accumulator.HardLinkEntriesDetected,
            accumulator.SparseFileCount, accumulator.CompressedFileCount, accumulator.FormatDiagnostics(), rootResults);

    private static bool IsExpected(Exception exception) => exception is UnauthorizedAccessException or IOException;

    private static string ReasonCodeFor(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "root.access-denied",
        FileNotFoundException or DirectoryNotFoundException => "root.entry-changed",
        _ => "root.enumeration-failed"
    };

    private static ScanIssueSeverity SeverityFor(Exception exception) => exception switch
    {
        UnauthorizedAccessException => ScanIssueSeverity.PermissionLimited,
        FileNotFoundException or DirectoryNotFoundException => ScanIssueSeverity.DataChanged,
        _ => ScanIssueSeverity.AccessWarning
    };

    /// <summary>The trusted, already-validated stable identifier when the originating scan supplied one;
    /// otherwise a redacted path prefix — never the full path — so a diagnostic can never leak a complete,
    /// privacy-sensitive filesystem path.</summary>
    private static string SafeRootLabel(PermissionLimitedRoot root) =>
        root.StableRootIdentifier ?? (root.NormalizedRootPath.Length <= 3 ? root.NormalizedRootPath : root.NormalizedRootPath[..3] + "<redacted>");

    /// <summary>
    /// Running, memory-bounded totals for one retry attempt. Deliberately holds only aggregate counters and one
    /// small identity set (bounded by the number of distinct files actually observed this attempt) plus a
    /// diagnostics dictionary capped at the request's own <see cref="ElevatedScanRetryRequest.MaximumDiagnosticCount"/>
    /// — never a per-file inventory, and never an unbounded diagnostics collection. Hard-link accounting mirrors
    /// <c>ScanCoordinator.ObserveAllocation</c> exactly: a file whose stable identity could not be read is still
    /// counted (conservatively, as its own unique allocation) rather than silently dropped or guessed away.
    /// </summary>
    private sealed class Accumulator(int maximumDiagnosticCount)
    {
        private readonly HashSet<ulong> seenFileIdentities = [];
        private readonly Dictionary<(string Code, ScanIssueSeverity Severity, string Root), long> diagnostics = [];

        public long FilesExamined { get; private set; }
        public long DirectoriesExamined { get; set; }
        public long LogicalBytesObserved { get; private set; }
        public long AllocatedBytesObserved { get; private set; }
        public long UniqueAllocatedBytesObserved { get; private set; }
        public long HardLinkEntriesDetected { get; private set; }
        public long SparseFileCount { get; private set; }
        public long CompressedFileCount { get; private set; }
        public long AllocationUnavailableCount { get; private set; }

        public void RecordFile(FileSystemEntry entry)
        {
            FilesExamined++;
            LogicalBytesObserved += Math.Max(0, entry.LogicalBytes);
            var allocated = entry.AllocatedBytes;
            if (allocated is { } value) AllocatedBytesObserved += Math.Max(0, value);
            if ((entry.Traits & EntryTraits.Sparse) != 0) SparseFileCount++;
            if ((entry.Traits & EntryTraits.Compressed) != 0) CompressedFileCount++;
            if (entry.FileIdentity is { } identity)
            {
                if (seenFileIdentities.Add(identity)) { if (allocated is { } unique) UniqueAllocatedBytesObserved += Math.Max(0, unique); }
                else HardLinkEntriesDetected++;
            }
            else if (allocated is { } fallback) UniqueAllocatedBytesObserved += Math.Max(0, fallback);
            if (allocated is null) { AllocationUnavailableCount++; RecordDiagnostic("allocation.unavailable", ScanIssueSeverity.Information, "multiple"); }
        }

        public void RecordDiagnostic(string code, ScanIssueSeverity severity, string rootLabel)
        {
            var key = (code, severity, rootLabel);
            if (diagnostics.TryGetValue(key, out var count)) { diagnostics[key] = count + 1; return; }
            // Once the distinct-key cap is reached, a genuinely new kind of diagnostic is dropped rather than
            // growing the collection past the caller's declared bound — repeats of an already-recorded key
            // still increment its count above, so no information already captured is ever lost by the cap.
            if (diagnostics.Count < maximumDiagnosticCount) diagnostics[key] = 1;
        }

        public ImmutableArray<string> FormatDiagnostics() =>
            [.. diagnostics.Select(pair => $"{pair.Key.Code} (severity={pair.Key.Severity}, root={pair.Key.Root}, count={pair.Value})")];
    }
}
