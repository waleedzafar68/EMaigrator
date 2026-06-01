using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

namespace EMaigrator.Infrastructure.Observability;

/// <summary>
/// Composition helpers that wire OpenTelemetry traces/metrics and the scrubbing Serilog pipeline
/// into the host's <see cref="IServiceCollection"/>.
/// </summary>
public static class ObservabilityExtensions
{
    /// <summary>
    /// Registers OpenTelemetry traces + metrics (OTLP exporter) and a Serilog logger with the
    /// secret-scrubbing enricher and OTLP log sink. OTLP endpoint comes from OTEL_EXPORTER_OTLP_ENDPOINT.
    /// </summary>
    public static IServiceCollection AddEmaigratorObservability(this IServiceCollection services, IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        var serviceName = config["OTEL_SERVICE_NAME"] ?? "emaigrator";

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(t => t
                .AddSource(Telemetry.SourceName)
                .AddOtlpExporter())
            .WithMetrics(m => m
                .AddMeter(Telemetry.SourceName)
                .AddRuntimeInstrumentation()
                .AddOtlpExporter());

        Log.Logger = BuildLogger(config, serviceName);
        services.AddLogging(b => b.AddSerilog(Log.Logger, dispose: false));
        return services;
    }

    /// <summary>Builds the scrubbing Serilog logger; exposed so tests and hosts share one config.</summary>
    public static Serilog.ILogger BuildLogger(IConfiguration config, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(config);

        var cfg = new LoggerConfiguration()
            .Enrich.With(new SecretScrubbingEnricher())
            .Enrich.WithProperty("service.name", serviceName)
            .MinimumLevel.Information()
            .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture);

        var otlp = config["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(otlp))
        {
            cfg = cfg.WriteTo.OpenTelemetry(o =>
            {
                o.Endpoint = otlp;
                o.ResourceAttributes = new Dictionary<string, object> { ["service.name"] = serviceName };
            });
        }

        return cfg.CreateLogger();
    }
}
