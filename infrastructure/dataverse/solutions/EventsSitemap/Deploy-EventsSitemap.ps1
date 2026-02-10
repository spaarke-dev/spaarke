<#
.SYNOPSIS
    Deploy Events Custom Page sitemap configuration to Dataverse.

.DESCRIPTION
    This script provides guidance and optional automation for configuring the
    Dataverse sitemap to use the Events Custom Page instead of the OOB entity view.

    IMPORTANT: Sitemap modification typically requires the App Designer UI.
    This script provides the commands for solution export/import if manual
    XML editing is needed.

.PARAMETER Environment
    The Dataverse environment URL (e.g., https://orgname.crm.dynamics.com)

.PARAMETER CustomPageName
    The logical name of the Events Custom Page (default: sprk_eventspage)

.PARAMETER AppModuleName
    The name of the Model-Driven App containing the Events navigation

.EXAMPLE
    .\Deploy-EventsSitemap.ps1 -Environment "https://spaarkedev1.crm.dynamics.com"

.NOTES
    Created: 2026-02-04
    Project: Events Workspace Apps UX R1
    Task: 067 - Configure Sitemap Navigation
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$Environment = "https://spaarkedev1.crm.dynamics.com",

    [Parameter(Mandatory = $false)]
    [string]$CustomPageName = "sprk_eventspage",

    [Parameter(Mandatory = $false)]
    [string]$AppModuleName = "Spaarke"
)

$ErrorActionPreference = "Stop"

Write-Host "================================================================" -ForegroundColor Cyan
Write-Host " Events Sitemap Configuration" -ForegroundColor Cyan
Write-Host " Replacing OOB Events view with Events Custom Page" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ""

# Check PAC CLI authentication
Write-Host "Step 1: Checking PAC CLI authentication..." -ForegroundColor Yellow
$authList = pac auth list 2>&1
if ($LASTEXITCODE -ne 0 -or $authList -like "*No profiles*") {
    Write-Host "ERROR: Not authenticated to Dataverse. Run: pac auth create --url $Environment" -ForegroundColor Red
    exit 1
}
Write-Host "  [OK] PAC CLI authenticated" -ForegroundColor Green
Write-Host ""

# Display configuration instructions
Write-Host "Step 2: Sitemap Configuration" -ForegroundColor Yellow
Write-Host ""
Write-Host "  RECOMMENDED: Use App Designer (UI-based approach)" -ForegroundColor White
Write-Host "  ------------------------------------------------" -ForegroundColor White
Write-Host "  1. Open https://make.powerapps.com" -ForegroundColor Gray
Write-Host "  2. Select your environment" -ForegroundColor Gray
Write-Host "  3. Navigate to Apps > $AppModuleName > Edit" -ForegroundColor Gray
Write-Host "  4. In left navigation, find 'Events' entry" -ForegroundColor Gray
Write-Host "  5. Click ... > Edit" -ForegroundColor Gray
Write-Host "  6. Change Page type: 'Entity' -> 'Custom page'" -ForegroundColor Gray
Write-Host "  7. Select: '$CustomPageName'" -ForegroundColor Gray
Write-Host "  8. Verify Title is 'Events' (NOT 'My Events')" -ForegroundColor Gray
Write-Host "  9. Save and Publish" -ForegroundColor Gray
Write-Host ""

Write-Host "  ALTERNATIVE: Export/Modify/Import sitemap XML" -ForegroundColor White
Write-Host "  --------------------------------------------" -ForegroundColor White
Write-Host ""

# Export command
Write-Host "  # Export solution with sitemap:" -ForegroundColor DarkGray
Write-Host "  pac solution export --name YourAppSolution --path ./exported/ --include-sitemap" -ForegroundColor DarkGray
Write-Host ""

# Sitemap XML modification
Write-Host "  # In SiteMap.xml, replace Entity SubArea with Custom Page SubArea:" -ForegroundColor DarkGray
Write-Host ""
Write-Host "  # BEFORE (Entity-based):" -ForegroundColor DarkGray
Write-Host '  <SubArea Id="nav_sprk_event" Entity="sprk_event">' -ForegroundColor DarkGray
Write-Host '    <Titles><Title LCID="1033" Title="Events" /></Titles>' -ForegroundColor DarkGray
Write-Host '  </SubArea>' -ForegroundColor DarkGray
Write-Host ""
Write-Host "  # AFTER (Custom Page):" -ForegroundColor DarkGray
Write-Host "  <SubArea Id=`"nav_sprk_eventspage`" Url=`"/main.aspx?pagetype=custom&amp;name=$CustomPageName`">" -ForegroundColor DarkGray
Write-Host '    <Titles><Title LCID="1033" Title="Events" /></Titles>' -ForegroundColor DarkGray
Write-Host '  </SubArea>' -ForegroundColor DarkGray
Write-Host ""

# Import command
Write-Host "  # Repack and import:" -ForegroundColor DarkGray
Write-Host "  pac solution pack --zipfile ./updated.zip --folder ./exported/" -ForegroundColor DarkGray
Write-Host "  pac solution import --path ./updated.zip --publish-changes" -ForegroundColor DarkGray
Write-Host ""

# Verification steps
Write-Host "Step 3: Verification" -ForegroundColor Yellow
Write-Host ""
Write-Host "  After configuration, verify:" -ForegroundColor White
Write-Host "  [ ] Events navigation opens Custom Page (not entity grid)" -ForegroundColor Gray
Write-Host "  [ ] Navigation label is 'Events' (not 'My Events')" -ForegroundColor Gray
Write-Host "  [ ] Calendar and Grid components render correctly" -ForegroundColor Gray
Write-Host "  [ ] Icon is preserved from original entry" -ForegroundColor Gray
Write-Host "  [ ] Previous OOB view not accessible via sitemap" -ForegroundColor Gray
Write-Host ""

Write-Host "================================================================" -ForegroundColor Cyan
Write-Host " Configuration guidance complete" -ForegroundColor Cyan
Write-Host " Follow the steps above to update the sitemap" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
