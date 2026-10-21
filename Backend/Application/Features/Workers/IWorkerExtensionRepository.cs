namespace CommonService.Application.Features.Workers;

/// <summary>The extension ("Làm lần 2") of the order of an assignment, with the shift it extends.</summary>
public sealed record ExtensionForWorker(
    int ExtensionId,
    long OrderId,
    int WorkerId,
    decimal ExtraHours,
    decimal ExtraAmount,
    string ExtStatus,
    DateOnly ScheduledDate,
    string ShiftCode);

/// <summary>What the Workers module needs of JOB_ORDER_EXTENSION to answer a paid extension (contract workers.md 2.6.1, BR-08).</summary>
public interface IWorkerExtensionRepository
{
    /// <summary>
    /// The extension of the order of <paramref name="assignmentId"/>, only when that assignment belongs to <paramref name="workerId"/> and the extension
    /// is asked of the same worker; null otherwise (an unknown assignment, another worker's one and an order without extension look the same).
    /// </summary>
    Task<ExtensionForWorker?> FindForWorkerAsync(long assignmentId, int workerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// ACCEPTED (<c>ext_status</c> and <c>worker_decision</c>, with <c>decided_at</c>) in one conditional update that only applies while the extension is
    /// still PAID. False, with nothing changed, when another answer came first, so two concurrent answers decide once.
    /// </summary>
    Task<bool> TryAcceptAsync(int extensionId, DateTime decidedAtUtc, CancellationToken cancellationToken = default);
}
