using CommonService.Application.Features.Dispatch;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Infrastructure.Persistence;
using CommonService.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Dispatch;

/// <summary>
/// CHECK_IN_LOG repository of the Dispatch module (BE-M3-10).
/// </summary>
public class EfFieldCheckInRepository : EfRepository<CheckInLog, long>, IFieldCheckInRepository
{
    public EfFieldCheckInRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<JobAssignment?> GetAssignmentAsync(long assignmentId, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Select(l => l.Assignment)
            .FirstOrDefaultAsync(l => l.AssignmentId == assignmentId, cancellationToken);
    }

    public async Task<CheckInLog?> GetCheckInLogAsync(long assignmentId, CancellationToken cancellationToken = default)
    {
        return await DbSet.FirstOrDefaultAsync(l => l.AssignmentId == assignmentId, cancellationToken);
    }

    public async Task<CheckInLog> SaveCheckInLogAsync(CheckInLog log, CancellationToken cancellationToken = default)
    {
        if (log.CheckinId == 0)
        {
            log.CheckinId = await DbSet.MaxAsync(l => (long?)l.CheckinId, cancellationToken) ?? 0;
            log.CheckinId++;
        }

        await DbSet.AddAsync(log, cancellationToken);
        await SaveChangesAsync(cancellationToken);
        return log;
    }

    public async Task<bool> TransitionToCheckedInAsync(long assignmentId, DateTime checkedInAtUtc, CancellationToken cancellationToken = default)
    {
        var assignment = await GetAssignmentAsync(assignmentId, cancellationToken);
        if (assignment == null)
        {
            return false;
        }

        if (assignment.AssignmentStatus != JobAssignmentStatus.Assigned)
        {
            return false;
        }

        assignment.TransitionTo(JobAssignmentStatus.CheckedIn);
        assignment.CheckedInAt = checkedInAtUtc;
        await SaveChangesAsync(cancellationToken);
        return true;
    }
}