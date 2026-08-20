// -----------------------------------------------------------------------------
// IntegrationWiringModule.cs
//
// L2 CONTROL-PLANE DI composition for the H14 post-deploy integration wiring
// handler + its 3 DAG-parallel sub-handlers (task 073).
//
// SCOPE:
//   - Bind IntegrationWiring:{ExchangePolicyDescriptionPrefix,
//     GraphRequestTimeout, GraphSubscriptionExpirationMinutes,
//     DataverseRequestTimeout, ServiceEndpoint*, KvReadTimeout,
//     SidecarBaseUrl, SidecarRequestTimeout, SidecarTransientRetryDelay,
//     SidecarSharedSecret*} options. (Task 162 W3 cleanup: removed
//     PwshExecutable / ExchangePolicyScriptPath / ExchangeScriptTimeout —
//     see IntegrationWiringOptions.cs file header for rationale.)
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
// TASK 160 (Wave G-6): IKvSecretReader swapped from AzCliKvSecretReader (`az
// keyvault secret show` shell-out, RETIRED — kept on disk unregistered, see
// that file's retirement banner) to SecretClientKvReader
// (Azure.Security.KeyVault.Secrets SDK port, the read-side counterpart to
// task 125's SecretClientKvWriter). Registration moved from a plain
// AddSingleton&lt;TInterface, TImpl&gt;() to a factory lambda because the new
// reader needs the shared UAMI-pinned TokenCredential singleton (already
// registered by AddCosmosModule, ADR-028 MI-outbound — NO second credential
// chain) — parity with SecretClientKvWriter's own factory-lambda
// registration in Worker/Program.cs. Options binding also swapped from a
// plain Configure&lt;T&gt;() to AddOptions&lt;T&gt;().Bind().Validate().ValidateOnStart()
// (NFR-05 fail-fast parity with task 153's RuntimeReferencesModule / task
// 151's AppConfigSeedModule) so a misconfigured KvReadTimeout fails the
// Worker at boot instead of surfacing only on H14b/H14c's first dispatch.
//
// TASK 161 (Wave G-6): IExchangePolicyApplier swapped from
// ExchangePolicyScriptApplier (pwsh + Set-ExchangeApplicationAccessPolicy.ps1
// shell-out, RETIRED — kept on disk unregistered, see that file's retirement
// banner) to ExchangePolicySidecarClient (typed HttpClient posting to task
// 114's Listener.ps1 sitecontainer sidecar on
// http://127.0.0.1:8091/apply-policy, per DS-1b §3). Registered via
// AddHttpClient&lt;TInterface, TImpl&gt;() — parity with GraphRestSubscriptionCreator
// / DataverseWebApiServiceEndpointWebhookRegistrar's own typed-HttpClient
// registrations — so IHttpClientFactory manages the underlying handler pool.
// The client depends on IKvSecretReader (task 160) for the per-boot
// X-Sidecar-Auth shared-secret read from platform KV; that seam is already
// registered above and needs no wiring change here.
//
// PLACEMENT JUSTIFICATION (CLAUDE.md §10):
//   H14 lives in L2 (not BFF) per spec §5.2 / D3 / D8 / D12; consumes NO
//   AI-internal types (ADR-013 forcing-function rule — no IActionResolver,
//   IActionRunner, IOpenAiClient, IPlaybookService injection). H14 uses
//   IProvisioningRunRepository (task 037) + the 4 dedicated seams; no
//   BFF-facade dependencies.
// -----------------------------------------------------------------------------

using Azure.Core;
using Microsoft.Extensions.Options;

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

        // Task 160 (Wave G-6): AddOptions<T>().Bind().Validate().ValidateOnStart()
        // replaces the plain Configure<T>() call — NFR-05 fail-fast parity
        // with RuntimeReferencesModule (task 153) / AppConfigSeedModule (task
        // 151). See IntegrationWiringOptions.Validate for the scoped
        // (KvReadTimeout-only) bounds check.
        services.AddOptions<IntegrationWiringOptions>()
            .Bind(configuration.GetSection(ConfigSection))
            .Validate(o =>
            {
                o.Validate();
                return true;
            }, "IntegrationWiring options failed validation — see inner exception (Validate throws).")
            .ValidateOnStart();

        // Collaborator seams — one production impl each (ADR-010 ≥2-impl
        // justification: the 2nd impl is the per-unit-test fake).
        //
        // Task 161 (Wave G-6): IExchangePolicyApplier bound to the new
        // ExchangePolicySidecarClient (typed HttpClient) — replaces the
        // retired ExchangePolicyScriptApplier's pwsh shell-out. Underlying
        // HttpClient is IHttpClientFactory-managed (handler pool) — parity
        // with GraphRestSubscriptionCreator's own AddHttpClient<> below.
        services.AddHttpClient<IExchangePolicyApplier, ExchangePolicySidecarClient>();

        // Task 180 (Wave G-7): IExchangePolicyReadClient bound to the new
        // ExchangePolicySidecarReadClient (typed HttpClient) -- the sibling
        // READ-ONLY client for the H13 T4 acceptance-gate probe. Same
        // sidecar (task 114's Listener.ps1), same auth model + shared
        // secret KV read, disjoint GET /policies route (task 180's route
        // extension). Registered here (not in E2EAcceptanceModule) because
        // the sidecar transport + KV secret plumbing lives in this module;
        // the T4 probe (ExchangePolicyCountT4Probe) will consume this
        // interface once assembly task 185 wires it into the aggregate
        // IE2ETrapVerifier composition, per the sibling standalone-probe
        // convention (T1/T5/T6 file headers).
        services.AddHttpClient<IExchangePolicyReadClient, ExchangePolicySidecarReadClient>();

        // Task 160: SecretClientKvReader needs the shared UAMI-pinned
        // TokenCredential singleton (AddCosmosModule, ADR-028 MI-outbound) —
        // factory-lambda registration, parity with SecretClientKvWriter's own
        // registration in Worker/Program.cs (AzCliKvSecretReader only needed
        // an ILogger, so its retired registration was a plain type mapping).
        services.AddSingleton<IKvSecretReader>(sp =>
        {
            var credential = sp.GetRequiredService<TokenCredential>();
            var options = sp.GetRequiredService<IOptions<IntegrationWiringOptions>>();
            var logger = sp.GetRequiredService<ILogger<SecretClientKvReader>>();
            return new SecretClientKvReader(credential, options, logger);
        });
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
