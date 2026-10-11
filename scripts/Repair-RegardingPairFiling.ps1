#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Finds and repairs project and work-assignment rows whose ADR-024 regarding pair (sprk_regardingrecordid +
    sprk_regardingrecordtype) files them under a matter or project in a way the #1616 rules no longer accept. Dry run by
    default; -Apply writes; -Verify checks.

.DESCRIPTION
    #1616: the pair is plain text, so before the fix any user with Write could set it through the Web API and skip the
    AppendTo check a typed lookup costs. After the fix:
      * a WORK ASSIGNMENT is filed under a matter or project only through its typed lookups (sprk_regardingmatter,
        sprk_regardingproject); a pair naming a matter or project NEITHER repeats is undetermined to the readers
        (SecureRootInheritance.PairNamesAParentOnItsOwn) — nothing is secured, shared or un-secured from it;
      * a PROJECT's pair stays its filing, but only the BFF can write it (scripts/Set-RegardingPairFieldSecurity.ps1).
    Rows written before the fix are decided here.

    CANDIDATES
      W  a sprk_workassignment whose pair names a sprk_matter or sprk_project that neither typed lookup names;
      P  a sprk_project whose pair names a sprk_matter or sprk_project.

    WHO SET THE PAIR — the latest audited change of sprk_regardingrecordid to its current value
    (RetrieveAttributeChangeHistory). That setter is TRUSTED when it is a -BffApplicationIds application user (the BFF
    asked the caller's AppendTo before writing), and VERIFIED when RetrievePrincipalAccess says it holds AppendTo on the
    record the pair names NOW (a later loss of access reads as unverified — the safe direction). No audit row, a setter
    that cannot be read, or no AppendTo → UNVERIFIED. Fails closed: unverified is never treated as allowed.

    DECISIONS (-Apply), each a single PATCH as the operator:
      W, target exists, trusted/verified, the matching typed lookup is EMPTY → SET that lookup to the pair's record
        (the row is then filed exactly as the pair said, now through a lookup Dataverse checks);
      W, anything else (target deleted, unverified, or the matching typed lookup names another record) → CLEAR the pair
        (sprk_regardingrecordid, sprk_regardingrecordtype and the display columns sprk_regardingrecordname /
        sprk_regardingrecordnumber / sprk_regardingrecordurl where the table has them); the typed lookups stay;
      P, target exists, trusted/verified → KEEP;
      P, target deleted or unverified → CLEAR the pair as above.
    The access job (SecureRootInheritanceJob) re-decides every changed row on its next cycle.

    -Verify exits 1 while any W candidate remains or any P candidate is not KEEP.

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
    #1616 (unified-access-control-r2). Run its -Apply BEFORE Set-RegardingPairFieldSecurity.ps1 -Apply. Auth: the
    operator's own az CLI identity (System Administrator). No secrets.
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
$Parents = @('sprk_matter', 'sprk_project')
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

function Get-PairSetter([string]$Table, [Guid]$Id, [string]$Pair) {
    $set = (Get-Meta $Table).Set
    $target = [uri]::EscapeDataString("{'@odata.id':'$set($Id)'}")
    try {
        $history = Invoke-DvGet "RetrieveAttributeChangeHistory(Target=@t,AttributeLogicalName=@a)?@t=$target&@a='$PairId'"
    } catch { return $null }
    $match = @($history.AuditDetailCollection.AuditDetails | Where-Object {
            $_.NewValue -and "$($_.NewValue.$PairId)".Trim().ToLowerInvariant() -eq $Pair
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

function Test-Exists([string]$Table, [Guid]$Id) {
    try { Invoke-DvGet "$((Get-Meta $Table).Set)($Id)?`$select=$($Table)id" | Out-Null; return $true }
    catch { if ($_.Exception.Response.StatusCode.value__ -eq 404) { return $false }; throw }
}

$decisions = [System.Collections.Generic.List[object]]::new()
foreach ($table in @('sprk_workassignment', 'sprk_project')) {
    $m = Get-Meta $table
    $typed = $TypedFiling[$table]
    $select = @("$($table)id", $PairId, "_$($PairType)_value") + @($typed.Values | ForEach-Object { "_$($_)_value" })
    $rows = Invoke-DvGetAll "$($m.Set)?`$select=$($select -join ',')&`$filter=$PairId ne null"
    foreach ($row in $rows) {
        $pair = "$($row.$PairId)".Trim().ToLowerInvariant()
        $parsed = [Guid]::Empty
        if (-not [Guid]::TryParse($pair, [ref]$parsed) -or $parsed -eq [Guid]::Empty) { continue }   # not a filing (the readers say undetermined; the BFF refuses it)
        $typeRef = $row."_$($PairType)_value"
        if (-not $typeRef) { continue }                                                               # untyped: named only through a lookup that repeats it
        $pairTable = $recordTypes[$typeRef.ToString().ToLowerInvariant()]
        if ($pairTable -notin $Parents) { continue }                                                  # names a non-parent (an invoice, an event...)
        $typedIds = @($typed.Values | ForEach-Object { "$($row."_$($_)_value")".ToLowerInvariant() } | Where-Object { $_ })
        if ($typedIds -contains $pair) { continue }                                                   # repeats a typed lookup: filed as before

        $id = [Guid]$row."$($table)id"
        $exists = Test-Exists $pairTable $parsed
        $setter = if ($exists) { Get-PairSetter $table $id $pair } else { $null }
        $trust = if (-not $exists) { 'target deleted' }
        elseif (-not $setter) { 'UNVERIFIED (no audited setter)' }
        elseif ($bffUserIds -contains $setter) { 'trusted (BFF)' }
        elseif (Test-AppendTo $setter $pairTable $parsed) { "verified (setter $setter has AppendTo)" }
        else { "UNVERIFIED (setter $setter has no AppendTo)" }
        $ok = $trust -like 'trusted*' -or $trust -like 'verified*'

        $action = 'CLEAR'; $column = $null
        if ($table -eq 'sprk_project') { if ($ok) { $action = 'KEEP' } }
        elseif ($ok) {
            $column = $typed[$pairTable]
            if (-not $row."_$($column)_value") { $action = 'SET' }
        }
        $decisions.Add([pscustomobject]@{ Table = $table; Id = $id; PairTable = $pairTable; Pair = $parsed; Trust = $trust; Action = $action; Column = $column })
    }
}

Write-Host "`nCandidates"
foreach ($d in $decisions) {
    $what = switch ($d.Action) {
        'SET' { "set $($d.Column) to $($d.PairTable) $($d.Pair)" }
        'KEEP' { 'keep the pair (its filing)' }
        default { 'clear the pair' }
    }
    Write-Host ("  {0,-5} {1} {2} → {3} {4} [{5}] : {6}" -f $d.Action, $d.Table, $d.Id, $d.PairTable, $d.Pair, $d.Trust, $what)
}
$toChange = @($decisions | Where-Object Action -ne 'KEEP')
Write-Host ("`n{0} candidate(s); {1} to change (SET {2}, CLEAR {3}); {4} kept." -f $decisions.Count, $toChange.Count,
    @($toChange | Where-Object Action -eq 'SET').Count, @($toChange | Where-Object Action -eq 'CLEAR').Count, ($decisions.Count - $toChange.Count))

if ($Verify) {
    if ($toChange.Count -eq 0) { Write-Host "`nPASS: no work assignment or project is filed by a pair the #1616 rules refuse." -ForegroundColor Green; exit 0 }
    Write-Host "`nFAIL: $($toChange.Count) row(s) still need the repair (-Apply)." -ForegroundColor Red; exit 1
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
Write-Host "`nAPPLIED. Run -Verify." -ForegroundColor Green
