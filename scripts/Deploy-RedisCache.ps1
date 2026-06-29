#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Deploy Spaarke BFF Redis cache instance per environment.

.DESCRIPTION
    Idempotent provisioning of `spaarke-bff-redis-{env}` Azure Cache for Redis instance.
    Uses `redis.bicep` module + per-environment `.bicepparam` files at
    `infrastructure/bicep/parameters/redis-{env}.bicepparam`.

    Optionally upserts the Redis connection string to Key Vault (`Redis-ConnectionString`)
    and cuts over `spaarke-bff-{env}` App Service settings to use Key Vault references.

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

.PARAMETER KeyVaultName
    Key Vault to upsert the `Redis-ConnectionString` secret into. Required if
    `-CutoverBffSettings` is specified. See `notes/dev-cutover-baseline.md`
    (project Phase 3) for the canonical dev Key Vault name.

.PARAMETER VerifyOnly
    Skip deploy; run `tests/manual/RedisValidationTests.ps1` against the existing
    instance and exit with the validation harness's exit code.

.PARAMETER CutoverBffSettings
    After successful deploy, update `spaarke-bff-{env}` App Service settings:
      Redis__Enabled                = true
      Redis__InstanceName           = spaarke:
      ConnectionStrings__Redis      = @Microsoft.KeyVault(VaultName=...;SecretName=Redis-ConnectionString)
      Redis__AllowInMemoryFallback  = false
    Requires `-KeyVaultName`.

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
    pwsh ./scripts/Deploy-RedisCache.ps1 -Environment dev -KeyVaultName spaarke-spekvcert -CutoverBffSettings

    Deploy dev Redis, upsert connection string to Key Vault, cut over BFF App Settings,
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

    [string]$KeyVaultName,

    [switch]$VerifyOnly,

    [switch]$CutoverBffSettings,

    [switch]$Force,

    [switch]$DeployAlerts,

    [string]$AppInsightsName,

    [string]$ActionGroupResourceId
)

$ErrorActionPreference = 'Stop'
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
$kvSecretName     = 'Redis-ConnectionString'
$redisName        = "spaarke-bff-redis-$Environment"

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
    Write-Error "Bicep param file not found: $bicepParam"
    exit 3
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
Write-Host "  KeyVault       : $(if ($KeyVaultName) { $KeyVaultName } else { '(not specified)' })"
Write-Host "  Mode           : $modeLabel"
Write-Host ""

# ---------------------------------------------------------------------------
# Verify-only path (NFR-06)
# ---------------------------------------------------------------------------
if ($VerifyOnly) {
    Write-Host "Verify-only mode: invoking RedisValidationTests.ps1..."
    if (-not (Test-Path $validationScript)) {
        Write-Error "Validation harness not found: $validationScript"
        exit 4
    }
    & pwsh $validationScript -RedisName $redisName -ResourceGroup $ResourceGroup
    exit $LASTEXITCODE
}

# ---------------------------------------------------------------------------
# Idempotency check (NFR-01)
# ---------------------------------------------------------------------------
$existing = $null
try {
    $existing = az redis show --resource-group $ResourceGroup --name $redisName --query "provisioningState" -o tsv 2>$null
} catch {
    $existing = $null
}

if ($existing -eq 'Succeeded') {
    Write-Host "Redis instance '$redisName' already exists (provisioningState=Succeeded). Idempotent: skipping create."
} else {
    if ($PSCmdlet.ShouldProcess("$redisName in $ResourceGroup", "Deploy Bicep (redis.bicep with redis-$Environment.bicepparam)")) {
        Write-Host "Deploying Bicep template..."
        az deployment group create `
            --resource-group $ResourceGroup `
            --template-file $bicepModule `
            --parameters $bicepParam `
            --query "properties.provisioningState" -o tsv
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Bicep deploy failed (exit $LASTEXITCODE)"
            exit $LASTEXITCODE
        }
    }
}

# ---------------------------------------------------------------------------
# Key Vault secret upsert
# ---------------------------------------------------------------------------
if ($KeyVaultName) {
    if ($PSCmdlet.ShouldProcess("$kvSecretName in $KeyVaultName", "Upsert KV secret with Redis connection string")) {
        $primaryKey = az redis list-keys --resource-group $ResourceGroup --name $redisName --query primaryKey -o tsv
        $hostName   = az redis show --resource-group $ResourceGroup --name $redisName --query hostName -o tsv
        $sslPort    = az redis show --resource-group $ResourceGroup --name $redisName --query sslPort -o tsv
        $connString = "${hostName}:${sslPort},password=${primaryKey},ssl=True,abortConnect=False"
        az keyvault secret set `
            --vault-name $KeyVaultName `
            --name $kvSecretName `
            --value $connString `
            --output none
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Key Vault secret upsert failed (exit $LASTEXITCODE)"
            exit $LASTEXITCODE
        }
        Write-Host "  Upserted KV secret '$kvSecretName' in '$KeyVaultName'."
    }
}

# ---------------------------------------------------------------------------
# Cut over BFF App Settings
# ---------------------------------------------------------------------------
if ($CutoverBffSettings) {
    if (-not $KeyVaultName) {
        Write-Error "-CutoverBffSettings requires -KeyVaultName."
        exit 5
    }
    if ($PSCmdlet.ShouldProcess("$bffAppName App Settings", "Cutover Redis settings to Key Vault reference")) {
        $kvRef = "@Microsoft.KeyVault(VaultName=$KeyVaultName;SecretName=$kvSecretName)"
        # BFF App Service typically lives in rg-spaarke-{env}, not the Redis RG.
        $bffRg = "rg-spaarke-$Environment"
        az webapp config appsettings set `
            --resource-group $bffRg `
            --name $bffAppName `
            --settings `
                "Redis__Enabled=true" `
                "Redis__InstanceName=spaarke:" `
                "ConnectionStrings__Redis=$kvRef" `
                "Redis__AllowInMemoryFallback=false" `
            --output none
        if ($LASTEXITCODE -ne 0) {
            Write-Error "BFF App Settings cutover failed (exit $LASTEXITCODE)"
            exit $LASTEXITCODE
        }
        Write-Host "  Cut over '$bffAppName' App Settings to KV reference."
    }
}

# ---------------------------------------------------------------------------
# Deploy alerts (FR-04 of spaarke-redis-cache-remediation-r2)
# ---------------------------------------------------------------------------
if ($DeployAlerts) {
    if (-not $ActionGroupResourceId) {
        Write-Error "-DeployAlerts requires -ActionGroupResourceId (full Azure resource ID of the on-call action group)."
        exit 6
    }
    if (-not (Test-Path $alertsBicep)) {
        Write-Error "Alerts Bicep template not found: $alertsBicep"
        exit 7
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
        az deployment group what-if `
            --resource-group $ResourceGroup `
            --template-file $alertsBicep `
            --parameters $alertParams `
            --no-pretty-print
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Alert deploy what-if failed (exit $LASTEXITCODE)"
            exit $LASTEXITCODE
        }
    } elseif ($PSCmdlet.ShouldProcess("alerts in $ResourceGroup targeting $redisName + $AppInsightsName", "Deploy 3 Redis cache alerts via alerts.bicep")) {
        az deployment group create `
            --resource-group $ResourceGroup `
            --template-file $alertsBicep `
            --parameters $alertParams `
            --query "properties.provisioningState" -o tsv
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Alert deploy failed (exit $LASTEXITCODE)"
            exit $LASTEXITCODE
        }
        Write-Host "  Alerts deployed. Verify with: az monitor metrics alert list -g $ResourceGroup"
    }
}

# ---------------------------------------------------------------------------
# Post-deploy verification (skip in -WhatIf mode)
# ---------------------------------------------------------------------------
if (-not $WhatIfPreference -and (Test-Path $validationScript)) {
    Write-Host "Post-deploy verification..."
    & pwsh $validationScript -RedisName $redisName -ResourceGroup $ResourceGroup
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Post-deploy verification failed (exit $LASTEXITCODE)"
        exit $LASTEXITCODE
    }
}

Write-Host ""
Write-Host "Deploy-RedisCache.ps1 completed successfully."
exit 0
