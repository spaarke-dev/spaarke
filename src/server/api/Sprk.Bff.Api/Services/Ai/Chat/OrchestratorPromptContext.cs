namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Contextual metadata about the current session and user, supplied to
/// <see cref="IOrchestratorPromptBuilder.BuildSystemPrompt"/> alongside the routing result.
///
/// All fields are consumed only by the stable prefix section of the prompt
/// (persona / entity enrichment). Fields that change per-turn (tool schemas,
/// retrieved context) are carried by <see cref="Capabilities.CapabilityRoutingResult"/>.
/// </summary>
/// <param name="UserDisplayName">
/// Display name of the authenticated user (e.g. "Jane Smith"). Injected into the
/// persona greeting when non-null. Never logged at Debug level (PII).
/// </param>
/// <param name="TenantId">
/// Azure AD / Dataverse tenant identifier. Used for matter-isolation reminders in
/// the standing instructions section.
/// </param>
/// <param name="MatterName">
/// Optional name of the active legal matter or entity record. When supplied, the
/// entity enrichment block ("You are assisting with matter 'Acme v. Widgets'…") is
/// appended to the prefix.
/// </param>
/// <param name="ConversationTurnCount">
/// Zero-based index of the current turn within the session. Used to decide whether
/// to include a brief orientation paragraph on the first turn.
/// </param>
/// <param name="ActivePlaybookName">
/// Optional name of the playbook active in this session (e.g. "Contract Review").
/// Included in the persona section so the model knows its operational scope.
/// </param>
public sealed record OrchestratorPromptContext(
    string UserDisplayName,
    string TenantId,
    string? MatterName = null,
    int ConversationTurnCount = 0,
    string? ActivePlaybookName = null);
