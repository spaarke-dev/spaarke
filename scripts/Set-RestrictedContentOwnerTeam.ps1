#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Sets up the memberless 'Spaarke Restricted Content Owners' team in the ROOT business unit, with a role that holds
    Read at User (Basic) depth on exactly the tables in config/restricted-content-owner-role.json. Dry run by default.

.DESCRIPTION
    Owner round 131 (the 086 ruling, unified-access-control-r2 task 194b R4b-2): an ordinary row of a hard-deny table
    (sprk_storedfile, sprk_procedureassignment, sprk_ruleset, sprk_policy, sprk_policyversion) is owned by this team in
    place of a business unit's default team. The BFF refuses those writes (record_owner_restricted_content_team_unresolved)
    while the team is missing, outside the root business unit or has a member, and Dataverse refuses the team as an owner
    while its role lacks Read on the table. So every environment needs this set up BEFORE a BFF with the rule runs.

    MODES
      (default)  Dry run. Reads everything and prints the plan. Zero writes.
      -Apply     Creates the team if it is missing (root business unit, teamtype 0), creates the role in the root
                 business unit if it is missing, adds each declared Read privilege at Basic, takes off every other
                 privilege of THIS role (the platform injects some on create and on every AddPrivilegesRole; setup
                 guide SECURE-PROJECT-ENVIRONMENT-SETUP.md section 5.4), assigns the role to the team, then reads
                 everything back and checks it as -Verify does. It never removes a member, moves a team, takes a role
                 off another principal or takes another role off the team: each of those is drift an owner decides,
                 and -Apply refuses before writing anything.
      -Verify    Read-only. Exits 1 and names each drift: the team or role missing; the team not an Owner team, a
                 default team or outside the root business unit; any member; any other role on the team; the role
                 (or one of its business-unit copies) assigned to any other team or user; a declared privilege
                 missing or at another depth; any privilege outside the file. Exits 0 when there is none.

    FAIL CLOSED: every read that fails stops the run (exit 1); nothing is decided on a partial read.

    Auth is the operator's own az CLI identity. No secrets in this script or the repository.

.PARAMETER EnvironmentUrl
    Dataverse environment URL, e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER Apply
    Write mode. Without it the script never writes. Needs the owner's go for the environment.

.PARAMETER WhatIf
    Forces a dry run even when -Apply is passed.

.PARAMETER Verify
    Read-only check with a pass/fail exit code. Cannot be combined with -Apply.

.PARAMETER ConfigPath
    The codified set. Defaults to config/restricted-content-owner-role.json at the repository root.

.EXAMPLE
    .\Set-RestrictedContentOwnerTeam.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com
    Dry run: the plan, with no writes.

.EXAMPLE
    .\Set-RestrictedContentOwnerTeam.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Verify
    Exit 0 = no drift; exit 1 = drift (each named) or a read failed.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$EnvironmentUrl,

    [switch]$Apply,

    [switch]$WhatIf,

    [switch]$Verify,

    [string]$ConfigPath = (Join-Path $PSScriptRoot '..' 'config' 'restricted-content-owner-role.json')
)

$ErrorActionPreference = 'Stop'
if ($Apply -and $Verify) {
    Write-Host '-Apply and -Verify are separate modes; run -Apply first, then -Verify.' -ForegroundColor Red
    exit 1
}
$IsDryRun = (-not $Apply.IsPresent) -or $WhatIf.IsPresent
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$Api = "$EnvironmentUrl/api/data/v9.2"

function Read-Config([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { throw "Config not found: $Path" }
    $c = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($c.schemaVersion -ne 1) { throw "Unsupported schemaVersion '$($c.schemaVersion)' in $Path" }
    if ($c.access -ne 'Read' -or $c.depth -ne 'Basic') {
        throw "Config asks for access '$($c.access)' at depth '$($c.depth)'. Only Read at Basic is supported, by design."
    }
    if ($c.businessUnit -ne 'root') { throw "Config names business unit '$($c.businessUnit)'. Only 'root' is supported, by design." }
    foreach ($field in 'teamName', 'roleName') {
        if ([string]::IsNullOrWhiteSpace($c.$field)) { throw "Config has no '$field'." }
    }
    $t = @($c.tables)
    if ($t.Count -eq 0) { throw "Config lists no tables: $Path" }
    foreach ($key in 'logicalName', 'privilegeName') {
        $dupes = @($t | Group-Object $key | Where-Object Count -gt 1)
        if ($dupes.Count -gt 0) { throw "Config lists a $key twice: $(($dupes | ForEach-Object Name) -join ', ')" }
    }
    foreach ($e in $t) {
        foreach ($field in 'logicalName', 'privilegeName', 'reason', 'evidence') {
            if ([string]::IsNullOrWhiteSpace($e.$field)) { throw "Config entry '$($e.logicalName)' has no '$field'." }
        }
    }
    return $c
}

function Q([string]$Value) { $Value -replace "'", "''" }

try {
    $config = Read-Config $ConfigPath
    $tables = @($config.tables)

    $token = az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv 2>$null
    if (-not $token) { throw "No Dataverse token for $EnvironmentUrl. Run 'az login' and retry." }
    $headers = @{
        Authorization      = "Bearer $token"
        Accept             = 'application/json'
        'OData-MaxVersion' = '4.0'
        'OData-Version'    = '4.0'
        'Content-Type'     = 'application/json; charset=utf-8'
    }
    function Invoke-DvGet([string]$RelativePath) { Invoke-RestMethod -Uri "$Api/$RelativePath" -Headers $headers -Method Get }
    # Every page of a collection (fail closed: a page that cannot be read stops the run, never a partial answer).
    function Get-DvAll([string]$RelativePath) {
        $rows = [System.Collections.Generic.List[object]]::new()
        $page = Invoke-DvGet $RelativePath
        while ($true) {
            foreach ($r in @($page.value)) { $rows.Add($r) }
            $next = $page.'@odata.nextLink'
            if (-not $next) { break }
            $page = Invoke-RestMethod -Uri $next -Headers $headers -Method Get
        }
        return $rows.ToArray()
    }
    function Invoke-DvPost([string]$RelativePath, $Body) {
        $json = $Body | ConvertTo-Json -Depth 6
        Invoke-RestMethod -Uri "$Api/$RelativePath" -Headers $headers -Method Post -Body ([Text.Encoding]::UTF8.GetBytes($json))
    }

    $orgName = (Invoke-DvGet 'organizations?$select=name').value[0].name
    Write-Host "Environment : $EnvironmentUrl (org '$orgName')"
    Write-Host "Config      : $ConfigPath ($($tables.Count) tables)"
    Write-Host ("Mode        : {0}" -f $(if ($Verify) { 'VERIFY (read-only)' } elseif ($IsDryRun) { 'DRY RUN (no writes)' } else { 'APPLY' }))

    # ── The root business unit: exactly one ──────────────────────────────────────────────────────────────────
    $roots = @((Invoke-DvGet 'businessunits?$select=businessunitid,name&$filter=_parentbusinessunitid_value eq null').value)
    if ($roots.Count -ne 1) { throw "Expected exactly one root business unit; found $($roots.Count)." }
    $rootId = $roots[0].businessunitid
    Write-Host "Root BU     : $($roots[0].name) ($rootId)"

    # ── Each table's Read privilege, from METADATA ──────────────────────────────────────────────────────────
    $wanted = foreach ($t in $tables) {
        $privs = @((Invoke-DvGet "EntityDefinitions(LogicalName='$($t.logicalName)')/Privileges").value |
            Where-Object { $_.PrivilegeType -eq 'Read' })
        if ($privs.Count -ne 1) { throw "Table '$($t.logicalName)': expected one Read privilege in metadata, found $($privs.Count)." }
        if ($privs[0].Name -cne $t.privilegeName) {
            throw "Table '$($t.logicalName)': metadata names its Read privilege '$($privs[0].Name)', the config says '$($t.privilegeName)'. Fix the config (casing follows the schema name)."
        }
        [pscustomobject]@{ Table = $t.logicalName; Name = $privs[0].Name; Id = $privs[0].PrivilegeId.ToString().ToLowerInvariant() }
    }
    $wantedIds = @($wanted | ForEach-Object Id)

    # ── Read the current state ──────────────────────────────────────────────────────────────────────────────
    function Get-State {
        $s = [ordered]@{ Team = $null; Members = @(); TeamRoles = @(); Role = $null; Copies = @(); Assigned = @(); Held = @() }
        $teams = @((Invoke-DvGet "teams?`$select=teamid,name,teamtype,isdefault,_businessunitid_value&`$filter=name eq '$(Q $config.teamName)'").value)
        if ($teams.Count -gt 1) { throw "Expected at most one team named '$($config.teamName)'; found $($teams.Count). Resolve it by hand (owner decision)." }
        if ($teams.Count -eq 1) {
            $s.Team = $teams[0]
            $s.Members = @(Get-DvAll "teams($($s.Team.teamid))/teammembership_association?`$select=systemuserid,fullname")
            $s.TeamRoles = @(Get-DvAll "teams($($s.Team.teamid))/teamroles_association?`$select=roleid,name")
        }

        $roles = @((Invoke-DvGet "roles?`$select=roleid,name,_parentrootroleid_value&`$filter=name eq '$(Q $config.roleName)' and _businessunitid_value eq $rootId").value)
        if ($roles.Count -gt 1) { throw "Expected at most one role named '$($config.roleName)' in the root business unit; found $($roles.Count)." }
        if ($roles.Count -eq 1) {
            $s.Role = $roles[0]
            $roleId = $s.Role.roleid
            # A role created in the root business unit is copied into every child unit; a copy can be assigned there.
            # Every copy's assignments count.
            $s.Copies = @(Get-DvAll "roles?`$select=roleid,_businessunitid_value&`$filter=_parentrootroleid_value eq $roleId")
            $roleIds = @($roleId) + @($s.Copies | ForEach-Object roleid | Where-Object { $_ -ne $roleId })
            $s.Assigned = foreach ($id in ($roleIds | Select-Object -Unique)) {
                foreach ($tm in @(Get-DvAll "roles($id)/teamroles_association?`$select=teamid,name")) {
                    [pscustomobject]@{ Kind = 'team'; Id = $tm.teamid; Name = $tm.name }
                }
                foreach ($u in @(Get-DvAll "roles($id)/systemuserroles_association?`$select=systemuserid,fullname")) {
                    [pscustomobject]@{ Kind = 'user'; Id = $u.systemuserid; Name = $u.fullname }
                }
            }
            $s.Assigned = @($s.Assigned)
            $s.Held = @((Invoke-DvGet "RetrieveRolePrivilegesRole(RoleId=@p)?@p=$roleId").RolePrivileges)
        }
        return [pscustomobject]$s
    }

    function Get-Drift($s) {
        $d = [System.Collections.Generic.List[string]]::new()
        if ($null -eq $s.Team) { $d.Add("TEAM MISSING: no team named '$($config.teamName)'.") }
        else {
            if ($s.Team.teamtype -ne 0) { $d.Add("TEAM TYPE: '$($config.teamName)' is teamtype $($s.Team.teamtype), expected 0 (Owner).") }
            if ($s.Team.isdefault) { $d.Add("TEAM DEFAULT: '$($config.teamName)' is a business unit's default team.") }
            if ($s.Team._businessunitid_value -ne $rootId) { $d.Add("TEAM BU: '$($config.teamName)' is in business unit $($s.Team._businessunitid_value), not the root $rootId.") }
            if ($s.Members.Count -gt 0) { $d.Add("TEAM MEMBERS: '$($config.teamName)' has $($s.Members.Count) member(s); it must have none.") }
            $otherRoles = @($s.TeamRoles | Where-Object { $null -eq $s.Role -or $_.roleid -ne $s.Role.roleid })
            foreach ($r in $otherRoles) { $d.Add("TEAM ROLE: '$($config.teamName)' holds another role '$($r.name)' ($($r.roleid)).") }
        }
        if ($null -eq $s.Role) { $d.Add("ROLE MISSING: no role named '$($config.roleName)' in the root business unit.") }
        else {
            $teamId = if ($s.Team) { $s.Team.teamid } else { $null }
            foreach ($a in @($s.Assigned | Where-Object { -not ($_.Kind -eq 'team' -and $_.Id -eq $teamId) })) {
                $d.Add("ROLE ASSIGNED ELSEWHERE: '$($config.roleName)' is assigned to $($a.Kind) '$($a.Name)' ($($a.Id)).")
            }
            if ($s.Team -and -not @($s.Assigned | Where-Object { $_.Kind -eq 'team' -and $_.Id -eq $teamId })) {
                $d.Add("ROLE NOT ASSIGNED: '$($config.roleName)' is not assigned to '$($config.teamName)'.")
            }
            $heldById = @{}
            foreach ($h in $s.Held) { $heldById[$h.PrivilegeId.ToString().ToLowerInvariant()] = $h }
            foreach ($w in $wanted) {
                $h = $heldById[$w.Id]
                if ($null -eq $h) { $d.Add("PRIVILEGE MISSING: $($w.Name) ($($w.Table)).") }
                elseif ($h.Depth -ne 'Basic') { $d.Add("PRIVILEGE DEPTH: $($w.Name) ($($w.Table)) at $($h.Depth), expected Basic.") }
            }
            foreach ($o in @($s.Held | Where-Object { $wantedIds -notcontains $_.PrivilegeId.ToString().ToLowerInvariant() } | Sort-Object PrivilegeName)) {
                $d.Add("PRIVILEGE OUTSIDE THE FILE: $($o.PrivilegeName) at $($o.Depth).")
            }
        }
        return , $d
    }

    function Write-State($s) {
        Write-Host ''
        if ($s.Team) {
            Write-Host "Team        : $($s.Team.name) ($($s.Team.teamid)) teamtype=$($s.Team.teamtype) isdefault=$($s.Team.isdefault) bu=$($s.Team._businessunitid_value) members=$($s.Members.Count) roles=$($s.TeamRoles.Count)"
        }
        else { Write-Host "Team        : (none named '$($config.teamName)')" }
        if ($s.Role) {
            Write-Host "Role        : $($s.Role.name) ($($s.Role.roleid)) copies=$($s.Copies.Count) assignments=$($s.Assigned.Count) privileges=$($s.Held.Count)"
        }
        else { Write-Host "Role        : (none named '$($config.roleName)' in the root business unit)" }
    }

    $state = Get-State
    Write-State $state
    $drift = Get-Drift $state
    Write-Host ''
    if ($drift.Count -eq 0) { Write-Host 'Drift       : none' -ForegroundColor Green }
    foreach ($line in $drift) { Write-Host "DRIFT       : $line" -ForegroundColor Yellow }

    # ── Verify ──────────────────────────────────────────────────────────────────────────────────────────────
    if ($Verify) {
        if ($drift.Count -eq 0) {
            Write-Host "`nVERIFY PASS: the team is memberless in the root business unit and holds only '$($config.roleName)', which holds Read at Basic on exactly the $($wanted.Count) tables." -ForegroundColor Green
            exit 0
        }
        Write-Host "`nVERIFY FAIL: $($drift.Count) drift finding(s) above." -ForegroundColor Red
        exit 1
    }

    # ── What -Apply may and may not change ──────────────────────────────────────────────────────────────────
    # Owner decisions, never changed here: refuse before anything is written.
    $ownerOnly = @($drift | Where-Object { $_ -match '^(TEAM TYPE|TEAM DEFAULT|TEAM BU|TEAM MEMBERS|TEAM ROLE|ROLE ASSIGNED ELSEWHERE):' })
    $plan = [System.Collections.Generic.List[string]]::new()
    if ($null -eq $state.Team) { $plan.Add("create the Owner team '$($config.teamName)' in the root business unit (teamtype 0), with no members") }
    if ($null -eq $state.Role) { $plan.Add("create the role '$($config.roleName)' in the root business unit") }
    $missing = @($wanted | Where-Object { $w = $_; $null -eq $state.Role -or -not @($state.Held | Where-Object { $_.PrivilegeId.ToString().ToLowerInvariant() -eq $w.Id -and $_.Depth -eq 'Basic' }) })
    if ($missing.Count -gt 0) { $plan.Add("add Read at Basic: $(($missing | ForEach-Object Name) -join ', ')") }
    $plan.Add("take off every privilege of '$($config.roleName)' outside the file, including any the platform injects (setup guide 5.4)")
    if ($null -eq $state.Team -or $null -eq $state.Role -or -not @($state.Assigned | Where-Object { $_.Kind -eq 'team' -and $_.Id -eq $state.Team.teamid })) {
        $plan.Add("assign '$($config.roleName)' to '$($config.teamName)'")
    }
    $plan.Add('read everything back and check it as -Verify does')

    Write-Host ''
    if ($ownerOnly.Count -gt 0) {
        Write-Host "OWNER DECISION NEEDED: -Apply never changes these; it refuses while they stand:" -ForegroundColor Red
        foreach ($line in $ownerOnly) { Write-Host "  $line" -ForegroundColor Red }
    }
    if ($drift.Count -eq 0) { Write-Host 'Nothing to do.' -ForegroundColor Green; exit 0 }
    Write-Host ("PLAN ({0}):" -f $(if ($IsDryRun) { 'dry run, nothing written' } else { 'applying' })) -ForegroundColor Cyan
    for ($i = 0; $i -lt $plan.Count; $i++) { Write-Host ("  {0}. {1}" -f ($i + 1), $plan[$i]) -ForegroundColor Cyan }
    if ($IsDryRun) {
        Write-Host "`nDRY RUN: no writes. Re-run with -Apply (owner go), then -Verify." -ForegroundColor Cyan
        exit 0
    }
    if ($ownerOnly.Count -gt 0) { throw 'Refused: the drift above is an owner decision. Nothing was written.' }

    # ── Apply ───────────────────────────────────────────────────────────────────────────────────────────────
    $createdTeam = $false
    if ($null -eq $state.Team) {
        $me = (Invoke-DvGet 'WhoAmI').UserId
        Invoke-DvPost 'teams' @{
            name                          = $config.teamName
            teamtype                      = 0
            'businessunitid@odata.bind'   = "/businessunits($rootId)"
            'administratorid@odata.bind'  = "/systemusers($me)"
            description                   = 'Memberless owner team for the hard-deny tables (config/restricted-content-owner-role.json). MUST NEVER have a member or any other role.'
        } | Out-Null
        $createdTeam = $true
        Write-Host "CREATED team '$($config.teamName)'." -ForegroundColor Green
    }
    if ($null -eq $state.Role) {
        Invoke-DvPost 'roles' @{
            name                        = $config.roleName
            'businessunitid@odata.bind' = "/businessunits($rootId)"
            description                 = 'Read at Basic on the hard-deny tables only (config/restricted-content-owner-role.json), so the memberless Spaarke Restricted Content Owners team can own their ordinary rows. MUST NOT be granted to any user or any other team.'
        } | Out-Null
        Write-Host "CREATED role '$($config.roleName)'." -ForegroundColor Green
    }

    $state = Get-State
    if ($null -eq $state.Team -or $null -eq $state.Role) { throw 'The team or the role could not be read back after it was created.' }
    $roleId = $state.Role.roleid

    if ($createdTeam -and $state.Members.Count -gt 0) {
        # Only a team THIS run created: a member it has now was added by the create itself, never by a person.
        Invoke-DvPost "teams($($state.Team.teamid))/Microsoft.Dynamics.CRM.RemoveMembersTeam" @{
            Members = @($state.Members | ForEach-Object { @{ '@odata.type' = 'Microsoft.Dynamics.CRM.systemuser'; systemuserid = $_.systemuserid } })
        } | Out-Null
        Write-Host "REMOVED the $($state.Members.Count) member(s) the create added." -ForegroundColor Green
    }

    $heldById = @{}
    foreach ($h in $state.Held) { $heldById[$h.PrivilegeId.ToString().ToLowerInvariant()] = $h }
    $toAdd = @($wanted | Where-Object { $null -eq $heldById[$_.Id] -or $heldById[$_.Id].Depth -ne 'Basic' })
    if ($toAdd.Count -gt 0) {
        foreach ($w in @($toAdd | Where-Object { $null -ne $heldById[$_.Id] })) {
            # Narrowing a declared privilege to Basic: take it off, then add it at Basic below.
            Invoke-DvPost "roles($roleId)/Microsoft.Dynamics.CRM.RemovePrivilegeRole" @{
                Privilege = @{ '@odata.type' = 'Microsoft.Dynamics.CRM.privilege'; privilegeid = $w.Id }
            } | Out-Null
        }
        Invoke-DvPost "roles($roleId)/Microsoft.Dynamics.CRM.AddPrivilegesRole" @{
            Privileges = @($toAdd | ForEach-Object {
                    @{ '@odata.type' = 'Microsoft.Dynamics.CRM.RolePrivilege'; Depth = 'Basic'; PrivilegeId = $_.Id; PrivilegeName = $_.Name; BusinessUnitId = $rootId }
                })
        } | Out-Null
        Write-Host "ADDED at Basic: $(($toAdd | ForEach-Object Name) -join ', ')" -ForegroundColor Green
    }

    # After AddPrivilegesRole, which re-injects platform privileges: everything outside the file comes off this role.
    $held = @((Invoke-DvGet "RetrieveRolePrivilegesRole(RoleId=@p)?@p=$roleId").RolePrivileges)
    $outside = @($held | Where-Object { $wantedIds -notcontains $_.PrivilegeId.ToString().ToLowerInvariant() })
    foreach ($o in $outside) {
        Invoke-DvPost "roles($roleId)/Microsoft.Dynamics.CRM.RemovePrivilegeRole" @{
            Privilege = @{ '@odata.type' = 'Microsoft.Dynamics.CRM.privilege'; privilegeid = $o.PrivilegeId.ToString() }
        } | Out-Null
    }
    if ($outside.Count -gt 0) { Write-Host "REMOVED outside the file: $(($outside | ForEach-Object PrivilegeName) -join ', ')" -ForegroundColor Green }

    if (-not @($state.Assigned | Where-Object { $_.Kind -eq 'team' -and $_.Id -eq $state.Team.teamid })) {
        $ref = @{ '@odata.id' = "$Api/roles($roleId)" }
        Invoke-DvPost "teams($($state.Team.teamid))/teamroles_association/`$ref" $ref | Out-Null
        Write-Host "ASSIGNED '$($config.roleName)' to '$($config.teamName)'." -ForegroundColor Green
    }

    $after = Get-State
    Write-State $after
    $left = Get-Drift $after
    if ($left.Count -gt 0) {
        foreach ($line in $left) { Write-Host "DRIFT       : $line" -ForegroundColor Red }
        throw "Read-back still shows $($left.Count) drift finding(s); the setup is not complete."
    }
    Write-Host "`nAPPLIED and read back: no drift. Dataverse caches principal privileges; re-run -Verify and probe across 3 polls (setup guide section 7)." -ForegroundColor Green
    exit 0
}
catch {
    Write-Host "FAILED (fail closed, exit 1): $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
