<#
.SYNOPSIS
    Adds the missing sprk_customerid column + alternate key to sprk_dataverseenvironment.

.DESCRIPTION
    Companion to scripts/Extend-DataverseEnvironmentSchema-v3.3.ps1 (task 023).
    Adds the 13th column that task 023 forgot to author but which L2 code REQUIRES:

      - sprk_customerid: String(8), Recommended — the ALT-KEY used by
        Sprk.Provisioning.ControlPlane.Core/Concurrency/DataverseRegistryConcurrencyStore
        (CustomerIdColumn) and CustomerRunGuard for row lookup via
        $filter=sprk_customerid eq '{id}' pattern.

    Also registers sprk_customerid as an alternate key on the entity to
    enforce uniqueness + provide index protection for lookups.

    Idempotent: skips column + alt-key if already present. If the column exists with a
    MaxLength other than 8 (it was created as 64 before T237), the column is updated to 8 —
    after checking that no row holds a longer value (the script stops if one does).

    MaxLength 8 is the customerId standard's hard rule (T237 / owner D10;
    docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md § "The customerId standard"):
    customer.bicep's Key Vault name sprk-{customerId}-{env}-kv must fit 24 characters
    without ending in a hyphen. The character rule (^[a-z][a-z0-9]{2,7}$) cannot be
    enforced by a text column — it is enforced at intake (POST /api/runs, intake schema,
    /provision-environment Step 1a).

.PARAMETER EnvironmentDomain
    Full Dataverse environment domain, e.g. spaarkedev1.crm.dynamics.com

.EXAMPLE
    .\Add-CustomerIdColumn.ps1 -EnvironmentDomain "spaarkedev1.crm.dynamics.com"

.NOTES
    Task: customer-provisioning-orchestration-r1 / 199 (reconciliation task)
    Discovered: 2026-08-26 first live batch dispatch of task 186 —
                task 023 script listed 12 columns but sprk_customerid was
                MISSING from the enumeration AND from the script itself,
                even though every L2 lookup path depends on it.
    Depends on: scripts/Extend-DataverseEnvironmentSchema-v3.3.ps1 (task 023
                12-column extension) already run against the target env.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$EnvironmentDomain
)

$ErrorActionPreference = 'Continue'
$CustomerIdMaxLength = 8
$CustomerIdDescription = "Customer short-id: the customerId standard ^[a-z][a-z0-9]{2,7}$ (3-8 lowercase letters/digits, starts with a letter; docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md). MaxLength 8 is the only part a text column can enforce; the character rule is enforced at intake. ALT-KEY used by L2 CustomerRunGuard + DataverseRegistryConcurrencyStore via `$filter=sprk_customerid eq '{id}'`. Added 2026-08-26 (task 199); MaxLength 64 -> 8 by T237 (2026-09-30)."
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

function Test-AltKeyExists([string]$SchemaName) {
    try {
        $r = Invoke-RestMethod -Uri "$BaseUrl/EntityDefinitions(LogicalName='sprk_dataverseenvironment')/Keys?`$select=SchemaName&`$filter=SchemaName%20eq%20'$SchemaName'" `
            -Headers $headers -Method GET -UseBasicParsing -ErrorAction Stop
        return ($r.value.Count -gt 0)
    } catch { return $false }
}

# Pre-check: entity must exist
try {
    Invoke-RestMethod -Uri "$BaseUrl/EntityDefinitions(LogicalName='sprk_dataverseenvironment')?`$select=LogicalName" `
        -Headers $headers -Method GET -UseBasicParsing -ErrorAction Stop | Out-Null
    Write-Host "Entity sprk_dataverseenvironment present - proceeding" -ForegroundColor Cyan
} catch {
    Write-Error "Entity sprk_dataverseenvironment NOT FOUND. Run Create-DataverseEnvironmentSchema.ps1 first."
    exit 1
}

# ============================================================================
# Step 1: Add sprk_customerid String column
# ============================================================================
Write-Host "`nStep 1: Add sprk_customerid column" -ForegroundColor Cyan

if (Test-AttributeExists "sprk_customerid") {
    # T237: an existing column created at MaxLength 64 is corrected to 8 — but never below a value
    # already stored (that would need a re-issued id, which is an owner decision).
    $attrPath = "EntityDefinitions(LogicalName='sprk_dataverseenvironment')/Attributes(LogicalName='sprk_customerid')"
    $current = Invoke-DV -Ep "$attrPath/Microsoft.Dynamics.CRM.StringAttributeMetadata"
    if (-not $current.Success) { Write-Host "  x read sprk_customerid metadata: $($current.Error)" -ForegroundColor Red; exit 1 }
    $attr = $current.Data
    $originalMaxLength = $attr.MaxLength   # captured before $attr is modified below (same object as $current.Data)
    if ($attr.MaxLength -eq $CustomerIdMaxLength) {
        Write-Host "  = sprk_customerid (already present, MaxLength $CustomerIdMaxLength - skipped)" -ForegroundColor Yellow
    } else {
        $rows = Invoke-DV -Ep "sprk_dataverseenvironments?`$select=sprk_customerid&`$filter=sprk_customerid ne null"
        if (-not $rows.Success) { Write-Host "  x read existing sprk_customerid values: $($rows.Error)" -ForegroundColor Red; exit 1 }
        $tooLong = @($rows.Data.value | Where-Object { $_.sprk_customerid.Length -gt $CustomerIdMaxLength })
        if ($tooLong.Count -gt 0) {
            Write-Host "  x $($tooLong.Count) row(s) hold a sprk_customerid longer than $CustomerIdMaxLength ($(($tooLong | ForEach-Object { $_.sprk_customerid }) -join ', ')). Re-issue those ids first (owner decision) - MaxLength NOT changed." -ForegroundColor Red
            exit 1
        }
        $attr.PSObject.Properties.Remove('@odata.context')
        $attr | Add-Member -NotePropertyName '@odata.type' -NotePropertyValue 'Microsoft.Dynamics.CRM.StringAttributeMetadata' -Force
        $attr.MaxLength = $CustomerIdMaxLength
        $attr.Description = New-Label $CustomerIdDescription
        try {
            Invoke-RestMethod -Uri "$BaseUrl/$attrPath" -Method PUT -UseBasicParsing -ErrorAction Stop `
                -Headers ($headers + @{ 'MSCRM.MergeLabels' = 'true' }) `
                -Body ($attr | ConvertTo-Json -Depth 20 -Compress) | Out-Null
            Write-Host "  ~ sprk_customerid MaxLength $originalMaxLength -> $CustomerIdMaxLength" -ForegroundColor Green
        } catch {
            Write-Host "  x update sprk_customerid MaxLength: $($_.Exception.Message)" -ForegroundColor Red
            exit 1
        }
    }
} else {
    $r = Invoke-DV -Ep "EntityDefinitions(LogicalName='sprk_dataverseenvironment')/Attributes" -Method "POST" -Body @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName"    = "sprk_customerid"
        "RequiredLevel" = @{ "Value" = "Recommended" }
        "MaxLength"     = $CustomerIdMaxLength
        "DisplayName"   = New-Label "Customer ID"
        "Description"   = New-Label $CustomerIdDescription
    }
    if ($r.Success) { Write-Host "  + sprk_customerid" -ForegroundColor Green }
    else            { Write-Host "  x sprk_customerid: $($r.Error)" -ForegroundColor Red; exit 1 }
}

# ============================================================================
# Step 2: Register sprk_customerid as alternate key
# ============================================================================
Write-Host "`nStep 2: Register sprk_customerid as alternate key" -ForegroundColor Cyan

$altKeySchemaName = "sprk_customerid_key"
if (Test-AltKeyExists $altKeySchemaName) {
    Write-Host "  = $altKeySchemaName (already present - skipped)" -ForegroundColor Yellow
} else {
    $r = Invoke-DV -Ep "EntityDefinitions(LogicalName='sprk_dataverseenvironment')/Keys" -Method "POST" -Body @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.EntityKeyMetadata"
        "SchemaName"    = $altKeySchemaName
        "DisplayName"   = New-Label "Customer ID Key"
        "KeyAttributes" = @("sprk_customerid")
    }
    if ($r.Success) {
        Write-Host "  + $altKeySchemaName (async index creation may take 2-5 min)" -ForegroundColor Green
    } else {
        Write-Host "  x $altKeySchemaName`: $($r.Error)" -ForegroundColor Red
        Write-Host "  NOTE: alt-key registration failure is non-fatal for immediate use — L2 code queries via `$filter=sprk_customerid eq '{id}'` which works without alt-key indexing (just slower). Retry alt-key registration later via maker portal if needed." -ForegroundColor Yellow
    }
}

# ============================================================================
# Step 3: Publish
# ============================================================================
Write-Host "`nStep 3: Publish entity customizations" -ForegroundColor Cyan
$r = Invoke-DV -Ep "PublishXml" -Method "POST" -Body @{
    "ParameterXml" = "<importexportxml><entities><entity>sprk_dataverseenvironment</entity></entities></importexportxml>"
}
if ($r.Success) { Write-Host "  Published" -ForegroundColor Green }
else            { Write-Host "  Publish failed: $($r.Error)" -ForegroundColor Red }

Write-Host "`nDONE - sprk_customerid column + alt-key added to sprk_dataverseenvironment in $EnvironmentDomain" -ForegroundColor Green
Write-Host "Total columns now: 17 (v2 baseline incl PK) + 12 (v3.3 extension) + 1 (customerid reconciliation) = 30" -ForegroundColor Cyan
