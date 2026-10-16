#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Ontology task 049 (spaarke-ontology-platform-r1): remove, from every business-unit copy of the named roles, exactly
    three privileges, then re-run the union-of-roles check (NFR-11). Dry run by default; -WhatIf is supported.

.DESCRIPTION
    THE THREE REMOVALS (closed list; nothing else is ever removed or added):
      D-17   Spaarke Console User           prvWritesprk_Signal            (only the BFF commit route closes a Signal)
      D-62   Spaarke Console User           prvCreatesprk_DecisionRecord   (only the BFF commit route creates a Decision Record)
      D-147  Spaarke Ontology Administrator prvCreatesprk_DecisionRecord   (same basis, owner 2026-10-15)

    uac-r2 K-A (binding): Write on sprk_signal never goes without Create on sprk_decisionrecord in the SAME change. The
    Create removals therefore run FIRST; if one fails the script stops before any Signal Write is removed.

    METHOD (task 008, #1618: SpaarkeMaster is never imported in full)
      1. Enumerate EVERY business-unit copy of each role (privileges live on the root copy; child copies read 0, which is
         the inherited-shell artifact of notes/security-roles.md 7.3, not a defect).
      2. RetrieveRolePrivilegesRole on every copy BEFORE: count and the exact privileges.
      3. RemovePrivilegeRole per copy that holds one of the privileges named for that role. A copy that does not hold it is
         skipped (this is what makes a re-run a no-op).
      4. RetrieveRolePrivilegesRole on every copy AFTER; diff. The run FAILS (exit 1) unless the removed set is exactly the
         planned set, nothing was added, and no depth changed. The script never tries to repair a diff; it reports it.
         (It never runs the Secure Record Owner SharePoint-privilege strip loop; that is a different role and a different
         procedure, project CLAUDE.md section 3, D-110.)
      5. Union check over every role in the environment: who holds Write or Delete on sprk_decisionrecord, Create on
         sprk_decisionrecord, and Write on sprk_signal, with each holder's members (human / application users, teams).

    ASSERTIONS (exit 1 if any fails)
      A1  No Spaarke role and no non-platform, non-admin role holds Write or Delete on sprk_decisionrecord, and the platform
          roles holding them have no human member.
      A2  Create on sprk_decisionrecord is held only by platform roles, admin roles and Spaarke Ontology Service (the writer).
      A3  Spaarke Console User does not hold Write on sprk_signal.
      A4  Control: Spaarke Ontology Service still holds Create on sprk_decisionrecord and Write on sprk_signal.
    NOT an assertion but always printed (FINDING): every other non-platform, non-admin role that still holds Write on
    sprk_signal. On spaarkedev1 that is Spaarke Ontology Administrator (Deep), which D-17 does not name.

    MODES
      (default)        Dry run. Reads everything, prints the plan and the PROJECTED union (the live union with the planned
                       removals taken out). Writes nothing to Dataverse.
      -WhatIf          Same as the default, and prints a "What if:" line for each RemovePrivilegeRole call.
      -Apply           Performs the removals, re-reads, diffs, and runs the union check on the real after state.
      -Verify          Read-only. Union check on the live state, no plan. Exit 0 / 1 on the assertions.

    EVIDENCE. Every mode writes the run transcript plus the before/after privilege listings to -EvidencePath (default:
    projects/spaarke-ontology-platform-r1/notes/049-role-removal-evidence-<mode>-<utc>.md).

    Auth: the operator's own az CLI identity (az account get-access-token). No secrets.

.EXAMPLE
    pwsh -File scripts/Remove-ConsoleSignalWriteAndDecisionRecordCreate.ps1 -WhatIf
    pwsh -File scripts/Remove-ConsoleSignalWriteAndDecisionRecordCreate.ps1 -Verify
    pwsh -File scripts/Remove-ConsoleSignalWriteAndDecisionRecordCreate.ps1 -Apply
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = 'https://spaarkedev1.crm.dynamics.com',
    [switch]$Apply,
    [switch]$Verify,
    [string]$EvidencePath
)

$ErrorActionPreference = 'Stop'
if ($Apply -and $Verify) { throw '-Apply and -Verify are separate modes.' }

$BaseUrl = $EnvironmentUrl.TrimEnd('/')
$Api = "$BaseUrl/api/data/v9.2"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Mode = if ($Verify) { 'verify' } elseif ($Apply -and -not $WhatIfPreference) { 'apply' } elseif ($WhatIfPreference) { 'whatif' } else { 'dryrun' }
$Stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
if (-not $EvidencePath) {
    $EvidencePath = Join-Path $RepoRoot "projects/spaarke-ontology-platform-r1/notes/049-role-removal-evidence-$Mode-$Stamp.md"
}
elseif (-not [IO.Path]::IsPathRooted($EvidencePath)) {
    $EvidencePath = Join-Path $RepoRoot $EvidencePath   # absolute: [IO.File] resolves against the process directory otherwise
}

# ── The closed list. Order matters: the two Decision Record Create removals run before the Signal Write removal (K-A). ──
$Removals = @(
    [pscustomobject]@{ Role = 'Spaarke Ontology Administrator'; Privilege = 'prvCreatesprk_DecisionRecord'; Decision = 'D-147' }
    [pscustomobject]@{ Role = 'Spaarke Console User'; Privilege = 'prvCreatesprk_DecisionRecord'; Decision = 'D-62' }
    [pscustomobject]@{ Role = 'Spaarke Console User'; Privilege = 'prvWritesprk_Signal'; Decision = 'D-17' }
)
$ControlRole = 'Spaarke Ontology Service'
$PlatformRoles = @('Service Writer', 'Service Deleter', 'Service Reader')   # held only by Microsoft first-party application identities
$AdminRoles = @('System Administrator', 'System Customizer')
$DepthNames = @{ 1 = 'Basic'; 2 = 'Local'; 4 = 'Deep'; 8 = 'Global' }

# ── Output: console and evidence together ────────────────────────────────────────────────────────────────────
$Transcript = New-Object System.Collections.Generic.List[string]
function Out-Line([string]$Text = '', [string]$Color = '') {
    $Transcript.Add($Text)
    if ($Color) { Write-Host $Text -ForegroundColor $Color } else { Write-Host $Text }
}

# ── Auth and HTTP ────────────────────────────────────────────────────────────────────────────────────────────
$token = az account get-access-token --resource $BaseUrl --query accessToken -o tsv 2>$null
if (-not $token) { throw "No Dataverse token for $BaseUrl. Run 'az login' and retry." }
$headers = @{
    Authorization      = "Bearer $token"
    Accept             = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'Content-Type'     = 'application/json; charset=utf-8'
}
function Invoke-DvGet([string]$Path) { Invoke-RestMethod -Uri "$Api/$Path" -Headers $headers -Method Get }
function Invoke-DvPost([string]$Path, [object]$Body) {
    Invoke-RestMethod -Uri "$Api/$Path" -Headers $headers -Method Post -Body ([Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 6))) | Out-Null
}
function Save-Evidence([object]$Sections) {
    $dir = Split-Path -Parent $EvidencePath
    if (-not (Test-Path -LiteralPath $dir)) { [void][IO.Directory]::CreateDirectory($dir) }
    $fence = '```'
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine("# Task 049 role-removal evidence ($Mode)")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("- Environment: $BaseUrl (org '$script:OrgName')")
    [void]$sb.AppendLine("- Run (UTC): $Stamp")
    [void]$sb.AppendLine("- Operator systemuserid: $script:OperatorId")
    [void]$sb.AppendLine("- Mode: $Mode$(if ($Mode -ne 'apply' -and $Mode -ne 'verify') { ' - NOTHING WAS CHANGED IN DATAVERSE' })")
    [void]$sb.AppendLine("- Script: scripts/Remove-ConsoleSignalWriteAndDecisionRecordCreate.ps1")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('## Run transcript')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine($fence + 'text')
    foreach ($l in $Transcript) { [void]$sb.AppendLine($l) }
    [void]$sb.AppendLine($fence)
    foreach ($key in $Sections.Keys) {
        [void]$sb.AppendLine()
        [void]$sb.AppendLine("## $key")
        [void]$sb.AppendLine()
        [void]$sb.AppendLine($fence + 'json')
        [void]$sb.AppendLine(($Sections[$key] | ConvertTo-Json -Depth 8))
        [void]$sb.AppendLine($fence)
    }
    [IO.File]::WriteAllText($EvidencePath, $sb.ToString(), (New-Object Text.UTF8Encoding($false)))
    Write-Host "Evidence    : $EvidencePath"
}

$script:OrgName = (Invoke-DvGet 'organizations?$select=name').value[0].name
$script:OperatorId = (Invoke-DvGet 'WhoAmI').UserId
Out-Line "Environment : $BaseUrl (org '$script:OrgName')"
Out-Line "Operator    : systemuserid $script:OperatorId"
Out-Line ("Mode        : {0}" -f $(switch ($Mode) {
            'apply' { 'APPLY (writes)' } 'verify' { 'VERIFY (read-only)' } 'whatif' { 'WHATIF (no writes)' } default { 'DRY RUN (no writes; pass -Apply to change roles)' } }))

# ── Business units, roles, privileges ────────────────────────────────────────────────────────────────────────
$buName = @{}
foreach ($b in (Invoke-DvGet 'businessunits?$select=businessunitid,name').value) { $buName["$($b.businessunitid)".ToLowerInvariant()] = $b.name }

$privCache = @{}
function Get-PrivilegeByName([string]$Name) {
    if ($privCache.ContainsKey($Name)) { return $privCache[$Name] }
    $p = @((Invoke-DvGet "privileges?`$select=privilegeid,name&`$filter=name eq '$Name'").value)
    if ($p.Count -ne 1) { throw "Privilege '$Name': expected exactly one in metadata, found $($p.Count)." }
    $privCache[$Name] = [pscustomobject]@{ Name = $Name; Id = "$($p[0].privilegeid)".ToLowerInvariant() }
    return $privCache[$Name]
}
function Get-RoleCopies([string]$RoleName) {
    $n = $RoleName -replace "'", "''"
    $rows = @((Invoke-DvGet "roles?`$select=roleid,name,_businessunitid_value,_parentrootroleid_value&`$filter=name eq '$n'").value)
    if ($rows.Count -eq 0) { throw "No role named '$RoleName' in this environment." }
    @($rows | ForEach-Object {
            $bu = "$($_._businessunitid_value)".ToLowerInvariant()
            [pscustomobject]@{
                RoleId = "$($_.roleid)".ToLowerInvariant(); RoleName = $_.name; BuId = $bu
                BuName = $(if ($buName.ContainsKey($bu)) { $buName[$bu] } else { $bu })
                IsRoot = ("$($_.roleid)" -eq "$($_._parentrootroleid_value)")
            }
        } | Sort-Object @{ Expression = 'IsRoot'; Descending = $true }, BuName)
}
function Get-RolePrivilegeList([string]$RoleId) {
    @((Invoke-DvGet "RetrieveRolePrivilegesRole(RoleId=@p)?@p=$RoleId").RolePrivileges |
            ForEach-Object { [pscustomobject]@{ Name = $_.PrivilegeName; Id = "$($_.PrivilegeId)".ToLowerInvariant(); Depth = "$($_.Depth)" } } |
            Sort-Object Name)
}
function Get-Snapshot([string]$RoleName) {
    $copies = Get-RoleCopies $RoleName
    @($copies | ForEach-Object {
            $privs = Get-RolePrivilegeList $_.RoleId
            [pscustomobject]@{ RoleName = $_.RoleName; RoleId = $_.RoleId; BuName = $_.BuName; IsRoot = $_.IsRoot; Count = $privs.Count; Privileges = $privs }
        })
}

# ── Step 1-2: BEFORE ─────────────────────────────────────────────────────────────────────────────────────────
$targetRoles = @($Removals.Role | Select-Object -Unique)
$before = [ordered]@{}
$planned = New-Object System.Collections.Generic.List[object]   # one entry per RemovePrivilegeRole call
$alreadyAbsent = New-Object System.Collections.Generic.List[string]

if ($Mode -ne 'verify') {
    foreach ($r in $Removals) { [void](Get-PrivilegeByName $r.Privilege) }   # throws unless each resolves to exactly one privilege
    Out-Line ''
    Out-Line 'BEFORE: every business-unit copy (RetrieveRolePrivilegesRole)'
    foreach ($roleName in $targetRoles + $ControlRole) {
        $snap = Get-Snapshot $roleName
        $before[$roleName] = $snap
        Out-Line ("  {0}: {1} copies" -f $roleName, $snap.Count)
        foreach ($c in $snap) {
            Out-Line ("    {0,-26} {1,-10} id={2}  privileges={3}" -f $c.BuName, $(if ($c.IsRoot) { '(root)' } else { '' }), $c.RoleId, $c.Count)
        }
    }
    foreach ($r in $Removals) {
        $pid_ = (Get-PrivilegeByName $r.Privilege).Id
        foreach ($c in $before[$r.Role]) {
            $held = @($c.Privileges | Where-Object Id -eq $pid_)
            if ($held.Count -eq 1) {
                $planned.Add([pscustomobject]@{ Role = $r.Role; RoleId = $c.RoleId; BuName = $c.BuName; IsRoot = $c.IsRoot; Privilege = $r.Privilege; PrivilegeId = $pid_; Depth = $held[0].Depth; Decision = $r.Decision })
            }
            elseif ($c.IsRoot) { $alreadyAbsent.Add("$($r.Role) root copy: $($r.Privilege) already absent ($($r.Decision))") }
        }
    }
    Out-Line ''
    Out-Line "PLAN: $($planned.Count) RemovePrivilegeRole call(s); order is the two Decision Record Create removals, then the Signal Write (K-A)"
    foreach ($p in $planned) { Out-Line ("  remove {0,-30} ({1}) from {2} [{3}{4}] {5}" -f $p.Privilege, $p.Depth, $p.Role, $p.BuName, $(if ($p.IsRoot) { ', root copy' } else { '' }), $p.Decision) 'Yellow' }
    foreach ($a in $alreadyAbsent) { Out-Line "  note: $a" }
    if ($planned.Count -eq 0) { Out-Line '  Nothing to remove: the privileges are already absent on every copy (idempotent re-run).' 'Green' }

    # K-A guard: the same change must carry both Console User privileges, unless the Decision Record Create is already gone.
    $consoleRoot = $before['Spaarke Console User'] | Where-Object IsRoot
    $consoleHasSignalWrite = @($consoleRoot.Privileges | Where-Object Name -eq 'prvWritesprk_Signal').Count -gt 0
    $consoleHasDrCreate = @($consoleRoot.Privileges | Where-Object Name -eq 'prvCreatesprk_DecisionRecord').Count -gt 0
    if ($consoleHasSignalWrite -and -not $consoleHasDrCreate -and $planned.Count -gt 0) {
        Out-Line 'NOTE: Console User already lacks Create on sprk_decisionrecord; the Signal Write removal is therefore safe under K-A.'
    }
}

# ── Step 3: APPLY ────────────────────────────────────────────────────────────────────────────────────────────
$removedCalls = New-Object System.Collections.Generic.List[object]
if ($Mode -ne 'verify') {
    Out-Line ''
    foreach ($p in $planned) {
        $target = "role '$($p.Role)' copy in BU '$($p.BuName)' ($($p.RoleId))"
        if ($PSCmdlet.ShouldProcess($target, "RemovePrivilegeRole $($p.Privilege) ($($p.Depth)) [$($p.Decision)]")) {
            if ($Mode -ne 'apply') { Out-Line "  dry run: would remove $($p.Privilege) from $target"; continue }
            # Child copies follow the root copy, so a root removal may already have reached this copy. Re-read before the call
            # and skip a privilege that is already gone (RemovePrivilegeRole on a role that no longer holds it is an error).
            if (@(Get-RolePrivilegeList $p.RoleId | Where-Object Id -eq $p.PrivilegeId).Count -eq 0) {
                Out-Line "  already absent on $target (followed the root copy); no call made" 'Gray'
                continue
            }
            Invoke-DvPost "roles($($p.RoleId))/Microsoft.Dynamics.CRM.RemovePrivilegeRole" @{
                Privilege = @{ '@odata.type' = 'Microsoft.Dynamics.CRM.privilege'; privilegeid = $p.PrivilegeId }
            }
            $removedCalls.Add($p)
            Out-Line "  removed $($p.Privilege) from $target at $([DateTime]::UtcNow.ToString('HH:mm:ss'))Z" 'Green'
        }
        else {
            Out-Line "  whatif: would remove $($p.Privilege) from $target"
        }
    }
}

# ── Step 4: AFTER and diff (apply only) ──────────────────────────────────────────────────────────────────────
$failures = New-Object System.Collections.Generic.List[string]
$after = [ordered]@{}
if ($Mode -eq 'apply') {
    Out-Line ''
    Out-Line 'AFTER: every business-unit copy, diffed against BEFORE'
    foreach ($roleName in $targetRoles + $ControlRole) {
        $after[$roleName] = Get-Snapshot $roleName
        foreach ($c in $after[$roleName]) {
            $b = $before[$roleName] | Where-Object RoleId -eq $c.RoleId
            $bMap = @{}; foreach ($x in $b.Privileges) { $bMap[$x.Name] = $x.Depth }
            $aMap = @{}; foreach ($x in $c.Privileges) { $aMap[$x.Name] = $x.Depth }
            $removed = @($bMap.Keys | Where-Object { -not $aMap.ContainsKey($_) } | Sort-Object)
            $added = @($aMap.Keys | Where-Object { -not $bMap.ContainsKey($_) } | Sort-Object)
            $depth = @($aMap.Keys | Where-Object { $bMap.ContainsKey($_) -and $bMap[$_] -ne $aMap[$_] } | Sort-Object)
            # Expected = everything planned for this copy, whether the script called it or the copy followed the root.
            $expected = @($planned | Where-Object RoleId -eq $c.RoleId | ForEach-Object Privilege | Sort-Object)
            $ok = (($removed -join ',') -eq ($expected -join ',')) -and $added.Count -eq 0 -and $depth.Count -eq 0 -and $c.Count -eq ($b.Count - $expected.Count)
            Out-Line ("  {0,-32} {1,-24} count {2} -> {3}; removed=[{4}] added=[{5}] depth-changed=[{6}] {7}" -f $roleName, $c.BuName, $b.Count, $c.Count, ($removed -join ', '), ($added -join ', '), ($depth -join ', '), $(if ($ok) { 'OK' } else { 'MISMATCH' })) $(if ($ok) { 'Gray' } else { 'Red' })
            if (-not $ok) { $failures.Add("DIFF: $roleName / $($c.BuName): removed=[$($removed -join ', ')] added=[$($added -join ', ')] depth-changed=[$($depth -join ', ')] expected removed=[$($expected -join ', ')]") }
        }
    }
}

# ── Step 5: union of roles, environment-wide ─────────────────────────────────────────────────────────────────
Out-Line ''
$projection = ($Mode -ne 'apply' -and $Mode -ne 'verify')
Out-Line $(if ($projection) { 'UNION OF ROLES (NFR-11), PROJECTED: the live state with the planned removals taken out' } else { 'UNION OF ROLES (NFR-11), LIVE state' })

$roleInfo = @{}
function Get-RoleInfo([string]$RoleId) {
    if (-not $roleInfo.ContainsKey($RoleId)) {
        $r = Invoke-DvGet "roles($RoleId)?`$select=roleid,name,_parentrootroleid_value,ismanaged"
        $root = if ($r._parentrootroleid_value) { "$($r._parentrootroleid_value)".ToLowerInvariant() } else { $RoleId }
        $roleInfo[$RoleId] = [pscustomobject]@{ RoleId = $RoleId; Name = $r.name; RootId = $root; Managed = [bool]$r.ismanaged }
    }
    $roleInfo[$RoleId]
}
function Get-RoleClass([string]$Name) {
    if ($PlatformRoles -contains $Name) { 'platform' }
    elseif ($AdminRoles -contains $Name) { 'admin' }
    elseif ($Name -eq $ControlRole) { 'spaarke-service' }
    elseif ($Name -like 'Spaarke *') { 'spaarke-human' }
    else { 'other' }
}
$removedKeys = @{}
if ($projection) { foreach ($p in $planned) { $removedKeys["$($p.RoleId)|$($p.PrivilegeId)"] = $true } }

$memberCache = @{}
function Get-RoleMembers([string]$RootId) {
    if ($memberCache.ContainsKey($RootId)) { return $memberCache[$RootId] }
    $humans = @{}; $disabled = @{}; $apps = @{}; $teams = @{}
    foreach ($c in @((Invoke-DvGet "roles?`$select=roleid&`$filter=_parentrootroleid_value eq $RootId").value)) {
        $rr = Invoke-DvGet "roles($($c.roleid))?`$expand=systemuserroles_association(`$select=systemuserid,fullname,domainname,applicationid,isdisabled),teamroles_association(`$select=teamid,name,teamtype)"
        foreach ($u in $rr.systemuserroles_association) {
            if ($u.applicationid) { $apps[$u.systemuserid] = $u.fullname }
            elseif ($u.isdisabled) { $disabled[$u.systemuserid] = $u.fullname }
            else { $humans[$u.systemuserid] = "$($u.fullname) <$($u.domainname)>" }
        }
        foreach ($t in $rr.teamroles_association) { $teams[$t.teamid] = $t.name }
    }
    $teamHumans = 0
    foreach ($tid in $teams.Keys) {
        try {
            $tm = Invoke-DvGet "teams($tid)?`$expand=teammembership_association(`$select=systemuserid,applicationid,isdisabled)"
            $teamHumans += @($tm.teammembership_association | Where-Object { -not $_.applicationid -and -not $_.isdisabled }).Count
        }
        catch { $teamHumans += 0 }
    }
    $memberCache[$RootId] = [pscustomobject]@{ HumanUsers = @($humans.Values | Sort-Object); DisabledHumans = $disabled.Count; AppUsers = $apps.Count; Teams = @($teams.Values | Sort-Object); HumansViaTeams = $teamHumans }
    return $memberCache[$RootId]
}
function Get-Holders([string]$PrivilegeName) {
    $pr = Get-PrivilegeByName $PrivilegeName
    $rows = @((Invoke-DvGet "roleprivilegescollection?`$filter=privilegeid eq $($pr.Id)&`$select=roleid,privilegedepthmask").value)
    $byRoot = @{}
    foreach ($row in $rows) {
        $rid = "$($row.roleid)".ToLowerInvariant()
        if ($removedKeys.ContainsKey("$rid|$($pr.Id)")) { continue }
        $info = Get-RoleInfo $rid
        $depth = $DepthNames[[int]$row.privilegedepthmask]
        if (-not $byRoot.ContainsKey($info.RootId)) { $byRoot[$info.RootId] = [pscustomobject]@{ RootId = $info.RootId; Name = $info.Name; Class = (Get-RoleClass $info.Name); Managed = $info.Managed; Depth = $depth; Copies = 0 } }
        $byRoot[$info.RootId].Copies++
    }
    @($byRoot.Values | Sort-Object Class, Name)
}

$checks = [ordered]@{
    'Write on sprk_decisionrecord'  = 'prvWritesprk_DecisionRecord'
    'Delete on sprk_decisionrecord' = 'prvDeletesprk_DecisionRecord'
    'Create on sprk_decisionrecord' = 'prvCreatesprk_DecisionRecord'
    'Write on sprk_signal'          = 'prvWritesprk_Signal'
}
$union = [ordered]@{}
foreach ($label in $checks.Keys) {
    $holders = Get-Holders $checks[$label]
    foreach ($h in $holders) { $h | Add-Member -NotePropertyName Members -NotePropertyValue (Get-RoleMembers $h.RootId) }
    $union[$label] = $holders
    Out-Line ''
    Out-Line "  $label ($($checks[$label])): $($holders.Count) role(s)"
    foreach ($h in $holders) {
        $m = $h.Members
        $who = "humans=$($m.HumanUsers.Count) apps=$($m.AppUsers) teams=$($m.Teams.Count) (humans via teams: $($m.HumansViaTeams))"
        Out-Line ("    {0,-34} {1,-16} {2,-7} {3}" -f $h.Name, $h.Class, $h.Depth, $who)
        if ($h.Class -notin @('platform') -and $m.HumanUsers.Count -gt 0) { foreach ($u in $m.HumanUsers) { Out-Line "        human: $u" } }
        if ($h.Class -notin @('platform') -and $m.Teams.Count -gt 0) { Out-Line ("        teams: " + ($m.Teams -join '; ')) }
    }
}

# Assertions
Out-Line ''
Out-Line 'ASSERTIONS'
function Test-Assert([string]$Id, [string]$Text, [bool]$Pass, [string]$Detail = '') {
    Out-Line ("  {0} {1,-4} {2}{3}" -f $(if ($Pass) { 'PASS' } else { 'FAIL' }), $Id, $Text, $(if (-not $Pass -and $Detail) { " -> $Detail" } else { '' })) $(if ($Pass) { 'Green' } else { 'Red' })
    if (-not $Pass) { $failures.Add("ASSERT $Id : $Text -> $Detail") }
}
$wd = @($union['Write on sprk_decisionrecord']) + @($union['Delete on sprk_decisionrecord'])
$a1Bad = @($wd | Where-Object { $_.Class -notin @('platform', 'admin') })
$a1Humans = @($wd | Where-Object { $_.Class -eq 'platform' -and ($_.Members.HumanUsers.Count -gt 0 -or $_.Members.HumansViaTeams -gt 0) })
Test-Assert 'A1' 'Write/Delete on sprk_decisionrecord is held only by platform and admin roles; platform roles have no human member' ($a1Bad.Count -eq 0 -and $a1Humans.Count -eq 0) ("non-platform/admin holders: [" + (($a1Bad.Name) -join ', ') + "]; platform roles with a human member: [" + (($a1Humans.Name) -join ', ') + "]")
$a2Bad = @($union['Create on sprk_decisionrecord'] | Where-Object { $_.Class -notin @('platform', 'admin', 'spaarke-service') })
Test-Assert 'A2' 'Create on sprk_decisionrecord is held only by platform roles, admin roles and Spaarke Ontology Service' ($a2Bad.Count -eq 0) ("unexpected holders: [" + (($a2Bad.Name) -join ', ') + "]")
$a3 = @($union['Write on sprk_signal'] | Where-Object Name -eq 'Spaarke Console User')
Test-Assert 'A3' 'Spaarke Console User does not hold Write on sprk_signal' ($a3.Count -eq 0) 'Console User still holds it'
$a4w = @($union['Write on sprk_signal'] | Where-Object Name -eq $ControlRole)
$a4c = @($union['Create on sprk_decisionrecord'] | Where-Object Name -eq $ControlRole)
Test-Assert 'A4' "Control: $ControlRole still holds Write on sprk_signal and Create on sprk_decisionrecord" ($a4w.Count -eq 1 -and $a4c.Count -eq 1) 'the writer lost a privilege it needs'
if ($Mode -eq 'apply' -and $failures.Count -gt 0) { Out-Line '' ; Out-Line 'The after-diff or an assertion failed: STOP, report, and decide whether to restore (escalation in the 049 POML).' 'Red' }

$findings = @($union['Write on sprk_signal'] | Where-Object { $_.Class -in @('spaarke-human', 'other') })
Out-Line ''
Out-Line 'FINDING (not an assertion): non-platform, non-admin, non-writer roles that still hold Write on sprk_signal'
if ($findings.Count -eq 0) { Out-Line '  none' 'Green' }
foreach ($f in $findings) { Out-Line ("  {0} ({1}); human members: {2}" -f $f.Name, $f.Depth, $(if ($f.Members.HumanUsers.Count) { $f.Members.HumanUsers -join '; ' } else { 'none' })) 'Yellow' }

Out-Line ''
$verdict = if ($failures.Count -eq 0) { "RESULT: PASS ($Mode)" } else { "RESULT: FAIL ($Mode): $($failures.Count) problem(s)" }
Out-Line $verdict $(if ($failures.Count -eq 0) { 'Green' } else { 'Red' })
if ($projection) { Out-Line 'Nothing was changed in Dataverse. The assertions above are PROJECTED; re-run with -Apply, then -Verify, for the live result.' }

# Evidence form of a snapshot: one "privilege=Depth" string per privilege, so the exact set is readable and diffable.
function ConvertTo-EvidenceSnapshot([object]$Snap) {
    $o = [ordered]@{}
    foreach ($roleName in $Snap.Keys) {
        $o[$roleName] = @($Snap[$roleName] | ForEach-Object {
                [ordered]@{ BusinessUnit = $_.BuName; IsRoot = $_.IsRoot; RoleId = $_.RoleId; Count = $_.Count; Privileges = @($_.Privileges | ForEach-Object { "$($_.Name)=$($_.Depth)" }) }
            })
    }
    $o
}
$sections = [ordered]@{}
if ($before.Count -gt 0) { $sections['BEFORE: RetrieveRolePrivilegesRole per business-unit copy'] = ConvertTo-EvidenceSnapshot $before }
if ($after.Count -gt 0) { $sections['AFTER: RetrieveRolePrivilegesRole per business-unit copy'] = ConvertTo-EvidenceSnapshot $after }
$sections['PLAN or APPLIED removals'] = $planned.ToArray()
$sections['Union of roles'] = $union
Save-Evidence $sections
if ($failures.Count -gt 0) { exit 1 }
exit 0
