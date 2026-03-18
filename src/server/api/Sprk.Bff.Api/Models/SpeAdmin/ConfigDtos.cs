using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.SpeAdmin;

// ─────────────────────────────────────────────────────────────────────────────
// List / Detail response DTOs
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Summary projection of a sprk_specontainertypeconfig record,
/// returned in list responses (GET /api/spe/configs).
/// </summary>
public sealed record ConfigSummaryDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("containerTypeId")]
    public string ContainerTypeId { get; init; } = string.Empty;

    [JsonPropertyName("containerTypeName")]
    public string? ContainerTypeName { get; init; }

    [JsonPropertyName("owningAppId")]
    public string OwningAppId { get; init; } = string.Empty;

    /// <summary>String enum: "trial" | "standard" | "directToCustomer"</summary>
    [JsonPropertyName("billingClassification")]
    public string BillingClassification { get; init; } = "standard";

    [JsonPropertyName("businessUnitId")]
    public Guid? BusinessUnitId { get; init; }

    [JsonPropertyName("businessUnitName")]
    public string? BusinessUnitName { get; init; }

    [JsonPropertyName("environmentId")]
    public Guid? EnvironmentId { get; init; }

    [JsonPropertyName("environmentName")]
    public string? EnvironmentName { get; init; }

    [JsonPropertyName("isRegistered")]
    public bool IsRegistered { get; init; }

    /// <summary>String enum: "active" | "inactive" (from statecode)</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = "active";

    [JsonPropertyName("createdOn")]
    public DateTimeOffset? CreatedOn { get; init; }

    [JsonPropertyName("modifiedOn")]
    public DateTimeOffset? ModifiedOn { get; init; }
}

/// <summary>
/// Full detail of a sprk_specontainertypeconfig record,
/// returned in single-record responses (GET /api/spe/configs/{id}).
/// </summary>
public sealed record ConfigDetailDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("containerTypeId")]
    public string ContainerTypeId { get; init; } = string.Empty;

    [JsonPropertyName("containerTypeName")]
    public string? ContainerTypeName { get; init; }

    /// <summary>String enum: "trial" | "standard" | "directToCustomer"</summary>
    [JsonPropertyName("billingClassification")]
    public string BillingClassification { get; init; } = "standard";

    [JsonPropertyName("owningAppId")]
    public string OwningAppId { get; init; } = string.Empty;

    /// <summary>Owning app display name — not stored in Dataverse, always empty from API.</summary>
    [JsonPropertyName("owningAppDisplayName")]
    public string OwningAppDisplayName { get; init; } = string.Empty;

    [JsonPropertyName("keyVaultSecretName")]
    public string KeyVaultSecretName { get; init; } = string.Empty;

    [JsonPropertyName("consumingAppId")]
    public string? ConsumingAppId { get; init; }

    [JsonPropertyName("consumingAppKeyVaultSecret")]
    public string? ConsumingAppKeyVaultSecret { get; init; }

    [JsonPropertyName("delegatedPermissions")]
    public string? DelegatedPermissions { get; init; }

    [JsonPropertyName("applicationPermissions")]
    public string? ApplicationPermissions { get; init; }

    [JsonPropertyName("isRegistered")]
    public bool IsRegistered { get; init; }

    [JsonPropertyName("registeredOn")]
    public DateTimeOffset? RegisteredOn { get; init; }

    [JsonPropertyName("defaultContainerId")]
    public string? DefaultContainerId { get; init; }

    [JsonPropertyName("maxStoragePerBytes")]
    public int? MaxStoragePerBytes { get; init; }

    /// <summary>String enum: "disabled" | "externalUserSharingOnly" | "externalUserAndGuestSharing" | "existingExternalUserSharingOnly"</summary>
    [JsonPropertyName("sharingCapability")]
    public string? SharingCapability { get; init; }

    [JsonPropertyName("isItemVersioningEnabled")]
    public bool IsItemVersioningEnabled { get; init; }

    [JsonPropertyName("itemMajorVersionLimit")]
    public int? ItemMajorVersionLimit { get; init; }

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    [JsonPropertyName("businessUnitId")]
    public Guid? BusinessUnitId { get; init; }

    [JsonPropertyName("businessUnitName")]
    public string? BusinessUnitName { get; init; }

    [JsonPropertyName("environmentId")]
    public Guid? EnvironmentId { get; init; }

    [JsonPropertyName("environmentName")]
    public string? EnvironmentName { get; init; }

    /// <summary>String enum: "active" | "inactive" (from statecode)</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = "active";

    [JsonPropertyName("createdOn")]
    public DateTimeOffset? CreatedOn { get; init; }

    [JsonPropertyName("modifiedOn")]
    public DateTimeOffset? ModifiedOn { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Mutation request DTOs
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Request body for creating a new container type config (POST /api/spe/configs).
/// </summary>
public sealed record CreateConfigRequest
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("containerTypeId")]
    public string ContainerTypeId { get; init; } = string.Empty;

    [JsonPropertyName("containerTypeName")]
    public string? ContainerTypeName { get; init; }

    /// <summary>String enum: "trial" | "standard" | "directToCustomer". Required.</summary>
    [JsonPropertyName("billingClassification")]
    public string BillingClassification { get; init; } = "standard";

    [JsonPropertyName("owningAppId")]
    public string OwningAppId { get; init; } = string.Empty;

    [JsonPropertyName("keyVaultSecretName")]
    public string KeyVaultSecretName { get; init; } = string.Empty;

    [JsonPropertyName("businessUnitId")]
    public Guid? BusinessUnitId { get; init; }

    [JsonPropertyName("environmentId")]
    public Guid? EnvironmentId { get; init; }

    [JsonPropertyName("consumingAppId")]
    public string? ConsumingAppId { get; init; }

    [JsonPropertyName("consumingAppKeyVaultSecret")]
    public string? ConsumingAppKeyVaultSecret { get; init; }

    [JsonPropertyName("delegatedPermissions")]
    public string? DelegatedPermissions { get; init; }

    [JsonPropertyName("applicationPermissions")]
    public string? ApplicationPermissions { get; init; }

    [JsonPropertyName("defaultContainerId")]
    public string? DefaultContainerId { get; init; }

    [JsonPropertyName("maxStoragePerBytes")]
    public int? MaxStoragePerBytes { get; init; }

    /// <summary>String enum: "disabled" | "externalUserSharingOnly" | "externalUserAndGuestSharing" | "existingExternalUserSharingOnly"</summary>
    [JsonPropertyName("sharingCapability")]
    public string? SharingCapability { get; init; }

    [JsonPropertyName("isItemVersioningEnabled")]
    public bool IsItemVersioningEnabled { get; init; }

    [JsonPropertyName("itemMajorVersionLimit")]
    public int? ItemMajorVersionLimit { get; init; }

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }
}

/// <summary>
/// Request body for updating an existing container type config (PUT /api/spe/configs/{id}).
/// All fields are optional — only supplied fields are applied.
/// </summary>
public sealed record UpdateConfigRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("containerTypeId")]
    public string? ContainerTypeId { get; init; }

    [JsonPropertyName("containerTypeName")]
    public string? ContainerTypeName { get; init; }

    /// <summary>String enum: "trial" | "standard" | "directToCustomer"</summary>
    [JsonPropertyName("billingClassification")]
    public string? BillingClassification { get; init; }

    [JsonPropertyName("owningAppId")]
    public string? OwningAppId { get; init; }

    [JsonPropertyName("keyVaultSecretName")]
    public string? KeyVaultSecretName { get; init; }

    [JsonPropertyName("businessUnitId")]
    public Guid? BusinessUnitId { get; init; }

    [JsonPropertyName("environmentId")]
    public Guid? EnvironmentId { get; init; }

    [JsonPropertyName("consumingAppId")]
    public string? ConsumingAppId { get; init; }

    [JsonPropertyName("consumingAppKeyVaultSecret")]
    public string? ConsumingAppKeyVaultSecret { get; init; }

    [JsonPropertyName("delegatedPermissions")]
    public string? DelegatedPermissions { get; init; }

    [JsonPropertyName("applicationPermissions")]
    public string? ApplicationPermissions { get; init; }

    [JsonPropertyName("defaultContainerId")]
    public string? DefaultContainerId { get; init; }

    [JsonPropertyName("maxStoragePerBytes")]
    public int? MaxStoragePerBytes { get; init; }

    /// <summary>String enum: "disabled" | "externalUserSharingOnly" | "externalUserAndGuestSharing" | "existingExternalUserSharingOnly"</summary>
    [JsonPropertyName("sharingCapability")]
    public string? SharingCapability { get; init; }

    [JsonPropertyName("isItemVersioningEnabled")]
    public bool? IsItemVersioningEnabled { get; init; }

    [JsonPropertyName("itemMajorVersionLimit")]
    public int? ItemMajorVersionLimit { get; init; }

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Internal Dataverse projection (for QueryAsync deserialization)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Raw Dataverse response projection for sprk_specontainertypeconfig records.
/// Property names match actual Dataverse logical attribute names / OData query results.
/// Used internally by ConfigEndpoints for QueryAsync deserialization only.
/// </summary>
internal sealed class ConfigDataverseRow
{
    [JsonPropertyName("sprk_specontainertypeconfigid")]
    public Guid Id { get; set; }

    [JsonPropertyName("sprk_name")]
    public string? Name { get; set; }

    [JsonPropertyName("sprk_containertypeid")]
    public string? ContainerTypeId { get; set; }

    [JsonPropertyName("sprk_containertypename")]
    public string? ContainerTypeName { get; set; }

    /// <summary>Raw Dataverse picklist int: 100000000=trial, 100000001=standard, 100000002=directToCustomer</summary>
    [JsonPropertyName("sprk_billingclassification")]
    public int BillingClassification { get; set; }

    [JsonPropertyName("sprk_owningappid")]
    public string? OwningAppId { get; set; }

    [JsonPropertyName("sprk_keyvaultsecretname")]
    public string? KeyVaultSecretName { get; set; }

    [JsonPropertyName("sprk_consumingappid")]
    public string? ConsumingAppId { get; set; }

    [JsonPropertyName("sprk_consumingappkvsecret")]
    public string? ConsumingAppKvSecret { get; set; }

    [JsonPropertyName("sprk_delegatedpermission")]
    public string? DelegatedPermissions { get; set; }

    [JsonPropertyName("sprk_applicationpermissions")]
    public string? ApplicationPermissions { get; set; }

    [JsonPropertyName("sprk_isregistered")]
    public bool IsRegistered { get; set; }

    [JsonPropertyName("sprk_registeredon")]
    public DateTimeOffset? RegisteredOn { get; set; }

    [JsonPropertyName("sprk_defaultcontainerid")]
    public string? DefaultContainerId { get; set; }

    [JsonPropertyName("sprk_maxstorageperbytes")]
    public int? MaxStoragePerBytes { get; set; }

    /// <summary>Raw Dataverse picklist int: 100000000=disabled, 100000001=externalUserSharingOnly, 100000002=externalUserAndGuestSharing, 100000003=existingExternalUserSharingOnly</summary>
    [JsonPropertyName("sprk_sharingcapability")]
    public int? SharingCapability { get; set; }

    [JsonPropertyName("sprk_itemversioningenabled")]
    public bool IsItemVersioningEnabled { get; set; }

    [JsonPropertyName("sprk_itemmajorversionlimit")]
    public int? ItemMajorVersionLimit { get; set; }

    [JsonPropertyName("sprk_notes")]
    public string? Notes { get; set; }

    /// <summary>Record status: 0=active, 1=inactive</summary>
    [JsonPropertyName("statecode")]
    public int StateCode { get; set; }

    // Expanded lookup fields
    [JsonPropertyName("_sprk_businessunit_value")]
    public Guid? BusinessUnitId { get; set; }

    [JsonPropertyName("_sprk_businessunit_value@OData.Community.Display.V1.FormattedValue")]
    public string? BusinessUnitName { get; set; }

    [JsonPropertyName("_sprk_environment_value")]
    public Guid? EnvironmentId { get; set; }

    [JsonPropertyName("_sprk_environment_value@OData.Community.Display.V1.FormattedValue")]
    public string? EnvironmentName { get; set; }

    [JsonPropertyName("createdon")]
    public DateTimeOffset? CreatedOn { get; set; }

    [JsonPropertyName("modifiedon")]
    public DateTimeOffset? ModifiedOn { get; set; }

    // ── Option set conversion helpers ─────────────────────────────────────────

    /// <summary>Converts Dataverse billingClassification int to TypeScript string enum.</summary>
    public static string BillingToString(int value) => value switch
    {
        100000000 => "trial",
        100000002 => "directToCustomer",
        _ => "standard"        // 100000001 = standard (default)
    };

    /// <summary>Converts TypeScript billingClassification string enum to Dataverse int.</summary>
    public static int BillingToInt(string? value) => value switch
    {
        "trial" => 100000000,
        "directToCustomer" => 100000002,
        _ => 100000001         // "standard" (default)
    };

    /// <summary>Converts Dataverse sharingCapability int to TypeScript string enum.</summary>
    public static string SharingToString(int? value) => value switch
    {
        100000001 => "externalUserSharingOnly",
        100000002 => "externalUserAndGuestSharing",
        100000003 => "existingExternalUserSharingOnly",
        _ => "disabled"        // 100000000 (default)
    };

    /// <summary>Converts TypeScript sharingCapability string enum to Dataverse int.</summary>
    public static int? SharingToInt(string? value) => value switch
    {
        "externalUserSharingOnly" => 100000001,
        "externalUserAndGuestSharing" => 100000002,
        "existingExternalUserSharingOnly" => 100000003,
        "disabled" => 100000000,
        null => (int?)null,
        _ => (int?)null
    };

    // ── Mapping helpers ───────────────────────────────────────────────────────

    public ConfigSummaryDto ToSummary() => new()
    {
        Id = Id,
        Name = Name ?? string.Empty,
        ContainerTypeId = ContainerTypeId ?? string.Empty,
        ContainerTypeName = ContainerTypeName,
        OwningAppId = OwningAppId ?? string.Empty,
        BillingClassification = BillingToString(BillingClassification),
        BusinessUnitId = BusinessUnitId,
        BusinessUnitName = BusinessUnitName,
        EnvironmentId = EnvironmentId,
        EnvironmentName = EnvironmentName,
        IsRegistered = IsRegistered,
        Status = StateCode == 0 ? "active" : "inactive",
        CreatedOn = CreatedOn,
        ModifiedOn = ModifiedOn
    };

    public ConfigDetailDto ToDetail() => new()
    {
        Id = Id,
        Name = Name ?? string.Empty,
        ContainerTypeId = ContainerTypeId ?? string.Empty,
        ContainerTypeName = ContainerTypeName,
        BillingClassification = BillingToString(BillingClassification),
        OwningAppId = OwningAppId ?? string.Empty,
        OwningAppDisplayName = string.Empty,
        KeyVaultSecretName = KeyVaultSecretName ?? string.Empty,
        ConsumingAppId = ConsumingAppId,
        ConsumingAppKeyVaultSecret = ConsumingAppKvSecret,
        DelegatedPermissions = DelegatedPermissions,
        ApplicationPermissions = ApplicationPermissions,
        IsRegistered = IsRegistered,
        RegisteredOn = RegisteredOn,
        DefaultContainerId = DefaultContainerId,
        MaxStoragePerBytes = MaxStoragePerBytes,
        SharingCapability = SharingToString(SharingCapability),
        IsItemVersioningEnabled = IsItemVersioningEnabled,
        ItemMajorVersionLimit = ItemMajorVersionLimit,
        Notes = Notes,
        BusinessUnitId = BusinessUnitId,
        BusinessUnitName = BusinessUnitName,
        EnvironmentId = EnvironmentId,
        EnvironmentName = EnvironmentName,
        Status = StateCode == 0 ? "active" : "inactive",
        CreatedOn = CreatedOn,
        ModifiedOn = ModifiedOn
    };
}
