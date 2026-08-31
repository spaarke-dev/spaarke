// -----------------------------------------------------------------------------
// DataverseEnvironmentRegistryOptions.cs
//
// L2 CONTROL-PLANE bound options for the real DataverseEnvironmentRegistryClient
// (task 112 — Path X MI-native admin-env Dataverse client).
//
// Bound from IConfiguration section "DataverseEnvironmentRegistry" via
// <see cref="DataverseEnvironmentRegistryModule.AddDataverseEnvironmentRegistry"/>.
// Fails fast at startup on invalid values (NFR-05 parity with
// CustomerRunGuardOptions / CosmosModule / ServiceBusModule).
//
// AUTH SHAPE (Path X — the reason this class exists):
//   `DefaultAzureCredential` with `ManagedIdentityClientId` set to the L2
//   UAMI's clientId. NO ClientSecret. NO TenantId cross-flow (the admin env
//   lives in the SPAARKE platform tenant, same tenant as the UAMI — plain
//   UAMI suffices; cross-tenant would require FIC/multitenant-app-reg).
//
//   Contrast: CustomerRunGuardOptions (task 059) still ships with
//   `ClientId + ClientSecret` for the BFF app-reg client-credentials flow
//   because pre-task-111 the L2 UAMI was NOT registered as a Dataverse App
//   User on the admin env. Task 111's Grant-ControlPlaneIdentity.ps1 fixes
//   that pre-condition, and DS-8 §6 step 3 mandates C1.4's registry client
//   be Path X from day one. When the guard is later migrated (per
//   CustomerRunGuardOptions FUTURE MIGRATION note, out of THIS task's scope),
//   it will consume THIS options shape too.
//
// FIELD DERIVATION:
//   - AdminEnvironmentUrl:      REQUIRED. Absolute URI (e.g. https://spaarkedev1.crm.dynamics.com).
//   - ManagedIdentityClientId:  Optional in this section; if absent, the module
//                               falls back to `ManagedIdentity:ClientId`
//                               (parity with CosmosModule.cs precedent so a
//                               single App Service setting drives every
//                               module's UAMI pin). Local dev without an MI
//                               attached chains through DefaultAzureCredential
//                               to AzureCliCredential — no code change needed.
//   - EntitySetName:            Plural OData set for sprk_dataverseenvironment.
//                               Defaults to "sprk_dataverseenvironments"
//                               (parity with CustomerRunGuardOptions.EntitySetName).
//   - RequestTimeout:           Per-request timeout. Default 30s.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Registry;

/// <summary>
/// Bound options for <see cref="DataverseEnvironmentRegistryClient"/> — the
/// real Path X (MI-native) admin-env registry client (task 112). Section
/// name = <c>DataverseEnvironmentRegistry</c>.
/// </summary>
public sealed class DataverseEnvironmentRegistryOptions
{
    /// <summary>Configuration section name (bound via <see cref="DataverseEnvironmentRegistryModule.AddDataverseEnvironmentRegistry"/>).</summary>
    public const string SectionName = "DataverseEnvironmentRegistry";

    /// <summary>
    /// Admin Dataverse environment URL (e.g. <c>https://spaarkedev1.crm.dynamics.com</c>).
    /// Must be an absolute URI. REQUIRED — no default. Read/PATCH calls target
    /// this environment's <c>sprk_dataverseenvironment</c> entity set.
    /// </summary>
    public string? AdminEnvironmentUrl { get; set; }

    /// <summary>
    /// L2 UAMI clientId used to pin <see cref="Azure.Identity.DefaultAzureCredentialOptions.ManagedIdentityClientId"/>.
    /// Optional in this section — <see cref="DataverseEnvironmentRegistryModule"/>
    /// falls back to <c>ManagedIdentity:ClientId</c> when this is null (parity
    /// with <c>CosmosModule.cs</c>). When both are absent, the impl relies on
    /// the default DefaultAzureCredential chain (AzureCliCredential for local
    /// dev; on deployed App Service without an MI attached the token call will
    /// fail loud on first invocation).
    /// </summary>
    public string? ManagedIdentityClientId { get; set; }

    /// <summary>
    /// Dataverse entity-set name for the registry table. Default
    /// <c>sprk_dataverseenvironments</c> — the plural OData set for
    /// <c>sprk_dataverseenvironment</c>. Parity with
    /// <c>CustomerRunGuardOptions.EntitySetName</c>.
    /// </summary>
    public string EntitySetName { get; set; } = "sprk_dataverseenvironments";

    /// <summary>
    /// HTTP request timeout for each Dataverse call. Default 30s. Both the
    /// READ path (H0.5 re-consent lookup) and the WRITE path (H13 Ready
    /// PATCH) sit in latency-sensitive positions (POST /api/runs 202 target
    /// per spec.md FR-22 R20; H13 aggregate acceptance-gate SLA per §15 #4).
    /// Must fall in [1s, 5m].
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Startup validation applied by <see cref="DataverseEnvironmentRegistryModule"/>.
    /// Throws <see cref="InvalidOperationException"/> on invalid values so a
    /// misconfigured L2 App Service fails fast at boot (NFR-05 parity with
    /// <c>CustomerRunGuardOptions.Validate</c>).
    /// </summary>
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(AdminEnvironmentUrl))
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:AdminEnvironmentUrl' is required. " +
                "Set the admin Dataverse env URL that hosts the sprk_dataverseenvironment registry " +
                "(e.g. https://spaarkedev1.crm.dynamics.com). In deployed environments this is bound " +
                "via App Service settings + platform-controlplane.bicep.");
        }
        if (!Uri.TryCreate(AdminEnvironmentUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:AdminEnvironmentUrl' must be an absolute URI (actual: '{AdminEnvironmentUrl}').");
        }
        if (string.IsNullOrWhiteSpace(EntitySetName))
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:EntitySetName' must be non-empty.");
        }
        if (RequestTimeout < TimeSpan.FromSeconds(1) || RequestTimeout > TimeSpan.FromMinutes(5))
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:RequestTimeout' must be between 1 second and 5 minutes (actual: {RequestTimeout}).");
        }
    }
}
