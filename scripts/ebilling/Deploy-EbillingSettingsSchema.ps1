<#
.SYNOPSIS
    eBilling R1 task 025 - creates the audited singleton settings table sprk_ebillingsetting (FR-48, D-59) and seeds its one
    default row.

.DESCRIPTION
    Idempotent, additive Dataverse Web API metadata script. Every item is check-then-create; a second run reports every item
    as skipped, writes nothing and publishes nothing. It never relabels, renumbers, removes or retypes an existing item, and
    it seeds the default row ONLY when the table holds no row (an admin edit is never overwritten).

    Items (tasks/025-ebilling-settings.poml, spec FR-48 / D-47 / D-54 / D-56 / D-59 / D-69):
      0  Pre-checks (read only, before any write): the dev environment, organization-level auditing is ON, the global choice
         sprk_billingapproverrole holds the D-33 values (the seed uses Assigned Attorney 1 and Assigned To Internal).
      1  Table sprk_ebillingsetting (display "eBilling Settings"), organization-owned, primary name sprk_name (100), auditing
         on the table.
      2  Singleton: required local choice sprk_singleton with exactly one option "Default" (default value set) and the alternate
         key sprk_ebillingsetting_singleton on it. A second row carries the same key value, so the platform refuses it. No
         plugin, business rule or flow (ADR-002).
      3  Columns (every custom column has auditing on; ranges are column metadata, so out-of-range values are refused on save):
           multi-select on the global choice sprk_billingapproverrole: sprk_matterapproverroles, sprk_projectapproverroles,
             sprk_workassignmentapproverroles
           decimal (precision 2, 0 .. 1): sprk_reliabilitythreshold, sprk_weightanalyzer, sprk_weightarithmetic,
             sprk_weightreferencematch, sprk_weightselfreport, sprk_arithmeticfailcap, sprk_referencemissingcap
           whole number: sprk_relatedwindowdays (0 .. 90), sprk_codingchunksize (1 .. 200)
           multiline text (100000): sprk_apexportmapping (JSON, D-56 default)
           two options: sprk_firmnoticeaienabled
           lookup: sprk_defaultcodeset -> sprk_billingcodeset (printed SKIPPED when that table does not exist; re-run after 018)
      4  Publish (entity-scoped, only when something changed).
      5  Seed: when the table has no row, one default row (approver roles Assigned Attorney 1 + Assigned To Internal for all
         three core-record types, threshold 0.90, window 7, chunk size 25, firm-notice AI on, AP mapping = the D-56 column set,
         PROVISIONAL weights and caps per D-69, default code set = "UTBMS Litigation" when it exists).
      6  Verification: a metadata read of everything this script owns (type, range, auditing, key, default).

    PROVISIONAL VALUES (D-69). The confidence weights and caps below are first estimates, owner-accepted as provisional and
    validated against the calibration report (task 161), the evaluation (162) and at UAT (163):
      weights: analyzer 0.50, arithmetic 0.25, reference match 0.15, model self-report 0.10 (sum 1.00)
      caps (maximum combined confidence when that check fails): arithmetic check fails 0.50, no reference-data match 0.80
    Both caps sit below the 0.90 rejection threshold, so a field whose arithmetic fails or that has no reference match can
    never reach the reject gate on its own (FR-26, I-5). "Caps" is interpreted here as a ceiling on combined confidence;
    task 046 confirms or changes the interpretation.

    The seeded AP mapping is a first JSON shape for task 150 to read (it owns the parser and may extend it): version 1,
    "columns" (one CSV row / JSON invoice object per invoice: name, dotted source path, optional format) and "lineColumns"
    (JSON adds lines[]).

    Eventually consistent metadata reads: a 404 right after a create is not trusted (the item is re-read); a create refused as
    "already exists" counts as skipped after a type check on a settled read; customization / publish locks and transient faults
    are retried with backoff (all in EbillingSchema.psm1).

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Only the dev environment is accepted in R1 (project constraint).

.PARAMETER SolutionUniqueName
    Unmanaged solution the created components are added to (header MSCRM.SolutionUniqueName).

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingSettingsSchema.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingSettingsSchema.ps1

.NOTES
    Project: ebilling-r1, task 025. Dev only in R1. Helpers: EbillingSchema.psm1. Patterns: Deploy-EbillingApproverRoleSchema.ps1
    (024: multi-select on the global choice), Deploy-EbillingExportSchema.ps1 (023: table, whole number, memo),
    Deploy-EbillingPolicyStoreSchema.ps1 (two options), Deploy-AccessEventEntity.ps1 (IsAuditEnabled on table and columns).
    Exit codes: 0 = done (created / skipped / deferred), 1 = error, 2 = escalation (conflicting existing item or a refused guard).
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
$DevUrl = 'https://spaarkedev1.crm.dynamics.com'
$Table = 'sprk_ebillingsetting'
$ChoiceName = 'sprk_billingapproverrole'
$CodeSetTable = 'sprk_billingcodeset'
$SingletonValue = 100000000
$RoleAttorney1 = 100000000
$RoleAssignedToInternal = 100000004

# Provisional (D-69). See the .DESCRIPTION.
$Seed = [ordered]@{
    Threshold          = 0.90
    WeightAnalyzer     = 0.50
    WeightArithmetic   = 0.25
    WeightReferenceMatch = 0.15
    WeightSelfReport   = 0.10
    ArithmeticFailCap  = 0.50
    ReferenceMissingCap = 0.80
    WindowDays         = 7
    ChunkSize          = 25
}

# D-56 default AP export column set. One row per approved invoice; JSON adds lines[]. GL / cost centre / tax columns are
# reserved for the fast followers (the mapping is data, so adding them needs no code change).
$ApMappingDefault = [ordered]@{
    version     = 1
    columns     = @(
        [ordered]@{ name = 'BatchId'; source = 'batch.id' }
        [ordered]@{ name = 'SpaarkeInvoiceId'; source = 'invoice.id' }
        [ordered]@{ name = 'VendorName'; source = 'firm.name' }
        [ordered]@{ name = 'VendorApId'; source = 'firm.apVendorId' }
        [ordered]@{ name = 'VendorTaxId'; source = 'firm.taxId' }
        [ordered]@{ name = 'InvoiceNumber'; source = 'invoice.firmInvoiceNumber' }
        [ordered]@{ name = 'TrackingNumber'; source = 'invoice.trackingNumber' }
        [ordered]@{ name = 'InvoiceDate'; source = 'invoice.invoiceDate'; format = 'date' }
        [ordered]@{ name = 'DueDate'; source = 'invoice.dueDate'; format = 'date' }
        [ordered]@{ name = 'BillingPeriodStart'; source = 'invoice.billingPeriodStart'; format = 'date' }
        [ordered]@{ name = 'BillingPeriodEnd'; source = 'invoice.billingPeriodEnd'; format = 'date' }
        [ordered]@{ name = 'Currency'; source = 'invoice.currency' }
        [ordered]@{ name = 'BilledTotal'; source = 'invoice.billedTotal'; format = 'decimal' }
        [ordered]@{ name = 'LineAdjustmentsTotal'; source = 'invoice.lineAdjustmentsTotal'; format = 'decimal' }
        [ordered]@{ name = 'InvoiceAdjustmentsTotal'; source = 'invoice.invoiceAdjustmentsTotal'; format = 'decimal' }
        [ordered]@{ name = 'ApprovedTotal'; source = 'invoice.approvedTotal'; format = 'decimal' }
        [ordered]@{ name = 'CoreRecordType'; source = 'record.type' }
        [ordered]@{ name = 'CoreRecordNumber'; source = 'record.number' }
        [ordered]@{ name = 'CoreRecordName'; source = 'record.name' }
        [ordered]@{ name = 'LawDepartmentMatterNumber'; source = 'record.lawDepartmentMatterNumber' }
        [ordered]@{ name = 'FirmClientMatterNumber'; source = 'record.firmClientMatterNumber' }
        [ordered]@{ name = 'PONumber'; source = 'invoice.poNumber' }
        [ordered]@{ name = 'ApprovedBy'; source = 'approval.approvedBy' }
        [ordered]@{ name = 'ApprovedOn'; source = 'approval.approvedOn'; format = 'date' }
        [ordered]@{ name = 'DocumentUrl'; source = 'invoice.documentUrl' }
    )
    lineColumns = @(
        [ordered]@{ name = 'date'; source = 'line.date'; format = 'date' }
        [ordered]@{ name = 'type'; source = 'line.type' }
        [ordered]@{ name = 'timekeeper'; source = 'line.timekeeper' }
        [ordered]@{ name = 'hours'; source = 'line.hours'; format = 'decimal' }
        [ordered]@{ name = 'rate'; source = 'line.rate'; format = 'decimal' }
        [ordered]@{ name = 'billed'; source = 'line.billed'; format = 'decimal' }
        [ordered]@{ name = 'approved'; source = 'line.approved'; format = 'decimal' }
        [ordered]@{ name = 'taskCode'; source = 'line.taskCode' }
        [ordered]@{ name = 'activityCode'; source = 'line.activityCode' }
        [ordered]@{ name = 'expenseCode'; source = 'line.expenseCode' }
    )
}

# Shared Dataverse helpers (scripts/ebilling/EbillingSchema.psm1). The state lists are shared by reference.
$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$DryRun -Entity $Table
$script:Created = $State.Created
$script:Changed = $State.Changed
$script:Skipped = $State.Skipped
$script:Expect = $State.Expect
$script:Deferred = $State.Deferred

# Range columns the verification checks: name -> @{ Kind; Min; Max; Precision }
$script:Ranges = [ordered]@{}

# ---------------------------------------------------------------------------------------------------------------
# Script-specific column builders (every custom column is created with auditing on)
# ---------------------------------------------------------------------------------------------------------------

function Set-Audit([hashtable]$Def) { $Def.IsAuditEnabled = @{ Value = $true }; return $Def }

function New-DecimalDef([string]$Schema, [string]$Display, [string]$Description, [double]$Min, [double]$Max) {
    return Set-Audit @{
        '@odata.type'  = 'Microsoft.Dynamics.CRM.DecimalAttributeMetadata'
        SchemaName     = $Schema
        DisplayName    = New-Label $Display
        Description    = New-Label $Description
        RequiredLevel  = @{ Value = 'None' }
        Precision      = 2
        MinValue       = $Min
        MaxValue       = $Max
    }
}

function New-WholeNumberDef([string]$Schema, [string]$Display, [string]$Description, [int]$Min, [int]$Max) {
    return Set-Audit @{
        '@odata.type'  = 'Microsoft.Dynamics.CRM.IntegerAttributeMetadata'
        SchemaName     = $Schema
        DisplayName    = New-Label $Display
        Description    = New-Label $Description
        RequiredLevel  = @{ Value = 'None' }
        Format         = 'None'
        MinValue       = $Min
        MaxValue       = $Max
    }
}

function New-TwoOptionDef([string]$Schema, [string]$Display, [string]$Description, [bool]$Default) {
    return Set-Audit @{
        '@odata.type'  = 'Microsoft.Dynamics.CRM.BooleanAttributeMetadata'
        SchemaName     = $Schema
        DisplayName    = New-Label $Display
        Description    = New-Label $Description
        RequiredLevel  = @{ Value = 'None' }
        DefaultValue   = $Default
        OptionSet      = @{
            '@odata.type' = 'Microsoft.Dynamics.CRM.BooleanOptionSetMetadata'
            OptionSetType = 'Boolean'
            TrueOption    = @{ Value = 1; Label = (New-Label 'Yes') }
            FalseOption   = @{ Value = 0; Label = (New-Label 'No') }
        }
    }
}

# Multi-select bound to the GLOBAL choice (pattern: Deploy-EbillingApproverRoleSchema.ps1 New-RoleColumnDef). AttributeType reads
# back as 'Virtual'.
function New-ApproverRolesDef([string]$Schema, [string]$Display, [string]$Description, [string]$ChoiceId) {
    return Set-Audit @{
        '@odata.type'                = 'Microsoft.Dynamics.CRM.MultiSelectPicklistAttributeMetadata'
        SchemaName                   = $Schema
        DisplayName                  = New-Label $Display
        Description                  = New-Label $Description
        RequiredLevel                = @{ Value = 'None' }
        'GlobalOptionSet@odata.bind' = "/GlobalOptionSetDefinitions($ChoiceId)"
    }
}

function Add-RangeColumn([string]$Column, [hashtable]$Def, [string]$Type, [double]$Min, [double]$Max) {
    $script:Ranges[$Column] = @{ Type = $Type; Min = $Min; Max = $Max }
    Add-Column $Column $Def $Type
}

# The singleton discriminator: a required local choice with exactly one option "Default" (default value set). The alternate key on
# it makes a second row impossible. Created with auditing on and the SystemRequired level (an API write without it is refused,
# ApplicationRequired would only be enforced by forms; DEF-009).
function Add-SingletonChoice {
    $column = 'sprk_singleton'
    $attr = Get-Attr $Table $column
    if ($attr) {
        Assert-AttrType $attr $column 'Picklist'
        $script:Expect.Add(@{ Table = $Table; Column = $column; Type = 'Picklist' })
        $have = @(Get-PicklistLabels $Table $column)
        if ($have.Count -ne 1 -or $have[0] -ne 'Default') {
            throw [EscalationException]::new("$Table.$column exists with options [$($have -join ', ')] but the singleton needs exactly [Default]. Editing or removing options needs an owner decision.")
        }
        $script:Skipped.Add((Format-Tally $Table $column)); Write-Host "  skipped  $column (exists, choice with 1 option)" -ForegroundColor Yellow
        return
    }
    $def = Set-Audit @{
        '@odata.type'    = 'Microsoft.Dynamics.CRM.PicklistAttributeMetadata'
        SchemaName       = 'sprk_Singleton'
        DisplayName      = New-Label 'Singleton'
        Description      = New-Label 'Always "Default". With the alternate key sprk_ebillingsetting_singleton it keeps this table to one row (D-59).'
        RequiredLevel    = @{ Value = 'SystemRequired' }
        DefaultFormValue = $SingletonValue
        OptionSet        = @{
            '@odata.type'  = 'Microsoft.Dynamics.CRM.OptionSetMetadata'
            IsGlobal       = $false
            OptionSetType  = 'Picklist'
            Options        = @(@{ Value = $SingletonValue; Label = (New-Label 'Default') })
        }
    }
    Add-Column -Column $column -Definition $def -ExpectedType 'Picklist'
}

# Auditing on the table itself. The module's Add-Table does not set it, so it is read and, when off, switched on with a full PUT
# of the table metadata (the same read-modify-PUT the task 010 script uses for display names).
function Set-TableAudit {
    $path = "EntityDefinitions(LogicalName='$Table')"
    $ent = Get-DvOrNull $path -Retries 4
    if (-not $ent) {
        if ($DryRun) { Write-Host "  WOULD SET auditing on $Table" -ForegroundColor Cyan; return }
        throw "$Table was not found."
    }
    if ($ent.IsAuditEnabled.Value -eq $true) { $script:Skipped.Add("$Table auditing"); Write-Host "  skipped  $Table auditing already on" -ForegroundColor Yellow; return }
    if ($DryRun) { $script:Changed.Add("$Table auditing"); Write-Host "  WOULD SET auditing on $Table" -ForegroundColor Cyan; return }
    $ent.IsAuditEnabled.Value = $true
    Invoke-WhilePropagating { Invoke-Dv -Path "EntityDefinitions($($ent.MetadataId))" -Method PUT -Body $ent -ExtraHeaders @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null }
    $chk = $null
    for ($i = 0; $i -lt 12; $i++) {
        $chk = Get-DvOrNull "$path`?`$select=IsAuditEnabled"
        if ($chk -and $chk.IsAuditEnabled.Value -eq $true) { break }
        Start-Sleep -Seconds 5
    }
    if (-not $chk -or $chk.IsAuditEnabled.Value -ne $true) { throw "$Table auditing is still off after the update." }
    $script:Changed.Add("$Table auditing"); Write-Host "  CHANGED  $Table auditing off -> on" -ForegroundColor Green
}

# The lookup create body (module Add-Lookup) has no auditing flag and a new lookup reads back as not audited, so it is switched
# on afterwards with a read-modify-PUT of the lookup attribute (same shape as Set-ColumnRequiredLevel in the module).
function Set-LookupAudit([string]$Column) {
    $path = "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.LookupAttributeMetadata"
    $a = Get-DvOrNull $path -Retries 4
    if (-not $a) { if ($DryRun) { Write-Host "  WOULD SET auditing on $Column" -ForegroundColor Cyan; return }; throw "$Table.$Column was not found (lookup)." }
    if ($a.IsAuditEnabled.Value -eq $true) { $script:Skipped.Add("$Table.$Column auditing"); Write-Host "  skipped  $Column auditing already on" -ForegroundColor Yellow; return }
    if ($DryRun) { $script:Changed.Add("$Table.$Column auditing"); Write-Host "  WOULD SET auditing on $Column" -ForegroundColor Cyan; return }
    $a.IsAuditEnabled.Value = $true
    Invoke-WhilePropagating { Invoke-Dv -Path $path -Method PUT -Body $a -ExtraHeaders @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null }
    $chk = $null
    for ($i = 0; $i -lt 12; $i++) {
        $chk = Get-DvOrNull $path
        if ($chk -and $chk.IsAuditEnabled.Value -eq $true) { break }
        Start-Sleep -Seconds 5
    }
    if (-not $chk -or $chk.IsAuditEnabled.Value -ne $true) { throw "$Table.$Column auditing is still off after the update." }
    $script:Changed.Add("$Table.$Column auditing"); Write-Host "  CHANGED  $Column auditing off -> on" -ForegroundColor Green
}

# ---------------------------------------------------------------------------------------------------------------
# Run
# ---------------------------------------------------------------------------------------------------------------

Write-Host ""
Write-Host "eBilling task 025 - eBilling settings table (sprk_ebillingsetting)" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Solution: $SolutionUniqueName   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })"
Write-Host ""

try {
    if ($EnvironmentUrl -ne $DevUrl) {
        throw [EscalationException]::new("This script runs only against $DevUrl in R1 (project constraint); got $EnvironmentUrl. Nothing was changed.")
    }

    # --- 0. Pre-checks (read only) ------------------------------------------------------------------------------------
    Write-Host "0. Pre-checks" -ForegroundColor Cyan
    $org = (Get-DvOrNull "organizations?`$select=isauditenabled").value | Select-Object -First 1
    if (-not $org -or $org.isauditenabled -ne $true) {
        throw [EscalationException]::new("Organization-level auditing is OFF in $EnvironmentUrl. The settings table must be audited (FR-48); changing an organization setting needs the owner's OK. Nothing was changed.")
    }
    Write-Host "  organization auditing: on"
    $choice = Get-DvOrNull "GlobalOptionSetDefinitions(Name='$ChoiceName')" -Retries 2
    if (-not $choice) { throw [EscalationException]::new("Global choice $ChoiceName does not exist in $EnvironmentUrl (task 024). Nothing was changed.") }
    $labels = @{}
    foreach ($o in $choice.Options) { $labels[[int]$o.Value] = [string]$o.Label.UserLocalizedLabel.Label }
    if ($labels[$RoleAttorney1] -ne 'Assigned Attorney 1' -or $labels[$RoleAssignedToInternal] -ne 'Assigned To Internal') {
        throw [EscalationException]::new("Global choice $ChoiceName values differ from D-33 (100000000 / 100000004 are '$($labels[$RoleAttorney1])' / '$($labels[$RoleAssignedToInternal])'). Nothing was changed.")
    }
    $ChoiceId = $choice.MetadataId
    Write-Host "  global choice ${ChoiceName}: $($labels.Count) options, seed roles 100000000 / 100000004 present"
    $codeSetExists = [bool](Get-DvOrNull "EntityDefinitions(LogicalName='$CodeSetTable')?`$select=LogicalName" -Retries 2)
    Write-Host "  $CodeSetTable`: $(if ($codeSetExists) { 'exists' } else { 'absent (the default code set lookup is skipped)' })"

    # --- 1. Table -----------------------------------------------------------------------------------------------------
    Write-Host "1. Table $Table" -ForegroundColor Cyan
    Add-Table -Table $Table -Display 'eBilling Setting' -Plural 'eBilling Settings' `
        -Description 'The one audited eBilling settings record (FR-48, D-59): default approver roles, rejection threshold, confidence weights and caps, related-work window, coding chunk size, AP export mapping.' `
        -PrimaryColumn 'sprk_name' -PrimaryDisplay 'Name' -PrimaryDescription 'Name of the settings record.' -PrimaryLength 100 -Ownership OrganizationOwned
    Set-PrimaryNameLength $Table 'sprk_name' 100
    Set-PrimaryNameRequired $Table 'sprk_name'
    Set-TableAudit

    # --- 2. Singleton -------------------------------------------------------------------------------------------------
    Write-Host "2. Singleton choice and alternate key" -ForegroundColor Cyan
    Add-SingletonChoice
    Add-AlternateKey $Table 'sprk_ebillingsetting_singleton' 'eBilling Settings singleton' @('sprk_singleton')

    # --- 3. Columns ---------------------------------------------------------------------------------------------------
    Write-Host "3. Columns" -ForegroundColor Cyan
    Add-Column 'sprk_matterapproverroles' (New-ApproverRolesDef 'sprk_MatterApproverRoles' 'Matter Approver Roles' 'Default approver roles for a Matter whose Billing Approver Role is empty (D-54).' $ChoiceId) 'Virtual'
    Add-Column 'sprk_projectapproverroles' (New-ApproverRolesDef 'sprk_ProjectApproverRoles' 'Project Approver Roles' 'Default approver roles for a Project whose Billing Approver Role is empty (D-54).' $ChoiceId) 'Virtual'
    Add-Column 'sprk_workassignmentapproverroles' (New-ApproverRolesDef 'sprk_WorkAssignmentApproverRoles' 'Work Assignment Approver Roles' 'Default approver roles for a Work Assignment whose Billing Approver Role is empty (D-54).' $ChoiceId) 'Virtual'

    Add-RangeColumn 'sprk_reliabilitythreshold' (New-DecimalDef 'sprk_ReliabilityThreshold' 'Reliability Threshold' 'Combined confidence a field must reach before it can support an automatic rejection (FR-26, D-47). 0 to 1.' 0 1) 'Decimal' 0 1
    Add-RangeColumn 'sprk_weightanalyzer' (New-DecimalDef 'sprk_WeightAnalyzer' 'Weight: Analyzer Confidence' 'Weight of the analyzer (OCR) confidence in the combined confidence (D-47). 0 to 1. Provisional until calibrated (D-69).' 0 1) 'Decimal' 0 1
    Add-RangeColumn 'sprk_weightarithmetic' (New-DecimalDef 'sprk_WeightArithmetic' 'Weight: Arithmetic Check' 'Weight of the arithmetic-consistency check in the combined confidence (D-47). 0 to 1. Provisional until calibrated (D-69).' 0 1) 'Decimal' 0 1
    Add-RangeColumn 'sprk_weightreferencematch' (New-DecimalDef 'sprk_WeightReferenceMatch' 'Weight: Reference Match' 'Weight of the reference-data match (roster, billing period) in the combined confidence (D-47). 0 to 1. Provisional until calibrated (D-69).' 0 1) 'Decimal' 0 1
    Add-RangeColumn 'sprk_weightselfreport' (New-DecimalDef 'sprk_WeightSelfReport' 'Weight: Model Self-Report' 'Weight of the model self-reported confidence in the combined confidence (D-47). 0 to 1. Provisional until calibrated (D-69).' 0 1) 'Decimal' 0 1
    Add-RangeColumn 'sprk_arithmeticfailcap' (New-DecimalDef 'sprk_ArithmeticFailCap' 'Cap: Arithmetic Fails' 'Maximum combined confidence of a field when its arithmetic check fails (D-47). 0 to 1. Provisional until calibrated (D-69).' 0 1) 'Decimal' 0 1
    Add-RangeColumn 'sprk_referencemissingcap' (New-DecimalDef 'sprk_ReferenceMissingCap' 'Cap: No Reference Match' 'Maximum combined confidence of a field with no reference-data match (D-47). 0 to 1. Provisional until calibrated (D-69).' 0 1) 'Decimal' 0 1

    Add-RangeColumn 'sprk_relatedwindowdays' (New-WholeNumberDef 'sprk_RelatedWindowDays' 'Related Work Window (days)' 'Days before and after a line date searched for related work product (FR-34). 0 to 90.' 0 90) 'Integer' 0 90
    Add-RangeColumn 'sprk_codingchunksize' (New-WholeNumberDef 'sprk_CodingChunkSize' 'Coding Chunk Size' 'Invoice lines sent to the coding Action per call (FR-22). 1 to 200.' 1 200) 'Integer' 1 200

    Add-Column 'sprk_apexportmapping' (Set-Audit (New-MemoDef 'sprk_ApExportMapping' 'AP Export Mapping' 'JSON field mapping for the AP export files (FR-41, D-56). Snapshotted per batch.' 100000)) 'Memo'
    Add-Column 'sprk_firmnoticeaienabled' (New-TwoOptionDef 'sprk_FirmNoticeAiEnabled' 'Firm Notice AI Enabled' 'Whether firm notices are drafted by the AI; off uses the template only (FR-50, D-60).' $true) 'Boolean'

    # --- 4. Default code set lookup ----------------------------------------------------------------------------------
    Write-Host "4. Lookup sprk_defaultcodeset -> $CodeSetTable" -ForegroundColor Cyan
    if ($codeSetExists -or (Test-TableKnown $CodeSetTable)) {
        Add-Lookup -Column 'sprk_defaultcodeset' -SchemaName 'sprk_DefaultCodeSet' -RelSchema 'sprk_ebillingsetting_defaultcodeset_billingcodeset' -Target $CodeSetTable `
            -Display 'Default Code Set' -Description 'The UTBMS code set used when nothing else resolves one for a line (FR-22 step 5). Seeded to Litigation.' -DeleteBehavior 'RemoveLink'
        Set-LookupAudit 'sprk_defaultcodeset'
    } else {
        $script:Deferred.Add("sprk_defaultcodeset: $CodeSetTable does not exist yet (task 018); re-run this script after it")
        Write-Host "  SKIPPED  sprk_defaultcodeset ($CodeSetTable does not exist; task 029 re-runs the script after 018)" -ForegroundColor DarkYellow
    }

    # --- Publish (only when something changed; entity-scoped, never PublishAllXml) ------------------------------------
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
        Write-Host "  customizations published" -ForegroundColor Green
    }

    # --- 5. Seed the default row (only when the table has no row) ----------------------------------------------------
    Write-Host "5. Seed" -ForegroundColor Cyan
    $setInfo = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=EntitySetName" -Retries 4
    if (-not $setInfo) {
        if ($DryRun) { Write-Host "  WOULD SEED one default row after the table is created" -ForegroundColor Cyan }
        else { throw "$Table entity set name could not be read." }
    } else {
        $rows = Get-RowCountExact $setInfo.EntitySetName "$($Table)id"
        if ($null -eq $rows) { throw "Could not count the rows of $Table; not seeding." }
        if ($rows -gt 0) {
            $script:Skipped.Add('seed row'); Write-Host "  skipped  seed: $Table already has $rows row(s); nothing is overwritten" -ForegroundColor Yellow
        } else {
            $litigation = $null
            if ($codeSetExists) {
                $q = Get-DvOrNull "sprk_billingcodesets?`$select=sprk_billingcodesetid,sprk_name&`$filter=sprk_name eq 'UTBMS Litigation' and statecode eq 0"
                $litigation = @($q.value) | Select-Object -First 1
            }
            $roles = "$RoleAttorney1,$RoleAssignedToInternal"
            $body = [ordered]@{
                sprk_name                        = 'Default'
                sprk_singleton                   = $SingletonValue
                sprk_matterapproverroles         = $roles
                sprk_projectapproverroles        = $roles
                sprk_workassignmentapproverroles = $roles
                sprk_reliabilitythreshold        = $Seed.Threshold
                sprk_weightanalyzer              = $Seed.WeightAnalyzer
                sprk_weightarithmetic            = $Seed.WeightArithmetic
                sprk_weightreferencematch        = $Seed.WeightReferenceMatch
                sprk_weightselfreport            = $Seed.WeightSelfReport
                sprk_arithmeticfailcap           = $Seed.ArithmeticFailCap
                sprk_referencemissingcap         = $Seed.ReferenceMissingCap
                sprk_relatedwindowdays       = $Seed.WindowDays
                sprk_codingchunksize             = $Seed.ChunkSize
                sprk_firmnoticeaienabled         = $true
                sprk_apexportmapping             = ($ApMappingDefault | ConvertTo-Json -Depth 10 -Compress)
            }
            if ($litigation) {
                # The new lookup's navigation property can read back empty for a while (eventual consistency): wait for it, and never
                # seed without the default code set when the set exists (a re-run would not reseed).
                $navName = $null
                for ($i = 0; $i -lt 12 -and -not $navName; $i++) {
                    $nav = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/ManyToOneRelationships?`$select=ReferencingEntityNavigationPropertyName&`$filter=ReferencingAttribute eq 'sprk_defaultcodeset'"
                    $navName = @($nav.value)[0].ReferencingEntityNavigationPropertyName
                    if (-not $navName -and -not $DryRun) { Start-Sleep -Seconds 5 }
                    if ($DryRun) { break }
                }
                if ($navName) { $body["$navName@odata.bind"] = "/sprk_billingcodesets($($litigation.sprk_billingcodesetid))" }
                elseif (-not $DryRun) { throw "The navigation property of sprk_defaultcodeset is not readable after 60 s; not seeding without the default code set. Re-run." }
            } else {
                Write-Host "  note: no 'UTBMS Litigation' code set found; the seed row has no default code set" -ForegroundColor DarkYellow
            }
            if ($DryRun) {
                $script:Created.Add('seed row'); Write-Host "  WOULD SEED one default row ($($body.Count) values$(if ($litigation) { ', default code set UTBMS Litigation' }))" -ForegroundColor Cyan
            } else {
                Invoke-WhilePropagating { Invoke-Dv -Path $setInfo.EntitySetName -Method POST -Body $body | Out-Null } | Out-Null
                $rows = Get-RowCountExact $setInfo.EntitySetName "$($Table)id"
                if ($rows -ne 1) { throw "After seeding, $Table holds $rows row(s), expected 1." }
                $script:Created.Add('seed row'); Write-Host "  SEEDED   default row (approver roles $roles, threshold $($Seed.Threshold), window $($Seed.WindowDays), chunk $($Seed.ChunkSize))" -ForegroundColor Green
            }
        }
    }

    # --- Verification (metadata read of everything this script owns) --------------------------------------------------
    if (-not $DryRun) {
        Write-Host "Verify" -ForegroundColor Cyan
        $bad = [System.Collections.Generic.List[string]]::new()
        $own = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=OwnershipType,PrimaryNameAttribute,EntitySetName,IsAuditEnabled" -Retries 4
        if (-not $own) { $bad.Add("$Table not readable") }
        else {
            Write-Host ("  {0}: {1}, primary name {2}, entity set {3}, auditing {4}" -f $Table, $own.OwnershipType, $own.PrimaryNameAttribute, $own.EntitySetName, $own.IsAuditEnabled.Value)
            if ($own.OwnershipType -ne 'OrganizationOwned') { $bad.Add("$Table ownership is $($own.OwnershipType), expected OrganizationOwned") }
            if ($own.IsAuditEnabled.Value -ne $true) { $bad.Add("$Table auditing is off") }
        }
        foreach ($e in $script:Expect) {
            $a = $null
            for ($i = 0; $i -lt 12 -and -not $a; $i++) {
                $a = Get-Attr $e.Table $e.Column 'LogicalName,AttributeType,IsAuditEnabled,RequiredLevel'
                if (-not $a) { Start-Sleep -Seconds 5 }
            }
            if (-not $a) { $bad.Add("$($e.Table).$($e.Column) missing"); continue }
            if ($a.AttributeType -ne $e.Type) { $bad.Add("$($e.Table).$($e.Column) is $($a.AttributeType), expected $($e.Type)"); continue }
            if ($a.IsAuditEnabled.Value -ne $true) { $bad.Add("$($e.Table).$($e.Column) auditing is off") }
            $base = "EntityDefinitions(LogicalName='$($e.Table)')/Attributes(LogicalName='$($e.Column)')"
            if ($script:Ranges.Contains($e.Column)) {
                $r = $script:Ranges[$e.Column]
                $rg = Get-DvOrNull "$base/Microsoft.Dynamics.CRM.$($e.Type)AttributeMetadata?`$select=MinValue,MaxValue" -Retries 4
                Write-Host ("  {0}: {1}, min {2}, max {3}, auditing {4}" -f $e.Column, $e.Type.ToLower(), $rg.MinValue, $rg.MaxValue, $a.IsAuditEnabled.Value)
                if (-not $rg -or [double]$rg.MinValue -ne $r.Min -or [double]$rg.MaxValue -ne $r.Max) { $bad.Add("$($e.Column) range is [$($rg.MinValue), $($rg.MaxValue)], expected [$($r.Min), $($r.Max)]") }
            } elseif ($e.Column -eq 'sprk_singleton') {
                $pk = $null
                for ($i = 0; $i -lt 12; $i++) {
                    $pk = Get-DvOrNull "$base/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=DefaultFormValue&`$expand=OptionSet(`$select=Options,IsGlobal)"
                    if ($pk -and @($pk.OptionSet.Options).Count -gt 0) { break }
                    Start-Sleep -Seconds 5
                }
                $have = @($pk.OptionSet.Options)
                Write-Host ("  sprk_singleton: choice (local, global={0}) {1}, default {2}, required {3}" -f $pk.OptionSet.IsGlobal, (($have | ForEach-Object { "$($_.Value)=$($_.Label.UserLocalizedLabel.Label)" }) -join ', '), $pk.DefaultFormValue, $a.RequiredLevel.Value)
                if ($have.Count -ne 1 -or $have[0].Label.UserLocalizedLabel.Label -ne 'Default') { $bad.Add('sprk_singleton options differ from the single option Default') }
                if ([int]$pk.DefaultFormValue -ne $SingletonValue) { $bad.Add("sprk_singleton default is $($pk.DefaultFormValue), expected $SingletonValue") }
                if ($a.RequiredLevel.Value -ne 'SystemRequired') { $bad.Add("sprk_singleton required level is $($a.RequiredLevel.Value), expected SystemRequired") }
            } elseif ($e.Type -eq 'Virtual') {
                $mp = Get-DvOrNull "$base/Microsoft.Dynamics.CRM.MultiSelectPicklistAttributeMetadata?`$select=LogicalName&`$expand=OptionSet(`$select=Name,IsGlobal)" -Retries 4
                Write-Host ("  {0}: multi-select on '{1}' (global: {2}), auditing {3}" -f $e.Column, $mp.OptionSet.Name, $mp.OptionSet.IsGlobal, $a.IsAuditEnabled.Value)
                if ($mp.OptionSet.Name -ne $ChoiceName -or $mp.OptionSet.IsGlobal -ne $true) { $bad.Add("$($e.Column) is not bound to the global choice $ChoiceName") }
            } elseif ($e.Type -eq 'Lookup') {
                $lk = $null
                for ($i = 0; $i -lt 12; $i++) {
                    $lk = Get-DvOrNull "$base/Microsoft.Dynamics.CRM.LookupAttributeMetadata?`$select=Targets"
                    if ($lk -and @($lk.Targets).Count -gt 0) { break }
                    Start-Sleep -Seconds 5
                }
                if (-not $lk -or $lk.Targets -notcontains $e.Target) { $bad.Add("$($e.Table).$($e.Column) does not target $($e.Target)") }
                else { Write-Host ("  {0}: lookup -> {1}, auditing {2}" -f $e.Column, (@($lk.Targets) -join ','), $a.IsAuditEnabled.Value) }
            } elseif ($e.Type -eq 'Memo') {
                $mm = Get-DvOrNull "$base/Microsoft.Dynamics.CRM.MemoAttributeMetadata?`$select=MaxLength" -Retries 4
                Write-Host ("  {0}: multiline, max length {1}, auditing {2}" -f $e.Column, $mm.MaxLength, $a.IsAuditEnabled.Value)
                if (-not $mm -or [int]$mm.MaxLength -ne 100000) { $bad.Add("$($e.Column) max length is $($mm.MaxLength), expected 100000") }
            } elseif ($e.Type -eq 'Boolean') {
                $bo = Get-DvOrNull "$base/Microsoft.Dynamics.CRM.BooleanAttributeMetadata?`$select=DefaultValue" -Retries 4
                Write-Host ("  {0}: two options, default {1}, auditing {2}" -f $e.Column, $bo.DefaultValue, $a.IsAuditEnabled.Value)
            } else {
                Write-Host ("  {0}: {1}, auditing {2}" -f $e.Column, $a.AttributeType.ToLower(), $a.IsAuditEnabled.Value)
            }
        }
        $pn = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='sprk_name')/Microsoft.Dynamics.CRM.StringAttributeMetadata?`$select=MaxLength,RequiredLevel,IsAuditEnabled" -Retries 4
        Write-Host ("  sprk_name: text, max length {0}, required {1}, auditing {2}" -f $pn.MaxLength, $pn.RequiredLevel.Value, $pn.IsAuditEnabled.Value)
        if (-not $pn -or [int]$pn.MaxLength -ne 100) { $bad.Add("sprk_name max length is $($pn.MaxLength), expected 100") }
        $keys = Get-Keys $Table | Where-Object { $_.SchemaName -eq 'sprk_ebillingsetting_singleton' }
        if (-not $keys) { $bad.Add('alternate key sprk_ebillingsetting_singleton missing') }
        else { Write-Host ("  key sprk_ebillingsetting_singleton on ({0}): index {1}" -f (@($keys.KeyAttributes) -join ','), $keys.EntityKeyIndexStatus); if ($keys.EntityKeyIndexStatus -ne 'Active') { $bad.Add("key index is $($keys.EntityKeyIndexStatus)") } }
        if ($bad.Count -gt 0) { throw "Verification failed: $($bad -join '; ')" }
        Write-Host "  all $($script:Expect.Count) expected columns present with the right type, range and auditing" -ForegroundColor Green
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
if ($State.Deferred.Count -gt 0) { Write-Host ("deferred: {0}  {1}" -f $State.Deferred.Count, ($State.Deferred -join ', ')) -ForegroundColor DarkYellow }
if ($DryRun) { Write-Host "WhatIf run: nothing was written." -ForegroundColor Cyan }
exit 0
