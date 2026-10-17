#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Re-owns EXISTING app-owned records to a business unit's DEFAULT OWNER TEAM, record-first — the backfill for
    write-path invariant I-6 (spaarkeai-word-add-in-r1 task 080). Dry-run by default; every write is reversible.

.DESCRIPTION
    Until task 080, every record the BFF created app-only was owned by the BFF APPLICATION USER, which sits in the
    ROOT business unit (measured 2026-09-22: all 512 sprk_document rows). Dataverse Deep depth reaches a unit's
    descendants, never its parent, so no child-business-unit user could read them. New creates are now owned by a
    default owner team (RecordOwnershipResolver). This script fixes the rows that already exist.

    WHICH ROWS (candidates): rows of the chosen entities whose owner is an APPLICATION user (systemuser with an
    applicationid). Rows owned by a human user or by a team are left alone and only counted — they are not the
    defect this fixes, and re-owning them would change who they belong to.

    WHICH TEAM — record-first, the SAME order RecordOwnershipResolver applies to new creates:
      sprk_document : the filed record — sprk_matter, sprk_project, sprk_invoice, sprk_workassignment, then the
                      related-family slots the save also writes: sprk_relatedevent, sprk_relatedtodo, sprk_relatedcontact.
      sprk_todo     : the record regarding (matter / project / invoice), then the document, then the communication.
      sprk_externalrecordaccess (unified-access-control-r2 task 099b, D-11, #1010): the grant's ROOT — sprk_project,
                      sprk_matter, sprk_workassignment (a grant binds exactly one) — the SAME parent the BFF's grant core
                      hands RecordOwnershipResolver for a new grant. ACTIVE grants only (statecode = 0); app-owned inactive
                      grants are counted (NotActive) and left alone.
    The target's owningbusinessunit → that unit's default owner team (isdefault = true AND teamtype = 0 — both
    predicates are required; dev has non-default Owner teams and Access teams in the same units).

    SECURE ROOTS (sprk_externalrecordaccess) — the resolver's task 144 / 146 rules, as in
    Migrate-SecureRecordsToNamedOwnerTeam.ps1:
      - a root owned in the Secure Record business unit → that unit's NAMED owner team ('Secure Record Owners',
        teamtype = 0, isdefault = false, exactly one) — never the Secure Record unit's default team;
      - a root whose sprk_issecure is true OR EMPTY (fail closed, task 150) but is owned OUTSIDE the Secure Record unit is
        NOT ISOLATED (an interrupted provisioning) → UNRESOLVABLE, never an ordinary team;
      - more than one business unit with the Secure Record name, or a missing/duplicate named team → UNRESOLVABLE.
    (sprk_document / sprk_todo keep the rule this script always applied to them; see the PR for 099b.)

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
      - A todo filed to a document derives from the DOCUMENT'S business unit: run sprk_document first (the default
        order), then re-run for sprk_todo, so todos follow their documents' new units.

.PARAMETER EnvironmentUrl
    Dataverse environment URL, e.g. https://spaarkedev1.crm.dynamics.com

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
    Dry run: candidates, the planned team per row, unresolvable rows, and per-entity totals. Zero writes.

.EXAMPLE
    .\Backfill-RecordOwnership.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply -MaxWritesPerRun 5
    The SAMPLE: re-own five rows, each read back; writes a reversal manifest. Check them in the app before going on.

.EXAMPLE
    .\Backfill-RecordOwnership.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -RevertManifest .\logs\record-ownership-manifest-20260930-120000.csv -Apply
    Undo that run exactly.

.EXAMPLE
    .\Backfill-RecordOwnership.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Entities sprk_externalrecordaccess
    The grant backfill dry run (task 099b). It is also the VERIFY: after -Apply, it must report ToWrite: 0 and
    Candidates: 0 (no active grant owned by an application user).
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$EnvironmentUrl,

    [switch]$Apply,

    [switch]$WhatIf,

    [ValidateSet('sprk_document', 'sprk_todo', 'sprk_externalrecordaccess')]
    [string[]]$Entities = @('sprk_document', 'sprk_todo'),

    [string]$SecureBusinessUnitName = 'Secure Record',

    [string]$SecureOwnerTeamName = 'Secure Record Owners',

    [int]$MaxWritesPerRun = 0,

    [string]$RevertManifest,

    [string]$LogPath
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
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
# Entities whose candidates are ACTIVE rows only (amendment 4: "0 active grants owned by an application user").
$ActiveOnly = @{ 'sprk_externalrecordaccess' = $true }
# Entities resolved with the resolver's secure-root rules (task 144 / 146 / 150) — see SECURE ROOTS above.
$SecureAware = @{ 'sprk_externalrecordaccess' = $true }
$OwnerTeamType = 0

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

function ConvertTo-CleanGuid {
    # ADR-044: bare, lowercase, at every boundary.
    param([AllowEmptyString()][string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return $null }
    return $Value.Trim().Trim('{', '}').ToLowerInvariant()
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

function Get-Lookup {
    param($Row, [string]$Attribute)
    $p = $Row.PSObject.Properties["_${Attribute}_value"]
    if ($null -eq $p -or $null -eq $p.Value) { return $null }
    return ConvertTo-CleanGuid $p.Value
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

$script:BuByTarget = @{}
function Resolve-TargetBusinessUnit {
    param([string]$EntitySetName, [string]$RecordId)
    $key = "$EntitySetName|$RecordId"
    if ($script:BuByTarget.ContainsKey($key)) { return $script:BuByTarget[$key] }
    try {
        $row = (Invoke-DvGet "$EntitySetName($RecordId)?`$select=_owningbusinessunit_value")[0]
        $bu = Get-Lookup $row 'owningbusinessunit'
    } catch {
        $bu = $null
    }
    $script:BuByTarget[$key] = $bu
    return $bu
}

# ── Task 099b: the resolver's secure-root rules (RecordOwnershipResolver.ResolveFromParentsAsync) ─────────────────
# Resolved ONCE per run. Ambiguity is recorded, not thrown: every secure-aware row is then UNRESOLVABLE (the resolver
# refuses every create in that state), and the other entities are unaffected.
$script:SecureTopology = $null
function Get-SecureTopology {
    if ($script:SecureTopology) { return $script:SecureTopology }
    $escapedBu = $SecureBusinessUnitName.Replace("'", "''")
    $bus = @(Invoke-DvGet "businessunits?`$select=businessunitid&`$filter=name eq '$escapedBu'&`$top=2")
    $topology = @{ BuId = $null; Ambiguous = $false; TeamId = $null; TeamCount = 0 }
    if ($bus.Count -gt 1) { $topology.Ambiguous = $true }
    elseif ($bus.Count -eq 1) {
        $topology.BuId = ConvertTo-CleanGuid $bus[0].businessunitid
        $escapedTeam = $SecureOwnerTeamName.Replace("'", "''")
        $teams = @(Invoke-DvGet "teams?`$select=teamid&`$filter=_businessunitid_value eq $($topology.BuId) and name eq '$escapedTeam' and teamtype eq $OwnerTeamType and isdefault eq false&`$top=2")
        $topology.TeamCount = $teams.Count
        if ($teams.Count -eq 1) { $topology.TeamId = ConvertTo-CleanGuid $teams[0].teamid }
    }
    $script:SecureTopology = $topology
    Write-BackfillLog "SECURE-TOPOLOGY bu='$SecureBusinessUnitName' id=$($topology.BuId) ambiguous=$($topology.Ambiguous) namedTeam='$SecureOwnerTeamName' id=$($topology.TeamId) matches=$($topology.TeamCount)"
    return $topology
}

# A ROOT's owning business unit and its secure flag. sprk_issecure EMPTY reads as flagged (fail closed, task 150).
$script:RootFacts = @{}
function Get-RootFacts {
    param([string]$EntitySetName, [string]$RecordId)
    $key = "$EntitySetName|$RecordId"
    if ($script:RootFacts.ContainsKey($key)) { return $script:RootFacts[$key] }
    try {
        $row = (Invoke-DvGet "$EntitySetName($RecordId)?`$select=_owningbusinessunit_value,sprk_issecure")[0]
        $flag = $row.PSObject.Properties['sprk_issecure']
        $facts = @{ Bu = (Get-Lookup $row 'owningbusinessunit'); Flagged = ($null -eq $flag -or $null -eq $flag.Value -or $flag.Value -eq $true) }
    } catch {
        $facts = @{ Bu = $null; Flagged = $true }
    }
    $script:RootFacts[$key] = $facts
    return $facts
}

# The team the resolver would answer for a row filed under ONE root, or a refusal reason. Returns @{ Team; Bu; Reason }.
function Resolve-SecureAwareTeam {
    param([string]$EntitySetName, [string]$RecordId)
    $facts = Get-RootFacts $EntitySetName $RecordId
    if (-not $facts.Bu) { return @{ Team = $null; Bu = $null; Reason = 'the root does not exist or has no owning business unit' } }
    $secure = Get-SecureTopology
    if ($secure.Ambiguous) { return @{ Team = $null; Bu = $facts.Bu; Reason = "more than one business unit is named '$SecureBusinessUnitName'" } }
    if ($facts.Flagged -and $facts.Bu -ne $secure.BuId) {
        return @{ Team = $null; Bu = $facts.Bu; Reason = 'the root is flagged secure (or its flag is empty) but is not owned in the Secure Record business unit (not isolated)' }
    }
    if ($secure.BuId -and $facts.Bu -eq $secure.BuId) {
        if (-not $secure.TeamId) { return @{ Team = $null; Bu = $facts.Bu; Reason = "the named owner team '$SecureOwnerTeamName' resolved to $($secure.TeamCount) teams, not one" } }
        return @{ Team = $secure.TeamId; Bu = $facts.Bu; Reason = $null }
    }
    $team = Resolve-DefaultOwnerTeam $facts.Bu
    if (-not $team) { return @{ Team = $null; Bu = $facts.Bu; Reason = 'the business unit has no single default owner team' } }
    return @{ Team = $team; Bu = $facts.Bu; Reason = $null }
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
$inList = ($appUserIds | ForEach-Object { "'$_'" }) -join ','

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
    $select = (@("$($entity)id", '_ownerid_value', '_owninguser_value', '_owningbusinessunit_value') +
        ($order.Keys | ForEach-Object { "_$($_)_value" })) -join ','
    $appOwned = "Microsoft.Dynamics.CRM.In(PropertyName='owninguser',PropertyValues=[$inList])"
    $notActive = '-'
    if ($ActiveOnly[$entity]) {
        $candidates = Invoke-DvGet "$set`?`$select=$select&`$filter=$appOwned and statecode eq 0"
        # Reported, never written: an inactive grant confers nothing and is out of D-11's "active grants" check.
        $notActive = @(Invoke-DvGet "$set`?`$select=$($entity)id&`$filter=$appOwned and statecode ne 0").Count
    } else {
        $candidates = Invoke-DvGet "$set`?`$select=$select&`$filter=$appOwned"
    }

    $stats = [ordered]@{ Entity = $entity; Candidates = @($candidates).Count; ToWrite = 0; Written = 0; Failed = 0; Unfiled = 0; Unresolvable = 0; NotActive = $notActive }
    foreach ($row in $candidates) {
        $id = ConvertTo-CleanGuid $row."$($entity)id"
        $target = $null
        foreach ($attr in $order.Keys) {
            $value = Get-Lookup $row $attr
            if ($value) { $target = @{ Attribute = $attr; Set = $order[$attr]; Id = $value }; break }
        }

        if (-not $target) {
            $stats.Unfiled++
            Write-BackfillLog "UNFILED $entity $id — filed to nothing; the acting user is not recorded (app-only create). Not written."
            continue
        }

        if ($SecureAware[$entity]) {
            $answer = Resolve-SecureAwareTeam $target.Set $target.Id
            $bu = $answer.Bu
            $team = $answer.Team
            if (-not $team) {
                $stats.Unresolvable++
                Write-BackfillLog "UNRESOLVABLE $entity $id — target $($target.Attribute)=$($target.Id) BU=${bu}: $($answer.Reason). Not written."
                continue
            }
        } else {
            $bu = Resolve-TargetBusinessUnit $target.Set $target.Id
            $team = if ($bu) { Resolve-DefaultOwnerTeam $bu } else { $null }
            if (-not $team) {
                $stats.Unresolvable++
                Write-BackfillLog "UNRESOLVABLE $entity $id — target $($target.Attribute)=$($target.Id) BU=$bu has no single default owner team. Not written."
                continue
            }
        }

        $stats.ToWrite++
        $previousBu = Get-Lookup $row 'owningbusinessunit'
        $previousOwner = Get-Lookup $row 'owninguser'
        Write-BackfillLog "PLAN $entity $id — via $($target.Attribute)=$($target.Id): owner systemuser($previousOwner)@$previousBu -> team($team)@$bu"

        if ($IsDryRun -or ($MaxWritesPerRun -gt 0 -and $writes -ge $MaxWritesPerRun)) { continue }

        # WRITE-AHEAD: the reversal row is on disk before the write it reverses.
        $manifest.WriteLine("$entity,$id,systemuser,$previousOwner,$previousBu,$team,$bu,$((Get-Date).ToUniversalTime().ToString('o'))")
        $manifest.Flush()

        $err = Set-Owner $set $id 'teams' $team 'owningteam'
        $writes++
        if ($err) { $stats.Failed++; Write-BackfillLog "FAIL $entity ${id}: $err" }
        else { $stats.Written++; Write-BackfillLog "WROTE $entity $id -> team($team)" }
    }
    $summary.Add([pscustomobject]$stats)
}

if ($manifest) { $manifest.Dispose() }
$script:LogWriter.Dispose()

Write-Host ''
Write-Host "Record-ownership backfill — $(if ($IsDryRun) { 'DRY RUN (no writes)' } else { 'APPLY' }) — $EnvironmentUrl"
Write-Host "Application users treated as the defective owner: $($appUserIds.Count)"
$summary | Format-Table -AutoSize | Out-String | Write-Host
if (-not $IsDryRun) { Write-Host "Reversal manifest: $manifestPath  (undo: -RevertManifest `"$manifestPath`" -Apply)" }
Write-Host "Log: $LogPath"
exit ([int](@($summary | Where-Object { $_.Failed -gt 0 }).Count -gt 0))
