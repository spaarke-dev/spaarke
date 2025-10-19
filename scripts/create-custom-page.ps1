#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Creates Custom Page in Dataverse using Power Apps API
.DESCRIPTION
    Attempts to create the Universal Document Upload custom page programmatically
#>

param()

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Custom Page Creation - Automated Attempt" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Unfortunately, Microsoft does not provide a supported API to" -ForegroundColor Yellow
Write-Host "programmatically create Custom Pages with PCF control bindings." -ForegroundColor Yellow
Write-Host ""
Write-Host "Custom Pages must be created through the Power Apps Studio interface." -ForegroundColor Yellow
Write-Host ""
Write-Host "However, here's a SIMPLIFIED approach you can try:" -ForegroundColor Cyan
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "OPTION 1: Simplified Custom Page Creation" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Instead of creating in the visual designer, try this:" -ForegroundColor White
Write-Host ""
Write-Host "1. Go to https://make.powerapps.com" -ForegroundColor White
Write-Host "2. Solutions -> Spaarke Core -> + New -> More -> Custom page" -ForegroundColor White
Write-Host "3. In the designer that opens:" -ForegroundColor White
Write-Host "   - Press F12 to open browser console" -ForegroundColor White
Write-Host "   - Look for options to import/paste JSON" -ForegroundColor White
Write-Host "   - OR check the 'View' menu for 'Code view' or 'Formula bar'" -ForegroundColor White
Write-Host ""
Write-Host "The Custom Page JSON is located at:" -ForegroundColor Cyan
Write-Host "c:\code_files\spaarke\src\controls\UniversalQuickCreate\UniversalQuickCreateSolution\CustomPages\sprk_universaldocumentupload_page.json" -ForegroundColor Gray
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "OPTION 2: Use Command Line (Experimental)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "You could try using the undocumented canvas app APIs:" -ForegroundColor Yellow
Write-Host ""
Write-Host "  pac canvas create --help" -ForegroundColor Gray
Write-Host ""
Write-Host "But this is not officially supported for Custom Pages." -ForegroundColor Yellow
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "RECOMMENDED: Manual Creation (10 minutes)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "The manual process is straightforward:" -ForegroundColor White
Write-Host ""
Write-Host "Step 1: Create blank custom page named 'sprk_universaldocumentupload_page'" -ForegroundColor White
Write-Host "Step 2: Click '+' Insert -> Get more components -> Import component" -ForegroundColor White
Write-Host "Step 3: Find 'UniversalDocumentUpload' and add it" -ForegroundColor White
Write-Host "Step 4: Resize to fill screen (Width=Parent.Width, Height=Parent.Height)" -ForegroundColor White
Write-Host "Step 5: Add 4 parameters (see MANUAL-DEPLOYMENT-STEPS.md)" -ForegroundColor White
Write-Host "Step 6: Bind properties using Param() function" -ForegroundColor White
Write-Host "Step 7: Save and Publish" -ForegroundColor White
Write-Host ""
Write-Host "Detailed step-by-step instructions:" -ForegroundColor Cyan
Write-Host "c:\code_files\spaarke\src\controls\UniversalQuickCreate\MANUAL-DEPLOYMENT-STEPS.md" -ForegroundColor Gray
Write-Host ""
