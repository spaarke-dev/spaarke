// -----------------------------------------------------------------------------
// RuntimeReferencesModule.cs
//
// L2 CONTROL-PLANE DI composition for the H12c runtime references handler
// (task 072 — wave Cp DAG-join point requiring H12a + H12b).
//
// SCOPE:
//   - Bind RuntimeReferences:{SharedPlatformOpenAiEndpoint, DataverseRequestTimeout}
//     options.
//   - Register IModelDeploymentReferenceWriter as a typed HttpClient
//     (DefaultAzureCredential token cache reused across invocations, parity
//     with H10's Dataverse/Graph typed-HttpClient registrations).
//   - Register H12cRuntimeReferencesHandler as Scoped (parity with H0/H0.5/
//     H1/H2a/H12a/H12b handler scoping).
//
// UNCONDITIONAL REGISTRATION (ADR-032): no feature-gate branch.
//
// PLACEMENT JUSTIFICATION (CLAUDE.md §10): H12c lives in L2 (not BFF) per
// spec §5.2 / D3 / D8 / D12; it consumes NO AI-internal types (ADR-013
// forcing-function rule — no IActionResolver, IActionRunner, IOpenAiClient,
// IPlaybookService injection). It uses IProvisioningRunRepository (task 037)
// + IHandlerEnqueuer (task 038) + the local IModelDeploymentReferenceWriter
// seam; no BFF-facade dependencies.
//
// Extension-method pattern mirrors AppConfigSeedModule.AddH12bAppConfigSeedHandler
// — keeps the Program.cs edit surface to a single new line + avoids merge-
// conflict pressure against sibling handler tasks landing in the same
// Program.cs during this parallel batch (per task 050/051/053 precedent).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.RuntimeReferences;

/// <summary>
/// DI registration for the H12c runtime references handler + its
/// <see cref="IModelDeploymentReferenceWriter"/> dependency.
/// </summary>
public static class RuntimeReferencesModule
{
    /// <summary>Configuration section for runtime-references options.</summary>
    public const string ConfigSection = "RuntimeReferences";

    /// <summary>
    /// Registers <see cref="H12cRuntimeReferencesHandler"/> + its
    /// <see cref="IModelDeploymentReferenceWriter"/> dependency with the DI
    /// container.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Configuration (reads the <c>RuntimeReferences</c> section).</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddH12cRuntimeReferencesHandler(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<RuntimeReferencesOptions>(configuration.GetSection(ConfigSection));

        services.AddHttpClient<IModelDeploymentReferenceWriter, DataverseWebApiModelDeploymentReferenceWriter>();

        services.AddScoped<H12cRuntimeReferencesHandler>();

        return services;
    }
}
