using CommonService.Application;
using CommonService.Application.Features.Dispatch;
using CommonService.Infrastructure;
using CommonService.Infrastructure.Swagger;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CommonService.Tests.Dispatch;

/// <summary>
/// DISPATCH-WIRE-01 (#308): the real application container (same registrations as Program.cs) builds with scope and on-build validation,
/// and every MediatR notification handler resolves. Before this ticket <see cref="OrderPaidDispatchHandler"/> could not be activated
/// (DispatchOfferEngine was never registered), so publishing <c>OrderPaid</c> after the IPN commit threw.
/// Building the container opens no database connection.
/// </summary>
public class DispatchContainerTests
{
    private static WebApplication BuildApp()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HARNESS.md")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
            ContentRootPath = Path.Combine(dir.FullName, "Backend"),
        });
        builder.Host.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = true;
            options.ValidateOnBuild = true;
        });

        builder.Services.AddControllers();
        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.AddConfiguredSwagger();

        return builder.Build(); // ValidateOnBuild: throws if any registered service cannot be constructed
    }

    [Fact]
    public void Every_notification_handler_resolves_in_the_real_container()
    {
        using var app = BuildApp();
        using var scope = app.Services.CreateScope();

        var handlerTypes = typeof(OrderPaidDispatchHandler).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false })
            .SelectMany(t => t.GetInterfaces())
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INotificationHandler<>))
            .Distinct()
            .ToList();

        Assert.NotEmpty(handlerTypes);
        foreach (var handlerType in handlerTypes)
        {
            var resolved = scope.ServiceProvider.GetServices(handlerType).ToList();
            Assert.NotEmpty(resolved);
        }
    }

    [Fact]
    public void OrderPaid_reaches_the_dispatch_engine()
    {
        using var app = BuildApp();
        using var scope = app.Services.CreateScope();

        var handlers = scope.ServiceProvider.GetServices<INotificationHandler<CommonService.Domain.Events.OrderPaid>>();

        Assert.Contains(handlers, h => h is OrderPaidDispatchHandler);
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<DispatchOfferEngine>());
        Assert.IsType<CommonService.Infrastructure.Modules.Dispatch.EfDispatchRepository>(
            scope.ServiceProvider.GetRequiredService<IDispatchRepository>());
    }
}
