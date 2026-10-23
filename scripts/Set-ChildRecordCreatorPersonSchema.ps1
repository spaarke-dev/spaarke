#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Adds the server-stamped "Created By (Person)" column — sprk_createdbyperson, a lookup to systemuser — to every CHILD
    table the BFF creates as the application, and locks it with field-level security so ONLY the BFF can write it
    (unified-access-control-r2 task 146 c1-r1; owner decision round 13 item 9, 2026-10-03). Dry run by default; -Apply
    writes; -Verify checks. Idempotent. In the pattern of scripts/Set-RecordCreatorPersonSchema.ps1 (task 133, the roots).

.DESCRIPTION
    WHY. Owner round 10 item 7 made moving a CHILD out of a secure record an un-secure: only a Full Access holder on the
    secure record, or the person who created the child, may do it (F3). For a child the BFF creates APP-ONLY — a G5 chat
    create, an Office save, an event, an analysis, an outbound email, an invoice — `createdby` is the BFF application user
    and `createdonbehalfby` is empty, so the "or the creator" branch could never admit anyone. Owner round 13 item 9:
    "children the BFF creates as the application record the person who asked: sprk_createdbyperson is added to the child
    tables ... and stamped by every app-create writer, so F3's 'or the creator' branch works for them."

    The BFF stamps the column in the create payload of every app-create writer that acts for a person
    (RecordOwnershipContext.RequestedBy -> RecordOwnerResolution.CreatedByPerson -> ApplyTo / StampCreatorOn, and the
    shared seams' CreatedByPersonId). Writers that act for nobody (inbound mail, background jobs) leave it empty. F3 reads
    it in RecordOwnershipResolver.ReparentAsync; a table that lacks the column there is "could not tell" (unverifiable),
    never "allowed".

    The script provisions, in order:
      (a) COLUMN     sprk_createdbyperson (schema sprk_CreatedByPerson, navigation property sprk_CreatedByPerson) on each
                     child table: a lookup to systemuser through a 1:N relationship sprk_systemuser_<table>_createdbyperson
                     whose cascade is NoCascade for Assign/Share/Unshare/Reparent/Merge and RemoveLink for Delete (a user is
                     never deleted, only disabled — and nothing about the user may move the record).
      (b) PROFILES   The SAME two profiles task 133 created for the roots: "Spaarke BFF-Managed Field Readers" (Read; every
                     business unit's default team) and "Spaarke BFF-Managed Field Writers" (Read + Create + Update; the BFF
                     application user(s) ONLY). Created here only when absent (an environment where task 133's script has
                     not run). Members are associated BEFORE the columns are secured, so the BFF never loses access. Any
                     OTHER member of the writer profile — a human, an application user not named in -BffApplicationIds, or
                     any team — is reported FAIL in every mode: the profile's membership IS the lock. Reported, never
                     removed.
      (b2) NARROW    D-136 (ontology-platform-r1, owner 2026-10-13): the ontology writer managed identity creates budget
           PROFILE    revisions, Decision Records and Signals, and is NOT in the BFF writer profile (that profile also guards
                     sprk_issecure and ~35 other columns, and its "only the BFF" rule must hold). So a SECOND profile,
                     "Spaarke Ontology Writer Field Writers", is created when absent; its ONLY member is the application
                     user named by -OntologyWriterApplicationId; it holds read=4 create=4 update=4 on sprk_createdbyperson
                     for EXACTLY $OntologyWriterTables and nothing else. -Verify FAILs on a missing profile, any other
                     member (user or team), a row that is not 4/4/4, a missing row, or any permission outside those three
                     columns. The profile travels in SpaarkeMaster (membership is per environment and is not in the source).
      (c) FLS        Each column becomes field-secured; the reader profile gets read=4, the writer profile read=4 create=4
                     update=4. EMPTY-BEFORE-SECURING GATE (every table; ebilling-r1 081, UAC #2276): before this script
                     secures ANY sprk_createdbyperson that is currently unsecured, it counts the rows holding a value. A
                     value written while the column was unsecured could name anyone, and the BFF trusts the column on a
                     stamped table (UAC #2277). A non-zero count FAILs that table: it is NOT secured (no PUT), no field
                     permission is granted, and the run exits 1 (-Apply, -Verify and the dry run alike: the dry run exits 1
                     on any FAIL or MISSING it finds, though it writes nothing). After
                     -Apply secures a column it counts AGAIN: a value that arrived between the count and the secure FAILs
                     the run (exit 1) -- the column stays secured; review those rows before deploying the BFF. Any OTHER profile (besides the platform's System Administrator profile, and the D-136 narrow
                     profile on its three tables) that can create or update the column is reported FAIL.
      (d) SOLUTION   The lookups, their relationships and both profiles are in -SolutionUniqueName (SpaarkeCore). A column
                     or relationship counts as included when its table is in the solution with rootcomponentbehavior = 0
                     ("include subcomponents") — the false negative the batch-4 live run found in task 133's verify
                     (notes/batch4-live-gates-2026-10-03.md) is not repeated here. A table listed in $SolutionHomes
                     (sprk_budgetrevision, sprk_decisionrecord, sprk_signal: their column travels in SpaarkeMaster and
                     OntologyPlatformSolution, which hold the
                     whole table, not in SpaarkeCore) is checked against THOSE solutions instead, read-only: a gap there is
                     reported, never written, so SpaarkeCore is not reported as a false gap. For such a table -Apply
                     NEVER creates a missing column or relationship (that would put them in SpaarkeCore, the wrong
                     solution): it reports FAIL "import the owning solution first" and exits 1. The D-136 narrow profile
                     is checked in, and added to, SpaarkeMaster -- not SpaarkeCore.
      (e) PUBLISH    every child table.

    INFORMATIONAL (never written): how many existing rows of each table were created by an application user and carry no
    sprk_createdbyperson. Those rows predate the column; there is no person to backfill (createdonbehalfby is empty), so F3
    admits only Full Access holders for them.

    ⚠️ DEPLOY ORDER — a HARD pre-deploy gate (task 146 G146-6). A BFF build carrying task 146 c1-r1 WRITES this column on
    every app-created child it creates for a person. Dataverse refuses a create naming a column that does not exist, so
    those creates FAIL until (a) has run. Apply this script — and see -Verify exit 0 — in an environment BEFORE deploying
    such a BFF to it.

    ⚠️ DEPLOY ORDER — a SECOND HARD gate (D-136, ontology task 147). A BFF build carrying ontology task 147 stamps
    sprk_createdbyperson on every budget revision the ontology writer creates, task 142 on every Decision Record, and 143
    does the same for Signals. The writer is not a BFF user, so without the narrow profile every such create is refused by field
    security. In every target environment the profile "Spaarke Ontology Writer Field Writers" must exist, with the writer as
    its only member and the three 4/4/4 rows, BEFORE that BFF deploys: run -Apply, then -Verify to exit 0. For an
    environment other than dev, sprk_budgetrevision.sprk_createdbyperson, sprk_decisionrecord.sprk_createdbyperson and
    sprk_signal.sprk_createdbyperson must first arrive by importing SpaarkeMaster / OntologyPlatformSolution (this script will
    not create them).

    ⚠️ A business unit created later needs its default team added to the reader profile (re-run -Apply).

.PARAMETER EnvironmentUrl
    e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER BffApplicationIds
    Application (client) ids whose Dataverse application users must be members of the writer profile — the BFF managed
    identity (dev: 5967251e-171c-46fe-a6c2-ef843c90309d) and, while it is still an application user, the BFF app
    registration (dev: 1e40baad-e065-4aea-a8d4-4b7ab273458c). Required for -Apply and -Verify.

.PARAMETER OntologyWriterApplicationId
    Application (client) id of the ontology writer's Dataverse application user -- the ONLY member of the D-136 profile
    "Spaarke Ontology Writer Field Writers". Dev: 69040982-612e-469e-a85f-26d5172367c5 (# mi-ontology-writer-dev,
    systemuserid 3121bf1b-9fbf-f111-aaaf-0022482913fc; the UAMI's client id, i.e. Ontology:Writer:ManagedIdentityClientId).
    Required for -Apply and -Verify.

.PARAMETER SolutionUniqueName
    The unmanaged solution that carries the components. Default SpaarkeCore.

.PARAMETER Apply
    Write mode. Without it the script never writes.

.PARAMETER Verify
    Read-only check with a pass/fail exit code (1 names each gap). Cannot be combined with -Apply.

.EXAMPLE
    .\Set-ChildRecordCreatorPersonSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c `
        -OntologyWriterApplicationId 69040982-612e-469e-a85f-26d5172367c5
    Dry run: every step's present / missing state; zero writes.

.EXAMPLE
    .\Set-ChildRecordCreatorPersonSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c `
        -OntologyWriterApplicationId 69040982-612e-469e-a85f-26d5172367c5 -Apply

.EXAMPLE
    .\Set-ChildRecordCreatorPersonSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c `
        -OntologyWriterApplicationId 69040982-612e-469e-a85f-26d5172367c5 -Verify

.NOTES
    unified-access-control-r2 task 146 c1-r1 (#1034). Code: src/server/api/Sprk.Bff.Api/Services/Dataverse/RecordCreatorPerson.cs
    (StampedChildTables) and src/server/shared/Spaarke.Dataverse/RecordCreatorPersonColumn.cs — the column name,
    navigation property, target and tables are pinned against this script by ChildRecordCreatorPersonSchemaAgreementTests.
    Run it inside PowerShell (`& ./scripts/Set-ChildRecordCreatorPersonSchema.ps1 ...`): `pwsh -File` passes the comma
    list as ONE string (the batch-4 live run's lesson). Auth: the operator's own az CLI identity (System Administrator in
    the environment). No secrets.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EnvironmentUrl,
    [string[]]$BffApplicationIds = @(),
    [string]$OntologyWriterApplicationId = '',
    [string]$SolutionUniqueName = 'SpaarkeCore',
    [switch]$Apply,
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
if ($Apply -and $Verify) { throw '-Apply and -Verify are separate modes; run -Apply first, then -Verify.' }
if (($Apply -or $Verify) -and $BffApplicationIds.Count -eq 0) {
    throw '-BffApplicationIds is required for -Apply and -Verify: the writer profile must name the BFF application user(s) explicitly.'
}
# D-136: the narrow profile (section b2) covers sprk_budgetrevision, which is always in scope, so its member must be named.
if (($Apply -or $Verify) -and -not $OntologyWriterApplicationId.Trim()) {
    throw '-OntologyWriterApplicationId is required for -Apply and -Verify: the D-136 profile "Spaarke Ontology Writer Field Writers" must name the ontology writer application user explicitly (dev: 69040982-612e-469e-a85f-26d5172367c5).'
}
$OntologyWriterApplicationId = $OntologyWriterApplicationId.Trim()
# A single comma-joined string (pwsh -File) is split, never sent as one id.
$BffApplicationIds = @($BffApplicationIds | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$Api = "$EnvironmentUrl/api/data/v9.2"

# ── Constants (the BFF uses these exact names: RecordCreatorPersonColumn / RecordCreatorPerson.StampedChildTables) ──
$Column = 'sprk_createdbyperson'
$ColumnSchemaName = 'sprk_CreatedByPerson'
$NavigationProperty = 'sprk_CreatedByPerson'
$TargetEntity = 'systemuser'
$Tables = @('sprk_document', 'sprk_todo', 'sprk_event', 'sprk_eventlog', 'sprk_communication', 'sprk_communicationthread', 'sprk_communicationattachment', 'sprk_communicationparticipant', 'sprk_emailreviewlog', 'sprk_analysis', 'sprk_analysisoutput', 'sprk_emailartifact', 'sprk_attachmentartifact', 'sprk_fileversion', 'sprk_invoice', 'sprk_spendsignal', 'sprk_spendsnapshot', 'sprk_memo', 'sprk_reportcard', 'sprk_budget', 'sprk_kpiassessment', 'sprk_billingevent', 'sprk_budgetrevision', 'sprk_decisionrecord', 'sprk_signal', 'sprk_invoicesubmission', 'sprk_invoiceapproval', 'sprk_externalidentifier', 'sprk_invoicecorrection', 'sprk_procedure', 'sprk_procedureversion', 'sprk_policy', 'sprk_policyversion', 'sprk_ruleset', 'sprk_procedureassignment')
# ebilling-r1 task 081 (procedure model 13, O-23; ontology F4-1 / D-173; UAC #2276): before this script secures ANY unsecured column, on any
# table, it asserts the column holds no value yet (Test-ColumnEmptyBeforeSecuring, section (c)). 080 created the four Procedure/policy
# columns unsecured; the owner created sprk_budgetrevision's, sprk_decisionrecord's and sprk_signal's by hand. ebilling-r1 task 090 (UAC
# answer to 081 Q5): 080 also created sprk_ruleset's and sprk_procedureassignment's unsecured; they pass the same gate.
$ReaderProfileName = 'Spaarke BFF-Managed Field Readers'
$WriterProfileName = 'Spaarke BFF-Managed Field Writers'
# D-136 (owner 2026-10-13): the ontology writer's narrow profile. Fixed id so the live profile and SpaarkeMaster's source
# (Other/FieldSecurityProfiles.xml) are the same component. Exactly these three tables, this one column, 4/4/4, one member.
$OntologyWriterProfileName = 'Spaarke Ontology Writer Field Writers'
$OntologyWriterProfileFixedId = 'b710d90c-0f27-4d6a-a146-2769c8aa921f'
$OntologyWriterProfileSolution = 'SpaarkeMaster'
$OntologyWriterTables = @('sprk_budgetrevision', 'sprk_decisionrecord', 'sprk_signal')
$Secured = $true
$ExpectedCascade = [ordered]@{ Assign = 'NoCascade'; Share = 'NoCascade'; Unshare = 'NoCascade'; Reparent = 'NoCascade'; Merge = 'NoCascade'; Delete = 'RemoveLink' }
function RelationshipSchemaName([string]$Table) { "sprk_systemuser_$($Table)_createdbyperson" }
# The relationship's name depends on how the lookup was made. This script creates `sprk_systemuser_{table}_createdbyperson`
# (RelationshipSchemaName); a lookup an owner adds in the maker is named `{table}_CreatedByPerson_systemuser`
# (D-135 Q27: sprk_budgetrevision, sprk_decisionrecord, sprk_signal). Both are the same relationship (same lookup, same
# navigation property, same cascade checks below), so neither is "created another way". Canonical form first.
function Get-CreatorPersonRelationshipNames([string]$Table) { @((RelationshipSchemaName $Table), "$($Table)_CreatedByPerson_systemuser") }
# Reads each accepted name through $Read (a scriptblock taking the schema name, answering the relationship or $null) and
# returns the first that exists; $null when none does (a name this script does not know: that stays a FAIL for a hand check).
function Find-CreatorPersonRelationship([string]$Table, [scriptblock]$Read) {
    foreach ($name in (Get-CreatorPersonRelationshipNames $Table)) {
        $found = & $Read $name
        if ($found) { return $found }
    }
    return $null
}
# Where a table's column travels when it is NOT SpaarkeCore. D-135 (c)(d), closed on dev 2026-10-12: the column on these
# tables is in BOTH solutions below, each of which holds the whole table, and not in SpaarkeCore. For such a table section (d)
# checks those solutions instead of SpaarkeCore, so it does not report a false gap -- and never writes to them (a product
# solution's membership is the owner's call).
$SolutionHomes = @{
    sprk_budgetrevision = @('SpaarkeMaster', 'OntologyPlatformSolution')
    sprk_decisionrecord = @('SpaarkeMaster', 'OntologyPlatformSolution')   # ontology task 142 (D-135), as 147 did for the budget revision
    sprk_signal         = @('SpaarkeMaster', 'OntologyPlatformSolution')   # ontology task 143 (D-135), as 142 did for the Decision Record
    # ebilling-r1 task 013: the invoice family's new tables live only in SpaarkeMaster (scripts/ebilling/Deploy-Ebilling*Schema.ps1
    # create each table there, its sprk_createdbyperson lookup included); this script secures the column and checks it.
    sprk_invoicesubmission  = @('SpaarkeMaster')
    sprk_invoiceapproval    = @('SpaarkeMaster')
    sprk_externalidentifier = @('SpaarkeMaster')
    sprk_invoicecorrection  = @('SpaarkeMaster')
    # ebilling-r1 task 081: the Procedure tables live only in SpaarkeMaster (task 080's Deploy-ProcedureSchema created each one, its
    # sprk_createdbyperson lookup included); this script secures the column and checks it, never creates it.
    sprk_procedure          = @('SpaarkeMaster')
    sprk_procedureversion   = @('SpaarkeMaster')
    # ebilling-r1 task 081: 080 added the column on the policy tables in SpaarkeMaster (and left sprk_policy unpublished for this script).
    sprk_policy             = @('SpaarkeMaster')
    sprk_policyversion      = @('SpaarkeMaster')
    # ebilling-r1 task 090 (UAC answer to 081 Q5): 080 added the column on sprk_ruleset and created sprk_procedureassignment with it,
    # both in SpaarkeMaster; this script secures the column and checks it, never creates it.
    sprk_ruleset              = @('SpaarkeMaster')
    sprk_procedureassignment  = @('SpaarkeMaster')
}

# ── Auth + helpers ──────────────────────────────────────────────────────────────────────────────────────────
$token = az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv 2>$null
if (-not $token) { throw "No Dataverse token for $EnvironmentUrl. Run 'az login' and retry." }
$headers = @{
    Authorization      = "Bearer $token"
    Accept             = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'Content-Type'     = 'application/json; charset=utf-8'
}
function Invoke-DvGet([string]$Path) { Invoke-RestMethod -Uri "$Api/$Path" -Headers $headers -Method Get }
# Counts every row a query matches, following @odata.nextLink past Dataverse's 5,000-row page.
function Measure-DvRows([string]$Path) {
    $h = $headers.Clone(); $h['Prefer'] = 'odata.maxpagesize=5000'
    $count = 0; $next = "$Api/$Path"
    while ($next) {
        $page = Invoke-RestMethod -Uri $next -Headers $h -Method Get
        $count += @($page.value).Count
        $next = $page.'@odata.nextLink'
    }
    $count
}
function Invoke-DvWrite([string]$Method, [string]$Path, $Body, [hashtable]$Extra = @{}) {
    $h = $headers.Clone(); foreach ($k in $Extra.Keys) { $h[$k] = $Extra[$k] }
    $json = if ($null -eq $Body) { $null } else { $Body | ConvertTo-Json -Depth 20 -Compress }
    Invoke-RestMethod -Uri "$Api/$Path" -Headers $h -Method $Method -Body $json
}
function Try-DvGet([string]$Path) { try { Invoke-DvGet $Path } catch { $null } }
# Metadata writes propagate asynchronously: poll a read until it answers, or give up loudly — never treat "not visible
# yet" as "absent".
function Wait-DvRead([string]$Path, [string]$What, [int]$Attempts = 12, [int]$DelaySeconds = 10) {
    for ($i = 1; $i -le $Attempts; $i++) {
        $r = Try-DvGet $Path
        if ($r) { return $r }
        Write-Host "    waiting for $What to become readable ($i/$Attempts)..."
        Start-Sleep -Seconds $DelaySeconds
    }
    throw "$What was created but is still not readable after $($Attempts * $DelaySeconds)s; re-run the script (it is idempotent)."
}
function New-Label([string]$Text) {
    @{ '@odata.type' = 'Microsoft.Dynamics.CRM.Label'; LocalizedLabels = @(@{ '@odata.type' = 'Microsoft.Dynamics.CRM.LocalizedLabel'; Label = $Text; LanguageCode = 1033 }) }
}

$IsDryRun = -not $Apply.IsPresent
. (Join-Path $PSScriptRoot 'common/DataverseSolutionMembership.ps1')
$gaps = [System.Collections.Generic.List[string]]::new()
function Report([string]$State, [string]$What) {
    $color = switch ($State) { 'OK' { 'Green' } 'MISSING' { 'Yellow' } 'WOULD' { 'Cyan' } 'DONE' { 'Green' } 'INFO' { 'Gray' } default { 'Red' } }
    Write-Host ("  {0,-8} {1}" -f $State, $What) -ForegroundColor $color
    if ($State -in 'MISSING', 'FAIL') { $gaps.Add($What) }
}

$org = (Invoke-DvGet 'organizations?$select=name').value[0].name
Write-Host "Environment : $EnvironmentUrl (org '$org')"
Write-Host ("Mode        : {0}" -f $(if ($Verify) { 'VERIFY (read-only)' } elseif ($IsDryRun) { 'DRY RUN (no writes)' } else { 'APPLY' }))
Write-Host "Solution    : $SolutionUniqueName"
Write-Host "Tables      : $($Tables.Count) child tables"

# ── (a) COLUMN ──────────────────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(a) $Column on each child table"
$attrs = @{}
$relByTable = @{}
function ColumnAttributePath([string]$Table) { "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.LookupAttributeMetadata?`$select=LogicalName,Targets,IsSecured,MetadataId" }
foreach ($t in $Tables) {
    $attrPath = ColumnAttributePath $t
    $attr = Try-DvGet $attrPath
    if ($attr) {
        $targets = @($attr.Targets)
        if ($targets.Count -eq 1 -and $targets[0] -eq $TargetEntity) { Report 'OK' "$t.$Column (lookup -> $TargetEntity)" }
        else { Report 'FAIL' "$t.$Column exists but targets [$($targets -join ', ')] — expected exactly $TargetEntity" }
        $attrs[$t] = $attr
    } elseif ($SolutionHomes.ContainsKey($t)) {
        # F3 (ontology 147 review): this table's column travels in its $SolutionHomes solutions, never in $SolutionUniqueName.
        # Creating it here would put the column and its relationship in the WRONG solution, so no mode creates it.
        $homes = $SolutionHomes[$t] -join ' / '
        Report $(if ($Verify) { 'MISSING' } else { 'FAIL' }) "$t.$Column is missing and this script never creates it for $t (it travels in $homes, not $SolutionUniqueName): import the owning solution ($homes) first, then re-run"
    } elseif ($Verify) { Report 'MISSING' "$t.$Column" }
    elseif ($IsDryRun) { Report 'WOULD' "create $t.$Column (lookup -> $TargetEntity, relationship $(RelationshipSchemaName $t))" }
    else {
        Invoke-DvWrite POST 'RelationshipDefinitions' @{
            '@odata.type'        = 'Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata'
            SchemaName           = (RelationshipSchemaName $t)
            ReferencedEntity     = $TargetEntity
            ReferencingEntity    = $t
            ReferencingEntityNavigationPropertyName = $NavigationProperty
            CascadeConfiguration = @{ Assign = 'NoCascade'; Delete = 'RemoveLink'; Merge = 'NoCascade'; Reparent = 'NoCascade'; Share = 'NoCascade'; Unshare = 'NoCascade' }
            AssociatedMenuConfiguration = @{ Behavior = 'DoNotDisplay'; Group = 'Details'; Order = 10000 }
            Lookup               = @{
                '@odata.type' = 'Microsoft.Dynamics.CRM.LookupAttributeMetadata'
                SchemaName    = $ColumnSchemaName
                DisplayName   = (New-Label 'Created By (Person)')
                Description   = (New-Label 'The PERSON who asked for this record. Stamped by the BFF only (field-secured): createdby is the BFF application user for a record the BFF creates app-only, so this column is what records the person. F3 lets that person move this record out of a secure record. unified-access-control-r2 task 146, owner round 13 item 9.')
                RequiredLevel = @{ Value = 'None' }
            }
        } @{ 'MSCRM.SolutionUniqueName' = $SolutionUniqueName } | Out-Null
        Report 'DONE' "created $t.$Column"
        $attrs[$t] = Wait-DvRead $attrPath "$t.$Column metadata"
        Wait-DvRead "$( (Invoke-DvGet "EntityDefinitions(LogicalName='$t')?`$select=EntitySetName").EntitySetName )?`$select=_$($Column)_value&`$top=1" "$t.$Column in data queries" | Out-Null
    }

    # The relationship's cascade and navigation property: nothing about the USER may move, share or reparent the record,
    # and a Web API write binds the name the BFF uses.
    $readRel = { param($name) Try-DvGet "RelationshipDefinitions(SchemaName='$name')/Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata?`$select=SchemaName,ReferencedEntity,ReferencingEntity,ReferencingAttribute,ReferencingEntityNavigationPropertyName,CascadeConfiguration,MetadataId" }
    $rel = Find-CreatorPersonRelationship $t $readRel
    # Relationship metadata is read through a cache: right after the create it can still answer 404 although the
    # relationship exists (seen on spaarkedev1, 2026-10-05). Poll for up to 2 minutes before reporting it missing.
    for ($try = 1; -not $rel -and $attrs[$t] -and $try -le 24; $try++) {
        Start-Sleep -Seconds 5
        $rel = Find-CreatorPersonRelationship $t $readRel
    }
    $relByTable[$t] = $rel
    if ($rel) {
        $relName = $rel.SchemaName
        $bad = @($ExpectedCascade.Keys | Where-Object { $rel.CascadeConfiguration.$_ -ne $ExpectedCascade[$_] } |
            ForEach-Object { "$_=$($rel.CascadeConfiguration.$_) (expected $($ExpectedCascade[$_]))" })
        if ($rel.ReferencingAttribute -ne $Column) { Report 'FAIL' "$relName references $($rel.ReferencingAttribute), not $Column" }
        elseif ($rel.ReferencingEntityNavigationPropertyName -ne $NavigationProperty) { Report 'FAIL' "$relName navigation property is $($rel.ReferencingEntityNavigationPropertyName), not $NavigationProperty — the BFF's Web API binds would be refused" }
        elseif ($bad.Count -gt 0) { Report 'FAIL' "$relName cascade: $($bad -join '; ')" }
        else { Report 'OK' "$relName (navigation $NavigationProperty; no Assign/Share cascade; Delete RemoveLink)" }
    } elseif ($attrs[$t]) { Report 'FAIL' "$t.$Column exists but none of its relationships ($((Get-CreatorPersonRelationshipNames $t) -join ', ')) was found — it was created another way; check its cascade and navigation property by hand" }
    elseif ($Verify) { Report 'MISSING' "relationship $(RelationshipSchemaName $t)" }
}

# ── (b) PROFILES + MEMBERS ──────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(b) Field security profiles and members (shared with task 133's roots)"
function Ensure-Profile([string]$Name, [string]$Description, [string]$FixedId = '') {
    $p = @((Invoke-DvGet "fieldsecurityprofiles?`$select=fieldsecurityprofileid,name&`$filter=name eq '$Name'").value) | Select-Object -First 1
    if ($p) { Report 'OK' "profile '$Name'"; return $p.fieldsecurityprofileid }
    if ($Verify) { Report 'MISSING' "profile '$Name'"; return $null }
    if ($IsDryRun) { Report 'WOULD' "create profile '$Name'"; return $null }
    $body = @{ name = $Name; description = $Description }
    if ($FixedId) { $body.fieldsecurityprofileid = $FixedId }   # D-136: the id SpaarkeMaster's source carries
    $created = Invoke-DvWrite POST 'fieldsecurityprofiles' $body @{ Prefer = 'return=representation' }
    Report 'DONE' "created profile '$Name'"
    return $created.fieldsecurityprofileid
}
$readerId = Ensure-Profile $ReaderProfileName 'Read on columns ONLY the BFF writes (sprk_createdbyperson on the secure roots and on every child table the BFF creates app-only; sprk_issecure). Associated with EVERY business unit default team, so every user keeps reading them — a secured column is HIDDEN from anyone without Read. unified-access-control-r2 tasks 133 / 146.'
$writerId = Ensure-Profile $WriterProfileName 'Read/Create/Update on columns ONLY the BFF writes (sprk_createdbyperson; sprk_issecure). Members: the BFF application user(s) ONLY. unified-access-control-r2 tasks 133 / 146.'
$ontologyWriterProfileId = Ensure-Profile $OntologyWriterProfileName "Read/Create/Update on sprk_createdbyperson for $($OntologyWriterTables -join ', ') ONLY. Members: the ontology writer application user ONLY. ontology-platform-r1 D-136 (task 147)." $OntologyWriterProfileFixedId

$bffUsers = @()
foreach ($appId in $BffApplicationIds) {
    $u = @((Invoke-DvGet "systemusers?`$select=systemuserid,fullname&`$filter=applicationid eq $appId").value) | Select-Object -First 1
    if (-not $u) { Report 'FAIL' "no application user for appId $appId in this environment"; continue }
    $bffUsers += $u
}
$ontologyWriterUsers = @()
if ($OntologyWriterApplicationId) {
    $u = @((Invoke-DvGet "systemusers?`$select=systemuserid,fullname&`$filter=applicationid eq $OntologyWriterApplicationId").value) | Select-Object -First 1
    if ($u) { $ontologyWriterUsers += $u } else { Report 'FAIL' "no application user for the ontology writer appId $OntologyWriterApplicationId in this environment" }
}
$defaultTeams = @((Invoke-DvGet 'teams?$select=teamid,name&$filter=isdefault eq true').value)
function Ensure-Member([string]$ProfileId, [string]$ProfileName, [string]$Nav, [string]$Set, $Id, [string]$Label) {
    if (-not $ProfileId) { Report $(if ($Verify) { 'MISSING' } else { 'WOULD' }) "$ProfileName member: $Label"; return }
    $key = if ($Set -eq 'teams') { 'teamid' } else { 'systemuserid' }
    $existing = @((Invoke-DvGet "fieldsecurityprofiles($ProfileId)/$Nav`?`$select=$key").value)
    if ($existing | Where-Object { $_.$key -eq $Id }) { Report 'OK' "$ProfileName member: $Label"; return }
    if ($Verify) { Report 'MISSING' "$ProfileName member: $Label"; return }
    if ($IsDryRun) { Report 'WOULD' "add $Label to $ProfileName"; return }
    Invoke-DvWrite POST "fieldsecurityprofiles($ProfileId)/$Nav/`$ref" @{ '@odata.id' = "$Api/$Set($Id)" } | Out-Null
    Report 'DONE' "added $Label to $ProfileName"
}
foreach ($u in $bffUsers) { Ensure-Member $writerId $WriterProfileName 'systemuserprofiles_association' 'systemusers' $u.systemuserid "app user '$($u.fullname)'" }
foreach ($t in $defaultTeams) { Ensure-Member $readerId $ReaderProfileName 'teamprofiles_association' 'teams' $t.teamid "default team '$($t.name)'" }
foreach ($u in $ontologyWriterUsers) { Ensure-Member $ontologyWriterProfileId $OntologyWriterProfileName 'systemuserprofiles_association' 'systemusers' $u.systemuserid "app user '$($u.fullname)'" }

# A writer profile's membership IS the lock: a human — or a team — added to it can name anyone as a record's creator,
# and F3 would then admit them. So every member that is not one of the named application users is FAIL, in every mode.
# Used for the BFF writer profile (-BffApplicationIds) and the D-136 narrow profile (-OntologyWriterApplicationId).
function Test-WriterProfileMembers([string]$ProfileId, [string]$ProfileName, $AllowedUsers, [string]$ParamName, [string]$WhoMayWrite, [string]$AllowedLabel) {
    if (-not $ProfileId) { return }
    $allowed = @($AllowedUsers | ForEach-Object { $_.systemuserid.ToString().ToLowerInvariant() })
    $users = @((Invoke-DvGet "fieldsecurityprofiles($ProfileId)/systemuserprofiles_association?`$select=systemuserid,fullname,applicationid").value)
    $strangers = @($users | Where-Object { $_.systemuserid.ToString().ToLowerInvariant() -notin $allowed })
    foreach ($m in $strangers) {
        $kind = if ($m.applicationid) { "application user (appId $($m.applicationid)) not in $ParamName" } else { 'a HUMAN user' }
        Report 'FAIL' "$ProfileName member '$($m.fullname)' ($($m.systemuserid)) is $kind — only $WhoMayWrite may write $Column"
    }
    $teams = @((Invoke-DvGet "fieldsecurityprofiles($ProfileId)/teamprofiles_association?`$select=teamid,name").value)
    foreach ($tm in $teams) {
        Report 'FAIL' "$ProfileName has team '$($tm.name)' ($($tm.teamid)) as a member — every member of it could write $Column"
    }
    if ($strangers.Count -eq 0 -and $teams.Count -eq 0) { Report 'OK' "$ProfileName has no member besides $AllowedLabel" }
}
Test-WriterProfileMembers $writerId $WriterProfileName $bffUsers '-BffApplicationIds' 'the BFF' 'the BFF application user(s)'
Test-WriterProfileMembers $ontologyWriterProfileId $OntologyWriterProfileName $ontologyWriterUsers '-OntologyWriterApplicationId' 'the ontology writer' 'the ontology writer application user'

# ── (c) FIELD-LEVEL SECURITY ────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(c) Field-level security"
function Set-ColumnSecured([string]$Table) {
    $typed = Invoke-DvGet "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')"
    $typed.IsSecured = $Secured
    Invoke-DvWrite PUT "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')" $typed @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null
    Report 'DONE' "secured $Table.$Column"
}
# Task 081 / UAC #2276: how many rows of $Table hold a value in $Column (every mode reads; nothing is written).
function Measure-ColumnValues([string]$Table) {
    $set = (Invoke-DvGet "EntityDefinitions(LogicalName='$Table')?`$select=EntitySetName").EntitySetName
    Measure-DvRows "$set`?`$select=$($Table)id&`$filter=_$($Column)_value ne null"
}
# The empty-before-securing gate, for EVERY table whose column is not secured yet: $true when it holds no value (safe to secure);
# otherwise FAIL, and the caller secures nothing and grants nothing on that table (a field permission on an unsecured column is
# refused 0x8004f508, so granting would only stall).
function Test-ColumnEmptyBeforeSecuring([string]$Table) {
    $valued = Measure-ColumnValues $Table
    if ($valued -gt 0) {
        Report 'FAIL' "$Table.$Column already holds $valued value(s) while unsecured; it is NOT secured and no permission is granted by this run -- review those rows first"
        return $false
    }
    Report 'OK' "$Table.$Column holds no value yet (safe to secure)"
    return $true
}
# Count-then-secure is a race (uac-r2 review): a value written between the count and the secure stays behind the now-secured column,
# unreviewed. Counted again after -Apply secured it; any value FAILs the run (exit 1).
function Assert-ColumnStillEmptyAfterSecuring([string]$Table) {
    $raced = Measure-ColumnValues $Table
    if ($raced -gt 0) {
        Report 'FAIL' "$Table.$Column gained $raced value(s) between the empty check and securing; the column IS now secured, but those rows were written by someone other than this run -- review them before deploying the BFF"
    }
    else { Report 'OK' "$Table.$Column still holds no value after securing" }
}
# One profile's read/create/update on $Table.$Column: checked in every mode, written by -Apply only. Create/Update are the
# expected levels (read is always 4).
function Ensure-FieldPermission([string]$ProfileId, [string]$ProfileName, [string]$Table, [int]$Create, [int]$Update) {
    if (-not $ProfileId) { Report $(if ($Verify) { 'MISSING' } else { 'WOULD' }) "$ProfileName permission on $Table.$Column"; return }
    $perm = @((Invoke-DvGet "fieldpermissions?`$select=fieldpermissionid,canread,cancreate,canupdate&`$filter=_fieldsecurityprofileid_value eq $ProfileId and entityname eq '$Table' and attributelogicalname eq '$Column'").value) | Select-Object -First 1
    if ($perm) {
        if ($perm.canread -eq 4 -and $perm.cancreate -eq $Create -and $perm.canupdate -eq $Update) { Report 'OK' "$ProfileName on $Table.$Column" }
        else { Report 'FAIL' "$ProfileName on $Table.$Column is read=$($perm.canread) create=$($perm.cancreate) update=$($perm.canupdate); expected read=4 create=$Create update=$Update" }
        return
    }
    if ($Verify) { Report 'MISSING' "$ProfileName on $Table.$Column"; return }
    if ($IsDryRun) { Report 'WOULD' "grant $ProfileName on $Table.$Column (read=4 create=$Create update=$Update)"; return }
    # Securing a column propagates asynchronously: a grant right after it may be refused 0x8004f508 "... is NOT
    # secured for entity fieldpermission". Retry that one error; anything else throws.
    for ($attempt = 1; ; $attempt++) {
        try {
            Invoke-DvWrite POST 'fieldpermissions' @{
                entityname = $Table; attributelogicalname = $Column
                canread = 4; cancreate = $Create; canupdate = $Update
                'fieldsecurityprofileid@odata.bind' = "/fieldsecurityprofiles($ProfileId)"
            } | Out-Null
            break
        } catch {
            $notYetSecured = "$($_.ErrorDetails.Message) $($_.Exception.Message)" -match '0x8004f508'
            if (-not $notYetSecured -or $attempt -ge 12) { throw }
            Write-Host "    $Table.$Column not yet seen as secured by the field-permission service; retrying ($attempt/12)..."
            Start-Sleep -Seconds 10
        }
    }
    Report 'DONE' "granted $ProfileName on $Table.$Column"
}
# Tables the empty-before-securing gate refused: their column stays unsecured, so nothing may be granted on it (live Dataverse refuses a
# field permission on an unsecured column, 0x8004f508) and (c2) skips them.
$gatedOut = @{}
foreach ($t in $Tables) {
    if (-not $attrs[$t]) { Report $(if ($Verify) { 'MISSING' } else { 'WOULD' }) "secure $t.$Column and grant both profiles (after the column exists)"; continue }

    # Task 081 / UAC #2276: the empty-before-securing gate, on every table.
    if ($attrs[$t].IsSecured -ne $Secured -and -not (Test-ColumnEmptyBeforeSecuring $t)) { $gatedOut[$t] = $true; continue }

    if ($attrs[$t].IsSecured -eq $Secured) { Report 'OK' "$t.$Column is field-secured" }
    elseif ($Verify) { Report 'MISSING' "$t.$Column is NOT field-secured — any user with Write can name someone else as the record's creator" }
    elseif ($IsDryRun) { Report 'WOULD' "secure $t.$Column" }
    else { Set-ColumnSecured $t; Assert-ColumnStillEmptyAfterSecuring $t }

    foreach ($spec in @(@{ Id = $readerId; Name = $ReaderProfileName; Create = 0; Update = 0 }, @{ Id = $writerId; Name = $WriterProfileName; Create = 4; Update = 4 })) {
        Ensure-FieldPermission $spec.Id $spec.Name $t $spec.Create $spec.Update
    }

    # Besides the BFF writer profile and System Administrator, the D-136 narrow profile may write — on its own tables only.
    $mayWrite = @($WriterProfileName, 'System Administrator')
    if ($t -in $OntologyWriterTables) { $mayWrite += $OntologyWriterProfileName }
    $writers = @((Invoke-DvGet "fieldpermissions?`$select=canupdate,cancreate&`$expand=fieldsecurityprofileid(`$select=name)&`$filter=entityname eq '$t' and attributelogicalname eq '$Column' and (canupdate eq 4 or cancreate eq 4)").value)
    foreach ($w in $writers | Where-Object { $_.fieldsecurityprofileid.name -notin $mayWrite }) {
        Report 'FAIL' "profile '$($w.fieldsecurityprofileid.name)' can also write $t.$Column — only the BFF may"
    }
}

# ── (c2) D-136: THE ONTOLOGY WRITER'S NARROW PROFILE ────────────────────────────────────────────────────────
# Exactly $OntologyWriterTables, this one column, read/create/update = 4/4/4, and nothing else on the profile.
Write-Host "`n(c2) $OntologyWriterProfileName (D-136)"
foreach ($t in $OntologyWriterTables) {
    # (c) refused this table's securing (the column holds values); the narrow profile cannot hold a permission on an unsecured column.
    if ($gatedOut[$t]) { continue }
    $a = $attrs[$t]
    if (-not $a -and $t -notin $Tables) { $a = Try-DvGet (ColumnAttributePath $t) }   # a narrow-profile table not in $Tables (none since task 143)
    if (-not $a) {
        if ($t -in $Tables) { Report $(if ($Verify) { 'MISSING' } else { 'WOULD' }) "grant $OntologyWriterProfileName on $t.$Column (after the column exists)" }
        else { Report 'FAIL' "$t.$Column does not exist in this environment: import the owning solution first (D-135) -- $OntologyWriterProfileName cannot cover it" }
        continue
    }
    if ($t -notin $Tables) {
        $attrs[$t] = $a
        # (c) secured every $Tables column already; these are secured here, by the same write and the same empty gate.
        if ($a.IsSecured -ne $Secured -and -not (Test-ColumnEmptyBeforeSecuring $t)) { continue }
        if ($a.IsSecured -eq $Secured) { Report 'OK' "$t.$Column is field-secured" }
        elseif ($Verify) { Report 'MISSING' "$t.$Column is NOT field-secured" }
        elseif ($IsDryRun) { Report 'WOULD' "secure $t.$Column" }
        else { Set-ColumnSecured $t; Assert-ColumnStillEmptyAfterSecuring $t }
    }
    Ensure-FieldPermission $ontologyWriterProfileId $OntologyWriterProfileName $t 4 4
}
if ($ontologyWriterProfileId) {
    $narrowRows = @((Invoke-DvGet "fieldpermissions?`$select=entityname,attributelogicalname&`$filter=_fieldsecurityprofileid_value eq $ontologyWriterProfileId").value)
    $strayRows = @($narrowRows | Where-Object { -not ($_.attributelogicalname -eq $Column -and $_.entityname -in $OntologyWriterTables) })
    foreach ($r in $strayRows) { Report 'FAIL' "$OntologyWriterProfileName also grants $($r.entityname).$($r.attributelogicalname) -- it may hold $Column on $($OntologyWriterTables -join ', ') only" }
    if ($strayRows.Count -eq 0) { Report 'OK' "$OntologyWriterProfileName holds no permission outside $Column on $($OntologyWriterTables -join ', ')" }
}

# ── (d) SOLUTION ────────────────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(d) Solution components"
$solution = @((Invoke-DvGet "solutions?`$select=solutionid,uniquename&`$filter=uniquename eq '$SolutionUniqueName'").value) | Select-Object -First 1
if (-not $solution) { Report 'FAIL' "solution '$SolutionUniqueName' not found" }
else {
    # Paged, and a table in the solution with rootcomponentbehavior = 0 ("include subcomponents") carries its columns
    # and relationships implicitly (scripts/common/DataverseSolutionMembership.ps1).
    $membership = Get-DvSolutionMembership -Api $Api -Headers $headers -SolutionId $solution.solutionid

    # Tables whose column does not travel in SpaarkeCore (see $SolutionHomes) are checked against the solutions that hold them.
    $homeMemberships = @{}
    $components = [System.Collections.Generic.List[object]]::new()
    $homeComponents = [System.Collections.Generic.List[object]]::new()
    foreach ($t in $Tables) {
        $tableId = (Invoke-DvGet "EntityDefinitions(LogicalName='$t')?`$select=MetadataId").MetadataId
        # (not `$target = if ...`: an empty list assigned through the pipeline unrolls to $null)
        $target = $components
        if ($SolutionHomes.ContainsKey($t)) { $target = $homeComponents }
        if ($attrs[$t]) { $target.Add(@{ Id = $attrs[$t].MetadataId; Type = 2; Label = "$t.$Column"; TableId = $tableId; Table = $t }) }
        $rel = $relByTable[$t]
        if ($rel) { $target.Add(@{ Id = $rel.MetadataId; Type = 10; Label = "relationship $($rel.SchemaName)"; TableId = $tableId; Table = $t }) }
    }
    function Get-HomeMembership([string]$Name) {
        if (-not $homeMemberships.ContainsKey($Name)) {
            $homeSolution = @((Invoke-DvGet "solutions?`$select=solutionid,uniquename&`$filter=uniquename eq '$Name'").value) | Select-Object -First 1
            $homeMemberships[$Name] = if ($homeSolution) { Get-DvSolutionMembership -Api $Api -Headers $headers -SolutionId $homeSolution.solutionid } else { $null }
        }
        $homeMemberships[$Name]
    }
    foreach ($c in $homeComponents) {
        foreach ($homeName in $SolutionHomes[$c.Table]) {
            $null = Get-HomeMembership $homeName
            if (-not $homeMemberships[$homeName]) { Report 'FAIL' "solution '$homeName' (which carries $($c.Table)) not found"; continue }
            $how = Test-DvInSolution -Membership $homeMemberships[$homeName] -ComponentId $c.Id -TableMetadataId $c['TableId']
            if ($how -eq 'Direct') { Report 'OK' "$($c.Label) in $homeName" }
            elseif ($how -eq 'ViaTable') { Report 'OK' "$($c.Label) in $homeName (its table includes subcomponents)" }
            else { Report 'MISSING' "$($c.Label) in $homeName (this table's column travels there, not in $SolutionUniqueName; add it by hand — the script does not write to it)" }
        }
    }
    # D-136: the narrow profile travels in SpaarkeMaster, not in $SolutionUniqueName; -Apply adds it there (the owner decided
    # it goes there, unlike a table's column, whose membership stays the owner's call).
    if ($ontologyWriterProfileId) {
        $profileLabel = "field security profile '$OntologyWriterProfileName'"
        $profileMembership = Get-HomeMembership $OntologyWriterProfileSolution
        if (-not $profileMembership) { Report 'FAIL' "solution '$OntologyWriterProfileSolution' (which carries $OntologyWriterProfileName) not found" }
        elseif ((Test-DvInSolution -Membership $profileMembership -ComponentId $ontologyWriterProfileId -TableMetadataId $null) -in 'Direct', 'ViaTable') { Report 'OK' "$profileLabel in $OntologyWriterProfileSolution" }
        elseif ($Verify) { Report 'MISSING' "$profileLabel in $OntologyWriterProfileSolution" }
        elseif ($IsDryRun) { Report 'WOULD' "add $profileLabel to $OntologyWriterProfileSolution" }
        else {
            Invoke-DvWrite POST 'AddSolutionComponent' @{ ComponentId = $ontologyWriterProfileId; ComponentType = 70; SolutionUniqueName = $OntologyWriterProfileSolution; AddRequiredComponents = $false } | Out-Null
            Report 'DONE' "added $profileLabel to $OntologyWriterProfileSolution"
        }
    }
    foreach ($profileId in $readerId, $writerId) { if ($profileId) { $components.Add(@{ Id = $profileId; Type = 70; Label = "field security profile $profileId" }) } }

    foreach ($c in $components) {
        $how = Test-DvInSolution -Membership $membership -ComponentId $c.Id -TableMetadataId $c['TableId']
        if ($how -eq 'Direct') { Report 'OK' "$($c.Label) in $SolutionUniqueName"; continue }
        if ($how -eq 'ViaTable') { Report 'OK' "$($c.Label) in $SolutionUniqueName (its table includes subcomponents)"; continue }
        if ($Verify) { Report 'MISSING' "$($c.Label) in $SolutionUniqueName"; continue }
        if ($IsDryRun) { Report 'WOULD' "add $($c.Label) to $SolutionUniqueName"; continue }
        Invoke-DvWrite POST 'AddSolutionComponent' @{ ComponentId = $c.Id; ComponentType = $c.Type; SolutionUniqueName = $SolutionUniqueName; AddRequiredComponents = $false } | Out-Null
        Report 'DONE' "added $($c.Label) to $SolutionUniqueName"
    }
}

# ── INFORMATIONAL: app-created rows the column cannot help (never written) ─────────────────────────────────────
Write-Host "`nInformational: rows created by an application user with no person recorded"
$appUserIds = @((Invoke-DvGet 'systemusers?$select=systemuserid&$filter=applicationid ne null').value | ForEach-Object systemuserid)
if ($appUserIds.Count -eq 0) { Report 'INFO' 'no application users in this environment' }
foreach ($t in $Tables) {
    $set = (Invoke-DvGet "EntityDefinitions(LogicalName='$t')?`$select=EntitySetName").EntitySetName
    $count = 0
    for ($i = 0; $i -lt $appUserIds.Count; $i += 20) {
        $batch = $appUserIds[$i..([math]::Min($i + 19, $appUserIds.Count - 1))]
        $or = ($batch | ForEach-Object { "_createdby_value eq $_" }) -join ' or '
        $filter = if ($attrs[$t]) { "($or) and _$($Column)_value eq null" } else { "($or)" }
        $count += Measure-DvRows "$set`?`$select=$($t)id&`$filter=$filter"
    }
    Report 'INFO' "$t : $count app-created row(s) with no person recorded (F3 admits only Full Access holders for them)"
}

# ── (e) PUBLISH ─────────────────────────────────────────────────────────────────────────────────────────────
if ($Apply) {
    # (only tables that have the column: publishing a table that is absent from the environment is an error)
    $published = @(@($Tables) + @($OntologyWriterTables | Where-Object { $_ -notin $Tables }) | Where-Object { $attrs[$_] })
    $entities = ($published | ForEach-Object { "<entity>$_</entity>" }) -join ''
    Invoke-DvWrite POST 'PublishXml' @{ ParameterXml = "<importexportxml><entities>$entities</entities></importexportxml>" } | Out-Null
    Write-Host "`nPublished $($published -join ', ')." -ForegroundColor Green
    # F3: a FAIL/MISSING in -Apply (e.g. a SolutionHomes column that is not there) means the environment is NOT ready.
    if ($gaps.Count -gt 0) {
        Write-Host "`nAPPLY INCOMPLETE ($($gaps.Count)) -- not everything could be put in place:" -ForegroundColor Red
        $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
        exit 1
    }
}

if ($Verify) {
    if ($gaps.Count -eq 0) { Write-Host "`nVERIFY PASS: sprk_createdbyperson is in place on every child table; only the BFF can write it (and the ontology writer, on its three D-136 tables)." -ForegroundColor Green; exit 0 }
    Write-Host "`nVERIFY FAIL ($($gaps.Count)):" -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
if ($IsDryRun) {
    Write-Host "`nDRY RUN complete — nothing was written. Re-run with -Apply." -ForegroundColor Cyan
    # A FAIL in a dry run (e.g. a column the empty-before-securing gate refuses to secure) is the same gap -Apply would end on: exit 1.
    if ($gaps.Count -gt 0) {
        Write-Host "`nDRY RUN found $($gaps.Count) problem(s) that -Apply cannot fix by itself:" -ForegroundColor Red
        $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
        exit 1
    }
}
