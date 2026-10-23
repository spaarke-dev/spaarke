<#
.SYNOPSIS
    Creates or updates the ONE sprk_gridconfiguration row that defines the ontology worklist (task 050): membership and
    base order per lane (Decide, Do) plus the three secondary sets (closed today, suppressed, closed earlier).

.DESCRIPTION
    Source of truth: config/worklist.gridconfiguration.json (the repo definition). The row is executed server-side by the
    BFF Signal read route (task 038, D-15), never by <DataGrid> in the browser, so it holds FetchXML only: no actions
    (those come from the policy version's decision plan, FR-49) and no per-user filter.

    Idempotent. The row has a fixed id (below) so the route can find it:
      row absent              -> POST (create)
      row present, differs    -> PATCH sprk_name, sprk_entitylogicalname, sprk_configjson (update)
      row present, identical  -> nothing written ("unchanged")
    "Identical" compares the parsed definition, not whitespace. After a write the row is read back and compared; any
    difference fails the script.

    Before anything else (also in -WhatIf and -Verify) the definition is linted: it must parse, carry the five queries,
    fit sprk_configjson (20000 characters), each FetchXML must be well-formed XML on sprk_signal, and no order clause may
    name sprk_rankscore, a severity column or a high-priority column (D-42: rank is applied by the route).

    This script is a dev data change when it writes. Run it with -WhatIf first; -Verify never writes.

    ORDERING REQUIREMENT (D-185, task 163, #2285). Nothing in Deploy-Release.ps1 or the provisioning seeders (H12b seeds only
    the reconciliation grids) applies this row, so run this script by hand on every environment, in this order:
      (1) deploy the BFF that carries task 163 (it reads the policy columns as the BFF);
      (2) apply this definition (it has no `policy` link);
      (3) only then import the roles (Spaarke Basic User and Console User lose Read on sprk_policy / sprk_policyversion).
    Roles before step 2 give every user an empty worklist (HTTP 200, one log warning): the old definition still links
    sprk_policy as the caller. An old BFF with the new definition shows no rule names and ranks everything last.

.PARAMETER DataverseUrl
    Target environment, e.g. https://spaarkedev1.crm.dynamics.com. Defaults to $env:DATAVERSE_URL. Required.

.PARAMETER DefinitionPath
    The definition JSON. Default: config/worklist.gridconfiguration.json beside the repo root.

.PARAMETER Verify
    READ-ONLY. Lints the definition, runs each of the five FetchXML queries against the target as a read and prints the
    row count of each, and reports the row's state (ABSENT, IN SYNC or DRIFT). Exit 1 on a lint failure, a query error or
    DRIFT; ABSENT is exit 0 unless -RequireRow is given.

.PARAMETER RequireRow
    With -Verify: treat an ABSENT row as a failure (use after the apply).

.PARAMETER WhatIf
    Standard PowerShell switch (SupportsShouldProcess): report the create/update that would happen; write nothing.

.EXAMPLE
    # Read-only validation (no row needed):
    pwsh scripts/Apply-WorklistGridConfiguration.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com -Verify
.EXAMPLE
    # Plan, then apply, then verify the row is in sync (the apply needs owner approval on a shared environment):
    pwsh scripts/Apply-WorklistGridConfiguration.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com -WhatIf
    pwsh scripts/Apply-WorklistGridConfiguration.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com
    pwsh scripts/Apply-WorklistGridConfiguration.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com -Verify -RequireRow
#>
#Requires -Version 7.0
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$DataverseUrl = $env:DATAVERSE_URL,
    [string]$DefinitionPath,
    [switch]$Verify,
    [switch]$RequireRow
)

$ErrorActionPreference = 'Stop'

# Fixed id so the Signal read route (task 038) can find the row. 5001-5003 are the reconciliation rows
# (scripts/seed-reconciliation-gridconfig.ps1); 5010 is the worklist.
$RowId = '00000000-0000-4000-8000-000000005010'
$RowName = 'Ontology Worklist - Signal membership and order'
$RowEntity = 'sprk_signal'
$MaxConfigJson = 20000   # sprk_gridconfiguration.sprk_configjson MaxLength on spaarkedev1

if (-not $DefinitionPath) { $DefinitionPath = Join-Path $PSScriptRoot '..\config\worklist.gridconfiguration.json' }
if (-not (Test-Path -LiteralPath $DefinitionPath)) { throw "Definition not found: $DefinitionPath" }
if (-not $DataverseUrl) { throw 'DataverseUrl is required (parameter or $env:DATAVERSE_URL).' }
$DataverseUrl = $DataverseUrl.TrimEnd('/')
$ApiBase = "$DataverseUrl/api/data/v9.2"

# ---- the definition and its lint ------------------------------------------------------------------------------------
$definition = Get-Content -LiteralPath $DefinitionPath -Raw -Encoding utf8 | ConvertFrom-Json -Depth 32
$compact = $definition | ConvertTo-Json -Depth 32 -Compress

function Get-Queries($def) {
    # name -> FetchXML, in a fixed order
    [ordered]@{
        'lane:decide'            = $def.lanes.decide.fetchXml
        'lane:do'                = $def.lanes.do.fetchXml
        'secondary:closedToday'  = $def.secondary.closedToday.fetchXml
        'secondary:suppressed'   = $def.secondary.suppressed.fetchXml
        'secondary:closedEarlier' = $def.secondary.closedEarlier.fetchXml
    }
}

function Test-Definition($def, [string]$json) {
    $problems = [System.Collections.Generic.List[string]]::new()
    if ($def._version -ne '1.0' -or $def.kind -ne 'worklist') { $problems.Add('definition must have _version 1.0 and kind worklist') }
    if ($def.entity -ne $RowEntity) { $problems.Add("entity must be $RowEntity") }
    if ($json.Length -gt $MaxConfigJson) { $problems.Add("compact JSON is $($json.Length) characters; sprk_configjson holds $MaxConfigJson") }
    foreach ($q in (Get-Queries $def).GetEnumerator()) {
        if ([string]::IsNullOrWhiteSpace($q.Value)) { $problems.Add("$($q.Key): fetchXml is missing"); continue }
        try { $xml = [xml]$q.Value } catch { $problems.Add("$($q.Key): fetchXml is not well-formed XML ($($_.Exception.Message))"); continue }
        if ($xml.fetch.entity.name -ne $RowEntity) { $problems.Add("$($q.Key): the query must be on $RowEntity") }
        foreach ($o in @($xml.fetch.entity.order)) {
            if ($o.attribute -match 'rankscore|severity|highpriority') { $problems.Add("$($q.Key): order names $($o.attribute); rank is applied by the route (D-42)") }
        }
    }
    if ($def.PSObject.Properties.Name -contains 'actions' -or $def.PSObject.Properties.Name -contains 'secondaryActions') {
        $problems.Add('the row carries no actions (FR-49)')
    }
    $problems
}

$lint = @(Test-Definition $definition $compact)
if ($lint.Count -gt 0) {
    $lint | ForEach-Object { Write-Host "LINT FAIL: $_" -ForegroundColor Red }
    throw "Definition lint failed ($($lint.Count) problem(s)). Nothing was read from or written to $DataverseUrl."
}
Write-Host "Definition OK: $DefinitionPath ($($compact.Length) of $MaxConfigJson characters, 5 queries)."

# ---- Dataverse access -----------------------------------------------------------------------------------------------
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

function Get-QueryRowCount([string]$FetchXml) {
    $uri = "$ApiBase/sprk_signals?fetchXml=" + [uri]::EscapeDataString($FetchXml)
    $r = Invoke-Dv GET $uri $null
    $n = @($r.value).Count
    if ($r.'@Microsoft.Dynamics.CRM.morerecords') { "$n+ (more pages)" } else { "$n" }
}

# ---- -Verify: read only ---------------------------------------------------------------------------------------------
if ($Verify) {
    $failed = $false
    Write-Host "--- Verify (read-only) against $DataverseUrl ---"
    foreach ($q in (Get-Queries $definition).GetEnumerator()) {
        try { Write-Host ("{0,-26} rows = {1}" -f $q.Key, (Get-QueryRowCount $q.Value)) }
        catch { Write-Host ("{0,-26} ERROR: {1}" -f $q.Key, $_.Exception.Message) -ForegroundColor Red; $failed = $true }
    }
    $row = Get-Row
    if ($null -eq $row) {
        Write-Host "Row $RowId : ABSENT (not applied)." -ForegroundColor Yellow
        if ($RequireRow) { $failed = $true }
    }
    elseif ($row.statecode -ne 0) { Write-Host "Row $RowId : INACTIVE (statecode $($row.statecode)); the route would not find it." -ForegroundColor Red; $failed = $true }
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
