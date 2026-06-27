#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Rotate Spaarke BFF Redis cache keys via safe-window algorithm with automatic rollback.

.DESCRIPTION
    Implements the FR-07 safe-window key rotation for `spaarke-bff-redis-{env}`:

      1. Pre-flight: verify Redis, Key Vault, and App Service exist; verify operator
         has required permissions (probe via `az` get-only calls).
      2. Read current connection string from Key Vault (`Redis-ConnectionString`)
         and capture it as CONN_OLD (rollback baseline).
      3. Regenerate the Redis **Secondary** key (Primary still serves the live
         BFF connection — this is the "safe window").
      4. Build CONN_NEW pointing at the new Secondary key.
      5. Upsert `Redis-ConnectionString` in Key Vault with CONN_NEW (creates a new
         secret version; App Settings `@Microsoft.KeyVault(...)` reference resolves
         automatically on the next App Service config refresh).
      6. Restart the BFF App Service.
      7. Poll `https://{bff-host}/healthz` for HTTP 200 with a 120s timeout.
      8. SUCCESS path: regenerate the Redis **Primary** key — eliminates the
         now-unused old primary; rotation complete.
      9. FAILURE path (healthz fails to return 200 within 120s): roll back by
         re-upserting CONN_OLD into Key Vault, restart BFF, exit non-zero with a
         clear message. Operator can manually investigate without lost service.

    Every step emits a `RedisKeyRotation` customEvent to Application Insights via
    the public ingestion endpoint `https://dc.services.visualstudio.com/v2/track`
    (no SDK needed — direct REST POST). Emitted properties: `environment`,
    `step`, `outcome`, `duration_ms`. The script is also fully idempotent under
    `-WhatIf` (no `az` mutations are issued in plan mode).

    Replaces the 90-day manual rotation procedure documented in
    `docs/guides/redis-cache-azure-setup.md` §6.

    Production and staging environments REJECT execution without an explicit
    `-Force` flag per NFR-05 — same gate as `Deploy-RedisCache.ps1`.

.PARAMETER Environment
    Target environment: dev, staging, or prod.

.PARAMETER Force
    Required to target `prod` or `staging` environments per NFR-05. Without
    `-Force`, the script exits with code 2 and an NFR-05 message.

.PARAMETER VerboseLog
    Emit additional `Write-Verbose` step-level diagnostic output (alias for
    `-Verbose`; preserved as a named switch for parity with the operator-facing
    Deploy-RedisCache.ps1 calling conventions).

.PARAMETER WhatIf
    Native PowerShell `-WhatIf` via `SupportsShouldProcess`. Shows the planned
    rotation actions only — no Azure resources are read or modified beyond
    safe `get` calls used for pre-flight discovery. App Insights custom events
    are also suppressed under `-WhatIf`.

.PARAMETER ResourceMapFile
    Optional JSON file overriding the built-in env-to-resource map. Useful for
    one-off targets or when staging/prod resource names are finalized by the
    operator. Schema:
        {
          "dev":     { "redis": "...", "keyVault": "...", "appService": "...",
                       "resourceGroup": "...", "bffResourceGroup": "...",
                       "bffHost": "...", "appInsightsConnectionString": "..." },
          "staging": { ... },
          "prod":    { ... }
        }

.EXAMPLE
    pwsh ./scripts/Rotate-RedisKey.ps1 -Environment dev -WhatIf

    Plan-only run against dev. Prints the rotation plan; performs only read-only
    `az` discovery calls; emits no Key Vault writes, no Redis regenerate-key
    calls, no App Service restarts, no App Insights events.

.EXAMPLE
    pwsh ./scripts/Rotate-RedisKey.ps1 -Environment dev

    Live rotation against dev — Secondary regenerate → KV upsert → BFF restart
    → /healthz poll → Primary regenerate. On any healthz failure, rolls back KV
    to CONN_OLD and restarts BFF.

.EXAMPLE
    pwsh ./scripts/Rotate-RedisKey.ps1 -Environment prod -Force

    Live rotation against prod — requires `-Force` per NFR-05.

.EXAMPLE
    pwsh ./scripts/Rotate-RedisKey.ps1 -Environment staging -ResourceMapFile ./config/rotation-map.json -Force

    Live rotation against staging using an operator-supplied resource map.

.NOTES
    Project : spaarke-redis-cache-remediation-r2
    Task    : 010
    Version : 1.0.0
    Constraints:
      FR-07  — safe-window algorithm with automatic rollback on healthz failure.
      NFR-05 — reject prod/staging without `-Force`.
      ADR-028 — KV reference pattern preserved (`@Microsoft.KeyVault(...)`).

    Exit codes:
       0  Success — rotation complete (both Secondary and Primary regenerated,
          BFF healthy on /healthz).
       2  NFR-05 violation — `-Environment prod|staging` without `-Force`.
       3  Pre-flight failure — Redis, Key Vault, or App Service not reachable
          with current `az` identity.
       4  Key Vault read failed — could not capture CONN_OLD.
       5  Secondary regenerate failed.
       6  Key Vault upsert with CONN_NEW failed.
       7  BFF App Service restart failed.
       8  /healthz did not return HTTP 200 within 120s — ROLLBACK ATTEMPTED.
          Inspect logs for rollback outcome.
       9  Primary regenerate failed (post-success-path) — rotation succeeded
          but the old primary remains valid. Operator must regenerate manually.
      10  Rollback itself failed — MANUAL INTERVENTION REQUIRED.
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('dev', 'staging', 'prod')]
    [string]$Environment,

    [switch]$Force,

    [switch]$VerboseLog,

    [string]$ResourceMapFile
)

$ErrorActionPreference = 'Stop'
if ($VerboseLog) { $VerbosePreference = 'Continue' }

# ---------------------------------------------------------------------------
# NFR-05 prod/staging gate (mirrors Deploy-RedisCache.ps1)
# ---------------------------------------------------------------------------
if (($Environment -in @('prod', 'staging')) -and (-not $Force)) {
    $Host.UI.WriteErrorLine("NFR-05: -Environment $Environment requires -Force flag. Key rotation against $Environment must be an explicit operator decision. Aborting.")
    exit 2
}

# ---------------------------------------------------------------------------
# Env-to-resource map
#   - dev: confirmed (R1 baseline)
#   - staging / prod: TBD by operator. Provide values either by:
#       (a) editing the $defaultMap block below, or
#       (b) passing -ResourceMapFile <path>.
#   The script fails pre-flight if any required slot is empty.
# ---------------------------------------------------------------------------
$defaultMap = @{
    dev = @{
        redis                       = 'spaarke-bff-redis-dev'
        keyVault                    = 'spaarke-spekvcert'
        appService                  = 'spaarke-bff-dev'
        resourceGroup               = 'spe-infrastructure-westus2'
        bffResourceGroup            = 'rg-spaarke-dev'
        bffHost                     = 'spaarke-bff-dev.azurewebsites.net'
        appInsightsConnectionString = ''   # Optional: set to enable App Insights event emission.
    }
    staging = @{
        redis                       = 'spaarke-bff-redis-staging'
        keyVault                    = ''
        appService                  = 'spaarke-bff-staging'
        resourceGroup               = 'rg-spaarke-staging'
        bffResourceGroup            = 'rg-spaarke-staging'
        bffHost                     = ''
        appInsightsConnectionString = ''
    }
    prod = @{
        redis                       = 'spaarke-bff-redis-prod'
        keyVault                    = ''
        appService                  = 'spaarke-bff-prod'
        resourceGroup               = 'rg-spaarke-prod'
        bffResourceGroup            = 'rg-spaarke-prod'
        bffHost                     = ''
        appInsightsConnectionString = ''
    }
}

if ($ResourceMapFile) {
    if (-not (Test-Path $ResourceMapFile)) {
        Write-Error "ResourceMapFile not found: $ResourceMapFile"
        exit 3
    }
    Write-Verbose "Loading resource map override from $ResourceMapFile"
    $override = Get-Content -Raw -Path $ResourceMapFile | ConvertFrom-Json
    foreach ($env in @('dev', 'staging', 'prod')) {
        if ($override.PSObject.Properties.Name -contains $env) {
            foreach ($prop in $override.$env.PSObject.Properties) {
                $defaultMap[$env][$prop.Name] = $prop.Value
            }
        }
    }
}

$cfg = $defaultMap[$Environment]
$kvSecretName = 'Redis-ConnectionString'
$healthTimeoutSec = 120
$healthPollIntervalSec = 5

# ---------------------------------------------------------------------------
# Banner
# ---------------------------------------------------------------------------
$modeLabel = if ($WhatIfPreference) { 'what-if (plan only)' } else { 'live' }
Write-Information "Rotate-RedisKey.ps1 starting" -InformationAction Continue
Write-Information "  Environment    : $Environment" -InformationAction Continue
Write-Information "  Redis          : $($cfg.redis)" -InformationAction Continue
Write-Information "  KeyVault       : $($cfg.keyVault)" -InformationAction Continue
Write-Information "  App Service    : $($cfg.appService)" -InformationAction Continue
Write-Information "  RG (Redis/KV)  : $($cfg.resourceGroup)" -InformationAction Continue
Write-Information "  RG (BFF)       : $($cfg.bffResourceGroup)" -InformationAction Continue
Write-Information "  /healthz host  : $($cfg.bffHost)" -InformationAction Continue
Write-Information "  Mode           : $modeLabel" -InformationAction Continue
Write-Information "" -InformationAction Continue

# ---------------------------------------------------------------------------
# Helper: App Insights customEvent emission
# ---------------------------------------------------------------------------
function Send-RotationEvent {
    param(
        [Parameter(Mandatory)] [string]$Step,
        [Parameter(Mandatory)] [string]$Outcome,
        [int]$DurationMs = 0,
        [string]$Detail = ''
    )

    if ($WhatIfPreference) {
        Write-Verbose "[what-if] Skipping App Insights event: step=$Step outcome=$Outcome duration_ms=$DurationMs"
        return
    }
    if ([string]::IsNullOrWhiteSpace($cfg.appInsightsConnectionString)) {
        Write-Verbose "App Insights connection string not configured for $Environment; skipping event emission."
        return
    }

    # Parse iKey out of the connection string (`InstrumentationKey=<guid>;...`)
    $iKey = ($cfg.appInsightsConnectionString -split ';' |
             Where-Object { $_ -like 'InstrumentationKey=*' } |
             ForEach-Object { ($_ -split '=', 2)[1] }) | Select-Object -First 1

    if ([string]::IsNullOrWhiteSpace($iKey)) {
        Write-Verbose "App Insights InstrumentationKey not parseable; skipping event."
        return
    }

    $payload = @{
        name = "Microsoft.ApplicationInsights.$iKey.Event"
        time = (Get-Date).ToUniversalTime().ToString('o')
        iKey = $iKey
        data = @{
            baseType = 'EventData'
            baseData = @{
                ver = 2
                name = 'RedisKeyRotation'
                properties = @{
                    environment = $Environment
                    step        = $Step
                    outcome     = $Outcome
                    detail      = $Detail
                }
                measurements = @{
                    duration_ms = $DurationMs
                }
            }
        }
    } | ConvertTo-Json -Depth 8 -Compress

    try {
        Invoke-RestMethod `
            -Uri 'https://dc.services.visualstudio.com/v2/track' `
            -Method Post `
            -ContentType 'application/json' `
            -Body $payload `
            -TimeoutSec 10 | Out-Null
        Write-Verbose "AppInsights event sent: step=$Step outcome=$Outcome"
    } catch {
        Write-Warning "AppInsights event POST failed (non-fatal): $($_.Exception.Message)"
    }
}

function Measure-Step {
    param(
        [Parameter(Mandatory)] [string]$Step,
        [Parameter(Mandatory)] [scriptblock]$Action
    )
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        & $Action
        $sw.Stop()
        Send-RotationEvent -Step $Step -Outcome 'success' -DurationMs ([int]$sw.ElapsedMilliseconds)
    } catch {
        $sw.Stop()
        Send-RotationEvent -Step $Step -Outcome 'failure' -DurationMs ([int]$sw.ElapsedMilliseconds) -Detail $_.Exception.Message
        throw
    }
}

# ---------------------------------------------------------------------------
# Step 1: Pre-flight
# ---------------------------------------------------------------------------
Write-Information "[1/8] Pre-flight: verifying resources + permissions..." -InformationAction Continue

foreach ($slot in @('redis', 'keyVault', 'appService', 'resourceGroup', 'bffResourceGroup', 'bffHost')) {
    if ([string]::IsNullOrWhiteSpace($cfg.$slot)) {
        Write-Error "Pre-flight: '$slot' is not configured for environment '$Environment'. Edit `$defaultMap in this script or pass -ResourceMapFile."
        exit 3
    }
}

try {
    $redisState = az redis show --resource-group $cfg.resourceGroup --name $cfg.redis --query provisioningState -o tsv 2>$null
    if (-not $redisState) { throw "Redis '$($cfg.redis)' not found in RG '$($cfg.resourceGroup)' or operator lacks read permission." }
    Write-Verbose "  Redis provisioningState = $redisState"

    $kvCheck = az keyvault show --name $cfg.keyVault --query name -o tsv 2>$null
    if (-not $kvCheck) { throw "Key Vault '$($cfg.keyVault)' not reachable." }
    Write-Verbose "  Key Vault reachable: $kvCheck"

    $appCheck = az webapp show --resource-group $cfg.bffResourceGroup --name $cfg.appService --query name -o tsv 2>$null
    if (-not $appCheck) { throw "App Service '$($cfg.appService)' not found in RG '$($cfg.bffResourceGroup)'." }
    Write-Verbose "  App Service reachable: $appCheck"

    # Permission probe: confirm we can READ the secret (a write probe would mutate).
    $kvProbe = az keyvault secret show --vault-name $cfg.keyVault --name $kvSecretName --query name -o tsv 2>$null
    if (-not $kvProbe) { throw "Operator lacks 'get' permission on KV secret '$kvSecretName' (or secret missing)." }
    Write-Verbose "  KV secret 'get' probe OK: $kvProbe"
} catch {
    Write-Error "Pre-flight failed: $($_.Exception.Message)"
    Send-RotationEvent -Step 'preflight' -Outcome 'failure' -Detail $_.Exception.Message
    exit 3
}
Send-RotationEvent -Step 'preflight' -Outcome 'success'

# ---------------------------------------------------------------------------
# Step 2: Read CONN_OLD
# ---------------------------------------------------------------------------
Write-Information "[2/8] Reading current Redis-ConnectionString (CONN_OLD) from Key Vault..." -InformationAction Continue

$connOld = $null
if ($PSCmdlet.ShouldProcess("$($cfg.keyVault)/$kvSecretName", "Read CONN_OLD from Key Vault")) {
    try {
        Measure-Step -Step 'read_conn_old' -Action {
            $script:connOld = az keyvault secret show --vault-name $cfg.keyVault --name $kvSecretName --query value -o tsv 2>$null
            if (-not $script:connOld) { throw "Failed to read $kvSecretName from $($cfg.keyVault)." }
        }
    } catch {
        Write-Error "Key Vault read failed: $($_.Exception.Message)"
        exit 4
    }
} else {
    Write-Information "  [what-if] Would read $kvSecretName from $($cfg.keyVault)." -InformationAction Continue
}

# ---------------------------------------------------------------------------
# Step 3: Regenerate Secondary key, build CONN_NEW
# ---------------------------------------------------------------------------
Write-Information "[3/8] Regenerating Redis Secondary key + building CONN_NEW..." -InformationAction Continue

$connNew = $null
if ($PSCmdlet.ShouldProcess("$($cfg.redis) Secondary key", "az redis regenerate-key --key-type Secondary")) {
    try {
        Measure-Step -Step 'regenerate_secondary' -Action {
            az redis regenerate-key --resource-group $cfg.resourceGroup --name $cfg.redis --key-type Secondary --output none
            if ($LASTEXITCODE -ne 0) { throw "az redis regenerate-key (Secondary) failed (exit $LASTEXITCODE)" }

            $newSecondary = az redis list-keys --resource-group $cfg.resourceGroup --name $cfg.redis --query secondaryKey -o tsv
            $hostName     = az redis show     --resource-group $cfg.resourceGroup --name $cfg.redis --query hostName     -o tsv
            $sslPort      = az redis show     --resource-group $cfg.resourceGroup --name $cfg.redis --query sslPort      -o tsv
            $script:connNew = "${hostName}:${sslPort},password=${newSecondary},ssl=True,abortConnect=False"
        }
    } catch {
        Write-Error "Secondary regenerate failed: $($_.Exception.Message)"
        exit 5
    }
} else {
    Write-Information "  [what-if] Would regenerate Secondary key + build CONN_NEW for $($cfg.redis)." -InformationAction Continue
}

# ---------------------------------------------------------------------------
# Step 4: Upsert KV with CONN_NEW
# ---------------------------------------------------------------------------
Write-Information "[4/8] Upserting Key Vault secret '$kvSecretName' with CONN_NEW..." -InformationAction Continue

if ($PSCmdlet.ShouldProcess("$($cfg.keyVault)/$kvSecretName", "Upsert Redis-ConnectionString (CONN_NEW)")) {
    try {
        Measure-Step -Step 'kv_upsert_new' -Action {
            az keyvault secret set --vault-name $cfg.keyVault --name $kvSecretName --value $script:connNew --output none
            if ($LASTEXITCODE -ne 0) { throw "az keyvault secret set (CONN_NEW) failed (exit $LASTEXITCODE)" }
        }
    } catch {
        Write-Error "KV upsert (CONN_NEW) failed: $($_.Exception.Message)"
        exit 6
    }
} else {
    Write-Information "  [what-if] Would upsert KV secret with CONN_NEW (new secret version)." -InformationAction Continue
}

# ---------------------------------------------------------------------------
# Step 5: Restart BFF
# ---------------------------------------------------------------------------
Write-Information "[5/8] Restarting BFF App Service '$($cfg.appService)'..." -InformationAction Continue

if ($PSCmdlet.ShouldProcess($cfg.appService, "az webapp restart")) {
    try {
        Measure-Step -Step 'bff_restart' -Action {
            az webapp restart --resource-group $cfg.bffResourceGroup --name $cfg.appService --output none
            if ($LASTEXITCODE -ne 0) { throw "az webapp restart failed (exit $LASTEXITCODE)" }
        }
    } catch {
        Write-Error "BFF restart failed: $($_.Exception.Message)"
        exit 7
    }
} else {
    Write-Information "  [what-if] Would restart $($cfg.appService)." -InformationAction Continue
}

# ---------------------------------------------------------------------------
# Step 6: Poll /healthz
# ---------------------------------------------------------------------------
Write-Information "[6/8] Polling https://$($cfg.bffHost)/healthz (timeout ${healthTimeoutSec}s)..." -InformationAction Continue

$healthOk = $false
if ($PSCmdlet.ShouldProcess("https://$($cfg.bffHost)/healthz", "Poll for HTTP 200")) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $deadline = (Get-Date).AddSeconds($healthTimeoutSec)
    while ((Get-Date) -lt $deadline) {
        try {
            $resp = Invoke-WebRequest -Uri "https://$($cfg.bffHost)/healthz" -Method Get -UseBasicParsing -TimeoutSec 10 -ErrorAction Stop
            if ($resp.StatusCode -eq 200) {
                $healthOk = $true
                break
            }
        } catch {
            Write-Verbose "  healthz not ready: $($_.Exception.Message)"
        }
        Start-Sleep -Seconds $healthPollIntervalSec
    }
    $sw.Stop()
    Send-RotationEvent -Step 'healthz_poll' -Outcome ($(if ($healthOk) { 'success' } else { 'failure' })) -DurationMs ([int]$sw.ElapsedMilliseconds)
} else {
    Write-Information "  [what-if] Would poll https://$($cfg.bffHost)/healthz for HTTP 200." -InformationAction Continue
    $healthOk = $true
}

# ---------------------------------------------------------------------------
# Step 7a: SUCCESS — regenerate Primary
# Step 7b: FAILURE — rollback
# ---------------------------------------------------------------------------
if ($healthOk) {
    Write-Information "[7/8] /healthz returned 200 — regenerating Primary key to eliminate the now-unused old primary..." -InformationAction Continue

    if ($PSCmdlet.ShouldProcess("$($cfg.redis) Primary key", "az redis regenerate-key --key-type Primary")) {
        try {
            Measure-Step -Step 'regenerate_primary' -Action {
                az redis regenerate-key --resource-group $cfg.resourceGroup --name $cfg.redis --key-type Primary --output none
                if ($LASTEXITCODE -ne 0) { throw "az redis regenerate-key (Primary) failed (exit $LASTEXITCODE)" }
            }
        } catch {
            Write-Error "Primary regenerate FAILED — rotation succeeded but old primary remains valid. Operator should regenerate manually. Detail: $($_.Exception.Message)"
            Send-RotationEvent -Step 'rotation' -Outcome 'partial' -Detail 'primary_regenerate_failed'
            exit 9
        }
    } else {
        Write-Information "  [what-if] Would regenerate Primary key for $($cfg.redis)." -InformationAction Continue
    }

    Write-Information "[8/8] Rotation complete." -InformationAction Continue
    Send-RotationEvent -Step 'rotation' -Outcome 'success'
    exit 0
} else {
    Write-Warning "/healthz did NOT return 200 within ${healthTimeoutSec}s. Initiating rollback..."
    Send-RotationEvent -Step 'rotation' -Outcome 'healthz_timeout'

    # Rollback: restore CONN_OLD, restart BFF
    try {
        if ($PSCmdlet.ShouldProcess("$($cfg.keyVault)/$kvSecretName", "ROLLBACK: restore CONN_OLD")) {
            Measure-Step -Step 'rollback_kv_restore' -Action {
                az keyvault secret set --vault-name $cfg.keyVault --name $kvSecretName --value $script:connOld --output none
                if ($LASTEXITCODE -ne 0) { throw "Rollback KV restore failed (exit $LASTEXITCODE)" }
            }
        }
        if ($PSCmdlet.ShouldProcess($cfg.appService, "ROLLBACK: restart BFF")) {
            Measure-Step -Step 'rollback_bff_restart' -Action {
                az webapp restart --resource-group $cfg.bffResourceGroup --name $cfg.appService --output none
                if ($LASTEXITCODE -ne 0) { throw "Rollback restart failed (exit $LASTEXITCODE)" }
            }
        }
        Write-Error "ROLLBACK COMPLETE. Rotation aborted; CONN_OLD restored. Investigate /healthz failure before retrying."
        Send-RotationEvent -Step 'rollback' -Outcome 'success'
        exit 8
    } catch {
        Write-Error "ROLLBACK FAILED — MANUAL INTERVENTION REQUIRED. Restore $kvSecretName in $($cfg.keyVault) to the prior secret version + restart $($cfg.appService). Detail: $($_.Exception.Message)"
        Send-RotationEvent -Step 'rollback' -Outcome 'failure' -Detail $_.Exception.Message
        exit 10
    }
}
