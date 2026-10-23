using CommonService.Application.Features.Agencies.Dtos;
using CommonService.Domain.Entities;

namespace CommonService.Application.Features.Agencies;

/// <summary>Persistence port of the Agencies module (registration part; later M5 tickets extend it).</summary>
public interface IAgencyRepository
{
    /// <summary>True when an agency with this exact tax code exists.</summary>
    Task<bool> TaxCodeExistsAsync(string taxCode, CancellationToken cancellationToken = default);

    /// <summary>True when an agency with this contact email exists (the caller passes the lower-cased email).</summary>
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts the agency and saves. When the unique index on <c>tax_code</c> is violated by a concurrent registration,
    /// throws <see cref="CommonService.Application.Exceptions.BusinessRuleViolationException"/> with code
    /// <see cref="AgencyErrorCodes.TaxCodeExists"/> (HTTP 409), never a 500.
    /// </summary>
    Task AddAsync(PartnerAgency agency, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets paged list of agency workers with their status and count of future assigned jobs.
    /// </summary>
    Task<(IReadOnlyList<AgencyWorkerItem> Items, int TotalCount)> GetAgencyWorkersAsync(
        int agencyId, int page, int pageSize, string? statusFilter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a worker by ID if it belongs to the agency, otherwise null.
    /// </summary>
    Task<Worker?> GetAgencyWorkerAsync(int agencyId, int workerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of future ASSIGNED jobs for a worker.
    /// </summary>
    Task<int> GetFutureAssignedJobCountAsync(int workerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the list of future ASSIGNED jobs for a worker (for handover).
    /// </summary>
    Task<IReadOnlyList<FutureJobHandover>> GetFutureAssignedJobsAsync(int workerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new agency staff worker (same validation as import).
    /// </summary>
    Task<Worker> CreateAgencyStaffAsync(int agencyId, NewWorkerDetails details, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a phone number or national ID already exists in the worker table.
    /// </summary>
    Task<bool> WorkerPhoneOrNationalIdExistsAsync(string phoneNumber, string nationalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates worker slots' worker_id from old worker to new worker for future assigned/held jobs.
    /// Returns the number of slots updated.
    /// </summary>
    Task<int> UpdateWorkerSlotsAsync(int oldWorkerId, int newWorkerId, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates assignments' worker_id from old worker to new worker for future ASSIGNED jobs.
    /// Returns the number of assignments updated.
    /// </summary>
    Task<int> UpdateWorkerAssignmentsAsync(int oldWorkerId, int newWorkerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets workers with Retiring status that have no active or future jobs (for sweep).
    /// </summary>
    Task<IReadOnlyList<Worker>> GetRetiringWorkersReadyForRetiredAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a worker has any active (CheckedIn, InProgress, AwaitingAcceptance) or future ASSIGNED jobs.
    /// </summary>
    Task<bool> HasActiveOrFutureJobsAsync(int workerId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Information about a future job that was handed over.
/// </summary>
public sealed record FutureJobHandover(
    long AssignmentId,
    DateOnly SlotDate,
    string ShiftCode
);