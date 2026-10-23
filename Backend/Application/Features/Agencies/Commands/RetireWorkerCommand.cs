using CommonService.Application.Features.Agencies.Dtos;
using MediatR;

namespace CommonService.Application.Features.Agencies.Commands;

/// <summary>
/// Command to retire an agency worker, optionally with a replacement.
/// </summary>
public sealed record RetireWorkerCommand(
    int WorkerId,
    RetireWorkerRequest Request
) : IRequest<RetireWorkerResponse>;