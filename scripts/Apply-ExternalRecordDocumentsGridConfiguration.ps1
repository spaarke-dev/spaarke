<#
.SYNOPSIS
    Creates or updates the ONE sprk_gridconfiguration row "External record documents": the Documents list on every record
    in the external SPA's record template (spaarke-SPA-external-access-platform-r4 task 023b; the SPA side is task 023c).

.DESCRIPTION
    Source of truth: config/external-record-documents.gridconfiguration.json (the repo definition). The file carries the
    row's id, name and entity and, in its `configJson` member, exactly what the shared <DataGrid configId=...> reads from
    sprk_configjson (types/DataGridConfiguration.ts, v1.0). Only `configJson` is written to sprk_configjson.

    The SPA gives this grid its rows from the record route (the grid's rowSource), so the FetchXML in the row is never
    executed by the SPA; it is kept valid for any other host. The read side is the BFF's `grid-configuration` module: the
    row id must be in OutsideCounselGridConfigurationIds (ExternalAccessModule.cs) or the SPA's config load is refused.
    Deploy order: BFF with the id admitted (via uac-r2), THEN this script, THEN the SPA that uses the grid.

    Idempotent. The row has a fixed id (in the definition):
      row absent              -> POST (create)
      row present, differs    -> PATCH sprk_name, sprk_entitylogicalname, sprk_configjson (update)
      row present, identical  -> nothing written ("unchanged")
    "Identical" compares the parsed definition, not whitespace. After a write the row is read back and compared; any
    difference fails the script.

    Before anything else (also in -WhatIf, -Verify and -LintOnly) the definition is linted: it must parse, carry the fixed
    id / name / entity, fit sprk_configjson (20000 characters), hold a well-formed inline FetchXML on sprk_document that
    selects no SPE pointer column and not sprk_summary (not on the documents module's ReadableColumns: requesting it would
    fail the whole read with DV_FETCHXML_COLUMN_NOT_PERMITTED), and show exactly Name, Type, Date Added and AI Summary with
    truncateWithTooltip on.

    This script is a Dataverse data change when it writes. It is NOT run by the task that created it: it is run in task 028
    with the owner's approval (-WhatIf, then apply, then -Verify -RequireRow).

    ROLLBACK: delete the row by id (DELETE <DataverseUrl>/api/data/v9.2/sprk_gridconfigurations(<id>)). Nothing else
    references it until the SPA that uses it is deployed; after that, roll the SPA back first.

.PARAMETER DataverseUrl
    Target environment, e.g. https://spaarkedev1.crm.dynamics.com. Defaults to $env:DATAVERSE_URL. Required except with -LintOnly.

.PARAMETER DefinitionPath
    The definition JSON. Default: config/external-record-documents.gridconfiguration.json beside the repo root.

.PARAMETER LintOnly
    OFFLINE. Lints the definition and exits: no environment, no token, no network.

.PARAMETER Verify
    READ-ONLY (reads the one configuration row; reads no document). Lints the definition and reports the row's state (ABSENT,
    INACTIVE, IN SYNC or DRIFT). Exit 1 on a lint failure, INACTIVE or DRIFT; ABSENT is exit 0 unless -RequireRow is given.

.PARAMETER RequireRow
    With -Verify: treat an ABSENT row as a failure (use after the apply).

.PARAMETER WhatIf
    Standard PowerShell switch (SupportsShouldProcess): report the create/update that would happen; write nothing.

.EXAMPLE
    # Offline lint (no environment needed):
    pwsh scripts/Apply-ExternalRecordDocumentsGridConfiguration.ps1 -LintOnly
.EXAMPLE
    # Plan, then apply, then verify the row is in sync (the apply needs owner approval on a shared environment):
    pwsh scripts/Apply-ExternalRecordDocumentsGridConfiguration.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com -WhatIf
    pwsh scripts/Apply-ExternalRecordDocumentsGridConfiguration.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com
    pwsh scripts/Apply-ExternalRecordDocumentsGridConfiguration.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com -Verify -RequireRow
#>
#Requires -Version 7.0
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$DataverseUrl = $env:DATAVERSE_URL,
    [string]$DefinitionPath,
    [switch]$LintOnly,
    [switch]$Verify,
    [switch]$RequireRow
)

$ErrorActionPreference = 'Stop'

# Fixed id (the BFF's OutsideCounselGridConfigurationIds names it; the SPA's RECORD_DOCUMENTS_GRID constant is the same id).
$ExpectedRowId = '286be8b4-f96f-4d5a-b1d5-82fd83ca9693'
$ExpectedName = 'External record documents'
$ExpectedEntity = 'sprk_document'
$MaxConfigJson = 20000   # sprk_gridconfiguration.sprk_configjson MaxLength on spaarkedev1
# Never selectable on the external plane: the SPE pointer columns (ExternalModuleRegistry.PointerColumns), and sprk_summary,
# which is not on the documents module's ReadableColumns (the SPA's rows supply it).
$ForbiddenFetchAttributes = @('sprk_graphdriveid', 'sprk_graphitemid', 'sprk_filepath', 'sprk_summary')
$ExpectedColumns = [ordered]@{ sprk_documentname = 'Name'; sprk_documenttype = 'Type'; createdon = 'Date Added'; sprk_summary = 'AI Summary' }

if (-not $DefinitionPath) { $DefinitionPath = Join-Path $PSScriptRoot '..\config\external-record-documents.gridconfiguration.json' }
if (-not (Test-Path -LiteralPath $DefinitionPath)) { throw "Definition not found: $DefinitionPath" }

# ---- the definition and its lint ------------------------------------------------------------------------------------
$definition = Get-Content -LiteralPath $DefinitionPath -Raw -Encoding utf8 | ConvertFrom-Json -Depth 32
$RowId = [string]$definition.id
$RowName = [string]$definition.name
$RowEntity = [string]$definition.entity
$configObject = $definition.configJson
$compact = $configObject | ConvertTo-Json -Depth 32 -Compress

function Test-Definition($def, [string]$json) {
    $problems = [System.Collections.Generic.List[string]]::new()
    if ($def._definition -ne '1.0' -or $def.kind -ne 'datagrid') { $problems.Add('definition must have _definition 1.0 and kind datagrid') }
    if ($def.id -ne $ExpectedRowId) { $problems.Add("id must be $ExpectedRowId") }
    if ($def.name -ne $ExpectedName) { $problems.Add("name must be '$ExpectedName'") }
    if ($def.entity -ne $ExpectedEntity) { $problems.Add("entity must be $ExpectedEntity") }
    $cfg = $def.configJson
    if ($null -eq $cfg) { $problems.Add('configJson is missing'); return $problems }
    if ($cfg._version -ne '1.0') { $problems.Add('configJson._version must be 1.0') }
    if ($json.Length -gt $MaxConfigJson) { $problems.Add("compact configJson is $($json.Length) characters; sprk_configjson holds $MaxConfigJson") }
    if ($cfg.source.type -ne 'inline') { $problems.Add('source.type must be inline') }

    $xml = $null
    if ([string]::IsNullOrWhiteSpace($cfg.source.fetchXml)) { $problems.Add('source.fetchXml is missing') }
    else {
        try { $xml = [xml]$cfg.source.fetchXml } catch { $problems.Add("source.fetchXml is not well-formed XML ($($_.Exception.Message))") }
    }
    if ($null -ne $xml) {
        if ($xml.fetch.entity.name -ne $ExpectedEntity) { $problems.Add("the FetchXML must be on $ExpectedEntity") }
        foreach ($a in @($xml.fetch.entity.attribute)) {
            if ($ForbiddenFetchAttributes -contains $a.name) { $problems.Add("the FetchXML selects $($a.name), which the external documents read refuses") }
        }
        foreach ($c in @($xml.SelectNodes('//condition | //order'))) {
            if ($ForbiddenFetchAttributes -contains $c.attribute) { $problems.Add("the FetchXML uses $($c.attribute) in a condition or order") }
        }
    }

    $layout = $null
    if ([string]::IsNullOrWhiteSpace($cfg.source.layoutXml)) { $problems.Add('source.layoutXml is missing') }
    else {
        try { $layout = [xml]$cfg.source.layoutXml } catch { $problems.Add("source.layoutXml is not well-formed XML ($($_.Exception.Message))") }
    }
    if ($null -ne $layout) {
        $cells = @($layout.SelectNodes('//cell') | ForEach-Object { $_.name })
        if (($cells -join ',') -ne ($ExpectedColumns.Keys -join ',')) { $problems.Add("layout cells must be exactly: $($ExpectedColumns.Keys -join ', ') (in that order); found: $($cells -join ', ')") }
        foreach ($cell in $ExpectedColumns.GetEnumerator()) {
            $label = $cfg.columns.($cell.Key).label
            if ($label -ne $cell.Value) { $problems.Add("columns.$($cell.Key).label must be '$($cell.Value)'") }
        }
        # Every non-summary layout column must be selected, or its cell renders blank for a host that runs the FetchXML.
        if ($null -ne $xml) {
            $selected = @($xml.fetch.entity.attribute | ForEach-Object { $_.name })
            foreach ($name in ($cells | Where-Object { $_ -ne 'sprk_summary' })) {
                if ($selected -notcontains $name) { $problems.Add("layout column $name is not selected in the FetchXML") }
            }
        }
    }
    if ($cfg.display.truncateWithTooltip -ne $true) { $problems.Add('display.truncateWithTooltip must be true') }
    $problems
}

$lint = @(Test-Definition $definition $compact)
if ($lint.Count -gt 0) {
    $lint | ForEach-Object { Write-Host "LINT FAIL: $_" -ForegroundColor Red }
    throw "Definition lint failed ($($lint.Count) problem(s)). Nothing was read from or written to any environment."
}
Write-Host "Definition OK: $DefinitionPath ($($compact.Length) of $MaxConfigJson characters, row $RowId)."
if ($LintOnly) { return }

# ---- Dataverse access -----------------------------------------------------------------------------------------------
if (-not $DataverseUrl) { throw 'DataverseUrl is required (parameter or $env:DATAVERSE_URL).' }
$DataverseUrl = $DataverseUrl.TrimEnd('/')
$ApiBase = "$DataverseUrl/api/data/v9.2"

$token = az account get-access-token --resource $DataverseUrl --query accessToken -o tsv
if (-not $token) { throw "Could not get a token for $DataverseUrl (az login?)." }
$headers = @{
    Authorization      = "Bearer $token"
    'Content-Type'     = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
}

function Invoke-Dv([string]$Method, [string]$Uri, $Body) {
    try {
        if ($null -ne $Body) { Invoke-RestMethod -Method $Method -Uri $Uri -Headers $headers -Body ($Body | ConvertTo-Json -Depth 5 -Compress) }
        else { Invoke-RestMethod -Method $Method -Uri $Uri -Headers $headers }
    }
    catch {
        $detail = if ($_.ErrorDetails.Message) { $_.ErrorDetails.Message } else { $_.Exception.Message }
        throw "$Method $Uri failed: $detail"
    }
}

function Get-Row {
    $r = Invoke-Dv GET "$ApiBase/sprk_gridconfigurations?`$select=sprk_gridconfigurationid,sprk_name,sprk_entitylogicalname,sprk_configjson,statecode&`$filter=sprk_gridconfigurationid eq $RowId" $null
    if ($r.value.Count -eq 0) { return $null }
    $r.value[0]
}

function Test-RowMatches($row) {
    # Compare the parsed definition, not whitespace.
    $live = try { ($row.sprk_configjson | ConvertFrom-Json -Depth 32 | ConvertTo-Json -Depth 32 -Compress) } catch { $null }
    ($row.sprk_name -eq $RowName) -and ($row.sprk_entitylogicalname -eq $RowEntity) -and ($live -ceq $compact)
}

# ---- -Verify: read only ---------------------------------------------------------------------------------------------
if ($Verify) {
    $failed = $false
    Write-Host "--- Verify (read-only) against $DataverseUrl ---"
    $row = Get-Row
    if ($null -eq $row) {
        Write-Host "Row $RowId : ABSENT (not applied)." -ForegroundColor Yellow
        if ($RequireRow) { $failed = $true }
    }
    elseif ($row.statecode -ne 0) { Write-Host "Row $RowId : INACTIVE (statecode $($row.statecode)); the grid would not load it." -ForegroundColor Red; $failed = $true }
    elseif (Test-RowMatches $row) { Write-Host "Row $RowId : IN SYNC with $DefinitionPath." -ForegroundColor Green }
    else { Write-Host "Row $RowId : DRIFT (the live row differs from the repo definition)." -ForegroundColor Red; $failed = $true }
    if ($failed) { exit 1 }
    return
}

# ---- apply: create / update / unchanged -----------------------------------------------------------------------------
$row = Get-Row
$body = @{ sprk_name = $RowName; sprk_entitylogicalname = $RowEntity; sprk_configjson = $compact }

if ($null -eq $row) {
    if ($PSCmdlet.ShouldProcess("$DataverseUrl sprk_gridconfiguration $RowId", "CREATE '$RowName'")) {
        Invoke-Dv POST "$ApiBase/sprk_gridconfigurations" ($body + @{ sprk_gridconfigurationid = $RowId }) | Out-Null
        Write-Host "CREATED: $RowName -> $RowId"
    }
}
elseif (Test-RowMatches $row) {
    Write-Host "UNCHANGED: $RowName -> $RowId (the live row already equals the definition)."
    return
}
else {
    if ($PSCmdlet.ShouldProcess("$DataverseUrl sprk_gridconfiguration $RowId", "UPDATE '$RowName' (name, entity, configjson)")) {
        Invoke-Dv PATCH "$ApiBase/sprk_gridconfigurations($RowId)" $body | Out-Null
        Write-Host "UPDATED: $RowName -> $RowId"
    }
}

if ($WhatIfPreference) { return }

# read-back: any difference fails the script
$after = Get-Row
if ($null -eq $after -or -not (Test-RowMatches $after)) { throw "Read-back of $RowId does not match the definition." }
Write-Host "READ-BACK OK: row $RowId equals the definition."
