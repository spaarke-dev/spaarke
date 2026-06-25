using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Api.Reporting;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Startup;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Foundry;

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

        // Agent Service options (AIPU-061) — gated on AgentService:Enabled kill switch (ADR-018).
        // Validation deferred (no ValidateOnStart): Endpoint/AgentId are [Required] but only
        // needed when Enabled=true. App starts cleanly with Enabled=false and no Foundry config.
        // AgentServiceClient.GuardEnabled() enforces the kill switch at call time.
        services
            .AddOptions<AgentServiceOptions>()
            .Bind(configuration.GetSection(AgentServiceOptions.SectionName))
            .ValidateDataAnnotations();

        // Code Interpreter options (AIPU-070) — gated on CodeInterpreter:Enabled kill switch (ADR-018).
        // Validation deferred (no ValidateOnStart) so the app starts cleanly when disabled.
        // CodeInterpreterTools checks Enabled before every sandbox invocation.
        services
            .AddOptions<Sprk.Bff.Api.Services.Ai.Foundry.CodeInterpreterOptions>()
            .Bind(configuration.GetSection(Sprk.Bff.Api.Services.Ai.Foundry.CodeInterpreterOptions.SectionName))
            .ValidateDataAnnotations();

        // Bing Grounding options (AIPU-071) — gated on BingGrounding:Enabled kill switch (ADR-018).
        // BingConnectionName is NOT [Required] at the option-class level (removed Wave B-G8
        // 2026-06-09 after a startup crash on Spaarke Dev: LegalResearchHandler ctor calls
        // .Value which triggered DataAnnotation eagerly even though comment said "validation
        // deferred"). Required-when-Enabled semantics enforced at use-site in
        // LegalResearchHandler.RunBingGroundingAsync; kill switch at the call sites already
        // prevents the use-site code from running when Enabled=false.
        services
            .AddOptions<Sprk.Bff.Api.Services.Ai.Foundry.BingGroundingOptions>()
            .Bind(configuration.GetSection(Sprk.Bff.Api.Services.Ai.Foundry.BingGroundingOptions.SectionName))
            .ValidateDataAnnotations();

        // Workspace options — pre-fill / AI summary playbook IDs used by MatterPreFillService,
        // ProjectPreFillService, and WorkspaceAiService. All properties are nullable with code-side
        // fallbacks to hardcoded defaults, so validation is deferred (no ValidateOnStart) and the
        // app starts cleanly when the "Workspace" section is absent.
        services
            .AddOptions<WorkspaceOptions>()
            .Bind(configuration.GetSection(WorkspaceOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // SharePoint Embedded options — StagingContainerId used by pre-fill services (matter/project)
        // for staged file uploads. Nullable with code-side fallback (in-memory text extraction when
        // unset), so binding succeeds even when the "SharePointEmbedded" section is absent.
        services
            .AddOptions<SharePointEmbeddedOptions>()
            .Bind(configuration.GetSection(SharePointEmbeddedOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Custom validation for conditional requirements
        services.AddSingleton<IValidateOptions<GraphOptions>, GraphOptionsValidator>();
        services.AddSingleton<IValidateOptions<DocumentIntelligenceOptions>, DocumentIntelligenceOptionsValidator>();
        // Phase 1R FR-1R-06: deprecation warning when any Workspace__*PlaybookId env var
        // is set (routing now lives in sprk_playbookconsumer Dataverse table; env vars
        // are graceful-degrade fallback only during the deprecation window).
        services.AddSingleton<IValidateOptions<WorkspaceOptions>, WorkspaceOptionsValidator>();

        // Startup health check to validate configuration
        services.AddHostedService<StartupValidationService>();

        return services;
    }
}
