using MediatR;

namespace CommonService.Domain.Events;

/// <summary>
/// Published by Dispatch (M3) after a freelancer cancelled an accepted job (decisions Q15, D35). The seat is already released and the
/// order is back to DISPATCHING (or failed when the re-dispatch cutoff had passed). <paramref name="CancelCount"/> is the worker's
/// cancellations inside <c>Cancel.WorkerCancelWindowDays</c>, this one included.
/// </summary>
public record AssignmentCancelledByWorker(
    long AssignmentId,
    long OrderId,
    int WorkerId,
    byte AssignmentSeq,
    string Reason,
    int CancelCount,
    bool WorkerLocked,
    DateTime CancelledAtUtc) : INotification;
