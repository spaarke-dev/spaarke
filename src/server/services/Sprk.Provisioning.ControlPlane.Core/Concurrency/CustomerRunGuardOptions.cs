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
// AUTH SHAPE — PATH X (REG-02 migration, 2026-08-27, Wave 2 pre-dispatch
// remediation punch REG-02 + Wave 0 Decision 9):
//   DefaultAzureCredential pinned to the L2 UAMI via
//   `ManagedIdentityClientId`, scoped to `{TargetDataverseUrl}/.default` —
//   VERBATIM shape of DataverseEnvironmentRegistryClient.AcquireTokenAsync
//   (Sprk.Provisioning.ControlPlane.Core/Registry/DataverseEnvironmentRegistryClient.cs).
//   The L2 UAMI is registered as a Dataverse Application User on the admin
//   env by task 111's Grant-ControlPlaneIdentity.ps1 — the exact
//   prerequisite already in place for the registry client — so no new grant
//   is required.
//
//   The Path X migration is the ONLY way I5 concurrency-serialization
//   actually works in the secret-free production. Before this row landed,
//   the guard bound `TenantId + ClientId + ClientSecret` for a
//   `ClientSecretCredential` — those settings are OMITTED from secret-free
//   deployments (auth-v4 SS9.1 empty-is-the-signal rule), so
//   `Enabled=true` combined with `requireSecretFreeIdentity=true` failed
//   at Validate(), and the operator's only path was to keep the guard
//   Enabled=false (the ADR-032 kill-switch), which meant two simultaneous
//   POST /api/runs for the same customer both succeeded → catastrophic
//   race per spec §4D I5.
//
// FIELD-REMOVAL NOTE:
//   `TenantId`, `ClientId`, `ClientSecret` were REMOVED in this row. The
//   Bicep app-settings for those three keys ceased to be emitted; the
//   `CustomerRunGuard__ClientSecret` KV-ref was deleted from
//   `legacyClientSecretAppSettings`. Any downstream code binding those
//   settings via IConfiguration will surface as an unbound-property warning
//   at boot — grep for `CustomerRunGuard:TenantId` / `CustomerRunGuard:ClientId`
//   / `CustomerRunGuard:ClientSecret` before adding them back.
//
// URL COLLAPSE (REG-05 companion):
//   REG-05 required a cross-check that `TargetDataverseUrl` and
//   `DataverseEnvironmentRegistry:AdminEnvironmentUrl` point at the same
//   admin env. This row collapses to a single URL: the guard now READS
//   `DataverseEnvironmentRegistry:AdminEnvironmentUrl` as its
//   `TargetDataverseUrl` fallback in the module composer, and
//   `CustomerRunGuardModule.PostConfigure` throws when the two are set
//   to different hosts. Preference order:
//     1. `CustomerRunGuard:TargetDataverseUrl` (if set explicitly)
//     2. `DataverseEnvironmentRegistry:AdminEnvironmentUrl` (fallback)
//   Test hosts that do not register the registry module keep working by
//   setting `TargetDataverseUrl` directly.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Concurrency;

/// <summary>
/// Bound options for the I5 concurrency guard
/// (<see cref="ICustomerRunGuard"/> / <see cref="CustomerRunGuard"/>). Section
/// name = <c>CustomerRunGuard</c>. Path X credential model (REG-02) — the
/// guard authenticates via <c>DefaultAzureCredential</c> pinned to the L2
/// UAMI's <see cref="ManagedIdentityClientId"/>; NO ClientSecret is bound.
/// </summary>
public sealed class CustomerRunGuardOptions
{
    /// <summary>Configuration section name (bound via <see cref="CustomerRunGuardModule.AddCustomerRunGuard"/>).</summary>
    public const string SectionName = "CustomerRunGuard";

    /// <summary>
    /// Admin Dataverse environment URL (e.g. <c>https://spaarke-admin.crm.dynamics.com</c>).
    /// Must be an absolute URI. Required when <see cref="Enabled"/> is true.
    /// When absent, <see cref="CustomerRunGuardModule"/> falls back to
    /// <c>DataverseEnvironmentRegistry:AdminEnvironmentUrl</c> so a single
    /// setting drives both admin-env clients (REG-05 URL collapse).
    /// </summary>
    public string? TargetDataverseUrl { get; set; }

    /// <summary>
    /// L2 UAMI clientId used to pin
    /// <see cref="Azure.Identity.DefaultAzureCredentialOptions.ManagedIdentityClientId"/>.
    /// Optional in this section — <see cref="CustomerRunGuardModule"/> falls
    /// back to <c>ManagedIdentity:ClientId</c> when this is null (parity with
    /// <c>DataverseEnvironmentRegistryOptions.ManagedIdentityClientId</c> and
    /// <c>CosmosModule.cs</c>). When both are absent, the impl relies on the
    /// default DefaultAzureCredential chain (AzureCliCredential for local dev;
    /// on deployed App Service without a UAMI attached the token call fails
    /// loud on first invocation).
    /// </summary>
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
    /// Kill-switch. Defaults to <c>true</c> as of REG-02 (2026-08-27, Wave 2
    /// pre-dispatch remediation) because Path X removes the last credential-
    /// missing failure mode — Enabled=true is now safe on every deployment
    /// shape (secret-free and legacy alike), and I5 same-customer serialization
    /// is a load-bearing invariant per spec.md §4D I5 / FR-32. The ADR-032
    /// kill-switch semantics remain in <see cref="CustomerRunGuard"/> for
    /// explicit test-host opt-out and for the rare rollback scenario.
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
            // once TargetDataverseUrl + UAMI grant are verified).
            return;
        }

        if (string.IsNullOrWhiteSpace(TargetDataverseUrl))
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:TargetDataverseUrl' is required when '{SectionName}:Enabled' is true. " +
                $"REG-02 (2026-08-27): the module also falls back to '{DataverseEnvironmentRegistryConfigKeys.AdminEnvironmentUrl}' " +
                "when this key is unset — set one or the other (both are cross-checked to be the same host when both are set).");
        }
        if (!Uri.TryCreate(TargetDataverseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:TargetDataverseUrl' must be an absolute URI (actual: '{TargetDataverseUrl}').");
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

/// <summary>
/// Config-key constants for cross-module references — kept here so
/// CustomerRunGuardOptions error messages can cite the exact
/// DataverseEnvironmentRegistry key name without introducing a compile-time
/// dependency on the Registry namespace.
/// </summary>
internal static class DataverseEnvironmentRegistryConfigKeys
{
    /// <summary>Matches <c>DataverseEnvironmentRegistryOptions.SectionName + ":AdminEnvironmentUrl"</c>.</summary>
    public const string AdminEnvironmentUrl = "DataverseEnvironmentRegistry:AdminEnvironmentUrl";
}
