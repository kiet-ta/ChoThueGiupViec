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
public sealed class OtpService : IOtpService
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
                return OtpRequestResult.RateLimited(retryAfter, $"Please wait {retryAfter} seconds before requesting a new OTP code.");
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
        var (isValidPhone, normalizedPhone) = PhoneValidator.TryNormalize(phoneNumber);
        if (!isValidPhone)
        {
            return OtpVerifyResult.ValidationError(new Dictionary<string, string[]>
            {
                ["phoneNumber"] = ["Invalid Vietnamese mobile phone number format."]
            });
        }

        if (!Enum.TryParse<UserRole>(roleString, ignoreCase: true, out var role) ||
            (role != UserRole.Customer && role != UserRole.Worker))
        {
            return OtpVerifyResult.ValidationError(new Dictionary<string, string[]>
            {
                ["role"] = ["Role must be either 'Customer' or 'Worker'."]
            });
        }

        if (string.IsNullOrWhiteSpace(code) || code.Length != _rules.Otp.Length || !code.All(char.IsDigit))
        {
            return OtpVerifyResult.ValidationError(new Dictionary<string, string[]>
            {
                ["code"] = [$"Code must be a {_rules.Otp.Length}-digit numeric string."]
            });
        }

        var now = _clock.UtcNow;

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

        if (role == UserRole.Customer)
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

            var (accessToken, accessExpiry) = _tokenService.GenerateAccessToken(customer.CustomerId, "Customer");
            var rawRefreshToken = _tokenService.GenerateRefreshToken();
            var tokenHash = _tokenService.HashRefreshToken(rawRefreshToken);

            var refreshTokenRecord = new RefreshToken
            {
                TokenHash = tokenHash,
                SubjectRole = UserRole.Customer,
                SubjectId = customer.CustomerId,
                FamilyId = Guid.NewGuid(),
                CreatedAt = now,
                ExpiresAt = now.AddDays(_rules.Auth.RefreshTokenDays),
                RevokedAt = null,
                ReplacedById = null
            };
            _db.RefreshTokens.Add(refreshTokenRecord);
            await _db.SaveChangesAsync(ct);

            return OtpVerifyResult.Ok(new AuthResultDto
            {
                TokenType = "Bearer",
                AccessToken = accessToken,
                AccessTokenExpiresInSeconds = accessExpiry,
                RefreshToken = rawRefreshToken,
                User = new AuthUserDto
                {
                    Id = customer.CustomerId,
                    Role = "Customer",
                    IsNewUser = isNewUser
                }
            });
        }

        // Worker role
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
        var (workerAccessToken, workerAccessExpiry) = _tokenService.GenerateAccessToken(worker.WorkerId, "Worker");
        var rawWorkerRefreshToken = _tokenService.GenerateRefreshToken();
        var workerTokenHash = _tokenService.HashRefreshToken(rawWorkerRefreshToken);

        var workerRefreshTokenRecord = new RefreshToken
        {
            TokenHash = workerTokenHash,
            SubjectRole = UserRole.Worker,
            SubjectId = worker.WorkerId,
            FamilyId = Guid.NewGuid(),
            CreatedAt = now,
            ExpiresAt = now.AddDays(_rules.Auth.RefreshTokenDays),
            RevokedAt = null,
            ReplacedById = null
        };
        _db.RefreshTokens.Add(workerRefreshTokenRecord);
        await _db.SaveChangesAsync(ct);

        return OtpVerifyResult.Ok(new AuthResultDto
        {
            TokenType = "Bearer",
            AccessToken = workerAccessToken,
            AccessTokenExpiresInSeconds = workerAccessExpiry,
            RefreshToken = rawWorkerRefreshToken,
            User = new AuthUserDto
            {
                Id = worker.WorkerId,
                Role = "Worker",
                IsNewUser = false
            }
        });
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
        var (newAccessToken, accessExpiry) = _tokenService.GenerateAccessToken(
            tokenRecord.SubjectId, tokenRecord.SubjectRole.ToString());

        var newRawRefreshToken = _tokenService.GenerateRefreshToken();
        var newTokenHash = _tokenService.HashRefreshToken(newRawRefreshToken);

        var newTokenRecord = new RefreshToken
        {
            TokenHash = newTokenHash,
            SubjectRole = tokenRecord.SubjectRole,
            SubjectId = tokenRecord.SubjectId,
            FamilyId = tokenRecord.FamilyId,
            CreatedAt = now,
            ExpiresAt = now.AddDays(_rules.Auth.RefreshTokenDays),
            RevokedAt = null,
            ReplacedById = null
        };
        _db.RefreshTokens.Add(newTokenRecord);
        await _db.SaveChangesAsync(ct);

        // Mark current token replaced and revoked
        tokenRecord.RevokedAt = now;
        tokenRecord.ReplacedById = newTokenRecord.RefreshTokenId;
        await _db.SaveChangesAsync(ct);

        return RefreshResult.Ok(new AuthResultDto
        {
            TokenType = "Bearer",
            AccessToken = newAccessToken,
            AccessTokenExpiresInSeconds = accessExpiry,
            RefreshToken = newRawRefreshToken,
            User = new AuthUserDto
            {
                Id = tokenRecord.SubjectId,
                Role = tokenRecord.SubjectRole.ToString(),
                IsNewUser = false
            }
        });
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
