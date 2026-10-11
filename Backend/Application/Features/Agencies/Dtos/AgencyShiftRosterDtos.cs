using CommonService.Application.Common.Models;

namespace CommonService.Application.Features.Agencies.Dtos;

/// <summary>
/// DTO for creating a new agency shift roster entry
/// </summary>
public sealed record CreateShiftRosterAgencyRequest(
    int WorkerId,
    DateOnly SlotDate,
    string ShiftCode,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string? Note = null
);

/// <summary>
/// DTO for updating an existing agency shift roster entry
/// </summary>
public sealed record UpdateShiftRosterAgencyRequest(
    DateOnly SlotDate,
    string ShiftCode,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string? Note = null
);

/// <summary>
/// DTO for agency shift roster entry response
/// </summary>
public sealed record ShiftRosterAgencyResponse(
    long RosterId,
    int AgencyId,
    int WorkerId,
    string WorkerFullName,
    string WorkerPhoneNumber,
    DateOnly SlotDate,
    string ShiftCode,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string? Note,
    DateTime CreatedAt,
    DateTime UpdatedAt
);
