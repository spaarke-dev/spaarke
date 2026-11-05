<#
.SYNOPSIS
    eBilling R1 082c-2 (owner decision 2026-11-04 "keep In Review, with a description"; ontology D-205; uac-r2 2026-11-04 conditions) -
    sprk_invoice.sprk_reviewdetail, the partner-readable review detail. -WhatIf is a dry run, -Apply writes, -Verify checks.

.DESCRIPTION
    WHY. A held invoice (a Procedure gap, billing terms that could not be checked, or a missing firm) stays statuscode In Review by the
    owner's decision, so the partner's grid and record say "In Review" with no explanation. This column names which kind of review it is.
    It is a label, not a hold: approval reads the holds from the stored submission state (InvoiceProcedureHold, InvoiceFirmHold), so they
    hold on an environment where this column does not exist yet.

    THE COLUMN. sprk_invoice.sprk_reviewdetail ("Review Detail"), a LOCAL choice (uac-r2 (a): not global), optional, NOT field-secured (the
    partner reads it through ExternalAccessModule's ReadableColumns), closed options with publisher-range values pinned in code
    (InvoiceReviewDetail; pinned against this script by InvoiceReviewDetailScriptAgreementTests):
        659490000  Waiting for billing terms            (ebilling.submission.procedure_gap)
        659490001  Billing terms could not be checked   (ebilling.submission.procedures_unresolved)
        659490002  Waiting for the firm to be confirmed (ebilling.submission.firm_missing)
    Blank = a normal review. An option value is never reused for another meaning.

    THE VALUE IS DERIVED (ontology D-205): InvoiceReviewDetail.ReviewDetailOf(open hold reasons), priority firm > could not be checked >
    waiting for terms. Written only by the BFF's named writers (InvoiceReviewDetailWritersGuardTests); refused to field-mapping rules
    (RecordTableRules MappingProtectedColumns).

    INVARIANT (uac-r2 (b)): non-blank only while statuscode is In Review (1). Every exit from In Review writes blank in the same write as
    the status. -Verify counts the invoices that break it.

    NO BACKFILL. The BFF writes the column from the build that carries 082c-2 on. Invoices held before the column exists read blank (a
    normal review) until their next writer; their holds are unaffected.

    DEPLOY ORDER. Either order is safe: a BFF build without the column leaves it out (ReviewDetailColumnProbe, a warning logged once), and the
    SPA treats a missing value as blank. Run -Apply (with the owner's go) and check that -Verify exits 0. A stamp provisioned from
    SpaarkeMaster gets the column from the solution source once it is exported there.

    CONFLICTS (exit 2, nothing changed): the column exists with another type, as a global choice, or with options other than exactly the
    three above (by value and label). This script never alters an existing column.

    -VERIFY (read only; exit 0 = pass, 1 = a gap, each one named): the column exists; is a local choice; has exactly the three options; is not
    field-secured; and no invoice with a non-blank review detail has a statuscode other than In Review. It also counts the REVERSE
    invariant as a WARNING (never a failure, exit code unchanged): In Review invoices with an open Procedure or firm hold and a BLANK review
    detail. The holds are read as the BFF reads them (InvoiceFirmHold: the invoice's own sprk_firmhold, and the stored submission outcome
    reason and findings; InvoiceProcedureHold: the same stored state). It is a warning because an invoice held before the column existed
    reads blank until its next writer (no backfill); after the build that carries 082c-2 is deployed, a count that grows means a writer
    missed an invoice.

    Publish is entity-scoped (PublishXml for sprk_invoice only, never the tenant-wide publish), and only when this run wrote metadata.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the column is added to (header MSCRM.SolutionUniqueName). Default SpaarkeMaster.

.PARAMETER Apply
    Write: create the column and publish sprk_invoice. Without -Apply, -Verify or -WhatIf the script refuses to run.

.PARAMETER Verify
    Read-only check with a pass / fail exit code. Cannot be combined with -WhatIf or -Apply.

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingInvoiceReviewDetailColumn.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingInvoiceReviewDetailColumn.ps1 -Apply
    pwsh scripts/ebilling/Deploy-EbillingInvoiceReviewDetailColumn.ps1 -Verify

.NOTES
    Project: ebilling-r1, task 082c-2. Pattern: Deploy-EbillingSubmissionPreviousSequenceColumn.ps1 (one column, escalation, -WhatIf / -Apply /
    -Verify, entity-scoped publish only on a write). The choice is built here, not with Add-LocalChoice, because that helper numbers options
    from 100000000 and these values are pinned in the publisher range. Auth: the operator's own az CLI identity. No secrets.
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

# ── Constants (pinned by InvoiceReviewDetailScriptAgreementTests against InvoiceReviewDetail) ────────────────────────────────
$Table = 'sprk_invoice'
$EntitySet = 'sprk_invoices'
$Column = 'sprk_reviewdetail'
$ColumnSchemaName = 'sprk_ReviewDetail'
$InReviewStatusCode = 1
$Options = [ordered]@{
    659490000 = 'Waiting for billing terms'
    659490001 = 'Billing terms could not be checked'
    659490002 = 'Waiting for the firm to be confirmed'
}
# The reverse invariant (-Verify, a warning): the stored state the BFF reads the holds from (InvoiceFirmHold, InvoiceProcedureHold).
$SubmissionSet = 'sprk_invoicesubmissions'
$FirmHold = 'sprk_firmhold'
$FirmMissingReason = 'ebilling.submission.firm_missing'
$ProcedureReasons = @('ebilling.submission.procedures_unresolved', 'ebilling.submission.procedure_gap')
$RulesNotChecked = 'rules.not_checked'
$ReadCap = 20000        # most In Review invoices with a blank detail read for the reverse invariant
$ChunkSize = 20         # invoice ids per submission read

$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$DryRun -Entity $Table
$script:Created = $State.Created
$script:Changed = $State.Changed
$script:Skipped = $State.Skipped

$Description = "Which kind of review an In Review invoice is in (blank = a normal review). Derived by the server from the invoice's open holds (082c-2); blank on every other status. Partner-readable; decides no access."

function New-ReviewDetailDef {
    $opts = @()
    foreach ($value in $Options.Keys) { $opts += @{ Value = [int]$value; Label = New-Label $Options[$value] } }
    return @{
        '@odata.type'    = 'Microsoft.Dynamics.CRM.PicklistAttributeMetadata'
        SchemaName       = $ColumnSchemaName
        DisplayName      = New-Label 'Review Detail'
        Description      = New-Label $Description
        RequiredLevel    = @{ Value = 'None' }
        DefaultFormValue = -1
        OptionSet        = @{
            '@odata.type' = 'Microsoft.Dynamics.CRM.OptionSetMetadata'
            IsGlobal      = $false
            OptionSetType = 'Picklist'
            Options       = $opts
        }
    }
}

# Every way the column differs from the specification above; empty when it is as specified. Read only. A column that does not exist is one gap.
function Get-ColumnGaps {
    $gaps = [System.Collections.Generic.List[string]]::new()
    $attr = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')?`$select=LogicalName,AttributeType,IsSecured" -Retries 2
    if (-not $attr) { $gaps.Add("$Table.$Column does not exist"); return , $gaps }
    if ($attr.AttributeType -ne 'Picklist') {
        $gaps.Add("$Table.$Column is '$($attr.AttributeType)', not a choice (Picklist)")
        return , $gaps
    }
    $pk = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=LogicalName&`$expand=OptionSet(`$select=Options,IsGlobal)" -Retries 4
    if (-not $pk) { $gaps.Add("$Table.$Column options could not be read") }
    else {
        if ($pk.OptionSet.IsGlobal -eq $true) { $gaps.Add("$Table.$Column uses a global choice; uac-r2 (a) requires a local one") }
        $have = @{}
        foreach ($o in @($pk.OptionSet.Options)) { $have[[int]$o.Value] = $o.Label.UserLocalizedLabel.Label }
        foreach ($value in $Options.Keys) {
            if (-not $have.ContainsKey([int]$value)) { $gaps.Add("$Table.$Column has no option $value ('$($Options[$value])')") }
            elseif ($have[[int]$value] -ne $Options[$value]) { $gaps.Add("$Table.$Column option $value is '$($have[[int]$value])', expected '$($Options[$value])'") }
        }
        foreach ($value in $have.Keys) {
            if (-not $Options.Contains($value)) { $gaps.Add("$Table.$Column has an extra option $value ('$($have[$value])'); the set is closed") }
        }
    }
    if ($attr.IsSecured -eq $true) {
        $gaps.Add("$Table.$Column is field-secured; the partner reads it (ExternalAccessModule ReadableColumns), so it must not be secured")
    }
    return , $gaps
}

# The invariant (uac-r2 (b)): invoices with a review detail that are not In Review. Read only; needs the column.
function Get-InvariantBreaks {
    $filter = "$Column ne null and statuscode ne $InReviewStatusCode"
    $rows = Invoke-Dv -Path "${EntitySet}?`$select=sprk_invoiceid,statuscode,$Column&`$filter=$filter&`$top=50"
    return @($rows.value)
}

# Rows of a read-only query, every page (follows @odata.nextLink), at most $ReadCap rows.
function Get-Pages([string]$Path) {
    $rows = [System.Collections.Generic.List[object]]::new()
    $next = $Path
    while ($next -and $rows.Count -lt $ReadCap) {
        $page = Invoke-Dv -Path $next
        foreach ($row in @($page.value)) { $rows.Add($row) }
        $next = $page.'@odata.nextLink'
    }
    return , $rows
}

# The hold reasons one stored submission names, as InvoiceProcedureHold.ReasonsOf and InvoiceFirmHold.IsHeld read them: the outcome reason code,
# and the findings' reasonCode (procedure reasons), pipelineFindings[].reasonCode and the rules.not_checked line findings' cause. Findings that
# cannot be parsed are held as 'procedures not checked' (fail closed, as the BFF).
function Get-SubmissionHoldReasons($Submission) {
    $reasons = [System.Collections.Generic.HashSet[string]]::new()
    $code = [string]$Submission.sprk_outcomereasoncode
    if ($code -eq $FirmMissingReason -or $ProcedureReasons -contains $code) { [void]$reasons.Add($code) }
    $json = [string]$Submission.sprk_findingsjson
    if ([string]::IsNullOrWhiteSpace($json)) { return $reasons }
    try {
        $root = $json | ConvertFrom-Json -ErrorAction Stop
        if ($root -isnot [pscustomobject]) { throw 'not an object' }
        if ($ProcedureReasons -contains [string]$root.reasonCode) { [void]$reasons.Add([string]$root.reasonCode) }
        foreach ($finding in @($root.pipelineFindings)) {
            if ($null -eq $finding) { continue }
            $c = [string]$finding.reasonCode
            if ($c -eq $FirmMissingReason -or $ProcedureReasons -contains $c) { [void]$reasons.Add($c) }
        }
        foreach ($line in @($root.lineFindings)) {
            if ($null -eq $line -or [string]$line.code -ne $RulesNotChecked) { continue }
            $cause = [string]$line.cause
            if ($cause -eq $FirmMissingReason -or $ProcedureReasons -contains $cause) { [void]$reasons.Add($cause) }
        }
    } catch {
        [void]$reasons.Add($ProcedureReasons[0])
    }
    return $reasons
}

# The reverse invariant (a warning, never a failure): In Review invoices with an open Procedure or firm hold and a BLANK review detail. Read only
# (GETs); needs the column. Invoices held before the column existed read blank until their next writer, so a non-zero count is not by itself a defect.
# Returns @{ Checked; Capped; Breaks } where Breaks is a list of "invoiceId (expected detail)".
function Get-HeldWithBlankDetail {
    $invoices = Get-Pages "${EntitySet}?`$select=sprk_invoiceid,$FirmHold&`$filter=statuscode eq $InReviewStatusCode and $Column eq null"
    $capped = $invoices.Count -ge $ReadCap
    $reasonsOf = @{}
    foreach ($invoice in $invoices) {
        $set = [System.Collections.Generic.HashSet[string]]::new()
        if (-not [string]::IsNullOrWhiteSpace([string]$invoice.$FirmHold)) { [void]$set.Add($FirmMissingReason) }
        $reasonsOf[[string]$invoice.sprk_invoiceid] = $set
    }
    $ids = @($reasonsOf.Keys)
    for ($i = 0; $i -lt $ids.Count; $i += $ChunkSize) {
        $chunk = $ids[$i..([Math]::Min($i + $ChunkSize, $ids.Count) - 1)]
        $or = ($chunk | ForEach-Object { "_sprk_invoice_value eq $_" }) -join ' or '
        foreach ($submission in (Get-Pages "${SubmissionSet}?`$select=_sprk_invoice_value,sprk_outcomereasoncode,sprk_findingsjson&`$filter=$or")) {
            $owner = [string]$submission._sprk_invoice_value
            if ($reasonsOf.ContainsKey($owner)) { $reasonsOf[$owner].UnionWith((Get-SubmissionHoldReasons $submission)) }
        }
    }
    $breaks = [System.Collections.Generic.List[string]]::new()
    foreach ($id in $ids) {
        $reasons = $reasonsOf[$id]
        if ($reasons.Count -eq 0) { continue }
        # The expected detail by the fixed priority (ontology D-205): the firm, then could not be checked, then waiting for terms.
        $expected = if ($reasons.Contains($FirmMissingReason)) { 659490002 } elseif ($reasons.Contains($ProcedureReasons[0])) { 659490001 } else { 659490000 }
        $breaks.Add("$id (expected $expected)")
    }
    return @{ Checked = $invoices.Count; Capped = $capped; Breaks = $breaks }
}

Write-Host ""
Write-Host "eBilling 082c-2 - $Table.$Column" -ForegroundColor Cyan
Write-Host ("Environment: {0}   Solution: {1}   Mode: {2}" -f $EnvironmentUrl, $SolutionUniqueName, $(if ($Verify) { 'VERIFY (read only)' } elseif ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' }))
Write-Host ""

if ($Verify) {
    try {
        if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=LogicalName" -Retries 4)) { throw "$Table does not exist in $EnvironmentUrl." }
        $gaps = Get-ColumnGaps
        $warnings = [System.Collections.Generic.List[string]]::new()
        if (-not ($gaps | Where-Object { $_ -like '*does not exist' -or $_ -like '*not a choice*' })) {
            $breaks = Get-InvariantBreaks
            if ($breaks.Count -gt 0) {
                $gaps.Add("$($breaks.Count) invoice(s) (first 50 read) carry a review detail but are not In Review (uac-r2 (b)): $(($breaks | ForEach-Object { "$($_.sprk_invoiceid) statuscode=$($_.statuscode)" }) -join '; ')")
            }

            # The reverse invariant, a warning only (see the header): a failed read of it is reported, never a pass or a fail by itself.
            try {
                $held = Get-HeldWithBlankDetail
                if ($held.Breaks.Count -gt 0) {
                    $shown = @($held.Breaks | Select-Object -First 20) -join '; '
                    $cap = if ($held.Capped) { ' (the read stopped at the cap; there may be more)' } else { '' }
                    $warnings.Add("$($held.Breaks.Count) of $($held.Checked) In Review invoice(s) with a blank review detail have an open Procedure or firm hold$cap. Expected for invoices held before this column existed (no backfill: they read blank until their next writer); a count that grows after the 082c-2 BFF build is deployed means a writer missed an invoice. First: $shown")
                }
            } catch {
                $warnings.Add("The reverse invariant (In Review invoices with an open hold and a blank review detail) could not be read: $($_.Exception.Message)")
            }
        }
    } catch {
        Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }
    foreach ($w in $warnings) { Write-Host "WARN: $w" -ForegroundColor Yellow }
    if ($gaps.Count -eq 0) {
        Write-Host "PASS: $Table.$Column exists as a local choice with the three pinned options, not field-secured; no non-In-Review invoice carries a review detail." -ForegroundColor Green
        exit 0
    }
    Write-Host "GAPS:" -ForegroundColor Red
    foreach ($g in $gaps) { Write-Host "  $g" -ForegroundColor Red }
    exit 1
}

try {
    if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=LogicalName" -Retries 4)) { throw "$Table does not exist in $EnvironmentUrl." }

    Write-Host "1. $Table.$Column (local choice, $($Options.Count) options $(@($Options.Keys) -join '/'), optional, not secured)" -ForegroundColor Cyan
    Add-Column $Column (New-ReviewDetailDef) 'Picklist'

    # Add-Column checks the type only. An existing choice with other options, a global one, or a secured one is not this column: never altered here.
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
