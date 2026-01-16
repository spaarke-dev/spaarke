<#
.SYNOPSIS
    Deploy SVG ribbon icons as web resources to Dataverse

.DESCRIPTION
    Deploys the Refresh, Open in Web, and Open in Desktop SVG icons
    as web resources for use by Document ribbon buttons.

.NOTES
    Requires: PAC CLI authenticated to Dataverse
    Version: 1.0.0
#>

param(
    [string]$SolutionName = "spaarke_core"
)

$ErrorActionPreference = "Stop"

Write-Host "`n=== Deploy Ribbon Icons to Dataverse ===" -ForegroundColor Cyan

# Define icons to deploy
$icons = @(
    @{
        Name = "sprk_RefreshIcon"
        DisplayName = "Refresh Icon (SVG)"
        FilePath = "src/client/assets/icons/sprk_RefreshIcon.svg"
        Type = 11  # SVG
    },
    @{
        Name = "sprk_OpenInWebIcon"
        DisplayName = "Open in Web Icon (SVG)"
        FilePath = "src/client/assets/icons/sprk_OpenInWebIcon.svg"
        Type = 11  # SVG
    },
    @{
        Name = "sprk_OpenInDesktopIcon"
        DisplayName = "Open in Desktop Icon (SVG)"
        FilePath = "src/client/assets/icons/sprk_OpenInDesktopIcon.svg"
        Type = 11  # SVG
    }
)

# Get PAC auth token for REST API calls
Write-Host "`nGetting authentication token..." -ForegroundColor Yellow

# Use pac org export to get the org URL
$orgInfo = pac org who 2>&1 | Out-String
if ($orgInfo -match "Org URL:\s+(https://[^\s]+)") {
    $orgUrl = $Matches[1].TrimEnd("/")
    Write-Host "Organization URL: $orgUrl" -ForegroundColor Green
} else {
    throw "Could not determine org URL from 'pac org who'"
}

# Get access token using Azure CLI or PAC
# PAC CLI stores auth internally, we need to use it for API calls
# Alternative: Use pac data import/export which handles auth

Write-Host "`nDeploying icons using PAC solution push..." -ForegroundColor Yellow

# For each icon, we'll create a simple solution structure and push it
foreach ($icon in $icons) {
    $iconPath = Join-Path $PSScriptRoot ".." $icon.FilePath

    if (-not (Test-Path $iconPath)) {
        Write-Host "  [SKIP] $($icon.Name) - File not found: $iconPath" -ForegroundColor Red
        continue
    }

    Write-Host "  Deploying $($icon.Name)..." -ForegroundColor White

    # Read the SVG content
    $svgContent = Get-Content $iconPath -Raw
    $svgBase64 = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($svgContent))

    # Create a temporary JSON file for pac data create
    $tempDir = Join-Path $env:TEMP "ribbon-icons"
    if (-not (Test-Path $tempDir)) {
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
    }

    # For web resources, we need to use the Dataverse API
    # PAC CLI doesn't have a direct "create web resource" command
    # We'll use pac data create with the webresource entity

    $webResourceData = @{
        name = $icon.Name
        displayname = $icon.DisplayName
        webresourcetype = $icon.Type
        content = $svgBase64
        ismanaged = $false
        iscustomizable = @{
            Value = $true
        }
    }

    $jsonFile = Join-Path $tempDir "$($icon.Name).json"
    $webResourceData | ConvertTo-Json -Depth 10 | Set-Content $jsonFile -Encoding UTF8

    Write-Host "    Created JSON: $jsonFile" -ForegroundColor Gray
}

Write-Host "`n[INFO] Web resources need to be created manually or via solution import." -ForegroundColor Yellow
Write-Host "[INFO] The RibbonDiff.xml references these web resources:" -ForegroundColor Yellow
foreach ($icon in $icons) {
    Write-Host "  - `$webresource:$($icon.Name).svg" -ForegroundColor White
}

Write-Host "`n[NEXT STEPS]:" -ForegroundColor Cyan
Write-Host "1. Open Power Apps maker portal: $orgUrl" -ForegroundColor White
Write-Host "2. Navigate to Solutions > spaarke_core (or Default)" -ForegroundColor White
Write-Host "3. Add New > More > Web Resource for each SVG icon" -ForegroundColor White
Write-Host "4. Or import a solution containing these web resources" -ForegroundColor White

Write-Host "`n=== Script Complete ===" -ForegroundColor Cyan
