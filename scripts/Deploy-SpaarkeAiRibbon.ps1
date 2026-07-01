<#
.SYNOPSIS
    Deploy SpaarkeAi ribbon JS web resources to Dataverse.

.DESCRIPTION
    Uploads each dist-ribbon/*.js bundle as a Dataverse Script (JScript)
    web resource. The web resource name is derived deterministically from the
    bundle filename:

        dist-ribbon/DocumentComposeLaunch.js
            -> web resource: sprk_spaarkeai_documentcomposelaunch

    Matches the $webresource:sprk_spaarkeai_documentcomposelaunch references
    used in the ribbon XML at
    infrastructure/dataverse/ribbon/DocumentRibbons/opencompose-button.xml
    and its peers.

    Steps for each JS bundle:
      1. Verify the file exists under src/solutions/SpaarkeAi/dist-ribbon/.
      2. UPSERT the web resource (create if missing, PATCH content if present).
      3. Publish the web resource.

    Follows the same UPSERT + publish pattern as Deploy-SpaarkeAi.ps1.

.PARAMETER DataverseUrl
    Target Dataverse environment URL.
    Default: https://spaarkedev1.crm.dynamics.com

.EXAMPLE
    .\Deploy-SpaarkeAiRibbon.ps1
    # Deploy all dist-ribbon/*.js bundles to default dev environment

.EXAMPLE
    .\Deploy-SpaarkeAiRibbon.ps1 -DataverseUrl 'https://spaarke-prod.crm.dynamics.com'
    # Deploy to production
#>

param(
    [string]$DataverseUrl = 'https://spaarkedev1.crm.dynamics.com'
)
$ErrorActionPreference = 'Stop'
$AZ = 'C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd'

Write-Host '========================================='
Write-Host 'SpaarkeAi Ribbon Web Resource Deployment'
Write-Host "Target: $DataverseUrl"
Write-Host '========================================='

$scriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot   = Split-Path -Parent $scriptDir
$ribbonDir  = Join-Path $repoRoot 'src\solutions\SpaarkeAi\dist-ribbon'

# -- Discover bundles --
if (-not (Test-Path $ribbonDir)) {
    Write-Error "Ribbon output directory not found: $ribbonDir -- run 'npm run build:ribbon' in src/solutions/SpaarkeAi first"
    exit 1
}
$bundles = Get-ChildItem -Path $ribbonDir -Filter '*.js' -File
if ($bundles.Count -eq 0) {
    Write-Error "No .js bundles found under $ribbonDir -- run 'npm run build:ribbon' in src/solutions/SpaarkeAi first"
    exit 1
}
Write-Host "[1/3] Discovered $($bundles.Count) ribbon bundle(s):"
foreach ($b in $bundles) {
    $kb = [math]::Round($b.Length / 1KB)
    Write-Host ("       $($b.Name) ($kb KB)")
}

# -- Auth --
Write-Host '[2/3] Getting access token...'
$accessToken = & $AZ account get-access-token --resource $DataverseUrl --query accessToken -o tsv
if ([string]::IsNullOrEmpty($accessToken)) {
    Write-Error 'Failed to get access token. Run: az login'
    exit 1
}
Write-Host '       Token acquired' -ForegroundColor Green

$apiUrl  = "$DataverseUrl/api/data/v9.2"
$headers = @{
    'Authorization'    = "Bearer $accessToken"
    'Content-Type'     = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'Accept'           = 'application/json'
}

# -- Per-bundle UPSERT + collect ids for publish --
Write-Host '[3/3] Deploying each ribbon bundle...'
$webResourceIds = @()
foreach ($bundle in $bundles) {
    $baseName    = [System.IO.Path]::GetFileNameWithoutExtension($bundle.Name)
    $wrName      = ('sprk_spaarkeai_' + $baseName).ToLower()
    $displayName = "SpaarkeAi Ribbon - $baseName"
    $description = "SpaarkeAi ribbon script bundle: $baseName. Built from src/solutions/SpaarkeAi/src/ribbon/$baseName.ts via scripts/build-ribbon.mjs."

    $fileBytes   = [System.IO.File]::ReadAllBytes($bundle.FullName)
    $fileContent = [Convert]::ToBase64String($fileBytes)

    Write-Host "       - $wrName"
    $searchUrl      = "$apiUrl/webresourceset?`$filter=name eq '$wrName'"
    $searchResponse = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get

    if ($searchResponse.value.Count -eq 0) {
        $createBody = @{
            name            = $wrName
            displayname     = $displayName
            description     = $description
            webresourcetype = 3   # Script (JScript)
            content         = $fileContent
        } | ConvertTo-Json -Depth 5
        $createHeaders           = $headers.Clone()
        $createHeaders['Prefer'] = 'return=representation'
        $createResponse  = Invoke-WebRequest -Uri "$apiUrl/webresourceset" -Headers $createHeaders -Method Post -Body $createBody -UseBasicParsing
        $webResourceId   = ($createResponse.Content | ConvertFrom-Json).webresourceid
        Write-Host "         Created: $webResourceId" -ForegroundColor Green
    } else {
        $webResourceId = $searchResponse.value[0].webresourceid
        Invoke-RestMethod -Uri "$apiUrl/webresourceset($webResourceId)" -Headers $headers -Method Patch `
            -Body (@{ content = $fileContent } | ConvertTo-Json -Depth 5) | Out-Null
        Write-Host "         Updated: $webResourceId" -ForegroundColor Cyan
    }
    $webResourceIds += $webResourceId
}

# -- Publish all deployed web resources in one call --
Write-Host '       Publishing customizations...'
$wrElements = ($webResourceIds | ForEach-Object { "<webresource>{$_}</webresource>" }) -join ''
$publishXml = "<importexportxml><webresources>$wrElements</webresources></importexportxml>"
Invoke-RestMethod -Uri "$apiUrl/PublishXml" -Headers $headers -Method Post `
    -Body (@{ ParameterXml = $publishXml } | ConvertTo-Json) | Out-Null
Write-Host '       Published' -ForegroundColor Green

Write-Host '========================================='
Write-Host 'Ribbon Deployment Complete!' -ForegroundColor Green
Write-Host ("Deployed {0} bundle(s):" -f $bundles.Count)
foreach ($id in $webResourceIds) {
    Write-Host "  $id"
}
Write-Host '========================================='
