#Requires -Version 7.0
<#
.SYNOPSIS
    eBilling R1 task 091 - verify the deployed file-store custom role and its assignments (read-only, operator sign-in).

.DESCRIPTION
    Checks what infrastructure/bicep/modules/ebilling-file-store.bicep deployed, against LIVE Azure, for unified-access-control-r2's
    conditions on option A (projects/ebilling-r1/notes/091-storage-provisioning.md section 8):

      1. Exactness (condition 1). The custom role "Spaarke eBilling File Store Blob Data (...)" in the resource group has one
         permission block whose actions equal built-in Storage Blob Data Contributor's actions and whose dataActions equal its
         dataActions plus .../containers/blobs/tags/read, both read LIVE from `az role definition list` (so a later change to the
         built-in shows up here); no '*' anywhere, no tags/write, empty notActions / notDataActions, no condition, and
         assignableScopes exactly the resource group. Any difference fails.
      2. Per-container assignments only (condition 4). Every assignment of that role, to anyone, is at the scope of one of the
         expected containers of the expected account; the principal holds it on each expected container. An assignment at the
         account, the resource group, the subscription or any other scope fails.
      3. Context (not a failure): any built-in Storage Blob Data role the principal holds above container scope on the account is
         listed as WARN - it would make the container scoping moot (091 POML escalation trigger 1). Removing it is the owner's
         live step on dev and customer-provisioning's on a stamp, never this script's.

    The data-plane proof (condition 2: Set Blob Tags and Put Blob with x-ms-tags are 403 for the assigned identity) cannot run
    under an operator sign-in; it runs inside the App Service as the identity: scripts/ebilling/test-ebilling-file-store-tag-write.sh.

    The script changes nothing in Azure and reads no key, connection string or secret.

.PARAMETER SubscriptionId
    Subscription holding the account. Passed to every az call; the script never runs 'az account set'.

.PARAMETER ResourceGroup
    Resource group the module was deployed into (the storage account's resource group).

.PARAMETER StorageAccountName
    The storage account the containers live in.

.PARAMETER PrincipalId
    Object id of the BFF managed identity the module granted (dev: mi-bff-api-dev).

.PARAMETER ContainerNames
    The file-store containers. Default: procedures, invoice-files, billing-exports.

.EXAMPLE
    ./scripts/ebilling/Test-EbillingFileStoreRole.ps1 -SubscriptionId <sub> -ResourceGroup <rg> -StorageAccountName <account> -PrincipalId <object id>

.NOTES
    Exit 0 = all checks pass; 1 = a check failed; 2 = could not read (az not signed in, role not found).
    Pester (pure checks, no az): Import-Module Pester -RequiredVersion 3.4.0 ; Invoke-Pester tests/scripts/Test-EbillingFileStoreRole.Tests.ps1
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$SubscriptionId,
    [Parameter(Mandatory)] [string]$ResourceGroup,
    [Parameter(Mandatory)] [string]$StorageAccountName,
    [Parameter(Mandatory)] [string]$PrincipalId,
    [string[]]$ContainerNames = @('procedures', 'invoice-files', 'billing-exports')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RoleNamePrefix = 'Spaarke eBilling File Store Blob Data'
$BuiltInBlobDataRoles = @('Storage Blob Data Contributor', 'Storage Blob Data Owner', 'Storage Blob Data Reader')

# ============================================================================
# PURE CHECKS (Pester-tested)
# ============================================================================

function Get-FileStoreRoleDifferences {
    <# Every way $Definition (an az role definition object) differs from $BuiltIn (built-in Storage Blob Data Contributor) plus
       tags/read, with $ResourceGroupId the only assignable scope. Empty = exact. #>
    param(
        [Parameter(Mandatory)] $Definition,
        [Parameter(Mandatory)] $BuiltIn,
        [Parameter(Mandatory)] [string]$ResourceGroupId
    )

    $tagsRead = 'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/tags/read'
    $problems = [System.Collections.Generic.List[string]]::new()
    $blocks = @($Definition.permissions)
    if ($blocks.Count -ne 1) { $problems.Add("$($blocks.Count) permission blocks (expected exactly 1)") }
    $builtInBlock = @($BuiltIn.permissions)[0]

    $actual = @{
        actions        = @($blocks | ForEach-Object { @($_.actions) } | Where-Object { $_ })
        dataActions    = @($blocks | ForEach-Object { @($_.dataActions) } | Where-Object { $_ })
        notActions     = @($blocks | ForEach-Object { @($_.notActions) } | Where-Object { $_ })
        notDataActions = @($blocks | ForEach-Object { @($_.notDataActions) } | Where-Object { $_ })
    }
    $expected = @{
        actions     = @($builtInBlock.actions)
        dataActions = @(@($builtInBlock.dataActions) + $tagsRead)
    }

    foreach ($key in 'actions', 'dataActions') {
        foreach ($a in $actual[$key]) { if ($expected[$key] -cnotcontains $a) { $problems.Add("$key has unexpected '$a'") } }
        foreach ($e in $expected[$key]) { if ($actual[$key] -cnotcontains $e) { $problems.Add("$key lacks '$e'") } }
    }
    foreach ($a in @($actual.actions + $actual.dataActions)) {
        if ($a.Contains('*')) { $problems.Add("wildcard '$a'") }
        if ($a -match '/tags/write') { $problems.Add("tag write '$a'") }
    }
    foreach ($key in 'notActions', 'notDataActions') {
        if ($actual[$key].Count -gt 0) { $problems.Add("$key is not empty: $($actual[$key] -join ', ')") }
    }
    foreach ($b in $blocks) {
        if ($b.PSObject.Properties['condition'] -and $b.condition) { $problems.Add('has a condition') }
    }
    $scopes = @($Definition.assignableScopes)
    if ($scopes.Count -ne 1 -or $scopes[0] -ne $ResourceGroupId) {
        $problems.Add("assignableScopes is [$($scopes -join ', ')] (expected only [$ResourceGroupId])")
    }
    return $problems.ToArray()
}

function Get-FileStoreAssignmentProblems {
    <# Every assignment of the file-store role ($Assignments, from az role assignment list) not at one of $ContainerScopes, and every
       container scope where $PrincipalId lacks it. Scopes compare case-insensitively (ARM ids are). #>
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]]$Assignments,
        [Parameter(Mandatory)] [string[]]$ContainerScopes,
        [Parameter(Mandatory)] [string]$PrincipalId
    )

    $problems = [System.Collections.Generic.List[string]]::new()
    foreach ($a in $Assignments) {
        if (-not ($ContainerScopes | Where-Object { $_ -ieq $a.scope })) {
            $problems.Add("assignment to $($a.principalId) at '$($a.scope)' - not one of the file-store containers")
        }
    }
    foreach ($scope in $ContainerScopes) {
        if (-not ($Assignments | Where-Object { $_.scope -ieq $scope -and $_.principalId -ieq $PrincipalId })) {
            $problems.Add("principal $PrincipalId has no assignment at '$scope'")
        }
    }
    return $problems.ToArray()
}

# ============================================================================
# LIVE READS
# ============================================================================

function Invoke-AzJson([string[]]$Arguments) {
    $out = & az @Arguments --subscription $SubscriptionId -o json 2>$null
    if ($LASTEXITCODE -ne 0) { throw "az $($Arguments -join ' ') failed (exit $LASTEXITCODE)" }
    return ($out | ConvertFrom-Json)
}

function Write-Result([string]$Kind, [string]$Message) {
    $color = @{ PASS = 'Green'; FAIL = 'Red'; WARN = 'Yellow'; INFO = 'Gray' }[$Kind]
    Write-Host "   [$Kind] $Message" -ForegroundColor $color
}

$failed = $false
$rgId = "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup"
$accountId = "$rgId/providers/Microsoft.Storage/storageAccounts/$StorageAccountName"
$containerScopes = @($ContainerNames | ForEach-Object { "$accountId/blobServices/default/containers/$_" })

try {
    Write-Host "`n>> 1. Role definition is exact (uac-r2 condition 1)" -ForegroundColor Cyan
    $builtIn = @(Invoke-AzJson @('role', 'definition', 'list', '--name', 'Storage Blob Data Contributor'))[0]
    $roles = @(Invoke-AzJson @('role', 'definition', 'list', '--custom-role-only', 'true', '--scope', $rgId) |
        Where-Object { $_.roleName -like "$RoleNamePrefix*" })
    if ($roles.Count -ne 1) {
        Write-Result FAIL "expected one custom role named '$RoleNamePrefix (...)' assignable at $rgId, found $($roles.Count)"
        exit 2
    }
    $role = $roles[0]
    Write-Result INFO "role '$($role.roleName)' id $($role.name)"
    $diff = @(Get-FileStoreRoleDifferences -Definition $role -BuiltIn $builtIn -ResourceGroupId $rgId)
    if ($diff.Count -eq 0) { Write-Result PASS 'exactly Storage Blob Data Contributor (live) + blobs/tags/read; no wildcard, no tags/write' }
    else { $failed = $true; $diff | ForEach-Object { Write-Result FAIL $_ } }

    Write-Host "`n>> 2. Assigned per container only (uac-r2 condition 4)" -ForegroundColor Cyan
    $assignments = @(Invoke-AzJson @('role', 'assignment', 'list', '--role', $role.name, '--all'))
    Write-Result INFO "$($assignments.Count) assignment(s) of the role in the subscription"
    $scopeProblems = @(Get-FileStoreAssignmentProblems -Assignments $assignments -ContainerScopes $containerScopes -PrincipalId $PrincipalId)
    if ($scopeProblems.Count -eq 0) { Write-Result PASS "one assignment per container ($($ContainerNames -join ', ')), none above container scope" }
    else { $failed = $true; $scopeProblems | ForEach-Object { Write-Result FAIL $_ } }

    Write-Host "`n>> 3. Other blob data roles the principal holds on the account (context)" -ForegroundColor Cyan
    $held = @(Invoke-AzJson @('role', 'assignment', 'list', '--assignee', $PrincipalId, '--all') |
        Where-Object { $BuiltInBlobDataRoles -contains $_.roleDefinitionName -and ($accountId -ieq $_.scope -or $accountId.StartsWith($_.scope + '/', [StringComparison]::OrdinalIgnoreCase) -or $_.scope -eq '/') })
    if ($held.Count -eq 0) { Write-Result PASS 'no built-in Storage Blob Data role on the account or above' }
    else { $held | ForEach-Object { Write-Result WARN "holds '$($_.roleDefinitionName)' at '$($_.scope)' - container scoping is moot until it is removed (owner's live step)" } }

    Write-Host "`n>> 4. Data-plane 403 proof (uac-r2 condition 2)" -ForegroundColor Cyan
    Write-Result INFO 'not run here: run scripts/ebilling/test-ebilling-file-store-tag-write.sh in the App Service SSH console (as the identity)'
}
catch {
    Write-Result FAIL $_.Exception.Message
    exit 2
}

if ($failed) { exit 1 }
exit 0
