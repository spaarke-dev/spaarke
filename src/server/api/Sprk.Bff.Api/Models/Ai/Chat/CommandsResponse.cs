namespace Sprk.Bff.Api.Models.Ai.Chat;

/// <summary>
/// Response DTO for <c>GET /api/ai/chat/sessions/{sessionId}/commands</c>.
///
/// Partitions the flat <see cref="CommandEntry"/> catalog into two arrays so the
/// frontend <c>useDynamicSlashCommands</c> hook can merge system defaults client-side
/// while treating dynamic commands (playbook + scope) as context-specific overlays.
///
/// Each item carries a <see cref="CommandResponseItem.Source"/> discriminator
/// (<c>"system"</c>, <c>"playbook"</c>, or <c>"scope"</c>) so the frontend
/// <c>SlashCommandMenu</c> can group commands by origin category (R2-036).
/// </summary>
/// <param name="SystemCommands">
/// Built-in commands always present regardless of session context (/help, /clear, /export).
/// </param>
/// <param name="DynamicCommands">
/// Context-specific commands from playbooks and/or scope capabilities.
/// Empty when no playbooks or scopes are configured for the session's entity type.
/// </param>
public sealed record CommandsResponse(
    IReadOnlyList<CommandResponseItem> SystemCommands,
    IReadOnlyList<CommandResponseItem> DynamicCommands);

/// <summary>
/// A single command entry projected for the frontend slash command menu.
///
/// Extends the internal <see cref="CommandEntry"/> with an explicit
/// <see cref="Source"/> discriminator (<c>"system"</c>, <c>"playbook"</c>,
/// <c>"scope"</c>) derived from <see cref="CommandEntry.Category"/> so the
/// frontend can group and style commands without parsing category labels.
///
/// The <see cref="SourceName"/> field carries a human-readable origin label
/// (e.g., the scope name or playbook name) for display as a subtitle in the menu.
/// </summary>
/// <param name="Id">Unique command identifier (slug).</param>
/// <param name="Label">Human-readable display label.</param>
/// <param name="Description">Tooltip or aria description.</param>
/// <param name="Trigger">Slash command string including leading slash.</param>
/// <param name="Category">
/// Source category used for grouping: <c>"system"</c>, <c>"playbook"</c>, or <c>"scope"</c>.
/// For scope commands, normalized to <c>"scope"</c> (the scope-qualified label is in <see cref="SourceName"/>).
/// </param>
/// <param name="Source">
/// Origin discriminator: <c>"system"</c>, <c>"playbook"</c>, or <c>"scope"</c>.
/// Matches <c>SlashCommandSource</c> on the frontend.
/// </param>
/// <param name="SourceName">
/// Human-readable name of the contributing source.
/// Null for system commands. For playbook commands: the playbook name.
/// For scope commands: the scope-qualified label (e.g., "Legal Research -- Search").
/// </param>
public sealed record CommandResponseItem(
    string Id,
    string Label,
    string Description,
    string Trigger,
    string Category,
    string Source,
    string? SourceName);
