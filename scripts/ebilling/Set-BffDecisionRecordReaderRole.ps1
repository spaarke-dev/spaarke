#!/usr/bin/env pwsh
<#
.SYNOPSIS
    ebilling-r1 DEF-059: gives the BFF application identity the "Spaarke Decision Record Reader" role (organization-wide
    Read on sprk_decisionrecord, nothing else) and verifies it. DRY RUN by default; idempotent; -Verify exits non-zero on drift.

.DESCRIPTION
    WHY. The BFF reads sprk_decisionrecord app-only, as its own application identity, across the organization (counted
    spend, ebilling-r1 task 165b-4; writer-created reasons, task 130). No SpaarkeMaster role assigned to that identity
    granted the read: it worked only through System Administrator. The existing roles do not fit. Spaarke Ontology Service
    also creates Decision Records and creates and writes Signals, which only the server writer identity may hold (write
    provenance). Spaarke Ontology Administrator is a business-unit-scoped role for people with authoring rights. So
    SpaarkeMaster ships a role with exactly one privilege, and this script assigns it to the BFF identity.

    WHAT IT DOES
      - Reads the role from the root business unit. The role is created by the SpaarkeMaster import; this script never
        creates it and never adds or changes a privilege on it.
      - Checks the root role holds EXACTLY prvReadsprk_DecisionRecord at Global (the source pin:
        FeatureRolePrivilegeGuardTests.ExactPrivilegeSets). Anything else is drift. -Apply first strips every privilege
        Dataverse added outside that one (removal only, this role only, on the root role, as Set-CustomerOwnerTeams.ps1
        does; Dataverse propagates it to the per-business-unit copies), reads the role back and requires exactly the
        declared set. A missing role, a missing declared privilege or one at another depth is real drift: -Apply exits 2
        and writes nothing. -Verify stays strict: any extra privilege fails it.
      - For each -BffApplicationIds application user that exists in the environment (the BFF app registration's and the
        BFF user-assigned managed identity's, where both exist), assigns the role copy of THAT user's business unit when
        it is not already assigned. A user that is not in the environment is reported and skipped; none present is drift.
      - Lists every holder of every copy of the role. A user not in -BffApplicationIds (a person, or another application
        user such as the ontology writer) or any team is drift. Reported, never removed: removal is an operator decision.

    It never assigns the role to a person, to a team or to any application user not named in -BffApplicationIds.

    MODES
      (default)  Dry run: the state and the plan (including the extra privileges -Apply would strip). Zero writes. Exits 0
                 (1 when the drift is not one -Apply can fix).
      -Apply     Strips extra privileges, then assigns the missing copies, then re-reads. Exits 0 when the state then holds,
                 1 when gaps remain, 2 when it refused (role or declared privilege wrong) or the strip did not leave the exact set.
      -Verify    Read-only. Exit 0 when the state holds, exit 1 naming each gap.

    Dataverse caches privileges: after -Apply, re-probe the identity's access before trusting a live check.

.PARAMETER EnvironmentUrl
    The Dataverse environment, e.g. https://contoso.crm.dynamics.com. Required.

.PARAMETER BffApplicationIds
    Application (client) ids of the BFF's Dataverse application users: the BFF app registration and the BFF user-assigned
    managed identity. Required. Never the ontology writer identity's id.

.EXAMPLE
    pwsh -File scripts/ebilling/Set-BffDecisionRecordReaderRole.ps1 -EnvironmentUrl <env> -BffApplicationIds <bff-app-id>,<bff-uami-client-id>
    pwsh -File scripts/ebilling/Set-BffDecisionRecordReaderRole.ps1 -EnvironmentUrl <env> -BffApplicationIds <bff-app-id>,<bff-uami-client-id> -Apply
    pwsh -File scripts/ebilling/Set-BffDecisionRecordReaderRole.ps1 -EnvironmentUrl <env> -BffApplicationIds <bff-app-id>,<bff-uami-client-id> -Verify
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$EnvironmentUrl,
    [Parameter(Mandatory)] [string[]]$BffApplicationIds,
    [switch]$Apply,
    [switch]$Verify,
    [string]$RoleName = 'Spaarke Decision Record Reader'
)

$ErrorActionPreference = 'Stop'
if ($Apply -and $Verify) { throw '-Apply and -Verify are separate modes; run -Apply first, then -Verify.' }
$BffApplicationIds = @($BffApplicationIds | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim().ToLowerInvariant() } | Where-Object { $_ })
if ($BffApplicationIds.Count -eq 0) { throw '-BffApplicationIds is empty: name the BFF application user(s) explicitly.' }
foreach ($id in $BffApplicationIds) { if (-not ($id -as [guid])) { throw "-BffApplicationIds: '$id' is not a GUID." } }

# The pinned set: exactly this, nothing else (FeatureRolePrivilegeGuardTests.ExactPrivilegeSets).
$PinnedPrivilege = 'prvReadsprk_DecisionRecord'
$PinnedDepth = 'Global'

$BaseUrl = $EnvironmentUrl.TrimEnd('/')
$Api = "$BaseUrl/api/data/v9.2"

# ── Auth: the operator's own az CLI identity. No secrets. ───────────────────────────────────────────────────
$token = az account get-access-token --resource $BaseUrl --query accessToken -o tsv 2>$null
if (-not $token) { throw "No Dataverse token for $BaseUrl. Run 'az login' and retry." }
$headers = @{
    Authorization      = "Bearer $token"
    Accept             = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'Content-Type'     = 'application/json; charset=utf-8'
}
function Invoke-DvGet([string]$Path) { Invoke-RestMethod -Uri "$Api/$Path" -Headers $headers -Method Get }
function Invoke-DvPost([string]$Path, [object]$Body) {
    Invoke-RestMethod -Uri "$Api/$Path" -Headers $headers -Method Post -Body ([Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 4)))
}

# The role's privileges read against the pin. Missing: the declared privilege is absent or at another depth (real drift; never
# repaired by adding). Extras: every other privilege (platform noise Dataverse adds; -Apply strips these, nothing else).
function Get-RolePrivilegeState([string]$RoleId) {
    $held = @((Invoke-DvGet "RetrieveRolePrivilegesRole(RoleId=@p)?@p=$RoleId").RolePrivileges)
    $pinned = @($held | Where-Object { $_.PrivilegeName -eq $PinnedPrivilege -and "$($_.Depth)" -eq $PinnedDepth })
    $extras = @($held | Where-Object { $_.PrivilegeName -ne $PinnedPrivilege } | Sort-Object PrivilegeName)
    [pscustomobject]@{ Missing = ($pinned.Count -ne 1); Extras = $extras }
}

Write-Host "Environment : $BaseUrl"
Write-Host ("Mode        : {0}" -f $(if ($Verify) { 'VERIFY (read-only)' } elseif ($Apply) { 'APPLY' } else { 'DRY RUN (no writes)' }))
Write-Host "Role        : $RoleName"

# ── The state ───────────────────────────────────────────────────────────────────────────────────────────────
function Read-State {
    $state = [ordered]@{ Gaps = [System.Collections.Generic.List[string]]::new(); Fixable = [System.Collections.Generic.List[object]]::new(); Role = $null; PrivilegeMissing = $false; Extras = @() }

    $rootBu = @((Invoke-DvGet 'businessunits?$select=businessunitid,name&$filter=_parentbusinessunitid_value eq null').value)
    if ($rootBu.Count -ne 1) { throw "Expected one root business unit; found $($rootBu.Count)." }
    $n = $RoleName -replace "'", "''"
    $roots = @((Invoke-DvGet "roles?`$select=roleid,name&`$filter=name eq '$n' and _businessunitid_value eq $($rootBu[0].businessunitid)").value)
    if ($roots.Count -gt 1) { throw "More than one role named '$RoleName' in the root business unit." }
    if ($roots.Count -eq 0) {
        $state.Gaps.Add("role '$RoleName' does not exist in the root business unit: import SpaarkeMaster first (this script never creates it)")
        return $state
    }
    $role = $roots[0]
    $state.Role = $role

    # Exactly the pinned privilege, at the pinned depth.
    $priv = Get-RolePrivilegeState $role.roleid
    if ($priv.Missing) { $state.Gaps.Add("role does not hold $PinnedPrivilege at $PinnedDepth") }
    foreach ($x in $priv.Extras) { $state.Gaps.Add("role holds $($x.PrivilegeName) at $($x.Depth), outside its one-privilege set") }
    $state.PrivilegeMissing = $priv.Missing
    $state.Extras = $priv.Extras

    # Every copy (one per business unit; the root role is its own parent root role).
    $copies = @((Invoke-DvGet "roles?`$select=roleid,_businessunitid_value&`$filter=_parentrootroleid_value eq $($role.roleid)").value)

    # The BFF application users: each present one holds the copy of its own business unit.
    $bffUserIds = @{}
    foreach ($appId in $BffApplicationIds) {
        $u = @((Invoke-DvGet "systemusers?`$filter=applicationid eq $appId&`$select=systemuserid,fullname,isdisabled,_businessunitid_value").value)
        if ($u.Count -eq 0) { Write-Host "  BFF app user $appId : not in this environment (skipped)" -ForegroundColor DarkYellow; continue }
        if ($u.Count -gt 1) { $state.Gaps.Add("more than one application user has application id $appId"); continue }
        $user = $u[0]
        $bffUserIds["$($user.systemuserid)".ToLowerInvariant()] = $true
        $copy = @($copies | Where-Object { "$($_._businessunitid_value)" -eq "$($user._businessunitid_value)" })
        if ($copy.Count -ne 1) { $state.Gaps.Add("no single '$RoleName' copy in the business unit of BFF app user $($user.fullname) ($appId)"); continue }
        $has = @((Invoke-DvGet "systemusers($($user.systemuserid))/systemuserroles_association?`$filter=roleid eq $($copy[0].roleid)&`$select=roleid").value)
        if ($has.Count -gt 0) { Write-Host "  BFF app user $($user.fullname) ($appId): holds the role" }
        else {
            $state.Gaps.Add("BFF app user $($user.fullname) ($appId) does not hold the role")
            $state.Fixable.Add([pscustomobject]@{ UserId = $user.systemuserid; Name = $user.fullname; AppId = $appId; CopyId = $copy[0].roleid })
        }
    }
    if ($bffUserIds.Count -eq 0) { $state.Gaps.Add('none of -BffApplicationIds is an application user in this environment') }

    # Holders outside the BFF identity: reported, never removed.
    foreach ($c in $copies) {
        foreach ($h in @((Invoke-DvGet "roles($($c.roleid))/systemuserroles_association?`$select=systemuserid,fullname,applicationid").value)) {
            if (-not $bffUserIds.ContainsKey("$($h.systemuserid)".ToLowerInvariant())) {
                $kind = if ($h.applicationid) { "application user (appId $($h.applicationid)) not in -BffApplicationIds" } else { 'a PERSON' }
                $state.Gaps.Add("held by $($h.fullname): $kind; only the BFF identity may hold it (not removed: an operator decision)")
            }
        }
        foreach ($t in @((Invoke-DvGet "roles($($c.roleid))/teamroles_association?`$select=teamid,name").value)) {
            $state.Gaps.Add("held by team $($t.name): no team may hold it (not removed: an operator decision)")
        }
    }
    return $state
}

$state = Read-State
$fixableNames = @($state.Fixable | ForEach-Object { "BFF app user $($_.Name) ($($_.AppId)) does not hold the role" }) +
@($state.Extras | ForEach-Object { "role holds $($_.PrivilegeName) at $($_.Depth), outside its one-privilege set" })
$unfixable = @($state.Gaps | Where-Object { $fixableNames -notcontains $_ })

if ($Verify) {
    if ($state.Gaps.Count -eq 0) { Write-Host "`nVERIFY PASS: every BFF application user present holds '$RoleName' ($PinnedPrivilege at $PinnedDepth, nothing else), and nobody else does." -ForegroundColor Green; exit 0 }
    foreach ($g in $state.Gaps) { Write-Host "  GAP  $g" -ForegroundColor Red }
    Write-Host "`nVERIFY FAIL: $($state.Gaps.Count) gap(s)." -ForegroundColor Red
    exit 1
}

Write-Host "`nPLAN"
foreach ($f in $state.Fixable) { Write-Host "  assign '$RoleName' (copy $($f.CopyId)) to BFF app user $($f.Name) ($($f.AppId))" -ForegroundColor Yellow }
if ($state.Fixable.Count -eq 0) { Write-Host '  nothing to assign' }
foreach ($x in $state.Extras) { Write-Host "  -Apply would strip $($x.PrivilegeName) (at $($x.Depth)) from '$RoleName': outside its one-privilege set" -ForegroundColor Yellow }
if ($state.Extras.Count -eq 0) { Write-Host '  nothing to strip' }
foreach ($g in $unfixable) { Write-Host "  NOT FIXED HERE: $g" -ForegroundColor Red }

if (-not $Apply) {
    Write-Host "`nDRY RUN: re-run with -Apply to assign; then -Verify." -ForegroundColor Cyan
    exit $(if ($unfixable.Count -gt 0) { 1 } else { 0 })
}
# Real drift (role missing, the declared privilege missing or at another depth) is never repaired by adding: nothing is written.
if (-not $state.Role -or $state.PrivilegeMissing) {
    Write-Host "`nREFUSED: the role is missing or does not hold $PinnedPrivilege at $PinnedDepth. Nothing was stripped or assigned." -ForegroundColor Red
    exit 2
}

# ── Apply, step 1: strip what Dataverse added outside the declared privilege (removal only, this role only) ──────
# Mirrors scripts/Set-CustomerOwnerTeams.ps1 Set-RolePrivileges (:474-482): RemovePrivilegeRole on the ROOT role; Dataverse
# propagates the removal to the per-business-unit copies. Never adds or changes a privilege, never touches another role.
if ($state.Extras.Count -gt 0) {
    foreach ($x in $state.Extras) {
        Invoke-DvPost "roles($($state.Role.roleid))/Microsoft.Dynamics.CRM.RemovePrivilegeRole" @{
            Privilege = @{ '@odata.type' = 'Microsoft.Dynamics.CRM.privilege'; privilegeid = $x.PrivilegeId.ToString() }
        } | Out-Null
    }
    Write-Host "  STRIPPED from '$RoleName' (outside its one-privilege set): $(($state.Extras | ForEach-Object PrivilegeName) -join ', ')" -ForegroundColor Green
    $check = Get-RolePrivilegeState $state.Role.roleid
    if ($check.Missing -or $check.Extras.Count -gt 0) {
        Write-Host "`nREFUSED: after the strip the role is not exactly $PinnedPrivilege at $PinnedDepth (still held: $(($check.Extras | ForEach-Object PrivilegeName) -join ', ')). Nothing was assigned." -ForegroundColor Red
        exit 2
    }
}

# ── Apply, step 2: only the missing assignments, each to the copy of the user's own business unit ────────────────────
foreach ($f in $state.Fixable) {
    Invoke-DvPost "systemusers($($f.UserId))/systemuserroles_association/`$ref" @{ '@odata.id' = "$Api/roles($($f.CopyId))" } | Out-Null
    Write-Host "  assigned '$RoleName' to BFF app user $($f.Name) ($($f.AppId))" -ForegroundColor Green
}

$after = Read-State
if ($after.Gaps.Count -eq 0) { Write-Host "`nAPPLIED. The state holds; run -Verify to confirm." -ForegroundColor Green; exit 0 }
foreach ($g in $after.Gaps) { Write-Host "  GAP  $g" -ForegroundColor Red }
Write-Host "`nAPPLIED with $($after.Gaps.Count) gap(s) remaining (see above)." -ForegroundColor Red
exit 1
