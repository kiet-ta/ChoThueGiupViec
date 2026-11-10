using CommonService.Application.Features.Disputes;
using CommonService.Application.Interfaces.Ports;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Disputes;

public class EfOpenDisputeQuery(AppDbContext db) : IOpenDisputeQuery
{
    public async Task<IReadOnlySet<long>> GetOpenOrderIdsAsync(IReadOnlyCollection<long> orderIds, CancellationToken cancellationToken = default)
    {
        var ids = await db.DisputeTickets.AsNoTracking()
            .Where(d => orderIds.Contains(d.OrderId) && (d.DisputeStatus == DisputeConstants.Open || d.DisputeStatus == DisputeConstants.InReview))
            .Select(d => d.OrderId).ToListAsync(cancellationToken);
        return ids.ToHashSet();
    }
}
