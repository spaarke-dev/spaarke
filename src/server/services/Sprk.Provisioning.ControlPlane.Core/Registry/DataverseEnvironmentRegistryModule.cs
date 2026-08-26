// -----------------------------------------------------------------------------
// DataverseEnvironmentRegistryModule.cs
//
// L2 CONTROL-PLANE DI composition for the real (Path X, MI-native)
// DataverseEnvironmentRegistryClient (task 112 Wave G-1 C1.4).
//
// SPEC / ADR references:
//   - spec.md FR-38 + design.md §9.6:  Path X — MI-native admin-env registry
//                                      client via DefaultAzureCredential pinned
//                                      to the L2 UAMI. NO client secret.
//   - DS-8 §6 step 3:                  Build C1.4 registry client MI-native
//                                      from day one; two H13/H0.5 placeholders
//                                      swap onto it (task 122, task 184).
//   - ADR-010:                         Feature-module extension method — ONE
//                                      Program.cs edit line per subsystem.
//   - ADR-032 (P2):                    NullDataverseEnvironmentRegistryClient
//                                      stays as the kill-switch fallback in
//                                      the Worker's Program.cs; task 122
//                                      swaps H0.5 onto this module's real
//                                      registration; task 184 swaps H13's
//                                      IRegistrySetupStatusUpdater onto its
//                                      real impl that delegates to the client.
//                                      THIS module registers the REAL impl;
//                                      swap semantics are handled at the
//                                      composition root by the consuming task.
//   - NFR-05:                          Fail-fast validation of the bound
//                                      options at boot (parity with
//                                      CosmosModule / CustomerRunGuardModule).
//
// TASK 112 SCOPE (per POML step 3):
//   Register the real client + its options + a named HttpClient. Do NOT
//   modify the Worker's Program.cs — the parent task instructions explicitly
//   defer the DI swap to task 122 (H0.5) + task 184 (H13). This module is
//   the seam BOTH swaps consume; adding it here in a standalone module keeps
//   the Program.cs edit surface for those follow-on tasks minimal (ONE line
//   addition + ONE line change per task).
//
// PLACEMENT JUSTIFICATION (CLAUDE.md §10):
//   Not a BFF concern. Lives entirely in L2 .Core alongside sibling registry
//   types (IDataverseEnvironmentRegistryClient, NullDataverseEnvironmentRegistryClient).
//   Consumed by L2 handlers (H0.5, H13) not BFF endpoints.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Registry;

/// <summary>
/// DI registration for the real (Path X, MI-native)
/// <see cref="DataverseEnvironmentRegistryClient"/> and its bound options +
/// named HttpClient. Composed behind a single
/// <see cref="AddDataverseEnvironmentRegistry"/> extension method to minimize
/// composition-root edit surface (ADR-010 / NFR-07 god-class ratchet).
/// </summary>
public static class DataverseEnvironmentRegistryModule
{
    /// <summary>
    /// Registers <see cref="DataverseEnvironmentRegistryOptions"/> (bound
    /// from the <c>DataverseEnvironmentRegistry</c> section with fail-fast
    /// validation at startup), a named HttpClient for the client's outbound
    /// Dataverse calls, and the real <see cref="DataverseEnvironmentRegistryClient"/>
    /// as the <see cref="IDataverseEnvironmentRegistryClient"/> DI binding.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Bound configuration (reads <c>DataverseEnvironmentRegistry</c> section + falls back to <c>ManagedIdentity:ClientId</c> for the UAMI pin).</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="Microsoft.Extensions.Options.OptionsValidationException">
    /// Thrown at boot when <c>DataverseEnvironmentRegistry:AdminEnvironmentUrl</c>
    /// is missing or invalid (NFR-05 fail-fast contract).
    /// </exception>
    public static IServiceCollection AddDataverseEnvironmentRegistry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<DataverseEnvironmentRegistryOptions>()
            .Bind(configuration.GetSection(DataverseEnvironmentRegistryOptions.SectionName))
            .PostConfigure(options =>
            {
                // Parity with CosmosModule — if the section did not set an
                // MI clientId, inherit from the shared ManagedIdentity:ClientId
                // App Service setting so one property pins every module's UAMI.
                if (string.IsNullOrWhiteSpace(options.ManagedIdentityClientId))
                {
                    options.ManagedIdentityClientId = configuration["ManagedIdentity:ClientId"];
                }
            })
            .Validate(o =>
            {
                o.Validate();
                return true;
            }, "DataverseEnvironmentRegistry options failed validation — see inner exception (Validate throws).")
            .ValidateOnStart();

        // Named HttpClient — token-cache reuse across handler invocations
        // (ADR-028 UAMI-outbound MUST rule). Parity with H5 health probe +
        // I5 concurrency store's named client pattern.
        services.AddHttpClient(DataverseEnvironmentRegistryClient.HttpClientName);

        // The real client. When this module is registered by a Program.cs,
        // the resulting IDataverseEnvironmentRegistryClient resolution
        // returns THIS impl — the Null-Object is only registered by the
        // Worker's Program.cs BEFORE task 122 lands the swap; task 122
        // replaces the AddSingleton<IDataverseEnvironmentRegistryClient, NullX>
        // line with a call to this module.
        services.AddScoped<IDataverseEnvironmentRegistryClient, DataverseEnvironmentRegistryClient>();

        return services;
    }
}
