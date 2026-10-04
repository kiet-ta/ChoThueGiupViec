using System.Reflection;

namespace CommonService.Infrastructure.Modularity;

public static class ModuleLoader
{
    /// <summary>Find every public, concrete <see cref="IModule"/> in the given assemblies and register it.</summary>
    public static IServiceCollection AddModules(
        this IServiceCollection services,
        IConfiguration configuration,
        params Assembly[] assemblies)
    {
        var moduleTypes = assemblies
            .Distinct()
            .SelectMany(a => a.GetExportedTypes())
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IModule).IsAssignableFrom(t));

        return services.AddModules(configuration, moduleTypes.ToArray());
    }

    /// <summary>Register the given module types. Fails fast on a missing public parameterless constructor or a duplicate module name.</summary>
    public static IServiceCollection AddModules(
        this IServiceCollection services,
        IConfiguration configuration,
        params Type[] moduleTypes)
    {
        var modules = moduleTypes
            .Distinct()
            .Select(Create)
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ToList();

        var duplicate = modules.GroupBy(m => m.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            var types = string.Join(", ", duplicate.Select(m => m.GetType().FullName));
            throw new InvalidOperationException($"Duplicate module name '{duplicate.Key}': {types}.");
        }

        foreach (var module in modules)
        {
            services.AddSingleton(typeof(IModule), module);
            module.ConfigureServices(services, configuration);
        }

        return services;
    }

    /// <summary>Let every registered module map its own endpoints. Call once from Program.cs after MapControllers.</summary>
    public static IEndpointRouteBuilder MapModuleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        foreach (var module in endpoints.ServiceProvider.GetServices<IModule>())
        {
            module.MapEndpoints(endpoints);
        }

        return endpoints;
    }

    private static IModule Create(Type type)
    {
        if (!typeof(IModule).IsAssignableFrom(type) || type.IsAbstract || type.IsInterface)
        {
            throw new InvalidOperationException($"{type.FullName} is not a concrete IModule.");
        }

        if (type.GetConstructor(Type.EmptyTypes) is null)
        {
            throw new InvalidOperationException($"Module {type.FullName} needs a public parameterless constructor.");
        }

        return (IModule)Activator.CreateInstance(type)!;
    }
}
