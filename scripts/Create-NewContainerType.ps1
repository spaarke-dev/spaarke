# Create New Container Type for SPE Document Storage
# Owner: BFF API app (performs all server-side Graph operations)
# Creates container type, registers owning app, and optionally creates a test container
#
# T6 FIX (spec.md FR-11 + § MUST rules): This script now acquires SPE-facing
# tokens via confidential-client CERT-BASED flow (cert bootstrapped from KV).
# The prior client_secret path was removed on 2026-08-17 by task 011 because
# Microsoft Graph SPE APIs reject public/delegated clients with 403
# "public client not allowed" (silent-fail trap T6, design.md §4B).
#
# Prerequisite (H0 preflight):
#   1. Cert uploaded to KV as a base64 PFX secret (or via `az keyvault
#      certificate import`; the associated secret is base64 PFX by the same
#      name — see `az keyvault secret show`).
#   2. Cert added to the owning app registration:
#        az ad app credential reset --id <OwningAppId> --cert @cert.cer --append
#   3. Up to 24h SPE cert-replication window per FR-01 lead-time.

param(
    [string]$OwningAppId      = $env:API_APP_ID,
    [string]$TenantId         = $env:TENANT_ID,
    [string]$SharePointDomain = $env:SHAREPOINT_DOMAIN,   # e.g., "spaarke.sharepoint.com"

    # Cert bootstrap — PRODUCTION path (KV): pass both.
    [string]$KeyVaultName     = $env:SPE_KV_NAME,
    [string]$CertSecretName   = $env:SPE_CERT_SECRET_NAME,

    # Cert bootstrap — DEV FALLBACK: pass thumbprint (cert already in CurrentUser\My).
    [string]$CertThumbprint   = $env:SPE_CERT_THUMBPRINT,

    [string]$DisplayName         = "Spaarke Document Storage",
    [string]$Description         = "Container type for document storage - owned by BFF API app",
    [switch]$CreateTestContainer = $false
)

$ErrorActionPreference = 'Stop'

if (-not $OwningAppId)      { throw "OwningAppId required. Pass -OwningAppId or set API_APP_ID env var." }
if (-not $TenantId)         { throw "TenantId required. Pass -TenantId or set TENANT_ID env var." }
if (-not $SharePointDomain) { throw "SharePointDomain required. Pass -SharePointDomain or set SHAREPOINT_DOMAIN env var." }

$useKeyVault = ($KeyVaultName -and $CertSecretName)
if (-not $useKeyVault -and -not $CertThumbprint) {
    throw "Cert bootstrap required. Pass -KeyVaultName + -CertSecretName (production) or -CertThumbprint (dev). Env vars: SPE_KV_NAME + SPE_CERT_SECRET_NAME, or SPE_CERT_THUMBPRINT."
}

# --- Dot-source the SPE cert-based token helper (T6 fix) ---
. (Join-Path $PSScriptRoot 'common/Get-SpeConfidentialClientToken.ps1')

Write-Host "===============================================" -ForegroundColor Cyan
Write-Host "CREATE NEW CONTAINER TYPE" -ForegroundColor Cyan
Write-Host "===============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "This will create a NEW container type owned by the BFF API app." -ForegroundColor White
Write-Host "Auth mode:    confidential-client cert-based (T6 fix, FR-11)" -ForegroundColor Green
Write-Host "Cert source:  $(if ($useKeyVault) { "Key Vault '$KeyVaultName' secret '$CertSecretName'" } else { "CurrentUser cert store thumbprint $CertThumbprint" })" -ForegroundColor Gray
Write-Host ""
Write-Host "Owning App:   $OwningAppId (BFF API)" -ForegroundColor Gray
Write-Host "Display Name: $DisplayName" -ForegroundColor Gray
Write-Host "SP Domain:    $SharePointDomain" -ForegroundColor Gray
Write-Host ""

function Get-SpeTokenForScope([string]$Scope) {
    if ($useKeyVault) {
        return Get-SpeConfidentialClientToken `
            -TenantId       $TenantId `
            -ClientId       $OwningAppId `
            -Scope          $Scope `
            -KeyVaultName   $KeyVaultName `
            -CertSecretName $CertSecretName
    }
    else {
        return Get-SpeConfidentialClientToken `
            -TenantId       $TenantId `
            -ClientId       $OwningAppId `
            -Scope          $Scope `
            -CertThumbprint $CertThumbprint
    }
}

try {
    # Step 1: Get Graph token (confidential-client, cert-based)
    Write-Host "Step 1: Acquiring Graph API access token (confidential-client, cert-based)..." -ForegroundColor Yellow
    $graphToken = Get-SpeTokenForScope 'https://graph.microsoft.com/.default'
    Write-Host "Got Graph access token" -ForegroundColor Green
    Write-Host ""

    # Step 2: Create container type via Graph API
    Write-Host "Step 2: Creating container type via Graph API..." -ForegroundColor Yellow

    $containerTypeBody = @{
        displayName         = $DisplayName
        description         = $Description
        owningApplicationId = $OwningAppId
    } | ConvertTo-Json

    $headers = @{
        "Authorization" = "Bearer $graphToken"
        "Content-Type"  = "application/json"
        "Accept"        = "application/json"
    }

    $createUri = "https://graph.microsoft.com/beta/storage/fileStorage/containerTypes"

    Write-Host "Calling: POST $createUri" -ForegroundColor Gray
    Write-Host ""

    $containerType = Invoke-RestMethod -Uri $createUri `
        -Method Post `
        -Headers $headers `
        -Body $containerTypeBody `
        -ErrorAction Stop

    Write-Host "CONTAINER TYPE CREATED!" -ForegroundColor Green
    Write-Host ""
    Write-Host "===============================================" -ForegroundColor Cyan
    Write-Host "NEW CONTAINER TYPE DETAILS" -ForegroundColor Cyan
    Write-Host "===============================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Container Type ID: $($containerType.id)" -ForegroundColor Yellow
    Write-Host "Display Name:      $($containerType.displayName)" -ForegroundColor White
    Write-Host "Owner App:         $($containerType.owningApplicationId)" -ForegroundColor White
    Write-Host "Created:           $($containerType.createdDateTime)" -ForegroundColor Gray
    Write-Host ""

    $newContainerTypeId = $containerType.id

    # T6-cleared log line (task 011 acceptance criterion)
    Write-Host "T6 cleared: container-type ID $newContainerTypeId created via confidential-client cert-based auth." -ForegroundColor Green
    Write-Host ""

    # Step 3: Get SharePoint token (confidential-client, cert-based)
    Write-Host "Step 3: Acquiring SharePoint access token (confidential-client, cert-based)..." -ForegroundColor Yellow
    $spToken = Get-SpeTokenForScope "https://$SharePointDomain/.default"
    Write-Host "Got SharePoint access token" -ForegroundColor Green
    Write-Host ""

    # Step 4: Register owning app with container type (full permissions)
    Write-Host "Step 4: Registering owning app with container type..." -ForegroundColor Yellow
    Write-Host "  - Owning App: Full delegated + Full appOnly permissions" -ForegroundColor Gray

    $registrationBody = @{
        value = @(
            @{
                appId     = $OwningAppId
                delegated = @("full")
                appOnly   = @("full")
            }
        )
    } | ConvertTo-Json -Depth 3

    $spHeaders = @{
        "Authorization" = "Bearer $spToken"
        "Content-Type"  = "application/json"
        "Accept"        = "application/json"
    }

    $regUri = "https://$SharePointDomain/_api/v2.1/storageContainerTypes/$newContainerTypeId/applicationPermissions"

    Write-Host "Calling: PUT $regUri" -ForegroundColor Gray
    Write-Host ""

    $regResponse = Invoke-RestMethod -Uri $regUri `
        -Method Put `
        -Headers $spHeaders `
        -Body $registrationBody `
        -ErrorAction Stop

    Write-Host "APPLICATION REGISTERED!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Registered Application:" -ForegroundColor Cyan
    foreach ($app in $regResponse.value) {
        Write-Host "  - App ID: $($app.appId)" -ForegroundColor White
        Write-Host "    Delegated: $($app.delegated -join ', ')" -ForegroundColor Green
        Write-Host "    App-Only: $($app.appOnly -join ', ')" -ForegroundColor Gray
        Write-Host ""
    }

    # Step 5: Optionally create a test container
    if ($CreateTestContainer) {
        Write-Host "Step 5: Creating test container..." -ForegroundColor Yellow

        $testContainerBody = @{
            displayName     = "$DisplayName - Test"
            description     = "Test container for validation"
            containerTypeId = $newContainerTypeId
        } | ConvertTo-Json

        $testContainer = Invoke-RestMethod -Uri "https://graph.microsoft.com/beta/storage/fileStorage/containers" `
            -Method Post `
            -Headers $headers `
            -Body $testContainerBody `
            -ErrorAction Stop

        Write-Host "TEST CONTAINER CREATED!" -ForegroundColor Green
        Write-Host ""
        Write-Host "Container ID:   $($testContainer.id)" -ForegroundColor Yellow
        Write-Host "Display Name:   $($testContainer.displayName)" -ForegroundColor White
        Write-Host "Status:         $($testContainer.status)" -ForegroundColor Green
        Write-Host ""

        # T6-cleared log line for the container-create path as well.
        Write-Host "T6 cleared: container ID $($testContainer.id) created via confidential-client cert-based auth." -ForegroundColor Green
        Write-Host ""
    }

    # Final summary
    Write-Host "===============================================" -ForegroundColor Cyan
    Write-Host "SUCCESS" -ForegroundColor Cyan
    Write-Host "===============================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Container Type ID: $newContainerTypeId" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "NEXT STEPS:" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "1. Store container type ID in Key Vault:" -ForegroundColor White
    Write-Host "   az keyvault secret set --vault-name <name> --name 'Spe--ContainerTypeId' --value '$newContainerTypeId'" -ForegroundColor Gray
    Write-Host ""
    Write-Host "2. Create a container for the root business unit:" -ForegroundColor White
    Write-Host "   .\New-BusinessUnitContainer.ps1 -ContainerTypeId '$newContainerTypeId' ..." -ForegroundColor Gray
    Write-Host ""
    Write-Host "3. Test file upload via BFF API:" -ForegroundColor White
    Write-Host "   PUT /api/containers/{containerId}/files/test.txt" -ForegroundColor Gray
    Write-Host ""

    # Save configuration (non-secret metadata only)
    $config = @{
        ContainerTypeId = $newContainerTypeId
        OwningAppId     = $OwningAppId
        CreatedDateTime = $containerType.createdDateTime
    }

    $configPath = Join-Path $PSScriptRoot "new-container-type-config.json"
    $config | ConvertTo-Json -Depth 3 | Out-File -FilePath $configPath -Encoding UTF8

    Write-Host "Configuration saved to: $configPath" -ForegroundColor Gray
    Write-Host ""

} catch {
    Write-Host "ERROR" -ForegroundColor Red
    Write-Host ""
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red

    if ($_.Exception.Response) {
        try {
            $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
            $reader.BaseStream.Position = 0
            $reader.DiscardBufferedData()
            $responseBody = $reader.ReadToEnd()
            Write-Host ""
            Write-Host "Response body:" -ForegroundColor Yellow
            Write-Host $responseBody -ForegroundColor Gray

            try {
                $errorJson = $responseBody | ConvertFrom-Json
                Write-Host ""
                Write-Host "Error Details:" -ForegroundColor Yellow
                Write-Host "  Code:    $($errorJson.error.code)" -ForegroundColor Red
                Write-Host "  Message: $($errorJson.error.message)" -ForegroundColor Red
            } catch {
                # Not JSON
            }
        } catch {
            Write-Host "Could not read error response" -ForegroundColor Gray
        }
    }

    Write-Host ""
    Write-Host "===============================================" -ForegroundColor Cyan
    Write-Host "TROUBLESHOOTING" -ForegroundColor Cyan
    Write-Host "===============================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Common Issues:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "1. 'AADSTS700027' invalid_client / cert not trusted:" -ForegroundColor White
    Write-Host "   - Verify cert public key is attached to app registration:" -ForegroundColor Gray
    Write-Host "     az ad app credential list --id $OwningAppId" -ForegroundColor Gray
    Write-Host "   - If missing, re-append: az ad app credential reset --id <app> --cert @cert.cer --append" -ForegroundColor Gray
    Write-Host ""
    Write-Host "2. 'Public client not allowed' (T6 silent-fail) — should NOT occur under this refactor:" -ForegroundColor White
    Write-Host "   - This script now uses confidential-client cert-based auth" -ForegroundColor Gray
    Write-Host "   - If you see this, verify no delegated fallback was added" -ForegroundColor Gray
    Write-Host ""
    Write-Host "3. Missing Graph permissions:" -ForegroundColor White
    Write-Host "   - Owning app needs FileStorageContainer.Selected (app-only)" -ForegroundColor Gray
    Write-Host "   - Owning app needs FileStorageContainerType.Manage.All (app-only)" -ForegroundColor Gray
    Write-Host "   - Check Azure Portal > App Registrations > API Permissions" -ForegroundColor Gray
    Write-Host ""
    Write-Host "4. Missing SharePoint permissions:" -ForegroundColor White
    Write-Host "   - Owning app needs Container.Selected (app-only)" -ForegroundColor Gray
    Write-Host ""
    Write-Host "5. Admin consent not granted:" -ForegroundColor White
    Write-Host "   - Check permissions show 'Granted' status" -ForegroundColor Gray
    Write-Host ""
    exit 1
}
