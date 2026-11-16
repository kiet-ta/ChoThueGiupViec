using CommonService.Infrastructure.Modules.DevCors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CommonService.Tests.DevCors;

/// <summary>MOB-PLATFORM-01 (#942), decisions D39: the dev-only CORS policy for the Flutter app in a browser.</summary>
public class DevCorsTests
{
    [Theory]
    [InlineData("http://localhost:5173", true)]
    [InlineData("http://localhost:54321", true)]
    [InlineData("http://localhost", true)]
    [InlineData("http://127.0.0.1:8081", true)]
    [InlineData("https://localhost:5173", false)]
    [InlineData("http://localhost.evil.com", false)]
    [InlineData("http://evil.com/localhost", false)]
    [InlineData("http://192.168.1.10:5173", false)]
    [InlineData("https://app.example.com", false)]
    [InlineData("null", false)]
    public void Only_http_localhost_and_loopback_origins_are_local(string origin, bool expected) =>
        Assert.Equal(expected, DevCorsModule.IsLocalOrigin(origin));

    private static async Task<(HttpResponseMessage Get, HttpResponseMessage Preflight)> CallAsync(string environment, string origin)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddRouting();
        new DevCorsModule().ConfigureServices(builder.Services, builder.Configuration);

        await using var app = builder.Build();
        app.MapGet("/ping", () => "pong");
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            using var client = new HttpClient { BaseAddress = new Uri(address) };

            var get = new HttpRequestMessage(HttpMethod.Get, "/ping");
            get.Headers.Add("Origin", origin);

            var preflight = new HttpRequestMessage(HttpMethod.Options, "/ping");
            preflight.Headers.Add("Origin", origin);
            preflight.Headers.Add("Access-Control-Request-Method", "POST");
            preflight.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");

            return (await client.SendAsync(get), await client.SendAsync(preflight));
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task In_development_a_local_origin_gets_the_CORS_headers_and_the_preflight_is_answered()
    {
        var (get, preflight) = await CallAsync(Environments.Development, "http://localhost:5173");

        Assert.Equal("http://localhost:5173", get.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", get.Headers.GetValues("Access-Control-Allow-Credentials").Single());
        Assert.Contains("Retry-After", get.Headers.GetValues("Access-Control-Expose-Headers").Single());
        Assert.True(preflight.IsSuccessStatusCode);
        Assert.Equal("http://localhost:5173", preflight.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Contains("POST", preflight.Headers.GetValues("Access-Control-Allow-Methods").Single());
    }

    [Fact]
    public async Task In_development_a_foreign_origin_gets_no_CORS_headers()
    {
        var (get, preflight) = await CallAsync(Environments.Development, "https://app.example.com");

        Assert.False(get.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(preflight.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Outside_development_even_a_local_origin_gets_no_CORS_headers(string environment)
    {
        var (get, preflight) = await CallAsync(environment, "http://localhost:5173");

        Assert.False(get.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(preflight.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
