<#
.SYNOPSIS
    Seeds the eBilling system policy BILL-SYSTEM-SUBMISSION (sprk_policy + one sprk_policyversion) of ebilling-r1 task 049 (D-123, D-125).

.DESCRIPTION
    The policy backs the findings the submission job creates directly: the D-91 invoice-number mismatch and the 047 coding findings
    (refused code, firm-code mismatch, code set needs review). Its rule type is SubmissionIntegrity (sprk_ruletype 100000014, added by
    scripts/ebilling/Deploy-EbillingSubmissionIntegrityOption.ps1, which must run first). The type is never authorable and never evaluated
    from a body: the body is the strict empty object {}, the consequence is Flag (the only one the type allows).

    Pattern: scripts/Apply-DoLanePolicies.ps1. IDEMPOTENT by sprk_policycode:
      policy absent        -> POST (sprk_enabled = Yes, domain Billing, lane Decide, subject type Invoice, severity Info, no rule set, no matter)
      policy present       -> descriptive fields are PATCHed when they differ. sprk_enabled is NEVER written on an existing row: an
                              administrator may turn the policy off, and a re-run must not turn it back on.
      version 1 absent     -> POST, in force from a FIXED date (2026-10-19) with no end date
      version 1 present    -> NEVER written (a published version is immutable, FR-49). A difference is reported as DRIFT and fails.
      sprk_currentversion  -> PATCHed to version 1 once it exists.
    Every write carries MSCRM.SolutionUniqueName (the policy and version are solution components of SpaarkeMaster). After a write the rows are
    read back and compared; any difference fails the script.

    OWNER (D-197, optional -CustomerBusinessUnit): the policy and the version this script CREATES belong to the customer top business
    unit's restricted-content owner team instead of the operator who runs it. The ownership code is copied from
    scripts/Apply-DoLanePolicies.ps1 (ontology task 171) and behaves the same; without the parameter the script behaves exactly as before.

    Dev only, and only with the owner's OK; task 053 adds it to the SpaarkeMaster solution. Run -WhatIf first; -Verify never writes.
    This file is written, never run, by the agent that authored it.

.PARAMETER DataverseUrl
    Target environment, e.g. https://spaarkedev1.crm.dynamics.com. Defaults to $env:DATAVERSE_URL. Required.

.PARAMETER SolutionUniqueName
    Unmanaged solution the rows are created in (MSCRM.SolutionUniqueName header). Default SpaarkeMaster.

.PARAMETER Verify
    READ-ONLY. Reports the policy and version as ABSENT, IN SYNC or DRIFT. Exit 1 on DRIFT; ABSENT is exit 0 unless -RequireRows is given.

.PARAMETER RequireRows
    With -Verify: treat an ABSENT policy or version as a failure (use after the apply).

.PARAMETER CustomerBusinessUnit
    OPTIONAL. The customer's top business unit (the root's direct child that provisioning H10 creates), by id or by name. D-197
    (projects/spaarke-ontology-platform-r1/notes/decisions.md); ebilling-r1 task 049 item 4. With it, the sprk_policy and sprk_policyversion
    rows the script CREATES are owned by that unit's restricted-content owner team (`ownerid@odata.bind` on the two creates) instead of by
    the operator. The team is found by the SAME rule as Infrastructure/Dataverse/RestrictedContentOwnerTeam.cs (see
    Resolve-RestrictedContentOwner): a non-default Owner team named 'Spaarke Restricted Content Owners', exactly one in that unit, with no
    member. Existing rows are never re-owned; -Verify reports each existing row owned by anyone else as DRIFT (and writes nothing).
    -WhatIf names the owner it would bind. FAIL CLOSED, before any write (non-zero exit): the unit is the root, is not the root's direct
    child, is the Secure Record unit, cannot be found or is ambiguous, or its team is missing, ambiguous or has a member; and (apply and
    -WhatIf only) the operator does not hold Assign on sprk_policy and sprk_policyversion. A parameter that is passed but blank is refused.

.EXAMPLE
    pwsh scripts/ebilling/Seed-BillingSystemPolicy.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com -WhatIf
    pwsh scripts/ebilling/Seed-BillingSystemPolicy.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com
    pwsh scripts/ebilling/Seed-BillingSystemPolicy.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com -Verify -RequireRows
    pwsh scripts/ebilling/Seed-BillingSystemPolicy.ps1 -DataverseUrl https://<customer>.crm.dynamics.com -CustomerBusinessUnit <top business unit id or name> -WhatIf
#>
#Requires -Version 7.0
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$DataverseUrl = $env:DATAVERSE_URL,
    [string]$SolutionUniqueName = 'SpaarkeMaster',
    [switch]$Verify,
    [switch]$RequireRows,
    [string]$CustomerBusinessUnit
)

$ErrorActionPreference = 'Stop'

if (-not $DataverseUrl) { throw 'DataverseUrl is required (parameter or $env:DATAVERSE_URL).' }
$DataverseUrl = $DataverseUrl.TrimEnd('/')
# D-197 (as in Apply-DoLanePolicies.ps1, task 171): the parameter is judged by whether it was PASSED, not by its value. `-CustomerBusinessUnit
# $env:X` with X unset binds an empty string; treating that as "not passed" would seed operator-owned rows and report success. Refused first.
if ($PSBoundParameters.ContainsKey('CustomerBusinessUnit') -and [string]::IsNullOrWhiteSpace($CustomerBusinessUnit)) {
    throw '-CustomerBusinessUnit was passed but is empty or blank (is the variable it came from unset?). Pass the customer top business unit id or name, or leave the parameter out. Nothing was written.'
}
$ApiBase = "$DataverseUrl/api/data/v9.2"

# ---- The definition (the system policy is one fixed row; its text mirrors BillingRuleCatalogue / BillingFinding) ----------------------
$PolicyCode = 'BILL-SYSTEM-SUBMISSION'        # BillingRuleCatalogue.SystemPolicyCode
$PolicyId = '04900125-0000-4000-8000-000000000001'
$VersionId = '04900125-0000-4000-8000-000000000002'
$RuleType = 'SubmissionIntegrity'             # RuleType.SubmissionIntegrity = 100000014
$ConsequenceFlag = 100000001                  # sprk_policyversion.sprk_consequence: Reject 100000000, Flag 100000001, Suggest 100000002
$DomainBilling = 100000000                    # sprk_policydomain
$InForceFrom = '2026-10-19T00:00:00Z'         # fixed: the decision date (D-125); never "now"
$Def = [ordered]@{
    Name           = 'Submission integrity (system)'
    ShortName      = 'Submission integrity'
    Lane           = 'Decide'
    SubjectType    = 'Invoice'
    Severity       = 'Info'
    Priority       = 500
    Headline       = 'Submission integrity check'
    Template       = 'A submission integrity check noted a difference.'   # token-free; each reason has its own message (BillingRuleCatalogue.ReasonTemplate)
    ProposedAction = 'Review the difference noted on the submission.'
    Body           = '{}'
}

# D-197. The restricted-content owner team rule of RestrictedContentOwnerTeam.cs, copied from scripts/Apply-DoLanePolicies.ps1 (task 171),
# which copies the C# and never redefines it. The names are that class's DEFAULTS (RestrictedContentOwnerTeam.DefaultOwnerTeamName,
# SecureRecordOwnerTeam.DefaultBusinessUnitName); the C# reads RestrictedContent:OwnerTeamName and SecureRecord:BusinessUnitName from
# configuration, which a script cannot see, so a deployment that renames either gets a refusal here (team not found) and edits these.
# The two limits are RestrictedContentOwnerTeam.MaxParentWalk and .MemberReadLimit.
$RestrictedOwnerTeamName = 'Spaarke Restricted Content Owners'
$SecureBusinessUnitName = 'Secure Record'
$MaxParentWalk = 32; $MemberReadLimit = 5
$AssignPrivileges = @('prvAssignsprk_policy', 'prvAssignsprk_policyversion')

# ---- Dataverse access ---------------------------------------------------------------------------------------------
$token = az account get-access-token --resource $DataverseUrl --query accessToken -o tsv
if (-not $token) { throw "Could not get a token for $DataverseUrl (az login?)." }
$headers = @{
    Authorization            = "Bearer $token"
    'Content-Type'           = 'application/json'
    'OData-MaxVersion'       = '4.0'
    'OData-Version'          = '4.0'
    'MSCRM.SolutionUniqueName' = $SolutionUniqueName
}

function Invoke-Dv([string]$Method, [string]$Uri, $Body) {
    try {
        if ($null -ne $Body) { Invoke-RestMethod -Method $Method -Uri $Uri -Headers $headers -Body ($Body | ConvertTo-Json -Depth 8 -Compress) }
        else { Invoke-RestMethod -Method $Method -Uri $Uri -Headers $headers }
    }
    catch {
        $detail = if ($_.ErrorDetails.Message) { $_.ErrorDetails.Message } else { $_.Exception.Message }
        throw "$Method $Uri failed: $detail"
    }
}

function Get-OptionMap([string]$Entity, [string]$Column) {
    $r = Invoke-Dv GET "$ApiBase/EntityDefinitions(LogicalName='$Entity')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=LogicalName&`$expand=OptionSet(`$select=Options)" $null
    $map = @{}
    foreach ($o in $r.OptionSet.Options) { $map[$o.Label.UserLocalizedLabel.Label] = [int]$o.Value }
    $map
}

$laneMap = Get-OptionMap 'sprk_policy' 'sprk_lane'
$subjectMap = Get-OptionMap 'sprk_policy' 'sprk_subjecttype'
$severityMap = Get-OptionMap 'sprk_policy' 'sprk_severity'
$ruleTypeMap = Get-OptionMap 'sprk_policyversion' 'sprk_ruletype'

# Refuse before any read or write when the live columns do not carry what the definition needs.
$problems = @()
if (-not $laneMap.ContainsKey($Def.Lane)) { $problems += "lane '$($Def.Lane)' is not on sprk_policy.sprk_lane" }
if (-not $subjectMap.ContainsKey($Def.SubjectType)) { $problems += "subject type '$($Def.SubjectType)' is not on sprk_policy.sprk_subjecttype" }
if (-not $severityMap.ContainsKey($Def.Severity)) { $problems += "severity '$($Def.Severity)' is not on sprk_policy.sprk_severity" }
if (-not $ruleTypeMap.ContainsKey($RuleType) -or $ruleTypeMap[$RuleType] -ne 100000014) { $problems += "rule type '$RuleType' (100000014) is not on sprk_policyversion.sprk_ruletype: run Deploy-EbillingSubmissionIntegrityOption.ps1 first" }
if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "PRECONDITION FAIL: $_" -ForegroundColor Red }
    throw "Preconditions failed. Nothing was written to $DataverseUrl."
}

function Get-PolicyRow {
    $r = Invoke-Dv GET "$ApiBase/sprk_policies?`$select=sprk_policyid,sprk_name,sprk_policycode,sprk_shortname,sprk_lane,sprk_subjecttype,sprk_severity,sprk_priority,sprk_domain,sprk_enabled,statecode,_sprk_currentversion_value,_sprk_ruleset_value$(Get-OwnerSelect)&`$filter=sprk_policycode eq '$PolicyCode'" $null
    if (@($r.value).Count -eq 0) { return $null }
    if (@($r.value).Count -gt 1) { throw "More than one sprk_policy has the code $PolicyCode; refusing to guess." }
    $r.value[0]
}

function Get-VersionRow([string]$PolicyRowId) {
    $r = Invoke-Dv GET "$ApiBase/sprk_policyversions?`$select=sprk_policyversionid,sprk_name,_sprk_policy_value,sprk_versionnumber,sprk_ruletype,sprk_rulebody,sprk_shortheadline,sprk_messagetemplate,sprk_proposedaction,sprk_consequence,sprk_inforcefrom,sprk_inforceto,statecode$(Get-OwnerSelect)&`$filter=_sprk_policy_value eq $PolicyRowId and sprk_versionnumber eq 1" $null
    if (@($r.value).Count -eq 0) { return $null }
    $r.value[0]
}

function Get-ParsedCompact([string]$Text) { try { $Text | ConvertFrom-Json -Depth 8 | ConvertTo-Json -Depth 8 -Compress } catch { $null } }

# The policy fields the script manages (sprk_enabled is deliberately not among them: an administrator may turn the policy off).
function Get-PolicyDiffs($row) {
    $expected = [ordered]@{
        sprk_name        = $Def.Name
        sprk_shortname   = $Def.ShortName
        sprk_lane        = $laneMap[$Def.Lane]
        sprk_subjecttype = $subjectMap[$Def.SubjectType]
        sprk_severity    = $severityMap[$Def.Severity]
        sprk_priority    = [int]$Def.Priority
        sprk_domain      = $DomainBilling
    }
    $diffs = [System.Collections.Generic.List[string]]::new()
    foreach ($k in $expected.Keys) { if ($row.$k -ne $expected[$k]) { $diffs.Add("$k live='$($row.$k)' expected='$($expected[$k])'") } }
    if ($null -ne $row._sprk_ruleset_value) { $diffs.Add("sprk_ruleset live='$($row._sprk_ruleset_value)' expected='(none)': the system policy belongs to no rule set") }
    $diffs
}

# A version is immutable: every comparison is a DRIFT, never a write.
function Get-VersionDiffs($row, [string]$PolicyRowId) {
    $diffs = [System.Collections.Generic.List[string]]::new()
    if ($row._sprk_policy_value -ne $PolicyRowId) { $diffs.Add('sprk_policy') }
    if ($row.sprk_ruletype -ne $ruleTypeMap[$RuleType]) { $diffs.Add('sprk_ruletype') }
    if ((Get-ParsedCompact $row.sprk_rulebody) -cne $Def.Body) { $diffs.Add('sprk_rulebody') }
    if ($row.sprk_consequence -ne $ConsequenceFlag) { $diffs.Add('sprk_consequence') }
    if ($row.sprk_messagetemplate -cne $Def.Template) { $diffs.Add('sprk_messagetemplate') }
    if ($row.sprk_shortheadline -cne $Def.Headline) { $diffs.Add('sprk_shortheadline') }
    if ($row.sprk_proposedaction -cne $Def.ProposedAction) { $diffs.Add('sprk_proposedaction') }
    if ($row.statecode -ne 0) { $diffs.Add('statecode') }
    if ($null -ne $row.sprk_inforceto) { $diffs.Add('sprk_inforceto (the version must have no end date)') }
    if ($null -eq $row.sprk_inforcefrom -or ([datetime]$row.sprk_inforcefrom).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'") -ne $InForceFrom) { $diffs.Add('sprk_inforcefrom') }
    $diffs
}

# ---- owner (D-197) ------------------------------------------------------------------------------------------------
# Copied from scripts/Apply-DoLanePolicies.ps1 (ontology task 171), unchanged in behaviour: keep the two in step. It is not dot-sourced
# from a shared helper because that script runs on load and its Pester tests read these functions out of its own AST.
# With -CustomerBusinessUnit the owner is read too (-Verify drift and the read-back of created rows); without it the reads are as before.
function Get-OwnerSelect { if ($script:OwnerTeamId) { ',_ownerid_value' } else { '' } }

# ---- owner (task 171, D-197) --------------------------------------------------------------------------------------
# The owner of a row is only judged with -CustomerBusinessUnit; without it this returns nothing.
function Get-OwnerDiff($row) {
    if ($script:OwnerTeamId -and $row._ownerid_value -ne $script:OwnerTeamId) { "ownerid live='$($row._ownerid_value)' expected='$script:OwnerTeamId'" }
}

function ConvertTo-ODataLiteral([string]$Text) { [uri]::EscapeDataString($Text.Replace("'", "''")) }

# The customer's top business unit above $BusinessUnitId: the direct child of the root it sits under (itself when it is one).
# Port of RestrictedContentOwnerTeam.CustomerTopBusinessUnitAsync, same statuses. A Dataverse fault propagates (Invoke-Dv throws).
function Get-CustomerTopBusinessUnit([string]$BusinessUnitId) {
    $visited = @{}
    $below = $null
    $current = $BusinessUnitId
    for ($step = 0; $step -le $MaxParentWalk; $step++) {
        if (-not $current -or $current -eq [guid]::Empty.ToString() -or $visited.ContainsKey($current)) { return @{ TopBusinessUnitId = $null; Status = 'ParentChainLoops' } }
        $visited[$current] = $true
        $r = Invoke-Dv GET "$ApiBase/businessunits?`$select=businessunitid,_parentbusinessunitid_value&`$filter=businessunitid eq $current&`$top=1" $null
        $row = @($r.value) | Select-Object -First 1
        if ($null -eq $row) { return @{ TopBusinessUnitId = $null; Status = 'ParentChainUnreadable' } }
        $parent = $row._parentbusinessunitid_value
        if (-not $parent -or $parent -eq [guid]::Empty.ToString()) {
            # `current` is the root: the unit below it on the walk is the customer's top unit, or there is none (the unit is the root).
            if ($below) { return @{ TopBusinessUnitId = $below; Status = 'Resolved' } }
            return @{ TopBusinessUnitId = $null; Status = 'AnswerIsRootBusinessUnit' }
        }
        $below = $current
        $current = [string]$parent
    }
    @{ TopBusinessUnitId = $null; Status = 'ParentChainLoops' }
}

# WHICH team owns the customer top business unit's restricted-content rows, and whether it may be used. A port of
# RestrictedContentOwnerTeam.IdentifyAsync (src/server/api/Sprk.Bff.Api/Infrastructure/Dataverse/RestrictedContentOwnerTeam.cs): not the
# Secure Record unit; the named, non-default Owner team (teamtype 0), exactly one, in that unit; no member of any kind. Never throws
# for an answer; a Dataverse fault propagates. Status is the C# RestrictedContentOwnerTeamStatus name, plus three the script adds
# because it is handed a unit rather than a record: CustomerBusinessUnitNotFound / CustomerBusinessUnitAmbiguous (a name that matches
# none or several) and CustomerBusinessUnitNotTopLevel (the C# would walk UP to the top unit; the script refuses a unit that is not
# already the root's direct child, so the operator names exactly the unit that owns the rows).
function New-OwnerResult($Status, $TeamId, $BusinessUnitId, $Members) {
    @{ Status = $Status; OwnerTeamId = $TeamId; CustomerBusinessUnitId = $BusinessUnitId; MembersFound = $Members }
}

function Resolve-RestrictedContentOwner([string]$BusinessUnit) {
    $parsed = [guid]::Empty
    if ([guid]::TryParse($BusinessUnit, [ref]$parsed)) { $buId = $parsed.ToString() }
    else {
        $r = Invoke-Dv GET "$ApiBase/businessunits?`$select=businessunitid&`$filter=name eq '$(ConvertTo-ODataLiteral $BusinessUnit)'&`$top=2" $null
        $found = @($r.value)
        if ($found.Count -eq 0) { return New-OwnerResult 'CustomerBusinessUnitNotFound' $null $null 0 }
        if ($found.Count -gt 1) { return New-OwnerResult 'CustomerBusinessUnitAmbiguous' $null $null 0 }
        $buId = [string]$found[0].businessunitid
    }

    $walk = Get-CustomerTopBusinessUnit $buId
    if ($walk.Status -ne 'Resolved') { return New-OwnerResult $walk.Status $null $null 0 }
    $top = [string]$walk.TopBusinessUnitId
    if ($top -ne $buId) { return New-OwnerResult 'CustomerBusinessUnitNotTopLevel' $null $top 0 }

    # The Secure Record unit, by name (SecureRecordOwnerTeam.ResolveSecureBusinessUnitAsync: TOP 2; two matches cannot be classified;
    # none means this environment has no Secure Record unit, so none is it).
    $secure = @((Invoke-Dv GET "$ApiBase/businessunits?`$select=businessunitid&`$filter=name eq '$(ConvertTo-ODataLiteral $SecureBusinessUnitName)'&`$top=2" $null).value)
    if ($secure.Count -gt 1) { return New-OwnerResult 'SecureBusinessUnitAmbiguous' $null $top 0 }
    if ($secure.Count -eq 1 -and [string]$secure[0].businessunitid -eq $top) { return New-OwnerResult 'CustomerBusinessUnitIsSecureRecord' $null $top 0 }

    $teams = @((Invoke-Dv GET "$ApiBase/teams?`$select=teamid&`$filter=name eq '$(ConvertTo-ODataLiteral $RestrictedOwnerTeamName)' and teamtype eq 0 and isdefault eq false and _businessunitid_value eq $top&`$top=2" $null).value)
    if ($teams.Count -eq 0 -or -not $teams[0].teamid -or [string]$teams[0].teamid -eq [guid]::Empty.ToString()) { return New-OwnerResult 'OwnerTeamNotFound' $null $top 0 }
    if ($teams.Count -gt 1) { return New-OwnerResult 'OwnerTeamAmbiguous' $null $top 0 }

    # No member of any kind (human or application user): the membership rows themselves.
    $teamId = [string]$teams[0].teamid
    $members = @((Invoke-Dv GET "$ApiBase/teammemberships?`$select=systemuserid&`$filter=teamid eq $teamId&`$top=$MemberReadLimit" $null).value).Count
    if ($members -gt 0) { return New-OwnerResult 'OwnerTeamHasMembers' $teamId $top $members }
    New-OwnerResult 'Resolved' $teamId $top 0
}

# The Assign privileges the operator is missing (names), by the operator's effective privileges (RetrieveUserPrivileges covers the
# roles of the user and of their teams). A privilege that does not exist on the environment counts as missing: fail closed.
# This checks PRESENCE, not DEPTH: Assign at Basic depth does not cover a team in another business unit. A depth shortfall is not
# caught here; it surfaces as a 403 on the first create, which stops the run (fail closed) before any row exists.
function Get-MissingAssignPrivileges([string]$UserId) {
    $held = @((Invoke-Dv GET "$ApiBase/systemusers($UserId)/Microsoft.Dynamics.CRM.RetrieveUserPrivileges()" $null).RolePrivileges | ForEach-Object { ([string]$_.PrivilegeId).ToLowerInvariant() })
    foreach ($name in $AssignPrivileges) {
        $priv = @((Invoke-Dv GET "$ApiBase/privileges?`$select=privilegeid,name&`$filter=name eq '$name'" $null).value) | Select-Object -First 1
        if ($null -eq $priv -or $held -notcontains ([string]$priv.privilegeid).ToLowerInvariant()) { $name }
    }
}

# The WhoAmI user (the operator), once.
$whoami = $null
function Get-AuthoredBy {
    if ($null -eq $script:whoami) { $script:whoami = (Invoke-Dv GET "$ApiBase/WhoAmI" $null).UserId }
    $script:whoami
}

# Resolved before anything is written (also in -WhatIf and -Verify). $OwnerTeamId stays $null without -CustomerBusinessUnit.
$OwnerTeamId = $null
if ($PSBoundParameters.ContainsKey('CustomerBusinessUnit')) {
    $owner = Resolve-RestrictedContentOwner $CustomerBusinessUnit
    if ($owner.Status -ne 'Resolved') {
        Write-Host "OWNER FAIL: $($owner.Status) for customer business unit '$CustomerBusinessUnit' (business unit $($owner.CustomerBusinessUnitId); members found $($owner.MembersFound))." -ForegroundColor Red
        throw "The restricted-content owner team cannot be used ($($owner.Status)). Nothing was written to $DataverseUrl."
    }
    $OwnerTeamId = $owner.OwnerTeamId
    Write-Host "Owner: created rows belong to team '$RestrictedOwnerTeamName' ($OwnerTeamId) in customer business unit $($owner.CustomerBusinessUnitId) (D-197)."
    if (-not $Verify) {
        $missing = @(Get-MissingAssignPrivileges (Get-AuthoredBy))
        if ($missing.Count -gt 0) {
            Write-Host "OWNER FAIL: the operator lacks $($missing -join ', ')." -ForegroundColor Red
            throw "The operator cannot assign the rows to the owner team (missing $($missing -join ', ')). Nothing was written to $DataverseUrl."
        }
    }
}

# ---- -Verify: read only -------------------------------------------------------------------------------------------
if ($Verify) {
    $failed = $false
    Write-Host "--- Verify (read-only) against $DataverseUrl ---"
    $row = Get-PolicyRow
    if ($null -eq $row) {
        Write-Host "$PolicyCode policy ABSENT" -ForegroundColor Yellow
        if ($RequireRows) { $failed = $true }
    }
    else {
        $d = @(Get-PolicyDiffs $row) + @(Get-OwnerDiff $row)
        if ($d.Count -eq 0) { Write-Host "$PolicyCode policy IN SYNC (enabled=$($row.sprk_enabled), statecode=$($row.statecode))" -ForegroundColor Green }
        else { Write-Host "$PolicyCode policy DRIFT: $($d -join '; ')" -ForegroundColor Red; $failed = $true }
        $vr = Get-VersionRow $row.sprk_policyid
        if ($null -eq $vr) { Write-Host "$PolicyCode v1 ABSENT" -ForegroundColor Yellow; if ($RequireRows) { $failed = $true } }
        else {
            $vd = @(Get-VersionDiffs $vr $row.sprk_policyid) + @(Get-OwnerDiff $vr)
            if ($vd.Count -eq 0) { Write-Host "$PolicyCode v1 IN SYNC" -ForegroundColor Green }
            else { Write-Host "$PolicyCode v1 DRIFT: $($vd -join '; ')" -ForegroundColor Red; $failed = $true }
        }
    }
    if ($failed) { exit 1 }
    return
}

# ---- apply --------------------------------------------------------------------------------------------------------
$created = [System.Collections.Generic.HashSet[string]]::new()
$row = Get-PolicyRow
$policyRowId = if ($null -ne $row) { $row.sprk_policyid } else { $PolicyId }

if ($null -eq $row) {
    $body = [ordered]@{
        sprk_policyid    = $PolicyId
        sprk_name        = $Def.Name
        sprk_policycode  = $PolicyCode
        sprk_shortname   = $Def.ShortName
        sprk_lane        = $laneMap[$Def.Lane]
        sprk_subjecttype = $subjectMap[$Def.SubjectType]
        sprk_severity    = $severityMap[$Def.Severity]
        sprk_priority    = [int]$Def.Priority
        sprk_domain      = $DomainBilling
        sprk_enabled     = $true
    }
    if ($OwnerTeamId) { $body['ownerid@odata.bind'] = "/teams($OwnerTeamId)" }
    $ownerNote = if ($OwnerTeamId) { ", owner = team $OwnerTeamId" } else { '' }
    if ($PSCmdlet.ShouldProcess("$DataverseUrl sprk_policy $PolicyId", "CREATE $PolicyCode (enabled, Billing, Decide, Invoice, Info, no rule set$ownerNote)")) {
        Invoke-Dv POST "$ApiBase/sprk_policies" $body | Out-Null
        [void]$created.Add([string]$PolicyId)
        Write-Host "CREATED policy $PolicyCode -> $PolicyId"
    }
}
else {
    # ontology-r1 review: never take over a row this script did not create. A policy that holds the code but is in another domain, or
    # has another id, belongs to someone else; PATCHing its domain and name would move it into Billing. Refuse before any write.
    if ([int]$row.sprk_domain -ne [int]$DomainBilling) {
        throw "$PolicyCode exists in another domain (sprk_domain $($row.sprk_domain), id $($row.sprk_policyid)); this script owns only the Billing system policy. Not touched: owner decision needed."
    }
    if ([string]$row.sprk_policyid -ne [string]$PolicyId) {
        throw "$PolicyCode exists with id $($row.sprk_policyid), not this script's $PolicyId; refusing to adopt a row it did not create. Not touched: owner decision needed."
    }
    $d = @(Get-PolicyDiffs $row | Where-Object { $_ -notlike 'sprk_ruleset*' })
    if (@(Get-PolicyDiffs $row | Where-Object { $_ -like 'sprk_ruleset*' }).Count -gt 0) {
        throw "$PolicyCode is attached to a rule set ($($row._sprk_ruleset_value)); the system policy belongs to none. Not touched: owner decision needed."
    }
    if ($d.Count -gt 0) {
        $patch = [ordered]@{
            sprk_name = $Def.Name; sprk_shortname = $Def.ShortName; sprk_lane = $laneMap[$Def.Lane]; sprk_subjecttype = $subjectMap[$Def.SubjectType]
            sprk_severity = $severityMap[$Def.Severity]; sprk_priority = [int]$Def.Priority; sprk_domain = $DomainBilling
        }
        if ($PSCmdlet.ShouldProcess("$DataverseUrl sprk_policy $policyRowId", "UPDATE ${PolicyCode}: $($d -join '; ')")) {
            Invoke-Dv PATCH "$ApiBase/sprk_policies($policyRowId)" $patch | Out-Null
            Write-Host "UPDATED policy ${PolicyCode}: $($d -join '; ')"
        }
    }
    else { Write-Host "UNCHANGED policy $PolicyCode (enabled stays $($row.sprk_enabled): the script never changes it)" }
}

$vr = if ($null -ne $row) { Get-VersionRow $policyRowId } else { $null }
if ($null -eq $vr) {
    $vbody = [ordered]@{
        sprk_policyversionid     = $VersionId
        sprk_name                = "$PolicyCode v1"
        'sprk_Policy@odata.bind' = "/sprk_policies($policyRowId)"
        sprk_versionnumber       = 1
        sprk_ruletype            = $ruleTypeMap[$RuleType]
        sprk_rulebody            = $Def.Body
        sprk_shortheadline       = $Def.Headline
        sprk_messagetemplate     = $Def.Template
        sprk_proposedaction      = $Def.ProposedAction
        sprk_consequence         = $ConsequenceFlag
        sprk_inforcefrom         = $InForceFrom
    }
    if ($OwnerTeamId) { $vbody['ownerid@odata.bind'] = "/teams($OwnerTeamId)" }
    $ownerNote = if ($OwnerTeamId) { ", owner = team $OwnerTeamId" } else { '' }
    if ($PSCmdlet.ShouldProcess("$DataverseUrl sprk_policyversion $VersionId", "CREATE $PolicyCode v1 (Flag, body {}, in force from $InForceFrom, no end date$ownerNote)")) {
        $vbody['sprk_AuthoredBy@odata.bind'] = "/systemusers($(Get-AuthoredBy))"
        Invoke-Dv POST "$ApiBase/sprk_policyversions" $vbody | Out-Null
        [void]$created.Add([string]$VersionId)
        Write-Host "CREATED version $PolicyCode v1 -> $VersionId"
    }
    elseif ($OwnerTeamId) { Write-Host "  would write ownerid = /teams($OwnerTeamId)" }
}
else {
    $d = @(Get-VersionDiffs $vr $policyRowId)
    if ($d.Count -gt 0) { throw "$PolicyCode v1: the live version differs from the definition ($($d -join ', ')). A published version is immutable (FR-49): not edited. Owner decision needed." }
    Write-Host "UNCHANGED version $PolicyCode v1"
}

# The pointer moves only once the version exists.
$liveRow = if ($WhatIfPreference) { $row } else { Get-PolicyRow }
$liveVersion = if ($null -ne $liveRow -and -not $WhatIfPreference) { Get-VersionRow $liveRow.sprk_policyid } else { $vr }
if ($null -ne $liveRow -and $null -ne $liveVersion -and $liveRow._sprk_currentversion_value -ne $liveVersion.sprk_policyversionid) {
    if ($PSCmdlet.ShouldProcess("$DataverseUrl sprk_policy $($liveRow.sprk_policyid)", "SET current version -> v1")) {
        Invoke-Dv PATCH "$ApiBase/sprk_policies($($liveRow.sprk_policyid))" @{ 'sprk_CurrentVersion@odata.bind' = "/sprk_policyversions($($liveVersion.sprk_policyversionid))" } | Out-Null
        Write-Host "SET current version of $PolicyCode -> v1"
    }
}
elseif ($null -eq $liveVersion) { Write-Host "  would set current version of $PolicyCode -> v1 once the version exists" }

if ($WhatIfPreference) { return }

# ---- read-back: any difference fails the script ---------------------------------------------------------------------
$row = Get-PolicyRow
if ($null -eq $row) { throw "READ-BACK FAIL: policy $PolicyCode is absent." }
$d = @(Get-PolicyDiffs $row)
# Only rows this run created are held to the owner: an existing row is never re-owned (-Verify reports it).
if ($created.Contains([string]$row.sprk_policyid)) { $d += @(Get-OwnerDiff $row) }
$vr = Get-VersionRow $row.sprk_policyid
if ($null -eq $vr) { throw "READ-BACK FAIL: version $PolicyCode v1 is absent." }
$d += @(Get-VersionDiffs $vr $row.sprk_policyid)
if ($created.Contains([string]$vr.sprk_policyversionid)) { $d += @(Get-OwnerDiff $vr) }
if ($row._sprk_currentversion_value -ne $vr.sprk_policyversionid) { $d += 'sprk_currentversion is not v1' }
if ($d.Count -gt 0) { throw "READ-BACK FAIL: $($d -join '; ')" }
Write-Host "READ-BACK OK: $PolicyCode policy $($row.sprk_policyid) (enabled=$($row.sprk_enabled)) and v1 $($vr.sprk_policyversionid) equal the definition." -ForegroundColor Green
