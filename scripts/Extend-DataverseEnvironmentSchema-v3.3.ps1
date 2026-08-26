<#
.SYNOPSIS
    Extends the sprk_dataverseenvironment entity with 12 new columns for
    customer-provisioning-orchestration-r1 (registry schema v3.3).

.DESCRIPTION
    Additive-only extension of the v2 baseline entity (16 columns) created by
    scripts/Create-DataverseEnvironmentSchema.ps1. Adds 12 new columns and one
    new local Choice option-set (sprk_tenancymodel).

    Mirrors the Web API PowerShell authoring pattern of the v2 baseline (per
    the operational pattern already established in this repo for
    sprk_dataverseenvironment — no Entity.xml has ever been unpacked to
    src/dataverse/solutions/spaarke_core/**; see project doc-deviation note
    projects/customer-provisioning-orchestration-r1/notes/registry-column-audit-2026-08.md).

    Idempotent: skips any column already present on the entity. Never modifies
    an existing column (MUST NOT rename/retype the existing 16 per FR-26).

    The 12 new columns (all citing FR-26 + section per project constraint):

    6 v2 additions carried through v3 (per design.md §6.1 rows 522-527):
      1. sprk_azuresubscriptionid   String(100), Optional
      2. sprk_resourcegroupname     String(200), Optional
      3. sprk_appservicename        String(200), Optional
      4. sprk_keyvaultname          String(200), Optional
      5. sprk_containertypeid       String(100), Optional  (distinct from sprk_specontainerid)
      6. sprk_provisionedon         DateTime,    Optional  (missed by spec FR-26 "11 new columns" enumeration — discovery §9)

    3 v3 additions (per design.md §6.1 rows 528-530):
      7. sprk_currentrunid          String(40),  Optional  (§4D I5 concurrency guard; ADR-044 canonical string form)
      8. sprk_tenancymodel          Choice,      Optional  (§3A A1 Model1Shared=0 / Model2Dedicated=1)
      9. sprk_tenantid              String(40),  Optional  (D18 Entra tenant; ADR-044 canonical string form)

    3 v3.3 additions (per design.md §14A):
     10. sprk_bffversion            String(50),  Optional  (§14A upgrade-mode preflight version compat)
     11. sprk_solutionversion       String(50),  Optional  (§14A upgrade-mode preflight version compat)
     12. sprk_ClientCacheBustToken  String(100), Optional  (§7.9 upgrade cache-bust token; PascalCase per existing FR-35 grandfather)

    ADR-044 note: GUID-shaped columns (sprk_currentrunid, sprk_tenantid,
    sprk_azuresubscriptionid) are stored as String attributes; canonicalization
    (bare, lowercase) is the CALLER's responsibility per ADR-044 ("normalize at
    every boundary"). Column descriptions cite ADR-044 for downstream tracing.

.PARAMETER EnvironmentDomain
    The Dataverse environment domain, e.g. spaarkedev1.crm.dynamics.com

.EXAMPLE
    .\Extend-DataverseEnvironmentSchema-v3.3.ps1 -EnvironmentDomain "spaarkedev1.crm.dynamics.com"

.NOTES
    Task: customer-provisioning-orchestration-r1 / 023
    Depends on: scripts/Create-DataverseEnvironmentSchema.ps1 (v2 baseline already run)
    Consumers: DataverseEnvironmentRecord.cs will be extended in a LATER task
               (H0 preflight, H2a Bicep composition, H12c runtime refs) — this
               task is schema-only.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$EnvironmentDomain
)

$ErrorActionPreference = 'Continue'
$token = az account get-access-token --resource "https://$EnvironmentDomain" --query accessToken -o tsv
if (-not $token) { Write-Error "Failed to get token"; exit 1 }
Write-Host "Token acquired" -ForegroundColor Green

$BaseUrl = "https://$EnvironmentDomain/api/data/v9.2"
$headers = @{
    "Authorization"    = "Bearer $token"
    "OData-MaxVersion" = "4.0"
    "OData-Version"    = "4.0"
    "Content-Type"     = "application/json"
    "Accept"           = "application/json"
    "Prefer"           = "return=representation"
}

function New-Label([string]$Text) {
    @{ "@odata.type" = "Microsoft.Dynamics.CRM.Label"; "LocalizedLabels" = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; "Label" = $Text; "LanguageCode" = 1033 }) }
}

function Invoke-DV([string]$Ep, [string]$Method = "GET", [object]$Body = $null) {
    $p = @{ Uri = "$BaseUrl/$Ep"; Headers = $headers; Method = $Method; UseBasicParsing = $true }
    if ($Body) { $p.Body = $Body | ConvertTo-Json -Depth 20 -Compress }
    try { $r = Invoke-RestMethod @p; @{ Success = $true; Data = $r } }
    catch { @{ Success = $false; Error = $_.Exception.Message } }
}

function Test-AttributeExists([string]$LogicalName) {
    try {
        Invoke-RestMethod -Uri "$BaseUrl/EntityDefinitions(LogicalName='sprk_dataverseenvironment')/Attributes(LogicalName='$LogicalName')?`$select=LogicalName" `
            -Headers $headers -Method GET -UseBasicParsing -ErrorAction Stop | Out-Null
        return $true
    } catch { return $false }
}

# Precondition: entity must already exist (v2 baseline run first)
try {
    Invoke-RestMethod -Uri "$BaseUrl/EntityDefinitions(LogicalName='sprk_dataverseenvironment')?`$select=LogicalName" `
        -Headers $headers -Method GET -UseBasicParsing -ErrorAction Stop | Out-Null
    Write-Host "Entity sprk_dataverseenvironment present - proceeding with additive extension" -ForegroundColor Cyan
} catch {
    Write-Error "Entity sprk_dataverseenvironment NOT FOUND. Run Create-DataverseEnvironmentSchema.ps1 first."
    exit 1
}

# ============================================================================
# Step 1: String columns (10 of 12 — 5 v2 + 5 v3/v3.3)
# ============================================================================
Write-Host "`nStep 1: String columns (v2 + v3 + v3.3)" -ForegroundColor Cyan

$stringCols = @(
    # ---- v2 additions (5 String; sprk_provisionedon handled in Step 2) ----
    @{
        N    = "sprk_azuresubscriptionid"; D = "Azure Subscription ID"; L = 100; R = "None"
        Desc = "Azure subscription hosting this customer environment. FR-26 v2 addition (design.md §6.1). ADR-044: canonicalize to bare-lowercase GUID at every boundary (callers own normalization)."
    },
    @{
        N    = "sprk_resourcegroupname"; D = "Resource Group"; L = 200; R = "None"
        Desc = "Azure resource group name (pattern: rg-spaarke-{customerId}-{env}). FR-26 v2 addition (design.md §7.1)."
    },
    @{
        N    = "sprk_appservicename"; D = "App Service Name"; L = 200; R = "None"
        Desc = "BFF App Service name (pattern: sprk-{customerId}-{env}-api). FR-26 v2 addition (design.md §7.1)."
    },
    @{
        N    = "sprk_keyvaultname"; D = "Key Vault Name"; L = 200; R = "None"
        Desc = "Customer Key Vault name (Model 1: sprk-{env}-kv; Model 2: sprk-{customerId}-{env}-kv; dev exception: spaarke-spekvcert). FR-26 v2 addition (design.md §7.1 canonical naming per r3 task 063)."
    },
    @{
        N    = "sprk_containertypeid"; D = "SPE Container Type ID"; L = 100; R = "None"
        Desc = "SharePoint Embedded container-type ID (distinct from sprk_specontainerid which stores the container instance ID). FR-26 v2 addition (design.md §6.1)."
    },
    # ---- v3 additions (2 String; sprk_tenancymodel handled in Step 3) ----
    @{
        N    = "sprk_currentrunid"; D = "Current Provisioning Run ID"; L = 40; R = "None"
        Desc = "Active ProvisioningRun ID (Cosmos runs container document id). L2 optimistically sets null->newRunId; conflict = 409 with winning run ID. Cleared on terminal state. FR-26 v3 addition (design.md §4D I5 concurrency guard). ADR-044: canonicalize to bare-lowercase GUID at every boundary."
    },
    @{
        N    = "sprk_tenantid"; D = "Entra Tenant ID"; L = 40; R = "None"
        Desc = "Entra tenant ID. Model 1: Spaarke tenant. Model 2: customer tenant, captured via H0.5 consent-callback. FR-26 v3 addition (design.md D18 / §4D I1 tenant isolation). ADR-044: canonicalize to bare-lowercase GUID at every boundary."
    },
    # ---- v3.3 additions (3 String) ----
    @{
        N    = "sprk_bffversion"; D = "BFF Version"; L = 50; R = "None"
        Desc = "BFF version pinned to this customer environment (semantic version, e.g. 1.4.2). H0 upgrade-mode preflight reads this + sprk_solutionversion, queries version-compatibility matrix, blocks red-cell pairs. FR-26 v3.3 addition (design.md §14A upgrade model)."
    },
    @{
        N    = "sprk_solutionversion"; D = "Dataverse Solution Version"; L = 50; R = "None"
        Desc = "Dataverse solution version pinned to this customer environment (semantic version, e.g. 2.1.0). H0 upgrade-mode preflight companion to sprk_bffversion. FR-26 v3.3 addition (design.md §14A upgrade model)."
    },
    @{
        # PascalCase schema name is intentional per project convention — see design.md §7.9
        # canonical-naming grandfather clause; the display name is human-friendly.
        N    = "sprk_ClientCacheBustToken"; D = "Client Cache-Bust Token"; L = 100; R = "None"
        Desc = "Cache-bust token distributed to clients after upgrade so they invalidate cached bundles (localStorage 60-min TTL). H7 sets a new value on upgrade. FR-26 v3.3 addition (design.md §7.9 / §14A upgrade cache-bust)."
    }
)

foreach ($c in $stringCols) {
    if (Test-AttributeExists $c.N) {
        Write-Host "  = $($c.N) (already present - skipped)" -ForegroundColor Yellow
        continue
    }
    $r = Invoke-DV -Ep "EntityDefinitions(LogicalName='sprk_dataverseenvironment')/Attributes" -Method "POST" -Body @{
        "@odata.type"  = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName"   = $c.N
        "RequiredLevel" = @{ "Value" = $c.R }
        "MaxLength"    = $c.L
        "DisplayName"  = New-Label $c.D
        "Description"  = New-Label $c.Desc
    }
    if ($r.Success) { Write-Host "  + $($c.N)" -ForegroundColor Green }
    else            { Write-Host "  x $($c.N): $($r.Error)" -ForegroundColor Red }
}

# ============================================================================
# Step 2: DateTime column (sprk_provisionedon — v2 addition; missed by FR-26 "11" count)
# ============================================================================
Write-Host "`nStep 2: DateTime column (v2)" -ForegroundColor Cyan

if (Test-AttributeExists "sprk_provisionedon") {
    Write-Host "  = sprk_provisionedon (already present - skipped)" -ForegroundColor Yellow
} else {
    $r = Invoke-DV -Ep "EntityDefinitions(LogicalName='sprk_dataverseenvironment')/Attributes" -Method "POST" -Body @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
        "SchemaName"    = "sprk_provisionedon"
        "RequiredLevel" = @{ "Value" = "None" }
        "DisplayName"   = New-Label "Provisioned On"
        "Description"   = New-Label "Timestamp when H13 acceptance gate first transitioned this environment to Setup Status = Ready. Non-null indicates upgrade-mode for subsequent handler runs. FR-26 v2 addition (design.md §6.1; missed by spec FR-26 '11 new columns' enumeration — discovery report §9)."
        "Format"        = "DateAndTime"
        "DateTimeBehavior" = @{ "Value" = "UserLocal" }
    }
    if ($r.Success) { Write-Host "  + sprk_provisionedon" -ForegroundColor Green }
    else            { Write-Host "  x sprk_provisionedon: $($r.Error)" -ForegroundColor Red }
}

# ============================================================================
# Step 3: Choice column with local option-set (sprk_tenancymodel)
# ============================================================================
Write-Host "`nStep 3: Choice column (v3 - sprk_tenancymodel)" -ForegroundColor Cyan

if (Test-AttributeExists "sprk_tenancymodel") {
    Write-Host "  = sprk_tenancymodel (already present - skipped)" -ForegroundColor Yellow
} else {
    $r = Invoke-DV -Ep "EntityDefinitions(LogicalName='sprk_dataverseenvironment')/Attributes" -Method "POST" -Body @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
        "SchemaName"    = "sprk_tenancymodel"
        "RequiredLevel" = @{ "Value" = "None" }
        "DisplayName"   = New-Label "Tenancy Model"
        "Description"   = New-Label "Deployment tenancy tier. Model1Shared (trial/SMB — shared platform floors per §3A A1). Model2Dedicated (regulated/enterprise — dedicated stamp per D3). Drives Bicep stack composition (model1-shared.bicep vs model2-full.bicep) + handler behavior differences (§4.1a). FR-26 v3 addition (design.md §3A A1 / §6.1)."
        "OptionSet"     = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.OptionSetMetadata"
            "IsGlobal"      = $false
            "OptionSetType" = "Picklist"
            "Options"       = @(
                @{ "Value" = 0; "Label" = New-Label "Model1Shared";    "Description" = New-Label "Model 1: trial/SMB shared-platform tier (§3A A1)." },
                @{ "Value" = 1; "Label" = New-Label "Model2Dedicated"; "Description" = New-Label "Model 2: regulated/enterprise dedicated stamp (D3)." }
            )
        }
    }
    if ($r.Success) { Write-Host "  + sprk_tenancymodel (Model1Shared=0, Model2Dedicated=1)" -ForegroundColor Green }
    else            { Write-Host "  x sprk_tenancymodel: $($r.Error)" -ForegroundColor Red }
}

# ============================================================================
# Step 4: Publish
# ============================================================================
Write-Host "`nStep 4: Publish entity customizations" -ForegroundColor Cyan
$r = Invoke-DV -Ep "PublishXml" -Method "POST" -Body @{
    "ParameterXml" = "<importexportxml><entities><entity>sprk_dataverseenvironment</entity></entities></importexportxml>"
}
if ($r.Success) { Write-Host "  Published" -ForegroundColor Green }
else            { Write-Host "  Publish failed: $($r.Error)" -ForegroundColor Red }

Write-Host "`nDONE - sprk_dataverseenvironment extended with 12 new columns (v3.3) in $EnvironmentDomain" -ForegroundColor Green
Write-Host "Total columns now: 16 (v2 baseline) + 12 (v3.3 extension) = 28" -ForegroundColor Cyan
