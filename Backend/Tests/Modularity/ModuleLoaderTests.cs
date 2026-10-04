using CommonService.Application;
using CommonService.Application.Interfaces.IServices;
using CommonService.Infrastructure;
using CommonService.Infrastructure.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CommonService.Tests.Modularity;

public class ModuleLoaderTests
{
    private static IConfiguration EmptyConfig() => new ConfigurationBuilder().Build();

    [Fact]
    public void Fake_module_in_another_assembly_is_found_and_its_service_resolves_without_touching_Program()
    {
        var services = new ServiceCollection();

        services.AddModules(EmptyConfig(), typeof(FakeModule).Assembly);

        using var provider = services.BuildServiceProvider();
        Assert.Equal("hello from the fake module", provider.GetRequiredService<IFakeGreeter>().Greet());
        Assert.Contains(provider.GetServices<IModule>(), m => m is FakeModule);
    }

    [Fact]
    public void Module_endpoints_are_mapped_by_MapModuleEndpoints()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddModules(EmptyConfig(), typeof(FakeModule));
        var app = builder.Build();

        app.MapModuleEndpoints();

        var patterns = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(d => d.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText);
        Assert.Contains("/_fake/ping", patterns);
    }

    [Fact]
    public void Modules_are_registered_in_name_order_whatever_the_input_order()
    {
        var services = new ServiceCollection();

        services.AddModules(EmptyConfig(), typeof(ZetaModule), typeof(AlphaModule));

        using var provider = services.BuildServiceProvider();
        Assert.Equal(["Alpha", "Zeta"], provider.GetServices<IModule>().Select(m => m.Name));
    }

    [Fact]
    public void Duplicate_module_name_fails_fast()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddModules(EmptyConfig(), typeof(FakeModule), typeof(DuplicateNameModule)));

        Assert.Contains("Duplicate module name 'Fake'", ex.Message);
    }

    [Fact]
    public void Module_without_public_parameterless_constructor_fails_fast()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddModules(EmptyConfig(), typeof(NoDefaultConstructorModule)));

        Assert.Contains("parameterless constructor", ex.Message);
    }

    [Fact]
    public void Abstract_and_non_public_module_types_are_ignored_by_the_assembly_scan()
    {
        var services = new ServiceCollection();

        services.AddModules(EmptyConfig(), typeof(FakeModule).Assembly);

        using var provider = services.BuildServiceProvider();
        var names = provider.GetServices<IModule>().Select(m => m.Name).ToList();
        Assert.Equal(["Fake"], names);
    }

    [Fact]
    public void Real_composition_used_by_Program_still_builds_with_the_module_hook()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddApplication();
        services.AddInfrastructure(EmptyConfig());

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<ICacheService>());
    }

    private sealed class AlphaModule : IModule
    {
        public string Name => "Alpha";
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration) { }
    }

    private sealed class ZetaModule : IModule
    {
        public string Name => "Zeta";
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration) { }
    }

    private sealed class DuplicateNameModule : IModule
    {
        public string Name => "Fake";
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration) { }
    }

    private sealed class NoDefaultConstructorModule(int unused) : IModule
    {
        public string Name => $"NoCtor{unused}";
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration) { }
    }
}
