# Upload Web Resources for UniversalDocumentUpload PCF Control
# This script creates web resources for bundle.js and CSS

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Uploading PCF Web Resources" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$controlPath = "C:\code_files\spaarke\src\controls\UniversalQuickCreate\out\controls\UniversalQuickCreate"
$bundlePath = "$controlPath\bundle.js"
$cssPath = "$controlPath\css\UniversalQuickCreate.css"

# Verify files exist
if (-not (Test-Path $bundlePath)) {
    Write-Host "ERROR: bundle.js not found at $bundlePath" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $cssPath)) {
    Write-Host "ERROR: CSS file not found at $cssPath" -ForegroundColor Red
    exit 1
}

Write-Host "`nFiles found:" -ForegroundColor Green
Write-Host "  Bundle: $bundlePath" -ForegroundColor White
Write-Host "  CSS: $cssPath" -ForegroundColor White

Write-Host "`nManual Upload Instructions:" -ForegroundColor Yellow
Write-Host "Unfortunately, PAC CLI doesn't have a direct command to upload web resources." -ForegroundColor Gray
Write-Host "You need to upload manually through the Power Apps maker portal:" -ForegroundColor White
Write-Host ""
Write-Host "1. Go to: https://make.powerapps.com" -ForegroundColor Cyan
Write-Host "2. Solutions -> Universal Quick Create" -ForegroundColor Cyan
Write-Host "3. Click 'New' -> 'More' -> 'Developer' -> 'Web resource'" -ForegroundColor Cyan
Write-Host ""
Write-Host "4. For bundle.js:" -ForegroundColor Yellow
Write-Host "   - Display name: UniversalDocumentUpload Bundle" -ForegroundColor White
Write-Host "   - Name: sprk_Spaarke.Controls.UniversalDocumentUpload/bundle.js" -ForegroundColor White
Write-Host "   - Type: JScript" -ForegroundColor White
Write-Host "   - Upload file: $bundlePath" -ForegroundColor White
Write-Host ""
Write-Host "5. Click 'New' again for CSS:" -ForegroundColor Yellow
Write-Host "   - Display name: UniversalDocumentUpload CSS" -ForegroundColor White
Write-Host "   - Name: sprk_Spaarke.Controls.UniversalDocumentUpload/css/UniversalQuickCreate.css" -ForegroundColor White
Write-Host "   - Type: CSS" -ForegroundColor White
Write-Host "   - Upload file: $cssPath" -ForegroundColor White
Write-Host ""
Write-Host "6. Click 'Publish all customizations'" -ForegroundColor Cyan
Write-Host ""
Write-Host "File paths for copy/paste:" -ForegroundColor Yellow
Write-Host "Bundle: $bundlePath" -ForegroundColor Green
Write-Host "CSS: $cssPath" -ForegroundColor Green
