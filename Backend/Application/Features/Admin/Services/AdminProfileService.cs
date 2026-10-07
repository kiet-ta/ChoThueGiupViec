using CommonService.Application.Features.Admin.Dtos;
using CommonService.Domain.Entities;

namespace CommonService.Application.Features.Admin.Services;

public sealed class AdminProfileResult
{
    public bool Success { get; init; }
    public int StatusCode { get; init; }
    public string? ErrorMessage { get; init; }
    public AdminProfileDto? Data { get; init; }

    public static AdminProfileResult Ok(AdminProfileDto data) => new() { Success = true, StatusCode = 200, Data = data };

    public static AdminProfileResult NotFound() => new() { Success = false, StatusCode = 404, ErrorMessage = "Admin not found." };
}

public interface IAdminProfileService
{
    Task<AdminProfileResult> GetAsync(int adminId, CancellationToken cancellationToken = default);
}

/// <summary>GET /api/admin/me (contract admin.md section 2.1, BE-M6-06a).</summary>
public sealed class AdminProfileService(IAdminProfileReader admins) : IAdminProfileService
{
    public async Task<AdminProfileResult> GetAsync(int adminId, CancellationToken cancellationToken = default)
    {
        var admin = await admins.FindAsync(adminId, cancellationToken);
        return admin is null ? AdminProfileResult.NotFound() : AdminProfileResult.Ok(ToDto(admin));
    }

    private static AdminProfileDto ToDto(AdminAccount admin) => new()
    {
        AdminId = admin.AdminId,
        Email = admin.Email,
        FullName = admin.FullName,
        AdminRole = admin.AdminRole,
        IsActive = admin.IsActive,
        CreatedAt = DateTime.SpecifyKind(admin.CreatedAt, DateTimeKind.Utc),
    };
}
