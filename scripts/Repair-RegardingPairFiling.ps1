#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Finds and repairs project and work-assignment rows whose ADR-024 regarding pair (sprk_regardingrecordid +
    sprk_regardingrecordtype) names a matter, project or work assignment in a way the #1616 rules no longer accept. Dry
    run by default; -Apply writes; -Verify checks — the gate that runs AFTER the project pair is locked.

.DESCRIPTION
    #1616: the pair is plain text, so before the fix any user with Write could set it through the Web API and skip the
    AppendTo check a typed lookup costs. After the fix:
      * a WORK ASSIGNMENT is filed under a matter or project only through its typed lookups (sprk_regardingmatter,
        sprk_regardingproject); a pair naming a matter or project NEITHER repeats is undetermined to the readers
        (SecureRootInheritance.PairNamesAParentOnItsOwn) — nothing is shared or un-secured from it — and a pair naming
        any root (a matter, a project or ANOTHER work assignment) that no typed lookup repeats is refused by the container
        resolver (no document is stored in, or answered from, that root's container);
      * a PROJECT's pair stays its filing, but only the BFF can write it (scripts/Set-RegardingPairFieldSecurity.ps1).
    Rows written before the fix are decided here.

    CANDIDATES (a root is sprk_matter, sprk_project or sprk_workassignment; ids compared as GUIDs, never as text)
      W  a sprk_workassignment whose pair names a root that no typed lookup names (a work assignment has no typed lookup
         to another work assignment, so a pair naming one is always a candidate);
      P  a sprk_project whose pair names a root.

    WHO SET THE PAIR — the latest audited change of sprk_regardingrecordid to its current value
    (RetrieveAttributeChangeHistory). That setter is TRUSTED when it is a -BffApplicationIds application user (the BFF
    asked the caller's AppendTo before writing), and VERIFIED when RetrievePrincipalAccess says it holds AppendTo on the
    record the pair names NOW (a later loss of access reads as unverified — the safe direction). No audit row, a setter
    that cannot be read, or no AppendTo → UNVERIFIED. Fails closed: unverified is never treated as allowed.

    DECISIONS (-Apply), each a single PATCH as the operator:
      W, a matter or project target exists, trusted/verified, the matching typed lookup is EMPTY → SET that lookup to the
        pair's record (the row is then filed exactly as the pair said, now through a lookup Dataverse checks);
      W, anything else (target deleted, unverified, a work-assignment target, or the matching typed lookup names another
        record) → CLEAR the pair (sprk_regardingrecordid, sprk_regardingrecordtype and the display columns
        sprk_regardingrecordname / sprk_regardingrecordnumber / sprk_regardingrecordurl where the table has them); the
        typed lookups stay;
      P, target exists, trusted/verified → KEEP;
      P, target deleted or unverified → CLEAR the pair as above;
      REVIEW instead of CLEAR when the row is secure (sprk_issecure) and the record its pair names exists and is secure:
        the row may be secured THROUGH that pair, and clearing it could let the access job loosen it. Nothing is
        written for a REVIEW row; the operator decides it by hand (set the typed lookup, or clear the pair and re-secure).
    The access job (SecureRootInheritanceJob) re-decides every changed row on its next cycle. Documents already stored in
    a container a cleared pair led to stay where they are; the dry run lists the rows so they can be checked.

    -Verify exits 1 while any row is still to SET, CLEAR or REVIEW (every P candidate must be KEEP).

.PARAMETER EnvironmentUrl
    e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER BffApplicationIds
    Application (client) ids of the BFF's Dataverse application users (dev: 5967251e-171c-46fe-a6c2-ef843c90309d,
    1e40baad-e065-4aea-a8d4-4b7ab273458c).

.PARAMETER Apply
    Write mode. Without it the script never writes.

.PARAMETER Verify
    Read-only check with a pass/fail exit code. Cannot be combined with -Apply.

.EXAMPLE
    .\Repair-RegardingPairFiling.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c

.NOTES
    #1616 (unified-access-control-r2). LIVE ORDER: (1) deploy the BFF with #1616; (2) Set-RegardingPairFieldSecurity.ps1
    -Apply (LOCK the project pair, so no user can write it any more); (3) THIS script -Apply; (4) THIS script -Verify — the
    gate. Securing a column does not clear what was written before, and a pair PATCHed between a repair and the lock would
    survive it, so the repair runs after the lock and its -Verify is the last step. Auth: the operator's own az CLI
    identity (System Administrator). No secrets.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EnvironmentUrl,
    [Parameter(Mandatory)][string[]]$BffApplicationIds,
    [switch]$Apply,
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
if ($Apply -and $Verify) { throw '-Apply and -Verify are separate modes; run -Apply first, then -Verify.' }
$BffApplicationIds = @($BffApplicationIds | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
foreach ($id in $BffApplicationIds) { if (-not [Guid]::TryParse($id, [ref][Guid]::Empty)) { throw "-BffApplicationIds: '$id' is not a GUID." } }
$Api = "$EnvironmentUrl/api/data/v9.2"

# ── Constants (the same names as SecureRootInheritance: PairIdColumn, PairTypeColumn, TypedFilingColumns) ─────────
$PairId = 'sprk_regardingrecordid'
$PairType = 'sprk_regardingrecordtype'
$PairDisplay = @('sprk_regardingrecordname', 'sprk_regardingrecordnumber', 'sprk_regardingrecordurl')
$Roots = @('sprk_matter', 'sprk_project', 'sprk_workassignment')   # RecordContainerResolver.ChildAncestorLinks RecordKind.Root
$TypedFiling = @{ sprk_workassignment = @{ sprk_matter = 'sprk_regardingmatter'; sprk_project = 'sprk_regardingproject' }; sprk_project = @{} }

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
function Invoke-DvGetAll([string]$Path) {
    $rows = [System.Collections.Generic.List[object]]::new()
    $h = $headers.Clone(); $h['Prefer'] = 'odata.maxpagesize=500'
    $next = "$Api/$Path"
    while ($next) {
        $page = Invoke-RestMethod -Uri $next -Headers $h -Method Get
        foreach ($r in @($page.value)) { $rows.Add($r) }
        $next = $page.'@odata.nextLink'
    }
    , $rows
}
function Invoke-DvPatch([string]$Path, $Body) {
    $h = $headers.Clone(); $h['If-Match'] = '*'
    Invoke-RestMethod -Uri "$Api/$Path" -Headers $h -Method Patch -Body ($Body | ConvertTo-Json -Depth 5 -Compress) | Out-Null
}

$org = (Invoke-DvGet 'organizations?$select=name').value[0].name
Write-Host "Environment : $EnvironmentUrl (org '$org')"
Write-Host ("Mode        : {0}" -f $(if ($Verify) { 'VERIFY (read-only)' } elseif ($Apply) { 'APPLY' } else { 'DRY RUN (no writes)' }))

$bffUserIds = @()
foreach ($appId in $BffApplicationIds) {
    $u = @((Invoke-DvGet "systemusers?`$select=systemuserid&`$filter=applicationid eq $appId").value) | Select-Object -First 1
    if ($u) { $bffUserIds += $u.systemuserid.ToString().ToLowerInvariant() }
    else { Write-Host "  WARN     no application user for appId $appId — rows it set read as unverified" -ForegroundColor Yellow }
}

$recordTypes = @{}
foreach ($rt in (Invoke-DvGetAll 'sprk_recordtype_refs?$select=sprk_recordtype_refid,sprk_recordlogicalname')) {
    $recordTypes[$rt.sprk_recordtype_refid.ToString().ToLowerInvariant()] = "$($rt.sprk_recordlogicalname)".Trim().ToLowerInvariant()
}
$meta = @{}
function Get-Meta([string]$Table) {
    if (-not $meta.ContainsKey($Table)) {
        $def = Invoke-DvGet "EntityDefinitions(LogicalName='$Table')?`$select=EntitySetName"
        $attrs = @((Invoke-DvGet "EntityDefinitions(LogicalName='$Table')/Attributes?`$select=LogicalName").value | ForEach-Object { $_.LogicalName })
        $nav = @{}
        foreach ($rel in @((Invoke-DvGet "EntityDefinitions(LogicalName='$Table')/ManyToOneRelationships?`$select=ReferencingAttribute,ReferencingEntityNavigationPropertyName").value)) {
            $nav[$rel.ReferencingAttribute] = $rel.ReferencingEntityNavigationPropertyName
        }
        $meta[$Table] = @{ Set = $def.EntitySetName; Attributes = $attrs; Nav = $nav }
    }
    $meta[$Table]
}

function ConvertTo-Guid($Value) {
    $g = [Guid]::Empty
    if ($null -ne $Value -and [Guid]::TryParse("$Value".Trim(), [ref]$g) -and $g -ne [Guid]::Empty) { return $g }
    $null
}

function Get-PairSetter([string]$Table, [Guid]$Id, [Guid]$Pair) {
    $set = (Get-Meta $Table).Set
    $target = [uri]::EscapeDataString("{'@odata.id':'$set($Id)'}")
    try {
        $history = Invoke-DvGet "RetrieveAttributeChangeHistory(Target=@t,AttributeLogicalName=@a)?@t=$target&@a='$PairId'"
    } catch { return $null }
    $match = @($history.AuditDetailCollection.AuditDetails | Where-Object {
            $_.NewValue -and (ConvertTo-Guid $_.NewValue.$PairId) -eq $Pair
        } | Sort-Object { [datetime]$_.AuditRecord.createdon } -Descending) | Select-Object -First 1
    if ($match -and $match.AuditRecord._userid_value) { return $match.AuditRecord._userid_value.ToString().ToLowerInvariant() }
    $null
}

function Test-AppendTo([string]$UserId, [string]$ParentTable, [Guid]$ParentId) {
    $target = [uri]::EscapeDataString("{'@odata.id':'$((Get-Meta $ParentTable).Set)($ParentId)'}")
    try {
        $rights = Invoke-DvGet "systemusers($UserId)/Microsoft.Dynamics.CRM.RetrievePrincipalAccess(Target=@t)?@t=$target"
        return "$($rights.AccessRights)" -match 'AppendToAccess'
    } catch { return $false }
}

# The record a pair names: $null when it does not exist; otherwise the row with its sprk_issecure (every root carries it).
function Get-Target([string]$Table, [Guid]$Id) {
    try { return Invoke-DvGet "$((Get-Meta $Table).Set)($Id)?`$select=$($Table)id,sprk_issecure" }
    catch { if ($_.Exception.Response.StatusCode.value__ -eq 404) { return $null }; throw }
}

$decisions = [System.Collections.Generic.List[object]]::new()
foreach ($table in @('sprk_workassignment', 'sprk_project')) {
    $m = Get-Meta $table
    $typed = $TypedFiling[$table]
    $select = @("$($table)id", $PairId, "_$($PairType)_value", 'sprk_issecure') + @($typed.Values | ForEach-Object { "_$($_)_value" })
    $rows = Invoke-DvGetAll "$($m.Set)?`$select=$($select -join ',')&`$filter=$PairId ne null"
    foreach ($row in $rows) {
        $parsed = ConvertTo-Guid $row.$PairId
        if (-not $parsed) { continue }                                                                # not a filing (the readers say undetermined; the BFF refuses it)
        $typeRef = $row."_$($PairType)_value"
        if (-not $typeRef) { continue }                                                               # untyped: named only through a lookup that repeats it
        $pairTable = $recordTypes[$typeRef.ToString().ToLowerInvariant()]
        if ($pairTable -notin $Roots) { continue }                                                    # names a non-root (an invoice, an event...)
        $typedIds = @($typed.Keys | Where-Object { $_ -eq $pairTable } | ForEach-Object { ConvertTo-Guid $row."_$($typed[$_])_value" } | Where-Object { $_ })
        if ($typedIds -contains $parsed) { continue }                                                 # repeats the typed lookup to that table: filed as before

        $id = [Guid]$row."$($table)id"
        $target = Get-Target $pairTable $parsed
        $exists = $null -ne $target
        $setter = if ($exists) { Get-PairSetter $table $id $parsed } else { $null }
        $trust = if (-not $exists) { 'target deleted' }
        elseif (-not $setter) { 'UNVERIFIED (no audited setter)' }
        elseif ($bffUserIds -contains $setter) { 'trusted (BFF)' }
        elseif (Test-AppendTo $setter $pairTable $parsed) { "verified (setter $setter has AppendTo)" }
        else { "UNVERIFIED (setter $setter has no AppendTo)" }
        $ok = $trust -like 'trusted*' -or $trust -like 'verified*'

        $action = 'CLEAR'; $column = $null
        if ($table -eq 'sprk_project') { if ($ok) { $action = 'KEEP' } }
        elseif ($ok -and $typed.ContainsKey($pairTable)) {
            $column = $typed[$pairTable]
            if (-not $row."_$($column)_value") { $action = 'SET' }
        }
        # A secure row whose pair names a secure record may be secured THROUGH that pair: never cleared unattended.
        if ($action -eq 'CLEAR' -and $row.sprk_issecure -eq $true -and $exists -and $target.sprk_issecure -eq $true) { $action = 'REVIEW' }
        $decisions.Add([pscustomobject]@{ Table = $table; Id = $id; PairTable = $pairTable; Pair = $parsed; Trust = $trust; Action = $action; Column = $column })
    }
}

Write-Host "`nCandidates"
foreach ($d in $decisions) {
    $what = switch ($d.Action) {
        'SET' { "set $($d.Column) to $($d.PairTable) $($d.Pair)" }
        'KEEP' { 'keep the pair (its filing)' }
        'REVIEW' { 'REVIEW by hand: the row is secure and its pair names a secure record (nothing written)' }
        default { 'clear the pair' }
    }
    Write-Host ("  {0,-5} {1} {2} → {3} {4} [{5}] : {6}" -f $d.Action, $d.Table, $d.Id, $d.PairTable, $d.Pair, $d.Trust, $what)
}
$toChange = @($decisions | Where-Object { $_.Action -in @('SET', 'CLEAR') })
$toReview = @($decisions | Where-Object Action -eq 'REVIEW')
Write-Host ("`n{0} candidate(s); {1} to change (SET {2}, CLEAR {3}); {4} to REVIEW by hand; {5} kept." -f $decisions.Count, $toChange.Count,
    @($toChange | Where-Object Action -eq 'SET').Count, @($toChange | Where-Object Action -eq 'CLEAR').Count, $toReview.Count,
    @($decisions | Where-Object Action -eq 'KEEP').Count)

if ($Verify) {
    if ($toChange.Count -eq 0 -and $toReview.Count -eq 0) { Write-Host "`nPASS: no work assignment or project is filed by a pair the #1616 rules refuse." -ForegroundColor Green; exit 0 }
    Write-Host "`nFAIL: $($toChange.Count) row(s) still need the repair (-Apply) and $($toReview.Count) a decision by hand (REVIEW)." -ForegroundColor Red; exit 1
}
if (-not $Apply) { Write-Host "`nDRY RUN complete — nothing was written." -ForegroundColor Cyan; exit 0 }

$failed = 0
foreach ($d in $toChange) {
    $m = Get-Meta $d.Table
    try {
        if ($d.Action -eq 'SET') {
            $nav = $m.Nav[$d.Column]
            if (-not $nav) { throw "no navigation property for $($d.Table).$($d.Column)" }
            Invoke-DvPatch "$($m.Set)($($d.Id))" @{ "$nav@odata.bind" = "/$((Get-Meta $d.PairTable).Set)($($d.Pair))" }
        } else {
            $nav = $m.Nav[$PairType]
            if (-not $nav) { throw "no navigation property for $($d.Table).$PairType" }
            $body = @{ $PairId = $null; "$nav@odata.bind" = $null }
            foreach ($c in $PairDisplay | Where-Object { $_ -in $m.Attributes }) { $body[$c] = $null }
            Invoke-DvPatch "$($m.Set)($($d.Id))" $body
        }
        Write-Host "  DONE     $($d.Action) $($d.Table) $($d.Id)" -ForegroundColor Green
    } catch {
        $failed++
        Write-Host "  FAIL     $($d.Action) $($d.Table) $($d.Id): $($_.ErrorDetails.Message) $($_.Exception.Message)" -ForegroundColor Red
    }
}
if ($failed -gt 0) { Write-Host "`n$failed write(s) failed; re-run -Apply, then -Verify." -ForegroundColor Red; exit 1 }
if ($toReview.Count -gt 0) { Write-Host "`n$($toReview.Count) REVIEW row(s) were not written; decide them by hand, then -Verify." -ForegroundColor Yellow }
Write-Host "`nAPPLIED. Run -Verify." -ForegroundColor Green
