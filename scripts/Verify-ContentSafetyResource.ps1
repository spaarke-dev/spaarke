#Requires -Version 7.0
<#
.SYNOPSIS
    Verify an Azure AI Content Safety account exists and that both Prompt Shields
    and Groundedness Detection answer a Microsoft Entra (bearer-token) call.

.DESCRIPTION
    This script:
      1. Checks whether the account exists in the target resource group.
      2. Reports the account's region (the two API calls below are the real availability test).
      3. Reports whether local (key) auth is disabled — it is on every customer stamp (task 246).
      4. Gets an Entra token for https://cognitiveservices.azure.com with the operator's own az login.
      5. Sends a test request to Prompt Shields (shieldPrompt) and asserts HTTP 200.
      6. Sends a test request to Groundedness Detection (detectGroundedness) and asserts HTTP 200.

    Keyless (task 246, owner D13): the script reads no API key and writes nothing to Key Vault —
    the BFF authenticates with its managed identity, and customer-stamp accounts reject keys.
    The script is read-only: it changes no Azure resource.

    The calling identity needs the "Cognitive Services User" role on the account (the role the
    BFF identity holds). "Cognitive Services OpenAI User" does NOT cover Content Safety.

.PARAMETER ResourceGroup
    Resource group containing the account.
    Default: spe-infrastructure-westus2 (shared dev). Customer stamp: rg-spaarke-{customer}-{env}.

.PARAMETER ResourceName
    Name of the Cognitive Services account serving Content Safety.
    Default: spaarke-openai-dev (shared dev serves Content Safety from its multi-service AIServices
    account). Customer stamp: sprk-{customer}-{env}-contentsafety.

.PARAMETER SubscriptionId
    Subscription holding the account. Default: the az CLI's current subscription. Passed to every az
    call with --subscription; the script never runs 'az account set'.

.EXAMPLE
    # Shared dev
    ./scripts/Verify-ContentSafetyResource.ps1

.EXAMPLE
    # A customer stamp
    ./scripts/Verify-ContentSafetyResource.ps1 -SubscriptionId <sub> -ResourceGroup rg-spaarke-acme-prod -ResourceName sprk-acme-prod-contentsafety

.NOTES
    Requires:
      - Azure CLI (az) signed in as the operator (az login)
      - PowerShell 7+ (uses Invoke-RestMethod with -SkipHttpErrorCheck)
#>

[CmdletBinding()]
param(
    [string]$ResourceGroup  = 'spe-infrastructure-westus2',
    [string]$ResourceName   = 'spaarke-openai-dev',
    [string]$SubscriptionId = ''
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

$subscriptionArgs = if ([string]::IsNullOrWhiteSpace($SubscriptionId)) { @() } else { @('--subscription', $SubscriptionId) }

# ============================================================================
# STEP 1 — Confirm az CLI is authenticated
# ============================================================================

Write-Step 'Verifying Azure CLI authentication'
# stderr is discarded rather than merged: az prints upgrade/extension notices there, which would corrupt the JSON.
$accountJson = az account show @subscriptionArgs --output json 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace("$accountJson")) {
    Write-Fail 'az account show failed. Run: az login (and check -SubscriptionId).'
    exit 1
}
$account = $accountJson | ConvertFrom-Json
Write-Pass "Authenticated as: $($account.user.name)"
Write-Info  "Subscription: $($account.name) ($($account.id))"

# ============================================================================
# STEP 2 — Check resource existence
# ============================================================================

Write-Step "Checking Content Safety account: $ResourceName in $ResourceGroup"

$resourceJson = az cognitiveservices account list `
    --resource-group $ResourceGroup `
    @subscriptionArgs `
    --query "[?name=='$ResourceName']" `
    --output json 2>$null

if ($LASTEXITCODE -ne 0) {
    Write-Fail "Could not list Cognitive Services accounts in resource group '$ResourceGroup' (does it exist in this subscription?)."
    exit 1
}

$resources = @($resourceJson | ConvertFrom-Json)

if ($resources.Count -eq 0) {
    Write-Fail "Account '$ResourceName' not found in resource group '$ResourceGroup'."
    Write-Info 'Customer stamps get their account from infrastructure/bicep/customer.bicep (modules/content-safety.bicep).'
    exit 1
}

$resource = $resources[0]
Write-Pass "Account found: $($resource.name)"
Write-Info  "Kind     : $($resource.kind)"
Write-Info  "SKU      : $($resource.sku.name)"
Write-Info  "Location : $($resource.location)"
Write-Info  "State    : $($resource.properties.provisioningState)"

# ============================================================================
# STEP 3 — Region (informational)
# Prompt Shields and Groundedness Detection are regional. No fixed list is checked here: the shared dev
# account is in East US and stamps default to West US 2, and Microsoft extends availability over time.
# Steps 5 and 6 are the real test — an unsupported region answers with an error, not HTTP 200.
# ============================================================================

Write-Step 'Region'
Write-Info "Region '$($resource.location)' — Steps 5 and 6 confirm both APIs answer there."

# ============================================================================
# STEP 4 — Endpoint, local-auth state and Entra token
# ============================================================================

Write-Step 'Retrieving endpoint and an Entra token'

$endpoint = "$($resource.properties.endpoint)".TrimEnd('/')
if ([string]::IsNullOrWhiteSpace($endpoint)) {
    Write-Fail 'Could not read the endpoint from the account properties.'
    exit 1
}
Write-Pass "Endpoint: $endpoint"

$localAuthDisabled = $resource.properties.PSObject.Properties['disableLocalAuth'] -and $resource.properties.disableLocalAuth
if ($localAuthDisabled) {
    Write-Pass 'Local (key) auth is disabled — Entra only.'
} else {
    Write-Info 'Local (key) auth is enabled on this account (expected only on shared dev; customer stamps disable it).'
}

$token = az account get-access-token `
    --resource 'https://cognitiveservices.azure.com' `
    @subscriptionArgs `
    --query 'accessToken' `
    --output tsv 2>$null

if ([string]::IsNullOrWhiteSpace($token)) {
    Write-Fail 'Could not get an Entra token for https://cognitiveservices.azure.com. Run: az login'
    exit 1
}
Write-Pass 'Entra token acquired (not displayed).'

$headers = @{ Authorization = "Bearer $token"; 'Content-Type' = 'application/json' }

function Write-HttpFailure([string]$Api, [int]$Status, $Response) {
    Write-Fail "$Api returned HTTP $Status."
    if ($Status -in 401, 403) {
        Write-Info 'Your identity needs the Cognitive Services User role on this account (OpenAI User does not cover Content Safety).'
    }
    Write-Info "Response: $($Response | ConvertTo-Json -Depth 5)"
}

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
        -Headers     $headers `
        -Body        $promptShieldsBody `
        -StatusCodeVariable statusCode `
        -SkipHttpErrorCheck

    if ($statusCode -eq 200) {
        Write-Pass "Prompt Shields API returned HTTP 200."
        Write-Info  "userPromptAnalysis.attackDetected: $($response.userPromptAnalysis.attackDetected)"
        Write-Info  "documentsAnalysis count: $($response.documentsAnalysis.Count)"
    } else {
        Write-HttpFailure 'Prompt Shields API' $statusCode $response
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
        -Headers     $headers `
        -Body        $groundednessBody `
        -StatusCodeVariable statusCode `
        -SkipHttpErrorCheck

    if ($statusCode -eq 200) {
        Write-Pass "Groundedness Detection API returned HTTP 200."
        Write-Info  "ungroundedDetected: $($response.ungroundedDetected)"
    } else {
        Write-HttpFailure 'Groundedness Detection API' $statusCode $response
        exit 1
    }
} catch {
    Write-Fail "Groundedness Detection API request failed: $_"
    exit 1
}

Remove-Variable -Name token, headers -ErrorAction SilentlyContinue

# ============================================================================
# SUMMARY
# ============================================================================

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ' Content Safety Verification — PASSED' -ForegroundColor Green
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ''
Write-Host "  Account    : $ResourceName"
Write-Host "  Endpoint   : $endpoint"
Write-Host "  Region     : $($resource.location)"
Write-Host "  SKU        : $($resource.sku.name)"
Write-Host "  Local auth : $(if ($localAuthDisabled) { 'disabled' } else { 'enabled' })"
Write-Host "  APIs tested: Prompt Shields, Groundedness Detection (Entra token)"
Write-Host ''
