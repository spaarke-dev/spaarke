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
}
