# Register BFF API with Container Type using PnP PowerShell
# This uses SharePoint PnP cmdlets instead of REST API

param(
    [string]$ContainerTypeId = "8a6ce34c-6055-4681-8f87-2f4f9f921c06",
    [string]$BffApiAppId = "1e40baad-e065-4aea-a8d4-4b7ab273458c",
    [string]$PcfAppId = "170c98e1-d486-4355-bcbe-170454e0207c"
)

Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "REGISTER BFF API WITH CONTAINER TYPE (PnP)" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "Container Type: $ContainerTypeId" -ForegroundColor White
Write-Host "BFF API:        $BffApiAppId" -ForegroundColor White
Write-Host "PCF App:        $PcfAppId" -ForegroundColor White
Write-Host ""

# Check if PnP PowerShell is installed
$pnpModule = Get-Module -ListAvailable -Name PnP.PowerShell

if (-not $pnpModule) {
    Write-Host "Installing PnP.PowerShell..." -ForegroundColor Yellow
    Install-Module -Name PnP.PowerShell -Scope CurrentUser -Force -AllowClobber
}

Import-Module PnP.PowerShell

Write-Host "Connecting to SharePoint Admin Center..." -ForegroundColor Yellow
Write-Host "You'll be prompted to sign in interactively" -ForegroundColor Gray
Write-Host ""

try {
    Connect-PnPOnline -Url "https://spaarke-admin.sharepoint.com" -Interactive

    Write-Host "✅ Connected" -ForegroundColor Green
    Write-Host ""

    # Check if there's a cmdlet for managing container type permissions
    Write-Host "Checking available container type cmdlets..." -ForegroundColor Yellow

    $containerCmdlets = Get-Command -Module PnP.PowerShell | Where-Object { $_.Name -like "*Container*" }

    Write-Host "Available cmdlets:" -ForegroundColor Cyan
    foreach ($cmd in $containerCmdlets) {
        Write-Host "  - $($cmd.Name)" -ForegroundColor Gray
    }

    Write-Host ""
    Write-Host "⚠️  NOTE: Container type application registration may need to be done via:" -ForegroundColor Yellow
    Write-Host "  1. SharePoint Admin Center UI, OR" -ForegroundColor Gray
    Write-Host "  2. Direct SharePoint REST API (not available via PnP yet), OR" -ForegroundColor Gray
    Write-Host "  3. Microsoft support ticket" -ForegroundColor Gray
    Write-Host ""

    # Try to get container type info
    Write-Host "Getting container type info..." -ForegroundColor Yellow

    try {
        $containerType = Get-SPOContainerType | Where-Object { $_.ContainerTypeId -eq $ContainerTypeId }

        if ($containerType) {
            Write-Host "✅ Found container type" -ForegroundColor Green
            Write-Host "  Name: $($containerType.ContainerTypeName)" -ForegroundColor White
            Write-Host "  Owner: $($containerType.OwningApplicationId)" -ForegroundColor White
            Write-Host ""

            Write-Host "⚠️  As of now, there is no PowerShell cmdlet to manage application" -ForegroundColor Yellow
            Write-Host "   permissions for container types." -ForegroundColor Yellow
            Write-Host ""
            Write-Host "You may need to:" -ForegroundColor Cyan
            Write-Host "  1. Contact Microsoft Support" -ForegroundColor White
            Write-Host "  2. Use SharePoint Admin Center UI (if available)" -ForegroundColor White
            Write-Host "  3. Wait for API/cmdlet availability" -ForegroundColor White
        }
    } catch {
        Write-Host "❌ Could not get container type: $($_.Exception.Message)" -ForegroundColor Red
    }

    Disconnect-PnPOnline

} catch {
    Write-Host "❌ Error: $($_.Exception.Message)" -ForegroundColor Red
}
