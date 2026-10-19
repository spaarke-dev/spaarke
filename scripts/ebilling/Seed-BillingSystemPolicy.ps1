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

.EXAMPLE
    pwsh scripts/ebilling/Seed-BillingSystemPolicy.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com -WhatIf
    pwsh scripts/ebilling/Seed-BillingSystemPolicy.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com
    pwsh scripts/ebilling/Seed-BillingSystemPolicy.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com -Verify -RequireRows
#>
#Requires -Version 7.0
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$DataverseUrl = $env:DATAVERSE_URL,
    [string]$SolutionUniqueName = 'SpaarkeMaster',
    [switch]$Verify,
    [switch]$RequireRows
)

$ErrorActionPreference = 'Stop'

if (-not $DataverseUrl) { throw 'DataverseUrl is required (parameter or $env:DATAVERSE_URL).' }
$DataverseUrl = $DataverseUrl.TrimEnd('/')
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
    $r = Invoke-Dv GET "$ApiBase/sprk_policies?`$select=sprk_policyid,sprk_name,sprk_policycode,sprk_shortname,sprk_lane,sprk_subjecttype,sprk_severity,sprk_priority,sprk_domain,sprk_enabled,statecode,_sprk_currentversion_value,_sprk_ruleset_value&`$filter=sprk_policycode eq '$PolicyCode'" $null
    if (@($r.value).Count -eq 0) { return $null }
    if (@($r.value).Count -gt 1) { throw "More than one sprk_policy has the code $PolicyCode; refusing to guess." }
    $r.value[0]
}

function Get-VersionRow([string]$PolicyRowId) {
    $r = Invoke-Dv GET "$ApiBase/sprk_policyversions?`$select=sprk_policyversionid,sprk_name,_sprk_policy_value,sprk_versionnumber,sprk_ruletype,sprk_rulebody,sprk_shortheadline,sprk_messagetemplate,sprk_proposedaction,sprk_consequence,sprk_inforcefrom,sprk_inforceto,statecode&`$filter=_sprk_policy_value eq $PolicyRowId and sprk_versionnumber eq 1" $null
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
        $d = @(Get-PolicyDiffs $row)
        if ($d.Count -eq 0) { Write-Host "$PolicyCode policy IN SYNC (enabled=$($row.sprk_enabled), statecode=$($row.statecode))" -ForegroundColor Green }
        else { Write-Host "$PolicyCode policy DRIFT: $($d -join '; ')" -ForegroundColor Red; $failed = $true }
        $vr = Get-VersionRow $row.sprk_policyid
        if ($null -eq $vr) { Write-Host "$PolicyCode v1 ABSENT" -ForegroundColor Yellow; if ($RequireRows) { $failed = $true } }
        else {
            $vd = @(Get-VersionDiffs $vr $row.sprk_policyid)
            if ($vd.Count -eq 0) { Write-Host "$PolicyCode v1 IN SYNC" -ForegroundColor Green }
            else { Write-Host "$PolicyCode v1 DRIFT: $($vd -join '; ')" -ForegroundColor Red; $failed = $true }
        }
    }
    if ($failed) { exit 1 }
    return
}

# ---- apply --------------------------------------------------------------------------------------------------------
$whoami = $null
function Get-AuthoredBy {
    if ($null -eq $script:whoami) { $script:whoami = (Invoke-Dv GET "$ApiBase/WhoAmI" $null).UserId }
    $script:whoami
}

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
    if ($PSCmdlet.ShouldProcess("$DataverseUrl sprk_policy $PolicyId", "CREATE $PolicyCode (enabled, Billing, Decide, Invoice, Info, no rule set)")) {
        Invoke-Dv POST "$ApiBase/sprk_policies" $body | Out-Null
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
    if ($PSCmdlet.ShouldProcess("$DataverseUrl sprk_policyversion $VersionId", "CREATE $PolicyCode v1 (Flag, body {}, in force from $InForceFrom, no end date)")) {
        $vbody['sprk_AuthoredBy@odata.bind'] = "/systemusers($(Get-AuthoredBy))"
        Invoke-Dv POST "$ApiBase/sprk_policyversions" $vbody | Out-Null
        Write-Host "CREATED version $PolicyCode v1 -> $VersionId"
    }
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
$vr = Get-VersionRow $row.sprk_policyid
if ($null -eq $vr) { throw "READ-BACK FAIL: version $PolicyCode v1 is absent." }
$d += @(Get-VersionDiffs $vr $row.sprk_policyid)
if ($row._sprk_currentversion_value -ne $vr.sprk_policyversionid) { $d += 'sprk_currentversion is not v1' }
if ($d.Count -gt 0) { throw "READ-BACK FAIL: $($d -join '; ')" }
Write-Host "READ-BACK OK: $PolicyCode policy $($row.sprk_policyid) (enabled=$($row.sprk_enabled)) and v1 $($vr.sprk_policyversionid) equal the definition." -ForegroundColor Green
