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
/// Tenancy model values (maps to sprk_tenancymodel local Choice option-set;
/// added by customer-provisioning-orchestration-r1 task 023 v3 addition per
/// design.md §3A A1). Model1Shared = trial/SMB shared-platform tier;
/// Model2Dedicated = regulated/enterprise dedicated stamp.
/// </summary>
public enum TenancyModel
{
    Model1Shared = 0,
    Model2Dedicated = 1
}

/// <summary>
/// Read model for a sprk_dataverseenvironment record from Dataverse.
/// Maps all 30 entity columns as of 2026-08-27 SESSION 13
/// (17 v2 baseline + 12 v3.3 additions via task 023 + 1 sprk_customerid alt-key via task 199).
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

    // ---- customer-provisioning-orchestration-r1 task 199 (2026-08-27) ----
    /// <summary>Customer short-id (kebab-case). ALT-KEY on sprk_customerid_key. Used by L2 CustomerRunGuard + DataverseRegistryConcurrencyStore.</summary>
    public string? CustomerId { get; set; }

    // ---- task 023 v2-rolled-forward additions (design.md §6.1) ----
    /// <summary>Azure subscription hosting this customer environment. ADR-044: canonical bare-lowercase GUID.</summary>
    public string? AzureSubscriptionId { get; set; }
    /// <summary>Azure resource group name (pattern: rg-spaarke-{customerId}-{env}).</summary>
    public string? ResourceGroupName { get; set; }
    /// <summary>BFF App Service name (pattern: sprk-{customerId}-{env}-api).</summary>
    public string? AppServiceName { get; set; }
    /// <summary>Customer Key Vault name (Model 1 shared vs Model 2 per-customer; canonical per §7.9).</summary>
    public string? KeyVaultName { get; set; }
    /// <summary>SPE container-type ID (distinct from SpeContainerId which is the container instance).</summary>
    public string? ContainerTypeId { get; set; }
    /// <summary>H13 acceptance-gate first-transition-to-Ready timestamp. Non-null indicates upgrade-mode for subsequent handler runs.</summary>
    public DateTimeOffset? ProvisionedOn { get; set; }

    // ---- task 023 v3 additions (design.md §4D I5, §3A A1, D18) ----
    /// <summary>Active ProvisioningRun ID (Cosmos document ID). L2 sets null→newRunId; conflict = 409. Cleared on terminal state. ADR-044 canonical.</summary>
    public string? CurrentRunId { get; set; }
    /// <summary>Deployment tenancy tier. Drives Bicep composition + handler behavior (§4.1a).</summary>
    public TenancyModel? TenancyModelValue { get; set; }
    /// <summary>Entra tenant ID. Model 1: Spaarke tenant. Model 2: customer tenant via H0.5 consent. IMMUTABLE post-placeholder-create (§4D I1). ADR-044 canonical.</summary>
    public string? TenantId { get; set; }

    // ---- task 023 v3.3 additions (design.md §14A upgrade model, §7.9 cache-bust) ----
    /// <summary>BFF version pinned to this customer environment (semantic, e.g. 1.4.2). H0 upgrade-mode preflight reads this.</summary>
    public string? BffVersion { get; set; }
    /// <summary>Dataverse solution version pinned to this customer environment (semantic, e.g. 2.1.0). H0 upgrade-mode preflight companion.</summary>
    public string? SolutionVersion { get; set; }
    /// <summary>Cache-bust token distributed to clients after upgrade so they invalidate cached bundles (localStorage 60-min TTL). H7 sets a new value on upgrade.</summary>
    public string? ClientCacheBustToken { get; set; }

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
    /// Extended by customer-provisioning-orchestration-r1 task 199 (2026-08-27) with the 12 v3.3
    /// columns from task 023 + sprk_customerid alt-key. Total: 30 columns.
    /// </summary>
    public static readonly string[] AllColumns =
    {
        // v2 baseline (17 including PK)
        "sprk_dataverseenvironmentid", "sprk_name", "sprk_environmenttype",
        "sprk_dataverseurl", "sprk_mdaappid", "sprk_description",
        "sprk_isactive", "sprk_isdefault", "sprk_setupstatus",
        "sprk_envaccountdomain", "sprk_businessunitname", "sprk_teamname",
        "sprk_specontainerid", "sprk_securitygroupid", "sprk_defaultdurationdays",
        "sprk_licenseconfigjson", "sprk_adminemails",
        // task 199 (2026-08-27): sprk_customerid alt-key
        "sprk_customerid",
        // task 023 v2-rolled-forward (6)
        "sprk_azuresubscriptionid", "sprk_resourcegroupname", "sprk_appservicename",
        "sprk_keyvaultname", "sprk_containertypeid", "sprk_provisionedon",
        // task 023 v3 additions (3)
        "sprk_currentrunid", "sprk_tenancymodel", "sprk_tenantid",
        // task 023 v3.3 additions (3)
        // REG-06 (2026-08-27): Dataverse logical names are ALWAYS lowercase — the schema
        // SchemaName may be PascalCase (`sprk_ClientCacheBustToken`) but the OData wire
        // form used in $select / property reads / PATCH bodies is lowercase
        // (`sprk_clientcachebusttoken`). Verified via Dataverse MCP describe against admin
        // env spaarkedev1 (2026-08-27). PascalCase here silently broke the read path
        // (TryGetProperty returned false → ClientCacheBustToken always null) and would
        // 400 on future writes. All AllColumns entries are lowercase per this rule (see
        // ArchTest DataverseEnvironmentRecordTests.AllColumns_AreLowerCaseLogicalNames).
        "sprk_bffversion", "sprk_solutionversion", "sprk_clientcachebusttoken"
    };

    /// <summary>
    /// Selects the default environment from a list of active environments. Returns the record
    /// where <see cref="IsDefault"/> is true; if none is marked default, falls back to the first
    /// entry. Throws <see cref="InvalidOperationException"/> if the list is empty.
    ///
    /// Preserves the historical selection semantics from the (now removed) usages of
    /// <c>DemoProvisioningOptions.Environments</c> + <c>DemoProvisioningOptions.DefaultEnvironment</c>
    /// so that DemoExpirationService and RegistrationDataverseService can migrate onto
    /// <see cref="DataverseEnvironmentService"/> without functional regression. See
    /// customer-provisioning-orchestration-r1 tasks 080 and 081.
    /// </summary>
    public static DataverseEnvironmentRecord SelectDefault(IReadOnlyList<DataverseEnvironmentRecord> envs)
    {
        ArgumentNullException.ThrowIfNull(envs);
        if (envs.Count == 0)
            throw new InvalidOperationException("No active Dataverse environments configured.");
        return envs.FirstOrDefault(e => e.IsDefault) ?? envs[0];
    }

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
            AppId = json.TryGetProperty("sprk_mdaappid", out var appProp) ? appProp.GetString() : null,
            Description = json.TryGetProperty("sprk_description", out var descProp) ? descProp.GetString() : null,
            IsActive = json.TryGetProperty("sprk_isactive", out var activeProp) && activeProp.ValueKind == JsonValueKind.True,
            IsDefault = json.TryGetProperty("sprk_isdefault", out var defProp) && defProp.ValueKind == JsonValueKind.True,
            SetupStatus = json.TryGetProperty("sprk_setupstatus", out var ssProp) && ssProp.ValueKind == JsonValueKind.Number
                ? (EnvironmentSetupStatus)ssProp.GetInt32() : null,
            AccountDomain = json.TryGetProperty("sprk_envaccountdomain", out var adProp) ? adProp.GetString() : null,
            BusinessUnitName = json.TryGetProperty("sprk_businessunitname", out var buProp) ? buProp.GetString() : null,
            TeamName = json.TryGetProperty("sprk_teamname", out var tmProp) ? tmProp.GetString() : null,
            SpeContainerId = json.TryGetProperty("sprk_specontainerid", out var speProp) ? speProp.GetString() : null,
            SecurityGroupId = json.TryGetProperty("sprk_securitygroupid", out var sgProp) ? sgProp.GetString() : null,
            DefaultDurationDays = json.TryGetProperty("sprk_defaultdurationdays", out var ddProp) && ddProp.ValueKind == JsonValueKind.Number
                ? ddProp.GetInt32() : null,
            LicenseConfigJson = json.TryGetProperty("sprk_licenseconfigjson", out var lcProp) ? lcProp.GetString() : null,
            AdminEmails = json.TryGetProperty("sprk_adminemails", out var aeProp) ? aeProp.GetString() : null,
            // task 199 (2026-08-27): sprk_customerid alt-key
            CustomerId = json.TryGetProperty("sprk_customerid", out var ciProp) ? ciProp.GetString() : null,
            // task 023 v2-rolled-forward
            AzureSubscriptionId = json.TryGetProperty("sprk_azuresubscriptionid", out var asiProp) ? asiProp.GetString() : null,
            ResourceGroupName = json.TryGetProperty("sprk_resourcegroupname", out var rgnProp) ? rgnProp.GetString() : null,
            AppServiceName = json.TryGetProperty("sprk_appservicename", out var asnProp) ? asnProp.GetString() : null,
            KeyVaultName = json.TryGetProperty("sprk_keyvaultname", out var kvnProp) ? kvnProp.GetString() : null,
            ContainerTypeId = json.TryGetProperty("sprk_containertypeid", out var ctiProp) ? ctiProp.GetString() : null,
            ProvisionedOn = json.TryGetProperty("sprk_provisionedon", out var poProp) && poProp.ValueKind == JsonValueKind.String
                ? DateTimeOffset.Parse(poProp.GetString()!) : null,
            // task 023 v3 additions
            CurrentRunId = json.TryGetProperty("sprk_currentrunid", out var criProp) ? criProp.GetString() : null,
            TenancyModelValue = json.TryGetProperty("sprk_tenancymodel", out var tmProp2) && tmProp2.ValueKind == JsonValueKind.Number
                ? (TenancyModel)tmProp2.GetInt32() : null,
            TenantId = json.TryGetProperty("sprk_tenantid", out var tiProp) ? tiProp.GetString() : null,
            // task 023 v3.3 additions
            BffVersion = json.TryGetProperty("sprk_bffversion", out var bvProp) ? bvProp.GetString() : null,
            SolutionVersion = json.TryGetProperty("sprk_solutionversion", out var svProp) ? svProp.GetString() : null,
            // REG-06: lowercase logical name (verified via Dataverse MCP describe 2026-08-27).
            ClientCacheBustToken = json.TryGetProperty("sprk_clientcachebusttoken", out var ccbtProp) ? ccbtProp.GetString() : null,
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
