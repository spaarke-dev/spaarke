<#
.SYNOPSIS
    eBilling R1 task 049 (owner decision D-124) - the tracking-number column on sprk_invoice.

.DESCRIPTION
    Idempotent, additive Dataverse Web API metadata script. The item is check-then-create; a second run reports it as skipped and
    makes no write (no publish either).

    Item:
      1  sprk_invoice.sprk_trackingnumber - Text, 100, optional. The D-87 tracking number {firm invoice number}.{n}, copied from the
         submission's sprk_trackingnumber when 049 creates the invoice and never changed afterwards. The firm's own number stays in
         sprk_invoicenumber (Invoice Number).

    Length: 100, as the submission's column. The allocator caps the firm invoice number at 89 characters (TrackingNumberAllocator
    .MaxFirmInvoiceNumberLength) and adds a dot and an n of up to 10 digits, so the longest tracking number is 100 characters.

    Optional (no required level): invoices created before eBilling have no tracking number, and the platform cannot refuse a null on
    them. 049 always sets it on an invoice it creates. Not an alternate key: the (firm, tracking number) key lives on
    sprk_invoicesubmission (task 020) and an invoice belongs to exactly one submission.

    NEVER deletes or renames anything. A same-name column of another type STOPS the script with exit code 2 and an ESCALATION
    message. Publish is entity-scoped (PublishXml for sprk_invoice), and only when this run wrote metadata.
    This script is NOT run by the task that adds it. The owner's OK is needed to run it on dev; task 053 adds it to SpaarkeMaster.
    The column is not placed on a form here; the invoice form's eBilling section is Deploy-EbillingFormsViews.ps1's.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the created component is added to (header MSCRM.SolutionUniqueName).

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingInvoiceTrackingNumberSchema.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingInvoiceTrackingNumberSchema.ps1

.NOTES
    Project: ebilling-r1, task 049 part A (D-124). Dev only in R1. Pattern: scripts/ebilling/Deploy-EbillingPendingSpendSchema.ps1.
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

$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$DryRun -Entity $Table
$script:Created = $State.Created
$script:Changed = $State.Changed
$script:Skipped = $State.Skipped

$Description = 'Tracking number {firm invoice number}.{n}, for example INV-002.2 (D-87), copied from the submission when the invoice is created and never changed. The firm''s own number stays in Invoice Number (D-124).'

Write-Host ""
Write-Host "eBilling task 049 - $Table.sprk_trackingnumber (D-124)" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Solution: $SolutionUniqueName   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })"
Write-Host ""

try {
    if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=LogicalName" -Retries 4)) { throw "$Table does not exist in $EnvironmentUrl." }

    Write-Host "1. $Table.sprk_trackingnumber" -ForegroundColor Cyan
    Add-Column 'sprk_trackingnumber' (New-StringDef 'sprk_TrackingNumber' 'Tracking Number' $Description 100) 'String'

    # An existing column must also have the right length: a shorter one would fault the 049 create on a long tracking number.
    $a = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='sprk_trackingnumber')/Microsoft.Dynamics.CRM.StringAttributeMetadata?`$select=MaxLength" -Retries 4
    if ($a -and [int]$a.MaxLength -lt 100) {
        throw [EscalationException]::new("$Table.sprk_trackingnumber exists with max length $($a.MaxLength), but a tracking number can be 100 characters. Owner decision needed (this script does not shorten or alter an existing column).")
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
