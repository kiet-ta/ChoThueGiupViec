using System.Net.Mail;
using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Identity.Dtos;
using CommonService.Application.Features.Identity.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CommonService.Infrastructure.Modules.Identity;

/// <summary>
/// Implements Admin / Partner email + password login per contract identity.md §2.3 and decisions Q16, SC-1, SC-8.
/// Lockout state lives in failed_login_count / locked_until on ADMIN and PARTNER_AGENCY (no cache).
/// </summary>
public class PasswordLoginService : IPasswordLoginService
{
    private const int MaxEmailLength = 255;

    /// <summary>Unknown email and wrong password share one response (contract §2.3).</summary>
    private const string InvalidCredentials = "Invalid email or password.";

    private readonly AppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IClock _clock;
    private readonly BusinessRules _rules;
    private readonly ITokenService _tokenService;
    private readonly ILogger<PasswordLoginService> _logger;

    public PasswordLoginService(
        AppDbContext db,
        IPasswordHasher hasher,
        IClock clock,
        IOptions<BusinessRules> rules,
        ITokenService tokenService,
        ILogger<PasswordLoginService> logger)
    {
        _db = db;
        _hasher = hasher;
        _clock = clock;
        _rules = rules.Value;
        _tokenService = tokenService;
        _logger = logger;
    }

    public async Task<AuthResultDto> LoginAsync(
        string email,
        string password,
        string roleString,
        CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string[]>();

        var normalizedEmail = (email ?? string.Empty).Trim();
        if (!IsValidEmail(normalizedEmail))
        {
            errors["email"] = ["A valid email address is required."];
        }

        if (string.IsNullOrEmpty(password))
        {
            errors["password"] = ["Password is required."];
        }

        if (!Enum.TryParse<UserRole>(roleString, ignoreCase: true, out var role) ||
            (role != UserRole.Admin && role != UserRole.Partner))
        {
            errors["role"] = ["Role must be either 'Admin' or 'Partner'."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException("Validation failed", errors);
        }

        return role == UserRole.Admin
            ? await LoginAdminAsync(normalizedEmail, password!, ct)
            : await LoginPartnerAsync(normalizedEmail, password!, ct);
    }

    private async Task<AuthResultDto> LoginAdminAsync(string email, string password, CancellationToken ct)
    {
        var admin = await _db.Admins.FirstOrDefaultAsync(a => a.Email == email, ct);
        if (admin == null)
        {
            throw new UnauthorizedAccessException(InvalidCredentials);
        }

        var now = _clock.UtcNow;
        await CheckCredentialsAsync(
            now,
            admin.PasswordHash,
            password,
            () => (admin.FailedLoginCount, admin.LockedUntil),
            (count, lockedUntil) =>
            {
                admin.FailedLoginCount = count;
                admin.LockedUntil = lockedUntil;
            },
            ct);

        // Only a correct password reveals that the account is disabled (no state enumeration).
        if (!admin.IsActive)
        {
            throw new ForbiddenAccessException("Account is disabled.");
        }

        return await IssueTokensAsync(admin.AdminId, UserRole.Admin, now, ct);
    }

    private async Task<AuthResultDto> LoginPartnerAsync(string email, string password, CancellationToken ct)
    {
        var agency = await _db.PartnerAgencies.FirstOrDefaultAsync(a => a.ContactEmail == email, ct);

        // A partner without a password yet (created by M5 later) cannot log in; same answer as unknown email.
        if (agency == null || string.IsNullOrEmpty(agency.PasswordHash))
        {
            throw new UnauthorizedAccessException(InvalidCredentials);
        }

        var now = _clock.UtcNow;
        await CheckCredentialsAsync(
            now,
            agency.PasswordHash,
            password,
            () => (agency.FailedLoginCount, agency.LockedUntil),
            (count, lockedUntil) =>
            {
                agency.FailedLoginCount = count;
                agency.LockedUntil = lockedUntil;
            },
            ct);

        // A SUSPENDED partner may still log in (decisions Q09, O6): no agency_status check here.
        return await IssueTokensAsync(agency.AgencyId, UserRole.Partner, now, ct);
    }

    /// <summary>
    /// Shared lockout + hash check. Throws on failure (after saving the failure counter); returns when the password is
    /// correct and the failure counter has been reset (saved with the tokens). Order: lock first (a correct password during a lock is still 423),
    /// then hash verification, then failure counting.
    /// </summary>
    private async Task CheckCredentialsAsync(
        DateTime now,
        string passwordHash,
        string password,
        Func<(byte FailedCount, DateTime? LockedUntil)> read,
        Action<byte, DateTime?> write,
        CancellationToken ct)
    {
        var (failedCount, lockedUntil) = read();

        if (lockedUntil.HasValue)
        {
            if (lockedUntil.Value > now)
            {
                throw new AccountLockedException(SecondsUntil(lockedUntil.Value, now));
            }

            // Lock expired: start counting from zero again.
            failedCount = 0;
            lockedUntil = null;
            write(failedCount, lockedUntil);
        }

        if (!_hasher.Verify(password, passwordHash))
        {
            failedCount++;
            if (failedCount >= _rules.Auth.LockoutFailures)
            {
                lockedUntil = now.AddMinutes(_rules.Auth.LockoutMinutes);
                write(failedCount, lockedUntil);
                await _db.SaveChangesAsync(ct);
                _logger.LogWarning("Account locked until {LockedUntil:O} after {Failures} failed password logins.", lockedUntil, failedCount);
                throw new AccountLockedException(SecondsUntil(lockedUntil.Value, now));
            }

            write(failedCount, lockedUntil);
            await _db.SaveChangesAsync(ct);
            throw new UnauthorizedAccessException(InvalidCredentials);
        }

        if (failedCount != 0 || lockedUntil.HasValue)
        {
            write(0, null);
        }
    }

    private async Task<AuthResultDto> IssueTokensAsync(int subjectId, UserRole role, DateTime now, CancellationToken ct)
    {
        var (authResult, _) = SessionTokenIssuer.IssueSession(_db, _tokenService, _rules, subjectId, role, now);

        // Saves the reset failure counter together with the new refresh token.
        await _db.SaveChangesAsync(ct);

        return authResult;
    }

    private static int SecondsUntil(DateTime lockedUntil, DateTime now) =>
        (int)Math.Max(1, Math.Ceiling((lockedUntil - now).TotalSeconds));

    private static bool IsValidEmail(string value)
    {
        if (value.Length == 0 || value.Length > MaxEmailLength)
        {
            return false;
        }

        return MailAddress.TryCreate(value, out var parsed) && parsed.Address == value;
    }
}
