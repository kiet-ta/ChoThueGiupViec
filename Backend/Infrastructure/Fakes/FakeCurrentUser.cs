using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;

namespace CommonService.Infrastructure.Fakes;

/// <summary>Anonymous until a test or a dev sets a user.</summary>
public sealed class FakeCurrentUser : ICurrentUser
{
    public bool IsAuthenticated => UserId.HasValue;

    public int? UserId { get; private set; }

    public UserRole? Role { get; private set; }

    public void SignIn(int userId, UserRole role)
    {
        UserId = userId;
        Role = role;
    }

    public void SignOut()
    {
        UserId = null;
        Role = null;
    }
}
