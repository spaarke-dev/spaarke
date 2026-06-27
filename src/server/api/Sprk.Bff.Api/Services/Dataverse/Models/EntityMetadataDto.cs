namespace Sprk.Bff.Api.Services.Dataverse.Models;

/// <summary>
/// Projected Dataverse entity metadata returned by <c>GET /api/dataverse/metadata/{entityLogicalName}</c>
/// (FR-BFF-03 of the Spaarke DataGrid Framework R1).
/// </summary>
/// <remarks>
/// <para>
/// This shape is intentionally narrow. Raw <c>EntityMetadata</c> from the Dataverse SDK is hundreds of KB
/// per entity (full localized labels, audit/security descriptors, every privilege set, etc.) — far too
/// large to ship to the browser. The framework consumers (filter chip auto-derivation, column rendering,
/// option-set chips per task 006) only need the fields below.
/// </para>
/// <para>
/// Payload budget per FR-BFF-03: &lt;50KB per entity. The dropped fields (notably
/// <c>displayName.localizedLabels[]</c> arrays and the privilege catalog) are what makes that budget achievable.
/// </para>
/// </remarks>
public sealed record EntityMetadataDto(
    string LogicalName,
    string PrimaryIdAttribute,
    string PrimaryNameAttribute,
    IReadOnlyList<AttributeDto> Attributes);

/// <summary>
/// Projected attribute metadata.
/// </summary>
/// <remarks>
/// <c>Format</c> is the attribute's format (e.g., <c>Text</c>, <c>Email</c>, <c>Url</c>, <c>DateOnly</c>) when
/// the SDK exposes one for the attribute type; <c>null</c> otherwise. <c>OptionSet</c> is populated only for
/// picklist/state/status attributes; <c>null</c> for everything else.
/// </remarks>
public sealed record AttributeDto(
    string LogicalName,
    string AttributeType,
    string? Format,
    bool IsPrimaryName,
    bool IsPrimaryId,
    OptionSetDto? OptionSet);

/// <summary>
/// Projected option-set metadata. Only <c>Value</c>, <c>Label</c>, and <c>Color</c> are kept; localized
/// label arrays and description metadata are dropped to stay under the 50KB-per-entity budget.
/// </summary>
public sealed record OptionSetDto(IReadOnlyList<OptionDto> Options);

/// <summary>
/// Projected option metadata. <c>Label</c> is the user-language label resolved at projection time.
/// <c>Color</c> is the optional hex color string (Dataverse stores this for state/status options).
/// </summary>
public sealed record OptionDto(int Value, string Label, string? Color);
