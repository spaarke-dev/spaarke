<#
.SYNOPSIS
H0 preflight: verify Dataverse environment-creation rate quota has ≥1 slot
available in the current hourly bucket for the target tenant.

.DESCRIPTION
Queries `pac admin` for the current tenant's environment-count vs quota, and
approximates "how many envs have been created in the current hourly bucket"
via the CreatedOn timestamp on active environments (PAC exposes creation
timestamps but does not expose the raw per-hour rate meter). Because Dataverse
throttles env-creation at ~4/hour per tenant typical (per spec.md § External
Dependencies), H0 uses the min-slots-required arg to guarantee headroom BEFORE
H2c fires (Dataverse env create).

If PAC output shape drifts, fail with a diagnostic pointing at escalation per
root CLAUDE.md §6 (H0 contract requires stable shape).

.PARAMETER TenantId
Entra tenant id (informational — PAC uses the active auth profile). If not the
active `pac auth` profile, `pac auth select` first.

.PARAMETER MinSlotsRequired
Minimum number of environment-creation slots that MUST be free in the current
hourly bucket for this check to pass. Default: 1 (Model 2 needs 1 slot per
customer stamp).

.PARAMETER RateWindowHours
Hourly-bucket window used to count recently-created envs. Default: 1.

.PARAMETER RateLimit
Assumed hourly rate limit per tenant (Microsoft-documented soft limit is ~4/hr
per PPAC per spec.md § External Dependencies). Override only if a
tenant-specific quota bump is in effect. Default: 4.

.PARAMETER PacListJsonPath
(Test-mode escape hatch) — path to a pre-captured JSON file containing the
`pac admin list --json` output. When set, skips live PAC call.

.OUTPUTS
[PSCustomObject] with Result, CheckName, Headroom, Diagnostic.

.EXAMPLE
$r = & ./Test-DataverseEnvCreationRate.ps1 -TenantId $env:SPAARKE_TENANT_ID
if ($r.Result -eq 'Fail') { throw $r.Diagnostic }

.EXAMPLE
$r = & ./Test-DataverseEnvCreationRate.ps1 -MinSlotsRequired 10 -RateLimit 4
# Simulated fail path — asking for more slots than the hourly quota allows.

.NOTES
Escalation: if `pac admin list --json` command name changes, or output no
longer includes CreatedOn/created field, STOP and escalate per root
CLAUDE.md §6. H0 depends on this contract.
#>

[CmdletBinding()]
[OutputType([PSCustomObject])]
param(
    [Parameter()][string]$TenantId,

    [Parameter()][int]$MinSlotsRequired = 1,

    [Parameter()][int]$RateWindowHours = 1,

    [Parameter()][int]$RateLimit = 4,

    [Parameter()][string]$PacListJsonPath
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

$checkName = 'DataverseEnvCreationRate'

function New-PreflightResult {
    param(
        [ValidateSet('Pass','Fail')][string]$Result,
        [hashtable]$Headroom,
        [string]$Diagnostic
    )
    return [PSCustomObject]@{
        Result     = $Result
        CheckName  = $checkName
        Headroom   = $Headroom
        Diagnostic = $Diagnostic
    }
}

try {
    # Immediate arg sanity — if operator asks for more slots than the rate limit,
    # no query can pass.
    if ($MinSlotsRequired -gt $RateLimit) {
        $r = New-PreflightResult Fail @{
            tenantId          = $TenantId
            minSlotsRequired  = $MinSlotsRequired
            rateLimit         = $RateLimit
            rateWindowHours   = $RateWindowHours
        } "MinSlotsRequired ($MinSlotsRequired) exceeds tenant hourly RateLimit ($RateLimit env/hr). Cannot satisfy in a single hourly bucket. File Dataverse env-creation rate quota bump (1-3 day lead time)."
        $r | Write-Output
        exit 1
    }

    # -- Fetch env list -----------------------------------------------------
    if ($PacListJsonPath) {
        if (-not (Test-Path $PacListJsonPath)) {
            $r = New-PreflightResult Fail @{ tenantId = $TenantId } `
                "Test-mode PacListJsonPath '$PacListJsonPath' not found."
            $r | Write-Output
            exit 2
        }
        $pacJson = Get-Content -Raw -Path $PacListJsonPath
    }
    else {
        # Verify pac CLI available
        $pacCmd = Get-Command pac -ErrorAction SilentlyContinue
        if (-not $pacCmd) {
            $r = New-PreflightResult Fail @{ tenantId = $TenantId } `
                "PAC CLI (`pac`) not found on PATH. Install Power Platform CLI + run `pac auth create`."
            $r | Write-Output
            exit 3
        }

        # `pac admin list --json` is the current documented shape (2026)
        $pacJson = pac admin list --json 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0) {
            $r = New-PreflightResult Fail @{ tenantId = $TenantId } `
                "pac admin list --json failed. Verify PAC auth (`pac auth list`) selects the target tenant. Raw: $pacJson"
            $r | Write-Output
            exit 4
        }
    }

    try { $envs = $pacJson | ConvertFrom-Json -ErrorAction Stop }
    catch {
        $r = New-PreflightResult Fail @{ tenantId = $TenantId } `
            "Failed to parse pac admin list --json output. Escalate per README (PAC shape drift?). Error: $_"
        $r | Write-Output
        exit 5
    }

    if (-not $envs) {
        # Zero envs = zero recent env creations; check trivially passes rate-limit-wise.
        $r = New-PreflightResult Pass @{
            tenantId          = $TenantId
            observedRecentEnvs = 0
            rateLimit         = $RateLimit
            rateWindowHours   = $RateWindowHours
            slotsFree         = $RateLimit
            minSlotsRequired  = $MinSlotsRequired
        } "Dataverse env-creation rate has full headroom ($RateLimit/hr slots free)."
        $r | Write-Output
        exit 0
    }

    # Coerce to array
    if ($envs -isnot [System.Collections.IEnumerable] -or $envs -is [string]) { $envs = @($envs) }

    # Find a CreatedOn-like field. PAC has used both 'CreatedOn' and 'created' historically.
    $sample = $envs[0]
    $createdField = @('CreatedOn','created','CreatedTime','createdOn') |
        Where-Object { $sample.PSObject.Properties.Name -contains $_ } |
        Select-Object -First 1

    if (-not $createdField) {
        $r = New-PreflightResult Fail @{ tenantId = $TenantId } `
            "pac admin list output has no CreatedOn/created field. API shape may have drifted. Escalate per README."
        $r | Write-Output
        exit 6
    }

    # Count envs whose CreatedOn is within the last N hours
    $cutoff = (Get-Date).ToUniversalTime().AddHours(-$RateWindowHours)
    $recent = @()
    foreach ($e in $envs) {
        $raw = $e.$createdField
        if (-not $raw) { continue }
        try {
            $ts = ([datetime]$raw).ToUniversalTime()
            if ($ts -gt $cutoff) { $recent += $e }
        }
        catch {
            # Skip un-parseable timestamps — they're likely long-lived legacy envs.
            continue
        }
    }

    $observedRecent = $recent.Count
    $slotsFree      = $RateLimit - $observedRecent

    $headroom = @{
        tenantId           = $TenantId
        observedRecentEnvs = $observedRecent
        rateLimit          = $RateLimit
        rateWindowHours    = $RateWindowHours
        slotsFree          = $slotsFree
        minSlotsRequired   = $MinSlotsRequired
    }

    if ($slotsFree -ge $MinSlotsRequired) {
        $r = New-PreflightResult Pass $headroom `
            "Dataverse env-creation rate OK for tenant '$TenantId': $observedRecent envs created in last ${RateWindowHours}h, $slotsFree slots free (need $MinSlotsRequired)."
        $r | Write-Output
        exit 0
    }

    $r = New-PreflightResult Fail $headroom `
        "Dataverse env-creation rate EXHAUSTED for tenant '$TenantId': $observedRecent env(s) created in last ${RateWindowHours}h against rate limit $RateLimit/hr → $slotsFree slot(s) free (need $MinSlotsRequired). Wait for hourly bucket rollover, OR file rate-quota bump via PPAC support (1-3 day lead time)."
    $r | Write-Output
    exit 1
}
catch {
    $r = New-PreflightResult Fail @{ tenantId = $TenantId } `
        "Unhandled error in Test-DataverseEnvCreationRate: $($_.Exception.Message)"
    $r | Write-Output
    exit 99
}
