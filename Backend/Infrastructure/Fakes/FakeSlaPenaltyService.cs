using System.Collections.Concurrent;
using CommonService.Application.Interfaces.Ports;

namespace CommonService.Infrastructure.Fakes;

/// <summary>
/// Applies the point table of decisions Q09 (NO_SHOW -20, SHORTAGE -10, QUALITY_COMPLAINT -5) to a score that starts at 100.00,
/// and deducts customer refund + rescue cost from an endless escrow. The real service reads these values from BusinessRules.
/// </summary>
public sealed class FakeSlaPenaltyService : ISlaPenaltyService
{
    private readonly ConcurrentDictionary<int, decimal> _scores = new();
    private readonly ConcurrentQueue<SlaPenaltyRequest> _requests = new();

    public IReadOnlyCollection<SlaPenaltyRequest> Requests => _requests.ToArray();

    public Task<SlaPenaltyResult> ApplyAsync(SlaPenaltyRequest request, CancellationToken cancellationToken = default)
    {
        _requests.Enqueue(request);
        var delta = request.Violation switch
        {
            SlaViolation.NoShow => -20m,
            SlaViolation.Shortage => -10m,
            _ => -5m
        };
        var score = _scores.AddOrUpdate(request.AgencyId, 100m + delta, (_, current) => current + delta);
        return Task.FromResult(new SlaPenaltyResult(delta, score, request.CustomerRefundAmount + request.RescueCost, 0m, false));
    }
}
