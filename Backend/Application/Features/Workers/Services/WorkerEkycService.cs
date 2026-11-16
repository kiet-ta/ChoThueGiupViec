using CommonService.Application.Common.Models;
using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Workers.Services;

public class WorkerEkycService(
    IWorkerRepository workerRepository,
    IEkycProvider ekycProvider,
    ICurrentUser currentUser,
    IOptions<BusinessRules> rulesOptions) : IWorkerEkycService
{
    private const int MaxPhotoUrlLength = 500; // WORKER.ekyc_front_cccd_url, ekyc_back_cccd_url, ekyc_selfie_url

    private readonly BusinessRules _rules = rulesOptions.Value;

    public async Task<ApiResponse<EkycResultResponse>> SubmitAsync(SubmitEkycRequest request, CancellationToken ct = default)
    {
        if (currentUser.UserId is not int workerId)
        {
            throw new ForbiddenAccessException("Authenticated worker user ID is required.");
        }

        var worker = await workerRepository.GetByIdAsync(workerId, ct);
        if (worker == null)
        {
            throw new NotFoundException("Worker profile not found.");
        }

        if (string.IsNullOrWhiteSpace(request.FrontCccdUrl) || string.IsNullOrWhiteSpace(request.BackCccdUrl) || string.IsNullOrWhiteSpace(request.SelfieUrl))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "ekycPhotos", ["Front CCCD, Back CCCD, and Selfie photo URLs are required."] }
            });
        }

        var (frontUrl, backUrl, selfieUrl) = ValidatedPhotoUrls(request);

        // 1. Call eKYC provider (Phase 1 Fake provider)
        var ekycReq = new EkycRequest(worker.NationalId, frontUrl, selfieUrl);
        var result = await ekycProvider.VerifyAsync(ekycReq, ct);

        // 2. Evaluate auto-approval threshold (Confidence >= 85.00% and no FraudFlag)
        bool autoApproved = !result.FraudFlag && result.Confidence >= _rules.Ekyc.AutoApproveConfidence;
        string newKycStatus = autoApproved ? "APPROVED" : "MANUAL_REVIEW";

        // The three addresses are kept with the result: the Admin review shows them (ADMIN-UAT-15).
        worker.SetKycResult(result.Confidence, newKycStatus, frontUrl, backUrl, selfieUrl);
        workerRepository.Update(worker);
        await workerRepository.SaveChangesAsync(ct);

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

    public async Task<ApiResponse<EkycStatusResponse>> GetStatusAsync(CancellationToken ct = default)
    {
        if (currentUser.UserId is not int workerId)
        {
            throw new ForbiddenAccessException("Authenticated worker user ID is required.");
        }

        var worker = await workerRepository.GetByIdAsync(workerId, ct);
        if (worker == null)
        {
            throw new NotFoundException("Worker profile not found.");
        }

        var response = new EkycStatusResponse(
            worker.WorkerId,
            worker.KycStatus,
            worker.EkycConfidence,
            worker.UpdatedAt,
            null
        );

        return ApiResponse<EkycStatusResponse>.Ok(response);
    }

    public async Task<ApiResponse<EkycQueuePagedResponse>> GetQueueAsync(int page, int pageSize, string status, CancellationToken ct = default)
    {
        if (currentUser.Role != UserRole.Admin)
        {
            throw new ForbiddenAccessException("Only Admin users can view the eKYC review queue.");
        }

        var queueStatus = string.IsNullOrWhiteSpace(status) ? EkycQueueStatus.Pending : status.Trim().ToUpperInvariant();
        if (!EkycQueueStatus.All.Contains(queueStatus))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["status"] = [$"Status must be one of: {string.Join(", ", EkycQueueStatus.All)}."],
            });
        }

        var (workers, totalCount) = await workerRepository.GetEkycQueueAsync(page, pageSize, queueStatus, ct);

        var queueItems = workers.Select(w => new EkycQueueItemResponse(
            w.WorkerId,
            w.FullName,
            w.PhoneNumber,
            w.NationalId,
            w.EkycFrontCccdUrl,
            w.EkycBackCccdUrl,
            w.EkycSelfieUrl,
            w.EkycConfidence,
            w.KycStatus,
            w.CompletedJobs,
            w.KycStatus == "AUDIT_PENDING" || w.CompletedJobs <= 5,
            w.UpdatedAt
        )).ToList();

        var pagedResponse = new EkycQueuePagedResponse(queueItems, totalCount, page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize);
        return ApiResponse<EkycQueuePagedResponse>.Ok(pagedResponse, "Retrieved eKYC queue successfully.");
    }

    public async Task<ApiResponse<EkycResultResponse>> ReviewAsync(int workerId, ReviewEkycRequest request, CancellationToken ct = default)
    {
        if (currentUser.Role != UserRole.Admin)
        {
            throw new ForbiddenAccessException("Only Admin users can perform eKYC manual reviews.");
        }

        if (currentUser.UserId is not int adminId)
        {
            throw new ForbiddenAccessException("Admin user ID is required.");
        }

        var worker = await workerRepository.GetByIdAsync(workerId, ct);
        if (worker == null)
        {
            throw new NotFoundException($"Worker profile with ID {workerId} not found.");
        }

        worker.ReviewKyc(request.Approved, adminId);
        workerRepository.Update(worker);
        await workerRepository.SaveChangesAsync(ct);

        var response = new EkycResultResponse(
            worker.WorkerId,
            worker.EkycConfidence ?? 0m,
            worker.KycStatus,
            false,
            worker.UpdatedAt,
            request.Approved ? null : (request.RejectionReason ?? "Manual review rejected by Admin.")
        );

        string message = request.Approved
            ? $"eKYC manually approved for worker #{worker.WorkerId}."
            : $"eKYC manually rejected for worker #{worker.WorkerId}. Account locked.";

        return ApiResponse<EkycResultResponse>.Ok(response, message);
    }

    /// <summary>
    /// The three photo addresses of a submit, trimmed (ADMIN-UAT-15). Each must be an absolute <c>http</c> or <c>https</c> address or a
    /// root-relative path, at most 500 characters (WORKER.ekyc_*_url), without spaces; anything else (<c>javascript:</c>, <c>data:</c>, ...)
    /// is a 400 naming the field, because the Admin page puts the address in an <c>img</c> tag.
    /// </summary>
    private static (string Front, string Back, string Selfie) ValidatedPhotoUrls(SubmitEkycRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var front = CheckPhotoUrl(request.FrontCccdUrl, "frontCccdUrl", errors);
        var back = CheckPhotoUrl(request.BackCccdUrl, "backCccdUrl", errors);
        var selfie = CheckPhotoUrl(request.SelfieUrl, "selfieUrl", errors);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        return (front, back, selfie);
    }

    private static string CheckPhotoUrl(string raw, string field, Dictionary<string, string[]> errors)
    {
        var url = raw.Trim();
        if (url.Length > MaxPhotoUrlLength)
        {
            errors[field] = [$"The photo address must be at most {MaxPhotoUrlLength} characters."];
        }
        else if (!IsSafePhotoUrl(url))
        {
            errors[field] = ["The photo address must be an http(s) address or a path starting with '/', without spaces."];
        }

        return url;
    }

    public static bool IsSafePhotoUrl(string url)
    {
        if (url.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            return false;
        }

        if (url.StartsWith('/'))
        {
            return !url.StartsWith("//", StringComparison.Ordinal) && !url.Contains('\\'); // "//host" would load from another site
        }

        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && uri.Host.Length > 0;
    }
}
