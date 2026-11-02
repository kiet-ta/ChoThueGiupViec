namespace CommonService.Application.Features.Workers.Dtos;

/// <summary>Body of <c>PUT /api/workers/me/location</c>: where the worker is now (decisions D19).</summary>
public record UpdateWorkerLocationRequest(double? Latitude, double? Longitude);

public record WorkerLocationResponse(double Latitude, double Longitude);
