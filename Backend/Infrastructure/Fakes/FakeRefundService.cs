using System.Collections.Concurrent;
using CommonService.Application.Interfaces.Ports;

namespace CommonService.Infrastructure.Fakes;

/// <summary>Always succeeds and remembers the requests.</summary>
public sealed class FakeRefundService : IRefundService
{
    private readonly ConcurrentQueue<RefundRequest> _requests = new();

    public IReadOnlyCollection<RefundRequest> Requests => _requests.ToArray();

    public Task<RefundResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default)
    {
        _requests.Enqueue(request);
        return Task.FromResult(new RefundResult(true, request.Amount, null));
    }
}
