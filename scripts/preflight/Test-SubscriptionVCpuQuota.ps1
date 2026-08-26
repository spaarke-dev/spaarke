<#
.SYNOPSIS
H0 preflight: verify subscription vCPU headroom per SKU family per region for
the App Service Plan + AI Search stamps that H2a will provision.

.DESCRIPTION
Queries `az vm list-usage --location <region>` and confirms that the CURRENT +
REQUESTED vCPU count per SKU family does not exceed the regional limit. Runs
against a caller-supplied hashtable of family -> requested vCPU so different
customer profiles (Small vs Standard vs Premium per §3A A1) can request
different SKU families.

If any family's projected usage exceeds its quota, the check fails with a
diagnostic naming the shortfall — actionable for filing an Azure quota-bump
support ticket (1-3 day lead time per spec.md § External Dependencies).

Returns the shared preflight PSCustomObject contract:
    Result / CheckName / Headroom / Diagnostic

.PARAMETER SubscriptionId
Azure subscription to query. Optional; uses currently-selected `az account` if
omitted.

.PARAMETER Region
Azure region for vCPU quota query.

.PARAMETER RequestedVCpuPerFamily
Hashtable of SKU-family-name (matched against `az vm list-usage` `name.value`)
-> requested vCPU count for the +1-customer stamp. Example:
    @{ 'standardDv5Family' = 8; 'standardFsv2Family' = 4 }

The default reflects a Standard-profile stamp (App Service Plan P1v3 = 2 vCPU
+ AI Search Standard = 2 vCPU + 4 vCPU headroom for elasticity):
    @{ 'standardDv5Family' = 8 }

Callers with different profiles override this.

.PARAMETER UsageJsonPath
(Test-mode escape hatch) — path to a pre-captured JSON file containing the
`az vm list-usage` output. When set, skips live `az` call.

.OUTPUTS
[PSCustomObject] with Result, CheckName, Headroom, Diagnostic.

.EXAMPLE
$r = & ./Test-SubscriptionVCpuQuota.ps1 -Region eastus
if ($r.Result -eq 'Fail') { throw $r.Diagnostic }

.EXAMPLE
$r = & ./Test-SubscriptionVCpuQuota.ps1 `
    -Region eastus `
    -RequestedVCpuPerFamily @{ 'standardDv5Family' = 99999 }
# Simulated fail path — absurdly high request → Result = Fail with shortfall.

.NOTES
Escalation: if `az vm list-usage` command name or output shape changes, STOP
and escalate per root CLAUDE.md §6.
#>

[CmdletBinding()]
[OutputType([PSCustomObject])]
param(
    [Parameter()][string]$SubscriptionId,

    [Parameter(Mandatory=$true)][string]$Region,

    [Parameter()][hashtable]$RequestedVCpuPerFamily = @{
        'standardDv5Family' = 8
    },

    [Parameter()][string]$UsageJsonPath
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

$checkName = 'SubscriptionVCpuQuota'

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
    # -- Fetch usage --------------------------------------------------------
    if ($UsageJsonPath) {
        if (-not (Test-Path $UsageJsonPath)) {
            $r = New-PreflightResult Fail @{ region = $Region } `
                "Test-mode UsageJsonPath '$UsageJsonPath' not found."
            $r | Write-Output
            exit 2
        }
        $usageJson = Get-Content -Raw -Path $UsageJsonPath
    }
    else {
        if ($SubscriptionId) {
            $null = az account set --subscription $SubscriptionId 2>&1
            if ($LASTEXITCODE -ne 0) {
                $r = New-PreflightResult Fail @{ region = $Region; subscriptionId = $SubscriptionId } `
                    "az account set failed for subscription '$SubscriptionId'. Run `az login` and verify subscription id."
                $r | Write-Output
                exit 3
            }
        }

        $usageJson = az vm list-usage --location $Region --output json 2>&1
        if ($LASTEXITCODE -ne 0) {
            $r = New-PreflightResult Fail @{ region = $Region } `
                "az vm list-usage failed for region '$Region'. Raw: $usageJson"
            $r | Write-Output
            exit 4
        }
    }

    try { $usage = $usageJson | ConvertFrom-Json -ErrorAction Stop }
    catch {
        $r = New-PreflightResult Fail @{ region = $Region } `
            "Failed to parse az vm list-usage output as JSON. Escalate per README (API drift?). Error: $_"
        $r | Write-Output
        exit 5
    }

    if (-not $usage -or $usage.Count -eq 0) {
        $r = New-PreflightResult Fail @{ region = $Region } `
            "az vm list-usage returned empty for region '$Region'. Possible: region invalid, or subscription lacks Compute usage access."
        $r | Write-Output
        exit 6
    }

    # Verify shape
    $sample = $usage[0]
    foreach ($field in 'name','currentValue','limit') {
        if ($sample.PSObject.Properties.Name -notcontains $field) {
            $r = New-PreflightResult Fail @{ region = $Region } `
                "az vm list-usage output missing expected field '$field' — API shape may have drifted. Escalate per README."
            $r | Write-Output
            exit 7
        }
    }

    # -- Per-family headroom check ------------------------------------------
    $perFamilyReport = @{}
    $failedFamilies = @()

    foreach ($family in $RequestedVCpuPerFamily.Keys) {
        $requested = [int]$RequestedVCpuPerFamily[$family]

        # `az vm list-usage` returns entries where name.value is e.g.
        # "standardDv5Family", "cores" (regional total), "standardFsv2Family" etc.
        # Case-insensitive exact match on name.value.
        $matched = $usage | Where-Object {
            $nv = if ($_.name.PSObject.Properties.Name -contains 'value') { $_.name.value } else { $_.name }
            $nv -and ($nv.ToString().ToLowerInvariant() -eq $family.ToLowerInvariant())
        }

        if (-not $matched -or $matched.Count -eq 0) {
            $perFamilyReport[$family] = @{
                observed        = 'not-reported'
                limit           = 'not-reported'
                requested       = $requested
                projected_after = 'unknown'
                fits            = $false
            }
            $failedFamilies += $family
            continue
        }

        # Take the first matched entry (family names are unique in az vm list-usage per region)
        $observed = [int]$matched[0].currentValue
        $limit    = [int]$matched[0].limit

        $projected = $observed + $requested
        $fits = ($projected -le $limit)

        $perFamilyReport[$family] = @{
            observed        = $observed
            limit           = $limit
            requested       = $requested
            projected_after = $projected
            fits            = $fits
        }

        if (-not $fits) { $failedFamilies += $family }
    }

    $headroom = @{
        region    = $Region
        perFamily = $perFamilyReport
    }

    if ($failedFamilies.Count -eq 0) {
        $r = New-PreflightResult Pass $headroom `
            "Subscription vCPU headroom OK in '$Region' for all $($RequestedVCpuPerFamily.Count) SKU family(ies)."
        $r | Write-Output
        exit 0
    }

    $lines = @("Subscription vCPU headroom INSUFFICIENT in region '$Region' for $($failedFamilies.Count) SKU family(ies).")
    foreach ($f in $failedFamilies) {
        $p = $perFamilyReport[$f]
        if ($p.observed -eq 'not-reported') {
            $lines += "  - Family '$f': NOT REPORTED by az vm list-usage for region '$Region' (requested $($p.requested) vCPU). Possible causes: family name mismatch, SKU not offered in region, or subscription lacks Compute usage access. File Azure vCPU quota-bump support ticket if family is expected (1-3 day lead time)."
        }
        else {
            $shortfall = [int]$p.projected_after - [int]$p.limit
            $lines += "  - Family '$f': observed $($p.observed) vCPU + requested $($p.requested) vCPU = projected $($p.projected_after), regional quota = $($p.limit). SHORTFALL: $shortfall vCPU. File Azure vCPU quota-bump support ticket (1-3 day lead time per External Dependencies)."
        }
    }
    $r = New-PreflightResult Fail $headroom ($lines -join "`n")
    $r | Write-Output
    exit 1
}
catch {
    $r = New-PreflightResult Fail @{ region = $Region } `
        "Unhandled error in Test-SubscriptionVCpuQuota: $($_.Exception.Message)"
    $r | Write-Output
    exit 99
}
