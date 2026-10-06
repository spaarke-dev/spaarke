# ═════════════════════════════════════════════════════════════════════════════
# RETIRED 2026-09-28 (customer-provisioning-orchestration-r1 task 215)
# ─────────────────────────────────────────────────────────────────────────────
# This script is ARCHIVED and no longer functional. Do NOT invoke.
#
# Why retired:
#   - Targets cert `spe-app-cert` (thumbprint 269691A5A60536050FA76C0163BD4A942ECD724D)
#     which drifted 2026-08-30 (task 213.2 H8 live-test evidence) — the KV thumbprint
#     is NOT registered on any Spaarke app-reg, so the downstream Register-BffApi
#     call would fail with app-only-auth 403.
#   - H8-B (task 214, commit a26e30dd2 SESSION 21) removed the pre-H8-B cert
#     convention entirely. New SpeConfidentialClientGraphFactory reads cert from
#     `SPE-OwnerCert-Pfx` per topology doc §3A E-1 per-owning-app-cert convention.
#   - KV `spe-app-cert` + `spe-app-cert-pass` scheduled for soft-delete under
#     task 215 (operator-run az command; 90-day recovery window). After soft-delete
#     the `az keyvault secret download` in Step 1 of this script would 404.
#
# Preserved for historical reference of the pre-H8-B bootstrap flow only.
# See: projects/customer-provisioning-orchestration-r1/tasks/215-cert-retirement-spe-app-cert.poml
# ═════════════════════════════════════════════════════════════════════════════

throw "Import-And-Register.ps1 is RETIRED (task 215, 2026-09-28). See header comment. Use H8-B provisioning flow via /provision-environment L3 skill instead."

# Import Certificate and Register BFF API
# This script combines all steps for convenience

param(
    [string]$VaultName = "spaarke-spekvcert",
    [string]$CertName = "spe-app-cert",
    [string]$PasswordSecretName = "spe-app-cert-pass",
    [string]$DownloadPath = "C:\temp\spe-app-cert.pfx",
    [string]$ExpectedThumbprint = "269691A5A60536050FA76C0163BD4A942ECD724D"
)

Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "IMPORT CERTIFICATE AND REGISTER BFF API" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# Step 1: Download certificate
Write-Host "Step 1: Downloading certificate from Key Vault..." -ForegroundColor Yellow
New-Item -ItemType Directory -Force -Path "C:\temp" | Out-Null

az keyvault secret download `
    --vault-name $VaultName `
    --name $CertName `
    --file $DownloadPath `
    --encoding base64

if (-not (Test-Path $DownloadPath)) {
    Write-Host "❌ Failed to download certificate" -ForegroundColor Red
    exit 1
}

Write-Host "✅ Certificate downloaded" -ForegroundColor Green
Write-Host ""

# Step 2: Get password
Write-Host "Step 2: Getting certificate password..." -ForegroundColor Yellow
$certPassword = az keyvault secret show `
    --vault-name $VaultName `
    --name $PasswordSecretName `
    --query value `
    --output tsv

if (-not $certPassword) {
    Write-Host "❌ Failed to get certificate password" -ForegroundColor Red
    exit 1
}

Write-Host "✅ Certificate password retrieved" -ForegroundColor Green
Write-Host ""

# Step 3: Import certificate
Write-Host "Step 3: Importing certificate to CurrentUser\My store..." -ForegroundColor Yellow
$securePassword = ConvertTo-SecureString -String $certPassword -AsPlainText -Force

try {
    Import-PfxCertificate `
        -FilePath $DownloadPath `
        -CertStoreLocation Cert:\CurrentUser\My `
        -Password $securePassword `
        -Exportable | Out-Null

    Write-Host "✅ Certificate imported" -ForegroundColor Green
} catch {
    Write-Host "❌ Failed to import certificate: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
Write-Host ""

# Step 4: Verify certificate
Write-Host "Step 4: Verifying certificate installation..." -ForegroundColor Yellow
$cert = Get-ChildItem -Path Cert:\CurrentUser\My | Where-Object {
    $_.Thumbprint -eq $ExpectedThumbprint
}

if (-not $cert) {
    Write-Host "❌ Certificate not found after import" -ForegroundColor Red
    exit 1
}

Write-Host "✅ Certificate verified" -ForegroundColor Green
Write-Host "   Subject: $($cert.Subject)" -ForegroundColor Gray
Write-Host "   Thumbprint: $($cert.Thumbprint)" -ForegroundColor Gray
Write-Host "   Expires: $($cert.NotAfter)" -ForegroundColor Gray
Write-Host "   Has Private Key: $($cert.HasPrivateKey)" -ForegroundColor Gray
Write-Host ""

# Step 5: Run registration script
Write-Host "Step 5: Running certificate-based registration..." -ForegroundColor Yellow
Write-Host ""

$scriptPath = Join-Path $PSScriptRoot "Register-BffApi-WithCertificate.ps1"
& $scriptPath

Write-Host ""
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "NEXT STEPS" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "1. Restart BFF API to clear MSAL cache:" -ForegroundColor White
Write-Host "   az webapp restart --name spe-api-dev-67e2xz" -ForegroundColor Gray
Write-Host ""
Write-Host "2. Test OBO upload endpoint" -ForegroundColor White
Write-Host "   Should return HTTP 200 OK (not 403 Forbidden)" -ForegroundColor Gray
Write-Host ""

# Cleanup
Write-Host "Cleaning up downloaded PFX file..." -ForegroundColor Gray
Remove-Item -Path $DownloadPath -Force -ErrorAction SilentlyContinue
