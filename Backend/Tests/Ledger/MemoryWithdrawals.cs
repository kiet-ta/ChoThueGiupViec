using CommonService.Application.Features.Ledger;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;

namespace CommonService.Tests.Ledger;

/// <summary>
/// The same rules as the SQL withdrawal repository, in memory: one PENDING request per worker, conditional updates, and a lock per
/// worker that really excludes (so two concurrent approvals queue up like they do on the application lock).
/// </summary>
public class MemoryWithdrawals : IWithdrawalRepository
{
    private readonly Dictionary<int, SemaphoreSlim> _locks = [];

    public Dictionary<int, WithdrawalWorker> Workers { get; } = new() { [7] = new WithdrawalWorker(WorkerType.Freelancer, "0123456789", "ACB") };
    public List<WithdrawalRequest> Rows { get; } = [];
    public bool IndexRejectsNextAdd { get; set; }
    public int LockedCalls { get; private set; }
    public int Saves { get; private set; }
    public bool HoldsLock { get; private set; }

    public Task<WithdrawalWorker?> GetWorkerAsync(int workerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Workers.GetValueOrDefault(workerId));

    public async Task<T> ExecuteLockedAsync<T>(int workerId, Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        SemaphoreSlim gate;
        lock (_locks) gate = _locks.TryGetValue(workerId, out var existing) ? existing : _locks[workerId] = new SemaphoreSlim(1, 1);
        await gate.WaitAsync(cancellationToken);
        HoldsLock = true;
        LockedCalls++;
        try
        {
            await Task.Yield(); // lets a competing call run up to the lock, like two requests in flight
            return await action();
        }
        finally
        {
            HoldsLock = false;
            gate.Release();
        }
    }

    public Task<WithdrawalRequest?> GetPendingAsync(int workerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Rows.FirstOrDefault(r => r.WorkerId == workerId && r.RequestStatus == WithdrawalStatus.Pending));

    public Task<WithdrawalRequest?> GetAsync(int workerId, int withdrawalId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Rows.FirstOrDefault(r => r.WorkerId == workerId && r.WithdrawalId == withdrawalId));

    public Task<WithdrawalRequest?> GetByIdAsync(int withdrawalId, CancellationToken cancellationToken = default)
    {
        var row = Rows.FirstOrDefault(r => r.WithdrawalId == withdrawalId);
        return Task.FromResult(row is null ? null : Copy(row)); // like AsNoTracking: the service changes its copy, never the row
    }

    public Task<(IReadOnlyList<WithdrawalRequest> Items, int Total)> ListAsync(int workerId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var own = Rows.Where(r => r.WorkerId == workerId).OrderByDescending(r => r.RequestedAt).ThenByDescending(r => r.WithdrawalId).ToList();
        return Task.FromResult<(IReadOnlyList<WithdrawalRequest>, int)>((own.Skip((page - 1) * pageSize).Take(pageSize).ToList(), own.Count));
    }

    public Task<(IReadOnlyList<AdminWithdrawalRow> Items, int Total)> ListForAdminAsync(
        WithdrawalStatus? status, int page, int pageSize, AdminWithdrawalSort? sort, CancellationToken cancellationToken = default)
    {
        var all = Rows.Where(r => status is null || r.RequestStatus == status).ToList();
        IEnumerable<WithdrawalRequest> ordered;
        if (sort is null)
        {
            ordered = status == WithdrawalStatus.Pending
                ? all.OrderBy(r => r.RequestedAt).ThenBy(r => r.WithdrawalId)
                : all.OrderByDescending(r => r.RequestedAt).ThenByDescending(r => r.WithdrawalId);
        }
        else
        {
            // The same keys as the SQL repository; the worker is named "Worker {id}" here, compared as text like the database does.
            Func<WithdrawalRequest, object?> key = sort.Key switch
            {
                AdminWithdrawalSortKey.Amount => r => r.Amount,
                AdminWithdrawalSortKey.Status => r => DbEnum.ToDb(r.RequestStatus),
                AdminWithdrawalSortKey.Bank => r => (r.BankName is null ? "0" : "1" + r.BankName) + "|" + (r.BankAccountNo is null ? "0" : "1" + r.BankAccountNo), // NULL first when ascending, like SQL Server
                AdminWithdrawalSortKey.Worker => r => $"Worker {r.WorkerId}",
                _ => r => r.RequestedAt,
            };
            ordered = sort.Descending
                ? all.OrderByDescending(key, Comparer<object?>.Create(Compare)).ThenByDescending(r => r.WithdrawalId)
                : all.OrderBy(key, Comparer<object?>.Create(Compare)).ThenBy(r => r.WithdrawalId);
        }

        var items = ordered.Skip((page - 1) * pageSize).Take(pageSize).Select(r => new AdminWithdrawalRow(r, $"Worker {r.WorkerId}", $"09000000{r.WorkerId:00}")).ToList();
        return Task.FromResult<(IReadOnlyList<AdminWithdrawalRow>, int)>((items, all.Count));
    }

    /// <summary>Text is compared ordinally so the expected order does not depend on the culture of the machine.</summary>
    private static int Compare(object? a, object? b) =>
        a is string x && b is string y ? string.CompareOrdinal(x, y) : Comparer<object?>.Default.Compare(a, b);

    public Task<bool> TryAddAsync(WithdrawalRequest request, CancellationToken cancellationToken = default)
    {
        Assert.True(HoldsLock, "A request must be created under the worker lock.");
        if (IndexRejectsNextAdd || Rows.Any(r => r.WorkerId == request.WorkerId && r.RequestStatus == WithdrawalStatus.Pending))
        {
            IndexRejectsNextAdd = false;
            return Task.FromResult(false);
        }

        request.WithdrawalId = Rows.Count + 1;
        Rows.Add(Copy(request));
        return Task.FromResult(true);
    }

    public Task<bool> TryCancelAsync(int workerId, int withdrawalId, CancellationToken cancellationToken = default)
    {
        Assert.True(HoldsLock, "A cancel must run under the worker lock.");
        var row = Rows.FirstOrDefault(r => r.WorkerId == workerId && r.WithdrawalId == withdrawalId && r.RequestStatus == WithdrawalStatus.Pending);
        if (row is null) return Task.FromResult(false);
        row.RequestStatus = WithdrawalStatus.Cancelled;
        return Task.FromResult(true);
    }

    public Task<bool> TryApproveAsync(int withdrawalId, long entryId, int adminId, DateTime atUtc, CancellationToken cancellationToken = default)
    {
        Assert.True(HoldsLock, "An approval must run under the worker lock.");
        var row = Rows.FirstOrDefault(r => r.WithdrawalId == withdrawalId && r.RequestStatus == WithdrawalStatus.Pending);
        if (row is null) return Task.FromResult(false);
        (row.RequestStatus, row.EntryId, row.ReviewedBy, row.ReviewedAt) = (WithdrawalStatus.Paid, entryId, adminId, atUtc);
        return Task.FromResult(true);
    }

    public Task<bool> TryRejectAsync(int withdrawalId, string reason, int adminId, DateTime atUtc, CancellationToken cancellationToken = default)
    {
        Assert.True(HoldsLock, "A rejection must run under the worker lock.");
        var row = Rows.FirstOrDefault(r => r.WithdrawalId == withdrawalId && r.RequestStatus == WithdrawalStatus.Pending);
        if (row is null) return Task.FromResult(false);
        (row.RequestStatus, row.RejectReason, row.ReviewedBy, row.ReviewedAt) = (WithdrawalStatus.Rejected, reason, adminId, atUtc);
        return Task.FromResult(true);
    }

    public Task SaveAsync(CancellationToken cancellationToken = default)
    {
        Saves++;
        return Task.CompletedTask;
    }

    private static WithdrawalRequest Copy(WithdrawalRequest r) => new()
    {
        WithdrawalId = r.WithdrawalId,
        WorkerId = r.WorkerId,
        Amount = r.Amount,
        RequestStatus = r.RequestStatus,
        BankAccountNo = r.BankAccountNo,
        BankName = r.BankName,
        RejectReason = r.RejectReason,
        EntryId = r.EntryId,
        ReviewedBy = r.ReviewedBy,
        ReviewedAt = r.ReviewedAt,
        RequestedAt = r.RequestedAt,
    };
}
