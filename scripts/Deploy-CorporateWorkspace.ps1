# Deploy CorporateWorkspace Web Resource to Dataverse
#
# RETIRED (2026-05-26, R4 task 041 / OC-R4-05):
# The standalone `sprk_corporateworkspace` web resource is no longer deployed.
# See: docs/architecture/LEGALWORKSPACE-RETIREMENT.md
# This script is preserved (not deleted) for history; the deploy body below is gated by
# an early-exit guard so callers fail gracefully (exit 0 with a clear log message).
# To force a deploy in an emergency rollback scenario, pass -ForceRetiredDeploy.

param(
    [string]$DataverseUrl = $env:DATAVERSE_URL,
    [switch]$ForceRetiredDeploy
)

# ---- Retirement guard --------------------------------------------------------
if (-not $ForceRetiredDeploy) {
    Write-Host ''
    Write-Host '====================================================================' -ForegroundColor Yellow
    Write-Host '  CorporateWorkspace Deploy SKIPPED — sprk_corporateworkspace RETIRED' -ForegroundColor Yellow
    Write-Host '====================================================================' -ForegroundColor Yellow
    Write-Host '  Per operator decision OC-R4-05 (2026-05-25), the standalone' -ForegroundColor Gray
    Write-Host '  LegalWorkspace code page is no longer deployed. LegalWorkspace' -ForegroundColor Gray
    Write-Host '  components remain available as a library, consumed by SpaarkeAi' -ForegroundColor Gray
    Write-Host '  via embedded mode.' -ForegroundColor Gray
    Write-Host ''
    Write-Host '  Retirement doc: docs/architecture/LEGALWORKSPACE-RETIREMENT.md' -ForegroundColor Cyan
    Write-Host '  To override (emergency rollback only): -ForceRetiredDeploy' -ForegroundColor Gray
    Write-Host '====================================================================' -ForegroundColor Yellow
    Write-Host ''
    exit 0
}

Write-Host ''
Write-Host '*** -ForceRetiredDeploy specified — proceeding with deploy of RETIRED resource ***' -ForegroundColor Red
Write-Host '*** This is an emergency-rollback path only. See LEGALWORKSPACE-RETIREMENT.md.   ***' -ForegroundColor Red
Write-Host ''

if (-not $DataverseUrl) {
    Write-Error "DataverseUrl is required. Set DATAVERSE_URL env var or pass -DataverseUrl parameter."
    exit 1
}

$ErrorActionPreference = 'Stop'
$orgUrl = $DataverseUrl

Write-Host '====================================='
Write-Host 'CorporateWorkspace Web Resource Deployment'
Write-Host '====================================='

# Get access token
Write-Host '[1/4] Getting access token...'
$accessToken = az account get-access-token --resource $orgUrl --query accessToken -o tsv
if ([string]::IsNullOrEmpty($accessToken)) {
    Write-Host 'Error: Failed to get access token' -ForegroundColor Red
    exit 1
}
Write-Host '      Token acquired' -ForegroundColor Green

$apiUrl = "$orgUrl/api/data/v9.2"
$headers = @{
    'Authorization' = "Bearer $accessToken"
    'Content-Type' = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version' = '4.0'
    'Accept' = 'application/json'
}

# Find web resource
Write-Host '[2/4] Finding sprk_corporateworkspace.html...'
$wrName = 'sprk_corporateworkspace'
$searchUrl = "$apiUrl/webresourceset?`$filter=name eq '$wrName'"
$exactResponse = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get

if ($exactResponse.value.Count -eq 0) {
    Write-Host "Web resource $wrName not found!" -ForegroundColor Red
    exit 1
}

$webResourceId = $exactResponse.value[0].webresourceid
Write-Host "      Found: $webResourceId" -ForegroundColor Green

# Update
Write-Host '[3/4] Updating web resource...'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$filePath = Join-Path $repoRoot 'src\solutions\LegalWorkspace\dist\corporateworkspace.html'
$fileBytes = [System.IO.File]::ReadAllBytes($filePath)
$fileContent = [Convert]::ToBase64String($fileBytes)

$updateUrl = "$apiUrl/webresourceset($webResourceId)"
$updateBody = @{ content = $fileContent } | ConvertTo-Json
Invoke-RestMethod -Uri $updateUrl -Headers $headers -Method Patch -Body $updateBody | Out-Null
Write-Host '      Updated' -ForegroundColor Green

# Publish
Write-Host '[4/4] Publishing...'
$publishUrl = "$apiUrl/PublishXml"
$publishXml = "<importexportxml><webresources><webresource>{$webResourceId}</webresource></webresources></importexportxml>"
$publishBody = @{ ParameterXml = $publishXml } | ConvertTo-Json
Invoke-RestMethod -Uri $publishUrl -Headers $headers -Method Post -Body $publishBody | Out-Null
Write-Host '      Published' -ForegroundColor Green

Write-Host '====================================='
Write-Host 'Deployment Complete!' -ForegroundColor Green
Write-Host '====================================='
$fileSize = [math]::Round((Get-Item $filePath).Length / 1KB)
Write-Host "Bundle Size: $fileSize KB"
