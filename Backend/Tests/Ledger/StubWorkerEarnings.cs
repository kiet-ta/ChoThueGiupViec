using CommonService.Application.Features.Payouts;
using CommonService.Domain.Enums;

namespace CommonService.Tests.Ledger;

/// <summary>An earnings repository that knows no settled month (the history tests do not read payout items).</summary>
public class StubWorkerEarnings : IWorkerEarningsRepository
{
    public Task<WorkerType?> GetWorkerTypeAsync(int workerId, CancellationToken cancellationToken = default) => Task.FromResult<WorkerType?>(WorkerType.Freelancer);

    public Task<WorkerBatchState?> GetBatchStateAsync(int workerId, string periodMonth, CancellationToken cancellationToken = default) => Task.FromResult<WorkerBatchState?>(null);

    public Task<(IReadOnlyList<WorkerPayoutHistoryRow> Items, int Total)> GetClosedItemsAsync(int workerId, int page, int pageSize, CancellationToken cancellationToken = default) =>
        Task.FromResult<(IReadOnlyList<WorkerPayoutHistoryRow>, int)>(([], 0));
}
