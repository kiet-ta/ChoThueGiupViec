using CommonService.Application.Interfaces.Ports;

namespace CommonService.Infrastructure.Fakes;

/// <summary>Returns whatever list a test or a dev puts in <see cref="Workers"/> (up to the query limit).</summary>
public sealed class FakeWorkerAvailabilityQuery : IWorkerAvailabilityQuery
{
    public List<AvailableWorker> Workers { get; } = [];

    public Task<IReadOnlyList<AvailableWorker>> FindAvailableFreelancersAsync(AvailabilityQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AvailableWorker>>(Workers.Where(w => w.DistanceKm <= query.RadiusKm).OrderBy(w => w.DistanceKm).Take(query.Limit).ToList());
}
