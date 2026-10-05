using System.Collections.Concurrent;
using CommonService.Application.Interfaces.Ports;

namespace CommonService.Infrastructure.Fakes;

public sealed class FakeWorkerReputation : IWorkerReputation
{
    private readonly ConcurrentDictionary<int, WorkerReputationDto> _items = new();

    public void Set(WorkerReputationDto reputation) => _items[reputation.WorkerId] = reputation;

    public Task<WorkerReputationDto?> GetAsync(int workerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_items.TryGetValue(workerId, out var item) ? item : null);
}
