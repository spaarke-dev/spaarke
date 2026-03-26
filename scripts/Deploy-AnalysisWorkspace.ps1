param(
    [string]$DataverseUrl = 'https://spaarkedev1.crm.dynamics.com'
)
$ErrorActionPreference = 'Stop'
$AZ = 'C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd'

Write-Host '========================================='
Write-Host 'Analysis Workspace Web Resource Deployment'
Write-Host "Target: $DataverseUrl"
Write-Host '========================================='

# -- Step 1: Verify build artifact --
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot  = Split-Path -Parent $scriptDir
$filePath  = Join-Path $repoRoot 'src\client\code-pages\AnalysisWorkspace\out\sprk_analysisworkspace.html'

Write-Host '[1/4] Verifying build artifact...'
if (-not (Test-Path $filePath)) {
    Write-Error "Build artifact not found: $filePath -- run npm run build + build-webresource.ps1 first"
    exit 1
}
$fileBytes  = [System.IO.File]::ReadAllBytes($filePath)
$fileSizeKb = [math]::Round($fileBytes.Length / 1KB)
Write-Host "      Found: sprk_analysisworkspace.html ($fileSizeKb KB)" -ForegroundColor Green

# -- Step 2: Get access token --
Write-Host '[2/4] Getting access token...'
$accessToken = & $AZ account get-access-token --resource $DataverseUrl --query accessToken -o tsv
if ([string]::IsNullOrEmpty($accessToken)) {
    Write-Error 'Failed to get access token. Run: az login'
    exit 1
}
Write-Host '      Token acquired' -ForegroundColor Green

$apiUrl  = "$DataverseUrl/api/data/v9.2"
$headers = @{
    'Authorization'    = "Bearer $accessToken"
    'Content-Type'     = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'Accept'           = 'application/json'
}
$fileContent = [Convert]::ToBase64String($fileBytes)

# -- Step 3: Create or update web resource --
$wrName = 'sprk_analysisworkspace'
Write-Host "[3/4] Checking for existing $wrName..."
$searchUrl      = "$apiUrl/webresourceset?`$filter=name eq '$wrName'"
$searchResponse = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get

if ($searchResponse.value.Count -eq 0) {
    Write-Host '      Not found - creating...' -ForegroundColor Yellow
    $createBody = @{
        name            = $wrName
        displayname     = 'Analysis Workspace'
        description     = 'Unified Analysis Workspace with embedded SprkChat panel.'
        webresourcetype = 1
        content         = $fileContent
    } | ConvertTo-Json -Depth 5
    $createHeaders         = $headers.Clone()
    $createHeaders['Prefer'] = 'return=representation'
    $createResponse = Invoke-WebRequest -Uri "$apiUrl/webresourceset" -Headers $createHeaders -Method Post -Body $createBody -UseBasicParsing
    $webResourceId  = ($createResponse.Content | ConvertFrom-Json).webresourceid
    Write-Host "      Created: $webResourceId" -ForegroundColor Green
} else {
    $webResourceId = $searchResponse.value[0].webresourceid
    Write-Host "      Found existing: $webResourceId" -ForegroundColor Green
    Invoke-RestMethod -Uri "$apiUrl/webresourceset($webResourceId)" -Headers $headers -Method Patch `
        -Body (@{ content = $fileContent } | ConvertTo-Json -Depth 5) | Out-Null
    Write-Host '      Updated' -ForegroundColor Green
}

# -- Step 4: Publish --
Write-Host '[4/4] Publishing customizations...'
$publishXml = "<importexportxml><webresources><webresource>{$webResourceId}</webresource></webresources></importexportxml>"
Invoke-RestMethod -Uri "$apiUrl/PublishXml" -Headers $headers -Method Post `
    -Body (@{ ParameterXml = $publishXml } | ConvertTo-Json) | Out-Null
Write-Host '      Published' -ForegroundColor Green

Write-Host '========================================='
Write-Host 'Deployment Complete!' -ForegroundColor Green
Write-Host "Web Resource : $wrName"
Write-Host "Resource ID  : $webResourceId"
Write-Host "Bundle Size  : $fileSizeKb KB"
Write-Host '========================================='
