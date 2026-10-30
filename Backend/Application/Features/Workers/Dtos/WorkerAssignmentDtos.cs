namespace CommonService.Application.Features.Workers.Dtos;

/// <summary>
/// One job booked for the signed-in worker (contract workers.md 2.3.3). Only the district of the address is returned: the
/// street address, the coordinates and the customer's name or phone are not part of this read.
/// </summary>
public sealed record WorkerAssignmentResponse(
    long AssignmentId,
    long OrderId,
    string OrderCode,
    DateOnly SlotDate,
    string ShiftCode,
    string StartTime,
    string EndTime,
    string Status,
    string ServiceTier,
    string District,
    decimal PayoutAmount
);
