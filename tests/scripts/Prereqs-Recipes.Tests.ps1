# tests/scripts/Prereqs-Recipes.Tests.ps1
# ---------------------------------------------------------------------------
# customer-provisioning-orchestration-r1 tasks 206 + 207: the check recipes in scripts/provisioning-prereqs/prereqs.yaml
# decide a provisioning run's Step 0.5 result by EXIT CODE. These tests run the real recipe text under `bash -c`
# against fake `az` / `curl` / `pac` / `pwsh` commands and prove that the specific condition each prereq exists to
# catch exits 1, and that the satisfied state exits 0. No live call is made.
#
# Technique: a tiny bash dispatcher is installed as az / curl / pac / pwsh on a private PATH directory. Each tool
# reads "<tool>.rules" from $FAKE_DIR - one rule per line, TAB separated: <substring of the argument line>, <exit
# code>, <stdout>, <body written to the -o file, curl only>. The first matching rule wins; an unmatched call exits
# 99 so a recipe that makes an unexpected call fails loudly instead of passing by accident.
#
# Pester 3.4:  Import-Module Pester -RequiredVersion 3.4.0 ; Invoke-Pester tests/scripts/Prereqs-Recipes.Tests.ps1
# Requires bash (Git Bash on Windows) on PATH and the powershell-yaml module (installed by validate.ps1).
# ---------------------------------------------------------------------------

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$script:ManifestPath = Join-Path $repoRoot 'scripts/provisioning-prereqs/prereqs.yaml'
$script:Validate = Join-Path $repoRoot 'scripts/provisioning-prereqs/validate.ps1'
$script:Catalog = Join-Path $repoRoot 'src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/RuntimeReferences/PinnedModelCatalog.cs'
$script:GraphRoles = Join-Path $repoRoot 'src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/ControlPlaneGraphAppRoles.cs'
$script:Dedication = Join-Path $repoRoot 'src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/SubscriptionReadiness/SubscriptionDedication.cs'
$script:ReadinessOptions = Join-Path $repoRoot 'src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/SubscriptionReadiness/SubscriptionReadinessOptions.cs'
$script:CustomerBicep = Join-Path $repoRoot 'infrastructure/bicep/customer.bicep'

if (-not (Get-Module -ListAvailable -Name powershell-yaml)) { Install-Module powershell-yaml -Scope CurrentUser -Force -Confirm:$false | Out-Null }
Import-Module powershell-yaml
$script:Prereqs = @{}
foreach ($p in (Get-Content -Raw $script:ManifestPath | ConvertFrom-Yaml).prereqs) { $script:Prereqs[$p.id] = $p }

$script:Dispatcher = @'
#!/usr/bin/env bash
tool=$(basename "$0" .sh)
args="$*"
echo "$tool $args" >> "$FAKE_DIR/calls.log"
out_file=""
prev=""
for a in "$@"; do
  if [ "$prev" = "-o" ]; then out_file="$a"; fi
  prev="$a"
done
if [ -f "$FAKE_DIR/$tool.rules" ]; then
  while IFS=$'\t' read -r pat rc out body; do
    [ -z "$pat" ] && continue
    case "$args" in
      *"$pat"*)
        [ "$tool" = curl ] && [ -n "$out_file" ] && printf '%b' "$body" > "$out_file"
        printf '%b' "$out"
        exit "$rc" ;;
    esac
  done < "$FAKE_DIR/$tool.rules"
fi
echo "FAKE $tool: no rule for: $args" >&2
exit 99
'@

# `bash` on a Windows PowerShell PATH can be the WSL stub (C:\Windows\System32\bash.exe, "no installed distributions").
# Use Git for Windows' bash when it is there - it is the shell the recipes are written for.
function Get-BashPath {
    if ($IsWindows) {
        $git = Get-Command git -ErrorAction SilentlyContinue
        if ($git) {
            $candidate = Join-Path (Split-Path (Split-Path $git.Source)) 'bin/bash.exe'
            if (Test-Path $candidate) { return $candidate }
        }
    }
    return 'bash'
}
$script:Bash = Get-BashPath
function New-FakeTools {
    $dir = Join-Path ([IO.Path]::GetTempPath()) ("prq-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dir | Out-Null
    foreach ($tool in 'az', 'curl', 'pac', 'pwsh') {
        [IO.File]::WriteAllText((Join-Path $dir $tool), ($script:Dispatcher -replace "`r`n", "`n"), (New-Object Text.UTF8Encoding $false))
        if (-not $IsWindows) { & chmod +x (Join-Path $dir $tool) }
    }
    return $dir
}

function Set-Rules {
    param([string]$Dir, [string]$Tool, [string[][]]$Rules)
    $lines = foreach ($r in $Rules) { ($r -join "`t") }
    [IO.File]::WriteAllText((Join-Path $Dir "$Tool.rules"), (($lines -join "`n") + "`n"), (New-Object Text.UTF8Encoding $false))
}

# Substitute {tokens} the way SKILL Step 0.5b does, then run the recipe under bash -c with the fakes first on PATH.
function Invoke-Recipe {
    param([string]$Id, [hashtable]$Tokens, [string]$Dir)
    $cli = [string]$script:Prereqs[$Id].check_recipe.cli
    foreach ($k in $Tokens.Keys) { $cli = $cli.Replace("{$k}", [string]$Tokens[$k]) }
    $left = [regex]::Match($cli, '\{[a-zA-Z_][a-zA-Z_0-9]*\}')
    if ($left.Success) { throw "$Id still has the unresolved placeholder $($left.Value)" }
    $oldPath = $env:PATH
    $env:PATH = "$Dir$([IO.Path]::PathSeparator)$oldPath"
    $env:FAKE_DIR = $Dir
    try {
        # Git for Windows' bash puts /mingw64/bin, /usr/bin and ~/bin ahead of the inherited PATH, so the fakes are put first from inside bash.
        $prefix = 'if command -v cygpath >/dev/null 2>&1; then fake=$(cygpath -u "$FAKE_DIR"); else fake="$FAKE_DIR"; fi; PATH="$fake:$PATH"' + "`n"
        $output = & $script:Bash -c ($prefix + $cli) 2>&1 | Out-String
        return @{ Exit = $LASTEXITCODE; Output = $output.Trim() }
    }
    finally { $env:PATH = $oldPath; Remove-Item Env:FAKE_DIR -ErrorAction SilentlyContinue }
}

$script:Common = @{
    env = 'dev'; openAiRegion = 'westus3'; l2UamiPrincipalId = '11111111-1111-1111-1111-111111111111'
    l2UamiClientId = '22222222-2222-2222-2222-222222222222'; l2UamiSpId = '33333333-3333-3333-3333-333333333333'
    graphAppId = '00000003-0000-0000-c000-000000000000'; sbNamespace = 'spaarke-servicebus-dev'
    artifactsStorageId = '/subscriptions/s/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/sprkcpartifactsdev'
    acrId = '/subscriptions/s/resourceGroups/rg/providers/Microsoft.ContainerRegistry/registries/sprkcontrolplanedevacr'
    kvResourceId = '/subscriptions/s/resourceGroups/rg/providers/Microsoft.KeyVault/vaults/sprk-controlplane-dev-kv'
    containerTypeId = 'fb3817a8-5a55-42ba-8cc9-12cf055168b8'; adminDvUrl = 'https://spaarkedev1.crm.dynamics.com'
    customerId = 'acme'; stampSubscriptionId = '44444444-4444-4444-4444-444444444444'; stampEnvironment = 'prod'
    dvUrl = 'https://spaarke-acme.crm.dynamics.com/'; exchangePolicyScopeGroupId = 'scope@acme.example'
    environmentSecurityGroupId = '55555555-5555-5555-5555-555555555555'; customerManagementGroupId = 'spaarke-customers'
}

Describe 'prereqs.yaml recipes honour the exit-code contract' {
    BeforeEach { $script:Dir = New-FakeTools }
    AfterEach { Remove-Item -Recurse -Force $script:Dir -ErrorAction SilentlyContinue }

    Context 'PRQ-S-00 dedicated subscription (T186: it refused ANY rg-spaarke-* group; H1 counts only rg-spaarke-{id}-{env} of another id)' {
        BeforeEach { $script:S00Names = { param($n) Set-Rules $script:Dir az @(@('account show', '0', 'Enabled\r\n'), @('group list', '0', $n)) } }
        It 'exits 0 for an empty subscription, and for a group that is not rg-spaarke-{id}-{env} (rg-spaarke-demo, unrelated groups)' {
            & $script:S00Names ''
            (Invoke-Recipe 'PRQ-S-00' $script:Common $script:Dir).Exit | Should Be 0
            & $script:S00Names 'rg-spaarke-demo\r\nrg-ppbilling-acme\r\nNetworkWatcherRG\r\nrg-spaarke-ab-dev\r\n'
            (Invoke-Recipe 'PRQ-S-00' $script:Common $script:Dir).Exit | Should Be 0
        }
        It 'exits 0 for the customer''s own stamp group, whatever the case or env' {
            & $script:S00Names 'rg-spaarke-acme-dev\r\nRG-Spaarke-ACME-prod\r\n'
            (Invoke-Recipe 'PRQ-S-00' $script:Common $script:Dir).Exit | Should Be 0
        }
        It 'exits 1 and names another customer''s stamp group' {
            & $script:S00Names 'rg-spaarke-demo\r\nrg-spaarke-other-prod\r\n'
            $r = Invoke-Recipe 'PRQ-S-00' $script:Common $script:Dir
            $r.Exit | Should Be 1
            $r.Output | Should Match 'rg-spaarke-other-prod'
            $r.Output | Should Not Match 'rg-spaarke-demo'
        }
        It 'exits 1 for Spaarke''s own reserved ids (platform, shared, byok) - foreign to any customer' {
            & $script:S00Names 'rg-spaarke-platform-dev\n'
            (Invoke-Recipe 'PRQ-S-00' $script:Common $script:Dir).Exit | Should Be 1
            & $script:S00Names 'rg-spaarke-shared-prod\n'
            (Invoke-Recipe 'PRQ-S-00' $script:Common $script:Dir).Exit | Should Be 1
        }
        It 'exits 1 for an id that merely starts with the customerId (acmeco, acme0), as H1 compares the whole id' {
            & $script:S00Names 'rg-spaarke-acmeco-prod\n'
            (Invoke-Recipe 'PRQ-S-00' $script:Common $script:Dir).Exit | Should Be 1
            & $script:S00Names 'rg-spaarke-acme0-dev\n'
            (Invoke-Recipe 'PRQ-S-00' $script:Common $script:Dir).Exit | Should Be 1
        }
        It 'exits 1 when the subscription is not Enabled' {
            Set-Rules $script:Dir az @(@('account show', '0', 'Disabled\n'), @('group list', '0', ''))
            $r = Invoke-Recipe 'PRQ-S-00' $script:Common $script:Dir
            $r.Exit | Should Be 1
            $r.Output | Should Match 'not Enabled'
        }
    }

    Context 'PRQ-S-01 billing agreement (T186: az account show returns no quotaId on az 2.77; ARM does)' {
        It 'exits 0 and prints the quotaId read from ARM' {
            Set-Rules $script:Dir az @(, @('api-version=2022-12-01', '0', 'PayAsYouGo_2014-09-01\r\n'))
            $r = Invoke-Recipe 'PRQ-S-01' $script:Common $script:Dir
            $r.Exit | Should Be 0
            $r.Output | Should Match 'ok quotaId=PayAsYouGo_2014-09-01'
            $calls = Get-Content (Join-Path $script:Dir 'calls.log') -Raw
            $calls | Should Match 'az rest --method get --url https://management.azure.com/subscriptions/44444444-4444-4444-4444-444444444444\?api-version=2022-12-01 --query subscriptionPolicies.quotaId'
            $calls | Should Not Match 'account show'
        }
        It 'exits 1 when ARM returns no quotaId' {
            Set-Rules $script:Dir az @(, @('api-version=2022-12-01', '0', ''))
            (Invoke-Recipe 'PRQ-S-01' $script:Common $script:Dir).Exit | Should Be 1
        }
        It 'exits 1 when the ARM call fails' {
            Set-Rules $script:Dir az @(, @('api-version=2022-12-01', '1', ''))
            (Invoke-Recipe 'PRQ-S-01' $script:Common $script:Dir).Exit | Should Be 1
        }
    }

    Context 'PRQ-S-03 provider registration (T206: the loop used to exit 0 on a partial registration; T186: the list is the stamp template''s plus H1''s)' {
        It 'exits 0 when every namespace is Registered' {
            Set-Rules $script:Dir az @(, @('provider show', '0', 'Registered\r\n'))
            (Invoke-Recipe 'PRQ-S-03' $script:Common $script:Dir).Exit | Should Be 0
        }
        It 'exits 1 and names the namespace that is not registered' {
            Set-Rules $script:Dir az @(@('namespace Microsoft.Cache ', '0', 'NotRegistered\n'), @('provider show', '0', 'Registered\n'))
            $r = Invoke-Recipe 'PRQ-S-03' $script:Common $script:Dir
            $r.Exit | Should Be 1
            $r.Output | Should Match 'Microsoft.Cache'
        }
        It 'exits 1 for each of EventGrid, SignalRService, Communication and OperationalInsights when it alone is not registered' {
            foreach ($ns in 'Microsoft.EventGrid', 'Microsoft.SignalRService', 'Microsoft.Communication', 'Microsoft.OperationalInsights') {
                Set-Rules $script:Dir az @(@("namespace $ns ", '0', 'NotRegistered\n'), @('provider show', '0', 'Registered\n'))
                $r = Invoke-Recipe 'PRQ-S-03' $script:Common $script:Dir
                $r.Exit | Should Be 1
                $r.Output | Should Match ([regex]::Escape("NOT_REGISTERED: $ns"))
            }
        }
        It 'does not ask about Microsoft.ContainerRegistry (no customer stamp deploys it) or Microsoft.ManagedServices (Model 2 Lighthouse)' {
            Set-Rules $script:Dir az @(@('namespace Microsoft.ContainerRegistry ', '0', 'NotRegistered\n'), @('namespace Microsoft.ManagedServices ', '0', 'NotRegistered\n'), @('provider show', '0', 'Registered\n'))
            (Invoke-Recipe 'PRQ-S-03' $script:Common $script:Dir).Exit | Should Be 0
            (Get-Content (Join-Path $script:Dir 'calls.log') -Raw) | Should Not Match 'ContainerRegistry|ManagedServices'
        }
    }

    Context 'PRQ-S-04 / PRQ-S-05 subscription RBAC' {
        It 'S-04 exits 1 when the L2 UAMI has no Owner assignment' {
            Set-Rules $script:Dir az @(, @('role assignment list', '0', ''))
            (Invoke-Recipe 'PRQ-S-04' $script:Common $script:Dir).Exit | Should Be 1
        }
        It 'S-04 exits 0 when it does' {
            Set-Rules $script:Dir az @(, @('role assignment list', '0', '/subscriptions/x/providers/Microsoft.Authorization/roleAssignments/abc\r\n'))
            (Invoke-Recipe 'PRQ-S-04' $script:Common $script:Dir).Exit | Should Be 0
        }
        It 'S-05 exits 1 for Contributor alone, 0 for Contributor + User Access Administrator, 0 for Owner' {
            Set-Rules $script:Dir az @(@('signed-in-user', '0', 'me\n'), @('role assignment list', '0', 'Contributor\n'))
            (Invoke-Recipe 'PRQ-S-05' $script:Common $script:Dir).Exit | Should Be 1
            Set-Rules $script:Dir az @(@('signed-in-user', '0', 'me\n'), @('role assignment list', '0', 'Contributor\r\nUser Access Administrator\r\n'))
            (Invoke-Recipe 'PRQ-S-05' $script:Common $script:Dir).Exit | Should Be 0
            Set-Rules $script:Dir az @(@('signed-in-user', '0', 'me\n'), @('role assignment list', '0', 'Owner\n'))
            (Invoke-Recipe 'PRQ-S-05' $script:Common $script:Dir).Exit | Should Be 0
        }
    }

    Context 'PRQ-S-06 customer subscription in the spaarke-customers management group (T262, G36)' {
        It 'exits 0 when the subscription''s parent is spaarke-customers (any case), naming the stamp subscription' {
            Set-Rules $script:Dir az @(, @('management-group subscription show', '0', '/providers/Microsoft.Management/managementGroups/Spaarke-Customers\r\n'))
            (Invoke-Recipe 'PRQ-S-06' $script:Common $script:Dir).Exit | Should Be 0
            (Get-Content (Join-Path $script:Dir 'calls.log') -Raw) | Should Match '--name spaarke-customers --subscription 44444444-4444-4444-4444-444444444444'
        }
        It 'exits 1 when the parent is another group (e.g. spaarke-environments)' {
            Set-Rules $script:Dir az @(, @('management-group subscription show', '0', '/providers/Microsoft.Management/managementGroups/spaarke-environments\n'))
            $r = Invoke-Recipe 'PRQ-S-06' $script:Common $script:Dir
            $r.Exit | Should Be 1
            $r.Output | Should Match 'spaarke-environments'
        }
        It 'exits 1 when az cannot find the subscription under the group (NotFound, exit 3)' {
            Set-Rules $script:Dir az @(, @('management-group subscription show', '3', ''))
            $r = Invoke-Recipe 'PRQ-S-06' $script:Common $script:Dir
            $r.Exit | Should Be 1
            $r.Output | Should Match 'not a member'
        }
        It 'exits 1 on empty output with exit 0' {
            Set-Rules $script:Dir az @(, @('management-group subscription show', '0', ''))
            (Invoke-Recipe 'PRQ-S-06' $script:Common $script:Dir).Exit | Should Be 1
        }
        It 'the token resolves to spaarke-constants.yaml management_groups.customers.id' {
            $mg = (Get-Content -Raw (Join-Path $repoRoot 'scripts/provisioning-prereqs/spaarke-constants.yaml') | ConvertFrom-Yaml).management_groups
            $script:Common.customerManagementGroupId | Should Be $mg.customers.id
        }
    }

    Context 'PRQ-E-07 Graph app roles (T207: the old recipe lost its OData query to bash expansion of $filter)' {
        BeforeEach {
            $script:RoleIds = @(Select-String -Path $script:GraphRoles -Pattern '^\s*private const string Id[A-Za-z]+ = "([0-9a-f-]{36})"' | ForEach-Object { $_.Matches[0].Groups[1].Value })
        }
        It 'parses the L2 Worker role catalog from ControlPlaneGraphAppRoles.cs (task 261)' { $script:RoleIds.Count | Should BeGreaterThan 5 }
        It 'exits 0 when every catalog role is granted to the L2 UAMI on Graph' {
            Set-Rules $script:Dir az @(@('ad sp show', '0', 'graph-sp\n'), @('appRoleAssignments', '0', (($script:RoleIds -join '\r\n') + '\r\n')))
            (Invoke-Recipe 'PRQ-E-07' $script:Common $script:Dir).Exit | Should Be 0
        }
        It 'exits 1 and prints the missing role when one grant is absent' {
            $missing = $script:RoleIds[3]
            Set-Rules $script:Dir az @(@('ad sp show', '0', 'graph-sp\n'), @('appRoleAssignments', '0', ((($script:RoleIds | Where-Object { $_ -ne $missing }) -join '\n') + '\n')))
            $r = Invoke-Recipe 'PRQ-E-07' $script:Common $script:Dir
            $r.Exit | Should Be 1
            $r.Output | Should Match $missing
        }
        It 'queries the UAMI service principal, with no $filter that bash could expand' {
            Set-Rules $script:Dir az @(@('ad sp show', '0', 'graph-sp\n'), @('appRoleAssignments', '0', ''))
            Invoke-Recipe 'PRQ-E-07' $script:Common $script:Dir | Out-Null
            $calls = Get-Content (Join-Path $script:Dir 'calls.log') -Raw
            $calls | Should Match 'servicePrincipals/33333333-3333-3333-3333-333333333333/appRoleAssignments'
        }
    }

    Context 'PRQ-E-09 platform vault secrets' {
        It 'exits 1 when one of the three seeded secrets is missing' {
            Set-Rules $script:Dir az @(, @('secret list', '0', 'Dataverse-ClientSecret\nBFF-API-ClientSecret\n'))
            $r = Invoke-Recipe 'PRQ-E-09' $script:Common $script:Dir
            $r.Exit | Should Be 1
            $r.Output | Should Match 'Sidecar-Shared-Secret'
        }
        It 'exits 0 when all three are present (case-insensitive)' {
            Set-Rules $script:Dir az @(, @('secret list', '0', 'dataverse-clientsecret\r\nBFF-API-ClientSecret\r\nSidecar-Shared-Secret\r\n'))
            (Invoke-Recipe 'PRQ-E-09' $script:Common $script:Dir).Exit | Should Be 0
        }
    }

    Context 'PRQ-E-11 Service Bus roles need Sender AND Receiver' {
        BeforeEach { $script:Ns = '/subscriptions/s/resourceGroups/rg1/providers/Microsoft.ServiceBus/namespaces/spaarke-servicebus-dev' }
        It 'exits 1 when only Data Sender is assigned' {
            Set-Rules $script:Dir az @(@('resource list', '0', "$script:Ns\n"), @('role assignment list', '0', 'Azure Service Bus Data Sender\n'))
            $r = Invoke-Recipe 'PRQ-E-11' $script:Common $script:Dir
            $r.Exit | Should Be 1
            $r.Output | Should Match 'Data Receiver'
        }
        It 'exits 0 when both are assigned' {
            Set-Rules $script:Dir az @(@('resource list', '0', "$script:Ns\n"), @('role assignment list', '0', 'Azure Service Bus Data Sender\r\nAzure Service Bus Data Receiver\r\n'))
            (Invoke-Recipe 'PRQ-E-11' $script:Common $script:Dir).Exit | Should Be 0
        }
    }

    Context 'PRQ-E-12 provisioning queue settings (T108 recreate ceremony)' {
        BeforeEach { $script:Ns = '/subscriptions/s/resourceGroups/rg1/providers/Microsoft.ServiceBus/namespaces/spaarke-servicebus-dev' }
        It 'exits 1 when sessions are off' {
            Set-Rules $script:Dir az @(@('resource list', '0', "$script:Ns\n"), @('requiresSession', '0', 'False\n'), @('requiresDuplicateDetection', '0', 'True\n'))
            (Invoke-Recipe 'PRQ-E-12' $script:Common $script:Dir).Exit | Should Be 1
        }
        It 'exits 1 when duplicate detection is off' {
            Set-Rules $script:Dir az @(@('resource list', '0', "$script:Ns\n"), @('requiresSession', '0', 'True\n'), @('requiresDuplicateDetection', '0', 'False\n'))
            (Invoke-Recipe 'PRQ-E-12' $script:Common $script:Dir).Exit | Should Be 1
        }
        It 'exits 0 when both are on' {
            Set-Rules $script:Dir az @(@('resource list', '0', "$script:Ns\n"), @('requiresSession', '0', 'True\r\n'), @('requiresDuplicateDetection', '0', 'True\r\n'))
            (Invoke-Recipe 'PRQ-E-12' $script:Common $script:Dir).Exit | Should Be 0
        }
    }

    Context 'PRQ-C-01 OpenAI quota headroom (a fresh subscription reports limit 0)' {
        It 'exits 1 when a pinned quota has limit 0' {
            Set-Rules $script:Dir az @(@("gpt-4o'", '0', '0.0\t0.0\n'), @("gpt4.1-mini'", '0', '2000.0\t0.0\n'), @("embedding-3-large'", '0', '1000.0\t0.0\n'))
            $r = Invoke-Recipe 'PRQ-C-01' $script:Common $script:Dir
            $r.Exit | Should Be 1
            $r.Output | Should Match 'QUOTA OpenAI.DataZoneStandard.gpt-4o'
        }
        It 'exits 1 when the quota row is absent' {
            Set-Rules $script:Dir az @(@("gpt-4o'", '0', '300.0\t0.0\n'), @("gpt4.1-mini'", '0', ''), @("embedding-3-large'", '0', '1000.0\t0.0\n'))
            (Invoke-Recipe 'PRQ-C-01' $script:Common $script:Dir).Exit | Should Be 1
        }
        It 'exits 1 when the limit is there but already used' {
            Set-Rules $script:Dir az @(@("gpt-4o'", '0', '300.0\t200.0\n'), @("gpt4.1-mini'", '0', '2000.0\t0.0\n'), @("embedding-3-large'", '0', '1000.0\t0.0\n'))
            (Invoke-Recipe 'PRQ-C-01' $script:Common $script:Dir).Exit | Should Be 1
        }
        It 'exits 0 with the fresh-subscription DataZoneStandard grants (gpt-4o 300, gpt4.1-mini 2000, embedding 1000)' {
            Set-Rules $script:Dir az @(@("gpt-4o'", '0', '300.0\t0.0\r\n'), @("gpt4.1-mini'", '0', '2000.0\t0.0\r\n'), @("embedding-3-large'", '0', '1000.0\t0.0\r\n'))
            (Invoke-Recipe 'PRQ-C-01' $script:Common $script:Dir).Exit | Should Be 0
        }
    }

    Context 'PRQ-C-02 pinned model lifecycle (the SESSION 12 silent-PASS on Deprecated)' {
        It 'exits 1 when a pin is Deprecating, naming the pin' {
            Set-Rules $script:Dir az @(@("model.name=='gpt-4o' ", '0', 'GenerallyAvailable\n'), @("gpt-4.1-mini", '0', 'Deprecating\n'), @("text-embedding-3-large", '0', 'GenerallyAvailable\n'))
            $r = Invoke-Recipe 'PRQ-C-02' $script:Common $script:Dir
            $r.Exit | Should Be 1
            $r.Output | Should Match 'gpt-4.1-mini 2025-04-14'
        }
        It 'exits 1 when a pin is not offered in the region' {
            Set-Rules $script:Dir az @(@("model.name=='gpt-4o' ", '0', 'Legacy\n'), @("gpt-4.1-mini", '0', 'Legacy\n'), @("text-embedding-3-large", '0', ''))
            (Invoke-Recipe 'PRQ-C-02' $script:Common $script:Dir).Exit | Should Be 1
        }
        It 'exits 0 when every pin is GenerallyAvailable or Legacy' {
            Set-Rules $script:Dir az @(@("model.name=='gpt-4o' ", '0', 'Legacy\r\nLegacy\r\n'), @("gpt-4.1-mini", '0', 'Legacy\n'), @("text-embedding-3-large", '0', 'GenerallyAvailable\n'))
            (Invoke-Recipe 'PRQ-C-02' $script:Common $script:Dir).Exit | Should Be 0
        }
    }

    # PRQ-C-06 (maxuploadfilesize >= 25 MB) retired by T186 (2026-10-14): H6 applies the org-settings contract itself before the
    # import (H6SolutionImportHandler step 7.6; the 'retired entries' Context below proves the entry carries no recipe).
    # PRQ-C-07 (required applications) retired by T253 (2026-10-08): H6 installs no application;
    # SpaarkeMasterApplicationDependencyTests guards the package instead.

    Context 'PRQ-C-08 Exchange scope group, read through Microsoft Graph (T186: Connect-ExchangeOnline needs an interactive window)' {
        BeforeEach {
            $script:Grp = {
                param([bool]$MailEnabled = $true, [bool]$SecurityEnabled = $true, [string[]]$Types = @())
                $o = [ordered]@{ id = '50327bb8-2598-4092-9f90-ba5731df2e42'; displayName = 'Spaarke-AppAccess-acme'; mailEnabled = $MailEnabled; securityEnabled = $SecurityEnabled; groupTypes = @($Types) }
                ($o | ConvertTo-Json -Compress -Depth 4) -replace ',', ',\r\n  '
            }
            $script:WithGroup = { param([string]$Id) $t = $script:Common.Clone(); $t['exchangePolicyScopeGroupId'] = $Id; $t }
            $script:AsList = { param([string[]]$Groups) '{\r\n  "value": [' + ($Groups -join ',\r\n') + ']\r\n}\r\n' }
        }
        It 'exits 0 for the email address when exactly one mail-enabled security group matches (looked up with $filter=mail eq)' {
            Set-Rules $script:Dir az @(, @('groups?', '0', (& $script:AsList @(& $script:Grp))))
            (Invoke-Recipe 'PRQ-C-08' $script:Common $script:Dir).Exit | Should Be 0
            (Get-Content (Join-Path $script:Dir 'calls.log') -Raw) | Should Match 'groups\?\$filter=mail%20eq%20''scope@acme.example'''
        }
        It 'exits 0 for the object id (GET groups/{id})' {
            Set-Rules $script:Dir az @(, @('groups/50327bb8', '0', ((& $script:Grp) + '\r\n')))
            (Invoke-Recipe 'PRQ-C-08' (& $script:WithGroup '50327bb8-2598-4092-9f90-ba5731df2e42') $script:Dir).Exit | Should Be 0
            (Get-Content (Join-Path $script:Dir 'calls.log') -Raw) | Should Match 'v1.0/groups/50327bb8-2598-4092-9f90-ba5731df2e42\?'
        }
        It 'never starts an Exchange Online session' {
            Set-Rules $script:Dir az @(, @('groups?', '0', (& $script:AsList @(& $script:Grp))))
            Invoke-Recipe 'PRQ-C-08' $script:Common $script:Dir | Out-Null
            (Get-Content (Join-Path $script:Dir 'calls.log') -Raw) | Should Not Match 'pwsh|Connect-ExchangeOnline'
        }
        It 'exits 1 when no group has the address' {
            Set-Rules $script:Dir az @(, @('groups?', '0', '{\r\n  "value": []\r\n}\r\n'))
            $r = Invoke-Recipe 'PRQ-C-08' $script:Common $script:Dir
            $r.Exit | Should Be 1
            $r.Output | Should Match 'not found'
        }
        It 'exits 1 when the object id does not exist (Graph 404)' {
            Set-Rules $script:Dir az @(, @('groups/', '1', ''))
            $r = Invoke-Recipe 'PRQ-C-08' (& $script:WithGroup '50327bb8-2598-4092-9f90-ba5731df2e42') $script:Dir
            $r.Exit | Should Be 1
            $r.Output | Should Match 'not found'
        }
        It 'exits 1 when more than one group has the address' {
            Set-Rules $script:Dir az @(, @('groups?', '0', (& $script:AsList @((& $script:Grp), (& $script:Grp)))))
            $r = Invoke-Recipe 'PRQ-C-08' $script:Common $script:Dir
            $r.Exit | Should Be 1
            $r.Output | Should Match 'more than one'
        }
        It 'exits 1 for a Microsoft 365 (Unified) group - Exchange RBAC scopes only to a mail-enabled security group' {
            Set-Rules $script:Dir az @(, @('groups?', '0', (& $script:AsList @(& $script:Grp -Types @('Unified')))))
            $r = Invoke-Recipe 'PRQ-C-08' $script:Common $script:Dir
            $r.Exit | Should Be 1
            $r.Output | Should Match 'Unified'
        }
        It 'exits 1 for a group that is not mail-enabled' {
            Set-Rules $script:Dir az @(, @('groups?', '0', (& $script:AsList @(& $script:Grp -MailEnabled $false))))
            $r = Invoke-Recipe 'PRQ-C-08' $script:Common $script:Dir
            $r.Exit | Should Be 1
            $r.Output | Should Match 'not mail-enabled'
        }
        It 'exits 1 for a group that is not security-enabled (a distribution list)' {
            Set-Rules $script:Dir az @(, @('groups?', '0', (& $script:AsList @(& $script:Grp -SecurityEnabled $false))))
            $r = Invoke-Recipe 'PRQ-C-08' $script:Common $script:Dir
            $r.Exit | Should Be 1
            $r.Output | Should Match 'not security-enabled'
        }
    }

    Context 'PRQ-C-10 environment security group (T186: a list projection prints two LINES on az 2.77)' {
        It 'exits 0 for the one tab-separated row of the dict projection, and uses that projection' {
            Set-Rules $script:Dir az @(, @('groups/55555555', '0', 'sprk-acme-users\ttrue\r\n'))
            (Invoke-Recipe 'PRQ-C-10' $script:Common $script:Dir).Exit | Should Be 0
            (Get-Content (Join-Path $script:Dir 'calls.log') -Raw) | Should Match '--query \{n:displayName,s:securityEnabled\}'
        }
        It 'exits 0 when az prints the two values on two lines (the shape that made the old recipe refuse the right group)' {
            Set-Rules $script:Dir az @(, @('groups/55555555', '0', 'sprk-acme-users\r\nTrue\r\n'))
            (Invoke-Recipe 'PRQ-C-10' $script:Common $script:Dir).Exit | Should Be 0
        }
        It 'exits 1 for another customer''s group, naming it' {
            Set-Rules $script:Dir az @(, @('groups/55555555', '0', 'sprk-other-users\ttrue\n'))
            $r = Invoke-Recipe 'PRQ-C-10' $script:Common $script:Dir
            $r.Exit | Should Be 1
            $r.Output | Should Match 'sprk-other-users'
        }
        It 'exits 1 for the right name when the group is not security-enabled, in either output shape' {
            Set-Rules $script:Dir az @(, @('groups/55555555', '0', 'sprk-acme-users\tfalse\n'))
            (Invoke-Recipe 'PRQ-C-10' $script:Common $script:Dir).Exit | Should Be 1
            Set-Rules $script:Dir az @(, @('groups/55555555', '0', 'sprk-acme-users\nfalse\n'))
            $r = Invoke-Recipe 'PRQ-C-10' $script:Common $script:Dir
            $r.Exit | Should Be 1
            $r.Output | Should Match 'not security-enabled'
        }
        It 'exits 1 when the group cannot be read' {
            Set-Rules $script:Dir az @(, @('groups/55555555', '1', ''))
            (Invoke-Recipe 'PRQ-C-10' $script:Common $script:Dir).Exit | Should Be 1
        }
    }

    Context 'PRQ-C-09 operator-created environment' {
        It 'exits 1 when the L2 identity is not an application user (no parenthesis in the az query)' {
            Set-Rules $script:Dir az @(, @('systemusers', '0', ''))
            (Invoke-Recipe 'PRQ-C-09' $script:Common $script:Dir).Exit | Should Be 1
        }
        It 'exits 0 with exactly one enabled application user' {
            Set-Rules $script:Dir az @(, @('systemusers', '0', 'aaaaaaaa-0000-0000-0000-000000000000\r\n'))
            (Invoke-Recipe 'PRQ-C-09' $script:Common $script:Dir).Exit | Should Be 0
        }
    }

    Context 'PRQ-C-11 pay-as-you-go billing (T186 L12: Power Platform API 2024-10-01, not preview pac licensing)' {
        It 'exits 0 when an Enabled policy on the stamp subscription has the environment linked' {
            Set-Rules $script:Dir az @(@('api.bap.microsoft.com', '0', 'env-1\r\n'), @('billingPolicies?api-version', '0', 'pol-1\r\n'), @('billingPolicies/pol-1/environments/env-1', '0', ''))
            (Invoke-Recipe 'PRQ-C-11' $script:Common $script:Dir).Exit | Should Be 0
            (Get-Content (Join-Path $script:Dir 'calls.log') -Raw) | Should Match "billingInstrument.subscriptionId=='44444444-4444-4444-4444-444444444444'"
        }
        It 'exits 1 when no Enabled policy is on the stamp subscription' {
            Set-Rules $script:Dir az @(@('api.bap.microsoft.com', '0', 'env-1\n'), @('billingPolicies?api-version', '0', ''))
            $r = Invoke-Recipe 'PRQ-C-11' $script:Common $script:Dir
            $r.Exit | Should Be 1
            $r.Output | Should Match 'no Enabled pay-as-you-go billing policy'
        }
        It 'exits 1 when the policy does not have the environment linked (404)' {
            Set-Rules $script:Dir az @(@('api.bap.microsoft.com', '0', 'env-1\n'), @('billingPolicies?api-version', '0', 'pol-1\n'), @('billingPolicies/pol-1/environments/env-1', '3', ''))
            $r = Invoke-Recipe 'PRQ-C-11' $script:Common $script:Dir
            $r.Exit | Should Be 1
            $r.Output | Should Match 'linked to no Enabled billing policy'
        }
        It 'exits 1 when no environment has the URL' {
            Set-Rules $script:Dir az @(, @('api.bap.microsoft.com', '0', ''))
            $r = Invoke-Recipe 'PRQ-C-11' $script:Common $script:Dir
            $r.Exit | Should Be 1
            $r.Output | Should Match 'no Power Platform environment has the URL'
        }
    }

    Context 'PRQ-C-12 guest access' {
        It 'exits 1 while guest access is restricted, 0 once it is off' {
            Set-Rules $script:Dir az @(, @('restrictGuestUserAccess', '0', 'True\r\n'))
            (Invoke-Recipe 'PRQ-C-12' $script:Common $script:Dir).Exit | Should Be 1
            Set-Rules $script:Dir az @(, @('restrictGuestUserAccess', '0', 'False\r\n'))
            (Invoke-Recipe 'PRQ-C-12' $script:Common $script:Dir).Exit | Should Be 0
        }
    }

    Context 'PRQ-C-14 customer attestation (outbound B2B collaboration; Spaarke cannot read the customer policy)' {
        It 'exits 1 when the attestation is false or absent' {
            foreach ($v in 'false', '') {
                $r = Invoke-Recipe 'PRQ-C-14' ($script:Common + @{ customerOutboundB2BAttested = $v }) $script:Dir
                $r.Exit | Should Be 1
                $r.Output | Should Match 'PRQ-C-14'
            }
        }
        It 'exits 0 when attested true, and 0 (SKIP) for a run that is not B2BGuest' {
            (Invoke-Recipe 'PRQ-C-14' ($script:Common + @{ customerOutboundB2BAttested = 'true' }) $script:Dir).Exit | Should Be 0
            $r = Invoke-Recipe 'PRQ-C-14' ($script:Common + @{ customerOutboundB2BAttested = 'notApplicable' }) $script:Dir
            $r.Exit | Should Be 0
            $r.Output | Should Match 'SKIP'
        }
    }

    # PRQ-T-04 (Word add-in app) retired by T186 (2026-10-14): one shared client app serves Outlook and Word - PRQ-T-03.
    Context 'PRQ-T-03 shared client apps H3 pre-authorizes (T186: the per-host "Outlook/Word Add-in {env}" apps never existed)' {
        BeforeEach {
            $script:OfficeId = '1958aec2-0218-495e-8e3c-37133e9b8357'
            $script:OtherId = '77777777-7777-7777-7777-777777777777'
            $script:S0 = 'EntraAppRegOptions__PreAuthorizedClientAppIds__0'
            $script:S1 = 'EntraAppRegOptions__PreAuthorizedClientAppIds__1'
            $script:T03 = {
                param([string]$Names, [string]$Id0, [string]$Id1, [string]$Dn0, [string]$Dn1)
                Set-Rules $script:Dir az @(
                    @("ClientAppIds__0'", '0', "$Id0\r\n"), @("ClientAppIds__1'", '0', "$Id1\r\n"),
                    @("appId eq '$script:OfficeId'", '0', "$Dn0\r\n"), @("appId eq '$script:OtherId'", '0', "$Dn1\r\n"),
                    @('appsettings list', '0', $Names))
            }
            $script:Both = "$($script:S0)\r\n$($script:S1)\r\nOther__Name\r\n"
        }
        It 'exits 0 when every pre-authorized app resolves and one is Spaarke Office Add-in (Production)' {
            & $script:T03 $script:Both $script:OfficeId $script:OtherId 'Spaarke Office Add-in (Production)' 'Spaarke BFF Client'
            $r = Invoke-Recipe 'PRQ-T-03' $script:Common $script:Dir
            $r.Exit | Should Be 0
            $r.Output | Should Match 'ok'
            $calls = Get-Content (Join-Path $script:Dir 'calls.log') -Raw
            $calls | Should Match 'spaarke-provisioning-controlplane-worker-dev --resource-group rg-spaarke-platform-dev'
            $calls | Should Not Match "Other__Name'"
        }
        It 'exits 0 with the single Office add-in setting' {
            & $script:T03 "$($script:S0)\r\n" $script:OfficeId '' 'Spaarke Office Add-in (Production)' ''
            (Invoke-Recipe 'PRQ-T-03' $script:Common $script:Dir).Exit | Should Be 0
        }
        It 'exits 1 when the Worker has no PreAuthorizedClientAppIds setting' {
            & $script:T03 'Other__Name\r\n' '' '' '' ''
            $r = Invoke-Recipe 'PRQ-T-03' $script:Common $script:Dir
            $r.Exit | Should Be 1
            $r.Output | Should Match 'no EntraAppRegOptions__PreAuthorizedClientAppIds__N setting'
        }
        It 'exits 1 when the Worker app cannot be read' {
            Set-Rules $script:Dir az @(, @('appsettings list', '1', ''))
            (Invoke-Recipe 'PRQ-T-03' $script:Common $script:Dir).Exit | Should Be 1
        }
        It 'exits 1 and names an app id that has no service principal in the tenant' {
            & $script:T03 $script:Both $script:OfficeId $script:OtherId 'Spaarke Office Add-in (Production)' ''
            $r = Invoke-Recipe 'PRQ-T-03' $script:Common $script:Dir
            $r.Exit | Should Be 1
            $r.Output | Should Match ([regex]::Escape($script:OtherId))
        }
        It 'exits 1 when a setting holds no value' {
            & $script:T03 $script:Both $script:OfficeId '' 'Spaarke Office Add-in (Production)' ''
            $r = Invoke-Recipe 'PRQ-T-03' $script:Common $script:Dir
            $r.Exit | Should Be 1
            $r.Output | Should Match 'is empty'
        }
        It 'exits 1 when none of the pre-authorized apps is the production Office add-in' {
            & $script:T03 $script:Both $script:OfficeId $script:OtherId 'Spaarke Word Add-in dev' 'Spaarke BFF Client'
            $r = Invoke-Recipe 'PRQ-T-03' $script:Common $script:Dir
            $r.Exit | Should Be 1
            $r.Output | Should Match 'none of the pre-authorized'
        }
    }

    Context 'PRQ-T-02 container-type application permissions (T186: the beta containerTypes/{id}/applicationPermissionGrants path never existed)' {
        BeforeEach { Set-Rules $script:Dir az @(, @('get-access-token', '0', 'tok\n')) }
        It 'asks the v1.0 containerTypeRegistrations path, never the beta one' {
            Set-Rules $script:Dir curl @(, @('applicationPermissionGrants', '0', 'HTTP/2 404\r\n\r\n', '{}'))
            Invoke-Recipe 'PRQ-T-02' $script:Common $script:Dir | Out-Null
            $calls = Get-Content (Join-Path $script:Dir 'calls.log') -Raw
            $calls | Should Match 'https://graph.microsoft.com/v1.0/storage/fileStorage/containerTypeRegistrations/fb3817a8-5a55-42ba-8cc9-12cf055168b8/applicationPermissionGrants'
            $calls | Should Not Match '/beta/'
        }
        It 'exits 0 when a grant carries readContent + writeContent' {
            Set-Rules $script:Dir curl @(, @('applicationPermissionGrants', '0', 'HTTP/2 200\r\n\r\n', '{"value":[{"appId":"a","delegatedPermissions":["none"],"applicationPermissions":["readContent","writeContent","create"]}]}'))
            $r = Invoke-Recipe 'PRQ-T-02' $script:Common $script:Dir
            $r.Exit | Should Be 0
            $r.Output | Should Match 'ok'
        }
        It 'exits 0 for a pretty-printed body, and for a grant that carries full' {
            Set-Rules $script:Dir curl @(, @('applicationPermissionGrants', '0', 'HTTP/2 200\r\n\r\n', '{\r\n  "value": [\r\n    {\r\n      "applicationPermissions": [\r\n        "readContent",\r\n        "writeContent"\r\n      ]\r\n    }\r\n  ]\r\n}'))
            (Invoke-Recipe 'PRQ-T-02' $script:Common $script:Dir).Exit | Should Be 0
            Set-Rules $script:Dir curl @(, @('applicationPermissionGrants', '0', 'HTTP/2 200\r\n\r\n', '{"value":[{"applicationPermissions":["full"]}]}'))
            (Invoke-Recipe 'PRQ-T-02' $script:Common $script:Dir).Exit | Should Be 0
        }
        It 'exits 1 when the grants hold only readContent, or none' {
            Set-Rules $script:Dir curl @(, @('applicationPermissionGrants', '0', 'HTTP/2 200\r\n\r\n', '{"value":[{"applicationPermissions":["readContent"]}]}'))
            (Invoke-Recipe 'PRQ-T-02' $script:Common $script:Dir).Exit | Should Be 1
            Set-Rules $script:Dir curl @(, @('applicationPermissionGrants', '0', 'HTTP/2 200\r\n\r\n', '{"value":[]}'))
            (Invoke-Recipe 'PRQ-T-02' $script:Common $script:Dir).Exit | Should Be 1
        }
        It 'exits 1 when readContent and writeContent are split across two grants (one grant must hold both)' {
            Set-Rules $script:Dir curl @(, @('applicationPermissionGrants', '0', 'HTTP/2 200\r\n\r\n', '{"value":[{"applicationPermissions":["readContent"]},{"applicationPermissions":["writeContent"]}]}'))
            (Invoke-Recipe 'PRQ-T-02' $script:Common $script:Dir).Exit | Should Be 1
        }
        It 'exits 1 on 404 saying the container type is not registered in this tenant' {
            Set-Rules $script:Dir curl @(, @('applicationPermissionGrants', '0', 'HTTP/2 404\r\n\r\n', '{}'))
            $r = Invoke-Recipe 'PRQ-T-02' $script:Common $script:Dir
            $r.Exit | Should Be 1
            $r.Output | Should Match 'container type fb3817a8-5a55-42ba-8cc9-12cf055168b8 is not registered in this tenant'
        }
        It 'exits 1 on any other status' {
            Set-Rules $script:Dir curl @(, @('applicationPermissionGrants', '0', 'HTTP/2 500\r\n\r\n', '{}'))
            (Invoke-Recipe 'PRQ-T-02' $script:Common $script:Dir).Exit | Should Be 1
        }
        It 'exits 0 with SKIP on the documented 403 (the operator''s Azure CLI token is not consented)' {
            Set-Rules $script:Dir curl @(, @('applicationPermissionGrants', '0', 'HTTP/2 403\r\n\r\n', '{}'))
            $r = Invoke-Recipe 'PRQ-T-02' $script:Common $script:Dir
            $r.Exit | Should Be 0
            $r.Output | Should Match 'SKIP'
        }
    }

    Context 'PRQ-T-01 container type (HTTP status read from curl headers; no undefined bash variable)' {
        BeforeEach { Set-Rules $script:Dir az @(, @('get-access-token', '0', 'tok\n')) }
        It 'exits 1 on 404' {
            Set-Rules $script:Dir curl @(, @('containerTypes', '0', 'HTTP/2 404\r\n\r\n', '{}'))
            (Invoke-Recipe 'PRQ-T-01' $script:Common $script:Dir).Exit | Should Be 1
        }
        It 'exits 1 on 200 with a different id' {
            Set-Rules $script:Dir curl @(, @('containerTypes', '0', 'HTTP/2 200\r\n\r\n', '{"id": "00000000-0000-0000-0000-000000000000"}'))
            (Invoke-Recipe 'PRQ-T-01' $script:Common $script:Dir).Exit | Should Be 1
        }
        It 'exits 0 on 200 with the id, and 0 (SKIP) on the documented 403' {
            Set-Rules $script:Dir curl @(, @('containerTypes', '0', 'HTTP/2 200\r\n\r\n', '{"id": "fb3817a8-5a55-42ba-8cc9-12cf055168b8"}'))
            (Invoke-Recipe 'PRQ-T-01' $script:Common $script:Dir).Exit | Should Be 0
            Set-Rules $script:Dir curl @(, @('containerTypes', '0', 'HTTP/2 403\r\n\r\n', '{}'))
            (Invoke-Recipe 'PRQ-T-01' $script:Common $script:Dir).Output | Should Match 'SKIP'
        }
        It 'sends a real bearer token, never an empty one' {
            Set-Rules $script:Dir curl @(, @('containerTypes', '0', 'HTTP/2 404\r\n\r\n', '{}'))
            Invoke-Recipe 'PRQ-T-01' $script:Common $script:Dir | Out-Null
            (Get-Content (Join-Path $script:Dir 'calls.log') -Raw) | Should Match 'Authorization: Bearer tok'
        }
    }
}

Describe 'prereqs.yaml retired entries (T186) carry no recipe, so nothing runs them' {
    foreach ($id in 'PRQ-T-04', 'PRQ-E-05', 'PRQ-E-13', 'PRQ-C-06') {
        It "$id is retired, named RETIRED, dated, attributed and has no check_recipe" {
            $p = $script:Prereqs[$id]
            $p | Should Not BeNullOrEmpty
            $p.status | Should Be 'retired'
            $p.name.StartsWith('RETIRED') | Should Be $true
            $p.retired_on | Should Not BeNullOrEmpty
            $p.retired_by | Should Match 'T186'
            $p.ContainsKey('check_recipe') | Should Be $false
            [string]$p.consequence_of_absence | Should Not BeNullOrEmpty
            [string]$p.remediation | Should Not BeNullOrEmpty
        }
    }
    It 'the recipes that stay active are still active' {
        foreach ($id in 'PRQ-T-02', 'PRQ-T-03', 'PRQ-S-00', 'PRQ-S-01', 'PRQ-S-03', 'PRQ-C-08', 'PRQ-C-10') {
            $script:Prereqs[$id].ContainsKey('status') | Should Be $false
            [string]$script:Prereqs[$id].check_recipe.cli | Should Not BeNullOrEmpty
        }
    }
}

Describe 'prereqs.yaml mirrors the code that owns its constants' {
    It 'PRQ-S-00 counts the same resource-group pattern SubscriptionDedication.StampGroup (H1) counts' {
        $cs = [regex]::Match((Get-Content -Raw $script:Dedication), '@"(?<p>\^rg-spaarke-[^"]+)"').Groups['p'].Value
        $cs | Should Not BeNullOrEmpty
        $plain = $cs -replace '\(\?<\w+>', '' -replace '\)', ''   # drop the named groups: ^rg-spaarke-[a-z][a-z0-9]{2,7}-[a-z0-9]+$
        $cli = [string]$script:Prereqs['PRQ-S-00'].check_recipe.cli
        [regex]::Match($cli, "grep -iE '(?<p>\^rg-spaarke-[^']+)'").Groups['p'].Value | Should Be $plain
        (Get-Content -Raw $script:Dedication) | Should Match 'RegexOptions.IgnoreCase'
    }
    It 'PRQ-S-03 lists the namespaces customer.bicep and the modules it composes deploy' {
        $bicep = Get-Content -Raw $script:CustomerBicep
        $files = @($script:CustomerBicep) + @([regex]::Matches($bicep, "(?m)^module \w+ 'modules/(?<f>[\w-]+\.bicep)'") | ForEach-Object { Join-Path (Split-Path $script:CustomerBicep) "modules/$($_.Groups['f'].Value)" })
        $files.Count | Should BeGreaterThan 10
        $deployed = $files | ForEach-Object { [regex]::Matches((Get-Content -Raw $_), "'(Microsoft\.[A-Za-z]+)/") | ForEach-Object { $_.Groups[1].Value } } |
            Where-Object { $_ -notin 'Microsoft.Authorization', 'Microsoft.Resources' } | Sort-Object -Unique
        $cli = [string]$script:Prereqs['PRQ-S-03'].check_recipe.cli
        $recipe = ([regex]::Match($cli, '(?s)for ns in (?<l>.*?); do').Groups['l'].Value -replace '\\\r?\n', ' ') -split '\s+' | Where-Object { $_ } | Sort-Object -Unique
        ($recipe -join ',') | Should Be ($deployed -join ',')
    }
    It 'PRQ-S-03 is H1''s default RequiredResourceProviders minus Microsoft.ManagedServices (Model 2 Lighthouse)' {
        $h1 = [regex]::Matches((Get-Content -Raw $script:ReadinessOptions), '(?m)^\s*"(Microsoft\.[A-Za-z]+)",') | ForEach-Object { $_.Groups[1].Value }
        $h1.Count | Should BeGreaterThan 10
        $cli = [string]$script:Prereqs['PRQ-S-03'].check_recipe.cli
        $recipe = ([regex]::Match($cli, '(?s)for ns in (?<l>.*?); do').Groups['l'].Value -replace '\\\r?\n', ' ') -split '\s+' | Where-Object { $_ } | Sort-Object -Unique
        (($h1 | Where-Object { $_ -ne 'Microsoft.ManagedServices' } | Sort-Object -Unique) -join ',') | Should Be ($recipe -join ',')
    }

    $dep = [regex]::Matches((Get-Content -Raw $script:Catalog), 'new PinnedDeployment\("(?<n>[^"]+)",\s*"(?<m>[^"]+)",\s*"(?<v>[^"]+)",\s*"(?<s>[^"]+)",\s*(?<c>\d+),\s*"(?<q>[^"]+)"')
    It 'parses the PinnedModelCatalog deployments' { $dep.Count | Should BeGreaterThan 0 }
    It 'PRQ-C-01 requests exactly the quota name:TPM pairs PinnedModelCatalog.RequestedTpmByQuotaName computes' {
        $sum = @{}
        foreach ($d in $dep) { $q = $d.Groups['q'].Value; $sum[$q] = [int]$sum[$q] + [int]$d.Groups['c'].Value }
        $cli = [string]$script:Prereqs['PRQ-C-01'].check_recipe.cli
        $pins = [regex]::Matches($cli, '"([^":]+):(\d+)"') | ForEach-Object { "$($_.Groups[1].Value)=$($_.Groups[2].Value)" } | Sort-Object
        $want = $sum.Keys | ForEach-Object { "$_=$($sum[$_])" } | Sort-Object
        ($pins -join ',') | Should Be ($want -join ',')
    }
    It 'PRQ-C-02 checks exactly the model:version pairs PinnedModelCatalog.Models holds' {
        $want = $dep | ForEach-Object { "$($_.Groups['m'].Value):$($_.Groups['v'].Value)" } | Sort-Object -Unique
        $cli = [string]$script:Prereqs['PRQ-C-02'].check_recipe.cli
        $loop = [regex]::Match($cli, 'for pin in ([^;]+); do').Groups[1].Value.Trim() -split '\s+' | Sort-Object -Unique
        ($loop -join ',') | Should Be ($want -join ',')
    }
}

Describe 'validate.ps1 (the recipe lints)' {
    It 'passes on the committed manifest' {
        & pwsh -NoProfile -File $script:Validate *> $null
        $LASTEXITCODE | Should Be 0
    }
    It 'fails a manifest whose tenant-scope recipe uses an env-only token, an undefined bash variable, a PowerShell cmdlet or no exit 1' {
        $bad = Join-Path ([IO.Path]::GetTempPath()) ("bad-" + [guid]::NewGuid().ToString('N') + '.yaml')
        # A synthetic tenant-scope entry (no active once_per_tenant recipe is left since T-01..T-05 moved to
        # once_per_env and T-06 retired, 2026-10-09), inserted before PRQ-T-05 so it sits inside the prereqs list.
        $synthetic = @'
  - id: PRQ-X-99
    name: synthetic lint fixture
    scope: once_per_tenant
    frequency: once
    owner: test
    consequence_of_absence: none
    check_recipe:
      cli: |
        az ad sp list --display-name "{env}" | Select-String "$undefinedVar"
        echo x | grep -iF y
      expect: n/a
    remediation: none

'@ -replace "`r`n", "`n"
        $text = (Get-Content -Raw $script:ManifestPath) -replace "`r`n", "`n"
        $text = $text.Replace("  - id: PRQ-T-05`n", $synthetic + "  - id: PRQ-T-05`n")
        [IO.File]::WriteAllText($bad, $text)
        try {
            $out = & pwsh -NoProfile -File $script:Validate -ManifestPath $bad 2>&1 | Out-String
            $LASTEXITCODE | Should Be 1
            $out | Should Match 'PRQ-X-99: recipe uses \{env\}'
            $out | Should Match 'PRQ-X-99: recipe references \$undefinedVar'
            $out | Should Match 'PRQ-X-99: recipe has no explicit .exit 1.'
            $out | Should Match "PRQ-X-99: recipe runs under bash -c but uses the PowerShell cmdlet 'Select-String'"
            # T262: lint e was inert (its code sat on one comment line joined by literal \n) until 2026-10-09.
            $out | Should Match "PRQ-X-99: recipe uses 'grep' with both -i and -F"
        }
        finally { Remove-Item $bad -ErrorAction SilentlyContinue }
    }
}
