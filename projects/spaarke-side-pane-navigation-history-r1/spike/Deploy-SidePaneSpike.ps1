<#
.SYNOPSIS
  Task 001 SPIKE deploy — UPSERT the two spike web resources to Dataverse and publish.

.DESCRIPTION
  Deploys (create-if-missing, else PATCH content):
    - sprk_sidepanespike.html            (webresourcetype 1 / HTML)  — diagnostic pane body
    - sprk_sidepanespikebootstrap.js     (webresourcetype 3 / JScript) — Path B bootstrap
  Then PublishXml both. Mirrors scripts/Deploy-CalendarSidePane.ps1 (az token -> Web API
  base64 -> PublishXml) with create logic added (CalendarSidePane's script only PATCHes
  an existing WR).

  Auth: uses `az account get-access-token` — run `az login` first if needed.
  RibbonDiffXml is NOT deployed here (Web API cannot apply it) — see the report §Stage 2.

.PARAMETER DataverseUrl
  Target org. Default https://spaarkedev1.crm.dynamics.com (the only env for this project).

.EXAMPLE
  ./Deploy-SidePaneSpike.ps1
#>
param(
    [string]$DataverseUrl = "https://spaarkedev1.crm.dynamics.com"
)

$ErrorActionPreference = 'Stop'
$orgUrl = $DataverseUrl.TrimEnd('/')
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host '=====================================' -ForegroundColor Cyan
Write-Host ' Side-Pane Spike — Web Resource Deploy' -ForegroundColor Cyan
Write-Host " Target: $orgUrl" -ForegroundColor Cyan
Write-Host '=====================================' -ForegroundColor Cyan

# [1] token
Write-Host '[1/4] Getting access token…'
$accessToken = az account get-access-token --resource $orgUrl --query accessToken -o tsv
if ([string]::IsNullOrEmpty($accessToken)) { Write-Error 'Failed to get access token (run: az login)'; exit 1 }
Write-Host '      Token acquired' -ForegroundColor Green

$apiUrl = "$orgUrl/api/data/v9.2"
$headers = @{
    'Authorization'    = "Bearer $accessToken"
    'Content-Type'     = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'Accept'           = 'application/json'
}

# web resources to upsert: name, type (1=HTML,3=JScript), file, displayname
$resources = @(
    @{ name = 'sprk_sidepanespike.html';         type = 1; file = 'sprk_sidepanespike.html';         display = 'Side-Pane Spike (pane body)' },
    @{ name = 'sprk_sidepanespikebootstrap.js';  type = 3; file = 'sprk_sidepanespikebootstrap.js';  display = 'Side-Pane Spike (bootstrap)' }
)

$publishIds = @()

foreach ($r in $resources) {
    $filePath = Join-Path $scriptDir $r.file
    if (-not (Test-Path $filePath)) { Write-Error "Source file not found: $filePath"; exit 1 }
    $b64 = [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($filePath))

    Write-Host ("[2/4] Upserting {0}…" -f $r.name)
    $searchUrl = "$apiUrl/webresourceset?`$select=webresourceid,name&`$filter=name eq '$($r.name)'"
    $found = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get

    if ($found.value.Count -gt 0) {
        $id = $found.value[0].webresourceid
        $body = @{ content = $b64 } | ConvertTo-Json
        Invoke-RestMethod -Uri "$apiUrl/webresourceset($id)" -Headers $headers -Method Patch -Body $body | Out-Null
        Write-Host ("      Updated existing: {0}" -f $id) -ForegroundColor Green
    }
    else {
        $body = @{
            name           = $r.name
            displayname    = $r.display
            webresourcetype = $r.type
            content        = $b64
        } | ConvertTo-Json
        $created = Invoke-RestMethod -Uri "$apiUrl/webresourceset" -Headers $headers -Method Post -Body $body -ResponseHeadersVariable rh
        # OData-EntityId header carries the new id
        $id = $null
        if ($rh.'OData-EntityId') { $id = ([regex]::Match($rh.'OData-EntityId', '\(([0-9a-fA-F-]+)\)')).Groups[1].Value }
        if (-not $id) {
            $re = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get
            $id = $re.value[0].webresourceid
        }
        Write-Host ("      Created new: {0}" -f $id) -ForegroundColor Green
    }
    $publishIds += $id
}

# [3] publish both
Write-Host '[3/4] Publishing…'
$wrXml = ($publishIds | ForEach-Object { "<webresource>{$_}</webresource>" }) -join ''
$publishXml = "<importexportxml><webresources>$wrXml</webresources></importexportxml>"
$publishBody = @{ ParameterXml = $publishXml } | ConvertTo-Json
Invoke-RestMethod -Uri "$apiUrl/PublishXml" -Headers $headers -Method Post -Body $publishBody | Out-Null
Write-Host '      Published' -ForegroundColor Green

# [4] done
Write-Host '[4/4] Done.' -ForegroundColor Green
Write-Host ''
Write-Host 'Deployed web resources:' -ForegroundColor Cyan
foreach ($r in $resources) { Write-Host ("  - {0}" -f $r.name) }
Write-Host ''
Write-Host 'NEXT (Stage 1 — no ribbon needed): open Matter Management, then in DevTools console run:' -ForegroundColor Yellow
Write-Host '  Xrm.WebApi ? "" : 0;  // ensure you are on the app frame' -ForegroundColor DarkGray
Write-Host "  var s=document.createElement('script'); s.src='/WebResources/sprk_sidepanespikebootstrap.js'; document.body.appendChild(s);" -ForegroundColor DarkGray
Write-Host '  // …then Spaarke.SidePaneSpike.initialize()' -ForegroundColor DarkGray
Write-Host 'See notes/task-001-spike-report.md for the full runbook.' -ForegroundColor Yellow
