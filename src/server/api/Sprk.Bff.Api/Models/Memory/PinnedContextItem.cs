using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Memory;

/// <summary>
/// R6 Pillar 7 / task 065 (D-C-18) — pinned-context entity.
///
/// <para>
/// Persistent, user-curated "memory anchor" stored in the Cosmos <c>memory</c> container.
/// Pinned items are user-authored content that the LLM is instructed to ALWAYS keep in
/// system-prompt context (per spec FR-42 — pinned items never drop from prompt assembly).
/// Examples: "always respond in terse / no-bullet style"; "matter X has clause Y";
/// "internal company rule: avoid 'going forward'".
/// </para>
///
/// <para>
/// Storage: Cosmos container <c>memory</c> (reused — same container as
/// <see cref="MatterMemory"/> + workspace-tab durable rows), partition key <c>/tenantId</c>
/// per ADR-014 (binding). Document discriminator <c>documentType = "pinned-context"</c>
/// co-exists with matter-memory + workspace-tab documents on the same partition without
/// id collision (the <c>pinned-context_</c> id prefix is the disambiguator — see
/// <see cref="Services.Ai.Memory.PinnedContextService.BuildDocumentId"/>).
/// </para>
///
/// <para>
/// Contract stability: this shape is the contract consumed by the Q7 "Pinned Memory" UI
/// (task 070 in R7). Drift here breaks 070. Sign-off required if fields are added or
/// renamed mid-Phase-C per the R6 Pillar 7 contract-stability rule.
/// </para>
///
/// <para>
/// ADR-015 user-authored content invariant: pinned content is BY DESIGN user-curated;
/// the user explicitly chose to remember it. Deterministic identifiers (userId, matterId,
/// pinId) live in metadata fields and are never embedded into <see cref="Content"/>.
/// </para>
/// </summary>
public sealed class PinnedContextItem
{
    /// <summary>
    /// Cosmos document id. Format: <c>pinned-context_{tenantId}_{userId}_{pinId}</c>.
    /// The <c>pinned-context_</c> prefix is the documentType-id discriminator that prevents
    /// id collisions with <see cref="MatterMemory"/> docs (<c>{tenantId}_{matterId}</c>) and
    /// workspace-tab docs (<c>workspace-tab_{tenantId}_{tabId}</c>) on the same container.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// Cosmos document discriminator — always <c>"pinned-context"</c>. Mirrors the id prefix
    /// for query convenience (the query uses <c>c.documentType = @type</c> rather than
    /// <c>STARTSWITH(c.id, ...)</c>).
    /// </summary>
    [JsonPropertyName("documentType")]
    public required string DocumentType { get; init; }

    /// <summary>
    /// Tenant identifier — also the Cosmos partition key <c>/tenantId</c> (ADR-014 binding).
    /// All queries and writes are scoped to a single tenant per NFR-16.
    /// </summary>
    [JsonPropertyName("tenantId")]
    public required string TenantId { get; init; }

    /// <summary>
    /// Owning user. Logical partition within the tenant; queryable index.
    /// "Personal" pins (<see cref="PinType.UserPreference"/>) are user-scoped;
    /// "system rule" pins are still attributed to the creator but apply broadly per
    /// agent prompt-assembly policy (composition logic in task 067).
    /// </summary>
    [JsonPropertyName("userId")]
    public required string UserId { get; init; }

    /// <summary>
    /// Pin classification. Constrained to the 3 values defined by spec FR-42:
    /// <see cref="PinType.UserPreference"/>, <see cref="PinType.SystemRule"/>,
    /// <see cref="PinType.MatterFact"/>. Serialised as kebab-case ("user-preference",
    /// "system-rule", "matter-fact") per the Q7 UI consumption contract.
    /// </summary>
    [JsonPropertyName("pinType")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required PinType PinType { get; init; }

    /// <summary>
    /// Short, user-authored display label (≤200 chars). Shown in the Q7 UI list and as
    /// the inline accordion header in the agent prompt-assembly path (task 067).
    /// </summary>
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>
    /// User-authored body content (≤1000 chars per service-level cap). Injected verbatim
    /// into the LLM system prompt when this pin is resolved at chat-agent build time
    /// (FR-42). Length enforced at the service layer rather than via DataAnnotations to
    /// keep the model POCO clean for Cosmos serialisation.
    /// </summary>
    [JsonPropertyName("content")]
    public required string Content { get; init; }

    /// <summary>
    /// Optional matter binding. When non-null, the pin is only resolved into the prompt
    /// for sessions on this matter. When null, the pin applies globally for the user
    /// (subject to <see cref="PinType"/> semantics — system-rule applies broadly,
    /// user-preference is user-scoped, matter-fact requires this field).
    /// </summary>
    [JsonPropertyName("matterId")]
    public string? MatterId { get; init; }

    /// <summary>UTC timestamp of pin creation; never changes after first write.</summary>
    [JsonPropertyName("createdAt")]
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>UTC timestamp of the last update (UpdateAsync). Equals CreatedAt on first write.</summary>
    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// User principal name (or stable user id) of the creator. Distinct from
    /// <see cref="UserId"/> because UserId is the partition / query key while CreatedBy
    /// captures the source for audit-log + Q7 UI display ("Pinned by Jane Doe").
    /// </summary>
    [JsonPropertyName("createdBy")]
    public required string CreatedBy { get; init; }

    /// <summary>
    /// Cosmos DB ETag for optimistic concurrency. Mirrors the
    /// <see cref="MatterMemory.ETag"/> pattern. Mapped to the Cosmos system property
    /// via the explicit <c>[JsonPropertyName("_etag")]</c> attribute.
    /// </summary>
    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}

/// <summary>
/// FR-42 pin classification enum. Serialised as kebab-case strings:
/// <c>"user-preference"</c>, <c>"system-rule"</c>, <c>"matter-fact"</c>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PinType
{
    /// <summary>Per-user preference (e.g., "respond terse, no bullets").</summary>
    [JsonStringEnumMemberName("user-preference")]
    UserPreference,

    /// <summary>Org or system rule (e.g., "never reveal internal cost data").</summary>
    [JsonStringEnumMemberName("system-rule")]
    SystemRule,

    /// <summary>Matter-bound fact (e.g., "Matter X has settlement clause Y").</summary>
    [JsonStringEnumMemberName("matter-fact")]
    MatterFact,
}
