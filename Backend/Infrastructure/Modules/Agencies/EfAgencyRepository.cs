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

    public Task<Worker?> GetAgencyWorkerForUpdateAsync(int agencyId, int workerId, CancellationToken cancellationToken = default) =>
        dbContext.Workers
            .Where(w => w.WorkerId == workerId && w.AgencyId == agencyId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<(int WorkerId, int AgencyId)>> ListRetiringWorkersAsync(CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Workers.AsNoTracking()
            .Where(w => w.WorkStatus == WorkStatus.Retiring && w.AgencyId != null)
            .OrderBy(w => w.WorkerId)
            .Select(w => new { w.WorkerId, AgencyId = w.AgencyId!.Value })
            .ToListAsync(cancellationToken);
        return rows.Select(r => (r.WorkerId, r.AgencyId)).ToList();
    }

    public Task<int> GetFutureAssignedJobCountAsync(int workerId, CancellationToken cancellationToken = default) =>
        dbContext.JobAssignments.AsNoTracking()
            .Where(a => a.WorkerId == workerId && a.AssignmentStatus == JobAssignmentStatus.Assigned)
            .CountAsync(cancellationToken);

    public async Task<IReadOnlyList<FutureJob>> GetFutureAssignedJobsAsync(int workerId, CancellationToken cancellationToken = default)
    {
        return await (
            from a in dbContext.JobAssignments.AsNoTracking()
            join s in dbContext.BookingSlots.AsNoTracking() on a.SlotId equals s.SlotId
            where a.WorkerId == workerId && a.AssignmentStatus == JobAssignmentStatus.Assigned
            orderby s.SlotDate, s.ShiftCode
            select new FutureJob(a.AssignmentId, a.OrderId, a.SlotId, s.SlotDate, s.ShiftCode)
        ).ToListAsync(cancellationToken);
    }

    public Task<bool> HasActiveJobAsync(int workerId, CancellationToken cancellationToken = default) =>
        dbContext.JobAssignments.AsNoTracking()
            .AnyAsync(a => a.WorkerId == workerId
                && (a.AssignmentStatus == JobAssignmentStatus.CheckedIn
                    || a.AssignmentStatus == JobAssignmentStatus.InProgress
                    || a.AssignmentStatus == JobAssignmentStatus.AwaitingAcceptance), cancellationToken);

    public Task<int> CountFutureHeldSlotsAsync(int workerId, DateOnly fromDate, CancellationToken cancellationToken = default) =>
        dbContext.BookingSlots.AsNoTracking()
            .CountAsync(s => s.WorkerId == workerId && s.SlotStatus == DispatchSlotStatuses.Held && s.SlotDate >= fromDate, cancellationToken);

    public Task<int> RemoveFutureAvailableSlotsAsync(int workerId, DateOnly fromDate, CancellationToken cancellationToken = default) =>
        // A slot that an assignment (even a finished or reassigned one) points to stays: deleting it would break the foreign key.
        dbContext.BookingSlots
            .Where(s => s.WorkerId == workerId
                && s.SlotStatus == DispatchSlotStatuses.Available
                && s.SlotDate >= fromDate
                && !dbContext.JobAssignments.Any(a => a.SlotId == s.SlotId))
            .ExecuteDeleteAsync(cancellationToken);

    public async Task<Worker> CreateAgencyStaffAsync(int agencyId, NewWorkerDetails details, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var worker = Worker.CreateAgencyStaff(agencyId, details.PhoneNumber!, details.NationalId!, details.FullName!);
        worker.BankName = details.BankName;
        worker.BankAccountNo = details.BankAccountNo;
        worker.CreatedAt = nowUtc;
        worker.UpdatedAt = nowUtc;

        dbContext.Workers.Add(worker);
        // Saved here so the id is known; the caller's unit of work (one transaction) rolls it back with everything else.
        await dbContext.SaveChangesAsync(cancellationToken);
        return worker;
    }

    public Task<bool> WorkerPhoneOrNationalIdExistsAsync(string phoneNumber, string nationalId, CancellationToken cancellationToken = default) =>
        dbContext.Workers.AsNoTracking()
            .AnyAsync(w => w.PhoneNumber == phoneNumber || w.NationalId == nationalId, cancellationToken);

    public Task<int> UpdateWorkerSlotsAsync(int oldWorkerId, int newWorkerId, DateOnly fromDate, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        // BOOKED slots move only with a future ASSIGNED job: the slot of a job in progress stays with the worker who does it.
        var assignedSlotIds = dbContext.JobAssignments
            .Where(a => a.WorkerId == oldWorkerId && a.AssignmentStatus == JobAssignmentStatus.Assigned)
            .Select(a => a.SlotId);

        return dbContext.BookingSlots
            .Where(s => s.WorkerId == oldWorkerId
                && ((s.SlotStatus == DispatchSlotStatuses.Held && s.SlotDate >= fromDate)
                    || (s.SlotStatus == DispatchSlotStatuses.Booked && assignedSlotIds.Contains(s.SlotId))))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.WorkerId, newWorkerId)
                .SetProperty(x => x.UpdatedAt, nowUtc), cancellationToken);
    }

    public Task<int> UpdateWorkerAssignmentsAsync(int oldWorkerId, int newWorkerId, CancellationToken cancellationToken = default) =>
        dbContext.JobAssignments
            .Where(a => a.WorkerId == oldWorkerId && a.AssignmentStatus == JobAssignmentStatus.Assigned)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.WorkerId, newWorkerId), cancellationToken);
}
