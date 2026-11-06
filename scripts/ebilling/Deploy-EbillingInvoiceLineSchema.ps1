<#
.SYNOPSIS
    eBilling R1 task 010 - turns sprk_billingevent into the Invoice Line (D-18, FR-04) and adds the FR-05 core-record fields.

.DESCRIPTION
    Idempotent, additive Dataverse Web API metadata script. Every item is check-then-create; a second run reports
    every item as skipped and makes no write (no publish either).

    Items (project constraint list, tasks/010-invoice-line-columns.poml):
      1  Entity display names "Invoice Line" / "Invoice Lines" (label change only).
      2  Option "Adjustment" (100000002) added to the global choice sprk_costtype (Fee 100000000, Expense 100000001).
      3  sprk_resolvedtimekeeper lookup -> sprk_timekeeper; existing TEXT column sprk_timekeeper relabelled
         "Timekeeper (as billed)" (display name only; logical name unchanged).
      4  sprk_approvedrate, sprk_approvedamount (money), sprk_adjustmentreason (multiline text),
         sprk_reviewedby (lookup -> systemuser), sprk_reviewedon (date-time).
      5  sprk_linestatus local choice: Pending, Clean, Flagged, Accepted, Reduced, Disallowed.
      6  sprk_taskcode / sprk_activitycode / sprk_expensecode lookups -> sprk_billingcode (DEFERRED while
         sprk_billingcode does not exist; task 018 creates it), text sprk_taskcodeasbilled /
         sprk_activitycodeasbilled / sprk_expensecodeasbilled, decimal sprk_codingconfidence.
      7  sprk_extractionconfidence (multiline text, JSON).
      8  sprk_adjustmentmethod local choice Percent / Fixed, decimal sprk_adjustmentvalue.
      9  sprk_adjustmentrule lookup -> sprk_policyversion.
     10  FR-05: sprk_regardingworkassignment lookup -> sprk_workassignment and the five ADR-024 resolver fields
         (sprk_regardingrecordtype lookup -> sprk_recordtype_ref, sprk_regardingrecordid, sprk_regardingrecordname,
         sprk_regardingrecordurl, sprk_regardingrecordnumber). Types and max lengths are read from sprk_invoice
         at run time (documented fallbacks used if sprk_invoice does not carry them yet).

    The typed sprk_matter / sprk_project lookups already on sprk_billingevent are the line's Matter and Project
    lookups (no sprk_regardingmatter / sprk_regardingproject here). Derivation and the one-lookup guard are task 013.

    NEVER deletes or renames anything. If an existing column has the same logical name but a different type (or a
    lookup targets a different table, or an existing choice has different options), the script STOPS with exit code 2
    and an ESCALATION message (project CLAUDE.md section 5).

    ADR-002: no plugin, business rule, calculated / rollup column or flow. Values are written server-side by later tasks.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the created components are added to (header MSCRM.SolutionUniqueName). SpaarkeMaster already
    contains sprk_billingevent with all subcomponents; passing it keeps new relationships / columns in the solution
    that task 029 exports.

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingInvoiceLineSchema.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingInvoiceLineSchema.ps1

.NOTES
    Project: ebilling-r1, task 010. Dev only in R1. Exemplar: scripts/Deploy-ChartDefinitionEntity.ps1.
    Exit codes: 0 = done (created / skipped / deferred), 1 = error, 2 = escalation (destructive or conflicting).
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
$Entity = 'sprk_billingevent'

# Shared Dataverse helpers (scripts/ebilling/EbillingSchema.psm1). The state lists are shared by reference.
$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$DryRun -Entity $Entity -UnqualifiedTallies
$script:Created = $State.Created
$script:Changed = $State.Changed
$script:Skipped = $State.Skipped
$script:Expect = $State.Expect
$script:Deferred = $State.Deferred

# ---------------------------------------------------------------------------------------------------------------
# Item helpers
# ---------------------------------------------------------------------------------------------------------------

function New-DecimalDef([string]$Schema, [string]$Display, [string]$Description, [int]$Precision, [double]$Min, [double]$Max) {
    return @{
        '@odata.type'  = 'Microsoft.Dynamics.CRM.DecimalAttributeMetadata'
        SchemaName     = $Schema
        DisplayName    = New-Label $Display
        Description    = New-Label $Description
        RequiredLevel  = @{ Value = 'None' }
        Precision      = $Precision
        MinValue       = $Min
        MaxValue       = $Max
    }
}

# ---------------------------------------------------------------------------------------------------------------
# Run
# ---------------------------------------------------------------------------------------------------------------

Write-Host ""
Write-Host "eBilling task 010 - Invoice Line schema on $Entity" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Solution: $SolutionUniqueName   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })"
Write-Host ""

try {
    if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$Entity')?`$select=LogicalName" -Retries 4)) {
        throw "$Entity does not exist in $EnvironmentUrl."
    }

    # --- 1. Entity display names ------------------------------------------------------------------------------
    Write-Host "1. Entity display names" -ForegroundColor Cyan
    $ent = Invoke-Dv "EntityDefinitions(LogicalName='$Entity')"
    $curName = $ent.DisplayName.UserLocalizedLabel.Label
    $curPlural = $ent.DisplayCollectionName.UserLocalizedLabel.Label
    if ($curName -eq 'Invoice Line' -and $curPlural -eq 'Invoice Lines') {
        $script:Skipped.Add('entity display names'); Write-Host "  skipped  display names already 'Invoice Line' / 'Invoice Lines'" -ForegroundColor Yellow
    } elseif ($DryRun) {
        $script:Changed.Add('entity display names'); Write-Host "  WOULD RELABEL '$curName' / '$curPlural' -> 'Invoice Line' / 'Invoice Lines'" -ForegroundColor Cyan
    } else {
        $ent.DisplayName = New-Label 'Invoice Line'
        $ent.DisplayCollectionName = New-Label 'Invoice Lines'
        Invoke-Dv -Path "EntityDefinitions($($ent.MetadataId))" -Method PUT -Body $ent -ExtraHeaders @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null
        $script:Changed.Add('entity display names'); Write-Host "  RELABELLED '$curName' / '$curPlural' -> 'Invoice Line' / 'Invoice Lines'" -ForegroundColor Green
    }

    # --- 2. Line type: add Adjustment to sprk_costtype ---------------------------------------------------------
    Write-Host "2. sprk_costtype option 'Adjustment'" -ForegroundColor Cyan
    $cost = Get-DvOrNull "GlobalOptionSetDefinitions(Name='sprk_costtype')" -Retries 4
    if (-not $cost) { throw "Global choice sprk_costtype does not exist in $EnvironmentUrl." }
    $byLabel = $cost.Options | Where-Object { $_.Label.UserLocalizedLabel.Label -eq 'Adjustment' }
    $byValue = $cost.Options | Where-Object { $_.Value -eq 100000002 }
    if ($byLabel) {
        if ($byLabel.Value -ne 100000002) { throw [EscalationException]::new("sprk_costtype already has 'Adjustment' with value $($byLabel.Value), not 100000002. Owner decision needed.") }
        $script:Skipped.Add('sprk_costtype.Adjustment'); Write-Host "  skipped  option Adjustment = 100000002 exists" -ForegroundColor Yellow
    } elseif ($byValue) {
        throw [EscalationException]::new("sprk_costtype value 100000002 is already used by '$($byValue.Label.UserLocalizedLabel.Label)'. Owner decision needed.")
    } elseif ($DryRun) {
        $script:Created.Add('sprk_costtype.Adjustment'); Write-Host "  WOULD CREATE option Adjustment = 100000002" -ForegroundColor Cyan
    } else {
        Invoke-Dv -Path 'InsertOptionValue' -Method POST -Body @{
            OptionSetName = 'sprk_costtype'; Value = 100000002; Label = (New-Label 'Adjustment')
        } | Out-Null
        $script:Created.Add('sprk_costtype.Adjustment'); Write-Host "  CREATED  option Adjustment = 100000002" -ForegroundColor Green
    }

    # --- 3. Timekeeper: roster lookup + relabel of the existing text column ------------------------------------
    Write-Host "3. Timekeeper" -ForegroundColor Cyan
    Add-Lookup -Column 'sprk_resolvedtimekeeper' -SchemaName 'sprk_ResolvedTimekeeper' -Target 'sprk_timekeeper' `
        -Display 'Timekeeper' -Description 'Resolved roster timekeeper (sprk_timekeeper table); the billed name stays in Timekeeper (as billed).'
    $tk = Get-DvOrNull "EntityDefinitions(LogicalName='$Entity')/Attributes(LogicalName='sprk_timekeeper')/Microsoft.Dynamics.CRM.StringAttributeMetadata" -Retries 4
    if (-not $tk) { throw "Expected the existing text column $Entity.sprk_timekeeper was not found (or is not a string)." }
    if ($tk.DisplayName.UserLocalizedLabel.Label -eq 'Timekeeper (as billed)') {
        $script:Skipped.Add('sprk_timekeeper label'); Write-Host "  skipped  sprk_timekeeper already labelled 'Timekeeper (as billed)'" -ForegroundColor Yellow
    } elseif ($DryRun) {
        $script:Changed.Add('sprk_timekeeper label'); Write-Host "  WOULD RELABEL sprk_timekeeper '$($tk.DisplayName.UserLocalizedLabel.Label)' -> 'Timekeeper (as billed)'" -ForegroundColor Cyan
    } else {
        $tk.DisplayName = New-Label 'Timekeeper (as billed)'
        Invoke-Dv -Path "EntityDefinitions(LogicalName='$Entity')/Attributes(LogicalName='sprk_timekeeper')/Microsoft.Dynamics.CRM.StringAttributeMetadata" -Method PUT -Body $tk -ExtraHeaders @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null
        $script:Changed.Add('sprk_timekeeper label'); Write-Host "  RELABELLED sprk_timekeeper -> 'Timekeeper (as billed)'" -ForegroundColor Green
    }

    # --- 4. Review outcome columns ------------------------------------------------------------------------------
    Write-Host "4. Review outcome columns" -ForegroundColor Cyan
    Add-Column 'sprk_approvedrate' (New-MoneyDef 'sprk_ApprovedRate' 'Approved Rate' 'Hourly rate approved in review.') 'Money'
    Add-Column 'sprk_approvedamount' (New-MoneyDef 'sprk_ApprovedAmount' 'Approved Amount' 'Line amount approved in review.') 'Money'
    Add-Column 'sprk_adjustmentreason' (New-MemoDef 'sprk_AdjustmentReason' 'Adjustment Reason' 'Reason recorded when a line is reduced or disallowed.' 2000) 'Memo'
    Add-Lookup -Column 'sprk_reviewedby' -SchemaName 'sprk_ReviewedBy' -Target 'systemuser' -Display 'Reviewed By' -Description 'User who decided this line.'
    Add-Column 'sprk_reviewedon' (New-DateTimeDef 'sprk_ReviewedOn' 'Reviewed On' 'When this line was decided.') 'DateTime'

    # --- 5. Line status ------------------------------------------------------------------------------------------
    Write-Host "5. Line status" -ForegroundColor Cyan
    Add-LocalChoice -Column 'sprk_linestatus' -Schema 'sprk_LineStatus' -Display 'Line Status' `
        -Description 'Review state of the invoice line. Pending is shown as Processing in the review UI.' `
        -Labels @('Pending', 'Clean', 'Flagged', 'Accepted', 'Reduced', 'Disallowed')

    # --- 6. UTBMS coding -----------------------------------------------------------------------------------------
    Write-Host "6. UTBMS coding" -ForegroundColor Cyan
    $billingCodeExists = [bool](Get-DvOrNull "EntityDefinitions(LogicalName='sprk_billingcode')?`$select=LogicalName")
    foreach ($c in @(
            @{ Col = 'sprk_taskcode'; Schema = 'sprk_TaskCode'; Display = 'Task Code'; Desc = 'UTBMS task code (sprk_billingcode).' },
            @{ Col = 'sprk_activitycode'; Schema = 'sprk_ActivityCode'; Display = 'Activity Code'; Desc = 'UTBMS activity code (sprk_billingcode).' },
            @{ Col = 'sprk_expensecode'; Schema = 'sprk_ExpenseCode'; Display = 'Expense Code'; Desc = 'UTBMS expense code (sprk_billingcode).' })) {
        if ($billingCodeExists) {
            Add-Lookup -Column $c.Col -SchemaName $c.Schema -Target 'sprk_billingcode' -Display $c.Display -Description $c.Desc
        } else {
            $script:Deferred.Add($c.Col); Write-Host "  deferred: sprk_billingcode missing - $($c.Col) not created (task 018 creates the table; re-run afterwards)" -ForegroundColor DarkYellow
        }
    }
    Add-Column 'sprk_taskcodeasbilled' (New-StringDef 'sprk_TaskCodeAsBilled' 'Task Code (as billed)' 'Task code exactly as it appears on the invoice.' 100) 'String'
    Add-Column 'sprk_activitycodeasbilled' (New-StringDef 'sprk_ActivityCodeAsBilled' 'Activity Code (as billed)' 'Activity code exactly as it appears on the invoice.' 100) 'String'
    Add-Column 'sprk_expensecodeasbilled' (New-StringDef 'sprk_ExpenseCodeAsBilled' 'Expense Code (as billed)' 'Expense code exactly as it appears on the invoice.' 100) 'String'
    Add-Column 'sprk_codingconfidence' (New-DecimalDef 'sprk_CodingConfidence' 'Coding Confidence' 'Combined confidence (0 to 1) of the AI-assigned UTBMS codes.' 4 0 1) 'Decimal'

    # --- 7. Extraction confidence --------------------------------------------------------------------------------
    Write-Host "7. Extraction confidence" -ForegroundColor Cyan
    Add-Column 'sprk_extractionconfidence' (New-MemoDef 'sprk_ExtractionConfidence' 'Extraction Confidence' 'JSON: per-field confidence with source page and bounding box (FR-04 / FR-21).' 100000) 'Memo'

    # --- 8. Adjustment lines -------------------------------------------------------------------------------------
    Write-Host "8. Adjustment method and value" -ForegroundColor Cyan
    Add-LocalChoice -Column 'sprk_adjustmentmethod' -Schema 'sprk_AdjustmentMethod' -Display 'Adjustment Method' `
        -Description 'How an Adjustment line is applied: percent of fees or a fixed amount.' -Labels @('Percent', 'Fixed')
    Add-Column 'sprk_adjustmentvalue' (New-DecimalDef 'sprk_AdjustmentValue' 'Adjustment Value' 'Percent or fixed amount for an Adjustment line, per Adjustment Method.' 4 0 1000000000) 'Decimal'

    # --- 9. Adjustment rule --------------------------------------------------------------------------------------
    Write-Host "9. Adjustment rule" -ForegroundColor Cyan
    Add-Lookup -Column 'sprk_adjustmentrule' -SchemaName 'sprk_AdjustmentRule' -Target 'sprk_policyversion' `
        -Display 'Adjustment Rule' -Description 'Optional published policy version the adjustment was made under (FR-32).'

    # --- 10. FR-05 core-record fields ----------------------------------------------------------------------------
    Write-Host "10. FR-05 core record (work assignment lookup + ADR-024 resolver fields)" -ForegroundColor Cyan
    Add-Lookup -Column 'sprk_regardingworkassignment' -SchemaName 'sprk_RegardingWorkAssignment' -Target 'sprk_workassignment' `
        -Display 'Regarding Work Assignment' -Description 'Core record when the line is filed under a work assignment (typed sprk_matter / sprk_project cover Matter and Project).'
    # The five resolver fields mirror sprk_invoice (read live); fallbacks are the values read on 2026-10-15.
    $inv = @{}
    foreach ($n in 'sprk_regardingrecordid', 'sprk_regardingrecordname', 'sprk_regardingrecordnumber', 'sprk_regardingrecordurl') {
        $a = Get-DvOrNull "EntityDefinitions(LogicalName='sprk_invoice')/Attributes(LogicalName='$n')/Microsoft.Dynamics.CRM.StringAttributeMetadata?`$select=MaxLength,FormatName"
        if ($a) { $inv[$n] = $a }
    }
    function Get-InvLen([string]$n, [int]$fallback) { if ($inv[$n]) { [int]$inv[$n].MaxLength } else { $fallback } }
    Add-Lookup -Column 'sprk_regardingrecordtype' -SchemaName 'sprk_RegardingRecordType' -Target 'sprk_recordtype_ref' `
        -Display 'Regarding Record Type' -Description 'ADR-024 resolver field: the record type of the core record.'
    Add-Column 'sprk_regardingrecordid' (New-StringDef 'sprk_RegardingRecordId' 'Regarding Record Id' 'ADR-024 resolver field: id of the core record.' (Get-InvLen 'sprk_regardingrecordid' 100)) 'String'
    Add-Column 'sprk_regardingrecordname' (New-StringDef 'sprk_RegardingRecordName' 'Regarding Record Name' 'ADR-024 resolver field: name of the core record.' (Get-InvLen 'sprk_regardingrecordname' 100)) 'String'
    Add-Column 'sprk_regardingrecordurl' (New-StringDef 'sprk_RegardingRecordURL' 'Regarding Record URL' 'ADR-024 resolver field: link to the core record.' (Get-InvLen 'sprk_regardingrecordurl' 250) 'Url') 'String'
    Add-Column 'sprk_regardingrecordnumber' (New-StringDef 'sprk_RegardingRecordNumber' 'Regarding Record Number' 'ADR-024 resolver field: business number of the core record.' (Get-InvLen 'sprk_regardingrecordnumber' 1000)) 'String'

    # --- Publish (only when something changed) ------------------------------------------------------------------
    Write-Host "Publish" -ForegroundColor Cyan
    $wrote = ($script:Created.Count + $script:Changed.Count) -gt 0
    if ($DryRun) {
        Write-Host "  WhatIf: publish skipped" -ForegroundColor Cyan
    } elseif (-not $wrote) {
        Write-Host "  nothing changed - no publish" -ForegroundColor Yellow
    } else {
        Invoke-Dv -Path 'PublishXml' -Method POST -Body @{
            ParameterXml = "<importexportxml><entities><entity>$Entity</entity></entities><optionsets><optionset>sprk_costtype</optionset></optionsets></importexportxml>"
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
Write-Host ("deferred: {0}  {1}" -f $script:Deferred.Count, ($script:Deferred -join ', '))
if ($DryRun) { Write-Host "WhatIf run: nothing was written." -ForegroundColor Cyan }
exit 0
