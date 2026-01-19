# Setup-TestDocumentStorage.ps1
# Sets up Azure Blob Storage for Quick Test functionality

param(
    [string]$StorageAccountName = "sprkspaarkedevaifsa",
    [string]$ResourceGroup = "spe-infrastructure-westus2",
    [string]$AppServiceName = "spe-api-dev-67e2xz"
)

Write-Host "=== Quick Test Storage Setup ===" -ForegroundColor Cyan
Write-Host "Storage Account: $StorageAccountName"
Write-Host "Resource Group: $ResourceGroup"
Write-Host "App Service: $AppServiceName"
Write-Host ""

# Step 1: Create container
Write-Host "[1/4] Creating test-documents container..." -ForegroundColor Yellow
az storage container create --name "test-documents" --account-name $StorageAccountName --auth-mode login --output none
Write-Host "  Done" -ForegroundColor Green

# Step 2: Apply lifecycle policy
Write-Host "[2/4] Applying lifecycle policy (24hr auto-delete)..." -ForegroundColor Yellow
$PolicyPath = Join-Path $PSScriptRoot "..\infrastructure\bicep\modules\test-documents-lifecycle-policy.json"
az storage account management-policy create --account-name $StorageAccountName --resource-group $ResourceGroup --policy $PolicyPath --output none 2>$null
Write-Host "  Done" -ForegroundColor Green

# Step 3: Get connection string
Write-Host "[3/4] Retrieving connection string..." -ForegroundColor Yellow
$ConnectionString = az storage account show-connection-string --name $StorageAccountName --resource-group $ResourceGroup --query connectionString --output tsv
Write-Host "  Done" -ForegroundColor Green

# Step 4: Update App Service
Write-Host "[4/4] Updating App Service settings..." -ForegroundColor Yellow
az webapp config appsettings set --name $AppServiceName --resource-group $ResourceGroup --settings "AzureStorage:ConnectionString=$ConnectionString" --output none
Write-Host "  Done" -ForegroundColor Green

Write-Host ""
Write-Host "=== Setup Complete ===" -ForegroundColor Cyan
Write-Host "Connection String (for local testing):"
Write-Host $ConnectionString
