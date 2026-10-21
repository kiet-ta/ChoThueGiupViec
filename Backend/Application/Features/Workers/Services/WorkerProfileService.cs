using CommonService.Application.Common.Models;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using Microsoft.Extensions.Configuration;

namespace CommonService.Application.Features.Workers.Services;

public sealed class WorkerProfileService(
    IWorkerRepository workerRepository,
    ICurrentUser currentUser,
    IClock clock,
    IConfiguration configuration) : IWorkerProfileService
{
    // WORKER.current_lat / current_lng are DECIMAL(9,6): six decimals (about 10 cm) are all that is kept.
    private const int LocationDecimals = 6;

    public async Task<ApiResponse<WorkerProfileResponse>> RegisterAsync(RegisterWorkerRequest request, CancellationToken ct = default)
    {
        // 1. Validate registration token
        var phoneNumber = RegistrationTokenValidator.ValidateRegistrationToken(request.RegistrationToken, configuration);
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "registrationToken", ["Invalid or expired worker registration token."] }
            });
        }

        // 2. Validate inputs
        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "fullName", ["Full name is required."] }
            });
        }

        var trimmedNationalId = request.NationalId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedNationalId) || trimmedNationalId.Length != 12 || !trimmedNationalId.All(char.IsDigit))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "nationalId", ["National ID (CCCD) must be exactly 12 digits."] }
            });
        }

        // 3. Check for existing worker with same phone number or national ID
        var exists = await workerRepository.ExistsByPhoneOrNationalIdAsync(phoneNumber, trimmedNationalId, ct);
        if (exists)
        {
            throw new BusinessRuleViolationException("A worker with this phone number or national ID is already registered.");
        }

        // 4. Create worker entity (Freelancer STI, WorkStatus = PENDING, KycStatus = PENDING)
        var worker = Worker.CreateFreelancer(phoneNumber, trimmedNationalId, request.FullName.Trim());
        worker.KycStatus = "PENDING";
        worker.RatingAvg = 5.00m;
        worker.CompletedJobs = 0;
        worker.CreatedAt = DateTime.UtcNow;
        worker.UpdatedAt = DateTime.UtcNow;

        await workerRepository.AddAsync(worker, ct);
        await workerRepository.SaveChangesAsync(ct);

        return ApiResponse<WorkerProfileResponse>.Ok(ToProfileResponse(worker), "Worker registered successfully.");
    }

    public async Task<ApiResponse<WorkerProfileResponse>> GetProfileAsync(CancellationToken ct = default)
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

        return ApiResponse<WorkerProfileResponse>.Ok(ToProfileResponse(worker));
    }

    public async Task<ApiResponse<WorkerProfileResponse>> UpdateProfileAsync(UpdateWorkerProfileRequest request, CancellationToken ct = default)
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

        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "fullName", ["Full name cannot be empty."] }
            });
        }

        worker.UpdateProfile(request.FullName);
        workerRepository.Update(worker);
        await workerRepository.SaveChangesAsync(ct);

        return ApiResponse<WorkerProfileResponse>.Ok(ToProfileResponse(worker), "Profile updated successfully.");
    }

    public async Task<ApiResponse<WorkerPublicProfileResponse>> GetByIdAsync(int workerId, CancellationToken ct = default)
    {
        var worker = await workerRepository.GetByIdAsync(workerId, ct);
        if (worker == null)
        {
            throw new NotFoundException("Worker not found.");
        }

        var response = new WorkerPublicProfileResponse(
            worker.WorkerId,
            worker.FullName,
            null,
            worker.WorkerType.ToString().ToUpperInvariant(),
            worker.WorkStatus.ToString().ToUpperInvariant(),
            worker.KycStatus,
            worker.RatingAvg,
            worker.CompletedJobs,
            worker.IsSuperFreelancer
        );

        return ApiResponse<WorkerPublicProfileResponse>.Ok(response);
    }

    public async Task<ApiResponse<WorkerBankResponse>> GetBankAsync(CancellationToken ct = default)
    {
        if (currentUser.UserId is not int workerId)
        {
            throw new ForbiddenAccessException("Authenticated worker user ID is required.");
        }

        var worker = await workerRepository.GetByIdAsync(workerId, ct)
            ?? throw new NotFoundException("Worker profile not found.");
        var name = worker.BankName ?? string.Empty;
        var account = worker.BankAccountNo ?? string.Empty;
        return ApiResponse<WorkerBankResponse>.Ok(new WorkerBankResponse(name, account, name.Length > 0 && account.Length > 0));
    }

    public async Task<ApiResponse<WorkerBankResponse>> UpdateBankAsync(UpdateWorkerBankRequest? request, CancellationToken ct = default)
    {
        if (currentUser.UserId is not int workerId)
        {
            throw new ForbiddenAccessException("Authenticated worker user ID is required.");
        }

        var body = request ?? throw new ValidationException("request", "Request body is required.");
        var (name, account) = WorkerBankValidator.Validate(body.BankName, body.BankAccountNo);

        var worker = await workerRepository.GetByIdAsync(workerId, ct)
            ?? throw new NotFoundException("Worker profile not found.");
        worker.BankName = name;
        worker.BankAccountNo = account;
        worker.UpdatedAt = clock.UtcNow;
        workerRepository.Update(worker);
        await workerRepository.SaveChangesAsync(ct);

        return ApiResponse<WorkerBankResponse>.Ok(new WorkerBankResponse(name, account, true), "Bank account saved.");
    }

    /// <summary>
    /// Stores the worker's position in <c>WORKER.current_lat / current_lng</c>. The dispatch search only offers a job to a worker who has one
    /// (<c>WorkerAvailabilityQuery</c>), and nothing wrote it before, so no freelancer could ever be found (ECON-FLOW-FIX).
    /// </summary>
    public async Task<ApiResponse<WorkerLocationResponse>> UpdateLocationAsync(UpdateWorkerLocationRequest? request, CancellationToken ct = default)
    {
        if (currentUser.UserId is not int workerId)
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

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var worker = await workerRepository.GetByIdAsync(workerId, ct)
            ?? throw new NotFoundException("Worker profile not found.");

        var latitude = Math.Round((decimal)body.Latitude!.Value, LocationDecimals);
        var longitude = Math.Round((decimal)body.Longitude!.Value, LocationDecimals);
        worker.CurrentLat = latitude;
        worker.CurrentLng = longitude;
        worker.UpdatedAt = clock.UtcNow;
        workerRepository.Update(worker);
        await workerRepository.SaveChangesAsync(ct);

        return ApiResponse<WorkerLocationResponse>.Ok(new WorkerLocationResponse((double)latitude, (double)longitude), "Location saved.");
    }

    private static WorkerProfileResponse ToProfileResponse(Worker worker) => new(
        worker.WorkerId,
        worker.PhoneNumber,
        worker.FullName,
        worker.NationalId,
        worker.WorkerType.ToString().ToUpperInvariant(),
        worker.AgencyId,
        worker.WorkStatus.ToString().ToUpperInvariant(),
        worker.KycStatus,
        worker.RatingAvg,
        worker.CompletedJobs,
        worker.IsSuperFreelancer,
        worker.CreatedAt
    );
}
