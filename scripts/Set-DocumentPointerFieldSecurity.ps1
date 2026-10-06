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
                                           owner round 45 item 1: it says who wrote a moved file's history). The two
                                           relocation columns are created already secured; this grants their profiles;
      -Target ReportCatalog              — sprk_report.sprk_pbi_reportid / sprk_workspaceid / sprk_datasetid /
                                           sprk_iscustom, the Power BI pointer the reporting module derives embed
                                           tokens, exports and deletes from (owner round 25 item 6).
    Dry run by default; -Apply writes; -Verify checks. scripts/Set-ReportCatalogFieldSecurity.ps1 is the named entry
    point for the second target.

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
      (p5) no sprk_fieldmappingrule, sprk_aitopicregistry or sprk_emailupdatefield row targets a locked column (maker
           configuration that writes outside the BFF; it would fail every write it drives once locked);
      (p6) the table is a ROOT component of -SolutionUniqueName with rootcomponentbehavior 0 (include all
           subcomponents), so the secured columns travel with the solution to every other environment;
      (p7) every column to lock exists (DocumentPointers: sprk_relocationpending and sprk_relocatedversions are created by
           scripts/Set-DocumentRelocationSchema.ps1 -Apply, task 166 f1-v1 / f1-v2).

    STEPS (-Apply), per column, in this order:
      (a) secure the column (IsSecured = true);
      (b) IMMEDIATELY grant the reader profile read=4, then the writer profile read=4 create=4 update=4 (retried while
          the platform propagates the secured state, 0x8004f508). If a grant fails, the column is REVERTED to unsecured
          and the run stops — a secured column with no reader permission reads EMPTY for every non-administrator;
      (c) report any OTHER profile that can create or update the column (FAIL); list System Administrator holders
          (full field access by platform rule, informational);
      (d) publish the table.

    LIVE ORDER (DocumentPointers): deploy the BFF that attaches files server-side → deploy the clients (code pages, PCFs,
    form scripts) that stop writing the pointer → THIS SCRIPT (dry run: p4a must be OK) → -ClientNoLongerWritesPointers
    -Apply → -Verify → scripts/Invoke-DocumentContainerMigration.ps1 (dry run → -Apply → -Verify) → set
    DocumentPointer__StrictDerivedContainer=true on the BFF (task 166 note §20).
    LIVE ORDER (ReportCatalog): deploy the BFF (task 166 f1) → set PowerBi__AllowedWorkspaces → THIS SCRIPT -Target
    ReportCatalog (dry run) → -BffWritesCatalogPointers -Apply → -Verify.

.PARAMETER EnvironmentUrl
    e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER BffApplicationIds
    Application (client) ids of the BFF's Dataverse application users (dev: 5967251e-171c-46fe-a6c2-ef843c90309d and,
    while it is still an application user, 1e40baad-e065-4aea-a8d4-4b7ab273458c). Per-customer input (D-13), never
    hard-coded.

.PARAMETER Target
    DocumentPointers (default) or ReportCatalog.

.PARAMETER SolutionUniqueName
    The unmanaged solution carrying the profiles and the table. Default SpaarkeCore.

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
    [ValidateSet('DocumentPointers', 'ReportCatalog')][string]$Target = 'DocumentPointers',
    [string]$SolutionUniqueName = 'SpaarkeCore',
    [switch]$ClientNoLongerWritesPointers,
    [switch]$BffWritesCatalogPointers,
    [switch]$Apply,
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
if ($Apply -and $Verify) { throw '-Apply and -Verify are separate modes; run -Apply first, then -Verify.' }
. (Join-Path $PSScriptRoot 'common/DataverseSolutionMembership.ps1')
if ($Target -eq 'ReportCatalog' -and $ClientNoLongerWritesPointers) { throw '-ClientNoLongerWritesPointers applies to -Target DocumentPointers only.' }
if ($Target -eq 'DocumentPointers' -and $BffWritesCatalogPointers) { throw '-BffWritesCatalogPointers applies to -Target ReportCatalog only.' }
$BffApplicationIds = @($BffApplicationIds | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
foreach ($id in $BffApplicationIds) { if (-not [Guid]::TryParse($id, [ref][Guid]::Empty)) { throw "-BffApplicationIds: '$id' is not a GUID." } }
$Api = "$EnvironmentUrl/api/data/v9.2"

# ── Constants ───────────────────────────────────────────────────────────────────────────────────────────────
$Targets = @{
    DocumentPointers = @{ Table = 'sprk_document'; Columns = @('sprk_graphdriveid', 'sprk_graphitemid', 'sprk_relocationpending', 'sprk_relocatedversions'); What = 'the document pointers and the relocation record (ledger and version record)' }
    ReportCatalog    = @{ Table = 'sprk_report'; Columns = @('sprk_pbi_reportid', 'sprk_workspaceid', 'sprk_datasetid', 'sprk_iscustom'); What = 'the report catalog pointers' }
}
$Table = $Targets[$Target].Table
$Columns = $Targets[$Target].Columns
$ReaderProfileName = 'Spaarke BFF-Managed Field Readers'
$WriterProfileName = 'Spaarke BFF-Managed Field Writers'
$RelocationRecordColumns = @('sprk_relocationpending', 'sprk_relocatedversions')

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
Write-Host "Target      : $Target ($Table : $($Columns -join ', '))"
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
foreach ($pair in @(@{ Rows = $readers; Name = $ReaderProfileName }, @{ Rows = $writers; Name = $WriterProfileName })) {
    if ($pair.Rows.Count -eq 1) { Report 'OK' "(p1) profile '$($pair.Name)' exists" }
    else { Report 'FAIL' "(p1) $($pair.Rows.Count) profile(s) named '$($pair.Name)' — exactly one is required; create it with scripts/Set-RecordCreatorPersonSchema.ps1 -Apply (this script never creates it)" }
}
if ($readers.Count -eq 1) { $reader = $readers[0] }
if ($writers.Count -eq 1) { $writer = $writers[0] }

$solution = @((Invoke-DvGet "solutions?`$select=solutionid&`$filter=uniquename eq '$SolutionUniqueName'").value) | Select-Object -First 1
if (-not $solution) { Report 'FAIL' "(p1) solution '$SolutionUniqueName' not found" }
else {
    # Solution membership through the ONE shared helper (scripts/common/DataverseSolutionMembership.ps1; every page read).
    $membership = Get-DvSolutionMembership -Api $Api -Headers $headers -SolutionId $solution.solutionid
    foreach ($p in @($reader, $writer) | Where-Object { $_ }) {
        if (Test-DvInSolution -Membership $membership -ComponentId $p.fieldsecurityprofileid) { Report 'OK' "(p1) '$($p.name)' is in $SolutionUniqueName" }
        else { Report 'FAIL' "(p1) '$($p.name)' is not in $SolutionUniqueName — Set-RecordCreatorPersonSchema.ps1 -Apply adds it" }
    }

    # (p6) the table is a root component with rootcomponentbehavior 0, so the secured columns ship with the solution.
    $entityMeta = Invoke-DvGet "EntityDefinitions(LogicalName='$Table')?`$select=MetadataId"
    $tableIn = Test-DvInSolution -Membership $membership -ComponentId $entityMeta.MetadataId
    if (-not $tableIn) { Report 'FAIL' "(p6) $Table is not a component of $SolutionUniqueName — the secured columns would not travel" }
    elseif (-not $membership.TablesWithSubcomponents.Contains($entityMeta.MetadataId.ToString())) { Report 'FAIL' "(p6) $Table is in $SolutionUniqueName without rootcomponentbehavior 0 (include all subcomponents), which is required" }
    else { Report 'OK' "(p6) $Table is a root component of $SolutionUniqueName (rootcomponentbehavior 0)" }
}

if ($reader) {
    $onReader = @($reader.teamprofiles_association | ForEach-Object { $_.teamid.ToString().ToLowerInvariant() })
    $defaultTeams = @((Invoke-DvGet 'teams?$select=teamid,name&$filter=isdefault eq true&$expand=businessunitid($select=name)').value)
    if ($defaultTeams.Count -eq 0) { Report 'FAIL' '(p2) no business-unit default team was read — the query is wrong' }
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
if ($writer) {
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
            Report 'FAIL' "(p5) $($ch.T) row $($row."$($ch.T)id") targets $column — remove or retarget it; only the BFF writes it"
        }
    }
}
if ($configuredWriters -eq 0) { Report 'OK' "(p5) no field-mapping rule, AI topic-registry row or email update field targets the locked columns" }

# (p7) every column to lock exists.
$missingColumns = 0
foreach ($column in $Columns) {
    try { Invoke-DvGet "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$column')?`$select=LogicalName" | Out-Null }
    catch {
        $missingColumns++
        $hint = if ($column -in $RelocationRecordColumns) { ' — run scripts/Set-DocumentRelocationSchema.ps1 -Apply first' } else { '' }
        Report 'FAIL' "(p7) $Table.$column does not exist in this environment$hint"
    }
}
if ($missingColumns -eq 0) { Report 'OK' "(p7) every column to lock exists ($($Columns -join ', '))" }

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
else {
    if ($BffWritesCatalogPointers) { Report 'OK' '(p4) operator confirms the BFF build that stamps the catalog pointer app-only (task 166 f1) is deployed' }
    elseif ($Apply) { Report 'FAIL' '(p4) -BffWritesCatalogPointers not passed: an older BFF registers catalog rows WITH the pointer as the caller, which the lock refuses' }
    else { Report 'INFO' '(p4) -BffWritesCatalogPointers is required for -Apply' }
}

if ($Apply -and $gaps.Count -gt 0) {
    Write-Host "`nREFUSED: -Apply needs every precondition to pass ($($gaps.Count) gap(s)). Nothing was written." -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
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
    Invoke-DvWrite PUT $attrPath $typed @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null
}
function Write-MaskedRecovery([string]$Column, [string]$Cause) {
    Write-Host ''
    Write-Host "  !!! RECOVERY REQUIRED NOW — $Table.$Column may be field-secured WITHOUT its reader permission !!!" -ForegroundColor Red
    Write-Host "  Every non-administrator then reads $Column EMPTY (files / reports stop opening)." -ForegroundColor Red
    Write-Host "  Cause: $Cause" -ForegroundColor Red
    Write-Host '  Do ONE of these, now: (1) fix the cause and re-run -Apply (it grants only what is missing on an already' -ForegroundColor Red
    Write-Host "  secured column), then -Verify; (2) or clear 'Enable column security' on $Table.$Column and publish $Table." -ForegroundColor Red
}

foreach ($column in $Columns) {
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
    elseif ($Verify) { Report 'MISSING' "$Table.$column is NOT field-secured — any user with Write can re-point a row" }
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
        if (-not $s.P) { continue }
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
    foreach ($o in $otherWriters) { Report 'FAIL' "profile '$($o.fieldsecurityprofileid.name)' can also write $Table.$column — only the BFF may" }
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
    Invoke-DvWrite POST 'PublishXml' @{ ParameterXml = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>" } | Out-Null
    Write-Host "`nPublished $Table." -ForegroundColor Green
}

if ($Verify -or $Apply) {
    if ($gaps.Count -eq 0) { Write-Host "`nPASS: $($Targets[$Target].What) are locked, every business unit's default team reads them, and only the BFF (and System Administrator) can write them." -ForegroundColor Green; exit 0 }
    Write-Host "`nFAIL ($($gaps.Count)):" -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
Write-Host "`nDRY RUN complete — nothing was written." -ForegroundColor Cyan
