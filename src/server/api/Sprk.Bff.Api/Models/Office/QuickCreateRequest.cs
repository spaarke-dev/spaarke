using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Office;

/// <summary>
/// Request model for Quick Create entity operations.
/// Corresponds to POST /office/quickcreate/{entityType} endpoint.
/// </summary>
/// <remarks>
/// <para>
/// Quick Create allows users to create new association target entities
/// (Matter, Project, Invoice, Account, Contact) inline from the Office add-in
/// with minimal required fields.
/// </para>
/// <para>
/// Field requirements vary by entity type - see <see cref="QuickCreateFieldRequirements"/>
/// for the required and optional fields per entity type.
/// </para>
/// </remarks>
public record QuickCreateRequest
{
    /// <summary>
    /// Name of the entity to create.
    /// Required for all entity types except Contact (use FirstName/LastName).
    /// </summary>
    /// <remarks>
    /// For Contact entity type, this field is ignored - use FirstName and LastName instead.
    /// </remarks>
    [MaxLength(200)]
    public string? Name { get; init; }

    /// <summary>
    /// Description of the entity.
    /// Optional for all entity types.
    /// </summary>
    [MaxLength(2000)]
    public string? Description { get; init; }

    /// <summary>
    /// Client/Account ID for Matter, Project, or Invoice entities.
    /// This creates a relationship to an Account record.
    /// </summary>
    public Guid? ClientId { get; init; }

    /// <summary>
    /// Industry for Account entity type.
    /// </summary>
    [MaxLength(100)]
    public string? Industry { get; init; }

    /// <summary>
    /// City for Account entity type.
    /// </summary>
    [MaxLength(100)]
    public string? City { get; init; }

    /// <summary>
    /// First name for Contact entity type.
    /// Required when creating a Contact.
    /// </summary>
    [MaxLength(100)]
    public string? FirstName { get; init; }

    /// <summary>
    /// Last name for Contact entity type.
    /// Required when creating a Contact.
    /// </summary>
    [MaxLength(100)]
    public string? LastName { get; init; }

    /// <summary>
    /// Email address for Contact entity type.
    /// </summary>
    [EmailAddress]
    [MaxLength(320)]
    public string? Email { get; init; }

    /// <summary>
    /// Account ID to associate a Contact with.
    /// </summary>
    public Guid? AccountId { get; init; }
}

/// <summary>
/// Supported entity types for Quick Create operations.
/// </summary>
public enum QuickCreateEntityType
{
    /// <summary>
    /// Matter entity (sprk_matter).
    /// </summary>
    Matter,

    /// <summary>
    /// Project entity (sprk_project).
    /// </summary>
    Project,

    /// <summary>
    /// Invoice entity (sprk_invoice).
    /// </summary>
    Invoice,

    /// <summary>
    /// Account entity (standard Dataverse account).
    /// </summary>
    Account,

    /// <summary>
    /// Contact entity (standard Dataverse contact).
    /// </summary>
    Contact
}

/// <summary>
/// Defines required and optional fields per entity type for Quick Create.
/// </summary>
/// <remarks>
/// Per spec: Field sets are code-defined in QuickCreateFieldsProvider.
/// This static class provides the validation rules.
/// </remarks>
public static class QuickCreateFieldRequirements
{
    /// <summary>
    /// Gets the logical name for a Quick Create entity type.
    /// </summary>
    public static string GetLogicalName(QuickCreateEntityType entityType) => entityType switch
    {
        QuickCreateEntityType.Matter => "sprk_matter",
        QuickCreateEntityType.Project => "sprk_project",
        QuickCreateEntityType.Invoice => "sprk_invoice",
        QuickCreateEntityType.Account => "account",
        QuickCreateEntityType.Contact => "contact",
        _ => throw new ArgumentOutOfRangeException(nameof(entityType))
    };

    /// <summary>
    /// Gets the display name for a Quick Create entity type.
    /// </summary>
    public static string GetDisplayName(QuickCreateEntityType entityType) => entityType switch
    {
        QuickCreateEntityType.Matter => "Matter",
        QuickCreateEntityType.Project => "Project",
        QuickCreateEntityType.Invoice => "Invoice",
        QuickCreateEntityType.Account => "Account",
        QuickCreateEntityType.Contact => "Contact",
        _ => throw new ArgumentOutOfRangeException(nameof(entityType))
    };

    /// <summary>
    /// Validates the request for a given entity type and returns validation errors.
    /// </summary>
    /// <param name="entityType">The entity type being created.</param>
    /// <param name="request">The Quick Create request.</param>
    /// <returns>Dictionary of field names to error messages. Empty if valid.</returns>
    public static Dictionary<string, string[]> Validate(QuickCreateEntityType entityType, QuickCreateRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        switch (entityType)
        {
            case QuickCreateEntityType.Matter:
            case QuickCreateEntityType.Project:
            case QuickCreateEntityType.Invoice:
            case QuickCreateEntityType.Account:
                // Name is required for these entity types
                if (string.IsNullOrWhiteSpace(request.Name))
                {
                    errors["name"] = ["Name is required"];
                }
                break;

            case QuickCreateEntityType.Contact:
                // FirstName and LastName are required for Contact
                if (string.IsNullOrWhiteSpace(request.FirstName))
                {
                    errors["firstName"] = ["First name is required"];
                }
                if (string.IsNullOrWhiteSpace(request.LastName))
                {
                    errors["lastName"] = ["Last name is required"];
                }
                break;
        }

        return errors;
    }

    /// <summary>
    /// Tries to parse an entity type string to the enum value.
    /// </summary>
    /// <param name="entityType">The entity type string (case-insensitive).</param>
    /// <param name="result">The parsed enum value if successful.</param>
    /// <returns>True if parsing succeeded, false otherwise.</returns>
    public static bool TryParse(string entityType, out QuickCreateEntityType result)
    {
        return Enum.TryParse(entityType, ignoreCase: true, out result);
    }
}
