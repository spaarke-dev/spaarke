using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.SpeAdmin;

/// <summary>
/// Response model for a Dataverse business unit.
/// Returned by GET /api/spe/businessunits to populate BU selection dropdowns
/// in the SPE Admin UI when creating or filtering container type configs.
/// </summary>
public sealed record BusinessUnitDto(
    /// <summary>Dataverse businessunitid (primary key).</summary>
    [property: JsonPropertyName("businessUnitId")] Guid Id,

    /// <summary>Display name of the business unit.</summary>
    [property: JsonPropertyName("name")] string Name,

    /// <summary>
    /// True when this is the root/organization-level business unit
    /// (i.e. has no parent). Computed from ParentBusinessUnitId == null.
    /// </summary>
    [property: JsonPropertyName("isRootUnit")] bool IsRootUnit,

    /// <summary>
    /// ID of the parent business unit, or null for the root organization BU.
    /// Mirrors the _parentbusinessunitid_value OData lookup column.
    /// </summary>
    [property: JsonPropertyName("parentBusinessUnitId")] Guid? ParentBusinessUnitId);
