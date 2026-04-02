<#
.SYNOPSIS
    Complete deployment of the Spaarke M365 Copilot agent to a target environment.
    Runs all configuration steps in sequence: entity descriptions, glossary,
    synonyms, and generates the Teams app package ready for upload.

.DESCRIPTION
    This script is the single entry point for deploying the Copilot agent
    to any Spaarke environment. It orchestrates:
    1. Dataverse entity descriptions - teaches Copilot the data model
    2. Copilot glossary terms and synonyms - maps user vocabulary
    3. Teams app package generation - ready for upload to org catalog

    Include this in the deployment runbook for every new environment.

.PARAMETER EnvironmentUrl
    Target Dataverse environment URL.
    Default: https://spaarkedev1.crm.dynamics.com

.PARAMETER BffApiUrl
    BFF API base URL for the target environment.
    Default: https://spe-api-dev-67e2xz.azurewebsites.net

.PARAMETER BotAppId
    Entra app registration ID for the Copilot Bot in this environment.
    Default: f257a0a9-1061-4f9b-8918-3ad056fe90db

.PARAMETER BffAppId
    BFF API Entra app registration ID.
    Default: 1e40baad-e065-4aea-a8d4-4b7ab273458c

.PARAMETER Version
    App version for the Teams manifest. Must be incremented for updates.
    Default: 1.0.0

.PARAMETER OutputPath
    Path for the generated Teams app package ZIP.
    Default: ./spaarke-copilot-agent.zip

.PARAMETER SkipDataverse
    Skip Dataverse configuration steps - useful when repackaging only.

.EXAMPLE
    .\Deploy-CopilotAgent.ps1
    .\Deploy-CopilotAgent.ps1 -EnvironmentUrl "https://customer.crm.dynamics.com" -Version "1.0.5"
    .\Deploy-CopilotAgent.ps1 -SkipDataverse -Version "1.0.6"
#>

param(
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",
    [string]$BffApiUrl = "https://spe-api-dev-67e2xz.azurewebsites.net",
    [string]$BotAppId = "f257a0a9-1061-4f9b-8918-3ad056fe90db",
    [string]$BffAppId = "1e40baad-e065-4aea-a8d4-4b7ab273458c",
    [string]$Version = "1.0.0",
    [string]$OutputPath = "./spaarke-copilot-agent.zip",
    [switch]$SkipDataverse
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$copilotDir = Join-Path $repoRoot "src/solutions/CopilotAgent"

Write-Host "`n================================================================" -ForegroundColor Cyan
Write-Host "  Spaarke Copilot Agent Deployment" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  Environment:  $EnvironmentUrl"
Write-Host "  BFF API:      $BffApiUrl"
Write-Host "  Bot App ID:   $BotAppId"
Write-Host "  BFF App ID:   $BffAppId"
Write-Host "  Version:      $Version"
Write-Host "  Output:       $OutputPath"
Write-Host "================================================================`n"

# ============================================================================
# STEP 1: Configure Dataverse for Copilot
# ============================================================================

if (-not $SkipDataverse) {
    Write-Host "[1/4] Configuring Dataverse entity descriptions..." -ForegroundColor Cyan
    & "$scriptDir/Update-CopilotEntityDescriptions.ps1" -EnvironmentUrl $EnvironmentUrl
    if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne $null) {
        Write-Host "  Warning: Entity description update had errors - continuing" -ForegroundColor Yellow
    }

    Write-Host "`n[2/4] Configuring Copilot glossary and synonyms..." -ForegroundColor Cyan
    $glossaryScript = Join-Path $scriptDir "Configure-CopilotKnowledge.ps1"
    if (Test-Path $glossaryScript) {
        & $glossaryScript -EnvironmentUrl $EnvironmentUrl
        if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne $null) {
            Write-Host "  Warning: Glossary configuration had errors - continuing" -ForegroundColor Yellow
        }
    } else {
        Write-Host "  Skipped: Configure-CopilotKnowledge.ps1 not found" -ForegroundColor Yellow
    }
} else {
    Write-Host "[1/4] Skipped: Dataverse configuration" -ForegroundColor Yellow
    Write-Host "[2/4] Skipped: Copilot glossary and synonyms" -ForegroundColor Yellow
}

# ============================================================================
# STEP 2: Generate Teams App Package
# ============================================================================

Write-Host "`n[3/4] Building Teams app package..." -ForegroundColor Cyan

$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) "copilot-package-$(Get-Date -Format 'yyyyMMddHHmmss')"
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

# Copy manifest and update placeholders
$manifest = Get-Content (Join-Path $copilotDir "appPackage/manifest.json") -Raw | ConvertFrom-Json
$manifest.id = $BotAppId
$manifest.version = $Version
$manifest.validDomains = @(
    ($BffApiUrl -replace "https://", ""),
    ($EnvironmentUrl -replace "https://", "")
)
if ($manifest.webApplicationInfo) {
    $manifest.webApplicationInfo.id = $BffAppId
    $manifest.webApplicationInfo.resource = "api://$BffAppId"
}
# Write without BOM — Teams Admin Center rejects BOM-prefixed JSON
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText((Join-Path $tempDir "manifest.json"), ($manifest | ConvertTo-Json -Depth 10), $utf8NoBom)

# Copy agent files
Copy-Item (Join-Path $copilotDir "declarativeAgent.json") $tempDir
Copy-Item (Join-Path $copilotDir "spaarke-api-plugin.json") $tempDir

# Update OpenAPI spec server URL
$openapiContent = Get-Content (Join-Path $copilotDir "spaarke-bff-openapi.yaml") -Raw
$openapiContent = $openapiContent -replace "https://spe-api-dev-67e2xz\.azurewebsites\.net", $BffApiUrl
Set-Content (Join-Path $tempDir "spaarke-bff-openapi.yaml") $openapiContent -Encoding UTF8

# Generate icons if not present
$colorIconPath = Join-Path $tempDir "color.png"
$outlineIconPath = Join-Path $tempDir "outline.png"

if (-not (Test-Path (Join-Path $copilotDir "appPackage/color.png"))) {
    Add-Type -AssemblyName System.Drawing
    $bmp = New-Object System.Drawing.Bitmap 192, 192
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::FromArgb(79,107,237))
    $font = New-Object System.Drawing.Font 'Arial', 72, ([System.Drawing.FontStyle]::Bold)
    $g.DrawString('S', $font, [System.Drawing.Brushes]::White, 50, 45)
    $g.Dispose(); $font.Dispose()
    $bmp.Save($colorIconPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
} else {
    Copy-Item (Join-Path $copilotDir "appPackage/color.png") $colorIconPath
}

if (-not (Test-Path (Join-Path $copilotDir "appPackage/outline.png"))) {
    Add-Type -AssemblyName System.Drawing
    $bmp = New-Object System.Drawing.Bitmap 32, 32, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    for ($x = 0; $x -lt 32; $x++) { for ($y = 0; $y -lt 32; $y++) { $bmp.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(0,0,0,0)) } }
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 2
    $g.DrawRectangle($pen, 2, 2, 27, 27)
    $font = New-Object System.Drawing.Font 'Arial', 16, ([System.Drawing.FontStyle]::Bold)
    $g.DrawString('S', $font, [System.Drawing.Brushes]::White, 6, 3)
    $g.Dispose(); $pen.Dispose(); $font.Dispose()
    $bmp.Save($outlineIconPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
} else {
    Copy-Item (Join-Path $copilotDir "appPackage/outline.png") $outlineIconPath
}

# Create ZIP
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
if (Test-Path $resolvedOutput) { Remove-Item $resolvedOutput -Force }
Compress-Archive -Path "$tempDir/*" -DestinationPath $resolvedOutput -Force

# Cleanup
Remove-Item $tempDir -Recurse -Force

$fileCount = (Get-ChildItem $tempDir -ErrorAction SilentlyContinue).Count
Write-Host "  Package created: $resolvedOutput" -ForegroundColor Green
Write-Host "  Version: $Version"

# ============================================================================
# STEP 3: Instructions
# ============================================================================

Write-Host "`n[4/4] Next steps..." -ForegroundColor Cyan
Write-Host ""
Write-Host "  Upload the package to Teams Admin Center:"
Write-Host "    https://admin.teams.microsoft.com/policies/manage-apps"
Write-Host ""
Write-Host "  For updates: find 'Spaarke AI' > Upload file > select $resolvedOutput"
Write-Host "  For new installs: Upload new app > select $resolvedOutput"
Write-Host ""
Write-Host "  After upload, configure in Copilot Studio:"
Write-Host "    1. Open MDA app in App Designer"
Write-Host "    2. Click '...' > 'Configure in Copilot Studio'"
Write-Host "    3. Add Dataverse tables as knowledge sources"
Write-Host "    4. Add glossary terms and synonyms"
Write-Host "    5. Publish the agent"
Write-Host ""
Write-Host "  See: docs/guides/COPILOT-KNOWLEDGE-CONFIGURATION-GUIDE.md"
Write-Host ""

Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  Deployment Complete" -ForegroundColor Cyan
Write-Host "  Package: $resolvedOutput" -ForegroundColor Cyan
Write-Host "  Version: $Version" -ForegroundColor Cyan
Write-Host "================================================================`n" -ForegroundColor Cyan
