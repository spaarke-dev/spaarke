// -----------------------------------------------------------------------------
// KeyVaultSecretRef.cs
//
// Compile-time cleartext-secret prevention for ProvisioningRun.Parameters.
//
// Any secret referenced in a ProvisioningRun document MUST be a Key Vault URI
// reference — never a raw string value. This type has NO `Value` property by
// deliberate design; runtime handlers resolve the secret through UAMI at the
// moment of use (never persisted to Cosmos).
//
// DESIGN REF:
//   - projects/customer-provisioning-orchestration-r1/design.md §6.2 (B3 note):
//     "secrets stored as KV URI refs — { 'clientSecret': '@Microsoft.KeyVault(...)' },
//     resolved at handler runtime via UAMI. No cleartext secrets in Cosmos."
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-27 acceptance
//   - ADR-028 (Spaarke Auth v2) MUST NOT: "plaintext secrets in appsettings"
//     — extended by this project to Cosmos payloads.
//
// VERIFICATION:
//   Task 025 ArchTest asserts: (a) RunParameters.Secrets is typed as
//   IDictionary<string, KeyVaultSecretRef>, NOT string; (b) KeyVaultSecretRef has
//   no `Value` / `SecretValue` / `Plaintext` property.
// -----------------------------------------------------------------------------

using System;
using System.Text.Json.Serialization;

namespace Sprk.Provisioning.ControlPlane.Models;

/// <summary>
/// Key Vault URI reference for a secret used by a ProvisioningRun handler.
/// STRUCTURAL secret-safety guarantee — this record intentionally exposes no
/// cleartext value property. Runtime resolution happens through Managed Identity
/// at the moment of handler use.
/// </summary>
/// <param name="VaultName">
/// Key Vault name (e.g. <c>sprk-prod-kv</c> for Model 1 shared, or
/// <c>sprk-{customerId}-{env}-kv</c> for Model 2 dedicated per design.md §7.1).
/// </param>
/// <param name="SecretName">
/// Secret name inside the vault (e.g. <c>customer-{customerId}-bff-client-secret</c>
/// per the canonical KV secret naming from r3 task 063).
/// </param>
/// <param name="VersionId">
/// Optional specific secret version. When null, resolution uses the current version.
/// </param>
public sealed record KeyVaultSecretRef(
    [property: JsonPropertyName("vaultName")] string VaultName,
    [property: JsonPropertyName("secretName")] string SecretName,
    [property: JsonPropertyName("versionId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? VersionId = null)
{
    /// <summary>
    /// Render this reference as the App-Service-style Key Vault reference literal
    /// (e.g. <c>@Microsoft.KeyVault(VaultName=sprk-prod-kv;SecretName=x)</c>).
    /// This is the ONLY string form of the reference; there is no cleartext value form.
    /// </summary>
    public string ToKeyVaultReference()
    {
        if (string.IsNullOrWhiteSpace(VaultName))
        {
            throw new InvalidOperationException("KeyVaultSecretRef.VaultName is required.");
        }
        if (string.IsNullOrWhiteSpace(SecretName))
        {
            throw new InvalidOperationException("KeyVaultSecretRef.SecretName is required.");
        }

        return VersionId is null
            ? $"@Microsoft.KeyVault(VaultName={VaultName};SecretName={SecretName})"
            : $"@Microsoft.KeyVault(VaultName={VaultName};SecretName={SecretName};SecretVersion={VersionId})";
    }
}
