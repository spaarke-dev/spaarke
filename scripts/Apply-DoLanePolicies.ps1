<#
.SYNOPSIS
    Creates or verifies the four Do-lane policies (sprk_policy + sprk_policyversion) of ontology task 061: overdue task,
    task coming due, work assignment past its response date, overdue To Do. With -DefinitionPath config/know-promotion.policies.json
    it applies the Know-promotion policy of ontology task 063 (a Decide-lane rule) the same way.

.DESCRIPTION
    Source of truth: config/do-lane.policies.json. Each policy and each version has a FIXED id, so the script is an upsert:
      policy absent            -> POST (create, sprk_enabled = No, no current version yet)
      version absent           -> POST (create)
      policy.currentversion    -> PATCH to the definition's current version when it differs
      policy descriptive fields (name, short name, lane, subject type, work type, severity, priority, counts-toward-suppression)
                               -> PATCH when they differ
      version row present      -> NEVER written, with ONE exception below. A published version is immutable (FR-49): a body, plan,
                                  template or headline that differs from the definition is reported as DRIFT and fails the script;
                                  the fix is a new version entry in the definition, not an edit.
      superseded version       -> when the definition's current version is newer, each older version's sprk_inforceto is stamped with
                                  the NEXT version's inForceFrom, only when it is empty (task 159). This mirrors the product's publish
                                  order (OntologyAdminAuthoringService, task 033): create the new version, END the old one, then move
                                  sprk_currentversion. sprk_inforceto is the only column of an existing version row the script writes.
                                  The current version must have no sprk_inforceto; an end that differs from the expected one, or an
                                  end on the current version, is DRIFT (never rewritten).
      sprk_enabled             -> set to No on CREATE only. The script never changes it on an existing row: turning a policy on is
                                  the owner's review decision.
    After a write the rows are read back and compared; any difference fails the script.

    Before anything else (also in -WhatIf and -Verify) the definition is linted: it parses, ids and codes are unique, every
    option label exists on the live column, each rule body is JSON, each decision plan names only Do-lane codes, text fits its column.
    The authoritative rule-body, template and plan validation (the compiler, ValidateForSave, DecisionPlanService) is the unit test
    DoLanePolicyDefinitionTests (config/do-lane.policies.json) or KnowPromotionPolicyDefinitionTests (config/know-promotion.policies.json),
    which read the same definition file. The definition's kind fixes the lane and the plan codes the lint accepts (see $KindRules).

    This script is a dev data change when it writes. Run -WhatIf first; -Verify never writes.

.PARAMETER DataverseUrl
    Target environment, e.g. https://spaarkedev1.crm.dynamics.com. Defaults to $env:DATAVERSE_URL. Required.

.PARAMETER DefinitionPath
    The definition JSON. Default: config/do-lane.policies.json beside the repo root. For task 063 pass config/know-promotion.policies.json.

.PARAMETER Verify
    READ-ONLY. Reports each policy and version as ABSENT, IN SYNC or DRIFT. Exit 1 on a lint failure or DRIFT; ABSENT is exit 0
    unless -RequireRows is given.

.PARAMETER RequireRows
    With -Verify: treat an ABSENT policy or version as a failure (use after the apply).

.PARAMETER CustomerBusinessUnit
    OPTIONAL. The customer's top business unit (the root's direct child that provisioning H10 creates), by id or by name. Task 171,
    D-197 (UAC round 137). With it, every sprk_policy and sprk_policyversion row the script CREATES is owned by that unit's
    restricted-content owner team (`ownerid@odata.bind` on the two creates) instead of by the operator who runs it. The team is found
    by the SAME rule as Infrastructure/Dataverse/RestrictedContentOwnerTeam.cs (see Resolve-RestrictedContentOwner): a non-default
    Owner team named 'Spaarke Restricted Content Owners', exactly one in that unit, with no member. Existing rows are never re-owned;
    -Verify reports each existing row owned by anyone else as DRIFT (and writes nothing). -WhatIf names the owner it would bind.
    FAIL CLOSED, before any write (non-zero exit): the unit is the root, is not the root's direct child, is the Secure Record unit,
    cannot be found or is ambiguous, or its team is missing, ambiguous or has a member; and (apply and -WhatIf only) the operator does
    not hold Assign on sprk_policy and sprk_policyversion. Without the parameter the script behaves exactly as before.

.EXAMPLE
    pwsh scripts/Apply-DoLanePolicies.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com -WhatIf
    pwsh scripts/Apply-DoLanePolicies.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com
    pwsh scripts/Apply-DoLanePolicies.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com -Verify -RequireRows
    pwsh scripts/Apply-DoLanePolicies.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com -DefinitionPath config/know-promotion.policies.json -WhatIf
    pwsh scripts/Apply-DoLanePolicies.ps1 -DataverseUrl https://<customer>.crm.dynamics.com -CustomerBusinessUnit <top business unit id or name> -WhatIf
#>
#Requires -Version 7.0
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$DataverseUrl = $env:DATAVERSE_URL,
    [string]$DefinitionPath,
    [switch]$Verify,
    [switch]$RequireRows,
    [string]$CustomerBusinessUnit
)

$ErrorActionPreference = 'Stop'

if (-not $DefinitionPath) { $DefinitionPath = Join-Path $PSScriptRoot '..\config\do-lane.policies.json' }
if (-not (Test-Path -LiteralPath $DefinitionPath)) { throw "Definition not found: $DefinitionPath" }
if (-not $DataverseUrl) { throw 'DataverseUrl is required (parameter or $env:DATAVERSE_URL).' }
$DataverseUrl = $DataverseUrl.TrimEnd('/')
$ApiBase = "$DataverseUrl/api/data/v9.2"

# Per definition kind: the lane the policies must be in and the plan codes of the closed action catalog for that lane
# (DecisionActionCatalog.cs, PlanLane), and the Next-step codes (IsNextStep; the same set for every lane). A plan naming anything
# else is refused by DecisionPlanService at read time, so it is refused here first. These lists are hardcoded; the C# guard
# DecisionActionCatalogScriptListsTests fails when they and the catalog diverge.
$NextStepCodes = @('add-todo', 'create-event', 'send-email', 'assign-work')
$KindRules = @{
    'do-lane-policies'        = @{ Lane = 'Do'; NextSteps = $NextStepCodes; Codes = @('mark-complete', 'reschedule', 'reassign', 'send-reminder', 'extend-response-date', 'record-the-response') }
    'know-promotion-policies' = @{ Lane = 'Decide'; NextSteps = $NextStepCodes; Codes = @('send-budget-inquiry', 'revise-budget', 'approve-variance', 'assign-work', 'assign-task') }
}
$MaxHeadline = 200; $MaxProposedAction = 400; $MaxShortName = 100

# Task 171 (D-197). The restricted-content owner team rule of RestrictedContentOwnerTeam.cs, which this script copies and never
# redefines. The names are that class's DEFAULTS (RestrictedContentOwnerTeam.DefaultOwnerTeamName,
# SecureRecordOwnerTeam.DefaultBusinessUnitName); the C# reads RestrictedContent:OwnerTeamName and SecureRecord:BusinessUnitName from
# configuration, which a script cannot see, so a deployment that renames either gets a refusal here (team not found) and edits these.
# The two limits are RestrictedContentOwnerTeam.MaxParentWalk and .MemberReadLimit.
$RestrictedOwnerTeamName = 'Spaarke Restricted Content Owners'
$SecureBusinessUnitName = 'Secure Record'
$MaxParentWalk = 32; $MemberReadLimit = 5
$AssignPrivileges = @('prvAssignsprk_policy', 'prvAssignsprk_policyversion')

$definition = Get-Content -LiteralPath $DefinitionPath -Raw -Encoding utf8 | ConvertFrom-Json -Depth 32

# ---- Dataverse access ---------------------------------------------------------------------------------------------
$token = az account get-access-token --resource $DataverseUrl --query accessToken -o tsv
if (-not $token) { throw "Could not get a token for $DataverseUrl (az login?)." }
$headers = @{
    Authorization      = "Bearer $token"
    'Content-Type'     = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
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
$workMap = Get-OptionMap 'sprk_policy' 'sprk_worktype'
$severityMap = Get-OptionMap 'sprk_policy' 'sprk_severity'
$ruleTypeMap = Get-OptionMap 'sprk_policyversion' 'sprk_ruletype'

# ConvertFrom-Json turns an ISO timestamp string into a DateTime; the definition and the wire both want the ISO text.
function Get-Instant($value) {
    if ($value -is [datetime]) { $value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'") } else { [string]$value }
}

# The version after $v in the definition, when $v has been superseded (its number is below the policy's current version); else $null.
function Get-NextVersion($p, $v) {
    if ([int]$v.versionNumber -ge [int]$p.currentVersion) { return $null }
    $p.versions | Where-Object { [int]$_.versionNumber -gt [int]$v.versionNumber } | Sort-Object { [int]$_.versionNumber } | Select-Object -First 1
}

# Where a version must end: the next version's inForceFrom (ISO UTC) for a superseded one, $null (no end) for the current one.
function Get-ExpectedInForceTo($p, $v) {
    $next = Get-NextVersion $p $v
    if ($null -eq $next) { return $null }
    Get-Instant $next.inForceFrom
}

# What the live version row's sprk_inforceto needs: 'None' (as expected), 'Stamp' (superseded and still open: the one allowed write)
# or 'Drift' (any other difference, including an end on the current version; never rewritten).
function Get-VersionEndAction($p, $v, $row) {
    $expected = Get-ExpectedInForceTo $p $v
    $live = if ($null -ne $row.sprk_inforceto) { Get-Instant $row.sprk_inforceto } else { $null }
    if ($null -eq $live -and $null -eq $expected) { return 'None' }
    if ($null -eq $live) { return 'Stamp' }
    if ($live -ceq $expected) { return 'None' }
    'Drift'
}

function Get-InForceToDiff($p, $v, $row) {
    if ((Get-VersionEndAction $p $v $row) -eq 'None') { return $null }
    $live = if ($null -ne $row.sprk_inforceto) { Get-Instant $row.sprk_inforceto } else { '' }
    "sprk_inforceto live='$live' expected='$(Get-ExpectedInForceTo $p $v)'"
}

# ---- lint ---------------------------------------------------------------------------------------------------------
function Test-Definition($def) {
    $problems = [System.Collections.Generic.List[string]]::new()
    if ($def._version -ne '1.0' -or -not $KindRules.ContainsKey([string]$def.kind)) { $problems.Add("definition must have _version 1.0 and kind one of: $($KindRules.Keys -join ', ')"); return $problems }
    $rules = $KindRules[[string]$def.kind]
    $ids = @(); $codes = @()
    foreach ($p in $def.policies) {
        $at = $p.code
        if ($p.policyId -notmatch '^[0-9a-f]{8}-([0-9a-f]{4}-){3}[0-9a-f]{12}$') { $problems.Add("${at}: policyId is not a GUID") }
        $ids += $p.policyId; $codes += $p.code
        if ($p.shortName.Length -gt $MaxShortName) { $problems.Add("${at}: shortName exceeds $MaxShortName characters") }
        if ($p.lane -ne $rules.Lane -or -not $laneMap.ContainsKey($p.lane)) { $problems.Add("${at}: lane must be $($rules.Lane) (kind $($def.kind)) and exist on the live column") }
        if (-not $subjectMap.ContainsKey($p.subjectType)) { $problems.Add("${at}: subjectType '$($p.subjectType)' is not on the live column") }
        if (-not $workMap.ContainsKey($p.workType)) { $problems.Add("${at}: workType '$($p.workType)' is not on the live column") }
        if (-not $severityMap.ContainsKey($p.severity)) { $problems.Add("${at}: severity '$($p.severity)' is not on the live column") }
        $numbers = @($p.versions | ForEach-Object { $_.versionNumber })
        if ($numbers.Count -eq 0 -or ($numbers | Select-Object -Unique).Count -ne $numbers.Count) { $problems.Add("${at}: version numbers must be present and unique") }
        if ($numbers -notcontains $p.currentVersion) { $problems.Add("${at}: currentVersion $($p.currentVersion) is not one of the versions") }
        elseif ([int]$p.currentVersion -ne [int](($numbers | Measure-Object -Maximum).Maximum)) { $problems.Add("${at}: currentVersion must be the highest version number (the script advances to the newest version and ends the ones before it)") }
        $byNumber = @($p.versions | Sort-Object { [int]$_.versionNumber })
        for ($i = 1; $i -lt $byNumber.Count; $i++) {
            # Strictly increasing: an equal pair is refused too (the superseded version would end at the instant it began).
            if ((Get-Instant $byNumber[$i].inForceFrom) -le (Get-Instant $byNumber[$i - 1].inForceFrom)) { $problems.Add("${at}: v$($byNumber[$i].versionNumber) inForceFrom must be after v$($byNumber[$i - 1].versionNumber) inForceFrom") }
        }
        foreach ($v in $p.versions) {
            $va = "$at v$($v.versionNumber)"
            if ($v.versionId -notmatch '^[0-9a-f]{8}-([0-9a-f]{4}-){3}[0-9a-f]{12}$') { $problems.Add("${va}: versionId is not a GUID") }
            $ids += $v.versionId
            if (-not $ruleTypeMap.ContainsKey($v.ruleType)) { $problems.Add("${va}: ruleType '$($v.ruleType)' is not on the live column") }
            if ($null -eq $v.ruleBody -or $v.ruleBody.type -ne $v.ruleType) { $problems.Add("${va}: ruleBody must be an object whose type equals ruleType") }
            if ($v.ruleBody.PSObject.Properties.Name -contains 'sprk_finalduedate' -or ($v.ruleBody | ConvertTo-Json -Depth 32 -Compress) -match 'sprk_finalduedate') { $problems.Add("${va}: a body must not read sprk_finalduedate (D-27)") }
            if ($v.shortHeadline.Length -gt $MaxHeadline) { $problems.Add("${va}: shortHeadline exceeds $MaxHeadline characters") }
            if ($v.proposedAction.Length -gt $MaxProposedAction) { $problems.Add("${va}: proposedAction exceeds $MaxProposedAction characters") }
            if ($v.messageTemplate -match '\{\{') { $problems.Add("${va}: messageTemplate must be literal (no tokens)") }
            if ((Get-Instant $v.inForceFrom) -notmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$') { $problems.Add("${va}: inForceFrom must be an ISO UTC instant") }
            foreach ($code in @($v.decisionPlan.actions)) { if ($rules.Codes -notcontains $code) { $problems.Add("${va}: decision plan names '$code', not a $($rules.Lane)-lane catalog code") } }
            foreach ($code in @($v.decisionPlan.nextSteps)) { if ($rules.NextSteps -notcontains $code) { $problems.Add("${va}: decision plan Next step '$code' is not a catalog Next-step code") } }
            if (@($v.decisionPlan.actions).Count -eq 0) { $problems.Add("${va}: decision plan has no actions") }
        }
    }
    if (($ids | Select-Object -Unique).Count -ne $ids.Count) { $problems.Add('ids are not unique') }
    if (($codes | Select-Object -Unique).Count -ne $codes.Count) { $problems.Add('policy codes are not unique') }
    $problems
}

$lint = @(Test-Definition $definition)
if ($lint.Count -gt 0) {
    $lint | ForEach-Object { Write-Host "LINT FAIL: $_" -ForegroundColor Red }
    throw "Definition lint failed ($($lint.Count) problem(s)). Nothing was written to $DataverseUrl."
}
Write-Host "Definition OK: $DefinitionPath ($(@($definition.policies).Count) policies)."

# ---- reads and comparisons ----------------------------------------------------------------------------------------
# With -CustomerBusinessUnit the owner is read too (-Verify drift and the read-back of created rows); without it the reads are as before.
function Get-OwnerSelect { if ($script:OwnerTeamId) { ',_ownerid_value' } else { '' } }

function Get-PolicyRow([string]$Id) {
    $r = Invoke-Dv GET "$ApiBase/sprk_policies?`$select=sprk_policyid,sprk_name,sprk_policycode,sprk_shortname,sprk_lane,sprk_subjecttype,sprk_worktype,sprk_severity,sprk_priority,sprk_countstowardsuppression,sprk_enabled,statecode,_sprk_currentversion_value$(Get-OwnerSelect)&`$filter=sprk_policyid eq $Id" $null
    if (@($r.value).Count -eq 0) { return $null }
    $r.value[0]
}

function Get-VersionRow([string]$Id) {
    $r = Invoke-Dv GET "$ApiBase/sprk_policyversions?`$select=sprk_policyversionid,sprk_name,_sprk_policy_value,sprk_versionnumber,sprk_ruletype,sprk_rulebody,sprk_shortheadline,sprk_messagetemplate,sprk_proposedaction,sprk_decisionplan,sprk_inforcefrom,sprk_inforceto,statecode$(Get-OwnerSelect)&`$filter=sprk_policyversionid eq $Id" $null
    if (@($r.value).Count -eq 0) { return $null }
    $r.value[0]
}

function ConvertTo-CompactJson($value) { $value | ConvertTo-Json -Depth 32 -Compress }
function Get-ParsedCompact([string]$Text) { try { $Text | ConvertFrom-Json -Depth 32 | ConvertTo-Json -Depth 32 -Compress } catch { $null } }

# The policy fields the script manages, expected vs live. (sprk_enabled is deliberately not among them.)
function Get-PolicyDiffs($p, $row) {
    $expected = [ordered]@{
        sprk_name                    = $p.name
        sprk_policycode              = $p.code
        sprk_shortname               = $p.shortName
        sprk_lane                    = $laneMap[$p.lane]
        sprk_subjecttype             = $subjectMap[$p.subjectType]
        sprk_worktype                = $workMap[$p.workType]
        sprk_severity                = $severityMap[$p.severity]
        sprk_priority                = [int]$p.priority
        sprk_countstowardsuppression = [bool]$p.countsTowardSuppression
    }
    $diffs = [System.Collections.Generic.List[string]]::new()
    foreach ($k in $expected.Keys) { if ($row.$k -ne $expected[$k]) { $diffs.Add("$k live='$($row.$k)' expected='$($expected[$k])'") } }
    $currentId = ($p.versions | Where-Object { $_.versionNumber -eq $p.currentVersion }).versionId
    if ($row._sprk_currentversion_value -ne $currentId) { $diffs.Add("sprk_currentversion live='$($row._sprk_currentversion_value)' expected='$currentId'") }
    $diffs
}

# A version is immutable: every comparison is a DRIFT, never a write (sprk_inforceto of a superseded version is the one write, see the apply).
function Get-VersionDiffs($p, $v, $row) {
    $diffs = [System.Collections.Generic.List[string]]::new()
    if ($row._sprk_policy_value -ne $p.policyId) { $diffs.Add('sprk_policy') }
    if ($row.sprk_versionnumber -ne [int]$v.versionNumber) { $diffs.Add('sprk_versionnumber') }
    if ($row.sprk_ruletype -ne $ruleTypeMap[$v.ruleType]) { $diffs.Add('sprk_ruletype') }
    if ((Get-ParsedCompact $row.sprk_rulebody) -cne (ConvertTo-CompactJson $v.ruleBody)) { $diffs.Add('sprk_rulebody') }
    if ((Get-ParsedCompact $row.sprk_decisionplan) -cne (ConvertTo-CompactJson $v.decisionPlan)) { $diffs.Add('sprk_decisionplan') }
    if ($row.sprk_messagetemplate -cne $v.messageTemplate) { $diffs.Add('sprk_messagetemplate') }
    if ($row.sprk_shortheadline -cne $v.shortHeadline) { $diffs.Add('sprk_shortheadline') }
    if ($row.sprk_proposedaction -cne $v.proposedAction) { $diffs.Add('sprk_proposedaction') }
    # A version the nightly job refuses (deactivated, or ended in force) is not in sync even if its text matches. The current version
    # has no end; a superseded one ends at the next version's inForceFrom (task 159).
    if ($row.statecode -ne 0) { $diffs.Add('statecode') }
    if (Get-InForceToDiff $p $v $row) { $diffs.Add((Get-InForceToDiff $p $v $row)) }
    $diffs
}

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
    if ($teams.Count -eq 0 -or -not $teams[0].teamid) { return New-OwnerResult 'OwnerTeamNotFound' $null $top 0 }
    if ($teams.Count -gt 1) { return New-OwnerResult 'OwnerTeamAmbiguous' $null $top 0 }

    # No member of any kind (human or application user): the membership rows themselves.
    $teamId = [string]$teams[0].teamid
    $members = @((Invoke-Dv GET "$ApiBase/teammemberships?`$select=systemuserid&`$filter=teamid eq $teamId&`$top=$MemberReadLimit" $null).value).Count
    if ($members -gt 0) { return New-OwnerResult 'OwnerTeamHasMembers' $teamId $top $members }
    New-OwnerResult 'Resolved' $teamId $top 0
}

# The Assign privileges the operator is missing (names), by the operator's effective privileges (RetrieveUserPrivileges covers the
# roles of the user and of their teams). A privilege that does not exist on the environment counts as missing: fail closed.
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
if ($CustomerBusinessUnit) {
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
    foreach ($p in $definition.policies) {
        $row = Get-PolicyRow $p.policyId
        if ($null -eq $row) {
            Write-Host ("{0,-18} policy ABSENT" -f $p.code) -ForegroundColor Yellow
            if ($RequireRows) { $failed = $true }
        }
        else {
            $d = @(Get-PolicyDiffs $p $row) + @(Get-OwnerDiff $row)
            if ($d.Count -eq 0) { Write-Host ("{0,-18} policy IN SYNC (enabled={1}, statecode={2})" -f $p.code, $row.sprk_enabled, $row.statecode) -ForegroundColor Green }
            else { Write-Host ("{0,-18} policy DRIFT: {1}" -f $p.code, ($d -join '; ')) -ForegroundColor Red; $failed = $true }
        }
        foreach ($v in $p.versions) {
            $vr = Get-VersionRow $v.versionId
            if ($null -eq $vr) {
                Write-Host ("{0,-18} v{1} ABSENT" -f $p.code, $v.versionNumber) -ForegroundColor Yellow
                if ($RequireRows) { $failed = $true }
            }
            else {
                $d = @(Get-VersionDiffs $p $v $vr) + @(Get-OwnerDiff $vr)
                if ($d.Count -eq 0) { Write-Host ("{0,-18} v{1} IN SYNC" -f $p.code, $v.versionNumber) -ForegroundColor Green }
                else { Write-Host ("{0,-18} v{1} DRIFT: {2}" -f $p.code, $v.versionNumber, ($d -join '; ')) -ForegroundColor Red; $failed = $true }
            }
        }
    }
    if ($failed) { exit 1 }
    return
}

# ---- apply --------------------------------------------------------------------------------------------------------
$created = [System.Collections.Generic.HashSet[string]]::new()
foreach ($p in $definition.policies) {
    $currentVersion = $p.versions | Where-Object { $_.versionNumber -eq $p.currentVersion }
    $row = Get-PolicyRow $p.policyId

    if ($null -eq $row) {
        $body = [ordered]@{
            sprk_policyid                = $p.policyId
            sprk_name                    = $p.name
            sprk_policycode              = $p.code
            sprk_shortname               = $p.shortName
            sprk_lane                    = $laneMap[$p.lane]
            sprk_subjecttype             = $subjectMap[$p.subjectType]
            sprk_worktype                = $workMap[$p.workType]
            sprk_severity                = $severityMap[$p.severity]
            sprk_priority                = [int]$p.priority
            sprk_countstowardsuppression = [bool]$p.countsTowardSuppression
            sprk_enabled                 = $false
        }
        if ($OwnerTeamId) { $body['ownerid@odata.bind'] = "/teams($OwnerTeamId)" }
        $ownerNote = if ($OwnerTeamId) { ", owner = team $OwnerTeamId" } else { '' }
        if ($PSCmdlet.ShouldProcess("$DataverseUrl sprk_policy $($p.policyId)", "CREATE $($p.code) (sprk_enabled = No$ownerNote)")) {
            Invoke-Dv POST "$ApiBase/sprk_policies" $body | Out-Null
            [void]$created.Add([string]$p.policyId)
            Write-Host "CREATED policy $($p.code) -> $($p.policyId) (enabled = No)"
        }
    }
    else {
        $d = @(Get-PolicyDiffs $p $row | Where-Object { $_ -notlike 'sprk_currentversion*' })
        if ($d.Count -gt 0) {
            $patch = [ordered]@{
                sprk_name = $p.name; sprk_shortname = $p.shortName; sprk_lane = $laneMap[$p.lane]; sprk_subjecttype = $subjectMap[$p.subjectType]
                sprk_worktype = $workMap[$p.workType]; sprk_severity = $severityMap[$p.severity]; sprk_priority = [int]$p.priority
                sprk_countstowardsuppression = [bool]$p.countsTowardSuppression
            }
            if ($PSCmdlet.ShouldProcess("$DataverseUrl sprk_policy $($p.policyId)", "UPDATE $($p.code): $($d -join '; ')")) {
                Invoke-Dv PATCH "$ApiBase/sprk_policies($($p.policyId))" $patch | Out-Null
                Write-Host "UPDATED policy $($p.code): $($d -join '; ')"
            }
        }
        else { Write-Host "UNCHANGED policy $($p.code) (enabled stays $($row.sprk_enabled): the script never changes it)" }
    }

    foreach ($v in $p.versions) {
        $vr = Get-VersionRow $v.versionId
        if ($null -eq $vr) {
            $vbody = [ordered]@{
                sprk_policyversionid   = $v.versionId
                sprk_name              = "$($p.code) v$($v.versionNumber)"
                'sprk_Policy@odata.bind' = "/sprk_policies($($p.policyId))"
                sprk_versionnumber     = [int]$v.versionNumber
                sprk_ruletype          = $ruleTypeMap[$v.ruleType]
                sprk_rulebody          = (ConvertTo-CompactJson $v.ruleBody)
                sprk_shortheadline     = $v.shortHeadline
                sprk_messagetemplate   = $v.messageTemplate
                sprk_proposedaction    = $v.proposedAction
                sprk_decisionplan      = (ConvertTo-CompactJson $v.decisionPlan)
                sprk_inforcefrom       = (Get-Instant $v.inForceFrom)
            }
            if ($OwnerTeamId) { $vbody['ownerid@odata.bind'] = "/teams($OwnerTeamId)" }
            $ownerNote = if ($OwnerTeamId) { " (owner = team $OwnerTeamId)" } else { '' }
            if ($PSCmdlet.ShouldProcess("$DataverseUrl sprk_policyversion $($v.versionId)", "CREATE $($p.code) v$($v.versionNumber)$ownerNote")) {
                $vbody['sprk_AuthoredBy@odata.bind'] = "/systemusers($(Get-AuthoredBy))"
                Invoke-Dv POST "$ApiBase/sprk_policyversions" $vbody | Out-Null
                [void]$created.Add([string]$v.versionId)
                Write-Host "CREATED version $($p.code) v$($v.versionNumber) -> $($v.versionId)"
            }
            else {
                Write-Host "  would write sprk_rulebody = $(ConvertTo-CompactJson $v.ruleBody)"; Write-Host "  would write sprk_decisionplan = $(ConvertTo-CompactJson $v.decisionPlan)"
                if ($OwnerTeamId) { Write-Host "  would write ownerid = /teams($OwnerTeamId)" }
            }
        }
        else {
            # sprk_inforceto is judged in the end pass below (stamped when it is the allowed write, DRIFT otherwise); the rest is immutable.
            $d = @(Get-VersionDiffs $p $v $vr | Where-Object { $_ -notlike 'sprk_inforceto*' })
            if ($d.Count -gt 0) { throw "$($p.code) v$($v.versionNumber): the live version differs from the definition ($($d -join ', ')). A published version is immutable (FR-49): add a NEW version entry instead of editing this one." }
            Write-Host "UNCHANGED version $($p.code) v$($v.versionNumber)"
        }
    }

    # (b) End the superseded versions, after the new version exists and before the pointer moves (the product's publish order,
    # OntologyAdminAuthoringService): sprk_inforceto = the next version's inForceFrom, only where it is still empty. This is the only
    # write the script ever makes to an existing version row; a different end is DRIFT and fails the script.
    foreach ($v in $p.versions) {
        $next = Get-NextVersion $p $v
        if ($null -eq $next) { continue }
        $expectedEnd = Get-ExpectedInForceTo $p $v
        $vr = Get-VersionRow $v.versionId
        $nextRow = Get-VersionRow $next.versionId
        if ($null -eq $vr -or $null -eq $nextRow) {
            Write-Host "  would end $($p.code) v$($v.versionNumber) at $expectedEnd (sprk_inforceto) once v$($v.versionNumber) and v$($next.versionNumber) exist"
            continue
        }
        $action = Get-VersionEndAction $p $v $vr
        if ($action -eq 'Stamp') {
            if ($PSCmdlet.ShouldProcess("$DataverseUrl sprk_policyversion $($v.versionId)", "END $($p.code) v$($v.versionNumber): sprk_inforceto = $expectedEnd")) {
                Invoke-Dv PATCH "$ApiBase/sprk_policyversions($($v.versionId))" @{ sprk_inforceto = $expectedEnd } | Out-Null
                Write-Host "ENDED version $($p.code) v$($v.versionNumber) at $expectedEnd"
            }
        }
        elseif ($action -eq 'Drift') {
            throw "$($p.code) v$($v.versionNumber): its sprk_inforceto differs from the expected end ($(Get-InForceToDiff $p $v $vr)). A version row is never rewritten: fix it by hand in Dataverse or with the product's publish route."
        }
        else { Write-Host "UNCHANGED end of version $($p.code) v$($v.versionNumber)" }
    }

    # The pointer moves only once the version exists.
    $liveRow = if ($WhatIfPreference) { $row } else { Get-PolicyRow $p.policyId }
    if ($null -ne $liveRow -and $liveRow._sprk_currentversion_value -ne $currentVersion.versionId) {
        if ($PSCmdlet.ShouldProcess("$DataverseUrl sprk_policy $($p.policyId)", "SET current version -> $($currentVersion.versionId)")) {
            Invoke-Dv PATCH "$ApiBase/sprk_policies($($p.policyId))" @{ 'sprk_CurrentVersion@odata.bind' = "/sprk_policyversions($($currentVersion.versionId))" } | Out-Null
            Write-Host "SET current version of $($p.code) -> v$($currentVersion.versionNumber)"
        }
    }
    elseif ($null -eq $liveRow) {
        Write-Host "  would set current version of $($p.code) -> v$($currentVersion.versionNumber) once the policy exists"
    }
}

if ($WhatIfPreference) { return }

# ---- read-back: any difference fails the script ---------------------------------------------------------------------
$bad = $false
foreach ($p in $definition.policies) {
    $row = Get-PolicyRow $p.policyId
    if ($null -eq $row) { Write-Host "READ-BACK FAIL: policy $($p.code) is absent." -ForegroundColor Red; $bad = $true; continue }
    $d = @(Get-PolicyDiffs $p $row)
    # Only rows this run created are held to the owner: an existing row is never re-owned (-Verify reports it).
    if ($created.Contains([string]$p.policyId)) { $d += @(Get-OwnerDiff $row) }
    if ($d.Count -gt 0) { Write-Host "READ-BACK FAIL: policy $($p.code): $($d -join '; ')" -ForegroundColor Red; $bad = $true }
    else { Write-Host ("READ-BACK OK policy  {0} {1} lane={2} subject={3} worktype={4} severity={5} enabled={6} statecode={7} currentversion={8}" -f $p.code, $p.policyId, $row.sprk_lane, $row.sprk_subjecttype, $row.sprk_worktype, $row.sprk_severity, $row.sprk_enabled, $row.statecode, $row._sprk_currentversion_value) -ForegroundColor Green }
    foreach ($v in $p.versions) {
        $vr = Get-VersionRow $v.versionId
        if ($null -eq $vr) { Write-Host "READ-BACK FAIL: version $($p.code) v$($v.versionNumber) is absent." -ForegroundColor Red; $bad = $true; continue }
        $vd = @(Get-VersionDiffs $p $v $vr)
        if ($created.Contains([string]$v.versionId)) { $vd += @(Get-OwnerDiff $vr) }
        if ($vd.Count -gt 0) { Write-Host "READ-BACK FAIL: version $($p.code) v$($v.versionNumber): $($vd -join ', ')" -ForegroundColor Red; $bad = $true }
        else { Write-Host ("READ-BACK OK version {0} v{1} {2} ruletype={3} plan={4}" -f $p.code, $v.versionNumber, $v.versionId, $vr.sprk_ruletype, $vr.sprk_decisionplan) -ForegroundColor Green }
    }
}
if ($bad) { throw 'Read-back does not match the definition.' }
Write-Host 'READ-BACK OK: every policy and version equals the definition.'
