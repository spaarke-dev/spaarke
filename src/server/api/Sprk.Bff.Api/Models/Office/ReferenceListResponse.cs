namespace Sprk.Bff.Api.Models.Office;

/// <summary>
/// Response model for <c>GET /api/office/search/{list}</c> — the pane's load-once reference lists:
/// <c>matter-types</c> (task 038), <c>practice-areas</c> and <c>project-types</c> (task 100).
/// </summary>
/// <remarks>
/// <para>
/// A small, load-once list of active reference rows — NOT a typeahead search result. The pane loads each list
/// once (not per keystroke) to populate a dropdown on its "+ New" create form. Task 100 generalized the task-038
/// matter-types route into this one parameterized route; the matter-types URL and the wire shape
/// (<c>results: [{ id, name, code? }]</c>) are unchanged, so the deployed pane keeps working.
/// </para>
/// </remarks>
public record ReferenceListResponse
{
    /// <summary>
    /// Active reference rows, ordered by name.
    /// </summary>
    public required IReadOnlyList<ReferenceListOption> Results { get; init; }
}

/// <summary>
/// One reference row for a create-form dropdown.
/// </summary>
public record ReferenceListOption
{
    /// <summary>
    /// The row's primary key — the id the pane sends back on quick-create (<c>matterTypeId</c>,
    /// <c>practiceAreaId</c> or <c>projectTypeId</c>).
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// The row's display name (e.g. "Litigation").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The row's short code (e.g. "LITG"), when the table has one. Informational only; the pane does not send it or
    /// build a number from it (numbering is the platform's autonumber, task 076).
    /// </summary>
    public string? Code { get; init; }
}
