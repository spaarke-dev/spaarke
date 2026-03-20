<#
.SYNOPSIS
    Seed the production Key Vault with required secrets for BFF API startup.

.DESCRIPTION
    Populates sprk-platform-prod-kv with the minimum set of secrets required for
    the BFF API to start successfully. Uses real values where available (from existing
    App Service settings) and placeholder values where services are not yet provisioned.

    Secrets with placeholder values are marked with comments and MUST be updated
    before going live. The API will start and serve /healthz with these values,
    but feature-specific endpoints will fail until real secrets are provided.

    Prerequisite: Caller must have "Key Vault Secrets Officer" or equivalent RBAC role.

.PARAMETER VaultName
    Key Vault name. Default: sprk-platform-prod-kv

.PARAMETER SkipExisting
    Skip secrets that already exist in the vault. Default: $true

.EXAMPLE
    .\Seed-ProductionKeyVault.ps1
    # Seed all required secrets with defaults

.EXAMPLE
    .\Seed-ProductionKeyVault.ps1 -SkipExisting:$false
    # Overwrite all secrets (use when updating values)
#>

param(
    [string]$VaultName = "sprk-platform-prod-kv",
    [string]$DataverseUrl = $env:DATAVERSE_URL,
    [bool]$SkipExisting = $true
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  Production Key Vault Secret Seeding" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  Vault: $VaultName"
Write-Host "  Skip Existing: $SkipExisting"
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ""

# --- Helper ---
function Set-VaultSecret {
    param(
        [string]$Name,
        [string]$Value,
        [string]$Description,
        [bool]$IsPlaceholder = $false
    )

    if ($SkipExisting) {
        $existing = az keyvault secret show --vault-name $VaultName --name $Name --query "name" --output tsv 2>$null
        if ($existing) {
            Write-Host "  SKIP: $Name (already exists)" -ForegroundColor Gray
            return
        }
    }

    $label = if ($IsPlaceholder) { "placeholder" } else { "seeded" }
    $color = if ($IsPlaceholder) { "Yellow" } else { "Green" }
    $prefix = if ($IsPlaceholder) { "PLACEHOLDER" } else { "SET" }

    az keyvault secret set --vault-name $VaultName --name $Name --value $Value --description $Description --tags "source=seed-script" "placeholder=$IsPlaceholder" --output none 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  FAILED: $Name" -ForegroundColor Red
        return
    }
    Write-Host "  ${prefix}: $Name — $Description" -ForegroundColor $color
}

# === Tenant Identity ===
Write-Host ""
Write-Host "[1/7] Tenant Identity" -ForegroundColor Yellow

Set-VaultSecret -Name "TenantId" `
    -Value "a221a95e-6abc-4434-aecc-e48338a1b2f2" `
    -Description "Spaarke Azure AD tenant ID"

# === BFF API Authentication ===
Write-Host ""
Write-Host "[2/7] BFF API Authentication (Entra ID)" -ForegroundColor Yellow

# These should be populated by Register-EntraAppRegistrations.ps1 (task 021)
# Using placeholders if not yet run
Set-VaultSecret -Name "BFF-API-ClientId" `
    -Value "00000000-0000-0000-0000-000000000000" `
    -Description "BFF API Entra app client ID (update after Register-EntraAppRegistrations.ps1)" `
    -IsPlaceholder $true

Set-VaultSecret -Name "BFF-API-ClientSecret" `
    -Value "placeholder-update-after-entra-registration" `
    -Description "BFF API Entra app client secret (update after Register-EntraAppRegistrations.ps1)" `
    -IsPlaceholder $true

Set-VaultSecret -Name "BFF-API-Audience" `
    -Value "api://spaarke-bff-api-prod" `
    -Description "BFF API audience URI" `
    -IsPlaceholder $true

# === Data Services ===
Write-Host ""
Write-Host "[3/7] Data Services (Redis, Service Bus, Dataverse)" -ForegroundColor Yellow

Set-VaultSecret -Name "Redis-ConnectionString" `
    -Value "placeholder-redis-not-yet-provisioned" `
    -Description "Azure Cache for Redis connection string (update after Redis provisioning)" `
    -IsPlaceholder $true

Set-VaultSecret -Name "ServiceBus-ConnectionString" `
    -Value "placeholder-servicebus-not-yet-provisioned" `
    -Description "Azure Service Bus connection string (update after Service Bus provisioning)" `
    -IsPlaceholder $true

Set-VaultSecret -Name "Dataverse-ServiceUrl" `
    -Value ($DataverseUrl ?? "https://placeholder.crm.dynamics.com") `
    -Description "Dataverse environment URL (update for production Dataverse)" `
    -IsPlaceholder (-not $DataverseUrl)

# === SPE (SharePoint Embedded) ===
Write-Host ""
Write-Host "[4/7] SharePoint Embedded" -ForegroundColor Yellow

Set-VaultSecret -Name "SPE-ContainerTypeId" `
    -Value "00000000-0000-0000-0000-000000000000" `
    -Description "SPE Container Type ID (update after SPE provisioning)" `
    -IsPlaceholder $true

Set-VaultSecret -Name "SPE-DefaultContainerId" `
    -Value "placeholder-default-container" `
    -Description "SPE default container ID (update after customer provisioning)" `
    -IsPlaceholder $true

Set-VaultSecret -Name "SPE-CommunicationArchiveContainerId" `
    -Value "placeholder-comm-archive-container" `
    -Description "SPE communication archive container ID" `
    -IsPlaceholder $true

# === AI Services ===
Write-Host ""
Write-Host "[5/7] AI Services (OpenAI, Doc Intel, AI Search, Prompt Flow)" -ForegroundColor Yellow

Set-VaultSecret -Name "ai-openai-endpoint" `
    -Value "https://spaarke-openai-prod.openai.azure.com/" `
    -Description "Azure OpenAI endpoint"

Set-VaultSecret -Name "ai-openai-key" `
    -Value "placeholder-openai-key" `
    -Description "Azure OpenAI API key (update with real key)" `
    -IsPlaceholder $true

Set-VaultSecret -Name "ai-docintel-endpoint" `
    -Value "https://spaarke-docintel-prod.cognitiveservices.azure.com/" `
    -Description "Azure Document Intelligence endpoint"

Set-VaultSecret -Name "ai-docintel-key" `
    -Value "placeholder-docintel-key" `
    -Description "Azure Document Intelligence key (update with real key)" `
    -IsPlaceholder $true

Set-VaultSecret -Name "ai-search-endpoint" `
    -Value "https://spaarke-search-prod.search.windows.net/" `
    -Description "Azure AI Search endpoint"

Set-VaultSecret -Name "ai-search-key" `
    -Value "placeholder-search-key" `
    -Description "Azure AI Search admin key (update with real key)" `
    -IsPlaceholder $true

Set-VaultSecret -Name "PromptFlow-Endpoint" `
    -Value "https://placeholder-promptflow.azurewebsites.net" `
    -Description "AI Foundry Prompt Flow endpoint (update after AI Foundry setup)" `
    -IsPlaceholder $true

Set-VaultSecret -Name "PromptFlow-Key" `
    -Value "placeholder-promptflow-key" `
    -Description "AI Foundry Prompt Flow API key (update after AI Foundry setup)" `
    -IsPlaceholder $true

# === Monitoring ===
Write-Host ""
Write-Host "[6/7] Monitoring (Application Insights)" -ForegroundColor Yellow

Set-VaultSecret -Name "AppInsights-ConnectionString" `
    -Value "InstrumentationKey=bbbe0468-93b9-451c-9e78-9dfc675005f1;IngestionEndpoint=https://westus2-2.in.applicationinsights.azure.com/;LiveEndpoint=https://westus2.livediagnostics.monitor.azure.com/;ApplicationId=a15aa294-0ccc-4d63-a765-750d34639040" `
    -Description "Application Insights connection string"

# === Communication / Email ===
Write-Host ""
Write-Host "[7/7] Communication & Email" -ForegroundColor Yellow

Set-VaultSecret -Name "Communication-DefaultMailbox" `
    -Value "noreply@spaarke.com" `
    -Description "Default mailbox for communication features" `
    -IsPlaceholder $true

Set-VaultSecret -Name "Communication-WebhookUrl" `
    -Value "https://spaarke-bff-prod.azurewebsites.net/api/communication/webhook" `
    -Description "Communication webhook notification URL"

Set-VaultSecret -Name "communication-webhook-secret" `
    -Value "placeholder-webhook-secret-$(Get-Random -Minimum 100000 -Maximum 999999)" `
    -Description "Communication webhook client state secret" `
    -IsPlaceholder $true

Set-VaultSecret -Name "Email-WebhookSecret" `
    -Value "placeholder-email-webhook-$(Get-Random -Minimum 100000 -Maximum 999999)" `
    -Description "Email webhook secret" `
    -IsPlaceholder $true

# --- Summary ---
Write-Host ""
Write-Host "================================================================" -ForegroundColor Green
Write-Host "  Key Vault Seeding Complete" -ForegroundColor Green
Write-Host "================================================================" -ForegroundColor Green
Write-Host ""

# Count placeholders
$secrets = az keyvault secret list --vault-name $VaultName --query "[].name" --output tsv 2>$null
$totalCount = ($secrets | Measure-Object -Line).Lines
$placeholderCount = 0
foreach ($s in ($secrets -split "`n")) {
    $s = $s.Trim()
    if (-not $s) { continue }
    $tags = az keyvault secret show --vault-name $VaultName --name $s --query "tags.placeholder" --output tsv 2>$null
    if ($tags -eq "True") { $placeholderCount++ }
}

Write-Host "  Total secrets: $totalCount"
Write-Host "  Placeholder secrets: $placeholderCount (must update before production use)" -ForegroundColor Yellow
Write-Host "  Real secrets: $($totalCount - $placeholderCount)" -ForegroundColor Green
Write-Host ""
Write-Host "  Next steps:" -ForegroundColor Gray
Write-Host "    1. Run Register-EntraAppRegistrations.ps1 to populate Entra ID secrets"
Write-Host "    2. Provision Redis, Service Bus, and update connection strings"
Write-Host "    3. Get AI service keys and update ai-* secrets"
Write-Host "    4. Re-deploy BFF API (staging slot health check should now pass)"
Write-Host ""
