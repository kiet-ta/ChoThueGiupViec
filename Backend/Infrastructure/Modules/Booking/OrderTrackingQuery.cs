using CommonService.Application.Features.Booking;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Booking;

/// <summary>JOB_ASSIGNMENT-only reads of the Booking module (BE-M2-10, PRD 5.2): no Include, no join, no tracking.</summary>
public class OrderTrackingQuery(AppDbContext context) : IOrderTrackingQuery
{
    public IQueryable<AssignmentProgress> OrderProgressQuery(long orderId) =>
        context.JobAssignments
            .AsNoTracking()
            .Where(a => a.OrderId == orderId)
            .OrderBy(a => a.AssignmentSeq)
            .ThenBy(a => a.AssignmentId)
            .Select(a => new AssignmentProgress(
                a.AssignmentId, a.AssignmentSeq, a.WorkerId, a.AssignmentStatus, a.AcceptedAt, a.CompletedAt));

    public IQueryable<CustomerAssignmentSummary> CustomerHistoryQuery(int customerId) =>
        context.JobAssignments
            .AsNoTracking()
            .Where(a => a.CustomerId == customerId)
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.AssignmentId)
            .Select(a => new CustomerAssignmentSummary(
                a.AssignmentId, a.OrderId, a.AssignmentSeq, a.ServiceTier, a.AssignmentStatus, a.GrossAmount, a.CreatedAt, a.CompletedAt));

    public async Task<IReadOnlyList<AssignmentProgress>> GetOrderProgressAsync(long orderId, CancellationToken cancellationToken = default) =>
        await OrderProgressQuery(orderId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CustomerAssignmentSummary>> GetCustomerHistoryAsync(int customerId, CancellationToken cancellationToken = default) =>
        await CustomerHistoryQuery(customerId).ToListAsync(cancellationToken);
}
