<#
.SYNOPSIS
    eBilling R1 task 049 (owner decisions D-127, D-129) - lowers the minimum of the stored invoice totals on sprk_invoice, and of the export
    batch total on sprk_billingexport, so a credit memo can be saved and batched.

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
      7  sprk_billingexport.sprk_totalamount        the batch total, the sum of the batch's approved invoice totals (task 150; D-129 (4))
    Each MinValue is lowered to -922337203685477, the minimum sprk_rate already has. Only the minimum changes: the maximum, the precision
    and everything else are sent back exactly as read.

    Why (D-127): an all-credit invoice (a credit memo) has a negative billed total, negative fees, and a negative approved total. It is created,
    reviewed and approved like any invoice, and an approved credit memo reduces counted spend (D-103 sums signed totals). With a minimum of 0
    the 049 invoice create and the totals write would fault on the platform minimum. A disallowed credit (D-128) can also push a stored
    adjustments total below 0. A batch of credit memos can sum below 0 (D-129 (4)).

    WIDEN ONLY. The script never raises a minimum, never touches a maximum, and never changes a column of another type. A column whose current
    minimum is already at or below the target is skipped. A same-name column that is not Money STOPS the script with exit code 2 and an
    ESCALATION message, before anything is written. A missing table or column stops it with exit code 1 (the eBilling schema scripts create
    them). The change is not destructive: every stored value stays valid.

    Publish is entity-scoped (PublishXml for each of sprk_invoice and sprk_billingexport that this run wrote), and only when this run wrote
    metadata. This script is NOT run by the task that adds it. The owner's OK is needed to run it on dev; task 053 adds it to SpaarkeMaster.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the change belongs to (header MSCRM.SolutionUniqueName).

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingInvoiceTotalsRange.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingInvoiceTotalsRange.ps1

.NOTES
    Project: ebilling-r1, task 049 part A (D-127, D-129; extends D-121). Dev only in R1. Pattern: scripts/ebilling/Deploy-EbillingBillingEventAmountRange.ps1.
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
$Items = @(
    @{ Table = 'sprk_invoice'; Column = 'sprk_totalamount' },
    @{ Table = 'sprk_invoice'; Column = 'sprk_feestotal' },
    @{ Table = 'sprk_invoice'; Column = 'sprk_expensestotal' },
    @{ Table = 'sprk_invoice'; Column = 'sprk_lineadjustmentstotal' },
    @{ Table = 'sprk_invoice'; Column = 'sprk_invoiceadjustmentstotal' },
    @{ Table = 'sprk_invoice'; Column = 'sprk_approvedtotal' },
    @{ Table = 'sprk_billingexport'; Column = 'sprk_totalamount' }
)
$Tables = @($Items | ForEach-Object { $_.Table } | Select-Object -Unique)

# The minimum sprk_rate (and every Money column's _base companion) already has. A Money minimum cannot go lower.
$TargetMin = [long]-922337203685477

$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$DryRun -Entity $Tables[0]
$script:Created = $State.Created
$script:Changed = $State.Changed
$script:Skipped = $State.Skipped

Write-Host ""
Write-Host "eBilling task 049 - stored totals minimum (D-127 / D-129, credit memos)" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Solution: $SolutionUniqueName   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })"
Write-Host ""

try {
    foreach ($Table in $Tables) {
        if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=LogicalName" -Retries 4)) { throw "$Table does not exist in $EnvironmentUrl." }
    }

    # Check every column's type first, so a conflicting column stops the script before anything is written.
    foreach ($Item in $Items) {
        $attr = Get-Attr $Item.Table $Item.Column
        if (-not $attr) { throw "$($Item.Table).$($Item.Column) does not exist in $EnvironmentUrl (the eBilling schema scripts create it)." }
        Assert-AttrType $attr $Item.Column 'Money'
    }

    $n = 0
    $wroteTables = [System.Collections.Generic.List[string]]::new()
    foreach ($Item in $Items) {
        $n++
        $Table = $Item.Table
        $Column = $Item.Column
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
            if (-not $wroteTables.Contains($Table)) { $wroteTables.Add($Table) }
            Write-Host "  CHANGED  $Column minimum $was -> $($chk.MinValue) (maximum $($chk.MaxValue) unchanged)" -ForegroundColor Green
        }
    }

    Write-Host "Publish" -ForegroundColor Cyan
    if ($DryRun) {
        Write-Host "  WhatIf: publish skipped" -ForegroundColor Cyan
    } elseif ($wroteTables.Count -eq 0) {
        Write-Host "  nothing changed - no publish" -ForegroundColor Yellow
    } else {
        foreach ($PublishTable in $wroteTables) {
            Invoke-Dv -Path 'PublishXml' -Method POST -Body @{
                ParameterXml = "<importexportxml><entities><entity>$PublishTable</entity></entities></importexportxml>"
            } | Out-Null
            Write-Host "  published $PublishTable" -ForegroundColor Green
        }
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
