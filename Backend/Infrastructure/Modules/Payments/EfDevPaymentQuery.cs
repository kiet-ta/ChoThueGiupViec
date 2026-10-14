using CommonService.Application.Features.Payments.Services;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Payments;

/// <summary>EF Core implementation of <see cref="IDevPaymentQuery"/> (UAT-PAY).</summary>
public sealed class EfDevPaymentQuery(AppDbContext db) : IDevPaymentQuery
{
    public async Task<IReadOnlyList<DevPaymentDto>> ListPendingAsync(int take, CancellationToken cancellationToken = default)
    {
        var rows = await db.PaymentTransactions.AsNoTracking()
            .Where(t => t.TxnStatus == PaymentStatus.Pending)
            .OrderByDescending(t => t.PaymentId)
            .Take(take)
            .ToListAsync(cancellationToken);
        return rows
            .Select(t => new DevPaymentDto(
                t.PaymentId, t.Purpose.ToString().ToUpperInvariant(), t.OrderId, t.SubscriptionId, t.AgencyId, t.Amount, t.GatewayTxnRef, t.CreatedAt))
            .ToList();
    }
}
