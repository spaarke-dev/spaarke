<#
.SYNOPSIS
    eBilling R1 task 021 - creates the invoice approval table sprk_invoiceapproval (FR-11, FR-05, FR-35) with its status,
    amount, Decision Record link, ADR-024 core-record lookups and the queryable store of eligible approvers.

.DESCRIPTION
    Idempotent, additive Dataverse Web API metadata script. Every item is check-then-create; a second run reports every
    item as skipped and makes no write (no publish either).

    Items (tasks/021-invoiceapproval-schema.poml, spec FR-11 / FR-05 / FR-35):
      1  Table sprk_invoiceapproval (user-owned, "Invoice Approval"), primary name sprk_name (200), and the columns
         sprk_approvalstatus (local choice Pending / Approved / Rejected / Returned, default Pending),
         sprk_approvedamount (money), sprk_eligibleresolvedon and sprk_decidedon (date and time).
      2  Lookups (delete = RemoveLink, everything else NoCascade): sprk_invoice -> sprk_invoice (SystemRequired: the platform refuses a row without it),
         sprk_decisionrecord -> sprk_decisionrecord, sprk_decidedby -> contact, and the ADR-024 entity-specific lookups
         sprk_regardingmatter / sprk_regardingproject / sprk_regardingworkassignment plus sprk_regardingrecordtype ->
         sprk_recordtype_ref; resolver text sprk_regardingrecordid (100), sprk_regardingrecordname (200),
         sprk_regardingrecordnumber (100), sprk_regardingrecordurl (500, Url).
      3  N:N relationship sprk_invoiceapproval_eligibleapprover_contact between sprk_invoiceapproval and contact: the
         eligible approver persons. Task 103 writes it (server side); the "Invoices to review" view (task 029) filters
         through contact.sprk_systemuser for the current user.

    Pre-check (before ANY write, project escalation trigger / D-33): every assignment lookup that holds approvers on
    sprk_matter, sprk_project and sprk_workassignment must target contact, and contact.sprk_systemuser must target
    systemuser. Otherwise the script STOPS (exit 2) before creating the eligible-approver store.

    NEVER deletes or renames anything. An existing item that conflicts (a column of another type, a lookup to another
    table, a choice with other options) stops the script with exit code 2 and an ESCALATION message.

    ADR-002: no plugin, business rule, calculated / rollup column or flow. Eligible approvers are resolved and enforced by
    task 103 on the server; "one populated core lookup per row" is enforced by task 013; the invoice being required is the
    platform's SystemRequired level on sprk_invoice (ApplicationRequired is not enforced on API writes).

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
    pwsh scripts/ebilling/Deploy-EbillingApprovalSchema.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingApprovalSchema.ps1

.NOTES
    Project: ebilling-r1, task 021. Dev only in R1. The Dataverse helpers live in EbillingSchema.psm1 (shared by the
    eBilling schema scripts). Patterns: Deploy-EbillingTimekeeperRateCardSchema.ps1 (017),
    Deploy-EbillingBillingCodeSchema.ps1 (018), Deploy-EbillingSubmissionSchema.ps1 (020, same ADR-024 shape).
    Exit codes: 0 = done (created / skipped), 1 = error, 2 = escalation (conflicting existing item or D-33 pre-check).
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
$ApprovalTable = 'sprk_invoiceapproval'
$NnSchema = 'sprk_invoiceapproval_eligibleapprover_contact'
$StatusLabels = @('Pending', 'Approved', 'Rejected', 'Returned')
$StatusDefault = 100000000   # Pending

# Shared Dataverse helpers (scripts/ebilling/EbillingSchema.psm1). The state lists are shared by reference.
$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$DryRun -Entity $ApprovalTable
$script:Created = $State.Created
$script:Changed = $State.Changed
$script:Skipped = $State.Skipped
$script:Expect = $State.Expect

# ---------------------------------------------------------------------------------------------------------------
# Script-specific helpers
# ---------------------------------------------------------------------------------------------------------------

# D-33 pre-check: every assignment lookup that can hold approvers must target contact (the persons the N:N stores), and
# contact.sprk_systemuser must link a person to a user. Read-only; throws EscalationException before any write.
function Test-ApproverFieldTarget {
    $common = 'sprk_assignedattorney1', 'sprk_assignedattorney2', 'sprk_assignedparalegal1', 'sprk_assignedparalegal2', 'sprk_assignedtointernal', 'sprk_assignedtoexternal'
    $fields = [ordered]@{
        'sprk_matter'         = $common
        'sprk_project'        = $common
        'sprk_workassignment' = $common + 'sprk_assignedto'
    }
    $bad = [System.Collections.Generic.List[string]]::new()
    foreach ($t in $fields.Keys) {
        foreach ($f in $fields[$t]) {
            $lk = Get-DvOrNull "EntityDefinitions(LogicalName='$t')/Attributes(LogicalName='$f')/Microsoft.Dynamics.CRM.LookupAttributeMetadata?`$select=LogicalName,Targets" -Retries 2
            if (-not $lk) { Write-Host "  note     $t.$f does not exist (not every table carries every assignment field)" -ForegroundColor DarkYellow; continue }
            if (@($lk.Targets).Count -ne 1 -or $lk.Targets[0] -ne 'contact') { $bad.Add("$t.$f -> $($lk.Targets -join ',')"); continue }
            Write-Host "  ok       $t.$f -> contact"
        }
    }
    $su = Get-DvOrNull "EntityDefinitions(LogicalName='contact')/Attributes(LogicalName='sprk_systemuser')/Microsoft.Dynamics.CRM.LookupAttributeMetadata?`$select=LogicalName,Targets" -Retries 2
    if (-not $su -or $su.Targets -notcontains 'systemuser') { $bad.Add('contact.sprk_systemuser does not target systemuser (or is missing)') } else { Write-Host "  ok       contact.sprk_systemuser -> systemuser" }
    if ($bad.Count -gt 0) {
        throw [EscalationException]::new("D-33 assignment fields do not all target contact: $($bad -join '; '). The eligible-approver store (N:N to contact) depends on it. Nothing was changed.")
    }
}

# ---------------------------------------------------------------------------------------------------------------
# Run
# ---------------------------------------------------------------------------------------------------------------

Write-Host ""
Write-Host "eBilling task 021 - invoice approval schema" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Solution: $SolutionUniqueName   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })"
Write-Host ""

try {
    foreach ($ref in 'sprk_invoice', 'sprk_decisionrecord', 'contact', 'sprk_matter', 'sprk_project', 'sprk_workassignment', 'sprk_recordtype_ref') {
        if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$ref')?`$select=LogicalName" -Retries 4)) {
            throw "$ref does not exist in $EnvironmentUrl."
        }
    }

    # --- 0. D-33 pre-check (before ANY write) -------------------------------------------------------------------------
    Write-Host "0. Pre-check: approver assignment fields target contact (D-33)" -ForegroundColor Cyan
    Test-ApproverFieldTarget

    # --- 1. Table + columns -------------------------------------------------------------------------------------------
    Write-Host "1. Table $ApprovalTable" -ForegroundColor Cyan
    Add-Table -Table $ApprovalTable -Display 'Invoice Approval' -Plural 'Invoice Approvals' -Ownership 'UserOwned' `
        -Description 'The approval portion of an invoice: one row per invoice and core record (one portion per invoice in R1), with its decision status and the eligible approvers (FR-11).' `
        -PrimaryColumn 'sprk_name' -PrimaryDisplay 'Name' -PrimaryDescription 'Label of the approval, composed by the server (invoice number and core record).' -PrimaryLength 200
    Set-PrimaryNameLength $ApprovalTable 'sprk_name' 200
    Set-PrimaryNameRequired $ApprovalTable 'sprk_name'
    Add-LocalChoice -Column 'sprk_approvalstatus' -Schema 'sprk_ApprovalStatus' -Display 'Approval Status' `
        -Description 'Where the approval stands: Pending (awaiting a decision), Approved, Rejected or Returned (FR-11). Default Pending.' `
        -Labels $StatusLabels -DefaultValue $StatusDefault
    Add-Column 'sprk_approvedamount' (New-MoneyDef 'sprk_ApprovedAmount' 'Approved Amount' 'The approved total for this portion (frozen when approved; FR-35).') 'Money'
    Add-Column 'sprk_eligibleresolvedon' (New-DateTimeDef 'sprk_EligibleResolvedOn' 'Eligible Approvers Resolved On' 'When the eligible approvers were last resolved (at acceptance, then on each re-resolution; D-78).') 'DateTime'
    Add-Column 'sprk_decidedon' (New-DateTimeDef 'sprk_DecidedOn' 'Decided On' 'When the decision was made.') 'DateTime'

    # --- 2. Lookups ---------------------------------------------------------------------------------------------------
    Write-Host "2. Lookups" -ForegroundColor Cyan
    $RegardingNote = 'At most one of the three is populated per row (enforced server-side, task 013).'
    Add-Lookup -Column 'sprk_invoice' -SchemaName 'sprk_Invoice' -RelSchema 'sprk_invoiceapproval_invoice_invoice' -Target 'sprk_invoice' `
        -Display 'Invoice' -Description 'The invoice this approval is for. Required.' -RequiredLevel 'ApplicationRequired'
    Set-LookupRequiredLevel $ApprovalTable 'sprk_invoice' 'SystemRequired'
    Add-Lookup -Column 'sprk_decisionrecord' -SchemaName 'sprk_DecisionRecord' -RelSchema 'sprk_invoiceapproval_decisionrecord_decisionrecord' -Target 'sprk_decisionrecord' `
        -Display 'Decision Record' -Description 'The Decision Record written when the approval was decided.'
    Add-Lookup -Column 'sprk_decidedby' -SchemaName 'sprk_DecidedBy' -RelSchema 'sprk_invoiceapproval_decidedby_contact' -Target 'contact' `
        -Display 'Decided By' -Description 'The approver (a person) who decided, so a worklist can show it without reading the Decision Record.'
    Add-Lookup -Column 'sprk_regardingmatter' -SchemaName 'sprk_RegardingMatter' -RelSchema 'sprk_invoiceapproval_regmatter' -Target 'sprk_matter' `
        -Display 'Regarding Matter' -Description "ADR-024 entity-specific lookup. $RegardingNote"
    Add-Lookup -Column 'sprk_regardingproject' -SchemaName 'sprk_RegardingProject' -RelSchema 'sprk_invoiceapproval_regproject' -Target 'sprk_project' `
        -Display 'Regarding Project' -Description "ADR-024 entity-specific lookup. $RegardingNote"
    Add-Lookup -Column 'sprk_regardingworkassignment' -SchemaName 'sprk_RegardingWorkAssignment' -RelSchema 'sprk_invoiceapproval_regworkassignment' -Target 'sprk_workassignment' `
        -Display 'Regarding Work Assignment' -Description "ADR-024 entity-specific lookup. $RegardingNote"
    Add-Lookup -Column 'sprk_regardingrecordtype' -SchemaName 'sprk_RegardingRecordType' -RelSchema 'sprk_invoiceapproval_regrecordtype_recordtype_ref' -Target 'sprk_recordtype_ref' `
        -Display 'Regarding Record Type' -Description 'ADR-024 resolver field: the record type of the core record the approval is regarding.'
    # ebilling-r1 task 013: the creator-person lookup the BFF stamps on an app-only create (secure-record family).
    Add-Lookup -Column 'sprk_createdbyperson' -SchemaName 'sprk_CreatedByPerson' -RelSchema 'sprk_systemuser_sprk_invoiceapproval_createdbyperson' -Target 'systemuser' -Display 'Created By (Person)' -Description 'The PERSON who asked for this record. Stamped by the BFF only (field-secured by scripts/Set-ChildRecordCreatorPersonSchema.ps1): createdby is the BFF application user for a record the BFF creates app-only, so this column is what records the person. ebilling-r1 task 013 (secure-record family; unified-access-control-r2 task 146 shape).'
    Add-Column 'sprk_regardingrecordid' (New-StringDef 'sprk_RegardingRecordId' 'Regarding Record Id' 'ADR-024 resolver field: id of the core record the approval is regarding.' 100) 'String'
    Add-Column 'sprk_regardingrecordname' (New-StringDef 'sprk_RegardingRecordName' 'Regarding Record Name' 'ADR-024 resolver field: name of that record.' 200) 'String'
    Add-Column 'sprk_regardingrecordnumber' (New-StringDef 'sprk_RegardingRecordNumber' 'Regarding Record Number' 'ADR-024 resolver field: number of that record.' 100) 'String'
    Add-Column 'sprk_regardingrecordurl' (New-StringDef 'sprk_RegardingRecordUrl' 'Regarding Record URL' 'ADR-024 resolver field: URL of that record.' 500 'Url') 'String'

    # --- 3. Eligible approvers (N:N) ----------------------------------------------------------------------------------
    Write-Host "3. Eligible approvers (N:N to contact)" -ForegroundColor Cyan
    Add-ManyToMany -Schema $NnSchema -Entity1 $ApprovalTable -Entity2 'contact' -Label1 'Eligible Approvers' -Label2 'Invoice Approvals (eligible approver)'

    # --- Publish (only when something changed; entity-scoped, never a tenant-wide publish) ------------------------------------
    Write-Host "Publish" -ForegroundColor Cyan
    $wrote = ($script:Created.Count + $script:Changed.Count) -gt 0
    if ($DryRun) {
        Write-Host "  WhatIf: publish skipped" -ForegroundColor Cyan
    } elseif (-not $wrote) {
        Write-Host "  nothing changed - no publish" -ForegroundColor Yellow
    } else {
        Invoke-Dv -Path 'PublishXml' -Method POST -Body @{
            ParameterXml = "<importexportxml><entities><entity>$ApprovalTable</entity><entity>contact</entity><entity>sprk_invoice</entity><entity>sprk_decisionrecord</entity><entity>sprk_matter</entity><entity>sprk_project</entity><entity>sprk_workassignment</entity><entity>sprk_recordtype_ref</entity></entities></importexportxml>"
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
        $own = Get-DvOrNull "EntityDefinitions(LogicalName='$ApprovalTable')?`$select=OwnershipType,PrimaryNameAttribute,EntitySetName" -Retries 4
        if (-not $own) { $bad.Add("$ApprovalTable not readable") }
        else {
            Write-Host ("  {0}: {1}, primary name {2}, entity set {3}" -f $ApprovalTable, $own.OwnershipType, $own.PrimaryNameAttribute, $own.EntitySetName)
            if ($own.OwnershipType -ne 'UserOwned') { $bad.Add("$ApprovalTable ownership is $($own.OwnershipType), expected UserOwned") }
        }
        # sprk_invoice must be SystemRequired (enforced by the platform on every write; ApplicationRequired is not).
        $inv = $null
        for ($i = 0; $i -lt 12; $i++) {
            $inv = Get-DvOrNull "EntityDefinitions(LogicalName='$ApprovalTable')/Attributes(LogicalName='sprk_invoice')?`$select=RequiredLevel"
            if ($inv -and $inv.RequiredLevel.Value -eq 'SystemRequired') { break }
            Start-Sleep -Seconds 5
        }
        Write-Host "  sprk_invoice required level: $($inv.RequiredLevel.Value)"
        if (-not $inv -or $inv.RequiredLevel.Value -ne 'SystemRequired') { $bad.Add('sprk_invoice is not SystemRequired') }
        # Choice values and default.
        $pk = $null; $have = @()
        for ($i = 0; $i -lt 12 -and $have.Count -eq 0; $i++) {
            $pk = Get-DvOrNull "EntityDefinitions(LogicalName='$ApprovalTable')/Attributes(LogicalName='sprk_approvalstatus')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=DefaultFormValue&`$expand=OptionSet(`$select=Options)"
            $have = @($pk.OptionSet.Options)
            if ($have.Count -eq 0) { Start-Sleep -Seconds 5 }
        }
        Write-Host ("  sprk_approvalstatus: " + (($have | Sort-Object Value | ForEach-Object { "$($_.Value)=$($_.Label.UserLocalizedLabel.Label)" }) -join ', ') + "   default: $($pk.DefaultFormValue)")
        if (@($have | Where-Object { $_.Label.UserLocalizedLabel.Label -notin $StatusLabels }).Count -gt 0 -or $have.Count -ne $StatusLabels.Count) { $bad.Add('sprk_approvalstatus options differ from Pending / Approved / Rejected / Returned') }
        if ([int]$pk.DefaultFormValue -ne $StatusDefault) { $bad.Add("sprk_approvalstatus default is $($pk.DefaultFormValue), expected $StatusDefault (Pending)") }
        # N:N
        $nn = Get-ManyToMany $NnSchema
        if (-not $nn) { $bad.Add("N:N $NnSchema missing") }
        else { Write-Host ("  N:N {0}: {1} <-> {2}, intersect {3}" -f $nn.SchemaName, $nn.Entity1LogicalName, $nn.Entity2LogicalName, $nn.IntersectEntityName) }
        # Lookup cascade behaviour
        foreach ($c in 'sprk_invoice', 'sprk_decisionrecord', 'sprk_decidedby', 'sprk_regardingmatter', 'sprk_regardingproject', 'sprk_regardingworkassignment', 'sprk_regardingrecordtype') {
            $rn = (Get-DvOrNull "EntityDefinitions(LogicalName='$ApprovalTable')/ManyToOneRelationships?`$select=SchemaName,ReferencingAttribute,CascadeConfiguration&`$filter=ReferencingAttribute eq '$c'").value
            if (-not $rn) { $bad.Add("no many-to-one relationship for $c"); continue }
            $cc = @($rn)[0].CascadeConfiguration
            Write-Host ("  {0}: {1}  delete {2}, assign {3}, share {4}" -f $c, @($rn)[0].SchemaName, $cc.Delete, $cc.Assign, $cc.Share)
            if ($cc.Delete -ne 'RemoveLink') { $bad.Add("$c delete behaviour is $($cc.Delete), expected RemoveLink") }
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
