using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace CommonService.Infrastructure.Observability;

public static class OpenTelemetryExtensions
{
    public const string DefaultServiceName = "ChoThueGiupViec-Backend";

    public static IServiceCollection AddOpenTelemetryObservability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var serviceName = configuration["OpenTelemetry:ServiceName"]
            ?? configuration["OTEL_SERVICE_NAME"]
            ?? DefaultServiceName;
        var otlpEndpoint = configuration["OpenTelemetry:Otlp:Endpoint"]
            ?? configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
            ?? "http://tempo:4317";

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName: serviceName))
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddPrometheusExporter();
            })
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                    })
                    .AddHttpClientInstrumentation()
                    .AddEntityFrameworkCoreInstrumentation()
                    .AddOtlpExporter(options =>
                    {
                        if (!string.IsNullOrWhiteSpace(otlpEndpoint) &&
                            Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out var uri))
                        {
                            options.Endpoint = uri;
                        }
                    });
            });

        return services;
    }

    public static IServiceCollection AddObservability(
        this IServiceCollection services,
        IConfiguration configuration) => services.AddOpenTelemetryObservability(configuration);
}
