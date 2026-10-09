<#
.SYNOPSIS
Operator check: verify Azure OpenAI regional TPM headroom for the OpenAI
deployments a customer stamp creates.

.DESCRIPTION
Queries `az cognitiveservices usage list --location <region>` and confirms that
the CURRENT + REQUESTED TPM (per Azure quota name) does not exceed the
regional quota. The default request is the stamp deployment set (task 247):
openai.bicep's `deployments` default, mirrored in C# by PinnedModelCatalog.cs,
which the L2 H0 probe (ArmCognitiveServicesTpmProbe) evaluates the same way.
Change all three together.

Returns the shared preflight PSCustomObject contract:
    Result / CheckName / Headroom / Diagnostic

Exits 0 on Pass, non-zero on Fail (or on unexpected failure).

.PARAMETER SubscriptionId
Azure subscription to query. If omitted, uses the currently-selected
`az account`.

.PARAMETER Region
The stamp's OpenAI region — customer.bicep `openAiLocation` (default westus3),
not the primary stamp region.

.PARAMETER RequestedTpmPerModel
Hashtable of Azure quota name -> requested TPM (in thousands). Default = the
stamp deployment set (all DataZoneStandard):
  OpenAI.DataZoneStandard.gpt-4o                 = 150
  OpenAI.DataZoneStandard.gpt4.1-mini            = 200  (the `gpt-4o-mini` deployment)
  OpenAI.DataZoneStandard.text-embedding-3-large = 350

Keys are matched at the END of the usage `name.value`, so a full quota name
matches exactly one entry. Azure spells these names itself — note
`gpt4.1-mini` has no hyphen after "gpt".

.PARAMETER UsageJsonPath
(Test-mode escape hatch) — path to a pre-captured JSON file containing the
`az cognitiveservices usage list` output. When set, the script skips the live
`az` call and parses this file instead. Enables offline pass/fail-path testing
without a live Azure session.

.OUTPUTS
[PSCustomObject] with Result, CheckName, Headroom, Diagnostic.

.EXAMPLE
$r = & ./Test-AzureOpenAiTpmHeadroom.ps1 -Region westus3
if ($r.Result -eq 'Fail') { throw $r.Diagnostic }

.EXAMPLE
$r = & ./Test-AzureOpenAiTpmHeadroom.ps1 `
    -Region westus3 `
    -RequestedTpmPerModel @{ 'OpenAI.DataZoneStandard.gpt-4o' = 999999 }
# Simulated fail path — Result = Fail with diagnostic citing observed vs requested.

.NOTES
Escalation: if `az cognitiveservices usage list` command name changes OR
returns a shape that doesn't include `currentValue` / `limit` / `name.value`,
STOP and escalate per root CLAUDE.md §6 — do NOT silently adapt. The H0
handler depends on this contract.
#>

[CmdletBinding()]
[OutputType([PSCustomObject])]
param(
    [Parameter()][string]$SubscriptionId,

    [Parameter(Mandatory=$true)][string]$Region,

    [Parameter()][hashtable]$RequestedTpmPerModel = @{
        'OpenAI.DataZoneStandard.gpt-4o'                 = 150
        'OpenAI.DataZoneStandard.gpt4.1-mini'            = 200
        'OpenAI.DataZoneStandard.text-embedding-3-large' = 350
    },

    [Parameter()][string]$UsageJsonPath
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

$checkName = 'AzureOpenAiTpmHeadroom'

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
    # -- Query usage ---------------------------------------------------------
    if ($UsageJsonPath) {
        if (-not (Test-Path $UsageJsonPath)) {
            $r = New-PreflightResult Fail @{ region = $Region } "Test-mode UsageJsonPath '$UsageJsonPath' not found."
            $r | Write-Output
            exit 2
        }
        $usageJson = Get-Content -Raw -Path $UsageJsonPath
    }
    else {
        # Pass the subscription per call — never `az account set`, which changes the operator's
        # default subscription for every other shell.
        $subscriptionArgs = if ($SubscriptionId) { @('--subscription', $SubscriptionId) } else { @() }
        $usageJson = az cognitiveservices usage list --location $Region @subscriptionArgs --output json 2>&1
        if ($LASTEXITCODE -ne 0) {
            $r = New-PreflightResult Fail @{ region = $Region } `
                "az cognitiveservices usage list failed for region '$Region'. Raw: $usageJson"
            $r | Write-Output
            exit 4
        }
    }

    # -- Parse & validate shape ---------------------------------------------
    try { $usage = $usageJson | ConvertFrom-Json -ErrorAction Stop }
    catch {
        $r = New-PreflightResult Fail @{ region = $Region } `
            "Failed to parse az cognitiveservices usage list output as JSON. Escalate per README (API drift?). Error: $_"
        $r | Write-Output
        exit 5
    }

    if (-not $usage -or $usage.Count -eq 0) {
        $r = New-PreflightResult Fail @{ region = $Region } `
            "az cognitiveservices usage list returned empty for region '$Region'. Possible causes: region has no OpenAI account, or subscription lacks Cognitive Services. Escalate if unexpected."
        $r | Write-Output
        exit 6
    }

    # Verify shape — first entry must have name.value + currentValue + limit
    $sample = $usage[0]
    foreach ($field in 'name','currentValue','limit') {
        if ($sample.PSObject.Properties.Name -notcontains $field) {
            $r = New-PreflightResult Fail @{ region = $Region } `
                "az cognitiveservices usage list output missing expected field '$field' — API shape may have drifted. Escalate per README."
            $r | Write-Output
            exit 7
        }
    }

    # -- Per-model headroom check -------------------------------------------
    $perModelReport = @{}
    $failedModels = @()

    foreach ($modelName in $RequestedTpmPerModel.Keys) {
        $requested = [int]$RequestedTpmPerModel[$modelName]

        # Azure reports usage names like "Standard.gpt-4o", "OpenAI.Standard.gpt-4o",
        # or (in some quota reports) "Tokens Per Minute (thousands) - <model>".
        # Match at END of the name.value, preceded by a separator OR start-of-string,
        # so that (e.g.) requesting 'gpt-4o' does NOT also match 'gpt-4o-mini'.
        # Separators recognized: `.` `-` `_` `/` ` ` (space). Case-insensitive.
        $escaped  = [regex]::Escape($modelName)
        $pattern  = "(?i)(?:^|[.\-/_ ])$escaped$"
        $matched = $usage | Where-Object {
            $nv = if ($_.name.PSObject.Properties.Name -contains 'value') { $_.name.value } else { $_.name }
            $nv -and ($nv.ToString() -match $pattern)
        }

        if (-not $matched -or $matched.Count -eq 0) {
            # If we can't find the quota entry, treat as fail — H0 shouldn't proceed on unknown quota.
            $perModelReport[$modelName] = @{
                observed        = 'not-reported'
                limit           = 'not-reported'
                requested       = $requested
                projected_after = 'unknown'
                fits            = $false
            }
            $failedModels += $modelName
            continue
        }

        # If multiple entries match (e.g. Standard + Provisioned variants), sum current + take max limit
        $observed = ($matched | Measure-Object -Property currentValue -Sum).Sum
        $limit    = ($matched | Measure-Object -Property limit        -Maximum).Maximum

        $projected = [int]$observed + $requested
        $fits = ($projected -le [int]$limit)

        $perModelReport[$modelName] = @{
            observed        = [int]$observed
            limit           = [int]$limit
            requested       = $requested
            projected_after = $projected
            fits            = $fits
        }

        if (-not $fits) { $failedModels += $modelName }
    }

    $headroom = @{
        region   = $Region
        perModel = $perModelReport
    }

    if ($failedModels.Count -eq 0) {
        $r = New-PreflightResult Pass $headroom `
            "OpenAI regional TPM headroom OK in '$Region' for all $($RequestedTpmPerModel.Count) model deployments."
        $r | Write-Output
        exit 0
    }

    # Build actionable diagnostic per constraint: "MUST cite BOTH observed headroom AND requested capacity + region"
    $lines = @("OpenAI regional TPM headroom INSUFFICIENT in region '$Region' for $($failedModels.Count) model(s).")
    foreach ($m in $failedModels) {
        $p = $perModelReport[$m]
        if ($p.observed -eq 'not-reported') {
            $lines += "  - Model '$m': NOT REPORTED by az cognitiveservices usage list for region '$Region' (requested $($p.requested) TPM). Verify model name matches Azure's naming (e.g. 'gpt-4o', not 'GPT-4') and that model is available in region. File quota-bump request if expected."
        }
        else {
            $shortfall = [int]$p.projected_after - [int]$p.limit
            $lines += "  - Model '$m': observed usage $($p.observed) + requested $($p.requested) = projected $($p.projected_after), regional quota = $($p.limit). SHORTFALL: $shortfall. File quota-bump request per External Dependencies (1-3 day lead time)."
        }
    }
    $r = New-PreflightResult Fail $headroom ($lines -join "`n")
    $r | Write-Output
    exit 1
}
catch {
    $r = New-PreflightResult Fail @{ region = $Region } `
        "Unhandled error in Test-AzureOpenAiTpmHeadroom: $($_.Exception.Message)"
    $r | Write-Output
    exit 99
}
