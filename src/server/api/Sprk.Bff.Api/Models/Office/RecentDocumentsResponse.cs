using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Office;

/// <summary>
/// Response model for GET /office/recent endpoint.
/// Returns recently accessed entities and documents for quick selection in Office add-ins.
/// </summary>
/// <remarks>
/// <para>
/// This endpoint supports the Office add-in quick access feature by returning:
/// - Recently used association targets (Matter, Project, Invoice, Account, Contact)
/// - Recently accessed/modified documents from the user's activity
/// </para>
/// <para>
/// The recent items are tracked when users save documents via the Office add-in
/// and are presented at the top of entity pickers for quick selection.
/// </para>
/// </remarks>
public record RecentDocumentsResponse
{
    /// <summary>
    /// Recently used association targets, grouped by entity type.
    /// </summary>
    /// <remarks>
    /// Sorted by most recently used first. Users see these at the top of the
    /// entity picker in the Office add-in task pane.
    /// </remarks>
    public IReadOnlyList<RecentAssociation> RecentAssociations { get; init; } = [];

    /// <summary>
    /// Recently accessed or modified documents.
    /// </summary>
    /// <remarks>
    /// Sorted by most recently accessed/modified first. Used for the "Share"
    /// flow where users want to quickly find documents they've been working with.
    /// </remarks>
    public IReadOnlyList<RecentDocument> RecentDocuments { get; init; } = [];

    /// <summary>
    /// User's favorited entities for quick access.
    /// </summary>
    /// <remarks>
    /// Favorites are explicitly pinned by users and persist until unpinned.
    /// These appear in a separate section in the entity picker.
    /// </remarks>
    public IReadOnlyList<FavoriteEntity> Favorites { get; init; } = [];
}

/// <summary>
/// A recently used association target (Matter, Project, Invoice, Account, or Contact).
/// </summary>
public record RecentAssociation
{
    /// <summary>
    /// Dataverse record ID.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Entity type (Matter, Project, Invoice, Account, Contact).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required AssociationType EntityType { get; init; }

    /// <summary>
    /// Dataverse logical name (e.g., "sprk_matter", "account").
    /// </summary>
    public required string LogicalName { get; init; }

    /// <summary>
    /// Display name of the entity.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Additional display information (e.g., "Client: Acme Corp | Status: Active").
    /// </summary>
    public string? DisplayInfo { get; init; }

    /// <summary>
    /// When the entity was last used for document association.
    /// </summary>
    public required DateTimeOffset LastUsed { get; init; }

    /// <summary>
    /// Number of times this entity has been used.
    /// </summary>
    public int UseCount { get; init; }
}

/// <summary>
/// A recently accessed or modified document.
/// </summary>
public record RecentDocument
{
    /// <summary>
    /// Document ID (Dataverse record ID).
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Document display name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// URL to open the document in the browser.
    /// </summary>
    public string? WebUrl { get; init; }

    /// <summary>
    /// When the document was last modified.
    /// </summary>
    public required DateTimeOffset ModifiedDate { get; init; }

    /// <summary>
    /// Reference to the associated entity.
    /// </summary>
    public EntityReference? EntityReference { get; init; }

    /// <summary>
    /// File content type (MIME type).
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long? FileSize { get; init; }
}

/// <summary>
/// Reference to an associated entity.
/// </summary>
public record EntityReference
{
    /// <summary>
    /// Entity ID.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Entity type.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required AssociationType EntityType { get; init; }

    /// <summary>
    /// Entity logical name.
    /// </summary>
    public required string LogicalName { get; init; }

    /// <summary>
    /// Entity display name.
    /// </summary>
    public required string Name { get; init; }
}

/// <summary>
/// A favorited entity for quick access.
/// </summary>
public record FavoriteEntity
{
    /// <summary>
    /// Dataverse record ID.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Entity type.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required AssociationType EntityType { get; init; }

    /// <summary>
    /// Dataverse logical name.
    /// </summary>
    public required string LogicalName { get; init; }

    /// <summary>
    /// Display name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// When the entity was favorited.
    /// </summary>
    public required DateTimeOffset FavoritedAt { get; init; }
}

/// <summary>
/// Association target types.
/// </summary>
/// <remarks>
/// These are the valid entity types that documents can be associated with
/// in the Spaarke DMS. A document MUST be associated to exactly one of these.
/// </remarks>
public enum AssociationType
{
    /// <summary>
    /// Matter (sprk_matter).
    /// </summary>
    Matter,

    /// <summary>
    /// Project (sprk_project).
    /// </summary>
    Project,

    /// <summary>
    /// Invoice (sprk_invoice).
    /// </summary>
    Invoice,

    /// <summary>
    /// Account (standard Dataverse account).
    /// </summary>
    Account,

    /// <summary>
    /// Contact (standard Dataverse contact).
    /// </summary>
    Contact
}
