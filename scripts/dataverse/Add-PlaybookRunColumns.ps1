<#
.SYNOPSIS
    Adds the durable-run-status gap columns to sprk_playbookrun and sprk_playbooknoderun, and widens both tables to be
    the one AI-run record (engine, prompted-Action and coded-workflow runs).

.DESCRIPTION
    spaarkeai-mcp-bff-api-r2 task 005 (FR-13) and the owner's run-record decision (D-run-record-decided, 2026-11-25).
    PlaybookRunRecordStore (the one writer for every run kind) writes these tables on the run's own execution path; the
    columns below are the ones the existing tables lack (projects/spaarkeai-mcp-bff-api-r2/notes/005-schema.md).

      sprk_playbookrun
        sprk_correlationid        String(100)   correlation / idempotency id (no alternate key)
        sprk_errorcode            String(100)   stable failure code
        sprk_regardingentitytype  String(100)   regarding record: entity logical name
        sprk_regardingrecordid    String(100)   regarding record: id (GUID "D" format)
        sprk_summary              Memo(4000)    short, content-free run summary
        sprk_runkind              Choice        Engine 100000000 / Prompted 100000001 / Coded 100000002 (default Engine)
        sprk_analysisactionid     Lookup        -> sprk_analysisaction    (the Action a prompted or coded run executed)
        sprk_playbookconsumerid   Lookup        -> sprk_playbookconsumer  (Binding: the routing row the run came through)
        sprk_parentrunid          Lookup        -> sprk_playbookrun       (parent run of a nested run)
        sprk_playbookid           (existing)    lookup made OPTIONAL: RequiredLevel ApplicationRequired -> None
      sprk_playbooknoderun
        sprk_startedon            DateTime
        sprk_completedon          DateTime
        sprk_sequence             Integer >= 0  start order within the run
        sprk_confidence           Decimal(10)   the node's numeric confidence score, range +-1000 (not content)
        sprk_analysisactionid     Lookup        -> sprk_analysisaction    (the Action the row stands for)
        sprk_playbooknodeid       (existing)    lookup made OPTIONAL: RequiredLevel ApplicationRequired -> None

    Deliberately NOT added here: a profile-version lookup and a gate-decision reference (specialist-legal-agents-r1
    Phase 1 owns them).

    Additive and idempotent: an existing column is checked for the expected type and skipped; a column that exists
    with a different type stops the script. The two required-level changes are metadata updates of existing lookups and
    are skipped when the lookup is already optional. Nothing is deleted. Publishes the two tables at the end.
    Owner-gated: run against an environment only with the owner's explicit go (project CLAUDE.md §5).

.PARAMETER EnvironmentUrl
    Dataverse environment URL, e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER SolutionName
    Unmanaged solution that receives the columns (default SpaarkeCore, which already contains sprk_playbookrun).

.PARAMETER WhatIf
    Print what would be created; make no changes.

.EXAMPLE
    pwsh scripts/dataverse/Add-PlaybookRunColumns.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -WhatIf
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$EnvironmentUrl,

    [string]$SolutionName = "SpaarkeCore",

    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$BaseUrl = "$EnvironmentUrl/api/data/v9.2"

function Get-DataverseToken {
    $token = az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv 2>&1
    if ($LASTEXITCODE -ne 0) { throw "az account get-access-token failed: $token (run 'az login')" }
    return $token.Trim()
}

function Invoke-Dv {
    param([string]$Method = "GET", [string]$Endpoint, [object]$Body = $null, [hashtable]$ExtraHeaders = @{})
    $headers = @{
        "Authorization"           = "Bearer $script:Token"
        "OData-MaxVersion"        = "4.0"
        "OData-Version"           = "4.0"
        "Accept"                  = "application/json"
        "Content-Type"            = "application/json; charset=utf-8"
        "MSCRM.SolutionUniqueName" = $SolutionName
    }
    foreach ($key in $ExtraHeaders.Keys) { $headers[$key] = $ExtraHeaders[$key] }
    $params = @{ Uri = "$BaseUrl/$Endpoint"; Method = $Method; Headers = $headers }
    if ($null -ne $Body) { $params.Body = ($Body | ConvertTo-Json -Depth 20) }
    return Invoke-RestMethod @params
}

function New-Label([string]$Text) {
    return @{
        "@odata.type"     = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels" = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; "Label" = $Text; "LanguageCode" = 1033 })
    }
}

# Returns the existing attribute's AttributeType, or $null when the column does not exist (404).
# Any other failure (auth, throttling, network) stops the script instead of being read as "missing".
function Get-AttributeType([string]$Entity, [string]$Attribute) {
    try {
        $a = Invoke-Dv -Endpoint "EntityDefinitions(LogicalName='$Entity')/Attributes(LogicalName='$Attribute')?`$select=AttributeType"
        return $a.AttributeType
    }
    catch {
        $status = $null
        if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
        if ($status -eq 404) { return $null }
        throw "Reading $Entity.$Attribute failed (HTTP $status): $($_.Exception.Message)"
    }
}

function New-StringColumn([string]$Name, [string]$Display, [string]$Description, [int]$MaxLength) {
    return @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName"    = $Name
        "RequiredLevel" = @{ "Value" = "None" }
        "MaxLength"     = $MaxLength
        "FormatName"    = @{ "Value" = "Text" }
        "DisplayName"   = New-Label $Display
        "Description"   = New-Label $Description
    }
}

function New-MemoColumn([string]$Name, [string]$Display, [string]$Description, [int]$MaxLength) {
    return @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
        "SchemaName"    = $Name
        "RequiredLevel" = @{ "Value" = "None" }
        "MaxLength"     = $MaxLength
        "Format"        = "TextArea"
        "DisplayName"   = New-Label $Display
        "Description"   = New-Label $Description
    }
}

function New-DateTimeColumn([string]$Name, [string]$Display, [string]$Description) {
    return @{
        "@odata.type"      = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
        "SchemaName"       = $Name
        "RequiredLevel"    = @{ "Value" = "None" }
        "Format"           = "DateAndTime"
        "DateTimeBehavior" = @{ "Value" = "UserLocal" }
        "DisplayName"      = New-Label $Display
        "Description"      = New-Label $Description
    }
}

function New-IntegerColumn([string]$Name, [string]$Display, [string]$Description) {
    return @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata"
        "SchemaName"    = $Name
        "RequiredLevel" = @{ "Value" = "None" }
        "Format"        = "None"
        "MinValue"      = 0
        "MaxValue"      = 2147483647
        "DisplayName"   = New-Label $Display
        "Description"   = New-Label $Description
    }
}

function New-DecimalColumn([string]$Name, [string]$Display, [string]$Description) {
    return @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.DecimalAttributeMetadata"
        "SchemaName"    = $Name
        "RequiredLevel" = @{ "Value" = "None" }
        "Precision"     = 10
        "MinValue"      = -1000
        "MaxValue"      = 1000
        "DisplayName"   = New-Label $Display
        "Description"   = New-Label $Description
    }
}

# Local choice column (the option set belongs to the column; Spaarke 100000000+ values); $DefaultValue is the form default.
function New-PicklistColumn([string]$Name, [string]$Display, [string]$Description, [hashtable[]]$Options, [int]$DefaultValue) {
    return @{
        "@odata.type"      = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
        "SchemaName"       = $Name
        "RequiredLevel"    = @{ "Value" = "None" }
        "DisplayName"      = New-Label $Display
        "Description"      = New-Label $Description
        "DefaultFormValue" = $DefaultValue
        "OptionSet"        = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.OptionSetMetadata"
            "OptionSetType" = "Picklist"
            "IsGlobal"      = $false
            "Options"       = @($Options | ForEach-Object { @{ "Value" = $_.Value; "Label" = New-Label $_.Label; "Description" = New-Label $_.Description } })
        }
    }
}

# A lookup cannot be created on the Attributes endpoint: it is created through its 1:N relationship (dataverse-create-schema).
# Relationship schema name: sprk_{referenced}_{referencing}_{lookup}, table names without the sprk_ prefix (the repo's
# convention, scripts/Create-AiChatContextMapEntity.ps1; the skill's formula would double the prefix).
# Delete = RemoveLink: a run record is an audit row and must outlive the Action, Binding or parent run it points at.
function New-LookupRelationship([string]$Referenced, [string]$Referencing, [string]$LookupName, [string]$Display, [string]$Description) {
    $short = $LookupName -replace '^sprk_', ''
    $relationship = "sprk_$($Referenced -replace '^sprk_', '')_$($Referencing -replace '^sprk_', '')_$short"
    return @{
        "@odata.type"          = "Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata"
        "SchemaName"           = $relationship
        "ReferencedEntity"     = $Referenced
        "ReferencingEntity"    = $Referencing
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
            "SchemaName"    = $LookupName
            "DisplayName"   = New-Label $Display
            "Description"   = New-Label $Description
            "RequiredLevel" = @{ "Value" = "None" }
        }
    }
}

# The full definition of an existing lookup attribute (read for the required-level update and its verification).
function Get-LookupAttribute([string]$Entity, [string]$Attribute) {
    return Invoke-Dv -Endpoint "EntityDefinitions(LogicalName='$Entity')/Attributes(LogicalName='$Attribute')/Microsoft.Dynamics.CRM.LookupAttributeMetadata"
}

# Expected AttributeType per definition @odata.type, for the idempotency type check.
$ExpectedType = @{
    "Microsoft.Dynamics.CRM.StringAttributeMetadata"   = "String"
    "Microsoft.Dynamics.CRM.MemoAttributeMetadata"     = "Memo"
    "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata" = "DateTime"
    "Microsoft.Dynamics.CRM.IntegerAttributeMetadata"  = "Integer"
    "Microsoft.Dynamics.CRM.DecimalAttributeMetadata"  = "Decimal"
    "Microsoft.Dynamics.CRM.PicklistAttributeMetadata" = "Picklist"
}

$Columns = @(
    @{ Entity = "sprk_playbookrun"; Def = New-StringColumn "sprk_correlationid" "Correlation Id" "Correlation / idempotency id of the request that started the run (FR-13)." 100 }
    @{ Entity = "sprk_playbookrun"; Def = New-StringColumn "sprk_errorcode" "Error Code" "Stable failure code of a failed run, e.g. playbook_node_failed (FR-13)." 100 }
    @{ Entity = "sprk_playbookrun"; Def = New-StringColumn "sprk_regardingentitytype" "Regarding Entity Type" "Logical name of the record the run is about, e.g. sprk_matter (FR-13)." 100 }
    @{ Entity = "sprk_playbookrun"; Def = New-StringColumn "sprk_regardingrecordid" "Regarding Record Id" "Id of the record the run is about (GUID) (FR-13)." 100 }
    @{ Entity = "sprk_playbookrun"; Def = New-MemoColumn "sprk_summary" "Summary" "Short run summary built from counts; never document content, prompts or model output (FR-13, ADR-015)." 4000 }
    @{ Entity = "sprk_playbookrun"; Def = New-PicklistColumn "sprk_runkind" "Run Kind" "How the run executed: Engine (playbook node engine), Prompted (one prompted Action) or Coded (a coded workflow). Engine rows are written by the playbook orchestrator." @(
            @{ Value = 100000000; Label = "Engine"; Description = "A playbook run on the node engine." }
            @{ Value = 100000001; Label = "Prompted"; Description = "A single prompted Action run." }
            @{ Value = 100000002; Label = "Coded"; Description = "A coded-workflow run." }
        ) 100000000 }
    @{ Entity = "sprk_playbooknoderun"; Def = New-DateTimeColumn "sprk_startedon" "Started On" "When the node started (FR-13)." }
    @{ Entity = "sprk_playbooknoderun"; Def = New-DateTimeColumn "sprk_completedon" "Completed On" "When the node finished (FR-13)." }
    @{ Entity = "sprk_playbooknoderun"; Def = New-IntegerColumn "sprk_sequence" "Sequence" "Start order of the node within its run, from 1 (FR-13)." }
    @{ Entity = "sprk_playbooknoderun"; Def = New-DecimalColumn "sprk_confidence" "Confidence" "The node's numeric confidence score, as the run detail returns it; a number, never content (FR-13)." }
)

# Lookups, created through their relationships. Idempotent: the lookup attribute already exists as a Lookup -> skipped.
$Lookups = @(
    @{ Entity = "sprk_playbookrun"; Target = "sprk_analysisaction"; Name = "sprk_analysisactionid"; Display = "Action"; Description = "The Action a prompted or coded run executed. Empty for an engine run." }
    @{ Entity = "sprk_playbookrun"; Target = "sprk_playbookconsumer"; Name = "sprk_playbookconsumerid"; Display = "Binding"; Description = "The Binding (sprk_playbookconsumer row) the run was routed through, when it had one." }
    @{ Entity = "sprk_playbookrun"; Target = "sprk_playbookrun"; Name = "sprk_parentrunid"; Display = "Parent Run"; Description = "The run that started this run (a nested run); empty for a top-level run." }
    @{ Entity = "sprk_playbooknoderun"; Target = "sprk_analysisaction"; Name = "sprk_analysisactionid"; Display = "Action"; Description = "The Action this row stands for when the run has no playbook node (a prompted or coded run)." }
)

# Existing lookups made optional: a prompted or coded run has no playbook and no playbook node. A metadata update of the
# attribute, skipped when the lookup is already RequiredLevel None.
$OptionalLookups = @(
    @{ Entity = "sprk_playbookrun"; Name = "sprk_playbookid" }
    @{ Entity = "sprk_playbooknoderun"; Name = "sprk_playbooknodeid" }
)

Write-Host "Environment: $EnvironmentUrl   Solution: $SolutionName   WhatIf: $WhatIf" -ForegroundColor Cyan
$script:Token = Get-DataverseToken

foreach ($entity in ($Columns | ForEach-Object { $_.Entity } | Select-Object -Unique)) {
    Invoke-Dv -Endpoint "EntityDefinitions(LogicalName='$entity')?`$select=LogicalName" | Out-Null
}

$created = 0
foreach ($column in $Columns) {
    $name = $column.Def.SchemaName
    $wanted = $ExpectedType[$column.Def."@odata.type"]
    $existing = Get-AttributeType $column.Entity $name
    if ($existing) {
        if ($existing -ne $wanted) {
            throw "$($column.Entity).$name exists as $existing; expected $wanted. Resolve by hand; this script does not alter or drop columns."
        }
        Write-Host "  exists   $($column.Entity).$name ($existing)" -ForegroundColor Yellow
        continue
    }
    if ($WhatIf) {
        Write-Host "  would add $($column.Entity).$name ($wanted)" -ForegroundColor Gray
        continue
    }
    Invoke-Dv -Method POST -Endpoint "EntityDefinitions(LogicalName='$($column.Entity)')/Attributes" -Body $column.Def | Out-Null
    Write-Host "  added    $($column.Entity).$name ($wanted)" -ForegroundColor Green
    $created++
}

foreach ($lookup in $Lookups) {
    $existing = Get-AttributeType $lookup.Entity $lookup.Name
    if ($existing) {
        if ($existing -ne "Lookup") {
            throw "$($lookup.Entity).$($lookup.Name) exists as $existing; expected Lookup. Resolve by hand; this script does not alter or drop columns."
        }
        Write-Host "  exists   $($lookup.Entity).$($lookup.Name) (Lookup)" -ForegroundColor Yellow
        continue
    }
    if ($WhatIf) {
        Write-Host "  would add $($lookup.Entity).$($lookup.Name) (Lookup -> $($lookup.Target))" -ForegroundColor Gray
        continue
    }
    $relationship = New-LookupRelationship $lookup.Target $lookup.Entity $lookup.Name $lookup.Display $lookup.Description
    Invoke-Dv -Method POST -Endpoint "RelationshipDefinitions" -Body $relationship | Out-Null
    Write-Host "  added    $($lookup.Entity).$($lookup.Name) (Lookup -> $($lookup.Target))" -ForegroundColor Green
    $created++
}

$relaxed = 0
foreach ($optional in $OptionalLookups) {
    $attr = Get-LookupAttribute $optional.Entity $optional.Name
    if ($attr.RequiredLevel.Value -eq "None") {
        Write-Host "  optional $($optional.Entity).$($optional.Name) (already RequiredLevel None)" -ForegroundColor Yellow
        continue
    }
    if ($WhatIf) {
        Write-Host "  would make $($optional.Entity).$($optional.Name) optional (RequiredLevel $($attr.RequiredLevel.Value) -> None)" -ForegroundColor Gray
        continue
    }
    $before = $attr.RequiredLevel.Value
    $attr.PSObject.Properties.Remove("@odata.context")
    $attr.RequiredLevel.Value = "None"
    Invoke-Dv -Method PUT -Endpoint "EntityDefinitions(LogicalName='$($optional.Entity)')/Attributes(LogicalName='$($optional.Name)')" -Body $attr -ExtraHeaders @{ "MSCRM.MergeLabels" = "true" } | Out-Null
    Write-Host "  relaxed  $($optional.Entity).$($optional.Name) (RequiredLevel $before -> None)" -ForegroundColor Green
    $relaxed++
}

if (-not $WhatIf) {
    $entitiesXml = "<entity>sprk_playbookrun</entity><entity>sprk_playbooknoderun</entity>"
    Invoke-Dv -Method POST -Endpoint "PublishXml" -Body @{ ParameterXml = "<importexportxml><entities>$entitiesXml</entities></importexportxml>" } | Out-Null
    Write-Host "Published sprk_playbookrun, sprk_playbooknoderun ($created column(s) added, $relaxed lookup(s) made optional)." -ForegroundColor Green

    foreach ($column in $Columns) {
        $type = Get-AttributeType $column.Entity $column.Def.SchemaName
        if (-not $type) { throw "Verification failed: $($column.Entity).$($column.Def.SchemaName) is missing after publish." }
    }
    foreach ($lookup in $Lookups) {
        $type = Get-AttributeType $lookup.Entity $lookup.Name
        if ($type -ne "Lookup") { throw "Verification failed: $($lookup.Entity).$($lookup.Name) is '$type' after publish; expected Lookup." }
    }
    foreach ($optional in $OptionalLookups) {
        $attr = Get-LookupAttribute $optional.Entity $optional.Name
        if ($attr.RequiredLevel.Value -ne "None") { throw "Verification failed: $($optional.Entity).$($optional.Name) is still RequiredLevel $($attr.RequiredLevel.Value)." }
    }
    Write-Host "Verified $($Columns.Count) columns, $($Lookups.Count) lookups and $($OptionalLookups.Count) optional lookups." -ForegroundColor Green
}
