<#
.SYNOPSIS
    eBilling R1 task 049 (owner decision D-121) - lowers sprk_billingevent.sprk_amount's minimum so an Invoice Line can be a credit.

.DESCRIPTION
    Idempotent Dataverse Web API metadata script. Checks the column, widens it if needed, and reports; a second run reports the
    item as skipped and makes no write (no publish either).

    Item:
      1  sprk_billingevent.sprk_amount (Money) - MinValue lowered from 0 to -922337203685477, the minimum sprk_rate already has.
         Only the minimum changes: the maximum (1,000,000,000), the precision and everything else are sent back exactly as read.

    Why (D-121): sprk_quantity and sprk_rate already accept negatives, so a D-105 credit line (a reversal) is a legitimate invoice line,
    but sprk_amount refused it and the 049 transaction would fail. Credit lines are stored as billed; the totals calculator (016)
    sums them as signed values.

    WIDEN ONLY. The script never raises the minimum, never touches the maximum, and never changes a column of another type. If the
    current minimum is already at or below the target the item is skipped. A same-name column that is not Money STOPS the script with
    exit code 2 and an ESCALATION message. The change is not destructive: every stored value stays valid.

    Publish is entity-scoped (PublishXml for sprk_billingevent), and only when this run wrote metadata.
    This script is NOT run by the task that adds it. The owner's OK is needed to run it on dev; task 053 adds it to SpaarkeMaster.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the change belongs to (header MSCRM.SolutionUniqueName).

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingBillingEventAmountRange.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingBillingEventAmountRange.ps1

.NOTES
    Project: ebilling-r1, task 049 part A (D-121). Dev only in R1. Pattern: scripts/ebilling/Deploy-EbillingPendingSpendSchema.ps1.
    Exit codes: 0 = done (changed / skipped), 1 = error, 2 = escalation (conflicting).
#>

using module .\EbillingSchema.psm1

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = $(if ($env:DATAVERSE_URL) { $env:DATAVERSE_URL } else { 'https://spaarkedev1.crm.dynamics.com' }),
    [string]$SolutionUniqueName = 'SpaarkeMaster'
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$DryRun = [bool]$WhatIfPreference
$Table = 'sprk_billingevent'
$Column = 'sprk_amount'

# The minimum sprk_rate (and every Money column's _base companion) already has. A Money minimum cannot go lower.
$TargetMin = [long]-922337203685477

$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$DryRun -Entity $Table
$script:Created = $State.Created
$script:Changed = $State.Changed
$script:Skipped = $State.Skipped

$Path = "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.MoneyAttributeMetadata"

Write-Host ""
Write-Host "eBilling task 049 - $Table.$Column minimum (D-121, credit lines)" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Solution: $SolutionUniqueName   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })"
Write-Host ""

try {
    if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=LogicalName" -Retries 4)) { throw "$Table does not exist in $EnvironmentUrl." }

    Write-Host "1. $Table.$Column minimum" -ForegroundColor Cyan

    # A same-name column of another type is an escalation, never a change (the typed cast below would 404 on it).
    $attr = Get-Attr $Table $Column
    if (-not $attr) { throw "$Table.$Column does not exist in $EnvironmentUrl (task 010's schema script creates the line columns)." }
    Assert-AttrType $attr $Column 'Money'

    $a = Get-DvOrNull $Path -Retries 4
    if (-not $a) { throw "$Table.$Column metadata could not be read as Money." }
    $current = [double]$a.MinValue
    Write-Host ("  current: precision {0}, {1} .. {2}" -f $a.Precision, $a.MinValue, $a.MaxValue)

    if ($current -le [double]$TargetMin) {
        $script:Skipped.Add("$Table.$Column minimum")
        Write-Host "  skipped  minimum is already $($a.MinValue) (at or below $TargetMin); nothing to widen" -ForegroundColor Yellow
    } elseif ($DryRun) {
        $script:Changed.Add("$Table.$Column minimum")
        Write-Host "  WOULD SET $Column minimum $($a.MinValue) -> $TargetMin (maximum $($a.MaxValue) unchanged)" -ForegroundColor Cyan
    } else {
        $was = $a.MinValue
        $maxBefore = $a.MaxValue
        $a.MinValue = $TargetMin
        Invoke-WhilePropagating { Invoke-Dv -Path $Path -Method PUT -Body $a -ExtraHeaders @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null }

        # Eventually consistent: wait for a read that shows the new minimum, and confirm nothing else moved.
        $chk = $null
        for ($i = 0; $i -lt 12; $i++) {
            $chk = Get-DvOrNull $Path
            if ($chk -and [double]$chk.MinValue -le [double]$TargetMin) { break }
            Start-Sleep -Seconds 5
        }
        if (-not $chk -or [double]$chk.MinValue -gt [double]$TargetMin) { throw "$Table.$Column minimum is still $($chk.MinValue) after the update." }
        if ([double]$chk.MaxValue -ne [double]$maxBefore) { throw "$Table.$Column maximum changed from $maxBefore to $($chk.MaxValue); the script must only widen the minimum. Investigate before re-running." }
        $script:Changed.Add("$Table.$Column minimum")
        Write-Host "  CHANGED  $Column minimum $was -> $($chk.MinValue) (maximum $($chk.MaxValue) unchanged)" -ForegroundColor Green
    }

    Write-Host "Publish" -ForegroundColor Cyan
    $wrote = ($script:Created.Count + $script:Changed.Count) -gt 0
    if ($DryRun) {
        Write-Host "  WhatIf: publish skipped" -ForegroundColor Cyan
    } elseif (-not $wrote) {
        Write-Host "  nothing changed - no publish" -ForegroundColor Yellow
    } else {
        Invoke-Dv -Path 'PublishXml' -Method POST -Body @{
            ParameterXml = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>"
        } | Out-Null
        Write-Host "  published $Table" -ForegroundColor Green
    }
}
catch [EscalationException] {
    Write-Host ""
    Write-Host "ESCALATION - STOPPED, nothing further was changed: $($_.Exception.Message)" -ForegroundColor Red
    exit 2
}
catch {
    Write-Host ""
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "The script is re-runnable." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "================ Summary ================" -ForegroundColor Cyan
Write-Host ("changed : {0}  {1}" -f $script:Changed.Count, ($script:Changed -join ', '))
Write-Host ("skipped : {0}" -f $script:Skipped.Count)
if ($DryRun) { Write-Host "WhatIf run: nothing was written." -ForegroundColor Cyan }
exit 0
