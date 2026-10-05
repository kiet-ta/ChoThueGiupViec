using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;

namespace CommonService.Infrastructure.Swagger;

/// <summary>
/// Configures Swagger OpenAPI documentation with consistent operation IDs,
/// Bearer authentication scheme, and API metadata (BASE-13, decisions Q16).
/// </summary>
public static class SwaggerConfiguration
{
    public static IServiceCollection AddConfiguredSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Cho Thue Giup Viec API",
                Version = "v1",
                Description = "Cho Thue Giup Viec multi-module platform API (PRN232)."
            });

            // Consistent, deterministic operationId: {Controller}_{Action}
            options.CustomOperationIds(apiDesc =>
            {
                var controller = apiDesc.ActionDescriptor.RouteValues["controller"] ?? "Api";
                var action = apiDesc.ActionDescriptor.RouteValues["action"] ?? apiDesc.HttpMethod ?? "Action";
                return $"{controller}_{action}";
            });

            // Bearer JWT security scheme
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Description = "JWT Authorization header using the Bearer scheme. Enter token without Bearer prefix or 'Bearer {token}'.",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });
        });

        return services;
    }
}
