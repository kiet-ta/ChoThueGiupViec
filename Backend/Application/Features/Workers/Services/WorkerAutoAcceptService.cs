using CommonService.Application.Common.Models;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Workers.Services;

/// <summary>Error codes of <see cref="IWorkerAutoAcceptService"/> (409, <c>data.code</c>).</summary>
public static class WorkerAutoAcceptErrorCodes
{
    public const string NotFreelancer = "NOT_FREELANCER";
    public const string WorkAreaRequired = "WORK_AREA_REQUIRED";
}

public class WorkerAutoAcceptService(
    IWorkerRepository workerRepository,
    ICurrentUser currentUser,
    IClock clock) : IWorkerAutoAcceptService
{
    public async Task<ApiResponse<WorkerAutoAcceptResponse>> GetAsync(CancellationToken ct = default)
    {
        var worker = await FindWorkerAsync(ct);
        return ApiResponse<WorkerAutoAcceptResponse>.Ok(ToResponse(worker), "Retrieved auto-accept setting successfully.");
    }

    public async Task<ApiResponse<WorkerAutoAcceptResponse>> SaveAsync(UpdateWorkerAutoAcceptRequest? request, CancellationToken ct = default)
    {
        if (currentUser.UserId is not int)
        {
            throw new ForbiddenAccessException("Authenticated worker user ID is required.");
        }

        if (request?.Enabled is not bool enabled)
        {
            throw new ValidationException("enabled", "enabled is required (true or false).");
        }

        var worker = await FindWorkerAsync(ct);
        if (worker.WorkerType != WorkerType.Freelancer)
        {
            throw new BusinessRuleViolationException(
                "Chỉ thợ tự do mới dùng được tự động nhận đơn.", WorkerAutoAcceptErrorCodes.NotFreelancer);
        }

        if (enabled && !HasWorkArea(worker))
        {
            throw new BusinessRuleViolationException(
                "Bạn cần đăng ký khu vực làm việc trước khi bật tự động nhận đơn.", WorkerAutoAcceptErrorCodes.WorkAreaRequired);
        }

        if (worker.AutoAcceptOffers != enabled)
        {
            worker.AutoAcceptOffers = enabled;
            worker.UpdatedAt = clock.UtcNow;
            workerRepository.Update(worker);
            await workerRepository.SaveChangesAsync(ct);
        }

        return ApiResponse<WorkerAutoAcceptResponse>.Ok(
            ToResponse(worker), enabled ? "Auto-accept turned on." : "Auto-accept turned off.");
    }

    private static bool HasWorkArea(Worker worker) =>
        worker.WorkLat is not null && worker.WorkLng is not null && worker.WorkRadiusKm is not null;

    private async Task<Worker> FindWorkerAsync(CancellationToken ct)
    {
        if (currentUser.UserId is not int workerId)
        {
            throw new ForbiddenAccessException("Authenticated worker user ID is required.");
        }

        return await workerRepository.GetByIdAsync(workerId, ct)
            ?? throw new NotFoundException("Worker profile not found.");
    }

    private static WorkerAutoAcceptResponse ToResponse(Worker worker) =>
        new(worker.WorkerType == WorkerType.Freelancer && worker.AutoAcceptOffers);
}
