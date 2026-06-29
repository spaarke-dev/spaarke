<#
.SYNOPSIS
    Deploy PlaybookBuilder web resource to Dataverse.

.DESCRIPTION
    Uploads the PlaybookBuilder Webpack code page (single self-contained HTML)
    as web resource sprk_playbookbuilder via Dataverse Web API + publishes.

    Steps:
    1. Verify build artifact (out/sprk_playbookbuilder.html — produced by
       `npm run build && pwsh -File build-webresource.ps1` inside the code-page dir)
    2. Get access token for Dataverse via Azure CLI
    3. Create or update the sprk_playbookbuilder web resource
    4. Publish customizations

    Mirrors Deploy-SpaarkeAi.ps1 exactly — same upsert + publish pattern.
    Created during R7 Wave 8 deploys (2026-06-29) to fill the gap left by the
    code-page-deploy skill's docs claim that Webpack code pages need manual
    maker-portal upload. PAC CLI lacks `pac webresource update`, but Web API
    PATCH on /webresourceset/{id}/content works identically.

.PARAMETER DataverseUrl
    Target Dataverse environment URL.
    Default: https://spaarkedev1.crm.dynamics.com

.EXAMPLE
    .\Deploy-PlaybookBuilder.ps1
    # Deploy to default dev environment

.EXAMPLE
    .\Deploy-PlaybookBuilder.ps1 -DataverseUrl 'https://spaarke-prod.crm.dynamics.com'
    # Deploy to production
#>

param(
    [string]$DataverseUrl = 'https://spaarkedev1.crm.dynamics.com'
)
$ErrorActionPreference = 'Stop'
$AZ = 'C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd'

Write-Host '========================================='
Write-Host 'PlaybookBuilder Web Resource Deployment'
Write-Host "Target: $DataverseUrl"
Write-Host '========================================='

# -- Step 1: Verify build artifact --
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot  = Split-Path -Parent $scriptDir
$filePath  = Join-Path $repoRoot 'src\client\code-pages\PlaybookBuilder\out\sprk_playbookbuilder.html'

Write-Host '[1/4] Verifying build artifact...'
if (-not (Test-Path $filePath)) {
    Write-Error "Build artifact not found: $filePath -- run 'npm run build && pwsh -File build-webresource.ps1' in src/client/code-pages/PlaybookBuilder first"
    exit 1
}
$fileBytes  = [System.IO.File]::ReadAllBytes($filePath)
$fileSizeKb = [math]::Round($fileBytes.Length / 1KB)
Write-Host "      Found: sprk_playbookbuilder.html ($fileSizeKb KB)" -ForegroundColor Green

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
$wrName = 'sprk_playbookbuilder'
Write-Host "[3/4] Checking for existing $wrName..."
$searchUrl      = "$apiUrl/webresourceset?`$filter=name eq '$wrName'"
$searchResponse = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get

if ($searchResponse.value.Count -eq 0) {
    Write-Host '      Not found - creating...' -ForegroundColor Yellow
    $createBody = @{
        name            = $wrName
        displayname     = 'Playbook Builder'
        description     = 'AI Playbook visual node builder — R7 single-hop dispatch (33-executor selector, typed config forms, UnknownNode warning, Action tab).'
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
