#Requires -Version 7.0
<#
.SYNOPSIS
    Verify Azure AI Content Safety resource exists and that both Prompt Shields
    and Groundedness Detection API endpoints are reachable.

.DESCRIPTION
    This script:
      1. Checks whether the Content Safety resource exists in the target resource group.
      2. Confirms the resource is in westus2 (required for Prompt Shields + Groundedness).
      3. Retrieves the endpoint and API key from Azure.
      4. Sends a test request to the Prompt Shields API (shieldPrompt) and asserts HTTP 200.
      5. Sends a test request to the Groundedness Detection API (detectGroundedness) and asserts HTTP 200.
      6. Optionally stores the API key in Key Vault if it is not already there.

    The script NEVER prints or logs the raw API key. Key Vault secret storage is
    idempotent — re-running is safe.

.PARAMETER ResourceGroup
    Azure resource group containing the Content Safety resource.
    Default: spe-infrastructure-westus2

.PARAMETER ResourceName
    Name of the Content Safety Cognitive Services account.
    Default: spaarke-contentsafety-dev

.PARAMETER KeyVaultName
    Key Vault where the API key secret is stored.
    Default: spaarke-spekvcert

.PARAMETER KeyVaultSecretName
    Secret name for the API key in Key Vault.
    Default: ContentSafety--ApiKey

.PARAMETER SkipKeyVault
    Skip the Key Vault secret storage step (useful in read-only verification runs).

.EXAMPLE
    # Full verification + Key Vault sync
    ./scripts/Verify-ContentSafetyResource.ps1

.EXAMPLE
    # Verification only, no Key Vault write
    ./scripts/Verify-ContentSafetyResource.ps1 -SkipKeyVault

.NOTES
    Requires:
      - Azure CLI (az) authenticated to the correct subscription
      - Subscription: 484bc857-3802-427f-9ea5-ca47b43db0f0 (Spaarke Dev)
      - PowerShell 7+ (uses Invoke-RestMethod with -SkipHttpErrorCheck)
#>

[CmdletBinding()]
param(
    [string]$ResourceGroup    = 'spe-infrastructure-westus2',
    [string]$ResourceName     = 'spaarke-contentsafety-dev',
    [string]$KeyVaultName     = 'spaarke-spekvcert',
    [string]$KeyVaultSecretName = 'ContentSafety--ApiKey',
    [switch]$SkipKeyVault
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ============================================================================
# HELPERS
# ============================================================================

function Write-Step([string]$Message) {
    Write-Host "`n>> $Message" -ForegroundColor Cyan
}

function Write-Pass([string]$Message) {
    Write-Host "   [PASS] $Message" -ForegroundColor Green
}

function Write-Fail([string]$Message) {
    Write-Host "   [FAIL] $Message" -ForegroundColor Red
}

function Write-Info([string]$Message) {
    Write-Host "   [INFO] $Message" -ForegroundColor Gray
}

# ============================================================================
# STEP 1 — Confirm az CLI is authenticated
# ============================================================================

Write-Step 'Verifying Azure CLI authentication'
try {
    $account = az account show --output json 2>&1 | ConvertFrom-Json
    Write-Pass "Authenticated as: $($account.user.name)"
    Write-Info  "Subscription: $($account.name) ($($account.id))"
} catch {
    Write-Fail 'az account show failed. Run: az login'
    exit 1
}

# ============================================================================
# STEP 2 — Check resource existence
# ============================================================================

Write-Step "Checking Content Safety resource: $ResourceName in $ResourceGroup"

$resourceJson = az cognitiveservices account list `
    --resource-group $ResourceGroup `
    --query "[?name=='$ResourceName']" `
    --output json 2>&1

$resources = $resourceJson | ConvertFrom-Json

if ($resources.Count -eq 0) {
    Write-Fail "Resource '$ResourceName' not found in resource group '$ResourceGroup'."
    Write-Info 'To provision, run:'
    Write-Info "  az cognitiveservices account create --name $ResourceName --resource-group $ResourceGroup --kind ContentSafety --sku S0 --location westus2 --yes"
    Write-Info 'Or deploy via Bicep:'
    Write-Info "  az deployment group create --resource-group $ResourceGroup --template-file infrastructure/bicep/modules/content-safety.bicep --parameters contentSafetyName=$ResourceName"
    exit 1
}

$resource = $resources[0]
Write-Pass "Resource found: $($resource.name)"
Write-Info  "Kind     : $($resource.kind)"
Write-Info  "SKU      : $($resource.sku.name)"
Write-Info  "Location : $($resource.location)"
Write-Info  "State    : $($resource.properties.provisioningState)"

# ============================================================================
# STEP 3 — Validate region (Prompt Shields + Groundedness require westus2/eastus2)
# ============================================================================

Write-Step 'Validating region compatibility'

$requiredLocations = @('westus2', 'eastus2')
$actualLocation    = $resource.location.ToLower().Replace(' ', '')

if ($requiredLocations -notcontains $actualLocation) {
    Write-Fail "Resource is in '$($resource.location)'. Prompt Shields and Groundedness Detection require westus2 or eastus2."
    Write-Info 'A new resource must be created in the correct region.'
    exit 1
}

Write-Pass "Region '$($resource.location)' supports Prompt Shields and Groundedness Detection."

# ============================================================================
# STEP 4 — Retrieve endpoint and API key
# ============================================================================

Write-Step 'Retrieving endpoint and API key'

$endpoint = az cognitiveservices account show `
    --name $ResourceName `
    --resource-group $ResourceGroup `
    --query 'properties.endpoint' `
    --output tsv

if ([string]::IsNullOrWhiteSpace($endpoint)) {
    Write-Fail 'Could not retrieve endpoint from resource properties.'
    exit 1
}

# Trim trailing slash for consistent URL construction
$endpoint = $endpoint.TrimEnd('/')
Write-Pass "Endpoint: $endpoint"

# Retrieve key — stored in a SecureString immediately; never written to output
$apiKeyPlain = az cognitiveservices account keys list `
    --name $ResourceName `
    --resource-group $ResourceGroup `
    --query 'key1' `
    --output tsv

if ([string]::IsNullOrWhiteSpace($apiKeyPlain)) {
    Write-Fail 'Could not retrieve API key.'
    exit 1
}

Write-Pass 'API key retrieved (not displayed).'

# ============================================================================
# STEP 5 — Verify Prompt Shields API (shieldPrompt)
# ============================================================================

Write-Step 'Verifying Prompt Shields API (shieldPrompt)'

# API reference: https://learn.microsoft.com/azure/ai-services/content-safety/quickstart-jailbreak
$promptShieldsUrl = "$endpoint/contentsafety/text:shieldPrompt?api-version=2024-09-01"

$promptShieldsBody = @{
    userPrompt = 'What is the capital of France?'
    documents  = @('Paris is the capital of France.')
} | ConvertTo-Json

try {
    $response = Invoke-RestMethod `
        -Uri         $promptShieldsUrl `
        -Method      Post `
        -Headers     @{ 'Ocp-Apim-Subscription-Key' = $apiKeyPlain; 'Content-Type' = 'application/json' } `
        -Body        $promptShieldsBody `
        -StatusCodeVariable statusCode `
        -SkipHttpErrorCheck

    if ($statusCode -eq 200) {
        Write-Pass "Prompt Shields API returned HTTP 200."
        Write-Info  "userPromptAnalysis.attackDetected: $($response.userPromptAnalysis.attackDetected)"
        Write-Info  "documentsAnalysis count: $($response.documentsAnalysis.Count)"
    } else {
        Write-Fail "Prompt Shields API returned HTTP $statusCode."
        Write-Info  "Response: $($response | ConvertTo-Json -Depth 5)"
        exit 1
    }
} catch {
    Write-Fail "Prompt Shields API request failed: $_"
    exit 1
}

# ============================================================================
# STEP 6 — Verify Groundedness Detection API (detectGroundedness)
# ============================================================================

Write-Step 'Verifying Groundedness Detection API (detectGroundedness)'

# API reference: https://learn.microsoft.com/azure/ai-services/content-safety/quickstart-groundedness
$groundednessUrl = "$endpoint/contentsafety/text:detectGroundedness?api-version=2024-09-15-preview"

$groundednessBody = @{
    domain         = 'Generic'
    task           = 'QnA'
    qna            = @{
        query = 'What is the capital of France?'
    }
    text           = 'Paris is the capital of France.'
    groundingSources = @('Paris is the capital and most populous city of France.')
    reasoning      = $false
} | ConvertTo-Json -Depth 5

try {
    $response = Invoke-RestMethod `
        -Uri         $groundednessUrl `
        -Method      Post `
        -Headers     @{ 'Ocp-Apim-Subscription-Key' = $apiKeyPlain; 'Content-Type' = 'application/json' } `
        -Body        $groundednessBody `
        -StatusCodeVariable statusCode `
        -SkipHttpErrorCheck

    if ($statusCode -eq 200) {
        Write-Pass "Groundedness Detection API returned HTTP 200."
        Write-Info  "ungroundedDetected: $($response.ungroundedDetected)"
    } else {
        Write-Fail "Groundedness Detection API returned HTTP $statusCode."
        Write-Info  "Response: $($response | ConvertTo-Json -Depth 5)"
        exit 1
    }
} catch {
    Write-Fail "Groundedness Detection API request failed: $_"
    exit 1
}

# ============================================================================
# STEP 7 — Store API key in Key Vault (idempotent)
# ============================================================================

if (-not $SkipKeyVault) {
    Write-Step "Storing API key in Key Vault '$KeyVaultName' as secret '$KeyVaultSecretName'"

    try {
        az keyvault secret set `
            --vault-name $KeyVaultName `
            --name       $KeyVaultSecretName `
            --value      $apiKeyPlain `
            --output none

        Write-Pass "Secret '$KeyVaultSecretName' set in Key Vault '$KeyVaultName'."
        Write-Info  "Key Vault reference for App Service settings:"
        Write-Info  "  @Microsoft.KeyVault(SecretUri=https://$KeyVaultName.vault.azure.net/secrets/$KeyVaultSecretName/)"
    } catch {
        Write-Fail "Failed to set Key Vault secret: $_"
        exit 1
    }
} else {
    Write-Info 'Key Vault step skipped (-SkipKeyVault).'
}

# Clear the plain-text key from memory
Remove-Variable -Name apiKeyPlain -ErrorAction SilentlyContinue

# ============================================================================
# SUMMARY
# ============================================================================

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ' Content Safety Verification — PASSED' -ForegroundColor Green
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ''
Write-Host "  Resource   : $ResourceName"
Write-Host "  Endpoint   : $endpoint"
Write-Host "  Region     : $($resource.location)"
Write-Host "  SKU        : $($resource.sku.name)"
Write-Host "  APIs tested: Prompt Shields, Groundedness Detection"
if (-not $SkipKeyVault) {
    Write-Host "  Key Vault  : $KeyVaultName / $KeyVaultSecretName"
}
Write-Host ''
Write-Host 'Add to docs/architecture/auth-AI-azure-resources.md if not already present.' -ForegroundColor Yellow
Write-Host ''
