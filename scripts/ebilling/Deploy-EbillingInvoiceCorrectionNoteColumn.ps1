<#
.SYNOPSIS
    eBilling R1 task 095f review (EB-D-225) - sprk_invoice.sprk_correctionnote, the "Correction Note" a corrected invoice carries.

.DESCRIPTION
    Idempotent, additive Dataverse Web API metadata script. The item is check-then-create; a second run reports it as skipped and
    makes no write (no publish either).

    Item:
      1  sprk_invoice.sprk_correctionnote - Memo (multiple lines of text), maximum length 4000, optional, display name "Correction Note".
         The one place a reader finds why an invoice changed (EB-D-225). Two writers share it: an Outlook (from-email) Correction, which
         saves the note on the NEW invoice in the persister's create, and the Invoice Review adjust and decision steps. The BFF limits a
         note to 2000 characters (InvoiceCorrectionNote.MaxLength = InvoiceLineDecisionService.ReasonMaxLength); the column is longer so a
         limit raised later needs no schema change.

    Before this script existed the column was created by hand on dev (Memo, maximum length 4000) and is in no solution source. This script
    is the reproducible definition: it must run on every environment before a BFF build that writes the column is deployed there, or a
    from-email Correction fails closed in the invoice create. A New invoice and every other channel never name the column.

    An existing column that is not a Memo column, or has a maximum length below 2000 (the BFF's note limit), STOPS the script with exit code
    2 and an ESCALATION message (nothing is altered or renamed). Publish is entity-scoped (PublishXml for sprk_invoice only), and only when
    this run wrote metadata.

    This script is NOT run by the task that adds it; the owner's OK is needed to run it. -WhatIf is read-only and may be run against dev at
    any time.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the created component is added to (header MSCRM.SolutionUniqueName).

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingInvoiceCorrectionNoteColumn.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingInvoiceCorrectionNoteColumn.ps1

.NOTES
    Project: ebilling-r1, task 095f (EB-D-225). Pattern: scripts/ebilling/Deploy-EbillingApExportEmailColumn.ps1 (one column, check-then-create,
    escalation on a conflict, entity-scoped publish only on a write). Auth: the operator's own az CLI identity. No secrets.
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
$Table = 'sprk_invoice'
$Column = 'sprk_correctionnote'
$MaxLength = 4000        # the column's length; the BFF's note limit (2000) must not exceed it
$BffNoteLimit = 2000     # InvoiceCorrectionNote.MaxLength

$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$DryRun -Entity $Table
$script:Created = $State.Created
$script:Changed = $State.Changed
$script:Skipped = $State.Skipped

$Description = 'Why this invoice was corrected or changed (EB-D-225). Written once by the server: from an Outlook Correction (the note the submitter typed) or by an Invoice Review adjustment or decision. Blank on an invoice nobody corrected.'

Write-Host ""
Write-Host "eBilling task 095f - $Table.$Column (EB-D-225)" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Solution: $SolutionUniqueName   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })"
Write-Host ""

try {
    if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=LogicalName" -Retries 4)) { throw "$Table does not exist in $EnvironmentUrl." }

    Write-Host "1. $Table.$Column (Memo, max length $MaxLength, optional)" -ForegroundColor Cyan
    Add-Column $Column (New-MemoDef 'sprk_CorrectionNote' 'Correction Note' $Description $MaxLength) 'Memo'

    # Add-Column checks the type only. An existing Memo shorter than the BFF's note limit would cut a note off, so it is not this column.
    $a = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.MemoAttributeMetadata?`$select=MaxLength" -Retries 4
    if ($a -and [int]$a.MaxLength -lt $BffNoteLimit) {
        throw [EscalationException]::new("$Table.$Column exists with max length $($a.MaxLength), but a note can be $BffNoteLimit characters. Owner decision needed (this script does not shorten or alter an existing column).")
    }

    Write-Host "Publish" -ForegroundColor Cyan
    $wrote = ($script:Created.Count + $script:Changed.Count) -gt 0
    if ($DryRun) {
        Write-Host "  WhatIf: publish skipped" -ForegroundColor Cyan
    } elseif (-not $wrote) {
        Write-Host "  nothing changed - no publish" -ForegroundColor Yellow
    } else {
        # Entity-scoped (never the tenant-wide publish).
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
    Write-Host "The column, if created, stays (the script is re-runnable)." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "================ Summary ================" -ForegroundColor Cyan
Write-Host ("created : {0}  {1}" -f $script:Created.Count, ($script:Created -join ', '))
Write-Host ("changed : {0}  {1}" -f $script:Changed.Count, ($script:Changed -join ', '))
Write-Host ("skipped : {0}" -f $script:Skipped.Count)
if ($DryRun) { Write-Host "WhatIf run: nothing was written." -ForegroundColor Cyan }
exit 0
