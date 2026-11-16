using CommonService.Infrastructure.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CommonService.Infrastructure.Modules.DevCors;

/// <summary>
/// MOB-PLATFORM-01 (#942), decisions D39: the Flutter app running in a browser (<c>flutter run -d chrome</c>) is another origin
/// than the API (<c>http://localhost:5004</c>), so the browser blocks every call without CORS headers. In the <b>Development</b>
/// environment only, any <c>http://localhost:&lt;port&gt;</c> and <c>http://127.0.0.1:&lt;port&gt;</c> origin is allowed (any
/// method, any header, credentials for the SignalR negotiate). In every other environment nothing is added: production origins
/// need their own decision. Registered through the module mechanism; the middleware comes from a startup filter, so
/// <c>Program.cs</c> is untouched.
/// </summary>
public class DevCorsModule : IModule
{
    public const string PolicyName = "DevLocalOrigins";

    public string Name => "DevCors";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddCors(options => options.AddPolicy(PolicyName, policy => policy
            .SetIsOriginAllowed(IsLocalOrigin)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            // A browser hides every response header outside the CORS-safelisted ones from the app; the app reads the cooldown.
            .WithExposedHeaders("Retry-After")));
        services.AddTransient<IStartupFilter, DevCorsStartupFilter>();
    }

    /// <summary>True for <c>http://localhost[:port]</c> and <c>http://127.0.0.1[:port]</c> (not <c>https</c>, not a lookalike host).</summary>
    public static bool IsLocalOrigin(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttp
        && (uri.Host == "localhost" || uri.Host == "127.0.0.1")
        && string.IsNullOrEmpty(uri.PathAndQuery.Trim('/'));

    private sealed class DevCorsStartupFilter(IHostEnvironment environment) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            // First in the pipeline, so the preflight (OPTIONS) is answered before authentication and the error middleware.
            if (environment.IsDevelopment())
            {
                app.UseCors(PolicyName);
            }

            next(app);
        };
    }
}
