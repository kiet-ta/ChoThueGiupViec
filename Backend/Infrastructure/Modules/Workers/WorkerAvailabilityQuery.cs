using CommonService.Application.Common.Options;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CommonService.Infrastructure.Modules.Workers;

/// <summary>
/// Real implementation of <see cref="IWorkerAvailabilityQuery"/> querying WORKER and BOOKING_SLOT tables via EF Core (BE-M4-07).
/// Used by Dispatch (M3) candidate scanner to find available Freelancers.
/// Decisions D24: the search matches on the work area the freelancer registered (base point + radius), not on the last GPS point.
/// Decisions D34: a shift slot stays AVAILABLE after a timed job, so with <see cref="AvailabilityQuery.OrderId"/> the worker's live jobs
/// that day are also checked (no overlap with the travel buffer, daily hour cap).
/// </summary>
public class WorkerAvailabilityQuery(AppDbContext dbContext, IGeoService geoService, IOptions<BusinessRules>? rules = null) : IWorkerAvailabilityQuery
{
    private readonly BusinessRules _rules = rules?.Value ?? new BusinessRules();

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
        // - A registered work area (base point WorkLat & WorkLng); a worker without one is never found (decisions D24)
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

               && (double)worker.WorkLat!.Value >= minLat
               && (double)worker.WorkLat!.Value <= maxLat
               && (double)worker.WorkLng!.Value >= minLng
               && (double)worker.WorkLng!.Value <= maxLng
            select new
            {
                worker.WorkerId,
                slot.SlotId,
                slot.StartTime,
                slot.EndTime,
                Lat = (double)worker.WorkLat!.Value,
                Lng = (double)worker.WorkLng!.Value,

                worker.RatingAvg
            }
        ).ToListAsync(cancellationToken);

        // Decisions D34: leave out a worker whose live jobs that day clash with this order's window.
        if (query.OrderId is long orderId && candidateRows.Count > 0)
        {
            var first = candidateRows[0];
            var window = await WorkerJobTimes.OrderWindowAsync(dbContext, orderId, query.ShiftCode, first.StartTime, first.EndTime, cancellationToken);
            if (window is { } wanted)
            {
                var jobs = await WorkerJobTimes.LoadAsync(dbContext, candidateRows.Select(c => c.WorkerId).Distinct().ToList(), query.Date, cancellationToken);
                candidateRows = candidateRows.Where(c => WorkerJobTimes.Fits(jobs.GetValueOrDefault(c.WorkerId), wanted, _rules)).ToList();
            }
        }

        // Compute exact Haversine distance and keep the workers inside the search radius step
        var availableWorkers = new List<AvailableWorker>();

        foreach (var c in candidateRows)
        {
            var workerLocation = new GeoPoint(c.Lat, c.Lng);
            double distanceMeters = geoService.DistanceMeters(query.Location, workerLocation);
            double distanceKm = distanceMeters / 1000.0;

            if (distanceKm <= query.RadiusKm)
            {
                availableWorkers.Add(new AvailableWorker(c.WorkerId, c.SlotId, distanceKm, c.Lat, c.Lng));
            }
        }

        // Sort by DistanceKm ascending and return up to Limit
        return availableWorkers
            .OrderBy(w => w.DistanceKm)
            .Take(query.Limit)
            .ToList();
    }
}
