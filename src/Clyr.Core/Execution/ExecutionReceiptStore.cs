using Clyr.Contracts;

namespace Clyr.Core.Execution;

public sealed class ExecutionReceiptStoreException(string code, string message, Exception inner) : Exception(message, inner)
{
    public string Code { get; } = code;
}

/// <summary>
/// The durable execution lifecycle every production mutation path must go through — see
/// <see cref="NonElevatedCleanupExecutor"/>, the only production caller today. A row is written once, exactly
/// twice: <see cref="BeginAsync"/> before any mutation may occur, and <see cref="CompleteAsync"/> once the outcome
/// is known. If a process crashes between those two calls, the row a future launch finds is durable proof that an
/// execution began and never confirmed how it ended — see <see cref="ReconcileInterruptedAsync"/>. There is
/// deliberately no third way to write a receipt: nothing here lets a caller insert a terminal row with no prior
/// Started row, which would defeat the entire guarantee.
/// </summary>
public interface IExecutionReceiptStore
{
    /// <summary>Durably records that an execution began, using the exact <see cref="ExecutionReceipt.ExecutionId"/>
    /// its terminal record will later complete. Must be called — and must succeed — before any mutation may
    /// occur; a caller that cannot durably prove an execution began must not attempt it. Fails
    /// (<c>receipt.duplicate-begin</c>) if a row for this <see cref="ExecutionReceipt.ExecutionId"/> already
    /// exists, since a genuinely new execution attempt must always be a fresh row.</summary>
    Task BeginAsync(ExecutionReceipt startRecord, CancellationToken cancellationToken = default);

    /// <summary>Finalizes the exact Started row <paramref name="id"/> identifies with the terminal
    /// <paramref name="finalReceipt"/>. Fails closed (<c>receipt.unknown-execution</c>) if no Started row exists
    /// for this ID — completion can never silently insert a fresh terminal row with no matching start history.
    /// Fails (<c>receipt.completion-mismatch</c>) if the plan/digest/scan/evidence/drive/session/user identity in
    /// <paramref name="finalReceipt"/> does not match what <see cref="BeginAsync"/> recorded — completion can
    /// never silently retarget another plan or digest. Calling this again for an already-terminal row is
    /// idempotent only when <paramref name="finalReceipt"/>'s digest matches what is already stored
    /// (<c>receipt.immutable</c> otherwise — the same code <see cref="BeginAsync"/>'s duplicate-row case used
    /// before this correction, for the same reason: a terminal row is never silently overwritten).</summary>
    Task CompleteAsync(ExecutionId id, ExecutionReceipt finalReceipt, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExecutionReceiptSummary>> ListAsync(int limit = 50, CancellationToken cancellationToken = default);
    Task<ExecutionReceipt?> GetAsync(ExecutionId id, CancellationToken cancellationToken = default);
    Task<bool> DiscardAsync(ExecutionId id, CancellationToken cancellationToken = default);

    /// <summary>Durable replay protection: true when any record (Started or terminal, from this launch or a
    /// prior one) already references <paramref name="planId"/> or <paramref name="planDigest"/>. Checked before
    /// every execution attempt so a restart — which clears every in-memory attempted-plan guard — can never make
    /// the exact same plan silently executable again. A genuinely new plan (a fresh, randomly generated
    /// <see cref="CleanupPlanId"/> and a digest that always differs because the plan ID itself is part of it)
    /// never matches, so this never blocks unrelated future plans.</summary>
    Task<bool> HasRecordForPlanAsync(CleanupPlanId planId, string planDigest, CancellationToken cancellationToken = default);

    /// <summary>Marks any receipt left in an in-flight state past <paramref name="staleAfter"/> as Interrupted.
    /// This never guesses success — an abandoned "Started" row can only ever become Interrupted, never Completed.</summary>
    Task<int> ReconcileInterruptedAsync(TimeSpan staleAfter, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
}
