using System.Data;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Workers;

public class EfWorkerBankChangeRepository(AppDbContext db) : IWorkerBankChangeRepository
{
    // SQL Server errors of a unique index violation.
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    public async Task<BankWorker?> GetWorkerAsync(int workerId, CancellationToken cancellationToken = default)
    {
        var row = await db.Workers.AsNoTracking().Where(w => w.WorkerId == workerId)
            .Select(w => new { w.WorkerType, w.BankName, w.BankAccountNo })
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : new BankWorker(row.WorkerType, row.BankName, row.BankAccountNo);
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

    /// <summary>The same resource as the withdrawals of that worker (<c>EfWithdrawalRepository</c>): a withdrawal copies the account under it.</summary>
    private async Task LockAsync(int workerId, CancellationToken cancellationToken)
    {
        var result = new SqlParameter("@result", SqlDbType.Int) { Direction = ParameterDirection.Output };
        var resource = new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = $"ledger-party:WORKER:{workerId}" };
        await db.Database.ExecuteSqlRawAsync(
            "EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000",
            [result, resource], cancellationToken);
        if ((int)result.Value < 0) throw new BusinessRuleViolationException("The account is busy, try again.", "LEDGER_BUSY");
    }

    public Task<WorkerBankChangeRequest?> GetLatestAsync(int workerId, CancellationToken cancellationToken = default) =>
        db.WorkerBankChangeRequests.AsNoTracking().Where(r => r.WorkerId == workerId)
            .OrderByDescending(r => r.RequestedAt).ThenByDescending(r => r.RequestId)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<WorkerBankChangeRequest?> GetPendingAsync(int workerId, CancellationToken cancellationToken = default) =>
        db.WorkerBankChangeRequests.AsNoTracking()
            .FirstOrDefaultAsync(r => r.WorkerId == workerId && r.RequestStatus == BankChangeStatus.Pending, cancellationToken);

    public async Task<bool> TryAddAsync(WorkerBankChangeRequest request, CancellationToken cancellationToken = default)
    {
        db.WorkerBankChangeRequests.Add(request);
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

    public async Task<bool> TrySetFirstAccountAsync(int workerId, string bankName, string bankAccountNo, DateTime atUtc, CancellationToken cancellationToken = default) =>
        await db.Workers
            .Where(w => w.WorkerId == workerId && (w.BankAccountNo == null || w.BankAccountNo == "" || w.BankName == null || w.BankName == ""))
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.BankName, bankName)
                .SetProperty(w => w.BankAccountNo, bankAccountNo)
                .SetProperty(w => w.UpdatedAt, atUtc), cancellationToken) == 1;

    public Task<WorkerBankChangeRequest?> GetByIdAsync(int requestId, CancellationToken cancellationToken = default) =>
        db.WorkerBankChangeRequests.AsNoTracking().FirstOrDefaultAsync(r => r.RequestId == requestId, cancellationToken);

    public async Task<BankChangeRow?> GetRowAsync(int requestId, CancellationToken cancellationToken = default)
    {
        var request = await GetByIdAsync(requestId, cancellationToken);
        if (request is null) return null;
        var worker = await db.Workers.AsNoTracking().Where(w => w.WorkerId == request.WorkerId)
            .Select(w => new { w.FullName, w.PhoneNumber }).FirstOrDefaultAsync(cancellationToken);
        return new BankChangeRow(request, worker?.FullName ?? string.Empty, worker?.PhoneNumber ?? string.Empty);
    }

    public async Task<(IReadOnlyList<BankChangeRow> Items, int Total)> ListForAdminAsync(
        BankChangeStatus? status, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = db.WorkerBankChangeRequests.AsNoTracking().AsQueryable();
        if (status is not null) query = query.Where(r => r.RequestStatus == status);
        var total = await query.CountAsync(cancellationToken);
        var ordered = status == BankChangeStatus.Pending
            ? query.OrderBy(r => r.RequestedAt).ThenBy(r => r.RequestId)
            : query.OrderByDescending(r => r.RequestedAt).ThenByDescending(r => r.RequestId);
        var requests = await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        var ids = requests.Select(r => r.WorkerId).Distinct().ToList();
        var workers = await db.Workers.AsNoTracking().Where(w => ids.Contains(w.WorkerId))
            .Select(w => new { w.WorkerId, w.FullName, w.PhoneNumber }).ToDictionaryAsync(w => w.WorkerId, cancellationToken);
        var rows = requests
            .Select(r => new BankChangeRow(r, workers.TryGetValue(r.WorkerId, out var w) ? w.FullName : string.Empty, workers.TryGetValue(r.WorkerId, out var p) ? p.PhoneNumber : string.Empty))
            .ToList();
        return (rows, total);
    }

    public async Task<bool> TryApproveAsync(int requestId, int adminId, DateTime atUtc, CancellationToken cancellationToken = default)
    {
        var request = await GetByIdAsync(requestId, cancellationToken);
        if (request is null) return false;

        var updated = await db.WorkerBankChangeRequests
            .Where(r => r.RequestId == requestId && r.RequestStatus == BankChangeStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.RequestStatus, BankChangeStatus.Approved)
                .SetProperty(r => r.ReviewedBy, adminId)
                .SetProperty(r => r.ReviewedAt, atUtc), cancellationToken);
        if (updated != 1) return false;

        await db.Workers.Where(w => w.WorkerId == request.WorkerId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.BankName, request.BankName)
                .SetProperty(w => w.BankAccountNo, request.BankAccountNo)
                .SetProperty(w => w.UpdatedAt, atUtc), cancellationToken);
        return true;
    }

    public async Task<bool> TryRejectAsync(int requestId, string reason, int adminId, DateTime atUtc, CancellationToken cancellationToken = default) =>
        await db.WorkerBankChangeRequests
            .Where(r => r.RequestId == requestId && r.RequestStatus == BankChangeStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.RequestStatus, BankChangeStatus.Rejected)
                .SetProperty(r => r.RejectReason, reason)
                .SetProperty(r => r.ReviewedBy, adminId)
                .SetProperty(r => r.ReviewedAt, atUtc), cancellationToken) == 1;

    public Task SaveAsync(CancellationToken cancellationToken = default) => db.SaveChangesAsync(cancellationToken);
}
