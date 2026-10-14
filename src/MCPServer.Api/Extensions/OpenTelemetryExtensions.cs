using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry;
namespace MCPServer.Api;

public static class OpenTelemetryExtensions
{
    private static readonly string ServiceVersion = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "-";
    
    public static IServiceCollection AddOpenTelemetryObservability(this IServiceCollection services, IConfiguration configuration)
    {
        var endpoint = configuration["OpenTelemetry:Endpoint"] ?? "http://localhost:4317";
        var serviceName = configuration["OpenTelemetry:ServiceName"] ?? "boiler-plate";

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName, serviceVersion: ServiceVersion))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.RecordException = true;
                })
                .AddHttpClientInstrumentation(options =>
                {
                    options.RecordException = true;
                }))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter("boiler-plate"))
                .UseOtlpExporter(
                    baseUrl: new Uri(endpoint),
                    protocol: OtlpExportProtocol.Grpc);

        return services;
    }
}