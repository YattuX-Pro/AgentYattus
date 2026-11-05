using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace AgentYattus.Api;

internal static class Telemetry
{
    /// <summary>Nom commun des traces et des métriques émises par les agents (appels au modèle, exécutions d'agent).</summary>
    public const string SourceName = "AgentYattus";

    public static IServiceCollection AddTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        var otel = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("AgentYattus.Api"))
            .WithTracing(tracing => tracing.AddSource(SourceName).AddAspNetCoreInstrumentation())
            .WithMetrics(metrics => metrics.AddMeter(SourceName).AddAspNetCoreInstrumentation());

        // On n'envoie les données que si une destination est configurée (par exemple le tableau de bord Aspire).
        if (!string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            otel.UseOtlpExporter();

        return services;
    }
}
