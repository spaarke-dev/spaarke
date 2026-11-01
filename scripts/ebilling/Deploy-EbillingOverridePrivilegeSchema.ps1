<#
.SYNOPSIS
    eBilling R1 task 105 (D-76) - creates the carrier of the "eBilling Override" privilege: the small dedicated table
    sprk_ebillingoverride, and the security role "eBilling Override" that holds Write on it.

.DESCRIPTION
    Idempotent, additive Dataverse Web API script (dev only in R1). Every item is check-then-create; a second run reports every item as
    skipped and writes nothing. Run -WhatIf first: it reads everything, prints what it would create and writes nothing.

    Why (decisions-log D-76; project CLAUDE.md): Override of a blocked internal submission needs "the eBilling Override privilege".
    Dataverse has no custom privilege type, so a privilege needs a TABLE to belong to. The BFF asks the CALLER's own Dataverse whether
    they hold Write on this table (CallerRecordAccessProbe.CallerHoldsPrivilegeAsync, on-behalf-of, uncached, fail closed;
    Services/Finance/EbillingOverridePrivilege.cs). No security-role name is checked and no Entra app role is involved. The same
    mechanism, on sprk_ruleset, makes someone a billing admin (D-108).

    Items:
      1  Table sprk_ebillingoverride, "eBilling Override", ORGANIZATION-owned, primary name sprk_name (100). It holds no rows and has
         no other column: it exists only so Dataverse generates prvCreate / prvRead / prvWrite / prvDelete / prvAppend / prvAppendTo
         for it. Only prvWritesprk_ebillingoverride is used. An organization-owned table offers Global depth only.
      2  Security role "eBilling Override" in the root business unit, with Write on sprk_ebillingoverride at Global depth and nothing
         else. An administrator gives it to the people allowed to override (alongside their normal role), or adds the same privilege
         to a role of their own. The role carries no other privilege, so holding it grants nothing else.

    The privilege name is prv + Write + the table's SCHEMA name, with its case. The table is created with an all-lower-case schema name
    (as sprk_ruleset), so the privilege is prvWritesprk_ebillingoverride; the script reads it back and STOPS if it is named otherwise.

    NEVER removes or lowers anything. If sprk_ebillingoverride already exists with another primary name, or the role exists but is not a
    single root role, the script STOPS with exit code 2 (an escalation) and changes nothing further.

    No publish of anything but the one table (entity-scoped, never a tenant-wide publish); privileges and roles are not published. The table and
    role are added to the unmanaged solution named by -SolutionUniqueName; the SpaarkeMaster solution source is synced by the deploy
    task 112 (project CLAUDE.md section 3), not here.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the created components are added to (header MSCRM.SolutionUniqueName).

.PARAMETER RoleName
    The role created to carry the privilege. Default: eBilling Override.

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingOverridePrivilegeSchema.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingOverridePrivilegeSchema.ps1

.NOTES
    Project: ebilling-r1, task 105. Dev only in R1. Dataverse helpers: EbillingSchema.psm1. Patterns: Deploy-EbillingExternalIdentifierSchema.ps1
    (table) and Deploy-EbillingRolePrivileges.ps1 (role privileges).
    Exit codes: 0 = done (created / skipped), 1 = error, 2 = escalation (a conflicting existing item; nothing further changed).
#>

using module .\EbillingSchema.psm1

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = $(if ($env:DATAVERSE_URL) { $env:DATAVERSE_URL } else { 'https://spaarkedev1.crm.dynamics.com' }),
    [string]$SolutionUniqueName = 'SpaarkeMaster',
    [string]$RoleName = 'eBilling Override'
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$DryRun = [bool]$WhatIfPreference
$Table = 'sprk_ebillingoverride'
$PrivilegeName = "prvWrite$Table"

$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$DryRun -Entity $Table
$script:Created = $State.Created
$script:Changed = $State.Changed
$script:Skipped = $State.Skipped
$script:Planned = $State.Planned

# The root role of a name (the role in the root business unit; the per-business-unit copies point at it). $null when there is none.
function Get-RootRole([string]$Name) {
    $r = Invoke-Dv "roles?`$select=roleid,name,_businessunitid_value,_parentrootroleid_value&`$filter=name eq '$Name'"
    $all = @($r.value)
    if ($all.Count -eq 0) { return $null }
    $root = @($all | Where-Object { $_._parentrootroleid_value -eq $_.roleid })
    if ($root.Count -ne 1) { throw [EscalationException]::new("Role '$Name' exists but is not a single root role in $EnvironmentUrl ($($all.Count) rows, $($root.Count) root). Nothing was changed.") }
    return [pscustomobject]@{ Id = [string]$root[0].roleid; BusinessUnitId = [string]$root[0]._businessunitid_value }
}

function Get-RootBusinessUnitId {
    $r = Invoke-Dv "businessunits?`$select=businessunitid&`$filter=parentbusinessunitid eq null"
    $all = @($r.value)
    if ($all.Count -ne 1) { throw [EscalationException]::new("Expected exactly one root business unit, found $($all.Count). Nothing was changed.") }
    return [string]$all[0].businessunitid
}

# privilege id (lower case) -> depth, for a role.
function Get-RoleDepths([string]$RoleId) {
    $r = Invoke-Dv "RetrieveRolePrivilegesRole(RoleId=$RoleId)"
    $m = @{}
    foreach ($p in @($r.RolePrivileges)) { $m[([string]$p.PrivilegeId).ToLower()] = [string]$p.Depth }
    return $m
}

Write-Host ""
Write-Host "eBilling task 105 - the eBilling Override privilege (D-76)" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Solution: $SolutionUniqueName   Role: $RoleName   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })"
Write-Host ""

try {
    # --- 1. The carrier table ------------------------------------------------------------------------------------------
    Write-Host "1. Table $Table" -ForegroundColor Cyan
    Add-Table -Table $Table -Display 'eBilling Override' -Plural 'eBilling Overrides' -Ownership 'OrganizationOwned' `
        -Description 'Carrier of the "eBilling Override" privilege (ebilling-r1 D-76). Holds no rows. A user who holds Write on this table may accept an internal invoice submission that the rejection rule blocked.' `
        -PrimaryColumn 'sprk_name' -PrimaryDisplay 'Name' -PrimaryDescription 'Not used: the table holds no rows.' -PrimaryLength 100
    Set-PrimaryNameLength $Table 'sprk_name' 100

    Write-Host "Publish" -ForegroundColor Cyan
    $wrote = ($script:Created.Count + $script:Changed.Count) -gt 0
    if ($DryRun) {
        Write-Host "  WhatIf: publish skipped" -ForegroundColor Cyan
    } elseif (-not $wrote) {
        Write-Host "  nothing changed - no publish" -ForegroundColor Yellow
    } else {
        Invoke-Dv -Path 'PublishXml' -Method POST -Body @{ ParameterXml = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>" } | Out-Null
        Write-Host "  customizations published" -ForegroundColor Green
    }

    # --- the privilege Dataverse generated for the table ----------------------------------------------------------------
    $priv = $null
    if (-not $DryRun -or ($script:Planned -notcontains $Table)) {
        for ($i = 0; $i -lt 12 -and -not $priv; $i++) {
            $r = Invoke-Dv "privileges?`$select=name,privilegeid,canbeglobal,canbedeep&`$filter=name eq '$PrivilegeName'"
            $priv = @($r.value) | Where-Object { $_.name -ceq $PrivilegeName } | Select-Object -First 1
            if (-not $priv -and -not $DryRun) { Start-Sleep -Seconds 5 }
            if ($DryRun) { break }
        }
        if (-not $priv) { throw [EscalationException]::new("Privilege $PrivilegeName was not found after the table exists. The BFF asks for exactly this name (EbillingOverridePrivilege.PrivilegeName). Nothing further was changed.") }
        if (-not $priv.canbeglobal) { throw [EscalationException]::new("$PrivilegeName cannot be held at Global depth, which the role needs. Nothing further was changed.") }
        Write-Host ("  privilege {0} ({1}) exists" -f $priv.name, $priv.privilegeid)
    } else {
        Write-Host "  WhatIf: $PrivilegeName will exist once the table is created" -ForegroundColor Cyan
    }

    # --- 2. The role -----------------------------------------------------------------------------------------------------
    Write-Host "2. Role $RoleName" -ForegroundColor Cyan
    $role = Get-RootRole $RoleName
    if ($role) {
        $script:Skipped.Add("role $RoleName"); Write-Host "  skipped  role $RoleName (exists, $($role.Id))" -ForegroundColor Yellow
    } elseif ($DryRun) {
        $script:Created.Add("role $RoleName"); Write-Host "  WOULD CREATE role $RoleName in the root business unit" -ForegroundColor Cyan
    } else {
        $bu = Get-RootBusinessUnitId
        Invoke-Dv -Path 'roles' -Method POST -Body @{ name = $RoleName; 'businessunitid@odata.bind' = "/businessunits($bu)" } -ExtraHeaders $State.SolutionHeader | Out-Null
        for ($i = 0; $i -lt 12 -and -not $role; $i++) {
            $role = Get-RootRole $RoleName
            if (-not $role) { Start-Sleep -Seconds 3 }
        }
        if (-not $role) { throw "Role $RoleName was created but is not readable after 36 s." }
        $script:Created.Add("role $RoleName"); Write-Host "  CREATED  role $RoleName ($($role.Id))" -ForegroundColor Green
    }

    # --- the role's one privilege ------------------------------------------------------------------------------------------
    if ($role -and $priv) {
        $have = Get-RoleDepths $role.Id
        $key = ([string]$priv.privilegeid).ToLower()
        if ($have.ContainsKey($key) -and $have[$key] -eq 'Global') {
            $script:Skipped.Add("role $RoleName holds $PrivilegeName"); Write-Host "  skipped  $PrivilegeName (held at Global)" -ForegroundColor Yellow
        } elseif ($DryRun) {
            $script:Changed.Add("role $RoleName + $PrivilegeName Global"); Write-Host "  WOULD ADD $PrivilegeName at Global" -ForegroundColor Cyan
        } else {
            $body = @{ Privileges = @(@{ Depth = 'Global'; PrivilegeId = [string]$priv.privilegeid; BusinessUnitId = $role.BusinessUnitId }) }
            Invoke-Dv -Path "roles($($role.Id))/Microsoft.Dynamics.CRM.AddPrivilegesRole" -Method POST -Body $body | Out-Null
            $after = $null
            for ($i = 0; $i -lt 6; $i++) {
                $after = Get-RoleDepths $role.Id
                if ($after.ContainsKey($key) -and $after[$key] -eq 'Global') { break }
                Start-Sleep -Seconds 3
            }
            if (-not ($after.ContainsKey($key) -and $after[$key] -eq 'Global')) { throw "AddPrivilegesRole on '$RoleName' did not take for $PrivilegeName." }
            $script:Changed.Add("role $RoleName + $PrivilegeName Global"); Write-Host "  ADDED    $PrivilegeName at Global" -ForegroundColor Green
        }
    } elseif ($DryRun) {
        Write-Host "  WhatIf: the role's privilege is added once the table and role exist" -ForegroundColor Cyan
    }
}
catch [EscalationException] {
    Write-Host ""
    Write-Host "ESCALATION - STOPPED, nothing further was changed: $($_.Exception.Message)" -ForegroundColor Red
    exit 2
}
catch {
    Write-Host ""
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Items already created stay (the script is re-runnable)." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "================ Summary ================" -ForegroundColor Cyan
Write-Host ("created : {0}  {1}" -f $script:Created.Count, ($script:Created -join ', '))
Write-Host ("changed : {0}  {1}" -f $script:Changed.Count, ($script:Changed -join ', '))
Write-Host ("skipped : {0}" -f $script:Skipped.Count)
if ($DryRun) { Write-Host "WhatIf run: nothing was written." -ForegroundColor Cyan }
Write-Host "NEXT: an administrator assigns the role '$RoleName' to the people allowed to override. The deploy task 112 syncs the table and role into SpaarkeMaster." -ForegroundColor DarkYellow
exit 0
