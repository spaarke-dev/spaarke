<#
.SYNOPSIS
    eBilling R1 task 095d-1a, K4 (owner decision 2026-11-04, "new column, never reuse") - sprk_invoicesubmission.sprk_previoussequence, the
    sequence a submission gave up in the 'none' tracking scope when a renumber moved it into a firm's scope. -WhatIf is a dry run, -Apply
    writes, -Verify checks.

.DESCRIPTION
    WHY. Task 095d's assign-firm renumbers a no-firm submission (tracking number INV-7.1 in the 'none' scope) into the firm's own sequence and
    overwrites sprk_submissionsequence and sprk_trackingnumber. Nothing in the 'none' scope then remembers the n it gave up, so the next no-firm
    intake of INV-7 would be numbered INV-7.1 again: one number naming two submissions over time. The renumber therefore writes the old
    'none'-scope sequence to this column, in the same update that moves the row (TrackingNumberAllocator.RenumberSubmissionAsync), and the
    'none' scope's next-number read counts it: highest n = max(sequences of rows still in 'none', previous sequences of rows with that firm
    invoice number). A renumber only ever leaves 'none', so the column always refers to it.

    THE COLUMN. sprk_invoicesubmission.sprk_previoussequence, Whole number (Integer, NOT BigInt: Dataverse cannot field-secure BigInt, and the
    value is a copy of an Integer sequence), minimum 1, maximum 2147483647 (the range of sprk_submissionsequence, whose values it copies),
    optional, NOT field-secured. Empty on every row that has not been renumbered. Written only by the BFF's tracking number allocator; it is
    refused to the caller's columns of a create or a renumber (TrackingNumberAllocator.OwnedColumns), and decides no access.

    NO BACKFILL. No row has been renumbered before this column exists: the renumber is refused ('ebilling.tracking.schema_missing') until it
    does, so every row's value is correctly empty.

    DEPLOY ORDER (binding, notes/095c-from-email-firm-optional.md section 16.6). Run -Apply (with the owner's go) BEFORE any BFF build that
    carries 095d-1 is deployed, and check that -Verify exits 0. Until the column exists the BFF stays safe: the renumber is refused with
    'ebilling.tracking.schema_missing' (nothing is written; a 503), and the no-firm create keeps numbering from the 'none' scope alone, logging
    a warning once. A stamp provisioned from SpaarkeMaster gets the column from the solution source (Entities/sprk_invoicesubmission/Entity.xml)
    and needs no script.

    CONFLICTS (exit 2, nothing changed): the column exists with another type (BigInt, Decimal, text...), or as a whole number with a minimum
    above 1 or a maximum below 2147483647 (a smaller range could refuse a legitimate sequence). This script never alters an existing column.

    -VERIFY (read only; exit 0 = pass, 1 = a gap, each one named): the column exists, is a whole number (Integer), has minimum 1 and maximum
    2147483647, and is not field-secured (a masked read would read as "no previous sequence" and the number would be issued again).

    Publish is entity-scoped (PublishXml for sprk_invoicesubmission only, never the tenant-wide publish), and only when this run wrote
    metadata. A second -Apply reports the column as skipped and writes nothing (no publish either).

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the column is added to (header MSCRM.SolutionUniqueName). Default SpaarkeMaster.

.PARAMETER Apply
    Write: create the column and publish sprk_invoicesubmission. Without -Apply, -Verify or -WhatIf the script refuses to run.

.PARAMETER Verify
    Read-only check with a pass / fail exit code. Cannot be combined with -WhatIf or -Apply.

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingSubmissionPreviousSequenceColumn.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingSubmissionPreviousSequenceColumn.ps1 -Apply
    pwsh scripts/ebilling/Deploy-EbillingSubmissionPreviousSequenceColumn.ps1 -Verify

.NOTES
    Project: ebilling-r1, task 095d-1a. Pattern: scripts/ebilling/Deploy-EbillingSentToApDateColumn.ps1 (one column, escalation, entity-scoped
    publish only on a write) and Set-EbillingInvoiceFirmHoldColumn.ps1 (the -WhatIf / -Apply / -Verify modes); the whole-number definition is
    Deploy-EbillingSubmissionSchema.ps1's New-WholeNumberDef. Pinned against the allocator's column name and the SpaarkeMaster source by
    TrackingNumberPreviousSequenceColumnAgreementTests. Auth: the operator's own az CLI identity. No secrets.
    Exit codes: 0 = pass / done, 1 = a gap or an error, 2 = escalation (a conflicting column).
#>

using module .\EbillingSchema.psm1

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = $(if ($env:DATAVERSE_URL) { $env:DATAVERSE_URL } else { 'https://spaarkedev1.crm.dynamics.com' }),
    [string]$SolutionUniqueName = 'SpaarkeMaster',
    [switch]$Apply,
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$DryRun = [bool]$WhatIfPreference
if ($DryRun -and $Verify) { throw '-WhatIf and -Verify are separate modes.' }
if ($Apply -and $Verify) { throw '-Apply and -Verify are separate modes.' }
if (-not $Apply -and -not $Verify -and -not $DryRun) { throw 'Choose a mode: -WhatIf (dry run), -Apply (write) or -Verify (check).' }

# ── Constants (pinned by TrackingNumberPreviousSequenceColumnAgreementTests) ────────────────────────────────
$Table = 'sprk_invoicesubmission'
$Column = 'sprk_previoussequence'
$ColumnSchemaName = 'sprk_PreviousSequence'
$ColumnMin = 1
$ColumnMax = 2147483647

$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$DryRun -Entity $Table
$script:Created = $State.Created
$script:Changed = $State.Changed
$script:Skipped = $State.Skipped

$Description = "The sequence this submission held in the none tracking scope before a renumber moved it into a firm's scope (task 095d-1a). The none scope never issues it again. Written only by the server in the renumber; decides no access."

# A whole number column (IntegerAttributeMetadata; copied from Deploy-EbillingSubmissionSchema.ps1's New-WholeNumberDef).
function New-WholeNumberDef([string]$Schema, [string]$Display, [string]$Text, [int]$Min, [int]$Max) {
    return @{
        '@odata.type' = 'Microsoft.Dynamics.CRM.IntegerAttributeMetadata'
        SchemaName    = $Schema
        DisplayName   = New-Label $Display
        Description   = New-Label $Text
        RequiredLevel = @{ Value = 'None' }
        Format        = 'None'
        MinValue      = $Min
        MaxValue      = $Max
    }
}

# Every way the column differs from the specification above; empty when it is as specified. Read only. A column that does not exist is one gap.
function Get-ColumnGaps {
    $gaps = [System.Collections.Generic.List[string]]::new()
    $attr = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')?`$select=LogicalName,AttributeType,IsSecured" -Retries 2
    if (-not $attr) { $gaps.Add("$Table.$Column does not exist"); return , $gaps }
    if ($attr.AttributeType -ne 'Integer') {
        $gaps.Add("$Table.$Column is '$($attr.AttributeType)', not a whole number (Integer)")
        return , $gaps
    }
    $range = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.IntegerAttributeMetadata?`$select=MinValue,MaxValue" -Retries 4
    if (-not $range) { $gaps.Add("$Table.$Column range could not be read") }
    else {
        if ([int64]$range.MinValue -gt $ColumnMin) { $gaps.Add("$Table.$Column minimum is $($range.MinValue), expected at most $ColumnMin") }
        if ([int64]$range.MaxValue -lt $ColumnMax) { $gaps.Add("$Table.$Column maximum is $($range.MaxValue), expected at least $ColumnMax") }
    }
    if ($attr.IsSecured -eq $true) {
        $gaps.Add("$Table.$Column is field-secured; a masked read would read as 'no previous sequence' and the number would be issued again. It must not be secured")
    }
    return , $gaps
}

Write-Host ""
Write-Host "eBilling task 095d-1a (K4) - $Table.$Column" -ForegroundColor Cyan
Write-Host ("Environment: {0}   Solution: {1}   Mode: {2}" -f $EnvironmentUrl, $SolutionUniqueName, $(if ($Verify) { 'VERIFY (read only)' } elseif ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' }))
Write-Host ""

if ($Verify) {
    try {
        if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=LogicalName" -Retries 4)) { throw "$Table does not exist in $EnvironmentUrl." }
        $gaps = Get-ColumnGaps
    } catch {
        Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }
    if ($gaps.Count -eq 0) {
        Write-Host "PASS: $Table.$Column exists as a whole number ($ColumnMin to $ColumnMax), not field-secured." -ForegroundColor Green
        exit 0
    }
    Write-Host "GAPS:" -ForegroundColor Red
    foreach ($g in $gaps) { Write-Host "  $g" -ForegroundColor Red }
    exit 1
}

try {
    if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=LogicalName" -Retries 4)) { throw "$Table does not exist in $EnvironmentUrl." }

    Write-Host "1. $Table.$Column (whole number $ColumnMin to $ColumnMax, optional, not secured)" -ForegroundColor Cyan
    Add-Column $Column (New-WholeNumberDef $ColumnSchemaName 'Previous Sequence' $Description $ColumnMin $ColumnMax) 'Integer'

    # Add-Column checks the type only. An existing whole number with a narrower range, or one that is secured, is not this column: never altered
    # here. (A column this run created has the specified shape by construction, so only an existing one is checked.)
    if ($script:Skipped.Count -gt 0) {
        $gaps = Get-ColumnGaps
        if ($gaps.Count -gt 0) {
            throw [EscalationException]::new("$Table.$Column is not as specified: $($gaps -join '; '). Owner decision needed (this script does not alter an existing column).")
        }
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
