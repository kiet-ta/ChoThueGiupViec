using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Workers;

/// <summary>
/// Real implementation of <see cref="IWorkerAvailabilityQuery"/> querying WORKER and BOOKING_SLOT tables via EF Core (BE-M4-07).
/// Used by Dispatch (M3) candidate scanner to find available Freelancers.
/// Decisions D24: the search matches on the work area the freelancer registered (base point + radius), not on the last GPS point.
/// </summary>
public sealed class WorkerAvailabilityQuery(AppDbContext dbContext, IGeoService geoService) : IWorkerAvailabilityQuery
{
    public async Task<IReadOnlyList<AvailableWorker>> FindAvailableFreelancersAsync(
        AvailabilityQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.RadiusKm <= 0 || query.Limit <= 0)
        {
            return Array.Empty<AvailableWorker>();
        }

        // Bounding box pre-filtering for lat/lng (1 degree lat ~= 111.0 km)
        double latDegreeDelta = query.RadiusKm / 111.0;
        double minLat = query.Location.Latitude - latDegreeDelta;
        double maxLat = query.Location.Latitude + latDegreeDelta;

        double cosLat = Math.Cos(query.Location.Latitude * Math.PI / 180.0);
        double lngDegreeDelta = query.RadiusKm / (111.0 * Math.Max(0.01, Math.Abs(cosLat)));
        double minLng = query.Location.Longitude - lngDegreeDelta;
        double maxLng = query.Location.Longitude + lngDegreeDelta;

        // Query joining BOOKING_SLOT and WORKER directly using database indexes
        // Filters:
        // - WorkerType = FREELANCER (agency_id IS NULL)
        // - KycStatus = APPROVED
        // - WorkStatus = Idle
        // - BookingSlot: SlotDate == query.Date, ShiftCode == query.ShiftCode, SlotStatus == "AVAILABLE"
        // - A registered work area (base point + radius); a worker without one is never found (decisions D24)
        // - Bounding box on the base point WorkLat & WorkLng
        var candidateRows = await (
            from slot in dbContext.BookingSlots.AsNoTracking()
            join worker in dbContext.Workers.AsNoTracking() on slot.WorkerId equals worker.WorkerId
            where slot.SlotDate == query.Date
               && slot.ShiftCode == query.ShiftCode
               && slot.SlotStatus == "AVAILABLE"
               && worker.WorkerType == WorkerType.Freelancer
               && worker.AgencyId == null
               && worker.KycStatus == "APPROVED"
               && worker.WorkStatus == WorkStatus.Idle
               && worker.WorkLat != null
               && worker.WorkLng != null
               && worker.WorkRadiusKm != null
               && (double)worker.WorkLat!.Value >= minLat
               && (double)worker.WorkLat!.Value <= maxLat
               && (double)worker.WorkLng!.Value >= minLng
               && (double)worker.WorkLng!.Value <= maxLng
            select new
            {
                worker.WorkerId,
                slot.SlotId,
                Lat = (double)worker.WorkLat!.Value,
                Lng = (double)worker.WorkLng!.Value,
                RadiusKm = (int)worker.WorkRadiusKm!.Value,
                worker.RatingAvg
            }
        ).ToListAsync(cancellationToken);

        // Compute exact Haversine distance and keep the workers inside both the search step and their own radius
        var availableWorkers = new List<AvailableWorker>();

        foreach (var c in candidateRows)
        {
            var workerLocation = new GeoPoint(c.Lat, c.Lng);
            double distanceMeters = geoService.DistanceMeters(query.Location, workerLocation);
            double distanceKm = distanceMeters / 1000.0;

            if (distanceKm <= query.RadiusKm && distanceKm <= c.RadiusKm)
            {
                availableWorkers.Add(new AvailableWorker(c.WorkerId, c.SlotId, distanceKm));
            }
        }

        // Sort by DistanceKm ascending and return up to Limit
        return availableWorkers
            .OrderBy(w => w.DistanceKm)
            .Take(query.Limit)
            .ToList();
    }
}
