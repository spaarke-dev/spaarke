namespace Sprk.Bff.Api.Models.Office;

/// <summary>
/// Response model for <c>GET /api/office/search/matter-types</c> (spaarkeai-word-add-in-r1 task 038).
/// </summary>
/// <remarks>
/// <para>
/// A small, load-once reference list of active <c>sprk_mattertype_ref</c> rows — NOT a typeahead search
/// result. The pane's Matter quick-create loads this once (not per keystroke) to populate a required
/// Matter Type dropdown; see the endpoint's own remarks for why this is a sibling route under
/// <c>/api/office/search</c> rather than a filter on <c>/api/office/search/entities</c>.
/// </para>
/// </remarks>
public record MatterTypeListResponse
{
    /// <summary>
    /// Active matter type reference rows, ordered by name.
    /// </summary>
    public required IReadOnlyList<MatterTypeOption> Results { get; init; }
}

/// <summary>
/// One <c>sprk_mattertype_ref</c> row for the Matter Type dropdown.
/// </summary>
public record MatterTypeOption
{
    /// <summary>
    /// <c>sprk_mattertype_refid</c> — the id sent back as <c>matterTypeId</c> on quick-create.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// <c>sprk_mattertypename</c> — the display label (e.g. "Litigation").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// <c>sprk_mattertypecode</c> — the short code (e.g. "LITG"). Informational only; the pane does not
    /// send it or use it to build a matter number (numbering is a separate server-side project).
    /// </summary>
    public string? Code { get; init; }
}
