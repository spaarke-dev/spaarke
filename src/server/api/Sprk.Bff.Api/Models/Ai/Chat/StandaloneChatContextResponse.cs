namespace Sprk.Bff.Api.Models.Ai.Chat;

/// <summary>
/// A single context field descriptor for a standalone entity type.
///
/// Describes one Dataverse attribute that is surfaced as context for AI chat
/// (e.g. the matter name, practice area, or assigned user on a <c>sprk_matter</c> record).
/// </summary>
/// <param name="LogicalName">Dataverse attribute logical name (e.g. <c>sprk_mattername</c>).</param>
/// <param name="DisplayLabel">Human-readable display label for the UI (e.g. "Matter Name").</param>
/// <param name="FieldType">
/// Simple field type hint for client formatting:
/// <c>"text"</c>, <c>"datetime"</c>, <c>"lookup"</c>, <c>"optionset"</c>, <c>"number"</c>.
/// </param>
/// <param name="IsRequired">
/// Whether the field is expected to have a value for effective AI context resolution.
/// </param>
public record StandaloneContextField(
    string LogicalName,
    string DisplayLabel,
    string FieldType,
    bool IsRequired = false);

/// <summary>
/// Resolved standalone chat context for a given Dataverse entity type and record ID.
///
/// Returned by <c>GET /api/ai/chat/context-mappings/standalone</c> and consumed
/// by the Spaarke AI Code Page (<c>sprk_spaarkeai</c>) to populate the context panel
/// when SprkChat is opened without an associated analysis record.
///
/// The <see cref="ContextFields"/> list describes which Dataverse attributes are
/// available as AI context for this entity type. The client uses these field descriptors
/// to render context metadata in the chat side panel.
///
/// Caching (ADR-009, ADR-014): Redis-first with 30-minute absolute TTL.
/// Cache key pattern: <c>chat-context:{tenantId}:standalone:{entityType}:{entityId}</c>
/// (tenant-scoped per ADR-014).
/// </summary>
/// <param name="EntityType">Dataverse entity logical name (e.g. <c>contact</c>, <c>sprk_matter</c>).</param>
/// <param name="EntityId">Record ID as a string GUID.</param>
/// <param name="DisplayName">Human-readable name for this entity type (e.g. "Contact").</param>
/// <param name="ContextFields">
/// Ordered list of context field descriptors for the entity type.
/// Each field maps a Dataverse attribute to a display label and type hint.
/// </param>
/// <param name="RecommendedPlaybookId">
/// Optional Dataverse GUID string of the recommended playbook for this entity type,
/// resolved from <c>sprk_aichatcontextmapping</c>. Null when no mapping exists.
/// </param>
public record StandaloneChatContextResponse(
    string EntityType,
    string EntityId,
    string DisplayName,
    IReadOnlyList<StandaloneContextField> ContextFields,
    string? RecommendedPlaybookId = null);
