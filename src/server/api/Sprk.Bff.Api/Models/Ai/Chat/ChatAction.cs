namespace Sprk.Bff.Api.Models.Ai.Chat;

/// <summary>
/// Categories for the SprkChat action menu.
/// Actions are grouped by category in the UI action picker.
/// </summary>
public enum ActionCategory
{
    /// <summary>Playbook-switching actions (e.g., switch to a different playbook).</summary>
    Playbooks,

    /// <summary>Executable actions (e.g., write back, reanalyze, summarize).</summary>
    Actions,

    /// <summary>Search-related actions (e.g., document search, web search).</summary>
    Search,

    /// <summary>Settings and preferences actions (e.g., mode toggle, preferences).</summary>
    Settings
}

/// <summary>
/// Represents a single action available in the SprkChat action menu.
///
/// Actions are filtered by the playbook's declared capabilities — only actions
/// whose <see cref="RequiredCapability"/> is null (always shown) or matches a
/// capability in the active playbook are returned.
/// </summary>
/// <param name="Id">Unique action identifier (e.g., "write_back", "document_search").</param>
/// <param name="Label">Display label shown in the action menu UI.</param>
/// <param name="Description">Tooltip or secondary description for the action.</param>
/// <param name="Icon">Fluent UI icon name for rendering in the action menu.</param>
/// <param name="Category">Action category for grouping in the UI.</param>
/// <param name="Shortcut">Optional keyboard shortcut hint (e.g., "Ctrl+S"). Null if none.</param>
/// <param name="RequiredCapability">
/// Playbook capability required for this action to appear. Null means the action is
/// always visible regardless of playbook capabilities.
/// </param>
public sealed record ChatAction(
    string Id,
    string Label,
    string Description,
    string Icon,
    ActionCategory Category,
    string? Shortcut = null,
    string? RequiredCapability = null);
