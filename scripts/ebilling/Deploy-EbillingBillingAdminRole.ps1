<#
.SYNOPSIS
    eBilling R1 task 120 (D-76, D-108, D-185 / #2285) - creates the "Spaarke Billing Administrator" role and removes Read on
    sprk_ruleset from Spaarke Core User and Spaarke Basic User, so that only billing roles read rule sets.

.DESCRIPTION
    Idempotent Dataverse Web API script (dev only in R1), shaped like Deploy-EbillingRolePrivileges.ps1: every step is read,
    compare, then write; a second run reports no change and writes nothing.

    Why. A billing admin is whoever holds Write on sprk_ruleset (BillingAdminAuthorization, D-76; any depth, D-108). No role
    granted it, so only System Administrators passed the billing-admin routes. Owner decision D-185 (#2285, via ontology-r1):
    only billing roles may read rule sets, so Core and Basic User lose the Read on sprk_ruleset that D-90 gave them.

    What it does (the role is edited in the ROOT business unit; Dataverse carries it to the per-business-unit copies, which the
    script checks):
      1. "Spaarke Billing Administrator" exists with the FIXED role id 810926e0-a97c-44ea-9891-e80dadef73f0 (the id of
         src/dataverse/solutions/SpaarkeMaster/Roles/Spaarke Billing Administrator.xml), in the root business unit and in the
         SpaarkeMaster solution. Created when absent (AddPrivilegesRole re-injects the platform defaults every Spaarke role
         carries: the plugin-metadata reads and the SharePoint four).
      2. It holds Read at Global, and Create / Write / Append / AppendTo at Basic (User), on sprk_ruleset, sprk_Policy and
         sprk_PolicyVersion, added with AddPrivilegesRole. Basic, not Global (uac-r2 review of the task 120 pre-notice): the
         BFF uses Create / Write only as markers at any depth (D-108) and writes these tables app-only (D-173), so Global
         would only let a billing admin create or edit any rule set or policy natively, bypassing RuleSetService's
         invariants (ADR-002). A privilege held deeper than that stops a plain run (exit 2; this script never lowers one).
         Never Delete, Assign or Share; nothing on sprk_procedure, sprk_procedureversion or sprk_procedureassignment (task
         121 adds those after uac-r2 task 191). Holding one of these stops the run (exit 2).
      3. Spaarke Core User and Spaarke Basic User: prvReadsprk_ruleset is removed with RemovePrivilegeRole, one call per
         root role. That is the ONLY removal this script makes. If either role holds any OTHER privilege on sprk_ruleset, or
         holds the Read at a depth other than Deep (the depth the role source showed), the run stops before writing (exit 2).
      No ReplacePrivilegesRole. No sprk_Policy / sprk_PolicyVersion privilege of an existing role is touched (ontology-r1 owns
      those, #2298); the script prints them for Core, Basic and Console User so a live #2298 change is visible.

    ASSIGNMENT (uac-r2 K-1). The role is assigned to USERS DIRECTLY, never to a team: flagged-external systemusers hold the
    roles of their business unit's default team. This script assigns the role to nobody; who gets it is the owner's decision.
    -Verify fails when any team holds it.

    Modes:
      -WhatIf   reads everything, prints the plan and what -Verify would report; writes nothing.
      (plain)   applies the plan; reads every change back (root role and per-business-unit copies).
      -Verify   read-only; exit 0 when the state above holds, exit 1 naming every gap (drift).

    No publish is needed (privileges are not a published customization). The role source in
    src/dataverse/solutions/SpaarkeMaster/Roles is kept in step by hand (projects/ebilling-r1/notes/120-billing-admin-role.md);
    RuleSetRolePrivilegeGuardTests pins it.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    The solution the role belongs to (created under its header; added with AddSolutionComponent when missing).

.PARAMETER Verify
    Read-only drift check. Exit 0 = the state holds, 1 = drift (each gap is listed).

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingBillingAdminRole.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingBillingAdminRole.ps1
    pwsh scripts/ebilling/Deploy-EbillingBillingAdminRole.ps1 -Verify

.NOTES
    Project: ebilling-r1, task 120. Dataverse helpers: EbillingSchema.psm1; solution membership: scripts/common/DataverseSolutionMembership.ps1.
    Exit codes: 0 = done / no drift, 1 = error or drift (-Verify), 2 = escalation (a refused guard; nothing further changed).
#>

using module .\EbillingSchema.psm1

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = $(if ($env:DATAVERSE_URL) { $env:DATAVERSE_URL } else { 'https://spaarkedev1.crm.dynamics.com' }),
    [string]$SolutionUniqueName = 'SpaarkeMaster',
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$ReadOnly = [bool]$WhatIfPreference -or $Verify
. (Join-Path $PSScriptRoot '..' 'common' 'DataverseSolutionMembership.ps1')

$RoleId = '810926e0-a97c-44ea-9891-e80dadef73f0'
$RoleName = 'Spaarke Billing Administrator'
$RoleDescription = 'Spaarke billing administrator (ebilling-r1 D-76, D-108, D-185): reviews, edits, drafts and publishes billing rule sets; the only Spaarke role that reads rule sets. Assign it to users directly, never to a team.'
# logical name -> schema name as the live privilege names spell it (BillingAdminAuthorization).
$AdminTables = [ordered]@{ sprk_ruleset = 'sprk_ruleset'; sprk_policy = 'sprk_Policy'; sprk_policyversion = 'sprk_PolicyVersion' }
$AdminVerbs = 'Create', 'Read', 'Write', 'Append', 'AppendTo'
# The depth of each, the same on all three tables (uac-r2: only Read is Global; the rest are markers, held at Basic).
$AdminDepth = @{ Create = 'Basic'; Read = 'Global'; Write = 'Basic'; Append = 'Basic'; AppendTo = 'Basic' }
$Rank = @{ Basic = 0; Local = 1; Deep = 2; Global = 3 }
$Verbs = 'Create', 'Read', 'Write', 'Delete', 'Append', 'AppendTo', 'Assign', 'Share'
$ProcedureTables = 'sprk_procedure', 'sprk_procedureversion', 'sprk_procedureassignment'
$PlatformDefaults = 'prvReadPluginAssembly', 'prvReadPluginType', 'prvReadSdkMessage', 'prvReadSdkMessageProcessingStep',
'prvReadSdkMessageProcessingStepImage', 'prvCreateSharePointData', 'prvReadSharePointData', 'prvWriteSharePointData', 'prvReadSharePointDocument'
$StripRoles = 'Spaarke Core User', 'Spaarke Basic User'
$StripPrivilegeName = 'prvReadsprk_ruleset'
$StripSourceDepth = 'Deep'   # the depth the role source showed before task 120
$PolicyWatchRoles = 'Spaarke Core User', 'Spaarke Basic User', 'Spaarke Console User'
$DepthNames = @{ 1 = 'Basic'; 2 = 'Local'; 4 = 'Deep'; 8 = 'Global' }

$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$ReadOnly
$script:Changed = $State.Changed
$script:Skipped = $State.Skipped
$Gaps = [System.Collections.Generic.List[string]]::new()

# ---------------------------------------------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------------------------------------------

function Get-TableInfo([string]$Table) {
    $e = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=SchemaName,OwnershipType" -Retries 2
    if (-not $e) { throw "$Table does not exist in $EnvironmentUrl." }
    return [pscustomobject]@{ Table = $Table; Schema = [string]$e.SchemaName; Ownership = [string]$e.OwnershipType }
}

# verb -> privilege (name, id, can be Basic / Global) of one table.
function Get-TablePrivileges($info) {
    $r = Invoke-Dv "privileges?`$select=name,privilegeid,canbebasic,canbeglobal&`$filter=endswith(name,'$($info.Schema)')"
    $map = [ordered]@{}
    foreach ($v in $Verbs) {
        $p = @($r.value | Where-Object { $_.name -ceq "prv$v$($info.Schema)" })
        if ($p.Count -eq 1) { $map[$v] = [pscustomobject]@{ Verb = $v; Name = [string]$p[0].name; Id = ([string]$p[0].privilegeid).ToLower(); Basic = [bool]$p[0].canbebasic; Global = [bool]$p[0].canbeglobal } }
    }
    return $map
}

function Get-RootBusinessUnitId {
    $r = @((Invoke-Dv 'businessunits?$select=businessunitid&$filter=_parentbusinessunitid_value eq null').value)
    if ($r.Count -ne 1) { throw "Expected one root business unit, found $($r.Count)." }
    return ([string]$r[0].businessunitid).ToLower()
}

# The root role of a name (the copy whose parent root role is itself).
function Get-RootRole([string]$Name) {
    $n = $Name -replace "'", "''"
    $r = Invoke-Dv "roles?`$select=roleid,name,_businessunitid_value,_parentrootroleid_value&`$filter=name eq '$n'"
    $root = @($r.value | Where-Object { $_._parentrootroleid_value -eq $_.roleid })
    if ($root.Count -gt 1) { throw "More than one root role is named '$Name'." }
    if ($root.Count -eq 0) { return $null }
    return [pscustomobject]@{ Id = ([string]$root[0].roleid).ToLower(); Name = $Name; BusinessUnitId = ([string]$root[0]._businessunitid_value).ToLower() }
}

# privilege id (lower case) -> { Name, Depth } for a role.
function Get-RolePrivilegeMap([string]$Id) {
    $r = Invoke-Dv "RetrieveRolePrivilegesRole(RoleId=$Id)"
    $m = @{}
    foreach ($p in @($r.RolePrivileges)) { $m[([string]$p.PrivilegeId).ToLower()] = [pscustomobject]@{ Name = [string]$p.PrivilegeName; Depth = [string]$p.Depth } }
    return $m
}

function Get-RoleCopies([string]$RootId) {
    return @((Invoke-Dv "roles?`$select=roleid,_businessunitid_value&`$filter=_parentrootroleid_value eq $RootId and roleid ne $RootId").value)
}

# Re-read until the test passes (role edits settle after a few seconds), at most $Tries times.
function Wait-Settled([scriptblock]$Read, [scriptblock]$Test, [int]$Tries = 6, [int]$Seconds = 3) {
    $v = $null
    for ($i = 0; $i -lt $Tries; $i++) {
        $v = & $Read
        if (& $Test $v) { return $v }
        Start-Sleep -Seconds $Seconds
    }
    return $v
}

function Format-Held($map, [object[]]$privs) {
    $held = foreach ($p in $privs) { if ($map.ContainsKey($p.Id)) { "$($p.Verb)=$($map[$p.Id].Depth)" } }
    if ($held) { return ($held -join ' ') } else { return '(none)' }
}

# ---------------------------------------------------------------------------------------------------------------
# Run
# ---------------------------------------------------------------------------------------------------------------

Write-Host ""
Write-Host "eBilling task 120 - Spaarke Billing Administrator; D-185: only billing roles read rule sets" -ForegroundColor Cyan
Write-Host ("Environment: {0}   Mode: {1}" -f $EnvironmentUrl, $(if ($Verify) { 'VERIFY (read-only)' } elseif ($ReadOnly) { 'WHATIF (no writes)' } else { 'APPLY' }))
Write-Host ""

try {
    # --- 0. Tables and privileges ------------------------------------------------------------------------------------
    Write-Host "0. Tables" -ForegroundColor Cyan
    $privs = @{}
    foreach ($t in @($AdminTables.Keys) + $ProcedureTables) {
        $info = Get-TableInfo $t
        if ($AdminTables.Contains($t) -and $info.Schema -cne $AdminTables[$t]) {
            throw [EscalationException]::new("$t has schema name '$($info.Schema)', not '$($AdminTables[$t])' (the casing BillingAdminAuthorization asks for). Nothing was changed.")
        }
        $privs[$t] = Get-TablePrivileges $info
        Write-Host ("  {0,-26} {1,-10} {2} privileges" -f $t, $info.Ownership, $privs[$t].Count)
    }
    foreach ($t in $AdminTables.Keys) {
        foreach ($v in $Verbs) { if (-not $privs[$t].Contains($v)) { throw "Privilege prv$v$($AdminTables[$t]) not found." } }
        foreach ($v in $AdminVerbs) { if (-not $privs[$t][$v].($AdminDepth[$v])) { throw [EscalationException]::new("$($privs[$t][$v].Name) cannot be held at $($AdminDepth[$v]). Nothing was changed.") } }
    }
    $wanted = @(foreach ($t in $AdminTables.Keys) { foreach ($v in $AdminVerbs) { $p = $privs[$t][$v]; [pscustomobject]@{ Name = $p.Name; Id = $p.Id; Depth = $AdminDepth[$v] } } })
    $forbiddenForAdmin = @(foreach ($t in $AdminTables.Keys) { foreach ($v in $Verbs) { if ($AdminVerbs -notcontains $v) { $privs[$t][$v] } } }) +
    @(foreach ($t in $ProcedureTables) { $privs[$t].Values })
    $ruleSetPrivs = @($privs['sprk_ruleset'].Values)
    $stripPriv = $privs['sprk_ruleset']['Read']
    $rootBu = Get-RootBusinessUnitId
    $solution = @((Invoke-Dv "solutions?`$select=solutionid,ismanaged&`$filter=uniquename eq '$SolutionUniqueName'").value)
    if ($solution.Count -ne 1 -or $solution[0].ismanaged) { throw "Solution '$SolutionUniqueName' is missing or managed. Nothing was changed." }

    # --- 1. The role -------------------------------------------------------------------------------------------------
    Write-Host ""
    Write-Host "1. Role $RoleName ($RoleId)" -ForegroundColor Cyan
    $byId = Get-DvOrNull "roles($RoleId)?`$select=roleid,name,description,_businessunitid_value,_parentrootroleid_value"
    $byName = Get-RootRole $RoleName
    if ($byName -and $byName.Id -ne $RoleId) {
        throw [EscalationException]::new("A role named '$RoleName' exists with id $($byName.Id), not the fixed id $RoleId of the role source. STOP: it was not made by this script. Nothing was changed.")
    }
    if ($byId -and ($byId.name -ne $RoleName -or ([string]$byId._businessunitid_value).ToLower() -ne $rootBu)) {
        throw [EscalationException]::new("Role id $RoleId exists as '$($byId.name)' in business unit $($byId._businessunitid_value) (expected '$RoleName' in the root business unit $rootBu). Nothing was changed.")
    }
    $roleExists = [bool]$byId
    if ($roleExists) {
        $script:Skipped.Add("role $RoleName"); Write-Host "  skipped  role exists in the root business unit" -ForegroundColor Yellow
        Write-Host "  description: $($byId.description)"
    } else {
        $Gaps.Add("role '$RoleName' ($RoleId) does not exist")
        if ($ReadOnly) {
            $script:Changed.Add("create role $RoleName ($RoleId)")
            Write-Host "  WOULD CREATE role '$RoleName' with id $RoleId in the root business unit $rootBu, in solution $SolutionUniqueName" -ForegroundColor Cyan
            Write-Host "               description: $RoleDescription" -ForegroundColor Cyan
        } else {
            $body = @{ roleid = $RoleId; name = $RoleName; description = $RoleDescription; 'businessunitid@odata.bind' = "/businessunits($rootBu)" }
            Invoke-Dv -Path 'roles' -Method POST -Body $body -ExtraHeaders $State.SolutionHeader | Out-Null
            $byId = Wait-Settled { Get-DvOrNull "roles($RoleId)?`$select=roleid,name,_businessunitid_value" } { param($x) [bool]$x } 12
            if (-not $byId) { throw "Role $RoleName was created but is not readable after 36 s." }
            $roleExists = $true
            $script:Changed.Add("created role $RoleName ($RoleId)"); Write-Host "  CREATED  role '$RoleName' ($RoleId)" -ForegroundColor Green
        }
    }

    # --- 2. Solution membership ----------------------------------------------------------------------------------------
    if ($roleExists) {
        $headers = @{ Authorization = "Bearer $(Get-DataverseToken)"; Accept = 'application/json'; 'OData-MaxVersion' = '4.0'; 'OData-Version' = '4.0' }
        $inSolution = Test-DvInSolution -Membership (Get-DvSolutionMembership -Api $State.ApiBase -Headers $headers -SolutionId $solution[0].solutionid) -ComponentId $RoleId
        if ($inSolution) {
            $script:Skipped.Add("role in $SolutionUniqueName"); Write-Host "  skipped  role is in solution $SolutionUniqueName" -ForegroundColor Yellow
        } elseif ($ReadOnly) {
            $Gaps.Add("role '$RoleName' is not in solution $SolutionUniqueName")
            $script:Changed.Add("add role to $SolutionUniqueName"); Write-Host "  WOULD ADD the role to solution $SolutionUniqueName" -ForegroundColor Cyan
        } else {
            Invoke-Dv -Path 'AddSolutionComponent' -Method POST -Body @{ ComponentId = $RoleId; ComponentType = 20; SolutionUniqueName = $SolutionUniqueName; AddRequiredComponents = $false } | Out-Null
            $script:Changed.Add("added role to $SolutionUniqueName"); Write-Host "  ADDED    role to solution $SolutionUniqueName" -ForegroundColor Green
        }
    } else {
        Write-Host "  WhatIf: the role is created under the $SolutionUniqueName solution header" -ForegroundColor Cyan
    }

    # --- 3. The role's privileges --------------------------------------------------------------------------------------
    Write-Host ""
    Write-Host "3. $RoleName privileges (Read Global; Create / Write / Append / AppendTo Basic)" -ForegroundColor Cyan
    $have = if ($roleExists) { Get-RolePrivilegeMap $RoleId } else { @{} }
    $forbidden = @($forbiddenForAdmin | Where-Object { $have.ContainsKey($_.Id) })
    if ($forbidden.Count) {
        $msg = "$RoleName holds $(($forbidden | ForEach-Object { "$($_.Name) ($($have[$_.Id].Depth))" }) -join ', '): no Delete / Assign / Share on the rule-set and policy tables and nothing on the Procedure tables (D-76, uac-r2 081)."
        if ($Verify) { $Gaps.Add($msg) } else { throw [EscalationException]::new("$msg STOP: this script removes nothing from the role. Nothing further was changed.") }
    }
    # Held deeper than wanted: an over-grant this script never lowers (uac-r2: Global Create / Write bypasses RuleSetService).
    $deeper = @($wanted | Where-Object { $have.ContainsKey($_.Id) -and $Rank[$have[$_.Id].Depth] -gt $Rank[$_.Depth] })
    if ($deeper.Count) {
        $msg = "$RoleName holds $(($deeper | ForEach-Object { "$($_.Name) at $($have[$_.Id].Depth) (wanted $($_.Depth))" }) -join ', ')."
        if ($Verify) { $Gaps.Add($msg) } else { throw [EscalationException]::new("$msg STOP: this script never lowers a privilege. Nothing further was changed.") }
    }
    $toAdd = @($wanted | Where-Object { -not $have.ContainsKey($_.Id) -or $Rank[$have[$_.Id].Depth] -lt $Rank[$_.Depth] })
    foreach ($w in $wanted) {
        if ($toAdd -contains $w -or $deeper -contains $w) { continue }
        $script:Skipped.Add("$RoleName $($w.Name)"); Write-Host ("  skipped  {0,-30} (held at {1})" -f $w.Name, $w.Depth) -ForegroundColor Yellow
    }
    foreach ($a in $toAdd) {
        $Gaps.Add("$RoleName lacks $($a.Name) at $($a.Depth)$(if ($have.ContainsKey($a.Id)) { " (held at $($have[$a.Id].Depth))" })")
        if ($ReadOnly) { $script:Changed.Add("$RoleName + $($a.Name) $($a.Depth)"); Write-Host ("  WOULD ADD {0,-30} {1}" -f $a.Name, $a.Depth) -ForegroundColor Cyan }
    }
    $allowed = @($wanted.Id) + @($forbiddenForAdmin.Id)
    $extras = @($have.Keys | Where-Object { $allowed -notcontains $_ -and $PlatformDefaults -notcontains $have[$_].Name } | ForEach-Object { "$($have[$_].Name) ($($have[$_].Depth))" } | Sort-Object)
    if ($extras.Count) {
        $Gaps.Add("$RoleName holds privileges outside its set (not removed by this script; take them to the owner): $($extras -join ', ')")
        Write-Host "  WARNING  holds privileges outside its set: $($extras -join ', ')" -ForegroundColor DarkYellow
    }
    if ($toAdd.Count -gt 0 -and -not $ReadOnly) {
        $body = @{ Privileges = @($toAdd | ForEach-Object { @{ Depth = $_.Depth; PrivilegeId = $_.Id; BusinessUnitId = $rootBu } }) }
        Invoke-Dv -Path "roles($RoleId)/Microsoft.Dynamics.CRM.AddPrivilegesRole" -Method POST -Body $body | Out-Null
        $after = Wait-Settled { Get-RolePrivilegeMap $RoleId } { param($m) @($toAdd | Where-Object { -not $m.ContainsKey($_.Id) -or $m[$_.Id].Depth -ne $_.Depth }).Count -eq 0 }
        $miss = @($toAdd | Where-Object { -not $after.ContainsKey($_.Id) -or $after[$_.Id].Depth -ne $_.Depth })
        if ($miss.Count) { throw "AddPrivilegesRole on '$RoleName' did not take for: $(($miss | ForEach-Object { "$($_.Name) at $($_.Depth)" }) -join ', ')" }
        foreach ($a in $toAdd) { $script:Changed.Add("$RoleName + $($a.Name) $($a.Depth)"); Write-Host ("  ADDED    {0,-30} {1}" -f $a.Name, $a.Depth) -ForegroundColor Green }
        $have = $after
    }
    if ($roleExists -or -not $ReadOnly) {
        foreach ($t in @($AdminTables.Keys) + $ProcedureTables) { Write-Host ("    {0,-26} {1}" -f $t, (Format-Held $have @($privs[$t].Values))) }
        $copies = Get-RoleCopies $RoleId
        $bad = @()
        foreach ($c in $copies) {
            $cid = ([string]$c.roleid).ToLower()
            $read = { Get-RolePrivilegeMap $cid }   # dynamic scope: $cid is read when Wait-Settled runs it
            $cm = if ($ReadOnly) { & $read } else { Wait-Settled $read { param($m) @($wanted | Where-Object { -not $m.ContainsKey($_.Id) -or $m[$_.Id].Depth -ne $_.Depth }).Count -eq 0 } 8 4 }
            $m = @($wanted | Where-Object { -not $cm.ContainsKey($_.Id) -or $cm[$_.Id].Depth -ne $_.Depth })
            if ($m.Count) { $bad += "$cid (business unit $($c._businessunitid_value)): $($m.Count) missing" }
        }
        if ($bad.Count) { $Gaps.Add("$RoleName business-unit copies without the set: $($bad -join '; ')"); Write-Host "  WARNING: business-unit copies without the set: $($bad -join '; ')" -ForegroundColor DarkYellow }
        else { Write-Host "  business-unit copies ($($copies.Count)) hold the set" -ForegroundColor Green }

        # uac-r2 K-1: users only, never a team.
        $teams = @((Invoke-Dv "roles($RoleId)/teamroles_association?`$select=name").value)
        $users = @((Invoke-Dv "roles($RoleId)/systemuserroles_association?`$select=fullname").value)
        Write-Host "  held by $($users.Count) user(s) directly, $($teams.Count) team(s)"
        if ($teams.Count) { $Gaps.Add("$RoleName is held by team(s) $(($teams | ForEach-Object { $_.name }) -join ', '): assign it to users directly, never to a team (uac-r2 K-1)") }
    }

    # --- 4. Core and Basic User lose Read on sprk_ruleset ------------------------------------------------------------
    foreach ($roleName in $StripRoles) {
        Write-Host ""
        Write-Host "4. $roleName - $StripPrivilegeName" -ForegroundColor Cyan
        $role = Get-RootRole $roleName
        if (-not $role) { throw [EscalationException]::new("Role '$roleName' was not found as a single root role in $EnvironmentUrl. Nothing was changed.") }
        if ($role.BusinessUnitId -ne $rootBu) { throw [EscalationException]::new("Root role '$roleName' is not in the root business unit. Nothing was changed.") }
        $before = Get-RolePrivilegeMap $role.Id
        Write-Host "  root role $($role.Id), $($before.Count) privileges; sprk_ruleset: $(Format-Held $before $ruleSetPrivs)"
        $heldRuleSet = @($ruleSetPrivs | Where-Object { $before.ContainsKey($_.Id) })
        $unexpected = @($heldRuleSet | Where-Object { $_.Id -ne $stripPriv.Id -or $before[$_.Id].Depth -ne $StripSourceDepth })
        if ($unexpected.Count) {
            $msg = "$roleName holds $(($unexpected | ForEach-Object { "$($_.Name) ($($before[$_.Id].Depth))" }) -join ', ') on sprk_ruleset; the role source showed only $StripPrivilegeName ($StripSourceDepth)."
            if ($Verify) { $Gaps.Add($msg) } else { throw [EscalationException]::new("$msg STOP and report (task 120 escalation): nothing further was changed.") }
        }
        if (-not $before.ContainsKey($stripPriv.Id)) {
            $script:Skipped.Add("$roleName $StripPrivilegeName absent"); Write-Host "  skipped  $StripPrivilegeName already absent" -ForegroundColor Yellow
        } else {
            $Gaps.Add("$roleName still holds $StripPrivilegeName ($($before[$stripPriv.Id].Depth)) (D-185)")
            if ($ReadOnly) {
                $script:Changed.Add("$roleName - $StripPrivilegeName"); Write-Host "  WOULD REMOVE $StripPrivilegeName ($($before[$stripPriv.Id].Depth)) from the root role (RemovePrivilegeRole)" -ForegroundColor Cyan
            } else {
                Invoke-Dv -Path "roles($($role.Id))/Microsoft.Dynamics.CRM.RemovePrivilegeRole" -Method POST -Body @{
                    Privilege = @{ '@odata.type' = 'Microsoft.Dynamics.CRM.privilege'; privilegeid = $stripPriv.Id }
                } | Out-Null
                $after = Wait-Settled { Get-RolePrivilegeMap $role.Id } { param($m) -not $m.ContainsKey($stripPriv.Id) }
                if ($after.ContainsKey($stripPriv.Id)) { throw "RemovePrivilegeRole on '$roleName' did not take: $StripPrivilegeName still reads as held." }
                # Exactly one privilege went; nothing else was removed or changed depth.
                $diff = @($before.Keys | Where-Object { $_ -ne $stripPriv.Id -and (-not $after.ContainsKey($_) -or $after[$_].Depth -ne $before[$_].Depth) })
                $added = @($after.Keys | Where-Object { -not $before.ContainsKey($_) })
                if ($diff.Count -or $added.Count) { throw [EscalationException]::new("After the removal, '$roleName' differs beyond $StripPrivilegeName (changed: $($diff.Count), added: $($added.Count)). STOP: investigate before another run.") }
                $script:Changed.Add("$roleName - $StripPrivilegeName"); Write-Host "  REMOVED  $StripPrivilegeName ($($before[$stripPriv.Id].Depth)); role now holds $($after.Count) privileges (was $($before.Count))" -ForegroundColor Green
            }
        }
        if (-not ($ReadOnly -and $before.ContainsKey($stripPriv.Id))) {
            $copies = Get-RoleCopies $role.Id
            $bad = @()
            foreach ($c in $copies) {
                $cid = ([string]$c.roleid).ToLower()
                $read = { Get-RolePrivilegeMap $cid }   # dynamic scope: $cid is read when Wait-Settled runs it
                $cm = if ($ReadOnly) { & $read } else { Wait-Settled $read { param($m) -not $m.ContainsKey($stripPriv.Id) } 8 4 }
                if ($cm.ContainsKey($stripPriv.Id)) { $bad += "$cid (business unit $($c._businessunitid_value))" }
            }
            if ($bad.Count) { $Gaps.Add("$roleName business-unit copies still holding ${StripPrivilegeName}: $($bad -join '; ')"); Write-Host "  WARNING: business-unit copies still holding ${StripPrivilegeName}: $($bad -join '; ')" -ForegroundColor DarkYellow }
            else { Write-Host "  business-unit copies ($($copies.Count)) do not hold $StripPrivilegeName" -ForegroundColor Green }
        }
    }

    # --- 5. Policy-table privileges of the shared roles (ontology-r1 #2298; never changed here) --------------------------
    Write-Host ""
    Write-Host "5. sprk_Policy / sprk_PolicyVersion on the shared roles (ontology-r1 #2298 owns these; read only)" -ForegroundColor Cyan
    foreach ($roleName in $PolicyWatchRoles) {
        $role = Get-RootRole $roleName
        if (-not $role) { Write-Host "  $roleName : not found"; continue }
        $m = Get-RolePrivilegeMap $role.Id
        Write-Host ("  {0,-22} sprk_Policy: {1}   sprk_PolicyVersion: {2}" -f $roleName, (Format-Held $m @($privs['sprk_policy'].Values)), (Format-Held $m @($privs['sprk_policyversion'].Values)))
    }

    # --- 6. Every role holding a privilege on sprk_ruleset -------------------------------------------------------------
    Write-Host ""
    Write-Host "6. Roles holding a privilege on sprk_ruleset (root roles; $(if ($ReadOnly) { 'live, before any change' } else { 'after the run' }))" -ForegroundColor Cyan
    $holders = @{}
    foreach ($p in $ruleSetPrivs) {
        foreach ($row in @((Invoke-Dv "roleprivilegescollection?`$filter=privilegeid eq $($p.Id)&`$select=roleid,privilegedepthmask").value)) {
            $rid = ([string]$row.roleid).ToLower()
            if (-not $holders.ContainsKey($rid)) { $holders[$rid] = [System.Collections.Generic.List[string]]::new() }
            $holders[$rid].Add("$($p.Verb)=$($DepthNames[[int]$row.privilegedepthmask])")
        }
    }
    foreach ($rid in $holders.Keys) {
        $r = Invoke-Dv "roles($rid)?`$select=name,_parentrootroleid_value"
        if ($r._parentrootroleid_value -and ([string]$r._parentrootroleid_value).ToLower() -ne $rid) { continue }
        # Core / Basic User are reported (and counted as a gap) in step 4; any other Spaarke role is a gap here.
        $mark = if ($r.name -eq $RoleName) { 'billing role' }
        elseif ($StripRoles -contains $r.name) { if ($ReadOnly -and -not $Verify) { 'loses Read in this run (step 4)' } else { 'NOT ALLOWED (D-185, step 4)' } }
        elseif ($r.name -like 'Spaarke *') { 'NOT ALLOWED (D-185)' }
        else { 'platform role, not a Spaarke role' }
        Write-Host ("  {0,-34} {1,-50} [{2}]" -f $r.name, ($holders[$rid] -join ' '), $mark) -ForegroundColor $(if ($mark -like 'NOT ALLOWED*') { 'Red' } else { 'Gray' })
        if ($mark -eq 'NOT ALLOWED (D-185)') { $Gaps.Add("Spaarke role '$($r.name)' holds a privilege on sprk_ruleset: $($holders[$rid] -join ' ') (D-185)") }
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
    Write-Host "The script is re-runnable (it reads, compares, then writes only what is missing)." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "================ Summary ================" -ForegroundColor Cyan
if ($Verify) {
    if ($Gaps.Count -eq 0) { Write-Host "VERIFY PASS: the task 120 / D-185 state holds." -ForegroundColor Green; exit 0 }
    foreach ($g in $Gaps) { Write-Host "  GAP  $g" -ForegroundColor Red }
    Write-Host "VERIFY FAIL: $($Gaps.Count) gap(s)." -ForegroundColor Red
    exit 1
}
Write-Host ("{0}: {1}" -f $(if ($ReadOnly) { 'would change' } else { 'changed' }), $script:Changed.Count)
foreach ($c in $script:Changed) { Write-Host "  $c" }
Write-Host ("held / skipped: {0}" -f $script:Skipped.Count)
if ($ReadOnly) {
    Write-Host "-Verify would report $($Gaps.Count) gap(s) now:" -ForegroundColor Cyan
    foreach ($g in $Gaps) { Write-Host "  GAP  $g" }
    Write-Host "WhatIf run: nothing was written." -ForegroundColor Cyan
}
exit 0
