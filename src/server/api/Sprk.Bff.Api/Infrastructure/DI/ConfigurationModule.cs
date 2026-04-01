using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Api.Reporting;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Startup;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI registration module for configuration options validation (ADR-010).
/// Registers all Options&lt;T&gt; bindings with ValidateOnStart() fail-fast behavior.
/// </summary>
public static class ConfigurationModule
{
    /// <summary>
    /// Registers and validates all configuration options with fail-fast behavior.
    /// </summary>
    public static IServiceCollection AddConfigurationModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<GraphOptions>()
            .Bind(configuration.GetSection(GraphOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddOptions<DataverseOptions>()
            .Bind(configuration.GetSection(DataverseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddOptions<ServiceBusOptions>()
            .Bind(configuration.GetSection(ServiceBusOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddOptions<RedisOptions>()
            .Bind(configuration.GetSection(RedisOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Document Intelligence Options - conditional validation (only when Enabled=true)
        services
            .AddOptions<DocumentIntelligenceOptions>()
            .Bind(configuration.GetSection(DocumentIntelligenceOptions.SectionName))
            .ValidateOnStart();

        // Analysis Options - AI-driven document analysis
        services
            .AddOptions<AnalysisOptions>()
            .Bind(configuration.GetSection(AnalysisOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Model Selector Options - tiered AI model selection for cost optimization
        services
            .AddOptions<ModelSelectorOptions>()
            .Bind(configuration.GetSection(ModelSelectorOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Power BI Embedded Reporting options (PBI-001) — gated on sprk_ReportingModuleEnabled
        // Validation deferred to first use (no ValidateOnStart) so the app starts
        // even without PBI config. Reporting endpoints fail gracefully at call time.
        services
            .AddOptions<PowerBiOptions>()
            .Bind(configuration.GetSection(PowerBiOptions.SectionName))
            .ValidateDataAnnotations();

        // Custom validation for conditional requirements
        services.AddSingleton<IValidateOptions<GraphOptions>, GraphOptionsValidator>();
        services.AddSingleton<IValidateOptions<DocumentIntelligenceOptions>, DocumentIntelligenceOptionsValidator>();

        // Startup health check to validate configuration
        services.AddHostedService<StartupValidationService>();

        return services;
    }
}
