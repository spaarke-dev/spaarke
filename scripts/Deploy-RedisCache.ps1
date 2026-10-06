#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Deploy Spaarke BFF Redis cache instance per environment.

.DESCRIPTION
    Idempotent provisioning of the per-environment Azure Managed Redis (`spaarke-bff-redis-{env}`,
    Microsoft.Cache/redisEnterprise, Microsoft Entra only). Uses `redis.bicep` module + per-environment
    `.bicepparam` files at `infrastructure/bicep/parameters/redis-{env}.bicepparam` (which list the
    identities granted the cache's access policy).

    Task 242 (owner D12/D13): the cache has access keys DISABLED. This script no longer lists keys or
    writes a `Redis-ConnectionString` Key Vault secret — there is none. `-CutoverBffSettings` sets the
    plain `Redis__Endpoint` app setting (host:10000); the BFF authenticates with its managed identity.

    Extracted from `Provision-Customer.ps1` lines 422-492 per project
    `spaarke-redis-cache-remediation-r1` FR-11 / FR-12 (per-customer Redis is
    deprecated per Q-E Architecture 1 — this script replaces that path).

    The script is idempotent (NFR-01) — safe to re-run. Pre-existing Redis instances
    in `Succeeded` provisioning state are detected and the deploy is skipped.

    Production and demo environments REJECT execution without an explicit `-Force`
    flag per NFR-05.

.PARAMETER Environment
    Target environment: dev, staging, prod, or demo.

.PARAMETER ResourceGroup
    Override the resource group. Defaults by environment:
      dev     -> spe-infrastructure-westus2
      staging -> rg-spaarke-staging
      prod    -> rg-spaarke-prod
      demo    -> rg-spaarke-demo

.PARAMETER RedisName
    Override the cache name. Default: `spaarke-bff-redis-{env}` (must match `redisName` in the
    environment's .bicepparam).

.PARAMETER VerifyOnly
    Skip deploy; run `tests/manual/RedisValidationTests.ps1` against the existing
    instance and exit with the validation harness's exit code.

.PARAMETER CutoverBffSettings
    After successful deploy, update `spaarke-bff-{env}` App Service settings:
      Redis__Enabled                = true
      Redis__InstanceName           = spaarke:
      Redis__Endpoint               = {cache host}:10000
      Redis__AllowInMemoryFallback  = false
    An existing `ConnectionStrings__Redis` is left in place on purpose: a T242+ build ignores it once
    Redis__Endpoint is set, while older builds still deployed to a shared environment read it. Task 242b
    removes it after master carries T242 (no-outage cut-over).

.PARAMETER RemoveBffConnectionString
    Remove `ConnectionStrings__Redis` and `Redis__ConnectionString` from `spaarke-bff-{env}` (task 242b).
    Refuses unless the app already carries `Redis__Endpoint`. Run it only once every build deployed to
    that environment is T242+ (in a shared environment: after master carries T242 and other worktrees
    were told to merge it) — an older build reads only the connection string. The Key Vault secret the
    setting referenced is left alone (secrets are never deleted).

.PARAMETER SubscriptionId
    Subscription of the cache and the BFF. Default: the az CLI's current subscription. Pass it rather than
    running `az account set` — the CLI context is shared by every session on the machine. (Demo lives in
    its own subscription.)

.PARAMETER Force
    Required to target `prod` or `demo` environments per NFR-05. Without `-Force`,
    the script exits with code 2 and a NFR-05 message.

.PARAMETER DeployAlerts
    Deploy the 3 Bicep-defined cache observability alerts (hit-rate <80% / 15min,
    P95 latency >100ms / 5min, memory >80% of SKU / 15min) from
    `infrastructure/bicep/alerts.bicep` to the target env's resource group.
    Requires `-AppInsightsName` and `-ActionGroupResourceId`. Mutually compatible
    with `-WhatIf` (uses `az deployment group what-if`) and with the standard
    Redis-deploy path (alert deploy runs after Redis idempotency check).

    Project: spaarke-redis-cache-remediation-r2 FR-04.

.PARAMETER AppInsightsName
    Application Insights component name used as the alert scope for the hit-rate
    + P95 KQL alerts (alert 3, memory, scopes the Redis cache directly).
    Defaults by environment:
      dev     -> spe-insights-dev-67e2xz
      staging -> spe-insights-staging
      prod    -> spe-insights-prod

.PARAMETER ActionGroupResourceId
    Full Azure resource ID of the on-call action group that receives the alert
    notifications (email). Required when `-DeployAlerts` is set. Format:
    `/subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.Insights/actionGroups/{name}`.

.PARAMETER WhatIf
    Native PowerShell `-WhatIf` via `SupportsShouldProcess`. Shows planned actions
    only — no Azure resources are created or modified. Combines with `-DeployAlerts`
    to surface an `az deployment group what-if` plan for the alerts.

.EXAMPLE
    pwsh ./scripts/Deploy-RedisCache.ps1 -Environment dev -WhatIf

    Plan-only run against dev — prints what would be deployed.

.EXAMPLE
    pwsh ./scripts/Deploy-RedisCache.ps1 -Environment dev -CutoverBffSettings

    Deploy dev Redis, set the BFF's Redis__Endpoint (managed identity),
    then run post-deploy validation.

.EXAMPLE
    pwsh ./scripts/Deploy-RedisCache.ps1 -Environment dev -VerifyOnly

    Skip deploy; run validation harness against existing dev instance.

.NOTES
    Project : spaarke-redis-cache-remediation-r1
    Version : 1.0.0
    Constraints:
      FR-11  — parameters per spec.
      NFR-01 — idempotent.
      NFR-05 — reject prod/demo without `-Force`.
      NFR-06 — `-WhatIf` (plan) + `-VerifyOnly` modes.
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('dev', 'staging', 'prod', 'demo')]
    [string]$Environment,

    [string]$ResourceGroup,

    [string]$RedisName,

    [switch]$VerifyOnly,

    [switch]$CutoverBffSettings,

    [switch]$RemoveBffConnectionString,

    [string]$SubscriptionId,

    [switch]$Force,

    [switch]$DeployAlerts,

    [string]$AppInsightsName,

    [string]$ActionGroupResourceId
)

$ErrorActionPreference = 'Stop'

# Write-Error throws under ErrorActionPreference=Stop, so a following `exit N` never ran and every failure
# exited 1. Report and exit with the documented code instead.
function Exit-WithError([string]$Message, [int]$Code) {
    Write-Host "ERROR: $Message" -ForegroundColor Red
    exit $Code
}
# Every az call names the subscription when one was given (never `az account set`).
$subArgs = if ($SubscriptionId) { @('--subscription', $SubscriptionId) } else { @() }
$repoRoot = Split-Path -Parent $PSScriptRoot

# ---------------------------------------------------------------------------
# NFR-05 prod/demo gate
# ---------------------------------------------------------------------------
if (($Environment -in @('prod', 'demo')) -and (-not $Force)) {
    Write-Host "NFR-05: -Environment $Environment requires -Force flag. This project must NOT touch prod/demo without explicit operator intent. Aborting." -ForegroundColor Red
    exit 2
}

# ---------------------------------------------------------------------------
# Resolve resource group default by environment
# ---------------------------------------------------------------------------
if (-not $ResourceGroup) {
    $ResourceGroup = switch ($Environment) {
        'dev'     { 'spe-infrastructure-westus2' }
        'staging' { 'rg-spaarke-staging' }
        'prod'    { 'rg-spaarke-prod' }
        'demo'    { 'rg-spaarke-demo' }
    }
}

# ---------------------------------------------------------------------------
# Resolve paths and resource names
# ---------------------------------------------------------------------------
$bicepModule      = Join-Path $repoRoot "infrastructure/bicep/modules/redis.bicep"
$bicepParam       = Join-Path $repoRoot "infrastructure/bicep/parameters/redis-$Environment.bicepparam"
$alertsBicep      = Join-Path $repoRoot "infrastructure/bicep/alerts.bicep"
$validationScript = Join-Path $repoRoot "tests/manual/RedisValidationTests.ps1"
$bffAppName       = "spaarke-bff-$Environment"
$redisName        = if ($RedisName) { $RedisName } else { "spaarke-bff-redis-$Environment" }

# DeployAlerts: resolve App Insights default by env (FR-04 of R2)
if ($DeployAlerts -and -not $AppInsightsName) {
    $AppInsightsName = switch ($Environment) {
        'dev'     { 'spe-insights-dev-67e2xz' }
        'staging' { 'spe-insights-staging' }
        'prod'    { 'spe-insights-prod' }
        'demo'    { 'spe-insights-demo' }
    }
}

if (-not (Test-Path $bicepParam)) {
    Exit-WithError "Bicep param file not found: $bicepParam" 3
}

# ---------------------------------------------------------------------------
# Banner
# ---------------------------------------------------------------------------
$modeLabel = if ($VerifyOnly) {
    'verify-only'
} elseif ($WhatIfPreference) {
    'what-if'
} else {
    'deploy'
}

Write-Host "Deploy-RedisCache.ps1 starting"
Write-Host "  Environment    : $Environment"
Write-Host "  ResourceGroup  : $ResourceGroup"
Write-Host "  Redis name     : $redisName"
Write-Host "  Bicep module   : $bicepModule"
Write-Host "  Bicep param    : $bicepParam"
Write-Host "  Redis          : $redisName (Azure Managed Redis, Entra only)"
Write-Host "  Mode           : $modeLabel"
Write-Host ""

# The template deploy is skipped when the cache exists, so an identity added to the .bicepparam later is
# never applied by this script. Both verification paths compare the live access-policy assignments with
# that list, so the gap fails loudly; the fix is the direct `az deployment group create` documented in the
# .bicepparam and docs/guides/redis-cache-azure-setup.md.
function Get-ExpectedPrincipalIds {
    $text = Get-Content -Raw -Path $bicepParam
    $block = [regex]::Match($text, 'param\s+accessPolicyPrincipalIds\s*=\s*\[(?<list>[^\]]*)\]')
    if (-not $block.Success) { return @() }
    return @([regex]::Matches($block.Groups['list'].Value, '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}') | ForEach-Object { $_.Value })
}

# ---------------------------------------------------------------------------
# Verify-only path (NFR-06)
# ---------------------------------------------------------------------------
if ($VerifyOnly) {
    Write-Host "Verify-only mode: invoking RedisValidationTests.ps1..."
    if (-not (Test-Path $validationScript)) {
        Exit-WithError "Validation harness not found: $validationScript" 4
    }
    $verifyArgs = @{ RedisName = $redisName; ResourceGroup = $ResourceGroup }
    if ($SubscriptionId) { $verifyArgs.SubscriptionId = $SubscriptionId }
    $expectedPrincipals = Get-ExpectedPrincipalIds
    if ($expectedPrincipals.Count -gt 0) { $verifyArgs.ExpectedPrincipalIds = $expectedPrincipals }
    & $validationScript @verifyArgs
    exit $LASTEXITCODE
}

# ---------------------------------------------------------------------------
# Idempotency check (NFR-01)
# ---------------------------------------------------------------------------
$existing = $null
try {
    $existing = az resource show @subArgs --resource-group $ResourceGroup --name $redisName --resource-type Microsoft.Cache/redisEnterprise --query "properties.provisioningState" -o tsv 2>$null
} catch {
    $existing = $null
}

if ($existing -eq 'Succeeded') {
    Write-Host "Redis instance '$redisName' already exists (provisioningState=Succeeded). Idempotent: skipping create."
} else {
    if ($PSCmdlet.ShouldProcess("$redisName in $ResourceGroup", "Deploy Bicep (redis.bicep with redis-$Environment.bicepparam)")) {
        Write-Host "Deploying Bicep template..."
        az deployment group create @subArgs `
            --resource-group $ResourceGroup `
            --template-file $bicepModule `
            --parameters $bicepParam `
            --query "properties.provisioningState" -o tsv
        if ($LASTEXITCODE -ne 0) {
            Exit-WithError "Bicep deploy failed (exit $LASTEXITCODE)" $LASTEXITCODE
        }
    }
}

# ---------------------------------------------------------------------------
# No Key Vault step (task 242): access keys are disabled on the cache, so there is no key or
# connection string to store. The identities in the .bicepparam hold its access policy.
# ---------------------------------------------------------------------------

# ---------------------------------------------------------------------------
# Cut over BFF App Settings to the managed-identity endpoint
# ---------------------------------------------------------------------------
if ($CutoverBffSettings) {
    if ($PSCmdlet.ShouldProcess("$bffAppName App Settings", "Set Redis__Endpoint (managed identity)")) {
        $hostName = az resource show @subArgs --resource-group $ResourceGroup --name $redisName --resource-type Microsoft.Cache/redisEnterprise --query "properties.hostName" -o tsv
        if ($LASTEXITCODE -ne 0 -or -not $hostName) {
            Exit-WithError "Could not read the host name of '$redisName' (Microsoft.Cache/redisEnterprise) in '$ResourceGroup'." 5
        }
        # BFF App Service typically lives in rg-spaarke-{env}, not the Redis RG.
        $bffRg = "rg-spaarke-$Environment"
        az webapp config appsettings set @subArgs `
            --resource-group $bffRg `
            --name $bffAppName `
            --settings `
                "Redis__Enabled=true" `
                "Redis__InstanceName=spaarke:" `
                "Redis__Endpoint=${hostName}:10000" `
                "Redis__AllowInMemoryFallback=false" `
            --output none
        if ($LASTEXITCODE -ne 0) {
            Exit-WithError "BFF App Settings cutover failed (exit $LASTEXITCODE)" $LASTEXITCODE
        }
        Write-Host "  Set Redis__Endpoint=${hostName}:10000 on '$bffAppName' (ConnectionStrings__Redis, if any, left in place — see -RemoveBffConnectionString)."
    }
}

# ---------------------------------------------------------------------------
# Remove the BFF's Redis connection-string settings (task 242b) — only once it has Redis__Endpoint
# ---------------------------------------------------------------------------
if ($RemoveBffConnectionString) {
    $bffRg = "rg-spaarke-$Environment"
    $endpointSetting = az webapp config appsettings list @subArgs --resource-group $bffRg --name $bffAppName --query "[?name=='Redis__Endpoint'].value | [0]" -o tsv
    if ($LASTEXITCODE -ne 0 -or -not $endpointSetting) {
        Exit-WithError "'$bffAppName' has no Redis__Endpoint: removing the connection string would leave it without Redis. Run -CutoverBffSettings first." 8
    }
    $thisCacheHost = az resource show @subArgs --resource-group $ResourceGroup --name $redisName --resource-type 'Microsoft.Cache/redisEnterprise' --query 'properties.hostName' -o tsv 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $thisCacheHost -or $endpointSetting -ne "${thisCacheHost}:10000") {
        Exit-WithError "'$bffAppName' Redis__Endpoint is '$endpointSetting', not this cache ('${thisCacheHost}:10000'): refusing to remove its connection string." 8
    }
    if ($PSCmdlet.ShouldProcess("$bffAppName App Settings", "Remove ConnectionStrings__Redis and Redis__ConnectionString")) {
        az webapp config appsettings delete @subArgs `
            --resource-group $bffRg `
            --name $bffAppName `
            --setting-names ConnectionStrings__Redis Redis__ConnectionString `
            --output none
        if ($LASTEXITCODE -ne 0) {
            Exit-WithError "Removing the Redis connection-string settings failed (exit $LASTEXITCODE)" $LASTEXITCODE
        }
        Write-Host "  Removed ConnectionStrings__Redis / Redis__ConnectionString from '$bffAppName' (it keeps Redis__Endpoint=$endpointSetting; the Key Vault secret is untouched)."
    }
}

# ---------------------------------------------------------------------------
# Deploy alerts (FR-04 of spaarke-redis-cache-remediation-r2)
# ---------------------------------------------------------------------------
if ($DeployAlerts) {
    if (-not $ActionGroupResourceId) {
        Exit-WithError "-DeployAlerts requires -ActionGroupResourceId (full Azure resource ID of the on-call action group)." 6
    }
    if (-not (Test-Path $alertsBicep)) {
        Exit-WithError "Alerts Bicep template not found: $alertsBicep" 7
    }

    Write-Host ""
    Write-Host "Deploying Redis cache alerts (FR-04 — alerts.bicep)..."
    Write-Host "  Template       : $alertsBicep"
    Write-Host "  App Insights   : $AppInsightsName"
    Write-Host "  ActionGroup ID : $ActionGroupResourceId"

    $alertParams = @(
        "redisCacheName=$redisName",
        "appInsightsName=$AppInsightsName",
        "actionGroupResourceId=$ActionGroupResourceId",
        "environment=$Environment"
    )

    if ($WhatIfPreference) {
        Write-Host "  Mode           : what-if (no resources will be created)"
        az deployment group what-if @subArgs `
            --resource-group $ResourceGroup `
            --template-file $alertsBicep `
            --parameters $alertParams `
            --no-pretty-print
        if ($LASTEXITCODE -ne 0) {
            Exit-WithError "Alert deploy what-if failed (exit $LASTEXITCODE)" $LASTEXITCODE
        }
    } elseif ($PSCmdlet.ShouldProcess("alerts in $ResourceGroup targeting $redisName + $AppInsightsName", "Deploy 3 Redis cache alerts via alerts.bicep")) {
        az deployment group create @subArgs `
            --resource-group $ResourceGroup `
            --template-file $alertsBicep `
            --parameters $alertParams `
            --query "properties.provisioningState" -o tsv
        if ($LASTEXITCODE -ne 0) {
            Exit-WithError "Alert deploy failed (exit $LASTEXITCODE)" $LASTEXITCODE
        }
        Write-Host "  Alerts deployed. Verify with: az monitor metrics alert list -g $ResourceGroup"
    }
}

# ---------------------------------------------------------------------------
# Post-deploy verification (skip in -WhatIf mode)
# ---------------------------------------------------------------------------
if (-not $WhatIfPreference -and (Test-Path $validationScript)) {
    Write-Host "Post-deploy verification..."
    $validationArgs = @{ RedisName = $redisName; ResourceGroup = $ResourceGroup }
    $expectedPrincipals = Get-ExpectedPrincipalIds
    if ($expectedPrincipals.Count -gt 0) { $validationArgs.ExpectedPrincipalIds = $expectedPrincipals }
    if ($SubscriptionId) { $validationArgs.SubscriptionId = $SubscriptionId }
    if ($CutoverBffSettings -or $RemoveBffConnectionString) {
        # The BFF settings were just changed: confirm Redis__Endpoint names this cache and no connection string remains
        # after a removal.
        $validationArgs.BffAppName = $bffAppName
        $validationArgs.BffResourceGroup = "rg-spaarke-$Environment"
        if ($RemoveBffConnectionString) { $validationArgs.RequireNoBffConnectionString = $true }
    }
    & $validationScript @validationArgs
    if ($LASTEXITCODE -ne 0) {
        Exit-WithError "Post-deploy verification failed (exit $LASTEXITCODE)" $LASTEXITCODE
    }
}

Write-Host ""
Write-Host "Deploy-RedisCache.ps1 completed successfully."
exit 0
