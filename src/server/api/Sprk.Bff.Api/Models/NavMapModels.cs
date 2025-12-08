namespace Sprk.Bff.Api.Models;

/// <summary>
/// Response model for EntitySetName endpoint.
/// </summary>
public record EntitySetNameResponse
{
    /// <summary>
    /// Entity logical name (input parameter).
    /// Example: "sprk_document"
    /// </summary>
    public required string EntityLogicalName { get; init; }

    /// <summary>
    /// Entity set name (plural collection name).
    /// Example: "sprk_documents"
    /// </summary>
    public required string EntitySetName { get; init; }

    /// <summary>
    /// Data source: "dataverse", "cache", or "hardcoded".
    /// </summary>
    public required string Source { get; init; }
}

/// <summary>
/// Response model for LookupNavigation endpoint (CRITICAL for @odata.bind).
/// </summary>
public record LookupNavigationResponse
{
    /// <summary>
    /// Child entity logical name (input parameter).
    /// Example: "sprk_document"
    /// </summary>
    public required string ChildEntity { get; init; }

    /// <summary>
    /// Relationship schema name (input parameter).
    /// Example: "sprk_matter_document"
    /// </summary>
    public required string Relationship { get; init; }

    /// <summary>
    /// Lookup attribute logical name (lowercase).
    /// Example: "sprk_matter"
    /// </summary>
    public required string LogicalName { get; init; }

    /// <summary>
    /// Lookup attribute schema name (may have different case).
    /// Example: "sprk_Matter"
    /// </summary>
    public required string SchemaName { get; init; }

    /// <summary>
    /// Navigation property name for @odata.bind (CASE-SENSITIVE!).
    /// Example: "sprk_Matter" (capital M)
    ///
    /// This is the CRITICAL value that solves the Phase 6 case-sensitivity issue.
    /// Use this exact string in @odata.bind operations.
    /// </summary>
    public required string NavigationPropertyName { get; init; }

    /// <summary>
    /// Target entity logical name (parent entity).
    /// Example: "sprk_matter"
    /// </summary>
    public required string TargetEntity { get; init; }

    /// <summary>
    /// Data source: "dataverse", "cache", or "hardcoded".
    /// </summary>
    public required string Source { get; init; }
}

/// <summary>
/// Response model for CollectionNavigation endpoint.
/// </summary>
public record CollectionNavigationResponse
{
    /// <summary>
    /// Parent entity logical name (input parameter).
    /// Example: "sprk_matter"
    /// </summary>
    public required string ParentEntity { get; init; }

    /// <summary>
    /// Relationship schema name (input parameter).
    /// Example: "sprk_matter_document"
    /// </summary>
    public required string Relationship { get; init; }

    /// <summary>
    /// Collection navigation property name.
    /// Example: "sprk_matter_document"
    /// </summary>
    public required string CollectionPropertyName { get; init; }

    /// <summary>
    /// Data source: "dataverse", "cache", or "hardcoded".
    /// </summary>
    public required string Source { get; init; }
}
