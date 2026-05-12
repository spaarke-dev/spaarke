<#
.SYNOPSIS
    Reconciles the Spaarke demo environment to match dev where required.

.DESCRIPTION
    Applies Tier 1 (blocking) and selected Tier 2 fixes for the demo environment:
    - Azure AI Search: clones 4 required indexes from dev
    - Service Bus: creates 2 missing queues (document-events, office-jobs)
    - App Service settings: adds 15+ missing keys (AgentToken__*, Workspace__*, etc.)
    - Dataverse env vars: strips /api suffix, updates DeploymentEnvironment
    - Dataverse seed: creates sprk_dataverseenvironment record

    Built incrementally on 2026-05-11 during demo bring-up. Safe to re-run (idempotent
    where possible). For other demo environments, update parameter defaults at top.

.PARAMETER WhatIf
    Show what would be done without making changes.

.EXAMPLE
    .\Reconcile-DemoEnvironment.ps1
    # Run the full reconciliation
#>

param([switch]$WhatIf)

$ErrorActionPreference = 'Stop'

# ============================================================================
# Environment parameters (demo defaults)
# ============================================================================
$DemoSubscription   = '2ff9ee48-6f1d-4664-865c-f11868dd1b50'
$DevSubscription    = '484bc857-3802-427f-9ea5-ca47b43db0f0'
$DemoResourceGroup  = 'rg-spaarke-demo'
$DevResourceGroup   = 'spe-infrastructure-westus2'
$DemoBffAppService  = 'spaarke-bff-demo'
$DemoKeyVault       = 'sprk-demo-kv'
$DemoSearchService  = 'spaarke-search-demo'
$DevSearchService   = 'spaarke-search-dev'
$DemoServiceBus     = 'spaarke-demo-sbus'
$DemoBffAppId       = 'da03fe1a-4b1d-4297-a4ce-4b83cae498a9'
$DevBffAppId        = '1e40baad-e065-4aea-a8d4-4b7ab273458c'
$DemoTenantId       = 'a221a95e-6abc-4434-aecc-e48338a1b2f2'
$DemoDataverseUrl   = 'https://spaarke-demo.crm.dynamics.com'
$DemoContainerId    = 'b!FzmtPrWQEEi1yPtUOXM4_h7X4udVbCVJgu1ClOi23elAbPdL3-EGQK-D8YZ9tcZp'
$DemoSpeContainerType = '362f90b3-7b72-4ab1-bb4c-20a1399ca838'

Write-Host "Reconcile-DemoEnvironment.ps1 (WhatIf=$WhatIf)"
Write-Host "Target: $DemoBffAppService in $DemoResourceGroup, sub $DemoSubscription"
Write-Host ""

# A. Set demo subscription
Write-Host '[A] Setting subscription to demo...'
if (-not $WhatIf) { az account set --subscription $DemoSubscription }

# B. AI Search indexes — clone from dev (run separately, see Reconcile-AiSearchIndexes.ps1)
Write-Host '[B] AI Search index cloning is handled by separate helper script Reconcile-AiSearchIndexes.ps1'

# C. Service Bus queues
Write-Host '[C] Creating missing Service Bus queues...'
foreach ($q in @('document-events','office-jobs')) {
    Write-Host "    queue: $q"
    if (-not $WhatIf) {
        az servicebus queue create -g $DemoResourceGroup --namespace-name $DemoServiceBus -n $q `
            --max-delivery-count 5 --enable-dead-lettering-on-message-expiration true `
            --query name -o tsv 2>&1 | Out-Null
    }
}

# D. App Service settings batch
Write-Host '[D] Applying app settings batch...'
$settings = @{
    # AgentToken section (7 keys) — required by AgentTokenOptions validator
    'AgentToken__AgentAppId'              = 'f257a0a9-1061-4f9b-8918-3ad056fe90db'
    'AgentToken__CacheTtlMinutes'         = '55'
    'AgentToken__ClientId'                = $DemoBffAppId
    'AgentToken__ClientSecret'            = "@Microsoft.KeyVault(VaultName=$DemoKeyVault;SecretName=BFF-API-ClientSecret)"
    'AgentToken__CopilotAudience'         = "api://auth-3e04ab58-8450-44d6-b95b-daca16b6cbdb/$DemoBffAppId"
    'AgentToken__DataverseEnvironmentUrl' = $DemoDataverseUrl
    'AgentToken__TenantId'                = $DemoTenantId

    # Workspace playbook IDs — match dev since playbook GUIDs are preserved on import
    'Workspace__AiSummaryPlaybookId' = '18cf3cc8-02ec-f011-8406-7c1e520aa4df'
    'Workspace__PreFillPlaybookId'   = '2d660cad-d418-f111-8343-7ced8d1dc988'

    # SPE Container Type for the SpeAdmin endpoints
    'SharePointEmbedded__ContainerTypeId' = $DemoSpeContainerType

    # Per-environment API keys (replace placeholders with fresh secrets if needed)
    'Communication__WebhookClientState' = [guid]::NewGuid().ToString()
    'BuilderAdmin__ApiKey'              = "spaarke-builder-admin-demo-$(Get-Date -Format yyyy)"
    'Rag__ApiKey'                       = "rag-demo-$([guid]::NewGuid())"

    # DemoProvisioning Environments array — registration provisioning needs this
    'DemoProvisioning__DefaultEnvironment'                       = 'Demo'
    'DemoProvisioning__Environments__0__Name'                    = 'Demo'
    'DemoProvisioning__Environments__0__DataverseUrl'            = $DemoDataverseUrl
    'DemoProvisioning__Environments__0__BusinessUnitName'        = 'Spaarke'
    'DemoProvisioning__Environments__0__TeamName'                = 'Spaarke'
    'DemoProvisioning__Environments__0__SpeContainerId'          = $DemoContainerId
    'DemoProvisioning__Environments__0__DefaultDemoDurationDays' = '14'
}

if (-not $WhatIf) {
    $args = @()
    foreach ($k in $settings.Keys) { $args += "$k=$($settings[$k])" }
    az webapp config appsettings set -g $DemoResourceGroup -n $DemoBffAppService --settings @args -o none 2>&1 | Out-Null
    Write-Host "    applied $($settings.Count) settings"
}

# E. Dataverse env var fixes
Write-Host '[E] Dataverse env var fixes (handled by helper script Reconcile-DemoDataverseEnvVars.ps1)'

# F. Seed sprk_dataverseenvironment record (handled by helper script Seed-DemoDataverseEnvironment.ps1)
Write-Host '[F] sprk_dataverseenvironment seed (handled by helper script Seed-DemoDataverseEnvironment.ps1)'

# G. Restart BFF
Write-Host '[G] Restarting BFF...'
if (-not $WhatIf) {
    az webapp restart -g $DemoResourceGroup -n $DemoBffAppService 2>&1 | Out-Null
}

Write-Host ''
Write-Host 'Done. Verify with:  curl -s -o NUL -w "%{http_code}" https://spaarke-bff-demo.azurewebsites.net/healthz'
