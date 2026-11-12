using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace CommonService.Infrastructure.Observability;

/// <summary>
/// Cross-cutting OpenTelemetry distributed tracing registration.
/// Traces incoming HTTP requests, EF Core SQL queries, and outbound HTTP client calls,
/// exporting spans to a standard OTLP receiver (such as Jaeger All-in-One).
/// </summary>
public static class OpenTelemetryExtensions
{
    public const string DefaultServiceName = "ChoThueGiupViec.Backend";

    public static IServiceCollection AddObservability(this IServiceCollection services, IConfiguration configuration)
    {
        var serviceName = configuration["OTEL_SERVICE_NAME"] ?? DefaultServiceName;
        var otlpEndpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://localhost:4318";

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing
                    // 1. Controller & Middleware: Inbound HTTP server requests
                    .AddAspNetCoreInstrumentation(opts =>
                    {
                        opts.RecordException = true;
                    })
                    // 2. Database: EF Core command execution
                    .AddEntityFrameworkCoreInstrumentation()
                    // 3. Outbound calls: HttpClient requests (Valhalla routing, reverse geocoding, Stripe, etc.)
                    .AddHttpClientInstrumentation()
                    // 4. Exporter: OTLP HTTP endpoint (Jaeger, Tempo, or OTel Collector)
                    .AddOtlpExporter(opts =>
                    {
                        var endpointUri = otlpEndpoint.EndsWith("/v1/traces", StringComparison.OrdinalIgnoreCase)
                            ? otlpEndpoint
                            : $"{otlpEndpoint.TrimEnd('/')}/v1/traces";
                        opts.Endpoint = new Uri(endpointUri);
                        opts.Protocol = OtlpExportProtocol.HttpProtobuf;
                    });
            });

        return services;
    }
}
