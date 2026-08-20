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
//
// TASK 153 (Wave G-5): replaced the plain Configure<RuntimeReferencesOptions>()
// call with AddOptions<T>().Bind().Validate().ValidateOnStart() — parity with
// DataverseEnvironmentRegistryModule.AddDataverseEnvironmentRegistry (task 122)
// — so a boot-time DataverseRequestTimeout misconfiguration fails loud instead
// of only surfacing on H12c's first dispatch. See RuntimeReferencesOptions.Validate
// doc comment for why SharedPlatformOpenAiEndpoint is NOT part of this
// boot-time check. Program.cs's single AddH12cRuntimeReferencesHandler(...)
// call line is unchanged — the god-class-ratchet extension-method surface
// this module exists for stays a one-line composition-root edit.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.RuntimeReferences;

/// <summary>
/// DI registration for the H12c runtime references handler + its
/// <see cref="IModelDeploymentReferenceWriter"/> dependency.
/// </summary>
public static class RuntimeReferencesModule
{
    /// <summary>
    /// Registers <see cref="H12cRuntimeReferencesHandler"/> + its
    /// <see cref="IModelDeploymentReferenceWriter"/> dependency with the DI
    /// container. Binds <see cref="RuntimeReferencesOptions"/> from its
    /// <see cref="RuntimeReferencesOptions.SectionName"/> section with
    /// fail-fast validation at startup (NFR-05, task 153).
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Configuration (reads the <c>RuntimeReferences</c> section).</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="Microsoft.Extensions.Options.OptionsValidationException">
    /// Thrown at boot when <c>RuntimeReferences:DataverseRequestTimeout</c> is out of bounds (NFR-05 fail-fast contract).
    /// </exception>
    public static IServiceCollection AddH12cRuntimeReferencesHandler(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<RuntimeReferencesOptions>()
            .Bind(configuration.GetSection(RuntimeReferencesOptions.SectionName))
            .Validate(o =>
            {
                o.Validate();
                return true;
            }, "RuntimeReferences options failed validation — see inner exception (Validate throws).")
            .ValidateOnStart();

        services.AddHttpClient<IModelDeploymentReferenceWriter, DataverseWebApiModelDeploymentReferenceWriter>();

        services.AddScoped<H12cRuntimeReferencesHandler>();

        return services;
    }
}
