namespace CommonService.Application.Features.Workers.Dtos;

/// <summary>
/// One job booked for the signed-in worker (contract workers.md 2.3.3). Only the district of the address is returned: the
/// street address, the coordinates and the customer's name or phone are not part of this read.
/// </summary>
public record WorkerAssignmentResponse(
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

/// <summary>A paid extension ("Làm lần 2") the worker has not answered yet (contract workers.md 2.3.4).</summary>
public record WorkerPendingExtension(int ExtensionId, decimal ExtraHours, decimal ExtraAmount);

/// <summary>
/// The detail of one job the signed-in worker holds (contract workers.md 2.3.4): the full address and coordinates of the order, shown from the moment the
/// worker accepted the job (decision of the owner, #442). The customer's name and phone are not part of it. <c>PendingExtension</c> is null when the
/// customer has no paid extension waiting for this worker.
/// </summary>
public record WorkerAssignmentDetailResponse(
    long AssignmentId,
    long OrderId,
    string OrderCode,
    DateOnly SlotDate,
    string ShiftCode,
    string StartTime,
    string EndTime,
    string Status,
    string ServiceTier,
    string AddressLine,
    string District,
    string City,
    decimal Latitude,
    decimal Longitude,
    decimal PayoutAmount,
    WorkerPendingExtension? PendingExtension
);
