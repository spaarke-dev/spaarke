<#
.SYNOPSIS
    Configure production App Service app settings with Key Vault references.

.DESCRIPTION
    Sets App Service app settings that reference Key Vault secrets. The App Service
    platform resolves these references automatically using the managed identity.

    Key Vault references in App Service app settings use the format:
      @Microsoft.KeyVault(VaultName=<vault>;SecretName=<secret>)

    This is different from appsettings.json — App Service app settings with this
    format are resolved by the platform BEFORE being passed to the application.

    Important: App Service app settings override appsettings.json values using
    the __ (double underscore) or : (colon) notation for nested keys.

.PARAMETER ResourceGroupName
    Resource group name. Default: rg-spaarke-platform-prod

.PARAMETER AppServiceName
    App Service name. Default: spaarke-bff-prod

.PARAMETER VaultName
    Key Vault name. Default: sprk-platform-prod-kv

.PARAMETER IncludeSlots
    Also configure the staging slot. Default: $true

.EXAMPLE
    .\Configure-ProductionAppSettings.ps1
#>

param(
    [string]$ResourceGroupName = "rg-spaarke-platform-prod",
    [string]$AppServiceName = "spaarke-bff-prod",
    [string]$VaultName = "sprk-platform-prod-kv",
    [bool]$IncludeSlots = $true
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  Configure Production App Settings" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  App Service:  $AppServiceName"
Write-Host "  Key Vault:    $VaultName"
Write-Host "  Include Slots: $IncludeSlots"
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ""

# Helper function to build Key Vault reference
function KVRef([string]$SecretName) {
    return "@Microsoft.KeyVault(VaultName=$VaultName;SecretName=$SecretName)"
}

# Build the app settings array
# These use double-underscore notation for nested keys (Linux App Service requirement)
$settings = @(
    # --- Core Identity ---
    "TENANT_ID=$(KVRef 'TenantId')",
    "API_APP_ID=$(KVRef 'BFF-API-ClientId')",
    "DEFAULT_CT_ID=$(KVRef 'SPE-ContainerTypeId')",

    # --- Credential selection (ADR-028 A4 — the BFF identity is secret-free) ---
    # The BFF authenticates as a confidential client using a Managed-Identity-issued federated
    # credential. There is deliberately NO ClientSecret entry beneath it: with nothing to fall
    # through to, a broken MI-FIC fails loudly instead of silently reverting to a secret while
    # every health signal stays green. RequireSecretFreeIdentity makes the app refuse to start
    # outside Development if ClientSecret is ever re-added to the order.
    # Removed 2026-08-24 by spaarke-auth-v4 task 033: Graph__ClientSecret, AzureAd__ClientSecret
    # and Dataverse__ClientSecret (all three were KV references to `BFF-API-ClientSecret`, which
    # no longer exists in Key Vault). Do NOT re-add them.
    "Graph__Credentials__Order__0=ManagedIdentityFederated",
    "Graph__Credentials__RequireSecretFreeIdentity=true",

    # --- Graph Configuration ---
    "Graph__TenantId=$(KVRef 'TenantId')",
    "Graph__ClientId=$(KVRef 'BFF-API-ClientId')",
    "Graph__Scopes__0=https://graph.microsoft.com/.default",

    # --- Azure AD Authentication ---
    "AzureAd__TenantId=$(KVRef 'TenantId')",
    "AzureAd__ClientId=$(KVRef 'BFF-API-ClientId')",
    "AzureAd__Audience=$(KVRef 'BFF-API-Audience')",

    # --- Dataverse ---
    "Dataverse__EnvironmentUrl=$(KVRef 'Dataverse-ServiceUrl')",
    "Dataverse__ClientId=$(KVRef 'BFF-API-ClientId')",
    "Dataverse__TenantId=$(KVRef 'TenantId')",

    # --- Service Bus ---
    "ServiceBus__ConnectionString=$(KVRef 'ServiceBus-ConnectionString')",

    # --- Redis ---
    "ConnectionStrings__Redis=$(KVRef 'Redis-ConnectionString')",

    # --- Document Intelligence ---
    "DocumentIntelligence__OpenAiEndpoint=$(KVRef 'ai-openai-endpoint')",
    "DocumentIntelligence__OpenAiKey=$(KVRef 'ai-openai-key')",
    "DocumentIntelligence__DocIntelEndpoint=$(KVRef 'ai-docintel-endpoint')",
    "DocumentIntelligence__DocIntelKey=$(KVRef 'ai-docintel-key')",
    "DocumentIntelligence__AiSearchEndpoint=$(KVRef 'ai-search-endpoint')",
    "DocumentIntelligence__AiSearchKey=$(KVRef 'AiSearch--AdminKey')",

    # --- Analysis ---
    # PromptFlowEndpoint / PromptFlowKey REMOVED 2026-08-21 (auth-v4 task 055, FR-E6): both were bound
    # and read by nothing, were never deployed, and their Key Vault entries were never-updated
    # placeholders. See projects/spaarke-auth-v4-dataverse-MI/notes/decisions/055-promptflow-key-disposition.md

    # --- Application Insights ---
    "ApplicationInsights__ConnectionString=$(KVRef 'AppInsights-ConnectionString')",

    # --- Email ---
    "Email__DefaultContainerId=$(KVRef 'SPE-DefaultContainerId')",
    "Email__WebhookSecret=$(KVRef 'Email-WebhookSecret')",

    # --- Communication ---
    "Communication__DefaultMailbox=$(KVRef 'Communication-DefaultMailbox')",
    "Communication__ArchiveContainerId=$(KVRef 'SPE-CommunicationArchiveContainerId')",
    "Communication__WebhookNotificationUrl=$(KVRef 'Communication-WebhookUrl')",
    "Communication__WebhookClientState=$(KVRef 'communication-webhook-secret')",

    # --- AI Search ---
    "AiSearch__Endpoint=$(KVRef 'ai-search-endpoint')",

    # --- Azure OpenAI ---
    "AzureOpenAI__Endpoint=$(KVRef 'ai-openai-endpoint')",

    # --- Scheduled RAG Indexing ---
    "ScheduledRagIndexing__TenantId=$(KVRef 'TenantId')",

    # --- Analysis Key Vault URL ---
    "Analysis__KeyVaultUrl=https://$VaultName.vault.azure.net/"
)

Write-Host "[1/2] Setting app settings on production slot ($($settings.Count) settings)..." -ForegroundColor Yellow

az webapp config appsettings set `
    --resource-group $ResourceGroupName `
    --name $AppServiceName `
    --settings @settings `
    --output none 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Host "  FAILED to set production app settings" -ForegroundColor Red
    exit 1
}
Write-Host "  Production slot: $($settings.Count) settings configured" -ForegroundColor Green

if ($IncludeSlots) {
    Write-Host ""
    Write-Host "[2/2] Setting app settings on staging slot..." -ForegroundColor Yellow

    az webapp config appsettings set `
        --resource-group $ResourceGroupName `
        --name $AppServiceName `
        --slot staging `
        --settings @settings `
        --output none 2>&1

    if ($LASTEXITCODE -ne 0) {
        Write-Host "  FAILED to set staging slot app settings" -ForegroundColor Red
        exit 1
    }
    Write-Host "  Staging slot: $($settings.Count) settings configured" -ForegroundColor Green
}

# --- Summary ---
Write-Host ""
Write-Host "================================================================" -ForegroundColor Green
Write-Host "  App Settings Configuration Complete" -ForegroundColor Green
Write-Host "================================================================" -ForegroundColor Green
Write-Host "  Total settings: $($settings.Count)"
Write-Host "  Key Vault references: Will resolve via managed identity"
Write-Host ""
Write-Host "  Verify with:" -ForegroundColor Gray
Write-Host "    az webapp config appsettings list --resource-group $ResourceGroupName --name $AppServiceName --output table"
Write-Host ""
