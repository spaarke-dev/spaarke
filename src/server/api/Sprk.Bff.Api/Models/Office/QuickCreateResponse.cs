using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Office;

/// <summary>
/// Response model for Quick Create entity operations.
/// Corresponds to POST /office/quickcreate/{entityType} response.
/// </summary>
/// <remarks>
/// <para>
/// Returns the created entity's ID, type, and name for immediate use
/// in the Office add-in's association picker.
/// </para>
/// </remarks>
public record QuickCreateResponse
{
    /// <summary>
    /// ID of the created entity (Dataverse record ID).
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Type of entity created.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required QuickCreateEntityType EntityType { get; init; }

    /// <summary>
    /// Logical name of the entity in Dataverse (e.g., "sprk_matter", "account").
    /// </summary>
    public required string LogicalName { get; init; }

    /// <summary>
    /// Display name of the created entity.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// URL to the entity in Dataverse (for direct navigation).
    /// </summary>
    /// <remarks>
    /// Format: https://{org}.crm.dynamics.com/main.aspx?etn={logicalname}&id={id}&pagetype=entityrecord
    /// </remarks>
    public string? Url { get; init; }

    /// <summary>
    /// Non-fatal diagnostics from server-side creation (e.g. no matter type supplied; a field-mapping rule
    /// skipped). Omitted when there are none (spaarkeai-word-add-in-r1 task 030).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Warnings { get; init; }
}

/// <summary>
/// Response model for <c>GET /office/quickcreate/defaults</c> (task 100, owner decision B): what the pane's "+ New"
/// form prefills, stated by the server so the prefill and the server's own default are the same answer.
/// </summary>
public record QuickCreateDefaultsResponse
{
    /// <summary>
    /// The caller's own linked contact (task 141's user↔contact link — never an email match), or
    /// <see langword="null"/> when the caller has none or it could not be read. The pane prefills Assigned To with it;
    /// it is the same contact a Matter or Project is assigned to when the request names none.
    /// </summary>
    public QuickCreateContactOption? AssignedTo { get; init; }
}

/// <summary>A contact as the pane's Assigned To field shows it.</summary>
public record QuickCreateContactOption
{
    /// <summary><c>contactid</c>.</summary>
    public required Guid Id { get; init; }

    /// <summary>The contact's <c>fullname</c>.</summary>
    public required string Name { get; init; }

    /// <summary>The contact's <c>emailaddress1</c>, when set.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Email { get; init; }
}
