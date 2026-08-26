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
// AUTH SHAPE (parity with H7 DataverseWebApiEnvVarValuesWriter + H6/H10):
//   Confidential-client (BFF app-reg client-credentials via
//   Azure.Identity.ClientSecretCredential) against the admin Dataverse
//   environment that hosts the registry. NOT MI/UAMI — the L2 App Service's
//   UAMI is not itself a Dataverse Application User on the admin env; the BFF
//   app-reg IS (that's the only SP with a systemuser record on the admin env
//   that reads/writes sprk_dataverseenvironment). This matches how H7 talks
//   to the CUSTOMER env with the BFF app-reg's client-credentials for the same
//   reason — the guard talks to the ADMIN env with the same shape.
//
// FUTURE MIGRATION:
//   When the L2 App Service's UAMI is granted a systemuser record on the admin
//   env (paired with an app-user creation script similar to H10's pattern for
//   customer envs), swap the ClientSecretCredential for DefaultAzureCredential
//   and delete the ClientId/ClientSecret fields. This is an incremental change
//   isolated to the CustomerRunGuardOptions + DataverseRegistryConcurrencyStore
//   auth call sites — the ICustomerRunGuard contract stays intact.
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

    /// <summary>
    /// Admin Dataverse environment URL (e.g. <c>https://spaarke-admin.crm.dynamics.com</c>).
    /// Must be an absolute URI. Required when <see cref="Enabled"/> is true.
    /// </summary>
    public string? TargetDataverseUrl { get; set; }

    /// <summary>
    /// Entra tenant id used to acquire the confidential-client token.
    /// Required when <see cref="Enabled"/> is true.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// BFF Entra app-registration client id (the SP registered as a Dataverse
    /// systemuser on the admin env). Required when <see cref="Enabled"/> is true.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// BFF Entra app-registration client secret. Populated via App Service
    /// KV reference (<c>@Microsoft.KeyVault(SecretUri=...)</c>) — cleartext
    /// value NEVER traverses Cosmos or logs. Required when <see cref="Enabled"/>
    /// is true.
    /// </summary>
    public string? ClientSecret { get; set; }

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
    /// Kill-switch. Defaults to <c>false</c> so a fresh L2 deployment without
    /// the admin-env credentials configured does not crash at boot; the guard
    /// detects the disabled state and returns <see cref="AcquireResult.Success"/>
    /// unconditionally per the null-object kill-switch pattern (ADR-032). A
    /// WARN-level log fires on every acquire attempt so operators notice.
    /// Production deployments MUST set this to <c>true</c> after wiring the
    /// KV references. Test hosts leave this false — the endpoint tests replace
    /// <see cref="ICustomerRunGuard"/> with an in-memory fake.
    /// </summary>
    public bool Enabled { get; set; }

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
                $"Configuration '{SectionName}:TargetDataverseUrl' is required when '{SectionName}:Enabled' is true.");
        }
        if (!Uri.TryCreate(TargetDataverseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:TargetDataverseUrl' must be an absolute URI (actual: '{TargetDataverseUrl}').");
        }
        if (string.IsNullOrWhiteSpace(TenantId))
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:TenantId' is required when '{SectionName}:Enabled' is true.");
        }
        if (string.IsNullOrWhiteSpace(ClientId))
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:ClientId' is required when '{SectionName}:Enabled' is true.");
        }
        if (string.IsNullOrWhiteSpace(ClientSecret))
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:ClientSecret' is required when '{SectionName}:Enabled' is true.");
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
