using CommonService.Domain.Enums;

namespace CommonService.Application.Interfaces.Ports;

/// <summary>The authenticated caller of the current request (claims of the JWT, BE-M1-02).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>Id of the customer / worker / partner agency / admin, according to <see cref="Role"/>. Null when anonymous.</summary>
    int? UserId { get; }

    /// <summary>Null when anonymous.</summary>
    UserRole? Role { get; }
}
