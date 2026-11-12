#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Locks the columns ONLY THE BFF may write with field-level security, while every user still reads them
    (unified-access-control-r2 task 166). Two targets, one mechanism:
      -Target DocumentPointers (default) — sprk_document.sprk_graphdriveid / sprk_graphitemid, the SharePoint Embedded
                                           pointer the BFF follows as the application (owner round 21 item 1 (a)), and
                                           sprk_relocationpending, the relocation ledger (task 166 f1-v1, owner round 37:
                                           it names a source the BFF may later delete, so only the BFF may write it),
                                           and sprk_relocatedversions, the relocation's version record (task 166 f1-v2,
                                           owner round 45 item 1: it says who wrote a moved file's history), and
                                           sprk_graphitemidbound, the BFF-only COPY of sprk_graphitemid (task 171, owner
                                           round 72 item 1 — sprk_graphitemid is in an alternate key and cannot be
                                           secured, so the BFF's pointer check compares it with this copy). Those three
                                           are created already secured; this grants their profiles;
      -Target ReportCatalog              — sprk_report.sprk_pbi_reportid / sprk_workspaceid / sprk_datasetid /
                                           sprk_iscustom, the Power BI pointer the reporting module derives embed
                                           tokens, exports and deletes from (owner round 25 item 6).
      -Target DocumentNarrowingLinks     — sprk_document.sprk_relatedcommunication / sprk_parentdocument, the two
                                           links the external document narrowing walks (unified-access-control-r2
                                           task 188, D7 §6.3 "remaining hole"): a user who clears either one on a
                                           document from an internal-only email (or under a Restricted parent) makes
                                           it visible to partners. Every writer is the BFF, app-only.
      -Target AccessMarkerColumns        — the access markers (unified-access-control-r2 task 191f, #2179 design §3.6,
                                           owner Q1 = a, 2026-11-11): sprk_accesspermission on EVERY table that carries it,
                                           sprk_communication.sprk_isinternalonly and sprk_communicationthread.
                                           sprk_privacystate. Unlike the other targets, internal users keep writing them:
                                           the writer is its OWN profile, "Spaarke Access Marker Writers", held by every
                                           INTERNAL business unit's default team and the BFF application users, and never
                                           by the External Collaborators unit's default team (task 191d), so a flagged
                                           external user can read a marker but never lower it (spike S1 finding F1).
    Dry run by default; -Apply writes; -Verify checks. scripts/Set-ReportCatalogFieldSecurity.ps1,
    scripts/Set-DocumentNarrowingFieldSecurity.ps1 and scripts/Set-AccessMarkerFieldSecurity.ps1 are the named entry
    points for the second, third and fourth targets.

.DESCRIPTION
    WHY. The BFF acts AS THE APPLICATION on what these columns name: it downloads a document's bytes from the item the
    pointer names, and it mints Power BI embed tokens / runs exports / deletes reports for the report a catalog row names.
    Any user with Write on a row could re-point it (MDA form, Xrm.WebApi) and have the BFF act on something else. Round 21
    (documents) and round 25 (the report catalog) decided BOTH controls: this lock (it stops the write) and a server-side
    check before every app-only action (it catches rows forged before the lock); neither alone is sufficient.

    It REUSES task 133's two profiles — it never creates or edits their membership (one mechanism for every column only
    the BFF writes; task 150's Set-SecureFlagFieldSecurity.ps1 uses the same two):
      "Spaarke BFF-Managed Field Readers"  Read; on EVERY business unit's default team (every user keeps reading).
      "Spaarke BFF-Managed Field Writers"  Read + Create + Update; the BFF application user(s) ONLY.
    Both, and their members, are created and maintained by scripts/Set-RecordCreatorPersonSchema.ps1.
    AccessMarkerColumns reuses the reader and has its OWN writer, "Spaarke Access Marker Writers" (fixed id, created in
    -SolutionUniqueName by this script's -Apply): Read + Create + Update; members = every internal business unit's
    default team (every default team but the External Collaborators unit's and the ontology writer unit's) and the
    -BffApplicationIds application users. -Apply ADDS a missing internal team or BFF user (before any column is secured,
    so no internal user loses the edit); it never removes a member — a wrong one (the External team, any other team or
    user) is a FAIL that refuses -Apply and fails -Verify. A business unit created later: re-run
    Set-RecordCreatorPersonSchema.ps1 -Apply (reader), then THIS target's -Apply (writer), then -Verify.

    PRECONDITIONS (checked in every mode; -Apply refuses unless all pass):
      (p1) both profiles exist and are in -SolutionUniqueName;
      (p2) the reader profile is on EVERY business unit's default team (else those users read the columns EMPTY);
      (p3) the writer profile's members are exactly the -BffApplicationIds application users (no human, no team);
      (p4) nothing outside the BFF still WRITES the columns:
           DocumentPointers — (p4a) EVIDENCE: no web resource deployed in the environment (code pages, form scripts and
                              PCF bundles — every JavaScript and HTML web resource) contains a client write of a locked
                              column: an object key, a computed key, a bracket or dotted assignment, a form setValue
                              (through getAttribute or through getControl(...).getAttribute()), or Reflect.set /
                              defineProperty — directly or through a name bound to the column in the same resource, or
                              any alias of it (task 166 f1-v1 F3 and f1-v2 F-D; Find-PointerWrite in
                              scripts/common/Find-ClientPointerWrite.ps1, the detector the CI guard also runs).
                              Since task 166 f1 the shipped clients create the row WITHOUT the pointer and call the
                              BFF's POST /api/v1/documents/{id}/file, so the scan is satisfiable; a hit names the web
                              resource still carrying an old bundle. (p4b) -ClientNoLongerWritesPointers: the operator's
                              confirmation that no browser can still be running a CACHED older bundle (the scan sees only
                              what is deployed).
           ReportCatalog    — -BffWritesCatalogPointers: the BFF build that registers a catalog row WITHOUT the four
                              columns and stamps them app-only (task 166 f1) is deployed. The Reporting code page never
                              wrote them; until that BFF is live, POST /api/reporting/reports would be refused.
           DocumentNarrowingLinks — EVIDENCE: no deployed JavaScript / HTML web resource writes either link (a Web API
                              bind `sprk_RelatedCommunication@odata.bind` / `sprk_ParentDocument@odata.bind`, or a form
                              getAttribute('<column>').setValue). No client ever wrote them (task 188 scan 2026-10-20:
                              the only client uses are reads), so there is no cached-bundle confirmation.
      (p5) no sprk_fieldmappingrule, sprk_aitopicregistry or sprk_emailupdatefield row targets a locked column (maker
           configuration that writes outside the BFF; it would fail every write it drives once locked);
      (p6) the table is a ROOT component of -SolutionUniqueName with rootcomponentbehavior 0 (include all
           subcomponents), so the secured columns travel with the solution to every other environment;
      (p7) every column to lock exists (DocumentPointers: sprk_relocationpending, sprk_relocatedversions and
           sprk_graphitemidbound are created by scripts/Set-DocumentRelocationSchema.ps1 -Apply, task 166 f1-v1 / f1-v2
           and task 171 round 72).
      AccessMarkerColumns only (its (p3) is the writer rule above; its (p4) counts flagged users still outside the External
      unit (INFORMATIONAL: the other writers are internal users, who keep writing through their unit's team, and the BFF's
      app-only writers; task 191f notes list every writer and its identity) and needs -BffOmitsMarkersFromCallerCreates
      for -Apply: the operator's confirmation that the BFF build whose caller-identity creates (the assistant's
      email.draft) no longer NAME a marker is deployed — CallerWriteAuthorization refuses a caller create that names a
      field-secured column, for internal users too):
      (p8) exactly one business unit carries the External Collaborators name (-ExternalBusinessUnitName, else
           config/external-collaborator-role.json businessUnit.name, task 191d). Without it every flagged user still
           sits in an internal unit whose team writes the markers, so the lock would prove nothing;
      (p9) every table that carries sprk_accesspermission is in the target (a table added later fails until it is);
      (p10) -Apply only: each BFF application user holds System Administrator, so the BFF never reads a marker EMPTY in
           the seconds between securing a column and the grants (the BFF maps an empty Access Permission to "not
           Restricted"). Dry run and -Verify report it as INFO: in the steady state the writer profile gives the read;
      (p11) no profile but the writer (and System Administrator) already grants create or update on a marker — the
           shared reader included — so securing a column never leaves a stray write in place.
      sprk_isexternal is NEVER a target column (a masked flag reads blank, and blank is internal; task 114 decision 6):
      the script refuses to start if any target names it.

    STEPS (-Apply), per column, in this order:
      (a0) AccessMarkerColumns only, ONCE before any column: create the writer profile if absent, add the missing
          internal default teams and BFF users, read it back; stop with nothing secured if a member did not land;
      (a) secure the column (IsSecured = true);
      (b) IMMEDIATELY grant the reader profile read=4, then the writer profile read=4 create=4 update=4 (retried while
          the platform propagates the secured state, 0x8004f508). If a grant fails, the column is REVERTED to unsecured
          and the run stops — a secured column with no reader permission reads EMPTY for every non-administrator;
      (c) report any OTHER profile that can create or update the column (FAIL); list System Administrator holders
          (full field access by platform rule, informational);
      (d) publish the target's tables.

    LIVE ORDER (DocumentPointers): deploy the BFF that attaches files server-side → deploy the clients (code pages, PCFs,
    form scripts) that stop writing the pointer → THIS SCRIPT (dry run: p4a must be OK) → -ClientNoLongerWritesPointers
    -Apply → -Verify → scripts/Invoke-DocumentContainerMigration.ps1 (dry run → -Apply → -Verify) → set
    DocumentPointer__StrictDerivedContainer=true on the BFF (task 166 note §20).
    LIVE ORDER (ReportCatalog): deploy the BFF (task 166 f1) → set PowerBi__AllowedWorkspaces → THIS SCRIPT -Target
    ReportCatalog (dry run) → -BffWritesCatalogPointers -Apply → -Verify.
    LIVE ORDER (AccessMarkerColumns, design §3.19 step 4, task 191i): Set-ExternalCollaboratorRole.ps1 -Apply (the unit
    and role) → Set-RecordCreatorPersonSchema.ps1 -Apply (the reader on the new unit's team) → deploy the BFF whose
    caller creates no longer name a marker → THIS SCRIPT (dry run) → -BffOmitsMarkersFromCallerCreates -Apply (step (a0)
    first creates the writer profile and adds its members, then (a)-(d)) → -Verify. After every later SpaarkeMaster
    import, run -Verify: the solution source does not carry IsSecured for these columns, so an import may clear it;
    -Apply again restores it.

.PARAMETER EnvironmentUrl
    e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER BffApplicationIds
    Application (client) ids of the BFF's Dataverse application users (dev: 5967251e-171c-46fe-a6c2-ef843c90309d and,
    while it is still an application user, 1e40baad-e065-4aea-a8d4-4b7ab273458c). Per-customer input (D-13), never
    hard-coded.

.PARAMETER Target
    DocumentPointers (default), ReportCatalog, DocumentNarrowingLinks or AccessMarkerColumns.

.PARAMETER SolutionUniqueName
    The unmanaged solution carrying the profiles and the table. Default SpaarkeCore; SpaarkeMaster for
    AccessMarkerColumns (the one solution that carries every marker table with all subcomponents).

.PARAMETER ExternalBusinessUnitName
    AccessMarkerColumns only: the External Collaborators business unit's name. Default: businessUnit.name in
    -ExternalCollaboratorConfigPath (the one place task 191d defines it).

.PARAMETER ExternalCollaboratorConfigPath
    AccessMarkerColumns only: the External Collaborators definition. Default config/external-collaborator-role.json.

.PARAMETER BffOmitsMarkersFromCallerCreates
    AccessMarkerColumns only: the operator's confirmation for its (p4). Required with -Apply.

.PARAMETER ClientNoLongerWritesPointers
    DocumentPointers only: the operator's confirmation for (p4b). Required with -Apply.

.PARAMETER BffWritesCatalogPointers
    ReportCatalog only: the operator's confirmation for (p4). Required with -Apply.

.PARAMETER Apply
    Write mode. Without it the script never writes.

.PARAMETER Verify
    Read-only check with a pass/fail exit code (1 names each gap). Cannot be combined with -Apply.

.EXAMPLE
    .\Set-DocumentPointerFieldSecurity.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c
    Dry run: every precondition (including the deployed-web-resource scan) and step's state; zero writes.

.EXAMPLE
    .\Set-DocumentPointerFieldSecurity.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d -ClientNoLongerWritesPointers -Apply

.EXAMPLE
    .\Set-DocumentPointerFieldSecurity.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Target ReportCatalog `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d -BffWritesCatalogPointers -Apply

.EXAMPLE
    .\Set-DocumentPointerFieldSecurity.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Target AccessMarkerColumns `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Verify
    Through its named entry point: scripts/Set-AccessMarkerFieldSecurity.ps1.

.NOTES
    unified-access-control-r2 task 166 r1 (#1105), generalised to the report catalog and given its evidence check in
    task 166 f1. Modelled line for line on task 150's scripts/Set-SecureFlagFieldSecurity.ps1. Auth: the operator's own
    az CLI identity (System Administrator in the environment). No secrets. Live runs = main-session manual gates (task
    note §20).
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EnvironmentUrl,
    [Parameter(Mandatory)][string[]]$BffApplicationIds,
    [ValidateSet('DocumentPointers', 'ReportCatalog', 'DocumentNarrowingLinks', 'AccessMarkerColumns')][string]$Target = 'DocumentPointers',
    [string]$SolutionUniqueName = 'SpaarkeCore',
    [string]$ExternalBusinessUnitName,
    [string]$ExternalCollaboratorConfigPath,
    [switch]$ClientNoLongerWritesPointers,
    [switch]$BffWritesCatalogPointers,
    [switch]$BffOmitsMarkersFromCallerCreates,
    [switch]$Apply,
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
if ($Apply -and $Verify) { throw '-Apply and -Verify are separate modes; run -Apply first, then -Verify.' }
. (Join-Path $PSScriptRoot 'common/DataverseSolutionMembership.ps1')
. (Join-Path $PSScriptRoot 'common/OntologyWriterUnit.ps1')
if ($Target -eq 'ReportCatalog' -and $ClientNoLongerWritesPointers) { throw '-ClientNoLongerWritesPointers applies to -Target DocumentPointers only.' }
if ($Target -ne 'ReportCatalog' -and $BffWritesCatalogPointers) { throw '-BffWritesCatalogPointers applies to -Target ReportCatalog only.' }
if ($Target -in 'DocumentNarrowingLinks', 'AccessMarkerColumns' -and $ClientNoLongerWritesPointers) { throw '-ClientNoLongerWritesPointers applies to -Target DocumentPointers only.' }
if ($Target -ne 'AccessMarkerColumns' -and ($ExternalBusinessUnitName -or $ExternalCollaboratorConfigPath -or $BffOmitsMarkersFromCallerCreates)) { throw '-ExternalBusinessUnitName, -ExternalCollaboratorConfigPath and -BffOmitsMarkersFromCallerCreates apply to -Target AccessMarkerColumns only.' }
$BffApplicationIds = @($BffApplicationIds | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
foreach ($id in $BffApplicationIds) { if (-not [Guid]::TryParse($id, [ref][Guid]::Empty)) { throw "-BffApplicationIds: '$id' is not a GUID." } }
$Api = "$EnvironmentUrl/api/data/v9.2"

# ── Constants ───────────────────────────────────────────────────────────────────────────────────────────────
$Targets = @{
    DocumentPointers = @{ Table = 'sprk_document'; Columns = @('sprk_graphdriveid', 'sprk_graphitemid', 'sprk_graphitemidbound', 'sprk_relocationpending', 'sprk_relocatedversions'); What = 'the document pointers (incl. the item-id copy) and the relocation record (ledger and version record)' }
    ReportCatalog    = @{ Table = 'sprk_report'; Columns = @('sprk_pbi_reportid', 'sprk_workspaceid', 'sprk_datasetid', 'sprk_iscustom'); What = 'the report catalog pointers' }
    # Task 188 (D7 §6.3): the links ExternalDocumentNarrowing walks. The predicate reads them app-only on every surface, so a
    # mask never opens a document; (p2) still keeps users and forms reading them (defence in depth).
    DocumentNarrowingLinks = @{ Table = 'sprk_document'; Columns = @('sprk_relatedcommunication', 'sprk_parentdocument'); What = 'the document narrowing links (related communication, parent document)' }
    # Task 191f (#2179 design §3.6, owner Q1 = a): the access markers, one 'table.column' per pair. sprk_accesspermission on
    # EVERY table that carries it (read on spaarkedev1 2026-11-11: these eight; (p9) fails on a ninth), the communication's
    # internal-only flag and the thread's privacy. Its own writer profile (internal units' default teams + the BFF users).
    AccessMarkerColumns = @{
        Pairs             = @(
            'sprk_matter.sprk_accesspermission', 'sprk_project.sprk_accesspermission', 'sprk_workassignment.sprk_accesspermission',
            'sprk_document.sprk_accesspermission', 'sprk_communication.sprk_accesspermission', 'sprk_event.sprk_accesspermission',
            'sprk_todo.sprk_accesspermission', 'sprk_agreement.sprk_accesspermission',
            'sprk_communication.sprk_isinternalonly', 'sprk_communicationthread.sprk_privacystate')
        What              = 'the access markers (Access Permission on every table that carries it, internal-only, thread privacy)'
        WriterProfileName = 'Spaarke Access Marker Writers'
        WriterProfileId   = '60b9344e-f253-49cd-af1a-4be2cf52c3a1'
        WriterDescription = 'Read/Create/Update on the access markers ONLY: sprk_accesspermission on every table that carries it, sprk_communication.sprk_isinternalonly, sprk_communicationthread.sprk_privacystate. Members: every INTERNAL business unit default team (never the External Collaborators unit''s) and the BFF application user(s). unified-access-control-r2 task 191f, owner Q1 = a.'
    }
}
# A column that must never be field-secured, in any target: a masked sprk_isexternal reads blank, and blank is internal
# (task 114 decision 6; design §1.1). Checked before anything is read.
$NeverSecuredColumns = @('sprk_isexternal')
foreach ($name in $Targets.Keys) {
    $named = @($Targets[$name].Columns) + @($Targets[$name].Pairs | Where-Object { $_ } | ForEach-Object { ($_ -split '\.', 2)[1] })
    $hit = @($named | Where-Object { $_ -in $NeverSecuredColumns })
    if ($hit.Count -gt 0) { throw "Target $name names $($hit -join ', '), which is never field-secured (a masked flag reads blank, and blank is internal)." }
}
$TargetSpec = $Targets[$Target]
$IsMarkerTarget = $Target -eq 'AccessMarkerColumns'
# Every target as (table, column) pairs; the single-table targets keep their Table + Columns shape (parsed by
# DocumentBffOwnedColumnAppOnlyWriterTests).
$Pairs = if ($TargetSpec.Pairs) {
    @($TargetSpec.Pairs | ForEach-Object { $t, $c = $_ -split '\.', 2; [pscustomobject]@{ Table = $t; Column = $c } })
} else {
    @($TargetSpec.Columns | ForEach-Object { [pscustomobject]@{ Table = $TargetSpec.Table; Column = $_ } })
}
$TargetTables = @($Pairs | ForEach-Object { $_.Table } | Select-Object -Unique)
$Columns = @($Pairs | ForEach-Object { $_.Column } | Select-Object -Unique)
$ReaderProfileName = 'Spaarke BFF-Managed Field Readers'
$WriterProfileName = 'Spaarke BFF-Managed Field Writers'
if ($IsMarkerTarget) {
    $WriterProfileName = $TargetSpec.WriterProfileName
    # SpaarkeCore does not carry every marker table with all subcomponents (spaarkedev1 2026-11-11: sprk_agreement is not
    # in it; sprk_todo and sprk_communicationthread are there without rootcomponentbehavior 0). SpaarkeMaster, the source
    # in src/dataverse/solutions, carries all nine and the reader profile, so the lock travels with it.
    if (-not $PSBoundParameters.ContainsKey('SolutionUniqueName')) { $SolutionUniqueName = 'SpaarkeMaster' }
    if (-not $ExternalBusinessUnitName) {
        $configPath = if ($ExternalCollaboratorConfigPath) { $ExternalCollaboratorConfigPath } else { Join-Path $PSScriptRoot '..' 'config' 'external-collaborator-role.json' }
        if (-not (Test-Path -LiteralPath $configPath)) { throw "The External Collaborators definition '$configPath' is missing (task 191d). Pass -ExternalBusinessUnitName, or -ExternalCollaboratorConfigPath." }
        $ExternalBusinessUnitName = (Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json).businessUnit.name
        if (-not $ExternalBusinessUnitName) { throw "'$configPath' names no businessUnit.name." }
    }
}
# Created (already secured) by scripts/Set-DocumentRelocationSchema.ps1 — this script grants their profiles. The item-id
# copy sprk_graphitemidbound joined them in task 171 (owner round 72 item 1): sprk_graphitemid itself cannot be secured.
$RelocationRecordColumns = @('sprk_relocationpending', 'sprk_relocatedversions', 'sprk_graphitemidbound')

# A CLIENT write of a locked document column in deployed JavaScript: Find-PointerWrite, from the ONE detector file
# tests/Spaarke.ArchTests/ClientDocumentPointerWriteGuardTests.cs runs through pwsh over every write and read case of the
# CI guard (task 166 f1-v2, F-D), so this scan and the source-tree guard cannot drift apart unseen.
. (Join-Path $PSScriptRoot 'common/Find-ClientPointerWrite.ps1')

$token = az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv 2>$null
if (-not $token) { throw "No Dataverse token for $EnvironmentUrl. Run 'az login' and retry." }
$headers = @{
    Authorization      = "Bearer $token"
    Accept             = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'Content-Type'     = 'application/json; charset=utf-8'
}
function Invoke-DvGet([string]$Path, [hashtable]$Extra = @{}) {
    $h = $headers.Clone(); foreach ($k in $Extra.Keys) { $h[$k] = $Extra[$k] }
    $uri = if ($Path -match '^https?://') { $Path } else { "$Api/$Path" }
    Invoke-RestMethod -Uri $uri -Headers $h -Method Get
}
function Invoke-DvWrite([string]$Method, [string]$Path, $Body, [hashtable]$Extra = @{}) {
    $h = $headers.Clone(); foreach ($k in $Extra.Keys) { $h[$k] = $Extra[$k] }
    $json = if ($null -eq $Body) { $null } else { $Body | ConvertTo-Json -Depth 20 -Compress }
    Invoke-RestMethod -Uri "$Api/$Path" -Headers $h -Method $Method -Body $json
}

$IsDryRun = -not $Apply.IsPresent
$gaps = [System.Collections.Generic.List[string]]::new()
function Report([string]$State, [string]$What) {
    $color = switch ($State) { 'OK' { 'Green' } 'MISSING' { 'Yellow' } 'WOULD' { 'Cyan' } 'DONE' { 'Green' } 'INFO' { 'Gray' } default { 'Red' } }
    Write-Host ("  {0,-8} {1}" -f $State, $What) -ForegroundColor $color
    if ($State -in 'MISSING', 'FAIL') { $gaps.Add($What) }
}

$org = (Invoke-DvGet 'organizations?$select=name').value[0].name
Write-Host "Environment : $EnvironmentUrl (org '$org')"
Write-Host "Target      : $Target ($(($Pairs | ForEach-Object { "$($_.Table).$($_.Column)" }) -join ', '))"
Write-Host ("Mode        : {0}" -f $(if ($Verify) { 'VERIFY (read-only)' } elseif ($IsDryRun) { 'DRY RUN (no writes)' } else { 'APPLY' }))

# ── PRECONDITIONS ───────────────────────────────────────────────────────────────────────────────────────────
Write-Host "`nPreconditions"
function Get-Profile([string]$Name) {
    @((Invoke-DvGet ("fieldsecurityprofiles?`$select=fieldsecurityprofileid,name&`$filter=name eq '$Name'" +
        '&$expand=teamprofiles_association($select=teamid,name,isdefault),systemuserprofiles_association($select=systemuserid,fullname,applicationid)')).value)
}
$readers = Get-Profile $ReaderProfileName
$writers = Get-Profile $WriterProfileName
$reader = $null; $writer = $null
foreach ($pair in @(@{ Rows = $readers; Name = $ReaderProfileName; Own = $false }, @{ Rows = $writers; Name = $WriterProfileName; Own = $IsMarkerTarget })) {
    if ($pair.Rows.Count -eq 1) { Report 'OK' "(p1) profile '$($pair.Name)' exists" }
    elseif ($pair.Own -and $pair.Rows.Count -eq 0) {
        # AccessMarkerColumns' own writer: created by step (a0) of -Apply, never by hand.
        if ($Verify) { Report 'MISSING' "(p1) profile '$($pair.Name)' — run Set-AccessMarkerFieldSecurity.ps1 -Apply" }
        else { Report 'WOULD' "(p1) create profile '$($pair.Name)' ($($TargetSpec.WriterProfileId)) in $SolutionUniqueName (step a0)" }
    }
    elseif ($pair.Own) { Report 'FAIL' "(p1) $($pair.Rows.Count) profiles named '$($pair.Name)' — exactly one is allowed; remove the extra by hand" }
    else { Report 'FAIL' "(p1) $($pair.Rows.Count) profile(s) named '$($pair.Name)' — exactly one is required; create it with scripts/Set-RecordCreatorPersonSchema.ps1 -Apply (this script never creates it)" }
}
if ($readers.Count -eq 1) { $reader = $readers[0] }
if ($writers.Count -eq 1) { $writer = $writers[0] }
if ($IsMarkerTarget -and $writer -and $writer.fieldsecurityprofileid.ToString() -ne $TargetSpec.WriterProfileId) {
    # The fixed id is the one the solution source will carry: a profile made by hand under another id would be duplicated
    # by the next import.
    Report 'FAIL' "(p1) profile '$WriterProfileName' has id $($writer.fieldsecurityprofileid), not the fixed id $($TargetSpec.WriterProfileId) — it was not made by this script; remove it by hand"
}

$solution = @((Invoke-DvGet "solutions?`$select=solutionid&`$filter=uniquename eq '$SolutionUniqueName'").value) | Select-Object -First 1
if (-not $solution) { Report 'FAIL' "(p1) solution '$SolutionUniqueName' not found" }
else {
    # Solution membership through the ONE shared helper (scripts/common/DataverseSolutionMembership.ps1; every page read).
    $membership = Get-DvSolutionMembership -Api $Api -Headers $headers -SolutionId $solution.solutionid
    foreach ($p in @($reader, $writer) | Where-Object { $_ }) {
        if (Test-DvInSolution -Membership $membership -ComponentId $p.fieldsecurityprofileid) { Report 'OK' "(p1) '$($p.name)' is in $SolutionUniqueName" }
        elseif ($IsMarkerTarget -and $p -eq $writer) { Report 'FAIL' "(p1) '$($p.name)' is not in $SolutionUniqueName — add it by hand (AddSolutionComponent, type 70)" }
        else { Report 'FAIL' "(p1) '$($p.name)' is not in $SolutionUniqueName — Set-RecordCreatorPersonSchema.ps1 -Apply adds it" }
    }

    # (p6) each table is a root component with rootcomponentbehavior 0, so the secured columns ship with the solution.
    foreach ($tableName in $TargetTables) {
        $entityMeta = Invoke-DvGet "EntityDefinitions(LogicalName='$tableName')?`$select=MetadataId"
        $tableIn = Test-DvInSolution -Membership $membership -ComponentId $entityMeta.MetadataId
        if (-not $tableIn) { Report 'FAIL' "(p6) $tableName is not a component of $SolutionUniqueName — the secured columns would not travel" }
        elseif (-not $membership.TablesWithSubcomponents.Contains($entityMeta.MetadataId.ToString())) { Report 'FAIL' "(p6) $tableName is in $SolutionUniqueName without rootcomponentbehavior 0 (include all subcomponents), which is required" }
        else { Report 'OK' "(p6) $tableName is a root component of $SolutionUniqueName (rootcomponentbehavior 0)" }
    }
}

$allDefaultTeams = @((Invoke-DvGet 'teams?$select=teamid,name,_businessunitid_value&$filter=isdefault eq true&$expand=businessunitid($select=name)').value)
if ($allDefaultTeams.Count -eq 0) { Report 'FAIL' '(p2) no business-unit default team was read — the query is wrong' }
# Task 267 (#2618): every default team but the ontology writer unit's belongs on the reader profile; that one must be in NO
# field-security profile (H7b S24), so finding it there is a FAIL.
$defaultTeamSplit = Split-DvDefaultTeamsByOntologyWriterUnit -Api $Api -Headers $headers -DefaultTeams $allDefaultTeams
$defaultTeams = $defaultTeamSplit.ReaderTeams
if ($reader) {
    $onReader = @($reader.teamprofiles_association | ForEach-Object { $_.teamid.ToString().ToLowerInvariant() })
    foreach ($t in $defaultTeamSplit.WriterUnitTeams) {
        if ($onReader -contains $t.teamid.ToString().ToLowerInvariant()) { Report 'FAIL' "(p2) default team '$($t.name)' of the '$OntologyWriterBusinessUnitName' business unit is on the reader profile -- the ontology writer's unit team must be in NO field-security profile (H7b S24 refuses it); remove it by hand" }
        else { Report 'OK' "(p2) the '$OntologyWriterBusinessUnitName' business unit's default team is not on the reader profile" }
    }
    foreach ($t in $defaultTeams) {
        if ($onReader -contains $t.teamid.ToString().ToLowerInvariant()) { Report 'OK' "(p2) reader profile on default team '$($t.name)' (BU '$($t.businessunitid.name)')" }
        else { Report 'FAIL' "(p2) default team '$($t.name)' (BU '$($t.businessunitid.name)') is NOT on the reader profile — its users would read the columns EMPTY; run Set-RecordCreatorPersonSchema.ps1 -Apply" }
    }
}

$bffUsers = @()
foreach ($appId in $BffApplicationIds) {
    $u = @((Invoke-DvGet "systemusers?`$select=systemuserid,fullname&`$filter=applicationid eq $appId").value) | Select-Object -First 1
    if (-not $u) { Report 'FAIL' "(p3) no application user for appId $appId in this environment"; continue }
    $bffUsers += $u
}
# AccessMarkerColumns: who the writer profile must hold (internal default teams + BFF users), who it must never hold.
$PendingWriterTeams = [System.Collections.Generic.List[object]]::new()
$PendingWriterUsers = [System.Collections.Generic.List[object]]::new()
if ($IsMarkerTarget) {
    # (p8) the External Collaborators unit (task 191d). Its default team reads the markers (p2) and never writes them.
    $escaped = $ExternalBusinessUnitName.Replace("'", "''")
    $externalUnits = @((Invoke-DvGet "businessunits?`$select=businessunitid,name&`$filter=name eq '$escaped'").value)
    $externalUnitId = $null
    if ($externalUnits.Count -eq 1) {
        $externalUnitId = $externalUnits[0].businessunitid.ToString().ToLowerInvariant()
        Report 'OK' "(p8) business unit '$ExternalBusinessUnitName' exists"
    }
    elseif ($externalUnits.Count -eq 0) { Report 'FAIL' "(p8) no business unit named '$ExternalBusinessUnitName' — run scripts/Set-ExternalCollaboratorRole.ps1 -Apply first (task 191d): until flagged users sit in it, they sit in an internal unit whose team writes the markers" }
    else { Report 'FAIL' "(p8) $($externalUnits.Count) business units named '$ExternalBusinessUnitName' — exactly one may exist; which one holds the flagged users is not guessable" }
    $isExternalTeam = { param($t) $externalUnitId -and $t._businessunitid_value -and $t._businessunitid_value.ToString().ToLowerInvariant() -eq $externalUnitId }
    $externalTeams = @($allDefaultTeams | Where-Object { & $isExternalTeam $_ })
    $internalTeams = @($defaultTeams | Where-Object { -not (& $isExternalTeam $_) })
    if ($externalUnitId -and $externalTeams.Count -eq 0) { Report 'FAIL' "(p8) no default team of '$ExternalBusinessUnitName' was read — the query is wrong" }

    $writerTeamIds = @($writer.teamprofiles_association | Where-Object { $_ } | ForEach-Object { $_.teamid.ToString().ToLowerInvariant() })
    $writerUserIds = @($writer.systemuserprofiles_association | Where-Object { $_ } | ForEach-Object { $_.systemuserid.ToString().ToLowerInvariant() })
    foreach ($t in $internalTeams) {
        $label = "internal default team '$($t.name)' (BU '$($t.businessunitid.name)')"
        if ($writerTeamIds -contains $t.teamid.ToString().ToLowerInvariant()) { Report 'OK' "(p3) $label holds the writer profile" }
        elseif ($Verify) { Report 'MISSING' "(p3) $label is NOT on the writer profile — its users cannot change a locked marker; run -Apply" }
        else { Report 'WOULD' "(p3) add $label to '$WriterProfileName' (step a0)"; $PendingWriterTeams.Add($t) }
    }
    foreach ($t in $externalTeams) {
        if ($writerTeamIds -contains $t.teamid.ToString().ToLowerInvariant()) { Report 'FAIL' "(p3) the '$ExternalBusinessUnitName' unit's default team '$($t.name)' holds the WRITER profile — every flagged user could lower a marker; remove it by hand (this script never removes a member)" }
        else { Report 'OK' "(p3) the '$ExternalBusinessUnitName' unit's default team does not hold the writer profile" }
    }
    foreach ($t in $defaultTeamSplit.WriterUnitTeams) {
        if ($writerTeamIds -contains $t.teamid.ToString().ToLowerInvariant()) { Report 'FAIL' "(p3) the '$OntologyWriterBusinessUnitName' unit's default team holds the writer profile — it must hold no field-security profile (H7b S24); remove it by hand" }
    }
    $knownTeamIds = @($allDefaultTeams | ForEach-Object { $_.teamid.ToString().ToLowerInvariant() })
    foreach ($tm in @($writer.teamprofiles_association | Where-Object { $_ -and $_.teamid.ToString().ToLowerInvariant() -notin $knownTeamIds })) {
        Report 'FAIL' "(p3) writer profile has TEAM '$($tm.name)', which is not a business unit's default team — every member could lower a marker; remove it by hand"
    }
    $allowedUsers = @($bffUsers | ForEach-Object { $_.systemuserid.ToString().ToLowerInvariant() })
    foreach ($u in $bffUsers) {
        if ($writerUserIds -contains $u.systemuserid.ToString().ToLowerInvariant()) { Report 'OK' "(p3) BFF app user '$($u.fullname)' holds the writer profile (explicitly)" }
        elseif ($Verify) { Report 'MISSING' "(p3) BFF app user '$($u.fullname)' is NOT on the writer profile — the BFF reads and writes the markers through it wherever it lacks System Administrator; run -Apply" }
        else { Report 'WOULD' "(p3) add BFF app user '$($u.fullname)' to '$WriterProfileName' (step a0)"; $PendingWriterUsers.Add($u) }
    }
    foreach ($m in @($writer.systemuserprofiles_association | Where-Object { $_ -and $_.systemuserid.ToString().ToLowerInvariant() -notin $allowedUsers })) {
        $kind = if ($m.applicationid) { "an application user (appId $($m.applicationid)) not in -BffApplicationIds" } else { 'a user added by name (the writer is held through internal default teams only)' }
        Report 'FAIL' "(p3) writer profile member '$($m.fullname)' is $kind — remove it by hand"
    }
    if ($writer) {
        # The writer profile holds the target's columns and nothing else.
        $targetKeys = @($Pairs | ForEach-Object { "$($_.Table).$($_.Column)" })
        foreach ($r in @((Invoke-DvGet "fieldpermissions?`$select=entityname,attributelogicalname&`$filter=_fieldsecurityprofileid_value eq $($writer.fieldsecurityprofileid)").value |
                Where-Object { "$($_.entityname).$($_.attributelogicalname)" -notin $targetKeys })) {
            Report 'FAIL' "(p3) '$WriterProfileName' also grants $($r.entityname).$($r.attributelogicalname) — it may hold the access markers only"
        }
    }

    # (p4) INFORMATIONAL: the other writers are internal users (forms, Web API, the BFF routes that write as the caller) —
    # they keep writing through their unit's team. A flagged user outside the External unit is still one of them.
    $flagged = @((Invoke-DvGet "systemusers?`$select=systemuserid,_businessunitid_value&`$filter=sprk_isexternal eq true and isdisabled eq false").value)
    $outside = @($flagged | Where-Object { -not $externalUnitId -or -not $_._businessunitid_value -or $_._businessunitid_value.ToString().ToLowerInvariant() -ne $externalUnitId })
    Report 'INFO' "(p4) $($flagged.Count) enabled flagged-external user(s); $($outside.Count) outside '$ExternalBusinessUnitName' — each still writes the markers through its internal unit's team until the placement job (task 191e) moves it"
    # (p4) the BFF's caller-identity creates no longer NAME a marker. CallerWriteAuthorization refuses a caller create that
    # names a field-secured column, for internal users too; the assistant's email.draft named the inherited Access
    # Permission on every draft filed under a matter, project or work assignment (task 191f review finding 1). Until the
    # BFF build that leaves it to RecordCreationService's step-10 stamp is deployed, locking breaks those drafts.
    if ($BffOmitsMarkersFromCallerCreates) { Report 'OK' '(p4) operator confirms the BFF build whose caller creates (email.draft) no longer name a marker is deployed' }
    elseif ($Apply) { Report 'FAIL' '(p4) -BffOmitsMarkersFromCallerCreates not passed: an older BFF names the inherited Access Permission on filed email drafts, and every such draft would be refused once locked' }
    else { Report 'INFO' '(p4) -BffOmitsMarkersFromCallerCreates is required for -Apply' }

    # (p11) no OTHER profile already grants create or update on a marker (the shared reader included): it would keep that
    # write the moment the column is secured — the External unit's team included. Checked before anything is secured.
    foreach ($pr in $Pairs) {
        $grants = @((Invoke-DvGet "fieldpermissions?`$select=canupdate,cancreate&`$expand=fieldsecurityprofileid(`$select=name)&`$filter=entityname eq '$($pr.Table)' and attributelogicalname eq '$($pr.Column)' and (canupdate eq 4 or cancreate eq 4)").value |
            Where-Object { $_.fieldsecurityprofileid.name -notin $WriterProfileName, 'System Administrator' })
        foreach ($g in $grants) { Report 'FAIL' "(p11) profile '$($g.fieldsecurityprofileid.name)' already grants create/update on $($pr.Table).$($pr.Column) — only '$WriterProfileName' may; remove that permission by hand" }
    }

    # (p9) every table that carries sprk_accesspermission is in the target.
    $carrying = @((Invoke-DvGet "EntityDefinitions?`$select=LogicalName&`$expand=Attributes(`$select=LogicalName;`$filter=LogicalName eq 'sprk_accesspermission')").value |
        Where-Object { @($_.Attributes).Count -gt 0 } | ForEach-Object { $_.LogicalName })
    $listed = @($Pairs | Where-Object { $_.Column -eq 'sprk_accesspermission' } | ForEach-Object { $_.Table })
    if ($carrying.Count -eq 0) { Report 'FAIL' '(p9) no table carrying sprk_accesspermission was read — the query is wrong' }
    foreach ($t in @($carrying | Where-Object { $_ -notin $listed })) { Report 'FAIL' "(p9) $t carries sprk_accesspermission but is not in the target — add it to AccessMarkerColumns (a flagged user could lower it there)" }
    if ($carrying.Count -gt 0 -and @($carrying | Where-Object { $_ -notin $listed }).Count -eq 0) { Report 'OK' "(p9) every table that carries sprk_accesspermission is in the target ($($carrying.Count))" }

    # (p10) the BFF reads the markers unmasked during the seconds between securing a column and the grants.
    $sysAdminTemplate = '627090ff-40a3-4053-8790-584edc5be201'
    foreach ($u in $bffUsers) {
        $direct = @((Invoke-DvGet "systemusers($($u.systemuserid))/systemuserroles_association?`$select=name,_roletemplateid_value").value)
        $viaTeams = @((Invoke-DvGet "systemusers($($u.systemuserid))/teammembership_association?`$select=teamid&`$expand=teamroles_association(`$select=name,_roletemplateid_value)").value |
            ForEach-Object { @($_.teamroles_association) })
        $isAdmin = @(@($direct) + @($viaTeams) | Where-Object { $_ -and ("$($_._roletemplateid_value)" -eq $sysAdminTemplate -or $_.name -eq 'System Administrator') }).Count -gt 0
        if ($isAdmin) { Report 'OK' "(p10) BFF app user '$($u.fullname)' holds System Administrator — it reads the markers unmasked while -Apply secures them" }
        elseif ($Apply) { Report 'FAIL' "(p10) BFF app user '$($u.fullname)' does not hold System Administrator — between securing a column and the grants it would read the markers EMPTY (an empty Access Permission reads as not Restricted)" }
        else { Report 'INFO' "(p10) BFF app user '$($u.fullname)' does not hold System Administrator — -Apply refuses until it does (the writer profile gives it the read once -Apply is done)" }
    }
}
elseif ($writer) {
    $members = @($writer.systemuserprofiles_association)
    $memberIds = @($members | ForEach-Object { $_.systemuserid.ToString().ToLowerInvariant() })
    $allowed = @($bffUsers | ForEach-Object { $_.systemuserid.ToString().ToLowerInvariant() })
    foreach ($u in $bffUsers) {
        if ($memberIds -contains $u.systemuserid.ToString().ToLowerInvariant()) { Report 'OK' "(p3) BFF app user '$($u.fullname)' is a writer (explicitly)" }
        else { Report 'FAIL' "(p3) BFF app user '$($u.fullname)' is NOT on the writer profile — the BFF could not write the columns wherever it lacks System Administrator; run Set-RecordCreatorPersonSchema.ps1 -Apply" }
    }
    foreach ($m in $members | Where-Object { $_.systemuserid.ToString().ToLowerInvariant() -notin $allowed }) {
        $kind = if ($m.applicationid) { "an application user (appId $($m.applicationid)) not in -BffApplicationIds" } else { 'a HUMAN user' }
        Report 'FAIL' "(p3) writer profile member '$($m.fullname)' is $kind — it could re-point rows"
    }
    foreach ($tm in @($writer.teamprofiles_association)) { Report 'FAIL' "(p3) writer profile has TEAM '$($tm.name)' — every member could re-point rows" }
}

# (p5) no maker-authored configuration row writes a locked column.
$configuredWriters = 0
foreach ($ch in @(@{ T = 'sprk_fieldmappingrule'; C = 'sprk_targetfield' }, @{ T = 'sprk_aitopicregistry'; C = 'sprk_targetfield' }, @{ T = 'sprk_emailupdatefield'; C = 'sprk_targetfieldlogicalname' })) {
    $set = (Invoke-DvGet "EntityDefinitions(LogicalName='$($ch.T)')?`$select=EntitySetName").EntitySetName
    foreach ($column in $Columns) {
        foreach ($row in @((Invoke-DvGet "$set`?`$select=$($ch.T)id&`$filter=$($ch.C) eq '$column'").value)) {
            $configuredWriters++
            Report 'FAIL' "(p5) $($ch.T) row $($row."$($ch.T)id") targets $column — remove or retarget it; configuration that writes a column outside its writers is refused once it is locked"
        }
    }
}
if ($configuredWriters -eq 0) { Report 'OK' "(p5) no field-mapping rule, AI topic-registry row or email update field targets the locked columns" }

# (p7) every column to lock exists.
$missingColumns = 0
foreach ($p in $Pairs) {
    try { Invoke-DvGet "EntityDefinitions(LogicalName='$($p.Table)')/Attributes(LogicalName='$($p.Column)')?`$select=LogicalName" | Out-Null }
    catch {
        $missingColumns++
        $hint = if ($p.Column -in $RelocationRecordColumns) { ' — run scripts/Set-DocumentRelocationSchema.ps1 -Apply first' } else { '' }
        Report 'FAIL' "(p7) $($p.Table).$($p.Column) does not exist in this environment$hint"
    }
}
if ($missingColumns -eq 0) { Report 'OK' "(p7) every column to lock exists ($(($Pairs | ForEach-Object { "$($_.Table).$($_.Column)" }) -join ', '))" }

# (p4) nothing outside the BFF still writes the columns.
if ($Target -eq 'DocumentPointers') {
    # (p4a) EVIDENCE: scan every deployed JavaScript (3) and HTML (1) web resource — code pages, form scripts, PCF bundles.
    $scanned = 0; $hits = 0
    $next = "webresourceset?`$select=name,content,webresourcetype&`$filter=webresourcetype eq 1 or webresourcetype eq 3"
    while ($next) {
        $page = Invoke-DvGet $next @{ Prefer = 'odata.maxpagesize=25' }
        foreach ($wr in @($page.value)) {
            $scanned++
            if (-not $wr.content) { continue }
            $text = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($wr.content))
            $write = Find-PointerWrite $text
            if ($write) {
                $hits++
                Report 'FAIL' "(p4a) web resource '$($wr.name)' still WRITES a locked document column ('$write') — deploy its rebuilt bundle (task 166 f1) before locking"
            }
        }
        $next = $page.'@odata.nextLink'
    }
    if ($scanned -eq 0) { Report 'FAIL' '(p4a) no web resource was read — the scan is wrong, and an empty scan proves nothing' }
    elseif ($hits -eq 0) { Report 'OK' "(p4a) none of the $scanned deployed JavaScript / HTML web resources writes a document pointer" }

    if ($ClientNoLongerWritesPointers) { Report 'OK' '(p4b) operator confirms no browser still runs a cached older bundle' }
    elseif ($Apply) { Report 'FAIL' '(p4b) -ClientNoLongerWritesPointers not passed: a cached older bundle would still write the pointer, and every such upload would be refused once locked' }
    else { Report 'INFO' '(p4b) -ClientNoLongerWritesPointers is required for -Apply' }
}
elseif ($Target -eq 'DocumentNarrowingLinks') {
    # (p4) EVIDENCE: no deployed JavaScript (3) / HTML (1) web resource writes either link. Reads are fine (a parent map, a
    # filter); a write is a Web API bind on the navigation property or a form setValue on the column.
    $writePatterns = @(
        '(?i)sprk_(RelatedCommunication|ParentDocument)@odata\.bind',
        '(?i)getAttribute\(\s*[''"`](sprk_relatedcommunication|sprk_parentdocument)[''"`]\s*\)\s*\.setValue'
    )
    $scanned = 0; $hits = 0
    $next = "webresourceset?`$select=name,content,webresourcetype&`$filter=webresourcetype eq 1 or webresourcetype eq 3"
    while ($next) {
        $page = Invoke-DvGet $next @{ Prefer = 'odata.maxpagesize=25' }
        foreach ($wr in @($page.value)) {
            $scanned++
            if (-not $wr.content) { continue }
            $text = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($wr.content))
            foreach ($pattern in $writePatterns) {
                if ($text -match $pattern) {
                    $hits++
                    Report 'FAIL' "(p4) web resource '$($wr.name)' WRITES a narrowing link ('$($Matches[0])') — once locked that write is refused"
                    break
                }
            }
        }
        $next = $page.'@odata.nextLink'
    }
    if ($scanned -eq 0) { Report 'FAIL' '(p4) no web resource was read — the scan is wrong, and an empty scan proves nothing' }
    elseif ($hits -eq 0) { Report 'OK' "(p4) none of the $scanned deployed JavaScript / HTML web resources writes sprk_relatedcommunication or sprk_parentdocument" }
}
elseif ($Target -eq 'ReportCatalog') {
    if ($BffWritesCatalogPointers) { Report 'OK' '(p4) operator confirms the BFF build that stamps the catalog pointer app-only (task 166 f1) is deployed' }
    elseif ($Apply) { Report 'FAIL' '(p4) -BffWritesCatalogPointers not passed: an older BFF registers catalog rows WITH the pointer as the caller, which the lock refuses' }
    else { Report 'INFO' '(p4) -BffWritesCatalogPointers is required for -Apply' }
}

if ($Apply -and $gaps.Count -gt 0) {
    Write-Host "`nREFUSED: -Apply needs every precondition to pass ($($gaps.Count) gap(s)). Nothing was written." -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

# ── (a0) AccessMarkerColumns: the writer profile and its members, BEFORE any column is secured ───────────────
# Membership on a profile with no permission row is inert, so adding it first never widens anything; securing first would
# leave internal users unable to edit a marker until the add. Adds only; a wrong member was a FAIL above (refused).
if ($Apply -and $IsMarkerTarget) {
    Write-Host "`n(a0) $WriterProfileName"
    if (-not $writer) {
        Invoke-DvWrite POST 'fieldsecurityprofiles' @{ fieldsecurityprofileid = $TargetSpec.WriterProfileId; name = $WriterProfileName; description = $TargetSpec.WriterDescription } @{ 'MSCRM.SolutionUniqueName' = $SolutionUniqueName } | Out-Null
        Report 'DONE' "created profile '$WriterProfileName' ($($TargetSpec.WriterProfileId)) in $SolutionUniqueName"
    }
    $writerId = $TargetSpec.WriterProfileId
    foreach ($u in $PendingWriterUsers) {
        Invoke-DvWrite POST "fieldsecurityprofiles($writerId)/systemuserprofiles_association/`$ref" @{ '@odata.id' = "$Api/systemusers($($u.systemuserid))" } | Out-Null
        Report 'DONE' "added BFF app user '$($u.fullname)' to $WriterProfileName"
    }
    foreach ($t in $PendingWriterTeams) {
        Invoke-DvWrite POST "fieldsecurityprofiles($writerId)/teamprofiles_association/`$ref" @{ '@odata.id' = "$Api/teams($($t.teamid))" } | Out-Null
        Report 'DONE' "added default team '$($t.name)' (BU '$($t.businessunitid.name)') to $WriterProfileName"
    }
    # Read back before anything is secured: a member that did not land would lose (a team) or never gain (a BFF user) the
    # edit the moment its column is secured.
    $writer = @(Get-Profile $WriterProfileName) | Select-Object -First 1
    $backTeams = @($writer.teamprofiles_association | Where-Object { $_ } | ForEach-Object { $_.teamid.ToString().ToLowerInvariant() })
    $backUsers = @($writer.systemuserprofiles_association | Where-Object { $_ } | ForEach-Object { $_.systemuserid.ToString().ToLowerInvariant() })
    $notLanded = @(@($internalTeams | Where-Object { $_.teamid.ToString().ToLowerInvariant() -notin $backTeams } | ForEach-Object { "team '$($_.name)'" }) +
        @($bffUsers | Where-Object { $_.systemuserid.ToString().ToLowerInvariant() -notin $backUsers } | ForEach-Object { "BFF app user '$($_.fullname)'" }))
    if (-not $writer -or $notLanded.Count -gt 0) {
        Write-Host "`nSTOPPED before securing anything: the writer profile read back without $(if ($writer) { $notLanded -join ', ' } else { 'itself' }). Nothing is secured; re-run -Apply." -ForegroundColor Red
        exit 1
    }
    Report 'OK' "'$WriterProfileName' read back with every internal default team ($($internalTeams.Count)) and BFF app user ($($bffUsers.Count))"
}

# ── (a)+(b) SECURE, then grant at once ──────────────────────────────────────────────────────────────────────
Write-Host "`n(a)+(b) Field-level security"
function Get-Permission([string]$ProfileId, [string]$Column) {
    @((Invoke-DvGet "fieldpermissions?`$select=fieldpermissionid,canread,cancreate,canupdate&`$filter=_fieldsecurityprofileid_value eq $ProfileId and entityname eq '$Table' and attributelogicalname eq '$Column'").value) | Select-Object -First 1
}
function Grant-Permission([string]$ProfileId, [string]$Column, [int]$Create) {
    for ($attempt = 1; ; $attempt++) {
        try {
            Invoke-DvWrite POST 'fieldpermissions' @{
                entityname = $Table; attributelogicalname = $Column
                canread = 4; cancreate = $Create; canupdate = $Create
                'fieldsecurityprofileid@odata.bind' = "/fieldsecurityprofiles($ProfileId)"
            } | Out-Null
            return
        } catch {
            $notYetSecured = "$($_.ErrorDetails.Message) $($_.Exception.Message)" -match '0x8004f508'
            if (-not $notYetSecured -or $attempt -ge 12) { throw }
            Write-Host "    $Table.$Column not yet seen as secured by the field-permission service; retrying ($attempt/12)..."
            Start-Sleep -Seconds 5
        }
    }
}
function Set-ColumnSecured([string]$Column, [bool]$Value) {
    $attrPath = "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')"
    $typed = Invoke-DvGet $attrPath
    $typed.IsSecured = $Value
    try {
        Invoke-DvWrite PUT $attrPath $typed @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null
    } catch {
        # As scripts/Set-SecureFlagFieldSecurity.ps1 (task 150 r1): a Boolean column's definition carries its OptionSet as a
        # navigation property the plain GET omits, and the PUT may need it (sprk_communication.sprk_isinternalonly). Retry
        # ONCE with the Boolean cast and the OptionSet expanded; any other type, or a second refusal, throws. Either way the
        # failed PUT left IsSecured unchanged, so nothing is masked.
        if ($typed.'@odata.type' -ne '#Microsoft.Dynamics.CRM.BooleanAttributeMetadata') { throw }
        Write-Host "    PUT of $Table.$Column refused without its OptionSet ($($_.ErrorDetails.Message)); retrying once with the Boolean cast and `$expand=OptionSet..."
        $typed = Invoke-DvGet "$attrPath/Microsoft.Dynamics.CRM.BooleanAttributeMetadata?`$expand=OptionSet"
        $typed.IsSecured = $Value
        Invoke-DvWrite PUT $attrPath $typed @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null
    }
}
function Write-MaskedRecovery([string]$Column, [string]$Cause) {
    Write-Host ''
    Write-Host "  !!! RECOVERY REQUIRED NOW — $Table.$Column may be field-secured WITHOUT its reader permission !!!" -ForegroundColor Red
    $consequence = switch ($Target) {
        'DocumentNarrowingLinks' { 'users and forms then see no link; the narrowing itself reads app-only' }
        'AccessMarkerColumns' { 'a Restricted or internal-only record then LOOKS ordinary on forms, ribbons and grids; the BFF reads it as System Administrator' }
        default { 'files / reports stop opening' }
    }
    Write-Host "  Every non-administrator then reads $Column EMPTY ($consequence)." -ForegroundColor Red
    Write-Host "  Cause: $Cause" -ForegroundColor Red
    Write-Host '  Do ONE of these, now: (1) fix the cause and re-run -Apply (it grants only what is missing on an already' -ForegroundColor Red
    Write-Host "  secured column), then -Verify; (2) or clear 'Enable column security' on $Table.$Column and publish $Table." -ForegroundColor Red
}

foreach ($pairToLock in $Pairs) {
    # Get-Permission, Grant-Permission, Set-ColumnSecured and Write-MaskedRecovery read $Table from this scope.
    $Table = $pairToLock.Table
    $column = $pairToLock.Column
    # Dataverse cannot field-secure a column in an alternate key (0x80060896); sprk_graphitemid_uk is load-bearing (dedup, Compose upsert). Owner decision 2026-10-06.
    if ($Table -eq 'sprk_document' -and $column -eq 'sprk_graphitemid') { Report 'INFO' 'sprk_graphitemid not field-secured: part of alternate key sprk_graphitemid_uk (Dataverse 0x80060896); re-pointing is bounded by the locked drive id + the unique key + the BFF strict pointer check'; continue }
    $attr = try { Invoke-DvGet "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$column')?`$select=IsSecured" } catch { $null }
    if (-not $attr) { continue }   # (p7) reported it; -Apply was refused above
    $specs = @(@{ P = $reader; Name = $ReaderProfileName; Create = 0 }, @{ P = $writer; Name = $WriterProfileName; Create = 4 })

    if ($attr.IsSecured) {
        Report 'OK' "$Table.$column is field-secured"
        if ($Apply) {
            try { foreach ($s in $specs) { if (-not (Get-Permission $s.P.fieldsecurityprofileid $column)) { Grant-Permission $s.P.fieldsecurityprofileid $column $s.Create } } }
            catch { Write-MaskedRecovery $column "granting a missing profile permission on the already-secured column failed: $($_.Exception.Message)"; throw }
        }
    }
    elseif ($Verify) { Report 'MISSING' "$Table.$column is NOT field-secured — any user with Write can $(if ($IsMarkerTarget) { 'lower it' } else { 're-point a row' })" }
    elseif ($IsDryRun) { Report 'WOULD' "secure $Table.$column, then at once grant the reader (read) and writer (read/create/update) profiles" }
    else {
        $clock = [System.Diagnostics.Stopwatch]::StartNew()
        Set-ColumnSecured $column $true
        try {
            foreach ($s in $specs) { if (-not (Get-Permission $s.P.fieldsecurityprofileid $column)) { Grant-Permission $s.P.fieldsecurityprofileid $column $s.Create } }
        } catch {
            $grantError = $_
            Write-Host "  FAIL     granting the profiles on $Table.$column failed after it was secured: $($grantError.Exception.Message)" -ForegroundColor Red
            Write-Host "           reverting $Table.$column to NOT field-secured, so no reader is left masked..." -ForegroundColor Yellow
            try {
                Set-ColumnSecured $column $false
                Invoke-DvWrite POST 'PublishXml' @{ ParameterXml = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>" } | Out-Null
                $back = (Invoke-DvGet "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$column')?`$select=IsSecured").IsSecured
                if ($back) { throw "the revert was accepted but $Table.$column still reads IsSecured=true" }
                Write-Host "  REVERTED $Table.$column is NOT field-secured again. Fix the cause, then re-run -Apply." -ForegroundColor Yellow
            } catch {
                Write-MaskedRecovery $column "the grant failed ($($grantError.Exception.Message)) AND the revert failed ($($_.Exception.Message))"
            }
            throw $grantError
        }
        $clock.Stop()
        Report 'DONE' ("secured $Table.$column and granted both profiles — masked window {0:N1}s (secure → reader grant)" -f $clock.Elapsed.TotalSeconds)
    }

    foreach ($s in $specs) {
        if (-not $s.P) {
            # AccessMarkerColumns' writer before step (a0) has made it: (p1) reported it; a dry run says what -Apply grants.
            if (-not $Verify -and $IsDryRun) { Report 'WOULD' "grant $($s.Name) on $Table.$column" }
            continue
        }
        $perm = Get-Permission $s.P.fieldsecurityprofileid $column
        if ($perm) {
            if ($perm.canread -eq 4 -and $perm.cancreate -eq $s.Create -and $perm.canupdate -eq $s.Create) { Report 'OK' "$($s.Name) on $Table.$column (read=4 create=$($s.Create) update=$($s.Create))" }
            else { Report 'FAIL' "$($s.Name) on $Table.$column is read=$($perm.canread) create=$($perm.cancreate) update=$($perm.canupdate); expected read=4 create=$($s.Create) update=$($s.Create)" }
        } elseif ($Verify) { Report 'MISSING' "$($s.Name) on $Table.$column" }
        elseif ($IsDryRun) { Report 'WOULD' "grant $($s.Name) on $Table.$column" }
        else { Report 'FAIL' "$($s.Name) on $Table.$column is still missing after -Apply granted it" }
    }

    # (c) every OTHER writer
    $otherWriters = @((Invoke-DvGet "fieldpermissions?`$select=canupdate,cancreate&`$expand=fieldsecurityprofileid(`$select=name)&`$filter=entityname eq '$Table' and attributelogicalname eq '$column' and (canupdate eq 4 or cancreate eq 4)").value |
        Where-Object { $_.fieldsecurityprofileid.name -notin $WriterProfileName, 'System Administrator' })
    foreach ($o in $otherWriters) { Report 'FAIL' "profile '$($o.fieldsecurityprofileid.name)' can also write $Table.$column — only '$WriterProfileName' may" }
}

Write-Host "`nInformational: System Administrator role holders (full field access by platform rule)"
foreach ($role in @((Invoke-DvGet "roles?`$select=roleid&`$filter=name eq 'System Administrator'").value)) {
    foreach ($u in @((Invoke-DvGet "roles($($role.roleid))/systemuserroles_association?`$select=fullname,applicationid,isdisabled").value | Where-Object { -not $_.isdisabled })) {
        Report 'INFO' ("user '{0}'{1}" -f $u.fullname, $(if ($u.applicationid) { ' (application user)' } else { '' }))
    }
    foreach ($tm in @((Invoke-DvGet "roles($($role.roleid))/teamroles_association?`$select=name").value)) { Report 'INFO' "TEAM '$($tm.name)' — every member" }
}

# ── (d) PUBLISH ─────────────────────────────────────────────────────────────────────────────────────────────
if ($Apply) {
    $entities = ($TargetTables | ForEach-Object { "<entity>$_</entity>" }) -join ''
    Invoke-DvWrite POST 'PublishXml' @{ ParameterXml = "<importexportxml><entities>$entities</entities></importexportxml>" } | Out-Null
    Write-Host "`nPublished $($TargetTables -join ', ')." -ForegroundColor Green
}

if ($Verify -or $Apply) {
    $who = if ($IsMarkerTarget) { "only the internal business units' default teams and the BFF (and System Administrator) can write them; the '$ExternalBusinessUnitName' unit reads them only" } else { 'only the BFF (and System Administrator) can write them' }
    if ($gaps.Count -eq 0) { Write-Host "`nPASS: $($Targets[$Target].What) are locked, every business unit's default team reads them, and $who." -ForegroundColor Green; exit 0 }
    Write-Host "`nFAIL ($($gaps.Count)):" -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
Write-Host "`nDRY RUN complete — nothing was written." -ForegroundColor Cyan
