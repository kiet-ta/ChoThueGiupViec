using CommonService.Application.Common.Models;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;

namespace CommonService.Application.Features.Workers.Services;

public class WorkerWorkAreaService(
    IWorkerRepository workerRepository,
    ICurrentUser currentUser,
    IClock clock,
    IWardCatalog wards) : IWorkerWorkAreaService
{
    /// <summary>The radii a worker may choose, in km.</summary>
    public static readonly int[] AllowedRadiiKm = [5, 10, 15];

    /// <summary>At most this many wards (<c>WORKER.work_districts</c> is NVARCHAR(500): ten codes of five digits fit).</summary>
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

        var chosen = body.WardCodes is not null ? ChooseByCodes(body.WardCodes, errors) : ChooseByNames(body.Districts, errors);

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var worker = await FindWorkerAsync(ct);
        worker.WorkLat = Math.Round((decimal)body.Latitude!.Value, LocationDecimals);
        worker.WorkLng = Math.Round((decimal)body.Longitude!.Value, LocationDecimals);
        worker.WorkRadiusKm = (byte)body.RadiusKm!.Value;
        // Decisions D40: the codes of the wards, in the order the worker chose them.
        worker.WorkDistricts = chosen.Count == 0 ? null : string.Join(DistrictSeparator, chosen.Select(w => w.Code));
        var now = clock.UtcNow;
        worker.WorkAreaSetAt = now;
        worker.UpdatedAt = now;
        workerRepository.Update(worker);
        await workerRepository.SaveChangesAsync(ct);

        return ApiResponse<WorkerWorkAreaResponse>.Ok(ToResponse(worker), "Work area saved.");
    }

    /// <summary>The wards for <c>wardCodes</c>: at most 10, no repeat, every code a ward of the bundled list (errors under <c>wardCodes</c>).</summary>
    private List<WardInfo> ChooseByCodes(IReadOnlyList<int> codes, Dictionary<string, string[]> errors)
    {
        var chosen = new List<WardInfo>();
        if (codes.Count > MaxDistricts)
        {
            errors["wardCodes"] = [$"At most {MaxDistricts} wards."];
        }
        else if (codes.Distinct().Count() != codes.Count)
        {
            errors["wardCodes"] = ["Each ward may be chosen once."];
        }
        else
        {
            foreach (var code in codes)
            {
                var ward = wards.FindByCode(code);
                if (ward is null)
                {
                    errors["wardCodes"] = [$"{code} is not a ward code."];
                    break;
                }

                chosen.Add(ward);
            }
        }

        return chosen;
    }

    /// <summary>The old request form: <c>districts</c> names. Each must stand for exactly one ward (a current or a pre-merger name).</summary>
    private List<WardInfo> ChooseByNames(IReadOnlyList<string>? names, Dictionary<string, string[]> errors)
    {
        var chosen = new List<WardInfo>();
        var list = (names ?? []).Select(n => n?.Trim() ?? string.Empty).ToList();
        if (list.Count > MaxDistricts)
        {
            errors["districts"] = [$"At most {MaxDistricts} districts."];
            return chosen;
        }

        foreach (var name in list)
        {
            var ward = name.Length is > 0 and <= MaxDistrictLength ? wards.ResolveName(name) : null;
            if (ward is null)
            {
                errors["districts"] = [$"'{name}' is not exactly one ward; send wardCodes."];
                return [];
            }

            if (chosen.All(w => w.Code != ward.Code))
            {
                chosen.Add(ward);
            }
        }

        return chosen;
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

    private WorkerWorkAreaResponse ToResponse(Worker worker)
    {
        var isSet = worker.WorkLat is not null && worker.WorkLng is not null && worker.WorkRadiusKm is not null;
        if (!isSet)
        {
            return new WorkerWorkAreaResponse(false, null, null, null, [], null, [], []);
        }

        var (stored, unresolved) = ReadStored(worker.WorkDistricts);
        return new WorkerWorkAreaResponse(
            true,
            (double)worker.WorkLat!.Value,
            (double)worker.WorkLng!.Value,
            worker.WorkRadiusKm,
            stored.Select(w => w.Name).ToList(),
            worker.WorkAreaSetAt,
            stored.Select(w => new WorkerWardDto(w.Code, w.Name, w.ProvinceCode, w.ProvinceName)).ToList(),
            unresolved);
    }

    /// <summary>
    /// What <c>WORKER.work_districts</c> holds: codes (decisions D40) or, in rows saved before, ward names. A name is turned into
    /// the ward it stands for now (a pre-merger name resolves to the ward it was merged into); one that stands for no ward or
    /// for several is returned in the second list. Repeats collapse. Reading never writes: the next save stores codes.
    /// </summary>
    private (List<WardInfo> Wards, List<string> Unresolved) ReadStored(string? stored)
    {
        var found = new List<WardInfo>();
        var unresolved = new List<string>();
        foreach (var token in (stored ?? string.Empty).Split(DistrictSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var ward = int.TryParse(token, out var code) ? wards.FindByCode(code) : wards.ResolveName(token);
            if (ward is null)
            {
                unresolved.Add(token);
            }
            else if (found.All(w => w.Code != ward.Code))
            {
                found.Add(ward);
            }
        }

        return (found, unresolved);
    }
}
