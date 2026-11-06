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
#   7. -Remove (the config's removals block, kept apart from the adds): dry run, -Remove -Apply, -Remove -Verify and plain -Verify; the
#      add path ignores the block; a role outside the root business unit, a depth other than expected, a role file that still holds
#      the privilege, a privilege both added and removed and a copy that keeps the privilege all stop or fail; the read-back covers every
#      business-unit copy, a failed read-back or write prints the summary; nothing outside the block is touched; another privilege the
#      platform changed or added is named with its depth before and after;
#   8. with an empty AZURE_CONFIG_DIR and no stubs the dry run fails closed (exit 1, "Nothing was changed");
#   9. (fix round 3) a successful -Apply or -Remove -Apply that leaves a live depth above the declared one, a copy that differs from its
#      root role or a copy that still holds a removed privilege prints the same report -Verify prints and exits 1;
#  10. (task 046c) -Restore, the one-command rollback of the removals: the -Remove dry run and apply print it and say whether it is exact;
#      dry run, -Restore -Apply (AddPrivilegesRole at expectedDepth, read back on every copy, nothing else changed on the root role)
#      and -Restore -Verify; a depth other than expected, a depth the privilege cannot be held at, -Remove with -Restore, a copy that
#      never receives it and a failed write all stop or fail.
#  11. (046c fix round 2) -Remove refuses while any add is pending, a business-unit copy that differs on a changes role included;
#      -Verify -ChangesOnly passes with the adds in place and the removals pending; the rollback line after the apply repeats the
#      NOT exact note; a run that refuses prints no rollback line.
#
# Pester 5 (CI pins 5.7.1):  Import-Module Pester -RequiredVersion 5.7.1 ; Invoke-Pester tests/scripts/Set-FeatureRolePrivileges.Tests.ps1
# ---------------------------------------------------------------------------

BeforeAll {
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
        # Role C is only in the removals block: it holds pc (the privilege to remove) and pz (which must stay), on the root role and its copy.
        $s.roles += @(@{ id = 'rc'; name = 'Role C'; bu = 'bu-root'; root = 'rc' }, @{ id = 'rc1'; name = 'Role C'; bu = 'bu-c1'; root = 'rc' })
        $s.held['rc'] = @{ pc = 'Deep'; pz = 'Basic' }
        $s.held['rc1'] = @{ pc = 'Deep'; pz = 'Basic' }
        $s.privs['sprk_c'] = @(@{ Name = 'prvAppendTosprk_c'; PrivilegeId = 'pc' })
        # Role D is the second removal (-SecondRemoval): its root role no longer holds pd, one of its business-unit copies still does.
        $s.roles += @(@{ id = 'rd'; name = 'Role D'; bu = 'bu-root'; root = 'rd' }, @{ id = 'rd1'; name = 'Role D'; bu = 'bu-c1'; root = 'rd' })
        $s.held['rd'] = @{}
        $s.held['rd1'] = @{ pd = 'Deep' }
        $s.privs['sprk_d'] = @(@{ Name = 'prvAppendTosprk_d'; PrivilegeId = 'pd' })
        foreach ($k in $Override.Keys) { $s[$k] = $Override[$k] }
        $s
    }

    # Runs the script whole. $RoleA lets a test rename the roles (the config and role XML follow the scenario's role names).
    function Invoke-Frp([hashtable]$Scenario, [string[]]$ScriptArgs = @(), [switch]$RealAz, [string]$RoleA = 'Role A', [switch]$WithRemoval, [switch]$XmlHoldsRemoved, [switch]$AlsoAdded, [switch]$SecondRemoval, [string]$SecondGap = 'R2', [switch]$AddsPending) {
        $script:N++
        # -Remove refuses while a declared change is pending, so a -Remove run starts with the adds held (Role A pa, Role B pb, root and
        # copy) unless the test is about that refusal (-AddsPending leaves the scenario's own state).
        if (($ScriptArgs -contains '-Remove') -and -not $AddsPending) {
            $Scenario.held['ra'] = @{ pa = 'Deep' }; $Scenario.held['ra1'] = @{ pa = 'Deep' }
            $Scenario.held['rb'] = @{ pb = 'Deep' }; $Scenario.held['rb1'] = @{ pb = 'Deep' }
        }
        $dir = Join-Path $script:Work "run$($script:N)"
        $rolesDir = Join-Path $dir 'Roles'
        New-Item -ItemType Directory -Path $rolesDir -Force | Out-Null
        $changes = @(
            @{ gap = 'G1'; role = $RoleA; table = 'sprk_a'; privilege = 'prvCreatesprk_a'; depth = 'Deep'; from = 'None'; feature = 'f' },
            @{ gap = 'G2'; role = 'Role B'; table = 'sprk_b'; privilege = 'prvCreatesprk_b'; depth = 'Deep'; from = 'None'; feature = 'f' })
        $cfg = Join-Path $dir 'config.json'
        $doc = @{ schemaVersion = 1; changes = $changes }
        if ($WithRemoval) {
            $doc.removals = @(@{ gap = 'R1'; role = 'Role C'; table = 'sprk_c'; privilege = 'prvAppendTosprk_c'; expectedDepth = 'Deep'; feature = 'f' })
            if ($SecondRemoval) {
                $doc.removals += @{ gap = $SecondGap; role = 'Role D'; table = 'sprk_d'; privilege = 'prvAppendTosprk_d'; expectedDepth = 'Deep'; feature = 'f' }
                Set-Content -LiteralPath (Join-Path $rolesDir 'Role D.xml') -Encoding UTF8 -Value '<Role name="Role D"><RolePrivileges><RolePrivilege name="prvOtherstuff" level="Basic" /></RolePrivileges></Role>'
            }
            # A privilege both added and removed: the add is satisfied by the role XML (Role C holds it at Global there), so only the
            # script's own check for the pair can stop the run.
            if ($AlsoAdded) { $doc.changes += @{ gap = 'G3'; role = 'Role C'; table = 'sprk_c'; privilege = 'prvAppendTosprk_c'; depth = 'Global'; from = 'Deep'; feature = 'f' } }
            $xmlC = if ($AlsoAdded) { '<RolePrivilege name="prvAppendTosprk_c" level="Global" />' }
            elseif ($XmlHoldsRemoved) { '<RolePrivilege name="prvAppendTosprk_c" level="Deep" />' }
            else { '<RolePrivilege name="prvOtherstuff" level="Basic" />' }
            Set-Content -LiteralPath (Join-Path $rolesDir 'Role C.xml') -Encoding UTF8 -Value "<Role name=`"Role C`"><RolePrivileges>$xmlC</RolePrivileges></Role>"
        }
        ($doc | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $cfg -Encoding UTF8
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
        $exit = $LASTEXITCODE
        $requests = if (Test-Path -LiteralPath $log) { @(Get-Content -LiteralPath $log | ForEach-Object { $_ | ConvertFrom-Json }) } else { @() }
        [pscustomobject]@{ Exit = $exit; Out = $out; Requests = $requests; Posts = @($requests | Where-Object { $_.Method -eq 'Post' }) }
    }
}

AfterAll {
    Remove-Item -LiteralPath $script:Work -Recurse -Force -ErrorAction SilentlyContinue
}

Describe 'Set-FeatureRolePrivileges.ps1 (stubbed Dataverse)' {

    Context 'baseline' {
        It 'a dry run lists both changes, exits 0 and writes nothing' {
            $r = Invoke-Frp (New-Scenario)
            $r.Exit | Should -Be 0
            $r.Out | Should -Match 'DRY RUN: no writes'
            $r.Out | Should -Match 'prvCreatesprk_a\s+None\s+-> Deep'
            $r.Posts.Count | Should -Be 0
        }
        It '-Apply writes each root role once and reads back the copies, exit 0' {
            $r = Invoke-Frp (New-Scenario) @('-Apply')
            $r.Exit | Should -Be 0
            $r.Posts.Count | Should -Be 2
            $r.Out | Should -Match 'APPLIED and read back on every business-unit copy: 2 change'
        }
    }

    Context 'a role outside the root business unit (item 3)' {
        It 'stops before any write, exit 1, nothing changed' {
            $s = New-Scenario
            $s.roles[0].bu = 'bu-child'   # Role A's root role sits in a child business unit
            $r = Invoke-Frp $s @('-Apply')
            $r.Exit | Should -Be 1
            $r.Out | Should -Match "Root role 'Role A' is not in the root business unit"
            $r.Out | Should -Match 'Nothing was changed'
            $r.Posts.Count | Should -Be 0
        }
        It 'a root role in the root business unit is not stopped (control)' {
            (Invoke-Frp (New-Scenario)).Out | Should -Not -Match 'not in the root business unit'
        }
    }

    Context 'a depth the privilege cannot be held at (item 4)' {
        It 'stops before any write and names the privilege' {
            $r = Invoke-Frp (New-Scenario @{ can = @{ pb = @{ canbedeep = $false } } }) @('-Apply')
            $r.Exit | Should -Be 1
            $r.Out | Should -Match "prvCreatesprk_b' \(Role B\) cannot be held at depth 'Deep'"
            $r.Out | Should -Match 'Nothing was changed'
            $r.Posts.Count | Should -Be 0
        }
        It 'a privilege allowed at Deep is not stopped (control)' {
            (Invoke-Frp (New-Scenario @{ can = @{ pb = @{ canbebasic = $false } } })).Exit | Should -Be 0
        }
        It 'reads the allowed depths of every declared privilege before any write' {
            $r = Invoke-Frp (New-Scenario) @('-Apply')
            $first = [array]::FindIndex($r.Requests, [Predicate[object]] { param($x) $x.Method -eq 'Post' })
            $caps = @($r.Requests | Where-Object { $_.Uri -match 'privileges\(pa\)|privileges\(pb\)' })
            $caps.Count | Should -Be 2
            @($r.Requests[0..($first - 1)] | Where-Object { $_.Uri -match 'canbebasic,canbelocal,canbedeep,canbeglobal' }).Count | Should -Be 2
        }
    }

    Context '-Apply with nothing pending but drift (item 5)' {
        BeforeAll {
            # Both roles hold the declared depth; a business-unit copy of Role A holds a different one.
            $script:drift = { New-Scenario @{ held = @{ ra = @{ pa = 'Deep' }; ra1 = @{ pa = 'Basic' }; rb = @{ pb = 'Deep' }; rb1 = @{ pb = 'Deep' } } } }
        }
        It '-Apply exits 1 and prints the report' {
            $r = Invoke-Frp (& $script:drift) @('-Apply')
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'copy ra1 holds Basic'
            $r.Out | Should -Match 'APPLY: nothing to add or raise'
            $r.Posts.Count | Should -Be 0
        }
        It '-Verify exits 1 on the same report' {
            $r = Invoke-Frp (& $script:drift) @('-Verify')
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'copy ra1 holds Basic'
            $r.Out | Should -Match 'VERIFY FAIL'
        }
        It 'a dry run is not an apply: it still exits 0' {
            (Invoke-Frp (& $script:drift)).Exit | Should -Be 0
        }
        It 'nothing pending and no drift: -Apply exits 0 (control)' {
            $r = Invoke-Frp (New-Scenario @{ held = @{ ra = @{ pa = 'Deep' }; ra1 = @{ pa = 'Deep' }; rb = @{ pb = 'Deep' }; rb1 = @{ pb = 'Deep' } } }) @('-Apply')
            $r.Exit | Should -Be 0
            $r.Out | Should -Match 'Nothing to change'
        }
    }

    Context 'a successful -Apply that leaves the roles off the declared set (fix round 3)' {
        It 'a live depth above the declared one on a role not written: the apply is read back, the report is printed, exit 1' {
            # Role A has a change pending; Role B (and its copy) hold pb at Global, above the declared Deep, which the script never lowers.
            $r = Invoke-Frp (New-Scenario @{ held = @{ ra = @{}; ra1 = @{}; rb = @{ pb = 'Global' }; rb1 = @{ pb = 'Global' } } }) @('-Apply')
            $r.Exit | Should -Be 1
            $r.Posts.Count | Should -Be 1
            $r.Out | Should -Match 'APPLIED and read back on every business-unit copy: 1 change'
            $r.Out | Should -Match 'Role B\s+prvCreatesprk_b\s+live Global\s+declared Deep'
            $r.Out | Should -Match 'APPLY: the changes were applied and read back, but the live roles do not match the declared set: 1 live depth\(s\) above the declared one'
        }
        It 'a business-unit copy that differs from its root role on a role not written: exit 1 with the report' {
            $r = Invoke-Frp (New-Scenario @{ held = @{ ra = @{}; ra1 = @{}; rb = @{ pb = 'Deep' }; rb1 = @{ pb = 'Basic' } } }) @('-Apply')
            $r.Exit | Should -Be 1
            $r.Posts.Count | Should -Be 1
            $r.Out | Should -Match 'copy rb1 holds Basic'
            $r.Out | Should -Match '1 business-unit copy difference\(s\)'
        }
        It 'a copy that drifted before the write and was converged by it is not reported (control)' {
            $r = Invoke-Frp (New-Scenario @{ held = @{ ra = @{}; ra1 = @{ pa = 'Basic' }; rb = @{ pb = 'Deep' }; rb1 = @{ pb = 'Deep' } } }) @('-Apply')
            $r.Exit | Should -Be 0
            $r.Out | Should -Match 'APPLIED and read back'
        }
        It '-Remove -Apply leaves a copy of another listed privilege still holding it: exit 1 with the report' {
            $r = Invoke-Frp (New-Scenario) @('-Remove', '-Apply') -WithRemoval -SecondRemoval
            $r.Exit | Should -Be 1
            $r.Posts.Count | Should -Be 1
            $r.Out | Should -Match 'REMOVED and read back absent on the root role and every business-unit copy: 1 removal'
            $r.Out | Should -Match 'copy rd1 still holds Deep'
            $r.Out | Should -Match '1 business-unit copy still holding a removed privilege'
        }
    }

    Context 'a failed read-back prints the summary (item 6)' {
        It 'a read that fails after the writes: applied and not-applied lists, exit 1' {
            $r = Invoke-Frp (New-Scenario @{ failReadOf = 'ra1' }) @('-Apply')
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'FAIL: read-back on'
            $r.Out | Should -Match 'Applied in this run before the failure \(2\)'
            $r.Out | Should -Match 'Role A prvCreatesprk_a None -> Deep'
            $r.Out | Should -Match 'Not applied \(0\)'
            $r.Out | Should -Match 'Every write was sent'
        }
        It 'the post-write re-read itself throws: exit 1, the writes are reported as confirmed and the re-read as failed' {
            # Role A's root is read once by the read-back (the write is confirmed) and a second time by the final re-read, which fails.
            $r = Invoke-Frp (New-Scenario @{ failReadAfter = @{ ra = 1 } }) @('-Apply')
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'FAIL: re-read on'
            $r.Out | Should -Not -Match 'FAIL: read-back on'
            $r.Out | Should -Match 'Applied in this run before the failure \(2\)'
            $r.Out | Should -Match 'Not applied \(0\)'
            $r.Out | Should -Match 'The writes were confirmed by the read-back; the final re-read of the written roles failed'
            $r.Out | Should -Not -Match 'Every write was sent'
        }
        It 'a copy that never matches: the summary is printed too, naming the copy' {
            $r = Invoke-Frp (New-Scenario @{ noPropagate = @('rb1') }) @('-Apply')
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'FAIL: read-back on'
            $r.Out | Should -Match 'Role B \(copy rb1\) prvCreatesprk_b: expected Deep, read back None'
            $r.Out | Should -Match 'Applied in this run before the failure \(2\)'
        }
        It 'a failed write on the second role still prints it (control)' {
            $r = Invoke-Frp (New-Scenario @{ failPostOn = 'rb' }) @('-Apply')
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'FAIL: AddPrivilegesRole on'
            $r.Out | Should -Match 'Applied in this run before the failure \(1\)'
            $r.Out | Should -Match 'Not applied \(1\)'
        }
    }

    Context 'the role-name filter is URL-encoded (item 7)' {
        It 'doubles the quotes, then encodes: no raw quote, ampersand, hash or plus survives in the filter' {
            $name = "O'Neil & Co #1+%"
            $s = New-Scenario
            foreach ($row in $s.roles) { if ($row.name -eq 'Role A') { $row.name = $name } }
            $r = Invoke-Frp $s @() -RoleA $name
            $r.Exit | Should -Be 0
            $filter = @($r.Requests | Where-Object { $_.Uri -match '\$filter=name eq' -and $_.Uri -match 'Neil' })[0].Uri
            $filter | Should -Match ([regex]::Escape("name eq '" + [Uri]::EscapeDataString("O''Neil & Co #1+%") + "'"))
            ([regex]::Match($filter, "name eq '(.*)'$")).Groups[1].Value | Should -Not -Match "[&# +']"
        }
    }

    Context 'removals (-Remove): a separate block, an explicit mode' {
        BeforeAll {
            $script:held = { param($c, $r) @{ ra = @{}; ra1 = @{}; rb = @{}; rb1 = @{}; rc = $r; rc1 = $c } }
        }
        It 'a dry run lists the removal and writes nothing' {
            $r = Invoke-Frp (New-Scenario) @('-Remove') -WithRemoval
            $r.Exit | Should -Be 0
            $r.Out | Should -Match 'REMOVALS'
            $r.Out | Should -Match 'prvAppendTosprk_c\s+Deep\s+-> removed'
            $r.Out | Should -Match 'DRY RUN: no writes. Re-run with -Remove -Apply'
            $r.Posts.Count | Should -Be 0
        }
        It '-Remove -Apply removes exactly the listed privilege on the root role, reads back every copy, exit 0' {
            $r = Invoke-Frp (New-Scenario) @('-Remove', '-Apply') -WithRemoval
            $r.Exit | Should -Be 0
            $r.Posts.Count | Should -Be 1
            $r.Posts[0].Uri | Should -Match 'roles\(rc\)/Microsoft.Dynamics.CRM.RemovePrivilegeRole$'
            $r.Posts[0].Body | Should -Match '"privilegeid":\s*"pc"'
            $r.Posts[0].Body | Should -Not -Match 'pz'
            $r.Out | Should -Match 'REMOVED and read back absent on the root role and every business-unit copy: 1 removal'
            @($r.Requests | Where-Object { $_.Uri -match 'AddPrivilegesRole' }).Count | Should -Be 0
            # the read-back read the root role and its copy
            @($r.Requests | Where-Object { $_.Uri -match 'RetrieveRolePrivilegesRole\(RoleId=@p\)\?@p=rc1$' }).Count | Should -BeGreaterThan 0
        }
        It 'the add path ignores the block: -Apply without -Remove only adds, and never calls RemovePrivilegeRole' {
            $r = Invoke-Frp (New-Scenario) @('-Apply') -WithRemoval
            $r.Exit | Should -Be 0
            $r.Posts.Count | Should -Be 2
            @($r.Posts | Where-Object { $_.Uri -match 'RemovePrivilegeRole' }).Count | Should -Be 0
            @($r.Requests | Where-Object { $_.Uri -match '=rc' -or $_.Uri -match 'sprk_c' }).Count | Should -Be 0
        }
        It '-Remove never adds: the adds are read (for the refusal below) but never written' {
            $r = Invoke-Frp (New-Scenario) @('-Remove', '-Apply') -WithRemoval
            @($r.Posts | Where-Object { $_.Uri -match 'roles\((ra|ra1|rb|rb1)\)' -or $_.Uri -match 'AddPrivilegesRole' }).Count | Should -Be 0
        }
        It '-Remove -Verify fails while the removal is pending, and passes once it is absent everywhere' {
            $pending = Invoke-Frp (New-Scenario) @('-Remove', '-Verify') -WithRemoval
            $pending.Exit | Should -Be 1
            $pending.Out | Should -Match 'VERIFY FAIL: 1 removal\(s\) pending'
            $gone = Invoke-Frp (New-Scenario @{ held = (& $script:held @{ pz = 'Basic' } @{ pz = 'Basic' }) }) @('-Remove', '-Verify') -WithRemoval
            $gone.Exit | Should -Be 0
            $gone.Out | Should -Match 'VERIFY PASS'
        }
        It 'plain -Verify fails while a removal is pending even though the adds are held, and passes when both are done' {
            $addsHeld = @{ ra = @{ pa = 'Deep' }; ra1 = @{ pa = 'Deep' }; rb = @{ pb = 'Deep' }; rb1 = @{ pb = 'Deep' } }
            $pending = Invoke-Frp (New-Scenario @{ held = ($addsHeld + @{ rc = @{ pc = 'Deep' }; rc1 = @{ pc = 'Deep' } }) }) @('-Verify') -WithRemoval
            $pending.Exit | Should -Be 1
            $pending.Out | Should -Match '1 removal\(s\) pending'
            $done = Invoke-Frp (New-Scenario @{ held = ($addsHeld + @{ rc = @{}; rc1 = @{} }) }) @('-Verify') -WithRemoval
            $done.Exit | Should -Be 0
        }
        It 'nothing pending: -Remove -Apply exits 0' {
            $r = Invoke-Frp (New-Scenario @{ held = (& $script:held @{} @{}) }) @('-Remove', '-Apply') -WithRemoval
            $r.Exit | Should -Be 0
            $r.Out | Should -Match 'Nothing to change'
            $r.Posts.Count | Should -Be 0
        }
        It 'the root role no longer holds it but a copy does: -Remove -Apply exits 1, no write' {
            $r = Invoke-Frp (New-Scenario @{ held = (& $script:held @{ pc = 'Deep' } @{}) }) @('-Remove', '-Apply') -WithRemoval
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'copy rc1 still holds Deep'
            $r.Posts.Count | Should -Be 0
        }
        It 'a role outside the root business unit: stops before any write' {
            $s = New-Scenario
            foreach ($row in $s.roles) { if ($row.id -eq 'rc') { $row.bu = 'bu-child' } }
            $r = Invoke-Frp $s @('-Remove', '-Apply') -WithRemoval
            $r.Exit | Should -Be 1
            $r.Out | Should -Match "Root role 'Role C' is not in the root business unit"
            $r.Posts.Count | Should -Be 0
        }
        It 'held at a depth other than expected: nothing is removed, exit 1' {
            $r = Invoke-Frp (New-Scenario @{ held = (& $script:held @{ pc = 'Global' } @{ pc = 'Global' }) }) @('-Remove', '-Apply') -WithRemoval
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'held at Global, the config expects Deep'
            $r.Out | Should -Match 'Nothing was changed'
            $r.Posts.Count | Should -Be 0
        }
        It 'the role file still holds the privilege: stops before it connects' {
            $r = Invoke-Frp (New-Scenario) @('-Remove', '-Apply') -WithRemoval -XmlHoldsRemoved
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'the role XML still holds it'
            $r.Requests.Count | Should -Be 0
        }
        It 'a privilege both added and removed: stops before it connects, with that message only' {
            # Role C's file holds the privilege at the added depth, so the add check passes and nothing but the script's own
            # added-and-removed check can stop the run.
            $r = Invoke-Frp (New-Scenario) @('-Remove') -WithRemoval -AlsoAdded
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'both added and removed'
            $r.Out | Should -Not -Match 'the role XML still holds it'
            $r.Out | Should -Not -Match 'the role XML holds'
            $r.Requests.Count | Should -Be 0
        }
        It 'a copy that never drops it: the summary is printed and the exit is 1' {
            $r = Invoke-Frp (New-Scenario @{ noPropagate = @('rc1') }) @('-Remove', '-Apply') -WithRemoval
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'FAIL: read-back on'
            $r.Out | Should -Match 'Role C \(copy rc1\) prvAppendTosprk_c: expected None, read back Deep'
            $r.Out | Should -Match 'Applied in this run before the failure \(1\)'
            $r.Out | Should -Match 'Role C prvAppendTosprk_c Deep -> None'
        }
        It 'a read that fails after the removal: the summary is printed too' {
            $r = Invoke-Frp (New-Scenario @{ failReadOf = 'rc1' }) @('-Remove', '-Apply') -WithRemoval
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'FAIL: read-back on'
            $r.Out | Should -Match 'Not applied \(0\)'
            $r.Out | Should -Match 'Every write was sent'
        }
        It 'a failed RemovePrivilegeRole: nothing applied, one not applied, exit 1' {
            $r = Invoke-Frp (New-Scenario @{ failRemoveOn = 'rc' }) @('-Remove', '-Apply') -WithRemoval
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'FAIL: RemovePrivilegeRole on'
            $r.Out | Should -Match 'Applied in this run before the failure \(0\)'
            $r.Out | Should -Match 'Not applied \(1\)'
            $r.Out | Should -Match 'Re-run -Remove -Apply to converge'
        }
        It 'a platform that also drops another privilege (pz) fails the run, naming it with its depth before and after' {
            $r = Invoke-Frp (New-Scenario @{ removeAlsoDrops = @('pz') }) @('-Remove', '-Apply') -WithRemoval
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'pz is not in the removals block but changed or went missing \(before Basic, after None\)'
        }
        It 'a platform that also changes the depth of another privilege (pz) names both depths' {
            $r = Invoke-Frp (New-Scenario @{ removeAlsoSets = @{ pz = 'Deep' } }) @('-Remove', '-Apply') -WithRemoval
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'pz is not in the removals block but changed or went missing \(before Basic, after Deep\)'
        }
        It 'a platform that also adds a privilege (px) fails the run, naming its depth after' {
            $r = Invoke-Frp (New-Scenario @{ removeAlsoSets = @{ px = 'Global' } }) @('-Remove', '-Apply') -WithRemoval
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'px appeared during the removal \(before None, after Global\)'
        }
    }

    Context '-Restore: the one-command rollback of the removals (task 046c)' {
        BeforeAll {
            # After the removal: Role C's root and copy no longer hold pc (expectedDepth Deep); pz stays.
            $script:removed = { param($extra = @{}) New-Scenario (@{ held = @{ ra = @{}; ra1 = @{}; rb = @{}; rb1 = @{}; rc = @{ pz = 'Basic' }; rc1 = @{ pz = 'Basic' } } } + $extra) }
        }
        It 'the -Remove dry run prints the rollback command and says it restores exactly the removals it lists' {
            $r = Invoke-Frp (New-Scenario) @('-Remove') -WithRemoval
            $r.Exit | Should -Be 0
            $r.Out | Should -Match 'Rollback\s+: pwsh scripts/Set-FeatureRolePrivileges.ps1 -EnvironmentUrl https://example.invalid -Restore -Apply'
            $r.Out | Should -Match 'restores exactly the 1 removal\(s\) above'
            $r.Posts.Count | Should -Be 0
        }
        It 'the -Remove dry run names a listed removal that is not pending under the same gap, because -Restore would add it too' {
            $r = Invoke-Frp (New-Scenario) @('-Remove') -WithRemoval -SecondRemoval -SecondGap 'R1'
            $r.Out | Should -Match 'NOT exact: 1 listed removal\(s\) are not pending here'
            $r.Out | Should -Match 'Role D\s+prvAppendTosprk_d'
            $r.Out | Should -Not -Match 'restores exactly'
        }
        It 'a listed removal not pending under another gap: the rollback is limited with -Gap to the planned gaps, and is exact' {
            $r = Invoke-Frp (New-Scenario) @('-Remove') -WithRemoval -SecondRemoval   # R1 pending (Role C); R2 already absent on Role D's root
            $r.Exit | Should -Be 0
            $r.Out | Should -Match 'Rollback\s+: pwsh scripts/Set-FeatureRolePrivileges.ps1 -EnvironmentUrl https://example.invalid -Restore -Gap R1 -Apply'
            $r.Out | Should -Match 'restores exactly the 1 removal\(s\) above, each at the depth listed \(dry run first: -Restore -Gap R1\)'
            $r.Out | Should -Not -Match 'NOT exact'
        }
        It '-Gap without -Remove or -Restore stops before anything is read' {
            $r = Invoke-Frp (New-Scenario) @('-Gap', 'R1') -WithRemoval
            $r.Exit | Should -Be 1
            $r.Out | Should -Match '-Gap limits -Remove or -Restore'
            $r.Requests.Count | Should -Be 0
        }
        It '-Gap naming no listed removal stops before anything is read' {
            $r = Invoke-Frp (New-Scenario) @('-Remove', '-Gap', 'R9') -WithRemoval
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'the removals block lists no entry under R9'
            $r.Requests.Count | Should -Be 0
        }
        It '-Remove -Gap R1 works on R1 only: Role D is not read, and the re-run hint and rollback carry the -Gap' {
            $r = Invoke-Frp (New-Scenario) @('-Remove', '-Gap', 'R1') -WithRemoval -SecondRemoval
            $r.Exit | Should -Be 0
            @($r.Requests | Where-Object { $_.Uri -match '=rd|Role%20D|Role D|sprk_d' }).Count | Should -Be 0
            $r.Out | Should -Match 'Re-run with -Remove -Gap R1 -Apply to make the 1 removal\(s\) above'
            $r.Out | Should -Match 'Rollback\s+: .* -Restore -Gap R1 -Apply'
            $r.Out | Should -Match 'restores exactly the 1 removal'
        }
        It '-Restore -Gap R1 -Apply puts back R1 only, not another gap''s removal that is also absent' {
            $s = & $script:removed
            $s.held['rd'] = @{}; $s.held['rd1'] = @{}   # Role D's R2 removal is absent on its root and copy too
            $all = Invoke-Frp $s @('-Restore', '-Gap', 'R1,R2') -WithRemoval -SecondRemoval
            $all.Out | Should -Match 'DRY RUN: no writes. Re-run with -Restore -Gap R1,R2 -Apply to make the 2 restore\(s\) above'
            $r = Invoke-Frp $s @('-Restore', '-Gap', 'R1', '-Apply') -WithRemoval -SecondRemoval
            $r.Exit | Should -Be 0
            $r.Posts.Count | Should -Be 1
            $r.Posts[0].Uri | Should -Match 'roles\(rc\)/Microsoft.Dynamics.CRM.AddPrivilegesRole$'
            $r.Out | Should -Match 'RESTORED and read back on the root role and every business-unit copy: 1 restore'
        }
        It '-Remove -Apply prints the rollback command after the read-back' {
            $r = Invoke-Frp (New-Scenario) @('-Remove', '-Apply') -WithRemoval
            $r.Exit | Should -Be 0
            $r.Out | Should -Match 'Rollback\s+: .* -Restore -Apply \(puts back the 1 removal\(s\) above\)'
        }
        It 'a dry run lists the restore at the expected depth and writes nothing' {
            $r = Invoke-Frp (& $script:removed) @('-Restore') -WithRemoval
            $r.Exit | Should -Be 0
            $r.Out | Should -Match 'RESTORE \(rollback of the removals\)'
            $r.Out | Should -Match 'prvAppendTosprk_c\s+None\s+-> Deep'
            $r.Out | Should -Match 'DRY RUN: no writes. Re-run with -Restore -Apply to make the 1 restore\(s\) above'
            $r.Posts.Count | Should -Be 0
        }
        It '-Restore -Apply puts back exactly the listed privilege at its expectedDepth on the root role and reads back every copy, exit 0' {
            $r = Invoke-Frp (& $script:removed) @('-Restore', '-Apply') -WithRemoval
            $r.Exit | Should -Be 0
            $r.Posts.Count | Should -Be 1
            $r.Posts[0].Uri | Should -Match 'roles\(rc\)/Microsoft.Dynamics.CRM.AddPrivilegesRole$'
            $r.Posts[0].Body | Should -Match '"PrivilegeId":\s*"pc"'
            $r.Posts[0].Body | Should -Match '"Depth":\s*"Deep"'
            $r.Posts[0].Body | Should -Not -Match 'pz'
            @($r.Posts | Where-Object { $_.Uri -match 'RemovePrivilegeRole' }).Count | Should -Be 0
            @($r.Requests | Where-Object { $_.Uri -match 'RetrieveRolePrivilegesRole\(RoleId=@p\)\?@p=rc1$' }).Count | Should -BeGreaterThan 1
            $r.Out | Should -Match 'RESTORED and read back on the root role and every business-unit copy: 1 restore'
        }
        It '-Restore never touches the adds' {
            $r = Invoke-Frp (& $script:removed) @('-Restore', '-Apply') -WithRemoval
            @($r.Requests | Where-Object { $_.Uri -match 'sprk_a|sprk_b|=ra|=rb' }).Count | Should -Be 0
        }
        It '-Restore -Verify fails while the restore is pending and passes once root and copy hold it at the expected depth' {
            $pending = Invoke-Frp (& $script:removed) @('-Restore', '-Verify') -WithRemoval
            $pending.Exit | Should -Be 1
            $pending.Out | Should -Match 'VERIFY FAIL: 1 restore\(s\) pending'
            $done = Invoke-Frp (New-Scenario) @('-Restore', '-Verify') -WithRemoval   # the starting state: rc and rc1 hold pc at Deep
            $done.Exit | Should -Be 0
            $done.Out | Should -Match 'VERIFY PASS: the live roles hold every listed removal again'
        }
        It '-Restore -Verify fails when a copy differs from the restored root role' {
            $r = Invoke-Frp (New-Scenario @{ held = @{ ra = @{}; ra1 = @{}; rb = @{}; rb1 = @{}; rc = @{ pc = 'Deep' }; rc1 = @{} } }) @('-Restore', '-Verify') -WithRemoval
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'copy rc1 holds None\s+root holds Deep'
        }
        It 'held at another depth than the rollback restores: nothing is changed, exit 1' {
            $r = Invoke-Frp (New-Scenario @{ held = @{ ra = @{}; ra1 = @{}; rb = @{}; rb1 = @{}; rc = @{ pc = 'Global' }; rc1 = @{ pc = 'Global' } } }) @('-Restore', '-Apply') -WithRemoval
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'held at Global, the rollback restores Deep'
            $r.Out | Should -Match 'Nothing was changed'
            $r.Posts.Count | Should -Be 0
        }
        It 'a depth the privilege cannot be held at stops the rollback before any write' {
            $r = Invoke-Frp (& $script:removed @{ can = @{ pc = @{ canbedeep = $false } } }) @('-Restore', '-Apply') -WithRemoval
            $r.Exit | Should -Be 1
            $r.Out | Should -Match "prvAppendTosprk_c' \(Role C\) cannot be held at depth 'Deep'"
            $r.Posts.Count | Should -Be 0
        }
        It '-Remove and -Restore together stop before anything is read' {
            $r = Invoke-Frp (New-Scenario) @('-Remove', '-Restore') -WithRemoval
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'opposite modes'
            $r.Requests.Count | Should -Be 0
        }
        It 'a copy that never receives it: the summary is printed with the rollback re-run hint, exit 1' {
            $r = Invoke-Frp (& $script:removed @{ noPropagate = @('rc1') }) @('-Restore', '-Apply') -WithRemoval
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'FAIL: read-back on'
            $r.Out | Should -Match 'Role C \(copy rc1\) prvAppendTosprk_c: expected Deep, read back None'
            $r.Out | Should -Match 'Applied in this run before the failure \(1\)'
        }
        It 'a failed AddPrivilegesRole: nothing applied, the -Restore re-run hint carries the run''s own -Gap, exit 1' {
            $r = Invoke-Frp (& $script:removed @{ failPostOn = 'rc' }) @('-Restore', '-Gap', 'R1', '-Apply') -WithRemoval
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'FAIL: AddPrivilegesRole on'
            $r.Out | Should -Match 'Re-run -Restore -Gap R1 -Apply to converge'
            $r.Out | Should -Match 'Re-run -Restore -Gap R1 -Verify to see what is still pending'
        }
        It 'a failed AddPrivilegesRole without -Gap names the plain -Restore commands (control)' {
            $r = Invoke-Frp (& $script:removed @{ failPostOn = 'rc' }) @('-Restore', '-Apply') -WithRemoval
            $r.Out | Should -Match 'Re-run -Restore -Apply to converge'
            $r.Out | Should -Match 'Re-run -Restore -Verify to see'
        }
        It 'a platform that also adds another privilege (px) during the rollback fails the run, naming it' {
            $r = Invoke-Frp (& $script:removed @{ addAlsoSets = @{ px = 'Global' } }) @('-Restore', '-Apply') -WithRemoval
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'px appeared during the rollback \(before None, after Global\)'
        }
        It 'a platform that also changes another privilege (pz) during the rollback fails the run, naming both depths' {
            $r = Invoke-Frp (& $script:removed @{ addAlsoSets = @{ pz = 'Deep' } }) @('-Restore', '-Apply') -WithRemoval
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'pz is not in the rollback but changed or went missing \(before Basic, after Deep\)'
        }
        It 'nothing pending: -Restore -Apply exits 0 (control)' {
            $r = Invoke-Frp (New-Scenario) @('-Restore', '-Apply') -WithRemoval
            $r.Exit | Should -Be 0
            $r.Out | Should -Match 'Nothing to change'
            $r.Posts.Count | Should -Be 0
        }
    }

    Context '046c fix round: the run''s own mode flags in every hint' {
        It 'a failed RemovePrivilegeRole under -Gap: both hints carry -Remove -Gap R1' {
            $r = Invoke-Frp (New-Scenario @{ failRemoveOn = 'rc' }) @('-Remove', '-Gap', 'R1', '-Apply') -WithRemoval
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'Re-run -Remove -Gap R1 -Apply to converge'
            $r.Out | Should -Match 'Re-run -Remove -Gap R1 -Verify to see what is still pending'
        }
        It 'a failed read-back under -Gap: the hint carries -Remove -Gap R1' {
            $r = Invoke-Frp (New-Scenario @{ failReadOf = 'rc1' }) @('-Remove', '-Gap', 'R1', '-Apply') -WithRemoval
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'Re-run -Remove -Gap R1 -Verify to see what the live roles hold'
        }
        It 'a failed final re-read under -Restore -Gap: the hint carries -Restore -Gap R1' {
            $s = New-Scenario @{ held = @{ ra = @{}; ra1 = @{}; rb = @{}; rb1 = @{}; rc = @{ pz = 'Basic' }; rc1 = @{ pz = 'Basic' } }; failReadAfter = @{ rc = 1 } }
            $r = Invoke-Frp $s @('-Restore', '-Gap', 'R1', '-Apply') -WithRemoval
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'FAIL: re-read on'
            $r.Out | Should -Match 'Re-run -Restore -Gap R1 -Verify to see what the live roles hold'
        }
        It 'nothing to remove but a copy still holds the privilege: the -Apply message names -Remove -Gap R2 -Verify' {
            $r = Invoke-Frp (New-Scenario) @('-Remove', '-Gap', 'R2', '-Apply') -WithRemoval -SecondRemoval
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'APPLY: nothing to remove'
            $r.Out | Should -Match 're-run -Remove -Gap R2 -Verify'
        }
        It 'a successful -Remove -Apply that leaves a copy holding it: the final message names the run''s own flags' {
            $r = Invoke-Frp (New-Scenario) @('-Remove', '-Gap', 'R1,R2', '-Apply') -WithRemoval -SecondRemoval
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 're-run -Remove -Gap R1,R2 -Verify'
        }
        It 'without -Gap the same message names -Remove -Verify (control)' {
            $r = Invoke-Frp (New-Scenario) @('-Remove', '-Apply') -WithRemoval -SecondRemoval
            $r.Out | Should -Match 're-run -Remove -Verify\.'
        }
    }

    Context '046c fix round: -Remove refuses while a declared change is pending' {
        It 'a dry run exits 1, names the pending changes, says to apply them first, and writes nothing' {
            $r = Invoke-Frp (New-Scenario) @('-Remove') -WithRemoval -AddsPending
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'REFUSED: -Remove will not run while 2 declared change\(s\) are pending'
            $r.Out | Should -Match 'Role A prvCreatesprk_a \(G1\)'
            $r.Out | Should -Match 'Role B prvCreatesprk_b \(G2\)'
            $r.Out | Should -Match 'Apply them first'
            $r.Out | Should -Not -Match 'DRY RUN: no writes'
            $r.Posts.Count | Should -Be 0
        }
        It '-Remove -Apply exits 1 and removes nothing' {
            $r = Invoke-Frp (New-Scenario) @('-Remove', '-Apply') -WithRemoval -AddsPending
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'REFUSED'
            $r.Posts.Count | Should -Be 0
        }
        It '-Remove -Gap R1 -Apply refuses too, and the message carries the flags' {
            $r = Invoke-Frp (New-Scenario) @('-Remove', '-Gap', 'R1', '-Apply') -WithRemoval -AddsPending
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'REFUSED: -Remove -Gap R1 will not run'
            $r.Out | Should -Match 'then re-run -Remove -Gap R1 -Apply'
            $r.Posts.Count | Should -Be 0
        }
        It 'one change pending out of two: refuses, naming only that one' {
            $r = Invoke-Frp (New-Scenario @{ held = @{ ra = @{ pa = 'Deep' }; ra1 = @{ pa = 'Deep' }; rb = @{}; rb1 = @{}; rc = @{ pc = 'Deep'; pz = 'Basic' }; rc1 = @{ pc = 'Deep'; pz = 'Basic' } } }) @('-Remove', '-Apply') -WithRemoval -AddsPending
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'while 1 declared change\(s\) are pending: Role B prvCreatesprk_b \(G2\)'
            $r.Out | Should -Not -Match 'pending:.*Role A'
            $r.Posts.Count | Should -Be 0
        }
        It 'a pending change still refuses when no removal is pending' {
            $r = Invoke-Frp (New-Scenario @{ held = @{ ra = @{}; ra1 = @{}; rb = @{ pb = 'Deep' }; rb1 = @{ pb = 'Deep' }; rc = @{ pz = 'Basic' }; rc1 = @{ pz = 'Basic' } } }) @('-Remove', '-Apply') -WithRemoval -AddsPending
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'REFUSED'
            $r.Out | Should -Not -Match 'Nothing to change'
        }
        It 'it reads the adds but never writes them (refused: no POST at all)' {
            $r = Invoke-Frp (New-Scenario) @('-Remove', '-Apply') -WithRemoval -AddsPending
            @($r.Requests | Where-Object { $_.Uri -match '=ra|=rb' }).Count | Should -BeGreaterThan 0
            @($r.Posts | Where-Object { $_.Uri -match 'AddPrivilegesRole' }).Count | Should -Be 0
        }
        It 'the refusal does not print a rollback command' {
            (Invoke-Frp (New-Scenario) @('-Remove') -WithRemoval -AddsPending).Out | Should -Not -Match 'Rollback\s+:'
        }
        It 'the adds held (and the removal pending): -Remove runs (control)' {
            $r = Invoke-Frp (New-Scenario) @('-Remove') -WithRemoval
            $r.Exit | Should -Be 0
            $r.Out | Should -Match 'Changes\s+: none pending'
            $r.Out | Should -Not -Match 'REFUSED'
        }
        It '-Remove -Verify checks the removals only: a pending change does not fail it (control)' {
            $r = Invoke-Frp (New-Scenario @{ held = @{ ra = @{}; ra1 = @{}; rb = @{}; rb1 = @{}; rc = @{ pz = 'Basic' }; rc1 = @{ pz = 'Basic' } } }) @('-Remove', '-Verify') -WithRemoval -AddsPending
            $r.Exit | Should -Be 0
            $r.Out | Should -Match 'VERIFY PASS'
        }
        It '-Restore does not read the adds (control)' {
            $r = Invoke-Frp (New-Scenario) @('-Restore') -WithRemoval -AddsPending
            @($r.Requests | Where-Object { $_.Uri -match 'sprk_a|sprk_b|=ra|=rb' }).Count | Should -Be 0
        }
    }

    Context '046c fix round: -Gap must name a gap' {
        It '<case> stops before anything is read' -ForEach @(
            @{ case = '-Remove -Gap ""'; a = @('-Remove', '-Gap', "''") }
            @{ case = '-Remove -Gap @("")'; a = @('-Remove', '-Gap', '@('''')') }
            @{ case = '-Remove -Gap $null'; a = @('-Remove', '-Gap', '$null') }
            @{ case = '-Restore -Gap ""'; a = @('-Restore', '-Gap', "''") }
            @{ case = '-Remove -Gap ","'; a = @('-Remove', '-Gap', "','") }
        ) {
            $r = Invoke-Frp (New-Scenario) $a -WithRemoval
            $r.Exit | Should -Be 1
            $r.Out | Should -Match '-Gap names no gap\.'
            $r.Requests.Count | Should -Be 0
        }
        It 'an empty -Gap without -Remove or -Restore is still refused as a -Gap without a mode' {
            $r = Invoke-Frp (New-Scenario) @('-Gap', "''") -WithRemoval
            $r.Exit | Should -Be 1
            $r.Out | Should -Match '-Gap limits -Remove or -Restore'
            $r.Requests.Count | Should -Be 0
        }
        It 'no -Gap at all is not an empty -Gap (control)' {
            $r = Invoke-Frp (New-Scenario) @('-Remove') -WithRemoval
            $r.Out | Should -Not -Match 'names no gap'
            $r.Exit | Should -Be 0
        }
    }

    Context '046c fix round: the rollback line is printed once, before the apply' {
        It '-Remove -Apply whose final report is printed: no third rollback line, none for zero removals' {
            $r = Invoke-Frp (New-Scenario) @('-Remove', '-Apply') -WithRemoval -SecondRemoval
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'REMOVED and read back'
            $r.Out | Should -Not -Match 'restores exactly the 0 removal'
            # one line before the apply, one after the read-back; the final report prints none
            @([regex]::Matches($r.Out, 'Rollback\s+:')).Count | Should -Be 2
        }
        It 'the line after the read-back is the same command as the one before the apply' {
            $r = Invoke-Frp (New-Scenario) @('-Remove', '-Apply') -WithRemoval -SecondRemoval
            $cmds = @([regex]::Matches($r.Out, 'Rollback\s+: (pwsh [^\r\n]*?-Apply)') | ForEach-Object { $_.Groups[1].Value })
            $cmds.Count | Should -Be 2
            $cmds[0] | Should -Be $cmds[1]
            $cmds[0] | Should -Match '-Restore -Gap R1 -Apply$'
        }
        It 'a dry run with no removal pending prints no rollback line' {
            $r = Invoke-Frp (New-Scenario @{ held = @{ ra = @{}; ra1 = @{}; rb = @{}; rb1 = @{}; rc = @{ pz = 'Basic' }; rc1 = @{ pz = 'Basic' } } }) @('-Remove') -WithRemoval
            $r.Exit | Should -Be 0
            $r.Out | Should -Not -Match 'Rollback\s+:'
        }
    }

    Context '046c fix round: -Restore without -Gap when the block holds more than one gap' {
        It '-Restore refuses, naming the gaps, and reads nothing' {
            $r = Invoke-Frp (New-Scenario) @('-Restore') -WithRemoval -SecondRemoval
            $r.Exit | Should -Be 1
            $r.Out | Should -Match '-Restore without -Gap would restore the removals of 2 gaps \(R1, R2\)'
            $r.Requests.Count | Should -Be 0
        }
        It '-Restore -Apply and -Restore -Verify refuse the same way' {
            foreach ($mode in '-Apply', '-Verify') {
                $r = Invoke-Frp (New-Scenario) @('-Restore', $mode) -WithRemoval -SecondRemoval
                $r.Exit | Should -Be 1
                $r.Out | Should -Match 'Name the gap\(s\) to restore'
                $r.Posts.Count | Should -Be 0
            }
        }
        It '-Restore with one gap in the block needs no -Gap (control)' {
            (Invoke-Frp (New-Scenario) @('-Restore', '-Verify') -WithRemoval).Exit | Should -Be 0
        }
        It '-Restore -Gap names the gap and runs (control)' {
            (Invoke-Frp (New-Scenario) @('-Restore', '-Gap', 'R1', '-Verify') -WithRemoval -SecondRemoval).Exit | Should -Be 0
        }
        It 'a -Remove with several gaps pending prints a rollback that names them (the plain command would refuse)' {
            $s = New-Scenario
            $s.held['rd'] = @{ pd = 'Deep' }
            $r = Invoke-Frp $s @('-Remove') -WithRemoval -SecondRemoval
            $r.Exit | Should -Be 0
            $r.Out | Should -Match 'Rollback\s+: pwsh scripts/Set-FeatureRolePrivileges.ps1 -EnvironmentUrl https://example.invalid -Restore -Gap R1,R2 -Apply'
            $r.Out | Should -Match 'restores exactly the 2 removal\(s\) above'
        }
    }

    Context '046c fix round 2: the gate counts business-unit copy drift on the changes roles as a pending add' {
        BeforeAll {
            # Role A's root holds its add, its copy does not; everything else is in place and the removal is pending.
            $script:copyMissing = { New-Scenario @{ held = @{ ra = @{ pa = 'Deep' }; ra1 = @{}; rb = @{ pb = 'Deep' }; rb1 = @{ pb = 'Deep' }; rc = @{ pc = 'Deep'; pz = 'Basic' }; rc1 = @{ pc = 'Deep'; pz = 'Basic' } } } }
        }
        It '-Remove -Apply refuses, exits 1, lists the copy and posts nothing' {
            $r = Invoke-Frp (& $script:copyMissing) @('-Remove', '-Apply') -WithRemoval -AddsPending
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'REFUSED: -Remove will not run while 1 business-unit copy difference\(s\) on a declared privilege: Role A prvCreatesprk_a copy ra1 holds None root holds Deep'
            $r.Out | Should -Match 'Nothing was changed'
            $r.Posts.Count | Should -Be 0
        }
        It 'the dry run refuses too, prints the copy in the report and no rollback line' {
            $r = Invoke-Frp (& $script:copyMissing) @('-Remove') -WithRemoval -AddsPending
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'REFUSED'
            $r.Out | Should -Match '  Role A\s+prvCreatesprk_a\s+copy ra1 holds None'
            $r.Out | Should -Not -Match 'Rollback\s+:'
            $r.Out | Should -Not -Match 'DRY RUN: no writes'
            $r.Posts.Count | Should -Be 0
        }
        It 'a pending change AND a differing copy are both named, and the hint names the adds-only verify' {
            $s = & $script:copyMissing
            $s.held['rb'] = @{}; $s.held['rb1'] = @{}
            $r = Invoke-Frp $s @('-Remove', '-Gap', 'R1', '-Apply') -WithRemoval -AddsPending
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'while 1 declared change\(s\) are pending: Role B prvCreatesprk_b \(G2\) and 1 business-unit copy difference\(s\)'
            $r.Out | Should -Match 'confirm with pwsh scripts/Set-FeatureRolePrivileges.ps1 -EnvironmentUrl https://example.invalid -Verify -ChangesOnly, then re-run -Remove -Gap R1 -Apply'
            $r.Posts.Count | Should -Be 0
        }
        It 'a copy that matches its root role does not refuse (control)' {
            $r = Invoke-Frp (New-Scenario) @('-Remove', '-Apply') -WithRemoval
            $r.Exit | Should -Be 0
            $r.Posts.Count | Should -Be 1
        }
        It 'a drifted copy of a role that is not in the changes block does not refuse (control)' {
            # Role C's copy differs from its root role on pz, which is not a declared change.
            $s = New-Scenario
            $s.held['rc1'] = @{ pc = 'Deep' }
            $r = Invoke-Frp $s @('-Remove', '-Apply') -WithRemoval
            $r.Out | Should -Not -Match 'REFUSED'
        }
    }

    Context '046c fix round 2: -Verify -ChangesOnly, the check that can pass before the removals' {
        BeforeAll {
            $script:addsHeld = @{ ra = @{ pa = 'Deep' }; ra1 = @{ pa = 'Deep' }; rb = @{ pb = 'Deep' }; rb1 = @{ pb = 'Deep' } }
            $script:addsDoneRemovalPending = { New-Scenario @{ held = ($script:addsHeld + @{ rc = @{ pc = 'Deep'; pz = 'Basic' }; rc1 = @{ pc = 'Deep'; pz = 'Basic' } }) } }
        }
        It 'adds applied everywhere and the removal pending: -Verify -ChangesOnly exits 0 and does not read the removals roles' {
            $r = Invoke-Frp (& $script:addsDoneRemovalPending) @('-Verify', '-ChangesOnly') -WithRemoval
            $r.Exit | Should -Be 0
            $r.Out | Should -Match 'VERIFY PASS: the live roles hold the declared set \(-ChangesOnly: the removals were not checked\)'
            $r.Out | Should -Not -Match 'Removals\s+:'
            @($r.Requests | Where-Object { $_.Uri -match '=rc|sprk_c' }).Count | Should -Be 0
            $r.Posts.Count | Should -Be 0
        }
        It 'plain -Verify fails at the same state (control: the removal is pending)' {
            $r = Invoke-Frp (& $script:addsDoneRemovalPending) @('-Verify') -WithRemoval
            $r.Exit | Should -Be 1
            $r.Out | Should -Match '1 removal\(s\) pending'
        }
        It '-Verify -ChangesOnly fails while a change is pending, and while a copy differs' {
            $pending = Invoke-Frp (New-Scenario) @('-Verify', '-ChangesOnly') -WithRemoval
            $pending.Exit | Should -Be 1
            $pending.Out | Should -Match 'VERIFY FAIL: 2 change\(s\) pending'
            $drift = Invoke-Frp (New-Scenario @{ held = @{ ra = @{ pa = 'Deep' }; ra1 = @{}; rb = @{ pb = 'Deep' }; rb1 = @{ pb = 'Deep' }; rc = @{ pc = 'Deep' }; rc1 = @{ pc = 'Deep' } } }) @('-Verify', '-ChangesOnly') -WithRemoval
            $drift.Exit | Should -Be 1
            $drift.Out | Should -Match '1 business-unit copy difference\(s\)'
        }
        It '-ChangesOnly without -Verify, or with -Remove or -Restore, stops before anything is read' {
            foreach ($a in @(@('-ChangesOnly'), @('-ChangesOnly', '-Apply'), @('-ChangesOnly', '-Remove'), @('-Verify', '-ChangesOnly', '-Restore'))) {
                $r = Invoke-Frp (New-Scenario) $a -WithRemoval
                $r.Exit | Should -Be 1
                $r.Out | Should -Match '-ChangesOnly'
                $r.Requests.Count | Should -Be 0
            }
        }
        It 'the refusal of -Remove names this command, and it passes at the state the refusal waits for' {
            $refused = Invoke-Frp (New-Scenario) @('-Remove') -WithRemoval -AddsPending
            $refused.Out | Should -Match 'confirm with pwsh scripts/Set-FeatureRolePrivileges.ps1 -EnvironmentUrl https://example.invalid -Verify -ChangesOnly,'
            $refused.Out | Should -Not -Match 'confirm with -Verify,'
            (Invoke-Frp (& $script:addsDoneRemovalPending) @('-Verify', '-ChangesOnly') -WithRemoval).Exit | Should -Be 0
        }
    }

    Context '046c fix round 2: the rollback line after the apply carries the same exactness as the one before' {
        It 'not exact: both lines carry the NOT exact note, and the second does not claim to put back the removals above' {
            # R1 (Role C) is pending; R1 also lists Role D, whose root role no longer holds it, so -Restore would add Role D's too.
            $r = Invoke-Frp (New-Scenario) @('-Remove', '-Apply') -WithRemoval -SecondRemoval -SecondGap 'R1'
            $r.Out | Should -Match 'REMOVED and read back'
            @([regex]::Matches($r.Out, 'NOT exact: 1 listed removal\(s\) are not pending here')).Count | Should -Be 2
            @([regex]::Matches($r.Out, 'add them as well:\s+Role D\s+prvAppendTosprk_d')).Count | Should -Be 2
            $r.Out | Should -Not -Match 'puts back the'
            @([regex]::Matches($r.Out, 'Rollback\s+:')).Count | Should -Be 2
        }
        It 'exact: the line after the apply still says it puts back the removals above (control)' {
            $r = Invoke-Frp (New-Scenario) @('-Remove', '-Apply') -WithRemoval
            $r.Out | Should -Match 'Rollback\s+: .* \(puts back the 1 removal\(s\) above\)'
            $r.Out | Should -Not -Match 'NOT exact'
        }
    }

    Context '046c fix round 2: a run that will refuse prints no rollback line' {
        BeforeAll {
            $script:roleDGlobal = { $s = New-Scenario; $s.held['rd'] = @{ pd = 'Global' }; $s.held['rd1'] = @{ pd = 'Global' }; $s }
        }
        It 'a dry run with one removal planned and another held at another depth refuses and prints no rollback' {
            $r = Invoke-Frp (& $script:roleDGlobal) @('-Remove') -WithRemoval -SecondRemoval
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'held at Global, the config expects Deep'
            $r.Out | Should -Not -Match 'Rollback\s+:'
            $r.Posts.Count | Should -Be 0
        }
        It 'the same state under -Apply prints no rollback line either' {
            $r = Invoke-Frp (& $script:roleDGlobal) @('-Remove', '-Apply') -WithRemoval -SecondRemoval
            $r.Exit | Should -Be 1
            $r.Out | Should -Not -Match 'Rollback\s+:'
            $r.Posts.Count | Should -Be 0
        }
        It 'a dry run that does not refuse still prints it (control)' {
            (Invoke-Frp (New-Scenario) @('-Remove') -WithRemoval).Out | Should -Match 'Rollback\s+:'
        }
    }

    Context 'fail closed with no sign-in (the real az, an empty AZURE_CONFIG_DIR)' {
        It 'a dry run exits 1 and changes nothing' {
            $empty = Join-Path $script:Work 'empty-azure-config'
            New-Item -ItemType Directory -Path $empty -Force | Out-Null
            $old = $env:AZURE_CONFIG_DIR
            try { $env:AZURE_CONFIG_DIR = $empty; $r = Invoke-Frp (New-Scenario) -RealAz } finally { $env:AZURE_CONFIG_DIR = $old }
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'FAIL: a read failed'
            $r.Out | Should -Match 'Nothing was changed'
        }
    }
}
