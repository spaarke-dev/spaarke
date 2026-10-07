// -----------------------------------------------------------------------------
// CustomerRunGuardOptions.cs
//
// L2 CONTROL-PLANE per-customer serialization guard configuration (task 059).
//
// Bound from IConfiguration section "CustomerRunGuard" via
// <see cref="CustomerRunGuardModule.AddCustomerRunGuard"/>. Fails fast at
// startup on invalid values (NFR-05 parity with ReconcilerOptions +
// CosmosModule + ServiceBusModule).
//
// AUTH SHAPE (parity with DataverseEnvironmentRegistryClient):
//   The L2 App Service's user-assigned managed identity, via
//   DefaultAzureCredential pinned with ManagedIdentityClientId.
//
// MIGRATED 2026-08-27 — this block previously read "NOT MI/UAMI — the L2 App
// Service's UAMI is not itself a Dataverse Application User on the admin env",
// and prescribed a FUTURE MIGRATION gated on that changing. It had already
// changed. Verified in the admin environment: sprk-controlplane-dev-uami
// (app id 965a4a01-...) is the enabled application user
// '# sprk-controlplane-dev-uami' holding the Spaarke Provisioning Registry role.
// The blocker was gone; only the comment remained, and the ClientSecretCredential
// it justified was an ADR-028 A4 violation (E-3 CLOSED 2026-08-24).
//
// TenantId is retained but no longer used for token acquisition: the UAMI and the
// admin env share the SPAARKE platform tenant, so issuance targets the UAMI's own
// tenant intrinsically (same reasoning as DataverseEnvironmentRegistryClient's
// "NO TenantId set" note). It stays on the options for diagnostics + config parity
// with the sibling H6/H7 writers, which still address customer tenants explicitly.
//
// REG-02 / REG-05 RESTORED 2026-10-05 (task 242b, while merging to master): commit 3d60b9e59
// (2026-08-27) made the guard default ON, made TenantId optional, fell back to
// DataverseEnvironmentRegistry:AdminEnvironmentUrl and cross-checked the two admin URLs. A
// later master merge kept #847's version of these two files (the managed-identity half of the
// same change) and silently dropped the rest, leaving CustomerRunGuardModulePostConfigureTests
// red (task 221's last six failures). Restored on top of #847's credential code.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Concurrency;

/// <summary>
/// Bound options for the I5 concurrency guard
/// (<see cref="ICustomerRunGuard"/> / <see cref="CustomerRunGuard"/>). Section
/// name = <c>CustomerRunGuard</c>.
/// </summary>
public sealed class CustomerRunGuardOptions
{
    /// <summary>Configuration section name (bound via <see cref="CustomerRunGuardModule.AddCustomerRunGuard"/>).</summary>
    public const string SectionName = "CustomerRunGuard";

    /// <summary>The registry client's admin-environment setting the guard falls back to (REG-05).</summary>
    internal const string RegistryAdminEnvironmentUrlKey =
        Registry.DataverseEnvironmentRegistryOptions.SectionName + ":AdminEnvironmentUrl";

    /// <summary>
    /// Admin Dataverse environment URL (e.g. <c>https://spaarke-admin.crm.dynamics.com</c>).
    /// Must be an absolute URI. Required when <see cref="Enabled"/> is true. When unset,
    /// <see cref="CustomerRunGuardModule"/> falls back to
    /// <c>DataverseEnvironmentRegistry:AdminEnvironmentUrl</c> (REG-05: both clients read and write
    /// the same <c>sprk_dataverseenvironment</c> rows); when both are set their hosts must match.
    /// </summary>
    public string? TargetDataverseUrl { get; set; }

    /// <summary>
    /// Entra tenant id. Optional and not used for token issuance since the managed-identity
    /// migration (see the file header) — kept for diagnostics and config parity.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Client id of the USER-ASSIGNED MANAGED IDENTITY the L2 App Services run as. Used to
    /// disambiguate which identity <see cref="Azure.Identity.DefaultAzureCredential"/> should
    /// present — an App Service carrying more than one UAMI cannot pick for itself.
    /// Optional: when empty, the ambient/system-assigned identity is used.
    /// </summary>
    /// <remarks>
    /// MIGRATED 2026-08-27. This replaced <c>ClientId</c> + <c>ClientSecret</c>, which bound the
    /// BFF's own app registration to a client secret — the exact shape ADR-028 A4 forbids, and
    /// the one E-3 used to excuse before it was CLOSED on 2026-08-24 (auth-v4 task 033).
    /// <para>The migration this file already prescribed — "when the L2 App Service's UAMI is
    /// granted a systemuser record on the admin env, swap the ClientSecretCredential for
    /// DefaultAzureCredential and delete the ClientId/ClientSecret fields" — had its precondition
    /// satisfied without the code following. Verified in the admin environment on 2026-08-27:
    /// <c>sprk-controlplane-dev-uami</c> (app id <c>965a4a01-…</c>) exists as the enabled
    /// application user <c>#&#160;sprk-controlplane-dev-uami</c>, holding the
    /// <c>Spaarke Provisioning Registry</c> role. The blocker was gone; only the comment
    /// remained.</para>
    /// </remarks>
    public string? ManagedIdentityClientId { get; set; }

    /// <summary>
    /// Dataverse entity-set name for the registry table. Default
    /// <c>sprk_dataverseenvironments</c> — the plural OData set for
    /// <c>sprk_dataverseenvironment</c>.
    /// </summary>
    public string EntitySetName { get; set; } = "sprk_dataverseenvironments";

    /// <summary>
    /// HTTP request timeout for each Dataverse call. Default 30s. Kept short
    /// because the guard sits in the hot path of <c>POST /api/runs</c> and
    /// the endpoint's 202 latency target is &lt;100ms (spec.md FR-22 R20).
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Kill-switch. Defaults to <c>true</c> (REG-02): with the managed identity there is no
    /// credential left to be missing, and spec FR-32 requires the guard on in production. Set
    /// <c>false</c> explicitly to disable it (ADR-032 null-object behaviour: the guard returns
    /// <see cref="AcquireResult.Success"/> with a WARN log on every acquire). Endpoint test hosts set
    /// it false or replace <see cref="ICustomerRunGuard"/> with an in-memory fake.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Startup validation applied by <see cref="CustomerRunGuardModule"/>.
    /// Throws <see cref="InvalidOperationException"/> on invalid values so a
    /// misconfigured L2 App Service fails fast at boot (NFR-05 parity).
    /// </summary>
    internal void Validate()
    {
        if (!Enabled)
        {
            // Kill-switch: no validation of Dataverse-connection fields when
            // disabled. Enables staged rollout (module registers, guard
            // returns Success unconditionally, operator flips Enabled=true
            // once KV wiring is verified).
            return;
        }

        if (string.IsNullOrWhiteSpace(TargetDataverseUrl))
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:TargetDataverseUrl' is required when '{SectionName}:Enabled' is true. " +
                $"When it is unset the module falls back to '{RegistryAdminEnvironmentUrlKey}' — set one or the other " +
                "(when both are set their hosts must match).");
        }
        if (!Uri.TryCreate(TargetDataverseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:TargetDataverseUrl' must be an absolute URI (actual: '{TargetDataverseUrl}').");
        }
        // TenantId, ClientId and ClientSecret are not required: the store authenticates as the L2 UAMI (TenantId is
        // diagnostics-only; ClientId / ClientSecret were removed 2026-08-27 with the fields themselves). The store
        // now authenticates as the L2 UAMI; ManagedIdentityClientId is OPTIONAL by design (empty
        // means "the ambient identity"), so there is nothing further to require here.
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
