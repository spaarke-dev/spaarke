#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Sets up the declared memberless owner teams in ONE customer's top business unit (a direct child of the root, never the
    root and never Secure Record), each with its own role holding Read at User (Basic) depth on exactly the tables its config
    file declares. Dry run by default.

.DESCRIPTION
    The Spaarke root business unit holds only Spaarke product data; customer records and customer configuration belong to
    the customer's own top business unit, the direct child of the root that provisioning H10 creates (customerBusinessUnitId,
    a sibling of Secure Record) (owner round 136). Dataverse refuses to assign a row to a principal whose roles lack Read on
    the table (0x80040299), and a unit's default team is not an acceptable owner (a role on it reaches every member). So each
    customer top unit holds these declared, memberless owner teams, one per config file in $DeclaredRoleFiles:

      restricted-content-owner-role.json      'Spaarke Restricted Content Owners' / 'Restricted Content Owner'
          Owner rounds 131 (the 086 ruling) and 136 (unified-access-control-r2 task 194b R4b-2/R4b-2c): an ordinary row of
          a hard-deny table is owned by this team in place of a business unit's default team. The BFF finds it from the
          answer itself (it walks up to the root's direct child) and refuses the write
          (record_owner_restricted_content_team_unresolved) while the team is missing, ambiguous or has a member.

      customer-configuration-owner-role.json  'Spaarke Customer Configuration Owners' / 'Customer Configuration Owner'
          R4b-3b (field-mapping #2399, owner shape A): customer configuration rows (the declared configuration tables) are
          owned by this team. The BFF does not resolve it. Field-mapping's seeder (its task 004) and provisioning's H12b
          seeder (FR-39) WILL own new configuration rows to this team; until they ship, rows stay with the seeding identity
          and an operator re-owns them to this team after setup. Users keep reading the rows through their own roles
          (Spaarke Core User, Deep).

    Every customer top unit needs both set up BEFORE a BFF with the rule, or a seeder, writes its rows.

    THE ROLES. Dataverse creates a role in the root business unit and copies it into every child unit; a team can hold only
    its own unit's copy, and the privileges are the root role's. So each role is created once, in the root (a role is
    configuration, not a record), and the customer unit's COPY is assigned to that unit's team. The root role itself is
    assigned to nobody.

    MODES
      (default)  Dry run. Reads everything and prints the plan for each declared team. Zero writes.
      -Apply     For each declared team: creates the team in the named customer unit if it is missing (teamtype 0), creates
                 its role in the root if it is missing, adds each declared Read privilege at Basic, takes off every other
                 privilege of THAT role (the platform injects some on create and on every AddPrivilegesRole; setup guide
                 SECURE-PROJECT-ENVIRONMENT-SETUP.md section 5.4), assigns the customer unit's copy of the role to the team,
                 then reads everything back and checks it as -Verify does. It never removes a member, moves a team, takes a
                 role off another principal or takes another role off a team: each of those is drift an owner decides, and
                 -Apply refuses before writing anything, for every declared team.
                 A dry run exits 0 with the plan, or exits 1 with the OWNER DECISION NEEDED summary when any drift is
                 one -Apply refuses (a member, another role, the wrong team type, a default team, a role assigned
                 elsewhere): such a plan cannot be applied as it stands.
      -Verify    Read-only. Exits 1 and names each drift of the NAMED customer unit, per declared team: the team or role
                 missing; the team not an Owner team or a default team; any member; any other role on the team; the role
                 (or a copy of it) assigned to anything but a team of its name in a customer top unit; a declared privilege
                 missing or at another depth; any privilege outside the file. Every OTHER direct child of the root that lacks
                 a team is listed as information only (not a failure). Exits 0 when the named unit has no drift.

    REFUSED: a customer unit that is the root, that is the Secure Record unit, or that is not a direct child of the root;
    no business unit, or more than one, named -SecureBusinessUnitName (Secure Record exists by this step, H7b, so a name
    that matches nothing is a wrong name and would let the Secure Record unit pass as a customer unit); two config files that declare the same team, the same role or the same table.

    FAIL CLOSED: every read that fails stops the run (exit 1); nothing is decided on a partial read.

    Auth is the operator's own az CLI identity. No secrets in this script or the repository.

.PARAMETER EnvironmentUrl
    Dataverse environment URL, e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER CustomerBusinessUnitName
    The customer's top business unit, by name (exactly one match). Give this or -CustomerBusinessUnitId.

.PARAMETER CustomerBusinessUnitId
    The customer's top business unit, by id (provisioning's customerBusinessUnitId). Give this or -CustomerBusinessUnitName.

.PARAMETER SecureBusinessUnitName
    The Secure Record business unit (SecureRecord:BusinessUnitName), refused as a customer unit. Default 'Secure Record'.

.PARAMETER Apply
    Write mode. Without it the script never writes. Needs the owner's go for the environment.

.PARAMETER WhatIf
    Forces a dry run even when -Apply is passed.

.PARAMETER Verify
    Read-only check with a pass/fail exit code. Cannot be combined with -Apply.

.PARAMETER ConfigPaths
    The codified sets. Defaults to every file in $DeclaredRoleFiles under config/ at the repository root.

.EXAMPLE
    .\Set-CustomerOwnerTeams.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -CustomerBusinessUnitName 'Spaarke Business Unit 1'
    Dry run: the plan for both teams, with no writes.

.EXAMPLE
    .\Set-CustomerOwnerTeams.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -CustomerBusinessUnitId <id> -Verify
    Exit 0 = no drift in that unit for either team; exit 1 = drift (each named) or a read failed.
#>

[CmdletBinding(DefaultParameterSetName = 'ByName')]
param(
    [Parameter(Mandatory)]
    [string]$EnvironmentUrl,

    [Parameter(Mandatory, ParameterSetName = 'ByName')]
    [string]$CustomerBusinessUnitName,

    [Parameter(Mandatory, ParameterSetName = 'ById')]
    [guid]$CustomerBusinessUnitId,

    [string]$SecureBusinessUnitName = 'Secure Record',

    [switch]$Apply,

    [switch]$WhatIf,

    [switch]$Verify,

    [string[]]$ConfigPaths
)

# THE declared owner roles, one config file each. RestrictedContentOwnerRoleGuardTests requires this array (its uncommented
# entries) to EQUAL the guard's declared set: no declared file missing, no other file added.
$DeclaredRoleFiles = @(
    'restricted-content-owner-role.json',
    'customer-configuration-owner-role.json'
)

$ErrorActionPreference = 'Stop'
if ($Apply -and $Verify) {
    Write-Host '-Apply and -Verify are separate modes; run -Apply first, then -Verify.' -ForegroundColor Red
    exit 1
}
$IsDryRun = (-not $Apply.IsPresent) -or $WhatIf.IsPresent
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$Api = "$EnvironmentUrl/api/data/v9.2"
if (-not $ConfigPaths -or $ConfigPaths.Count -eq 0) {
    $ConfigPaths = @($DeclaredRoleFiles | ForEach-Object { Join-Path $PSScriptRoot '..' 'config' $_ })
}

function Read-Config([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { throw "Config not found: $Path" }
    $c = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($c.schemaVersion -ne 1) { throw "Unsupported schemaVersion '$($c.schemaVersion)' in $Path" }
    if ($c.access -ne 'Read' -or $c.depth -ne 'Basic') {
        throw "Config $Path asks for access '$($c.access)' at depth '$($c.depth)'. Only Read at Basic is supported, by design."
    }
    if ($c.businessUnit -ne 'customer') {
        throw "Config $Path names business unit '$($c.businessUnit)'. Only 'customer' (each customer's top business unit) is supported, by design (owner round 136)."
    }
    foreach ($field in 'teamName', 'roleName') {
        if ([string]::IsNullOrWhiteSpace($c.$field)) { throw "Config $Path has no '$field'." }
    }
    $t = @($c.tables)
    if ($t.Count -eq 0) { throw "Config lists no tables: $Path" }
    foreach ($key in 'logicalName', 'privilegeName') {
        $dupes = @($t | Group-Object $key | Where-Object Count -gt 1)
        if ($dupes.Count -gt 0) { throw "Config $Path lists a $key twice: $(($dupes | ForEach-Object Name) -join ', ')" }
    }
    foreach ($e in $t) {
        foreach ($field in 'logicalName', 'privilegeName', 'reason', 'evidence') {
            if ([string]::IsNullOrWhiteSpace($e.$field)) { throw "Config $Path entry '$($e.logicalName)' has no '$field'." }
        }
    }
    return $c
}

function Q([string]$Value) { $Value -replace "'", "''" }

try {
    # ── The declared roles ──────────────────────────────────────────────────────────────────────────────────
    $defs = @(foreach ($path in $ConfigPaths) {
            $c = Read-Config $path
            [pscustomobject]@{ Path = $path; File = (Split-Path $path -Leaf); Config = $c; Wanted = @(); WantedIds = @() }
        })
    foreach ($key in 'teamName', 'roleName') {
        $dupes = @($defs | Group-Object { $_.Config.$key } | Where-Object Count -gt 1)
        if ($dupes.Count -gt 0) { throw "Two config files declare the same $key '$($dupes[0].Name)'. Each team and role is declared once." }
    }
    $tableDupes = @($defs | ForEach-Object { $d = $_; @($d.Config.tables) | ForEach-Object { [pscustomobject]@{ Table = $_.logicalName; File = $d.File } } } |
        Group-Object Table | Where-Object Count -gt 1)
    if ($tableDupes.Count -gt 0) { throw "A table is declared by two roles: $(($tableDupes | ForEach-Object { "$($_.Name) ($(($_.Group | ForEach-Object File) -join ', '))" }) -join '; ')." }

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
    foreach ($d in $defs) { Write-Host "Config      : $($d.Path) ('$($d.Config.teamName)', $(@($d.Config.tables).Count) tables)" }
    Write-Host ("Mode        : {0}" -f $(if ($Verify) { 'VERIFY (read-only)' } elseif ($IsDryRun) { 'DRY RUN (no writes)' } else { 'APPLY' }))

    # ── The root, the Secure Record unit and the customer's top unit ─────────────────────────────────────────
    $units = @(Get-DvAll 'businessunits?$select=businessunitid,name,_parentbusinessunitid_value')
    $roots = @($units | Where-Object { -not $_._parentbusinessunitid_value })
    if ($roots.Count -ne 1) { throw "Expected exactly one root business unit; found $($roots.Count)." }
    $rootId = $roots[0].businessunitid
    Write-Host "Root BU     : $($roots[0].name) ($rootId)"

    # FAIL CLOSED (R4b-4): the Secure Record unit exists by this step (H7b), so a name that matches no unit is a wrong
    # -SecureBusinessUnitName (a non-default SecureRecord:BusinessUnitName) and would let Secure Record pass as a customer unit.
    $secureUnits = @($units | Where-Object { $_.name -eq $SecureBusinessUnitName })
    if ($secureUnits.Count -eq 0) { throw "No business unit is named '$SecureBusinessUnitName'. The Secure Record unit exists by this step (H7b); pass its name (SecureRecord:BusinessUnitName) as -SecureBusinessUnitName." }
    if ($secureUnits.Count -gt 1) { throw "More than one business unit is named '$SecureBusinessUnitName'; the Secure Record unit cannot be told apart." }
    $secureId = $secureUnits[0].businessunitid
    if (-not $secureId) { throw "The business unit named '$SecureBusinessUnitName' has no id." }
    Write-Host "Secure BU   : $SecureBusinessUnitName ($secureId)"

    $found = if ($PSCmdlet.ParameterSetName -eq 'ById') {
        @($units | Where-Object { $_.businessunitid -eq $CustomerBusinessUnitId.ToString() })
    }
    else { @($units | Where-Object { $_.name -eq $CustomerBusinessUnitName }) }
    $named = if ($PSCmdlet.ParameterSetName -eq 'ById') { "id $CustomerBusinessUnitId" } else { "'$CustomerBusinessUnitName'" }
    if ($found.Count -ne 1) { throw "Expected exactly one business unit $named; found $($found.Count)." }
    $customer = $found[0]
    $customerId = $customer.businessunitid
    if ($customerId -eq $rootId) { throw "Refused: $named is the ROOT business unit. The root holds only Spaarke product data; customer records go to the customer's own top unit (owner round 136)." }
    if ($customerId -eq $secureId) { throw "Refused: $named is the Secure Record business unit, never a customer unit." }
    if ($customer._parentbusinessunitid_value -ne $rootId) {
        throw "Refused: $named is not a direct child of the root (its parent is $($customer._parentbusinessunitid_value)). The teams live in the customer's TOP unit; the BFF walks up to it from any unit below."
    }
    Write-Host "Customer BU : $($customer.name) ($customerId)"

    # Every customer top unit: a direct child of the root that is not the Secure Record unit.
    $customerTops = @($units | Where-Object { $_._parentbusinessunitid_value -eq $rootId -and $_.businessunitid -ne $secureId })
    if (@($customerTops | ForEach-Object businessunitid) -contains $secureId) { throw 'The Secure Record unit was counted as a customer top unit. Nothing was decided.' }

    # ── Each table's Read privilege, from METADATA ──────────────────────────────────────────────────────────
    foreach ($d in $defs) {
        $d.Wanted = @(foreach ($t in @($d.Config.tables)) {
                $privs = @((Invoke-DvGet "EntityDefinitions(LogicalName='$($t.logicalName)')/Privileges").value |
                    Where-Object { $_.PrivilegeType -eq 'Read' })
                if ($privs.Count -ne 1) { throw "Table '$($t.logicalName)': expected one Read privilege in metadata, found $($privs.Count)." }
                if ($privs[0].Name -cne $t.privilegeName) {
                    throw "Table '$($t.logicalName)' ($($d.File)): metadata names its Read privilege '$($privs[0].Name)', the config says '$($t.privilegeName)'. Fix the config (casing follows the schema name)."
                }
                [pscustomobject]@{ Table = $t.logicalName; Name = $privs[0].Name; Id = $privs[0].PrivilegeId.ToString().ToLowerInvariant() }
            })
        $d.WantedIds = @($d.Wanted | ForEach-Object Id)
    }

    # ── Read the current state of one declared team ─────────────────────────────────────────────────────────
    function Get-State($d) {
        $config = $d.Config
        $s = [ordered]@{ Team = $null; Members = @(); TeamRoles = @(); Role = $null; Copies = @(); Copy = $null; Assigned = @(); Held = @(); AllTeams = @() }
        # Every team of this name, wherever it is: the named unit's is checked; the others decide which assignments are allowed.
        $s.AllTeams = @(Get-DvAll "teams?`$select=teamid,name,teamtype,isdefault,_businessunitid_value&`$filter=name eq '$(Q $config.teamName)'")
        $teams = @($s.AllTeams | Where-Object { $_._businessunitid_value -eq $customerId })
        if ($teams.Count -gt 1) { throw "Expected at most one team named '$($config.teamName)' in $($customer.name); found $($teams.Count). Resolve it by hand (owner decision)." }
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
            # A role created in the root business unit is copied into every child unit; a team holds its own unit's copy.
            # Every copy's assignments count.
            $s.Copies = @(Get-DvAll "roles?`$select=roleid,_businessunitid_value&`$filter=_parentrootroleid_value eq $roleId")
            $s.Copy = @($s.Copies | Where-Object { $_._businessunitid_value -eq $customerId -and $_.roleid -ne $roleId }) | Select-Object -First 1
            $roleIds = @($roleId) + @($s.Copies | ForEach-Object roleid | Where-Object { $_ -ne $roleId })
            $s.Assigned = foreach ($id in ($roleIds | Select-Object -Unique)) {
                foreach ($tm in @(Get-DvAll "roles($id)/teamroles_association?`$select=teamid,name")) {
                    [pscustomobject]@{ Kind = 'team'; Id = $tm.teamid; Name = $tm.name; RoleId = $id }
                }
                foreach ($u in @(Get-DvAll "roles($id)/systemuserroles_association?`$select=systemuserid,fullname")) {
                    [pscustomobject]@{ Kind = 'user'; Id = $u.systemuserid; Name = $u.fullname; RoleId = $id }
                }
            }
            $s.Assigned = @($s.Assigned)
            $s.Held = @((Invoke-DvGet "RetrieveRolePrivilegesRole(RoleId=@p)?@p=$roleId").RolePrivileges)
        }
        return [pscustomobject]$s
    }

    function Get-Drift($d, $s) {
        $config = $d.Config
        $r = [System.Collections.Generic.List[string]]::new()
        if ($null -eq $s.Team) { $r.Add("TEAM MISSING: no team named '$($config.teamName)' in $($customer.name).") }
        else {
            if ($s.Team.teamtype -ne 0) { $r.Add("TEAM TYPE: '$($config.teamName)' is teamtype $($s.Team.teamtype), expected 0 (Owner).") }
            if ($s.Team.isdefault) { $r.Add("TEAM DEFAULT: '$($config.teamName)' is a business unit's default team.") }
            if ($s.Members.Count -gt 0) { $r.Add("TEAM MEMBERS: '$($config.teamName)' has $($s.Members.Count) member(s); it must have none.") }
            $roleIds = @(@($s.Role | ForEach-Object roleid) + @($s.Copies | ForEach-Object roleid))
            $otherRoles = @($s.TeamRoles | Where-Object { $roleIds -notcontains $_.roleid })
            foreach ($o in $otherRoles) { $r.Add("TEAM ROLE: '$($config.teamName)' holds another role '$($o.name)' ($($o.roleid)).") }
        }
        if ($null -eq $s.Role) { $r.Add("ROLE MISSING: no role named '$($config.roleName)' in the root business unit.") }
        else {
            if ($null -eq $s.Copy) { $r.Add("ROLE COPY MISSING: '$($config.roleName)' has no copy in $($customer.name).") }
            # Allowed: a team of this name, an Owner non-default team in a customer top unit, holding THAT unit's copy.
            $copyByUnit = @{}
            foreach ($c in $s.Copies) { $copyByUnit[$c._businessunitid_value] = $c.roleid }
            $allowed = @{}
            foreach ($tm in @($s.AllTeams | Where-Object { $_.teamtype -eq 0 -and -not $_.isdefault })) {
                if (@($customerTops | ForEach-Object businessunitid) -contains $tm._businessunitid_value) {
                    $allowed["$($tm.teamid)|$($copyByUnit[$tm._businessunitid_value])"] = $true
                }
            }
            foreach ($a in @($s.Assigned | Where-Object { -not ($_.Kind -eq 'team' -and $allowed.ContainsKey("$($_.Id)|$($_.RoleId)")) })) {
                $r.Add("ROLE ASSIGNED ELSEWHERE: '$($config.roleName)' ($($a.RoleId)) is assigned to $($a.Kind) '$($a.Name)' ($($a.Id)).")
            }
            if ($s.Team -and $s.Copy -and -not @($s.Assigned | Where-Object { $_.Kind -eq 'team' -and $_.Id -eq $s.Team.teamid -and $_.RoleId -eq $s.Copy.roleid })) {
                $r.Add("ROLE NOT ASSIGNED: the $($customer.name) copy of '$($config.roleName)' is not assigned to '$($config.teamName)'.")
            }
            $heldById = @{}
            foreach ($h in $s.Held) { $heldById[$h.PrivilegeId.ToString().ToLowerInvariant()] = $h }
            foreach ($w in $d.Wanted) {
                $h = $heldById[$w.Id]
                if ($null -eq $h) { $r.Add("PRIVILEGE MISSING: $($w.Name) ($($w.Table)).") }
                elseif ($h.Depth -ne 'Basic') { $r.Add("PRIVILEGE DEPTH: $($w.Name) ($($w.Table)) at $($h.Depth), expected Basic.") }
            }
            foreach ($o in @($s.Held | Where-Object { $d.WantedIds -notcontains $_.PrivilegeId.ToString().ToLowerInvariant() } | Sort-Object PrivilegeName)) {
                $r.Add("PRIVILEGE OUTSIDE THE FILE: '$($config.roleName)' holds $($o.PrivilegeName) at $($o.Depth).")
            }
        }
        return , $r
    }

    function Write-State($d, $s) {
        $config = $d.Config
        Write-Host ''
        Write-Host "== $($config.teamName) ($($d.File))"
        if ($s.Team) {
            Write-Host "Team        : $($s.Team.name) ($($s.Team.teamid)) teamtype=$($s.Team.teamtype) isdefault=$($s.Team.isdefault) bu=$($s.Team._businessunitid_value) members=$($s.Members.Count) roles=$($s.TeamRoles.Count)"
        }
        else { Write-Host "Team        : (none named '$($config.teamName)' in $($customer.name))" }
        if ($s.Role) {
            $copy = if ($s.Copy) { $s.Copy.roleid } else { '(none)' }
            Write-Host "Role        : $($s.Role.name) ($($s.Role.roleid)) customer-unit copy=$copy copies=$($s.Copies.Count) assignments=$($s.Assigned.Count) privileges=$($s.Held.Count)"
        }
        else { Write-Host "Role        : (none named '$($config.roleName)' in the root business unit)" }
        # Information only: the other customer top units that do not have the team yet (each is set up by its own run).
        foreach ($u in @($customerTops | Where-Object { $_.businessunitid -ne $customerId } | Sort-Object name)) {
            $has = @($s.AllTeams | Where-Object { $_._businessunitid_value -eq $u.businessunitid -and $_.teamtype -eq 0 -and -not $_.isdefault })
            if ($has.Count -eq 0) { Write-Host "INFO        : customer top unit '$($u.name)' ($($u.businessunitid)) has no '$($config.teamName)' team (not this run's unit; not a failure)." }
        }
    }

    function Write-Drift($r) {
        if ($r.Count -eq 0) { Write-Host 'Drift       : none' -ForegroundColor Green }
        foreach ($line in $r) { Write-Host "DRIFT       : $line" -ForegroundColor Yellow }
    }

    # What -Apply would do for one declared team (the lines a dry run prints).
    function Get-Plan($d, $s) {
        $config = $d.Config
        $p = [System.Collections.Generic.List[string]]::new()
        if ($null -eq $s.Team) { $p.Add("create the Owner team '$($config.teamName)' in $($customer.name) (teamtype 0), with no members") }
        if ($null -eq $s.Role) { $p.Add("create the role '$($config.roleName)' in the root business unit (Dataverse copies it into every child unit)") }
        $missing = @($d.Wanted | Where-Object { $w = $_; $null -eq $s.Role -or -not @($s.Held | Where-Object { $_.PrivilegeId.ToString().ToLowerInvariant() -eq $w.Id -and $_.Depth -eq 'Basic' }) })
        if ($missing.Count -gt 0) { $p.Add("add Read at Basic to '$($config.roleName)': $(($missing | ForEach-Object Name) -join ', ')") }
        $p.Add("take off every privilege of '$($config.roleName)' outside $($d.File), including any the platform injects (setup guide 5.4)")
        if ($null -eq $s.Team -or $null -eq $s.Copy -or -not @($s.Assigned | Where-Object { $_.Kind -eq 'team' -and $_.Id -eq $s.Team.teamid -and $_.RoleId -eq $s.Copy.roleid })) {
            $p.Add("assign the $($customer.name) copy of '$($config.roleName)' to '$($config.teamName)'")
        }
        $p.Add("read '$($config.teamName)' back and check it as -Verify does")
        return , $p
    }

    function Invoke-ApplyTeam($d, $s) {
        $config = $d.Config
        $createdTeam = $false
        if ($null -eq $s.Team) {
            $me = (Invoke-DvGet 'WhoAmI').UserId
            Invoke-DvPost 'teams' @{
                name                         = $config.teamName
                teamtype                     = 0
                'businessunitid@odata.bind'  = "/businessunits($customerId)"
                'administratorid@odata.bind' = "/systemusers($me)"
                description                  = "Memberless owner team for the tables of config/$($d.File) in this customer business unit. MUST NEVER have a member or any other role."
            } | Out-Null
            $createdTeam = $true
            Write-Host "CREATED team '$($config.teamName)' in $($customer.name)." -ForegroundColor Green
        }
        if ($null -eq $s.Role) {
            Invoke-DvPost 'roles' @{
                name                        = $config.roleName
                'businessunitid@odata.bind' = "/businessunits($rootId)"
                description                 = "Read at Basic on the tables of config/$($d.File) only, so each customer unit's memberless $($config.teamName) team can own their rows. Its copies are assigned to those teams only. MUST NOT be granted to any user or any other team."
            } | Out-Null
            Write-Host "CREATED role '$($config.roleName)'." -ForegroundColor Green
        }

        $s = Get-State $d
        if ($null -eq $s.Team -or $null -eq $s.Role) { throw "'$($config.teamName)' or '$($config.roleName)' could not be read back after it was created." }
        if ($null -eq $s.Copy) { throw "'$($config.roleName)' has no copy in $($customer.name) to assign. Nothing further was written for it." }
        $roleId = $s.Role.roleid

        if ($createdTeam -and $s.Members.Count -gt 0) {
            # Only a team THIS run created: a member it has now was added by the create itself, never by a person.
            Invoke-DvPost "teams($($s.Team.teamid))/Microsoft.Dynamics.CRM.RemoveMembersTeam" @{
                Members = @($s.Members | ForEach-Object { @{ '@odata.type' = 'Microsoft.Dynamics.CRM.systemuser'; systemuserid = $_.systemuserid } })
            } | Out-Null
            Write-Host "REMOVED the $($s.Members.Count) member(s) the create added to '$($config.teamName)'." -ForegroundColor Green
        }

        $heldById = @{}
        foreach ($h in $s.Held) { $heldById[$h.PrivilegeId.ToString().ToLowerInvariant()] = $h }
        $toAdd = @($d.Wanted | Where-Object { $null -eq $heldById[$_.Id] -or $heldById[$_.Id].Depth -ne 'Basic' })
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
            Write-Host "ADDED at Basic to '$($config.roleName)': $(($toAdd | ForEach-Object Name) -join ', ')" -ForegroundColor Green
        }

        # After AddPrivilegesRole, which re-injects platform privileges: everything outside the file comes off this role.
        $held = @((Invoke-DvGet "RetrieveRolePrivilegesRole(RoleId=@p)?@p=$roleId").RolePrivileges)
        $outside = @($held | Where-Object { $d.WantedIds -notcontains $_.PrivilegeId.ToString().ToLowerInvariant() })
        foreach ($o in $outside) {
            Invoke-DvPost "roles($roleId)/Microsoft.Dynamics.CRM.RemovePrivilegeRole" @{
                Privilege = @{ '@odata.type' = 'Microsoft.Dynamics.CRM.privilege'; privilegeid = $o.PrivilegeId.ToString() }
            } | Out-Null
        }
        if ($outside.Count -gt 0) { Write-Host "REMOVED from '$($config.roleName)' outside the file: $(($outside | ForEach-Object PrivilegeName) -join ', ')" -ForegroundColor Green }

        if (-not @($s.Assigned | Where-Object { $_.Kind -eq 'team' -and $_.Id -eq $s.Team.teamid -and $_.RoleId -eq $s.Copy.roleid })) {
            $ref = @{ '@odata.id' = "$Api/roles($($s.Copy.roleid))" }
            Invoke-DvPost "teams($($s.Team.teamid))/teamroles_association/`$ref" $ref | Out-Null
            Write-Host "ASSIGNED the $($customer.name) copy of '$($config.roleName)' to '$($config.teamName)'." -ForegroundColor Green
        }
    }

    # ── Every declared team's state and drift ───────────────────────────────────────────────────────────────
    $results = @(foreach ($d in $defs) {
            $s = Get-State $d
            $r = Get-Drift $d $s
            Write-State $d $s
            Write-Drift $r
            [pscustomobject]@{ Def = $d; State = $s; Drift = $r }
        })
    $driftCount = ($results | ForEach-Object { $_.Drift.Count } | Measure-Object -Sum).Sum

    # ── Verify ──────────────────────────────────────────────────────────────────────────────────────────────
    if ($Verify) {
        if ($driftCount -eq 0) {
            Write-Host "`nVERIFY PASS: in $($customer.name) each declared team is memberless and holds only that unit's copy of its role, which holds Read at Basic on exactly its declared tables." -ForegroundColor Green
            exit 0
        }
        Write-Host "`nVERIFY FAIL: $driftCount drift finding(s) above." -ForegroundColor Red
        exit 1
    }

    # ── What -Apply may and may not change ──────────────────────────────────────────────────────────────────
    # Owner decisions, never changed here: refuse before anything is written, for every declared team.
    $ownerOnly = @($results | ForEach-Object { $_.Drift } | Where-Object { $_ -match '^(TEAM TYPE|TEAM DEFAULT|TEAM MEMBERS|TEAM ROLE|ROLE ASSIGNED ELSEWHERE):' })
    Write-Host ''
    if ($ownerOnly.Count -gt 0) {
        Write-Host "OWNER DECISION NEEDED: -Apply never changes these; it refuses while they stand:" -ForegroundColor Red
        foreach ($line in $ownerOnly) { Write-Host "  $line" -ForegroundColor Red }
    }
    if ($driftCount -eq 0) { Write-Host 'Nothing to do.' -ForegroundColor Green; exit 0 }
    $step = 0
    Write-Host ("PLAN ({0}):" -f $(if ($IsDryRun) { 'dry run, nothing written' } else { 'applying' })) -ForegroundColor Cyan
    foreach ($x in @($results | Where-Object { $_.Drift.Count -gt 0 })) {
        foreach ($line in (Get-Plan $x.Def $x.State)) { $step++; Write-Host ("  {0}. {1}" -f $step, $line) -ForegroundColor Cyan }
    }
    if ($IsDryRun) {
        if ($ownerOnly.Count -gt 0) {
            # R4b-4 (B-K4): a plan -Apply refuses is not a plan to run; the dry run fails with the owner-decision summary.
            Write-Host "`nDRY RUN: no writes. OWNER DECISION NEEDED: $($ownerOnly.Count) finding(s) above; -Apply refuses until an owner settles them. Exit 1." -ForegroundColor Red
            exit 1
        }
        Write-Host "`nDRY RUN: no writes. Re-run with -Apply (owner go), then -Verify." -ForegroundColor Cyan
        exit 0
    }
    if ($ownerOnly.Count -gt 0) { throw 'Refused: the drift above is an owner decision. Nothing was written.' }

    # ── Apply ───────────────────────────────────────────────────────────────────────────────────────────────
    foreach ($x in @($results | Where-Object { $_.Drift.Count -gt 0 })) { Invoke-ApplyTeam $x.Def $x.State }

    $left = 0
    foreach ($d in $defs) {
        $s = Get-State $d
        Write-State $d $s
        $r = Get-Drift $d $s
        foreach ($line in $r) { Write-Host "DRIFT       : $line" -ForegroundColor Red }
        $left += $r.Count
    }
    if ($left -gt 0) { throw "Read-back still shows $left drift finding(s); the setup is not complete." }
    Write-Host "`nAPPLIED and read back: no drift. Dataverse caches principal privileges; re-run -Verify and probe across 3 polls (setup guide section 7)." -ForegroundColor Green
    exit 0
}
catch {
    Write-Host "FAILED (fail closed, exit 1): $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
