# Enable Outlook Add-in Sideloading for Spaarke Tenant
# Run this in PowerShell (not Cloud Shell)

$ErrorActionPreference = "Stop"

Write-Host "=== Enable Outlook Add-in Sideloading ===" -ForegroundColor Cyan

# Install Exchange Online Management module if not present
if (!(Get-Module -ListAvailable -Name ExchangeOnlineManagement)) {
    Write-Host "Installing Exchange Online Management module..." -ForegroundColor Yellow
    Install-Module -Name ExchangeOnlineManagement -Force -AllowClobber
}

# Import module
Write-Host "Importing Exchange Online module..." -ForegroundColor Yellow
Import-Module ExchangeOnlineManagement

# Connect to Exchange Online
Write-Host "`nConnecting to Exchange Online..." -ForegroundColor Yellow
Write-Host "A browser window will open for authentication." -ForegroundColor Gray
Connect-ExchangeOnline -ShowBanner:$false

# Check current settings
Write-Host "`n=== Current Settings ===" -ForegroundColor Cyan
$orgConfig = Get-OrganizationConfig
Write-Host "AppsForOfficeEnabled: $($orgConfig.AppsForOfficeEnabled)" -ForegroundColor Yellow
Write-Host "IntegratedAppsEnabled: $($orgConfig.IntegratedAppsEnabled)" -ForegroundColor Yellow

# Enable custom add-ins
Write-Host "`n=== Enabling Custom Add-ins ===" -ForegroundColor Cyan
Set-OrganizationConfig -AppsForOfficeEnabled $true
Write-Host "✓ AppsForOfficeEnabled set to True" -ForegroundColor Green

# Enable integrated apps (if parameter exists)
try {
    Set-OrganizationConfig -IntegratedAppsEnabled $true
    Write-Host "✓ IntegratedAppsEnabled set to True" -ForegroundColor Green
} catch {
    Write-Host "⚠ IntegratedAppsEnabled parameter not available (this is OK)" -ForegroundColor Yellow
}

# Verify changes
Write-Host "`n=== Verifying Changes ===" -ForegroundColor Cyan
$orgConfig = Get-OrganizationConfig
Write-Host "AppsForOfficeEnabled: $($orgConfig.AppsForOfficeEnabled)" -ForegroundColor Green

# Check OWA Mailbox Policies
Write-Host "`n=== Checking OWA Mailbox Policies ===" -ForegroundColor Cyan
$owaPolicy = Get-OwaMailboxPolicy | Where-Object {$_.IsDefault -eq $true}
if ($owaPolicy) {
    Write-Host "Default OWA Policy: $($owaPolicy.Name)" -ForegroundColor Yellow

    # Try to enable add-ins on default policy
    try {
        Set-OwaMailboxPolicy -Identity $owaPolicy.Identity -AddinEnabled $true
        Write-Host "✓ Add-ins enabled on default OWA policy" -ForegroundColor Green
    } catch {
        Write-Host "⚠ Could not modify OWA policy (may not be necessary)" -ForegroundColor Yellow
    }
}

Write-Host "`n=== Success! ===" -ForegroundColor Green
Write-Host "Outlook add-in sideloading is now enabled." -ForegroundColor White
Write-Host "Note: Changes may take 5-10 minutes to propagate." -ForegroundColor Yellow
Write-Host "`nIf sideloading still fails, try deploying via M365 Admin Center instead." -ForegroundColor Gray

# Disconnect
Disconnect-ExchangeOnline -Confirm:$false
