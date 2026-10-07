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
// INTEGRATION (batch 4, merged with customer-provisioning-orchestration-r1 task
// 214): H8 no longer creates a container TYPE — the type is an operator prereq and
// an intake value (run parameter containerTypeId). The type-only fields of the
// round-49 record (containerTypeInDoubtSince and its statuses) went with that: H8
// creates ONE container per run in the pre-existing type, and this record is what
// it resumes from.
// -----------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Sprk.Provisioning.ControlPlane.Models;

/// <summary>
/// What this run's H8 created in SharePoint Embedded and has not yet finished with — the record every H8 re-entry
/// resumes from, so no container is ever created twice and none is left unbound.
/// </summary>
public sealed class SpeContainerCreationRecord
{
    /// <summary>Status: the root container was created, activated and recorded; not yet verified or bound.</summary>
    public const string StatusCreated = "created";

    /// <summary>
    /// Status: the root container was created but its <c>/activate</c> call failed (or got no answer). A resume activates
    /// THIS container again — it never creates another.
    /// </summary>
    public const string StatusActivationFailed = "activation-failed";

    /// <summary>Status: verification answered 404 — the up-to-24h SPE replication window.</summary>
    public const string StatusReplicationPending = "replication-pending";

    /// <summary>Status: the recorded root container was removed after a failed bind; a resume creates a new one.</summary>
    public const string StatusRootContainerRemoved = "root-container-removed";

    /// <summary>Status: the container POST got no authoritative answer — a container may exist that the run does not name.</summary>
    public const string StatusRootContainerInDoubt = "root-container-in-doubt";

    /// <summary>Status: the root container is bound to its business unit and handed to H7; H8 complete.</summary>
    public const string StatusBound = "bound";

    /// <summary>
    /// The root container H8 created — UNBOUND until H8 completes. Null when
    /// no root container is known. Not the H7 hand-off: <see cref="InterStepState.SpeContainerId"/> is written only on
    /// completion, after the bind.
    /// </summary>
    [JsonPropertyName("rootContainerId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RootContainerId { get; set; }

    /// <summary>
    /// Further containers this run's H8 created that are not the root — an UNBOUND hand-off an older H8 wrote into
    /// <see cref="InterStepState.SpeContainerId"/> next to a different recorded root. Each is bound to the same business
    /// unit as the root (or removed) before H8 completes, so none is left unbound.
    /// </summary>
    [JsonPropertyName("additionalContainerIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IList<string>? AdditionalContainerIds { get; set; }

    /// <summary>
    /// When the container POST got no authoritative answer (a client timeout, a dropped connection, a 2xx without an id —
    /// the container may exist unseen). While set and no root container is recorded, H8 creates NO container: it is
    /// QuarantineRequired until an operator has checked and cleared the quarantine (the clearance is the confirmation).
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
