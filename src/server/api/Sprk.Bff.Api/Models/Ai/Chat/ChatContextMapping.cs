namespace Sprk.Bff.Api.Models.Ai.Chat;

/// <summary>
/// Lightweight playbook descriptor returned by the context mapping resolution.
/// Contains only the fields needed to display playbook options in the SprkChat UI.
/// </summary>
/// <param name="Id">Dataverse GUID of the <c>sprk_analysisplaybook</c> record.</param>
/// <param name="Name">Display name (<c>sprk_name</c>) shown in the playbook selector.</param>
/// <param name="Description">Optional description for tooltip/help text.</param>
public record ChatPlaybookInfo(
    Guid Id,
    string Name,
    string? Description);

/// <summary>
/// Result of resolving which playbook(s) are available for a given entityType + pageType
/// combination via the <c>sprk_aichatcontextmapping</c> entity.
///
/// Resolution precedence (highest to lowest):
///   1. Exact match: entityType + pageType
///   2. Entity + any: entityType + "any"
///   3. Wildcard + pageType: "*" + pageType
///   4. Global fallback: "*" + "any"
///
/// Within each tier, records are sorted by <c>sprk_sortorder ASC</c>.
/// The first record with <c>sprk_isdefault = true</c> (or the first record if none
/// are marked default) becomes <see cref="DefaultPlaybook"/>.
/// </summary>
/// <param name="DefaultPlaybook">
/// The playbook to auto-select when SprkChat opens. Null when no mapping exists.
/// </param>
/// <param name="AvailablePlaybooks">
/// All playbooks available for the resolved context, ordered by sort order.
/// Empty when no mapping exists.
/// </param>
public record ChatContextMappingResponse(
    ChatPlaybookInfo? DefaultPlaybook,
    IReadOnlyList<ChatPlaybookInfo> AvailablePlaybooks);
