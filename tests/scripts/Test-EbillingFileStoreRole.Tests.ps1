# tests/scripts/Test-EbillingFileStoreRole.Tests.ps1
# ---------------------------------------------------------------------------
# ebilling-r1 task 091 (option A, unified-access-control-r2 conditions 1 and 4): scripts/ebilling/Test-EbillingFileStoreRole.ps1
# compares the LIVE custom role with LIVE built-in Storage Blob Data Contributor plus blobs/tags/read, and checks every assignment
# of it is at a file-store container. These tests prove the two pure checks with no az call:
#   1. Get-FileStoreRoleDifferences: must-not-fire on the exact set (any order); must-fire on tags/write, a tags wildcard, a blobs
#      wildcard, '*', a missing tags/read, a missing or extra action, notActions / notDataActions, a second block, a condition,
#      and an assignable scope other than the resource group;
#   2. Get-FileStoreAssignmentProblems: must-not-fire on one assignment per container (scope case differs); must-fire on an
#      account-, resource-group- or subscription-scope assignment, and on a container the principal lacks.
#
# Pester 3.4:  Import-Module Pester -RequiredVersion 3.4.0 ; Invoke-Pester tests/scripts/Test-EbillingFileStoreRole.Tests.ps1
# ---------------------------------------------------------------------------

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$script:ScriptPath = Join-Path $repoRoot 'scripts/ebilling/Test-EbillingFileStoreRole.ps1'

# Pull the functions under test out of the script (it cannot be dot-sourced: it has mandatory parameters and runs on load).
$errs = $null; $tokens = $null
$script:Ast = [System.Management.Automation.Language.Parser]::ParseFile($script:ScriptPath, [ref]$tokens, [ref]$errs)
foreach ($name in 'Get-FileStoreRoleDifferences', 'Get-FileStoreAssignmentProblems') {
    $fn = $script:Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name }, $true) | Select-Object -First 1
    Invoke-Expression $fn.Extent.Text
}

$script:Rg = '/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/rg-x'
$script:Account = "$script:Rg/providers/Microsoft.Storage/storageAccounts/sa"
$script:Containers = @('procedures', 'invoice-files', 'billing-exports' | ForEach-Object { "$script:Account/blobServices/default/containers/$_" })
$script:Principal = '11111111-1111-1111-1111-111111111111'
$script:Prefix = 'Microsoft.Storage/storageAccounts/blobServices'

# Built-in Storage Blob Data Contributor as az returns it (2026-10-21).
$script:BuiltIn = [pscustomobject]@{ permissions = @([pscustomobject]@{
    actions        = @("$script:Prefix/containers/delete", "$script:Prefix/containers/read", "$script:Prefix/containers/write", "$script:Prefix/generateUserDelegationKey/action")
    dataActions    = @("$script:Prefix/containers/blobs/delete", "$script:Prefix/containers/blobs/read", "$script:Prefix/containers/blobs/write", "$script:Prefix/containers/blobs/move/action", "$script:Prefix/containers/blobs/add/action")
    notActions     = @()
    notDataActions = @() }) }

function New-Role {
    param([string[]]$Actions, [string[]]$DataActions, [string[]]$NotActions = @(), [string[]]$NotDataActions = @(), [string[]]$Scopes = @($script:Rg), [switch]$ExtraBlock, [string]$Condition)
    $block = [pscustomobject]@{ actions = $Actions; dataActions = $DataActions; notActions = $NotActions; notDataActions = $NotDataActions; condition = $Condition }
    $blocks = @($block)
    if ($ExtraBlock) { $blocks += [pscustomobject]@{ actions = @(); dataActions = @(); notActions = @(); notDataActions = @(); condition = $null } }
    [pscustomobject]@{ roleName = 'Spaarke eBilling File Store Blob Data (x)'; permissions = $blocks; assignableScopes = $Scopes }
}

$script:A = @($script:BuiltIn.permissions[0].actions)
$script:D = @($script:BuiltIn.permissions[0].dataActions) + "$script:Prefix/containers/blobs/tags/read"

Describe 'Get-FileStoreRoleDifferences (uac-r2 condition 1)' {
    It 'must-not-fire: the exact set, in reverse order' {
        $role = New-Role -Actions ($script:A[($script:A.Count - 1)..0]) -DataActions ($script:D[($script:D.Count - 1)..0])
        @(Get-FileStoreRoleDifferences -Definition $role -BuiltIn $script:BuiltIn -ResourceGroupId $script:Rg).Count | Should Be 0
    }

    $cases = @(
        @{ Name = 'tags/write added';         Role = { New-Role -Actions $script:A -DataActions ($script:D + "$script:Prefix/containers/blobs/tags/write") } }
        @{ Name = 'tags wildcard';            Role = { New-Role -Actions $script:A -DataActions (@($script:BuiltIn.permissions[0].dataActions) + "$script:Prefix/containers/blobs/tags/*") } }
        @{ Name = 'blobs wildcard';           Role = { New-Role -Actions $script:A -DataActions @("$script:Prefix/containers/blobs/*") } }
        @{ Name = 'star';                     Role = { New-Role -Actions ($script:A + '*') -DataActions $script:D } }
        @{ Name = 'tags/read missing';        Role = { New-Role -Actions $script:A -DataActions @($script:BuiltIn.permissions[0].dataActions) } }
        @{ Name = 'action missing';           Role = { New-Role -Actions ($script:A | Select-Object -Skip 1) -DataActions $script:D } }
        @{ Name = 'extra action';             Role = { New-Role -Actions ($script:A + 'Microsoft.Storage/storageAccounts/listKeys/action') -DataActions $script:D } }
        @{ Name = 'notActions set';           Role = { New-Role -Actions $script:A -DataActions $script:D -NotActions @("$script:Prefix/containers/delete") } }
        @{ Name = 'notDataActions set';       Role = { New-Role -Actions $script:A -DataActions $script:D -NotDataActions @("$script:Prefix/containers/blobs/tags/read") } }
        @{ Name = 'second permission block';  Role = { New-Role -Actions $script:A -DataActions $script:D -ExtraBlock } }
        @{ Name = 'condition';                Role = { New-Role -Actions $script:A -DataActions $script:D -Condition "@Resource[x] StringEquals 'y'" } }
        @{ Name = 'subscription assignable';  Role = { New-Role -Actions $script:A -DataActions $script:D -Scopes @('/subscriptions/00000000-0000-0000-0000-000000000001') } }
        @{ Name = 'second assignable scope';  Role = { New-Role -Actions $script:A -DataActions $script:D -Scopes @($script:Rg, '/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/rg-y') } }
    )
    foreach ($case in $cases) {
        It "must-fire: $($case.Name)" {
            $role = & $case.Role
            @(Get-FileStoreRoleDifferences -Definition $role -BuiltIn $script:BuiltIn -ResourceGroupId $script:Rg).Count | Should BeGreaterThan 0
        }
    }

    It 'names tags/write explicitly' {
        $role = New-Role -Actions $script:A -DataActions ($script:D + "$script:Prefix/containers/blobs/tags/write")
        (Get-FileStoreRoleDifferences -Definition $role -BuiltIn $script:BuiltIn -ResourceGroupId $script:Rg) -join '|' | Should Match 'tag write'
    }
}

Describe 'Get-FileStoreAssignmentProblems (uac-r2 condition 4)' {
    function New-Assignment([string]$Scope, [string]$Principal = $script:Principal) { [pscustomobject]@{ scope = $Scope; principalId = $Principal } }

    It 'must-not-fire: one assignment per container, scope case differs' {
        $assignments = @($script:Containers | ForEach-Object { New-Assignment $_.ToLowerInvariant() })
        @(Get-FileStoreAssignmentProblems -Assignments $assignments -ContainerScopes $script:Containers -PrincipalId $script:Principal).Count | Should Be 0
    }

    foreach ($case in @(
            @{ Name = 'account scope'; Scope = $script:Account }
            @{ Name = 'resource-group scope'; Scope = $script:Rg }
            @{ Name = 'subscription scope'; Scope = '/subscriptions/00000000-0000-0000-0000-000000000001' }
            @{ Name = 'another container'; Scope = "$script:Account/blobServices/default/containers/ai-chunks" })) {
        It "must-fire: an extra assignment at $($case.Name)" {
            $assignments = @($script:Containers | ForEach-Object { New-Assignment $_ }) + (New-Assignment $case.Scope)
            $problems = @(Get-FileStoreAssignmentProblems -Assignments $assignments -ContainerScopes $script:Containers -PrincipalId $script:Principal)
            $problems.Count | Should Be 1
            $problems[0] | Should Match 'not one of the file-store containers'
        }
    }

    It 'must-fire: a container granted to another principal only' {
        $assignments = @(New-Assignment $script:Containers[0]; New-Assignment $script:Containers[1]; New-Assignment $script:Containers[2] '22222222-2222-2222-2222-222222222222')
        $problems = @(Get-FileStoreAssignmentProblems -Assignments $assignments -ContainerScopes $script:Containers -PrincipalId $script:Principal)
        $problems.Count | Should Be 1
        $problems[0] | Should Match 'has no assignment'
    }

    It 'must-fire: no assignment at all' {
        @(Get-FileStoreAssignmentProblems -Assignments @() -ContainerScopes $script:Containers -PrincipalId $script:Principal).Count | Should Be 3
    }
}
