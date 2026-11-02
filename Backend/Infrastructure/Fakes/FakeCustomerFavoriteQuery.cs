using System.Collections.Concurrent;
using CommonService.Application.Interfaces.Ports;

namespace CommonService.Infrastructure.Fakes;

/// <summary>In-memory favorites for tests and for running Booking before the Customers implementation is wired. Empty by default.</summary>
public sealed class FakeCustomerFavoriteQuery : ICustomerFavoriteQuery
{
    private readonly ConcurrentDictionary<(int CustomerId, int WorkerId), bool> _favorites = new();

    public void Add(int customerId, int workerId) => _favorites[(customerId, workerId)] = true;

    public Task<bool> IsFavoriteAsync(int customerId, int workerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_favorites.ContainsKey((customerId, workerId)));
}
