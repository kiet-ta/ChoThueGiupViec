using CommonService.Application.Common.Models;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using MediatR;

namespace CommonService.Application.Features.Workers.Commands;

public sealed record ReviewEkycCommand(int WorkerId, ReviewEkycRequest Request) : IRequest<ApiResponse<EkycResultResponse>>;

public sealed class ReviewEkycCommandHandler(
    IWorkerRepository workerRepository,
    ICurrentUser currentUser)
    : IRequestHandler<ReviewEkycCommand, ApiResponse<EkycResultResponse>>
{
    public async Task<ApiResponse<EkycResultResponse>> Handle(ReviewEkycCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.Role != UserRole.Admin)
        {
            throw new ForbiddenAccessException("Only Admin users can perform eKYC manual reviews.");
        }


        if (currentUser.UserId is not int adminId)
        {
            throw new ForbiddenAccessException("Admin user ID is required.");
        }

        var worker = await workerRepository.GetByIdAsync(command.WorkerId, cancellationToken);
        if (worker == null)
        {
            throw new NotFoundException($"Worker profile with ID {command.WorkerId} not found.");
        }

        var req = command.Request;
        worker.ReviewKyc(req.Approved, adminId);
        workerRepository.Update(worker);
        await workerRepository.SaveChangesAsync(cancellationToken);

        var response = new EkycResultResponse(
            worker.WorkerId,
            worker.EkycConfidence ?? 0m,
            worker.KycStatus,
            false,
            worker.UpdatedAt,
            req.Approved ? null : (req.RejectionReason ?? "Manual review rejected by Admin.")
        );

        string message = req.Approved
            ? $"eKYC manually approved for worker #{worker.WorkerId}."
            : $"eKYC manually rejected for worker #{worker.WorkerId}. Account locked.";

        return ApiResponse<EkycResultResponse>.Ok(response, message);
    }
}
