<#
.SYNOPSIS
    eBilling R1 task 080 (D-136 / D-137 / D-141 / D-144, ontology D-171 / D-172) - rule set and rate card scope schema:
    core-record lookups on sprk_ruleset, the Record and FirmRecord scope options, the all-business-units flag, the
    business-unit N:N, the explicit scope on sprk_ratecard with its one-time backfill, field security on the rule set's scope
    columns, and the field-secured "Business unit before Secure" snapshot column on the three core tables.

.DESCRIPTION
    Idempotent, additive Dataverse Web API script (dev only until the owner says otherwise). Every item is check-then-create; a
    second run reports every item as skipped, writes no row and does not publish. RUN -WhatIf FIRST: it reads everything,
    prints what it would create, change or write, and writes nothing. The real run needs the owner's approval (D-144, 2026-10-20)
    AND ontology-r1's schema-review PASS of this script and its -WhatIf output. Never run by the agent that wrote it.

    Design: projects/ebilling-r1/notes/rule-set-scope-design.md section 2 (items S1-S4 and S6-S8). S5 (the secure-record role
    row) and S9 (sprk_createdbyperson) are NOT here: tasks 081 and 086 ship them in one change with the BFF code that the shared
    guards (SecureFamilyConsistencyTests, ChildRecordCreatorPersonSchemaAgreementTests, the owner census) pin them to.

    NO DELETE-RESTRICT ANYWHERE (D-137 / ontology D-172, GitHub #2132): "Deleting a matter must never require first deleting its
    rate cards or rule sets, because the user may lack permission." Every lookup this script creates uses RemoveLink (the module
    default); the three existing sprk_ratecard core lookups are only READ (they must read RemoveLink) and never touched. A set
    or card whose record was deleted therefore keeps a cleared lookup and, through the explicit scope (S2, S6), applies to nothing.

    Items:
      S1  sprk_ruleset.sprk_matter / sprk_project / sprk_workassignment: optional lookups to the three core tables, relationships
          sprk_matter_ruleset_matter, sprk_project_ruleset_project, sprk_workassignment_ruleset_workassignment, RemoveLink.
      S2  sprk_ruleset.sprk_scope (LOCAL choice): inserts Record = 100000002 and FirmRecord = 100000003 (labels "Record" and
          "Firm record"; the existing labels are "Client default" 100000000 and "Firm" 100000001, which stay as they are). Refuses
          (exit 2, nothing written) when a value or a label exists with another meaning, or when 100000000 / 100000001 are not the
          expected ones.
      S3  sprk_ruleset.sprk_allbusinessunits: two-option "All business units", default Yes.
      S4  N:N sprk_ruleset_businessunit between sprk_ruleset and businessunit. Pre-check: EntityDefinitions(businessunit)
          CanBeInManyToMany; when it is not true the script STOPS (exit 2) - the intersect-table fallback is the owner's call.
      S6  sprk_ratecard.sprk_scope: new local choice "Applies to": Firm = 100000000, Core record (CoreRecord) = 100000001,
          optional, no default. Then the ONE-TIME BACKFILL (its own section, its own output): only rows whose scope is empty;
          exactly one core lookup -> Core record; none -> Firm; two or more -> left empty and listed. A card with no lookup whose
          audit history shows a core lookup that was set and later cleared (when auditing is on for sprk_ratecard) is marked Core
          record (it then applies to nothing) and listed as an orphaned card instead. Every card marked Firm is listed (name, id,
          firm) for the owner. An inactive card (statecode 1) cannot be updated by Dataverse and is listed as left empty. Never
          overwrites a set value.
      S7  Field security on sprk_ruleset.sprk_scope, sprk_firm, sprk_matter, sprk_project, sprk_workassignment (IsSecured true):
          Read for every user (the reader profile, on every business unit's default team), Create / Update for the BFF
          application user(s) only (the writer profile). The BFF's 409 ruleset.scope_locked (Q9) is otherwise bypassed by a
          model-driven edit.
      S8  sprk_securedfrombusinessunit ("Business unit before Secure"): lookup to businessunit on sprk_matter, sprk_project and
          sprk_workassignment, RemoveLink, optional, IsSecured true, writer profile ONLY (NO reader profile: no user reads or
          writes it), on no form and in no view. Relationships sprk_businessunit_matter_securedfrombusinessunit and the two
          like it. Written by task 087, read by task 082. No other business-unit column is added to the core tables (D-141).

    PROFILES. The script reuses the existing profile pair of unified-access-control-r2 (task 133 / 146:
    "Spaarke BFF-Managed Field Readers" on every business unit's default team, "Spaarke BFF-Managed Field Writers" with the BFF
    application users only) exactly as Set-EbillingFirmReasonFieldSecurity.ps1 and Set-RegardingPairFieldSecurity.ps1 do: it
    adds permission rows for its columns and NEVER adds or removes a member. Preconditions (checked before any write, in every
    mode; a failure is an escalation, exit 2): the reader profile exists once and is on every enabled business unit's default
    team; the writer profile exists once, holds every -BffApplicationIds user, and has no other user or team as a member. An
    ebilling-owned pair instead of uac-r2's is possible: pass other -ReaderProfileName / -WriterProfileName plus -CreateProfiles
    (creates a missing profile, the reader on every default team, the writer with the -BffApplicationIds users). Without
    -CreateProfiles an absent profile stops the script. The choice is settled with uac-r2 in the before-notice (task 080 step 4).

    Securing a column that already holds values (sprk_scope, sprk_firm) hides it from every user without Read until the reader
    grant lands, so each column is secured and granted back to back; a failed grant reverts the column to not secured and
    publishes (as Set-EbillingFirmReasonFieldSecurity.ps1 does).

    SOLUTION. Components are created in -SolutionUniqueName (SpaarkeMaster). sprk_ruleset, sprk_ratecard, sprk_matter, sprk_project
    and sprk_workassignment are whole-table components of SpaarkeMaster on dev (rootcomponentbehavior 0), so their new columns and
    relationships belong to it. SpaarkeMaster SOURCE (forms, views, Entity.xml) is NOT touched: task 079 syncs it after this run.
    No form and no view is created or changed here (079). No plugin, business rule or calculated column (ADR-002).

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the created components are added to (header MSCRM.SolutionUniqueName).

.PARAMETER ReaderProfileName
    Field security profile that reads the S7 columns for every user. Default: uac-r2's "Spaarke BFF-Managed Field Readers".

.PARAMETER WriterProfileName
    Field security profile that creates / updates the S7 and S8 columns. Default: uac-r2's "Spaarke BFF-Managed Field Writers".

.PARAMETER BffApplicationIds
    Application (client) ids of the BFF application users that must be members of the writer profile. Default: the two dev ones
    (managed identity 5967251e-171c-46fe-a6c2-ef843c90309d and app registration 1e40baad-e065-4aea-a8d4-4b7ab273458c).

.PARAMETER CreateProfiles
    Create a profile named by -ReaderProfileName / -WriterProfileName when it does not exist (with its members). Without it an
    absent profile stops the script. Existing profiles are never modified.

.PARAMETER BackfillOnly
    Run only the S6 backfill section (task 079 re-runs it before it deploys the BFF, to catch cards the old import created in
    between). The column must exist.

.PARAMETER SkipBackfill
    Run everything except the S6 backfill.

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingRuleSetScopeSchema.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingRuleSetScopeSchema.ps1
    pwsh scripts/ebilling/Deploy-EbillingRuleSetScopeSchema.ps1 -BackfillOnly -WhatIf

.NOTES
    Project: ebilling-r1, task 080. Dev only in R1. The Dataverse helpers live in EbillingSchema.psm1. Patterns:
    Deploy-EbillingTimekeeperRateCardSchema.ps1 (core lookups), Deploy-EbillingApprovalSchema.ps1 (N:N),
    Deploy-EbillingSubmissionIntegrityOption.ps1 (option insert, refuse relabel), Deploy-EbillingPolicyStoreSchema.ps1 (two-option
    with a default), Set-EbillingFirmReasonFieldSecurity.ps1 / scripts/Set-ChildRecordCreatorPersonSchema.ps1 (profiles, secure +
    grant). Exit codes: 0 = done (created / skipped), 1 = error or a security finding, 2 = escalation (nothing further changed).
#>

using module .\EbillingSchema.psm1

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = $(if ($env:DATAVERSE_URL) { $env:DATAVERSE_URL } else { 'https://spaarkedev1.crm.dynamics.com' }),
    [string]$SolutionUniqueName = 'SpaarkeMaster',
    [string]$ReaderProfileName = 'Spaarke BFF-Managed Field Readers',
    [string]$WriterProfileName = 'Spaarke BFF-Managed Field Writers',
    [string[]]$BffApplicationIds = @('5967251e-171c-46fe-a6c2-ef843c90309d', '1e40baad-e065-4aea-a8d4-4b7ab273458c'),
    [switch]$CreateProfiles,
    [switch]$BackfillOnly,
    [switch]$SkipBackfill
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$DryRun = [bool]$WhatIfPreference
if ($BackfillOnly -and $SkipBackfill) { throw '-BackfillOnly and -SkipBackfill exclude each other.' }
# `pwsh -File` passes a comma list as ONE string: split it.
$BffApplicationIds = @($BffApplicationIds | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })

$RuleSet = 'sprk_ruleset'
$Card = 'sprk_ratecard'
$ScopeColumn = 'sprk_scope'

# S1 / S8 / S6: the three core tables, with the rate-card naming for the S1 relationships.
$CoreLookups = @(
    @{ Column = 'sprk_matter'; Schema = 'sprk_Matter'; Table = 'sprk_matter'; Display = 'Matter'; Rel = 'sprk_matter_ruleset_matter'; CardRel = 'sprk_matter_ratecard_matter'; SecuredRel = 'sprk_businessunit_matter_securedfrombusinessunit' }
    @{ Column = 'sprk_project'; Schema = 'sprk_Project'; Table = 'sprk_project'; Display = 'Project'; Rel = 'sprk_project_ruleset_project'; CardRel = 'sprk_project_ratecard_project'; SecuredRel = 'sprk_businessunit_project_securedfrombusinessunit' }
    @{ Column = 'sprk_workassignment'; Schema = 'sprk_WorkAssignment'; Table = 'sprk_workassignment'; Display = 'Work Assignment'; Rel = 'sprk_workassignment_ruleset_workassignment'; CardRel = 'sprk_workassignment_ratecard_workassignment'; SecuredRel = 'sprk_businessunit_workassignment_securedfrombusinessunit' }
)
$CoreTables = @($CoreLookups | ForEach-Object { $_.Table })

# S2: the existing options (verified, never changed) and the two inserted.
# (Generic SortedDictionary[int,string], not [ordered]@{}: an OrderedDictionary's integer indexer is POSITIONAL.)
$ScopeKnown = [System.Collections.Generic.SortedDictionary[int, string]]::new()
$ScopeKnown.Add(100000000, 'Client default'); $ScopeKnown.Add(100000001, 'Firm')
$ScopeNew = [System.Collections.Generic.SortedDictionary[int, string]]::new()
$ScopeNew.Add(100000002, 'Record'); $ScopeNew.Add(100000003, 'Firm record')

# S4
$NnSchema = 'sprk_ruleset_businessunit'

# S6
$CardFirm = 100000000
$CardCoreRecord = 100000001
$CardScopeLabels = @('Firm', 'Core record')

# S8
$SnapshotColumn = 'sprk_securedfrombusinessunit'

# Columns that become field-secured. ReaderToo = the reader profile gets Read (S7); S8 has no reader.
$SecuredColumns = @(
    @{ Table = $RuleSet; Column = 'sprk_scope'; Kind = 'Picklist'; ReaderToo = $true }
    @{ Table = $RuleSet; Column = 'sprk_firm'; Kind = 'Lookup'; ReaderToo = $true }
    @{ Table = $RuleSet; Column = 'sprk_matter'; Kind = 'Lookup'; ReaderToo = $true }
    @{ Table = $RuleSet; Column = 'sprk_project'; Kind = 'Lookup'; ReaderToo = $true }
    @{ Table = $RuleSet; Column = 'sprk_workassignment'; Kind = 'Lookup'; ReaderToo = $true }
    @{ Table = 'sprk_matter'; Column = $SnapshotColumn; Kind = 'Lookup'; ReaderToo = $false }
    @{ Table = 'sprk_project'; Column = $SnapshotColumn; Kind = 'Lookup'; ReaderToo = $false }
    @{ Table = 'sprk_workassignment'; Column = $SnapshotColumn; Kind = 'Lookup'; ReaderToo = $false }
)

# Shared Dataverse helpers (scripts/ebilling/EbillingSchema.psm1). The state lists are shared by reference.
$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$DryRun -Entity $RuleSet
$script:Created = $State.Created
$script:Changed = $State.Changed
$script:Skipped = $State.Skipped
$script:Expect = $State.Expect
$script:Planned = $State.Planned
$SolutionHeader = $State.SolutionHeader
$Gaps = [System.Collections.Generic.List[string]]::new()
$script:Wrote = $false

# ---------------------------------------------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------------------------------------------

function New-TwoOptionDef([string]$Schema, [string]$Display, [string]$Description, [bool]$Default) {
    return @{
        '@odata.type' = 'Microsoft.Dynamics.CRM.BooleanAttributeMetadata'
        SchemaName    = $Schema
        DisplayName   = New-Label $Display
        Description   = New-Label $Description
        RequiredLevel = @{ Value = 'None' }
        DefaultValue  = $Default
        OptionSet     = @{
            '@odata.type' = 'Microsoft.Dynamics.CRM.BooleanOptionSetMetadata'
            OptionSetType = 'Boolean'
            TrueOption    = @{ Value = 1; Label = (New-Label 'Yes') }
            FalseOption   = @{ Value = 0; Label = (New-Label 'No') }
        }
    }
}

# Case and space blind comparison of option labels: "FirmRecord" and "Firm record" are the same meaning.
function ConvertTo-LabelKey([string]$Label) { return (($Label -replace '\s', '').ToLowerInvariant()) }

function Get-ScopeOptions {
    $pk = Get-DvOrNull "EntityDefinitions(LogicalName='$RuleSet')/Attributes(LogicalName='$ScopeColumn')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=LogicalName&`$expand=OptionSet(`$select=Options,IsGlobal,Name)" -Retries 2
    if (-not $pk) { throw [EscalationException]::new("$RuleSet.$ScopeColumn does not exist or is not a choice. Task 019 should have created it. Owner decision needed.") }
    if ($pk.OptionSet.IsGlobal) { throw [EscalationException]::new("$RuleSet.$ScopeColumn is bound to a GLOBAL choice ($($pk.OptionSet.Name)); expected a local choice. Owner decision needed.") }
    return @($pk.OptionSet.Options | Sort-Object Value | ForEach-Object { [pscustomobject]@{ Value = [int]$_.Value; Label = [string]$_.Label.UserLocalizedLabel.Label } })
}

function Format-Options($options) { return (($options | ForEach-Object { "$($_.Value)=$($_.Label)" }) -join ', ') }

# The many-to-one relationship of a lookup column, found by the column (so a lookup made under another relationship name is
# still checked). $null when the column has none.
function Get-LookupRelationship([string]$Table, [string]$Column) {
    $r = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/ManyToOneRelationships?`$select=SchemaName,ReferencedEntity,CascadeConfiguration&`$filter=ReferencingAttribute eq '$Column'" -Retries 2
    return @($r.value) | Select-Object -First 1
}

# RemoveLink is the only delete behaviour this project allows on these lookups (D-172). A lookup that exists with another one
# is not changed by script (that is the owner's call): the script stops.
function Assert-RemoveLink([string]$Table, [string]$Column, [string]$Why) {
    $rel = Get-LookupRelationship $Table $Column
    if (-not $rel) { return }
    $d = [string]$rel.CascadeConfiguration.Delete
    if ($d -ne 'RemoveLink') {
        throw [EscalationException]::new("$Table.$Column ($($rel.SchemaName)) has delete behaviour '$d', expected RemoveLink ($Why). Changing it needs an owner decision; not changed.")
    }
}

function Write-Report([string]$StateText, [string]$What) {
    $color = switch ($StateText) { 'OK' { 'Green' } 'MISSING' { 'Yellow' } 'WOULD' { 'Cyan' } 'DONE' { 'Green' } 'INFO' { 'Gray' } default { 'Red' } }
    Write-Host ("  {0,-8} {1}" -f $StateText, $What) -ForegroundColor $color
    if ($StateText -in 'MISSING', 'FAIL') { $Gaps.Add($What) }
    if ($StateText -eq 'DONE') { $script:Wrote = $true }
}

# ---------------------------------------------------------------------------------------------------------------
# S6 backfill
# ---------------------------------------------------------------------------------------------------------------

# True when the card's audit history shows any of the three core lookups with a value (set, later cleared). The audit row's
# changedata is JSON {"changedAttributes":[{"logicalName":..,"oldValue":..,"newValue":..}]}.
function Test-CoreLookupEverSet([string]$CardId) {
    $r = Invoke-Dv "audits?`$select=auditid,operation,changedata&`$filter=_objectid_value eq $CardId"
    foreach ($a in @($r.value)) {
        if (-not $a.changedata) { continue }
        try { $cd = $a.changedata | ConvertFrom-Json -ErrorAction Stop } catch { continue }
        foreach ($ch in @($cd.changedAttributes)) {
            if ($ch.logicalName -in ($CoreLookups | ForEach-Object { $_.Column })) {
                if ($ch.oldValue -or $ch.newValue) { return $true }
            }
        }
    }
    return $false
}

function Invoke-RateCardBackfill {
    Write-Host ""
    Write-Host "S6 backfill: $Card.$ScopeColumn on existing rate cards (only rows whose scope is empty; never overwrites a set value)" -ForegroundColor Cyan
    $scopeAttr = Get-Attr $Card $ScopeColumn
    $columnExists = [bool]$scopeAttr
    if (-not $columnExists -and -not $DryRun) { throw "$Card.$ScopeColumn does not exist; run the schema part first (without -BackfillOnly)." }
    if (-not $columnExists) { Write-Host "  note: $Card.$ScopeColumn does not exist yet (WhatIf): every card is treated as having an empty scope." -ForegroundColor DarkYellow }

    # Auditing on for sprk_ratecard? (entity AND at least one of the three core lookups; otherwise the history cannot show a cleared lookup)
    $ent = Get-DvOrNull "EntityDefinitions(LogicalName='$Card')?`$select=IsAuditEnabled" -Retries 2
    $auditOn = [bool]($ent -and $ent.IsAuditEnabled.Value)
    if ($auditOn) {
        $auditedLookups = @($CoreLookups | Where-Object { $a = Get-Attr $Card $_.Column 'LogicalName,IsAuditEnabled'; $a -and $a.IsAuditEnabled.Value })
        $auditOn = $auditedLookups.Count -gt 0
    }
    if ($auditOn) { Write-Host "  auditing is ON for $Card and its core lookups: a card with no lookup is checked against its audit history before it is marked Firm." }
    else { Write-Host "  auditing is OFF for $Card (or its core lookups): the orphaned-card check CANNOT run; every card with no core lookup is marked Firm and listed for the owner." -ForegroundColor DarkYellow }

    $select = 'sprk_ratecardid,sprk_name,_sprk_firm_value,_sprk_matter_value,_sprk_project_value,_sprk_workassignment_value,statecode,statuscode,createdon'
    if ($columnExists) { $select += ",$ScopeColumn" }
    $ann = @{ Prefer = 'odata.include-annotations="OData.Community.Display.V1.FormattedValue"' }
    $rows = [System.Collections.Generic.List[object]]::new()
    $next = "sprk_ratecards?`$select=$select"
    while ($next) {
        $page = Invoke-Dv -Path $next -ExtraHeaders $ann
        foreach ($v in @($page.value)) { $rows.Add($v) }
        $next = $page.'@odata.nextLink'
    }
    Write-Host "  rate cards read: $($rows.Count)"

    $toFirm = [System.Collections.Generic.List[object]]::new()
    $toCore = [System.Collections.Generic.List[object]]::new()
    $orphans = [System.Collections.Generic.List[object]]::new()
    $leftEmpty = [System.Collections.Generic.List[object]]::new()
    $alreadySet = 0
    foreach ($r in $rows) {
        $current = if ($columnExists) { $r.$ScopeColumn } else { $null }
        if ($null -ne $current) { $alreadySet++; continue }
        $n = @($r._sprk_matter_value, $r._sprk_project_value, $r._sprk_workassignment_value | Where-Object { $_ }).Count
        $firm = $r.'_sprk_firm_value@OData.Community.Display.V1.FormattedValue'
        $item = [pscustomobject]@{ Id = $r.sprk_ratecardid; Name = $r.sprk_name; Firm = $firm; FirmId = $r._sprk_firm_value; Lookups = $n; Status = $r.'statuscode@OData.Community.Display.V1.FormattedValue'; Inactive = ([int]$r.statecode -ne 0); Reason = '' }
        if ($n -ge 2) { $item.Reason = 'two or more core lookups (the resolver already ignores it; the owner picks the record)'; $leftEmpty.Add($item); continue }
        if ($item.Inactive) { $item.Reason = 'inactive (Dataverse does not update an inactive row; the resolver only uses Published cards)'; $leftEmpty.Add($item); continue }
        if ($n -eq 1) { $toCore.Add($item); continue }
        if ($auditOn -and (Test-CoreLookupEverSet $r.sprk_ratecardid)) { $item.Reason = 'audit history shows a core lookup that was set and later cleared'; $orphans.Add($item); continue }
        $toFirm.Add($item)
    }

    $mode = if ($DryRun) { 'WOULD MARK' } else { 'MARKED' }
    foreach ($group in @(@{ L = $toCore; V = $CardCoreRecord; T = 'Core record' }, @{ L = $orphans; V = $CardCoreRecord; T = 'Core record (orphaned)' }, @{ L = $toFirm; V = $CardFirm; T = 'Firm' })) {
        foreach ($i in $group.L) {
            if ($DryRun) { continue }
            Invoke-Dv -Path "sprk_ratecards($($i.Id))" -Method PATCH -Body @{ $ScopeColumn = $group.V } -ExtraHeaders @{ 'If-Match' = '*' } | Out-Null
            $script:Wrote = $true
        }
    }

    Write-Host ("  already set (left alone): {0}" -f $alreadySet)
    Write-Host ("  $mode Core record (exactly one core lookup): {0}" -f $toCore.Count)
    foreach ($i in $toCore) { Write-Host ("      {0} | {1} | firm: {2}" -f $i.Name, $i.Id, $i.Firm) }
    Write-Host ("  $mode Core record, ORPHANED (no lookup now; audit history shows one was cleared): {0}" -f $orphans.Count) -ForegroundColor Magenta
    foreach ($i in $orphans) { Write-Host ("      {0} | {1} | firm: {2}" -f $i.Name, $i.Id, $i.Firm) -ForegroundColor Magenta }
    Write-Host ("  $mode Firm (no core lookup) - THE OWNER CHECKS THIS LIST BEFORE TASK 085: {0}" -f $toFirm.Count) -ForegroundColor Magenta
    foreach ($i in $toFirm) { Write-Host ("      {0} | {1} | firm: {2}" -f $i.Name, $i.Id, $i.Firm) -ForegroundColor Magenta }
    Write-Host ("  LEFT EMPTY and listed (not set): {0}" -f $leftEmpty.Count) -ForegroundColor Yellow
    foreach ($i in $leftEmpty) { Write-Host ("      {0} | {1} | firm: {2} | {3}" -f $i.Name, $i.Id, $i.Firm, $i.Reason) -ForegroundColor Yellow }

    $marked = $toCore.Count + $orphans.Count + $toFirm.Count
    if ($marked -gt 0) { $script:Changed.Add("$Card.$ScopeColumn backfill (Firm $($toFirm.Count), Core record $($toCore.Count + $orphans.Count) of which orphaned $($orphans.Count), left empty $($leftEmpty.Count))") }
    elseif ($rows.Count -eq 0) { $script:Skipped.Add("$Card.$ScopeColumn backfill (no rate cards)") }
    else { $script:Skipped.Add("$Card.$ScopeColumn backfill (nothing to mark: $alreadySet set, $($leftEmpty.Count) left empty)") }

    if (-not $DryRun -and $marked -gt 0) {
        # Read back: no row that was marked is still empty, and no row set before the run changed (counted above, never written).
        $still = Get-RowCountExact 'sprk_ratecards' 'sprk_ratecardid' "$ScopeColumn eq null and statecode eq 0"
        $expectedEmpty = @($leftEmpty | Where-Object { -not $_.Inactive }).Count
        Write-Host "  read-back: active rate cards still without a scope: $still (expected $expectedEmpty)"
        if ($null -ne $still -and $still -ne $expectedEmpty) {
            # Not an error: the old rate card import (before task 085) can create a card with no scope while this runs; re-run the section.
            Write-Host "  WARNING: $still active card(s) have an empty scope, expected $expectedEmpty. A card created meanwhile by the old import? Re-run with -BackfillOnly." -ForegroundColor DarkYellow
        }
    }
}

# ---------------------------------------------------------------------------------------------------------------
# Field security (S7, S8)
# ---------------------------------------------------------------------------------------------------------------

$script:ReaderId = $null
$script:WriterId = $null
$script:ProfilesToCreate = [System.Collections.Generic.List[string]]::new()

function Get-ProfileByName([string]$Name) {
    $safe = $Name.Replace("'", "''")
    return @((Invoke-Dv "fieldsecurityprofiles?`$select=fieldsecurityprofileid,name&`$filter=name eq '$safe'").value)
}

# Preconditions of the profile pair. A failure stops the script (escalation) in every mode: nothing is written.
function Test-ProfilePreconditions {
    $problems = [System.Collections.Generic.List[string]]::new()
    $defaultTeams = @((Invoke-Dv "teams?`$select=teamid,name&`$filter=isdefault eq true&`$expand=businessunitid(`$select=name,isdisabled)").value |
            Where-Object { -not $_.businessunitid.isdisabled })
    if ($defaultTeams.Count -eq 0) { $problems.Add('no business-unit default team was read - the query is wrong') }

    $readers = Get-ProfileByName $ReaderProfileName
    if ($readers.Count -gt 1) { $problems.Add("$($readers.Count) profiles are named '$ReaderProfileName' - exactly one is allowed") }
    elseif ($readers.Count -eq 1) {
        $script:ReaderId = $readers[0].fieldsecurityprofileid
        $onReader = @((Invoke-Dv "fieldsecurityprofiles($($script:ReaderId))/teamprofiles_association?`$select=teamid,name").value | ForEach-Object { $_.teamid.ToString().ToLowerInvariant() })
        foreach ($t in $defaultTeams) {
            if ($onReader -contains $t.teamid.ToString().ToLowerInvariant()) { Write-Report 'OK' "reader profile '$ReaderProfileName' is on default team '$($t.name)' (BU '$($t.businessunitid.name)')" }
            else { $problems.Add("default team '$($t.name)' (BU '$($t.businessunitid.name)') is NOT on '$ReaderProfileName': its users would read the S7 columns EMPTY. Adding a team is a team change (the owner's / uac-r2's); this script never adds a member to an existing profile") }
        }
    }
    elseif ($CreateProfiles) { $script:ProfilesToCreate.Add($ReaderProfileName); Write-Report 'WOULD' "create reader profile '$ReaderProfileName' and add every business unit default team ($($defaultTeams.Count))" }
    else { $problems.Add("reader profile '$ReaderProfileName' does not exist (uac-r2 creates it: scripts/Set-RecordCreatorPersonSchema.ps1). Pass -CreateProfiles to create it here") }

    $bffUsers = @()
    foreach ($appId in $BffApplicationIds) {
        $u = @((Invoke-Dv "systemusers?`$select=systemuserid,fullname&`$filter=applicationid eq $appId").value) | Select-Object -First 1
        if ($u) { $bffUsers += $u } else { $problems.Add("no application user for appId $appId in this environment") }
    }
    if ($BffApplicationIds.Count -eq 0) { $problems.Add('-BffApplicationIds is empty: the writer profile must name the BFF application user(s)') }

    $writers = Get-ProfileByName $WriterProfileName
    if ($writers.Count -gt 1) { $problems.Add("$($writers.Count) profiles are named '$WriterProfileName' - exactly one is allowed") }
    elseif ($writers.Count -eq 1) {
        $script:WriterId = $writers[0].fieldsecurityprofileid
        $members = @((Invoke-Dv "fieldsecurityprofiles($($script:WriterId))/systemuserprofiles_association?`$select=systemuserid,fullname,applicationid").value)
        $allowed = @($bffUsers | ForEach-Object { $_.systemuserid.ToString().ToLowerInvariant() })
        foreach ($u in $bffUsers) {
            if ($members | Where-Object { $_.systemuserid.ToString().ToLowerInvariant() -eq $u.systemuserid.ToString().ToLowerInvariant() }) { Write-Report 'OK' "writer profile '$WriterProfileName' holds BFF application user '$($u.fullname)'" }
            else { $problems.Add("BFF application user '$($u.fullname)' is NOT a member of '$WriterProfileName': the BFF could not write the S7 / S8 columns after they are secured") }
        }
        foreach ($m in $members | Where-Object { $_.systemuserid.ToString().ToLowerInvariant() -notin $allowed }) {
            $kind = if ($m.applicationid) { "an application user (appId $($m.applicationid)) not in -BffApplicationIds" } else { 'a HUMAN user' }
            $problems.Add("'$WriterProfileName' has member '$($m.fullname)', $kind - the profile's membership IS the lock; only the BFF may write these columns")
        }
        $teams = @((Invoke-Dv "fieldsecurityprofiles($($script:WriterId))/teamprofiles_association?`$select=teamid,name").value)
        foreach ($tm in $teams) { $problems.Add("'$WriterProfileName' has TEAM '$($tm.name)' as a member - every member of it could write these columns") }
    }
    elseif ($CreateProfiles) { $script:ProfilesToCreate.Add($WriterProfileName); Write-Report 'WOULD' "create writer profile '$WriterProfileName' with BFF application user(s) $(($bffUsers | ForEach-Object { $_.fullname }) -join ', ')" }
    else { $problems.Add("writer profile '$WriterProfileName' does not exist (uac-r2 creates it). Pass -CreateProfiles to create it here") }

    if ($problems.Count -gt 0) {
        throw [EscalationException]::new("Field security profile preconditions failed (nothing was written):`n    - " + ($problems -join "`n    - "))
    }
    return @{ BffUsers = $bffUsers; DefaultTeams = $defaultTeams }
}

function New-ProfilesIfNeeded($pre) {
    foreach ($name in $script:ProfilesToCreate) {
        if ($DryRun) { continue }
        $isReader = $name -eq $ReaderProfileName
        $desc = if ($isReader) { 'Read on the rule set scope columns (ebilling-r1 task 080). Associated with EVERY business unit default team, so every user keeps reading them: a secured column is hidden from anyone without Read.' }
        else { 'Read/Create/Update on the rule set scope columns and the Secure business-unit snapshot columns (ebilling-r1 task 080). Members: the BFF application user(s) ONLY.' }
        $created = Invoke-Dv -Path 'fieldsecurityprofiles' -Method POST -Body @{ name = $name; description = $desc } -ExtraHeaders (@{ Prefer = 'return=representation' } + $SolutionHeader)
        $id = $created.fieldsecurityprofileid
        if ($isReader) {
            $script:ReaderId = $id
            foreach ($t in $pre.DefaultTeams) { Invoke-Dv -Path "fieldsecurityprofiles($id)/teamprofiles_association/`$ref" -Method POST -Body @{ '@odata.id' = "$($State.ApiBase)/teams($($t.teamid))" } | Out-Null }
        }
        else {
            $script:WriterId = $id
            foreach ($u in $pre.BffUsers) { Invoke-Dv -Path "fieldsecurityprofiles($id)/systemuserprofiles_association/`$ref" -Method POST -Body @{ '@odata.id' = "$($State.ApiBase)/systemusers($($u.systemuserid))" } | Out-Null }
        }
        $script:Created.Add("field security profile '$name'")
        Write-Report 'DONE' "created profile '$name'"
    }
}

function Get-FieldPermission([string]$ProfileId, [string]$Table, [string]$Column) {
    return @((Invoke-Dv "fieldpermissions?`$select=fieldpermissionid,canread,cancreate,canupdate&`$filter=_fieldsecurityprofileid_value eq $ProfileId and entityname eq '$Table' and attributelogicalname eq '$Column'").value) | Select-Object -First 1
}

function Grant-FieldPermission([string]$ProfileId, [string]$Table, [string]$Column, [int]$Write) {
    # Securing propagates asynchronously: a grant right after it can be refused 0x8004f508 "... is NOT secured". Retry that one error.
    for ($attempt = 1; ; $attempt++) {
        try {
            Invoke-Dv -Path 'fieldpermissions' -Method POST -Body @{
                entityname = $Table; attributelogicalname = $Column
                canread = 4; cancreate = $Write; canupdate = $Write
                'fieldsecurityprofileid@odata.bind' = "/fieldsecurityprofiles($ProfileId)"
            } | Out-Null
            return
        }
        catch {
            if ("$($_.Exception.Message)" -notmatch '0x8004f508' -or $attempt -ge 12) { throw }
            Write-Host "    $Table.$Column not yet seen as secured by the field-permission service; retrying ($attempt/12)..."
            Start-Sleep -Seconds 5
        }
    }
}

function Set-ColumnSecured([string]$Table, [string]$Column, [string]$Kind, [bool]$Value) {
    $path = "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.${Kind}AttributeMetadata"
    $a = Get-DvOrNull $path -Retries 4
    if (-not $a) { throw "$Table.$Column was not found ($Kind)." }
    $a.IsSecured = $Value
    try {
        Invoke-WhilePropagating { Invoke-Dv -Path $path -Method PUT -Body $a -ExtraHeaders @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null }
    }
    catch {
        # The typed GET-then-PUT is proven live on lookup and string columns (and Set-PicklistRequiredLevel on a choice). A choice's
        # definition carries its OptionSet as a navigation property the plain GET omits and the PUT may need it: retry ONCE with the
        # OptionSet expanded. A refused PUT leaves IsSecured unchanged, so nothing is masked. Any other kind, or a second refusal, throws.
        if ($Kind -ne 'Picklist') { throw }
        Write-Host "    PUT of $Table.$Column refused without its OptionSet ($($_.Exception.Message)); retrying once with `$expand=OptionSet..." -ForegroundColor DarkYellow
        $a = Get-DvOrNull "${path}?`$expand=OptionSet" -Retries 2
        $a.IsSecured = $Value
        Invoke-WhilePropagating { Invoke-Dv -Path $path -Method PUT -Body $a -ExtraHeaders @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null }
    }
}

function Write-MaskedRecovery([string]$Table, [string]$Column, [string]$Cause) {
    Write-Host ''
    Write-Host "  !!! RECOVERY REQUIRED NOW - $Table.$Column may be field-secured WITHOUT its grants !!!" -ForegroundColor Red
    Write-Host "  Cause: $Cause" -ForegroundColor Red
    Write-Host '  Either fix the cause and re-run this script (on an already-secured column it grants only what is missing), or un-secure the' -ForegroundColor Red
    Write-Host "  column: make.powerapps.com > Tables > $Table > Columns > $Column > Advanced options > clear 'Enable column security', save, publish $Table." -ForegroundColor Red
}

function Invoke-ColumnSecurity($spec) {
    $t = $spec.Table; $c = $spec.Column
    $tag = "$t.$c"
    $attr = Get-Attr $t $c 'LogicalName,AttributeType,IsSecured,CanBeSecuredForRead,CanBeSecuredForCreate,CanBeSecuredForUpdate,MetadataId'
    $grants = @(@{ Id = $script:WriterId; Name = $WriterProfileName; Write = 4 })
    if ($spec.ReaderToo) { $grants = @(@{ Id = $script:ReaderId; Name = $ReaderProfileName; Write = 0 }) + $grants }

    if (-not $attr) {
        if (-not $DryRun) { throw "$tag is missing after its create; cannot secure it." }
        $script:Changed.Add("secure $tag"); Write-Host "  WOULD SECURE $tag after it is created, then grant: $(($grants | ForEach-Object { "$($_.Name) (read=4 create=$($_.Write) update=$($_.Write))" }) -join '; ')" -ForegroundColor Cyan
        foreach ($g in $grants) { $script:Created.Add("field permission $($g.Name) on $tag") }
        return
    }
    if (-not ($attr.CanBeSecuredForRead -and $attr.CanBeSecuredForCreate -and $attr.CanBeSecuredForUpdate)) {
        throw [EscalationException]::new("$tag cannot be secured for read, create and update. Owner decision needed.")
    }

    $needSecure = -not $attr.IsSecured
    $missing = @()
    foreach ($g in $grants) {
        $perm = if ($g.Id) { Get-FieldPermission $g.Id $t $c } else { $null }
        if ($perm) {
            if ($perm.canread -eq 4 -and $perm.cancreate -eq $g.Write -and $perm.canupdate -eq $g.Write) { Write-Report 'OK' "$($g.Name) on $tag (read=4 create=$($g.Write) update=$($g.Write))" }
            else { Write-Report 'FAIL' "$($g.Name) on $tag is read=$($perm.canread) create=$($perm.cancreate) update=$($perm.canupdate); expected read=4 create=$($g.Write) update=$($g.Write)" }
        }
        else { $missing += $g }
    }

    if (-not $needSecure -and $missing.Count -eq 0) {
        $script:Skipped.Add("secure $tag"); Write-Host "  skipped  $tag (IsSecured, profiles granted)" -ForegroundColor Yellow
        return
    }
    if ($DryRun) {
        if ($needSecure) { $script:Changed.Add("secure $tag"); Write-Host "  WOULD SECURE $tag" -ForegroundColor Cyan }
        else { $script:Skipped.Add("secure $tag"); Write-Host "  skipped  $tag already IsSecured" -ForegroundColor Yellow }
        foreach ($g in $missing) { $script:Created.Add("field permission $($g.Name) on $tag"); Write-Host "  WOULD GRANT $($g.Name) on $tag (read=4 create=$($g.Write) update=$($g.Write))" -ForegroundColor Cyan }
        return
    }

    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    if ($needSecure) {
        Set-ColumnSecured $t $c $spec.Kind $true
        $script:Changed.Add("secure $tag"); Write-Report 'DONE' "secured $tag"
    }
    try {
        foreach ($g in $missing) {
            Grant-FieldPermission $g.Id $t $c $g.Write
            $script:Created.Add("field permission $($g.Name) on $tag"); Write-Report 'DONE' "granted $($g.Name) on $tag (read=4 create=$($g.Write) update=$($g.Write))"
        }
    }
    catch {
        $grantError = $_
        Write-Host "  FAIL     granting the profiles on $tag failed: $($grantError.Exception.Message)" -ForegroundColor Red
        if ($needSecure) {
            Write-Host "           reverting $tag to NOT field-secured, so no reader is left masked..." -ForegroundColor Yellow
            try {
                Set-ColumnSecured $t $c $spec.Kind $false
                Invoke-Dv -Path 'PublishXml' -Method POST -Body @{ ParameterXml = "<importexportxml><entities><entity>$t</entity></entities></importexportxml>" } | Out-Null
                $back = (Get-Attr $t $c 'LogicalName,IsSecured').IsSecured
                if ($back) { throw "the revert was accepted but $tag still reads IsSecured=true" }
                Write-Host "  REVERTED $tag is NOT field-secured again. Fix the cause, then re-run." -ForegroundColor Yellow
            }
            catch { Write-MaskedRecovery $t $c "the grant failed ($($grantError.Exception.Message)) AND the revert failed ($($_.Exception.Message))" }
        }
        throw $grantError
    }
    $clock.Stop()
    if ($needSecure) { Write-Host ("  masked window for {0}: {1:N1}s (secure -> grants)" -f $tag, $clock.Elapsed.TotalSeconds) }
}

# Nobody besides the writer profile and System Administrator may create or update a secured column; and an S8 column is held by
# NO reader profile at all (no user reads or writes the snapshot).
function Test-OtherProfiles {
    foreach ($spec in $SecuredColumns) {
        $t = $spec.Table; $c = $spec.Column
        if (-not (Get-Attr $t $c 'LogicalName')) { continue }
        $rows = @((Invoke-Dv "fieldpermissions?`$select=canread,cancreate,canupdate&`$expand=fieldsecurityprofileid(`$select=name)&`$filter=entityname eq '$t' and attributelogicalname eq '$c'").value)
        $otherWriters = @($rows | Where-Object { ($_.canupdate -eq 4 -or $_.cancreate -eq 4) -and $_.fieldsecurityprofileid.name -notin $WriterProfileName, 'System Administrator' })
        foreach ($o in $otherWriters) { Write-Report 'FAIL' "profile '$($o.fieldsecurityprofileid.name)' can also write $t.$c - only the BFF writer profile may" }
        if (-not $spec.ReaderToo) {
            $readers = @($rows | Where-Object { $_.fieldsecurityprofileid.name -notin $WriterProfileName, 'System Administrator' })
            foreach ($o in $readers) { Write-Report 'FAIL' "profile '$($o.fieldsecurityprofileid.name)' holds a permission on $t.$c - the snapshot has NO reader profile (D-141)" }
            if ($readers.Count -eq 0) { Write-Report 'OK' "$t.$c is held by no profile besides '$WriterProfileName' and System Administrator" }
        }
        elseif ($otherWriters.Count -eq 0) { Write-Report 'OK' "no profile besides '$WriterProfileName' and System Administrator can write $t.$c" }
    }
}

# ---------------------------------------------------------------------------------------------------------------
# Run
# ---------------------------------------------------------------------------------------------------------------

Write-Host ""
Write-Host "eBilling task 080 - rule set and rate card scope schema (S1-S4, S6-S8)" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Solution: $SolutionUniqueName   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })$(if ($BackfillOnly) { '   BACKFILL ONLY' })$(if ($SkipBackfill) { '   backfill skipped' })"
Write-Host "Profiles   : reader '$ReaderProfileName', writer '$WriterProfileName' (members are never changed on an existing profile)"
Write-Host ""

try {
    if ($BackfillOnly) {
        Invoke-RateCardBackfill
    }
    else {
        foreach ($ref in $RuleSet, $Card, 'sprk_organization', 'businessunit', 'systemuser' + $CoreTables) {
            if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$ref')?`$select=LogicalName" -Retries 4)) { throw "$ref does not exist in $EnvironmentUrl." }
        }

        # --- 0. Pre-flight: every refusal that can be known before the first write ----------------------------------------------
        Write-Host "0. Pre-flight (read-only)" -ForegroundColor Cyan
        # 0a. S4: businessunit must allow many-to-many; otherwise STOP (the intersect-table fallback is the owner's call).
        $bu = Get-DvOrNull "EntityDefinitions(LogicalName='businessunit')?`$select=CanBeInManyToMany" -Retries 2
        if (-not $bu -or -not $bu.CanBeInManyToMany.Value) {
            throw [EscalationException]::new("businessunit reports CanBeInManyToMany = false. S4 cannot create the N:N $NnSchema. STOP: the owner must approve option (b) of the design note (an intersect table sprk_rulesetbusinessunit) as a script change; no fallback is created here.")
        }
        Write-Host "  businessunit CanBeInManyToMany = true; sprk_ruleset CanBeInManyToMany = $((Get-DvOrNull "EntityDefinitions(LogicalName='$RuleSet')?`$select=CanBeInManyToMany").CanBeInManyToMany.Value)"
        # 0b. S2: the scope choice is a local choice with the two known options; the two new values / labels are free or already ours.
        $have = Get-ScopeOptions
        Write-Host "  $RuleSet.$ScopeColumn options now: $(Format-Options $have)"
        foreach ($v in $ScopeKnown.Keys) {
            $cur = $have | Where-Object { $_.Value -eq [int]$v }
            if (-not $cur -or (ConvertTo-LabelKey $cur.Label) -ne (ConvertTo-LabelKey $ScopeKnown[$v])) {
                throw [EscalationException]::new("$RuleSet.$ScopeColumn option $v is '$($cur.Label)', expected '$($ScopeKnown[$v])'. Not touched; owner decision needed.")
            }
        }
        foreach ($v in $ScopeNew.Keys) {
            $key = ConvertTo-LabelKey $ScopeNew[$v]
            $byLabel = @($have | Where-Object { (ConvertTo-LabelKey $_.Label) -eq $key -and $_.Value -ne [int]$v })
            if ($byLabel.Count -gt 0) { throw [EscalationException]::new("Label '$($ScopeNew[$v])' already exists as value $($byLabel[0].Value), expected $v. Owner decision needed; nothing renumbered.") }
            $cur = $have | Where-Object { $_.Value -eq [int]$v }
            if ($cur -and (ConvertTo-LabelKey $cur.Label) -ne $key) { throw [EscalationException]::new("Value $v is labelled '$($cur.Label)', expected '$($ScopeNew[$v])'. Owner decision needed; nothing relabelled.") }
        }
        $other = @($have | Where-Object { $_.Value -notin @($ScopeKnown.Keys) + @($ScopeNew.Keys) })
        if ($other.Count -gt 0) { Write-Host "  note: $RuleSet.$ScopeColumn has options outside the set this task knows: $(Format-Options $other)" -ForegroundColor Yellow }
        # 0c. Relationship and intersect names must not already mean something else.
        foreach ($l in $CoreLookups) {
            $checks = @(@{ T = $RuleSet; C = $l.Column; R = $l.Rel }, @{ T = $l.Table; C = $SnapshotColumn; R = $l.SecuredRel })
            foreach ($k in $checks) {
                $rel = Get-DvOrNull "RelationshipDefinitions(SchemaName='$($k.R)')"
                if ($rel -and -not (Get-Attr $k.T $k.C)) { throw [EscalationException]::new("Relationship $($k.R) already exists ($($rel.RelationshipType)) but $($k.T).$($k.C) does not: the name is taken with another meaning. Owner decision needed; nothing renamed.") }
            }
        }
        if ((Get-DvOrNull "EntityDefinitions(LogicalName='$NnSchema')?`$select=LogicalName") -and -not (Get-ManyToMany $NnSchema)) {
            throw [EscalationException]::new("A table named $NnSchema already exists but it is not the N:N relationship's intersect. Owner decision needed.")
        }
        # 0d. Existing lookups that S1 / S6 / S8 rely on must already be RemoveLink (never Restrict).
        foreach ($l in $CoreLookups) {
            Assert-RemoveLink $Card $l.Column 'D-172: no delete-Restrict; the rate card lookups stay RemoveLink'
            Assert-RemoveLink $RuleSet $l.Column 'D-172: no delete-Restrict'
            Assert-RemoveLink $l.Table $SnapshotColumn 'D-141'
        }
        Write-Host "  no existing item conflicts; every existing core lookup reads RemoveLink" -ForegroundColor Green
        # 0e. The profile pair (S7 / S8).
        Write-Host "  field security profiles:"
        $pre = Test-ProfilePreconditions

        # --- 1. S1 core lookups on sprk_ruleset ---------------------------------------------------------------------------------
        Write-Host "1. S1 $RuleSet core-record lookups (RemoveLink)" -ForegroundColor Cyan
        Set-EbEntity $RuleSet
        foreach ($l in $CoreLookups) {
            Add-Lookup -Column $l.Column -SchemaName $l.Schema -RelSchema $l.Rel -Target $l.Table -Display $l.Display `
                -Description "Scope: the $($l.Display.ToLowerInvariant()) this rule set applies to (scope Record or Firm record). Exactly one core lookup is set on such a set (enforced by RuleSetService). Written only by the BFF (field-secured). If the record is deleted the link is cleared and the set applies to nothing (D-172)." `
                -DeleteBehavior 'RemoveLink'
        }

        # --- 2. S2 scope options ------------------------------------------------------------------------------------------------
        Write-Host "2. S2 $RuleSet.$ScopeColumn options: Record = 100000002, Firm record = 100000003" -ForegroundColor Cyan
        $have = Get-ScopeOptions
        foreach ($v in $ScopeNew.Keys) {
            $label = $ScopeNew[$v]
            $cur = $have | Where-Object { $_.Value -eq [int]$v }
            $name = "$RuleSet.$ScopeColumn=$v"
            if ($cur) { $script:Skipped.Add($name); Write-Host "  skipped  $v $label (already present)" -ForegroundColor Yellow; continue }
            if ($DryRun) { $script:Created.Add($name); Write-Host "  WOULD INSERT $v $label" -ForegroundColor Cyan; continue }
            try {
                Invoke-Dv -Path 'InsertOptionValue' -Method POST -Body @{
                    EntityLogicalName = $RuleSet; AttributeLogicalName = $ScopeColumn; Value = [int]$v; Label = (New-Label $label)
                    SolutionUniqueName = $SolutionUniqueName
                } | Out-Null
            }
            catch {
                # A stale read hid an option that is already there: the refusal is authoritative, verify on a settled read.
                if (-not (Test-DuplicateRefusal $_.Exception.Message -Loose)) { throw }
                $found = $null
                for ($n = 0; $n -lt 12 -and -not $found; $n++) {
                    $found = Get-ScopeOptions | Where-Object { $_.Value -eq [int]$v -and (ConvertTo-LabelKey $_.Label) -eq (ConvertTo-LabelKey $label) }
                    if (-not $found) { Start-Sleep -Seconds 5 }
                }
                if (-not $found) { throw }
                $script:Skipped.Add($name); Write-Host "  skipped  $v $label (exists, found via create refusal)" -ForegroundColor Yellow; continue
            }
            $script:Created.Add($name); $script:Wrote = $true; Write-Host "  INSERTED $v $label" -ForegroundColor Green
        }

        # --- 3. S3 all-business-units flag --------------------------------------------------------------------------------------
        Write-Host "3. S3 $RuleSet.sprk_allbusinessunits (two-option, default Yes)" -ForegroundColor Cyan
        Set-EbEntity $RuleSet
        Add-Column 'sprk_allbusinessunits' (New-TwoOptionDef 'sprk_AllBusinessUnits' 'All business units' 'Yes (the default): the rule set applies to every business unit. No: only to the business units on its list; with No and an empty list it applies to nothing and publish refuses it (D-171). An empty list never means all.' $true) 'Boolean'
        if (Get-Attr $RuleSet 'sprk_allbusinessunits') {
            $bo = Get-DvOrNull "EntityDefinitions(LogicalName='$RuleSet')/Attributes(LogicalName='sprk_allbusinessunits')/Microsoft.Dynamics.CRM.BooleanAttributeMetadata?`$select=DefaultValue"
            if ($bo -and $bo.DefaultValue -ne $true) { throw [EscalationException]::new("$RuleSet.sprk_allbusinessunits exists with default $($bo.DefaultValue); S3 needs Yes. Owner decision needed.") }
            $nullRows = Get-RowCountExact 'sprk_rulesets' 'sprk_rulesetid' 'sprk_allbusinessunits eq null'
            Write-Host "  note: $nullRows existing $RuleSet row(s) hold no value in sprk_allbusinessunits (a column added later is empty on old rows; the loader must read empty as Yes). Not written here."
        }
        else { Write-Host "  note: existing $RuleSet rows will hold no value in sprk_allbusinessunits after the create; the loader must read empty as Yes. Not written here." }

        # --- 4. S4 business-unit N:N --------------------------------------------------------------------------------------------
        Write-Host "4. S4 N:N $NnSchema ($RuleSet <-> businessunit)" -ForegroundColor Cyan
        Add-ManyToMany -Schema $NnSchema -Entity1 $RuleSet -Entity2 'businessunit' -Label1 'Business Units' -Label2 'Rule Sets'

        # --- 5. S6 rate card scope column ---------------------------------------------------------------------------------------
        Write-Host "5. S6 $Card.$ScopeColumn (Applies to: Firm / Core record)" -ForegroundColor Cyan
        Set-EbEntity $Card
        Add-LocalChoice -Column $ScopeColumn -Schema 'sprk_Scope' -Display 'Applies to' `
            -Description 'Firm (every record of the firm) or Core record (the one matter, project or work assignment in the lookups). A card that is Core record with no lookup (the record was deleted) applies to nothing. Empty = not used (D-172, #2132).' `
            -Labels $CardScopeLabels
        $cardScope = Get-Attr $Card $ScopeColumn
        if ($cardScope) {
            $dv = Get-DvOrNull "EntityDefinitions(LogicalName='$Card')/Attributes(LogicalName='$ScopeColumn')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=DefaultFormValue"
            if ($dv -and $null -ne $dv.DefaultFormValue -and [int]$dv.DefaultFormValue -ge 0) { throw [EscalationException]::new("$Card.$ScopeColumn has a default ($($dv.DefaultFormValue)); S6 needs no default. Owner decision needed.") }
        }

        # --- 6. S8 snapshot columns on the three core tables --------------------------------------------------------------------
        Write-Host "6. S8 $SnapshotColumn on the three core tables (lookup -> businessunit, RemoveLink, field-secured, no form, no view)" -ForegroundColor Cyan
        foreach ($l in $CoreLookups) {
            Set-EbEntity $l.Table
            Add-Lookup -Column $SnapshotColumn -SchemaName 'sprk_SecuredFromBusinessUnit' -RelSchema $l.SecuredRel -Target 'businessunit' -Display 'Business unit before Secure' `
                -Description 'The business unit this record had before it was made Secure. Written only by the BFF on entry to Secure (task 087); read only while the record is held in the Secure Record business unit; Unsecure never reads it. Field-secured, no reader profile, on no form and in no view (D-141).' `
                -DeleteBehavior 'RemoveLink'
        }

        # --- 7. S7 + S8 field security ------------------------------------------------------------------------------------------
        Write-Host "7. S7 / S8 field security (reader: Read for every user; writer: BFF application user(s) only)" -ForegroundColor Cyan
        New-ProfilesIfNeeded $pre
        foreach ($spec in $SecuredColumns) { Invoke-ColumnSecurity $spec }
        Test-OtherProfiles

        # --- 8. Backfill --------------------------------------------------------------------------------------------------------
        if (-not $SkipBackfill) { Invoke-RateCardBackfill }
        else { Write-Host "8. S6 backfill skipped (-SkipBackfill)" -ForegroundColor Yellow }
    }

    # --- Publish (only when something changed; entity-scoped, never PublishAllXml) -------------------------------------------------
    Write-Host "Publish" -ForegroundColor Cyan
    $metaChanged = $script:Created.Count -gt 0 -or @($script:Changed | Where-Object { $_ -like 'secure *' }).Count -gt 0
    if ($DryRun) { Write-Host "  WhatIf: publish skipped" -ForegroundColor Cyan }
    elseif ($BackfillOnly) { Write-Host "  data only - no publish" -ForegroundColor Yellow }
    elseif (-not $metaChanged) { Write-Host "  nothing changed - no publish" -ForegroundColor Yellow }
    else {
        Invoke-Dv -Path 'PublishXml' -Method POST -Body @{
            ParameterXml = "<importexportxml><entities><entity>$RuleSet</entity><entity>$Card</entity><entity>sprk_matter</entity><entity>sprk_project</entity><entity>sprk_workassignment</entity><entity>businessunit</entity></entities></importexportxml>"
        } | Out-Null
        Write-Host "  customizations published" -ForegroundColor Green
    }

    # --- Verification (metadata read of everything this script owns) ---------------------------------------------------------------
    if (-not $DryRun -and -not $BackfillOnly) {
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
        # RemoveLink on the three sprk_ruleset relationships, the three snapshot relationships and the three untouched rate card ones.
        foreach ($l in $CoreLookups) {
            foreach ($k in @(@($RuleSet, $l.Column, $l.Rel), @($l.Table, $SnapshotColumn, $l.SecuredRel), @($Card, $l.Column, $l.CardRel))) {
                $rel = Get-LookupRelationship $k[0] $k[1]
                if (-not $rel) { $bad.Add("no many-to-one relationship for $($k[0]).$($k[1])"); continue }
                Write-Host ("  {0}.{1}: {2}  delete {3}, assign {4}, share {5}" -f $k[0], $k[1], $rel.SchemaName, $rel.CascadeConfiguration.Delete, $rel.CascadeConfiguration.Assign, $rel.CascadeConfiguration.Share)
                if ($rel.CascadeConfiguration.Delete -ne 'RemoveLink') { $bad.Add("$($k[0]).$($k[1]) delete behaviour is $($rel.CascadeConfiguration.Delete), expected RemoveLink") }
                if ($k[2] -ne $rel.SchemaName -and $k[0] -ne $Card) { $bad.Add("$($k[0]).$($k[1]) relationship is named $($rel.SchemaName), expected $($k[2])") }
            }
        }
        $now = Get-ScopeOptions
        Write-Host "  $RuleSet.$ScopeColumn options: $(Format-Options $now)"
        $want = @($ScopeKnown.Keys) + @($ScopeNew.Keys) | ForEach-Object { [int]$_ }
        if (@($now | Where-Object { $_.Value -notin $want }).Count -gt 0 -or @($want | Where-Object { $_ -notin $now.Value }).Count -gt 0) { $bad.Add("$RuleSet.$ScopeColumn options are not exactly 100000000-100000003") }
        $cardOpts = Get-PicklistLabels $Card $ScopeColumn
        Write-Host "  $Card.$ScopeColumn options: $($cardOpts -join ', ')"
        $nn = Get-ManyToMany $NnSchema
        if (-not $nn) { $bad.Add("N:N $NnSchema missing") } else { Write-Host "  N:N $($nn.SchemaName): $($nn.Entity1LogicalName) <-> $($nn.Entity2LogicalName), intersect $($nn.IntersectEntityName)" }
        foreach ($spec in $SecuredColumns) {
            $a = Get-Attr $spec.Table $spec.Column 'LogicalName,IsSecured'
            if (-not $a -or -not $a.IsSecured) { $bad.Add("$($spec.Table).$($spec.Column) is not IsSecured") }
            else { Write-Host "  $($spec.Table).$($spec.Column): IsSecured = true" }
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
Write-Host ("created : {0}" -f $script:Created.Count)
$script:Created | ForEach-Object { Write-Host "    + $_" }
Write-Host ("changed : {0}" -f $script:Changed.Count)
$script:Changed | ForEach-Object { Write-Host "    ~ $_" }
Write-Host ("skipped : {0}" -f $script:Skipped.Count)
$script:Skipped | ForEach-Object { Write-Host "    = $_" }
if ($State.Deferred.Count -gt 0) { Write-Host ("deferred: {0}  {1}" -f $State.Deferred.Count, ($State.Deferred -join ', ')) -ForegroundColor DarkYellow }
if ($Gaps.Count -gt 0) {
    Write-Host ""
    Write-Host "FINDINGS ($($Gaps.Count)):" -ForegroundColor Red
    $Gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
}
if ($DryRun) { Write-Host "WhatIf run: nothing was written." -ForegroundColor Cyan }
if ($Gaps.Count -gt 0) { exit 1 }
exit 0
