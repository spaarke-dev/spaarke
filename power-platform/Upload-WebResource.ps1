# Upload Web Resource to Power Platform
# This script uploads the JavaScript web resource to the Spaarke Core solution

param(
    [Parameter(Mandatory=$false)]
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",

    [Parameter(Mandatory=$false)]
    [string]$SolutionName = "spaarke_core",

    [Parameter(Mandatory=$false)]
    [string]$WebResourcePath = "webresources\scripts\sprk_DocumentOperations.js"
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Upload Web Resource to Power Platform" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check if file exists
if (-not (Test-Path $WebResourcePath)) {
    Write-Host "Error: Web resource file not found at: $WebResourcePath" -ForegroundColor Red
    exit 1
}

Write-Host "[1/5] Reading web resource file..." -ForegroundColor Yellow
$fileContent = [System.IO.File]::ReadAllBytes($WebResourcePath)
$base64Content = [System.Convert]::ToBase64String($fileContent)
$fileName = Split-Path $WebResourcePath -Leaf
Write-Host "✓ File read: $fileName ($($fileContent.Length) bytes)" -ForegroundColor Green
Write-Host ""

Write-Host "[2/5] Checking Power Platform authentication..." -ForegroundColor Yellow
$authList = pac auth list 2>&1
if ($authList -match "SPAARKE DEV 1") {
    Write-Host "✓ Authenticated to SPAARKE DEV 1" -ForegroundColor Green
} else {
    Write-Host "Error: Not authenticated to SPAARKE DEV 1" -ForegroundColor Red
    Write-Host "Run: pac auth create --environment $EnvironmentUrl" -ForegroundColor Yellow
    exit 1
}
Write-Host ""

Write-Host "[3/5] Preparing web resource data..." -ForegroundColor Yellow
$webResourceName = "sprk_DocumentOperations"
$displayName = "Spaarke Document Operations"
$description = "File management operations for SharePoint Embedded integration - Task 3.2"

Write-Host "Name: $webResourceName" -ForegroundColor Gray
Write-Host "Display Name: $displayName" -ForegroundColor Gray
Write-Host "Type: Script (JScript)" -ForegroundColor Gray
Write-Host "Solution: $SolutionName" -ForegroundColor Gray
Write-Host ""

Write-Host "[4/5] Creating/Updating web resource..." -ForegroundColor Yellow
Write-Host "Using PAC CLI to upload web resource..." -ForegroundColor Gray

# Create a temporary JSON file for the web resource
$tempJson = @"
{
  "webresourcetype": 3,
  "name": "$webResourceName",
  "displayname": "$displayName",
  "description": "$description",
  "content": "$base64Content"
}
"@

$tempFile = [System.IO.Path]::GetTempFileName()
$tempJson | Out-File -FilePath $tempFile -Encoding UTF8

Write-Host ""
Write-Host "⚠️ MANUAL STEP REQUIRED:" -ForegroundColor Yellow
Write-Host "The PAC CLI doesn't have a direct 'webresource push' command." -ForegroundColor Yellow
Write-Host "Please use ONE of the following methods:" -ForegroundColor Yellow
Write-Host ""
Write-Host "METHOD 1: Power Platform Maker Portal (Recommended)" -ForegroundColor Cyan
Write-Host "1. Open: https://make.powerapps.com" -ForegroundColor White
Write-Host "2. Go to: Solutions → Spaarke Core" -ForegroundColor White
Write-Host "3. Click: New → More → Web resource" -ForegroundColor White
Write-Host "4. Upload file: $WebResourcePath" -ForegroundColor White
Write-Host "5. Set properties:" -ForegroundColor White
Write-Host "   - Display name: $displayName" -ForegroundColor Gray
Write-Host "   - Name: $webResourceName" -ForegroundColor Gray
Write-Host "   - Type: Script (JScript)" -ForegroundColor Gray
Write-Host "6. Click: Save" -ForegroundColor White
Write-Host "7. Click: Publish" -ForegroundColor White
Write-Host ""
Write-Host "METHOD 2: Using XrmToolBox (Alternative)" -ForegroundColor Cyan
Write-Host "1. Download XrmToolBox from: https://www.xrmtoolbox.com" -ForegroundColor White
Write-Host "2. Install 'Webresources Manager' plugin" -ForegroundColor White
Write-Host "3. Connect to your environment" -ForegroundColor White
Write-Host "4. Upload the web resource" -ForegroundColor White
Write-Host ""
Write-Host "METHOD 3: PowerShell with SDK (Advanced)" -ForegroundColor Cyan
Write-Host "Requires Microsoft.Xrm.Tooling.Connector and additional setup" -ForegroundColor White
Write-Host ""

Write-Host "[5/5] Next steps after upload:" -ForegroundColor Yellow
Write-Host "1. Configure form events (OnLoad handler)" -ForegroundColor White
Write-Host "2. Create ribbon buttons for file operations" -ForegroundColor White
Write-Host "3. Test the JavaScript on a document form" -ForegroundColor White
Write-Host ""
Write-Host "✓ Instructions provided - ready for manual upload" -ForegroundColor Green
Write-Host ""

# Clean up
Remove-Item $tempFile -ErrorAction SilentlyContinue

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Web Resource Ready for Upload" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
