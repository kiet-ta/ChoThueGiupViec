using System.Data;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Ledger;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Ledger;

public class EfLedgerRepository(AppDbContext db) : ILedgerRepository
{
    public async Task<T> ExecuteLockedAsync<T>(Func<ILedgerLockedReader, Task<T>> action, CancellationToken cancellationToken = default)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            await LockAsync(cancellationToken);
            return await action(this);
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await LockAsync(cancellationToken);
                var result = await action(this);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        });
    }

    private async Task LockAsync(CancellationToken cancellationToken)
    {
        var result = new SqlParameter("@result", SqlDbType.Int) { Direction = ParameterDirection.Output };
        var resource = new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = "ledger-append" };
        await db.Database.ExecuteSqlRawAsync(
            "EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000",
            [result, resource], cancellationToken);
        if ((int)result.Value < 0) throw new BusinessRuleViolationException("The ledger is busy, try again.", "LEDGER_BUSY");
    }

    public async Task<IReadOnlySet<string>> FindExistingKeysAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default)
    {
        var rows = await db.LedgerEntries.AsNoTracking().Where(e => keys.Contains(e.IdempotencyKey))
            .Select(e => e.IdempotencyKey).ToListAsync(cancellationToken);
        return rows.ToHashSet(StringComparer.Ordinal);
    }

    public Task<string?> GetLastHashAsync(CancellationToken cancellationToken = default) =>
        db.LedgerEntries.AsNoTracking().OrderByDescending(e => e.EntryId).Select(e => e.EntryHash).FirstOrDefaultAsync(cancellationToken);

    public async Task AppendAndSealAsync(LedgerEntry entry, Func<LedgerEntry, string> hash, CancellationToken cancellationToken = default)
    {
        db.LedgerEntries.Add(entry);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            entry.EntryHash = hash(entry);
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            db.Entry(entry).State = EntityState.Detached;
        }
    }

    public async Task<IReadOnlyList<LedgerEntry>> ReadChainAsync(CancellationToken cancellationToken = default) =>
        await db.LedgerEntries.AsNoTracking().OrderBy(e => e.EntryId).ToListAsync(cancellationToken);

    public async Task<decimal> GetOrderRefundTotalAsync(long orderId, CancellationToken cancellationToken = default) =>
        await db.LedgerEntries.AsNoTracking().Where(e => e.OrderId == orderId && e.EntryType == LedgerEntryType.Refund)
            .SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0m;

    public async Task<decimal> GetOrderClearingBalanceAsync(long orderId, CancellationToken cancellationToken = default)
    {
        var incoming = await db.LedgerEntries.AsNoTracking().Where(e => e.OrderId == orderId && e.ToPartyType == LedgerPartyType.PlatformClearing && e.ToPartyId == null)
            .SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0m;
        var outgoing = await db.LedgerEntries.AsNoTracking().Where(e => e.OrderId == orderId && e.FromPartyType == LedgerPartyType.PlatformClearing && e.FromPartyId == null)
            .SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0m;
        return incoming - outgoing;
    }
}
