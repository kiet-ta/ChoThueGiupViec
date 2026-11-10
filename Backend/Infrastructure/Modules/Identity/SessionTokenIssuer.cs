using CommonService.Application.Common.Options;
using CommonService.Application.Features.Identity.Dtos;
using CommonService.Application.Features.Identity.Services;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;

namespace CommonService.Infrastructure.Modules.Identity;

/// <summary>
/// Lean shared helper issuing signed JWT access token and rolling/persisting refresh token records.
/// Deduplicates session issuance across OTP login, password login, and refresh rotation.
/// </summary>
internal static class SessionTokenIssuer
{
    public static (AuthResultDto AuthResult, RefreshToken Record) IssueSession(
        AppDbContext db,
        ITokenService tokenService,
        BusinessRules rules,
        int subjectId,
        UserRole role,
        DateTime now,
        Guid? familyId = null,
        bool isNewUser = false)
    {
        var roleStr = role.ToString();
        var (accessToken, accessExpiry) = tokenService.GenerateAccessToken(subjectId, roleStr);
        var rawRefreshToken = tokenService.GenerateRefreshToken();
        var tokenHash = tokenService.HashRefreshToken(rawRefreshToken);

        var record = new RefreshToken
        {
            TokenHash = tokenHash,
            SubjectRole = role,
            SubjectId = subjectId,
            FamilyId = familyId ?? Guid.NewGuid(),
            CreatedAt = now,
            ExpiresAt = now.AddDays(rules.Auth.RefreshTokenDays),
            RevokedAt = null,
            ReplacedById = null
        };
        db.RefreshTokens.Add(record);

        var authResult = new AuthResultDto
        {
            TokenType = "Bearer",
            AccessToken = accessToken,
            AccessTokenExpiresInSeconds = accessExpiry,
            RefreshToken = rawRefreshToken,
            User = new AuthUserDto
            {
                Id = subjectId,
                Role = roleStr,
                IsNewUser = isNewUser
            }
        };

        return (authResult, record);
    }
}
