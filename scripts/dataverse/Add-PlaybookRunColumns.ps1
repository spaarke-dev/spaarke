<#
.SYNOPSIS
    Adds the durable-run-status gap columns to sprk_playbookrun and sprk_playbooknoderun.

.DESCRIPTION
    spaarkeai-mcp-bff-api-r2 task 005 (FR-13). PlaybookOrchestrationService writes these tables on the run's own
    execution path; the columns below are the ones the existing tables lack (projects/spaarkeai-mcp-bff-api-r2/notes/005-schema.md).

      sprk_playbookrun
        sprk_correlationid        String(100)   correlation / idempotency id (no alternate key)
        sprk_errorcode            String(100)   stable failure code
        sprk_regardingentitytype  String(100)   regarding record: entity logical name
        sprk_regardingrecordid    String(100)   regarding record: id (GUID "D" format)
        sprk_summary              Memo(4000)    short, content-free run summary
      sprk_playbooknoderun
        sprk_startedon            DateTime
        sprk_completedon          DateTime
        sprk_sequence             Integer >= 0  start order within the run
        sprk_confidence           Decimal(10)   the node's numeric confidence score, range +-1000 (not content)

    Additive and idempotent: an existing column is checked for the expected type and skipped; a column that exists
    with a different type stops the script. Nothing is deleted. Publishes the two tables at the end.
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
    param([string]$Method = "GET", [string]$Endpoint, [object]$Body = $null)
    $headers = @{
        "Authorization"           = "Bearer $script:Token"
        "OData-MaxVersion"        = "4.0"
        "OData-Version"           = "4.0"
        "Accept"                  = "application/json"
        "Content-Type"            = "application/json; charset=utf-8"
        "MSCRM.SolutionUniqueName" = $SolutionName
    }
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

# Expected AttributeType per definition @odata.type, for the idempotency type check.
$ExpectedType = @{
    "Microsoft.Dynamics.CRM.StringAttributeMetadata"   = "String"
    "Microsoft.Dynamics.CRM.MemoAttributeMetadata"     = "Memo"
    "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata" = "DateTime"
    "Microsoft.Dynamics.CRM.IntegerAttributeMetadata"  = "Integer"
    "Microsoft.Dynamics.CRM.DecimalAttributeMetadata"  = "Decimal"
}

$Columns = @(
    @{ Entity = "sprk_playbookrun"; Def = New-StringColumn "sprk_correlationid" "Correlation Id" "Correlation / idempotency id of the request that started the run (FR-13)." 100 }
    @{ Entity = "sprk_playbookrun"; Def = New-StringColumn "sprk_errorcode" "Error Code" "Stable failure code of a failed run, e.g. playbook_node_failed (FR-13)." 100 }
    @{ Entity = "sprk_playbookrun"; Def = New-StringColumn "sprk_regardingentitytype" "Regarding Entity Type" "Logical name of the record the run is about, e.g. sprk_matter (FR-13)." 100 }
    @{ Entity = "sprk_playbookrun"; Def = New-StringColumn "sprk_regardingrecordid" "Regarding Record Id" "Id of the record the run is about (GUID) (FR-13)." 100 }
    @{ Entity = "sprk_playbookrun"; Def = New-MemoColumn "sprk_summary" "Summary" "Short run summary built from counts; never document content, prompts or model output (FR-13, ADR-015)." 4000 }
    @{ Entity = "sprk_playbooknoderun"; Def = New-DateTimeColumn "sprk_startedon" "Started On" "When the node started (FR-13)." }
    @{ Entity = "sprk_playbooknoderun"; Def = New-DateTimeColumn "sprk_completedon" "Completed On" "When the node finished (FR-13)." }
    @{ Entity = "sprk_playbooknoderun"; Def = New-IntegerColumn "sprk_sequence" "Sequence" "Start order of the node within its run, from 1 (FR-13)." }
    @{ Entity = "sprk_playbooknoderun"; Def = New-DecimalColumn "sprk_confidence" "Confidence" "The node's numeric confidence score, as the run detail returns it; a number, never content (FR-13)." }
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

if (-not $WhatIf) {
    $entitiesXml = "<entity>sprk_playbookrun</entity><entity>sprk_playbooknoderun</entity>"
    Invoke-Dv -Method POST -Endpoint "PublishXml" -Body @{ ParameterXml = "<importexportxml><entities>$entitiesXml</entities></importexportxml>" } | Out-Null
    Write-Host "Published sprk_playbookrun, sprk_playbooknoderun ($created column(s) added)." -ForegroundColor Green

    foreach ($column in $Columns) {
        $type = Get-AttributeType $column.Entity $column.Def.SchemaName
        if (-not $type) { throw "Verification failed: $($column.Entity).$($column.Def.SchemaName) is missing after publish." }
    }
    Write-Host "Verified all $($Columns.Count) columns." -ForegroundColor Green
}
