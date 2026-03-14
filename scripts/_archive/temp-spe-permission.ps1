# SPE Container Permission Setup
# Grant "Spaarke" security group access to the container

$containerId = "b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50"
$groupId = "2a0a28d9-5d5f-456f-972a-89a75b702bf8"
$groupName = "Spaarke Users"

Write-Host "=== SPE Container Permission Setup ===" -ForegroundColor Cyan
Write-Host "Container ID: $containerId"
Write-Host "Group: $groupName ($groupId)"
Write-Host ""

# Connect to SPO Admin
try {
    Write-Host "Connecting to SharePoint Online Admin..." -ForegroundColor Yellow
    Connect-SPOService -Url "https://spaarke-admin.sharepoint.com" -ErrorAction Stop
    Write-Host "Connected successfully!" -ForegroundColor Green
    Write-Host ""

    # Get current container info
    Write-Host "Current container permissions:" -ForegroundColor Cyan
    $container = Get-SPOContainer -Identity $containerId -ErrorAction SilentlyContinue
    if ($container) {
        Write-Host "  Container: $($container.ContainerName)"
        Write-Host "  Status: $($container.Status)"
    }
    Write-Host ""

    # Add the security group with Reader permission
    Write-Host "Adding security group '$groupName' with Reader permission..." -ForegroundColor Yellow
    Add-SPOContainerUser -Identity $containerId -Users $groupId -Role Reader -ErrorAction Stop
    Write-Host "SUCCESS: Security group added!" -ForegroundColor Green

    Write-Host ""
    Write-Host "=== Verification ===" -ForegroundColor Cyan
    Write-Host "The 'Spaarke' security group now has Reader access to the container."
    Write-Host "All members of this group can now access documents via SPE."

} catch {
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
