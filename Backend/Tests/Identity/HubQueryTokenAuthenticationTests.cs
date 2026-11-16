using System.Security.Claims;
using System.Text.Encodings.Web;
using CommonService.Application.Features.Identity.Services;
using CommonService.Infrastructure.Modules.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CommonService.Tests.Identity;

/// <summary>
/// MOB-RT-01 (#893, B1), decisions D37: a SignalR websocket client sends its token as <c>?access_token=</c>. The Bearer handler accepts it
/// only under <c>/hubs/</c>; every other path ignores the query string, and the <c>Authorization</c> header works as before.
/// </summary>
public class HubQueryTokenAuthenticationTests
{
    private const string ValidToken = "valid-token";

    private class StubTokenService : ITokenService
    {
        public ClaimsPrincipal? ValidateAccessToken(string token) => token == ValidToken
            ? new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "55"), new Claim(ClaimTypes.Role, "Worker")], "Bearer"))
            : null;

        public (string AccessToken, int ExpiresInSeconds) GenerateAccessToken(int userId, string role, IEnumerable<Claim>? additionalClaims = null) =>
            throw new NotImplementedException();

        public string GenerateRefreshToken() => throw new NotImplementedException();

        public string HashRefreshToken(string rawRefreshToken) => throw new NotImplementedException();

        public (string RegistrationToken, int ExpiresInSeconds) GenerateWorkerRegistrationToken(string phoneNumber) =>
            throw new NotImplementedException();
    }

    private class Monitor : IOptionsMonitor<AuthenticationSchemeOptions>
    {
        public AuthenticationSchemeOptions CurrentValue { get; } = new();
        public AuthenticationSchemeOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<AuthenticationSchemeOptions, string?> listener) => null;
    }

    private static async Task<AuthenticateResult> AuthenticateAsync(string path, string? query = null, string? authorization = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        if (query != null)
        {
            context.Request.QueryString = new QueryString(query);
        }

        if (authorization != null)
        {
            context.Request.Headers.Authorization = authorization;
        }

        var handler = new JwtBearerAuthenticationHandler(new Monitor(), NullLoggerFactory.Instance, UrlEncoder.Default, new StubTokenService());
        await handler.InitializeAsync(new AuthenticationScheme("Bearer", null, typeof(JwtBearerAuthenticationHandler)), context);
        return await handler.AuthenticateAsync();
    }

    [Theory]
    [InlineData("/hubs/notifications")]
    [InlineData("/hubs/notifications/negotiate")]
    [InlineData("/HUBS/notifications")]
    public async Task A_hub_request_authenticates_with_the_query_string_token(string path)
    {
        var result = await AuthenticateAsync(path, "?access_token=" + ValidToken);

        Assert.True(result.Succeeded);
        Assert.Equal("55", result.Principal!.FindFirst(ClaimTypes.NameIdentifier)!.Value);
    }

    [Fact]
    public async Task A_hub_request_with_a_bad_query_token_fails_and_without_one_is_anonymous()
    {
        Assert.False((await AuthenticateAsync("/hubs/notifications", "?access_token=forged")).Succeeded);
        Assert.True((await AuthenticateAsync("/hubs/notifications")).None);
    }

    [Theory]
    [InlineData("/api/v1/notifications")]
    [InlineData("/api/v1/hubs/notifications")]
    [InlineData("/hubsx/notifications")]
    public async Task Any_other_path_ignores_the_query_string_token(string path)
    {
        var result = await AuthenticateAsync(path, "?access_token=" + ValidToken);

        Assert.True(result.None); // anonymous, as if no token was sent
    }

    [Fact]
    public async Task The_authorization_header_works_as_before_on_every_path()
    {
        Assert.True((await AuthenticateAsync("/api/v1/notifications", authorization: "Bearer " + ValidToken)).Succeeded);
        Assert.True((await AuthenticateAsync("/hubs/notifications", authorization: "Bearer " + ValidToken)).Succeeded);
        Assert.False((await AuthenticateAsync("/api/v1/notifications", authorization: "Bearer forged")).Succeeded);
        Assert.True((await AuthenticateAsync("/api/v1/notifications", authorization: "Basic abc")).None);
    }
}
