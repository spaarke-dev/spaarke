namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Represents an entity association for a communication record.
/// Used in send requests to link the communication to Dataverse entities via the AssociationResolver pattern.
/// </summary>
public sealed record CommunicationAssociation
{
    /// <summary>
    /// Dataverse entity logical name (e.g., "sprk_matter", "sprk_project", "sprk_organization").
    /// </summary>
    public required string EntityType { get; init; }

    /// <summary>
    /// Dataverse record ID.
    /// </summary>
    public required Guid EntityId { get; init; }

    /// <summary>
    /// Display name for the associated record (optional, for UI display).
    /// </summary>
    public string? EntityName { get; init; }

    /// <summary>
    /// URL to the associated record in Dataverse (optional).
    /// </summary>
    public string? EntityUrl { get; init; }

    /// <summary>
    /// Ontology task 176 (D-216): a SECONDARY association (not <c>associations[0]</c>) whose typed <c>sprk_regarding*</c> lookup
    /// is also written, before the FR-26 stamp and the owner are decided, so the row is filed under it too (the budget inquiry
    /// email: its service request AND its matter). Only <c>associations[0]</c> is mapped otherwise. Server-set only: internal and
    /// never bound from a request body.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    internal bool AlsoRegarding { get; init; }
}
