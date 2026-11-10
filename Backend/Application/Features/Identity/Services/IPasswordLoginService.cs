using CommonService.Application.Features.Identity.Dtos;

namespace CommonService.Application.Features.Identity.Services;

/// <summary>
/// Email + password login for Admin and Partner Agency with failure lockout
/// (contract identity.md §2.3, decisions Q16, SC-1, SC-8).
/// </summary>
public interface IPasswordLoginService
{
    /// <summary>
    /// Verifies the credentials, enforces the lockout policy (Auth.LockoutFailures / Auth.LockoutMinutes),
    /// and on success issues an access token plus a new refresh token family.
    /// Throws ValidationException (400), UnauthorizedAccessException (401), ForbiddenAccessException (403) or
    /// AccountLockedException (423, Retry-After); ErrorHandlingMiddleware maps them.
    /// </summary>
    Task<AuthResultDto> LoginAsync(string email, string password, string roleString, CancellationToken ct = default);
}
