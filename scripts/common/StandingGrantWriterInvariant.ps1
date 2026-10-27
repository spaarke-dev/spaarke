<#
.SYNOPSIS
    The standing-grant writer invariant for the BFF application user(s), read-only: the queries -Verify runs and the findings
    it reports. Dot-sourced by scripts/Set-ExternalAccessContactSecuritySchema.ps1 (-Verify, step h).

.DESCRIPTION
    THE RULE. The BFF application user may update contact.sprk_standinggrant ONLY through its DIRECT membership of the
    Standing Grant Administrators field security profile, and no other profile it holds (directly or through a team) grants
    create or update on that column. The administrator removal clears the standing grant, so the BFF needs that update; any
    other route would widen who can set a standing grant and is not covered by provisioning's S19 other-writer check.

    Read-only. The membership is ADDED by a provisioning S-step (one owner); nothing here writes it. The team leg is ONE
    server-side join (teamprofiles -> teammembership, nested any()), never a client-side team list.

    Kept apart from the script so the findings are exercised offline, with no Dataverse:
    tests/integration/auth/UnifiedAccessControl/StandingGrantWriterInvariantScriptTests.cs runs these functions in pwsh.

    CLAUDE.md §11. Existing: -Verify reports Standing Grant Administrators' member counts only, and S19 checks which
    PROFILES may write the column, not which of them the BFF holds. Extension: this is a step of that -Verify, over the same
    pinned names. Cost of doing nothing: a BFF that could update the standing grant through a team or another profile would
    pass every check, and its route would not be the one provisioning manages.
#>

# The three read-only queries, for one BFF application user. The team leg is the server-side join (nested any()).
function Get-StandingGrantWriterQuery {
    param(
        [Parameter(Mandatory)][string]$SystemUserId,
        [Parameter(Mandatory)][string]$Table,
        [Parameter(Mandatory)][string]$Column
    )
    $sid = ([guid]$SystemUserId).ToString('D')
    @{
        DirectProfiles = "fieldsecurityprofiles?`$select=fieldsecurityprofileid,name&`$filter=systemuserprofiles_association/any(u:u/systemuserid eq $sid)"
        TeamProfiles   = "fieldsecurityprofiles?`$select=fieldsecurityprofileid,name&`$filter=teamprofiles_association/any(t:t/teammembership_association/any(u:u/systemuserid eq $sid))"
        Writers        = "fieldpermissions?`$select=canread,cancreate,canupdate,_fieldsecurityprofileid_value&`$filter=entityname eq '$Table' and attributelogicalname eq '$Column' and (cancreate eq 4 or canupdate eq 4)"
    }
}

# The findings for one BFF application user: one @{ State = 'OK' | 'FAIL'; What = '...' } per check.
#   DirectProfiles / TeamProfiles: rows with fieldsecurityprofileid and name (the two leg queries' answers).
#   WriterPermissions: rows with _fieldsecurityprofileid_value, cancreate, canupdate (the Writers query's answer).
function Get-StandingGrantWriterFinding {
    param(
        [Parameter(Mandatory)][string]$UserName,
        [Parameter(Mandatory)][string]$StandingGrantProfileId,
        [Parameter(Mandatory)][string]$StandingGrantProfileName,
        [Parameter(Mandatory)][string]$Column,
        [object[]]$DirectProfiles = @(),
        [object[]]$TeamProfiles = @(),
        [object[]]$WriterPermissions = @()
    )
    $sga = ([guid]$StandingGrantProfileId).ToString('D')
    $id = { param($v) ([guid]"$v").ToString('D') }
    $direct = @{}; foreach ($p in $DirectProfiles) { if ($p) { $direct[(& $id $p.fieldsecurityprofileid)] = $p.name } }
    $team = @{}; foreach ($p in $TeamProfiles) { if ($p) { $team[(& $id $p.fieldsecurityprofileid)] = $p.name } }
    $findings = [System.Collections.Generic.List[object]]::new()

    $sgaUpdates = @($WriterPermissions | Where-Object { $_ -and (& $id $_._fieldsecurityprofileid_value) -eq $sga -and $_.canupdate -eq 4 }).Count -gt 0
    if (-not $sgaUpdates) {
        $findings.Add(@{ State = 'FAIL'; What = "'$StandingGrantProfileName' grants no update on $Column, so $UserName cannot clear a standing grant (SpaarkeMaster ships that permission; provisioning's S19 verifies it)" })
    }

    if ($direct.ContainsKey($sga)) {
        $findings.Add(@{ State = 'OK'; What = "$UserName is a DIRECT member of '$StandingGrantProfileName'" })
    } else {
        $findings.Add(@{ State = 'FAIL'; What = "$UserName is not a DIRECT member of '$StandingGrantProfileName' — the person removal cannot clear a standing grant. A provisioning step adds this membership; this script never writes it" })
    }

    $others = 0
    if ($team.ContainsKey($sga)) {
        $others++
        $findings.Add(@{ State = 'FAIL'; What = "$UserName holds '$StandingGrantProfileName' through a team — only its direct membership may let it update $Column" })
    }

    foreach ($w in $WriterPermissions) {
        if (-not $w) { continue }
        $profileId = & $id $w._fieldsecurityprofileid_value
        if ($profileId -eq $sga) { continue }
        $rights = "create=$($w.cancreate) update=$($w.canupdate)"
        if ($direct.ContainsKey($profileId)) {
            $others++
            $findings.Add(@{ State = 'FAIL'; What = "$UserName holds profile '$($direct[$profileId])' ($profileId) directly, and it grants $Column ($rights) — only '$StandingGrantProfileName' may" })
        }
        if ($team.ContainsKey($profileId)) {
            $others++
            $findings.Add(@{ State = 'FAIL'; What = "$UserName holds profile '$($team[$profileId])' ($profileId) through a team, and it grants $Column ($rights) — only '$StandingGrantProfileName' may" })
        }
    }

    if ($others -eq 0) {
        $findings.Add(@{ State = 'OK'; What = "no other profile $UserName holds (directly or through a team) grants create or update on $Column" })
    }

    , $findings.ToArray()
}
