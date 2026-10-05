using CommonService.Infrastructure.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CommonService.Tests.Modularity;

public interface IFakeGreeter
{
    string Greet();
}

public sealed class FakeGreeter : IFakeGreeter
{
    public string Greet() => "hello from the fake module";
}

/// <summary>A module that lives only in the test assembly: no shared file knows about it.</summary>
public sealed class FakeModule : IModule
{
    public string Name => "Fake";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddSingleton<IFakeGreeter, FakeGreeter>();

    public void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/_fake/ping", () => "pong");
}
