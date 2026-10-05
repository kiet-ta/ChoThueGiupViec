using System.Security.Claims;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using Microsoft.AspNetCore.Http;

namespace CommonService.Infrastructure.Services;

/// <summary>
/// Real ICurrentUser implementation resolving claims from HttpContext (BE-M1-02).
/// Parses userId from NameIdentifier, sub, or userId claims.
/// Parses UserRole from Role or role claims.
/// </summary>
public sealed class ClaimsCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ClaimsCurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;

    public int? UserId
    {
        get
        {
            var user = User;
            if (user == null || user.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            var idClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? user.FindFirst("sub")?.Value
                ?? user.FindFirst("userId")?.Value;

            return int.TryParse(idClaim, out var id) ? id : null;
        }
    }

    public UserRole? Role
    {
        get
        {
            var user = User;
            if (user == null || user.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            var roleClaim = user.FindFirst(ClaimTypes.Role)?.Value
                ?? user.FindFirst("role")?.Value;

            if (string.IsNullOrWhiteSpace(roleClaim))
            {
                return null;
            }

            return Enum.TryParse<UserRole>(roleClaim, ignoreCase: true, out var role)
                ? role
                : null;
        }
    }
}
