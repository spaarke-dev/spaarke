# Test double of the Dataverse Web API for scripts/Set-CorePairFieldSecurity.ps1 (field-mapping-server-write-path-r1 task 044).
# Dot-sourced by Set-CorePairFieldSecurity.Tests.ps1 inside a CHILD pwsh, where it stands in for `az`, Invoke-RestMethod and
# Start-Sleep, so the whole script runs (exit codes included) with no environment, no token and no network.
#
# The scenario is a JSON file named by $env:CPF_SCENARIO:
#   attrs        {"table.column": {IsSecured, CanBeSecuredForCreate, CanBeSecuredForRead, CanBeSecuredForUpdate}} (absent = no column)
#   profiles     [{id, name, users:[systemuserid], teams:[teamid]}]
#   perms        [{profileid, entityname, attributelogicalname, canread, cancreate, canupdate}]
#   defaultTeams [{teamid, name, bu}]
#   users        [{systemuserid, fullname, applicationid, teams:[teamid]}]
#   sysAdmins    [systemuserid]           holders of the System Administrator role
#   rows         {table: {both:[id], typeOnly:n, idOnly:n}}
#   masked       {systemuserid: true}     a read AS that user (MSCRMCallerID) returns the core pair empty
#   acceptAnyCaller  true                  a made-up MSCRMCallerID is accepted (a server that ignores the header)
#   solutionComponents [objectid]
#   failGrant    "profileid|table.column"  that fieldpermissions POST fails (not 0x8004f508)
#   failRevert   true                      the PUT that un-secures a column fails
# Every request is appended to $env:CPF_LOG as one JSON line {Method, Uri, Body, Caller}. Any route the script does not use throws, so
# a new, unplanned call fails a test instead of passing silently.

$global:Scn = Get-Content -LiteralPath $env:CPF_SCENARIO -Raw | ConvertFrom-Json -AsHashtable
foreach ($k in 'profiles', 'perms', 'defaultTeams', 'users', 'sysAdmins', 'solutionComponents') { if (-not $global:Scn.ContainsKey($k)) { $global:Scn[$k] = @() } }
foreach ($k in 'attrs', 'rows', 'masked') { if (-not $global:Scn.ContainsKey($k)) { $global:Scn[$k] = @{} } }
$global:Perms = [System.Collections.Generic.List[object]]::new()
foreach ($p in $global:Scn.perms) { $global:Perms.Add($p) }
$global:Profiles = [System.Collections.Generic.List[object]]::new()
foreach ($p in $global:Scn.profiles) { $global:Profiles.Add($p) }

function az { 'fake-token' }
function Start-Sleep { param([int]$Seconds, [int]$Milliseconds) }

function Write-CpfLog([string]$Method, [string]$Uri, [string]$Body, [string]$Caller) {
    (@{ Method = $Method; Uri = $Uri; Body = $Body; Caller = $Caller } | ConvertTo-Json -Compress) | Add-Content -LiteralPath $env:CPF_LOG -Encoding UTF8
}
function Get-CpfUser([string]$Id) { $global:Scn.users | Where-Object { $_.systemuserid -eq $Id } | Select-Object -First 1 }
function Get-CpfProfile([string]$Id) { $global:Profiles | Where-Object { $_.id -eq $Id } | Select-Object -First 1 }
function ConvertTo-CpfProfileRow($p) {
    @{
        fieldsecurityprofileid         = $p.id
        name                           = $p.name
        teamprofiles_association       = @(@($p.teams) | Where-Object { $_ } | ForEach-Object { $tid = $_; $t = $global:Scn.defaultTeams | Where-Object { $_.teamid -eq $tid } | Select-Object -First 1; @{ teamid = $tid; name = $(if ($t) { $t.name } else { $tid }) } })
        systemuserprofiles_association = @(@($p.users) | Where-Object { $_ } | ForEach-Object { $u = Get-CpfUser $_; @{ systemuserid = $_; fullname = $u.fullname; applicationid = $u.applicationid } })
    }
}
function Get-CpfTable([string]$Set) { switch ($Set) { 'sprk_signals' { 'sprk_signal' } 'sprk_decisionrecords' { 'sprk_decisionrecord' } default { $null } } }

function Invoke-RestMethod {
    param([string]$Uri, $Headers, [string]$Method = 'Get', $Body)
    $bodyText = if ($null -eq $Body) { '' } elseif ($Body -is [byte[]]) { [Text.Encoding]::UTF8.GetString($Body) } else { [string]$Body }
    $caller = if ($Headers -and $Headers.ContainsKey('MSCRMCallerID')) { [string]$Headers['MSCRMCallerID'] } else { '' }
    Write-CpfLog $Method $Uri $bodyText $caller
    $rel = $Uri -replace '^https://[^/]+/api/data/v9\.2/', ''
    $d = [uri]::UnescapeDataString($rel)
    $s = $global:Scn

    if ($caller -and -not (Get-CpfUser $caller) -and -not $s.acceptAnyCaller) { throw 'The remote server returned an error: (404) Not Found. (caller)' }

    # ── writes ──
    if ($Method -eq 'Put' -and $d -match "^EntityDefinitions\(LogicalName='([^']+)'\)/Attributes\(LogicalName='([^']+)'\)$") {
        $key = "$($Matches[1]).$($Matches[2])"
        $b = $bodyText | ConvertFrom-Json
        if (-not $b.IsSecured -and $s.failRevert) { throw 'The remote server returned an error: (500) Internal Server Error. (revert)' }
        $s.attrs[$key].IsSecured = [bool]$b.IsSecured
        return $null
    }
    if ($Method -eq 'Post') {
        $b = if ($bodyText) { $bodyText | ConvertFrom-Json -AsHashtable } else { @{} }
        switch -Regex ($d) {
            '^fieldsecurityprofiles$' { $global:Profiles.Add(@{ id = $b.fieldsecurityprofileid; name = $b.name; users = @(); teams = @() }); return $null }
            '^fieldsecurityprofiles\(([^)]+)\)/systemuserprofiles_association/\$ref$' {
                $p = Get-CpfProfile $Matches[1]; $uid = ($b.'@odata.id' -replace '^.*systemusers\(([^)]+)\)$', '$1'); $p.users = @(@($p.users) + $uid); return $null
            }
            '^fieldpermissions$' {
                $pid_ = $b.'fieldsecurityprofileid@odata.bind' -replace '^/fieldsecurityprofiles\(([^)]+)\)$', '$1'
                if ($s.failGrant -eq "$pid_|$($b.entityname).$($b.attributelogicalname)") { throw 'The remote server returned an error: (500) Internal Server Error. (grant)' }
                $global:Perms.Add(@{ profileid = $pid_; entityname = $b.entityname; attributelogicalname = $b.attributelogicalname; canread = $b.canread; cancreate = $b.cancreate; canupdate = $b.canupdate })
                return $null
            }
            '^AddSolutionComponent$' { $s.solutionComponents = @(@($s.solutionComponents) + $b.ComponentId); return $null }
            '^PublishXml$' { return $null }
        }
        throw "Unplanned POST: $d"
    }
    if ($Method -ne 'Get') { throw "Unplanned $Method : $d" }

    # ── reads ──
    if ($d -match '^organizations\?') { return @{ value = @(@{ name = 'fake-org' }) } }
    if ($d -match "^EntityDefinitions\(LogicalName='([^']+)'\)/Attributes\(LogicalName='([^']+)'\)(\?.*)?$") {
        $key = "$($Matches[1]).$($Matches[2])"
        if (-not $s.attrs.ContainsKey($key)) { throw "The remote server returned an error: (404) Not Found. ($key)" }
        $a = $s.attrs[$key]
        return [pscustomobject]@{ '@odata.type' = '#Microsoft.Dynamics.CRM.StringAttributeMetadata'; LogicalName = $Matches[2]; IsSecured = [bool]$a.IsSecured
            CanBeSecuredForCreate = $a.CanBeSecuredForCreate -ne $false; CanBeSecuredForRead = $a.CanBeSecuredForRead -ne $false; CanBeSecuredForUpdate = $a.CanBeSecuredForUpdate -ne $false }
    }
    if ($d -match "^EntityDefinitions\(LogicalName='([^']+)'\)\?\`$select=EntitySetName$") { return @{ EntitySetName = "$($Matches[1])s" } }
    if ($d -match "^fieldsecurityprofiles\?.*\`$filter=name eq '([^']+)'") {
        $name = $Matches[1]
        return @{ value = @($global:Profiles | Where-Object { $_.name -eq $name } | ForEach-Object { ConvertTo-CpfProfileRow $_ }) }
    }
    if ($d -match '^fieldsecurityprofiles\(([^)]+)\)/systemuserprofiles_association\?') { return @{ value = @((ConvertTo-CpfProfileRow (Get-CpfProfile $Matches[1])).systemuserprofiles_association) } }
    if ($d -match '^fieldsecurityprofiles\(([^)]+)\)/teamprofiles_association\?') { return @{ value = @((ConvertTo-CpfProfileRow (Get-CpfProfile $Matches[1])).teamprofiles_association) } }
    if ($d -match "^fieldpermissions\?.*\`$filter=_fieldsecurityprofileid_value eq ([^ ]+) and entityname eq '([^']+)' and attributelogicalname eq '([^']+)'$") {
        $p = $Matches[1]; $t = $Matches[2]; $c = $Matches[3]
        return @{ value = @($global:Perms | Where-Object { $_.profileid -eq $p -and $_.entityname -eq $t -and $_.attributelogicalname -eq $c }) }
    }
    if ($d -match '^fieldpermissions\?.*\$filter=_fieldsecurityprofileid_value eq ([^ ]+)$') { $p = $Matches[1]; return @{ value = @($global:Perms | Where-Object { $_.profileid -eq $p }) } }
    if ($d -match "^fieldpermissions\?.*\`$expand=fieldsecurityprofileid.*\`$filter=entityname eq '([^']+)' and attributelogicalname eq '([^']+)'$") {
        $t = $Matches[1]; $c = $Matches[2]
        return @{ value = @($global:Perms | Where-Object { $_.entityname -eq $t -and $_.attributelogicalname -eq $c } | ForEach-Object {
                    @{ canread = $_.canread; cancreate = $_.cancreate; canupdate = $_.canupdate; fieldsecurityprofileid = @{ name = (Get-CpfProfile $_.profileid).name } } }) }
    }
    if ($d -match '^teams\?.*isdefault eq true') { return @{ value = @($s.defaultTeams | ForEach-Object { @{ teamid = $_.teamid; name = $_.name; businessunitid = @{ name = $_.bu } } }) } }
    if ($d -match '^systemusers\?.*applicationid eq ([0-9a-fA-F-]+)$') { $a = $Matches[1]; return @{ value = @($s.users | Where-Object { $_.applicationid -eq $a }) } }
    if ($d -match '^systemusers\(([^)]+)\)/teammembership_association\?') { $u = Get-CpfUser $Matches[1]; return @{ value = @(@($u.teams) | Where-Object { $_ } | ForEach-Object { @{ teamid = $_ } }) } }
    if ($d -match "^roles\?.*name eq 'System Administrator'") { return @{ value = @(@{ roleid = 'role-sa' }) } }
    if ($d -match '^roles\(role-sa\)/systemuserroles_association\?') { return @{ value = @($s.sysAdmins | ForEach-Object { $u = Get-CpfUser $_; @{ systemuserid = $_; fullname = $u.fullname; applicationid = $u.applicationid; isdisabled = $false } }) } }
    if ($d -match "^solutions\?.*uniquename eq '([^']+)'") { return @{ value = @(@{ solutionid = '11111111-1111-1111-1111-111111111111' }) } }
    if ($d -match '^solutioncomponents\?') { return @{ value = @($s.solutionComponents | ForEach-Object { @{ objectid = $_; componenttype = 70; rootcomponentbehavior = 0 } }) } }
    if ($d -match '^(sprk_signals|sprk_decisionrecords)\(([^)]+)\)\?') {
        $id = $Matches[2]
        if ($caller -and $s.masked[$caller]) { return @{ '_sprk_corerecordtype_value' = $null; sprk_corerecordid = $null } }
        return @{ '_sprk_corerecordtype_value' = 'type-1'; sprk_corerecordid = "core-of-$id" }
    }
    if ($d -match '^(sprk_signals|sprk_decisionrecords)\?(.*)$') {
        $table = Get-CpfTable $Matches[1]; $q = $Matches[2]
        $r = if ($s.rows.ContainsKey($table)) { $s.rows[$table] } else { @{ both = @(); typeOnly = 0; idOnly = 0 } }
        if ($q -match '\$top=1$') { return @{ value = @(@{ "$($table)id" = 'any' }) } }
        if ($q -match 'value ne null and sprk_corerecordid ne null') { return @{ value = @(@($r.both) | ForEach-Object { @{ "$($table)id" = $_ } }) } }
        $n = if ($q -match 'value ne null and sprk_corerecordid eq null') { [int]$r.typeOnly } else { [int]$r.idOnly }
        return @{ value = @(1..$n | Where-Object { $n -gt 0 } | ForEach-Object { @{ "$($table)id" = "r$_" } }) }
    }
    throw "Unplanned request: $Method $d"
}
