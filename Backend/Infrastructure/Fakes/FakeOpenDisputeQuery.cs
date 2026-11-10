using CommonService.Application.Interfaces.Ports;

namespace CommonService.Infrastructure.Fakes;

public class FakeOpenDisputeQuery : IOpenDisputeQuery
{
    public HashSet<long> OpenOrderIds { get; } = [];

    public Task<IReadOnlySet<long>> GetOpenOrderIdsAsync(IReadOnlyCollection<long> orderIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<long>>(orderIds.Where(OpenOrderIds.Contains).ToHashSet());
}
