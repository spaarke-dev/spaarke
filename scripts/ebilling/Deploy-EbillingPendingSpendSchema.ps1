<#
.SYNOPSIS
    eBilling R1 task 049 (owner decision D-104) - the stored Pending Spend rollup column on sprk_matter and sprk_project.

.DESCRIPTION
    Idempotent, additive Dataverse Web API metadata script. Every item is check-then-create; a second run reports every
    item as skipped and makes no write (no publish either).

    Items:
      1  sprk_matter.sprk_pendingspend  - Money, precision 2, -922337203685477 .. 922337203685477 (the range of sprk_totalspendtodate: a sum, not one invoice).
      2  sprk_project.sprk_pendingspend - the same column on the project.

    Pending Spend is the sum of sprk_invoice.sprk_totalamount over the parent's In Review invoices that are NOT held
    (a blocked submission awaiting Override / Return is held; D-101, D-102). It is kept separate from actual spend
    (sprk_totalspendtodate = Approved, Sent to AP, Paid; D-103). A plain stored column: no calculated / rollup / formula
    (ADR-002). Its ONLY writer is FinanceRollupService, which takes the status set and the held exclusion from
    FinanceBaseValues (the one definition, also used by the live measure).

    The column is stored but cannot be used in a Threshold rule until ontology-r1 adds it to
    PolicyVersionValidator.AuthorableColumns, the PredicateCompiler ranges and the RuleBodyDescriber labels (follow-up of
    their own); that is safe.

    NEVER deletes or renames anything. A same-name column of another type STOPS the script with exit code 2 and an
    ESCALATION message. Publish is entity-scoped (PublishXml for the two tables), and only when this run wrote metadata.
    This script is NOT run by the task that adds it; task 053 syncs SpaarkeMaster and task 079 runs it.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the created components are added to (header MSCRM.SolutionUniqueName).

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingPendingSpendSchema.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingPendingSpendSchema.ps1

.NOTES
    Project: ebilling-r1, task 049 (D-104). Dev only in R1. Pattern: scripts/ebilling/Deploy-EbillingSignalLineSubjectSchema.ps1.
    Exit codes: 0 = done (created / skipped), 1 = error, 2 = escalation (conflicting).
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

$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$DryRun -Entity 'sprk_matter'
$script:Created = $State.Created
$script:Changed = $State.Changed
$script:Skipped = $State.Skipped

$Parents = 'sprk_matter', 'sprk_project'
$Description = 'Pending Spend: the sum of this record''s In Review invoices that are not held (a blocked submission awaiting Override / Return is held). Kept apart from Total Spend to Date, which counts Approved, Sent to AP and Paid. Written server-side only, by the finance rollup (D-104).'

Write-Host ""
Write-Host "eBilling task 049 - Pending Spend rollup column (D-104)" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Solution: $SolutionUniqueName   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })"
Write-Host ""

try {
    foreach ($t in $Parents) {
        if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$t')?`$select=LogicalName" -Retries 4)) { throw "$t does not exist in $EnvironmentUrl." }
    }

    $n = 1
    foreach ($t in $Parents) {
        Write-Host "$n. $t.sprk_pendingspend" -ForegroundColor Cyan
        Set-EbEntity $t
        # A sum of invoices, so the range of sprk_totalspendtodate, not New-MoneyDef's per-invoice 0 .. 1e9 (ontology-r1 review F4).
        $def = New-MoneyDef 'sprk_PendingSpend' 'Pending Spend' $Description
        $def.MinValue = -922337203685477
        $def.MaxValue = 922337203685477
        Add-Column 'sprk_pendingspend' $def 'Money'
        $n++
    }

    Write-Host "Publish" -ForegroundColor Cyan
    $wrote = ($script:Created.Count + $script:Changed.Count) -gt 0
    if ($DryRun) {
        Write-Host "  WhatIf: publish skipped" -ForegroundColor Cyan
    } elseif (-not $wrote) {
        Write-Host "  nothing changed - no publish" -ForegroundColor Yellow
    } else {
        Invoke-Dv -Path 'PublishXml' -Method POST -Body @{
            ParameterXml = '<importexportxml><entities><entity>sprk_matter</entity><entity>sprk_project</entity></entities></importexportxml>'
        } | Out-Null
        Write-Host "  published sprk_matter, sprk_project" -ForegroundColor Green
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
    Write-Host "Items already created stay (the script is re-runnable)." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "================ Summary ================" -ForegroundColor Cyan
Write-Host ("created : {0}  {1}" -f $script:Created.Count, ($script:Created -join ', '))
Write-Host ("changed : {0}  {1}" -f $script:Changed.Count, ($script:Changed -join ', '))
Write-Host ("skipped : {0}" -f $script:Skipped.Count)
if ($DryRun) { Write-Host "WhatIf run: nothing was written." -ForegroundColor Cyan }
exit 0
