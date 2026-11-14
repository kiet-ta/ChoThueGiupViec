using System.Data;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Ledger;
using CommonService.Application.Features.Payouts;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Ledger;

public class EfWithdrawalRepository(AppDbContext db) : IWithdrawalRepository
{
    // SQL Server errors of a unique index violation.
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    public async Task<WithdrawalWorker?> GetWorkerAsync(int workerId, CancellationToken cancellationToken = default)
    {
        var row = await db.Workers.AsNoTracking().Where(w => w.WorkerId == workerId)
            .Select(w => new { w.WorkerType, w.BankAccountNo, w.BankName })
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : new WithdrawalWorker(row.WorkerType, row.BankAccountNo, row.BankName);
    }

    public async Task<T> ExecuteLockedAsync<T>(int workerId, Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            await LockAsync(workerId, cancellationToken);
            return await action();
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await LockAsync(workerId, cancellationToken);
                var result = await action();
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

    private async Task LockAsync(int workerId, CancellationToken cancellationToken)
    {
        var result = new SqlParameter("@result", SqlDbType.Int) { Direction = ParameterDirection.Output };
        var resource = new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = $"ledger-party:WORKER:{workerId}" };
        await db.Database.ExecuteSqlRawAsync(
            "EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000",
            [result, resource], cancellationToken);
        if ((int)result.Value < 0) throw new BusinessRuleViolationException("The wallet is busy, try again.", "LEDGER_BUSY");
    }

    public Task<WithdrawalRequest?> GetPendingAsync(int workerId, CancellationToken cancellationToken = default) =>
        db.WithdrawalRequests.AsNoTracking()
            .FirstOrDefaultAsync(w => w.WorkerId == workerId && w.RequestStatus == WithdrawalStatus.Pending, cancellationToken);

    public Task<WithdrawalRequest?> GetAsync(int workerId, int withdrawalId, CancellationToken cancellationToken = default) =>
        db.WithdrawalRequests.AsNoTracking()
            .FirstOrDefaultAsync(w => w.WithdrawalId == withdrawalId && w.WorkerId == workerId, cancellationToken);

    public async Task<(IReadOnlyList<WithdrawalRequest> Items, int Total)> ListAsync(
        int workerId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = db.WithdrawalRequests.AsNoTracking().Where(w => w.WorkerId == workerId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(w => w.RequestedAt).ThenByDescending(w => w.WithdrawalId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<bool> TryAddAsync(WithdrawalRequest request, CancellationToken cancellationToken = default)
    {
        db.WithdrawalRequests.Add(request);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: UniqueIndexViolation or UniqueConstraintViolation })
        {
            return false;
        }
        finally
        {
            db.Entry(request).State = EntityState.Detached;
        }
    }

    public async Task<bool> TryCancelAsync(int workerId, int withdrawalId, CancellationToken cancellationToken = default) =>
        await db.WithdrawalRequests
            .Where(w => w.WithdrawalId == withdrawalId && w.WorkerId == workerId && w.RequestStatus == WithdrawalStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.RequestStatus, WithdrawalStatus.Cancelled), cancellationToken) == 1;

    public Task<WithdrawalRequest?> GetByIdAsync(int withdrawalId, CancellationToken cancellationToken = default) =>
        db.WithdrawalRequests.AsNoTracking().FirstOrDefaultAsync(w => w.WithdrawalId == withdrawalId, cancellationToken);

    /// <summary>The requested order, ties by the request id in the same direction; no sort keeps the queue order (oldest first when pending, newest first otherwise).</summary>
    private IOrderedQueryable<WithdrawalRequest> Ordered(IQueryable<WithdrawalRequest> query, WithdrawalStatus? status, AdminWithdrawalSort? sort)
    {
        if (sort is null)
        {
            return status == WithdrawalStatus.Pending
                ? query.OrderBy(w => w.RequestedAt).ThenBy(w => w.WithdrawalId)
                : query.OrderByDescending(w => w.RequestedAt).ThenByDescending(w => w.WithdrawalId);
        }

        var desc = sort.Descending;
        return sort.Key switch
        {
            AdminWithdrawalSortKey.Amount => desc ? query.OrderByDescending(w => w.Amount).ThenByDescending(w => w.WithdrawalId) : query.OrderBy(w => w.Amount).ThenBy(w => w.WithdrawalId),
            AdminWithdrawalSortKey.Status => desc ? query.OrderByDescending(w => w.RequestStatus).ThenByDescending(w => w.WithdrawalId) : query.OrderBy(w => w.RequestStatus).ThenBy(w => w.WithdrawalId),
            AdminWithdrawalSortKey.Bank => desc
                ? query.OrderByDescending(w => w.BankName).ThenByDescending(w => w.BankAccountNo).ThenByDescending(w => w.WithdrawalId)
                : query.OrderBy(w => w.BankName).ThenBy(w => w.BankAccountNo).ThenBy(w => w.WithdrawalId),
            AdminWithdrawalSortKey.Worker => desc
                ? query.OrderByDescending(w => db.Workers.Where(x => x.WorkerId == w.WorkerId).Select(x => x.FullName).FirstOrDefault()).ThenByDescending(w => w.WithdrawalId)
                : query.OrderBy(w => db.Workers.Where(x => x.WorkerId == w.WorkerId).Select(x => x.FullName).FirstOrDefault()).ThenBy(w => w.WithdrawalId),
            _ => desc ? query.OrderByDescending(w => w.RequestedAt).ThenByDescending(w => w.WithdrawalId) : query.OrderBy(w => w.RequestedAt).ThenBy(w => w.WithdrawalId),
        };
    }

    public async Task<(IReadOnlyList<AdminWithdrawalRow> Items, int Total)> ListForAdminAsync(
        WithdrawalStatus? status, int page, int pageSize, AdminWithdrawalSort? sort, CancellationToken cancellationToken = default)
    {
        var query = db.WithdrawalRequests.AsNoTracking().AsQueryable();
        if (status is not null) query = query.Where(w => w.RequestStatus == status);
        var total = await query.CountAsync(cancellationToken);
        var ordered = Ordered(query, status, sort);
        var requests = await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        var workerIds = requests.Select(r => r.WorkerId).Distinct().ToList();
        var workers = await db.Workers.AsNoTracking().Where(w => workerIds.Contains(w.WorkerId))
            .Select(w => new { w.WorkerId, w.FullName, w.PhoneNumber })
            .ToDictionaryAsync(w => w.WorkerId, cancellationToken);
        var rows = requests
            .Select(r => new AdminWithdrawalRow(r, workers.GetValueOrDefault(r.WorkerId)?.FullName ?? string.Empty, workers.GetValueOrDefault(r.WorkerId)?.PhoneNumber ?? string.Empty))
            .ToList();
        return (rows, total);
    }

    public async Task<bool> TryApproveAsync(int withdrawalId, long entryId, int adminId, DateTime atUtc, CancellationToken cancellationToken = default) =>
        await db.WithdrawalRequests
            .Where(w => w.WithdrawalId == withdrawalId && w.RequestStatus == WithdrawalStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.RequestStatus, WithdrawalStatus.Paid)
                .SetProperty(w => w.EntryId, entryId)
                .SetProperty(w => w.ReviewedBy, adminId)
                .SetProperty(w => w.ReviewedAt, atUtc), cancellationToken) == 1;

    public async Task<bool> TryRejectAsync(int withdrawalId, string reason, int adminId, DateTime atUtc, CancellationToken cancellationToken = default) =>
        await db.WithdrawalRequests
            .Where(w => w.WithdrawalId == withdrawalId && w.RequestStatus == WithdrawalStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.RequestStatus, WithdrawalStatus.Rejected)
                .SetProperty(w => w.RejectReason, reason)
                .SetProperty(w => w.ReviewedBy, adminId)
                .SetProperty(w => w.ReviewedAt, atUtc), cancellationToken) == 1;

    public async Task SaveAsync(CancellationToken cancellationToken = default) => await db.SaveChangesAsync(cancellationToken);

    public async Task<bool> IsSettledForAsync(int workerId, string periodMonth, CancellationToken cancellationToken = default)
    {
        // Read only (decision D31). The settlement writes the worker's item and SETTLEMENT entry under the same worker lock as this check.
        var batch = await db.PayoutBatches.AsNoTracking().Where(b => b.PeriodMonth == periodMonth)
            .Select(b => new { b.BatchId, b.BatchStatus }).FirstOrDefaultAsync(cancellationToken);
        if (batch is null) return false;
        if (batch.BatchStatus == PayoutConstants.BatchClosed) return true;
        return await db.PayoutItems.AsNoTracking()
            .AnyAsync(i => i.BatchId == batch.BatchId && i.PayeeType == PayeeType.Freelancer && i.WorkerId == workerId, cancellationToken);
    }
}
