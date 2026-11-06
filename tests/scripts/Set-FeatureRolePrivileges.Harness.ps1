# Test double of the Dataverse Web API for scripts/Set-FeatureRolePrivileges.ps1 (task 046b). Dot-sourced by
# Set-FeatureRolePrivileges.Tests.ps1 inside a CHILD pwsh, where it stands in for `az`, Invoke-RestMethod and Start-Sleep, so the whole
# script runs (exit codes included) with no environment, no token and no network. Never run against Dataverse; there is nothing here to
# connect with.
#
# The scenario is a JSON file named by $env:FRP_SCENARIO:
#   orgName, rootBu            the organization and its one root business unit
#   roles   [{id,name,bu,root}] every role row (root = the root role's id; a root role has root = id)
#   held    {roleId:{privId:depth}}   what each role holds
#   privs   {table:[{Name,PrivilegeId}]}   table metadata
#   can     {privId:{canbebasic,canbelocal,canbedeep,canbeglobal}}   the depths each privilege allows (default: all)
#   failPostOn  roleId         the AddPrivilegesRole POST on that role fails
#   noPropagate [roleId]       copies that never receive the root role's change (the read-back never settles)
#   failReadOf  roleId         a privilege read of that role fails once any POST was made (the read-back cannot be completed)
#   failRemoveOn roleId        the RemovePrivilegeRole POST on that role fails
#   removeAlsoDrops [privId]   RemovePrivilegeRole also drops these privileges from the root role (a platform that removes more than asked)
#   removeAlsoSets {privId:depth}   RemovePrivilegeRole also adds, or sets the depth of, these privileges on the root role
# Every request is appended to $env:FRP_LOG as one JSON line {Method, Uri}. Any route the script does not use throws, so a new,
# unplanned call fails a test instead of passing silently.

$global:Scn = Get-Content -LiteralPath $env:FRP_SCENARIO -Raw | ConvertFrom-Json -AsHashtable
$global:PostMade = $false
$global:Held = @{}
foreach ($rid in $global:Scn.held.Keys) { $global:Held[$rid] = @{} + $global:Scn.held[$rid] }

function az { 'fake-token' }
function Start-Sleep { param([int]$Seconds, [int]$Milliseconds) }   # the settle wait costs no time here

function Write-FrpLog([string]$Method, [string]$Uri, [string]$Body = '') {
    (@{ Method = $Method; Uri = $Uri; Body = $Body } | ConvertTo-Json -Compress) | Add-Content -LiteralPath $env:FRP_LOG -Encoding UTF8
}

function Invoke-RestMethod {
    param([string]$Uri, $Headers, [string]$Method = 'Get', $Body)
    Write-FrpLog $Method $Uri $(if ($Body) { [Text.Encoding]::UTF8.GetString($Body) } else { '' })
    $rel = $Uri -replace '^https://[^/]+/api/data/v9\.2/', ''
    $decoded = [uri]::UnescapeDataString($rel)
    $s = $global:Scn

    if ($Method -eq 'Post' -and $decoded -match '^roles\(([^)]+)\)/Microsoft\.Dynamics\.CRM\.RemovePrivilegeRole$') {
        $rid = $Matches[1]
        if ($s.failRemoveOn -eq $rid) { throw 'The remote server returned an error: (500) Internal Server Error.' }
        $b = [Text.Encoding]::UTF8.GetString($Body) | ConvertFrom-Json
        $drop = @(@($b.Privilege.privilegeid) + @($s.removeAlsoDrops) | Where-Object { $_ })
        foreach ($id in $drop) {
            $global:Held[$rid].Remove($id)
            foreach ($copy in @($s.roles | Where-Object { $_.root -eq $rid -and $_.id -ne $rid })) {
                if (@($s.noPropagate) -notcontains $copy.id) { $global:Held[$copy.id].Remove($id) }
            }
        }
        # A platform that also sets (adds, or changes the depth of) privileges it was not asked to.
        foreach ($id in @($s.removeAlsoSets.Keys | Where-Object { $_ })) { $global:Held[$rid][$id] = [string]$s.removeAlsoSets[$id] }
        $global:PostMade = $true
        return $null
    }
    if ($Method -eq 'Post') {
        if ($decoded -notmatch '^roles\(([^)]+)\)/Microsoft\.Dynamics\.CRM\.AddPrivilegesRole$') { throw "Unplanned POST: $decoded" }
        $rid = $Matches[1]
        if ($s.failPostOn -eq $rid) { throw 'The remote server returned an error: (500) Internal Server Error.' }
        $b = [Text.Encoding]::UTF8.GetString($Body) | ConvertFrom-Json
        foreach ($p in @($b.Privileges)) {
            $global:Held[$rid][$p.PrivilegeId] = [string]$p.Depth
            foreach ($copy in @($s.roles | Where-Object { $_.root -eq $rid -and $_.id -ne $rid })) {
                if (@($s.noPropagate) -notcontains $copy.id) { $global:Held[$copy.id][$p.PrivilegeId] = [string]$p.Depth }
            }
        }
        $global:PostMade = $true
        return $null
    }

    if ($decoded -match '^organizations\?') { return @{ value = @(@{ name = $s.orgName }) } }
    if ($decoded -match '^businessunits\?') { return @{ value = @(@{ businessunitid = $s.rootBu; name = 'root' }) } }
    if ($decoded -match "^roles\?.*\`$filter=name eq '(.*)'$") {
        $name = $Matches[1].Replace("''", "'")
        $rows = @($s.roles | Where-Object { $_.name -eq $name } | ForEach-Object {
                @{ roleid = $_.id; name = $_.name; _businessunitid_value = $_.bu; _parentrootroleid_value = $_.root }
            })
        return @{ value = $rows }
    }
    if ($decoded -match '^RetrieveRolePrivilegesRole\(RoleId=@p\)\?@p=(.+)$') {
        $rid = $Matches[1]
        if ($global:PostMade -and $s.failReadOf -eq $rid) { throw 'Unable to connect to the remote server' }
        # PrivilegeId is a [guid] in the real response; the fake ids are plain strings, so return them as-is.
        $rows = @($global:Held[$rid].Keys | ForEach-Object { [pscustomobject]@{ PrivilegeId = $_; Depth = $global:Held[$rid][$_]; PrivilegeName = $_ } })
        return @{ RolePrivileges = $rows }
    }
    if ($decoded -match "^EntityDefinitions\(LogicalName='([^']+)'\)/Privileges$") {
        $t = $Matches[1]
        if (-not $s.privs.ContainsKey($t)) { throw "The remote server returned an error: (404) Not Found. ($t)" }
        return @{ value = @($s.privs[$t] | ForEach-Object { [pscustomobject]@{ Name = $_.Name; PrivilegeId = $_.PrivilegeId } }) }
    }
    if ($decoded -match '^privileges\(([^)]+)\)\?') {
        $id = $Matches[1]
        $all = @{ canbebasic = $true; canbelocal = $true; canbedeep = $true; canbeglobal = $true }
        if ($s.can -and $s.can.ContainsKey($id)) { foreach ($k in $s.can[$id].Keys) { $all[$k] = $s.can[$id][$k] } }
        return [pscustomobject]@{ name = $id; canbebasic = $all.canbebasic; canbelocal = $all.canbelocal; canbedeep = $all.canbedeep; canbeglobal = $all.canbeglobal }
    }
    throw "Unplanned request: $Method $decoded"
}
