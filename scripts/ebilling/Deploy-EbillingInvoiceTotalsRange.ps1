<#
.SYNOPSIS
    eBilling R1 task 049 (owner decision D-127) - lowers the minimum of the stored invoice totals on sprk_invoice so a credit memo can be saved.

.DESCRIPTION
    Idempotent Dataverse Web API metadata script. For each column below it reads the Money metadata, widens the minimum if needed and
    reports; a second run reports every item as skipped and makes no write (no publish either).

    Items (all Money, precision 2, maximum 1,000,000,000, minimum 0 before this script):
      1  sprk_invoice.sprk_totalamount              the billed total (existing column; read by InvoiceTotalsService)
      2  sprk_invoice.sprk_feestotal                written by InvoiceTotalsService.RecalculateAsync
      3  sprk_invoice.sprk_expensestotal            (same)
      4  sprk_invoice.sprk_lineadjustmentstotal     (same)
      5  sprk_invoice.sprk_invoiceadjustmentstotal  (same)
      6  sprk_invoice.sprk_approvedtotal            (same)
    Each MinValue is lowered to -922337203685477, the minimum sprk_rate already has. Only the minimum changes: the maximum, the precision
    and everything else are sent back exactly as read.

    Why (D-127): an all-credit invoice (a credit memo) has a negative billed total, negative fees, and a negative approved total. It is created,
    reviewed and approved like any invoice, and an approved credit memo reduces counted spend (D-103 sums signed totals). With a minimum of 0
    the 049 invoice create and the totals write would fault on the platform minimum. A disallowed credit (D-128) can also push a stored
    adjustments total below 0.

    WIDEN ONLY. The script never raises a minimum, never touches a maximum, and never changes a column of another type. A column whose current
    minimum is already at or below the target is skipped. A same-name column that is not Money STOPS the script with exit code 2 and an
    ESCALATION message. A missing column stops it with exit code 1 (the invoice header schema script creates the five totals). The change is not
    destructive: every stored value stays valid.

    Publish is entity-scoped (PublishXml for sprk_invoice), and only when this run wrote metadata.
    This script is NOT run by the task that adds it. The owner's OK is needed to run it on dev; task 053 adds it to SpaarkeMaster.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the change belongs to (header MSCRM.SolutionUniqueName).

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingInvoiceTotalsRange.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingInvoiceTotalsRange.ps1

.NOTES
    Project: ebilling-r1, task 049 part A (D-127; extends D-121). Dev only in R1. Pattern: scripts/ebilling/Deploy-EbillingBillingEventAmountRange.ps1.
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
$Table = 'sprk_invoice'
$Columns = @(
    'sprk_totalamount',
    'sprk_feestotal',
    'sprk_expensestotal',
    'sprk_lineadjustmentstotal',
    'sprk_invoiceadjustmentstotal',
    'sprk_approvedtotal'
)

# The minimum sprk_rate (and every Money column's _base companion) already has. A Money minimum cannot go lower.
$TargetMin = [long]-922337203685477

$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$DryRun -Entity $Table
$script:Created = $State.Created
$script:Changed = $State.Changed
$script:Skipped = $State.Skipped

Write-Host ""
Write-Host "eBilling task 049 - $Table stored totals minimum (D-127, credit memos)" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Solution: $SolutionUniqueName   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })"
Write-Host ""

try {
    if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=LogicalName" -Retries 4)) { throw "$Table does not exist in $EnvironmentUrl." }

    # Check every column's type first, so a conflicting column stops the script before anything is written.
    foreach ($Column in $Columns) {
        $attr = Get-Attr $Table $Column
        if (-not $attr) { throw "$Table.$Column does not exist in $EnvironmentUrl (Deploy-EbillingInvoiceHeaderSchema.ps1 creates the totals)." }
        Assert-AttrType $attr $Column 'Money'
    }

    $n = 0
    foreach ($Column in $Columns) {
        $n++
        $Path = "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.MoneyAttributeMetadata"
        Write-Host "$n. $Table.$Column minimum" -ForegroundColor Cyan

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
