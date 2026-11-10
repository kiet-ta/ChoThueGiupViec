using System.Security.Cryptography;
using System.Text;
using CommonService.Application.Common.Options;
using CommonService.Application.Features.Identity;
using CommonService.Application.Features.Identity.Dtos;
using CommonService.Application.Features.Identity.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CommonService.Infrastructure.Modules.Identity;

/// <summary>
/// Implements OTP and Refresh token flows per contract identity.md (§2.1, §2.2, §2.4, §2.5) and decisions Q06, Q16, Q20, SC-7.
/// </summary>
public class OtpService : IOtpService
{
    private readonly AppDbContext _db;
    private readonly IOtpSender _otpSender;
    private readonly IClock _clock;
    private readonly BusinessRules _rules;
    private readonly byte[] _hmacKey;
    private readonly ITokenService _tokenService;
    private readonly ILogger<OtpService> _logger;

    public OtpService(
        AppDbContext db,
        IOtpSender otpSender,
        IClock clock,
        IOptions<BusinessRules> rules,
        IConfiguration config,
        ITokenService tokenService,
        ILogger<OtpService> logger)
    {
        _db = db;
        _otpSender = otpSender;
        _clock = clock;
        _rules = rules.Value;
        _tokenService = tokenService;
        _logger = logger;

        var secret = config["Otp:HmacSecret"]
            ?? config["Jwt:Key"]
            ?? "ChoThueGiupViec_Dev_Default_Otp_Hmac_Secret_Key_At_Least_32_Chars!";
        _hmacKey = Encoding.UTF8.GetBytes(secret);
    }

    public async Task<OtpRequestResult> RequestOtpAsync(
        string phoneNumber,
        string roleString,
        string clientIp,
        CancellationToken ct = default)
    {
        var (isValidPhone, normalizedPhone) = PhoneValidator.TryNormalize(phoneNumber);
        if (!isValidPhone)
        {
            return OtpRequestResult.ValidationError(new Dictionary<string, string[]>
            {
                ["phoneNumber"] = ["Invalid Vietnamese mobile phone number format (must be 10 digits starting with 03, 05, 07, 08, or 09)."]
            });
        }

        if (!Enum.TryParse<UserRole>(roleString, ignoreCase: true, out var role) ||
            (role != UserRole.Customer && role != UserRole.Worker))
        {
            return OtpRequestResult.ValidationError(new Dictionary<string, string[]>
            {
                ["role"] = ["Role must be either 'Customer' or 'Worker'."]
            });
        }

        var now = _clock.UtcNow;

        // 1. Check cooldown (Otp.ResendCooldownSeconds, default 60s)
        var cooldownSeconds = _rules.Otp.ResendCooldownSeconds;
        var lastOtp = await _db.OtpCodes
            .Where(x => x.PhoneNumber == normalizedPhone && x.Role == role)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (lastOtp != null)
        {
            var elapsedSeconds = (now - lastOtp.CreatedAt).TotalSeconds;
            if (elapsedSeconds < cooldownSeconds)
            {
                var retryAfter = (int)Math.Max(1, Math.Ceiling(cooldownSeconds - elapsedSeconds));
                return OtpRequestResult.RateLimited(retryAfter, $"Vui lòng đợi {retryAfter} giây trước khi yêu cầu mã OTP mới.");
            }
        }

        // 2. Check per-phone hourly limit (Otp.MaxPerPhonePerHour, default 5)
        var oneHourAgo = now.AddHours(-1);
        var phoneCount = await _db.OtpCodes
            .CountAsync(x => x.PhoneNumber == normalizedPhone && x.CreatedAt >= oneHourAgo, ct);

        if (phoneCount >= _rules.Otp.MaxPerPhonePerHour)
        {
            var oldestPhone = await _db.OtpCodes
                .Where(x => x.PhoneNumber == normalizedPhone && x.CreatedAt >= oneHourAgo)
                .OrderBy(x => x.CreatedAt)
                .FirstOrDefaultAsync(ct);

            var retryAfter = oldestPhone != null
                ? (int)Math.Max(1, Math.Ceiling((oldestPhone.CreatedAt.AddHours(1) - now).TotalSeconds))
                : 3600;

            return OtpRequestResult.RateLimited(retryAfter, "Too many OTP requests for this phone number. Please try again later.");
        }

        // 3. Check per-IP hourly limit (Otp.MaxPerIpPerHour, default 20)
        var ip = string.IsNullOrWhiteSpace(clientIp) ? "127.0.0.1" : clientIp;
        var ipCount = await _db.OtpCodes
            .CountAsync(x => x.RequestedIp == ip && x.CreatedAt >= oneHourAgo, ct);

        if (ipCount >= _rules.Otp.MaxPerIpPerHour)
        {
            var oldestIp = await _db.OtpCodes
                .Where(x => x.RequestedIp == ip && x.CreatedAt >= oneHourAgo)
                .OrderBy(x => x.CreatedAt)
                .FirstOrDefaultAsync(ct);

            var retryAfter = oldestIp != null
                ? (int)Math.Max(1, Math.Ceiling((oldestIp.CreatedAt.AddHours(1) - now).TotalSeconds))
                : 3600;

            return OtpRequestResult.RateLimited(retryAfter, "Too many OTP requests from your IP address. Please try again later.");
        }

        // 4. Invalidate previous unconsumed codes for that phone+role (contract §2.1)
        var unconsumed = await _db.OtpCodes
            .Where(x => x.PhoneNumber == normalizedPhone && x.Role == role && x.ConsumedAt == null)
            .ToListAsync(ct);

        foreach (var item in unconsumed)
        {
            item.ConsumedAt = now;
        }

        // 5. Generate 6-digit code
        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString("D6");
        var codeHash = ComputeHmacHash(code);

        // 6. Save in OTP_CODE
        var otpCode = new OtpCode
        {
            PhoneNumber = normalizedPhone,
            Role = role,
            CodeHash = codeHash,
            AttemptCount = 0,
            RequestedIp = ip,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(_rules.Otp.TtlMinutes),
            ConsumedAt = null
        };

        _db.OtpCodes.Add(otpCode);
        await _db.SaveChangesAsync(ct);

        // 7. Dispatch via IOtpSender
        await _otpSender.SendAsync(normalizedPhone, code, ct);

        return OtpRequestResult.Ok(new OtpRequestResponseDto
        {
            ExpiresInSeconds = _rules.Otp.TtlMinutes * 60,
            ResendAvailableInSeconds = _rules.Otp.ResendCooldownSeconds
        });
    }

    public async Task<OtpVerifyResult> VerifyOtpAsync(
        string phoneNumber,
        string roleString,
        string code,
        CancellationToken ct = default)
    {
        var (validationError, normalizedPhone, role) = ValidateInputs(phoneNumber, roleString, code);
        if (validationError != null)
        {
            return validationError;
        }

        var now = _clock.UtcNow;
        var otpError = await VerifyAndConsumeOtpAsync(normalizedPhone, role, code, now, ct);
        if (otpError != null)
        {
            return otpError;
        }

        return role == UserRole.Customer
            ? await HandleCustomerLoginAsync(normalizedPhone, now, ct)
            : await HandleWorkerLoginAsync(normalizedPhone, now, ct);
    }

    private (OtpVerifyResult? Error, string NormalizedPhone, UserRole Role) ValidateInputs(
        string phoneNumber,
        string roleString,
        string code)
    {
        var (isValidPhone, normalizedPhone) = PhoneValidator.TryNormalize(phoneNumber);
        if (!isValidPhone)
        {
            return (OtpVerifyResult.ValidationError(new Dictionary<string, string[]>
            {
                ["phoneNumber"] = ["Invalid Vietnamese mobile phone number format."]
            }), string.Empty, default);
        }

        if (!Enum.TryParse<UserRole>(roleString, ignoreCase: true, out var role) ||
            (role != UserRole.Customer && role != UserRole.Worker))
        {
            return (OtpVerifyResult.ValidationError(new Dictionary<string, string[]>
            {
                ["role"] = ["Role must be either 'Customer' or 'Worker'."]
            }), string.Empty, default);
        }

        if (string.IsNullOrWhiteSpace(code) || code.Length != _rules.Otp.Length || !code.All(char.IsDigit))
        {
            return (OtpVerifyResult.ValidationError(new Dictionary<string, string[]>
            {
                ["code"] = [$"Code must be a {_rules.Otp.Length}-digit numeric string."]
            }), string.Empty, default);
        }

        return (null, normalizedPhone, role);
    }

    private async Task<OtpVerifyResult?> VerifyAndConsumeOtpAsync(
        string normalizedPhone,
        UserRole role,
        string code,
        DateTime now,
        CancellationToken ct)
    {
        var otp = await _db.OtpCodes
            .Where(x => x.PhoneNumber == normalizedPhone && x.Role == role && x.ConsumedAt == null)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (otp == null)
        {
            return OtpVerifyResult.Unauthorized("Invalid or expired OTP code.");
        }

        if (otp.ExpiresAt < now)
        {
            otp.ConsumedAt = now;
            await _db.SaveChangesAsync(ct);
            return OtpVerifyResult.Unauthorized("Invalid or expired OTP code.");
        }

        if (otp.AttemptCount >= _rules.Otp.MaxAttempts)
        {
            otp.ConsumedAt = now;
            await _db.SaveChangesAsync(ct);
            return OtpVerifyResult.TooManyAttempts(60);
        }

        var expectedHash = ComputeHmacHash(code);
        var isMatch = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedHash),
            Encoding.UTF8.GetBytes(otp.CodeHash));

        if (!isMatch)
        {
            otp.AttemptCount++;
            if (otp.AttemptCount >= _rules.Otp.MaxAttempts)
            {
                otp.ConsumedAt = now;
                await _db.SaveChangesAsync(ct);
                return OtpVerifyResult.TooManyAttempts(60);
            }

            await _db.SaveChangesAsync(ct);
            return OtpVerifyResult.Unauthorized("Invalid or expired OTP code.");
        }

        // Code matched: mark consumed
        otp.ConsumedAt = now;
        return null;
    }

    private async Task<OtpVerifyResult> HandleCustomerLoginAsync(
        string normalizedPhone,
        DateTime now,
        CancellationToken ct)
    {
        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.PhoneNumber == normalizedPhone, ct);

        bool isNewUser;
        if (customer == null)
        {
            customer = new Customer
            {
                PhoneNumber = normalizedPhone,
                FullName = string.Empty,
                Email = null,
                OtpVerifiedAt = now,
                TrustScore = _rules.Customer.InitialTrustScore,
                AccountStatus = CustomerAccountStatus.Active,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.Customers.Add(customer);
            await _db.SaveChangesAsync(ct);
            isNewUser = true;
        }
        else
        {
            if (customer.AccountStatus == CustomerAccountStatus.Locked)
            {
                await _db.SaveChangesAsync(ct);
                return OtpVerifyResult.Forbidden("Account is locked.");
            }

            customer.OtpVerifiedAt = now;
            customer.UpdatedAt = now;
            await _db.SaveChangesAsync(ct);
            isNewUser = false;
        }

        var (authResult, _) = SessionTokenIssuer.IssueSession(
            _db, _tokenService, _rules, customer.CustomerId, UserRole.Customer, now, isNewUser: isNewUser);
        await _db.SaveChangesAsync(ct);

        return OtpVerifyResult.Ok(authResult);
    }

    private async Task<OtpVerifyResult> HandleWorkerLoginAsync(
        string normalizedPhone,
        DateTime now,
        CancellationToken ct)
    {
        var worker = await _db.Workers
            .FirstOrDefaultAsync(w => w.PhoneNumber == normalizedPhone, ct);

        if (worker == null)
        {
            await _db.SaveChangesAsync(ct);
            var (regToken, regExpiry) = _tokenService.GenerateWorkerRegistrationToken(normalizedPhone);
            return OtpVerifyResult.RegistrationNeeded(new RegistrationRequiredDto
            {
                IsNewUser = true,
                RegistrationToken = regToken,
                RegistrationTokenExpiresInSeconds = regExpiry
            });
        }

        await _db.SaveChangesAsync(ct);
        var (authResult, _) = SessionTokenIssuer.IssueSession(
            _db, _tokenService, _rules, worker.WorkerId, UserRole.Worker, now, isNewUser: false);
        await _db.SaveChangesAsync(ct);

        return OtpVerifyResult.Ok(authResult);
    }

    public async Task<RefreshResult> RefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return RefreshResult.BadRequest("Refresh token is required.");
        }

        var hash = _tokenService.HashRefreshToken(refreshToken);
        var tokenRecord = await _db.RefreshTokens.FirstOrDefaultAsync(x => x.TokenHash == hash, ct);

        if (tokenRecord == null)
        {
            return RefreshResult.Unauthorized("Invalid or expired refresh token.");
        }

        var now = _clock.UtcNow;

        if (tokenRecord.ExpiresAt < now)
        {
            return RefreshResult.Unauthorized("Refresh token has expired.");
        }

        // REUSE DETECTION: if token was already replaced, revoke the entire family!
        if (tokenRecord.ReplacedById != null)
        {
            _logger.LogWarning("Refresh token reuse detected for family {FamilyId}. Revoking entire family chain.", tokenRecord.FamilyId);
            var familyTokens = await _db.RefreshTokens
                .Where(x => x.FamilyId == tokenRecord.FamilyId && x.RevokedAt == null)
                .ToListAsync(ct);

            foreach (var t in familyTokens)
            {
                t.RevokedAt = now;
            }
            await _db.SaveChangesAsync(ct);
            return RefreshResult.Unauthorized("Refresh token reuse detected. Token family has been revoked.");
        }

        if (tokenRecord.RevokedAt != null)
        {
            return RefreshResult.Unauthorized("Refresh token has been revoked.");
        }

        // Valid rotation: issue new access token and new refresh token with same family_id
        var (authResult, newTokenRecord) = SessionTokenIssuer.IssueSession(
            _db, _tokenService, _rules, tokenRecord.SubjectId, tokenRecord.SubjectRole, now, familyId: tokenRecord.FamilyId);
        await _db.SaveChangesAsync(ct);

        // Mark current token replaced and revoked
        tokenRecord.RevokedAt = now;
        tokenRecord.ReplacedById = newTokenRecord.RefreshTokenId;
        await _db.SaveChangesAsync(ct);

        return RefreshResult.Ok(authResult);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        var hash = _tokenService.HashRefreshToken(refreshToken);
        var tokenRecord = await _db.RefreshTokens.FirstOrDefaultAsync(x => x.TokenHash == hash, ct);

        if (tokenRecord != null && tokenRecord.RevokedAt == null)
        {
            tokenRecord.RevokedAt = _clock.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    private string ComputeHmacHash(string code)
    {
        using var hmac = new HMACSHA256(_hmacKey);
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(code));
        return Convert.ToHexString(bytes);
    }
}
