<#
.SYNOPSIS
    Deploy the sprk_navitem custom entity (per-user Navigator history/pin/bookmark store)
    per spaarke-side-pane-navigation-history-r1 spec FR-06 / task 020.

.DESCRIPTION
    Creates three GLOBAL option sets (sprk_type, sprk_source, sprk_pagetype), then the
    UserOwned sprk_navitem entity with its primary name attribute (sprk_displayname), then
    the remaining data-model fields (the three picklists bound to the global option sets
    created in step 1, plus sprk_targetlogicalname / sprk_targetid / sprk_url /
    sprk_lastvisited / sprk_visitcount), then publishes.

    ORDER IS LOAD-BEARING: the global option sets MUST exist before any picklist attribute
    that references them via GlobalOptionSet@odata.bind.

    Idempotent: skips each global option set / entity / attribute create if it already
    exists (Test-GlobalOptionSetExists / Test-EntityExists / Test-AttributeExists). Re-running
    this script against an already-provisioned environment is a no-op — it neither errors nor
    creates duplicate entities/attributes/option-set values.

    OWNERSHIP: sprk_navitem is UserOwned (NOT "User or Team" like the sprk_todo exemplar).
    This is an intentional override for per-user isolation (NFR-03) — Navigator history/pins
    are inherently personal and must never be assignable to a team.

    DEPLOYMENT APPROACH:
    ---------------------
    Web API (Microsoft.Dynamics.CRM Metadata) — PAC CLI has no `pac table create` /
    `pac column create`. Same approach as Deploy-SprkTodoEntity.ps1 / Deploy-ChartDefinitionEntity.ps1.

    THIS SCRIPT IS AUTHOR-ONLY FOR TASK 020 — it is not executed as part of task 020.
    Deployment against a target environment is task 021.

    PREREQUISITES:
    --------------
    - Azure CLI logged in:  az login
    - Target env URL passed via -EnvironmentUrl or DATAVERSE_URL env var
    - Target solution SpaarkeCore (unmanaged) present in the environment

.PARAMETER EnvironmentUrl
    Target Dataverse environment URL (e.g., https://spaarkedev1.crm.dynamics.com).
    Defaults to $env:DATAVERSE_URL.

.PARAMETER SolutionUniqueName
    Unmanaged solution to add all created components to. Defaults to "SpaarkeCore".

.EXAMPLE
    .\Deploy-SprkNavItemEntity.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com"

.EXAMPLE
    $env:DATAVERSE_URL = "https://spaarkedev1.crm.dynamics.com"
    .\Deploy-SprkNavItemEntity.ps1

.NOTES
    Project:  spaarke-side-pane-navigation-history-r1
    Task:     020 - Author sprk_navitem entity schema + deploy script (author-only; deploy is task 021)
    Schema:   src/solutions/SpaarkeCore/entities/sprk_navitem/entity-schema.md
    Created:  2026-08-13
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$EnvironmentUrl = $env:DATAVERSE_URL,

    [Parameter(Mandatory = $false)]
    [string]$SolutionUniqueName = "SpaarkeCore",

    [switch]$SkipVerification
)

$ErrorActionPreference = "Stop"
$StartTime = Get-Date

if (-not $EnvironmentUrl) {
    throw "EnvironmentUrl is required. Pass -EnvironmentUrl or set DATAVERSE_URL env var."
}
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')

# ============================================================================
# HELPERS
# ============================================================================

function Write-Header {
    param([string]$Message)
    Write-Host ""
    Write-Host "======================================================" -ForegroundColor Cyan
    Write-Host "  $Message" -ForegroundColor Cyan
    Write-Host "======================================================" -ForegroundColor Cyan
}

function Get-DataverseToken {
    param([string]$Url)
    $tokenResult = az account get-access-token --resource $Url --query "accessToken" -o tsv 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to get token from Azure CLI. Error: $tokenResult. Make sure you're logged in with 'az login'"
    }
    return $tokenResult.Trim()
}

function Invoke-DataverseApi {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$Endpoint,
        [string]$Method = "GET",
        [object]$Body = $null
    )
    $headers = @{
        "Authorization"    = "Bearer $Token"
        "OData-MaxVersion" = "4.0"
        "OData-Version"    = "4.0"
        "Accept"           = "application/json"
        "Content-Type"     = "application/json; charset=utf-8"
        "Prefer"           = "odata.include-annotations=*"
        "MSCRM.SolutionUniqueName" = $SolutionUniqueName
    }
    $uri = "$BaseUrl/api/data/v9.2/$Endpoint"
    $params = @{ Uri = $uri; Method = $Method; Headers = $headers }
    if ($Body) {
        $params.Body = ($Body | ConvertTo-Json -Depth 25)
    }
    try {
        return Invoke-RestMethod @params
    }
    catch {
        $errorDetails = $_.Exception.Message
        if ($_.ErrorDetails.Message) {
            $errJson = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue
            if ($errJson.error.message) {
                $errorDetails = $errJson.error.message
            }
        }
        throw "API Error ($Method $Endpoint): $errorDetails"
    }
}

function New-Label {
    param([string]$Text)
    return @{
        "@odata.type"     = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels" = @(
            @{
                "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                "Label"       = $Text
                "LanguageCode" = 1033
            }
        )
    }
}

function New-OptionLabel {
    # Same shape as New-Label but returned inline for Options[].Label (kept separate for clarity
    # at call sites that build Options arrays).
    param([string]$Text)
    return New-Label -Text $Text
}

function Test-EntityExists {
    param([string]$Token, [string]$BaseUrl, [string]$LogicalName)
    try {
        Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "EntityDefinitions(LogicalName='$LogicalName')?`$select=LogicalName" | Out-Null
        return $true
    } catch {
        if ($_.Exception.Message -match "does not exist|404|Could not find") { return $false }
        throw
    }
}

function Test-AttributeExists {
    param([string]$Token, [string]$BaseUrl, [string]$EntityLogicalName, [string]$AttributeLogicalName)
    try {
        Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes(LogicalName='$AttributeLogicalName')?`$select=LogicalName" | Out-Null
        return $true
    } catch {
        return $false
    }
}

function Get-GlobalOptionSet {
    param([string]$Token, [string]$BaseUrl, [string]$Name)
    try {
        return Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "GlobalOptionSetDefinitions(Name='$Name')"
    } catch {
        # Any error (404 / "does not exist" / "Could not find") means the option set doesn't exist.
        return $null
    }
}

function Test-GlobalOptionSetExists {
    param([string]$Token, [string]$BaseUrl, [string]$Name)
    return $null -ne (Get-GlobalOptionSet -Token $Token -BaseUrl $BaseUrl -Name $Name)
}

function New-GlobalOptionSetIfMissing {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$Name,
        [string]$DisplayName,
        [array]$Options
    )
    if (Test-GlobalOptionSetExists -Token $Token -BaseUrl $BaseUrl -Name $Name) {
        Write-Host "  [SKIP]   global option set $Name (already exists)" -ForegroundColor Yellow
        return
    }
    $optionSetDef = @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.OptionSetMetadata"
        "Name"          = $Name
        "DisplayName"   = New-Label -Text $DisplayName
        "IsGlobal"      = $true
        "OptionSetType" = "Picklist"
        "Options"       = $Options
    }
    Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
        -Endpoint "GlobalOptionSetDefinitions" -Method "POST" -Body $optionSetDef | Out-Null
    Write-Host "  [CREATE] global option set $Name" -ForegroundColor Green
}

# ============================================================================
# CONFIGURATION
# ============================================================================

$EntitySchemaName = "sprk_NavItem"
$EntityLogicalName = "sprk_navitem"
$PrimaryNameSchemaName = "sprk_DisplayName"
$PrimaryNameLogicalName = "sprk_displayname"

# ============================================================================
# STEP 1 (LOAD-BEARING — MUST run before any picklist attribute is created):
#   Create the three GLOBAL option sets.
# ============================================================================

Write-Header "Deploy sprk_navitem entity to $EnvironmentUrl (solution: $SolutionUniqueName)"

Write-Host "Authenticating to Dataverse..." -ForegroundColor Cyan
$Token = Get-DataverseToken -Url $EnvironmentUrl
Write-Host "Token acquired." -ForegroundColor Green

Write-Header "Step 1: Create global option sets (sprk_type, sprk_source, sprk_pagetype)"

# sprk_type: history / pin
New-GlobalOptionSetIfMissing -Token $Token -BaseUrl $EnvironmentUrl `
    -Name "sprk_type" -DisplayName "Type" -Options @(
        @{ "Value" = 100000000; "Label" = New-OptionLabel -Text "History" },
        @{ "Value" = 100000001; "Label" = New-OptionLabel -Text "Pin" }
    )

# sprk_source: captured / manual
New-GlobalOptionSetIfMissing -Token $Token -BaseUrl $EnvironmentUrl `
    -Name "sprk_source" -DisplayName "Source" -Options @(
        @{ "Value" = 100000000; "Label" = New-OptionLabel -Text "Captured" },
        @{ "Value" = 100000001; "Label" = New-OptionLabel -Text "Manual" }
    )

# sprk_pagetype: entityrecord / entitylist / custom / weblink
New-GlobalOptionSetIfMissing -Token $Token -BaseUrl $EnvironmentUrl `
    -Name "sprk_pagetype" -DisplayName "Page Type" -Options @(
        @{ "Value" = 100000000; "Label" = New-OptionLabel -Text "Entity Record" },
        @{ "Value" = 100000001; "Label" = New-OptionLabel -Text "Entity List" },
        @{ "Value" = 100000002; "Label" = New-OptionLabel -Text "Custom" },
        @{ "Value" = 100000003; "Label" = New-OptionLabel -Text "Weblink" }
    )

# Resolve MetadataIds now — needed to bind the picklist attributes in Step 3.
$TypeOptionSet     = Get-GlobalOptionSet -Token $Token -BaseUrl $EnvironmentUrl -Name "sprk_type"
$SourceOptionSet   = Get-GlobalOptionSet -Token $Token -BaseUrl $EnvironmentUrl -Name "sprk_source"
$PageTypeOptionSet = Get-GlobalOptionSet -Token $Token -BaseUrl $EnvironmentUrl -Name "sprk_pagetype"

if (-not $TypeOptionSet -or -not $SourceOptionSet -or -not $PageTypeOptionSet) {
    throw "Failed to retrieve one or more global option sets (sprk_type / sprk_source / sprk_pagetype) after creation."
}

$TypeOptionSetId     = $TypeOptionSet.MetadataId
$SourceOptionSetId   = $SourceOptionSet.MetadataId
$PageTypeOptionSetId = $PageTypeOptionSet.MetadataId
Write-Host "  sprk_type MetadataId:     $TypeOptionSetId" -ForegroundColor Gray
Write-Host "  sprk_source MetadataId:   $SourceOptionSetId" -ForegroundColor Gray
Write-Host "  sprk_pagetype MetadataId: $PageTypeOptionSetId" -ForegroundColor Gray

# ============================================================================
# STEP 2: Create the UserOwned sprk_navitem entity (with primary name) if not exists
# ============================================================================

Write-Header "Step 2: Create sprk_navitem entity (UserOwned, with primary name)"

$entityExists = Test-EntityExists -Token $Token -BaseUrl $EnvironmentUrl -LogicalName $EntityLogicalName
if ($entityExists) {
    Write-Host "  Entity $EntityLogicalName already exists. Skipping creation." -ForegroundColor Yellow
} else {
    Write-Host "  Creating entity $EntityLogicalName..." -ForegroundColor Cyan

    $entityDef = @{
        "@odata.type"           = "Microsoft.Dynamics.CRM.EntityMetadata"
        "SchemaName"            = $EntitySchemaName
        "DisplayName"           = New-Label -Text "Nav Item"
        "DisplayCollectionName" = New-Label -Text "Nav Items"
        "Description"           = New-Label -Text "A per-user Navigator entry: a captured history row (page/record view), a manual pin, or a bookmark. Owner-scoped for per-user isolation (NFR-03)."
        "OwnershipType"         = "UserOwned"
        "IsActivity"            = $false
        "HasNotes"              = $false
        "HasActivities"         = $false
        "PrimaryNameAttribute"  = $PrimaryNameLogicalName
        "Attributes"            = @(
            @{
                "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
                "SchemaName"    = $PrimaryNameSchemaName
                "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
                "MaxLength"     = 200
                "FormatName"    = @{ "Value" = "Text" }
                "DisplayName"   = New-Label -Text "Display Name"
                "Description"   = New-Label -Text "Resolved or user-supplied label (primary name)"
                "IsPrimaryName" = $true
            }
        )
    }

    Invoke-DataverseApi -Token $Token -BaseUrl $EnvironmentUrl `
        -Endpoint "EntityDefinitions" -Method "POST" -Body $entityDef
    Write-Host "  Entity created: $EntityLogicalName" -ForegroundColor Green
}

# ============================================================================
# STEP 3: Add remaining fields (picklists bound to the global option sets from
#          Step 1, plus the text/datetime/integer data fields).
# ============================================================================

Write-Header "Step 3: Add remaining sprk_navitem fields"

function Add-AttributeIfMissing {
    param([string]$LogicalName, [hashtable]$Definition)
    if (Test-AttributeExists -Token $Token -BaseUrl $EnvironmentUrl `
            -EntityLogicalName $EntityLogicalName -AttributeLogicalName $LogicalName) {
        Write-Host "  [SKIP]   $LogicalName (already exists)" -ForegroundColor Yellow
        return
    }
    Invoke-DataverseApi -Token $Token -BaseUrl $EnvironmentUrl `
        -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes" `
        -Method "POST" -Body $Definition | Out-Null
    Write-Host "  [CREATE] $LogicalName" -ForegroundColor Green
}

# --- sprk_type (Picklist bound to the global sprk_type option set) ---
Add-AttributeIfMissing -LogicalName "sprk_type" -Definition @{
    "@odata.type"                = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
    "SchemaName"                 = "sprk_Type"
    "RequiredLevel"              = @{ "Value" = "None" }
    "DisplayName"                = New-Label -Text "Type"
    "Description"                = New-Label -Text "history / pin — distinguishes a captured history row from a user-created pin."
    "GlobalOptionSet@odata.bind" = "/GlobalOptionSetDefinitions($TypeOptionSetId)"
}

# --- sprk_source (Picklist bound to the global sprk_source option set) ---
Add-AttributeIfMissing -LogicalName "sprk_source" -Definition @{
    "@odata.type"                = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
    "SchemaName"                 = "sprk_Source"
    "RequiredLevel"              = @{ "Value" = "None" }
    "DisplayName"                = New-Label -Text "Source"
    "Description"                = New-Label -Text "captured / manual — how the row was created."
    "GlobalOptionSet@odata.bind" = "/GlobalOptionSetDefinitions($SourceOptionSetId)"
}

# --- sprk_pagetype (Picklist bound to the global sprk_pagetype option set) ---
Add-AttributeIfMissing -LogicalName "sprk_pagetype" -Definition @{
    "@odata.type"                = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
    "SchemaName"                 = "sprk_PageType"
    "RequiredLevel"              = @{ "Value" = "None" }
    "DisplayName"                = New-Label -Text "Page Type"
    "Description"                = New-Label -Text "entityrecord / entitylist / custom / weblink — the kind of page/target this row represents."
    "GlobalOptionSet@odata.bind" = "/GlobalOptionSetDefinitions($PageTypeOptionSetId)"
}

# --- sprk_targetlogicalname (Text) ---
Add-AttributeIfMissing -LogicalName "sprk_targetlogicalname" -Definition @{
    "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
    "SchemaName"    = "sprk_TargetLogicalName"
    "RequiredLevel" = @{ "Value" = "None" }
    "MaxLength"     = 100
    "FormatName"    = @{ "Value" = "Text" }
    "DisplayName"   = New-Label -Text "Target Logical Name"
    "Description"   = New-Label -Text "e.g. sprk_matter, custompage. Nullable for raw-URL links (sprk_pagetype = weblink) that have no Dataverse entity target."
}

# --- sprk_targetid (Text — normalized GUID; NOT Uniqueidentifier; see entity-schema.md "Type Decision") ---
Add-AttributeIfMissing -LogicalName "sprk_targetid" -Definition @{
    "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
    "SchemaName"    = "sprk_TargetId"
    "RequiredLevel" = @{ "Value" = "None" }
    "MaxLength"     = 100
    "FormatName"    = @{ "Value" = "Text" }
    "DisplayName"   = New-Label -Text "Target Id"
    "Description"   = New-Label -Text "Normalized GUID (as text) of the target record when sprk_pagetype = entityrecord. Nullable for pages/lists/weblinks. Text, not Uniqueidentifier, because the target entity is polymorphic (varies per row via sprk_targetlogicalname) — same pattern as sprk_todo.sprk_regardingrecordid."
}

# --- sprk_url (Text, URL format) ---
Add-AttributeIfMissing -LogicalName "sprk_url" -Definition @{
    "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
    "SchemaName"    = "sprk_Url"
    "RequiredLevel" = @{ "Value" = "None" }
    "MaxLength"     = 500
    "FormatName"    = @{ "Value" = "Url" }
    "DisplayName"   = New-Label -Text "URL"
    "Description"   = New-Label -Text "Raw URL for manual bookmarks that don't parse to a host entity/page shape (weblink bookmarks); also usable as a fallback deep link."
}

# --- sprk_lastvisited (DateTime) ---
Add-AttributeIfMissing -LogicalName "sprk_lastvisited" -Definition @{
    "@odata.type"      = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
    "SchemaName"       = "sprk_LastVisited"
    "RequiredLevel"    = @{ "Value" = "None" }
    "Format"           = "DateAndTime"
    "DateTimeBehavior" = @{ "Value" = "UserLocal" }
    "DisplayName"      = New-Label -Text "Last Visited"
    "Description"      = New-Label -Text "Timestamp of most recent visit/creation; drives ordering and the 30-day prune-on-write retention policy."
}

# --- sprk_visitcount (Whole Number / Integer) ---
Add-AttributeIfMissing -LogicalName "sprk_visitcount" -Definition @{
    "@odata.type"   = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata"
    "SchemaName"    = "sprk_VisitCount"
    "RequiredLevel" = @{ "Value" = "None" }
    "DisplayName"   = New-Label -Text "Visit Count"
    "Description"   = New-Label -Text "Optional dedupe/rank counter; incremented on repeat capture of the same target instead of creating a duplicate history row."
    "MinValue"      = 0
    "MaxValue"      = 2147483647
}

# ============================================================================
# STEP 4: Publish customizations
# ============================================================================

Write-Header "Step 4: Publish customizations"

$publishXml = @{
    "ParameterXml" = "<importexportxml><entities><entity>$EntityLogicalName</entity></entities></importexportxml>"
}
Invoke-DataverseApi -Token $Token -BaseUrl $EnvironmentUrl `
    -Endpoint "PublishXml" -Method "POST" -Body $publishXml | Out-Null
Write-Host "  Customizations published for $EntityLogicalName" -ForegroundColor Green

# ============================================================================
# STEP 5: Verify
# ============================================================================

if (-not $SkipVerification) {
    Write-Header "Step 5: Verify"

    $attrResp = Invoke-DataverseApi -Token $Token -BaseUrl $EnvironmentUrl `
        -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes?`$select=LogicalName,AttributeType"
    $custom = @($attrResp.value | Where-Object { $_.LogicalName -like "sprk_*" } | Sort-Object LogicalName)
    Write-Host "  Custom sprk_* attributes on ${EntityLogicalName}: $($custom.Count)" -ForegroundColor Green
    foreach ($a in $custom) {
        Write-Host "    - $($a.LogicalName) [$($a.AttributeType)]" -ForegroundColor Gray
    }

    $entityMeta = Invoke-DataverseApi -Token $Token -BaseUrl $EnvironmentUrl `
        -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')?`$select=OwnershipType,PrimaryNameAttribute"
    Write-Host ""
    Write-Host "  OwnershipType: $($entityMeta.OwnershipType) (expected: UserOwned)" -ForegroundColor Gray
    Write-Host "  PrimaryNameAttribute: $($entityMeta.PrimaryNameAttribute) (expected: sprk_displayname)" -ForegroundColor Gray
}

$elapsed = (Get-Date) - $StartTime
Write-Host ""
Write-Host "Done. Elapsed: $($elapsed.ToString('mm\:ss'))" -ForegroundColor Cyan
