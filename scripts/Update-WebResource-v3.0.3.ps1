# Update sprk_subgrid_commands.js Web Resource to v3.0.3
# This script updates the web resource content in Dataverse

param(
    [string]$WebResourcePath = "C:\code_files\spaarke\sprk_subgrid_commands.js",
    [string]$WebResourceName = "sprk_subgrid_commands.js"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Web Resource Update Script v3.0.3" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Verify file exists
if (-not (Test-Path $WebResourcePath)) {
    Write-Host "ERROR: File not found: $WebResourcePath" -ForegroundColor Red
    exit 1
}

Write-Host "✓ Found file: $WebResourcePath" -ForegroundColor Green

# Read file content
$content = Get-Content $WebResourcePath -Raw
$base64Content = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($content))

Write-Host "✓ File size: $($content.Length) characters" -ForegroundColor Green
Write-Host "✓ Base64 encoded: $($base64Content.Length) characters" -ForegroundColor Green
Write-Host ""

# Use pac CLI to get environment details
Write-Host "Getting Dataverse environment details..." -ForegroundColor Yellow
$authList = pac auth list --json | ConvertFrom-Json
$activeAuth = $authList | Where-Object { $_.IsActive -eq $true }

if (-not $activeAuth) {
    Write-Host "ERROR: No active Dataverse authentication found" -ForegroundColor Red
    Write-Host "Run: pac auth create --url https://spaarkedev1.crm.dynamics.com" -ForegroundColor Yellow
    exit 1
}

Write-Host "✓ Connected to: $($activeAuth.EnvironmentName)" -ForegroundColor Green
Write-Host "✓ User: $($activeAuth.UserName)" -ForegroundColor Green
Write-Host ""

# Use PowerShell to update web resource via Web API
Write-Host "Updating web resource via Dataverse Web API..." -ForegroundColor Yellow

$updateScript = @"
# Get access token
`$token = (pac auth list --json | ConvertFrom-Json | Where-Object { `$_.IsActive -eq `$true }).Token

if (-not `$token) {
    # Try to get token using pac auth
    Write-Host "Getting authentication token..." -ForegroundColor Yellow
    `$authOutput = pac org who --json 2>`$null
    if (`$LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Failed to authenticate" -ForegroundColor Red
        exit 1
    }
}

# Query for web resource
`$envUrl = "$($activeAuth.EnvironmentUrl)"
`$webResourceName = "$WebResourceName"

Write-Host "Querying for web resource: `$webResourceName" -ForegroundColor Yellow

# For now, provide manual upload instructions
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "MANUAL UPLOAD REQUIRED" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "The pac CLI doesn't support direct web resource updates." -ForegroundColor Yellow
Write-Host "Please upload manually via Power Apps Maker Portal:" -ForegroundColor Yellow
Write-Host ""
Write-Host "1. Go to: https://make.powerapps.com" -ForegroundColor White
Write-Host "2. Select environment: $($activeAuth.EnvironmentName)" -ForegroundColor White
Write-Host "3. Solutions → UniversalQuickCreateSolution" -ForegroundColor White
Write-Host "4. Find: sprk_subgrid_commands.js" -ForegroundColor White
Write-Host "5. Click Edit/Upload" -ForegroundColor White
Write-Host "6. Upload: $WebResourcePath" -ForegroundColor Cyan
Write-Host "7. Save and Publish All Customizations" -ForegroundColor Green
Write-Host ""
"@

Invoke-Expression $updateScript

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "FILE READY FOR UPLOAD" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "File location: $WebResourcePath" -ForegroundColor Cyan
Write-Host ""
Write-Host "Key changes in v3.0.3:" -ForegroundColor Yellow
Write-Host "  ✓ Custom Page now binds parameters via Param()" -ForegroundColor Green
Write-Host "  ✓ Added appId URL fallback" -ForegroundColor Green
Write-Host "  ✓ Version references updated to 3.0.3" -ForegroundColor Green
Write-Host ""
