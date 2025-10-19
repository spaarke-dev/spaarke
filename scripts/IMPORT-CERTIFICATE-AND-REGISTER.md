# Import Certificate and Register BFF API with Container Type

## Why Certificate Authentication is Required

SharePoint Embedded container type management APIs **REQUIRE certificate authentication**, not client secret. This is documented in Microsoft's official documentation:

> "You will need to use the client credentials grant flow and **request a token with a certificate**"

This explains why all previous registration attempts failed with "invalidToken" - we were using client secret authentication.

## Certificate Details

- **Key Vault**: `spaarke-spekvcert`
- **Certificate Name**: `spe-app-cert`
- **Password Secret**: `spe-app-cert-pass`
- **Expected Thumbprint**: `269691A5A60536050FA76C0163BD4A942ECD724D`

## Step 1: Download Certificate from Azure Key Vault

```powershell
# Set variables
$vaultName = "spaarke-spekvcert"
$certName = "spe-app-cert"
$passwordSecretName = "spe-app-cert-pass"
$downloadPath = "C:\temp\spe-app-cert.pfx"

# Ensure download directory exists
New-Item -ItemType Directory -Force -Path "C:\temp" | Out-Null

# Download certificate (it's stored as a secret in PFX format)
Write-Host "Downloading certificate from Key Vault..." -ForegroundColor Yellow
az keyvault secret download `
    --vault-name $vaultName `
    --name $certName `
    --file $downloadPath `
    --encoding base64

# Get certificate password
Write-Host "Getting certificate password..." -ForegroundColor Yellow
$certPassword = az keyvault secret show `
    --vault-name $vaultName `
    --name $passwordSecretName `
    --query value `
    --output tsv

Write-Host "Certificate downloaded to: $downloadPath" -ForegroundColor Green
Write-Host "Certificate password retrieved from Key Vault" -ForegroundColor Green
```

## Step 2: Import Certificate to Local Certificate Store

```powershell
# Import certificate to CurrentUser\My store
Write-Host "Importing certificate to CurrentUser\My store..." -ForegroundColor Yellow

$securePassword = ConvertTo-SecureString -String $certPassword -AsPlainText -Force

Import-PfxCertificate `
    -FilePath $downloadPath `
    -CertStoreLocation Cert:\CurrentUser\My `
    -Password $securePassword `
    -Exportable

Write-Host "Certificate imported successfully!" -ForegroundColor Green
```

## Step 3: Verify Certificate Installation

```powershell
# Verify certificate is installed
Write-Host ""
Write-Host "Verifying certificate installation..." -ForegroundColor Yellow

$cert = Get-ChildItem -Path Cert:\CurrentUser\My | Where-Object {
    $_.Thumbprint -eq "269691A5A60536050FA76C0163BD4A942ECD724D"
}

if ($cert) {
    Write-Host "✅ Certificate found!" -ForegroundColor Green
    Write-Host "   Subject: $($cert.Subject)" -ForegroundColor White
    Write-Host "   Thumbprint: $($cert.Thumbprint)" -ForegroundColor White
    Write-Host "   Expires: $($cert.NotAfter)" -ForegroundColor White
    Write-Host "   Has Private Key: $($cert.HasPrivateKey)" -ForegroundColor White
} else {
    Write-Host "❌ Certificate NOT found in CurrentUser\My" -ForegroundColor Red
    Write-Host "Checking LocalMachine\My..." -ForegroundColor Yellow

    $cert = Get-ChildItem -Path Cert:\LocalMachine\My | Where-Object {
        $_.Thumbprint -eq "269691A5A60536050FA76C0163BD4A942ECD724D"
    }

    if ($cert) {
        Write-Host "✅ Certificate found in LocalMachine\My!" -ForegroundColor Green
    } else {
        Write-Host "❌ Certificate not found in either location" -ForegroundColor Red
        exit 1
    }
}
```

## Step 4: Run Certificate-Based Registration Script

```powershell
# Run the certificate-based registration script
Write-Host ""
Write-Host "Running certificate-based registration..." -ForegroundColor Cyan
Write-Host ""

cd c:\code_files\spaarke\scripts
.\Register-BffApi-WithCertificate.ps1
```

## Step 5: Verify Registration Success

After running the script, you should see:

```
✅ REGISTRATION SUCCESSFUL!

═══════════════════════════════════════════════
REGISTERED APPLICATIONS
═══════════════════════════════════════════════

App ID: 170c98e1-d486-4355-bcbe-170454e0207c
  Delegated: full
  App-Only: full

App ID: 1e40baad-e065-4aea-a8d4-4b7ab273458c
  Delegated: WriteContent, ReadContent
  App-Only:
```

## Step 6: Restart BFF API (Clear MSAL Cache)

```bash
# Restart the Azure App Service to clear MSAL token cache
az webapp restart --name spe-api-dev-67e2xz --resource-group <resource-group-name>
```

## Step 7: Test OBO Upload

After restart, test the OBO upload endpoint that was returning 403:

```http
PUT https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/containers/{containerId}/files/test.txt
Authorization: Bearer <user-token>
Content-Type: text/plain

Hello World - Testing OBO Upload
```

**Expected Result**: `HTTP 200 OK` (not 403 Forbidden)

## Complete PowerShell Script (All Steps Combined)

Save this as `Import-And-Register.ps1`:

```powershell
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
```

## Troubleshooting

### Error: "Certificate not found after import"

**Solution**: Check if certificate was imported to LocalMachine\My instead:

```powershell
Get-ChildItem -Path Cert:\LocalMachine\My | Where-Object {
    $_.Thumbprint -eq "269691A5A60536050FA76C0163BD4A942ECD724D"
}
```

If found there, update the registration script to look in LocalMachine instead of CurrentUser.

### Error: "Failed to download certificate"

**Possible Causes**:
- Not logged into Azure CLI: Run `az login`
- No access to Key Vault: Check permissions
- Certificate doesn't exist: Verify name is `spe-app-cert`

### Error: "MSAL.PS module not found"

**Solution**: The registration script will auto-install, but you can pre-install:

```powershell
Install-Module -Name MSAL.PS -Scope CurrentUser -Force
```

### Registration Still Fails

**If you still get 401 errors after using certificate**:

1. Verify certificate is attached to PCF app registration in Azure Portal
2. Check certificate hasn't expired
3. Ensure certificate thumbprint in Azure matches local certificate
4. Verify PCF app has `Container.Selected` permission for SharePoint (not Graph)

## Summary

This process:
1. Downloads the certificate from Azure Key Vault
2. Imports it to your local certificate store
3. Uses MSAL.PS to get a token with certificate authentication
4. Calls the SharePoint Embedded registration API with the certificate-authenticated token
5. Registers both PCF app (owning) and BFF API (guest) with the container type

**Why this will work**: SharePoint Embedded APIs reject tokens acquired with client secrets but accept tokens acquired with certificates. This is a security requirement for container type management.
