using CommonService.Application.Features.Booking;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Ledger;

/// <summary>Read-only (AsNoTracking) source facts for the ledger. It never writes to another module's tables.</summary>
public class EfLedgerSourceQuery(AppDbContext db) : ILedgerSourceQuery
{
    private const string ResolvedStatus = "RESOLVED";

    public Task<LedgerOrderSource?> GetOrderAsync(long orderId, CancellationToken cancellationToken = default) =>
        db.JobOrders.AsNoTracking().Where(o => o.OrderId == orderId)
            .Select(o => new LedgerOrderSource(o.OrderId, o.CustomerId, o.TotalAmount, o.RequiredWorkers))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<LedgerAssignmentSource>> GetOrderAssignmentsAsync(long orderId, CancellationToken cancellationToken = default) =>
        await db.JobAssignments.AsNoTracking().Where(a => a.OrderId == orderId).OrderBy(a => a.AssignmentId)
            .Select(a => new LedgerAssignmentSource(
                a.AssignmentId, a.OrderId, a.WorkerId, a.AgencyId, a.AssignmentSeq, a.GrossAmount, a.CommissionRate, a.CompletedAt,
                a.AssignmentStatus == JobAssignmentStatus.Completed))
            .ToListAsync(cancellationToken);

    public Task<LedgerExtensionSource?> GetExtensionAsync(int extensionId, CancellationToken cancellationToken = default) =>
        db.JobOrderExtensions.AsNoTracking().Where(e => e.ExtensionId == extensionId)
            .Select(e => new LedgerExtensionSource(
                e.ExtensionId, e.OrderId, e.WorkerId, e.ExtraAmount,
                e.ExtStatus == ExtensionStatuses.Paid || e.ExtStatus == ExtensionStatuses.Accepted))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<LedgerExtensionSource>> GetOrderExtensionsAsync(long orderId, CancellationToken cancellationToken = default) =>
        await db.JobOrderExtensions.AsNoTracking().Where(e => e.OrderId == orderId).OrderBy(e => e.ExtensionId)
            .Select(e => new LedgerExtensionSource(
                e.ExtensionId, e.OrderId, e.WorkerId, e.ExtraAmount,
                e.ExtStatus == ExtensionStatuses.Paid || e.ExtStatus == ExtensionStatuses.Accepted))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<LedgerPaymentSource>> GetOrderPaymentsAsync(long orderId, CancellationToken cancellationToken = default)
    {
        var extensionIds = await db.JobOrderExtensions.AsNoTracking().Where(e => e.OrderId == orderId)
            .Select(e => e.ExtensionId).ToListAsync(cancellationToken);
        var rows = await db.PaymentTransactions.AsNoTracking()
            .Where(p => (p.TxnStatus == PaymentStatus.Success || p.TxnStatus == PaymentStatus.Refunded) &&
                        ((p.Purpose == PaymentPurpose.Order && p.OrderId == orderId) ||
                         (p.Purpose == PaymentPurpose.Extension && p.ExtensionId != null && extensionIds.Contains(p.ExtensionId.Value))))
            .OrderBy(p => p.PaymentId)
            .Select(p => new { p.PaymentId, p.ExtensionId, p.Amount, p.RefundedAmount })
            .ToListAsync(cancellationToken);
        return rows.Select(p => new LedgerPaymentSource(p.PaymentId, orderId, p.ExtensionId, p.Amount, p.RefundedAmount)).ToList();
    }

    public Task<LedgerDisputeSource?> GetDisputeAsync(int disputeId, CancellationToken cancellationToken = default) =>
        db.DisputeTickets.AsNoTracking().Where(d => d.DisputeId == disputeId)
            .Select(d => new LedgerDisputeSource(
                d.DisputeId, d.OrderId, d.DisputeStatus == ResolvedStatus, d.FaultParty, d.Category, d.CompensationAmount ?? 0m, d.ResolvedAt))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<LedgerSweepPayment>> GetPaidPaymentsSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default) =>
        await db.PaymentTransactions.AsNoTracking()
            .Where(p => (p.TxnStatus == PaymentStatus.Success || p.TxnStatus == PaymentStatus.Refunded) &&
                        (p.Purpose == PaymentPurpose.Order || p.Purpose == PaymentPurpose.Extension || p.Purpose == PaymentPurpose.Subscription) &&
                        (p.PaidAt ?? p.CreatedAt) >= sinceUtc)
            .OrderBy(p => p.PaymentId)
            .Select(p => new LedgerSweepPayment(p.PaymentId, p.Purpose, p.OrderId, p.ExtensionId, p.AgencyId, p.Amount, p.PaidAt ?? p.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<LedgerAssignmentSource>> GetCompletedAssignmentsSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default) =>
        await db.JobAssignments.AsNoTracking()
            .Where(a => a.AssignmentStatus == JobAssignmentStatus.Completed && a.CompletedAt >= sinceUtc)
            .OrderBy(a => a.AssignmentId)
            .Select(a => new LedgerAssignmentSource(
                a.AssignmentId, a.OrderId, a.WorkerId, a.AgencyId, a.AssignmentSeq, a.GrossAmount, a.CommissionRate, a.CompletedAt, true))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<LedgerAbsenceSource>> GetAbsenceFeesSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default)
    {
        var rows = await db.JobAssignments.AsNoTracking()
            .Where(a => a.AssignmentStatus == JobAssignmentStatus.Absent && a.AbsenceFeeAmount > 0 && a.UpdatedAt >= sinceUtc)
            .OrderBy(a => a.AssignmentId)
            .Select(a => new
            {
                a.AssignmentId,
                a.OrderId,
                a.WorkerId,
                a.AgencyId,
                a.AssignmentSeq,
                a.GrossAmount,
                a.CommissionRate,
                a.CompletedAt,
                Fee = a.AbsenceFeeAmount!.Value,
            })
            .ToListAsync(cancellationToken);
        return rows.Select(a => new LedgerAbsenceSource(
            new LedgerAssignmentSource(a.AssignmentId, a.OrderId, a.WorkerId, a.AgencyId, a.AssignmentSeq, a.GrossAmount, a.CommissionRate, a.CompletedAt, false),
            a.Fee)).ToList();
    }

    public async Task<IReadOnlyList<LedgerDisputeSource>> GetResolvedDisputesSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default) =>
        await db.DisputeTickets.AsNoTracking()
            .Where(d => d.DisputeStatus == ResolvedStatus && d.ResolvedAt >= sinceUtc)
            .OrderBy(d => d.DisputeId)
            .Select(d => new LedgerDisputeSource(d.DisputeId, d.OrderId, true, d.FaultParty, d.Category, d.CompensationAmount ?? 0m, d.ResolvedAt))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<LedgerEscrowSource>> GetEscrowTransactionsSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default) =>
        await db.EscrowTransactions.AsNoTracking()
            .Where(t => t.CreatedAt >= sinceUtc)
            .OrderBy(t => t.EscrowTxnId)
            .Select(t => new LedgerEscrowSource(t.EscrowTxnId, t.AgencyId, t.TxnType, t.Amount, t.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<LedgerRefundedOrder>> GetRefundedOrdersAsync(DateTime sinceUtc, DateTime quietBeforeUtc, CancellationToken cancellationToken = default)
    {
        // Orders with a refund reserved since the start; then every refunded payment of those orders (an extension belongs to its order).
        var recent = await db.PaymentTransactions.AsNoTracking()
            .Where(p => p.RefundedAmount > 0 && p.RefundedAt >= sinceUtc && (p.Purpose == PaymentPurpose.Order || p.Purpose == PaymentPurpose.Extension))
            .Select(p => new { p.OrderId, p.ExtensionId })
            .ToListAsync(cancellationToken);
        if (recent.Count == 0) return [];

        var extensionIds = recent.Where(p => p.ExtensionId != null).Select(p => p.ExtensionId!.Value).Distinct().ToList();
        var extensionOrders = await db.JobOrderExtensions.AsNoTracking().Where(e => extensionIds.Contains(e.ExtensionId))
            .ToDictionaryAsync(e => e.ExtensionId, e => e.OrderId, cancellationToken);
        var orderIds = recent.Where(p => p.OrderId != null).Select(p => p.OrderId!.Value)
            .Concat(extensionOrders.Values).Distinct().ToList();

        var orderExtensions = await db.JobOrderExtensions.AsNoTracking().Where(e => orderIds.Contains(e.OrderId))
            .Select(e => new { e.ExtensionId, e.OrderId }).ToListAsync(cancellationToken);
        var extensionToOrder = orderExtensions.ToDictionary(e => e.ExtensionId, e => e.OrderId);
        var allExtensionIds = extensionToOrder.Keys.ToList();
        var payments = await db.PaymentTransactions.AsNoTracking()
            .Where(p => p.RefundedAmount > 0 &&
                        ((p.Purpose == PaymentPurpose.Order && p.OrderId != null && orderIds.Contains(p.OrderId.Value)) ||
                         (p.Purpose == PaymentPurpose.Extension && p.ExtensionId != null && allExtensionIds.Contains(p.ExtensionId.Value))))
            .Select(p => new { p.OrderId, p.ExtensionId, p.RefundedAmount, p.RefundedAt })
            .ToListAsync(cancellationToken);
        var customers = await db.JobOrders.AsNoTracking().Where(o => orderIds.Contains(o.OrderId))
            .ToDictionaryAsync(o => o.OrderId, o => o.CustomerId, cancellationToken);

        return payments
            .GroupBy(p => p.OrderId ?? extensionToOrder[p.ExtensionId!.Value])
            .Select(g => new LedgerRefundedOrder(g.Key, customers.GetValueOrDefault(g.Key), g.Sum(p => p.RefundedAmount), g.Max(p => p.RefundedAt ?? DateTime.MinValue)))
            .Where(o => o.LatestRefundedAtUtc >= sinceUtc && o.LatestRefundedAtUtc <= quietBeforeUtc && customers.ContainsKey(o.OrderId))
            .OrderBy(o => o.OrderId)
            .ToList();
    }
}
