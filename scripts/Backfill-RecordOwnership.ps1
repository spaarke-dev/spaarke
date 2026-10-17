#!/usr/bin/env pwsh
#Requires -Version 7.0
<#
.SYNOPSIS
    Re-owns EXISTING app-owned records to a business unit's DEFAULT OWNER TEAM, record-first — the backfill for
    write-path invariant I-6 (spaarkeai-word-add-in-r1 task 080). Dry-run by default; every write is reversible.

.DESCRIPTION
    Until task 080, every record the BFF created app-only was owned by the BFF APPLICATION USER, which sits in the
    ROOT business unit (measured 2026-09-22: all 512 sprk_document rows). Dataverse Deep depth reaches a unit's
    descendants, never its parent, so no child-business-unit user could read them. New creates are now owned by a
    default owner team (RecordOwnershipResolver). This script fixes the rows that already exist.

    WHICH ROWS (candidates):
      sprk_document, sprk_todo : rows whose owner is an APPLICATION user (systemuser with an applicationid). Rows
                      owned by a human user or by a team are left alone — they are not the defect this fixes, and
                      re-owning them would change who they belong to.
      sprk_externalrecordaccess : every row, in EVERY state, owned by ANY systemuser — application OR human (task 099b
                      review round 1, F3; review round 4: an inactive grant still names the record and the contact, so a
                      user-owned inactive grant is the same defect as an active one). D-11 says a grant row is owned by a
                      team, never a user, so a human-owned grant is the same defect. The summary splits the candidates into
                      AppOwned and HumanOwned, and the manifest records each row's previous owner, so either is restored
                      exactly by -RevertManifest. NotActive counts the planned writes (ToWrite) on INACTIVE rows — reporting
                      only; they are re-owned like active ones.
                      ALSO every row, in EVERY state (review round 3, F1: an inactive grant still names the record and the
                      contact), owned by a TEAM that is not the team the resolver gives its root (task 099b review round
                      2): a grant created on an ordinary root and left on that unit's default team when the root was made
                      secure before the secure-transition pass re-owned grants (or the reverse, left on the Secure team
                      after an unsecure). Reported as TeamMismatch; the manifest records the previous TEAM. A team-owned
                      row filed to nothing is counted Unfiled (review round 3, K4), and a row read both as user-owned and
                      as team-owned is processed once.

    WHICH TEAM — record-first, the SAME order RecordOwnershipResolver applies to new creates:
      sprk_document : the filed record — sprk_matter, sprk_project, sprk_invoice, sprk_workassignment, then the
                      related-family slots the save also writes: sprk_relatedevent, sprk_relatedtodo, sprk_relatedcontact.
      sprk_todo     : the record regarding (matter / project / invoice), then the document, then the communication.
      sprk_externalrecordaccess (unified-access-control-r2 task 099b, D-11, #1010): the grant's ROOT — sprk_project,
                      sprk_matter, sprk_workassignment (a grant binds exactly one) — the SAME parent the BFF's grant core
                      hands RecordOwnershipResolver for a new grant. User-owned and team-owned grants: every state.
    The target's owningbusinessunit → that unit's default owner team (isdefault = true AND teamtype = 0 — both
    predicates are required; dev has non-default Owner teams and Access teams in the same units).

    SECURE TARGETS (every entity) — the resolver's task 144 / 146 rules, as in Migrate-SecureRecordsToNamedOwnerTeam.ps1
    (Get-OwnerTeamDecision; task 099b review round 1 extended them from grants to documents and to-dos too):
      - a target owned in the Secure Record business unit → that unit's NAMED owner team ('Secure Record Owners',
        teamtype = 0, isdefault = false, exactly one) — never the Secure Record unit's default team;
      - a ROOT target (sprk_project, sprk_matter, sprk_workassignment — the tables that carry sprk_issecure) whose
        sprk_issecure is true OR EMPTY (fail closed, task 150) but is owned OUTSIDE the Secure Record unit is NOT
        ISOLATED (an interrupted provisioning) → UNRESOLVABLE, never an ordinary team;
      - more than one business unit with the Secure Record name, or a missing/duplicate named team → UNRESOLVABLE.
    Not mirrored: the resolver's look-through for a target that is itself a CHILD row and is not team-owned (it reads
    that row's own filing, up to four levels, to find a secure ancestor). Such a target is decided by its own unit here.

    WHAT IT WILL NOT GUESS. A row filed against nothing (an unfiled save, or an app-created matter/project/invoice)
    was created on behalf of a user this data does not record — the create was app-only, so createdby is the app
    user. Such rows are reported UNRESOLVABLE and never written. The owner's rule for 065 applies: "we can't 'guess'
    what record it belongs to." They stay where they are (root), visible to root-unit users only.

    SAFETY MODEL
      - Dry-run is the DEFAULT; -WhatIf forces a dry-run even with -Apply. A dry-run issues zero writes.
      - WRITE-AHEAD REVERSAL MANIFEST: before each write, the row's previous owner (type + id) and business unit are
        appended to a CSV and flushed. -RevertManifest <csv> -Apply restores every row in it to that owner, so a
        wrong run is undone by a second command, not a second migration.
      - READ-BACK: every assignment is re-read and verified (owningteam = the team). Dataverse accepts an
        unrecognised @odata.bind property and ignores it — "the request succeeded" is not evidence of the write.
      - SAMPLE FIRST: -MaxWritesPerRun bounds a run (e.g. 5) so the result can be checked in the app before a
        wholesale run. The summary always reports the true totals regardless of the cap.
      - Idempotent: a re-owned row is team-owned, so it is no longer a candidate. Re-running reports ToWrite: 0.
      - VERIFY (review round 6) is ToWrite 0 AND Unresolvable 0 AND ReadFailed 0, printed per entity as
        "VERIFY <entity>: PASS | NOT MET". An Unresolvable row sits on a root that still exists (e.g. flagged secure but not
        isolated) and stays user- or wrong-team-owned, so it fails VERIFY. A ReadFailed row's root could not be read (any
        error but a 404: throttling, 5xx, 403): its owner is undecided, so it is never written and fails VERIFY; each is
        logged with its HTTP status and the run goes on, so one run lists every failure. Unfiled rows (filed to nothing:
        the root is gone and the lookup empty) expose no record: known residue, counted and logged per row, not a failure.
      - A todo filed to a document derives from the DOCUMENT'S business unit: run sprk_document first (the default
        order), then re-run for sprk_todo, so todos follow their documents' new units.

.PARAMETER EnvironmentUrl
    Dataverse environment URL, e.g. https://spaarkedev1.crm.dynamics.com. Required except with -SelfTest.

.PARAMETER SelfTest
    Offline: runs the pure owner decision and candidate filters over inline cases; exits 1 on any mismatch.

.PARAMETER Apply
    Write mode. Without it the script always dry-runs.

.PARAMETER WhatIf
    Forces a dry-run even when -Apply is passed.

.PARAMETER Entities
    Entities to re-own (default: sprk_document, sprk_todo, in that order). sprk_externalrecordaccess is opt-in:
    -Entities sprk_externalrecordaccess.

.PARAMETER SecureBusinessUnitName
    The Secure Record business unit (SecureRecord:BusinessUnitName). Default 'Secure Record'.

.PARAMETER SecureOwnerTeamName
    Its named owner team (SecureRecord:OwnerTeamName). Default 'Secure Record Owners'.

.PARAMETER MaxWritesPerRun
    Cap on actual writes in one -Apply run (0 = unlimited). Use a small number for the sample run.

.PARAMETER RevertManifest
    Path to a manifest from an earlier -Apply run. With -Apply, restores every row in it to its previous owner.
    Without -Apply, previews the restore.

.PARAMETER LogPath
    Per-run log. Defaults to scripts/logs/backfill-record-ownership-<timestamp>.log.

.EXAMPLE
    .\Backfill-RecordOwnership.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com
    Dry run: candidates, the planned team per row, unresolvable rows, per-entity totals and the count per target
    team (the Secure Record owner team always listed). Zero writes.

.EXAMPLE
    .\Backfill-RecordOwnership.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply -MaxWritesPerRun 5
    The SAMPLE: re-own five rows, each read back; writes a reversal manifest. Check them in the app before going on.

.EXAMPLE
    .\Backfill-RecordOwnership.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -RevertManifest .\logs\record-ownership-manifest-20260930-120000.csv -Apply
    Undo that run exactly.

.EXAMPLE
    .\Backfill-RecordOwnership.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Entities sprk_externalrecordaccess
    The grant backfill dry run (task 099b). It is also the VERIFY: after -Apply, it must print "VERIFY <entity>: PASS",
    that is ToWrite 0 AND Unresolvable 0 AND ReadFailed 0 (review round 6): no grant in any state owned by a user,
    application or human (review rounds 1 and 4), and none owned by a team other than its root's (TeamMismatch 0, review
    rounds 2 and 3). An Unresolvable grant sits on a root that cannot be given a team (e.g. flagged secure but not
    isolated, or a 404 on the root), and a ReadFailed grant's root could not be read (429, 5xx, 403; logged with the
    status). Both stay owned against D-11, so both fail VERIFY. Candidates is NOT the rule (review round 5, F1-b): Unfiled
    rows (filed to nothing, e.g. an inactive grant whose root was deleted, which empties the lookup) stay in the candidate
    read forever. They expose no record: KNOWN RESIDUE, counted (Unfiled), logged with its id, never written, and they do
    not fail VERIFY.

.EXAMPLE
    .\Backfill-RecordOwnership.ps1 -SelfTest
    Offline: the owner decision (Get-OwnerTeamDecision) and the candidate filters over inline cases — a secure
    target never maps to an ordinary team. Needs no environment; exits 1 on any mismatch.
#>

[CmdletBinding()]
param(
    [string]$EnvironmentUrl,

    [switch]$Apply,

    [switch]$WhatIf,

    [ValidateSet('sprk_document', 'sprk_todo', 'sprk_externalrecordaccess')]
    [string[]]$Entities = @('sprk_document', 'sprk_todo'),

    [string]$SecureBusinessUnitName = 'Secure Record',

    [string]$SecureOwnerTeamName = 'Secure Record Owners',

    [int]$MaxWritesPerRun = 0,

    [string]$RevertManifest,

    [string]$LogPath,

    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'
if (-not $SelfTest -and [string]::IsNullOrWhiteSpace($EnvironmentUrl)) { throw '-EnvironmentUrl is required (except with -SelfTest).' }
if ($EnvironmentUrl) { $EnvironmentUrl = $EnvironmentUrl.TrimEnd('/') }
$IsDryRun = (-not $Apply.IsPresent) -or $WhatIf.IsPresent

# ── Record-first target order per entity. MUST mirror the writers (OfficeService / RecordOwnershipResolver) ──
#    lookup attribute → target entity set. First non-null wins; a named target that cannot be resolved is
#    UNRESOLVABLE (never falls through to a later slot — same rule as the resolver's refuse branch).
$TargetOrder = @{
    'sprk_document' = [ordered]@{
        'sprk_matter'         = 'sprk_matters'
        'sprk_project'        = 'sprk_projects'
        'sprk_invoice'        = 'sprk_invoices'
        'sprk_workassignment' = 'sprk_workassignments'
        # The related-family slots the Office save ALSO writes (DocumentAssociationMap: event, todo, contact).
        'sprk_relatedevent'   = 'sprk_events'
        'sprk_relatedtodo'    = 'sprk_todos'
        'sprk_relatedcontact' = 'contacts'
    }
    'sprk_todo'     = [ordered]@{
        'sprk_regardingmatter'        = 'sprk_matters'
        'sprk_regardingproject'       = 'sprk_projects'
        'sprk_regardinginvoice'       = 'sprk_invoices'
        'sprk_regardingdocument'      = 'sprk_documents'
        'sprk_regardingcommunication' = 'sprk_communications'
    }
    # Task 099b: a grant binds exactly ONE root (GrantExternalAccessEndpoint.BuildGrantPayload), the parent the grant core
    # hands the resolver.
    'sprk_externalrecordaccess' = [ordered]@{
        'sprk_project'        = 'sprk_projects'
        'sprk_matter'         = 'sprk_matters'
        'sprk_workassignment' = 'sprk_workassignments'
    }
}
$EntitySet = @{ 'sprk_document' = 'sprk_documents'; 'sprk_todo' = 'sprk_todos'; 'sprk_externalrecordaccess' = 'sprk_externalrecordaccesses' }
# Entities whose summary reports NotActive: of the planned writes, the INACTIVE rows (review round 4 — reporting only; an
# inactive grant names the record and the contact like an active one, so it is re-owned in every state, never skipped).
$ReportNotActive = @{ 'sprk_externalrecordaccess' = $true }
# Entities whose candidates are rows owned by ANY systemuser, human included (D-11: a grant is never user-owned; review
# round 1, F3). The others re-own application-owned rows only.
$AnyUserOwned = @{ 'sprk_externalrecordaccess' = $true }
# The tables that carry sprk_issecure (RecordOwnershipResolver.SecureFlaggedRoots): only these are read for the flag.
$SecureFlaggedSets = @{ 'sprk_projects' = $true; 'sprk_matters' = $true; 'sprk_workassignments' = $true }
$OwnerTeamType = 0

function ConvertTo-CleanGuid {
    # ADR-044: bare, lowercase, at every boundary.
    param([AllowEmptyString()][string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return $null }
    return $Value.Trim().Trim('{', '}').ToLowerInvariant()
}

function Get-Lookup {
    param($Row, [string]$Attribute)
    $p = $Row.PSObject.Properties["_${Attribute}_value"]
    if ($null -eq $p -or $null -eq $p.Value) { return $null }
    return ConvertTo-CleanGuid $p.Value
}

# ── PURE: the resolver's owner decision for a row filed under ONE target (RecordOwnershipResolver.ResolveFromParentsAsync) ──
#    $Facts    : @{ Bu = <target's owning business unit or $null>; Flagged = <sprk_issecure true/empty on a ROOT target>;
#                   ReadFailed = <the read failed with anything but a 404 (review round 6)>; Status; Error }
#    $Topology : @{ BuId = <Secure Record unit or $null>; Ambiguous = <bool>; TeamId = <named team or $null>; TeamCount = <n> }
#    $DefaultTeamOf : { param($bu) <that unit's single default owner team, or $null> }
#    Returns @{ Team; Bu; Reason; ReadFailed; Status } — Reason is set exactly when Team is $null (never written): UNRESOLVABLE,
#    or READ-FAILED when ReadFailed is $true (Status = the HTTP status, or "none" when there was no response).
function Get-OwnerTeamDecision {
    param([hashtable]$Facts, [hashtable]$Topology, [scriptblock]$DefaultTeamOf)
    # Review round 6 (F2): a target that could not be READ (any error but a 404) is not "does not exist": the owner is undecided.
    if ($Facts.ReadFailed) { return @{ Team = $null; Bu = $null; ReadFailed = $true; Status = $Facts.Status; Reason = "the target could not be read (HTTP $($Facts.Status)): $($Facts.Error)" } }
    if (-not $Facts.Bu) { return @{ Team = $null; Bu = $null; Reason = 'the target does not exist or has no owning business unit' } }
    if ($Topology.Ambiguous) { return @{ Team = $null; Bu = $Facts.Bu; Reason = "more than one business unit is named '$SecureBusinessUnitName'" } }
    if ($Facts.Flagged -and $Facts.Bu -ne $Topology.BuId) {
        return @{ Team = $null; Bu = $Facts.Bu; Reason = 'the target is flagged secure (or its flag is empty) but is not owned in the Secure Record business unit (not isolated)' }
    }
    if ($Topology.BuId -and $Facts.Bu -eq $Topology.BuId) {
        if (-not $Topology.TeamId) { return @{ Team = $null; Bu = $Facts.Bu; Reason = "the named owner team '$SecureOwnerTeamName' resolved to $($Topology.TeamCount) teams, not one" } }
        return @{ Team = $Topology.TeamId; Bu = $Facts.Bu; Reason = $null }
    }
    $team = & $DefaultTeamOf $Facts.Bu
    if (-not $team) { return @{ Team = $null; Bu = $Facts.Bu; Reason = 'the business unit has no single default owner team' } }
    return @{ Team = $team; Bu = $Facts.Bu; Reason = $null }
}

# ── PURE: the candidate $filter for an entity, in EVERY state (review round 4: no state clause — an inactive user-owned
#    grant was left on the user, against D-11). It NARROWS the read; it does not select the user-owned rows: measured on
#    spaarkedev1 2026-10-17, '_owninguser_value ne null' returns the same rows as '_owningteam_value ne null', team-owned
#    ones included. Select-UserOwnedRows decides, per row (review round 5, F1-a). ──
function Get-CandidateFilter {
    param([string]$Entity, [string[]]$AppUserIds)
    if ($AnyUserOwned[$Entity]) { return '_owninguser_value ne null' }
    return "Microsoft.Dynamics.CRM.In(PropertyName='owninguser',PropertyValues=[$(($AppUserIds | ForEach-Object { "'$_'" }) -join ',')])"
}

# ── PURE (review round 4): whether a row is INACTIVE (statecode set and not 0). Reporting only (NotActive), never a skip. ──
function Test-InactiveRow {
    param($Row)
    $p = $Row.PSObject.Properties['statecode']
    return [bool]($p -and $null -ne $p.Value -and [int]$p.Value -ne 0)
}

# ── PURE (task 099b review round 2): the read that finds the entity's TEAM-owned rows, or $null when they are not checked
#    (grants only). A row is a candidate only when it is team-owned (_owningteam_value set, checked per row) and
#    Test-TeamMismatch says its team is not the resolver's team for its root. The read has NO filter: every row in EVERY
#    state (review round 3, F1 — an inactive grant on the wrong team exposes the record like an active one), and team
#    ownership is tested on the row, never in the query (measured on spaarkedev1 2026-10-17: '_owningteam_value ne null'
#    returns USER-owned grant rows too). ──
function Get-TeamOwnedQuery {
    param([string]$Entity, [string]$EntitySetName, [string]$Select)
    if (-not $AnyUserOwned[$Entity]) { return $null }
    return "$EntitySetName`?`$select=$Select"
}

# ── PURE (review round 5, F1-a): of the rows the candidate $filter read, the USER-owned ones — owninguser set AND owningteam
#    empty, the mirror of Select-TeamOwnedRows. Every entity's candidate read goes through it: the filter does not select
#    (see Get-CandidateFilter), and a team-owned row taken as a candidate indexed the application-user set with $null, which
#    stopped the run (ErrorActionPreference Stop) — on the VERIFY run after the first grant -Apply. ──
function Select-UserOwnedRows {
    param([object[]]$Rows)
    return , @($Rows | Where-Object {
            $null -ne $_ -and $_.PSObject.Properties['_owninguser_value'] -and $_._owninguser_value -and
            -not ($_.PSObject.Properties['_owningteam_value'] -and $_._owningteam_value) })
}

# ── PURE: of the user-owned candidates, how many an APPLICATION user owns (the rest are HumanOwned). ──
function Measure-AppOwnedRows {
    param([object[]]$Rows, [hashtable]$AppUserSet)
    return @($Rows | Where-Object { $AppUserSet[(Get-Lookup $_ 'owninguser')] }).Count
}

# ── PURE: of the rows Get-TeamOwnedQuery read, the TEAM-owned ones (a user-owned row is a Select-UserOwnedRows candidate). ──
function Select-TeamOwnedRows {
    param([object[]]$Rows)
    return , @($Rows | Where-Object { $null -ne $_ -and $_.PSObject.Properties['_owningteam_value'] -and $_._owningteam_value })
}

# ── PURE: a team-owned row needs re-owning when the resolver gives its root a team (the decision resolved) that is not the
#    team owning it now. An UNRESOLVABLE decision is never a write (it is reported). ──
function Test-TeamMismatch {
    param([string]$CurrentTeam, [hashtable]$Decision)
    return [bool]($Decision.Team -and $CurrentTeam -and $Decision.Team -ne $CurrentTeam)
}

# ── PURE (review round 3, K4): the user-owned candidates, then the team-owned rows, each row ONCE. The two reads are separate
#    queries, so a row can land in both (both owner columns read back, or an owner change between the reads); the first
#    reading is kept. Returns @{ Rows; Duplicates = the ids dropped }. ──
function Merge-BackfillRows {
    param([object[]]$Candidates, [object[]]$TeamOwned, [string]$IdColumn)
    $seen = @{}; $rows = [System.Collections.Generic.List[object]]::new(); $duplicates = [System.Collections.Generic.List[string]]::new()
    foreach ($row in @($Candidates) + @($TeamOwned)) {
        if ($null -eq $row) { continue }
        $id = ConvertTo-CleanGuid $row.$IdColumn
        if ($seen.ContainsKey($id)) { $duplicates.Add($id); continue }
        $seen[$id] = $true
        $rows.Add($row)
    }
    return @{ Rows = $rows.ToArray(); Duplicates = $duplicates.ToArray() }
}

# ── PURE (review round 3, K4): what the backfill does with one row. $Order: lookup -> entity set, first non-null wins.
#    $Decide: { param($Target) <Get-OwnerTeamDecision's answer for that target> }. Returns @{ Outcome; Target; Decision;
#    CurrentTeam; Mismatch; Inactive }, Inactive = Test-InactiveRow (reporting only: the state never changes the Outcome),
#    Outcome one of:
#      Unfiled      — filed to nothing, WHATEVER its owner (a team-owned row too: counted, never skipped silently); not written
#      Unresolvable — the decision refused; not written
#      ReadFailed   — the target could not be read (any error but a 404; review round 6, F2); not written
#      Correct      — a team-owned row already on its root's team; nothing to do
#      Write        — re-own it to Decision.Team (Mismatch = it is team-owned by another team) ──
function Get-RowPlan {
    param($Row, [System.Collections.IDictionary]$Order, [scriptblock]$Decide)
    $currentTeam = Get-Lookup $Row 'owningteam'
    $target = $null
    foreach ($attr in $Order.Keys) {
        $value = Get-Lookup $Row $attr
        if ($value) { $target = @{ Attribute = $attr; Set = $Order[$attr]; Id = $value }; break }
    }
    $plan = @{ Outcome = $null; Target = $target; Decision = $null; CurrentTeam = $currentTeam; Mismatch = $false; Inactive = (Test-InactiveRow $Row) }
    if (-not $target) { $plan.Outcome = 'Unfiled'; return $plan }
    $plan.Decision = & $Decide $target
    if ($plan.Decision.ReadFailed) { $plan.Outcome = 'ReadFailed'; return $plan }
    if ($currentTeam) {
        $plan.Mismatch = Test-TeamMismatch -CurrentTeam $currentTeam -Decision $plan.Decision
        $plan.Outcome = if ($plan.Mismatch) { 'Write' } elseif ($plan.Decision.Team) { 'Correct' } else { 'Unresolvable' }
        return $plan
    }
    $plan.Outcome = if ($plan.Decision.Team) { 'Write' } else { 'Unresolvable' }
    return $plan
}

# ── PURE (review round 5, F1-b): one row's plan counted into its entity's summary. Unfiled, Unresolvable and ReadFailed are
#    counted (never written); Write counts ToWrite (and Candidates + TeamMismatch for a team-owned row on another team than its
#    root's, NotActive for an inactive row). ──
function Add-PlanToStats {
    param([System.Collections.IDictionary]$Stats, [hashtable]$Plan, [bool]$ReportNotActive)
    switch ($Plan.Outcome) {
        'Unfiled' { $Stats.Unfiled++ }
        'Unresolvable' { $Stats.Unresolvable++ }
        'ReadFailed' { $Stats.ReadFailed++ }
        'Write' {
            if ($Plan.Mismatch) { $Stats.Candidates++; $Stats.TeamMismatch++ }
            $Stats.ToWrite++
            if ($ReportNotActive -and $Plan.Inactive) { $Stats.NotActive++ }
        }
    }
}

# ── PURE (review round 6): the VERIFY rule — ToWrite 0 AND Unresolvable 0 AND ReadFailed 0. An Unresolvable row sits on a root
#    that still exists and stays owned against D-11; a ReadFailed row's owner is undecided (its root could not be read). Unfiled
#    rows (the root is gone, the lookup empty) expose no record: residue, not a failure, so Candidates is not the rule. ──
function Test-VerifySatisfied {
    param($Stats)
    return [bool]($Stats.ToWrite -eq 0 -and $Stats.Unresolvable -eq 0 -and $Stats.ReadFailed -eq 0)
}

# ── PURE (review round 6, F2): the HTTP status of a failed read, or $null when the error carries none (a network failure). ──
function Get-HttpStatusCode {
    param($Exception)
    $response = $Exception.PSObject.Properties['Response']
    if ($response -and $response.Value -and $response.Value.PSObject.Properties['StatusCode']) { return [int]$response.Value.StatusCode }
    return $null
}

# ── PURE (review round 6, F2): a target's facts from ONE read. $Read: { <the row, or throws> }. ONLY a 404 is "does not exist"
#    (Bu = $null); any other failure (429, 5xx, 403, no response) is ReadFailed with its status, never a missing root. The
#    failure is recorded, not rethrown: the run goes on, so one run lists every failed read. ──
function Read-TargetFacts {
    param([scriptblock]$Read, [bool]$IsRoot)
    try {
        $row = & $Read
        $flag = $row.PSObject.Properties['sprk_issecure']
        $flagged = $IsRoot -and ($null -eq $flag -or $null -eq $flag.Value -or $flag.Value -eq $true)
        return @{ Bu = (Get-Lookup $row 'owningbusinessunit'); Flagged = $flagged; ReadFailed = $false }
    } catch {
        $status = Get-HttpStatusCode $_.Exception
        if ($status -eq 404) { return @{ Bu = $null; Flagged = $IsRoot; ReadFailed = $false; Status = 404 } }
        return @{ Bu = $null; Flagged = $IsRoot; ReadFailed = $true; Status = $(if ($null -ne $status) { $status } else { 'none' }); Error = $_.Exception.Message }
    }
}

# ══ SELF-TEST (offline) ══════════════════════════════════════════════════════════════════════════════════════
if ($SelfTest) {
    $secureBu = 'aaaaaaaa-0000-0000-0000-000000000001'; $secureTeam = 'aaaaaaaa-0000-0000-0000-0000000000f1'
    $childBu = 'bbbbbbbb-0000-0000-0000-000000000002'; $childTeam = 'bbbbbbbb-0000-0000-0000-0000000000f2'
    $secureDefault = 'aaaaaaaa-0000-0000-0000-0000000000d1'
    $defaults = { param($bu) switch ($bu) { $secureBu { $secureDefault } $childBu { $childTeam } default { $null } } }
    $ok = @{ BuId = $secureBu; Ambiguous = $false; TeamId = $secureTeam; TeamCount = 1 }
    $cases = @(
        @{ Name = 'secure root (flagged, in the Secure BU) -> the named team'; Facts = @{ Bu = $secureBu; Flagged = $true }; Topology = $ok; Team = $secureTeam }
        @{ Name = 'unflagged target owned in the Secure BU (a child row) -> the named team'; Facts = @{ Bu = $secureBu; Flagged = $false }; Topology = $ok; Team = $secureTeam }
        @{ Name = 'ordinary target -> its unit default team'; Facts = @{ Bu = $childBu; Flagged = $false }; Topology = $ok; Team = $childTeam }
        @{ Name = 'flagged root NOT isolated -> UNRESOLVABLE'; Facts = @{ Bu = $childBu; Flagged = $true }; Topology = $ok; Team = $null }
        @{ Name = 'flagged root, no Secure BU in the environment -> UNRESOLVABLE'; Facts = @{ Bu = $childBu; Flagged = $true }; Topology = @{ BuId = $null; Ambiguous = $false; TeamId = $null; TeamCount = 0 }; Team = $null }
        @{ Name = 'secure root, named team missing -> UNRESOLVABLE (never the Secure BU default team)'; Facts = @{ Bu = $secureBu; Flagged = $true }; Topology = @{ BuId = $secureBu; Ambiguous = $false; TeamId = $null; TeamCount = 0 }; Team = $null }
        @{ Name = 'secure root, named team duplicated -> UNRESOLVABLE'; Facts = @{ Bu = $secureBu; Flagged = $true }; Topology = @{ BuId = $secureBu; Ambiguous = $false; TeamId = $null; TeamCount = 2 }; Team = $null }
        @{ Name = 'two Secure Record BUs -> UNRESOLVABLE, even for an ordinary target'; Facts = @{ Bu = $childBu; Flagged = $false }; Topology = @{ BuId = $null; Ambiguous = $true; TeamId = $null; TeamCount = 0 }; Team = $null }
        @{ Name = 'target unreadable (no BU) -> UNRESOLVABLE'; Facts = @{ Bu = $null; Flagged = $true }; Topology = $ok; Team = $null }
        @{ Name = 'unit with no default team -> UNRESOLVABLE'; Facts = @{ Bu = 'cccccccc-0000-0000-0000-000000000003'; Flagged = $false }; Topology = $ok; Team = $null }
    )
    $failures = 0
    foreach ($c in $cases) {
        $d = Get-OwnerTeamDecision -Facts $c.Facts -Topology $c.Topology -DefaultTeamOf $defaults
        $pass = ($d.Team -eq $c.Team) -and ([bool]$d.Reason -eq (-not $c.Team))
        # The invariant this whole check exists for: a target in the Secure BU, or a flagged root, NEVER gets an ordinary team.
        if (($c.Facts.Bu -eq $secureBu -or $c.Facts.Flagged) -and $d.Team -and $d.Team -ne $secureTeam) { $pass = $false }
        if (-not $pass) { $failures++ }
        Write-Host ("{0} {1} -> team={2} reason={3}" -f $(if ($pass) { 'PASS' } else { 'FAIL' }), $c.Name, $d.Team, $d.Reason)
    }
    $filters = @(
        @{ Name = 'grant candidates: rows owned by ANY user (F3), in EVERY state (review round 4: no state clause)'; Got = (Get-CandidateFilter 'sprk_externalrecordaccess' @('11')); Want = '_owninguser_value ne null' }
        @{ Name = 'document candidates: application-owned rows only'; Got = (Get-CandidateFilter 'sprk_document' @('11', '22')); Want = "Microsoft.Dynamics.CRM.In(PropertyName='owninguser',PropertyValues=['11','22'])" }
        @{ Name = 'grant team-owned read: every row in EVERY state, no filter; ownership tested per row (review rounds 2, 3)'; Got = (Get-TeamOwnedQuery 'sprk_externalrecordaccess' 'sprk_externalrecordaccesses' 'a,b'); Want = 'sprk_externalrecordaccesses?$select=a,b' }
        @{ Name = 'grant team-owned rows: only rows with an owning team (a user-owned row is not one)'; Got = ((Select-TeamOwnedRows @([pscustomobject]@{ _owningteam_value = 't1'; _owninguser_value = $null }, [pscustomobject]@{ _owningteam_value = $null; _owninguser_value = 'u1' })) | ForEach-Object { $_._owningteam_value }) -join ','; Want = 't1' }
        @{ Name = 'document team-owned read: none (team-owned documents are not this backfill''s)'; Got = [string](Get-TeamOwnedQuery 'sprk_document' 'sprk_documents' 'a,b'); Want = '' }
    )
    # Review round 2: which TEAM-owned grants are re-owned. The decision is Get-OwnerTeamDecision's, as for every row.
    $mismatches = @(
        @{ Name = 'grant on the unit default team, root made secure -> re-owned to the named team'; Current = $childTeam; Facts = @{ Bu = $secureBu; Flagged = $true }; Want = $true }
        @{ Name = 'grant on the named team, root unsecured -> re-owned to the unit default team'; Current = $secureTeam; Facts = @{ Bu = $childBu; Flagged = $false }; Want = $true }
        @{ Name = 'grant already on its root''s team (ordinary) -> not a candidate'; Current = $childTeam; Facts = @{ Bu = $childBu; Flagged = $false }; Want = $false }
        @{ Name = 'grant already on its root''s team (secure) -> not a candidate'; Current = $secureTeam; Facts = @{ Bu = $secureBu; Flagged = $true }; Want = $false }
        @{ Name = 'grant on a team, root flagged but not isolated -> never written (UNRESOLVABLE)'; Current = $childTeam; Facts = @{ Bu = $childBu; Flagged = $true }; Want = $false }
    )
    foreach ($m in $mismatches) {
        $d = Get-OwnerTeamDecision -Facts $m.Facts -Topology $ok -DefaultTeamOf $defaults
        $got = Test-TeamMismatch -CurrentTeam $m.Current -Decision $d
        $pass = $got -eq $m.Want
        if (-not $pass) { $failures++ }
        Write-Host ("{0} {1} -> mismatch={2} team={3}" -f $(if ($pass) { 'PASS' } else { 'FAIL' }), $m.Name, $got, $d.Team)
    }
    $cases += $mismatches
    # Review round 3 (K4): each row once, and a team-owned row filed to nothing is counted, not skipped.
    $grantOrder = $TargetOrder['sprk_externalrecordaccess']
    $toSecure = { param($t) Get-OwnerTeamDecision -Facts @{ Bu = $secureBu; Flagged = $true } -Topology $ok -DefaultTeamOf $defaults }
    $row = { param($id, $user, $team, $project, $state = 0) [pscustomobject]@{ sprk_externalrecordaccessid = $id; _owninguser_value = $user; _owningteam_value = $team; _sprk_project_value = $project; _sprk_matter_value = $null; _sprk_workassignment_value = $null; statecode = $state } }
    $planWithState = { param($r) $p = Get-RowPlan -Row $r -Order $grantOrder -Decide $toSecure; "$($p.Outcome) inactive=$($p.Inactive)" }
    $merged = Merge-BackfillRows -Candidates @((& $row 'r1' 'u1' $null 'p1'), (& $row 'r2' 'u1' $null 'p1')) -TeamOwned @((& $row 'R2' 'u1' 't1' 'p1'), (& $row 'r3' $null $childTeam 'p1')) -IdColumn 'sprk_externalrecordaccessid'
    $planOf = { param($r) (Get-RowPlan -Row $r -Order $grantOrder -Decide $toSecure).Outcome }
    $rowCases = @(
        @{ Name = 'dedup: a row in both the user-owned and the team-owned reads is processed once (first reading kept)'; Got = (($merged.Rows | ForEach-Object { $_.sprk_externalrecordaccessid }) -join ',') + ' dropped=' + ($merged.Duplicates -join ','); Want = 'r1,r2,r3 dropped=r2' }
        @{ Name = 'dedup: no overlap drops nothing'; Got = [string](Merge-BackfillRows -Candidates @((& $row 'r1' 'u1' $null 'p1')) -TeamOwned @((& $row 'r3' $null $childTeam 'p1')) -IdColumn 'sprk_externalrecordaccessid').Duplicates.Count; Want = '0' }
        @{ Name = 'unfiled: a TEAM-owned row filed to nothing is counted Unfiled, not skipped'; Got = (& $planOf (& $row 'r4' $null $childTeam $null)); Want = 'Unfiled' }
        @{ Name = 'unfiled: a user-owned row filed to nothing is Unfiled'; Got = (& $planOf (& $row 'r5' 'u1' $null $null)); Want = 'Unfiled' }
        @{ Name = 'plan: a team-owned row on another team than its root''s is written (TeamMismatch)'; Got = (& $planOf (& $row 'r6' $null $childTeam 'p1')); Want = 'Write' }
        @{ Name = 'plan: a team-owned row already on its root''s team is left (Correct)'; Got = (& $planOf (& $row 'r7' $null $secureTeam 'p1')); Want = 'Correct' }
        @{ Name = 'plan: a user-owned row filed to a root is written'; Got = (& $planOf (& $row 'r8' 'u1' $null 'p1')); Want = 'Write' }
        # Review round 4: an INACTIVE user-owned grant is planned like an active one, and reported as NotActive.
        @{ Name = 'plan: an INACTIVE user-owned grant is written, and reported inactive (NotActive)'; Got = (& $planWithState (& $row 'r9' 'u1' $null 'p1' 1)); Want = 'Write inactive=True' }
        @{ Name = 'plan: an INACTIVE grant already on its root''s team is left (Correct)'; Got = (& $planWithState (& $row 'r10' $null $secureTeam 'p1' 1)); Want = 'Correct inactive=True' }
        @{ Name = 'plan: an ACTIVE user-owned grant is written and not reported inactive'; Got = (& $planWithState (& $row 'r11' 'u1' $null 'p1')); Want = 'Write inactive=False' }
        @{ Name = 'dedup: an INACTIVE row in both the user-owned and the team-owned reads is processed once'; Got = (((Merge-BackfillRows -Candidates @((& $row 'r12' 'u1' $null 'p1' 1)) -TeamOwned @((& $row 'R12' 'u1' $childTeam 'p1' 1)) -IdColumn 'sprk_externalrecordaccessid').Rows | ForEach-Object { $_.sprk_externalrecordaccessid }) -join ','); Want = 'r12' }
    )
    # Review round 5 (F1-a): the candidate read returns team-owned rows too; they are not candidates and do not stop the run.
    $appSet = @{ 'u-app' = $true }
    $candidateRead = @((& $row 'c1' 'u-app' $null 'p1'), (& $row 'c2' 'u-human' $null 'p1'), (& $row 'c3' $null $childTeam 'p1'), (& $row 'c4' $null $secureTeam 'p1' 1))
    $countOrCrash = { param($rows) try { [string](Measure-AppOwnedRows $rows $appSet) } catch { 'CRASH' } }
    $rowCases += @(
        @{ Name = 'candidates: team-owned rows in the candidate read are not candidates (owninguser set AND owningteam empty)'; Got = ((Select-UserOwnedRows $candidateRead) | ForEach-Object { $_.sprk_externalrecordaccessid }) -join ','; Want = 'c1,c2' }
        @{ Name = 'candidates: a row naming both owners is not a user-owned candidate'; Got = [string](Select-UserOwnedRows @((& $row 'c5' 'u-app' $childTeam 'p1'))).Count; Want = '0' }
        @{ Name = 'candidates: the AppOwned count over the selected rows does not crash'; Got = (& $countOrCrash (Select-UserOwnedRows $candidateRead)); Want = '1' }
        @{ Name = 'candidates (must-fire control): the AppOwned count over the UNSELECTED read crashes on the team-owned row'; Got = (& $countOrCrash $candidateRead); Want = 'CRASH' }
    )
    # Review round 5 (F1-b): VERIFY is ToWrite 0; an unfiled inactive grant (its root deleted) is counted Unfiled and does not fail it.
    $verifyOf = { param([object[]]$rows, [scriptblock]$decide = $toSecure)
        # Candidates seeded as the main loop seeds it: the user-owned rows of the read, written or not.
        $s = [ordered]@{ Candidates = (Select-UserOwnedRows $rows).Count; TeamMismatch = 0; ToWrite = 0; Unfiled = 0; Unresolvable = 0; ReadFailed = 0; NotActive = 0 }
        foreach ($r in $rows) { Add-PlanToStats -Stats $s -Plan (Get-RowPlan -Row $r -Order $grantOrder -Decide $decide) -ReportNotActive $true }
        "Unfiled=$($s.Unfiled) Unresolvable=$($s.Unresolvable) ReadFailed=$($s.ReadFailed) ToWrite=$($s.ToWrite) NotActive=$($s.NotActive) verify=$(Test-VerifySatisfied $s)" }
    $toUnresolvable = { param($t) Get-OwnerTeamDecision -Facts @{ Bu = $childBu; Flagged = $true } -Topology $ok -DefaultTeamOf $defaults }
    $rowCases += @(
        @{ Name = 'verify: an unfiled INACTIVE grant (root deleted) is counted Unfiled and VERIFY is satisfied'; Got = (& $verifyOf @((& $row 'v1' 'u1' $null $null 1))); Want = 'Unfiled=1 Unresolvable=0 ReadFailed=0 ToWrite=0 NotActive=0 verify=True' }
        @{ Name = 'verify: a grant already on its root''s team beside unfiled residue -> VERIFY satisfied'; Got = (& $verifyOf @((& $row 'v2' $null $secureTeam 'p1'), (& $row 'v3' $null $childTeam $null 1))); Want = 'Unfiled=1 Unresolvable=0 ReadFailed=0 ToWrite=0 NotActive=0 verify=True' }
        # Review round 6: an Unresolvable grant sits on a root that still exists and stays user-owned against D-11, so it FAILS VERIFY.
        @{ Name = 'verify: an Unresolvable grant (root flagged, not isolated) is counted and FAILS VERIFY'; Got = (& $verifyOf @((& $row 'v4' 'u1' $null 'p1')) $toUnresolvable); Want = 'Unfiled=0 Unresolvable=1 ReadFailed=0 ToWrite=0 NotActive=0 verify=False' }
        @{ Name = 'verify (must-fire control): a user-owned grant left to write fails VERIFY'; Got = (& $verifyOf @((& $row 'v5' 'u1' $null 'p1' 1))); Want = 'Unfiled=0 Unresolvable=0 ReadFailed=0 ToWrite=1 NotActive=1 verify=False' }
    )
    # Review round 6 (F2): a failed root read, simulated as Invoke-RestMethod throws it (an HttpResponseException carrying the
    # status). ONLY a 404 is "does not exist" (Unresolvable); 403 / 429 / 5xx / no response are ReadFailed. Both: VERIFY NOT MET.
    $httpFail = { param([int]$code) [Microsoft.PowerShell.Commands.HttpResponseException]::new("Simulated Dataverse $code.", [System.Net.Http.HttpResponseMessage]::new([System.Net.HttpStatusCode]$code)) }
    $readingAs = { param($thrown)
        $decision = Get-OwnerTeamDecision -Facts (Read-TargetFacts -IsRoot $true -Read { throw $thrown }) -Topology $ok -DefaultTeamOf $defaults
        { param($t) $decision }.GetNewClosure() }
    $okRead = { param($t) Get-OwnerTeamDecision -Facts (Read-TargetFacts -IsRoot $true -Read { [pscustomobject]@{ _owningbusinessunit_value = $childBu; sprk_issecure = $false } }) -Topology $ok -DefaultTeamOf $defaults }
    $rowCases += @(
        @{ Name = 'read-failed: root read 403 -> ReadFailed, VERIFY NOT MET'; Got = (& $verifyOf @((& $row 'f1' 'u1' $null 'p1')) (& $readingAs (& $httpFail 403))); Want = 'Unfiled=0 Unresolvable=0 ReadFailed=1 ToWrite=0 NotActive=0 verify=False' }
        @{ Name = 'read-failed: root read 429 (throttled) -> ReadFailed, VERIFY NOT MET'; Got = (& $verifyOf @((& $row 'f2' 'u1' $null 'p1')) (& $readingAs (& $httpFail 429))); Want = 'Unfiled=0 Unresolvable=0 ReadFailed=1 ToWrite=0 NotActive=0 verify=False' }
        @{ Name = 'read-failed: root read 503 -> ReadFailed, VERIFY NOT MET'; Got = (& $verifyOf @((& $row 'f3' 'u1' $null 'p1')) (& $readingAs (& $httpFail 503))); Want = 'Unfiled=0 Unresolvable=0 ReadFailed=1 ToWrite=0 NotActive=0 verify=False' }
        @{ Name = 'read-failed: a TEAM-owned grant whose root read gets no response -> ReadFailed, VERIFY NOT MET'; Got = (& $verifyOf @((& $row 'f4' $null $childTeam 'p1')) (& $readingAs ([System.Net.Http.HttpRequestException]::new('Simulated connection reset.')))); Want = 'Unfiled=0 Unresolvable=0 ReadFailed=1 ToWrite=0 NotActive=0 verify=False' }
        @{ Name = 'read-failed: root read 429 -> the reason names the HTTP status'; Got = [string]([string](& (& $readingAs (& $httpFail 429)) $null).Reason -like '*HTTP 429*'); Want = 'True' }
        @{ Name = 'read 404: root does not exist -> Unresolvable, VERIFY NOT MET'; Got = (& $verifyOf @((& $row 'f5' 'u1' $null 'p1')) (& $readingAs (& $httpFail 404))); Want = 'Unfiled=0 Unresolvable=1 ReadFailed=0 ToWrite=0 NotActive=0 verify=False' }
        @{ Name = 'read 404: the reason is "does not exist"'; Got = [string](& (& $readingAs (& $httpFail 404)) $null).Reason; Want = 'the target does not exist or has no owning business unit' }
        @{ Name = 'verify: Unfiled residue only -> PASS'; Got = (& $verifyOf @((& $row 'f6' 'u1' $null $null), (& $row 'f7' $null $childTeam $null 1))); Want = 'Unfiled=2 Unresolvable=0 ReadFailed=0 ToWrite=0 NotActive=0 verify=True' }
        @{ Name = 'verify: everything clean (each grant on its root''s team, root read OK) -> PASS'; Got = (& $verifyOf @((& $row 'f8' $null $childTeam 'p1'), (& $row 'f9' $null $childTeam 'p2' 1)) $okRead); Want = 'Unfiled=0 Unresolvable=0 ReadFailed=0 ToWrite=0 NotActive=0 verify=True' }
    )
    foreach ($r in $rowCases) {
        $pass = $r.Got -eq $r.Want
        if (-not $pass) { $failures++ }
        Write-Host ("{0} {1}: {2}" -f $(if ($pass) { 'PASS' } else { 'FAIL' }), $r.Name, $r.Got)
    }
    $cases += $rowCases
    foreach ($f in $filters) {
        $pass = $f.Got -eq $f.Want
        if (-not $pass) { $failures++ }
        Write-Host ("{0} {1}: {2}" -f $(if ($pass) { 'PASS' } else { 'FAIL' }), $f.Name, $f.Got)
    }
    Write-Host "Self-test: $($cases.Count + $filters.Count - $failures) passed, $failures failed."
    exit ([int]($failures -gt 0))
}

# ── Logging ────────────────────────────────────────────────────────────────────────────────────────────────
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$logDir = Join-Path $PSScriptRoot 'logs'
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }
if (-not $LogPath) { $LogPath = Join-Path $logDir "backfill-record-ownership-$stamp.log" }
$script:LogWriter = [System.IO.StreamWriter]::new($LogPath, $false, [System.Text.Encoding]::UTF8)
function Write-BackfillLog { param([string]$Line) $script:LogWriter.WriteLine("$(Get-Date -Format 'HH:mm:ss') $Line") }

# ── Auth: the operator's own az CLI identity. No secrets in this script or the repo. ────────────────────────
function Get-DvToken {
    $t = az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv 2>$null
    if (-not $t) { throw "No Dataverse token for $EnvironmentUrl. Run 'az login' and retry." }
    return $t
}

$script:Token = Get-DvToken
$script:TokenAt = Get-Date
function Get-DvHeaders {
    param([switch]$ForWrite)
    # az tokens last ~60-90 minutes; a long run re-acquires rather than failing half-way through.
    if (((Get-Date) - $script:TokenAt).TotalMinutes -gt 45) { $script:Token = Get-DvToken; $script:TokenAt = Get-Date }
    $h = @{
        Authorization      = "Bearer $script:Token"
        Accept             = 'application/json'
        'OData-MaxVersion' = '4.0'
        'OData-Version'    = '4.0'
        Prefer             = 'odata.include-annotations="Microsoft.Dynamics.CRM.lookuplogicalname"'
    }
    if ($ForWrite) {
        $h['Content-Type'] = 'application/json'
        $h['If-Match'] = '*'   # update only — never an upsert that could mint a row
        $h['Prefer'] = 'return=minimal'
    }
    return $h
}

function Invoke-DvGet {
    param([Parameter(Mandatory)][string]$RelativePath)
    $uri = "$EnvironmentUrl/api/data/v9.2/$RelativePath"
    $all = [System.Collections.Generic.List[object]]::new()
    while ($uri) {
        $resp = Invoke-RestMethod -Uri $uri -Headers (Get-DvHeaders) -Method Get
        if ($null -ne $resp.value) { $all.AddRange(@($resp.value)) } else { return , @($resp) }
        $uri = $resp.'@odata.nextLink'
    }
    return , $all.ToArray()
}

# ── Business unit → default owner team (cached; ambiguity refuses, same as the resolver) ────────────────────
$script:TeamByBu = @{}
function Resolve-DefaultOwnerTeam {
    param([string]$BusinessUnitId)
    if ($script:TeamByBu.ContainsKey($BusinessUnitId)) { return $script:TeamByBu[$BusinessUnitId] }
    $teams = Invoke-DvGet "teams?`$select=teamid&`$filter=_businessunitid_value eq $BusinessUnitId and isdefault eq true and teamtype eq $OwnerTeamType&`$top=2"
    $team = if (@($teams).Count -eq 1) { ConvertTo-CleanGuid $teams[0].teamid } else { $null }
    $script:TeamByBu[$BusinessUnitId] = $team
    return $team
}

# ── Task 099b: the resolver's secure-root rules (RecordOwnershipResolver.ResolveFromParentsAsync) ─────────────────
# Resolved ONCE per run. Ambiguity is recorded, not thrown: every secure-aware row is then UNRESOLVABLE (the resolver
# refuses every create in that state), and the other entities are unaffected.
$script:SecureTopology = $null
function Get-SecureTopology {
    if ($script:SecureTopology) { return $script:SecureTopology }
    $escapedBu = $SecureBusinessUnitName.Replace("'", "''")
    # Assigned, never wrapped in @(...): Invoke-DvGet returns its array as ONE pipeline object (`return , $array`), so
    # @(Invoke-DvGet ...) is a one-element array whose Count is always 1 (review round 1: ambiguity was never detected).
    $bus = Invoke-DvGet "businessunits?`$select=businessunitid&`$filter=name eq '$escapedBu'&`$top=2"
    $topology = @{ BuId = $null; Ambiguous = $false; TeamId = $null; TeamCount = 0 }
    if ($bus.Count -gt 1) { $topology.Ambiguous = $true }
    elseif ($bus.Count -eq 1) {
        $topology.BuId = ConvertTo-CleanGuid $bus[0].businessunitid
        $escapedTeam = $SecureOwnerTeamName.Replace("'", "''")
        $teams = Invoke-DvGet "teams?`$select=teamid&`$filter=_businessunitid_value eq $($topology.BuId) and name eq '$escapedTeam' and teamtype eq $OwnerTeamType and isdefault eq false&`$top=2"
        $topology.TeamCount = $teams.Count
        if ($teams.Count -eq 1) { $topology.TeamId = ConvertTo-CleanGuid $teams[0].teamid }
    }
    $script:SecureTopology = $topology
    Write-BackfillLog "SECURE-TOPOLOGY bu='$SecureBusinessUnitName' id=$($topology.BuId) ambiguous=$($topology.Ambiguous) namedTeam='$SecureOwnerTeamName' id=$($topology.TeamId) matches=$($topology.TeamCount)"
    return $topology
}

# A target's owning business unit and, for a ROOT table (the three that carry sprk_issecure), its secure flag. An EMPTY
# flag reads as flagged (fail closed, task 150). Any other table has no flag (Flagged = $false); its unit decides.
$script:RootFacts = @{}
function Get-RootFacts {
    param([string]$EntitySetName, [string]$RecordId)
    $key = "$EntitySetName|$RecordId"
    if ($script:RootFacts.ContainsKey($key)) { return $script:RootFacts[$key] }
    $isRoot = [bool]$SecureFlaggedSets[$EntitySetName]
    $columns = if ($isRoot) { '_owningbusinessunit_value,sprk_issecure' } else { '_owningbusinessunit_value' }
    # Review round 6 (F2): only a 404 is "does not exist"; any other failure is ReadFailed (see Read-TargetFacts).
    $facts = Read-TargetFacts -IsRoot $isRoot -Read { (Invoke-DvGet "$EntitySetName($RecordId)?`$select=$columns")[0] }
    # A failed read is not cached: the next row on the same root reads it again (a throttle may have passed).
    if (-not $facts.ReadFailed) { $script:RootFacts[$key] = $facts }
    return $facts
}

# The team the resolver would answer for a row filed under ONE target, or a refusal reason. Returns @{ Team; Bu; Reason }.
function Resolve-SecureAwareTeam {
    param([string]$EntitySetName, [string]$RecordId)
    return Get-OwnerTeamDecision -Facts (Get-RootFacts $EntitySetName $RecordId) -Topology (Get-SecureTopology) `
        -DefaultTeamOf { param($bu) Resolve-DefaultOwnerTeam $bu }
}

# ── One write: assign, then READ BACK. Returns $null on success or an error string. ──────────────────────────
function Set-Owner {
    param([string]$EntitySetName, [string]$RecordId, [string]$OwnerSet, [string]$OwnerId, [string]$ExpectedOwningColumn)
    $body = @{ 'ownerid@odata.bind' = "/$OwnerSet($OwnerId)" } | ConvertTo-Json -Compress
    try {
        Invoke-RestMethod -Uri "$EnvironmentUrl/api/data/v9.2/$EntitySetName($RecordId)" `
            -Headers (Get-DvHeaders -ForWrite) -Method Patch -Body $body | Out-Null
        $reread = (Invoke-DvGet "$EntitySetName($RecordId)?`$select=_$($ExpectedOwningColumn)_value")[0]
        $actual = Get-Lookup $reread $ExpectedOwningColumn
        if ($actual -ne $OwnerId) { return "read-back mismatch: $ExpectedOwningColumn=$actual, expected $OwnerId" }
        return $null
    } catch {
        return $_.Exception.Message
    }
}

# ══ REVERT MODE ══════════════════════════════════════════════════════════════════════════════════════════════
if ($RevertManifest) {
    $rows = Import-Csv $RevertManifest
    Write-Host "Revert: $($rows.Count) row(s) from $RevertManifest ($(if ($IsDryRun) { 'DRY RUN' } else { 'APPLY' }))"
    $restored = 0; $failed = 0; $skipped = 0
    foreach ($r in $rows) {
        $ownerSet = if ($r.PreviousOwnerType -eq 'team') { 'teams' } else { 'systemusers' }
        $column = if ($r.PreviousOwnerType -eq 'team') { 'owningteam' } else { 'owninguser' }
        # Restore ONLY a row still owned by the team this run set. A row reassigned since then belongs to whoever
        # reassigned it; reverting it would clobber a later, deliberate change.
        $current = (Invoke-DvGet "$($EntitySet[$r.Entity])($($r.Id))?`$select=_owningteam_value")[0]
        if ((Get-Lookup $current 'owningteam') -ne $r.NewTeamId) {
            $skipped++
            Write-BackfillLog "REVERT-SKIP $($r.Entity) $($r.Id): no longer owned by team $($r.NewTeamId) — changed since the backfill"
            continue
        }
        if ($IsDryRun) {
            Write-BackfillLog "REVERT-PLAN $($r.Entity) $($r.Id) -> $ownerSet($($r.PreviousOwnerId))"
            continue
        }
        $err = Set-Owner $EntitySet[$r.Entity] $r.Id $ownerSet $r.PreviousOwnerId $column
        if ($err) { $failed++; Write-BackfillLog "REVERT-FAIL $($r.Entity) $($r.Id): $err" }
        else { $restored++; Write-BackfillLog "REVERTED $($r.Entity) $($r.Id) -> $ownerSet($($r.PreviousOwnerId))" }
    }
    $script:LogWriter.Dispose()
    Write-Host "Restored: $restored  Skipped (changed since): $skipped  Failed: $failed  Log: $LogPath"
    exit ([int]($failed -gt 0))
}

# ══ BACKFILL ═════════════════════════════════════════════════════════════════════════════════════════════════
$appUsers = Invoke-DvGet "systemusers?`$select=systemuserid&`$filter=applicationid ne null"
$appUserIds = @($appUsers | ForEach-Object { ConvertTo-CleanGuid $_.systemuserid })
if ($appUserIds.Count -eq 0) { throw 'No application users found — nothing can be app-owned; refusing to guess.' }
$appUserSet = @{}; foreach ($a in $appUserIds) { $appUserSet[$a] = $true }
$teamCounts = [ordered]@{}   # "<entity>|<team>" -> rows planned to that team

$manifestPath = Join-Path $logDir "record-ownership-manifest-$stamp.csv"
$manifest = $null
if (-not $IsDryRun) {
    $manifest = [System.IO.StreamWriter]::new($manifestPath, $false, [System.Text.Encoding]::UTF8)
    $manifest.WriteLine('Entity,Id,PreviousOwnerType,PreviousOwnerId,PreviousBusinessUnit,NewTeamId,NewBusinessUnit,WrittenAtUtc')
    $manifest.Flush()
}

$summary = [System.Collections.Generic.List[object]]::new()
$writes = 0
foreach ($entity in $Entities) {
    $set = $EntitySet[$entity]
    $order = $TargetOrder[$entity]
    $select = (@("$($entity)id", '_ownerid_value', '_owninguser_value', '_owningteam_value', '_owningbusinessunit_value', 'statecode') +
        ($order.Keys | ForEach-Object { "_$($_)_value" })) -join ','
    # Review round 4: the user-owned candidates in EVERY state (no state clause), so an inactive user-owned grant is re-owned
    # like an active one. NotActive counts, of the planned writes, the inactive rows (reporting only).
    $notActive = if ($ReportNotActive[$entity]) { 0 } else { '-' }
    # Review round 5 (F1-a): the filter narrows the read; Select-UserOwnedRows decides (a team-owned row is not a candidate).
    $candidates = Select-UserOwnedRows (Invoke-DvGet "$set`?`$select=$select&`$filter=$(Get-CandidateFilter $entity $appUserIds)")

    $appOwnedCount = Measure-AppOwnedRows $candidates $appUserSet
    $userOwnedCount = @($candidates).Count
    # Review round 2: the TEAM-owned grants, each checked against its root's team below (TeamMismatch) — in every state since
    # review round 3 (F1), so the read has no filter.
    $teamOwnedRows = @()
    $teamOwnedQuery = Get-TeamOwnedQuery $entity $set $select
    if ($teamOwnedQuery) { $teamOwnedRows = Select-TeamOwnedRows (Invoke-DvGet $teamOwnedQuery) }
    $stats = [ordered]@{ Entity = $entity; Candidates = $userOwnedCount; AppOwned = $appOwnedCount; HumanOwned = $userOwnedCount - $appOwnedCount; TeamOwnedChecked = $(if ($teamOwnedQuery) { @($teamOwnedRows).Count } else { '-' }); TeamMismatch = 0; ToWrite = 0; Written = 0; Failed = 0; Unfiled = 0; Unresolvable = 0; ReadFailed = 0; NotActive = $notActive }
    # Review round 3 (K4): a row read both as a user-owned candidate and as team-owned is processed once.
    $merged = Merge-BackfillRows -Candidates $candidates -TeamOwned $teamOwnedRows -IdColumn "$($entity)id"
    foreach ($dup in $merged.Duplicates) { Write-BackfillLog "DUPLICATE $entity $dup — read both as user-owned and as team-owned; processed once (first reading)." }
    # Every entity: the resolver's secure-aware decision (review round 1 — documents and to-dos owned in the Secure Record
    # unit used to map to that unit's DEFAULT team, and a flagged-but-not-isolated root to an ordinary team).
    $decide = { param($Target) Resolve-SecureAwareTeam $Target.Set $Target.Id }
    foreach ($row in $merged.Rows) {
        $id = ConvertTo-CleanGuid $row."$($entity)id"
        $plan = Get-RowPlan -Row $row -Order $order -Decide $decide
        $currentTeam = $plan.CurrentTeam
        $target = $plan.Target
        $ownedBy = if ($currentTeam) { " (team-owned by $currentTeam)" } else { '' }

        Add-PlanToStats -Stats $stats -Plan $plan -ReportNotActive ([bool]$ReportNotActive[$entity])
        if ($plan.Outcome -eq 'Unfiled') {
            # Review round 3 (K4): a team-owned row filed to nothing is counted too, never skipped silently (known residue).
            Write-BackfillLog "UNFILED $entity $id$ownedBy — filed to nothing; the acting user is not recorded (app-only create). Not written."
            continue
        }
        $bu = $plan.Decision.Bu
        $team = $plan.Decision.Team
        if ($plan.Outcome -eq 'Correct') { continue }
        if ($plan.Outcome -eq 'ReadFailed') {
            # Review round 6 (F2): the root could not be read, so the owner is undecided: not written, and VERIFY is NOT MET.
            Write-BackfillLog "READ-FAILED $entity $id$ownedBy — target $($target.Attribute)=$($target.Id) HTTP $($plan.Decision.Status): $($plan.Decision.Reason). Not written."
            continue
        }
        if ($plan.Outcome -eq 'Unresolvable') {
            Write-BackfillLog "UNRESOLVABLE $entity $id$ownedBy — target $($target.Attribute)=$($target.Id) BU=${bu}: $($plan.Decision.Reason). Not written."
            continue
        }
        # Counted by Add-PlanToStats above: ToWrite; Candidates and TeamMismatch for a TEAM-owned grant on another team than its
        # root's (review round 2); NotActive for an inactive row.
        $teamKey = "$entity|$team"
        $teamCounts[$teamKey] = 1 + $(if ($teamCounts.Contains($teamKey)) { $teamCounts[$teamKey] } else { 0 })
        $previousBu = Get-Lookup $row 'owningbusinessunit'
        $previousOwnerType = if ($currentTeam) { 'team' } else { 'systemuser' }
        $previousOwner = if ($currentTeam) { $currentTeam } else { Get-Lookup $row 'owninguser' }
        $ownerKind = if ($currentTeam) { 'team-mismatch' } elseif ($appUserSet[$previousOwner]) { 'application' } else { 'human' }
        $stateNote = if ($plan.Inactive) { ' [inactive]' } else { '' }
        Write-BackfillLog "PLAN $entity $id$stateNote — via $($target.Attribute)=$($target.Id): owner $previousOwnerType($previousOwner, $ownerKind)@$previousBu -> team($team)@$bu"

        if ($IsDryRun -or ($MaxWritesPerRun -gt 0 -and $writes -ge $MaxWritesPerRun)) { continue }

        # WRITE-AHEAD: the reversal row is on disk before the write it reverses.
        $manifest.WriteLine("$entity,$id,$previousOwnerType,$previousOwner,$previousBu,$team,$bu,$((Get-Date).ToUniversalTime().ToString('o'))")
        $manifest.Flush()

        $err = Set-Owner $set $id 'teams' $team 'owningteam'
        $writes++
        if ($err) { $stats.Failed++; Write-BackfillLog "FAIL $entity ${id}: $err" }
        else { $stats.Written++; Write-BackfillLog "WROTE $entity $id -> team($team)" }
    }
    $summary.Add([pscustomobject]$stats)
}

if ($manifest) { $manifest.Dispose() }

Write-Host ''
Write-Host "Record-ownership backfill — $(if ($IsDryRun) { 'DRY RUN (no writes)' } else { 'APPLY' }) — $EnvironmentUrl"
Write-Host "Application users: $($appUserIds.Count) (the defective owner for sprk_document / sprk_todo; sprk_externalrecordaccess re-owns ANY user-owned row in every state; NotActive = the inactive rows among ToWrite)"
# Review round 4: -Property * - Format-Table shows only the first 10 columns of an object without it, so Unresolvable and
# NotActive were never printed; -Width keeps a narrow console from cutting them.
$summary | Format-Table -Property * -AutoSize | Out-String -Width 4096 | Write-Host
# Review round 6: the VERIFY rule, per entity — ToWrite, Unresolvable and ReadFailed all 0. Unfiled is residue (logged per row).
foreach ($s in $summary) {
    Write-Host ("VERIFY {0}: {1} — ToWrite {2}, Unresolvable {3}, ReadFailed {4} (each must be 0). Residue, not a failure, each row in the log: Unfiled {5}." -f
        $s.Entity, $(if (Test-VerifySatisfied $s) { 'PASS' } else { 'NOT MET' }), $s.ToWrite, $s.Unresolvable, $s.ReadFailed, $s.Unfiled)
}

# Rows planned per target team (read-only name lookups). The Secure Record owner team is always listed, even at 0.
$secureTopology = Get-SecureTopology
$teamRows = [System.Collections.Generic.List[object]]::new()
foreach ($k in $teamCounts.Keys) {
    $entityName, $teamId = $k -split '\|', 2
    $teamName = try { (Invoke-DvGet "teams($teamId)?`$select=name")[0].name } catch { '?' }
    $teamRows.Add([pscustomobject]@{ Entity = $entityName; Team = $teamName; TeamId = $teamId; Rows = $teamCounts[$k] })
}
foreach ($entity in $Entities) {
    if ($secureTopology.TeamId -and -not $teamCounts.Contains("$entity|$($secureTopology.TeamId)")) {
        $teamRows.Add([pscustomobject]@{ Entity = $entity; Team = $SecureOwnerTeamName; TeamId = $secureTopology.TeamId; Rows = 0 })
    }
}
Write-Host "Rows planned per target team:"
$teamRows | Format-Table -AutoSize | Out-String | Write-Host
if (-not $IsDryRun) { Write-Host "Reversal manifest: $manifestPath  (undo: -RevertManifest `"$manifestPath`" -Apply)" }
# Disposed last: the per-team summary above may be the first caller of Get-SecureTopology, which logs.
$script:LogWriter.Dispose()
Write-Host "Log: $LogPath"
exit ([int](@($summary | Where-Object { $_.Failed -gt 0 }).Count -gt 0))
