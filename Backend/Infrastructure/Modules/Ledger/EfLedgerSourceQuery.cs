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
            .Select(p => new { p.PaymentId, p.ExtensionId, p.Amount })
            .ToListAsync(cancellationToken);
        return rows.Select(p => new LedgerPaymentSource(p.PaymentId, orderId, p.ExtensionId, p.Amount)).ToList();
    }

    public Task<LedgerDisputeSource?> GetDisputeAsync(int disputeId, CancellationToken cancellationToken = default) =>
        db.DisputeTickets.AsNoTracking().Where(d => d.DisputeId == disputeId)
            .Select(d => new LedgerDisputeSource(
                d.DisputeId, d.OrderId, d.DisputeStatus == ResolvedStatus, d.FaultParty, d.Category, d.CompensationAmount ?? 0m))
            .FirstOrDefaultAsync(cancellationToken);
}
