// -----------------------------------------------------------------------------
// HandlerDispatchRegistrationModule.cs
//
// L2 CONTROL-PLANE keyed handler-resolution surface (task 103, Phase C''
// Wave G-1 -- closes DS-2 §3.2 gap C1.2).
//
// PURPOSE:
//   Task 102's ProvisioningHandlerDispatcher resolves the handler for an
//   incoming HandlerEnvelope via
//   scopedProvider.GetKeyedService<IProvisioningHandler>(envelope.HandlerId).
//   Before this module, only H0 had an interface-typed registration
//   (HandlersModule.cs:103, non-keyed) and zero AddKeyedScoped usage
//   existed repo-wide (DS-2 §3.1 grep audit) -- every other dispatchable
//   handler would dead-letter as NoHandler.
//
// PATTERN -- factory-forwarding keyed registration (DS-2 §3.2), one line
// per handler below: AddKeyedScoped the interface, keyed by HandlerIds.HX,
// factory-forwarding to sp.GetRequiredService of the concrete handler type.
//   This guarantees the keyed resolution and the existing concrete-type
//   resolution (still used by unit tests + H14's in-process sub-handler
//   injection) return the SAME scoped instance -- no double construction
//   of a heavy dependency graph. .NET 10 keyed DI is native; no package.
//   The concrete-type registrations themselves are UNCHANGED and continue
//   to live wherever they already do (mostly
//   Sprk.Provisioning.ControlPlane.Worker/Program.cs; H12b/H12c/H13/H14
//   via their own per-handler *Module.cs files under Handlers/**).
//
// WHY CONSOLIDATED HERE (one file) RATHER THAN DISTRIBUTED PER-HANDLER:
//   DS-2 §3.2's own text describes the *intended* end-state as "distributed
//   one-liners in the modules that own each handler" -- but the actual
//   ownership surface (DS-2 §3.1 grep audit) is NOT one module file per
//   handler: 14 of 19 dispatchable handlers are registered directly inline
//   in Sprk.Provisioning.ControlPlane.Worker/Program.cs (no owning module
//   file exists), and Program.cs is concurrently being edited by several
//   in-flight Phase C'' Wave G-2/G-3 handler-implementation tasks. Because
//   AddKeyedScoped's factory lambda is LAZY (it only executes when the
//   keyed service is actually resolved, at dispatch time -- by which point
//   the full container, built after every registration call across every
//   module regardless of call order, already includes the concrete-type
//   registration), placing all 19 lines in ONE new file here is exactly as
//   correct as distributing them, avoids a 6-file+Program.cs edit footprint
//   that would collide with those in-flight tasks, and keeps the
//   dispatchable-id surface reviewable as a single diff (matching the
//   rationale HandlersModule.cs:80-84 documents for its own explicit-list
//   preflight-probe registration). Registration COMPLETENESS is verified by
//   HandlerRegistrationCompletenessTests, not by file layout.
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Handlers.AiSearchIndex;
using Sprk.Provisioning.ControlPlane.Handlers.AiSeedChain;
using Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed;
using Sprk.Provisioning.ControlPlane.Handlers.BffDeploy;
using Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy;
using Sprk.Provisioning.ControlPlane.Handlers.BulkAppSettings;
using Sprk.Provisioning.ControlPlane.Handlers.ConsentCapture;
using Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;
using Sprk.Provisioning.ControlPlane.Handlers.DataverseEnvCreation;
using Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;
using Sprk.Provisioning.ControlPlane.Handlers.EntraAppReg;
using Sprk.Provisioning.ControlPlane.Handlers.EnvVarValues;
using Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;
using Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;
using Sprk.Provisioning.ControlPlane.Handlers.Preflight;
using Sprk.Provisioning.ControlPlane.Handlers.RuntimeReferences;
using Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;
using Sprk.Provisioning.ControlPlane.Handlers.SpeContainerType;
using Sprk.Provisioning.ControlPlane.Handlers.SubscriptionReadiness;
using Sprk.Provisioning.ControlPlane.Handlers.UserProvisioning;

namespace Sprk.Provisioning.ControlPlane.Handlers;

/// <summary>
/// Registers the keyed <see cref="IProvisioningHandler"/> resolution surface
/// consumed by task 102's <c>ProvisioningHandlerDispatcher</c>. ONE
/// <c>AddKeyedScoped</c> line per <see cref="HandlerIds.Dispatchable"/> id,
/// factory-forwarding to each handler's existing concrete-type registration.
/// </summary>
public static class HandlerDispatchRegistrationModule
{
    /// <summary>
    /// Adds one <c>AddKeyedScoped&lt;IProvisioningHandler&gt;</c> factory
    /// per dispatchable <see cref="HandlerIds"/> constant (H14a/H14b/H14c
    /// are deliberately absent -- see file header; total count is asserted
    /// by <c>HandlerRegistrationCompletenessTests</c>). Safe to call
    /// regardless of registration order relative to the concrete handler
    /// types: the factory lambda only executes when the keyed service is
    /// actually resolved, by which point the full container is built.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddProvisioningHandlerKeyedRegistrations(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddKeyedScoped<IProvisioningHandler>(
            HandlerIds.H0, (sp, _) => sp.GetRequiredService<H0PreflightHandler>());
        services.AddKeyedScoped<IProvisioningHandler>(
            HandlerIds.H05, (sp, _) => sp.GetRequiredService<H05ConsentCaptureHandler>());
        services.AddKeyedScoped<IProvisioningHandler>(
            HandlerIds.H1, (sp, _) => sp.GetRequiredService<H1SubscriptionReadinessHandler>());
        services.AddKeyedScoped<IProvisioningHandler>(
            HandlerIds.H2a, (sp, _) => sp.GetRequiredService<H2aBicepInfraDeployHandler>());
        services.AddKeyedScoped<IProvisioningHandler>(
            HandlerIds.H2b, (sp, _) => sp.GetRequiredService<H2bAiSearchIndexHandler>());
        services.AddKeyedScoped<IProvisioningHandler>(
            HandlerIds.H3, (sp, _) => sp.GetRequiredService<H3EntraAppRegHandler>());
        services.AddKeyedScoped<IProvisioningHandler>(
            HandlerIds.H4, (sp, _) => sp.GetRequiredService<H4KvSecretsPopulationHandler>());
        // Task 200: H4-shared (F19 automation) — sibling of H4; targets the
        // SHARED KV via source-service SDK extraction.
        services.AddKeyedScoped<IProvisioningHandler>(
            HandlerIds.H4Shared, (sp, _) => sp.GetRequiredService<H4SharedKvSecretsPopulationHandler>());
        // Task 201: H4b (F20/F20a automation) — BulkAppSettings handler.
        // Thin wrapper around task 084's Configure-AppServiceSettings.generated.ps1
        // (extended with per_env_settings). Runs AFTER H4 + H4-shared, BEFORE H9.
        services.AddKeyedScoped<IProvisioningHandler>(
            HandlerIds.H4b, (sp, _) => sp.GetRequiredService<H4bBulkAppSettingsHandler>());
        services.AddKeyedScoped<IProvisioningHandler>(
            HandlerIds.H5, (sp, _) => sp.GetRequiredService<H5DataverseEnvCreationHandler>());
        services.AddKeyedScoped<IProvisioningHandler>(
            HandlerIds.H6, (sp, _) => sp.GetRequiredService<H6SolutionImportHandler>());
        services.AddKeyedScoped<IProvisioningHandler>(
            HandlerIds.H7, (sp, _) => sp.GetRequiredService<H7DataverseEnvVarValuesHandler>());
        services.AddKeyedScoped<IProvisioningHandler>(
            HandlerIds.H8, (sp, _) => sp.GetRequiredService<H8SpeContainerTypeHandler>());
        services.AddKeyedScoped<IProvisioningHandler>(
            HandlerIds.H9, (sp, _) => sp.GetRequiredService<H9BffDeployHandler>());
        services.AddKeyedScoped<IProvisioningHandler>(
            HandlerIds.H10, (sp, _) => sp.GetRequiredService<H10DataverseAppUserGraphParityHandler>());
        services.AddKeyedScoped<IProvisioningHandler>(
            HandlerIds.H11, (sp, _) => sp.GetRequiredService<H11UserProvisioningHandler>());
        services.AddKeyedScoped<IProvisioningHandler>(
            HandlerIds.H12a, (sp, _) => sp.GetRequiredService<H12aAiSeedChainHandler>());
        services.AddKeyedScoped<IProvisioningHandler>(
            HandlerIds.H12b, (sp, _) => sp.GetRequiredService<H12bAppConfigSeedHandler>());
        services.AddKeyedScoped<IProvisioningHandler>(
            HandlerIds.H12c, (sp, _) => sp.GetRequiredService<H12cRuntimeReferencesHandler>());
        services.AddKeyedScoped<IProvisioningHandler>(
            HandlerIds.H13, (sp, _) => sp.GetRequiredService<H13E2EAcceptanceGateHandler>());
        services.AddKeyedScoped<IProvisioningHandler>(
            HandlerIds.H14, (sp, _) => sp.GetRequiredService<H14IntegrationWiringHandler>());

        return services;
    }
}
