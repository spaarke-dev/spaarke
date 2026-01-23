namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Configuration options for automatic document re-indexing.
/// Used for check-in triggers and change-based re-indexing.
/// </summary>
public class ReindexingOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "Reindexing";

    /// <summary>
    /// Whether automatic re-indexing is enabled.
    /// When false, documents are not automatically re-indexed on check-in.
    /// Default: true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Azure AD Tenant ID for multi-tenant routing and AI Search index identification.
    /// Required when Enabled is true.
    /// IMPORTANT: Use the Azure AD tenant ID (e.g., "a221a95e-6abc-4434-aecc-e48338a1b2f2"),
    /// NOT the Dataverse organization ID. This must match the tenantId used by PCF controls
    /// and the web resource for consistent AI Search index access.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Whether to trigger re-indexing on document check-in.
    /// Default: true.
    /// </summary>
    public bool TriggerOnCheckin { get; set; } = true;
}
