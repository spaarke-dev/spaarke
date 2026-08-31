# Get SPE container metadata via APP-ONLY (confidential-client, cert-based) token
#
# WHY THIS SCRIPT EXISTS (T6 post-condition verification, spec.md FR-33):
#   scripts/Get-ContainerMetadata.ps1 acquires its Graph token via
#   `az account get-access-token` — a DELEGATED token bound to the identity of
#   whoever ran `az login`. That is exactly the auth mode T6 exists to forbid
#   for SPE resources (Microsoft Graph SPE APIs reject delegated/public
#   clients with 403 "public client not allowed"). Using the delegated script
#   to "verify" a container created via confidential-client auth would prove
#   nothing about the app-only path the BFF actually uses at runtime, and
#   could mask a genuine T6 regression by succeeding under the operator's own
#   session. This script performs the SAME GET using ONLY a confidential-
#   client cert-based token (via common/Get-SpeConfidentialClientToken.ps1),
#   matching the auth posture Create-NewContainerType.ps1 /
#   New-BusinessUnitContainer.ps1 already use (task 011 hardening).
#
# Usage: H8SpeContainerTypeHandler's ISpeContainerVerifier production impl
# invokes this AFTER container creation to prove the container is readable
# via the app-only identity path (§4D I4/I5 post-condition).

param(
    [Parameter(Mandatory)][string]$ContainerId,
    [string]$OwningAppId    = $env:API_APP_ID,
    [string]$TenantId       = $env:TENANT_ID,

    # Cert bootstrap - PRODUCTION path (KV): pass both.
    [string]$KeyVaultName   = $env:SPE_KV_NAME,
    [string]$CertSecretName = $env:SPE_CERT_SECRET_NAME,

    # Cert bootstrap - DEV FALLBACK: pass thumbprint (cert already in CurrentUser\My).
    [string]$CertThumbprint = $env:SPE_CERT_THUMBPRINT
)

$ErrorActionPreference = 'Stop'

if (-not $OwningAppId) { throw "OwningAppId required. Pass -OwningAppId or set API_APP_ID env var." }
if (-not $TenantId)    { throw "TenantId required. Pass -TenantId or set TENANT_ID env var." }

$useKeyVault = ($KeyVaultName -and $CertSecretName)
if (-not $useKeyVault -and -not $CertThumbprint) {
    throw "Cert bootstrap required. Pass -KeyVaultName + -CertSecretName (production) or -CertThumbprint (dev). Env vars: SPE_KV_NAME + SPE_CERT_SECRET_NAME, or SPE_CERT_THUMBPRINT."
}

# --- Dot-source the SPE cert-based token helper (T6 fix, same helper Create-NewContainerType.ps1 / New-BusinessUnitContainer.ps1 use) ---
. (Join-Path $PSScriptRoot 'common/Get-SpeConfidentialClientToken.ps1')

Write-Host "===============================================" -ForegroundColor Cyan
Write-Host "GET SPE CONTAINER METADATA (APP-ONLY)" -ForegroundColor Cyan
Write-Host "===============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Auth mode:    confidential-client cert-based (T6 fix)" -ForegroundColor Green
Write-Host "Container ID: $ContainerId" -ForegroundColor White
Write-Host ""

$tokenArgs = @{
    TenantId = $TenantId
    ClientId = $OwningAppId
    Scope    = 'https://graph.microsoft.com/.default'
}
if ($useKeyVault) {
    $tokenArgs.KeyVaultName   = $KeyVaultName
    $tokenArgs.CertSecretName = $CertSecretName
}
else {
    $tokenArgs.CertThumbprint = $CertThumbprint
}

try {
    $token = Get-SpeConfidentialClientToken @tokenArgs
}
catch {
    Write-Host "Failed to acquire app-only Graph token via cert-based confidential-client flow." -ForegroundColor Red
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host "App-only Graph token acquired." -ForegroundColor Green
Write-Host ""

$headers = @{
    "Authorization" = "Bearer $token"
    "Accept"        = "application/json"
}

$uri = "https://graph.microsoft.com/beta/storage/fileStorage/containers/$ContainerId"

try {
    $response = Invoke-RestMethod -Uri $uri -Method Get -Headers $headers -ErrorAction Stop

    Write-Host "CONTAINER VERIFIED (app-only GET returned 200)" -ForegroundColor Green
    Write-Host ""
    Write-Host "Container ID:   $($response.id)" -ForegroundColor White
    Write-Host "Display Name:   $($response.displayName)" -ForegroundColor White
    Write-Host "Status:         $($response.status)" -ForegroundColor Yellow
    Write-Host ""

    # T6-cleared log line (parity with Create-NewContainerType.ps1 / New-BusinessUnitContainer.ps1).
    Write-Host "T6 cleared: container ID $($response.id) verified via app-only (confidential-client cert-based) token. Status: $($response.status)" -ForegroundColor Green
    Write-Host ""
}
catch {
    Write-Host "ERROR" -ForegroundColor Red
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
        } catch {
            Write-Host "Could not read error response" -ForegroundColor Gray
        }
    }

    Write-Host ""
    Write-Host "If you see 'public client not allowed' — this SHOULD NOT occur under this script's" -ForegroundColor Yellow
    Write-Host "cert-based confidential-client flow. Investigate before retrying." -ForegroundColor Yellow
    exit 1
}
