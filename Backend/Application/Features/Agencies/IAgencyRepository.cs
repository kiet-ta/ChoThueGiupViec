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

    /// <summary>Read-only copy of a worker of the agency, otherwise null. Changes to it are NOT saved; use <see cref="GetAgencyWorkerForUpdateAsync"/>.</summary>
    Task<Worker?> GetAgencyWorkerAsync(int agencyId, int workerId, CancellationToken cancellationToken = default);

    /// <summary>Tracked worker of the agency (the unit of work saves its changes), otherwise null.</summary>
    Task<Worker?> GetAgencyWorkerForUpdateAsync(int agencyId, int workerId, CancellationToken cancellationToken = default);

    /// <summary>The <c>RETIRING</c> workers of every agency (worker id, agency id) for the sweep.</summary>
    Task<IReadOnlyList<(int WorkerId, int AgencyId)>> ListRetiringWorkersAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets the count of future ASSIGNED jobs for a worker.</summary>
    Task<int> GetFutureAssignedJobCountAsync(int workerId, CancellationToken cancellationToken = default);

    /// <summary>The future <c>ASSIGNED</c> jobs of a worker (with the order and the slot), oldest first.</summary>
    Task<IReadOnlyList<FutureJob>> GetFutureAssignedJobsAsync(int workerId, CancellationToken cancellationToken = default);

    /// <summary>True when the worker holds a job in progress: <c>CHECKED_IN</c>, <c>IN_PROGRESS</c> or <c>AWAITING_ACCEPTANCE</c>.</summary>
    Task<bool> HasActiveJobAsync(int workerId, CancellationToken cancellationToken = default);

    /// <summary>Number of <c>HELD</c> slots of the worker on or after <paramref name="fromDate"/> (a customer is in checkout).</summary>
    Task<int> CountFutureHeldSlotsAsync(int workerId, DateOnly fromDate, CancellationToken cancellationToken = default);

    /// <summary>Deletes the <c>AVAILABLE</c> slots of the worker on or after <paramref name="fromDate"/>; returns how many.</summary>
    Task<int> RemoveFutureAvailableSlotsAsync(int workerId, DateOnly fromDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new agency staff worker (same validation as import) and saves, so its id is known. The caller runs it inside
    /// the unit of work of the retirement, which rolls the worker back with everything else.
    /// </summary>
    Task<Worker> CreateAgencyStaffAsync(int agencyId, NewWorkerDetails details, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Checks if a phone number or national ID already exists in the worker table.</summary>
    Task<bool> WorkerPhoneOrNationalIdExistsAsync(string phoneNumber, string nationalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves to the new worker the slots of the old worker that belong to a future <c>ASSIGNED</c> job (<c>BOOKED</c>) and its
    /// <c>HELD</c> slots on or after <paramref name="fromDate"/>. Run it BEFORE <see cref="UpdateWorkerAssignmentsAsync"/>.
    /// Returns the number of slots moved.
    /// </summary>
    Task<int> UpdateWorkerSlotsAsync(int oldWorkerId, int newWorkerId, DateOnly fromDate, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Moves the future <c>ASSIGNED</c> jobs of the old worker to the new worker. Returns the number moved.</summary>
    Task<int> UpdateWorkerAssignmentsAsync(int oldWorkerId, int newWorkerId, CancellationToken cancellationToken = default);
}

/// <summary>A future <c>ASSIGNED</c> job of a worker as the repository reads it.</summary>
public sealed record FutureJob(long AssignmentId, long OrderId, int SlotId, DateOnly SlotDate, string ShiftCode);

/// <summary>A future job as the API shows it (contract agencies.md 9.2).</summary>
public sealed record FutureJobHandover(
    long AssignmentId,
    long OrderId,
    DateOnly SlotDate,
    string ShiftCode
);
