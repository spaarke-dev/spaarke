# Create SPE Container for a Business Unit
# Purpose: Creates a new SharePoint Embedded container and sets sprk_containerid on a Dataverse business unit
# Usage: Run when a new business unit is created and needs document storage
#
# Prerequisites:
#   - Container type must already exist and be registered (see Create-NewContainerType.ps1)
#   - Owning app must have FileStorageContainer.Selected permission
#   - az CLI must be logged in with permissions to Graph API and Dataverse
#
# Automation options (ADR-002 prohibits Dataverse plugins):
#   - Power Automate cloud flow triggered on BU creation
#   - BFF API endpoint called from ribbon button / command bar
#   - Manual execution of this script during customer onboarding

param(
    [Parameter(Mandatory)][string]$BusinessUnitId,       # Dataverse BU GUID
    [Parameter(Mandatory)][string]$BusinessUnitName,     # Display name for the container
    [Parameter(Mandatory)][string]$ContainerTypeId,      # SPE container type GUID
    [Parameter(Mandatory)][string]$DataverseUrl,         # e.g., "https://spaarke-prod.crm.dynamics.com"
    [switch]$Force = $false                              # Overwrite existing sprk_containerid
)

Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "CREATE SPE CONTAINER FOR BUSINESS UNIT" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "Business Unit:    $BusinessUnitName ($BusinessUnitId)" -ForegroundColor White
Write-Host "Container Type:   $ContainerTypeId" -ForegroundColor White
Write-Host "Dataverse:        $DataverseUrl" -ForegroundColor White
Write-Host ""

# Step 1: Get Graph API token
Write-Host "Step 1: Acquiring Graph API access token..." -ForegroundColor Yellow

$graphToken = az account get-access-token `
    --resource "https://graph.microsoft.com" `
    --query accessToken -o tsv 2>&1

if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($graphToken)) {
    Write-Host "Failed to acquire Graph API token: $graphToken" -ForegroundColor Red
    Write-Host "Ensure az CLI is logged in: az login" -ForegroundColor Yellow
    exit 1
}

Write-Host "Graph API token acquired." -ForegroundColor Green
Write-Host ""

# Step 2: Check if BU already has a container
Write-Host "Step 2: Checking existing container assignment..." -ForegroundColor Yellow

$dvToken = az account get-access-token `
    --resource $DataverseUrl `
    --query accessToken -o tsv 2>&1

if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($dvToken)) {
    Write-Host "Failed to acquire Dataverse token: $dvToken" -ForegroundColor Red
    Write-Host "Ensure az CLI has Dataverse permissions." -ForegroundColor Yellow
    exit 1
}

$dvHeaders = @{
    "Authorization"    = "Bearer $dvToken"
    "Content-Type"     = "application/json"
    "OData-MaxVersion" = "4.0"
    "OData-Version"    = "4.0"
}

try {
    $buResponse = Invoke-RestMethod `
        -Uri "$DataverseUrl/api/data/v9.2/businessunits($BusinessUnitId)?`$select=businessunitid,name,sprk_containerid" `
        -Headers $dvHeaders `
        -Method Get `
        -ErrorAction Stop

    if (-not [string]::IsNullOrWhiteSpace($buResponse.sprk_containerid) -and -not $Force) {
        Write-Host "Business unit already has sprk_containerid: $($buResponse.sprk_containerid)" -ForegroundColor Yellow
        Write-Host "Use -Force to overwrite." -ForegroundColor Yellow
        exit 0
    }

    if (-not [string]::IsNullOrWhiteSpace($buResponse.sprk_containerid) -and $Force) {
        Write-Host "Existing container ID: $($buResponse.sprk_containerid) (will be overwritten)" -ForegroundColor Yellow
    } else {
        Write-Host "No existing container. Proceeding." -ForegroundColor Green
    }
}
catch {
    Write-Host "Failed to query business unit: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Verify BusinessUnitId and Dataverse URL." -ForegroundColor Yellow
    exit 1
}

Write-Host ""

# Step 3: Create SPE container via Graph API
Write-Host "Step 3: Creating SPE container..." -ForegroundColor Yellow

$containerDisplayName = "$BusinessUnitName Documents"

$createBody = @{
    displayName     = $containerDisplayName
    description     = "Document storage for business unit: $BusinessUnitName"
    containerTypeId = $ContainerTypeId
} | ConvertTo-Json

$graphHeaders = @{
    "Authorization" = "Bearer $graphToken"
    "Content-Type"  = "application/json"
}

try {
    $container = Invoke-RestMethod `
        -Uri "https://graph.microsoft.com/v1.0/storage/fileStorage/containers" `
        -Method Post `
        -Headers $graphHeaders `
        -Body $createBody `
        -ErrorAction Stop

    $containerId = $container.id

    Write-Host "SPE container created!" -ForegroundColor Green
    Write-Host "  Container ID: $containerId" -ForegroundColor Yellow
    Write-Host "  Display Name: $containerDisplayName" -ForegroundColor White
    Write-Host ""
}
catch {
    Write-Host "Failed to create SPE container: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.ErrorDetails.Message) {
        Write-Host "Details: $($_.ErrorDetails.Message)" -ForegroundColor Red
    }
    Write-Host ""
    Write-Host "Troubleshooting:" -ForegroundColor Yellow
    Write-Host "  - Verify owning app has FileStorageContainer.Selected permission" -ForegroundColor Gray
    Write-Host "  - Verify container type is registered: Check-ContainerType-Registration.ps1" -ForegroundColor Gray
    exit 1
}

# Step 4: Update business unit with container ID
Write-Host "Step 4: Setting sprk_containerid on business unit..." -ForegroundColor Yellow

try {
    Invoke-RestMethod `
        -Uri "$DataverseUrl/api/data/v9.2/businessunits($BusinessUnitId)" `
        -Headers $dvHeaders `
        -Method Patch `
        -Body (@{ sprk_containerid = $containerId } | ConvertTo-Json) `
        -ErrorAction Stop

    Write-Host "sprk_containerid set on business unit." -ForegroundColor Green
    Write-Host ""
}
catch {
    Write-Host "Failed to update business unit: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    Write-Host "Container was created but BU was not updated." -ForegroundColor Yellow
    Write-Host "Manually set sprk_containerid = $containerId on BU $BusinessUnitId" -ForegroundColor Yellow
    exit 1
}

# Step 5: Verify
Write-Host "Step 5: Verifying..." -ForegroundColor Yellow

try {
    $verifyResponse = Invoke-RestMethod `
        -Uri "$DataverseUrl/api/data/v9.2/businessunits($BusinessUnitId)?`$select=businessunitid,name,sprk_containerid" `
        -Headers $dvHeaders `
        -Method Get `
        -ErrorAction Stop

    if ($verifyResponse.sprk_containerid -eq $containerId) {
        Write-Host "Verified: sprk_containerid matches." -ForegroundColor Green
    } else {
        Write-Host "Verification mismatch! Expected: $containerId, Got: $($verifyResponse.sprk_containerid)" -ForegroundColor Yellow
    }
}
catch {
    Write-Host "Could not verify (non-critical): $($_.Exception.Message)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "SUCCESS" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "Business Unit:   $BusinessUnitName" -ForegroundColor White
Write-Host "Container ID:    $containerId" -ForegroundColor Yellow
Write-Host ""
Write-Host "Users in this BU can now upload documents via the Document Upload Wizard." -ForegroundColor Gray
Write-Host ""
