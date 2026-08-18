# Register owning app with SharePoint Embedded Container Type
# The owning app (BFF API) gets full delegated + full appOnly permissions
#
# T6 FIX (spec.md FR-11 + § MUST rules): This script now acquires its
# SharePoint token via confidential-client CERT-BASED flow. The prior
# client_secret path was removed on 2026-08-17 by task 011 because
# Microsoft Graph SPE APIs reject public/delegated clients with 403
# "public client not allowed" (silent-fail trap T6, design.md §4B).
#
# Prerequisite (H0 preflight):
#   Cert uploaded to KV as PFX secret AND registered on the owning app
#   registration (24h SPE cert-replication window per FR-01).

param(
    [string]$ContainerTypeId  = $env:SPE_CONTAINER_TYPE_ID,
    [string]$OwningAppId      = $env:API_APP_ID,
    [string]$TenantId         = $env:TENANT_ID,
    [string]$SharePointDomain = $env:SHAREPOINT_DOMAIN,   # e.g., "spaarke.sharepoint.com"

    # Cert bootstrap — PRODUCTION path (KV): pass both.
    [string]$KeyVaultName     = $env:SPE_KV_NAME,
    [string]$CertSecretName   = $env:SPE_CERT_SECRET_NAME,

    # Cert bootstrap — DEV FALLBACK: pass thumbprint (cert already in CurrentUser\My).
    [string]$CertThumbprint   = $env:SPE_CERT_THUMBPRINT
)

$ErrorActionPreference = 'Stop'

if (-not $ContainerTypeId)  { throw "ContainerTypeId required. Pass -ContainerTypeId or set SPE_CONTAINER_TYPE_ID env var." }
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
Write-Host "REGISTER OWNING APP WITH CONTAINER TYPE" -ForegroundColor Cyan
Write-Host "===============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Auth mode:         confidential-client cert-based (T6 fix, FR-11)" -ForegroundColor Green
Write-Host "Cert source:       $(if ($useKeyVault) { "Key Vault '$KeyVaultName' secret '$CertSecretName'" } else { "CurrentUser cert store thumbprint $CertThumbprint" })" -ForegroundColor Gray
Write-Host ""
Write-Host "Container Type ID: $ContainerTypeId" -ForegroundColor White
Write-Host "Owning App ID:     $OwningAppId" -ForegroundColor White
Write-Host "SharePoint Domain: $SharePointDomain" -ForegroundColor White
Write-Host ""

try {
    # Step 1: Get SharePoint token (confidential-client, cert-based)
    Write-Host "Step 1: Acquiring SharePoint access token (confidential-client, cert-based)..." -ForegroundColor Yellow

    $tokenArgs = @{
        TenantId = $TenantId
        ClientId = $OwningAppId
        Scope    = "https://$SharePointDomain/.default"
    }
    if ($useKeyVault) {
        $tokenArgs.KeyVaultName   = $KeyVaultName
        $tokenArgs.CertSecretName = $CertSecretName
    }
    else {
        $tokenArgs.CertThumbprint = $CertThumbprint
    }

    $accessToken = Get-SpeConfidentialClientToken @tokenArgs

    Write-Host "Got SharePoint access token" -ForegroundColor Green
    Write-Host ""

    # Step 2: Register owning app with container type (full permissions)
    Write-Host "Step 2: Registering owning app with container type..." -ForegroundColor Yellow
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

    $headers = @{
        "Authorization" = "Bearer $accessToken"
        "Content-Type"  = "application/json"
        "Accept"        = "application/json"
    }

    $uri = "https://$SharePointDomain/_api/v2.1/storageContainerTypes/$ContainerTypeId/applicationPermissions"

    Write-Host "Calling: PUT $uri" -ForegroundColor Gray
    Write-Host ""

    $response = Invoke-RestMethod -Uri $uri `
        -Method Put `
        -Headers $headers `
        -Body $registrationBody `
        -ErrorAction Stop

    Write-Host "REGISTRATION SUCCESSFUL!" -ForegroundColor Green
    Write-Host ""
    Write-Host "T6 cleared: container-type $ContainerTypeId registration applied via confidential-client cert-based auth." -ForegroundColor Green
    Write-Host ""
    Write-Host "===============================================" -ForegroundColor Cyan
    Write-Host "REGISTRATION RESULT" -ForegroundColor Cyan
    Write-Host "===============================================" -ForegroundColor Cyan
    Write-Host ""

    if ($response.value) {
        foreach ($perm in $response.value) {
            Write-Host "App ID:              $($perm.appId)" -ForegroundColor White
            Write-Host "App Display Name:    $($perm.appDisplayName)" -ForegroundColor White
            Write-Host "Delegated Perms:     $($perm.delegated -join ', ')" -ForegroundColor Green
            Write-Host "App-Only Perms:      $($perm.appOnly -join ', ')" -ForegroundColor Gray
            Write-Host ""
        }
    } else {
        $response | ConvertTo-Json -Depth 5
    }

    Write-Host "===============================================" -ForegroundColor Cyan
    Write-Host "NEXT STEPS" -ForegroundColor Cyan
    Write-Host "===============================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "1. Restart the BFF API app service to clear MSAL cache:" -ForegroundColor White
    Write-Host "   az webapp restart --name <app-service-name> --resource-group <rg-name>" -ForegroundColor Gray
    Write-Host ""
    Write-Host "2. Test the upload endpoint:" -ForegroundColor White
    Write-Host "   PUT /api/containers/{containerId}/files/test.txt" -ForegroundColor Gray
    Write-Host ""
    Write-Host "3. Expected result: HTTP 200 OK (not 403 Forbidden)" -ForegroundColor White
    Write-Host ""

} catch {
    Write-Host "REGISTRATION FAILED" -ForegroundColor Red
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
                # Not JSON, already displayed
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
    Write-Host "If error is 'invalid_client' / 'AADSTS700027' (cert not trusted):" -ForegroundColor Yellow
    Write-Host "  - Verify cert public key is attached to app registration:" -ForegroundColor Gray
    Write-Host "     az ad app credential list --id $OwningAppId" -ForegroundColor Gray
    Write-Host "  - Re-append if missing: az ad app credential reset --id <app> --cert @cert.cer --append" -ForegroundColor Gray
    Write-Host ""
    Write-Host "If error is 'Public client not allowed' (T6) — should NOT occur under this refactor:" -ForegroundColor Yellow
    Write-Host "  - This script now uses confidential-client cert-based auth" -ForegroundColor Gray
    Write-Host "  - If you see this, verify no delegated fallback was added" -ForegroundColor Gray
    Write-Host ""
    Write-Host "If error is 'Access denied' or '401/403':" -ForegroundColor Yellow
    Write-Host "  - Verify the OwningAppId is the actual owner of this container type" -ForegroundColor Gray
    Write-Host "  - Use Find-ContainerTypeOwner.ps1 to check" -ForegroundColor Gray
    Write-Host "  - Requires SharePoint admin access to query container types" -ForegroundColor Gray
    Write-Host ""
    Write-Host "If error is 'Container.Selected permission required':" -ForegroundColor Yellow
    Write-Host "  - Grant FileStorageContainer.Selected permission to the owning app" -ForegroundColor Gray
    Write-Host "  - Azure Portal > App Registrations > owning app > API Permissions" -ForegroundColor Gray
    Write-Host "  - Add Microsoft Graph > Application > FileStorageContainer.Selected" -ForegroundColor Gray
    Write-Host "  - Grant admin consent" -ForegroundColor Gray
    Write-Host ""
    exit 1
}
