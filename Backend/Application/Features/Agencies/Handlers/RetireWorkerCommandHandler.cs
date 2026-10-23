using CommonService.Application.Features.Agencies.Commands;
using CommonService.Application.Features.Agencies.Dtos;
using CommonService.Application.Features.Agencies;
using MediatR;

namespace CommonService.Application.Features.Agencies.Handlers;

/// <summary>
/// Handler for RetireWorkerCommand.
/// </summary>
public sealed class RetireWorkerCommandHandler(
    WorkerRetirementService retirementService) : IRequestHandler<RetireWorkerCommand, RetireWorkerResponse>
{
    public async Task<RetireWorkerResponse> Handle(RetireWorkerCommand request, CancellationToken cancellationToken)
    {
        return await retirementService.RetireWorkerAsync(request.WorkerId, request.Request, cancellationToken);
    }
}