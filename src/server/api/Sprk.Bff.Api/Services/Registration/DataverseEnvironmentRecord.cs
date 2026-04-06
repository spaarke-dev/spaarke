using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Registration;

/// <summary>
/// Environment type values (maps to sprk_environmenttype choice field).
/// </summary>
public enum EnvironmentType
{
    Development = 0,
    Demo = 1,
    Sandbox = 2,
    Trial = 3,
    Partner = 4,
    Training = 5,
    Production = 6
}

/// <summary>
/// Setup status values (maps to sprk_setupstatus choice field).
/// </summary>
public enum EnvironmentSetupStatus
{
    NotStarted = 0,
    InProgress = 1,
    Ready = 2,
    Issue = 3
}

/// <summary>
/// Read model for a sprk_dataverseenvironment record from Dataverse.
/// Maps all 16 entity columns.
/// ADR-010: Concrete type, no interface.
/// </summary>
public class DataverseEnvironmentRecord
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public EnvironmentType? EnvironmentTypeValue { get; set; }
    public string? DataverseUrl { get; set; }
    public string? AppId { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public bool IsDefault { get; set; }
    public EnvironmentSetupStatus? SetupStatus { get; set; }
    public string? AccountDomain { get; set; }
    public string? BusinessUnitName { get; set; }
    public string? TeamName { get; set; }
    public string? SpeContainerId { get; set; }
    public string? SecurityGroupId { get; set; }
    public int? DefaultDurationDays { get; set; }
    public string? LicenseConfigJson { get; set; }
    public string? AdminEmails { get; set; }

    /// <summary>
    /// Parses sprk_licenseconfigjson into a typed LicenseConfig object.
    /// Throws JsonException if JSON is malformed (caller should handle per FR-12).
    /// </summary>
    public LicenseConfig? ParseLicenseConfig()
    {
        if (string.IsNullOrWhiteSpace(LicenseConfigJson))
            return null;

        return JsonSerializer.Deserialize<LicenseConfig>(LicenseConfigJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    /// <summary>
    /// Parses sprk_adminemails (comma-separated) into an array.
    /// </summary>
    public string[] ParseAdminEmails()
    {
        if (string.IsNullOrWhiteSpace(AdminEmails))
            return Array.Empty<string>();

        return AdminEmails.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// All Dataverse columns to include in $select queries.
    /// </summary>
    public static readonly string[] AllColumns =
    {
        "sprk_dataverseenvironmentid", "sprk_name", "sprk_environmenttype",
        "sprk_dataverseurl", "sprk_appid", "sprk_description",
        "sprk_isactive", "sprk_isdefault", "sprk_setupstatus",
        "sprk_accountdomain", "sprk_businessunitname", "sprk_teamname",
        "sprk_specontainerid", "sprk_securitygroupid", "sprk_defaultdurationdays",
        "sprk_licenseconfigjson", "sprk_adminemails"
    };

    /// <summary>
    /// Maps an OData JSON element to a DataverseEnvironmentRecord.
    /// Follows the same pattern as RegistrationDataverseService.MapToRecord.
    /// </summary>
    public static DataverseEnvironmentRecord MapFromJson(JsonElement json)
    {
        return new DataverseEnvironmentRecord
        {
            Id = json.TryGetProperty("sprk_dataverseenvironmentid", out var idProp) ? idProp.GetGuid() : Guid.Empty,
            Name = json.TryGetProperty("sprk_name", out var nameProp) ? nameProp.GetString() : null,
            EnvironmentTypeValue = json.TryGetProperty("sprk_environmenttype", out var etProp) && etProp.ValueKind == JsonValueKind.Number
                ? (EnvironmentType)etProp.GetInt32() : null,
            DataverseUrl = json.TryGetProperty("sprk_dataverseurl", out var urlProp) ? urlProp.GetString() : null,
            AppId = json.TryGetProperty("sprk_appid", out var appProp) ? appProp.GetString() : null,
            Description = json.TryGetProperty("sprk_description", out var descProp) ? descProp.GetString() : null,
            IsActive = json.TryGetProperty("sprk_isactive", out var activeProp) && activeProp.ValueKind == JsonValueKind.True,
            IsDefault = json.TryGetProperty("sprk_isdefault", out var defProp) && defProp.ValueKind == JsonValueKind.True,
            SetupStatus = json.TryGetProperty("sprk_setupstatus", out var ssProp) && ssProp.ValueKind == JsonValueKind.Number
                ? (EnvironmentSetupStatus)ssProp.GetInt32() : null,
            AccountDomain = json.TryGetProperty("sprk_accountdomain", out var adProp) ? adProp.GetString() : null,
            BusinessUnitName = json.TryGetProperty("sprk_businessunitname", out var buProp) ? buProp.GetString() : null,
            TeamName = json.TryGetProperty("sprk_teamname", out var tmProp) ? tmProp.GetString() : null,
            SpeContainerId = json.TryGetProperty("sprk_specontainerid", out var speProp) ? speProp.GetString() : null,
            SecurityGroupId = json.TryGetProperty("sprk_securitygroupid", out var sgProp) ? sgProp.GetString() : null,
            DefaultDurationDays = json.TryGetProperty("sprk_defaultdurationdays", out var ddProp) && ddProp.ValueKind == JsonValueKind.Number
                ? ddProp.GetInt32() : null,
            LicenseConfigJson = json.TryGetProperty("sprk_licenseconfigjson", out var lcProp) ? lcProp.GetString() : null,
            AdminEmails = json.TryGetProperty("sprk_adminemails", out var aeProp) ? aeProp.GetString() : null,
        };
    }
}

/// <summary>
/// License SKU configuration stored as JSON in sprk_licenseconfigjson.
/// Same structure as current LicenseSkuConfig in DemoProvisioningOptions.
/// </summary>
public class LicenseConfig
{
    [JsonPropertyName("PowerAppsPlan2TrialSkuId")]
    public string? PowerAppsPlan2TrialSkuId { get; set; }

    [JsonPropertyName("FabricFreeSkuId")]
    public string? FabricFreeSkuId { get; set; }

    [JsonPropertyName("PowerAutomateFreeSkuId")]
    public string? PowerAutomateFreeSkuId { get; set; }
}
