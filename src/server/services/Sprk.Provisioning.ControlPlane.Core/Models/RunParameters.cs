// -----------------------------------------------------------------------------
// RunParameters.cs
//
// Operator-supplied run parameters for a ProvisioningRun. The secret channel is
// STRUCTURALLY constrained to Key Vault URI references only — cleartext secrets
// in Cosmos payloads are a CATASTROPHIC leak vector per project spec.md FR-27
// acceptance + design.md §6.2 (B3 note).
//
// DESIGN REF:
//   - projects/customer-provisioning-orchestration-r1/design.md §6.2 field `parameters`
//   - projects/customer-provisioning-orchestration-r1/design.md §10.2 (v3 — secrets
//     moved to KV refs per B3)
//   - Task POML 024 constraint (line 43): "RunParameters.Secrets property MUST be
//     typed as IDictionary<string, KeyVaultSecretRef>, NOT string"
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Sprk.Provisioning.ControlPlane.Models;

/// <summary>
/// Parameters supplied to a ProvisioningRun by the operator (via L3 skill /
/// L2 REST API). Split into two shapes:
///   1) <see cref="NonSecret"/> — arbitrary open-ended key/value pairs (customer
///      display name, target env, feature flags, etc.) — safe to persist in Cosmos
///      as cleartext.
///   2) <see cref="Secrets"/> — Key Vault URI references only, one entry per
///      logical secret name. Never a raw string value at the type level.
/// </summary>
public sealed class RunParameters
{
    /// <summary>
    /// Non-secret run parameters (target env, customer display name, feature flags,
    /// tenancy model overrides, etc.). Persisted verbatim in Cosmos.
    /// </summary>
    /// <remarks>
    /// The value type is <c>string</c> for simplicity; richer scalar types can be
    /// encoded as strings by the caller (e.g. bool as <c>"true"</c>, int as
    /// <c>"42"</c>). This keeps the shape open without opening a JSON-fragment
    /// hole that could accidentally carry a secret payload.
    /// </remarks>
    [JsonPropertyName("nonSecret")]
    public IDictionary<string, string> NonSecret { get; set; }
        = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Secret run parameters. Keyed by logical name (e.g. <c>clientSecret</c>,
    /// <c>webhookSigningKey</c>); the value is ALWAYS a
    /// <see cref="KeyVaultSecretRef"/> — a URI reference with no cleartext value.
    ///
    /// STRUCTURAL secret-safety guarantee: the value type prevents a caller from
    /// ever assigning a cleartext string. Runtime handlers resolve the referenced
    /// secret via Managed Identity at the moment of use.
    /// </summary>
    [JsonPropertyName("secrets")]
    public IDictionary<string, KeyVaultSecretRef> Secrets { get; set; }
        = new Dictionary<string, KeyVaultSecretRef>(StringComparer.Ordinal);
}
