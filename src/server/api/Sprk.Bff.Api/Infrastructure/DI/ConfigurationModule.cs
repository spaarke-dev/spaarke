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
            // auth-v4 task 051 (FR-E2): reconcile the two config keys that both carried the SAS
            // credential. The section binds ServiceBus:ConnectionString, but the deployed estate
            // sets the connection string under ConnectionStrings:ServiceBus — that is what the
            // Bicep stacks emit (model1-shared.bicep:187, model2-full.bicep:199, both as
            // ConnectionStrings__ServiceBus) and it is the key live on spaarke-bff-dev today.
            // Meanwhile scripts/Configure-ProductionAppSettings.ps1:85 sets the OTHER key,
            // ServiceBus__ConnectionString. Both spellings were live simultaneously.
            //
            // Until task 033 removes the SAS path outright, back-fill from the legacy key so the
            // options object is the single source of truth for every consumer. Without this,
            // moving client construction onto ServiceBusOptions would read an EMPTY connection
            // string on the deployed app and take background job processing down — the namespace
            // is not configured yet, so there would be no credential at all.
            .PostConfigure<IConfiguration>((options, config) =>
            {
                if (string.IsNullOrWhiteSpace(options.ConnectionString))
                {
                    options.ConnectionString = config.GetConnectionString("ServiceBus") ?? string.Empty;
                }
            })
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddOptions<RedisOptions>()
            .Bind(configuration.GetSection(RedisOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Public runtime config (FR-36 — customer-provisioning-orchestration-r1 task 087).
        // Tier-1 fail-fast in DEPLOYED envs: PublicConfigOptionsValidator enforces
        // BffUrl + MsalClientId + TenantId at startup for Production / Staging / Demo / QA;
        // Development + Testing envs short-circuit (per .claude/constraints/bff-extensions.md
        // §F.2.1 Testing allow-list stance) so the 30+ per-endpoint test fixtures don't each
        // need to add PublicConfig:* entries. FeatureFlags is optional (empty dict is valid).
        // Consumed by GET /api/config (ConfigEndpoints.MapPublicConfigEndpoint). NO
        // .ValidateDataAnnotations() — the validator is the single source of truth for
        // requiredness semantics (mirrors the AgentServiceOptions r3 task 061 pattern).
        services
            .AddOptions<PublicConfigOptions>()
            .Bind(configuration.GetSection(PublicConfigOptions.SectionName))
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
        // task 061 fail-fast sweep: the Enabled→Endpoint/AgentId "required-WHEN-enabled" invariant is now
        // enforced AT STARTUP via AgentServiceOptionsValidator (IValidateOptions) + ValidateOnStart, instead
        // of deferring to the first agent call. This is SAFE for the Enabled=false boot path: the validator
        // short-circuits to Success when disabled, and Endpoint/AgentId are NOT bare [Required] on the class
        // (so DataAnnotations pass regardless of Enabled — no eager-.Value 2026-06-09 BingGrounding regression).
        // App still starts cleanly with Enabled=false and no Foundry config; a misconfigured Enabled=true
        // (missing Endpoint/AgentId) now fails startup naming the keys.
        services
            .AddOptions<AgentServiceOptions>()
            .Bind(configuration.GetSection(AgentServiceOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Code Interpreter options (AIPU-070) — gated on CodeInterpreter:Enabled kill switch (ADR-018).
        // Validation deferred (no ValidateOnStart) so the app starts cleanly when disabled.
        // CodeInterpreterHandler checks Enabled before every sandbox invocation.
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

        // FR-P3-01 (ai-architecture-redesign-r1 task 040): the Workspace typed-options binding (and its
        // deprecation-warning validator) was DELETED with the hard cutover to the
        // sprk_playbookconsumer Binding routing table — no code reads the "Workspace"
        // configuration section anymore. Playbook resolution for matter-pre-fill,
        // project-pre-fill, ai-summary, and summarize-file flows exclusively through
        // IConsumerRoutingService (see Infrastructure/DI/RoutingModule.cs).

        // SharePoint Embedded options — StagingContainerId used by pre-fill services (matter/project)
        // for staged file uploads. Nullable with code-side fallback (in-memory text extraction when
        // unset), so binding succeeds even when the "SharePointEmbedded" section is absent.
        services
            .AddOptions<SharePointEmbeddedOptions>()
            .Bind(configuration.GetSection(SharePointEmbeddedOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Sharing-link bounds for POST /api/documents/{documentId}/share-link
        // (unified-access-control-r2 task 072). ValidateOnStart is load-bearing here rather than
        // cosmetic: the [Range] ceilings ARE the security guarantee — a minted SPE URL survives
        // Dataverse revocation, so lifetime is this route's only revocation mechanism, and an operator
        // must not be able to configure an effectively-permanent link. Failing startup on a bad value is
        // the right direction; silently clamping would hide the misconfiguration. An absent section binds
        // valid defaults (14d / 7d / anonymous enabled) and boots.
        services
            .AddOptions<ShareLinkOptions>()
            .Bind(configuration.GetSection(ShareLinkOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // FR-P2-06 (task 035): the FR-46/FR-47 classifier-stack option bindings
        // (candidate-selector thresholds + reranker tuning knobs) were DELETED with
        // the dispatcher stack — no code reads their configuration sections anymore.

        // Custom validation for conditional requirements
        services.AddSingleton<IValidateOptions<GraphOptions>, GraphOptionsValidator>();
        services.AddSingleton<IValidateOptions<DocumentIntelligenceOptions>, DocumentIntelligenceOptionsValidator>();
        // task 061: Enabled→Endpoint cross-property invariant for the gated Foundry Agent option.
        services.AddSingleton<IValidateOptions<AgentServiceOptions>, AgentServiceOptionsValidator>();
        // customer-provisioning-orchestration-r1 task 087 (FR-36): env-aware fail-fast for
        // PublicConfigOptions — enforce in Production / Staging / Demo / QA; short-circuit
        // in Development / Testing envs so test fixtures don't need PublicConfig:* entries.
        services.AddSingleton<IValidateOptions<PublicConfigOptions>, PublicConfigOptionsValidator>();

        // Startup health check to validate configuration
        services.AddHostedService<StartupValidationService>();

        return services;
    }
}
