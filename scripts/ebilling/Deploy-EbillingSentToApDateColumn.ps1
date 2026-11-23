<#
.SYNOPSIS
    eBilling R1 task 151c-1a (EB-D-182) - the "Sent to AP Date" column on sprk_billingexport.

.DESCRIPTION
    Idempotent, additive Dataverse Web API metadata script. The item is check-then-create; a second run reports it as skipped and
    makes no write (no publish either).

    Item:
      1  sprk_billingexport.sprk_senttoapdate - Date and Time (user local), optional. Set by the BFF when Email to AP first succeeds for
         the batch, or by the Mark Sent to AP action for files delivered another way (EB-D-182 (3)). Empty means the batch has not been
         sent to AP. It is set once and never overwritten; there is no per-invoice copy.

    An existing column that is not a Date and Time column, or is Date Only format, STOPS the script with exit code 2 and an ESCALATION
    message (nothing is altered or renamed). Publish is entity-scoped (PublishXml for sprk_billingexport), and only when this run wrote
    metadata.

    The column is not placed on a form here; the Billing Export main form's "Export batch" section is Deploy-EbillingFormsViews.ps1's
    (Spec), and the SpaarkeMaster source form carries it. This script is NOT run by the task that adds it; the owner's OK is needed to
    run it on dev. -WhatIf is read-only and may be run against dev at any time.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the created component is added to (header MSCRM.SolutionUniqueName).

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingSentToApDateColumn.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingSentToApDateColumn.ps1

.NOTES
    Project: ebilling-r1, task 151c-1a (EB-D-182). Dev only in R1. Pattern: scripts/ebilling/Deploy-EbillingInvoiceTrackingNumberSchema.ps1.
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
$Table = 'sprk_billingexport'
$Column = 'sprk_senttoapdate'

$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$DryRun -Entity $Table
$script:Created = $State.Created
$script:Changed = $State.Changed
$script:Skipped = $State.Skipped

$Description = 'When the batch was first sent to AP: by Email to AP, or by Mark Sent to AP for files delivered another way (EB-D-182). Empty until then; set once and never overwritten.'

Write-Host ""
Write-Host "eBilling task 151c-1a - $Table.$Column (EB-D-182)" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Solution: $SolutionUniqueName   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })"
Write-Host ""

try {
    if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=LogicalName" -Retries 4)) { throw "$Table does not exist in $EnvironmentUrl." }

    Write-Host "1. $Table.$Column" -ForegroundColor Cyan
    Add-Column $Column (New-DateTimeDef 'sprk_SentToApDate' 'Sent to AP Date' $Description) 'DateTime'

    # Add-Column checks the type only (Date Only and Date and Time are both 'DateTime'). A Date Only column would drop the time the
    # routes stamp, so it is a conflict.
    $a = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.DateTimeAttributeMetadata?`$select=Format,DateTimeBehavior" -Retries 4
    if ($a -and $a.Format -ne 'DateAndTime') {
        throw [EscalationException]::new("$Table.$Column exists with format '$($a.Format)', but this task needs DateAndTime. Owner decision needed (this script does not alter an existing column).")
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
