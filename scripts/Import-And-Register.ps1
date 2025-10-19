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
