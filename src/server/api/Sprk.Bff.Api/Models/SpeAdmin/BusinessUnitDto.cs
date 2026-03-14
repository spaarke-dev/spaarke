namespace Sprk.Bff.Api.Models.SpeAdmin;

/// <summary>
/// Response model for a Dataverse business unit.
/// Returned by GET /api/spe/businessunits to populate BU selection dropdowns
/// in the SPE Admin UI when creating or filtering container type configs.
/// </summary>
public sealed record BusinessUnitDto(
    /// <summary>Dataverse businessunitid (primary key).</summary>
    Guid Id,

    /// <summary>Display name of the business unit.</summary>
    string Name,

    /// <summary>
    /// ID of the parent business unit, or null for the root organization BU.
    /// Mirrors the _parentbusinessunitid_value OData lookup column.
    /// </summary>
    Guid? ParentBusinessUnitId);
