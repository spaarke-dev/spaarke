<#
.SYNOPSIS
    Sets up Azure Service Bus infrastructure for Office Add-in workers.

.DESCRIPTION
    Creates Service Bus namespace, queues, and stores connection string in Key Vault.
    Required for Office Add-in email/document processing pipeline.

.NOTES
    Prerequisites:
    - Azure CLI logged in (az login)
    - Appropriate permissions on resource group and Key Vault
#>

param(
    [Parameter(Mandatory=$false)]
    [string]$ResourceGroup = "SharePointEmbedded",

    [Parameter(Mandatory=$false)]
    [string]$Location = "eastus",

    [Parameter(Mandatory=$false)]
    [string]$NamespaceName = "spaarke-servicebus-dev",

    [Parameter(Mandatory=$false)]
    [string]$KeyVaultName = "spaarke-spekvcert",

    [Parameter(Mandatory=$false)]
    [string]$AppServiceResourceGroup = "spe-infrastructure-westus2"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Office Service Bus Setup ===" -ForegroundColor Cyan
Write-Host "Service Bus Resource Group: $ResourceGroup"
Write-Host "App Service Resource Group: $AppServiceResourceGroup"
Write-Host "Location: $Location"
Write-Host "Namespace: $NamespaceName"
Write-Host ""

# Step 1: Create Service Bus namespace
Write-Host "[1/5] Creating Service Bus namespace..." -ForegroundColor Yellow
$namespace = az servicebus namespace show `
    --resource-group $ResourceGroup `
    --name $NamespaceName `
    2>$null | ConvertFrom-Json

if ($namespace) {
    Write-Host "  Namespace already exists: $NamespaceName" -ForegroundColor Green
} else {
    Write-Host "  Creating new namespace: $NamespaceName"

    $createResult = az servicebus namespace create `
        --resource-group $ResourceGroup `
        --name $NamespaceName `
        --location $Location `
        --sku Standard `
        --tags Environment=Development Project=SDAP 2>&1

    if ($LASTEXITCODE -ne 0) {
        Write-Host "  ERROR: Failed to create namespace" -ForegroundColor Red
        Write-Host $createResult
        exit 1
    }

    Write-Host "  Waiting for namespace provisioning..." -ForegroundColor Gray

    # Wait for namespace to be ready (max 2 minutes)
    $maxAttempts = 24
    $attempt = 0
    $ready = $false

    while ($attempt -lt $maxAttempts -and -not $ready) {
        Start-Sleep -Seconds 5
        $ns = az servicebus namespace show `
            --resource-group $ResourceGroup `
            --name $NamespaceName `
            2>$null | ConvertFrom-Json

        if ($ns -and $ns.status -eq "Active") {
            $ready = $true
            Write-Host "  Namespace created and active" -ForegroundColor Green
        } else {
            $attempt++
            Write-Host "  ...waiting ($attempt/$maxAttempts)" -ForegroundColor Gray
        }
    }

    if (-not $ready) {
        Write-Host "  ERROR: Namespace creation timed out" -ForegroundColor Red
        exit 1
    }
}

# Step 2: Create queues
Write-Host ""
Write-Host "[2/5] Creating Service Bus queues..." -ForegroundColor Yellow

$queues = @(
    @{
        Name = "office-upload-finalization"
        MaxDeliveryCount = 5
        Description = "Processes file uploads and creates Dataverse records"
    },
    @{
        Name = "office-profile"
        MaxDeliveryCount = 3
        Description = "Generates AI document profiles"
    },
    @{
        Name = "office-indexing"
        MaxDeliveryCount = 3
        Description = "Indexes documents in Azure AI Search"
    }
)

foreach ($queueConfig in $queues) {
    $existing = az servicebus queue show `
        --resource-group $ResourceGroup `
        --namespace-name $NamespaceName `
        --name $queueConfig.Name `
        2>$null | ConvertFrom-Json

    if ($existing) {
        Write-Host "  Queue already exists: $($queueConfig.Name)" -ForegroundColor Green
    } else {
        Write-Host "  Creating queue: $($queueConfig.Name)"

        $queueResult = az servicebus queue create `
            --resource-group $ResourceGroup `
            --namespace-name $NamespaceName `
            --name $queueConfig.Name `
            --max-delivery-count $queueConfig.MaxDeliveryCount `
            --default-message-time-to-live P7D `
            --enable-dead-lettering-on-message-expiration true `
            --lock-duration PT5M 2>&1

        if ($LASTEXITCODE -ne 0) {
            Write-Host "  ERROR: Failed to create queue $($queueConfig.Name)" -ForegroundColor Red
            Write-Host $queueResult
            exit 1
        }

        Write-Host "    Created: $($queueConfig.Name)" -ForegroundColor Green
    }
}

# Step 3: Get connection string
Write-Host ""
Write-Host "[3/5] Retrieving connection string..." -ForegroundColor Yellow

$connectionString = az servicebus namespace authorization-rule keys list `
    --resource-group $ResourceGroup `
    --namespace-name $NamespaceName `
    --name RootManageSharedAccessKey `
    --query primaryConnectionString `
    --output tsv

if (-not $connectionString) {
    Write-Host "  ERROR: Failed to retrieve connection string" -ForegroundColor Red
    exit 1
}

Write-Host "  Connection string retrieved" -ForegroundColor Green

# Step 4: Store in Key Vault
Write-Host ""
Write-Host "[4/5] Storing connection string in Key Vault..." -ForegroundColor Yellow

$kvResult = az keyvault secret set `
    --vault-name $KeyVaultName `
    --name "ServiceBus-ConnectionString" `
    --value $connectionString 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Host "  ERROR: Failed to store secret in Key Vault" -ForegroundColor Red
    Write-Host $kvResult
    exit 1
}

Write-Host "  Stored secret: ServiceBus-ConnectionString" -ForegroundColor Green

# Step 5: Configure App Service
Write-Host ""
Write-Host "[5/5] Updating App Service configuration..." -ForegroundColor Yellow

$keyVaultUrl = az keyvault show --name $KeyVaultName --query properties.vaultUri --output tsv

if ($LASTEXITCODE -ne 0) {
    Write-Host "  ERROR: Failed to get Key Vault URL" -ForegroundColor Red
    exit 1
}

$secretUri = "${keyVaultUrl}secrets/ServiceBus-ConnectionString"

$appResult = az webapp config appsettings set `
    --name spe-api-dev-67e2xz `
    --resource-group $AppServiceResourceGroup `
    --settings `
        "ServiceBus__ConnectionString=@Microsoft.KeyVault(SecretUri=$secretUri)" `
    --query "[?name=='ServiceBus__ConnectionString'].{Name:name,Value:value}" `
    --output table 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Host "  ERROR: Failed to configure App Service" -ForegroundColor Red
    Write-Host $appResult
    exit 1
}

Write-Host "  App Service configured" -ForegroundColor Green
Write-Host "  $appResult" -ForegroundColor Gray

# Summary
Write-Host ""
Write-Host "=== Setup Complete ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Service Bus Namespace: $NamespaceName" -ForegroundColor White
Write-Host "Queues created:" -ForegroundColor White
Write-Host "  - office-upload-finalization (max delivery: 5)" -ForegroundColor Gray
Write-Host "  - office-profile (max delivery: 3)" -ForegroundColor Gray
Write-Host "  - office-indexing (max delivery: 3)" -ForegroundColor Gray
Write-Host ""
Write-Host "Connection string stored in Key Vault: $KeyVaultName" -ForegroundColor White
Write-Host "App Service configured: spe-api-dev-67e2xz" -ForegroundColor White
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Restart the App Service to load new configuration" -ForegroundColor Gray
Write-Host "  2. Check worker logs: az webapp log tail --name spe-api-dev-67e2xz --resource-group $AppServiceResourceGroup" -ForegroundColor Gray
Write-Host "  3. Test Office Add-in email save flow" -ForegroundColor Gray
Write-Host ""
