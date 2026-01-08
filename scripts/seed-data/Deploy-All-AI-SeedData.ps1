# Deploy-All-AI-SeedData.ps1
# Master script to deploy all AI seed data in correct dependency order
#
# Usage:
#   .\Deploy-All-AI-SeedData.ps1
#   .\Deploy-All-AI-SeedData.ps1 -DryRun
#   .\Deploy-All-AI-SeedData.ps1 -Force   # Recreate existing records
#
# Deployment Order (per playbooks.json):
#   1. Type Lookups (action types, tool types, skill types, knowledge types)
#   2. Actions (analysis actions like Extract Entities, Classify Document)
#   3. Tools (AI tools like Entity Extractor, Document Classifier)
#   4. Knowledge (knowledge sources like Standard Contract Terms, Risk Categories)
#   5. Skills (AI skills like Contract Analysis, Risk Assessment)
#   6. Playbooks (complete analysis workflows)
#   7. Output Types (field mappings for Document Profile playbook)

param(
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",
    [switch]$DryRun = $false,
    [switch]$Force = $false,
    [switch]$SkipVerification = $false
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  AI Seed Data Deployment" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl"
if ($DryRun) {
    Write-Host "Mode: DRY RUN (no changes will be made)" -ForegroundColor Yellow
} else {
    Write-Host "Mode: LIVE DEPLOYMENT" -ForegroundColor Green
}
if ($Force) {
    Write-Host "Force: ENABLED (will recreate existing records)" -ForegroundColor Yellow
}
Write-Host ""

# Verify prerequisites
Write-Host "Checking prerequisites..." -ForegroundColor Gray

# Check if Azure CLI is logged in
try {
    $account = az account show 2>&1 | ConvertFrom-Json
    Write-Host "  ✓ Azure CLI authenticated as: $($account.user.name)" -ForegroundColor Green
} catch {
    Write-Host "  ✗ Azure CLI not authenticated" -ForegroundColor Red
    Write-Host "    Run: az login" -ForegroundColor Yellow
    exit 1
}

# Check if all JSON files exist
$requiredFiles = @(
    "type-lookups.json",
    "actions.json",
    "tools.json",
    "knowledge.json",
    "skills.json",
    "playbooks.json",
    "output-types.json"
)

$allFilesExist = $true
foreach ($file in $requiredFiles) {
    $path = Join-Path $ScriptDir $file
    if (Test-Path $path) {
        Write-Host "  ✓ $file" -ForegroundColor Green
    } else {
        Write-Host "  ✗ $file not found" -ForegroundColor Red
        $allFilesExist = $false
    }
}

if (-not $allFilesExist) {
    Write-Host ""
    Write-Host "Missing required JSON files. Cannot proceed." -ForegroundColor Red
    exit 1
}

Write-Host ""

# Confirmation prompt (skip if DryRun)
if (-not $DryRun) {
    Write-Host "This will deploy seed data to: $EnvironmentUrl" -ForegroundColor Yellow
    $confirmation = Read-Host "Continue? (y/N)"
    if ($confirmation -ne 'y') {
        Write-Host "Deployment cancelled." -ForegroundColor Yellow
        exit 0
    }
    Write-Host ""
}

# Track deployment results
$deploymentLog = @()

function Invoke-DeploymentScript {
    param(
        [string]$ScriptName,
        [string]$Description
    )

    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "  Step: $Description" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan

    $scriptPath = Join-Path $ScriptDir $ScriptName
    if (-not (Test-Path $scriptPath)) {
        Write-Host "ERROR: Script not found: $scriptPath" -ForegroundColor Red
        $script:deploymentLog += @{
            Step = $Description
            Status = "Error"
            Message = "Script not found"
        }
        return $false
    }

    try {
        $params = @{
            EnvironmentUrl = $EnvironmentUrl
        }
        if ($DryRun) { $params['DryRun'] = $true }
        if ($Force) { $params['Force'] = $true }

        & $scriptPath @params

        if ($LASTEXITCODE -eq 0 -or $null -eq $LASTEXITCODE) {
            $script:deploymentLog += @{
                Step = $Description
                Status = "Success"
            }
            return $true
        } else {
            $script:deploymentLog += @{
                Step = $Description
                Status = "Error"
                Message = "Script exited with code $LASTEXITCODE"
            }
            return $false
        }
    } catch {
        Write-Host "ERROR executing $ScriptName : $_" -ForegroundColor Red
        $script:deploymentLog += @{
            Step = $Description
            Status = "Error"
            Message = $_.Exception.Message
        }
        return $false
    }
}

# Execute deployment in correct order
$startTime = Get-Date

Write-Host ""
Write-Host "Starting deployment sequence..." -ForegroundColor Cyan
Write-Host ""

$success = $true

# Step 1: Type Lookups
$success = $success -and (Invoke-DeploymentScript -ScriptName "Deploy-TypeLookups.ps1" -Description "Deploy Type Lookups")

# Step 2: Actions
$success = $success -and (Invoke-DeploymentScript -ScriptName "Deploy-Actions.ps1" -Description "Deploy Actions")

# Step 3: Tools
$success = $success -and (Invoke-DeploymentScript -ScriptName "Deploy-Tools.ps1" -Description "Deploy Tools")

# Step 4: Knowledge
$success = $success -and (Invoke-DeploymentScript -ScriptName "Deploy-Knowledge.ps1" -Description "Deploy Knowledge")

# Step 5: Skills
$success = $success -and (Invoke-DeploymentScript -ScriptName "Deploy-Skills.ps1" -Description "Deploy Skills")

# Step 6: Playbooks
$success = $success -and (Invoke-DeploymentScript -ScriptName "Deploy-Playbooks.ps1" -Description "Deploy Playbooks")

# Step 7: Output Types (if script exists)
$outputTypesScript = Join-Path $ScriptDir "Deploy-OutputTypes.ps1"
if (Test-Path $outputTypesScript) {
    $success = $success -and (Invoke-DeploymentScript -ScriptName "Deploy-OutputTypes.ps1" -Description "Deploy Output Types")
} else {
    Write-Host ""
    Write-Host "Note: Deploy-OutputTypes.ps1 not found - skipping output types deployment" -ForegroundColor Yellow
    Write-Host "      Output types can be deployed manually later if needed" -ForegroundColor Yellow
}

$endTime = Get-Date
$duration = $endTime - $startTime

# Final Summary
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Deployment Complete" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

foreach ($log in $deploymentLog) {
    $color = if ($log.Status -eq "Success") { "Green" } elseif ($log.Status -eq "Error") { "Red" } else { "Yellow" }
    $icon = if ($log.Status -eq "Success") { "✓" } elseif ($log.Status -eq "Error") { "✗" } else { "⚠" }
    Write-Host "$icon $($log.Step): $($log.Status)" -ForegroundColor $color
    if ($log.Message) {
        Write-Host "  $($log.Message)" -ForegroundColor Gray
    }
}

Write-Host ""
Write-Host "Duration: $($duration.ToString('mm\:ss'))" -ForegroundColor Gray

if ($success) {
    Write-Host ""
    Write-Host "✓ All seed data deployed successfully!" -ForegroundColor Green

    if (-not $SkipVerification) {
        Write-Host ""
        Write-Host "Next Steps:" -ForegroundColor Cyan
        Write-Host "  1. Run verification scripts to confirm data:" -ForegroundColor Gray
        Write-Host "     .\Verify-TypeLookups.ps1" -ForegroundColor Gray
        Write-Host "     .\Verify-Actions.ps1" -ForegroundColor Gray
        Write-Host "     .\Verify-Tools.ps1" -ForegroundColor Gray
        Write-Host "     .\Verify-Knowledge.ps1" -ForegroundColor Gray
        Write-Host "     .\Verify-Skills.ps1" -ForegroundColor Gray
        Write-Host "     .\Verify-Playbooks.ps1" -ForegroundColor Gray
        Write-Host ""
        Write-Host "  2. Test the Document Profile playbook:" -ForegroundColor Gray
        Write-Host "     Upload a document via UniversalQuickCreate PCF control" -ForegroundColor Gray
        Write-Host "     Enable AI Summary and verify Document Profile executes" -ForegroundColor Gray
    }

    exit 0
} else {
    Write-Host ""
    Write-Host "✗ Deployment completed with errors" -ForegroundColor Red
    Write-Host "   Review the error messages above and fix issues before retrying" -ForegroundColor Yellow
    exit 1
}
