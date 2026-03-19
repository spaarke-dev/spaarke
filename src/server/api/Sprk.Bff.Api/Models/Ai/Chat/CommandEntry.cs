namespace Sprk.Bff.Api.Models.Ai.Chat;

/// <summary>
/// A single entry in the dynamic command catalog returned by <see cref="Services.Ai.Chat.DynamicCommandResolver"/>.
///
/// Commands are assembled from three sources:
///   1. <b>System</b> — hardcoded base commands (/help, /clear, /export) always present.
///   2. <b>Playbook</b> — derived from <c>sprk_analysisplaybook</c> records filtered by entity type.
///   3. <b>Scope</b> — derived from <c>sprk_capabilities</c> option set values on active scopes.
///
/// The record is serialized to JSON for the <c>GET /api/ai/chat/sessions/{sessionId}/commands</c>
/// endpoint and cached in Redis with a 5-minute TTL (ADR-009, ADR-014).
/// </summary>
/// <param name="Id">Unique command identifier, e.g. <c>"search-legal-database"</c>.</param>
/// <param name="Label">Human-readable display label, e.g. <c>"Search Legal Database"</c>.</param>
/// <param name="Description">Tooltip or aria description of what the command does.</param>
/// <param name="Trigger">The slash command string including leading slash, e.g. <c>"/search-legal-database"</c>.</param>
/// <param name="Category">
/// Source category: <c>"system"</c>, <c>"playbook"</c>, or <c>"scope"</c>.
/// Used by the UI to group and style commands differently.
/// </param>
/// <param name="Source">
/// Identifier of the playbook or scope that contributed this command.
/// Null for system commands.
/// </param>
public sealed record CommandEntry(
    string Id,
    string Label,
    string Description,
    string Trigger,
    string Category,
    string? Source);
