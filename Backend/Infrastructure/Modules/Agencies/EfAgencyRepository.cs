using CommonService.Application.Exceptions;
using CommonService.Application.Features.Agencies;
using CommonService.Application.Features.Agencies.Dtos;
using CommonService.Application.Features.Dispatch;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Agencies;

/// <summary>EF Core implementation of <see cref="IAgencyRepository"/> on <c>PARTNER_AGENCY</c>.</summary>
public sealed class EfAgencyRepository(AppDbContext dbContext) : IAgencyRepository
{
    public Task<bool> TaxCodeExistsAsync(string taxCode, CancellationToken cancellationToken = default) =>
        dbContext.PartnerAgencies.AsNoTracking().AnyAsync(a => a.TaxCode == taxCode, cancellationToken);

    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) =>
        dbContext.PartnerAgencies.AsNoTracking().AnyAsync(a => a.ContactEmail == email, cancellationToken);

    public async Task AddAsync(PartnerAgency agency, CancellationToken cancellationToken = default)
    {
        dbContext.PartnerAgencies.Add(agency);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (SqlUniqueViolation.IsUniqueViolation(ex))
        {
            // Two registrations with the same tax code passed the existence check at the same time.
            // PARTNER_AGENCY has exactly one unique index besides the primary key (tax_code).
            dbContext.Entry(agency).State = EntityState.Detached;
            throw new BusinessRuleViolationException(
                "An agency with this tax code is already registered.",
                ex,
                AgencyErrorCodes.TaxCodeExists);
        }
    }

    public async Task<(IReadOnlyList<AgencyWorkerItem> Items, int TotalCount)> GetAgencyWorkersAsync(
        int agencyId, int page, int pageSize, string? statusFilter, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Workers.AsNoTracking()
            .Where(w => w.AgencyId == agencyId);

        if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            if (Enum.TryParse<WorkStatus>(statusFilter, true, out var status))
            {
                query = query.Where(w => w.WorkStatus == status);
            }
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(w => w.WorkerId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(w => new AgencyWorkerItem(
                w.WorkerId,
                w.FullName,
                w.WorkStatus.ToString().ToUpperInvariant(),
                0 // Will be filled in below
            ))
            .ToListAsync(cancellationToken);

        // Fill in the future assigned job counts
        var workerIds = items.Select(i => i.WorkerId).ToList();
        var futureJobCounts = await dbContext.JobAssignments.AsNoTracking()
            .Where(a => workerIds.Contains(a.WorkerId)
                && a.AssignmentStatus == JobAssignmentStatus.Assigned)
            .GroupBy(a => a.WorkerId)
            .Select(g => new { WorkerId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.WorkerId, x => x.Count, cancellationToken);

        var finalItems = items.Select(i => i with
        {
            FutureAssignedJobs = futureJobCounts.TryGetValue(i.WorkerId, out var count) ? count : 0
        }).ToList();

        return (finalItems, totalCount);
    }

    public Task<Worker?> GetAgencyWorkerAsync(int agencyId, int workerId, CancellationToken cancellationToken = default) =>
        dbContext.Workers.AsNoTracking()
            .Where(w => w.WorkerId == workerId && w.AgencyId == agencyId)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<int> GetFutureAssignedJobCountAsync(int workerId, CancellationToken cancellationToken = default) =>
        dbContext.JobAssignments.AsNoTracking()
            .Where(a => a.WorkerId == workerId && a.AssignmentStatus == JobAssignmentStatus.Assigned)
            .CountAsync(cancellationToken);

    public async Task<IReadOnlyList<FutureJobHandover>> GetFutureAssignedJobsAsync(int workerId, CancellationToken cancellationToken = default)
    {
        return await (
            from a in dbContext.JobAssignments.AsNoTracking()
            join s in dbContext.BookingSlots.AsNoTracking() on a.SlotId equals s.SlotId
            where a.WorkerId == workerId && a.AssignmentStatus == JobAssignmentStatus.Assigned
            orderby s.SlotDate, s.ShiftCode
            select new FutureJobHandover(a.AssignmentId, s.SlotDate, s.ShiftCode)
        ).ToListAsync(cancellationToken);
    }

    public async Task<Worker> CreateAgencyStaffAsync(int agencyId, NewWorkerDetails details, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var worker = Worker.CreateAgencyStaff(agencyId, details.PhoneNumber!, details.NationalId!, details.FullName!);
        worker.BankName = details.BankName;
        worker.BankAccountNo = details.BankAccountNo;
        worker.CreatedAt = nowUtc;
        worker.UpdatedAt = nowUtc;

        dbContext.Workers.Add(worker);
        await dbContext.SaveChangesAsync(cancellationToken);
        return worker;
    }

    public Task<bool> WorkerPhoneOrNationalIdExistsAsync(string phoneNumber, string nationalId, CancellationToken cancellationToken = default) =>
        dbContext.Workers.AsNoTracking()
            .AnyAsync(w => w.PhoneNumber == phoneNumber || w.NationalId == nationalId, cancellationToken);

    public async Task<int> UpdateWorkerSlotsAsync(int oldWorkerId, int newWorkerId, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        // Update slots that are HELD or BOOKED (future jobs) and belong to the old worker
        // Only update slots where the new worker doesn't already have a slot for the same date/shift
        var slotsToUpdate = await dbContext.BookingSlots
            .Where(s => s.WorkerId == oldWorkerId
                && (s.SlotStatus == DispatchSlotStatuses.Held || s.SlotStatus == DispatchSlotStatuses.Booked)
                && !dbContext.BookingSlots.Any(s2 => s2.WorkerId == newWorkerId && s2.SlotDate == s.SlotDate && s2.ShiftCode == s.ShiftCode))
            .ToListAsync(cancellationToken);

        int updated = 0;
        foreach (var slot in slotsToUpdate)
        {
            slot.WorkerId = newWorkerId;
            slot.UpdatedAt = nowUtc;
            updated++;
        }

        if (updated > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return updated;
    }

    public async Task<int> UpdateWorkerAssignmentsAsync(int oldWorkerId, int newWorkerId, CancellationToken cancellationToken = default)
    {
        var assignmentsToUpdate = await dbContext.JobAssignments
            .Where(a => a.WorkerId == oldWorkerId && a.AssignmentStatus == JobAssignmentStatus.Assigned)
            .ToListAsync(cancellationToken);

        int updated = 0;
        foreach (var assignment in assignmentsToUpdate)
        {
            assignment.WorkerId = newWorkerId;
            updated++;
        }

        if (updated > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return updated;
    }

    public async Task<IReadOnlyList<Worker>> GetRetiringWorkersReadyForRetiredAsync(CancellationToken cancellationToken = default)
    {
        // Find workers with Retiring status that have no active or future jobs
        var retiringWorkers = await dbContext.Workers
            .Where(w => w.WorkStatus == WorkStatus.Retiring)
            .ToListAsync(cancellationToken);

        var result = new List<Worker>();
        foreach (var worker in retiringWorkers)
        {
            var hasActiveOrFuture = await HasActiveOrFutureJobsAsync(worker.WorkerId, cancellationToken);
            if (!hasActiveOrFuture)
            {
                result.Add(worker);
            }
        }

        return result;
    }

    public async Task<bool> HasActiveOrFutureJobsAsync(int workerId, CancellationToken cancellationToken = default)
    {
        // Check for active jobs: CheckedIn, InProgress, AwaitingAcceptance
        var hasActive = await dbContext.JobAssignments.AsNoTracking()
            .AnyAsync(a => a.WorkerId == workerId
                && (a.AssignmentStatus == JobAssignmentStatus.CheckedIn
                    || a.AssignmentStatus == JobAssignmentStatus.InProgress
                    || a.AssignmentStatus == JobAssignmentStatus.AwaitingAcceptance), cancellationToken);

        if (hasActive) return true;

        // Check for future ASSIGNED jobs
        var hasFuture = await dbContext.JobAssignments.AsNoTracking()
            .AnyAsync(a => a.WorkerId == workerId && a.AssignmentStatus == JobAssignmentStatus.Assigned, cancellationToken);

        return hasFuture;
    }
}