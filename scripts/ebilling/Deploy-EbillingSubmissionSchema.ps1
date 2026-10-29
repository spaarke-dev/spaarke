<#
.SYNOPSIS
    eBilling R1 task 020 - creates the table sprk_invoicesubmission (one row per submission attempt, FR-10) with its
    columns, lookups, ADR-024 resolver fields and the firm-scoped alternate key (firm, tracking number).

.DESCRIPTION
    Idempotent, additive Dataverse Web API metadata script. Every item is check-then-create; a second run reports every
    item as skipped and makes no write (no publish either). Nothing is ever deleted, retyped or relabelled.

    Why a table of its own: a rejected attempt stored as an sprk_invoice would be counted by Finance rollups and Ontology
    facts. The submission is the record of every attempt; an invoice exists only for an accepted one (FR-10, FR-17, FR-20).

    Items (tasks/020-invoicesubmission-schema.poml, spec FR-10 / FR-05, D-37):
      1  Table sprk_invoicesubmission (user-owned, display "Invoice Submission"), primary name sprk_name (200).
      2  Columns: sprk_trackingnumber (text 100), sprk_firminvoicenumber (text 100), sprk_submissionsequence (whole
         number, min 1), sprk_channel (local choice SPA / Internal / Mailbox / Outlook / API), sprk_processingstatus (local
         choice Received / Processing / Accepted / Rejected / Failed), sprk_outcomereasoncode (text 100), sprk_findingsjson
         (multiline 1048576), sprk_firmnoticetext (multiline 100000), sprk_ponumber (text 100), sprk_receivedon (date and
         time), sprk_correlationid (text 100).
      3  Lookups (delete = RemoveLink, other cascades NoCascade): sprk_submitter -> contact, sprk_vendororg ->
         sprk_organization (the firm), sprk_document -> sprk_document (the PDF), sprk_reportdocument -> sprk_document,
         sprk_invoice -> sprk_invoice (resulting invoice), sprk_resubmissionof -> sprk_invoicesubmission (self),
         sprk_regardingmatter -> sprk_matter, sprk_regardingproject -> sprk_project, sprk_regardingworkassignment ->
         sprk_workassignment, sprk_regardingrecordtype -> sprk_recordtype_ref.
      4  Resolver text fields (ADR-024): sprk_regardingrecordid (100), sprk_regardingrecordname (200),
         sprk_regardingrecordnumber (100), sprk_regardingrecordurl (500, Url).
      5  Alternate key sprk_invoicesubmission_firm_trackingnumber on (sprk_vendororg, sprk_trackingnumber); the script
         waits for the key index to be Active. Task 045 allocates tracking numbers against this key.
         The key columns sprk_vendororg and sprk_trackingnumber are SystemRequired (enforced by the platform on every write).

    At most one entity-specific regarding lookup is populated per row, and the resolver fields are filled together:
    both are enforced server-side by task 013, not by schema (ADR-002: no plugin, business rule, calculated / rollup
    column or flow). Forms and views are task 029's.

    Required levels (DEF-007 / DEF-009): ApplicationRequired is enforced by FORMS only, not by API writes. The primary name
    sprk_name is ApplicationRequired (required by the BFF write path, not enforced on API writes); only the key columns above are
    SystemRequired. Every other lookup is optional.

    If an existing item has the same name but another shape (a column of another type, a lookup to another table, a choice
    with other options, a key on other attributes, a table with another primary name), the script STOPS with exit code 2
    and an ESCALATION message (project CLAUDE.md section 5) and changes nothing further.

    Eventually consistent metadata reads: a 404 right after a create is not trusted (the item is re-read); a create
    refused with "already exists" counts as skipped after a type check on a settled read; a column create issued right
    after its table was created is retried while the table is still propagating (MetadataCache); the primary-name length in
    an entity create body is ignored by Dataverse (850) and is set afterwards while the table is empty; a choice option
    list can read back empty right after its create (re-read). Customization / publish locks (task 017 publishes schema
    in the same environment) and transient faults are retried with backoff.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the created components are added to (header MSCRM.SolutionUniqueName).

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingSubmissionSchema.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingSubmissionSchema.ps1

.NOTES
    Project: ebilling-r1, task 020. Dev only in R1. The Dataverse helpers live in EbillingSchema.psm1 (shared by the
    eBilling schema scripts). Pattern: scripts/ebilling/Deploy-EbillingPolicyStoreSchema.ps1 (task 019,
    new user-owned table) and Deploy-EbillingBillingCodeSchema.ps1 (task 018, alternate key). Resolver field lengths per
    projects/spaarke-ontology-platform-r1/notes/140-schema-delta.md section 2.
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
$SubTable = 'sprk_invoicesubmission'
$KeySchemaName = 'sprk_invoicesubmission_firm_trackingnumber'

# Shared Dataverse helpers (scripts/ebilling/EbillingSchema.psm1). The state lists are shared by reference.
$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$DryRun -Entity $SubTable
$script:Created = $State.Created
$script:Changed = $State.Changed
$script:Skipped = $State.Skipped
$script:Expect = $State.Expect
$script:Planned = $State.Planned
$script:JustCreated = $State.JustCreated

# ---------------------------------------------------------------------------------------------------------------
# Task 020 helpers
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

# ---------------------------------------------------------------------------------------------------------------
# Run
# ---------------------------------------------------------------------------------------------------------------

Write-Host ""
Write-Host "eBilling task 020 - sprk_invoicesubmission schema" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Solution: $SolutionUniqueName   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })"
Write-Host ""


# A typed attribute read can come back empty right after a create (eventual consistency): re-read before judging.
function Get-TypedAttr([string]$Path) {
    $r = $null
    for ($i = 0; $i -lt 12 -and -not $r; $i++) {
        $r = Get-DvOrNull $Path
        if (-not $r) { Start-Sleep -Seconds 5 }
    }
    return $r
}

# Relationships this script owns (referencing column -> target), for the final read-back.
$script:ExpectRel = [System.Collections.Generic.List[object]]::new()
function Add-SubLookup([string]$Column, [string]$SchemaName, [string]$RelSchema, [string]$Target, [string]$Display, [string]$Description) {
    $script:ExpectRel.Add(@{ Column = $Column; Target = $Target })
    Add-Lookup -Column $Column -SchemaName $SchemaName -RelSchema $RelSchema -Target $Target -Display $Display -Description $Description
}

$RegardingNote = 'At most one regarding lookup is set per row (enforced server-side by task 013, not by schema).'

try {
    foreach ($t in 'contact', 'sprk_organization', 'sprk_document', 'sprk_invoice', 'sprk_matter', 'sprk_project', 'sprk_workassignment', 'sprk_recordtype_ref') {
        if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$t')?`$select=LogicalName" -Retries 4)) {
            throw "$t does not exist in $EnvironmentUrl."
        }
    }

    # --- 1. Table -----------------------------------------------------------------------------------------------
    Write-Host "1. Table $SubTable" -ForegroundColor Cyan
    Add-Table -Table $SubTable -Display 'Invoice Submission' -Plural 'Invoice Submissions' -Ownership 'UserOwned' `
        -Description 'One row per invoice submission attempt (FR-10): accepted, rejected or failed. Only an accepted attempt becomes an sprk_invoice; a rejected attempt never does.' `
        -PrimaryColumn 'sprk_name' -PrimaryDisplay 'Name' -PrimaryDescription 'Display name of the submission, normally the tracking number.' -PrimaryLength 200
    Set-PrimaryNameLength $SubTable 'sprk_name' 200
    Set-PrimaryNameRequired $SubTable 'sprk_name'

    # --- 2. Columns ---------------------------------------------------------------------------------------------
    Write-Host "2. Columns" -ForegroundColor Cyan
    Add-Column 'sprk_trackingnumber' (New-StringDef 'sprk_TrackingNumber' 'Tracking Number' 'Tracking number {firm invoice number}.{n}, for example INV-002.2 (D-37). Unique per firm (alternate key). Allocated concurrency-safe by task 045.' 100) 'String'
    Add-Column 'sprk_firminvoicenumber' (New-StringDef 'sprk_FirmInvoiceNumber' 'Firm Invoice Number' 'The invoice number as submitted or, when none was entered, as extracted from the PDF.' 100) 'String'
    Add-Column 'sprk_submissionsequence' (New-WholeNumberDef 'sprk_SubmissionSequence' 'Submission Sequence' 'The n of the tracking number: 1 for the first submission of a firm invoice number, 2 for the next (D-37). Used by task 045.' 1 2147483647) 'Integer'
    Add-LocalChoice -Column 'sprk_channel' -Schema 'sprk_Channel' -Display 'Channel' `
        -Description 'How the invoice arrived. SPA and Internal in R1; Mailbox, Outlook and API are reserved.' `
        -Labels @('SPA', 'Internal', 'Mailbox', 'Outlook', 'API')
    Add-LocalChoice -Column 'sprk_processingstatus' -Schema 'sprk_ProcessingStatus' -Display 'Processing Status' `
        -Description 'Where the submission is in the pipeline: Received, Processing, Accepted, Rejected or Failed (FR-10, FR-20).' `
        -Labels @('Received', 'Processing', 'Accepted', 'Rejected', 'Failed')
    Add-Column 'sprk_outcomereasoncode' (New-StringDef 'sprk_OutcomeReasonCode' 'Outcome Reason Code' 'Stable reason code when the submission is Rejected or Failed (FR-21).' 100) 'String'
    Add-Column 'sprk_findingsjson' (New-MemoDef 'sprk_FindingsJson' 'Findings JSON' 'Findings that caused a rejection (rule, clause, values read), as JSON (FR-10).' 1048576) 'Memo'
    Add-Column 'sprk_firmnoticetext' (New-MemoDef 'sprk_FirmNoticeText' 'Firm Notice Text' 'The notice text sent to the firm for this submission (FR-50).' 100000) 'Memo'
    Add-Column 'sprk_ponumber' (New-StringDef 'sprk_PONumber' 'PO Number' 'Purchase order number as entered at submission (FR-18).' 100) 'String'
    Add-Column 'sprk_receivedon' (New-DateTimeDef 'sprk_ReceivedOn' 'Received On' 'When the submission was received.') 'DateTime'
    Add-Column 'sprk_correlationid' (New-StringDef 'sprk_CorrelationId' 'Correlation Id' 'Correlation id of the request that created the submission, as on sprk_invoice.' 100) 'String'

    # --- 3. Lookups ---------------------------------------------------------------------------------------------
    Write-Host "3. Lookups" -ForegroundColor Cyan
    Add-SubLookup 'sprk_submitter' 'sprk_Submitter' 'sprk_invoicesubmission_submitter_contact' 'contact' 'Submitter' 'The person who submitted the invoice (persons are contacts).'
    Add-SubLookup 'sprk_vendororg' 'sprk_VendorOrg' 'sprk_invoicesubmission_vendororg_organization' 'sprk_organization' 'Firm' 'The firm that sent the invoice. Part of the alternate key with the tracking number.'
    Add-SubLookup 'sprk_document' 'sprk_Document' 'sprk_invoicesubmission_document_document' 'sprk_document' 'Invoice Document' 'The submitted invoice PDF.'
    Add-SubLookup 'sprk_reportdocument' 'sprk_ReportDocument' 'sprk_invoicesubmission_reportdocument_document' 'sprk_document' 'Report Document' 'The adjustment or rejection report PDF generated for this submission.'
    Add-SubLookup 'sprk_invoice' 'sprk_Invoice' 'sprk_invoicesubmission_invoice_invoice' 'sprk_invoice' 'Resulting Invoice' 'The invoice created when the submission was accepted. Empty for a rejected or failed attempt.'
    Add-SubLookup 'sprk_resubmissionof' 'sprk_ResubmissionOf' 'sprk_invoicesubmission_resubmissionof' $SubTable 'Resubmission Of' 'The earlier submission this one resubmits (same firm invoice number, next .n; D-37).'
    Add-SubLookup 'sprk_regardingmatter' 'sprk_RegardingMatter' 'sprk_invoicesubmission_regmatter' 'sprk_matter' 'Regarding Matter' "ADR-024 entity-specific lookup. $RegardingNote"
    Add-SubLookup 'sprk_regardingproject' 'sprk_RegardingProject' 'sprk_invoicesubmission_regproject' 'sprk_project' 'Regarding Project' "ADR-024 entity-specific lookup. $RegardingNote"
    Add-SubLookup 'sprk_regardingworkassignment' 'sprk_RegardingWorkAssignment' 'sprk_invoicesubmission_regworkassignment' 'sprk_workassignment' 'Regarding Work Assignment' "ADR-024 entity-specific lookup. $RegardingNote"
    Add-SubLookup 'sprk_regardingrecordtype' 'sprk_RegardingRecordType' 'sprk_invoicesubmission_regrecordtype_recordtype_ref' 'sprk_recordtype_ref' 'Regarding Record Type' 'ADR-024 resolver field: the record type of the core record the submission is regarding.'
    # ebilling-r1 task 013: the creator-person lookup the BFF stamps on an app-only create (secure-record family).
    Add-SubLookup 'sprk_createdbyperson' 'sprk_CreatedByPerson' 'sprk_systemuser_sprk_invoicesubmission_createdbyperson' 'systemuser' 'Created By (Person)' 'The PERSON who asked for this record. Stamped by the BFF only (field-secured by scripts/Set-ChildRecordCreatorPersonSchema.ps1): createdby is the BFF application user for a record the BFF creates app-only, so this column is what records the person. ebilling-r1 task 013 (secure-record family; unified-access-control-r2 task 146 shape).'

    # --- 4. Resolver text fields (lengths per 140-schema-delta.md section 2) ------------------------------------
    Write-Host "4. Resolver text fields" -ForegroundColor Cyan
    Add-Column 'sprk_regardingrecordid' (New-StringDef 'sprk_RegardingRecordId' 'Regarding Record Id' 'ADR-024 resolver field: id of the core record the submission is regarding.' 100) 'String'
    Add-Column 'sprk_regardingrecordname' (New-StringDef 'sprk_RegardingRecordName' 'Regarding Record Name' 'ADR-024 resolver field: name of that record.' 200) 'String'
    Add-Column 'sprk_regardingrecordnumber' (New-StringDef 'sprk_RegardingRecordNumber' 'Regarding Record Number' 'ADR-024 resolver field: number of that record.' 100) 'String'
    Add-Column 'sprk_regardingrecordurl' (New-StringDef 'sprk_RegardingRecordUrl' 'Regarding Record URL' 'ADR-024 resolver field: URL of that record.' 500 'Url') 'String'

    # --- 4b. Required levels of the alternate key columns (DEF-009) -------------------------------------------------
    # A row without a firm or a tracking number is not covered by the key (two rows could then carry the same tracking number)
    # and D-70 scopes external access by firm, so both are SystemRequired: the platform refuses a null on every write.
    Write-Host "4b. Required levels (alternate key columns)" -ForegroundColor Cyan
    Set-LookupRequiredLevel $SubTable 'sprk_vendororg' 'SystemRequired'
    Set-StringRequiredLevel $SubTable 'sprk_trackingnumber' 'SystemRequired'

    # --- 5. Alternate key ---------------------------------------------------------------------------------------
    Write-Host "5. Alternate key (sprk_vendororg, sprk_trackingnumber)" -ForegroundColor Cyan
    Add-AlternateKey -Table $SubTable -Schema $KeySchemaName -Display 'Firm and Tracking Number' -Attributes @('sprk_vendororg', 'sprk_trackingnumber')

    # --- Publish (only when something changed; entity-scoped, never a tenant-wide publish) ------------------------------
    Write-Host "Publish" -ForegroundColor Cyan
    $wrote = ($script:Created.Count + $script:Changed.Count) -gt 0
    if ($DryRun) {
        Write-Host "  WhatIf: publish skipped" -ForegroundColor Cyan
    } elseif (-not $wrote) {
        Write-Host "  nothing changed - no publish" -ForegroundColor Yellow
    } else {
        Invoke-Dv -Path 'PublishXml' -Method POST -Body @{
            ParameterXml = "<importexportxml><entities><entity>$SubTable</entity></entities></importexportxml>"
        } | Out-Null
        Write-Host "  customizations published" -ForegroundColor Green
    }

    # --- Verification (metadata read of everything this script owns) --------------------------------------------
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
                'Picklist' {
                    # Right after a create the option list can read back empty (eventual consistency): re-read it.
                    $pk = $null
                    for ($i = 0; $i -lt 12; $i++) {
                        $pk = Get-DvOrNull "$base/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=LogicalName&`$expand=OptionSet(`$select=Name,IsGlobal,Options)"
                        if ($pk -and @($pk.OptionSet.Options).Count -gt 0) { break }
                        Start-Sleep -Seconds 5
                    }
                    if (-not $pk -or @($pk.OptionSet.Options).Count -eq 0) { $bad.Add("$($e.Table).$($e.Column) has no readable options"); continue }
                    Write-Host ("  {0}: choice (local) {1}" -f $e.Column, (($pk.OptionSet.Options | ForEach-Object { "$($_.Value)=$($_.Label.UserLocalizedLabel.Label)" }) -join ', '))
                }
                'Lookup' {
                    $lk = $null
                    for ($i = 0; $i -lt 12; $i++) {
                        $lk = Get-DvOrNull "$base/Microsoft.Dynamics.CRM.LookupAttributeMetadata?`$select=Targets"
                        if ($lk -and @($lk.Targets).Count -gt 0) { break }
                        Start-Sleep -Seconds 5
                    }
                    if (-not $lk -or @($lk.Targets).Count -eq 0) { $bad.Add("$($e.Table).$($e.Column) lookup targets not readable"); continue }
                    Write-Host ("  {0}: lookup -> {1}" -f $e.Column, (@($lk.Targets) -join ','))
                }
                'DateTime' {
                    $dt = Get-TypedAttr "$base/Microsoft.Dynamics.CRM.DateTimeAttributeMetadata?`$select=Format,DateTimeBehavior"
                    Write-Host ("  {0}: datetime format {1}, behavior {2}" -f $e.Column, $dt.Format, $dt.DateTimeBehavior.Value)
                }
                'Integer' {
                    $in = Get-TypedAttr "$base/Microsoft.Dynamics.CRM.IntegerAttributeMetadata?`$select=MinValue,MaxValue"
                    Write-Host ("  {0}: whole number, min {1}, max {2}" -f $e.Column, $in.MinValue, $in.MaxValue)
                    if ([int]$in.MinValue -ne 1) { $bad.Add("$($e.Table).$($e.Column) min is $($in.MinValue), expected 1") }
                }
                'String' {
                    $st = Get-TypedAttr "$base/Microsoft.Dynamics.CRM.StringAttributeMetadata?`$select=MaxLength,FormatName"
                    if (-not $st) { $bad.Add("$($e.Table).$($e.Column) string metadata not readable"); continue }
                    Write-Host ("  {0}: text, max length {1}, format {2}" -f $e.Column, $st.MaxLength, $st.FormatName.Value)
                }
                'Memo' {
                    $mm = Get-TypedAttr "$base/Microsoft.Dynamics.CRM.MemoAttributeMetadata?`$select=MaxLength"
                    if (-not $mm) { $bad.Add("$($e.Table).$($e.Column) memo metadata not readable"); continue }
                    Write-Host ("  {0}: multiline, max length {1}" -f $e.Column, $mm.MaxLength)
                }
            }
        }
        $own = Get-DvOrNull "EntityDefinitions(LogicalName='$SubTable')?`$select=OwnershipType,PrimaryNameAttribute,EntitySetName" -Retries 4
        if (-not $own) { $bad.Add("$SubTable not readable") }
        else {
            Write-Host ("  {0}: {1}, primary name {2}, entity set {3}" -f $SubTable, $own.OwnershipType, $own.PrimaryNameAttribute, $own.EntitySetName)
            if ($own.OwnershipType -ne 'UserOwned') { $bad.Add("$SubTable ownership is $($own.OwnershipType), expected UserOwned") }
        }
        # Relationships: ManyToOneRelationships of the table, matched by referencing attribute.
        $rels = $null
        for ($i = 0; $i -lt 12; $i++) {
            $rels = Get-DvOrNull "EntityDefinitions(LogicalName='$SubTable')/ManyToOneRelationships?`$select=SchemaName,ReferencingAttribute,ReferencedEntity,CascadeConfiguration"
            $have = @($rels.value | ForEach-Object { $_.ReferencingAttribute })
            if (@($script:ExpectRel | Where-Object { $_.Column -notin $have }).Count -eq 0) { break }
            Start-Sleep -Seconds 5
        }
        foreach ($x in $script:ExpectRel) {
            $r = $rels.value | Where-Object { $_.ReferencingAttribute -eq $x.Column } | Select-Object -First 1
            if (-not $r) { $bad.Add("relationship for $($x.Column) missing"); continue }
            Write-Host ("  rel {0}: {1} -> {2} (delete {3})" -f $r.SchemaName, $r.ReferencingAttribute, $r.ReferencedEntity, $r.CascadeConfiguration.Delete)
            if ($r.ReferencedEntity -ne $x.Target) { $bad.Add("$($x.Column) relationship targets $($r.ReferencedEntity), expected $($x.Target)") }
            if ($r.CascadeConfiguration.Delete -ne 'RemoveLink') { $bad.Add("$($x.Column) delete cascade is $($r.CascadeConfiguration.Delete), expected RemoveLink") }
        }
        $k = Get-Keys $SubTable | Where-Object { $_.SchemaName -eq $KeySchemaName } | Select-Object -First 1
        if (-not $k) { $bad.Add("key $KeySchemaName missing") }
        elseif ($k.EntityKeyIndexStatus -ne 'Active') { $bad.Add("key $KeySchemaName is $($k.EntityKeyIndexStatus), expected Active") }
        else { Write-Host ("  key {0} on ({1}): {2}" -f $k.SchemaName, (@($k.KeyAttributes) -join ', '), $k.EntityKeyIndexStatus) }
        if ($bad.Count -gt 0) { throw "Verification failed: $($bad -join '; ')" }
        Write-Host "  all $($script:Expect.Count) expected columns present with the right type" -ForegroundColor Green
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
