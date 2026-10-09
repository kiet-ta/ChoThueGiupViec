using CommonService.Application.Features.Booking;
using CommonService.Application.Features.Booking.Services;
using CommonService.Infrastructure.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CommonService.Infrastructure.Modules.Booking;

public class BookingModule : IModule
{
    public string Name => "Booking";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IPriceRuleRepository, PriceRuleRepository>();
        services.AddScoped<IPricingService, PricingService>();
        services.AddScoped<IOrderTrackingQuery, OrderTrackingQuery>();
    }
}
