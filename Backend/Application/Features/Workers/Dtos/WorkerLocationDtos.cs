namespace CommonService.Application.Features.Workers.Dtos;

/// <summary>Body of <c>PUT /api/workers/me/location</c>: where the worker is now (decisions D19).</summary>
public sealed record UpdateWorkerLocationRequest(double? Latitude, double? Longitude);

public sealed record WorkerLocationResponse(double Latitude, double Longitude);
