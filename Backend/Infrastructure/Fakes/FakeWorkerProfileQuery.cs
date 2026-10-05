using System.Collections.Concurrent;
using CommonService.Application.Interfaces.Ports;

namespace CommonService.Infrastructure.Fakes;

public sealed class FakeWorkerProfileQuery : IWorkerProfileQuery
{
    private readonly ConcurrentDictionary<int, WorkerProfileSummary> _items = new();

    public void Add(WorkerProfileSummary summary) => _items[summary.WorkerId] = summary;

    public Task<WorkerProfileSummary?> GetAsync(int workerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_items.TryGetValue(workerId, out var item) ? item : null);

    public Task<IReadOnlyDictionary<int, WorkerProfileSummary>> GetManyAsync(IEnumerable<int> workerIds, CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<int, WorkerProfileSummary> result = workerIds
            .Distinct()
            .Where(_items.ContainsKey)
            .ToDictionary(id => id, id => _items[id]);
        return Task.FromResult(result);
    }

    public Task<bool> ExistsAsync(int workerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_items.ContainsKey(workerId));
}
