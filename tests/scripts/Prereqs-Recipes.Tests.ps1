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
$script:GraphRoles = Join-Path $repoRoot 'src/server/api/Sprk.Bff.Api/Infrastructure/Auth/GraphAppRoles.cs'

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

    Context 'PRQ-S-03 provider registration (T206: the loop used to exit 0 on a partial registration)' {
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
        It 'parses a non-empty role catalog from GraphAppRoles.cs' { $script:RoleIds.Count | Should BeGreaterThan 10 }
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

    Context 'PRQ-C-06 maxuploadfilesize (the 5 MB default broke the SpaarkeMaster import, F14)' {
        It 'exits 1 on the 5 MB default' {
            Set-Rules $script:Dir az @(, @('maxuploadfilesize', '0', '5242880\n'))
            (Invoke-Recipe 'PRQ-C-06' $script:Common $script:Dir).Exit | Should Be 1
        }
        It 'exits 1 when the value cannot be read' {
            Set-Rules $script:Dir az @(, @('maxuploadfilesize', '1', ''))
            (Invoke-Recipe 'PRQ-C-06' $script:Common $script:Dir).Exit | Should Be 1
        }
        It 'exits 0 at 26214400 and at exactly 25600000' {
            Set-Rules $script:Dir az @(, @('maxuploadfilesize', '0', '26214400\r\n'))
            (Invoke-Recipe 'PRQ-C-06' $script:Common $script:Dir).Exit | Should Be 0
            Set-Rules $script:Dir az @(, @('maxuploadfilesize', '0', '25600000\n'))
            (Invoke-Recipe 'PRQ-C-06' $script:Common $script:Dir).Exit | Should Be 0
        }
    }

    # PRQ-C-07 (required applications) retired by T253 (2026-10-08): H6 installs no application;
    # SpaarkeMasterApplicationDependencyTests guards the package instead.

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

    Context 'PRQ-C-12 guest access' {
        It 'exits 1 while guest access is restricted, 0 once it is off' {
            Set-Rules $script:Dir az @(, @('restrictguestuseraccess', '0', 'True\r\n'))
            (Invoke-Recipe 'PRQ-C-12' $script:Common $script:Dir).Exit | Should Be 1
            Set-Rules $script:Dir az @(, @('restrictguestuseraccess', '0', 'False\r\n'))
            (Invoke-Recipe 'PRQ-C-12' $script:Common $script:Dir).Exit | Should Be 0
        }
    }

    Context 'PRQ-T-03 Entra app registration (az returns empty output and exit 0 on a miss)' {
        It 'exits 1 on empty output and 0 on a GUID' {
            Set-Rules $script:Dir az @(, @('ad app list', '0', ''))
            (Invoke-Recipe 'PRQ-T-03' $script:Common $script:Dir).Exit | Should Be 1
            Set-Rules $script:Dir az @(, @('ad app list', '0', '66666666-6666-6666-6666-666666666666\r\n'))
            (Invoke-Recipe 'PRQ-T-03' $script:Common $script:Dir).Exit | Should Be 0
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

Describe 'prereqs.yaml mirrors the code that owns its constants' {
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
