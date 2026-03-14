# Register BFF API with Container Type using Certificate Authentication
# SharePoint Embedded requires certificate auth for container type management APIs

param(
    [string]$ContainerTypeId = "8a6ce34c-6055-4681-8f87-2f4f9f921c06",
    [string]$BffApiAppId = "1e40baad-e065-4aea-a8d4-4b7ab273458c",
    [string]$OwningAppId = "170c98e1-d486-4355-bcbe-170454e0207c",
    [string]$TenantId = "a221a95e-6abc-4434-aecc-e48338a1b2f2",
    [string]$CertificateThumbprint = "269691A5A60536050FA76C0163BD4A942ECD724D"
)

Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "REGISTER BFF API WITH CERTIFICATE AUTH" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "SharePoint Embedded requires certificate authentication" -ForegroundColor Yellow
Write-Host "for container type management APIs" -ForegroundColor Yellow
Write-Host ""
Write-Host "Certificate Thumbprint: $CertificateThumbprint" -ForegroundColor White
Write-Host "Owning App: $OwningAppId" -ForegroundColor White
Write-Host ""

# Step 1: Find certificate in local store
Write-Host "Step 1: Looking for certificate in local store..." -ForegroundColor Yellow

$cert = Get-ChildItem -Path Cert:\CurrentUser\My | Where-Object { $_.Thumbprint -eq $CertificateThumbprint }

if (-not $cert) {
    $cert = Get-ChildItem -Path Cert:\LocalMachine\My | Where-Object { $_.Thumbprint -eq $CertificateThumbprint }
}

if (-not $cert) {
    Write-Host "❌ Certificate not found in local store" -ForegroundColor Red
    Write-Host ""
    Write-Host "The certificate needs to be imported from Azure Key Vault:" -ForegroundColor Yellow
    Write-Host "  1. Download from Key Vault: spaarke-spekvcert" -ForegroundColor Gray
    Write-Host "  2. Certificate name: spe-app-cert" -ForegroundColor Gray
    Write-Host "  3. Import to CurrentUser\My certificate store" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Using Azure CLI:" -ForegroundColor Cyan
    Write-Host "  az keyvault secret download --vault-name spaarke-spekvcert --name spe-app-cert --file cert.pfx --encoding base64" -ForegroundColor Gray
    Write-Host "  # Then get password from:" -ForegroundColor Gray
    Write-Host "  az keyvault secret show --vault-name spaarke-spekvcert --name spe-app-cert-pass --query value -o tsv" -ForegroundColor Gray
    Write-Host "  # Import PFX to certificate store" -ForegroundColor Gray
    exit 1
}

Write-Host "✅ Found certificate" -ForegroundColor Green
Write-Host "   Subject: $($cert.Subject)" -ForegroundColor Gray
Write-Host "   Expires: $($cert.NotAfter)" -ForegroundColor Gray
Write-Host ""

# Step 2: Get token using certificate
Write-Host "Step 2: Getting SharePoint token with certificate..." -ForegroundColor Yellow

# Use Connect-AzAccount with certificate
try {
    # Install MSAL.PS if needed
    if (-not (Get-Module -ListAvailable -Name MSAL.PS)) {
        Write-Host "Installing MSAL.PS module..." -ForegroundColor Gray
        Install-Module -Name MSAL.PS -Scope CurrentUser -Force
    }

    Import-Module MSAL.PS

    # Get token using certificate
    $token = Get-MsalToken `
        -ClientId $OwningAppId `
        -TenantId $TenantId `
        -ClientCertificate $cert `
        -Scopes "https://spaarke.sharepoint.com/.default"

    Write-Host "✅ Got token with certificate auth" -ForegroundColor Green
    Write-Host ""

    # Step 3: Register applications
    Write-Host "Step 3: Registering applications with container type..." -ForegroundColor Yellow

    $registrationBody = @{
        value = @(
            @{
                appId = $OwningAppId
                delegated = @("full")
                appOnly = @("full")
            },
            @{
                appId = $BffApiAppId
                delegated = @("WriteContent", "ReadContent")
                appOnly = @()
            }
        )
    } | ConvertTo-Json -Depth 3

    $headers = @{
        "Authorization" = "Bearer $($token.AccessToken)"
        "Content-Type" = "application/json"
        "Accept" = "application/json"
    }

    $uri = "https://spaarke.sharepoint.com/_api/v2.1/storageContainerTypes/$ContainerTypeId/applicationPermissions"

    Write-Host "Calling: PUT $uri" -ForegroundColor Gray
    Write-Host ""

    $response = Invoke-RestMethod -Uri $uri `
        -Method Put `
        -Headers $headers `
        -Body $registrationBody `
        -ErrorAction Stop

    Write-Host "✅ REGISTRATION SUCCESSFUL!" -ForegroundColor Green
    Write-Host ""
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "REGISTERED APPLICATIONS" -ForegroundColor Cyan
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host ""

    foreach ($app in $response.value) {
        Write-Host "App ID: $($app.appId)" -ForegroundColor White
        Write-Host "  Delegated: $($app.delegated -join ', ')" -ForegroundColor Green
        Write-Host "  App-Only: $($app.appOnly -join ', ')" -ForegroundColor Gray
        Write-Host ""
    }

    Write-Host "NEXT STEPS:" -ForegroundColor Cyan
    Write-Host "1. Restart BFF API: az webapp restart --name spe-api-dev-67e2xz" -ForegroundColor White
    Write-Host "2. Test OBO upload - should work now!" -ForegroundColor White
    Write-Host ""

} catch {
    Write-Host "❌ REGISTRATION FAILED" -ForegroundColor Red
    Write-Host ""
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red

    if ($_.ErrorDetails.Message) {
        try {
            $errorJson = $_.ErrorDetails.Message | ConvertFrom-Json
            Write-Host ""
            Write-Host "Error Details:" -ForegroundColor Yellow
            Write-Host "  Code: $($errorJson.error.code)" -ForegroundColor Red
            Write-Host "  Message: $($errorJson.error.message)" -ForegroundColor Red
        } catch {
            Write-Host "  $($_.ErrorDetails.Message)" -ForegroundColor Gray
        }
    }
}
