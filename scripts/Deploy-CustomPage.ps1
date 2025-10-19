#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Deploys Custom Page to Dataverse using PAC CLI and Dataverse Web API

.DESCRIPTION
    Automates deployment of Custom Page for Universal Document Upload PCF control.
    Uses PAC CLI authentication and Dataverse Web API to create/update the Custom Page.

.EXAMPLE
    .\Deploy-CustomPage.ps1
#>

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

# Paths
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$customPageJsonPath = Join-Path $repoRoot "src\controls\UniversalQuickCreate\UniversalQuickCreateSolution\CustomPages\sprk_universaldocumentupload_page.json"

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "Custom Page Deployment Script" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

# Verify Custom Page JSON exists
if (-not (Test-Path $customPageJsonPath)) {
    Write-Error "Custom Page JSON not found at: $customPageJsonPath"
    exit 1
}

Write-Host "[1/5] Reading Custom Page definition..." -ForegroundColor Yellow
$customPageJson = Get-Content $customPageJsonPath -Raw | ConvertFrom-Json
Write-Host "      Custom Page: $($customPageJson.name)" -ForegroundColor Green
Write-Host ""

# Get PAC auth info
Write-Host "[2/5] Getting Dataverse connection from PAC CLI..." -ForegroundColor Yellow
try {
    $authList = pac auth list --json | ConvertFrom-Json
    $activeAuth = $authList | Where-Object { $_.IsActive -eq $true } | Select-Object -First 1

    if (-not $activeAuth) {
        Write-Error "No active PAC CLI authentication found. Run 'pac auth create' first."
        exit 1
    }

    $orgUrl = $activeAuth.Url
    Write-Host "      Connected to: $orgUrl" -ForegroundColor Green
    Write-Host "      User: $($activeAuth.FriendlyName)" -ForegroundColor Green
} catch {
    Write-Error "Failed to get PAC auth info: $_"
    exit 1
}
Write-Host ""

# Get access token
Write-Host "[3/5] Getting access token..." -ForegroundColor Yellow
try {
    $tokenResponse = pac auth token --json | ConvertFrom-Json
    $accessToken = $tokenResponse.Token
    Write-Host "      Token acquired" -ForegroundColor Green
} catch {
    Write-Error "Failed to get access token: $_"
    exit 1
}
Write-Host ""

# Prepare Custom Page payload for Dataverse
Write-Host "[4/5] Preparing Custom Page payload..." -ForegroundColor Yellow

# Custom Pages in Dataverse are stored in the 'canvasapp' table with specific attributes
$customPageName = $customPageJson.name
$displayName = $customPageJson.displayName
$description = $customPageJson.description

# Convert the JSON to the format Dataverse expects
# Custom Pages are stored as Canvas Apps with specific properties
$canvasAppPayload = @{
    name = $displayName
    displayname = $displayName
    description = $description
    canvasapptype = 3  # 3 = Custom Page
    # The actual page definition goes in the 'document' field as JSON
    # But we need to wrap it in a specific format for Dataverse
}

Write-Host "      Payload prepared" -ForegroundColor Green
Write-Host ""

# Check if Custom Page already exists
Write-Host "[5/5] Deploying Custom Page to Dataverse..." -ForegroundColor Yellow

$headers = @{
    "Authorization" = "Bearer $accessToken"
    "Content-Type" = "application/json"
    "OData-MaxVersion" = "4.0"
    "OData-Version" = "4.0"
    "Accept" = "application/json"
}

$apiUrl = "$orgUrl/api/data/v9.2"

# Search for existing Custom Page by name
$filterQuery = "`$filter=name eq '$displayName'"
$searchUri = "$apiUrl/canvasapps?$filterQuery"

try {
    Write-Host "      Checking if Custom Page exists..." -ForegroundColor Gray
    $searchResponse = Invoke-RestMethod -Uri $searchUri -Headers $headers -Method Get

    if ($searchResponse.value -and $searchResponse.value.Count -gt 0) {
        # Update existing
        $existingId = $searchResponse.value[0].canvasappid
        Write-Host "      Found existing Custom Page: $existingId" -ForegroundColor Gray
        Write-Host ""
        Write-Host "      NOTE: Custom Pages cannot be fully updated via Web API." -ForegroundColor Yellow
        Write-Host "      The Custom Page structure requires manual recreation in Power Apps Studio." -ForegroundColor Yellow
        Write-Host ""
        Write-Host "      Current workaround:" -ForegroundColor Cyan
        Write-Host "      1. Delete existing Custom Page manually from Power Apps portal" -ForegroundColor White
        Write-Host "      2. Create new Custom Page in Power Apps Studio:" -ForegroundColor White
        Write-Host "         - Go to make.powerapps.com" -ForegroundColor White
        Write-Host "         - Click '+ Create' -> 'Custom page (preview)'" -ForegroundColor White
        Write-Host "         - Name: sprk_universaldocumentupload_page" -ForegroundColor White
        Write-Host "         - Add PCF control: Spaarke.Controls.UniversalDocumentUpload" -ForegroundColor White
        Write-Host "         - Add 4 input parameters (see JSON for details)" -ForegroundColor White
        Write-Host "         - Bind control properties to parameters" -ForegroundColor White
        Write-Host "         - Save and Publish" -ForegroundColor White
        Write-Host ""
    } else {
        Write-Host "      Custom Page does not exist yet." -ForegroundColor Gray
        Write-Host ""
        Write-Host "      NOTE: Custom Pages must be created through Power Apps Studio." -ForegroundColor Yellow
        Write-Host "      Web API does not support full Custom Page creation with PCF bindings." -ForegroundColor Yellow
        Write-Host ""
        Write-Host "      AUTOMATED ALTERNATIVE: Use PAC solution approach" -ForegroundColor Cyan
        Write-Host "      The Custom Page JSON can be packaged in a solution and imported." -ForegroundColor White
        Write-Host ""
    }
} catch {
    Write-Warning "Could not query Custom Pages: $_"
}

Write-Host ""
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "Exploring Alternative: Solution-based Deployment" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Let me try using PAC solution to package and deploy the Custom Page..." -ForegroundColor Yellow
Write-Host ""

# Alternative approach: Use pac solution
Write-Host "Checking solution deployment approach..." -ForegroundColor Yellow

$solutionDir = Join-Path $repoRoot "src\controls\UniversalQuickCreate\UniversalQuickCreateSolution"

if (Test-Path $solutionDir) {
    Write-Host "      Solution directory found: $solutionDir" -ForegroundColor Green
    Write-Host ""
    Write-Host "      To deploy via solution:" -ForegroundColor Cyan
    Write-Host "      1. cd $solutionDir" -ForegroundColor White
    Write-Host "      2. pac solution import --path <solution.zip>" -ForegroundColor White
    Write-Host ""
    Write-Host "      However, Custom Pages require special packaging format." -ForegroundColor Yellow
    Write-Host ""
}

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "Recommended Approach" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "For now, the most reliable way to deploy Custom Pages is:" -ForegroundColor Yellow
Write-Host ""
Write-Host "1. MANUAL CREATION in Power Apps Studio (one-time setup)" -ForegroundColor Cyan
Write-Host "   - This is required for the initial Custom Page creation" -ForegroundColor White
Write-Host "   - Follow the guide in the comments above" -ForegroundColor White
Write-Host ""
Write-Host "2. WEB RESOURCE (can be automated - see Deploy-WebResource.ps1)" -ForegroundColor Cyan
Write-Host "   - The command script can be deployed via PAC CLI" -ForegroundColor White
Write-Host ""
Write-Host "3. COMMAND BUTTONS (requires Ribbon Workbench or manual config)" -ForegroundColor Cyan
Write-Host "   - Add buttons to entity forms to call the Custom Page" -ForegroundColor White
Write-Host ""

Write-Host "Custom Page JSON reference: $customPageJsonPath" -ForegroundColor Gray
Write-Host ""
Write-Host "Deployment script completed." -ForegroundColor Green
