namespace CommonService.Application.Interfaces.Ports;

/// <param name="Location">Order location; candidates are searched around it.</param>
/// <param name="RadiusKm">Current dispatch radius step (5, 7 or 10 km, decisions section 4).</param>
public sealed record AvailabilityQuery(DateOnly Date, string ShiftCode, GeoPoint Location, double RadiusKm, int Limit);

/// <summary>A freelancer who is IDLE, KYC-approved, has a free slot and is inside the radius.</summary>
public sealed record AvailableWorker(int WorkerId, int SlotId, double DistanceKm);

/// <summary>Economy pool lookup. Implemented by Workers (M4); consumed by Dispatch (M3).</summary>
public interface IWorkerAvailabilityQuery
{
    Task<IReadOnlyList<AvailableWorker>> FindAvailableFreelancersAsync(AvailabilityQuery query, CancellationToken cancellationToken = default);
}
