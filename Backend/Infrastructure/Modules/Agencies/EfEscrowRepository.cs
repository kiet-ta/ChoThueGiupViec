using CommonService.Application.Features.Agencies;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Agencies;

/// <summary>EF Core implementation of <see cref="IEscrowRepository"/>. Not verified against a live SQL Server on this host.</summary>
public sealed class EfEscrowRepository(AppDbContext db) : IEscrowRepository
{
    public async Task<AgencyEscrowRow?> GetAgencyAsync(int agencyId, CancellationToken cancellationToken = default)
    {
        var row = await db.PartnerAgencies.AsNoTracking()
            .Where(a => a.AgencyId == agencyId)
            .Select(a => new { a.AgencyId, a.EscrowDepositBalance, a.SlaScore, a.AgencyStatus })
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : new AgencyEscrowRow(row.AgencyId, row.EscrowDepositBalance, row.SlaScore, row.AgencyStatus);
    }

    public async Task<bool> TryUpdateAgencyAsync(
        int agencyId, decimal expectedBalance, decimal newBalance, decimal newSlaScore, string newStatus, CancellationToken cancellationToken = default) =>
        await db.PartnerAgencies
            .Where(a => a.AgencyId == agencyId && a.EscrowDepositBalance == expectedBalance)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.EscrowDepositBalance, newBalance)
                .SetProperty(a => a.SlaScore, newSlaScore)
                .SetProperty(a => a.AgencyStatus, newStatus), cancellationToken) == 1;

    public void AddTransaction(EscrowTransaction row) => db.EscrowTransactions.Add(row);

    public async Task<EscrowTransaction?> GetTransactionAsync(long escrowTxnId, CancellationToken cancellationToken = default) =>
        await db.EscrowTransactions.AsNoTracking().FirstOrDefaultAsync(t => t.EscrowTxnId == escrowTxnId, cancellationToken);

    public async Task<EscrowTransaction?> FindPenaltyAsync(
        int agencyId, long? orderId, int? disputeId, string reason, CancellationToken cancellationToken = default) =>
        await db.EscrowTransactions.AsNoTracking()
            .Where(t => t.AgencyId == agencyId && t.TxnType == EscrowTransactionType.Penalty
                && t.OrderId == orderId && t.DisputeId == disputeId && t.Reason.StartsWith(reason))
            .OrderBy(t => t.EscrowTxnId)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>The appeal row is the link of record between a penalty and its reversal.</summary>
    public async Task<bool> HasReversalAsync(long penaltyTxnId, CancellationToken cancellationToken = default) =>
        await db.EscrowAppeals.AsNoTracking()
            .AnyAsync(a => a.EscrowTxnId == penaltyTxnId && a.Status == AppealStatuses.Accepted, cancellationToken);

    public async Task<(IReadOnlyList<EscrowTransaction> Items, int Total)> ListAsync(
        int agencyId, int skip, int take, CancellationToken cancellationToken = default)
    {
        var query = db.EscrowTransactions.AsNoTracking().Where(t => t.AgencyId == agencyId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(t => t.EscrowTxnId).Skip(skip).Take(take).ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<EscrowAppeal?> FindAppealByTransactionAsync(long escrowTxnId, CancellationToken cancellationToken = default) =>
        await db.EscrowAppeals.AsNoTracking().FirstOrDefaultAsync(a => a.EscrowTxnId == escrowTxnId, cancellationToken);

    public void AddAppeal(EscrowAppeal appeal) => db.EscrowAppeals.Add(appeal);

    public async Task<EscrowAppeal?> GetAppealForUpdateAsync(int appealId, CancellationToken cancellationToken = default) =>
        await db.EscrowAppeals.FirstOrDefaultAsync(a => a.AppealId == appealId, cancellationToken);

    public async Task<IReadOnlyList<EscrowAppeal>> ListAppealsAsync(string? status, int take, CancellationToken cancellationToken = default) =>
        await db.EscrowAppeals.AsNoTracking()
            .Where(a => status == null || a.Status == status)
            .OrderBy(a => a.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<PaymentTransaction?> FindPendingDepositAsync(long paymentId, CancellationToken cancellationToken = default) =>
        await db.PaymentTransactions.AsNoTracking()
            .FirstOrDefaultAsync(
                t => t.PaymentId == paymentId && t.Purpose == PaymentPurpose.EscrowDeposit && t.TxnStatus == PaymentStatus.Pending,
                cancellationToken);
}
