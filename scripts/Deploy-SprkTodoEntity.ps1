<#
.SYNOPSIS
    Deploy the sprk_todo custom entity with the full attribute set per smart-todo-decoupling-r3
    spec FR-01 / design §4.1.

.DESCRIPTION
    Creates sprk_todo (User/Team owned, NOT activity, no system Notes), all primary fields,
    kanban behavior fields, the 11 specific regarding lookups, the 4 resolver fields, and the
    5 Graph sync state fields. Mirrors the sprk_communication 11-lookup + 4-resolver shape so
    the existing PolymorphicResolverService works without modification (ADR-024).

    Idempotent: skips entity creation if sprk_todo already exists; skips attribute creation
    if each attribute already exists.

    PORTABILITY: The script accepts the target Dataverse URL via parameter or DATAVERSE_URL
    env var. No tenant-specific values are hardcoded. The resulting schema is exportable as
    an unmanaged solution and importable into any tenant where the 11 target entities exist.

    DEPLOYMENT APPROACH:
    ---------------------
    Web API (Microsoft.Dynamics.CRM Metadata) — PAC CLI has no `pac table create` /
    `pac column create`. Same approach as Deploy-ChartDefinitionEntity.ps1.

    PREREQUISITES:
    --------------
    - Azure CLI logged in:  az login
    - Target env URL passed via -EnvironmentUrl or DATAVERSE_URL env var
    - Target entities present in env (verified at runtime):
        sprk_matter, sprk_project, sprk_event, sprk_communication, sprk_workassignment,
        sprk_invoice, sprk_budget, sprk_analysis, sprk_organization, sprk_document,
        contact (OOB), sprk_recordtype_ref

.PARAMETER EnvironmentUrl
    Target Dataverse environment URL (e.g., https://spaarkedev1.crm.dynamics.com).
    Defaults to $env:DATAVERSE_URL.

.EXAMPLE
    .\Deploy-SprkTodoEntity.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com"

.EXAMPLE
    $env:DATAVERSE_URL = "https://spaarkedev1.crm.dynamics.com"
    .\Deploy-SprkTodoEntity.ps1

.NOTES
    Project:  smart-todo-decoupling-r3
    Task:     002 - Create sprk_todo entity with full attribute set
    Schema:   src/solutions/SpaarkeCore/entities/sprk_todo/entity-schema.md
    Created:  2026-06-07
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$EnvironmentUrl = $env:DATAVERSE_URL,

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

# ============================================================================
# CONFIGURATION
# ============================================================================

$EntitySchemaName = "sprk_todo"
$EntityLogicalName = "sprk_todo"
$PrimaryNameSchemaName = "sprk_Name"
$PrimaryNameLogicalName = "sprk_name"

# 11 specific regarding lookups: { LookupSchemaName, DisplayName, TargetEntity }
$RegardingLookups = @(
    @{ Schema = "sprk_RegardingMatter";          Display = "Regarding Matter";          Target = "sprk_matter" },
    @{ Schema = "sprk_RegardingProject";         Display = "Regarding Project";         Target = "sprk_project" },
    @{ Schema = "sprk_RegardingEvent";           Display = "Regarding Event";           Target = "sprk_event" },
    @{ Schema = "sprk_RegardingCommunication";   Display = "Regarding Communication";   Target = "sprk_communication" },
    @{ Schema = "sprk_RegardingWorkAssignment";  Display = "Regarding Work Assignment"; Target = "sprk_workassignment" },
    @{ Schema = "sprk_RegardingInvoice";         Display = "Regarding Invoice";         Target = "sprk_invoice" },
    @{ Schema = "sprk_RegardingBudget";          Display = "Regarding Budget";          Target = "sprk_budget" },
    @{ Schema = "sprk_RegardingAnalysis";        Display = "Regarding Analysis";        Target = "sprk_analysis" },
    @{ Schema = "sprk_RegardingOrganization";    Display = "Regarding Organization";    Target = "sprk_organization" },
    @{ Schema = "sprk_RegardingContact";         Display = "Regarding Contact";         Target = "contact" },
    @{ Schema = "sprk_RegardingDocument";        Display = "Regarding Document";        Target = "sprk_document" }
)

# ============================================================================
# PRE-CHECK: Target entities must exist
# ============================================================================

Write-Header "Deploy sprk_todo entity to $EnvironmentUrl"

Write-Host "Authenticating to Dataverse..." -ForegroundColor Cyan
$Token = Get-DataverseToken -Url $EnvironmentUrl
Write-Host "Token acquired." -ForegroundColor Green

Write-Header "Step 1: Verify target entities exist"

$RequiredTargets = @($RegardingLookups | ForEach-Object { $_.Target }) + @("sprk_recordtype_ref")
$missingTargets = @()
foreach ($t in $RequiredTargets) {
    if (Test-EntityExists -Token $Token -BaseUrl $EnvironmentUrl -LogicalName $t) {
        Write-Host "  [OK]      $t" -ForegroundColor Green
    } else {
        Write-Host "  [MISSING] $t" -ForegroundColor Red
        $missingTargets += $t
    }
}
if ($missingTargets.Count -gt 0) {
    throw "Missing target entities in $($EnvironmentUrl): $($missingTargets -join ', '). Cannot proceed; create those entities first."
}

# ============================================================================
# Step 2: Create entity (with primary name) if not exists
# ============================================================================

Write-Header "Step 2: Create sprk_todo entity (with primary name)"

$entityExists = Test-EntityExists -Token $Token -BaseUrl $EnvironmentUrl -LogicalName $EntityLogicalName
if ($entityExists) {
    Write-Host "  Entity $EntityLogicalName already exists. Skipping creation." -ForegroundColor Yellow
} else {
    Write-Host "  Creating entity $EntityLogicalName..." -ForegroundColor Cyan

    $entityDef = @{
        "@odata.type"             = "Microsoft.Dynamics.CRM.EntityMetadata"
        "SchemaName"              = "sprk_Todo"
        "DisplayName"             = New-Label -Text "To Do"
        "DisplayCollectionName"   = New-Label -Text "To Dos"
        "Description"             = New-Label -Text "A To Do (task) in Spaarke. May stand alone or be associated with one parent record via the Spaarke multi-entity resolution pattern (ADR-024). Optional bidirectional mirror to Microsoft Graph /me/todo."
        "OwnershipType"           = "UserOwned"
        "IsActivity"              = $false
        "HasNotes"                = $false
        "HasActivities"           = $false
        "PrimaryNameAttribute"    = $PrimaryNameLogicalName
        "Attributes"              = @(
            @{
                "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
                "SchemaName"    = $PrimaryNameSchemaName
                "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
                "MaxLength"     = 200
                "FormatName"    = @{ "Value" = "Text" }
                "DisplayName"   = New-Label -Text "Name"
                "Description"   = New-Label -Text "Primary name (kanban card title)"
                "IsPrimaryName" = $true
            }
        )
    }

    Invoke-DataverseApi -Token $Token -BaseUrl $EnvironmentUrl `
        -Endpoint "EntityDefinitions" -Method "POST" -Body $entityDef
    Write-Host "  Entity created: $EntityLogicalName" -ForegroundColor Green
}

# ============================================================================
# Step 3: Add non-lookup attributes
# ============================================================================

Write-Header "Step 3: Add core / kanban / sync attributes"

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

# --- Detail / description fields ---
Add-AttributeIfMissing -LogicalName "sprk_description" -Definition @{
    "@odata.type"   = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
    "SchemaName"    = "sprk_Description"
    "RequiredLevel" = @{ "Value" = "None" }
    "MaxLength"     = 4000
    "Format"        = "TextArea"
    "DisplayName"   = New-Label -Text "Description"
    "Description"   = New-Label -Text "Plain description (not the rich-text Notes)"
}

Add-AttributeIfMissing -LogicalName "sprk_notes" -Definition @{
    "@odata.type"   = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
    "SchemaName"    = "sprk_Notes"
    "RequiredLevel" = @{ "Value" = "None" }
    "MaxLength"     = 100000
    "Format"        = "TextArea"
    "DisplayName"   = New-Label -Text "Notes"
    "Description"   = New-Label -Text "Rich-text notes (replaces former sprk_eventtodo.sprk_todonotes)"
}

# --- Kanban behavior fields ---

# sprk_todocolumn - local Choice (Today/Tomorrow/Future)
Add-AttributeIfMissing -LogicalName "sprk_todocolumn" -Definition @{
    "@odata.type"   = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
    "SchemaName"    = "sprk_TodoColumn"
    "RequiredLevel" = @{ "Value" = "None" }
    "DisplayName"   = New-Label -Text "To Do Column"
    "Description"   = New-Label -Text "Kanban column position: Today / Tomorrow / Future"
    "OptionSet"     = @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.OptionSetMetadata"
        "OptionSetType" = "Picklist"
        "IsGlobal"      = $false
        "Options"       = @(
            @{ "Value" = 100000000; "Label" = New-Label -Text "Today" },
            @{ "Value" = 100000001; "Label" = New-Label -Text "Tomorrow" },
            @{ "Value" = 100000002; "Label" = New-Label -Text "Future" }
        )
    }
}

Add-AttributeIfMissing -LogicalName "sprk_todopinned" -Definition @{
    "@odata.type"   = "Microsoft.Dynamics.CRM.BooleanAttributeMetadata"
    "SchemaName"    = "sprk_TodoPinned"
    "RequiredLevel" = @{ "Value" = "None" }
    "DisplayName"   = New-Label -Text "Pinned"
    "Description"   = New-Label -Text "Locks the column assignment against auto-reassign"
    "DefaultValue"  = $false
    "OptionSet"     = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.BooleanOptionSetMetadata"
        "TrueOption"  = @{ "Value" = 1; "Label" = New-Label -Text "Yes" }
        "FalseOption" = @{ "Value" = 0; "Label" = New-Label -Text "No" }
    }
}

Add-AttributeIfMissing -LogicalName "sprk_priorityscore" -Definition @{
    "@odata.type"   = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata"
    "SchemaName"    = "sprk_PriorityScore"
    "RequiredLevel" = @{ "Value" = "None" }
    "DisplayName"   = New-Label -Text "Priority Score"
    "Description"   = New-Label -Text "Priority score 0-100. Independent from sprk_event.sprk_priorityscore."
    "MinValue"      = 0
    "MaxValue"      = 100
}

Add-AttributeIfMissing -LogicalName "sprk_effortscore" -Definition @{
    "@odata.type"   = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata"
    "SchemaName"    = "sprk_EffortScore"
    "RequiredLevel" = @{ "Value" = "None" }
    "DisplayName"   = New-Label -Text "Effort Score"
    "Description"   = New-Label -Text "Effort score 0-100. Independent from sprk_event.sprk_effortscore."
    "MinValue"      = 0
    "MaxValue"      = 100
}

Add-AttributeIfMissing -LogicalName "sprk_duedate" -Definition @{
    "@odata.type"      = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
    "SchemaName"       = "sprk_DueDate"
    "RequiredLevel"    = @{ "Value" = "None" }
    "Format"           = "DateOnly"
    "DateTimeBehavior" = @{ "Value" = "UserLocal" }
    "DisplayName"      = New-Label -Text "Due Date"
    "Description"      = New-Label -Text "Due date"
}

Add-AttributeIfMissing -LogicalName "sprk_completedon" -Definition @{
    "@odata.type"      = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
    "SchemaName"       = "sprk_CompletedOn"
    "RequiredLevel"    = @{ "Value" = "None" }
    "Format"           = "DateAndTime"
    "DateTimeBehavior" = @{ "Value" = "UserLocal" }
    "DisplayName"      = New-Label -Text "Completed On"
    "Description"      = New-Label -Text "Set on transition to Completed"
}

# --- Resolver fields (denormalized) ---
Add-AttributeIfMissing -LogicalName "sprk_regardingrecordid" -Definition @{
    "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
    "SchemaName"    = "sprk_RegardingRecordId"
    "RequiredLevel" = @{ "Value" = "None" }
    "MaxLength"     = 100
    "FormatName"    = @{ "Value" = "Text" }
    "DisplayName"   = New-Label -Text "Regarding Record Id"
    "Description"   = New-Label -Text "Resolver: normalized GUID of the regarding record. Populated by PolymorphicResolverService (ADR-024)."
}

Add-AttributeIfMissing -LogicalName "sprk_regardingrecordname" -Definition @{
    "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
    "SchemaName"    = "sprk_RegardingRecordName"
    "RequiredLevel" = @{ "Value" = "None" }
    "MaxLength"     = 200
    "FormatName"    = @{ "Value" = "Text" }
    "DisplayName"   = New-Label -Text "Regarding Record Name"
    "Description"   = New-Label -Text "Resolver: display name of regarding record."
}

Add-AttributeIfMissing -LogicalName "sprk_regardingrecordurl" -Definition @{
    "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
    "SchemaName"    = "sprk_RegardingRecordURL"
    "RequiredLevel" = @{ "Value" = "None" }
    "MaxLength"     = 500
    "FormatName"    = @{ "Value" = "Url" }
    "DisplayName"   = New-Label -Text "Regarding Record URL"
    "Description"   = New-Label -Text "Resolver: clickable link to regarding record."
}

# --- Graph sync state fields ---
Add-AttributeIfMissing -LogicalName "sprk_graphtodolistid" -Definition @{
    "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
    "SchemaName"    = "sprk_GraphTodoListId"
    "RequiredLevel" = @{ "Value" = "None" }
    "MaxLength"     = 100
    "FormatName"    = @{ "Value" = "Text" }
    "DisplayName"   = New-Label -Text "Graph To Do List Id"
    "Description"   = New-Label -Text "/me/todo/lists/{id} the todo mirrors into"
}

Add-AttributeIfMissing -LogicalName "sprk_graphtodotaskid" -Definition @{
    "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
    "SchemaName"    = "sprk_GraphTodoTaskId"
    "RequiredLevel" = @{ "Value" = "None" }
    "MaxLength"     = 100
    "FormatName"    = @{ "Value" = "Text" }
    "DisplayName"   = New-Label -Text "Graph To Do Task Id"
    "Description"   = New-Label -Text "Mirrored todoTask id from Microsoft Graph"
}

Add-AttributeIfMissing -LogicalName "sprk_lastsyncedutc" -Definition @{
    "@odata.type"      = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
    "SchemaName"       = "sprk_LastSyncedUtc"
    "RequiredLevel"    = @{ "Value" = "None" }
    "Format"           = "DateAndTime"
    "DateTimeBehavior" = @{ "Value" = "TimeZoneIndependent" }
    "DisplayName"      = New-Label -Text "Last Synced UTC"
    "Description"      = New-Label -Text "Last successful sync time (UTC)"
}

Add-AttributeIfMissing -LogicalName "sprk_synchash" -Definition @{
    "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
    "SchemaName"    = "sprk_SyncHash"
    "RequiredLevel" = @{ "Value" = "None" }
    "MaxLength"     = 64
    "FormatName"    = @{ "Value" = "Text" }
    "DisplayName"   = New-Label -Text "Sync Hash"
    "Description"   = New-Label -Text "SHA-256 content hash (truncated) for loop detection"
}

Add-AttributeIfMissing -LogicalName "sprk_syncerror" -Definition @{
    "@odata.type"   = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
    "SchemaName"    = "sprk_SyncError"
    "RequiredLevel" = @{ "Value" = "None" }
    "MaxLength"     = 2000
    "Format"        = "TextArea"
    "DisplayName"   = New-Label -Text "Sync Error"
    "Description"   = New-Label -Text "Last sync error message (if any)"
}

# ============================================================================
# Step 4: Lookup fields (must be created via RelationshipDefinitions)
# ============================================================================

Write-Header "Step 4: Create lookup fields (1:N relationships)"

function New-OneToManyLookup {
    param(
        [string]$ReferencedEntity,
        [string]$LookupSchemaName,
        [string]$LookupDisplayName
    )
    # Derive logical name from schema name (lowercase)
    $lookupLogicalName = $LookupSchemaName.ToLowerInvariant()
    if (Test-AttributeExists -Token $Token -BaseUrl $EnvironmentUrl `
            -EntityLogicalName $EntityLogicalName -AttributeLogicalName $lookupLogicalName) {
        Write-Host "  [SKIP]   $lookupLogicalName -> $ReferencedEntity (already exists)" -ForegroundColor Yellow
        return
    }
    # Relationship schema name: sprk_{Referenced}_{Referencing}_{LookupShort}
    $lookupShort = $LookupSchemaName -replace '^sprk_', ''
    $relationshipSchemaName = "sprk_${ReferencedEntity}_${EntityLogicalName}_${lookupShort}"
    # Truncate to 100 chars if needed (Dataverse limit)
    if ($relationshipSchemaName.Length -gt 100) {
        $relationshipSchemaName = $relationshipSchemaName.Substring(0, 100)
    }

    $relDef = @{
        "@odata.type"          = "Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata"
        "SchemaName"           = $relationshipSchemaName
        "ReferencedEntity"     = $ReferencedEntity
        "ReferencingEntity"    = $EntityLogicalName
        "CascadeConfiguration" = @{
            "Assign"   = "NoCascade"
            "Delete"   = "RemoveLink"
            "Merge"    = "NoCascade"
            "Reparent" = "NoCascade"
            "Share"    = "NoCascade"
            "Unshare"  = "NoCascade"
        }
        "Lookup"               = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.LookupAttributeMetadata"
            "SchemaName"    = $LookupSchemaName
            "DisplayName"   = New-Label -Text $LookupDisplayName
            "Description"   = New-Label -Text "$LookupDisplayName lookup (specific regarding per ADR-024)"
            "RequiredLevel" = @{ "Value" = "None" }
        }
    }

    Invoke-DataverseApi -Token $Token -BaseUrl $EnvironmentUrl `
        -Endpoint "RelationshipDefinitions" -Method "POST" -Body $relDef | Out-Null
    Write-Host "  [CREATE] $lookupLogicalName -> $ReferencedEntity" -ForegroundColor Green
}

# sprk_assignedto -> systemuser (User-only)
New-OneToManyLookup -ReferencedEntity "systemuser" `
    -LookupSchemaName "sprk_AssignedTo" `
    -LookupDisplayName "Assigned To"

# 11 specific regarding lookups
foreach ($lk in $RegardingLookups) {
    New-OneToManyLookup -ReferencedEntity $lk.Target `
        -LookupSchemaName $lk.Schema `
        -LookupDisplayName $lk.Display
}

# Resolver lookup: sprk_regardingrecordtype -> sprk_recordtype_ref
New-OneToManyLookup -ReferencedEntity "sprk_recordtype_ref" `
    -LookupSchemaName "sprk_RegardingRecordType" `
    -LookupDisplayName "Regarding Record Type"

# ============================================================================
# Step 5: Publish customizations
# ============================================================================

Write-Header "Step 5: Publish customizations"

$publishXml = @{
    "ParameterXml" = "<importexportxml><entities><entity>$EntityLogicalName</entity></entities></importexportxml>"
}
Invoke-DataverseApi -Token $Token -BaseUrl $EnvironmentUrl `
    -Endpoint "PublishXml" -Method "POST" -Body $publishXml | Out-Null
Write-Host "  Customizations published for $EntityLogicalName" -ForegroundColor Green

# ============================================================================
# Step 6: Verify
# ============================================================================

if (-not $SkipVerification) {
    Write-Header "Step 6: Verify"

    # Re-fetch attributes
    $attrResp = Invoke-DataverseApi -Token $Token -BaseUrl $EnvironmentUrl `
        -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes?`$select=LogicalName,AttributeType"
    $custom = @($attrResp.value | Where-Object { $_.LogicalName -like "sprk_*" } | Sort-Object LogicalName)
    Write-Host "  Custom sprk_* attributes on ${EntityLogicalName}: $($custom.Count)" -ForegroundColor Green
    foreach ($a in $custom) {
        Write-Host "    - $($a.LogicalName) [$($a.AttributeType)]" -ForegroundColor Gray
    }

    # Verify lookup targets via Relationships
    $relResp = Invoke-DataverseApi -Token $Token -BaseUrl $EnvironmentUrl `
        -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/ManyToOneRelationships?`$select=SchemaName,ReferencedEntity,ReferencingAttribute"
    Write-Host ""
    Write-Host "  Lookups (Many-to-One relationships):" -ForegroundColor Cyan
    foreach ($r in ($relResp.value | Sort-Object ReferencingAttribute)) {
        Write-Host "    - $($r.ReferencingAttribute) -> $($r.ReferencedEntity)" -ForegroundColor Gray
    }
}

$elapsed = (Get-Date) - $StartTime
Write-Host ""
Write-Host "Done. Elapsed: $($elapsed.ToString('mm\:ss'))" -ForegroundColor Cyan
