using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CommonService.Infrastructure.Modules.Identity;

/// <summary>
/// Seeds the initial development admin account on startup (overview §10).
/// Strictly runs in Development only; never logs the plain-text password.
/// </summary>
public sealed class AdminSeeder
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<AdminSeeder> _logger;

    public AdminSeeder(
        IConfiguration configuration,
        IHostEnvironment environment,
        IPasswordHasher passwordHasher,
        ILogger<AdminSeeder> logger)
    {
        _configuration = configuration;
        _environment = environment;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task SeedAsync(AppDbContext context, CancellationToken cancellationToken = default)
    {
        // 00-overview.md §10: Chỉ Development: môi trường khác (Staging/Production) không bao giờ chạy seeder
        if (!_environment.IsDevelopment())
        {
            _logger.LogInformation("AdminSeeder: Skipping because environment is '{Env}' (Development only).", _environment.EnvironmentName);
            return;
        }

        var email = _configuration["Seed:Admin:Email"] ?? "admin@dev.local";
        var password = _configuration["Seed:Admin:Password"] ?? "Admin@123456";
        var fullName = _configuration["Seed:Admin:FullName"] ?? "Dev Admin";

        if (string.IsNullOrWhiteSpace(email))
        {
            _logger.LogWarning("AdminSeeder: Seed:Admin:Email is empty, skipping admin seed.");
            return;
        }

        // Idempotent check: if an admin with that email exists, do nothing (do not change password, do not create duplicate)
        var exists = await context.Admins.AnyAsync(a => a.Email == email, cancellationToken);
        if (exists)
        {
            _logger.LogInformation("AdminSeeder: Admin account '{Email}' already exists. Skipping duplicate creation.", email);
            return;
        }

        var admin = new AdminAccount
        {
            Email = email,
            FullName = fullName,
            PasswordHash = _passwordHasher.Hash(password),
            AdminRole = "SUPER_ADMIN",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            FailedLoginCount = 0,
            LockedUntil = null
        };

        context.Admins.Add(admin);
        await context.SaveChangesAsync(cancellationToken);

        // 00-overview.md §10: Log chỉ ghi email, không log mật khẩu.
        _logger.LogInformation("AdminSeeder: Seeded initial dev admin account '{Email}' with role '{Role}'.", email, admin.AdminRole);
    }
}
