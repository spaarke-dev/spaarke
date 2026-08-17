# Create SPE Container for a Business Unit
# Purpose: Creates a new SharePoint Embedded container and sets sprk_containerid on a Dataverse business unit
# Usage: Run when a new business unit is created and needs document storage
#
# T6 FIX (spec.md FR-11 + § MUST rules): SPE container creation now uses
# confidential-client CERT-BASED auth. The prior `az account get-access-token`
# path was DELEGATED (user identity of whoever ran `az login`) and returned
# 403 "public client not allowed" against Microsoft Graph SPE container APIs
# (silent-fail trap T6, design.md §4B). Refactored on 2026-08-17 by task 011.
#
# Prerequisites:
#   - Container type must already exist and be registered (see Create-NewContainerType.ps1)
#   - Owning app must have FileStorageContainer.Selected (app-only) permission
#   - Owning app cert bootstrapped in KV as PFX secret (24h SPE cert-replication window)
#   - az CLI must be logged in with permissions to Key Vault (get secret) + Dataverse
#     (Dataverse call remains delegated by design — it writes back a Dataverse column,
#     not an SPE resource, and is orthogonal to the T6 trap.)
#
# Automation options (ADR-002 prohibits Dataverse plugins):
#   - Power Automate cloud flow triggered on BU creation (would call BFF H8 handler)
#   - BFF API endpoint called from ribbon button / command bar
#   - Manual execution of this script during customer onboarding

param(
    [Parameter(Mandatory)][string]$BusinessUnitId,       # Dataverse BU GUID - always specific
    [Parameter(Mandatory)][string]$BusinessUnitName,     # Display name for the container
    [string]$ContainerTypeId  = $env:SPE_CONTAINER_TYPE_ID,
    [string]$DataverseUrl     = $env:DATAVERSE_URL,      # e.g., "https://spaarke-prod.crm.dynamics.com"

    # Owning-app identity for cert-based SPE auth (T6 fix)
    [string]$OwningAppId      = $env:API_APP_ID,
    [string]$TenantId         = $env:TENANT_ID,

    # Cert bootstrap - PRODUCTION path (KV): pass both.
    [string]$KeyVaultName     = $env:SPE_KV_NAME,
    [string]$CertSecretName   = $env:SPE_CERT_SECRET_NAME,

    # Cert bootstrap - DEV FALLBACK: pass thumbprint (cert already in CurrentUser\My).
    [string]$CertThumbprint   = $env:SPE_CERT_THUMBPRINT,

    [switch]$Force = $false                              # Overwrite existing sprk_containerid
)

$ErrorActionPreference = 'Stop'

if (-not $ContainerTypeId) { throw "ContainerTypeId required. Pass -ContainerTypeId or set SPE_CONTAINER_TYPE_ID env var." }
if (-not $DataverseUrl)    { throw "DataverseUrl required. Pass -DataverseUrl or set DATAVERSE_URL env var." }
if (-not $OwningAppId)     { throw "OwningAppId required for SPE cert-based auth. Pass -OwningAppId or set API_APP_ID env var." }
if (-not $TenantId)        { throw "TenantId required for SPE cert-based auth. Pass -TenantId or set TENANT_ID env var." }

$useKeyVault = ($KeyVaultName -and $CertSecretName)
if (-not $useKeyVault -and -not $CertThumbprint) {
    throw "Cert bootstrap required. Pass -KeyVaultName + -CertSecretName (production) or -CertThumbprint (dev). Env vars: SPE_KV_NAME + SPE_CERT_SECRET_NAME, or SPE_CERT_THUMBPRINT."
}

# --- Dot-source the SPE cert-based token helper (T6 fix) ---
. (Join-Path $PSScriptRoot 'common/Get-SpeConfidentialClientToken.ps1')

Write-Host "===============================================" -ForegroundColor Cyan
Write-Host "CREATE SPE CONTAINER FOR BUSINESS UNIT" -ForegroundColor Cyan
Write-Host "===============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Auth mode (SPE):     confidential-client cert-based (T6 fix, FR-11)" -ForegroundColor Green
Write-Host "Cert source:         $(if ($useKeyVault) { "Key Vault '$KeyVaultName' secret '$CertSecretName'" } else { "CurrentUser cert store thumbprint $CertThumbprint" })" -ForegroundColor Gray
Write-Host "Auth mode (Dataverse): delegated (az CLI - orthogonal to T6)" -ForegroundColor Gray
Write-Host ""
Write-Host "Business Unit:       $BusinessUnitName ($BusinessUnitId)" -ForegroundColor White
Write-Host "Container Type:      $ContainerTypeId" -ForegroundColor White
Write-Host "Owning App:          $OwningAppId" -ForegroundColor White
Write-Host "Dataverse:           $DataverseUrl" -ForegroundColor White
Write-Host ""

# Step 1: Get Graph API token (confidential-client, cert-based) - T6 fix
Write-Host "Step 1: Acquiring Graph API access token (confidential-client, cert-based)..." -ForegroundColor Yellow

$graphTokenArgs = @{
    TenantId = $TenantId
    ClientId = $OwningAppId
    Scope    = 'https://graph.microsoft.com/.default'
}
if ($useKeyVault) {
    $graphTokenArgs.KeyVaultName   = $KeyVaultName
    $graphTokenArgs.CertSecretName = $CertSecretName
}
else {
    $graphTokenArgs.CertThumbprint = $CertThumbprint
}

try {
    $graphToken = Get-SpeConfidentialClientToken @graphTokenArgs
}
catch {
    Write-Host "Failed to acquire Graph API token via cert-based confidential-client flow." -ForegroundColor Red
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    Write-Host "Troubleshooting:" -ForegroundColor Yellow
    Write-Host "  - Verify cert public key registered on app: az ad app credential list --id $OwningAppId" -ForegroundColor Gray
    Write-Host "  - Verify KV access: az keyvault secret show --vault-name $KeyVaultName --name $CertSecretName --query name" -ForegroundColor Gray
    Write-Host "  - If cert was just added, wait up to 24h for SPE cert replication (FR-01 lead-time)" -ForegroundColor Gray
    exit 1
}

Write-Host "Graph API token acquired." -ForegroundColor Green
Write-Host ""

# Step 2: Check if BU already has a container (Dataverse call - remains delegated)
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

# Step 3: Create SPE container via Graph API (confidential-client, cert-based)
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

    # T6-cleared log line (task 011 acceptance criterion)
    Write-Host "T6 cleared: container ID $containerId created via confidential-client cert-based auth." -ForegroundColor Green
    Write-Host ""
}
catch {
    Write-Host "Failed to create SPE container: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.ErrorDetails.Message) {
        Write-Host "Details: $($_.ErrorDetails.Message)" -ForegroundColor Red
    }
    Write-Host ""
    Write-Host "Troubleshooting:" -ForegroundColor Yellow
    Write-Host "  - Verify owning app has FileStorageContainer.Selected permission (app-only)" -ForegroundColor Gray
    Write-Host "  - Verify container type is registered: Check-ContainerType-Registration.ps1" -ForegroundColor Gray
    Write-Host "  - If 403 'Public client not allowed' — this should NOT occur under cert-based flow (T6 fix)" -ForegroundColor Gray
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
Write-Host "===============================================" -ForegroundColor Cyan
Write-Host "SUCCESS" -ForegroundColor Cyan
Write-Host "===============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Business Unit:   $BusinessUnitName" -ForegroundColor White
Write-Host "Container ID:    $containerId" -ForegroundColor Yellow
Write-Host ""
Write-Host "Users in this BU can now upload documents via the Document Upload Wizard." -ForegroundColor Gray
Write-Host ""
