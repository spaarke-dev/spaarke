// -----------------------------------------------------------------------------
// GateState.cs
//
// Gate state enum + per-gate state-transition entry for ProvisioningRun.gateStates.
//
// DESIGN REF:
//   - projects/customer-provisioning-orchestration-r1/design.md §6.2 field `gateStates`:
//       object of { [gateId]: { status: "Pending" | "Verified" | "Cleared",
//                               verifiedAt, verifierHandler, evidence: {...} } }
//   - Evidence is gate-specific (Graph query result for admin-consent, Dataverse
//     query result for app-user, etc.) — modeled as JsonElement to keep the shape
//     open while remaining strongly typed at the entry boundary.
// -----------------------------------------------------------------------------

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sprk.Provisioning.ControlPlane.Models;

/// <summary>
/// Lifecycle state of a single provisioning gate. Serialized as the enum name
/// verbatim (e.g. "Pending", "Verified", "Cleared") per design.md §6.2.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GateState
{
    /// <summary>
    /// Gate exists but has not yet been verified. Default on first write.
    /// </summary>
    Pending,

    /// <summary>
    /// Gate was verified successfully by a handler; evidence recorded.
    /// </summary>
    Verified,

    /// <summary>
    /// Gate was explicitly cleared (e.g. operator override, no-longer-required,
    /// or advanced past by state-machine transition).
    /// </summary>
    Cleared,
}

/// <summary>
/// Single-gate entry stored inside <see cref="ProvisioningRun.GateStates"/> keyed
/// by <c>gateId</c> (the handler-authored gate identifier such as
/// <c>admin-consent</c>, <c>app-user-created</c>, <c>spe-consent</c>).
/// </summary>
public sealed class GateEntry
{
    /// <summary>Current lifecycle state of this gate.</summary>
    [JsonPropertyName("status")]
    public GateState Status { get; set; } = GateState.Pending;

    /// <summary>
    /// UTC timestamp at which the gate transitioned to <see cref="GateState.Verified"/>
    /// or <see cref="GateState.Cleared"/>. Null while <see cref="GateState.Pending"/>.
    /// </summary>
    [JsonPropertyName("verifiedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? VerifiedAt { get; set; }

    /// <summary>
    /// Handler identifier (e.g. <c>H3</c>, <c>H0.5</c>) that transitioned this
    /// gate — the "verifier". Null until first transition.
    /// </summary>
    [JsonPropertyName("verifierHandler")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VerifierHandler { get; set; }

    /// <summary>
    /// Gate-specific evidence payload (Graph query result for admin-consent,
    /// Dataverse query result for app-user-created, etc.). Kept as an open JSON
    /// document so gate shapes can evolve without a POCO change per gate.
    /// </summary>
    [JsonPropertyName("evidence")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Evidence { get; set; }
}
