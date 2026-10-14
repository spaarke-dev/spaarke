// -----------------------------------------------------------------------------
// IntegrationWiringModule.cs
//
// L2 CONTROL-PLANE DI composition for the H14 post-deploy integration wiring
// handler + its H14a sub-handler (task 073; H14b/H14c removed, ISS-019) + its
// H14m customer-mailbox sub-handler (task 263).
//
// SCOPE:
//   - Bind IntegrationWiring:{ExchangeAdminAppId, ExchangeAssignmentNamePrefix,
//     KvReadTimeout,
//     SidecarBaseUrl, SidecarRequestTimeout, SidecarTransientRetryDelay,
//     SidecarSharedSecret*} options. (Task 162 W3 cleanup: removed
//     PwshExecutable / ExchangePolicyScriptPath / ExchangeScriptTimeout —
//     see IntegrationWiringOptions.cs file header for rationale.)
//   - Register the collaborator seams (IExchangePolicyApplier,
//     IExchangePolicyReadClient, IKvSecretReader)
//     + the 2 handler types (H14a sub-handler + H14 parent).
//   - Register H14IntegrationWiringHandler + its sub-handler as Scoped
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
// Worker at boot instead of surfacing only on first use.
//
// TASK 161 (Wave G-6) / TASK 251: IExchangePolicyApplier + IExchangePolicyReadClient are
// both ExchangePolicySidecarClient (typed HttpClient posting to the Listener.ps1
// sitecontainer sidecar on http://127.0.0.1:8091/apply-mailbox-access and
// /read-mailbox-access, per DS-1b §3; the pwsh shell-out applier was deleted by
// task 251). Registered via AddHttpClient&lt;TImpl&gt;() so IHttpClientFactory manages the
// underlying handler pool.
// The client depends on IKvSecretReader (task 160) for the per-boot
// X-Sidecar-Auth shared-secret read from platform KV; that seam is already
// registered above and needs no wiring change here.
//
// PLACEMENT JUSTIFICATION (CLAUDE.md §10):
//   H14 lives in L2 (not BFF) per spec §5.2 / D3 / D8 / D12; consumes NO
//   AI-internal types (ADR-013 forcing-function rule — no IActionResolver,
//   IActionRunner, IOpenAiClient, IPlaybookService injection). H14 uses
//   IProvisioningRunRepository (task 037) + the dedicated seams; no
//   BFF-facade dependencies.
// -----------------------------------------------------------------------------

using Azure.Core;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;

/// <summary>
/// DI registration for the H14 post-deploy integration wiring handler + its
/// H14a sub-handler + the collaborator seams. Composed behind a single
/// <see cref="AddH14IntegrationWiringHandler"/> extension method to minimize
/// Program.cs edit surface + avoid merge-conflict pressure with sibling
/// Batch 3F tasks (054 H11, 072 H12c) that also touch Program.cs.
/// </summary>
public static class IntegrationWiringModule
{
    /// <summary>Configuration section for H14 options.</summary>
    public const string ConfigSection = "IntegrationWiring";

    /// <summary>
    /// Registers <see cref="H14IntegrationWiringHandler"/> + its H14a sub-handler
    /// + the collaborator seams with the DI container.
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
        // The H14a Exchange sidecar (task 251): ONE typed HttpClient for the one upstream
        // (localhost:8091), serving both seams — IExchangePolicyApplier (H14a apply) and
        // IExchangePolicyReadClient (H13 T4 read-only). The client also needs the Worker's
        // ExchangeAdminTokenSource (registered by the Worker host beside the other MI-FIC
        // credential source, SpeConfidentialClientGraphFactory — both depend on the
        // Worker-only WorkerDataverseCredentialFactory).
        services.AddHttpClient<ExchangePolicySidecarClient>();
        services.AddTransient<IExchangePolicyApplier>(sp => sp.GetRequiredService<ExchangePolicySidecarClient>());
        services.AddTransient<IExchangePolicyReadClient>(sp => sp.GetRequiredService<ExchangePolicySidecarClient>());
        // Task 263: the same client serves H14m's customer-mailbox ensure and H13's read (one transport, one token).
        services.AddTransient<ICustomerMailboxClient>(sp => sp.GetRequiredService<ExchangePolicySidecarClient>());

        // Task 263: the stamp's sprk_communicationaccount row (H14m writes, H13 reads) — H7b's identity and options
        // (EnvVarValuesOptions + the Worker's WorkerDataverseCredentialFactory), its own named HttpClient.
        services.AddHttpClient(DataverseWebApiCommunicationAccountStore.HttpClientName);
        services.AddScoped<ICommunicationAccountStore, DataverseWebApiCommunicationAccountStore>();

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

        // Sub-handlers — registered by concrete type (parity with every other
        // H-series handler's DI posture; a future reconciler resolves by
        // HandlerId string match, not by IProvisioningHandler interface
        // fan-out). Each is ALSO independently resolvable/testable per the
        // POML acceptance criterion ("each sub-handler... registers in L2 DI").
        services.AddScoped<H14aExchangePolicySubHandler>();
        services.AddScoped<H14mCustomerMailboxSubHandler>();   // task 263

        // Parent handler — the ONLY one of the 3 a reconciler dispatches off the
        // Service Bus queue (HandlerId "H14"); it resolves H14a via constructor
        // injection (see H14IntegrationWiringHandler.cs file header for the
        // single-writer rationale).
        services.AddScoped<H14IntegrationWiringHandler>();

        return services;
    }
}
