# Deploy EventsPage Web Resource to Dataverse
$ErrorActionPreference = 'Stop'
$orgUrl = 'https://spaarkedev1.crm.dynamics.com'

Write-Host '====================================='
Write-Host 'EventsPage Web Resource Deployment'
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
Write-Host '[2/4] Finding sprk_eventspage.html...'
$wrName = 'sprk_eventspage.html'
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
$filePath = Join-Path $repoRoot 'src\solutions\EventsPage\dist\index.html'
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
