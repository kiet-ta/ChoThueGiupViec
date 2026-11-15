using CommonService.Application.Common.Options;
using CommonService.Application.Features.Booking;
using CommonService.Application.Features.Workers;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CommonService.Infrastructure.Modules.Workers;

public class EfWorkerExtensionRepository(AppDbContext dbContext, IOptions<BusinessRules>? rules = null) : IWorkerExtensionRepository
{
    private readonly BusinessRules _rules = rules?.Value ?? new BusinessRules();

    public async Task<bool> ClashesWithOtherJobAsync(
        int workerId, DateOnly date, long orderId, TimeOnly from, TimeOnly to, CancellationToken cancellationToken = default)
    {
        var jobs = await WorkerJobTimes.LoadAsync(dbContext, [workerId], date, cancellationToken, excludeOrderId: orderId);
        return jobs.TryGetValue(workerId, out var list)
            && list.Any(job => JobWindow.Overlaps(job, (from, to), _rules.Booking.TravelBufferMinutes));
    }

    public async Task<ExtensionForWorker?> FindForWorkerAsync(long assignmentId, int workerId, CancellationToken cancellationToken = default) =>
        await (
            from assignment in dbContext.JobAssignments.AsNoTracking()
            join order in dbContext.JobOrders.AsNoTracking() on assignment.OrderId equals order.OrderId
            join extension in dbContext.JobOrderExtensions.AsNoTracking() on order.OrderId equals extension.OrderId
            where assignment.AssignmentId == assignmentId && assignment.WorkerId == workerId && extension.WorkerId == workerId
            select new ExtensionForWorker(
                extension.ExtensionId, order.OrderId, extension.WorkerId, extension.ExtraHours, extension.ExtraAmount,
                extension.ExtStatus, order.ScheduledDate, order.ShiftCode, order.StartTime, order.EndTime))
        .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> TryAcceptAsync(int extensionId, DateTime decidedAtUtc, CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.JobOrderExtensions
            .Where(e => e.ExtensionId == extensionId && e.ExtStatus == ExtensionStatuses.Paid)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(e => e.ExtStatus, ExtensionStatuses.Accepted)
                    .SetProperty(e => e.WorkerDecision, WorkerDecisions.Accepted)
                    .SetProperty(e => e.DecidedAt, decidedAtUtc),
                cancellationToken);
        return rows == 1;
    }
}
