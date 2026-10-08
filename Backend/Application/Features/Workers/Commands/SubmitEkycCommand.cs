using CommonService.Application.Common.Models;
using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Interfaces.Ports;
using MediatR;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Workers.Commands;

public sealed record SubmitEkycCommand(SubmitEkycRequest Request) : IRequest<ApiResponse<EkycResultResponse>>;

public sealed class SubmitEkycCommandHandler(
    IWorkerRepository workerRepository,
    IEkycProvider ekycProvider,
    ICurrentUser currentUser,
    IOptions<BusinessRules> rulesOptions)
    : IRequestHandler<SubmitEkycCommand, ApiResponse<EkycResultResponse>>
{
    private readonly BusinessRules _rules = rulesOptions.Value;

    public async Task<ApiResponse<EkycResultResponse>> Handle(SubmitEkycCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not int workerId)
        {
            throw new ForbiddenAccessException("Authenticated worker user ID is required.");
        }

        var worker = await workerRepository.GetByIdAsync(workerId, cancellationToken);
        if (worker == null)
        {
            throw new NotFoundException("Worker profile not found.");
        }

        var req = command.Request;
        if (string.IsNullOrWhiteSpace(req.FrontCccdUrl) || string.IsNullOrWhiteSpace(req.BackCccdUrl) || string.IsNullOrWhiteSpace(req.SelfieUrl))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "ekycPhotos", ["Front CCCD, Back CCCD, and Selfie photo URLs are required."] }
            });
        }

        // 1. Call eKYC provider (Phase 1 Fake provider)
        var ekycReq = new EkycRequest(worker.NationalId, req.FrontCccdUrl, req.SelfieUrl);
        var result = await ekycProvider.VerifyAsync(ekycReq, cancellationToken);

        // 2. Evaluate auto-approval threshold (Confidence >= 85.00% and no FraudFlag)
        bool autoApproved = !result.FraudFlag && result.Confidence >= _rules.Ekyc.AutoApproveConfidence;
        string newKycStatus = autoApproved ? "APPROVED" : "MANUAL_REVIEW";

        worker.SetKycResult(result.Confidence, newKycStatus);
        workerRepository.Update(worker);
        await workerRepository.SaveChangesAsync(cancellationToken);

        var response = new EkycResultResponse(
            worker.WorkerId,
            result.Confidence,
            worker.KycStatus,
            autoApproved,
            worker.UpdatedAt,
            null
        );

        string message = autoApproved
            ? "eKYC auto-approved successfully. Account activated."
            : "eKYC submitted. Account placed in manual review queue.";

        return ApiResponse<EkycResultResponse>.Ok(response, message);
    }
}
