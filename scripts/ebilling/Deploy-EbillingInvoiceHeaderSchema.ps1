<#
.SYNOPSIS
    eBilling R1 task 012 - extends sprk_invoice with the FR-05 work-assignment lookup, FR-07 totals, the D-43
    supersedes lookup, FR-21 billing-period dates and the FR-06 lifecycle statuscode values.

.DESCRIPTION
    Idempotent, additive Dataverse Web API metadata script. Every item is check-then-create; a second run reports
    every item as skipped and makes no write (no publish either).

    Items (project constraint list, tasks/012-invoice-header-schema.poml):
      1  sprk_regardingworkassignment lookup -> sprk_workassignment (FR-05; the CoreAncestorLookups name). The typed
         sprk_matter / sprk_project lookups already on sprk_invoice are the Matter / Project lookups (no duplicate
         sprk_regardingmatter / sprk_regardingproject, no data migration). This script does NOT touch the resolver:
         CoreAncestorResolver.IntermediateRootColumns["sprk_invoice"] and its TypeScript mirror are task 013.
      2  Money sprk_feestotal, sprk_expensestotal, sprk_lineadjustmentstotal, sprk_invoiceadjustmentstotal,
         sprk_approvedtotal (FR-07; sprk_totalamount stays the billed total). Plain columns: no calculated / rollup /
         formula (ADR-002) - task 016's calculator writes them.
      3  sprk_supersedesinvoice lookup -> sprk_invoice (D-43; resubmission after Return to Firm).
      4  Date-only sprk_billingperiodstart / sprk_billingperiodend (FR-21 header).
      5  statuscode (FR-06, D-41): value 1 "Active" -> "In Review"; inserted Approved and Sent to AP (statecode 0, Active)
         and Paid, Rejected, Returned to Firm (statecode 1, Inactive); value 2 "Inactive" -> "Cancelled" ONLY when no
         invoice is at statecode 1 (counted first; otherwise the script STOPS with exit code 2 before any write).
         Inserted values: Approved 659490000, Sent to AP 659490001 (state 0); Paid 659490002, Rejected 659490003,
         Returned to Firm 659490004 (state 1) - the publisher option-value range, as for sprk_todo.

    NEVER deletes or renames anything by logical name (display labels of statuscode values 1 and 2 change). If an
    existing column has the same logical name but a different type (or a lookup targets a different table), or a
    status value exists under another state, the script STOPS with exit code 2 and an ESCALATION message
    (project CLAUDE.md section 5). The old invoice-status choice column is not touched (task 015 retired it; D-41).

    Eventually consistent metadata reads: a 404 right after a create is not trusted; a create refused with "already
    exists" counts as skipped (after a type check on a settled read). Customization / publish locks and transient
    faults are retried with backoff.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the created components are added to (header MSCRM.SolutionUniqueName).

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingInvoiceHeaderSchema.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingInvoiceHeaderSchema.ps1

.NOTES
    Project: ebilling-r1, task 012. Dev only in R1. Pattern: scripts/ebilling/Deploy-EbillingInvoiceLineSchema.ps1;
    statuscode calls follow scripts/Customize-SprkTodoStatuscode.ps1.
    Exit codes: 0 = done (created / skipped), 1 = error, 2 = escalation (destructive or conflicting).
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
$Entity = 'sprk_invoice'

# Shared Dataverse helpers (scripts/ebilling/EbillingSchema.psm1). The state lists are shared by reference.
$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$DryRun -Entity $Entity -UnqualifiedTallies
$script:Created = $State.Created
$script:Changed = $State.Changed
$script:Skipped = $State.Skipped
$script:Expect = $State.Expect
$script:Deferred = $State.Deferred
$SolutionHeader = $State.SolutionHeader


# ---------------------------------------------------------------------------------------------------------------
# statuscode helpers (FR-06)
# ---------------------------------------------------------------------------------------------------------------

function Get-StatusOptions {
    $a = Get-DvOrNull "EntityDefinitions(LogicalName='$Entity')/Attributes(LogicalName='statuscode')/Microsoft.Dynamics.CRM.StatusAttributeMetadata?`$expand=OptionSet(`$select=Options)" -Retries 4
    if (-not $a) { throw "$Entity.statuscode metadata could not be read." }
    return @($a.OptionSet.Options | ForEach-Object {
            [pscustomobject]@{ Value = [int]$_.Value; State = [int]$_.State; Label = $_.Label.UserLocalizedLabel.Label }
        })
}

function Get-InvoiceCountAtState([int]$State) {
    # Counts rows only (no row data); @odata.count is exact for the filter.
    $r = Invoke-Dv "sprk_invoices?`$select=sprk_invoiceid&`$filter=statecode eq $State&`$top=1&`$count=true"
    return [int]$r.'@odata.count'
}

# Lifecycle values (FR-06 / D-41). Existing values 1 and 2 are relabelled; the others are inserted.
$StatusRelabel = @(
    @{ Value = 1; State = 0; From = 'Active'; To = 'In Review' }
    @{ Value = 2; State = 1; From = 'Inactive'; To = 'Cancelled' }
)
$StatusInsert = @(
    @{ Value = 659490000; State = 0; Label = 'Approved' }
    @{ Value = 659490001; State = 0; Label = 'Sent to AP' }
    @{ Value = 659490002; State = 1; Label = 'Paid' }
    @{ Value = 659490003; State = 1; Label = 'Rejected' }
    @{ Value = 659490004; State = 1; Label = 'Returned to Firm' }
)

function Set-StatusLabels {
    $opts = Get-StatusOptions
    foreach ($r in $StatusRelabel) {
        $cur = $opts | Where-Object { $_.Value -eq $r.Value }
        $name = "statuscode.$($r.Value)"
        if (-not $cur) { throw [EscalationException]::new("$Entity statuscode value $($r.Value) is missing; expected the system value '$($r.From)' / '$($r.To)'. Owner decision needed.") }
        if ($cur.Label -eq $r.To) { $script:Skipped.Add("$name ($($r.To))"); Write-Host "  skipped  value $($r.Value) already '$($r.To)' (statecode $($cur.State))" -ForegroundColor Yellow; continue }
        if ($cur.Label -ne $r.From) { throw [EscalationException]::new("$Entity statuscode value $($r.Value) is labelled '$($cur.Label)', expected '$($r.From)' or '$($r.To)'. Owner decision needed.") }
        if ($DryRun) { $script:Changed.Add("$name ($($r.To))"); Write-Host "  WOULD RELABEL value $($r.Value) '$($r.From)' -> '$($r.To)'" -ForegroundColor Cyan; continue }
        Invoke-Dv -Path 'UpdateOptionValue' -Method POST -ExtraHeaders $SolutionHeader -Body @{
            EntityLogicalName = $Entity; AttributeLogicalName = 'statuscode'; Value = $r.Value
            Label = (New-Label $r.To); MergeLabels = $true; SolutionUniqueName = $SolutionUniqueName
        } | Out-Null
        $script:Changed.Add("$name ($($r.To))"); Write-Host "  RELABELLED value $($r.Value) '$($r.From)' -> '$($r.To)'" -ForegroundColor Green
    }
}

function Add-StatusValues {
    $opts = Get-StatusOptions
    foreach ($s in $StatusInsert) {
        $name = "statuscode.$($s.Label)"
        $byLabel = $opts | Where-Object { $_.Label -eq $s.Label }
        $byValue = $opts | Where-Object { $_.Value -eq $s.Value }
        if ($byLabel) {
            if ($byLabel.State -ne $s.State) { throw [EscalationException]::new("$Entity statuscode '$($s.Label)' (value $($byLabel.Value)) exists under statecode $($byLabel.State) but FR-06 needs statecode $($s.State). A status cannot change state by script - owner decision needed.") }
            $script:Skipped.Add($name); Write-Host "  skipped  '$($s.Label)' = $($byLabel.Value) (statecode $($byLabel.State))" -ForegroundColor Yellow
            continue
        }
        if ($byValue) { throw [EscalationException]::new("$Entity statuscode value $($s.Value) is already used by '$($byValue.Label)'. Owner decision needed.") }
        if ($DryRun) { $script:Created.Add($name); Write-Host "  WOULD INSERT '$($s.Label)' = $($s.Value) (statecode $($s.State))" -ForegroundColor Cyan; continue }
        try {
            Invoke-Dv -Path 'InsertStatusValue' -Method POST -ExtraHeaders $SolutionHeader -Body @{
                AttributeLogicalName = 'statuscode'; EntityLogicalName = $Entity; Value = $s.Value; StateCode = $s.State
                Label = (New-Label $s.Label); SolutionUniqueName = $SolutionUniqueName
            } | Out-Null
        } catch {
            # A stale read hid an option that is already there: the refusal is authoritative, verify on a settled read.
            if (-not (Test-DuplicateRefusal $_.Exception.Message -Loose)) { throw }
            $found = $null
            for ($i = 0; $i -lt 12 -and -not $found; $i++) {
                $found = Get-StatusOptions | Where-Object { $_.Label -eq $s.Label -and $_.State -eq $s.State }
                if (-not $found) { Start-Sleep -Seconds 5 }
            }
            if (-not $found) { throw }
            $script:Skipped.Add($name); Write-Host "  skipped  '$($s.Label)' (exists, found via create refusal)" -ForegroundColor Yellow
            continue
        }
        $script:Created.Add($name); Write-Host "  INSERTED '$($s.Label)' = $($s.Value) (statecode $($s.State))" -ForegroundColor Green
    }
}

# ---------------------------------------------------------------------------------------------------------------
# Run
# ---------------------------------------------------------------------------------------------------------------

Write-Host ""
Write-Host "eBilling task 012 - Invoice header schema on $Entity" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Solution: $SolutionUniqueName   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })"
Write-Host ""

try {
    if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$Entity')?`$select=LogicalName" -Retries 4)) {
        throw "$Entity does not exist in $EnvironmentUrl."
    }

    # --- 0. Pre-check (before ANY write): invoices at statecode 1 block relabelling value 2 ---------------------
    Write-Host "0. Pre-check: invoices at statecode 1 (Inactive)" -ForegroundColor Cyan
    $opts0 = Get-StatusOptions
    $v2 = $opts0 | Where-Object { $_.Value -eq 2 }
    $inactive = Get-InvoiceCountAtState 1
    Write-Host "  invoices at statecode 0: $(Get-InvoiceCountAtState 0)   at statecode 1: $inactive   (statuscode options: $(($opts0 | ForEach-Object { "$($_.Value)=$($_.Label)/s$($_.State)" }) -join ', '))"
    if ($v2 -and $v2.Label -eq 'Inactive' -and $inactive -gt 0) {
        throw [EscalationException]::new("$inactive invoice(s) are at statecode 1 (Inactive). Relabelling statuscode 2 'Inactive' to 'Cancelled' would silently re-label them. Options: (a) relabel value 2 to Cancelled anyway and accept that the $inactive existing inactive invoice(s) read as Cancelled; (b) keep value 2 as Inactive and add Cancelled as a NEW statecode-1 value. Owner decision needed; nothing was changed.")
    }

    # --- 1. FR-05 work-assignment lookup ------------------------------------------------------------------------
    Write-Host "1. FR-05 core record: work assignment lookup" -ForegroundColor Cyan
    Add-Lookup -Column 'sprk_regardingworkassignment' -SchemaName 'sprk_RegardingWorkAssignment' -Target 'sprk_workassignment' `
        -Display 'Regarding Work Assignment' -Description 'Core record when the invoice is filed under a work assignment (typed sprk_matter / sprk_project cover Matter and Project).'

    # --- 2. FR-07 totals -----------------------------------------------------------------------------------------
    Write-Host "2. FR-07 totals (billed total stays sprk_totalamount)" -ForegroundColor Cyan
    Add-Column 'sprk_feestotal' (New-MoneyDef 'sprk_FeesTotal' 'Fees Total' 'Sum of fee lines as billed. Written server-side only (FR-07).') 'Money'
    Add-Column 'sprk_expensestotal' (New-MoneyDef 'sprk_ExpensesTotal' 'Expenses Total' 'Sum of expense lines as billed. Written server-side only (FR-07).') 'Money'
    Add-Column 'sprk_lineadjustmentstotal' (New-MoneyDef 'sprk_LineAdjustmentsTotal' 'Line Adjustments Total' 'Total reduced or disallowed on lines (billed minus approved). Written server-side only (FR-07).') 'Money'
    Add-Column 'sprk_invoiceadjustmentstotal' (New-MoneyDef 'sprk_InvoiceAdjustmentsTotal' 'Invoice Adjustments Total' 'Total of invoice-level Adjustment lines (percent of fees after line adjustments, or fixed). Written server-side only (FR-07).') 'Money'
    Add-Column 'sprk_approvedtotal' (New-MoneyDef 'sprk_ApprovedTotal' 'Approved Total' 'Billed total minus line and invoice adjustments (D-42). Written server-side only (FR-07).') 'Money'

    # --- 3. D-43 supersedes --------------------------------------------------------------------------------------
    Write-Host "3. D-43 supersedes lookup (self)" -ForegroundColor Cyan
    Add-Lookup -Column 'sprk_supersedesinvoice' -SchemaName 'sprk_SupersedesInvoice' -Target 'sprk_invoice' `
        -Display 'Supersedes Invoice' -Description 'The invoice returned to the firm that this resubmission supersedes (D-43).'

    # --- 4. FR-21 billing period ---------------------------------------------------------------------------------
    Write-Host "4. Billing period (date only)" -ForegroundColor Cyan
    Add-Column 'sprk_billingperiodstart' (New-DateOnlyDef 'sprk_BillingPeriodStart' 'Billing Period Start' 'First day of the billing period covered by the invoice.') 'DateTime'
    Add-Column 'sprk_billingperiodend' (New-DateOnlyDef 'sprk_BillingPeriodEnd' 'Billing Period End' 'Last day of the billing period covered by the invoice.') 'DateTime'

    # --- 5. FR-06 lifecycle statuscode ---------------------------------------------------------------------------
    Write-Host "5. FR-06 lifecycle statuscode: relabel values 1 and 2" -ForegroundColor Cyan
    Set-StatusLabels
    Write-Host "   insert lifecycle values" -ForegroundColor Cyan
    Add-StatusValues

    # --- Report the settled statuscode list (and flag anything unexpected) ----------------------------------------
    if (-not $DryRun) {
        Write-Host "   statuscode options now:" -ForegroundColor Cyan
        $expected = @('In Review', 'Cancelled') + ($StatusInsert | ForEach-Object { $_.Label })
        # A read right after InsertStatusValue can return the old list (eventual consistency): wait for all seven.
        for ($i = 0; $i -lt 12; $i++) {
            $final = Get-StatusOptions | Sort-Object State, Value
            if (@($expected | Where-Object { $_ -notin $final.Label }).Count -eq 0) { break }
            Start-Sleep -Seconds 5
        }
        $final | ForEach-Object { Write-Host ("    {0,-10} statecode {1}  {2}" -f $_.Value, $_.State, $_.Label) }
        $missing = @($expected | Where-Object { $_ -notin $final.Label })
        if ($missing.Count -gt 0) { throw "statuscode values not visible after 60 s: $($missing -join ', ')" }
        $extra = @($final | Where-Object { $_.Label -notin $expected })
        if ($extra.Count -gt 0) { Write-Host "  WARNING: statuscode values outside FR-06: $(($extra | ForEach-Object { "$($_.Value)=$($_.Label)" }) -join ', ')" -ForegroundColor DarkYellow }
    }

    # --- Publish (only when something changed) ------------------------------------------------------------------
    Write-Host "Publish" -ForegroundColor Cyan
    $wrote = ($script:Created.Count + $script:Changed.Count) -gt 0
    if ($DryRun) {
        Write-Host "  WhatIf: publish skipped" -ForegroundColor Cyan
    } elseif (-not $wrote) {
        Write-Host "  nothing changed - no publish" -ForegroundColor Yellow
    } else {
        Invoke-Dv -Path 'PublishXml' -Method POST -Body @{
            ParameterXml = "<importexportxml><entities><entity>$Entity</entity></entities></importexportxml>"
        } | Out-Null
        Write-Host "  customizations published" -ForegroundColor Green
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
