# Create New SharePoint Embedded Container Type
# Requires: SharePoint Administrator or Global Administrator permissions
# Uses: SharePoint Online Management Shell

param(
    [string]$ContainerTypeName = "Spaarke Document Storage OBO",
    [string]$OwningAppId = "170c98e1-d486-4355-bcbe-170454e0207c",  # PCF app
    [string]$ApplicationRedirectUrl = "https://localhost",  # Redirect URL for app
    [string]$AdminCenterUrl = "https://spaarke-admin.sharepoint.com",
    [switch]$Trial = $false  # Use trial container type (no billing)
)

Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "CREATE SHAREPOINT EMBEDDED CONTAINER TYPE" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "This will create a new container type using PowerShell." -ForegroundColor White
Write-Host "Requires SharePoint Administrator permissions." -ForegroundColor Yellow
Write-Host ""
Write-Host "Container Type Name: $ContainerTypeName" -ForegroundColor White
Write-Host "Owning Application:  $OwningAppId (PCF app)" -ForegroundColor White
Write-Host "Redirect URL:        $ApplicationRedirectUrl" -ForegroundColor White
Write-Host "Type:                $(if ($Trial) {'Trial'} else {'Standard'})" -ForegroundColor White
Write-Host ""

# Check if SharePoint Online Management Shell is installed
Write-Host "Step 1: Checking SharePoint Online Management Shell..." -ForegroundColor Yellow

$spoModule = Get-Module -ListAvailable -Name Microsoft.Online.SharePoint.PowerShell

if (-not $spoModule) {
    Write-Host "⚠️  SharePoint Online Management Shell not found" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Installing SharePoint Online Management Shell..." -ForegroundColor Yellow
    Write-Host "This may take a few minutes..." -ForegroundColor Gray

    try {
        Install-Module -Name Microsoft.Online.SharePoint.PowerShell -Scope CurrentUser -Force -AllowClobber
        Write-Host "✅ Installed successfully" -ForegroundColor Green
    } catch {
        Write-Host "❌ Failed to install: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host ""
        Write-Host "Please install manually:" -ForegroundColor Yellow
        Write-Host "  Install-Module -Name Microsoft.Online.SharePoint.PowerShell" -ForegroundColor Gray
        exit 1
    }
} else {
    Write-Host "✅ SharePoint Online Management Shell found" -ForegroundColor Green
}

Write-Host ""

# Import the module
Write-Host "Step 2: Importing module..." -ForegroundColor Yellow
Import-Module Microsoft.Online.SharePoint.PowerShell -ErrorAction Stop
Write-Host "✅ Module imported" -ForegroundColor Green
Write-Host ""

# Connect to SharePoint Online
Write-Host "Step 3: Connecting to SharePoint Admin Center..." -ForegroundColor Yellow
Write-Host "URL: $AdminCenterUrl" -ForegroundColor Gray
Write-Host "You will be prompted to sign in..." -ForegroundColor Gray
Write-Host ""

try {
    Connect-SPOService -Url $AdminCenterUrl -ErrorAction Stop
    Write-Host "✅ Connected successfully" -ForegroundColor Green
    Write-Host ""
} catch {
    Write-Host "❌ Failed to connect: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    Write-Host "Troubleshooting:" -ForegroundColor Yellow
    Write-Host "  - Make sure you have SharePoint Administrator permissions" -ForegroundColor Gray
    Write-Host "  - Verify the Admin Center URL: $AdminCenterUrl" -ForegroundColor Gray
    Write-Host "  - Check if MFA is configured for your account" -ForegroundColor Gray
    exit 1
}

# Create container type
Write-Host "Step 4: Creating container type..." -ForegroundColor Yellow

try {
    if ($Trial) {
        Write-Host "Creating TRIAL container type (no billing required)..." -ForegroundColor Gray

        $containerType = New-SPOContainerType `
            -TrialContainerType `
            -ContainerTypeName $ContainerTypeName `
            -OwningApplicationId $OwningAppId `
            -ApplicationRedirectUrl $ApplicationRedirectUrl `
            -ErrorAction Stop
    } else {
        Write-Host "Creating STANDARD container type..." -ForegroundColor Gray
        Write-Host "⚠️  Note: Standard types require Azure billing setup" -ForegroundColor Yellow

        $containerType = New-SPOContainerType `
            -ContainerTypeName $ContainerTypeName `
            -OwningApplicationId $OwningAppId `
            -ApplicationRedirectUrl $ApplicationRedirectUrl `
            -ErrorAction Stop
    }

    Write-Host "✅ CONTAINER TYPE CREATED!" -ForegroundColor Green
    Write-Host ""
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "NEW CONTAINER TYPE DETAILS" -ForegroundColor Cyan
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Container Type ID:   $($containerType.ContainerTypeId)" -ForegroundColor Yellow
    Write-Host "Display Name:        $($containerType.DisplayName)" -ForegroundColor White
    Write-Host "Owning Application:  $($containerType.OwningApplicationId)" -ForegroundColor White
    Write-Host "Created:             $($containerType.CreatedDateTime)" -ForegroundColor Gray
    Write-Host ""

    $newContainerTypeId = $containerType.ContainerTypeId

    # Save configuration
    $config = @{
        ContainerTypeId = $newContainerTypeId
        DisplayName = $containerType.DisplayName
        OwningApplicationId = $containerType.OwningApplicationId
        CreatedDateTime = $containerType.CreatedDateTime
        IsTrial = $Trial
    }

    $configPath = "c:\code_files\spaarke\scripts\new-container-type-config.json"
    $config | ConvertTo-Json -Depth 3 | Out-File -FilePath $configPath -Encoding UTF8

    Write-Host "Configuration saved to: $configPath" -ForegroundColor Gray
    Write-Host ""

    Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "NEXT STEPS" -ForegroundColor Cyan
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "1. Register BFF API with this container type:" -ForegroundColor White
    Write-Host "   .\Register-BffApiWithContainerType.ps1 -ContainerTypeId '$newContainerTypeId'" -ForegroundColor Gray
    Write-Host ""
    Write-Host "2. Create a test container:" -ForegroundColor White
    Write-Host "   Use Graph API or your application to create a container of this type" -ForegroundColor Gray
    Write-Host ""
    Write-Host "3. Test OBO file upload:" -ForegroundColor White
    Write-Host "   PUT /api/obo/containers/{containerId}/files/test.txt" -ForegroundColor Gray
    Write-Host ""

    if (-not $Trial) {
        Write-Host "4. Set up billing (STANDARD type only):" -ForegroundColor White
        Write-Host "   Add-SPOContainerTypeBilling -ContainerTypeId '$newContainerTypeId' `" -ForegroundColor Gray
        Write-Host "     -AzureSubscriptionId 'your-subscription-id' `" -ForegroundColor Gray
        Write-Host "     -ResourceGroup 'your-resource-group' `" -ForegroundColor Gray
        Write-Host "     -Region 'your-region'" -ForegroundColor Gray
        Write-Host ""
    }

    Write-Host "Container Type ID for reference:" -ForegroundColor Cyan
    Write-Host "  $newContainerTypeId" -ForegroundColor Yellow
    Write-Host ""

} catch {
    Write-Host "❌ FAILED TO CREATE CONTAINER TYPE" -ForegroundColor Red
    Write-Host ""
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "TROUBLESHOOTING" -ForegroundColor Cyan
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Common Issues:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "1. Insufficient permissions:" -ForegroundColor White
    Write-Host "   - You need SharePoint Administrator or Global Administrator role" -ForegroundColor Gray
    Write-Host "   - Check your role in Azure AD" -ForegroundColor Gray
    Write-Host ""
    Write-Host "2. Quota exceeded:" -ForegroundColor White
    Write-Host "   - Each tenant can have max 25 container types (including 1 trial)" -ForegroundColor Gray
    Write-Host "   - Check existing container types: Get-SPOContainerType" -ForegroundColor Gray
    Write-Host ""
    Write-Host "3. Invalid owning application:" -ForegroundColor White
    Write-Host "   - Verify the OwningApplicationId exists in Azure AD" -ForegroundColor Gray
    Write-Host "   - Make sure the app has a service principal in the tenant" -ForegroundColor Gray
    Write-Host ""
    Write-Host "4. Billing not configured (STANDARD type):" -ForegroundColor White
    Write-Host "   - Standard types require Azure subscription" -ForegroundColor Gray
    Write-Host "   - Use -Trial switch for testing without billing" -ForegroundColor Gray
    Write-Host ""
}

# Disconnect
Write-Host "Disconnecting from SharePoint Online..." -ForegroundColor Gray
Disconnect-SPOService
Write-Host "✅ Done" -ForegroundColor Green
