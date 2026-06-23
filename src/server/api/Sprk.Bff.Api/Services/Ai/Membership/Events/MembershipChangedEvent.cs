// R3 Part 1 Phase 2 — Membership change event payload contract
// Task 072 (2026-06-22): Defines the wire-format record published to the
// Service Bus topic `sprk-membership-changes` (spec FR-2P2.3) whenever a
// BFF mutation endpoint changes a configured person/team/BU Lookup field
// on a tracked entity (matter, document, event, task, opportunity).
// Subscribers (e.g., `recon-junction-updater` for FR-2P2.4 junction upsert,
// future cache warmers, Teams notifiers) deserialize this payload and
// react.
//
// CONTRACT IS WIRE-LEVEL — multiple subscribers consume it asynchronously.
// Every binding decision below is locked to support:
//
//   1. Stability across schemaVersion bumps. Enum-as-string serialization
//      (NOT enum-as-int) means a future enum-value rename forces an
//      explicit migration step — no silent integer collisions if the enum
//      shape evolves.
//   2. Forward + backward compatibility. `SchemaVersion` defaults to 1; any
//      future v2 payload includes the field explicitly. Subscribers that
//      receive a v1 payload (missing the field after deserialization)
//      compute v1 semantics; subscribers that receive a v2 payload they
//      do not understand log + dead-letter.
//   3. NFR-08 distributed-tracing requirement. `CorrelationId` is `required`
//      — System.Text.Json (.NET 7+) throws `JsonException` at deserialize
//      time when the property is missing from the source JSON. There is no
//      "default empty string" silently accepted path.
//   4. Future operator forensics. `OccurredOnUtc` records when the
//      mutation actually happened (not when the event was published), so
//      operators reconstructing a timeline don't see drift from Service
//      Bus enqueue latency. Optional (defaults to `null` so a publisher
//      that does not yet capture the value compiles) but recommended.
//
// NO SERVICE BUS CLIENT IN THIS TASK. Per the task POML, this is contract
// definition only. Publisher wiring (tasks 081–083) waits on operator-deploy
// of the topic (task 071) to complete.
//
// Reference: projects/spaarke-platform-foundations-r3/spec.md FR-2P2.2,
//            NFR-08; projects/spaarke-platform-foundations-r3/design.md
//            Part 1 Phase 2; sibling
//            src/server/api/Sprk.Bff.Api/Services/Ai/Membership/Models/
//            MembershipResponse.cs (JSON-serialization convention reference).

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.Membership.Events;

/// <summary>
/// Wire-format payload for a single membership-affecting mutation,
/// published to the Service Bus topic <c>sprk-membership-changes</c> per
/// spec FR-2P2.3. Subscribers consume to update the
/// <c>sprk_userentityassociation</c> junction, invalidate caches, or
/// notify downstream systems.
/// </summary>
/// <remarks>
/// <para>
/// Use <see cref="SerializerOptions"/> for canonical JSON serialization —
/// it pins camelCase property naming + enum-as-string conversion, which
/// the wire contract depends on.
/// </para>
/// <para>
/// <strong>NFR-08</strong>: <c>CorrelationId</c> is <c>required</c>.
/// System.Text.Json throws <see cref="JsonException"/> at deserialize time
/// when the property is missing from the source JSON. Publishers MUST set
/// it; consumers SHOULD log + dead-letter on deserialization failure
/// rather than swallow.
/// </para>
/// </remarks>
public sealed record MembershipChangedEvent
{
    /// <summary>
    /// Canonical <see cref="JsonSerializerOptions"/> for serializing /
    /// deserializing the event payload. Locks the wire contract:
    /// camelCase property names + enum-as-string. Reuse via
    /// <c>JsonSerializer.Serialize(evt, MembershipChangedEvent.SerializerOptions)</c>.
    /// </summary>
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Identifier of the person/team/organization whose membership changed.
    /// Interpretation depends on <see cref="PersonIdType"/>.
    /// </summary>
    [JsonPropertyName("personId")]
    public required Guid PersonId { get; init; }

    /// <summary>
    /// Dataverse identity-type label for <see cref="PersonId"/>. Closed
    /// enum (see <see cref="PersonIdentityType"/>) — distinct from the
    /// open-string label used by service-internal discovery.
    /// </summary>
    [JsonPropertyName("personIdType")]
    public required PersonIdentityType PersonIdType { get; init; }

    /// <summary>
    /// Dataverse logical name of the entity whose Lookup field mutated
    /// (e.g., <c>"sprk_matter"</c>, <c>"sprk_document"</c>).
    /// </summary>
    [JsonPropertyName("entityLogicalName")]
    public required string EntityLogicalName { get; init; }

    /// <summary>
    /// Dataverse record id of the entity instance whose Lookup field
    /// mutated (e.g., the specific matter id).
    /// </summary>
    [JsonPropertyName("entityRecordId")]
    public required Guid EntityRecordId { get; init; }

    /// <summary>
    /// Dataverse logical attribute name of the Lookup column that mutated
    /// (e.g., <c>"ownerid"</c>, <c>"sprk_assignedattorney1"</c>). Together
    /// with <see cref="PersonId"/> and <see cref="EntityRecordId"/> this
    /// forms the idempotency key consumers use per FR-2P2.4.
    /// </summary>
    [JsonPropertyName("sourceField")]
    public required string SourceField { get; init; }

    /// <summary>
    /// Public-facing role name (e.g., <c>"owner"</c>,
    /// <c>"assignedAttorney"</c>) derived from <see cref="SourceField"/> via
    /// the same role-name strategy used by
    /// <see cref="Models.MembershipDescriptor.Role"/>. Carried in the
    /// payload so consumers do not need to re-derive.
    /// </summary>
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    /// <summary>
    /// Classification of the mutation (Added / Removed / Updated).
    /// </summary>
    [JsonPropertyName("mutationType")]
    public required MembershipMutationType MutationType { get; init; }

    /// <summary>
    /// Distributed-tracing correlation identifier (NFR-08). MUST be
    /// non-null and non-empty when publishing — there is no
    /// silent-default path. System.Text.Json throws on deserialize when
    /// the property is missing from the source JSON.
    /// </summary>
    [JsonPropertyName("correlationId")]
    public required string CorrelationId { get; init; }

    /// <summary>
    /// Wire-contract version for forward / backward compatibility.
    /// Defaults to <c>1</c>. Subscribers consuming a payload whose
    /// <c>schemaVersion</c> they do not understand SHOULD log + dead-letter
    /// rather than reinterpret. Bump only when the field set changes.
    /// </summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 1;

    /// <summary>
    /// UTC timestamp at which the mutation actually happened (not when the
    /// event was enqueued). Optional — publishers that do not yet capture
    /// the value MAY leave <c>null</c>; consumers that need a wall-clock
    /// ordering primitive treat <c>null</c> as "use Service Bus enqueue
    /// time" instead.
    /// </summary>
    [JsonPropertyName("occurredOnUtc"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? OccurredOnUtc { get; init; }
}
