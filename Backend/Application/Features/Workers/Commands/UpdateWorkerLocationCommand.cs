using CommonService.Application.Common.Models;
using CommonService.Application.Exceptions;
using CommonService.Application.Interfaces.Ports;
using MediatR;

namespace CommonService.Application.Features.Workers.Commands;

/// <summary>Body of <c>PUT /api/workers/me/location</c>: where the worker is now (decisions D19).</summary>
public sealed record UpdateWorkerLocationRequest(double? Latitude, double? Longitude);

public sealed record WorkerLocationResponse(double Latitude, double Longitude);

public sealed record UpdateWorkerLocationCommand(UpdateWorkerLocationRequest? Request) : IRequest<ApiResponse<WorkerLocationResponse>>;

/// <summary>
/// Stores the worker's position in <c>WORKER.current_lat / current_lng</c>. The dispatch search only offers a job to a worker who has one
/// (<c>WorkerAvailabilityQuery</c>), and nothing wrote it before, so no freelancer could ever be found (ECON-FLOW-FIX).
/// </summary>
public sealed class UpdateWorkerLocationCommandHandler(
    IWorkerRepository workerRepository,
    ICurrentUser currentUser,
    IClock clock)
    : IRequestHandler<UpdateWorkerLocationCommand, ApiResponse<WorkerLocationResponse>>
{
    // WORKER.current_lat / current_lng are DECIMAL(9,6): six decimals (about 10 cm) are all that is kept.
    private const int Decimals = 6;

    public async Task<ApiResponse<WorkerLocationResponse>> Handle(UpdateWorkerLocationCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not int workerId)
        {
            throw new ForbiddenAccessException("Authenticated worker user ID is required.");
        }

        var request = command.Request ?? throw new ValidationException("request", "Request body is required.");
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (request.Latitude is not double lat || double.IsNaN(lat) || lat < -90 || lat > 90)
        {
            errors["latitude"] = ["latitude must be a number from -90 to 90."];
        }

        if (request.Longitude is not double lng || double.IsNaN(lng) || lng < -180 || lng > 180)
        {
            errors["longitude"] = ["longitude must be a number from -180 to 180."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var worker = await workerRepository.GetByIdAsync(workerId, cancellationToken)
            ?? throw new NotFoundException("Worker profile not found.");

        var latitude = Math.Round((decimal)request.Latitude!.Value, Decimals);
        var longitude = Math.Round((decimal)request.Longitude!.Value, Decimals);
        worker.CurrentLat = latitude;
        worker.CurrentLng = longitude;
        worker.UpdatedAt = clock.UtcNow;
        workerRepository.Update(worker);
        await workerRepository.SaveChangesAsync(cancellationToken);

        return ApiResponse<WorkerLocationResponse>.Ok(new WorkerLocationResponse((double)latitude, (double)longitude), "Location saved.");
    }
}
