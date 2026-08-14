<#
.SYNOPSIS
  Task 086 deploy — UPSERT the two Navigator web resources to Dataverse and publish.

.DESCRIPTION
  Deploys (create-if-missing, else PATCH content):
    - sprk_NavigatorPane.html   (webresourcetype 1 / HTML)    — the Vite single-file code page (dist/index.html)
    - sprk_SidePaneManager      (webresourcetype 3 / JScript) — Path B app-startup bootstrap

  The JS web resource is named EXACTLY 'sprk_SidePaneManager' (no extension) so it matches the
  spaarkedev1 application ribbon's still-live EnableRule reference
  Library="$webresource:sprk_SidePaneManager" / FunctionName="Spaarke.SidePaneManager.initialize"
  — recreating it reactivates the dormant rule (auto-launch at app load, no ribbon import).

  Then PublishXml both. Mirrors spike/Deploy-SidePaneSpike.ps1 (az token -> Web API base64 -> PublishXml).
  Auth: uses `az account get-access-token` — run `az login` first if needed.

.PARAMETER DataverseUrl
  Target org. Default https://spaarkedev1.crm.dynamics.com (the only env for this project).

.EXAMPLE
  ./Deploy-NavigatorPane.ps1
#>
param(
    [string]$DataverseUrl = "https://spaarkedev1.crm.dynamics.com"
)

$ErrorActionPreference = 'Stop'
$orgUrl = $DataverseUrl.TrimEnd('/')
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host '=====================================' -ForegroundColor Cyan
Write-Host ' NavigatorPane — Web Resource Deploy' -ForegroundColor Cyan
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

# web resources to upsert: name (EXACT), type (1=HTML,3=JScript), file, displayname
$resources = @(
    @{ name = 'sprk_NavigatorPane.html'; type = 1;  file = 'dist/index.html';                 display = 'Navigator (code page)' },
    @{ name = 'sprk_SidePaneManager';    type = 3;  file = 'bootstrap/sprk_SidePaneManager.js'; display = 'Spaarke Side-Pane Manager (bootstrap)' },
    @{ name = 'sprk_navigatorstar.svg';  type = 11; file = 'bootstrap/sprk_navigatorstar.svg';  display = 'Navigator rail icon (star)' }
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
            name            = $r.name
            displayname     = $r.display
            webresourcetype = $r.type
            content         = $b64
        } | ConvertTo-Json
        $created = Invoke-RestMethod -Uri "$apiUrl/webresourceset" -Headers $headers -Method Post -Body $body -ResponseHeadersVariable rh
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
foreach ($r in $resources) { Write-Host ("  - {0}  ({1} bytes source)" -f $r.name, (Get-Item (Join-Path $scriptDir $r.file)).Length) }
Write-Host ''
Write-Host 'NEXT (UAT): fully reload Matter Management (appid=729afe6d-ca73-f011-b4cb-6045bdd8b757).' -ForegroundColor Yellow
Write-Host 'Expected: the Navigator pane auto-appears docked on the right at app load (no click).' -ForegroundColor Yellow
Write-Host 'If it does NOT auto-appear, the dormant ribbon rule needs a pinned control — fallback in notes/086-deploy-evidence.md.' -ForegroundColor DarkGray
