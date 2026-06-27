<#
.SYNOPSIS
    Producer-fix patch: replaces the fetchXml inside the sprk_configjson
    of a "Query *" playbook node. Operates one node at a time so each
    channel can be reviewed + applied independently.

.DESCRIPTION
    The R1 Daily Briefing producer-layer playbooks were authored against
    a stale schema (sprk_todoflag, sprk_todostatus). Those fields do not
    exist on sprk_event in spaarkedev1 today, so the query nodes have
    been erroring silently for ~80 days, producing zero notifications.

    This script does a targeted PATCH of the fetchXml + description on
    ONE playbook query node. It does NOT touch the rest of the playbook.
    No new records. No schema changes.

    Built for Phase A of the producer-fix work (one channel per run).

.PARAMETER DataverseUrl
    Target Dataverse environment URL.

.PARAMETER NodeName
    The sprk_name of the playbook node to patch (e.g., 'Query Overdue Tasks').

.PARAMETER PlaybookName
    The sprk_name of the parent playbook (e.g., 'Tasks Overdue').
    Used as a safety check so we only update the node attached to the
    expected playbook.

.PARAMETER FetchXml
    The new fetchXml string to put inside sprk_configjson.fetchXml.

.PARAMETER Description
    Optional. New description string to put inside sprk_configjson.description.

.PARAMETER EntityLogicalName
    Optional. New entityLogicalName to put inside sprk_configjson.entityLogicalName.
    REQUIRED when the new fetchXml queries a different entity than the old one
    (e.g., appointment -> sprk_event). The BFF QueryDataverseNodeExecutor uses
    entityLogicalName to build the entitySetName for the OData URL path; if it
    doesn't match the fetchXml's <entity name="..."/>, the request fails.

.PARAMETER ClearParameters
    Optional switch. If set, removes the sprk_configjson.parameters block.
    Use when the old configjson had stale parameters (e.g., {{timeWindow}} that
    the BFF no longer supports).

.PARAMETER DryRun
    Preview the patch (print before/after) without writing.

.EXAMPLE
    .\Patch-PlaybookQueryFetchXml.ps1 `
        -DataverseUrl "https://spaarkedev1.crm.dynamics.com" `
        -PlaybookName 'Tasks Overdue' `
        -NodeName 'Query Overdue Tasks' `
        -FetchXml (Get-Content .\overdue-tasks-fetchxml.xml -Raw) `
        -Description 'Open Task-type sprk_events with due date OR final due date in the past, scoped to current user.' `
        -DryRun
#>
[CmdletBinding()]
param(
    [string]$DataverseUrl = $env:DATAVERSE_URL,
    [Parameter(Mandatory)] [string]$PlaybookName,
    [Parameter(Mandatory)] [string]$NodeName,
    [Parameter(Mandatory)] [string]$FetchXml,
    [string]$Description,
    [string]$EntityLogicalName,
    [switch]$ClearParameters,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

if (-not $DataverseUrl) {
    Write-Error "DataverseUrl is required. Set DATAVERSE_URL env var or pass -DataverseUrl parameter."
    exit 1
}

$ApiBase = "$DataverseUrl/api/data/v9.2"

Write-Host '======================================================'
Write-Host 'Producer-fix: replace fetchXml on a playbook query node'
Write-Host '======================================================'
Write-Host "Environment : $DataverseUrl"
Write-Host "Playbook    : $PlaybookName"
Write-Host "Node        : $NodeName"
Write-Host "Mode        : $(if ($DryRun) { 'DRY RUN' } else { 'LIVE' })"
Write-Host ''

# 1) Acquire access token
Write-Host '[1/5] Acquiring access token via Azure CLI...'
$token = az account get-access-token --resource $DataverseUrl --query accessToken -o tsv
if ([string]::IsNullOrWhiteSpace($token)) {
    Write-Error 'Failed to acquire Dataverse access token.'
    exit 1
}
$headers = @{
    'Authorization'    = "Bearer $token"
    'Content-Type'     = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'Accept'           = 'application/json'
}
Write-Host '       Token acquired.'

# 2) Resolve playbook id
Write-Host ''
Write-Host '[2/5] Resolving playbook id...'
$playbookFilter = "`$filter=sprk_name eq '$($PlaybookName -replace "'", "''")'&`$select=sprk_analysisplaybookid"
$playbookUrl = "$ApiBase/sprk_analysisplaybooks?$playbookFilter"
$playbookResp = Invoke-RestMethod -Uri $playbookUrl -Headers $headers -Method Get
if ($playbookResp.value.Count -eq 0) {
    Write-Error "Playbook '$PlaybookName' not found."
    exit 1
}
$playbookId = $playbookResp.value[0].sprk_analysisplaybookid
Write-Host "       Playbook id: $playbookId"

# 3) Resolve node id + read configjson
Write-Host ''
Write-Host '[3/5] Resolving node + reading current configjson...'
$nodeFilter = "`$filter=_sprk_playbookid_value eq '$playbookId' and sprk_name eq '$($NodeName -replace "'", "''")'&`$select=sprk_playbooknodeid,sprk_configjson"
$nodeUrl = "$ApiBase/sprk_playbooknodes?$nodeFilter"
$nodeResp = Invoke-RestMethod -Uri $nodeUrl -Headers $headers -Method Get
if ($nodeResp.value.Count -eq 0) {
    Write-Error "Node '$NodeName' not found under playbook '$PlaybookName'."
    exit 1
}
$nodeId = $nodeResp.value[0].sprk_playbooknodeid
$rawConfig = $nodeResp.value[0].sprk_configjson
Write-Host "       Node id     : $nodeId"

if ([string]::IsNullOrWhiteSpace($rawConfig)) {
    Write-Error "Node has empty sprk_configjson — cannot patch."
    exit 1
}

# 4) Parse, mutate, serialize
Write-Host ''
Write-Host '[4/5] Computing patched configjson...'
$config = $rawConfig | ConvertFrom-Json -AsHashtable -Depth 50

$oldFetch  = $config['fetchXml']
$oldEntity = $config['entityLogicalName']
$config['fetchXml'] = $FetchXml
if ($PSBoundParameters.ContainsKey('Description')) {
    $config['description'] = $Description
}
if ($PSBoundParameters.ContainsKey('EntityLogicalName')) {
    $config['entityLogicalName'] = $EntityLogicalName
}
if ($ClearParameters -and $config.ContainsKey('parameters')) {
    $config.Remove('parameters') | Out-Null
}
$newConfig = $config | ConvertTo-Json -Depth 50 -Compress

Write-Host ''
if ($PSBoundParameters.ContainsKey('EntityLogicalName')) {
    Write-Host "EntityLogicalName: $oldEntity -> $EntityLogicalName" -ForegroundColor Yellow
}
if ($ClearParameters) {
    Write-Host 'Parameters block: REMOVED' -ForegroundColor Yellow
}
Write-Host '------ OLD fetchXml ------' -ForegroundColor DarkGray
Write-Host $oldFetch
Write-Host '------ NEW fetchXml ------' -ForegroundColor Cyan
Write-Host $FetchXml
Write-Host '--------------------------'

if ($DryRun) {
    Write-Host ''
    Write-Host '[5/5] DRY RUN — no write performed.' -ForegroundColor Cyan
    exit 0
}

# 5) PATCH
Write-Host ''
Write-Host '[5/5] PATCHing sprk_playbooknodes(...) with new sprk_configjson...'
$patchUrl = "$ApiBase/sprk_playbooknodes($nodeId)"
$patchBody = @{ sprk_configjson = $newConfig } | ConvertTo-Json -Depth 10
Invoke-RestMethod -Uri $patchUrl -Headers $headers -Method Patch -Body $patchBody | Out-Null
Write-Host '       PATCH succeeded.' -ForegroundColor Green

Write-Host ''
Write-Host 'Verification hint:'
Write-Host '  1. Trigger a playbook tick (wait for scheduler or invoke manually).'
Write-Host '  2. Check sprk_playbookrun rows for the parent playbook — should now succeed.'
Write-Host '  3. Check appnotification records for new ones with this category.'
