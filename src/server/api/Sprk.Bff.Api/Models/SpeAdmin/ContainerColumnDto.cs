using Sprk.Bff.Api.Infrastructure.Graph;

namespace Sprk.Bff.Api.Models.SpeAdmin;

// ─────────────────────────────────────────────────────────────────────────────
// Container column DTOs — ADR-007: no Graph SDK types in public API surface
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Represents a single column (custom metadata field) on an SPE container.
/// Columns define the metadata schema for items stored within the container.
/// </summary>
/// <param name="Id">Graph column ID (opaque string, used for PATCH/DELETE).</param>
/// <param name="Name">Internal column name (API-addressable, no spaces).</param>
/// <param name="DisplayName">Human-readable display name shown in SPE UIs.</param>
/// <param name="Description">Optional description of the column's purpose.</param>
/// <param name="ColumnType">
/// Type of the column. One of: text, boolean, dateTime, currency, choice,
/// number, personOrGroup, hyperlinkOrPicture.
/// </param>
/// <param name="Required">True if a value must be provided when creating items.</param>
/// <param name="Indexed">True if the column is indexed for faster queries.</param>
/// <param name="ReadOnly">True if Graph reports this column as read-only (system-managed).</param>
public sealed record ContainerColumnDto(
    string Id,
    string Name,
    string? DisplayName,
    string? Description,
    string ColumnType,
    bool Required,
    bool Indexed,
    bool ReadOnly)
{
    /// <summary>Maps a <see cref="SpeAdminGraphService.SpeContainerColumn"/> domain record to a DTO.</summary>
    public static ContainerColumnDto FromDomain(SpeAdminGraphService.SpeContainerColumn col) =>
        new(
            col.Id,
            col.Name,
            col.DisplayName,
            col.Description,
            col.ColumnType,
            col.Required,
            col.Indexed,
            col.ReadOnly);
}

/// <summary>
/// Paginated list response returned by GET /api/spe/containers/{id}/columns.
/// </summary>
/// <param name="Items">Columns on this page.</param>
/// <param name="Count">Total number of items returned in this response.</param>
public sealed record ContainerColumnListResponse(
    IReadOnlyList<ContainerColumnDto> Items,
    int Count);

/// <summary>
/// Request body for POST /api/spe/containers/{id}/columns.
/// Creates a new custom column on the container's metadata schema.
/// </summary>
/// <param name="Name">
/// Internal column name. Must not contain spaces or special characters.
/// Required.
/// </param>
/// <param name="DisplayName">Optional human-readable label shown in SPE UIs.</param>
/// <param name="Description">Optional description of the column's purpose.</param>
/// <param name="ColumnType">
/// Type of the column. Required. One of: text, boolean, dateTime, currency,
/// choice, number, personOrGroup, hyperlinkOrPicture.
/// </param>
/// <param name="Required">Whether items must supply a value for this column.</param>
/// <param name="Indexed">Whether to index the column for search/filter performance.</param>
public sealed record CreateColumnRequest(
    string Name,
    string? DisplayName,
    string? Description,
    string ColumnType,
    bool Required = false,
    bool Indexed = false);

/// <summary>
/// Request body for PATCH /api/spe/containers/{id}/columns/{columnId}.
/// All fields are optional — only non-null fields are sent to Graph API.
/// At least one field must be non-null.
/// </summary>
/// <param name="DisplayName">New display name. Pass null to leave unchanged.</param>
/// <param name="Description">New description. Pass null to leave unchanged.</param>
/// <param name="Required">New required flag. Pass null to leave unchanged.</param>
/// <param name="Indexed">New indexed flag. Pass null to leave unchanged.</param>
public sealed record UpdateColumnRequest(
    string? DisplayName,
    string? Description,
    bool? Required,
    bool? Indexed);
