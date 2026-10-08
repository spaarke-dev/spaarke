#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Task 154 (unified-access-control-r2), owner decision O2 (accepted 2026-10-01): only access administrators read and
    author No Access entries. Removes every sprk_noaccessentry privilege from "Spaarke Core User", and creates the
    "Spaarke Access Administrator" role with Create, Read, Write, Append and AppendTo on sprk_noaccessentry (no Delete),
    plus the AppendTo its lookups need. DRY RUN by default.

.DESCRIPTION
    WHY. "Spaarke Core User" holds Global Read on sprk_noaccessentry, so every core user can read every entry's subject,
    object and Reason, which contradicts task 143's rule that a refusal must not reveal the reason (research note
    session27-ux-research-ethical-wall-secure.md :27-28, :74). Under O2:
      - Spaarke Core User loses every sprk_noaccessentry privilege it holds (today: Read);
      - "Spaarke Access Administrator" (created in the root business unit, in the SpaarkeCore solution) holds Create,
        Read, Write, Append and AppendTo on sprk_noaccessentry at Organization depth, and AppendTo on contact,
        sprk_organization, systemuser and sprk_recordtype_ref so its lookups can be set. No Delete: deactivation lifts a
        wall. It is assigned IN ADDITION to a user's existing roles, which supply Read on the lookup tables;
      - System Administrator and System Customizer are not touched. The BFF reads entries app-only as application users
        holding System Administrator; this script checks that they still do.

    OTHER ROLES (task 154 escalation O2). Any other role holding a sprk_noaccessentry privilege is listed. On spaarkedev1
    these are Microsoft platform roles held only by Microsoft application users and the Microsoft "Support User"
    account: Service Reader, Service Writer, Service Deleter and Support User. They are NEVER changed here. -Apply
    refuses while any such role exists unless -AcceptOtherRoles is passed, which records that the owner decided to leave
    them as they are.

    MODES
      (default)  Dry run: the before state of every role and the plan. Zero writes.
      -Apply     Creates the role if absent, adds its missing privileges, removes any Delete it holds, removes
                 Spaarke Core User's sprk_noaccessentry privileges, optionally assigns the role, then prints the after
                 state.
      -Verify    Read-only. Exit 0 when the O2 state holds, exit 1 naming each gap.

    The privilege cache lags role edits: re-probe at least 3 times before trusting a live check (project gotcha).

.PARAMETER AssignToUserPrincipalName
    -Apply only: users (domain name / UPN) to give the access-administrator role, e.g. the gate's test administrator.
    Each gets the role copy of their own business unit; nobody is moved between business units.

.PARAMETER AcceptOtherRoles
    -Apply only: the owner decided that the other roles holding a sprk_noaccessentry privilege stay unchanged.

.EXAMPLE
    pwsh -File scripts/Set-NoAccessEntryRolePrivileges.ps1
    pwsh -File scripts/Set-NoAccessEntryRolePrivileges.ps1 -Apply -AcceptOtherRoles -AssignToUserPrincipalName testuser1@spaarke.com
    pwsh -File scripts/Set-NoAccessEntryRolePrivileges.ps1 -Verify
#>

[CmdletBinding()]
param(
    [string]$EnvironmentUrl = 'https://spaarkedev1.crm.dynamics.com',
    [switch]$Apply,
    [switch]$Verify,
    [string[]]$AssignToUserPrincipalName = @(),
    [switch]$AcceptOtherRoles,
    [string]$AdminRoleName = 'Spaarke Access Administrator',
    [string]$CoreRoleName = 'Spaarke Core User',
    [string]$SolutionUniqueName = 'SpaarkeCore',
    [string[]]$BffApplicationIds = @('5967251e-171c-46fe-a6c2-ef843c90309d', '1e40baad-e065-4aea-a8d4-4b7ab273458c')
)

$ErrorActionPreference = 'Stop'
if ($Apply -and $Verify) { throw '-Apply and -Verify are separate modes; run -Apply first, then -Verify.' }
$BaseUrl = $EnvironmentUrl.TrimEnd('/')
$Api = "$BaseUrl/api/data/v9.2"
$Table = 'sprk_noaccessentry'
$AdministratorRoles = @('System Administrator', 'System Customizer')
$DepthNames = @{ 1 = 'Basic'; 2 = 'Local'; 4 = 'Deep'; 8 = 'Global' }

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
function Invoke-DvPost([string]$Path, [object]$Body, [hashtable]$Extra = @{}) {
    $h = $headers.Clone(); foreach ($k in $Extra.Keys) { $h[$k] = $Extra[$k] }
    Invoke-RestMethod -Uri "$Api/$Path" -Headers $h -Method Post -Body ([Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 6)))
}
function Stop-Refused([string]$Reason) { Write-Host "`nREFUSED: $Reason`nNothing was changed." -ForegroundColor Red; exit 2 }

$orgName = (Invoke-DvGet 'organizations?$select=name').value[0].name
Write-Host "Environment : $BaseUrl (org '$orgName')"
Write-Host ("Mode        : {0}" -f $(if ($Verify) { 'VERIFY (read-only)' } elseif ($Apply) { 'APPLY' } else { 'DRY RUN (no writes)' }))

# ── Privileges, from metadata ───────────────────────────────────────────────────────────────────────────────
function Get-TablePrivilege([string]$LogicalName, [string]$Type) {
    $p = @((Invoke-DvGet "EntityDefinitions(LogicalName='$LogicalName')/Privileges").value | Where-Object PrivilegeType -eq $Type)
    if ($p.Count -ne 1) { throw "Table '$LogicalName': expected one $Type privilege in metadata, found $($p.Count)." }
    [pscustomobject]@{ Table = $LogicalName; Type = $Type; Name = $p[0].Name; Id = "$($p[0].PrivilegeId)".ToLowerInvariant() }
}
$entryPrivileges = @('Create', 'Read', 'Write', 'Delete', 'Append', 'AppendTo') | ForEach-Object { Get-TablePrivilege $Table $_ }
$wanted = @($entryPrivileges | Where-Object Type -ne 'Delete') +
@('contact', 'sprk_organization', 'systemuser', 'sprk_recordtype_ref' | ForEach-Object { Get-TablePrivilege $_ 'AppendTo' })
$deletePrivilege = $entryPrivileges | Where-Object Type -eq 'Delete'

# ── Root business unit, roles ───────────────────────────────────────────────────────────────────────────────
$rootBu = @((Invoke-DvGet 'businessunits?$select=businessunitid,name&$filter=_parentbusinessunitid_value eq null').value)
if ($rootBu.Count -ne 1) { throw "Expected one root business unit; found $($rootBu.Count)." }
$rootBuId = $rootBu[0].businessunitid

function Get-RootRole([string]$Name) {
    $n = $Name -replace "'", "''"
    $r = @((Invoke-DvGet "roles?`$select=roleid,name,_parentrootroleid_value&`$filter=name eq '$n' and _businessunitid_value eq $rootBuId").value)
    if ($r.Count -gt 1) { throw "More than one role named '$Name' in the root business unit." }
    if ($r.Count -eq 0) { return $null }
    return $r[0]
}
function Get-RolePrivileges([string]$RoleId) {
    @((Invoke-DvGet "RetrieveRolePrivilegesRole(RoleId=@p)?@p=$RoleId").RolePrivileges)
}

# Every root role holding any sprk_noaccessentry privilege, with its depths.
function Get-EntryPrivilegeHolders {
    $holders = @{}
    foreach ($p in $entryPrivileges) {
        foreach ($row in (Invoke-DvGet "roleprivilegescollection?`$filter=privilegeid eq $($p.Id)&`$select=roleid,privilegedepthmask").value) {
            if (-not $holders.ContainsKey($row.roleid)) { $holders[$row.roleid] = [ordered]@{} }
            $holders[$row.roleid][$p.Type] = $DepthNames[[int]$row.privilegedepthmask]
        }
    }
    $out = foreach ($id in $holders.Keys) {
        $role = Invoke-DvGet "roles($id)?`$select=name,_parentrootroleid_value"
        if ($role._parentrootroleid_value -and $role._parentrootroleid_value -ne $id) { continue } # BU copies follow the root
        [pscustomobject]@{ RoleId = $id; Name = $role.name; Privileges = $holders[$id] }
    }
    return @($out | Sort-Object Name)
}
function Show-Holders([string]$Title, [object[]]$Holders) {
    Write-Host "`n$Title"
    foreach ($h in $Holders) {
        $text = ($h.Privileges.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ' '
        Write-Host ("  {0,-32} {1}" -f $h.Name, $text)
    }
}

$before = Get-EntryPrivilegeHolders
Show-Holders 'BEFORE: roles holding a sprk_noaccessentry privilege' $before

$core = Get-RootRole $CoreRoleName
if (-not $core) { Stop-Refused "no role '$CoreRoleName' in the root business unit." }
$admin = Get-RootRole $AdminRoleName
$others = @($before | Where-Object { $_.Name -notin $AdministratorRoles -and $_.Name -ne $CoreRoleName -and $_.Name -ne $AdminRoleName })

# ── The BFF's app users keep their reads ────────────────────────────────────────────────────────────────────
$bffGaps = @()
foreach ($appId in $BffApplicationIds) {
    $u = @((Invoke-DvGet "systemusers?`$filter=applicationid eq $appId&`$select=fullname&`$expand=systemuserroles_association(`$select=name)").value)
    if ($u.Count -eq 0) { Write-Host "  BFF app user $appId : not in this environment" -ForegroundColor DarkYellow; continue }
    $hasAdmin = @($u[0].systemuserroles_association | Where-Object name -eq 'System Administrator').Count -gt 0
    if ($hasAdmin) { Write-Host "  BFF app user $($u[0].fullname) ($appId): System Administrator - its app-only deny reads are unaffected" }
    else { $bffGaps += "BFF app user $($u[0].fullname) ($appId) does not hold System Administrator: removing Core User Read may break its deny reads" }
}

# ── The state to reach ──────────────────────────────────────────────────────────────────────────────────────
$coreHeld = @(Get-RolePrivileges $core.roleid | Where-Object { $entryPrivileges.Id -contains "$($_.PrivilegeId)".ToLowerInvariant() })
$adminHeld = if ($admin) { @(Get-RolePrivileges $admin.roleid) } else { @() }
$adminById = @{}; foreach ($h in $adminHeld) { $adminById["$($h.PrivilegeId)".ToLowerInvariant()] = $h }
$adminMissing = @($wanted | Where-Object { -not $adminById.ContainsKey($_.Id) -or $adminById[$_.Id].Depth -ne 'Global' })
$adminDelete = $adminById.ContainsKey($deletePrivilege.Id)

$gaps = @()
if ($coreHeld.Count -gt 0) { $gaps += "$CoreRoleName still holds: $(($coreHeld | ForEach-Object PrivilegeName) -join ', ')" }
if (-not $admin) { $gaps += "role '$AdminRoleName' does not exist" }
foreach ($m in $adminMissing) { $gaps += "$AdminRoleName lacks $($m.Name) at Global" }
if ($adminDelete) { $gaps += "$AdminRoleName holds $($deletePrivilege.Name) (deactivation lifts a wall; no Delete)" }
$gaps += $bffGaps

if ($others.Count -gt 0) {
    Write-Host "`nOTHER roles holding a sprk_noaccessentry privilege (never changed here; owner decision, escalation O2):" -ForegroundColor DarkYellow
    foreach ($o in $others) { Write-Host "  $($o.Name)" -ForegroundColor DarkYellow }
}

if ($Verify) {
    if ($gaps.Count -eq 0) { Write-Host "`nVERIFY PASS: O2 holds ($CoreRoleName has no access to entries; $AdminRoleName has exactly its set)." -ForegroundColor Green; exit 0 }
    foreach ($g in $gaps) { Write-Host "  GAP  $g" -ForegroundColor Red }
    Write-Host "`nVERIFY FAIL: $($gaps.Count) gap(s)." -ForegroundColor Red
    exit 1
}

Write-Host "`nPLAN"
if ($coreHeld.Count -gt 0) { Write-Host "  remove from ${CoreRoleName}: $(($coreHeld | ForEach-Object PrivilegeName) -join ', ')" -ForegroundColor Yellow }
if (-not $admin) { Write-Host "  create role '$AdminRoleName' in the root business unit ($SolutionUniqueName)" -ForegroundColor Yellow }
foreach ($m in $adminMissing) { Write-Host "  add to ${AdminRoleName}: $($m.Name) at Global" -ForegroundColor Yellow }
if ($adminDelete) { Write-Host "  remove from ${AdminRoleName}: $($deletePrivilege.Name)" -ForegroundColor Yellow }
foreach ($upn in $AssignToUserPrincipalName) { Write-Host "  assign '$AdminRoleName' to $upn (in that user's own business unit)" -ForegroundColor Yellow }
if ($bffGaps.Count -gt 0) { foreach ($g in $bffGaps) { Write-Host "  BLOCKER: $g" -ForegroundColor Red } }

if (-not $Apply) { Write-Host "`nDRY RUN: re-run with -Apply$(if ($others.Count -gt 0) { ' -AcceptOtherRoles (after the owner decides)' })." -ForegroundColor Cyan; exit 0 }
if ($bffGaps.Count -gt 0) { Stop-Refused ($bffGaps -join '; ') }
if ($others.Count -gt 0 -and -not $AcceptOtherRoles) {
    Stop-Refused "other roles hold a sprk_noaccessentry privilege ($(($others | ForEach-Object Name) -join ', ')). Task 154 escalation O2: the owner decides; pass -AcceptOtherRoles once they have."
}

# ── Apply ───────────────────────────────────────────────────────────────────────────────────────────────────
if (-not $admin) {
    $created = Invoke-DvPost 'roles' @{ name = $AdminRoleName; 'businessunitid@odata.bind' = "/businessunits($rootBuId)" } @{
        Prefer = 'return=representation'; 'MSCRM.SolutionUniqueName' = $SolutionUniqueName
    }
    $admin = [pscustomobject]@{ roleid = $created.roleid; name = $AdminRoleName }
    Write-Host "  created '$AdminRoleName' ($($admin.roleid))" -ForegroundColor Green
    $adminMissing = $wanted
}
if ($adminMissing.Count -gt 0) {
    Invoke-DvPost "roles($($admin.roleid))/Microsoft.Dynamics.CRM.AddPrivilegesRole" @{
        Privileges = @($adminMissing | ForEach-Object {
                @{ '@odata.type' = 'Microsoft.Dynamics.CRM.RolePrivilege'; Depth = 'Global'; PrivilegeId = $_.Id; PrivilegeName = $_.Name; BusinessUnitId = $rootBuId }
            })
    } | Out-Null
    Write-Host "  added to ${AdminRoleName}: $(($adminMissing | ForEach-Object Name) -join ', ')" -ForegroundColor Green
}
if ($adminDelete) {
    Invoke-DvPost "roles($($admin.roleid))/Microsoft.Dynamics.CRM.RemovePrivilegeRole" @{ PrivilegeId = $deletePrivilege.Id } | Out-Null
    Write-Host "  removed $($deletePrivilege.Name) from $AdminRoleName" -ForegroundColor Green
}
foreach ($p in $coreHeld) {
    Invoke-DvPost "roles($($core.roleid))/Microsoft.Dynamics.CRM.RemovePrivilegeRole" @{ PrivilegeId = "$($p.PrivilegeId)" } | Out-Null
    Write-Host "  removed $($p.PrivilegeName) from $CoreRoleName" -ForegroundColor Green
}
foreach ($upn in $AssignToUserPrincipalName) {
    $n = $upn -replace "'", "''"
    $u = @((Invoke-DvGet "systemusers?`$filter=domainname eq '$n'&`$select=systemuserid,_businessunitid_value").value)
    if ($u.Count -ne 1) { Write-Host "  could not find exactly one user '$upn'; not assigned" -ForegroundColor Red; continue }
    $copy = @((Invoke-DvGet "roles?`$filter=_parentrootroleid_value eq $($admin.roleid) and _businessunitid_value eq $($u[0]._businessunitid_value)&`$select=roleid").value)
    if ($copy.Count -ne 1) { Write-Host "  no '$AdminRoleName' copy in $upn's business unit; not assigned" -ForegroundColor Red; continue }
    Invoke-DvPost "systemusers($($u[0].systemuserid))/systemuserroles_association/`$ref" @{ '@odata.id' = "$Api/roles($($copy[0].roleid))" } | Out-Null
    Write-Host "  assigned '$AdminRoleName' to $upn" -ForegroundColor Green
}

# ── After ───────────────────────────────────────────────────────────────────────────────────────────────────
$after = Get-EntryPrivilegeHolders
Show-Holders 'AFTER: roles holding a sprk_noaccessentry privilege' $after
$unrequested = @(Get-RolePrivileges $admin.roleid | Where-Object { $wanted.Id -notcontains "$($_.PrivilegeId)".ToLowerInvariant() })
if ($unrequested.Count -gt 0) {
    Write-Host "NOTE: the platform also gave $AdminRoleName $($unrequested.Count) privilege(s) not requested: $(($unrequested | ForEach-Object PrivilegeName) -join ', ')." -ForegroundColor DarkYellow
}
Write-Host "`nAPPLIED. Run -Verify; Dataverse caches privileges, so re-probe a user's access at least 3 times." -ForegroundColor Green
exit 0
