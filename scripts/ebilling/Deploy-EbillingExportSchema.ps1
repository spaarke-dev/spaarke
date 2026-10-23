<#
.SYNOPSIS
    eBilling R1 task 023 - creates the AP export batch table sprk_billingexport (FR-13, FR-41, D-21, D-56) and the
    single-valued batch lookup sprk_invoice.sprk_billingexport that keeps an invoice in at most one batch.

.DESCRIPTION
    Idempotent, additive Dataverse Web API metadata script. Every item is check-then-create; a second run reports every
    item as skipped and makes no write (no publish either).

    Items (tasks/023-billingexport-schema.poml, spec FR-13 / FR-41):
      1  Table sprk_billingexport (user-owned, "Billing Export"), primary name sprk_name (text 100, the batch id), and the
         columns sprk_invoicecount (whole number, min 0), sprk_totalamount (money), sprk_formats (multi-select local choice
         CSV / JSON / XML; XML is reserved, D-21), sprk_exportstatus (local choice Generating / Completed / Failed),
         sprk_mappingsnapshot (multiline 1048576: the field mapping used, snapshotted per batch, FR-41) and
         sprk_failurereason (text 200). Created by / on are the platform columns.
      2  Lookups (delete = RemoveLink, everything else NoCascade): sprk_csvdocument -> sprk_document,
         sprk_jsondocument -> sprk_document, and on sprk_invoice: sprk_billingexport -> sprk_billingexport ("Export Batch").
         The invoice lookup is single-valued, which is what structurally keeps an invoice in one batch.

    Pre-check (before ANY write, project escalation trigger): if sprk_invoice already has a column named
    sprk_billingexport that is not a lookup to sprk_billingexport, or a lookup to another export / batch table, the script
    STOPS (exit 2) rather than adding a second batch link.

    NEVER deletes or renames anything. An existing item that conflicts (a column of another type, a lookup to another
    table, a choice with other options) stops the script with exit code 2 and an ESCALATION message.

    ADR-002: no plugin, business rule, calculated / rollup column or flow. Invoice count and total are written by the
    batch writer (task 150) on the server; they are plain columns here.

    Eventually consistent metadata reads: a 404 right after a create is not trusted (the item is re-read); a create
    refused with "already exists" counts as skipped after a type check on a settled read; a create issued right after a
    table create is retried while the table propagates (MetadataCache); customization / publish locks and transient
    faults are retried with backoff. The primary name's length AND required level in an entity-create body are ignored by
    Dataverse, so both are set afterwards while the table has no rows. ApplicationRequired is not enforced on API writes;
    nothing here is SystemRequired because a null value corrupts no meaning on any of these columns.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the created components are added to (header MSCRM.SolutionUniqueName).

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingExportSchema.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingExportSchema.ps1

.NOTES
    Project: ebilling-r1, task 023. Dev only in R1. The Dataverse helpers live in EbillingSchema.psm1 (shared by the
    eBilling schema scripts; DEF-012). Exit codes: 0 = done (created / skipped), 1 = error, 2 = escalation (conflicting existing item or
    an existing second batch link on sprk_invoice).
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
$ExportTable = 'sprk_billingexport'
$FormatLabels = @('CSV', 'JSON', 'XML')
$StatusLabels = @('Generating', 'Completed', 'Failed')

# Shared Dataverse helpers (scripts/ebilling/EbillingSchema.psm1). The state lists are shared by reference.
$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$DryRun -Entity $ExportTable
$script:Created = $State.Created
$script:Changed = $State.Changed
$script:Skipped = $State.Skipped
$script:Expect = $State.Expect
$script:Planned = $State.Planned
$script:JustCreated = $State.JustCreated

# ---------------------------------------------------------------------------------------------------------------
# Task 023 helpers
# ---------------------------------------------------------------------------------------------------------------

function New-WholeNumberDef([string]$Schema, [string]$Display, [string]$Description, [int]$Min, [int]$Max) {
    return @{
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

# A LOCAL multi-select choice column (pattern: scripts/Create-ScopeCapabilityFields.ps1). Its AttributeType reads back as
# 'Virtual'. If it exists its option labels must be exactly the expected set (never edited by script).
function Add-LocalMultiChoice {
    param([string]$Column, [string]$Schema, [string]$Display, [string]$Description, [string[]]$Labels)
    $table = Get-EbEntity
    $typed = "EntityDefinitions(LogicalName='$table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.MultiSelectPicklistAttributeMetadata?`$select=LogicalName&`$expand=OptionSet(`$select=Options,IsGlobal)"
    $attr = Get-Attr $table $Column
    if ($attr) {
        Assert-AttrType $attr $Column 'Virtual'
        $script:Expect.Add(@{ Table = $table; Column = $Column; Type = 'Virtual' })
        $have = @()
        for ($i = 0; $i -lt 12 -and $have.Count -eq 0; $i++) {
            $pk = Get-DvOrNull $typed
            $have = @($pk.OptionSet.Options | ForEach-Object { $_.Label.UserLocalizedLabel.Label })
            if ($have.Count -eq 0) { Start-Sleep -Seconds 5 }
        }
        if ($have.Count -ne $Labels.Count -or @($have | Where-Object { $_ -notin $Labels }).Count -gt 0) {
            throw [EscalationException]::new("$table.$Column exists with options [$($have -join ', ')] but the spec needs exactly [$($Labels -join ', ')]. Editing or removing options needs an owner decision.")
        }
        $script:Skipped.Add("$table.$Column"); Write-Host "  skipped  $Column (exists, multi-select choice with $($Labels.Count) options)" -ForegroundColor Yellow
        return
    }
    $options = @(); $v = 100000000
    foreach ($l in $Labels) { $options += @{ Value = $v; Label = New-Label $l }; $v++ }
    $def = @{
        '@odata.type'  = 'Microsoft.Dynamics.CRM.MultiSelectPicklistAttributeMetadata'
        SchemaName     = $Schema
        DisplayName    = New-Label $Display
        Description    = New-Label $Description
        RequiredLevel  = @{ Value = 'None' }
        OptionSet      = @{
            '@odata.type'  = 'Microsoft.Dynamics.CRM.OptionSetMetadata'
            IsGlobal       = $false
            OptionSetType  = 'Picklist'
            Options        = $options
        }
    }
    Add-Column -Column $Column -Definition $def -ExpectedType 'Virtual'
}

# Escalation pre-check (read-only, before ANY write): sprk_invoice must not already carry a column named sprk_billingexport
# that is anything but a lookup to sprk_billingexport, and must not already carry a lookup to another export / batch table
# (a second batch link would let an invoice sit in two batches).
function Test-InvoiceBatchLink {
    $conflict = [System.Collections.Generic.List[string]]::new()
    $col = Get-DvOrNull "EntityDefinitions(LogicalName='sprk_invoice')/Attributes(LogicalName='sprk_billingexport')?`$select=LogicalName,AttributeType" -Retries 2
    if ($col) {
        $lk = Get-DvOrNull "EntityDefinitions(LogicalName='sprk_invoice')/Attributes(LogicalName='sprk_billingexport')/Microsoft.Dynamics.CRM.LookupAttributeMetadata?`$select=Targets" -Retries 2
        if ($col.AttributeType -ne 'Lookup' -or -not $lk -or @($lk.Targets) -notcontains 'sprk_billingexport') {
            $conflict.Add("sprk_invoice.sprk_billingexport already exists (type $($col.AttributeType), targets $(if ($lk) { @($lk.Targets) -join ',' } else { 'n/a' })) but is not a lookup to sprk_billingexport")
        } else {
            Write-Host "  ok       sprk_invoice.sprk_billingexport is already the lookup to sprk_billingexport (a re-run)"
        }
    }
    $rels = Get-DvOrNull "EntityDefinitions(LogicalName='sprk_invoice')/ManyToOneRelationships?`$select=SchemaName,ReferencedEntity,ReferencingAttribute"
    foreach ($r in @($rels.value)) {
        if ($r.ReferencedEntity -match '(?i)export|batch' -and $r.ReferencedEntity -ne 'sprk_billingexport') {
            $conflict.Add("sprk_invoice.$($r.ReferencingAttribute) is already a lookup to $($r.ReferencedEntity)")
        }
        if ($r.ReferencedEntity -eq 'sprk_billingexport' -and $r.ReferencingAttribute -ne 'sprk_billingexport') {
            $conflict.Add("sprk_invoice.$($r.ReferencingAttribute) is already a second lookup to sprk_billingexport")
        }
    }
    if ($conflict.Count -gt 0) {
        throw [EscalationException]::new("sprk_invoice already has a batch link: $($conflict -join '; '). A second batch link is not added. Nothing was changed.")
    }
}

# ---------------------------------------------------------------------------------------------------------------
# Run
# ---------------------------------------------------------------------------------------------------------------

Write-Host ""
Write-Host "eBilling task 023 - billing export schema" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Solution: $SolutionUniqueName   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })"
Write-Host ""

try {
    foreach ($ref in 'sprk_invoice', 'sprk_document') {
        if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$ref')?`$select=LogicalName" -Retries 4)) {
            throw "$ref does not exist in $EnvironmentUrl."
        }
    }

    # --- 0. Escalation pre-check (before ANY write) -------------------------------------------------------------------
    Write-Host "0. Pre-check: sprk_invoice has no other batch link" -ForegroundColor Cyan
    Test-InvoiceBatchLink

    # --- 1. Table + columns -------------------------------------------------------------------------------------------
    Write-Host "1. Table $ExportTable" -ForegroundColor Cyan
    Add-Table -Table $ExportTable -Display 'Billing Export' -Plural 'Billing Exports' -Ownership 'UserOwned' `
        -Description 'One AP export batch: the approved invoices handed to AP in one run, with the files produced and the mapping used (FR-13, FR-41).' `
        -PrimaryColumn 'sprk_name' -PrimaryDisplay 'Name' -PrimaryDescription 'The batch id.' -PrimaryLength 100
    Set-PrimaryNameLength $ExportTable 'sprk_name' 100
    Set-PrimaryNameRequired $ExportTable 'sprk_name'
    Add-Column 'sprk_invoicecount' (New-WholeNumberDef 'sprk_InvoiceCount' 'Invoice Count' 'Number of invoices in the batch. Written by the batch writer (task 150).' 0 1000000) 'Integer'
    Add-Column 'sprk_totalamount' (New-MoneyDef 'sprk_TotalAmount' 'Total Amount' 'Sum of the approved totals of the invoices in the batch. Written by the batch writer (task 150).') 'Money'
    Add-LocalMultiChoice -Column 'sprk_formats' -Schema 'sprk_Formats' -Display 'Formats' `
        -Description 'The file formats this batch produced: CSV, JSON, XML (XML is reserved; D-21).' -Labels $FormatLabels
    Add-LocalChoice -Column 'sprk_exportstatus' -Schema 'sprk_ExportStatus' -Display 'Export Status' `
        -Description 'Where the batch stands: Generating, Completed or Failed.' -Labels $StatusLabels
    Add-Column 'sprk_mappingsnapshot' (New-MemoDef 'sprk_MappingSnapshot' 'Mapping Snapshot' 'The field mapping used to produce the files, snapshotted per batch so a past batch can be reproduced after a mapping change (FR-41).' 1048576) 'Memo'
    Add-Column 'sprk_failurereason' (New-StringDef 'sprk_FailureReason' 'Failure Reason' 'Why the batch failed (status Failed).' 200) 'String'

    # --- 2. Lookups ---------------------------------------------------------------------------------------------------
    Write-Host "2. Lookups" -ForegroundColor Cyan
    Add-Lookup -Column 'sprk_csvdocument' -SchemaName 'sprk_CsvDocument' -RelSchema 'sprk_billingexport_csvdocument_document' -Target 'sprk_document' `
        -Display 'CSV Document' -Description 'The Document holding the CSV file of this batch (E-Billing Output Files container).'
    Add-Lookup -Column 'sprk_jsondocument' -SchemaName 'sprk_JsonDocument' -RelSchema 'sprk_billingexport_jsondocument_document' -Target 'sprk_document' `
        -Display 'JSON Document' -Description 'The Document holding the JSON file of this batch (E-Billing Output Files container).'
    Set-EbEntity 'sprk_invoice'
    Add-Lookup -Column 'sprk_billingexport' -SchemaName 'sprk_BillingExport' -RelSchema 'sprk_invoice_billingexport_billingexport' -Target 'sprk_billingexport' `
        -Display 'Export Batch' -Description 'The AP export batch this invoice was sent in. Single-valued, so an invoice is in at most one batch (FR-41).'
    Set-EbEntity $ExportTable

    # --- Publish (only when something changed; entity-scoped, never PublishAllXml) ------------------------------------
    Write-Host "Publish" -ForegroundColor Cyan
    $wrote = ($script:Created.Count + $script:Changed.Count) -gt 0
    if ($DryRun) {
        Write-Host "  WhatIf: publish skipped" -ForegroundColor Cyan
    } elseif (-not $wrote) {
        Write-Host "  nothing changed - no publish" -ForegroundColor Yellow
    } else {
        Invoke-Dv -Path 'PublishXml' -Method POST -Body @{
            ParameterXml = "<importexportxml><entities><entity>$ExportTable</entity><entity>sprk_invoice</entity><entity>sprk_document</entity></entities></importexportxml>"
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
            $base = "EntityDefinitions(LogicalName='$($e.Table)')/Attributes(LogicalName='$($e.Column)')"
            switch ($e.Type) {
                'Lookup' {
                    $lk = $null
                    for ($i = 0; $i -lt 12; $i++) {
                        $lk = Get-DvOrNull "$base/Microsoft.Dynamics.CRM.LookupAttributeMetadata?`$select=Targets"
                        if ($lk -and @($lk.Targets).Count -gt 0) { break }
                        Start-Sleep -Seconds 5
                    }
                    if (-not $lk -or $lk.Targets -notcontains $e.Target) { $bad.Add("$($e.Table).$($e.Column) does not target $($e.Target)"); continue }
                    Write-Host ("  {0}.{1}: lookup -> {2}" -f $e.Table, $e.Column, (@($lk.Targets) -join ','))
                }
                'Integer' {
                    $in = Get-DvOrNull "$base/Microsoft.Dynamics.CRM.IntegerAttributeMetadata?`$select=MinValue,MaxValue" -Retries 4
                    Write-Host ("  {0}: whole number, min {1}, max {2}" -f $e.Column, $in.MinValue, $in.MaxValue)
                    if (-not $in -or [int]$in.MinValue -ne 0) { $bad.Add("$($e.Table).$($e.Column) min is $($in.MinValue), expected 0") }
                }
                'Money' {
                    $mn = Get-DvOrNull "$base/Microsoft.Dynamics.CRM.MoneyAttributeMetadata?`$select=Precision,MinValue" -Retries 4
                    Write-Host ("  {0}: currency, precision {1}, min {2}" -f $e.Column, $mn.Precision, $mn.MinValue)
                }
                'Memo' {
                    $mm = Get-DvOrNull "$base/Microsoft.Dynamics.CRM.MemoAttributeMetadata?`$select=MaxLength" -Retries 4
                    Write-Host ("  {0}: multiline, max length {1}" -f $e.Column, $mm.MaxLength)
                    if (-not $mm -or [int]$mm.MaxLength -ne 1048576) { $bad.Add("$($e.Table).$($e.Column) max length is $($mm.MaxLength), expected 1048576") }
                }
                'String' {
                    $st = Get-DvOrNull "$base/Microsoft.Dynamics.CRM.StringAttributeMetadata?`$select=MaxLength,RequiredLevel" -Retries 4
                    Write-Host ("  {0}: text, max length {1}, required {2}" -f $e.Column, $st.MaxLength, $st.RequiredLevel.Value)
                }
                'Picklist' {
                    $pk = $null
                    for ($i = 0; $i -lt 12; $i++) {
                        $pk = Get-DvOrNull "$base/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=LogicalName&`$expand=OptionSet(`$select=Options,IsGlobal)"
                        if ($pk -and @($pk.OptionSet.Options).Count -gt 0) { break }
                        Start-Sleep -Seconds 5
                    }
                    $have = @($pk.OptionSet.Options)
                    Write-Host ("  {0}: choice (local, global={1}) {2}" -f $e.Column, $pk.OptionSet.IsGlobal, (($have | Sort-Object Value | ForEach-Object { "$($_.Value)=$($_.Label.UserLocalizedLabel.Label)" }) -join ', '))
                    if ($have.Count -ne $StatusLabels.Count -or @($have | Where-Object { $_.Label.UserLocalizedLabel.Label -notin $StatusLabels }).Count -gt 0) { $bad.Add("$($e.Table).$($e.Column) options differ from Generating / Completed / Failed") }
                }
                'Virtual' {
                    $pk = $null
                    for ($i = 0; $i -lt 12; $i++) {
                        $pk = Get-DvOrNull "$base/Microsoft.Dynamics.CRM.MultiSelectPicklistAttributeMetadata?`$select=LogicalName&`$expand=OptionSet(`$select=Options,IsGlobal)"
                        if ($pk -and @($pk.OptionSet.Options).Count -gt 0) { break }
                        Start-Sleep -Seconds 5
                    }
                    $have = @($pk.OptionSet.Options)
                    Write-Host ("  {0}: multi-select choice (local, global={1}) {2}" -f $e.Column, $pk.OptionSet.IsGlobal, (($have | Sort-Object Value | ForEach-Object { "$($_.Value)=$($_.Label.UserLocalizedLabel.Label)" }) -join ', '))
                    if ($have.Count -ne $FormatLabels.Count -or @($have | Where-Object { $_.Label.UserLocalizedLabel.Label -notin $FormatLabels }).Count -gt 0) { $bad.Add("$($e.Table).$($e.Column) options differ from CSV / JSON / XML") }
                }
            }
        }
        $own = Get-DvOrNull "EntityDefinitions(LogicalName='$ExportTable')?`$select=OwnershipType,PrimaryNameAttribute,EntitySetName" -Retries 4
        if (-not $own) { $bad.Add("$ExportTable not readable") }
        else {
            Write-Host ("  {0}: {1}, primary name {2}, entity set {3}" -f $ExportTable, $own.OwnershipType, $own.PrimaryNameAttribute, $own.EntitySetName)
            if ($own.OwnershipType -ne 'UserOwned') { $bad.Add("$ExportTable ownership is $($own.OwnershipType), expected UserOwned") }
        }
        $pn = Get-DvOrNull "EntityDefinitions(LogicalName='$ExportTable')/Attributes(LogicalName='sprk_name')/Microsoft.Dynamics.CRM.StringAttributeMetadata?`$select=MaxLength,RequiredLevel" -Retries 4
        Write-Host ("  sprk_name: text, max length {0}, required {1}" -f $pn.MaxLength, $pn.RequiredLevel.Value)
        if (-not $pn -or [int]$pn.MaxLength -ne 100) { $bad.Add("sprk_name max length is $($pn.MaxLength), expected 100") }
        # Lookup cascade behaviour
        foreach ($c in @(@('sprk_billingexport', 'sprk_csvdocument'), @('sprk_billingexport', 'sprk_jsondocument'), @('sprk_invoice', 'sprk_billingexport'))) {
            $rn = $null
            for ($i = 0; $i -lt 12 -and -not $rn; $i++) {
                $rn = (Get-DvOrNull "EntityDefinitions(LogicalName='$($c[0])')/ManyToOneRelationships?`$select=SchemaName,ReferencingAttribute,CascadeConfiguration&`$filter=ReferencingAttribute eq '$($c[1])'").value
                if (-not $rn) { Start-Sleep -Seconds 5 }
            }
            if (-not $rn) { $bad.Add("no many-to-one relationship for $($c[0]).$($c[1])"); continue }
            $cc = @($rn)[0].CascadeConfiguration
            Write-Host ("  {0}.{1}: {2}  delete {3}, assign {4}, share {5}" -f $c[0], $c[1], @($rn)[0].SchemaName, $cc.Delete, $cc.Assign, $cc.Share)
            if ($cc.Delete -ne 'RemoveLink') { $bad.Add("$($c[1]) delete behaviour is $($cc.Delete), expected RemoveLink") }
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
if ($DryRun) { Write-Host "WhatIf run: nothing was written." -ForegroundColor Cyan }
exit 0
