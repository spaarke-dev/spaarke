using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.SpeAdmin;

/// <summary>
/// Request body for POST /api/spe/containertypes/{typeId}/register.
///
/// Registers a container type by granting the consuming application (identified by <see cref="AppId"/>)
/// the specified delegated and application permissions via the SharePoint REST API.
///
/// This is the key security operation that enables a consuming app to create and manage SPE containers
/// of the specified type. Without registration, the container type exists but cannot be used.
/// </summary>
/// <remarks>
/// The appId must be the Azure AD application (client) ID of the consuming app that will
/// create and manage containers of the registered type — not the admin app registration itself.
///
/// At least one permission must be supplied in either <see cref="DelegatedPermissions"/>
/// or <see cref="ApplicationPermissions"/>.
///
/// Valid permission values are defined in <see cref="ContainerTypePermissions"/>.
///
/// The <see cref="SharePointAdminUrl"/> is required because the registration call targets the
/// SharePoint REST API (not Graph API). It must be the SharePoint Admin Center URL for the tenant,
/// typically https://{tenant}-admin.sharepoint.com.
/// </remarks>
public sealed class RegisterContainerTypeRequest
{
    /// <summary>
    /// Azure AD application (client) ID of the consuming app to grant permissions to.
    /// Required. Must be a valid GUID string.
    /// </summary>
    [JsonPropertyName("appId")]
    public string AppId { get; init; } = string.Empty;

    /// <summary>
    /// SharePoint Admin Center URL for the tenant (e.g., https://contoso-admin.sharepoint.com).
    /// Required. Used as the base URL for the SharePoint REST API registration call.
    /// Must be an absolute HTTPS URL.
    /// </summary>
    [JsonPropertyName("sharePointAdminUrl")]
    public string SharePointAdminUrl { get; init; } = string.Empty;

    /// <summary>
    /// Delegated permission names to grant to the consuming app.
    /// These permissions apply when the app acts on behalf of a signed-in user.
    ///
    /// Valid values: ReadContent, WriteContent, Create, Delete, ManagePermissions, AddAllPermissions.
    /// May be empty if ApplicationPermissions are supplied.
    /// </summary>
    [JsonPropertyName("delegatedPermissions")]
    public IReadOnlyList<string> DelegatedPermissions { get; init; } = [];

    /// <summary>
    /// Application permission names to grant to the consuming app.
    /// These permissions apply when the app acts without a signed-in user (app-only).
    ///
    /// Valid values: ReadContent, WriteContent, Create, Delete, ManagePermissions, AddAllPermissions.
    /// May be empty if DelegatedPermissions are supplied.
    /// </summary>
    [JsonPropertyName("applicationPermissions")]
    public IReadOnlyList<string> ApplicationPermissions { get; init; } = [];
}

/// <summary>
/// Valid permission names for container type registration.
///
/// These values are passed to the SharePoint REST API:
///   PUT /_api/v2.1/storageContainerTypes/{typeId}/applicationPermissions
///
/// See: https://learn.microsoft.com/en-us/sharepoint/dev/embedded/concepts/app-concepts/app-permissions
/// </summary>
public static class ContainerTypePermissions
{
    /// <summary>Allows reading content from containers.</summary>
    public const string ReadContent = "ReadContent";

    /// <summary>Allows writing content to containers.</summary>
    public const string WriteContent = "WriteContent";

    /// <summary>Allows creating new containers of this type.</summary>
    public const string Create = "Create";

    /// <summary>Allows deleting containers and their content.</summary>
    public const string Delete = "Delete";

    /// <summary>Allows managing permissions on containers and items.</summary>
    public const string ManagePermissions = "ManagePermissions";

    /// <summary>
    /// Grants all available permissions. Use with caution — equivalent to full access.
    /// </summary>
    public const string AddAllPermissions = "AddAllPermissions";

    /// <summary>
    /// Set of all valid permission names accepted by the SharePoint REST API.
    /// Used for validation of incoming requests.
    /// </summary>
    public static readonly IReadOnlySet<string> ValidPermissions = new HashSet<string>(StringComparer.Ordinal)
    {
        ReadContent,
        WriteContent,
        Create,
        Delete,
        ManagePermissions,
        AddAllPermissions
    };
}
