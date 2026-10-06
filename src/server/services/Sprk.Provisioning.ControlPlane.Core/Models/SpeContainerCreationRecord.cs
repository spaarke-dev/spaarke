// -----------------------------------------------------------------------------
// SpeContainerCreationRecord.cs
//
// unified-access-control-r2 task 165, owner rounds 41 + 49 — H8's CREATION RECORD,
// persisted as TYPED fields of the run (InterStepState.SpeContainerCreation).
//
// WHY TYPED: round 41 recorded the root container in the h8-t6-verified gate's
// JsonElement evidence. The Cosmos SDK's default (Newtonsoft) serializer wrote that
// as {"valueKind":1}, so every re-entry found only the container type, created a
// SECOND root container and left the first UNBOUND (owner round 49 item 2). A typed
// property round-trips through Newtonsoft as itself; the L2 tests now drive H8
// through the production serializer (CosmosModule.BuildCosmosClient's
// ClientOptions.Serializer), so a field that does not survive persistence fails them.
//
// The container TYPE stays in InterStepState.ContainerTypeId (H8 is its only
// writer; the pre-round-41 path wrote it there too). Everything else H8 needs to
// resume without creating anything twice lives here.
// -----------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Sprk.Provisioning.ControlPlane.Models;

/// <summary>
/// What this run's H8 created in SharePoint Embedded and has not yet finished with — the record every H8 re-entry
/// resumes from, so no container type or root container is ever created twice and none is left unbound.
/// </summary>
public sealed class SpeContainerCreationRecord
{
    /// <summary>Status: the root container was created (or adopted) and recorded; not yet verified or bound.</summary>
    public const string StatusCreated = "created";

    /// <summary>Status: verification answered 404 — the up-to-24h SPE replication window.</summary>
    public const string StatusReplicationPending = "replication-pending";

    /// <summary>Status: the recorded root container was removed after a failed bind; the type remains.</summary>
    public const string StatusRootContainerRemoved = "root-container-removed";

    /// <summary>Status: a container POST into the recorded type got no authoritative answer — it may exist unseen.</summary>
    public const string StatusRootContainerInDoubt = "root-container-in-doubt";

    /// <summary>Status: the container-type POST got no authoritative answer — a type may exist that the run does not name.</summary>
    public const string StatusContainerTypeInDoubt = "container-type-in-doubt";

    /// <summary>Status: only the container type exists (creation stopped after it, with an authoritative answer).</summary>
    public const string StatusTypeOnly = "type-only";

    /// <summary>Status: the root container is bound to its business unit, the KV secret written, H8 complete.</summary>
    public const string StatusBound = "bound";

    /// <summary>
    /// The root container H8 created (or adopted from its own container type) — UNBOUND until H8 completes. Null when
    /// no root container is known. Not the H7 hand-off: <see cref="InterStepState.SpeContainerId"/> is written only on
    /// completion, after the bind.
    /// </summary>
    [JsonPropertyName("rootContainerId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RootContainerId { get; set; }

    /// <summary>
    /// Further containers found in the run's own container type when H8 adopted a root container (a creation that got
    /// no answer and was later repeated). Each is bound to the same business unit as the root (or removed) before H8
    /// completes, so none is left unbound.
    /// </summary>
    [JsonPropertyName("additionalContainerIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IList<string>? AdditionalContainerIds { get; set; }

    /// <summary>
    /// When the container-type POST got no authoritative answer (a client timeout or a lost response — the type may
    /// exist). While set and no type is recorded, H8 creates no type: it is QuarantineRequired until an operator has
    /// checked (with a delegated SharePoint Embedded admin token) and cleared the quarantine.
    /// </summary>
    [JsonPropertyName("containerTypeInDoubtSince")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ContainerTypeInDoubtSince { get; set; }

    /// <summary>
    /// When a root-container POST into the recorded type got no authoritative answer (the container may exist unseen).
    /// Within the replication window H8 creates no further root container while its type lists none — it waits.
    /// </summary>
    [JsonPropertyName("rootContainerInDoubtSince")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? RootContainerInDoubtSince { get; set; }

    /// <summary>The business unit the root container is stamped with — set when H8 completes (the bind read back).</summary>
    [JsonPropertyName("owningBusinessUnitId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OwningBusinessUnitId { get; set; }

    /// <summary>One of the <c>Status*</c> constants — for the operator; H8 decides from the fields above.</summary>
    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Status { get; set; }

    /// <summary>When H8 last wrote this record.</summary>
    [JsonPropertyName("updatedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? UpdatedAt { get; set; }
}
