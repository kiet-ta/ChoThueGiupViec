using CommonService.Application.Common.Models;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;

namespace CommonService.Application.Features.Workers.Services;

public sealed class WorkerWorkAreaService(
    IWorkerRepository workerRepository,
    ICurrentUser currentUser,
    IClock clock) : IWorkerWorkAreaService
{
    /// <summary>The radii a worker may choose, in km.</summary>
    public static readonly int[] AllowedRadiiKm = [5, 10, 15];

    public const int MaxDistricts = 10;
    public const int MaxDistrictLength = 100;
    public const char DistrictSeparator = ';';

    // WORKER.work_lat / work_lng are DECIMAL(9,6), like current_lat / current_lng.
    private const int LocationDecimals = 6;

    public async Task<ApiResponse<WorkerWorkAreaResponse>> GetAsync(CancellationToken ct = default)
    {
        var worker = await FindWorkerAsync(ct);
        return ApiResponse<WorkerWorkAreaResponse>.Ok(ToResponse(worker), "Retrieved work area successfully.");
    }

    public async Task<ApiResponse<WorkerWorkAreaResponse>> SaveAsync(UpdateWorkerWorkAreaRequest? request, CancellationToken ct = default)
    {
        if (currentUser.UserId is not int)
        {
            throw new ForbiddenAccessException("Authenticated worker user ID is required.");
        }

        var body = request ?? throw new ValidationException("request", "Request body is required.");
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (body.Latitude is not double lat || double.IsNaN(lat) || lat < -90 || lat > 90)
        {
            errors["latitude"] = ["latitude must be a number from -90 to 90."];
        }

        if (body.Longitude is not double lng || double.IsNaN(lng) || lng < -180 || lng > 180)
        {
            errors["longitude"] = ["longitude must be a number from -180 to 180."];
        }

        if (body.RadiusKm is not int radius || !AllowedRadiiKm.Contains(radius))
        {
            errors["radiusKm"] = [$"radiusKm must be one of {string.Join(", ", AllowedRadiiKm)}."];
        }

        var districts = (body.Districts ?? []).Select(d => d?.Trim() ?? string.Empty).ToList();
        if (districts.Count > MaxDistricts)
        {
            errors["districts"] = [$"At most {MaxDistricts} districts."];
        }
        else if (districts.Any(d => d.Length == 0 || d.Length > MaxDistrictLength || d.Contains(DistrictSeparator)))
        {
            errors["districts"] = [$"Each district must be 1 to {MaxDistrictLength} characters and must not contain '{DistrictSeparator}'."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var worker = await FindWorkerAsync(ct);
        worker.WorkLat = Math.Round((decimal)body.Latitude!.Value, LocationDecimals);
        worker.WorkLng = Math.Round((decimal)body.Longitude!.Value, LocationDecimals);
        worker.WorkRadiusKm = (byte)body.RadiusKm!.Value;
        worker.WorkDistricts = districts.Count == 0 ? null : string.Join(DistrictSeparator, districts.Distinct(StringComparer.OrdinalIgnoreCase));
        var now = clock.UtcNow;
        worker.WorkAreaSetAt = now;
        worker.UpdatedAt = now;
        workerRepository.Update(worker);
        await workerRepository.SaveChangesAsync(ct);

        return ApiResponse<WorkerWorkAreaResponse>.Ok(ToResponse(worker), "Work area saved.");
    }

    private async Task<Worker> FindWorkerAsync(CancellationToken ct)
    {
        if (currentUser.UserId is not int workerId)
        {
            throw new ForbiddenAccessException("Authenticated worker user ID is required.");
        }

        return await workerRepository.GetByIdAsync(workerId, ct)
            ?? throw new NotFoundException("Worker profile not found.");
    }

    private static WorkerWorkAreaResponse ToResponse(Worker worker)
    {
        var isSet = worker.WorkLat is not null && worker.WorkLng is not null && worker.WorkRadiusKm is not null;
        if (!isSet)
        {
            return new WorkerWorkAreaResponse(false, null, null, null, [], null);
        }

        var districts = string.IsNullOrEmpty(worker.WorkDistricts)
            ? []
            : worker.WorkDistricts.Split(DistrictSeparator, StringSplitOptions.RemoveEmptyEntries);
        return new WorkerWorkAreaResponse(
            true,
            (double)worker.WorkLat!.Value,
            (double)worker.WorkLng!.Value,
            worker.WorkRadiusKm,
            districts,
            worker.WorkAreaSetAt);
    }
}
