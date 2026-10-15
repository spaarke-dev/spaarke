<#
.SYNOPSIS
    eBilling R1 task 022 - creates the table sprk_externalidentifier (typed identifiers such as the firm invoice number,
    firm client-matter number, law-department matter number and PO as quoted; FR-12) with its ADR-024 regarding fields
    and the owner-decided alternate key D-68: (issuing organization, identifier type, normalized value, core record id).

.DESCRIPTION
    Idempotent, additive Dataverse Web API metadata script. Every item is check-then-create; a second run reports every
    item as skipped and makes no write (no publish either). Nothing is ever deleted, retyped or relabelled.

    Items (tasks/022-externalidentifier-schema.poml, spec FR-12 / FR-05, D-12, D-68):
      1  Table sprk_externalidentifier (user-owned, "External Identifier"), primary name sprk_name (200), and the columns
         sprk_identifiertype (local choice, eight values: the four R1 types Firm invoice number, Firm client-matter number,
         Law-department matter number, PO as quoted, then the four reserved AP document number, VAT invoice number,
         E-invoice / clearance ID, Peppol message ID), sprk_identifiervalue (text 200, as quoted), sprk_normalizedvalue
         (text 200, the matching value: upper-case, all whitespace removed; the rule is owned by task 044) and
         sprk_issuingsystem (text 200).
      2  Lookups (delete = RemoveLink, other cascades NoCascade): sprk_issuingorganization -> sprk_organization (the firm),
         sprk_regardinginvoice -> sprk_invoice, and the ADR-024 entity-specific lookups sprk_regardingmatter /
         sprk_regardingproject / sprk_regardingworkassignment plus sprk_regardingrecordtype -> sprk_recordtype_ref;
         resolver text sprk_regardingrecordid (100), sprk_regardingrecordname (200), sprk_regardingrecordnumber (100),
         sprk_regardingrecordurl (500, Url).
      3  Alternate key sprk_externalidentifier_firm_type_value_record on (sprk_issuingorganization, sprk_identifiertype,
         sprk_normalizedvalue, sprk_regardingrecordid) - D-68, decided by the owner 2026-10-15. The script waits for the key
         index to be Active. If Dataverse cannot build a key on these columns the script STOPS (exit 2) and does not
         choose another key.

    Key semantics: a row is covered by the key only when all four key columns hold a value (Dataverse does not index a
    row with an empty key column). The writer (task 044) therefore fills the issuing organization and the resolver id
    whenever it writes a firm-quoted identifier. Lookups by (firm, type, value) are filtered queries; one value on two
    core records is legal under the key and is refused by the resolver in 044.

    sprk_identifiertype and sprk_normalizedvalue are raised to SystemRequired: ApplicationRequired is not enforced on API
    writes (DEF-009), SystemRequired is, and an identifier with no type or no matching value cannot be looked up.

    At most one entity-specific regarding lookup is populated per row, and the resolver fields are filled together: both
    are enforced server-side by task 013, not by schema (ADR-002: no plugin, business rule, calculated / rollup column or
    flow). Forms and views are task 029's.

    If an existing item has the same name but another shape (a column of another type, a lookup to another table, a choice
    with other options, a key on other attributes, a table with another primary name), the script STOPS with exit code 2
    and an ESCALATION message (project CLAUDE.md section 5) and changes nothing further.

    Eventually consistent metadata reads: a 404 right after a create is not trusted (the item is re-read); a create
    refused with "already exists" (or 0x8009050d / 0x80072551) counts as skipped after a type check on a settled read; a
    create issued right after a table create is retried while the table propagates (MetadataCache), as is the key create;
    the primary name's length AND required level in an entity-create body are ignored by Dataverse, so both are set
    afterwards while the table has no rows; customization / publish locks (other schema tasks publish in the same
    environment) and transient faults are retried with backoff. Dataverse returns key attributes alphabetically.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the created components are added to (header MSCRM.SolutionUniqueName).

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingExternalIdentifierSchema.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingExternalIdentifierSchema.ps1

.NOTES
    Project: ebilling-r1, task 022. Dev only in R1. The Dataverse helpers live in EbillingSchema.psm1 (shared by the
    eBilling schema scripts; DEF-012). Patterns: Deploy-EbillingApprovalSchema.ps1 (021) and
    Deploy-EbillingSubmissionSchema.ps1 (020, alternate key). Resolver field lengths per
    projects/spaarke-ontology-platform-r1/notes/140-schema-delta.md section 2.
    Exit codes: 0 = done (created / skipped), 1 = error, 2 = escalation (conflicting existing item or key not buildable).
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
$IdTable = 'sprk_externalidentifier'
$KeySchemaName = 'sprk_externalidentifier_firm_type_value_record'
# Closed choice (task 022): the four R1 types first, then the four reserved ones; values sequential from 100000000.
$TypeLabels = @('Firm invoice number', 'Firm client-matter number', 'Law-department matter number', 'PO as quoted',
    'AP document number', 'VAT invoice number', 'E-invoice / clearance ID', 'Peppol message ID')
# D-68 key columns (Dataverse lists them alphabetically; the script compares sorted).
$KeyAttributes = @('sprk_issuingorganization', 'sprk_identifiertype', 'sprk_normalizedvalue', 'sprk_regardingrecordid')

# Shared Dataverse helpers (scripts/ebilling/EbillingSchema.psm1). The state lists are shared by reference.
$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$DryRun -Entity $IdTable
$script:Created = $State.Created
$script:Changed = $State.Changed
$script:Skipped = $State.Skipped
$script:Expect = $State.Expect
$script:Planned = $State.Planned
$script:JustCreated = $State.JustCreated

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
function Add-IdLookup([string]$Column, [string]$SchemaName, [string]$RelSchema, [string]$Target, [string]$Display, [string]$Description) {
    $script:ExpectRel.Add(@{ Column = $Column; Target = $Target })
    Add-Lookup -Column $Column -SchemaName $SchemaName -RelSchema $RelSchema -Target $Target -Display $Display -Description $Description
}

# ---------------------------------------------------------------------------------------------------------------
# Run
# ---------------------------------------------------------------------------------------------------------------

Write-Host ""
Write-Host "eBilling task 022 - sprk_externalidentifier schema" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Solution: $SolutionUniqueName   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })"
Write-Host ""

$RegardingNote = 'At most one regarding lookup is set per row (enforced server-side by task 013, not by schema).'

try {
    foreach ($ref in 'sprk_organization', 'sprk_invoice', 'sprk_matter', 'sprk_project', 'sprk_workassignment', 'sprk_recordtype_ref') {
        if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$ref')?`$select=LogicalName" -Retries 4)) {
            throw "$ref does not exist in $EnvironmentUrl."
        }
    }

    # --- 1. Table + columns -------------------------------------------------------------------------------------------
    Write-Host "1. Table $IdTable" -ForegroundColor Cyan
    Add-Table -Table $IdTable -Display 'External Identifier' -Plural 'External Identifiers' -Ownership 'UserOwned' `
        -Description 'A typed identifier quoted by an issuer for a record: firm invoice number, firm client-matter number, law-department matter number, PO as quoted, and the reserved AP / VAT / e-invoice / Peppol identifiers (FR-12, D-12).' `
        -PrimaryColumn 'sprk_name' -PrimaryDisplay 'Name' -PrimaryDescription 'Display name of the identifier, normally type and value.' -PrimaryLength 200
    Set-PrimaryNameLength $IdTable 'sprk_name' 200
    Set-PrimaryNameRequired $IdTable 'sprk_name'
    Add-LocalChoice -Column 'sprk_identifiertype' -Schema 'sprk_IdentifierType' -Display 'Identifier Type' `
        -Description 'What kind of identifier this is. R1 uses the first four; AP document number, VAT invoice number, E-invoice / clearance ID and Peppol message ID are reserved for later channels.' `
        -Labels $TypeLabels
    Add-Column 'sprk_identifiervalue' (New-StringDef 'sprk_IdentifierValue' 'Identifier Value' 'The identifier exactly as quoted by the issuer.' 200) 'String'
    Add-Column 'sprk_normalizedvalue' (New-StringDef 'sprk_NormalizedValue' 'Normalized Value' 'The matching value: the identifier upper-cased with all whitespace removed. Part of the alternate key. The rule is applied by task 044.' 200) 'String'
    Add-Column 'sprk_issuingsystem' (New-StringDef 'sprk_IssuingSystem' 'Issuing System' 'The system or ledger that issued the identifier, when known (for example the firm billing system or the AP system).' 200) 'String'

    # --- 2. Lookups + resolver fields ---------------------------------------------------------------------------------
    Write-Host "2. Lookups and resolver fields" -ForegroundColor Cyan
    Add-IdLookup 'sprk_issuingorganization' 'sprk_IssuingOrganization' 'sprk_externalidentifier_issuingorg_organization' 'sprk_organization' 'Issuing Organization' 'The organization (the firm) that issued the identifier. Part of the alternate key.'
    Add-IdLookup 'sprk_regardinginvoice' 'sprk_RegardingInvoice' 'sprk_externalidentifier_reginvoice' 'sprk_invoice' 'Regarding Invoice' "ADR-024 entity-specific lookup: the invoice this identifier identifies. $RegardingNote"
    Add-IdLookup 'sprk_regardingmatter' 'sprk_RegardingMatter' 'sprk_externalidentifier_regmatter' 'sprk_matter' 'Regarding Matter' "ADR-024 entity-specific lookup. $RegardingNote"
    Add-IdLookup 'sprk_regardingproject' 'sprk_RegardingProject' 'sprk_externalidentifier_regproject' 'sprk_project' 'Regarding Project' "ADR-024 entity-specific lookup. $RegardingNote"
    Add-IdLookup 'sprk_regardingworkassignment' 'sprk_RegardingWorkAssignment' 'sprk_externalidentifier_regworkassignment' 'sprk_workassignment' 'Regarding Work Assignment' "ADR-024 entity-specific lookup. $RegardingNote"
    Add-IdLookup 'sprk_regardingrecordtype' 'sprk_RegardingRecordType' 'sprk_externalidentifier_regrecordtype_recordtype_ref' 'sprk_recordtype_ref' 'Regarding Record Type' 'ADR-024 resolver field: the record type of the record the identifier is regarding.'
    # ebilling-r1 task 013: the creator-person lookup the BFF stamps on an app-only create (secure-record family).
    Add-IdLookup 'sprk_createdbyperson' 'sprk_CreatedByPerson' 'sprk_systemuser_sprk_externalidentifier_createdbyperson' 'systemuser' 'Created By (Person)' 'The PERSON who asked for this record. Stamped by the BFF only (field-secured by scripts/Set-ChildRecordCreatorPersonSchema.ps1): createdby is the BFF application user for a record the BFF creates app-only, so this column is what records the person. ebilling-r1 task 013 (secure-record family; unified-access-control-r2 task 146 shape).'
    Add-Column 'sprk_regardingrecordid' (New-StringDef 'sprk_RegardingRecordId' 'Regarding Record Id' 'ADR-024 resolver field: id of the record the identifier is regarding. Part of the alternate key.' 100) 'String'
    Add-Column 'sprk_regardingrecordname' (New-StringDef 'sprk_RegardingRecordName' 'Regarding Record Name' 'ADR-024 resolver field: name of that record.' 200) 'String'
    Add-Column 'sprk_regardingrecordnumber' (New-StringDef 'sprk_RegardingRecordNumber' 'Regarding Record Number' 'ADR-024 resolver field: number of that record.' 100) 'String'
    Add-Column 'sprk_regardingrecordurl' (New-StringDef 'sprk_RegardingRecordUrl' 'Regarding Record URL' 'ADR-024 resolver field: URL of that record.' 500 'Url') 'String'

    # --- 3. Required levels (SystemRequired is enforced on API writes; ApplicationRequired is not - DEF-009) ----------
    Write-Host "3. Required levels" -ForegroundColor Cyan
    Set-PicklistRequiredLevel $IdTable 'sprk_identifiertype' 'SystemRequired'
    Set-StringRequiredLevel $IdTable 'sprk_normalizedvalue' 'SystemRequired'

    # --- 4. Alternate key (D-68) --------------------------------------------------------------------------------------
    Write-Host "4. Alternate key D-68 (issuing organization, identifier type, normalized value, regarding record id)" -ForegroundColor Cyan
    # A refusal that is not "exists" or "still propagating" means Dataverse cannot build THIS key: that is an escalation (D-68),
    # never a reason to choose another key.
    try {
        Add-AlternateKey -Table $IdTable -Schema $KeySchemaName -Display 'Firm, Type, Value and Record' -Attributes $KeyAttributes
    } catch [EscalationException] { throw }
    catch {
        if ($_.Exception.Message -match 'HTTP 400|index FAILED') {
            throw [EscalationException]::new("Dataverse could not build the D-68 key $KeySchemaName on ($(($KeyAttributes | Sort-Object) -join ', ')): $($_.Exception.Message). The owner decided this key; do not choose another one - report to the owner.")
        }
        throw
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
            ParameterXml = "<importexportxml><entities><entity>$IdTable</entity><entity>sprk_organization</entity><entity>sprk_invoice</entity><entity>sprk_matter</entity><entity>sprk_project</entity><entity>sprk_workassignment</entity><entity>sprk_recordtype_ref</entity></entities></importexportxml>"
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
                    if (-not $lk -or @($lk.Targets).Count -eq 0) { $bad.Add("$($e.Table).$($e.Column) lookup targets not readable"); continue }
                    if ($lk.Targets -notcontains $e.Target) { $bad.Add("$($e.Table).$($e.Column) does not target $($e.Target)") }
                    Write-Host ("  {0}: lookup -> {1}" -f $e.Column, (@($lk.Targets) -join ','))
                }
                'String' {
                    $st = Get-TypedAttr "$base/Microsoft.Dynamics.CRM.StringAttributeMetadata?`$select=MaxLength,FormatName,RequiredLevel"
                    if (-not $st) { $bad.Add("$($e.Table).$($e.Column) string metadata not readable"); continue }
                    Write-Host ("  {0}: text, max length {1}, format {2}, required {3}" -f $e.Column, $st.MaxLength, $st.FormatName.Value, $st.RequiredLevel.Value)
                    if ($e.Column -eq 'sprk_normalizedvalue' -and $st.RequiredLevel.Value -ne 'SystemRequired') { $bad.Add('sprk_normalizedvalue is not SystemRequired') }
                    if ($e.Column -eq 'sprk_name' -and $st.RequiredLevel.Value -ne 'ApplicationRequired') { $bad.Add('sprk_name is not ApplicationRequired') }
                }
                'Picklist' {
                    $pk = $null; $have = @()
                    for ($i = 0; $i -lt 12 -and $have.Count -eq 0; $i++) {
                        $pk = Get-DvOrNull "$base/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=RequiredLevel&`$expand=OptionSet(`$select=Options,IsGlobal)"
                        $have = @($pk.OptionSet.Options)
                        if ($have.Count -eq 0) { Start-Sleep -Seconds 5 }
                    }
                    if ($have.Count -eq 0) { $bad.Add("$($e.Table).$($e.Column) has no readable options"); continue }
                    Write-Host ("  {0}: choice (local, required {1}) {2}" -f $e.Column, $pk.RequiredLevel.Value, (($have | Sort-Object Value | ForEach-Object { "$($_.Value)=$($_.Label.UserLocalizedLabel.Label)" }) -join ', '))
                    if ($pk.OptionSet.IsGlobal) { $bad.Add("$($e.Column) is a global choice, expected local") }
                    $labels = @($have | Sort-Object Value | ForEach-Object { $_.Label.UserLocalizedLabel.Label })
                    if ($labels.Count -ne $TypeLabels.Count -or (($labels -join '|') -ne ($TypeLabels -join '|'))) { $bad.Add('sprk_identifiertype options differ from the eight agreed values') }
                    if ($pk.RequiredLevel.Value -ne 'SystemRequired') { $bad.Add('sprk_identifiertype is not SystemRequired') }
                }
            }
        }
        $own = Get-DvOrNull "EntityDefinitions(LogicalName='$IdTable')?`$select=OwnershipType,PrimaryNameAttribute,EntitySetName" -Retries 4
        if (-not $own) { $bad.Add("$IdTable not readable") }
        else {
            Write-Host ("  {0}: {1}, primary name {2}, entity set {3}" -f $IdTable, $own.OwnershipType, $own.PrimaryNameAttribute, $own.EntitySetName)
            if ($own.OwnershipType -ne 'UserOwned') { $bad.Add("$IdTable ownership is $($own.OwnershipType), expected UserOwned") }
        }
        # Relationships: ManyToOneRelationships of the table, matched by referencing attribute.
        $rels = $null
        for ($i = 0; $i -lt 12; $i++) {
            $rels = Get-DvOrNull "EntityDefinitions(LogicalName='$IdTable')/ManyToOneRelationships?`$select=SchemaName,ReferencingAttribute,ReferencedEntity,CascadeConfiguration"
            $have = @($rels.value | ForEach-Object { $_.ReferencingAttribute })
            if (@($script:ExpectRel | Where-Object { $_.Column -notin $have }).Count -eq 0) { break }
            Start-Sleep -Seconds 5
        }
        foreach ($x in $script:ExpectRel) {
            $r = $rels.value | Where-Object { $_.ReferencingAttribute -eq $x.Column } | Select-Object -First 1
            if (-not $r) { $bad.Add("relationship for $($x.Column) missing"); continue }
            Write-Host ("  rel {0}: {1} -> {2} (delete {3}, assign {4}, share {5})" -f $r.SchemaName, $r.ReferencingAttribute, $r.ReferencedEntity, $r.CascadeConfiguration.Delete, $r.CascadeConfiguration.Assign, $r.CascadeConfiguration.Share)
            if ($r.ReferencedEntity -ne $x.Target) { $bad.Add("$($x.Column) relationship targets $($r.ReferencedEntity), expected $($x.Target)") }
            if ($r.CascadeConfiguration.Delete -ne 'RemoveLink') { $bad.Add("$($x.Column) delete cascade is $($r.CascadeConfiguration.Delete), expected RemoveLink") }
        }
        $want = (@($KeyAttributes | Sort-Object) -join ',')
        $k = Get-Keys $IdTable | Where-Object { $_.SchemaName -eq $KeySchemaName } | Select-Object -First 1
        if (-not $k) { $bad.Add("key $KeySchemaName missing") }
        elseif ((@($k.KeyAttributes | Sort-Object) -join ',') -ne $want) { $bad.Add("key $KeySchemaName is on ($(@($k.KeyAttributes) -join ', ')), expected ($want)") }
        elseif ($k.EntityKeyIndexStatus -ne 'Active') { $bad.Add("key $KeySchemaName is $($k.EntityKeyIndexStatus), expected Active") }
        else { Write-Host ("  key {0} on ({1}): {2}" -f $k.SchemaName, (@($k.KeyAttributes) -join ', '), $k.EntityKeyIndexStatus) }
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
# ebilling-r1 task 013: sprk_createdbyperson is created here UNSECURED; only the creator-person script secures it and grants the BFF profiles.
Write-Host "NEXT: run scripts/Set-ChildRecordCreatorPersonSchema.ps1 (dry run, -Apply, -Verify) so sprk_createdbyperson is field-secured (only the BFF may write it)." -ForegroundColor DarkYellow
exit 0
