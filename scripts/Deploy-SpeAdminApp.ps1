# Deploy SPE Admin App Web Resource to Dataverse
# Uploads src/solutions/SpeAdminApp/dist/speadmin.html as sprk_speadmin
# Creates the web resource if it does not exist, updates it if it does.
#
# SYNOPSIS
#   Deploy-SpeAdminApp.ps1
# PARAMETERS
#   None — uses fixed dev environment URL
# DEPENDENCIES
#   Azure CLI (az login), Dataverse connection, speadmin.html built artifact
# USAGE
#   .\Deploy-SpeAdminApp.ps1
#   (from scripts/ directory or repo root via: .\scripts\Deploy-SpeAdminApp.ps1)
$ErrorActionPreference = 'Stop'
$orgUrl = 'https://spaarkedev1.crm.dynamics.com'

Write-Host '====================================='
Write-Host 'SPE Admin App Web Resource Deployment'
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
    'Authorization'    = "Bearer $accessToken"
    'Content-Type'     = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'Accept'           = 'application/json'
}

# Read and base64-encode the HTML file
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot  = Split-Path -Parent $scriptDir
$filePath  = Join-Path $repoRoot 'src\solutions\SpeAdminApp\dist\speadmin.html'

if (-not (Test-Path $filePath)) {
    Write-Host "Error: Build artifact not found: $filePath" -ForegroundColor Red
    Write-Host "Run 'npm run build' in src/solutions/SpeAdminApp first." -ForegroundColor Yellow
    exit 1
}

$fileBytes   = [System.IO.File]::ReadAllBytes($filePath)
$fileContent = [Convert]::ToBase64String($fileBytes)
$fileSizeKb  = [math]::Round($fileBytes.Length / 1KB)

# Check if web resource exists
Write-Host '[2/4] Checking for existing sprk_speadmin web resource...'
$wrName    = 'sprk_speadmin'
$searchUrl = "$apiUrl/webresourceset?`$filter=name eq '$wrName'"
$searchResponse = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get

if ($searchResponse.value.Count -eq 0) {
    # CREATE — web resource does not exist yet
    Write-Host '      Not found — creating new web resource...' -ForegroundColor Yellow
    $createUrl  = "$apiUrl/webresourceset"
    $createBody = @{
        name            = $wrName
        displayname     = 'SPE Admin App'
        description     = 'SharePoint Embedded administration application. Manages environments, container type configurations, and audit logs.'
        webresourcetype = 1   # 1 = Webpage (HTML)
        content         = $fileContent
    } | ConvertTo-Json -Depth 5

    $createHeaders = $headers.Clone()
    $createHeaders['Prefer'] = 'return=representation'

    # Use Invoke-WebRequest to reliably capture the 201 response body
    $createResponse = Invoke-WebRequest -Uri $createUrl -Headers $createHeaders -Method Post -Body $createBody -UseBasicParsing
    $createJson = $createResponse.Content | ConvertFrom-Json
    $webResourceId = $createJson.webresourceid
    Write-Host "      Created: $webResourceId" -ForegroundColor Green
} else {
    # UPDATE — web resource already exists
    $webResourceId = $searchResponse.value[0].webresourceid
    Write-Host "      Found existing: $webResourceId" -ForegroundColor Green

    Write-Host '[3/4] Updating web resource content...'
    $updateUrl  = "$apiUrl/webresourceset($webResourceId)"
    $updateBody = @{ content = $fileContent } | ConvertTo-Json -Depth 5
    Invoke-RestMethod -Uri $updateUrl -Headers $headers -Method Patch -Body $updateBody | Out-Null
    Write-Host '      Updated' -ForegroundColor Green
}

# Publish
Write-Host '[4/4] Publishing customizations...'
$publishUrl  = "$apiUrl/PublishXml"
$publishXml  = "<importexportxml><webresources><webresource>{$webResourceId}</webresource></webresources></importexportxml>"
$publishBody = @{ ParameterXml = $publishXml } | ConvertTo-Json
Invoke-RestMethod -Uri $publishUrl -Headers $headers -Method Post -Body $publishBody | Out-Null
Write-Host '      Published' -ForegroundColor Green

Write-Host '====================================='
Write-Host 'Deployment Complete!' -ForegroundColor Green
Write-Host '====================================='
Write-Host "Web Resource Name : $wrName"
Write-Host "Web Resource ID   : $webResourceId"
Write-Host "Bundle Size       : $fileSizeKb KB"
Write-Host ''
Write-Host 'Access URL (relative to Dataverse org):'
Write-Host "  /WebResources/$wrName" -ForegroundColor Cyan
