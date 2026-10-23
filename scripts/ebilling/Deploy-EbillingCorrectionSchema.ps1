<#
.SYNOPSIS
    eBilling R1 task 027 - creates the structured correction record table sprk_invoicecorrection (D-64, FR-31, D-47): one
    row per field confirmation or correction, queryable per Action / analyzer version and outcome type.

.DESCRIPTION
    Idempotent, additive Dataverse Web API metadata script. Every item is check-then-create; a second run reports every
    item as skipped and makes no write (no publish either).

    Shape decision (D-82 proposed, notes/027-correction-record-schema.md): a CHILD TABLE, not columns on the invoice line
    (sprk_billingevent). Several fields per line each need their own AI value, confidence and outcome; header fields are
    corrected too (no line); one value can be confirmed and corrected later; rows must be queried per Action version.

    Items (tasks/027-correction-record-schema.poml, spec FR-31 / D-64):
      1  Table sprk_invoicecorrection (user-owned, "Invoice Correction"), primary name sprk_name (200), and the columns
         sprk_fieldname (100, SystemRequired), sprk_aivalue / sprk_finalvalue (multiline 4000), sprk_aiconfidence (decimal
         0..1, precision 4), sprk_outcometype (local choice Corrected / Confirmed individually / Accepted in bulk /
         Unreviewed, SystemRequired), sprk_islabel (two-option, default No), sprk_sourcecode (100), sprk_sourceversion (20),
         sprk_reviewerrole (100), sprk_reviewedon (date and time).
      2  Lookups: sprk_invoice -> sprk_invoice (SystemRequired, delete = Cascade), sprk_invoiceline -> sprk_billingevent
         (empty for header fields, delete = RemoveLink), sprk_reviewedby -> systemuser (delete = RemoveLink), and the ADR-024
         entity-specific lookups sprk_regardingmatter / sprk_regardingproject / sprk_regardingworkassignment plus
         sprk_regardingrecordtype -> sprk_recordtype_ref and the resolver text sprk_regardingrecordid (100),
         sprk_regardingrecordname (200), sprk_regardingrecordnumber (100), sprk_regardingrecordurl (500, Url), because the
         table holds values read from secure invoices and joins the invoice family (task 013 maps them; same shape as 021).

    Cascade delete on BOTH the invoice and the line is not possible: Dataverse treats a cascade-delete relationship as the
    parental one and allows a table only one ("is parented to Entity sprk_invoice. Cannot create another parental relation
    with Entity sprk_BillingEvent", 0x80047007). The invoice (always set) is the parent; the line link is removed if a line
    is deleted (the row keeps its invoice, field name and name label).

    NEVER deletes or renames anything. An existing item that conflicts (a column of another type, a lookup to another
    table, a choice with other options) stops the script with exit code 2 and an ESCALATION message.

    ADR-002: no plugin, business rule, calculated / rollup column or flow. The writers (tasks 100, 101, 103) set
    sprk_islabel from the outcome type (true only for Corrected and Confirmed individually) and treat the table as
    append-only; "one populated core lookup per row" is enforced by task 013. Required levels (DEF-009): ApplicationRequired
    is not enforced on API writes, so SystemRequired is used where a null corrupts meaning (the invoice, the field name and
    the outcome type); the primary name stays ApplicationRequired (forms).

    ADR-015: sprk_aivalue / sprk_finalvalue may hold invoice narrative text. No AI prompt content is stored, and logs never
    carry these values.

    Eventually consistent metadata reads: a 404 right after a create is not trusted (the item is re-read); a create
    refused with "already exists" counts as skipped after a type check on a settled read; a create issued right after a
    table create is retried while the table propagates (MetadataCache); customization / publish locks and transient
    faults are retried with backoff. The primary name's length AND required level in an entity-create body are ignored by
    Dataverse, so both are set afterwards while the table has no rows.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the created components are added to (header MSCRM.SolutionUniqueName).

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingCorrectionSchema.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingCorrectionSchema.ps1

.NOTES
    Project: ebilling-r1, task 027. Dev only in R1. The Dataverse helpers live in EbillingSchema.psm1 (shared by the
    eBilling schema scripts). Patterns: Deploy-EbillingApprovalSchema.ps1 (021, same ADR-024 shape),
    Deploy-EbillingSubmissionSchema.ps1 (020), Deploy-EbillingBillingCodeSchema.ps1 (018).
    Exit codes: 0 = done (created / skipped), 1 = error, 2 = escalation (conflicting existing item).
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
$CorrTable = 'sprk_invoicecorrection'
# Exactly the four D-64 outcome types (order = option values 100000000 .. 100000003).
$OutcomeLabels = @('Corrected', 'Confirmed individually', 'Accepted in bulk', 'Unreviewed')

# Shared Dataverse helpers (scripts/ebilling/EbillingSchema.psm1). The state lists are shared by reference.
$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$DryRun -Entity $CorrTable
$script:Created = $State.Created
$script:Changed = $State.Changed
$script:Skipped = $State.Skipped
$script:Expect = $State.Expect

# ---------------------------------------------------------------------------------------------------------------
# Script-specific helpers (column kinds the shared module does not build)
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

function New-TwoOptionDef([string]$Schema, [string]$Display, [string]$Description, [bool]$Default) {
    return @{
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

# Raises a choice column to SystemRequired (the module's Set-ColumnRequiredLevel handles String and Lookup only). Same
# rules: never lowers a level, and refuses while existing rows hold a null.
function Set-ChoiceSystemRequired([string]$Table, [string]$Column) {
    $path = "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
    $a = Get-DvOrNull $path -Retries 4
    if (-not $a) { if ($DryRun) { return }; throw "$Table.$Column was not found (choice)." }
    $rank = @{ None = 0; Recommended = 1; ApplicationRequired = 2; SystemRequired = 3 }
    $cur = [string]$a.RequiredLevel.Value
    for ($i = 0; $i -lt 3 -and $rank[$cur] -lt 3; $i++) {
        Start-Sleep -Seconds 2
        $again = Get-DvOrNull $path
        if ($again -and $rank[[string]$again.RequiredLevel.Value] -gt $rank[$cur]) { $a = $again; $cur = [string]$a.RequiredLevel.Value }
    }
    if ($rank[$cur] -ge 3) { $script:Skipped.Add("$Table.$Column required SystemRequired"); Write-Host "  skipped  $Column already SystemRequired" -ForegroundColor Yellow; return }
    $set = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=EntitySetName" -Retries 4
    $nulls = if ($set) { Get-RowCountExact $set.EntitySetName "$($Table)id" "$Column eq null" } else { $null }
    if ($null -eq $nulls -or $nulls -gt 0) {
        Write-Host "  WARNING: $Table.$Column left at $cur (wanted SystemRequired): $(if ($null -eq $nulls) { 'null rows could not be counted' } else { "$nulls row(s) with no value" })" -ForegroundColor DarkYellow
        $State.Deferred.Add("$Table.$Column required SystemRequired"); return
    }
    if ($DryRun) { $script:Changed.Add("$Table.$Column required SystemRequired"); Write-Host "  WOULD SET $Column required level $cur -> SystemRequired" -ForegroundColor Cyan; return }
    $a.RequiredLevel.Value = 'SystemRequired'
    Invoke-WhilePropagating { Invoke-Dv -Path $path -Method PUT -Body $a -ExtraHeaders @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null }
    $chk = $null
    for ($i = 0; $i -lt 12; $i++) {
        $chk = Get-DvOrNull $path
        if ($chk -and $chk.RequiredLevel.Value -eq 'SystemRequired') { break }
        Start-Sleep -Seconds 5
    }
    if (-not $chk -or $chk.RequiredLevel.Value -ne 'SystemRequired') { throw "$Table.$Column required level is still $($chk.RequiredLevel.Value) after the update." }
    $script:Changed.Add("$Table.$Column required SystemRequired"); Write-Host "  CHANGED  $Column required level $cur -> SystemRequired" -ForegroundColor Green
}

# ---------------------------------------------------------------------------------------------------------------
# Run
# ---------------------------------------------------------------------------------------------------------------

Write-Host ""
Write-Host "eBilling task 027 - structured correction record schema" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Solution: $SolutionUniqueName   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })"
Write-Host ""

try {
    foreach ($ref in 'sprk_invoice', 'sprk_billingevent', 'systemuser', 'sprk_matter', 'sprk_project', 'sprk_workassignment', 'sprk_recordtype_ref') {
        if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$ref')?`$select=LogicalName" -Retries 4)) {
            throw "$ref does not exist in $EnvironmentUrl."
        }
    }

    # --- 1. Table + columns -------------------------------------------------------------------------------------------
    Write-Host "1. Table $CorrTable" -ForegroundColor Cyan
    Add-Table -Table $CorrTable -Display 'Invoice Correction' -Plural 'Invoice Corrections' -Ownership 'UserOwned' `
        -Description 'One structured record per reviewer confirmation or correction of an extracted or AI-coded invoice value (D-64): field, AI value, final value, confidence, Action / analyzer code and version, outcome type, reviewer and when. Append-only for writers.' `
        -PrimaryColumn 'sprk_name' -PrimaryDisplay 'Name' -PrimaryDescription 'Label of the record, composed by the server (field name and line or invoice).' -PrimaryLength 200
    Set-PrimaryNameLength $CorrTable 'sprk_name' 200
    Set-PrimaryNameRequired $CorrTable 'sprk_name'
    Add-Column 'sprk_fieldname' (New-StringDef 'sprk_FieldName' 'Field Name' 'The invoice or line field this record is about (for example sprk_utbmscode).' 100 'Text' 'None') 'String'
    Set-StringRequiredLevel $CorrTable 'sprk_fieldname' 'SystemRequired'
    Add-Column 'sprk_aivalue' (New-MemoDef 'sprk_AiValue' 'AI Value' 'The value the AI produced (extraction or coding). May contain invoice narrative text (ADR-015); never logged.' 4000) 'Memo'
    Add-Column 'sprk_finalvalue' (New-MemoDef 'sprk_FinalValue' 'Final Value' 'The value after review. May contain invoice narrative text (ADR-015); never logged.' 4000) 'Memo'
    Add-Column 'sprk_aiconfidence' (New-DecimalDef 'sprk_AiConfidence' 'AI Confidence' 'Combined confidence of the AI value, 0 to 1 (D-47).' 4 0 1) 'Decimal'
    Add-LocalChoice -Column 'sprk_outcometype' -Schema 'sprk_OutcomeType' -Display 'Outcome Type' `
        -Description 'What the reviewer did: Corrected, Confirmed individually, Accepted in bulk or Unreviewed (D-64). Only Corrected and Confirmed individually count as labels.' `
        -Labels $OutcomeLabels -RequiredLevel 'ApplicationRequired'
    Set-ChoiceSystemRequired $CorrTable 'sprk_outcometype'
    Add-Column 'sprk_islabel' (New-TwoOptionDef 'sprk_IsLabel' 'Is Label' 'Yes only for Corrected and Confirmed individually outcomes (D-64). Stored so the calibration query and the later tuning platform filter labels without re-implementing the rule; writers set it from the outcome type.' $false) 'Boolean'
    Add-Column 'sprk_sourcecode' (New-StringDef 'sprk_SourceCode' 'Source Code' 'The Action code or analyzer id that produced the AI value (for example invoice-line-code or sprk_invoice_v1).' 100) 'String'
    Add-Column 'sprk_sourceversion' (New-StringDef 'sprk_SourceVersion' 'Source Version' 'The version of the Action or analyzer that produced the AI value.' 20) 'String'
    Add-Column 'sprk_reviewerrole' (New-StringDef 'sprk_ReviewerRole' 'Reviewer Role' 'The role in which the reviewer acted (for example Assigned Attorney 1).' 100) 'String'
    Add-Column 'sprk_reviewedon' (New-DateTimeDef 'sprk_ReviewedOn' 'Reviewed On' 'When the reviewer confirmed or corrected the value.') 'DateTime'

    # --- 2. Lookups ---------------------------------------------------------------------------------------------------
    Write-Host "2. Lookups" -ForegroundColor Cyan
    $RegardingNote = 'At most one of the three is populated per row (enforced server-side, task 013).'
    Add-Lookup -Column 'sprk_invoice' -SchemaName 'sprk_Invoice' -RelSchema 'sprk_invoicecorrection_invoice_invoice' -Target 'sprk_invoice' `
        -Display 'Invoice' -Description 'The invoice this record belongs to. Required.' -RequiredLevel 'ApplicationRequired' -DeleteBehavior 'Cascade'
    Set-LookupRequiredLevel $CorrTable 'sprk_invoice' 'SystemRequired'
    Add-Lookup -Column 'sprk_invoiceline' -SchemaName 'sprk_InvoiceLine' -RelSchema 'sprk_invoicecorrection_invoiceline_billingevent' -Target 'sprk_billingevent' `
        -Display 'Invoice Line' -Description 'The invoice line (sprk_billingevent) this record is about. Empty for a header field.' -DeleteBehavior 'RemoveLink'
    Add-Lookup -Column 'sprk_reviewedby' -SchemaName 'sprk_ReviewedBy' -RelSchema 'sprk_invoicecorrection_reviewedby_systemuser' -Target 'systemuser' `
        -Display 'Reviewed By' -Description 'The user who confirmed or corrected the value.' -DeleteBehavior 'RemoveLink'
    Add-Lookup -Column 'sprk_regardingmatter' -SchemaName 'sprk_RegardingMatter' -RelSchema 'sprk_invoicecorrection_regmatter' -Target 'sprk_matter' `
        -Display 'Regarding Matter' -Description "ADR-024 entity-specific lookup. $RegardingNote"
    Add-Lookup -Column 'sprk_regardingproject' -SchemaName 'sprk_RegardingProject' -RelSchema 'sprk_invoicecorrection_regproject' -Target 'sprk_project' `
        -Display 'Regarding Project' -Description "ADR-024 entity-specific lookup. $RegardingNote"
    Add-Lookup -Column 'sprk_regardingworkassignment' -SchemaName 'sprk_RegardingWorkAssignment' -RelSchema 'sprk_invoicecorrection_regworkassignment' -Target 'sprk_workassignment' `
        -Display 'Regarding Work Assignment' -Description "ADR-024 entity-specific lookup. $RegardingNote"
    Add-Lookup -Column 'sprk_regardingrecordtype' -SchemaName 'sprk_RegardingRecordType' -RelSchema 'sprk_invoicecorrection_regrecordtype_recordtype_ref' -Target 'sprk_recordtype_ref' `
        -Display 'Regarding Record Type' -Description 'ADR-024 resolver field: the record type of the core record the correction is regarding.'
    # ebilling-r1 task 013: the creator-person lookup the BFF stamps on an app-only create (secure-record family).
    Add-Lookup -Column 'sprk_createdbyperson' -SchemaName 'sprk_CreatedByPerson' -RelSchema 'sprk_systemuser_sprk_invoicecorrection_createdbyperson' -Target 'systemuser' -Display 'Created By (Person)' -Description 'The PERSON who asked for this record. Stamped by the BFF only (field-secured by scripts/Set-ChildRecordCreatorPersonSchema.ps1): createdby is the BFF application user for a record the BFF creates app-only, so this column is what records the person. ebilling-r1 task 013 (secure-record family; unified-access-control-r2 task 146 shape).'
    Add-Column 'sprk_regardingrecordid' (New-StringDef 'sprk_RegardingRecordId' 'Regarding Record Id' 'ADR-024 resolver field: id of the core record the correction is regarding.' 100) 'String'
    Add-Column 'sprk_regardingrecordname' (New-StringDef 'sprk_RegardingRecordName' 'Regarding Record Name' 'ADR-024 resolver field: name of that record.' 200) 'String'
    Add-Column 'sprk_regardingrecordnumber' (New-StringDef 'sprk_RegardingRecordNumber' 'Regarding Record Number' 'ADR-024 resolver field: number of that record.' 100) 'String'
    Add-Column 'sprk_regardingrecordurl' (New-StringDef 'sprk_RegardingRecordUrl' 'Regarding Record URL' 'ADR-024 resolver field: URL of that record.' 500 'Url') 'String'

    # --- Publish (only when something changed; entity-scoped, never PublishAllXml) ------------------------------------
    Write-Host "Publish" -ForegroundColor Cyan
    $wrote = ($script:Created.Count + $script:Changed.Count) -gt 0
    if ($DryRun) {
        Write-Host "  WhatIf: publish skipped" -ForegroundColor Cyan
    } elseif (-not $wrote) {
        Write-Host "  nothing changed - no publish" -ForegroundColor Yellow
    } else {
        Invoke-Dv -Path 'PublishXml' -Method POST -Body @{
            ParameterXml = "<importexportxml><entities><entity>$CorrTable</entity><entity>sprk_invoice</entity><entity>sprk_billingevent</entity><entity>systemuser</entity><entity>sprk_matter</entity><entity>sprk_project</entity><entity>sprk_workassignment</entity><entity>sprk_recordtype_ref</entity></entities></importexportxml>"
        } | Out-Null
        Write-Host "  customizations published" -ForegroundColor Green
    }

    # --- Verification (metadata read of everything this script owns) --------------------------------------------------
    if (-not $DryRun) {
        Write-Host "Verify" -ForegroundColor Cyan
        $bad = [System.Collections.Generic.List[string]]::new()
        foreach ($e in $script:Expect) {
            $a = $null
            for ($i = 0; $i -lt 12 -and -not $a; $i++) {
                $a = Get-Attr $e.Table $e.Column
                if (-not $a) { Start-Sleep -Seconds 5 }
            }
            if (-not $a) { $bad.Add("$($e.Table).$($e.Column) missing"); continue }
            if ($a.AttributeType -ne $e.Type) { $bad.Add("$($e.Table).$($e.Column) is $($a.AttributeType), expected $($e.Type)"); continue }
            if ($e.Type -eq 'Lookup') {
                $lk = Get-DvOrNull "EntityDefinitions(LogicalName='$($e.Table)')/Attributes(LogicalName='$($e.Column)')/Microsoft.Dynamics.CRM.LookupAttributeMetadata?`$select=Targets"
                if (-not $lk -or $lk.Targets -notcontains $e.Target) { $bad.Add("$($e.Table).$($e.Column) does not target $($e.Target)") }
            }
        }
        $own = Get-DvOrNull "EntityDefinitions(LogicalName='$CorrTable')?`$select=OwnershipType,PrimaryNameAttribute,EntitySetName" -Retries 4
        if (-not $own) { $bad.Add("$CorrTable not readable") }
        else {
            Write-Host ("  {0}: {1}, primary name {2}, entity set {3}" -f $CorrTable, $own.OwnershipType, $own.PrimaryNameAttribute, $own.EntitySetName)
            if ($own.OwnershipType -ne 'UserOwned') { $bad.Add("$CorrTable ownership is $($own.OwnershipType), expected UserOwned") }
        }
        # Required levels: the invoice, the field name and the outcome type are SystemRequired (enforced on every write).
        foreach ($rq in @(@('sprk_invoice', 'Lookup'), @('sprk_fieldname', 'String'), @('sprk_outcometype', 'Picklist'))) {
            $lvl = $null
            for ($i = 0; $i -lt 12; $i++) {
                $r = Get-DvOrNull "EntityDefinitions(LogicalName='$CorrTable')/Attributes(LogicalName='$($rq[0])')?`$select=RequiredLevel"
                $lvl = if ($r) { $r.RequiredLevel.Value } else { $null }
                if ($lvl -eq 'SystemRequired') { break }
                Start-Sleep -Seconds 5
            }
            Write-Host "  $($rq[0]) required level: $lvl"
            if ($lvl -ne 'SystemRequired') { $bad.Add("$($rq[0]) is not SystemRequired") }
        }
        # Outcome type: exactly the four D-64 values.
        $pk = $null; $have = @()
        for ($i = 0; $i -lt 12 -and $have.Count -eq 0; $i++) {
            $pk = Get-DvOrNull "EntityDefinitions(LogicalName='$CorrTable')/Attributes(LogicalName='sprk_outcometype')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=LogicalName&`$expand=OptionSet(`$select=Options,IsGlobal)"
            $have = @($pk.OptionSet.Options)
            if ($have.Count -eq 0) { Start-Sleep -Seconds 5 }
        }
        Write-Host ("  sprk_outcometype: " + (($have | Sort-Object Value | ForEach-Object { "$($_.Value)=$($_.Label.UserLocalizedLabel.Label)" }) -join ', ') + "   global: $($pk.OptionSet.IsGlobal)")
        if ($pk.OptionSet.IsGlobal -or $have.Count -ne $OutcomeLabels.Count -or @($have | Where-Object { $_.Label.UserLocalizedLabel.Label -notin $OutcomeLabels }).Count -gt 0) { $bad.Add('sprk_outcometype options differ from the four D-64 values (or the choice is global)') }
        # Confidence range and label default.
        $dc = Get-DvOrNull "EntityDefinitions(LogicalName='$CorrTable')/Attributes(LogicalName='sprk_aiconfidence')/Microsoft.Dynamics.CRM.DecimalAttributeMetadata?`$select=Precision,MinValue,MaxValue" -Retries 4
        Write-Host ("  sprk_aiconfidence: decimal precision {0}, {1} .. {2}" -f $dc.Precision, $dc.MinValue, $dc.MaxValue)
        if ($dc.Precision -ne 4 -or [double]$dc.MinValue -ne 0 -or [double]$dc.MaxValue -ne 1) { $bad.Add('sprk_aiconfidence is not decimal 0..1 precision 4') }
        $bo = Get-DvOrNull "EntityDefinitions(LogicalName='$CorrTable')/Attributes(LogicalName='sprk_islabel')/Microsoft.Dynamics.CRM.BooleanAttributeMetadata?`$select=DefaultValue" -Retries 4
        Write-Host "  sprk_islabel: two-option default $($bo.DefaultValue)"
        if ($bo.DefaultValue -ne $false) { $bad.Add("sprk_islabel default is $($bo.DefaultValue), expected False") }
        # Lookup cascade behaviour: the invoice (the parent) cascades on delete; everything else removes the link.
        foreach ($c in 'sprk_invoice', 'sprk_invoiceline', 'sprk_reviewedby', 'sprk_regardingmatter', 'sprk_regardingproject', 'sprk_regardingworkassignment', 'sprk_regardingrecordtype') {
            $rn = (Get-DvOrNull "EntityDefinitions(LogicalName='$CorrTable')/ManyToOneRelationships?`$select=SchemaName,ReferencingAttribute,CascadeConfiguration&`$filter=ReferencingAttribute eq '$c'").value
            if (-not $rn) { $bad.Add("no many-to-one relationship for $c"); continue }
            $cc = @($rn)[0].CascadeConfiguration
            Write-Host ("  {0}: {1}  delete {2}, assign {3}, share {4}" -f $c, @($rn)[0].SchemaName, $cc.Delete, $cc.Assign, $cc.Share)
            $want = if ($c -eq 'sprk_invoice') { 'Cascade' } else { 'RemoveLink' }
            if ($cc.Delete -ne $want) { $bad.Add("$c delete behaviour is $($cc.Delete), expected $want") }
        }
        if ($bad.Count -gt 0) { throw "Verification failed: $($bad -join '; ')" }
        Write-Host "  all $($script:Expect.Count) expected columns present with the right type and target" -ForegroundColor Green
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
# ebilling-r1 task 013: sprk_createdbyperson is created here UNSECURED; only the creator-person script secures it and grants the BFF profiles.
Write-Host "NEXT: run scripts/Set-ChildRecordCreatorPersonSchema.ps1 (dry run, -Apply, -Verify) so sprk_createdbyperson is field-secured (only the BFF may write it)." -ForegroundColor DarkYellow
exit 0
