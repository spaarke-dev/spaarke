#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Sets up the 'External Collaborators' business unit and the 'Spaarke External Collaborator' role from
    config/external-collaborator-role.json, and verifies them. Dry run by default.

.DESCRIPTION
    unified-access-control-r2 task 191d (#2179 design v2, sections 3.2 and 3.15). Every flagged-external systemuser
    (sprk_isexternal = true) lives in ONE business unit that owns no rows and holds one role at Basic depth, so the only
    path to a lineage row is a share (the External team share of task 191c, or a per-user share). This script codifies that
    unit and role. It never moves a user, never assigns the role, never touches a share and never changes a field-security
    profile: those are tasks 191c, 191e and the separate profile step (design 3.19 step 3).

    MODES
      (default)  Dry run. Reads everything and prints the plan: the business unit to create, the role to create, each
                 privilege to add, change or take off, and the gaps -Verify would report now. Zero writes. Exit 0, or exit 1
                 when the run is refused (below).
      -Apply     Creates the business unit under the root if it is missing; creates the role IN that unit if it is
                 missing (isinherited 1: direct user access); sets isinherited to 1 if it differs; adds every declared
                 privilege at its declared depth and takes every other privilege off the role (the platform injects some on
                 create and on every AddPrivilegesRole: setup guide SECURE-PROJECT-ENVIRONMENT-SETUP.md 5.4). Then reads
                 everything back and checks it as -Verify does (exit 1 if a gap remains). It writes nothing else.
                 LIVE / OWNER GO: run only in task 191i after the owner's go for that environment.
      -Verify    Read-only. Exit 0 when there is no gap; exit 1 naming each gap:
                   BU MISSING, BU PARENT, BU CONTAINER, BU DEFAULT TEAM, BU OWNS ROWS (per table, rows owned by the unit or
                   by anyone homed in it), DEFAULT TEAM ROLE, DEFAULT TEAM PROFILE, WRITER PROFILE, STORED FILE PROFILE,
                   READER PROFILE MISSING, ROLE MISSING, ROLE PLACE, ROLE INHERITANCE, ROLE HOLDER, PRIVILEGE MISSING,
                   PRIVILEGE DEPTH, PRIVILEGE OUTSIDE THE FILE, NEVER-TABLE PRIVILEGE, WRITE USER.
      -Apply and -Verify are separate modes: passing both throws. -WhatIf forces a dry run.

    REFUSED by -Apply before any write (both are BU PARENT / ROLE PLACE gaps in the other modes): the unit not a direct
    child of the root; a role of the name in another unit that is not a copy of the unit's role.
    REFUSED (every mode, before any write; exit 1): more than one business unit of the name; more than one role of the name
    created in the unit; a config that breaks its own rules (Basic only on lineage tables; Read/Write/Append/AppendTo/Create only;
    no table both lineage and never; no Share or Assign; no prvWriteUser; each platform privilege at the lowest depth the
    privilege supports; each catalog table organization-owned); a lineage or catalog table missing from metadata.

    FAIL CLOSED: every read that fails stops the run (exit 1); nothing is decided on a partial read. A 'never' table that
    does not exist in the environment has no privilege to hold and is reported as INFO.

    Auth is the operator's own az CLI identity. No secrets in this script or the repository. The output prints ids and
    counts; it never prints a user's name.

.PARAMETER EnvironmentUrl
    Dataverse environment URL, e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER Apply
    Write mode (owner go only, task 191i). Without it the script never writes.

.PARAMETER Verify
    Read-only check with a pass/fail exit code. Cannot be combined with -Apply.

.PARAMETER WhatIf
    Forces a dry run even when -Apply is passed.

.PARAMETER ConfigPath
    The codified definition. Default: config/external-collaborator-role.json at the repository root.

.EXAMPLE
    .\Set-ExternalCollaboratorRole.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com
    Dry run: the plan and the current gaps, no writes.

.EXAMPLE
    .\Set-ExternalCollaboratorRole.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Verify
    Exit 0 = the unit and role match the file; exit 1 = each gap named, or a read failed.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$EnvironmentUrl,

    [switch]$Apply,

    [switch]$Verify,

    [switch]$WhatIf,

    [string]$ConfigPath
)

$ErrorActionPreference = 'Stop'
if ($Apply -and $Verify) { throw '-Apply and -Verify are separate modes; run -Apply first, then -Verify.' }
$IsDryRun = (-not $Apply.IsPresent) -or $WhatIf.IsPresent
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$Api = "$EnvironmentUrl/api/data/v9.2"
if (-not $ConfigPath) { $ConfigPath = Join-Path $PSScriptRoot '..' 'config' 'external-collaborator-role.json' }

$LineageAccess = @('Read', 'Write', 'Append', 'AppendTo', 'Create')
$CatalogAccess = @('Read', 'AppendTo')

# The rules the file must keep (tests/Spaarke.ArchTests/ExternalCollaboratorRoleGuardTests.cs pins the same ones offline).
function Read-ExternalCollaboratorConfig([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { throw "Config not found: $Path" }
    $c = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($c.schemaVersion -ne 1) { throw "Unsupported schemaVersion '$($c.schemaVersion)' in $Path" }
    if ([string]::IsNullOrWhiteSpace($c.businessUnit.name)) { throw "Config $Path has no businessUnit.name." }
    if ($c.businessUnit.parent -ne 'root') { throw "Config $Path names businessUnit.parent '$($c.businessUnit.parent)'. Only 'root' is supported (design 3.2)." }
    if ([string]::IsNullOrWhiteSpace($c.roleName)) { throw "Config $Path has no roleName." }
    if ("$($c.isinherited)" -ne '1') { throw "Config $Path declares isinherited '$($c.isinherited)'. Only 1 (direct user access) is supported: the role is assigned to users." }
    if ($c.lineageDepth -ne 'Basic') { throw "Config $Path declares lineageDepth '$($c.lineageDepth)'. Only Basic is supported, by design." }
    $lineage = @($c.lineage); $never = @($c.never); $platform = @($c.platform); $catalogs = @($c.catalogs)
    if ($lineage.Count -eq 0 -or $never.Count -eq 0) { throw "Config $Path lists no lineage or no never tables." }
    $all = @($lineage + $never + $catalogs | ForEach-Object logicalName)
    $dupes = @($all | Group-Object | Where-Object Count -gt 1)
    if ($dupes.Count -gt 0) { throw "Config $Path lists a table twice (across lineage, never and catalogs): $(($dupes | ForEach-Object Name) -join ', ')" }
    foreach ($e in $lineage) {
        $access = @($e.access)
        if ($access -notcontains 'Read') { throw "Config $Path lineage '$($e.logicalName)' has no Read." }
        foreach ($a in $access) { if ($LineageAccess -cnotcontains $a) { throw "Config $Path lineage '$($e.logicalName)' asks for '$a'. Only $($LineageAccess -join ', ') are allowed (no Delete, Share or Assign)." } }
        if (@($access | Group-Object | Where-Object Count -gt 1).Count -gt 0) { throw "Config $Path lineage '$($e.logicalName)' lists an access type twice." }
        if ($null -ne $e.depth) { throw "Config $Path lineage '$($e.logicalName)' names a depth; every lineage table is at lineageDepth (Basic)." }
        if ([string]::IsNullOrWhiteSpace($e.reason)) { throw "Config $Path lineage '$($e.logicalName)' has no reason." }
    }
    foreach ($e in $catalogs) {
        foreach ($a in @($e.access)) { if ($CatalogAccess -cnotcontains $a) { throw "Config $Path catalog '$($e.logicalName)' asks for '$a'. Only Read and AppendTo are allowed." } }
        if ($e.depth -ne 'Global') { throw "Config $Path catalog '$($e.logicalName)' is at '$($e.depth)'. A catalog is an organization-owned table: Global is its only depth." }
    }
    foreach ($p in $platform) {
        $n = [string]$p.privilegeName
        if ($n -match 'sprk_') { throw "Config $Path platform '$n' names a sprk_ table. Spaarke tables go in lineage, never or catalogs." }
        if ($n -match '^prv(Share|Assign)') { throw "Config $Path platform '$n': the role holds no Share or Assign privilege." }
        if ($n -eq 'prvWriteUser') { throw "Config $Path platform names prvWriteUser. A flagged user must never write a systemuser row (it could clear sprk_isexternal)." }
        if (@('Basic', 'Local', 'Global') -cnotcontains $p.depth) { throw "Config $Path platform '$n' has depth '$($p.depth)'. Only Basic, Local or Global (the lowest each privilege supports; never Deep)." }
        if ($n -match '^prvDelete' -and $p.depth -ne 'Basic') { throw "Config $Path platform '$n' is a Delete above Basic." }
    }
    $pd = @($platform | Group-Object privilegeName | Where-Object Count -gt 1)
    if ($pd.Count -gt 0) { throw "Config $Path lists a platform privilege twice: $(($pd | ForEach-Object Name) -join ', ')" }
    return $c
}

function Q([string]$Value) { $Value -replace "'", "''" }

try {
    $config = Read-ExternalCollaboratorConfig $ConfigPath
    $buName = [string]$config.businessUnit.name
    $roleName = [string]$config.roleName

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
    function Invoke-DvPatch([string]$RelativePath, $Body) {
        $json = $Body | ConvertTo-Json -Depth 6
        Invoke-RestMethod -Uri "$Api/$RelativePath" -Headers ($headers + @{ 'If-Match' = '*' }) -Method Patch -Body ([Text.Encoding]::UTF8.GetBytes($json))
    }

    Write-Host "Environment : $EnvironmentUrl"
    Write-Host "Config      : $ConfigPath (unit '$buName', role '$roleName')"
    Write-Host ("Mode        : {0}" -f $(if ($Verify) { 'VERIFY (read-only)' } elseif ($IsDryRun) { 'DRY RUN (no writes)' } else { 'APPLY' }))

    # ── Metadata: every declared privilege, resolved by id ──────────────────────────────────────────────────
    function Get-TableMetadata([string]$Table) {
        $rows = @(Get-DvAll "EntityDefinitions?`$select=LogicalName,EntitySetName,OwnershipType,PrimaryIdAttribute&`$filter=LogicalName eq '$(Q $Table)'")
        if ($rows.Count -eq 0) { return $null }
        $privs = @((Invoke-DvGet "EntityDefinitions(LogicalName='$(Q $Table)')/Privileges").value)
        return [pscustomobject]@{ Table = $Table; EntitySet = $rows[0].EntitySetName; Ownership = [string]$rows[0].OwnershipType; IdColumn = $rows[0].PrimaryIdAttribute; Privileges = $privs }
    }
    function Get-LowestDepth($p) {
        if ($p.CanBeBasic) { return 'Basic' }
        if ($p.CanBeLocal) { return 'Local' }
        if ($p.CanBeDeep) { return 'Deep' }
        if ($p.CanBeGlobal) { return 'Global' }
        return $null
    }

    $wanted = [System.Collections.Generic.List[object]]::new()
    $tableMeta = @{}
    foreach ($e in @($config.lineage)) {
        $m = Get-TableMetadata $e.logicalName
        if ($null -eq $m) { throw "Lineage table '$($e.logicalName)' is not in this environment's metadata. Nothing was written." }
        $tableMeta[$e.logicalName] = $m
        foreach ($a in @($e.access)) {
            $p = @($m.Privileges | Where-Object { $_.PrivilegeType -eq $a })
            if ($p.Count -ne 1) { throw "Table '$($e.logicalName)': expected one $a privilege in metadata, found $($p.Count)." }
            if (-not $p[0].CanBeBasic) { throw "Table '$($e.logicalName)': $($p[0].Name) cannot be granted at Basic. Escalate (design 3.2: Basic only on lineage tables)." }
            $wanted.Add([pscustomobject]@{ Name = $p[0].Name; Id = $p[0].PrivilegeId.ToString().ToLowerInvariant(); Depth = 'Basic'; Source = "lineage $($e.logicalName)" })
        }
    }
    foreach ($e in @($config.catalogs)) {
        $m = Get-TableMetadata $e.logicalName
        if ($null -eq $m) { throw "Catalog table '$($e.logicalName)' is not in this environment's metadata. Nothing was written." }
        if ($m.Ownership -ne 'OrganizationOwned') { throw "Catalog table '$($e.logicalName)' is $($m.Ownership), not OrganizationOwned. A user-owned table never takes Global (owner decision)." }
        $tableMeta[$e.logicalName] = $m
        foreach ($a in @($e.access)) {
            $p = @($m.Privileges | Where-Object { $_.PrivilegeType -eq $a })
            if ($p.Count -ne 1) { throw "Table '$($e.logicalName)': expected one $a privilege in metadata, found $($p.Count)." }
            $wanted.Add([pscustomobject]@{ Name = $p[0].Name; Id = $p[0].PrivilegeId.ToString().ToLowerInvariant(); Depth = 'Global'; Source = "catalog $($e.logicalName)" })
        }
    }
    $platformNames = @($config.platform | ForEach-Object { [string]$_.privilegeName })
    if ($platformNames.Count -gt 0) {
        $list = ($platformNames | ForEach-Object { "'$(Q $_)'" }) -join ','
        $found = @(Get-DvAll "privileges?`$select=privilegeid,name,canbebasic,canbelocal,canbedeep,canbeglobal&`$filter=Microsoft.Dynamics.CRM.In(PropertyName='name',PropertyValues=[$list])")
        foreach ($e in @($config.platform)) {
            $p = @($found | Where-Object { $_.name -ceq $e.privilegeName })
            if ($p.Count -ne 1) { throw "Platform privilege '$($e.privilegeName)': expected one in this environment, found $($p.Count)." }
            $lowest = Get-LowestDepth ([pscustomobject]@{ CanBeBasic = $p[0].canbebasic; CanBeLocal = $p[0].canbelocal; CanBeDeep = $p[0].canbedeep; CanBeGlobal = $p[0].canbeglobal })
            if ($e.depth -ne $lowest) { throw "Platform privilege '$($e.privilegeName)' is declared at $($e.depth); the lowest depth it supports is $lowest. Every platform privilege is at its lowest depth." }
            $wanted.Add([pscustomobject]@{ Name = $p[0].name; Id = $p[0].privilegeid.ToString().ToLowerInvariant(); Depth = [string]$e.depth; Source = 'platform' })
        }
    }
    $wantedIds = @{}
    foreach ($w in $wanted) { $wantedIds[$w.Id] = $w }

    # Every privilege of every never table (a table absent from the environment has none to hold).
    $neverIds = @{}
    foreach ($e in @($config.never)) {
        $m = Get-TableMetadata $e.logicalName
        if ($null -eq $m) { Write-Host "INFO        : never table '$($e.logicalName)' is not in this environment (no privilege to hold)."; continue }
        $tableMeta[$e.logicalName] = $m
        foreach ($p in @($m.Privileges)) { $neverIds[$p.PrivilegeId.ToString().ToLowerInvariant()] = "$($p.Name) ($($e.logicalName))" }
    }
    foreach ($w in $wanted) { if ($neverIds.ContainsKey($w.Id)) { throw "The file grants $($w.Name), a privilege on a never table. Nothing was written." } }
    $writeUserId = $null
    $wu = @(Get-DvAll "privileges?`$select=privilegeid,name&`$filter=name eq 'prvWriteUser'")
    if ($wu.Count -eq 1) { $writeUserId = $wu[0].privilegeid.ToString().ToLowerInvariant() }

    # ── The unit, its default team and the role: read the current state ─────────────────────────────────────
    $roots = @(Get-DvAll 'businessunits?$select=businessunitid,name&$filter=_parentbusinessunitid_value eq null')
    if ($roots.Count -ne 1) { throw "Expected exactly one root business unit; found $($roots.Count)." }
    $rootId = [string]$roots[0].businessunitid
    Write-Host "Root BU     : $($roots[0].name) ($rootId)"

    function Get-State {
        $s = [ordered]@{ Unit = $null; DefaultTeam = $null; TeamRoles = @(); TeamProfiles = @(); Role = $null; OtherRoles = @(); Copies = @(); UserHolders = @(); TeamHolders = @(); Held = @(); Owned = @() }
        $units = @(Get-DvAll "businessunits?`$select=businessunitid,name,_parentbusinessunitid_value,sprk_containerid&`$filter=name eq '$(Q $buName)'")
        if ($units.Count -gt 1) { throw "Refused: $($units.Count) business units are named '$buName'. Resolve it by hand (owner decision). Nothing was written." }
        if ($units.Count -eq 1) {
            $s.Unit = $units[0]
            $unitId = [string]$s.Unit.businessunitid
            $teams = @(Get-DvAll "teams?`$select=teamid,name,isdefault&`$filter=_businessunitid_value eq $unitId and isdefault eq true")
            if ($teams.Count -eq 1) {
                $s.DefaultTeam = $teams[0]
                $s.TeamRoles = @(Get-DvAll "teams($($s.DefaultTeam.teamid))/teamroles_association?`$select=roleid,name")
                $s.TeamProfiles = @(foreach ($p in @(Get-DvAll "teams($($s.DefaultTeam.teamid))/teamprofiles_association?`$select=fieldsecurityprofileid,name")) {
                        $perms = @(Get-DvAll "fieldpermissions?`$select=entityname,attributelogicalname,canread,cancreate,canupdate&`$filter=_fieldsecurityprofileid_value eq $($p.fieldsecurityprofileid)")
                        [pscustomobject]@{ Id = [string]$p.fieldsecurityprofileid; Name = [string]$p.name; Permissions = $perms }
                    })
            }
            # Rows owned in the unit (by its default team or by anyone homed in it), per user/team-owned lineage or never table.
            $s.Owned = @(foreach ($t in @(@($config.lineage) + @($config.never) | ForEach-Object logicalName)) {
                    $m = $tableMeta[$t]
                    if ($null -eq $m -or $m.Ownership -ne 'UserOwned') { continue }
                    $page = Invoke-DvGet "$($m.EntitySet)?`$select=$($m.IdColumn)&`$filter=_owningbusinessunit_value eq $unitId&`$count=true&`$top=1"
                    $n = [int]$page.'@odata.count'
                    if ($n -gt 0) { [pscustomobject]@{ Table = $t; Count = $n } }
                })
        }
        $roles = @(Get-DvAll "roles?`$select=roleid,name,isinherited,_businessunitid_value,_parentrootroleid_value&`$filter=name eq '$(Q $roleName)'")
        if ($s.Unit) {
            $unitId = [string]$s.Unit.businessunitid
            $inUnit = @($roles | Where-Object { [string]$_._businessunitid_value -eq $unitId -and ([string]$_._parentrootroleid_value -eq [string]$_.roleid -or -not $_._parentrootroleid_value) })
            if ($inUnit.Count -gt 1) { throw "Refused: $($inUnit.Count) roles named '$roleName' were created in '$buName'. Resolve it by hand (owner decision). Nothing was written." }
            if ($inUnit.Count -eq 1) {
                $s.Role = $inUnit[0]
                $rid = [string]$s.Role.roleid
                $s.Copies = @($roles | Where-Object { [string]$_._parentrootroleid_value -eq $rid -and [string]$_.roleid -ne $rid })
                foreach ($r in @(@($s.Role) + @($s.Copies))) {
                    $s.UserHolders += @(Get-DvAll "roles($($r.roleid))/systemuserroles_association?`$select=systemuserid,_businessunitid_value")
                    $s.TeamHolders += @(Get-DvAll "roles($($r.roleid))/teamroles_association?`$select=teamid,_businessunitid_value")
                }
                $s.Held = @((Invoke-DvGet "RetrieveRolePrivilegesRole(RoleId=@p)?@p=$rid").RolePrivileges)
            }
            $mine = @(@($s.Role) + @($s.Copies) | Where-Object { $_ } | ForEach-Object { [string]$_.roleid })
            $s.OtherRoles = @($roles | Where-Object { $mine -notcontains [string]$_.roleid })
        }
        else { $s.OtherRoles = $roles }
        return [pscustomobject]$s
    }

    function Get-Gaps($s) {
        $g = [System.Collections.Generic.List[string]]::new()
        foreach ($o in $s.OtherRoles) { $g.Add("ROLE PLACE: a role named '$roleName' ($($o.roleid)) exists in business unit $($o._businessunitid_value), outside '$buName' and not a copy of its role.") }
        if ($null -eq $s.Unit) {
            $g.Add("BU MISSING: no business unit named '$buName' under the root.")
            $g.Add("ROLE MISSING: no role named '$roleName' in '$buName'.")
            return , $g
        }
        if ([string]$s.Unit._parentbusinessunitid_value -ne $rootId) { $g.Add("BU PARENT: '$buName' ($($s.Unit.businessunitid)) is not a direct child of the root (parent $($s.Unit._parentbusinessunitid_value)).") }
        if ($s.Unit.sprk_containerid) { $g.Add("BU CONTAINER: '$buName' names a container ($($s.Unit.sprk_containerid)); the unit holds no container.") }
        foreach ($o in $s.Owned) { $g.Add("BU OWNS ROWS: $($o.Count) $($o.Table) row(s) are owned in '$buName' (its default team or a user homed in it); the unit owns no rows.") }
        if ($null -eq $s.DefaultTeam) { $g.Add("BU DEFAULT TEAM: '$buName' has no default team.") }
        else {
            foreach ($r in $s.TeamRoles) { $g.Add("DEFAULT TEAM ROLE: the default team holds role '$($r.name)' ($($r.roleid)); every member would get its privileges. It holds no role.") }
            $allowed = @($config.defaultTeam.readerProfiles | ForEach-Object { [string]$_ })
            $storedFileTables = @($config.defaultTeam.storedFileTables | ForEach-Object { [string]$_ })
            foreach ($p in $s.TeamProfiles) {
                $writes = @($p.Permissions | Where-Object { $_.cancreate -eq 4 -or $_.canupdate -eq 4 })
                $stored = @($p.Permissions | Where-Object { $storedFileTables -contains [string]$_.entityname -and $_.canread -eq 4 })
                if ($writes.Count -gt 0) { $g.Add("WRITER PROFILE: the default team holds '$($p.Name)', which creates or updates $($writes.Count) column(s) (e.g. $($writes[0].entityname).$($writes[0].attributelogicalname)).") }
                if ($stored.Count -gt 0) { $g.Add("STORED FILE PROFILE: the default team holds '$($p.Name)', which reads $($stored.Count) Stored File column(s) (ebilling 088: never on this team).") }
                if ($allowed -notcontains $p.Name) { $g.Add("DEFAULT TEAM PROFILE: the default team holds '$($p.Name)', which is not in defaultTeam.readerProfiles.") }
            }
            foreach ($a in $allowed) { if (-not @($s.TeamProfiles | Where-Object { $_.Name -eq $a })) { $g.Add("READER PROFILE MISSING: the default team does not hold '$a' (design 3.19 step 3).") } }
        }
        if ($null -eq $s.Role) { $g.Add("ROLE MISSING: no role named '$roleName' in '$buName'."); return , $g }
        if ("$($s.Role.isinherited)" -ne '1') { $g.Add("ROLE INHERITANCE: '$roleName' isinherited is $($s.Role.isinherited), expected 1 (direct user access).") }
        $unitId = [string]$s.Unit.businessunitid
        foreach ($t in $s.TeamHolders) { $g.Add("ROLE HOLDER: team $($t.teamid) holds '$roleName'; only users homed in '$buName' hold it.") }
        foreach ($u in @($s.UserHolders | Where-Object { [string]$_._businessunitid_value -ne $unitId })) { $g.Add("ROLE HOLDER: user $($u.systemuserid) holds '$roleName' but is homed in business unit $($u._businessunitid_value), not '$buName'.") }
        $heldById = @{}
        foreach ($h in $s.Held) { $heldById[$h.PrivilegeId.ToString().ToLowerInvariant()] = $h }
        foreach ($w in $wanted) {
            $h = $heldById[$w.Id]
            if ($null -eq $h) { $g.Add("PRIVILEGE MISSING: $($w.Name) at $($w.Depth) ($($w.Source)).") }
            elseif ($h.Depth -ne $w.Depth) { $g.Add("PRIVILEGE DEPTH: $($w.Name) at $($h.Depth), expected $($w.Depth) ($($w.Source)).") }
        }
        foreach ($h in @($s.Held | Sort-Object PrivilegeName)) {
            $id = $h.PrivilegeId.ToString().ToLowerInvariant()
            if ($wantedIds.ContainsKey($id)) { continue }
            if ($neverIds.ContainsKey($id)) { $g.Add("NEVER-TABLE PRIVILEGE: '$roleName' holds $($neverIds[$id]) at $($h.Depth); a never table gets no privilege.") }
            elseif ($writeUserId -and $id -eq $writeUserId) { $g.Add("WRITE USER: '$roleName' holds prvWriteUser at $($h.Depth); a flagged user could clear its own sprk_isexternal.") }
            else { $g.Add("PRIVILEGE OUTSIDE THE FILE: '$roleName' holds $($h.PrivilegeName) at $($h.Depth).") }
        }
        return , $g
    }

    function Write-State($s) {
        if ($s.Unit) { Write-Host "Unit        : $($s.Unit.name) ($($s.Unit.businessunitid)) parent=root container=$(if ($s.Unit.sprk_containerid) { $s.Unit.sprk_containerid } else { '(none)' })" }
        else { Write-Host "Unit        : (none named '$buName')" }
        if ($s.DefaultTeam) { Write-Host "Default team: $($s.DefaultTeam.teamid) roles=$($s.TeamRoles.Count) profiles=$(@($s.TeamProfiles).Count)" }
        if ($s.Role) { Write-Host "Role        : $($s.Role.roleid) isinherited=$($s.Role.isinherited) copies=$($s.Copies.Count) user holders=$($s.UserHolders.Count) team holders=$($s.TeamHolders.Count) privileges=$($s.Held.Count)" }
        else { Write-Host "Role        : (none named '$roleName' in '$buName')" }
        Write-Host "Declared    : $($wanted.Count) privileges ($(@($wanted | Where-Object { $_.Source -like 'lineage*' }).Count) lineage at Basic, $(@($wanted | Where-Object { $_.Source -like 'catalog*' }).Count) catalog at Global, $(@($wanted | Where-Object Source -eq 'platform').Count) platform); never tables: $(@($config.never).Count)"
    }

    function Write-Gaps($g, [string]$Label) {
        if ($g.Count -eq 0) { Write-Host "$Label : none" -ForegroundColor Green }
        foreach ($line in $g) { Write-Host "$Label : $line" -ForegroundColor Yellow }
    }

    # What -Apply would write (the lines a dry run prints).
    function Get-Plan($s) {
        $p = [System.Collections.Generic.List[string]]::new()
        if ($null -eq $s.Unit) { $p.Add("create the business unit '$buName' as a direct child of the root (its default team is created with it)") }
        if ($null -eq $s.Role) { $p.Add("create the role '$roleName' in '$buName' (isinherited 1)") }
        elseif ("$($s.Role.isinherited)" -ne '1') { $p.Add("set isinherited on '$roleName' to 1") }
        $heldById = @{}
        foreach ($h in $s.Held) { $heldById[$h.PrivilegeId.ToString().ToLowerInvariant()] = $h }
        foreach ($w in $wanted) {
            $h = $heldById[$w.Id]
            if ($null -eq $h) { $p.Add("add $($w.Name) at $($w.Depth) ($($w.Source))") }
            elseif ($h.Depth -ne $w.Depth) { $p.Add("change $($w.Name) from $($h.Depth) to $($w.Depth) ($($w.Source))") }
        }
        foreach ($h in @($s.Held | Where-Object { -not $wantedIds.ContainsKey($_.PrivilegeId.ToString().ToLowerInvariant()) } | Sort-Object PrivilegeName)) {
            $p.Add("take off $($h.PrivilegeName) (at $($h.Depth)), outside the file")
        }
        $p.Add("take off every privilege the platform injects on create and on AddPrivilegesRole that is outside the file (setup guide 5.4)")
        $p.Add("read everything back and check it as -Verify does; no user, team, share or profile is touched")
        return , $p
    }

    $state = Get-State
    Write-State $state
    $gaps = Get-Gaps $state

    if ($Verify) {
        Write-Gaps $gaps 'GAP        '
        if ($gaps.Count -gt 0) { Write-Host "`nVERIFY FAIL: $($gaps.Count) gap(s)." -ForegroundColor Red; exit 1 }
        Write-Host "`nVERIFY PASS: '$buName' and '$roleName' match $ConfigPath." -ForegroundColor Green
        exit 0
    }

    $plan = Get-Plan $state
    foreach ($line in $plan) { Write-Host "PLAN        : $line" }
    Write-Gaps $gaps 'GAP NOW    '
    if ($IsDryRun) {
        Write-Host "`nDRY RUN: nothing was written. -Apply is an owner-go step (task 191i)." -ForegroundColor Cyan
        exit 0
    }

    # ── -Apply ───────────────────────────────────────────────────────────────────────────────────────────────
    # Drift this script does not decide: a unit in the wrong place, or a role of the name somewhere else. Refuse before any write.
    $refusals = @($gaps | Where-Object { $_ -like 'BU PARENT:*' -or $_ -like 'ROLE PLACE:*' })
    if ($refusals.Count -gt 0) { throw "Refused (owner decision; nothing was written): $($refusals -join ' | ')" }
    if ($null -eq $state.Unit) {
        Invoke-DvPost 'businessunits' @{ name = $buName; 'parentbusinessunitid@odata.bind' = "/businessunits($rootId)" } | Out-Null
        Write-Host "CREATED business unit '$buName' under the root." -ForegroundColor Green
        $state = Get-State
        if ($null -eq $state.Unit) { throw "'$buName' could not be read back after it was created." }
    }
    $unitId = [string]$state.Unit.businessunitid
    if ($null -eq $state.Role) {
        Invoke-DvPost 'roles' @{
            name                        = $roleName
            'businessunitid@odata.bind' = "/businessunits($unitId)"
            isinherited                 = 1
            description                 = 'Flagged-external collaborators (sprk_isexternal = true) only, assigned from the External Collaborators unit. Basic depth; no Share, Assign or Delete; no privilege on the never tables. Source: config/external-collaborator-role.json (uac-r2 191d). Do not edit by hand.'
        } | Out-Null
        Write-Host "CREATED role '$roleName' in '$buName'." -ForegroundColor Green
        $state = Get-State
        if ($null -eq $state.Role) { throw "'$roleName' could not be read back after it was created." }
    }
    elseif ("$($state.Role.isinherited)" -ne '1') {
        Invoke-DvPatch "roles($($state.Role.roleid))" @{ isinherited = 1 } | Out-Null
        Write-Host "SET isinherited = 1 on '$roleName'." -ForegroundColor Green
    }
    $roleId = [string]$state.Role.roleid

    $heldById = @{}
    foreach ($h in $state.Held) { $heldById[$h.PrivilegeId.ToString().ToLowerInvariant()] = $h }
    $toAdd = @($wanted | Where-Object { $null -eq $heldById[$_.Id] -or $heldById[$_.Id].Depth -ne $_.Depth })
    foreach ($w in @($toAdd | Where-Object { $null -ne $heldById[$_.Id] })) {
        # A declared privilege at another depth: take it off, then add it at the declared depth below.
        Invoke-DvPost "roles($roleId)/Microsoft.Dynamics.CRM.RemovePrivilegeRole" @{ Privilege = @{ '@odata.type' = 'Microsoft.Dynamics.CRM.privilege'; privilegeid = $w.Id } } | Out-Null
    }
    if ($toAdd.Count -gt 0) {
        Invoke-DvPost "roles($roleId)/Microsoft.Dynamics.CRM.AddPrivilegesRole" @{
            Privileges = @($toAdd | ForEach-Object { @{ '@odata.type' = 'Microsoft.Dynamics.CRM.RolePrivilege'; Depth = $_.Depth; PrivilegeId = $_.Id; PrivilegeName = $_.Name; BusinessUnitId = $unitId } })
        } | Out-Null
        Write-Host "ADDED to '$roleName': $(($toAdd | ForEach-Object { "$($_.Name)@$($_.Depth)" }) -join ', ')" -ForegroundColor Green
    }
    # After the last AddPrivilegesRole (which re-injects platform privileges): everything outside the file comes off.
    $now = @((Invoke-DvGet "RetrieveRolePrivilegesRole(RoleId=@p)?@p=$roleId").RolePrivileges)
    $outside = @($now | Where-Object { -not $wantedIds.ContainsKey($_.PrivilegeId.ToString().ToLowerInvariant()) })
    foreach ($o in $outside) {
        Invoke-DvPost "roles($roleId)/Microsoft.Dynamics.CRM.RemovePrivilegeRole" @{ Privilege = @{ '@odata.type' = 'Microsoft.Dynamics.CRM.privilege'; privilegeid = $o.PrivilegeId.ToString() } } | Out-Null
    }
    if ($outside.Count -gt 0) { Write-Host "REMOVED from '$roleName' outside the file: $(($outside | ForEach-Object PrivilegeName) -join ', ')" -ForegroundColor Green }

    $after = Get-State
    $afterGaps = Get-Gaps $after
    Write-Gaps $afterGaps 'GAP AFTER  '
    if ($afterGaps.Count -gt 0) { Write-Host "`nAPPLY DONE WITH GAPS: $($afterGaps.Count) remain (above). This script fixes only the unit, the role and its privileges." -ForegroundColor Yellow; exit 1 }
    Write-Host "`nAPPLY DONE: '$buName' and '$roleName' match the file." -ForegroundColor Green
    exit 0
}
catch {
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
