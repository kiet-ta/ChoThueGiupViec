using System.Security.Claims;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Services;
using Microsoft.AspNetCore.Http;

namespace CommonService.Tests.Infrastructure;

public class ClaimsCurrentUserTests
{
    private readonly HttpContextAccessor _accessor = new();
    private readonly ClaimsCurrentUser _currentUser;

    public ClaimsCurrentUserTests()
    {
        _currentUser = new ClaimsCurrentUser(_accessor);
    }

    [Fact]
    public void When_HttpContext_is_null_user_is_not_authenticated()
    {
        _accessor.HttpContext = null;

        Assert.False(_currentUser.IsAuthenticated);
        Assert.Null(_currentUser.UserId);
        Assert.Null(_currentUser.Role);
    }

    [Fact]
    public void When_unauthenticated_identity_returns_null()
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity()); // not authenticated
        _accessor.HttpContext = context;

        Assert.False(_currentUser.IsAuthenticated);
        Assert.Null(_currentUser.UserId);
        Assert.Null(_currentUser.Role);
    }

    [Fact]
    public void When_authenticated_with_sub_and_role_resolves_correctly()
    {
        var identity = new ClaimsIdentity("Bearer");
        identity.AddClaim(new Claim("sub", "123"));
        identity.AddClaim(new Claim(ClaimTypes.Role, "Customer"));

        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        _accessor.HttpContext = context;

        Assert.True(_currentUser.IsAuthenticated);
        Assert.Equal(123, _currentUser.UserId);
        Assert.Equal(UserRole.Customer, _currentUser.Role);
    }

    [Fact]
    public void When_authenticated_with_NameIdentifier_and_worker_role_resolves_correctly()
    {
        var identity = new ClaimsIdentity("Bearer");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "456"));
        identity.AddClaim(new Claim("role", "Worker"));

        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        _accessor.HttpContext = context;

        Assert.True(_currentUser.IsAuthenticated);
        Assert.Equal(456, _currentUser.UserId);
        Assert.Equal(UserRole.Worker, _currentUser.Role);
    }

    [Fact]
    public void Parses_Partner_and_Admin_roles_correctly()
    {
        var identity = new ClaimsIdentity("Bearer");
        identity.AddClaim(new Claim("userId", "789"));
        identity.AddClaim(new Claim("role", "Partner"));

        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        _accessor.HttpContext = context;

        Assert.Equal(789, _currentUser.UserId);
        Assert.Equal(UserRole.Partner, _currentUser.Role);

        // Test Admin
        var adminIdentity = new ClaimsIdentity("Bearer");
        adminIdentity.AddClaim(new Claim("userId", "1"));
        adminIdentity.AddClaim(new Claim(ClaimTypes.Role, "Admin"));
        context.User = new ClaimsPrincipal(adminIdentity);

        Assert.Equal(1, _currentUser.UserId);
        Assert.Equal(UserRole.Admin, _currentUser.Role);
    }

    [Fact]
    public void Non_integer_userId_returns_null()
    {
        var identity = new ClaimsIdentity("Bearer");
        identity.AddClaim(new Claim("sub", "not-a-number"));
        identity.AddClaim(new Claim("role", "Customer"));

        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        _accessor.HttpContext = context;

        Assert.True(_currentUser.IsAuthenticated);
        Assert.Null(_currentUser.UserId);
        Assert.Equal(UserRole.Customer, _currentUser.Role);
    }
}
