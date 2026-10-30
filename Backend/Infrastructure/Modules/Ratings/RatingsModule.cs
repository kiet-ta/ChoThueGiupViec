using CommonService.Application.Features.Ratings;
using CommonService.Application.Features.Ratings.Services;
using CommonService.Infrastructure.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CommonService.Infrastructure.Modules.Ratings;

/// <summary>Module registration for Ratings (M6). Self-registers via IModule (BASE-02).</summary>
public sealed class RatingsModule : IModule
{
    public string Name => "Ratings";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IRatingRepository, EfRatingRepository>();
        services.AddScoped<IRatingService, RatingService>();

        // What a saved rating moves: WORKER.rating_avg and the Q14 flags (the MediatR handler is found by assembly scanning).
        services.AddScoped<IWorkerRatingStore, EfWorkerRatingStore>();
    }
}
