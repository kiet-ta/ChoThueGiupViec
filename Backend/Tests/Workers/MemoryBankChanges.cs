using CommonService.Application.Features.Workers;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using Xunit;

namespace CommonService.Tests.Workers;

/// <summary>
/// The same rules as the SQL repository of the payout account requests, in memory: one PENDING request per worker, conditional updates, the
/// worker's account written by the first set and by an approval, and a lock per worker that really excludes.
/// </summary>
public class MemoryBankChanges : IWorkerBankChangeRepository
{
    private readonly Dictionary<int, SemaphoreSlim> _locks = [];

    /// <summary>Worker 7 starts with no account, worker 8 with one.</summary>
    public Dictionary<int, BankWorker> Workers { get; } = new()
    {
        [7] = new BankWorker(WorkerType.Freelancer, null, null),
        [8] = new BankWorker(WorkerType.Freelancer, "ACB", "0123456789"),
    };

    public List<WorkerBankChangeRequest> Rows { get; } = [];
    public bool IndexRejectsNextAdd { get; set; }
    public bool HoldsLock { get; private set; }
    public int Saves { get; private set; }

    public Task<BankWorker?> GetWorkerAsync(int workerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Workers.GetValueOrDefault(workerId));

    public async Task<T> ExecuteLockedAsync<T>(int workerId, Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        SemaphoreSlim gate;
        lock (_locks) gate = _locks.TryGetValue(workerId, out var existing) ? existing : _locks[workerId] = new SemaphoreSlim(1, 1);
        await gate.WaitAsync(cancellationToken);
        HoldsLock = true;
        try
        {
            await Task.Yield();
            return await action();
        }
        finally
        {
            HoldsLock = false;
            gate.Release();
        }
    }

    public Task<WorkerBankChangeRequest?> GetLatestAsync(int workerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Rows.Where(r => r.WorkerId == workerId).OrderByDescending(r => r.RequestedAt).ThenByDescending(r => r.RequestId).Select(Copy).FirstOrDefault());

    public Task<WorkerBankChangeRequest?> GetPendingAsync(int workerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Rows.Where(r => r.WorkerId == workerId && r.RequestStatus == BankChangeStatus.Pending).Select(Copy).FirstOrDefault());

    public Task<bool> TryAddAsync(WorkerBankChangeRequest request, CancellationToken cancellationToken = default)
    {
        Assert.True(HoldsLock, "A request must be created under the worker lock.");
        if (IndexRejectsNextAdd || Rows.Any(r => r.WorkerId == request.WorkerId && r.RequestStatus == BankChangeStatus.Pending))
        {
            IndexRejectsNextAdd = false;
            return Task.FromResult(false);
        }

        request.RequestId = Rows.Count + 1;
        Rows.Add(Copy(request));
        return Task.FromResult(true);
    }

    public Task<bool> TrySetFirstAccountAsync(int workerId, string bankName, string bankAccountNo, DateTime atUtc, CancellationToken cancellationToken = default)
    {
        Assert.True(HoldsLock, "The first account must be set under the worker lock.");
        var worker = Workers[workerId];
        if (worker.IsSet) return Task.FromResult(false);
        Workers[workerId] = worker with { BankName = bankName, BankAccountNo = bankAccountNo };
        return Task.FromResult(true);
    }

    public Task<WorkerBankChangeRequest?> GetByIdAsync(int requestId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Rows.Where(r => r.RequestId == requestId).Select(Copy).FirstOrDefault()); // like AsNoTracking: the service changes its copy

    public Task<BankChangeRow?> GetRowAsync(int requestId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Rows.Where(r => r.RequestId == requestId).Select(r => new BankChangeRow(Copy(r), $"Worker {r.WorkerId}", $"09000000{r.WorkerId:00}")).FirstOrDefault());

    public Task<(IReadOnlyList<BankChangeRow> Items, int Total)> ListForAdminAsync(
        BankChangeStatus? status, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var all = Rows.Where(r => status is null || r.RequestStatus == status).ToList();
        var ordered = status == BankChangeStatus.Pending
            ? all.OrderBy(r => r.RequestedAt).ThenBy(r => r.RequestId)
            : all.OrderByDescending(r => r.RequestedAt).ThenByDescending(r => r.RequestId);
        var items = ordered.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new BankChangeRow(Copy(r), $"Worker {r.WorkerId}", $"09000000{r.WorkerId:00}")).ToList();
        return Task.FromResult<(IReadOnlyList<BankChangeRow>, int)>((items, all.Count));
    }

    public Task<bool> TryApproveAsync(int requestId, int adminId, DateTime atUtc, CancellationToken cancellationToken = default)
    {
        Assert.True(HoldsLock, "An approval must run under the worker lock.");
        var row = Rows.FirstOrDefault(r => r.RequestId == requestId && r.RequestStatus == BankChangeStatus.Pending);
        if (row is null) return Task.FromResult(false);
        (row.RequestStatus, row.ReviewedBy, row.ReviewedAt) = (BankChangeStatus.Approved, adminId, atUtc);
        Workers[row.WorkerId] = Workers[row.WorkerId] with { BankName = row.BankName, BankAccountNo = row.BankAccountNo };
        return Task.FromResult(true);
    }

    public Task<bool> TryRejectAsync(int requestId, string reason, int adminId, DateTime atUtc, CancellationToken cancellationToken = default)
    {
        Assert.True(HoldsLock, "A rejection must run under the worker lock.");
        var row = Rows.FirstOrDefault(r => r.RequestId == requestId && r.RequestStatus == BankChangeStatus.Pending);
        if (row is null) return Task.FromResult(false);
        (row.RequestStatus, row.RejectReason, row.ReviewedBy, row.ReviewedAt) = (BankChangeStatus.Rejected, reason, adminId, atUtc);
        return Task.FromResult(true);
    }

    public Task SaveAsync(CancellationToken cancellationToken = default)
    {
        Saves++;
        return Task.CompletedTask;
    }

    private static WorkerBankChangeRequest Copy(WorkerBankChangeRequest r) => new()
    {
        RequestId = r.RequestId,
        WorkerId = r.WorkerId,
        BankName = r.BankName,
        BankAccountNo = r.BankAccountNo,
        PreviousBankName = r.PreviousBankName,
        PreviousBankAccountNo = r.PreviousBankAccountNo,
        RequestStatus = r.RequestStatus,
        RejectReason = r.RejectReason,
        ReviewedBy = r.ReviewedBy,
        ReviewedAt = r.ReviewedAt,
        RequestedAt = r.RequestedAt,
    };
}
