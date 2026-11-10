using System.Data;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Ledger;
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
}
