# tests/scripts/Set-FeatureRolePrivileges.Tests.ps1
# ---------------------------------------------------------------------------
# task 046b: scripts/Set-FeatureRolePrivileges.ps1 runs here WHOLE (exit codes included) in a child pwsh against
# Set-FeatureRolePrivileges.Harness.ps1, a test double of `az`, Invoke-RestMethod and Start-Sleep. No environment, no token, no network.
# Proven:
#   1. the baseline: a dry run lists the change and writes nothing; -Apply writes and reads back every business-unit copy;
#   2. a role that is not in the root business unit stops the run before any write (exit 1, nothing changed);
#   3. a declared depth the privilege cannot be held at stops the run before any write, naming the privilege;
#   4. nothing pending but a copy that differs: -Apply and -Verify both exit 1 with the same report; a dry run still exits 0;
#      nothing pending and no drift: -Apply exits 0;
#   5. a failed read-back (a read that fails, or a copy that never matches) prints the applied / not-applied summary and exits 1;
#      a failed AddPrivilegesRole on the second role still prints it;
#   6. the role-name filter is URL-encoded after its quotes are doubled;
#   7. with an empty AZURE_CONFIG_DIR and no stubs the dry run fails closed (exit 1, "Nothing was changed").
#
# Pester 3.4:  Import-Module Pester -RequiredVersion 3.4.0 ; Invoke-Pester tests/scripts/Set-FeatureRolePrivileges.Tests.ps1
# ---------------------------------------------------------------------------

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$script:ScriptPath = Join-Path $repoRoot 'scripts/Set-FeatureRolePrivileges.ps1'
$script:HarnessPath = Join-Path $PSScriptRoot 'Set-FeatureRolePrivileges.Harness.ps1'
$script:Work = Join-Path ([System.IO.Path]::GetTempPath()) ('frp-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $script:Work | Out-Null
$script:N = 0

function New-Scenario([hashtable]$Override = @{}) {
    $s = @{
        orgName = 'fake-org'; rootBu = 'bu-root'
        roles   = @(
            @{ id = 'ra'; name = 'Role A'; bu = 'bu-root'; root = 'ra' }, @{ id = 'ra1'; name = 'Role A'; bu = 'bu-c1'; root = 'ra' },
            @{ id = 'rb'; name = 'Role B'; bu = 'bu-root'; root = 'rb' }, @{ id = 'rb1'; name = 'Role B'; bu = 'bu-c1'; root = 'rb' })
        held    = @{ ra = @{}; ra1 = @{}; rb = @{}; rb1 = @{} }
        privs   = @{ sprk_a = @(@{ Name = 'prvCreatesprk_a'; PrivilegeId = 'pa' }); sprk_b = @(@{ Name = 'prvCreatesprk_b'; PrivilegeId = 'pb' }) }
        can     = @{}
        noPropagate = @()
    }
    foreach ($k in $Override.Keys) { $s[$k] = $Override[$k] }
    $s
}

# Runs the script whole. $Roles lets a test rename the roles (the config and role XML follow the scenario's role names).
function Invoke-Frp([hashtable]$Scenario, [string[]]$ScriptArgs = @(), [switch]$RealAz, [string]$RoleA = 'Role A') {
    $script:N++
    $dir = Join-Path $script:Work "run$($script:N)"
    $rolesDir = Join-Path $dir 'Roles'
    New-Item -ItemType Directory -Path $rolesDir -Force | Out-Null
    $changes = @(
        @{ gap = 'G1'; role = $RoleA; table = 'sprk_a'; privilege = 'prvCreatesprk_a'; depth = 'Deep'; from = 'None'; feature = 'f' },
        @{ gap = 'G2'; role = 'Role B'; table = 'sprk_b'; privilege = 'prvCreatesprk_b'; depth = 'Deep'; from = 'None'; feature = 'f' })
    $cfg = Join-Path $dir 'config.json'
    (@{ schemaVersion = 1; changes = $changes } | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $cfg -Encoding UTF8
    foreach ($c in $changes) {
        Set-Content -LiteralPath (Join-Path $rolesDir "$($c.role).xml") -Encoding UTF8 `
            -Value "<Role name=`"$([Security.SecurityElement]::Escape($c.role))`"><RolePrivileges><RolePrivilege name=`"$($c.privilege)`" level=`"Deep`" /></RolePrivileges></Role>"
    }
    $scn = Join-Path $dir 'scenario.json'
    ($Scenario | ConvertTo-Json -Depth 8) | Set-Content -LiteralPath $scn -Encoding UTF8
    $log = Join-Path $dir 'requests.jsonl'
    $env:FRP_SCENARIO = $scn; $env:FRP_LOG = $log
    $cmd = "try { & '$($script:ScriptPath)' -EnvironmentUrl https://example.invalid -ConfigPath '$cfg' -RolesXmlDir '$rolesDir' $($ScriptArgs -join ' '); `$c = `$LASTEXITCODE } catch { Write-Host (`"THROWN: `" + `$_.Exception.Message); `$c = 1 }; exit `$c"
    if (-not $RealAz) { $cmd = ". '$($script:HarnessPath)'; $cmd" }
    $out = (& pwsh -NoProfile -NonInteractive -Command $cmd 2>&1 | Out-String)
    $requests = if (Test-Path -LiteralPath $log) { @(Get-Content -LiteralPath $log | ForEach-Object { $_ | ConvertFrom-Json }) } else { @() }
    [pscustomobject]@{ Exit = $LASTEXITCODE; Out = $out; Requests = $requests; Posts = @($requests | Where-Object { $_.Method -eq 'Post' }) }
}

Describe 'Set-FeatureRolePrivileges.ps1 (stubbed Dataverse)' {

    Context 'baseline' {
        It 'a dry run lists both changes, exits 0 and writes nothing' {
            $r = Invoke-Frp (New-Scenario)
            $r.Exit | Should Be 0
            $r.Out | Should Match 'DRY RUN: no writes'
            $r.Out | Should Match 'prvCreatesprk_a\s+None\s+-> Deep'
            $r.Posts.Count | Should Be 0
        }
        It '-Apply writes each root role once and reads back the copies, exit 0' {
            $r = Invoke-Frp (New-Scenario) @('-Apply')
            $r.Exit | Should Be 0
            $r.Posts.Count | Should Be 2
            $r.Out | Should Match 'APPLIED and read back on every business-unit copy: 2 change'
        }
    }

    Context 'a role outside the root business unit (item 3)' {
        It 'stops before any write, exit 1, nothing changed' {
            $s = New-Scenario
            $s.roles[0].bu = 'bu-child'   # Role A's root role sits in a child business unit
            $r = Invoke-Frp $s @('-Apply')
            $r.Exit | Should Be 1
            $r.Out | Should Match "Root role 'Role A' is not in the root business unit"
            $r.Out | Should Match 'Nothing was changed'
            $r.Posts.Count | Should Be 0
        }
        It 'a root role in the root business unit is not stopped (control)' {
            (Invoke-Frp (New-Scenario)).Out | Should Not Match 'not in the root business unit'
        }
    }

    Context 'a depth the privilege cannot be held at (item 4)' {
        It 'stops before any write and names the privilege' {
            $r = Invoke-Frp (New-Scenario @{ can = @{ pb = @{ canbedeep = $false } } }) @('-Apply')
            $r.Exit | Should Be 1
            $r.Out | Should Match "prvCreatesprk_b' \(Role B\) cannot be held at depth 'Deep'"
            $r.Out | Should Match 'Nothing was changed'
            $r.Posts.Count | Should Be 0
        }
        It 'a privilege allowed at Deep is not stopped (control)' {
            (Invoke-Frp (New-Scenario @{ can = @{ pb = @{ canbebasic = $false } } })).Exit | Should Be 0
        }
        It 'reads the allowed depths of every declared privilege before any write' {
            $r = Invoke-Frp (New-Scenario) @('-Apply')
            $first = [array]::FindIndex($r.Requests, [Predicate[object]] { param($x) $x.Method -eq 'Post' })
            $caps = @($r.Requests | Where-Object { $_.Uri -match 'privileges\(pa\)|privileges\(pb\)' })
            $caps.Count | Should Be 2
            @($r.Requests[0..($first - 1)] | Where-Object { $_.Uri -match 'canbebasic,canbelocal,canbedeep,canbeglobal' }).Count | Should Be 2
        }
    }

    Context '-Apply with nothing pending but drift (item 5)' {
        # Both roles hold the declared depth; a business-unit copy of Role A holds a different one.
        $drift = { New-Scenario @{ held = @{ ra = @{ pa = 'Deep' }; ra1 = @{ pa = 'Basic' }; rb = @{ pb = 'Deep' }; rb1 = @{ pb = 'Deep' } } } }
        It '-Apply exits 1 and prints the report' {
            $r = Invoke-Frp (& $drift) @('-Apply')
            $r.Exit | Should Be 1
            $r.Out | Should Match 'copy ra1 holds Basic'
            $r.Out | Should Match 'APPLY: nothing to add or raise'
            $r.Posts.Count | Should Be 0
        }
        It '-Verify exits 1 on the same report' {
            $r = Invoke-Frp (& $drift) @('-Verify')
            $r.Exit | Should Be 1
            $r.Out | Should Match 'copy ra1 holds Basic'
            $r.Out | Should Match 'VERIFY FAIL'
        }
        It 'a dry run is not an apply: it still exits 0' {
            (Invoke-Frp (& $drift)).Exit | Should Be 0
        }
        It 'nothing pending and no drift: -Apply exits 0 (control)' {
            $r = Invoke-Frp (New-Scenario @{ held = @{ ra = @{ pa = 'Deep' }; ra1 = @{ pa = 'Deep' }; rb = @{ pb = 'Deep' }; rb1 = @{ pb = 'Deep' } } }) @('-Apply')
            $r.Exit | Should Be 0
            $r.Out | Should Match 'Nothing to change'
        }
    }

    Context 'a failed read-back prints the summary (item 6)' {
        It 'a read that fails after the writes: applied and not-applied lists, exit 1' {
            $r = Invoke-Frp (New-Scenario @{ failReadOf = 'ra1' }) @('-Apply')
            $r.Exit | Should Be 1
            $r.Out | Should Match 'FAIL: read-back on'
            $r.Out | Should Match 'Applied in this run before the failure \(2\)'
            $r.Out | Should Match 'Role A prvCreatesprk_a None -> Deep'
            $r.Out | Should Match 'Not applied \(0\)'
            $r.Out | Should Match 'Every write was sent'
        }
        It 'a copy that never matches: the summary is printed too, naming the copy' {
            $r = Invoke-Frp (New-Scenario @{ noPropagate = @('rb1') }) @('-Apply')
            $r.Exit | Should Be 1
            $r.Out | Should Match 'FAIL: read-back on'
            $r.Out | Should Match 'Role B \(copy rb1\) prvCreatesprk_b: expected Deep, read back None'
            $r.Out | Should Match 'Applied in this run before the failure \(2\)'
        }
        It 'a failed write on the second role still prints it (control)' {
            $r = Invoke-Frp (New-Scenario @{ failPostOn = 'rb' }) @('-Apply')
            $r.Exit | Should Be 1
            $r.Out | Should Match 'FAIL: AddPrivilegesRole on'
            $r.Out | Should Match 'Applied in this run before the failure \(1\)'
            $r.Out | Should Match 'Not applied \(1\)'
        }
    }

    Context 'the role-name filter is URL-encoded (item 7)' {
        It 'doubles the quotes, then encodes: no raw quote, ampersand, hash or plus survives in the filter' {
            $name = "O'Neil & Co #1+%"
            $s = New-Scenario
            foreach ($row in $s.roles) { if ($row.name -eq 'Role A') { $row.name = $name } }
            $r = Invoke-Frp $s @() -RoleA $name
            $r.Exit | Should Be 0
            $filter = @($r.Requests | Where-Object { $_.Uri -match '\$filter=name eq' -and $_.Uri -match 'Neil' })[0].Uri
            $filter | Should Match ([regex]::Escape("name eq '" + [Uri]::EscapeDataString("O''Neil & Co #1+%") + "'"))
            ([regex]::Match($filter, "name eq '(.*)'$")).Groups[1].Value | Should Not Match "[&# +']"
        }
    }

    Context 'fail closed with no sign-in (the real az, an empty AZURE_CONFIG_DIR)' {
        It 'a dry run exits 1 and changes nothing' {
            $empty = Join-Path $script:Work 'empty-azure-config'
            New-Item -ItemType Directory -Path $empty -Force | Out-Null
            $old = $env:AZURE_CONFIG_DIR
            try { $env:AZURE_CONFIG_DIR = $empty; $r = Invoke-Frp (New-Scenario) -RealAz } finally { $env:AZURE_CONFIG_DIR = $old }
            $r.Exit | Should Be 1
            $r.Out | Should Match 'FAIL: a read failed'
            $r.Out | Should Match 'Nothing was changed'
        }
    }
}

Remove-Item -LiteralPath $script:Work -Recurse -Force -ErrorAction SilentlyContinue
