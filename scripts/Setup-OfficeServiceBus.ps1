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

    ============================================================================
    DEPRECATION NOTICE (2026-08-26 — task 205h A38c fold-in per owner disposition)
    ============================================================================
    This script is LEGACY dev-bootstrap and is now MOSTLY SUPERSEDED:

    - Step 1 (SB namespace) — LIVE and idempotent: `spaarke-servicebus-dev` in RG
      `SharePointEmbedded` already exists (created 2025-09-29). Canonical
      infrastructure now provisions Service Bus via Bicep — see
      `infrastructure/bicep/modules/service-bus.bicep` (used by both `platform.bicep`
      and `customer.bicep`). This step remains a safe no-op.

    - Step 2 (3 Office queues) — LIVE and idempotent: `office-upload-finalization`,
      `office-profile`, `office-indexing` all exist since 2026-01-26 and are actively
      polled by `src/server/api/Sprk.Bff.Api/Workers/Office/*.cs` (queue names are
      hardcoded `const string QueueName = ...`). Queue creation for new environments
      belongs in canonical infrastructure, not this script.

    - Step 3 (get SAS connection string) — the OUTPUT is unused by current BFF.
      auth-v4 task 051 (FR-E2) migrated ServiceBus authentication to Managed Identity
      via `ServiceBusOptions.FullyQualifiedNamespace`; see
      `src/server/api/Sprk.Bff.Api/Configuration/ServiceBusOptions.cs:19-25` +
      `src/server/api/Sprk.Bff.Api/Infrastructure/Auth/ServiceBusClientFactory.cs`.
      SAS connection strings are a bearer-secret with no rotation story and are the
      exact retirement target of the ADR-028 Amendment A4 secret-free BFF-identity
      contract (E-3 closed 2026-08-24).

    - Step 4 (write `ServiceBus-ConnectionString` to Key Vault) — GATED (see below).
      auth-v4 task 033 deliberately deleted this KV secret; re-seeding it silently
      resurrects a retired SAS auth path. This script would write to the caller-
      supplied `-KeyVaultName` (default: legacy dev vault `spaarke-spekvcert` where
      the secret was already removed). An operator overriding with a canonical vault
      name (e.g. `-KeyVaultName sprk-{env}-kv`) would resurrect the exact reversal
      vector that A38a's manifest omit contract exists to prevent. Gated below with
      `Assert-SpaarkeSecretFreeGateNotTripped` (task 205h A38c pattern).

    - Step 5 (configure App Service `spe-api-dev-67e2xz` with KV-ref) —
      HARDCODED TARGET APP SERVICE NO LONGER EXISTS. Verified 2026-08-26:
      `az webapp show --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2`
      returns ResourceNotFound. This step will fail on any invocation.

    Bottom line: this script is retained as a historical reference and remains
    partially runnable (idempotent no-ops on the SB infra it verifies), but its
    Step 4 credential-write is now gated on the secret-free migration marker,
    and its Step 5 App Service config is expected to fail against current state.
    Do not use for new environments; use canonical Bicep instead.

    Row: customer-provisioning-orchestration-r1 A38c (see
    projects/customer-provisioning-orchestration-r1/notes/auth-v4-integration-draft-punch-rows.md).
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

# A38c secret-free marker pre-check gate (task 205h fold-in 2026-08-26 per owner disposition).
# See scripts/common/Assert-SpaarkeSecretFreeGate.ps1 header for full rationale + §11 justification.
# Gates Step 4 (ServiceBus-ConnectionString KV write) ONLY — Steps 1/2 (SB namespace + queues) are
# idempotent no-ops against current live state (verified 2026-08-26) and remain unrestricted; Step 5
# targets a non-existent App Service (`spe-api-dev-67e2xz`) and will fail independently of this gate.
. (Join-Path $PSScriptRoot 'common/Assert-SpaarkeSecretFreeGate.ps1')

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

# A38c gate: refuse to re-seed the ServiceBus-ConnectionString KV secret on any environment that
# carries the auth-v4 secret-free migration marker. On secret-free envs, BFF authenticates to
# Service Bus via Managed Identity (`ServiceBus__FullyQualifiedNamespace`) — a re-seeded SAS
# connection string would silently resurrect a retired credential path. Fails LOUD if tripped
# (Write-Error + non-zero exit); passes through when marker is absent (pre-migration envs).
Assert-SpaarkeSecretFreeGateNotTripped -SecretName "ServiceBus-ConnectionString" -KeyVaultName $KeyVaultName

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
