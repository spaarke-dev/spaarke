# scripts/common/OntologyWriterUnit.ps1
# ---------------------------------------------------------------------------
# Reusable helper: keep the ontology writer's business unit OUT of every "all default teams" field-security profile.
#
# Why this exists (customer-provisioning-orchestration-r1 task 267, #2618):
#   The ontology writer's application user sits ALONE in its own business unit "Spaarke Ontology Writer" (a direct child
#   of the root). That unit's DEFAULT TEAM must be in NO field-security profile: a user inherits its unit's default team's
#   profiles, so a reader profile on that team would give the writer Read on secured columns it has no business with.
#   H7b (SecureRecordSetupProcedure, procedure version 8) skips the team in S11/S16, refuses it in any profile at S24
#   (secure_setup.ontology_writer_holds_other_profile, QuarantineRequired) and fails verify when it is in a reader profile.
#   The operator scripts that add "every default team" to a reader profile follow the same rule through this file:
#     - the apply loop never adds that team;
#     - "every default team is on the reader profile" does not demand it;
#     - the team found ON a reader profile is a FAIL in every mode (reported, never removed: removing it is an operator step).
#   The unit's name is SecureRecordSetupProcedure.OntologyWriterBusinessUnitName (pinned by OntologyWriterUnitScriptGuardTests).
#
# Usage:
#   . (Join-Path $PSScriptRoot 'common/OntologyWriterUnit.ps1')
#   $split = Split-DvDefaultTeamsByOntologyWriterUnit -Api $Api -Headers $headers `
#       -DefaultTeams @((Invoke-DvGet 'teams?$select=teamid,name,_businessunitid_value&$filter=isdefault eq true').value)
#   $split.ReaderTeams       -> the default teams that belong on a reader profile (every one but the writer unit's)
#   $split.WriterUnitTeams   -> the writer unit's default team(s): must NOT be on any profile
#   Get-DvOntologyWriterUnitTeamsOnProfile -Api $Api -Headers $headers -ProfileId $readerId -WriterUnitTeams $split.WriterUnitTeams
#     -> the writer unit's teams that ARE on the profile (each one is a FAIL)
# Read-only. A failed read throws; two business units with the writer unit's name throw (which one is the writer's is not
# guessable, and the check must not pass on a guess).
# ---------------------------------------------------------------------------

# No Set-StrictMode here: this file is dot-sourced, so it would change the CALLING script's mode.

$OntologyWriterBusinessUnitName = 'Spaarke Ontology Writer'

function Get-DvOntologyWriterBusinessUnitId {
    param(
        [Parameter(Mandatory)][string]$Api,
        [Parameter(Mandatory)][hashtable]$Headers
    )

    $filter = "name eq '$($OntologyWriterBusinessUnitName.Replace("'", "''"))'"
    $units = @((Invoke-RestMethod -Uri "$Api/businessunits?`$select=businessunitid,name&`$filter=$filter" -Headers $Headers -Method Get).value)
    if ($units.Count -gt 1) {
        throw "Found $($units.Count) business units named '$OntologyWriterBusinessUnitName' ($(($units | ForEach-Object { $_.businessunitid }) -join ', ')). Exactly one may exist: it holds the ontology writer alone. Resolve the duplicate by hand before running this script; nothing was changed."
    }
    if ($units.Count -eq 0) { return $null }
    return $units[0].businessunitid.ToString().ToLowerInvariant()
}

function Split-DvDefaultTeamsByOntologyWriterUnit {
    param(
        [Parameter(Mandatory)][string]$Api,
        [Parameter(Mandatory)][hashtable]$Headers,
        # Default teams as the Web API returns them; each must carry _businessunitid_value (select it).
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$DefaultTeams
    )

    $writerUnitId = Get-DvOntologyWriterBusinessUnitId -Api $Api -Headers $Headers
    $readerTeams = [System.Collections.Generic.List[object]]::new()
    $writerUnitTeams = [System.Collections.Generic.List[object]]::new()
    foreach ($t in $DefaultTeams) {
        # A hashtable (as the tests' fakes answer) or the PSCustomObject Invoke-RestMethod returns.
        $unitId = if ($t -is [System.Collections.IDictionary]) { $t['_businessunitid_value'] } elseif ($t.PSObject.Properties['_businessunitid_value']) { $t._businessunitid_value } else { $null }
        if (-not $unitId) { throw "A default team ('$($t.name)', $($t.teamid)) was read without _businessunitid_value; the query must select it." }
        if ($writerUnitId -and $unitId.ToString().ToLowerInvariant() -eq $writerUnitId) { $writerUnitTeams.Add($t) } else { $readerTeams.Add($t) }
    }

    [pscustomobject]@{ WriterBusinessUnitId = $writerUnitId; ReaderTeams = $readerTeams.ToArray(); WriterUnitTeams = $writerUnitTeams.ToArray() }
}

function Get-DvOntologyWriterUnitTeamsOnProfile {
    param(
        [Parameter(Mandatory)][string]$Api,
        [Parameter(Mandatory)][hashtable]$Headers,
        # The profile's id; $null (the profile does not exist yet) answers nothing.
        [AllowNull()][AllowEmptyString()]$ProfileId,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$WriterUnitTeams
    )

    if (-not $ProfileId -or $WriterUnitTeams.Count -eq 0) { return @() }
    $onProfile = @((Invoke-RestMethod -Uri "$Api/fieldsecurityprofiles($ProfileId)/teamprofiles_association?`$select=teamid" -Headers $Headers -Method Get).value |
        ForEach-Object { $_.teamid.ToString().ToLowerInvariant() })
    return @($WriterUnitTeams | Where-Object { $onProfile -contains $_.teamid.ToString().ToLowerInvariant() })
}
