// -----------------------------------------------------------------------------
// IntegrationWiringModule.cs
//
// L2 CONTROL-PLANE DI composition for the H14 post-deploy integration wiring
// handler + its 3 DAG-parallel sub-handlers (task 073).
//
// SCOPE:
//   - Bind IntegrationWiring:{PwshExecutable, ExchangePolicyScriptPath,
//     ExchangeScriptTimeout, ExchangePolicyDescriptionPrefix,
//     GraphRequestTimeout, GraphSubscriptionExpirationMinutes,
//     DataverseRequestTimeout, ServiceEndpoint*} options.
//   - Register the 7 collaborator seams (IExchangePolicyApplier,
//     IKvSecretReader, IGraphSubscriptionCreator, IServiceEndpointWebhookRegistrar)
//     + the 4 handler types (H14a/b/c sub-handlers + H14 parent).
//   - Register H14IntegrationWiringHandler + its 3 sub-handlers as Scoped
//     (parity with every other H-series handler's DI lifetime).
//
// UNCONDITIONAL REGISTRATION (ADR-032): every registration below is
// UNCONDITIONAL — no feature-gate branch.
//
// PATTERN PARITY: single AddH14IntegrationWiringHandler() extension method
// keeps Program.cs additions to ONE new line (NFR-07 god-class ratchet;
// ADR-010 DI minimalism), same posture as task 071's
// AddH12bAppConfigSeedHandler() — and, per this batch's dispatcher context,
// Program.cs is a SHARED file across 3 parallel sibling tasks (054/072/073).
// A single-line addition minimizes merge-conflict surface with those siblings.
//
// PLACEMENT JUSTIFICATION (CLAUDE.md §10):
//   H14 lives in L2 (not BFF) per spec §5.2 / D3 / D8 / D12; consumes NO
//   AI-internal types (ADR-013 forcing-function rule — no IActionResolver,
//   IActionRunner, IOpenAiClient, IPlaybookService injection). H14 uses
//   IProvisioningRunRepository (task 037) + the 4 dedicated seams; no
//   BFF-facade dependencies.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;

/// <summary>
/// DI registration for the H14 post-deploy integration wiring handler + its 3
/// sub-handlers + 4 collaborator seams. Composed behind a single
/// <see cref="AddH14IntegrationWiringHandler"/> extension method to minimize
/// Program.cs edit surface + avoid merge-conflict pressure with sibling
/// Batch 3F tasks (054 H11, 072 H12c) that also touch Program.cs.
/// </summary>
public static class IntegrationWiringModule
{
    /// <summary>Configuration section for H14 options.</summary>
    public const string ConfigSection = "IntegrationWiring";

    /// <summary>
    /// Registers <see cref="H14IntegrationWiringHandler"/> + its 3 sub-handlers
    /// + 4 collaborator seams with the DI container.
    /// </summary>
    public static IServiceCollection AddH14IntegrationWiringHandler(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<IntegrationWiringOptions>(configuration.GetSection(ConfigSection));

        // Collaborator seams — one production impl each (ADR-010 ≥2-impl
        // justification: the 2nd impl is the per-unit-test fake).
        services.AddSingleton<IExchangePolicyApplier, ExchangePolicyScriptApplier>();
        services.AddSingleton<IKvSecretReader, AzCliKvSecretReader>();
        services.AddHttpClient<IGraphSubscriptionCreator, GraphRestSubscriptionCreator>();
        services.AddHttpClient<IServiceEndpointWebhookRegistrar, DataverseWebApiServiceEndpointWebhookRegistrar>();

        // Sub-handlers — registered by concrete type (parity with every other
        // H-series handler's DI posture; a future reconciler resolves by
        // HandlerId string match, not by IProvisioningHandler interface
        // fan-out). Each is ALSO independently resolvable/testable per the
        // POML acceptance criterion ("each sub-handler... registers in L2 DI").
        services.AddScoped<H14aExchangePolicySubHandler>();
        services.AddScoped<H14bGraphWebhookSubHandler>();
        services.AddScoped<H14cDataverseWebhookSubHandler>();

        // Parent handler — the ONLY one of the 4 a future reconciler would
        // dispatch off the Service Bus queue (HandlerId "H14"); it resolves
        // the 3 sub-handlers via constructor injection + Task.WhenAll
        // (see H14IntegrationWiringHandler.cs file header for the DAG-parallel
        // single-writer rationale).
        services.AddScoped<H14IntegrationWiringHandler>();

        return services;
    }
}
