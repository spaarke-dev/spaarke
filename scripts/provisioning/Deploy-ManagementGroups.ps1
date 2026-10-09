<#
.SYNOPSIS
    Creates Spaarke's management-group hierarchy, assigns the common customer policy, and places the listed
    subscriptions (ADR-027, G36). Plan only unless -Apply.

.DESCRIPTION
    customer-provisioning-orchestration-r1 task 262. Three steps, each idempotent:

      1. infrastructure/bicep/management-groups.bicep at TENANT scope:
           Tenant Root Group
           └── spaarke-environments  "Spaarke Environments"
               └── spaarke-customers "Spaarke Customers"
      2. infrastructure/bicep/customer-policy.bicep at the spaarke-customers management group (built-in definitions,
         Audit / DoNotEnforce only — nothing it assigns can refuse a deployment).
      3. Moves each listed subscription under its group (`az account management-group subscription add`), skipping one
         already there, then reads every placement back.

    The group ids and display names come from scripts/provisioning-prereqs/spaarke-constants.yaml `management_groups`
    (the same values PRQ-S-06 checks) and are passed to the template explicitly.

    Without -Apply (the default, or with -WhatIf) the script makes READ-ONLY az calls only — the current hierarchy, each
    subscription's current parent, the caller's role assignments at "/" — and prints the plan and the exact commands.

    Never runs `az account set`: every call names its target (--subscription / --management-group-id / --name).

    REQUIRED ACCESS (-Apply). A tenant-scope deployment needs Microsoft.Resources/deployments/write at "/" (Microsoft
    Learn, "Tenant deployments with Bicep — Required access"); assigning policy needs
    Microsoft.Authorization/policyAssignments/write; moving a subscription needs management-group write on the target
    group and on the subscription (none on the Tenant Root Group when moving out of it). The script refuses -Apply unless
    the caller holds, at "/", Owner — or Contributor together with User Access Administrator or Resource Policy
    Contributor. A Global Administrator obtains them by elevating access (Entra admin center → Properties → Access
    management for Azure resources = Yes, which grants User Access Administrator at "/") and assigning Contributor at
    "/" for the run; remove both afterwards.

.PARAMETER Apply
    Deploy, assign and move. Without it the script only reads and plans.

.PARAMETER WhatIf
    Plan only (the default). Wins over -Apply.

.PARAMETER CustomerSubscriptionIds
    Subscriptions to place in spaarke-customers. For a new customer, pass its subscription id alone (PRQ-S-06).

.PARAMETER EnvironmentSubscriptionIds
    Subscriptions to place directly in spaarke-environments (Spaarke's own platform subscriptions). No policy is
    assigned at that level today.

    When NEITHER list is passed, the script applies the initial placement decided on 2026-10-09:
      spaarke-customers    <- Spaarke Demo Environment (2ff9ee48-…; the T186 customer stamp's subscription, owner)
      spaarke-environments <- Spaarke Devlopment Environment (484bc857-…), Spaarke Shared Production (cd95fcec-…)
    SPRK Power Platform 1 and Spaarke Legal Rules Solution stay at the Tenant Root Group (not Spaarke environments).
    Passing either list replaces the initial placement entirely (so pwsh -File callers never need an empty-array
    literal, which -File would pass as the string '@()').

.PARAMETER Location
    Deployment metadata location for the tenant- and management-group-scope deployments. Default westus2.

.EXAMPLE
    pwsh scripts/provisioning/Deploy-ManagementGroups.ps1
    Prints the plan; changes nothing.

.EXAMPLE
    pwsh scripts/provisioning/Deploy-ManagementGroups.ps1 -Apply
    Creates the groups, assigns the policy, applies the initial placement, verifies.

.EXAMPLE
    pwsh scripts/provisioning/Deploy-ManagementGroups.ps1 -Apply -CustomerSubscriptionIds 11111111-2222-3333-4444-555555555555
    Places one new customer subscription; the hierarchy and policy re-deploy unchanged. Needs the same rights at '/'
    as the first run — a routine new customer is placed with the PRQ-S-06 one-liner instead
    (az account management-group subscription add --name spaarke-customers --subscription <id>).
#>
[CmdletBinding()]
param(
    [switch]$Apply,

    [switch]$WhatIf,

    [string[]]$CustomerSubscriptionIds = @(),

    [string[]]$EnvironmentSubscriptionIds = @(),

    [string]$Location = 'westus2',

    [string]$ConstantsPath = (Join-Path $PSScriptRoot '..' 'provisioning-prereqs' 'spaarke-constants.yaml')
)

$ErrorActionPreference = 'Stop'
$IsPlanOnly = (-not $Apply.IsPresent) -or $WhatIf.IsPresent
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$mgTemplate = Join-Path $repoRoot 'infrastructure/bicep/management-groups.bicep'
$policyTemplate = Join-Path $repoRoot 'infrastructure/bicep/customer-policy.bicep'
$mgPrefix = '/providers/Microsoft.Management/managementGroups/'

function Invoke-Az {
    # Runs az, returns stdout; throws with az's stderr on a non-zero exit.
    param([Parameter(Mandatory)][string[]]$Arguments)
    $out = & az @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "az $($Arguments -join ' ') failed (exit $LASTEXITCODE): $($out -join "`n")" }
    return ($out | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] }) -join "`n"
}

# --- Constants (single source with PRQ-S-06) ---------------------------------------------------------------------
if (-not (Get-Module -ListAvailable -Name powershell-yaml)) { Install-Module powershell-yaml -Scope CurrentUser -Force -Confirm:$false | Out-Null }
Import-Module powershell-yaml
$mg = (Get-Content -Raw $ConstantsPath | ConvertFrom-Yaml).management_groups
if (-not $mg -or -not $mg.environments.id -or -not $mg.customers.id) { throw "spaarke-constants.yaml has no management_groups.environments.id / customers.id ($ConstantsPath)." }
$envGroup = [string]$mg.environments.id
$custGroup = [string]$mg.customers.id

# --- Inputs --------------------------------------------------------------------------------------------------------
if (-not $PSBoundParameters.ContainsKey('CustomerSubscriptionIds') -and -not $PSBoundParameters.ContainsKey('EnvironmentSubscriptionIds')) {
    # Initial placement (2026-10-09) — see .PARAMETER EnvironmentSubscriptionIds.
    $CustomerSubscriptionIds = @('2ff9ee48-6f1d-4664-865c-f11868dd1b50')                                   # Spaarke Demo Environment
    $EnvironmentSubscriptionIds = @('484bc857-3802-427f-9ea5-ca47b43db0f0', 'cd95fcec-6b89-49ea-8339-c2b579b12587') # Development, Shared Production
}
$guid = '^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$'
$CustomerSubscriptionIds = @($CustomerSubscriptionIds | Where-Object { $_ } | ForEach-Object { $_.Trim().ToLowerInvariant() })
$EnvironmentSubscriptionIds = @($EnvironmentSubscriptionIds | Where-Object { $_ } | ForEach-Object { $_.Trim().ToLowerInvariant() })
foreach ($s in $CustomerSubscriptionIds + $EnvironmentSubscriptionIds) { if ($s -notmatch $guid) { throw "'$s' is not a subscription id (GUID)." } }
$both = @($CustomerSubscriptionIds | Where-Object { $_ -in $EnvironmentSubscriptionIds })
if ($both.Count) { throw "Subscription(s) listed for both groups: $($both -join ', '). A subscription has one parent." }

$targets = [ordered]@{}
foreach ($s in $CustomerSubscriptionIds) { $targets[$s] = $custGroup }
foreach ($s in $EnvironmentSubscriptionIds) { $targets[$s] = $envGroup }
if (-not $targets.Count) { throw 'No subscription listed. Pass -CustomerSubscriptionIds and/or -EnvironmentSubscriptionIds.' }

# --- Read-only state -----------------------------------------------------------------------------------------------
# Tenant: from the first listed subscription (named explicitly; the current az default is never used or changed).
$first = @($targets.Keys)[0]
$tenantId = (Invoke-Az @('account', 'show', '--subscription', $first, '--query', 'tenantId', '-o', 'tsv')).Trim()
$subNames = @{}
foreach ($s in $targets.Keys) {
    $acct = Invoke-Az @('account', 'show', '--subscription', $s, '-o', 'json') | ConvertFrom-Json
    if ($acct.tenantId -ne $tenantId) { throw "Subscription $s is in tenant $($acct.tenantId), not $tenantId. One tenant per run." }
    $subNames[$s] = $acct.name
}

$existing = @{}
foreach ($g in (Invoke-Az @('account', 'management-group', 'list', '-o', 'json') | ConvertFrom-Json)) { $existing[$g.name] = $g }

# Each listed subscription's current parent, from the hierarchy under the Tenant Root Group.
$parentOf = @{}
$groupParent = @{}
function Read-Hierarchy($node, [string]$parentName) {
    foreach ($c in @($node.children)) {
        if (-not $c) { continue }
        if ($c.type -match 'subscriptions$') { $parentOf[$c.name.ToLowerInvariant()] = $node.name }
        else { $groupParent[$c.name] = $node.name; Read-Hierarchy $c $c.name }
    }
}
$root = Invoke-Az @('account', 'management-group', 'show', '--name', $tenantId, '--expand', '--recurse', '-o', 'json') | ConvertFrom-Json
Read-Hierarchy $root $null

# Existing groups must sit where the template puts them; a group elsewhere is a conflict the template would move.
foreach ($pair in @(@($envGroup, $tenantId), @($custGroup, $envGroup))) {
    if ($existing.ContainsKey($pair[0]) -and $groupParent.ContainsKey($pair[0]) -and $groupParent[$pair[0]] -ne $pair[1]) {
        throw "Management group '$($pair[0])' exists under '$($groupParent[$pair[0]])', not '$($pair[1])'. Resolve by hand before running."
    }
}

# Caller's access at "/" (role assignments include groups the caller belongs to).
$me = (Invoke-Az @('ad', 'signed-in-user', 'show', '--query', 'id', '-o', 'tsv')).Trim()
$rootRoles = @((Invoke-Az @('role', 'assignment', 'list', '--assignee', $me, '--scope', '/', '--include-groups', '--query', '[].roleDefinitionName', '-o', 'tsv')) -split "`r?`n" | Where-Object { $_ })
$canApply = ('Owner' -in $rootRoles) -or (('Contributor' -in $rootRoles) -and (('User Access Administrator' -in $rootRoles) -or ('Resource Policy Contributor' -in $rootRoles)))

# --- Plan ----------------------------------------------------------------------------------------------------------
$stamp = Get-Date -Format 'yyyyMMddHHmmss'
$mgDeployArgs = @('deployment', 'tenant', 'create', '--name', "spaarke-management-groups-$stamp", '--location', $Location,
    '--template-file', $mgTemplate, '--parameters',
    "environmentsGroupId=$envGroup", "environmentsGroupDisplayName=$($mg.environments.displayName)",
    "customersGroupId=$custGroup", "customersGroupDisplayName=$($mg.customers.displayName)", '-o', 'none')
$policyDeployArgs = @('deployment', 'mg', 'create', '--management-group-id', $custGroup, '--name', "spaarke-customer-policy-$stamp",
    '--location', $Location, '--template-file', $policyTemplate, '--query', 'properties.outputs.assignmentIds.value', '-o', 'json')

Write-Host "Tenant $tenantId — caller $me — roles at '/': $(if ($rootRoles) { $rootRoles -join ', ' } else { '<none>' })" -ForegroundColor Cyan
Write-Host "Management groups:" -ForegroundColor Cyan
Write-Host ("  {0,-22} {1}" -f $envGroup, $(if ($existing.ContainsKey($envGroup)) { 'exists (template re-applies)' } else { "CREATE under Tenant Root Group ($tenantId)" }))
Write-Host ("  {0,-22} {1}" -f $custGroup, $(if ($existing.ContainsKey($custGroup)) { 'exists (template re-applies)' } else { "CREATE under $envGroup" }))
Write-Host "Policy: customer-policy.bicep at $custGroup (built-in, Audit / DoNotEnforce)." -ForegroundColor Cyan
Write-Host "Subscriptions:" -ForegroundColor Cyan
$moves = @()
foreach ($s in $targets.Keys) {
    $cur = if ($parentOf.ContainsKey($s)) { $parentOf[$s] } else { '<not found under the Tenant Root Group>' }
    $action = if ($cur -eq $targets[$s]) { 'already there' } else { $moves += $s; "MOVE $cur -> $($targets[$s])" }
    Write-Host ("  {0} {1,-32} {2}" -f $s, $subNames[$s], $action)
}
function Format-Args([string[]]$a) { ($a | ForEach-Object { if ($_ -match '\s') { "`"$_`"" } else { $_ } }) -join ' ' }
Write-Host "Commands (-Apply):" -ForegroundColor Cyan
Write-Host "  az $(Format-Args $mgDeployArgs)"
Write-Host "  az $(Format-Args $policyDeployArgs)"
foreach ($s in $moves) { Write-Host "  az account management-group subscription add --name $($targets[$s]) --subscription $s" }

if ($IsPlanOnly) {
    if (-not $canApply) { Write-Warning "-Apply would be refused: the caller needs Owner, or Contributor + User Access Administrator (or Resource Policy Contributor), at '/'. See .DESCRIPTION REQUIRED ACCESS." }
    Write-Host "Plan only — nothing changed. Re-run with -Apply." -ForegroundColor Yellow
    exit 0
}

if (-not $canApply) { throw "Refusing -Apply: the caller holds '$($rootRoles -join ', ')' at '/'; needs Owner, or Contributor + User Access Administrator (or Resource Policy Contributor). See .DESCRIPTION REQUIRED ACCESS." }

# --- Apply ---------------------------------------------------------------------------------------------------------
Write-Host "1/3 Management groups (tenant deployment)..." -ForegroundColor Cyan
Invoke-Az $mgDeployArgs | Out-Null
Write-Host "2/3 Customer policy at $custGroup..." -ForegroundColor Cyan
$expectedAssignments = @((Invoke-Az $policyDeployArgs | ConvertFrom-Json) | ForEach-Object { ($_ -split '/')[-1] })
Write-Host "3/3 Subscriptions..." -ForegroundColor Cyan
foreach ($s in $moves) {
    Invoke-Az @('account', 'management-group', 'subscription', 'add', '--name', $targets[$s], '--subscription', $s) | Out-Null
    Write-Host "  moved $s -> $($targets[$s])"
}

# --- Verify (read back) --------------------------------------------------------------------------------------------
$failed = @()
foreach ($s in $targets.Keys) {
    try { $parent = (Invoke-Az @('account', 'management-group', 'subscription', 'show', '--name', $targets[$s], '--subscription', $s, '--query', 'parent.id', '-o', 'tsv')).Trim() }
    catch { $parent = "<not under $($targets[$s])>" }
    if ($parent -ne "$mgPrefix$($targets[$s])") { $failed += "$s parent is '$parent', expected $mgPrefix$($targets[$s])" }
}
$custParent = (Invoke-Az @('account', 'management-group', 'show', '--name', $custGroup, '--query', 'details.parent.name', '-o', 'tsv')).Trim()
if ($custParent -ne $envGroup) { $failed += "$custGroup parent is '$custParent', expected $envGroup" }
$assigned = @((Invoke-Az @('policy', 'assignment', 'list', '--scope', "$mgPrefix$custGroup", '--query', '[].name', '-o', 'tsv')) -split "`r?`n" | Where-Object { $_ })
if ($expectedAssignments.Count -lt 1) { $failed += "the policy deployment returned no assignment" }
foreach ($a in $expectedAssignments) { if ($a -notin $assigned) { $failed += "policy assignment '$a' is not listed at $custGroup" } }
if ($failed.Count) { $failed | ForEach-Object { Write-Host "  FAIL $_" -ForegroundColor Red }; exit 1 }
Write-Host "Done: hierarchy in place, $($assigned.Count) assignment(s) at $custGroup, $($targets.Count) subscription(s) verified." -ForegroundColor Green
exit 0
