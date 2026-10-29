# Test double of the Dataverse Web API for scripts/Apply-DoLanePolicies.ps1 (ontology task 171, D-197). Dot-sourced by
# Apply-DoLanePolicies.Tests.ps1 inside a CHILD pwsh, where it stands in for `az` and Invoke-RestMethod, so the whole script runs
# (exit codes included) with no environment and no network. Never run against Dataverse; there is nothing here to connect with.
#
# State lives in $global:FakeDb (a hashtable loaded from the scenario file); every request is appended to $global:FakeLogPath as one
# JSON line {Method, Uri, Body}. The routes are exactly the ones the script uses; any other request throws, so a new, unplanned call
# fails a test instead of passing silently.

function Get-FakeFilterValue([string]$Filter, [string]$Name) {
    # The value of "<Name> eq <value>" in an OData filter: 'quoted' ('' for an apostrophe) or bare.
    $m = [regex]::Match($Filter, "(?<!\w)$([regex]::Escape($Name)) eq ('(?<q>(?:[^']|'')*)'|(?<b>[^\s']+))")
    if (-not $m.Success) { return $null }
    if ($m.Groups['q'].Success) { $m.Groups['q'].Value.Replace("''", "'") } else { $m.Groups['b'].Value }
}

function Merge-FakeBody([hashtable]$Body, [hashtable]$Row) {
    # A POST/PATCH body into a row as the Web API would read it back: `X@odata.bind: /set(id)` becomes `_x_value: id`.
    foreach ($k in @($Body.Keys)) {
        if ($k -like '*@odata.bind') {
            $nav = $k.Substring(0, $k.IndexOf('@')).ToLowerInvariant()
            $Row["_$($nav)_value"] = [regex]::Match([string]$Body[$k], '\(([^)]+)\)$').Groups[1].Value
        }
        else { $Row[$k] = $Body[$k] }
    }
}

function Select-FakeRows($Rows, [hashtable]$Equals, [int]$Top = 0) {
    $out = @($Rows | Where-Object {
            $row = $_
            $ok = $true
            foreach ($k in $Equals.Keys) { if ([string]$row[$k] -ne [string]$Equals[$k]) { $ok = $false } }
            $ok
        })
    if ($Top -gt 0) { $out = @($out | Select-Object -First $Top) }
    , $out
}

function Invoke-FakeDataverse([string]$Method, [string]$Uri, $Body) {
    $decoded = [uri]::UnescapeDataString($Uri)
    # -WhatIf:$false: the script's -WhatIf preference reaches this function, and the log must still be written.
    Add-Content -LiteralPath $global:FakeLogPath -WhatIf:$false -Value (@{ Method = $Method; Uri = $decoded; Body = $Body } | ConvertTo-Json -Compress -Depth 8)
    $db = $global:FakeDb
    $route = $decoded -replace '^https://[^/]+/api/data/v9\.2/', ''
    $query = ''
    if ($route.Contains('?')) { $query = $route.Substring($route.IndexOf('?') + 1); $route = $route.Substring(0, $route.IndexOf('?')) }
    $filter = [regex]::Match($query, '\$filter=(.*?)(?:&\$|$)').Groups[1].Value
    $top = [regex]::Match($query, '\$top=(\d+)').Groups[1].Value
    $topN = if ($top) { [int]$top } else { 0 }

    if ($Method -eq 'GET') {
        if ($route -eq 'WhoAmI') { return @{ UserId = $db.userId } }
        if ($route -match "^EntityDefinitions\(LogicalName='\w+'\)/Attributes\(LogicalName='(\w+)'\)/") {
            $column = $Matches[1]
            $options = @($db.options[$column].GetEnumerator() | ForEach-Object { @{ Value = $_.Value; Label = @{ UserLocalizedLabel = @{ Label = $_.Key } } } })
            return @{ OptionSet = @{ Options = $options } }
        }
        if ($route -eq 'sprk_policies') { return @{ value = (Select-FakeRows $db.policies @{ sprk_policyid = (Get-FakeFilterValue $filter 'sprk_policyid') }) } }
        if ($route -eq 'sprk_policyversions') { return @{ value = (Select-FakeRows $db.versions @{ sprk_policyversionid = (Get-FakeFilterValue $filter 'sprk_policyversionid') }) } }
        if ($route -eq 'businessunits') {
            $id = Get-FakeFilterValue $filter 'businessunitid'
            $eq = if ($id) { @{ businessunitid = $id } } else { @{ name = (Get-FakeFilterValue $filter 'name') } }
            return @{ value = (Select-FakeRows $db.businessUnits $eq $topN) }
        }
        if ($route -eq 'teams') {
            $eq = @{
                name                  = (Get-FakeFilterValue $filter 'name')
                _businessunitid_value = (Get-FakeFilterValue $filter '_businessunitid_value')
                teamtype              = (Get-FakeFilterValue $filter 'teamtype')
                isdefault             = (Get-FakeFilterValue $filter 'isdefault')
            }
            return @{ value = (Select-FakeRows $db.teams $eq $topN) }
        }
        if ($route -eq 'teammemberships') { return @{ value = (Select-FakeRows $db.memberships @{ teamid = (Get-FakeFilterValue $filter 'teamid') } $topN) } }
        if ($route -eq 'privileges') { return @{ value = (Select-FakeRows $db.privileges @{ name = (Get-FakeFilterValue $filter 'name') } $topN) } }
        if ($route -match '^systemusers\([^)]+\)/Microsoft\.Dynamics\.CRM\.RetrieveUserPrivileges\(\)$') {
            return @{ RolePrivileges = @($db.userPrivilegeIds | ForEach-Object { @{ PrivilegeId = $_; Depth = 3 } }) }
        }
    }
    elseif ($Method -eq 'POST' -and $route -in 'sprk_policies', 'sprk_policyversions') {
        $row = @{ statecode = 0; _ownerid_value = $db.userId }   # no ownerid in the body: the creating user owns the row
        Merge-FakeBody ($Body | ConvertFrom-Json -AsHashtable -Depth 32) $row
        $list = if ($route -eq 'sprk_policies') { 'policies' } else { 'versions' }
        $db[$list] = @($db[$list]) + @($row)
        return $null
    }
    elseif ($Method -eq 'PATCH' -and $route -match '^(sprk_policies|sprk_policyversions)\(([^)]+)\)$') {
        $list = if ($Matches[1] -eq 'sprk_policies') { 'policies' } else { 'versions' }
        $idColumn = if ($list -eq 'policies') { 'sprk_policyid' } else { 'sprk_policyversionid' }
        $row = @($db[$list] | Where-Object { [string]$_[$idColumn] -eq $Matches[2] }) | Select-Object -First 1
        if (-not $row) { throw "FAKE: PATCH of an absent row $route" }
        Merge-FakeBody ($Body | ConvertFrom-Json -AsHashtable -Depth 32) $row
        return $null
    }
    throw "FAKE: unhandled request $Method $decoded"
}

# The stand-ins the script reaches for by name.
function az { 'fake-token' }
function Invoke-RestMethod {
    param($Method, $Uri, $Headers, $Body)
    Invoke-FakeDataverse $Method $Uri $Body
}
