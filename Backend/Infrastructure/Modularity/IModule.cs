namespace CommonService.Infrastructure.Modularity;

/// <summary>
/// Entry point of one business module (Identity, Booking, ...). A public class implementing this
/// interface, with a public parameterless constructor, is found by <see cref="ModuleLoader"/>
/// at startup, so adding a module never requires editing Program.cs or a shared DependencyInjection file.
/// Convention: Infrastructure/Modules/&lt;Module&gt;/&lt;Module&gt;Module.cs.
/// </summary>
public interface IModule
{
    /// <summary>Unique module name (e.g. "Identity"). Used for ordering and error messages.</summary>
    string Name { get; }

    /// <summary>Register the module's own services, options, hosted services and EF configurations.</summary>
    void ConfigureServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>Map module endpoints that are not MVC controllers (for example a SignalR hub). Controllers need no mapping.</summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
