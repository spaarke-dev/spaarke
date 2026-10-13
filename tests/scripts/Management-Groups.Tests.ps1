# tests/scripts/Management-Groups.Tests.ps1
# ---------------------------------------------------------------------------
# customer-provisioning-orchestration-r1 task 262 (G36, ADR-027): the management-group hierarchy, the common customer
# policy and the script that applies them. Proves:
#   - customer-policy.bicep assigns BUILT-IN definitions only, every assignment is Audit or DoNotEnforce (nothing can
#     refuse a deployment), every one has a non-compliance message and a name of at most 24 characters;
#   - the policy only asks for what customer.bicep already does (its regions, the tags it sets on its resource group);
#   - management-groups.bicep's defaults equal spaarke-constants.yaml `management_groups` (PRQ-S-06 reads the latter);
#   - Deploy-ManagementGroups.ps1 makes no write call without -Apply, refuses -Apply without the access it needs, moves
#     only subscriptions not already in place, and never runs `az account set`.
#
# Technique (as Set-AiSpendLimit.Tests.ps1): Pester 3.4 cannot Mock an external application, so `az` is shadowed by a
# global function that records each call and answers from a small in-memory model. The Bicep cases compile with the
# real `az bicep build` (no Azure call) before any shadow is installed.
#
# Pester 3.4:  Import-Module Pester -RequiredVersion 3.4.0 ; Invoke-Pester tests/scripts/Management-Groups.Tests.ps1
# ---------------------------------------------------------------------------

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$script:Script = Join-Path $repoRoot 'scripts/provisioning/Deploy-ManagementGroups.ps1'
$script:MgBicep = Join-Path $repoRoot 'infrastructure/bicep/management-groups.bicep'
$script:PolicyBicep = Join-Path $repoRoot 'infrastructure/bicep/customer-policy.bicep'
$script:CustomerBicep = Join-Path $repoRoot 'infrastructure/bicep/customer.bicep'
$script:Constants = Join-Path $repoRoot 'scripts/provisioning-prereqs/spaarke-constants.yaml'

if (-not (Get-Module -ListAvailable -Name powershell-yaml)) { Install-Module powershell-yaml -Scope CurrentUser -Force -Confirm:$false | Out-Null }
Import-Module powershell-yaml
$script:Mg = (Get-Content -Raw $script:Constants | ConvertFrom-Yaml).management_groups

$script:Tenant = 'aaaaaaaa-0000-0000-0000-000000000001'
$script:Demo = '2ff9ee48-6f1d-4664-865c-f11868dd1b50'
$script:Dev = '484bc857-3802-427f-9ea5-ca47b43db0f0'
$script:Shared = 'cd95fcec-6b89-49ea-8339-c2b579b12587'

# --- az shadow --------------------------------------------------------------------------------------------------
# $State: Groups = @{ name = parentName }, Parents = @{ subId = groupName }, Roles = @('...').
function Set-AzShadow {
    param([hashtable]$State)
    $global:AzCalls = [System.Collections.Generic.List[string]]::new()
    $global:AzState = $State
    Set-Item -Path function:global:az -Value {
        $line = $args -join ' '
        $global:AzCalls.Add($line)
        $global:LASTEXITCODE = 0
        $s = $global:AzState
        $sub = $null; $i = [array]::IndexOf($args, '--subscription'); if ($i -ge 0) { $sub = $args[$i + 1] }
        $name = $null; $i = [array]::IndexOf($args, '--name'); if ($i -ge 0) { $name = $args[$i + 1] }
        $mgid = $null; $i = [array]::IndexOf($args, '--management-group-id'); if ($i -ge 0) { $mgid = $args[$i + 1] }
        switch -Regex ($line) {
            '^account show .*--query tenantId' { return $global:AzTenant }
            '^account show ' { return (@{ tenantId = $global:AzTenant; name = "sub $sub" } | ConvertTo-Json) }
            '^account management-group list' { return (@($s.Groups.Keys | ForEach-Object { @{ name = $_ } }) + @(@{ name = $global:AzTenant }) | ConvertTo-Json) }
            '^account management-group show .*--recurse' {
                function node($n) {
                    $kids = @($s.Groups.Keys | Where-Object { $s.Groups[$_] -eq $n } | ForEach-Object { node $_ }) +
                            @($s.Parents.Keys | Where-Object { $s.Parents[$_] -eq $n } | ForEach-Object { @{ name = $_; type = '/subscriptions'; children = $null } })
                    @{ name = $n; type = 'Microsoft.Management/managementGroups'; children = $kids }
                }
                return (node $global:AzTenant | ConvertTo-Json -Depth 20)
            }
            '^account management-group show .*details\.parent\.name' { return $s.Groups[$name] }
            '^ad signed-in-user show' { return 'me' }
            '^role assignment list' { return ($s.Roles -join "`n") }
            '^deployment tenant create' {
                $p = @{}; foreach ($a in $args) { if ($a -match '^(\w+)=(.+)$') { $p[$Matches[1]] = $Matches[2] } }
                $s.Groups[$p.environmentsGroupId] = $global:AzTenant; $s.Groups[$p.customersGroupId] = $p.environmentsGroupId
                return ''
            }
            '^deployment mg create' { $s.Assigned = $true; return (@('sprk-locations', 'sprk-kv-rbac') | ForEach-Object { "/providers/Microsoft.Management/managementGroups/$mgid/providers/Microsoft.Authorization/policyAssignments/$_" } | ConvertTo-Json) }
            '^account management-group subscription add' { $s.Parents[$sub] = $name; return '' }
            '^account management-group subscription show' {
                if ($s.Parents[$sub] -ne $name) { $global:LASTEXITCODE = 3; return "ERROR: (NotFound) not found" }
                return "/providers/Microsoft.Management/managementGroups/$name"
            }
            '^policy assignment list' { if ($s.Assigned) { return (@('sprk-locations', 'sprk-kv-rbac') | Where-Object { $_ -ne $s.DropAssignment }) -join "`n" } else { return '' } }
            default { $global:LASTEXITCODE = 99; return "FAKE az: no rule for: $line" }
        }
    }
}
function Remove-AzShadow { Remove-Item -Path function:global:az -ErrorAction SilentlyContinue }
function New-RootState([string[]]$Roles) {
    $global:AzTenant = $script:Tenant
    @{ Groups = @{}; Parents = @{ $script:Demo = $script:Tenant; $script:Dev = $script:Tenant; $script:Shared = $script:Tenant }; Roles = $Roles; Assigned = $false }
}
$script:WriteCall = '^(deployment (tenant|mg) create|account management-group (subscription add|create|update)|policy assignment create)'

# --- Bicep (compiled with the real az bicep; no Azure call) --------------------------------------------------------
Describe 'customer-policy.bicep (T262)' {
    $out = Join-Path ([IO.Path]::GetTempPath()) ("mg-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $out | Out-Null
    & az bicep build --file $script:PolicyBicep --outfile (Join-Path $out 'policy.json') 2>&1 | Out-Null
    $built = $LASTEXITCODE
    $json = Get-Content -Raw (Join-Path $out 'policy.json') | ConvertFrom-Json
    Remove-Item -Recurse -Force $out -ErrorAction SilentlyContinue
    $vars = $json.variables
    $res = @($json.resources | Where-Object { $_.type -eq 'Microsoft.Authorization/policyAssignments' })
    $tagCopy = @($vars.copy | Where-Object { $_.name -eq 'tagAssignments' })[0]
    $defaultTags = @($json.parameters.requiredResourceGroupTags.defaultValue)
    $defaultLocations = @($json.parameters.allowedLocations.defaultValue)

    It 'compiles' { $built | Should Be 0 }
    It 'targets a management group' { $json.'$schema' | Should Match 'managementGroupDeploymentTemplate' }
    It 'assigns through tenantResourceId(policyDefinitions) only - a built-in id, never a custom definition' {
        $res.Count | Should Be 1
        $res[0].properties.policyDefinitionId | Should Match "^\[tenantResourceId\('Microsoft\.Authorization/policyDefinitions', "
        $json.resources | Where-Object { $_.type -match 'policyDefinitions|policySetDefinitions' } | Should BeNullOrEmpty
    }
    It 'names only the verified built-in definition ids' {
        $verified = @('e56962a6-4747-49cd-b67b-bf8b01975c4c', 'e765b5de-1225-4ba3-bd56-1ac6695af988', '96670d01-0a4d-4649-9c89-2d3abc0a5025',
            '404c3081-a854-4457-ae30-26a93ef643f9', 'fe83a0eb-a853-422d-aac2-1bffd182c5d0', '12d4fa5e-1f9f-4c21-97a9-b99b3c6611b5',
            '0b60c0b2-2dc2-4e1c-b5c9-abbed971de53')
        @($vars.builtIn.PSObject.Properties.Value | Where-Object { $_ -notin $verified }) | Should BeNullOrEmpty
    }
    It 'every fixed assignment is Audit (enforcement Default) or DoNotEnforce, has a message and a name of at most 24 characters' {
        foreach ($a in $vars.fixedAssignments) {
            $ok = ($a.enforcementMode -eq 'DoNotEnforce') -or ($a.enforcementMode -eq 'Default' -and $a.parameters.effect.value -eq 'Audit')
            "$($a.name):$ok" | Should Be "$($a.name):True"
            [string]::IsNullOrWhiteSpace([string]$a.message) | Should Be $false
            $a.name.Length | Should BeLessThan 25
        }
    }
    It 'every tag assignment is DoNotEnforce (the built-in is deny-only) with a message, and its name fits 24 characters' {
        $tagCopy.input.enforcementMode | Should Be 'DoNotEnforce'
        [string]$tagCopy.input.message | Should Match 'tag'
        foreach ($t in $defaultTags) { ("sprk-rgtag-" + $t.ToLowerInvariant()).Length | Should BeLessThan 25 }
    }
    It 'passes every assignment its non-compliance message and enforcement mode' {
        $res[0].properties.nonComplianceMessages[0].message | Should Match "message"
        $res[0].properties.enforcementMode | Should Match 'enforcementMode'
    }
    It 'requires only tags customer.bicep sets on its resource group (never component)' {
        $src = Get-Content -Raw $script:CustomerBicep
        $block = [regex]::Match($src, '(?s)param tags object = \{(.*?)\n\}').Groups[1].Value
        $set = [regex]::Matches($block, '(?m)^\s*(\w+):') | ForEach-Object { $_.Groups[1].Value }
        $defaultTags.Count | Should BeGreaterThan 0
        @($defaultTags | Where-Object { $_ -notin $set }) | Should BeNullOrEmpty
        $defaultTags -contains 'component' | Should Be $false
    }
    It 'allows every region customer.bicep deploys to by default (location, openAiLocation, contentSafetyLocation)' {
        $src = Get-Content -Raw $script:CustomerBicep
        foreach ($p in 'location', 'openAiLocation', 'contentSafetyLocation') {
            $v = [regex]::Match($src, "(?m)^param $p string = '([a-z0-9]+)'").Groups[1].Value
            $v | Should Not BeNullOrEmpty
            $defaultLocations -contains $v | Should Be $true
        }
    }
}

Describe 'management-groups.bicep mirrors spaarke-constants.yaml (T262)' {
    $src = Get-Content -Raw $script:MgBicep
    function Default($p) { [regex]::Match($src, "(?m)^param $p string = '([^']+)'").Groups[1].Value }
    It 'is a tenant-scope template' { $src | Should Match "(?m)^targetScope = 'tenant'" }
    It 'environments group id + display name' {
        Default 'environmentsGroupId' | Should Be $script:Mg.environments.id
        Default 'environmentsGroupDisplayName' | Should Be $script:Mg.environments.displayName
    }
    It 'customers group id + display name' {
        Default 'customersGroupId' | Should Be $script:Mg.customers.id
        Default 'customersGroupDisplayName' | Should Be $script:Mg.customers.displayName
    }
    It 'ids are lowercase-hyphenated' {
        $script:Mg.environments.id | Should MatchExactly '^[a-z][a-z0-9-]+$'
        $script:Mg.customers.id | Should MatchExactly '^[a-z][a-z0-9-]+$'
    }
    It 'nests customers under environments' { $src | Should Match '(?s)resource customers .*parent: \{\s*id: environments\.id' }
}

# --- Deploy-ManagementGroups.ps1 against the shadow ------------------------------------------------------------------
Describe 'Deploy-ManagementGroups.ps1 (T262)' {
    AfterEach { Remove-AzShadow }

    It 'plan mode (the default) makes only read-only calls and changes nothing' {
        Set-AzShadow (New-RootState @('Owner'))
        & $script:Script *> $null
        $LASTEXITCODE | Should Be 0
        @($global:AzCalls | Where-Object { $_ -match $script:WriteCall }) | Should BeNullOrEmpty
        $global:AzState.Groups.Count | Should Be 0
    }

    It '-WhatIf wins over -Apply' {
        Set-AzShadow (New-RootState @('Owner'))
        & $script:Script -Apply -WhatIf *> $null
        @($global:AzCalls | Where-Object { $_ -match $script:WriteCall }) | Should BeNullOrEmpty
    }

    It 'refuses -Apply with User Access Administrator alone at "/" (no deployment right), before any write' {
        Set-AzShadow (New-RootState @('User Access Administrator'))
        { & $script:Script -Apply *> $null } | Should Throw 'Refusing -Apply'
        @($global:AzCalls | Where-Object { $_ -match $script:WriteCall }) | Should BeNullOrEmpty
    }

    It '-Apply with Contributor + User Access Administrator: creates the groups from the constants, assigns the policy at spaarke-customers, moves and verifies' {
        Set-AzShadow (New-RootState @('Contributor', 'User Access Administrator'))
        & $script:Script -Apply *> $null
        $LASTEXITCODE | Should Be 0
        $calls = $global:AzCalls -join "`n"
        $calls | Should Match "deployment tenant create .*environmentsGroupId=$($script:Mg.environments.id) .*customersGroupId=$($script:Mg.customers.id)"
        $calls | Should Match "deployment mg create --management-group-id $($script:Mg.customers.id) "
        $global:AzState.Parents[$script:Demo] | Should Be $script:Mg.customers.id
        $global:AzState.Parents[$script:Dev] | Should Be $script:Mg.environments.id
        $global:AzState.Parents[$script:Shared] | Should Be $script:Mg.environments.id
        $global:AzState.Groups[$script:Mg.customers.id] | Should Be $script:Mg.environments.id
    }

    It 'moves only a subscription not already in place, and every subscription call names --subscription' {
        $st = New-RootState @('Owner')
        $st.Groups = @{ $script:Mg.environments.id = $script:Tenant; $script:Mg.customers.id = $script:Mg.environments.id }
        $st.Parents[$script:Demo] = $script:Mg.customers.id
        Set-AzShadow $st
        & $script:Script -Apply -CustomerSubscriptionIds $script:Demo *> $null
        $LASTEXITCODE | Should Be 0
        @($global:AzCalls | Where-Object { $_ -match 'subscription add' }) | Should BeNullOrEmpty
        @($global:AzCalls | Where-Object { $_ -match '^account (show|management-group subscription)' -and $_ -notmatch '--subscription [0-9a-f-]{36}' }) | Should BeNullOrEmpty
    }

    It 'fails (exit 1) when a deployed policy assignment is not listed at the group on read-back' {
        $st = New-RootState @('Owner'); $st.DropAssignment = 'sprk-kv-rbac'
        Set-AzShadow $st
        & $script:Script -Apply *> $null
        $LASTEXITCODE | Should Be 1
    }

    It 'a new customer (only -CustomerSubscriptionIds) moves that subscription alone - the initial placement applies only when no list is passed' {
        $new = '99999999-9999-9999-9999-999999999999'
        $st = New-RootState @('Owner'); $st.Parents[$new] = $script:Tenant
        Set-AzShadow $st
        & $script:Script -Apply -CustomerSubscriptionIds $new *> $null
        $LASTEXITCODE | Should Be 0
        @($global:AzCalls | Where-Object { $_ -match 'subscription add' }).Count | Should Be 1
        $global:AzState.Parents[$new] | Should Be $script:Mg.customers.id
        $global:AzState.Parents[$script:Dev] | Should Be $script:Tenant
    }

    It 'never runs az account set' {
        Set-AzShadow (New-RootState @('Owner'))
        & $script:Script -Apply *> $null
        @($global:AzCalls | Where-Object { $_ -match '^account set' }) | Should BeNullOrEmpty
    }

    It 'refuses a subscription listed for both groups, before any az call' {
        Set-AzShadow (New-RootState @('Owner'))
        { & $script:Script -CustomerSubscriptionIds $script:Dev -EnvironmentSubscriptionIds $script:Dev *> $null } | Should Throw 'both groups'
        $global:AzCalls.Count | Should Be 0
    }

    It 'refuses when spaarke-customers already exists under another parent' {
        $st = New-RootState @('Owner')
        $st.Groups = @{ $script:Mg.environments.id = $script:Tenant; $script:Mg.customers.id = $script:Tenant }
        Set-AzShadow $st
        { & $script:Script -Apply *> $null } | Should Throw 'Resolve by hand'
        @($global:AzCalls | Where-Object { $_ -match $script:WriteCall }) | Should BeNullOrEmpty
    }
}
