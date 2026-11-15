using System.Text.Encodings.Web;
using CommonService.Application.Features.Identity.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CommonService.Infrastructure.Modules.Identity;

/// <summary>
/// Custom authentication handler validating Bearer JWT tokens via ITokenService (BE-M1-02).
/// Enables native [Authorize], role requirements, and policies without third-party dependencies.
/// </summary>
public class JwtBearerAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly ITokenService _tokenService;

    public JwtBearerAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ITokenService tokenService)
        : base(options, logger, encoder)
    {
        _tokenService = tokenService;
    }

    /// <summary>Path prefix of the SignalR hubs, where a websocket client can only send its token in the query string.</summary>
    public const string HubPathPrefix = "/hubs/";

    /// <summary>Query-string key the SignalR client uses for <c>accessTokenFactory</c>.</summary>
    public const string HubAccessTokenQueryKey = "access_token";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? token;
        if (Request.Headers.TryGetValue("Authorization", out var authHeader) && !string.IsNullOrWhiteSpace(authHeader))
        {
            var headerValue = authHeader.ToString().Trim();
            if (!headerValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            token = headerValue["Bearer ".Length..].Trim();
        }
        else if (Request.Path.StartsWithSegments(HubPathPrefix.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
            && Request.Query.TryGetValue(HubAccessTokenQueryKey, out var queryToken)
            && !string.IsNullOrWhiteSpace(queryToken))
        {
            // Decisions D37 (MOB-RT-01, #893): a SignalR websocket cannot set headers, so under /hubs/ only the token may come
            // from ?access_token=. Every other path ignores the query string (a token in a URL ends up in logs and history).
            token = queryToken.ToString().Trim();
        }
        else
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return Task.FromResult(AuthenticateResult.Fail("Empty bearer token."));
        }

        var principal = _tokenService.ValidateAccessToken(token);
        if (principal == null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid or expired bearer token."));
        }

        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
