# Setup-LocalAuth.ps1
# Configure local development authentication using environment variables

Write-Host "=== SDAP Local Development Authentication Setup ===" -ForegroundColor Yellow
Write-Host ""
Write-Host "This script helps configure local authentication for SPE APIs." -ForegroundColor Gray
Write-Host ""

$tenantId = "a221a95e-6abc-4434-aecc-e48338a1b2f2"
$clientId = "170c98e1-d486-4355-bcbe-170454e0207c"

Write-Host "App Registration: Spaarke DSM-SPE Dev 2" -ForegroundColor Cyan
Write-Host "  Client ID: $clientId" -ForegroundColor Gray
Write-Host "  Tenant ID: $tenantId" -ForegroundColor Gray
Write-Host ""

# Check if secret is already set
$currentSecret = $env:AZURE_CLIENT_SECRET
if ($currentSecret) {
    Write-Host "✓ AZURE_CLIENT_SECRET is already set" -ForegroundColor Green
    Write-Host "  Hint: $($currentSecret.Substring(0, [Math]::Min(3, $currentSecret.Length)))..." -ForegroundColor Gray
    Write-Host ""
    $continue = Read-Host "Do you want to update it? (y/N)"
    if ($continue -ne 'y' -and $continue -ne 'Y') {
        Write-Host "Keeping existing secret." -ForegroundColor Yellow
        exit 0
    }
}

Write-Host "Please enter the client secret for 'Spaarke DSM-SPE Dev 2':" -ForegroundColor Cyan
Write-Host "  (Available secrets: 'SharePointEmbeddedVSCode' or 'SPE Dev 2 Functions Secret')" -ForegroundColor Gray
Write-Host ""
$secret = Read-Host "Client Secret" -AsSecureString
$secretPlainText = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto(
    [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret))

if ([string]::IsNullOrWhiteSpace($secretPlainText)) {
    Write-Host "✗ No secret provided. Exiting." -ForegroundColor Red
    exit 1
}

# Set environment variables for current session
$env:AZURE_TENANT_ID = $tenantId
$env:AZURE_CLIENT_ID = $clientId
$env:AZURE_CLIENT_SECRET = $secretPlainText

Write-Host ""
Write-Host "✓ Environment variables set for current PowerShell session:" -ForegroundColor Green
Write-Host "  AZURE_TENANT_ID=$tenantId" -ForegroundColor Gray
Write-Host "  AZURE_CLIENT_ID=$clientId" -ForegroundColor Gray
Write-Host "  AZURE_CLIENT_SECRET=***" -ForegroundColor Gray
Write-Host ""

Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Run the API from THIS PowerShell window:" -ForegroundColor Gray
Write-Host "   cd src/api/Spe.Bff.Api && dotnet run" -ForegroundColor White
Write-Host ""
Write-Host "2. Test the SPE APIs:" -ForegroundColor Gray
Write-Host "   .\Test-SpeApis.ps1 -ContainerTypeId '8a6ce34c-6055-4681-8f87-2f4f9f921c06'" -ForegroundColor White
Write-Host ""
Write-Host "Note: These variables only persist in THIS session." -ForegroundColor Yellow
Write-Host "To persist them permanently, add to your PowerShell profile or use .NET User Secrets." -ForegroundColor Yellow
Write-Host ""

# Offer to save to user secrets
$saveUserSecrets = Read-Host "Would you like to save to .NET User Secrets? (y/N)"
if ($saveUserSecrets -eq 'y' -or $saveUserSecrets -eq 'Y') {
    Write-Host ""
    Write-Host "Setting up .NET User Secrets..." -ForegroundColor Cyan

    Push-Location "src/api/Spe.Bff.Api"

    # Initialize user secrets if not already done
    $hasUserSecrets = dotnet user-secrets list 2>&1 | Select-String "UserSecretsId"
    if (-not $hasUserSecrets) {
        Write-Host "  Initializing user secrets..." -ForegroundColor Gray
        dotnet user-secrets init | Out-Null
    }

    # Set the secrets
    dotnet user-secrets set "AZURE_TENANT_ID" $tenantId | Out-Null
    dotnet user-secrets set "AZURE_CLIENT_ID" $clientId | Out-Null
    dotnet user-secrets set "AZURE_CLIENT_SECRET" $secretPlainText | Out-Null
    dotnet user-secrets set "API_CLIENT_SECRET" $secretPlainText | Out-Null

    Pop-Location

    Write-Host "  ✓ Saved to User Secrets" -ForegroundColor Green
    Write-Host "  You can now run the API from any terminal without setting env vars" -ForegroundColor Gray
}

Write-Host ""
Write-Host "Setup complete! 🎉" -ForegroundColor Green
