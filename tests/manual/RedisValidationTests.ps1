<#
.SYNOPSIS
    Post-deploy verification of a per-environment Azure Managed Redis (keyless, Microsoft Entra only).

.DESCRIPTION
    Called by scripts/Deploy-RedisCache.ps1 (after a deploy, and by -VerifyOnly) with -RedisName and -ResourceGroup.
    Read-only: Azure Resource Manager reads only, no data-plane access.

    Task 242b (customer-provisioning-orchestration-r1, 2026-10-04) rewrote this harness. The previous version
    (Sprint 4, 2025) checked files under `src/api/Spe.Bff.Api/`, a layout that no longer exists, so it failed at its
    first test on every run, and its connectivity test needed a Redis connection string, which keyless caches do not
    have. Code-level behaviour (endpoint → managed identity, the connection-string refusal, the null-object kill
    switch) is covered by tests/unit/Sprk.Bff.Api.Tests/Infrastructure/DI/CacheModuleTests.cs and the control plane's
    DispatchModuleTests.

    Checks (each failure increments the failure count; exit 1 if any failed):
      1. The cluster is Microsoft.Cache/redisEnterprise, provisioningState Succeeded, resourceState Running, a
         Balanced_* SKU (high availability is reported).
      2. Database `default`: accessKeysAuthentication Disabled, clientProtocol Encrypted, OSSCluster, port 10000
         (ADR-009 / owner D12-D13).
      3. At least one `default` access-policy assignment; with -ExpectedPrincipalIds the set must match exactly.
      4. With -BffAppName: the app's `Redis__Endpoint` setting equals `{hostName}:10000`; with
         -RequireNoBffConnectionString it also has no `ConnectionStrings__Redis` / `Redis__ConnectionString`.

.PARAMETER RedisName
    Cache name, e.g. spaarke-bff-redis-dev.

.PARAMETER ResourceGroup
    Resource group of the cache.

.PARAMETER ExpectedPrincipalIds
    Optional object ids that must be exactly the cache's access-policy assignments.

.PARAMETER BffAppName
    Optional App Service whose `Redis__Endpoint` must point at this cache.

.PARAMETER BffResourceGroup
    Resource group of -BffAppName.

.PARAMETER RequireNoBffConnectionString
    Fail if -BffAppName still has a Redis connection-string setting (after the task 242b removal).

.PARAMETER SubscriptionId
    Subscription of the cache and the app. Default: the az CLI's current subscription.

.EXAMPLE
    pwsh tests/manual/RedisValidationTests.ps1 -RedisName spaarke-bff-redis-dev -ResourceGroup spe-infrastructure-westus2 `
        -BffAppName spaarke-bff-dev -BffResourceGroup rg-spaarke-dev
#>
param(
    [Parameter(Mandatory)][string]$RedisName,
    [Parameter(Mandatory)][string]$ResourceGroup,
    [string[]]$ExpectedPrincipalIds = @(),
    [string]$BffAppName,
    [string]$BffResourceGroup,
    [switch]$RequireNoBffConnectionString,
    [string]$SubscriptionId
)

$ErrorActionPreference = 'Stop'
$script:FailureCount = 0
if ([bool]$BffAppName -ne [bool]$BffResourceGroup) {
    Write-Host "ERROR: pass -BffAppName and -BffResourceGroup together." -ForegroundColor Red
    exit 2
}
$apiVersion = '2025-07-01'
# `pwsh -File ... -ExpectedPrincipalIds a,b` passes one string: accept comma-separated values either way.
$ExpectedPrincipalIds = @($ExpectedPrincipalIds | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })

function Pass([string]$Message) { Write-Host "  [OK]   $Message" -ForegroundColor Green }
function Fail([string]$Message) { Write-Host "  [FAIL] $Message" -ForegroundColor Red; $script:FailureCount++ }

function Get-ArmJson([string]$Path) {
    $out = az rest --method get --url "https://management.azure.com$Path`?api-version=$apiVersion" 2>&1
    if ($LASTEXITCODE -ne 0) {
        # Show why (403, expired login, 404) instead of reporting every error as "not found".
        $reason = ($out | Out-String).Trim()
        if ($reason -notmatch 'ResourceNotFound|NotFound') { Write-Host "  [--]   az rest: $reason" -ForegroundColor DarkYellow }
        return $null
    }
    if (-not $out) { return $null }
    return ($out | Out-String | ConvertFrom-Json)
}

Write-Host "Azure Managed Redis validation - $RedisName ($ResourceGroup)" -ForegroundColor Cyan

$subId = if ($SubscriptionId) { $SubscriptionId } else { az account show --query id -o tsv }
$clusterPath = "/subscriptions/$subId/resourceGroups/$ResourceGroup/providers/Microsoft.Cache/redisEnterprise/$RedisName"

# 1. Cluster
Write-Host "[1] Cluster" -ForegroundColor Yellow
$cluster = Get-ArmJson $clusterPath
if (-not $cluster) {
    Fail "No Microsoft.Cache/redisEnterprise '$RedisName' in '$ResourceGroup' (subscription $subId)."
} else {
    if ($cluster.properties.provisioningState -eq 'Succeeded') { Pass 'provisioningState Succeeded' } else { Fail "provisioningState $($cluster.properties.provisioningState)" }
    if ($cluster.properties.resourceState -eq 'Running') { Pass 'resourceState Running' } else { Fail "resourceState $($cluster.properties.resourceState)" }
    if ($cluster.sku.name -like 'Balanced_*') { Pass "SKU $($cluster.sku.name), high availability $($cluster.properties.highAvailability)" } else { Fail "SKU $($cluster.sku.name) (expected Balanced_*)" }
}

# 2. Database
Write-Host "[2] Database 'default'" -ForegroundColor Yellow
$database = if ($cluster) { Get-ArmJson "$clusterPath/databases/default" } else { $null }
if (-not $database) {
    Fail "Database 'default' not found."
} else {
    if ($database.properties.accessKeysAuthentication -eq 'Disabled') { Pass 'access keys disabled (Microsoft Entra only)' } else { Fail "accessKeysAuthentication $($database.properties.accessKeysAuthentication) (expected Disabled)" }
    if ($database.properties.clientProtocol -eq 'Encrypted') { Pass 'TLS only' } else { Fail "clientProtocol $($database.properties.clientProtocol)" }
    if ($database.properties.clusteringPolicy -eq 'OSSCluster') { Pass 'OSSCluster' } else { Fail "clusteringPolicy $($database.properties.clusteringPolicy) (expected OSSCluster)" }
    if ($database.properties.port -eq 10000) { Pass 'port 10000' } else { Fail "port $($database.properties.port)" }
}

# 3. Access policy
Write-Host "[3] Access-policy assignments" -ForegroundColor Yellow
$assignments = if ($database) { Get-ArmJson "$clusterPath/databases/default/accessPolicyAssignments" } else { $null }
$objectIds = @($assignments.value | Where-Object { $_.properties.accessPolicyName -eq 'default' } | ForEach-Object { $_.properties.user.objectId })
if ($objectIds.Count -eq 0) {
    Fail 'No access-policy assignment — with access keys disabled nobody can use the cache.'
} else {
    Pass "$($objectIds.Count) assignment(s): $($objectIds -join ', ')"
    if ($ExpectedPrincipalIds.Count -gt 0) {
        $missing = @($ExpectedPrincipalIds | Where-Object { $_ -notin $objectIds })
        $extra = @($objectIds | Where-Object { $_ -notin $ExpectedPrincipalIds })
        if ($missing.Count -eq 0 -and $extra.Count -eq 0) { Pass 'exactly the expected principals' } else { Fail "missing: $($missing -join ', '); unexpected: $($extra -join ', ')" }
    }
}

# 4. BFF setting
if ($BffAppName) {
    Write-Host "[4] $BffAppName Redis__Endpoint" -ForegroundColor Yellow
    $expected = "$($cluster.properties.hostName):10000"
    $settings = az webapp config appsettings list --subscription $subId --resource-group $BffResourceGroup --name $BffAppName -o json 2>$null | ConvertFrom-Json
    $actual = ($settings | Where-Object { $_.name -eq 'Redis__Endpoint' }).value
    if ($actual -eq $expected) { Pass "Redis__Endpoint = $expected" } else { Fail "Redis__Endpoint = '$actual' (expected '$expected')" }
    if ($RequireNoBffConnectionString) {
        $left = @($settings | Where-Object { $_.name -in @('ConnectionStrings__Redis', 'Redis__ConnectionString') } | ForEach-Object { $_.name })
        if ($left.Count -eq 0) { Pass 'no Redis connection-string setting' } else { Fail "still set: $($left -join ', ')" }
    }
}

Write-Host ""
if ($script:FailureCount -gt 0) {
    Write-Host "Validation FAILED ($script:FailureCount check(s))" -ForegroundColor Red
    exit 1
}
Write-Host "Validation PASSED" -ForegroundColor Green
exit 0
