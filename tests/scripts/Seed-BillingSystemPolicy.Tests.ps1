# Pester 5. Run: Invoke-Pester tests/scripts/Seed-BillingSystemPolicy.Tests.ps1
# ebilling-r1 task 049 item 4 (D-197): the policy and version scripts/ebilling/Seed-BillingSystemPolicy.ps1 CREATES belong to the customer
# top business unit's restricted-content owner team when -CustomerBusinessUnit is passed, exactly as scripts/Apply-DoLanePolicies.ps1 does
# (ontology task 171). Without the parameter the script is unchanged.
#
# The whole script runs in a child pwsh over the Dataverse test double in Apply-DoLanePolicies.Harness.ps1 (no environment, no network);
# exit codes and the requests made are the evidence. The owner code is a COPY of Apply-DoLanePolicies.ps1's, so the last Describe pins the
# two copies to each other, read out of both scripts' syntax trees: a change to one fails here until the other follows.

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:ScriptPath = Join-Path $script:RepoRoot 'scripts' 'ebilling' 'Seed-BillingSystemPolicy.ps1'
    $script:ApplyPath = Join-Path $script:RepoRoot 'scripts' 'Apply-DoLanePolicies.ps1'
}

Describe 'Owner of the rows Seed-BillingSystemPolicy creates (-CustomerBusinessUnit, ebilling-r1 task 049, D-197)' {
    BeforeAll {
        $script:Pwsh = (Get-Process -Id $PID).Path
        $script:Harness = Join-Path $PSScriptRoot 'Apply-DoLanePolicies.Harness.ps1'
        $script:TeamName = 'Spaarke Restricted Content Owners'
        $script:PolicyId = '04900125-0000-4000-8000-000000000001'
        $script:VersionId = '04900125-0000-4000-8000-000000000002'
        $script:Ids = @{
            Root = '00000000-0000-4000-8000-0000000b0001'; Customer = '00000000-0000-4000-8000-0000000b0002'
            Secure = '00000000-0000-4000-8000-0000000b0003'; Child = '00000000-0000-4000-8000-0000000b0004'
            Other = '00000000-0000-4000-8000-0000000b0005'; Team = '16685363-43d3-f111-84e8-00224820fd20'
            Team2 = '16685363-43d3-f111-84e8-00224820fd21'; User = '00000000-0000-4000-8000-0000000c0001'
            Stranger = '00000000-0000-4000-8000-0000000c0002'; Missing = '00000000-0000-4000-8000-0000000b00ff'
            AssignPolicy = '00000000-0000-4000-8000-0000000d0001'; AssignVersion = '00000000-0000-4000-8000-0000000d0002'
        }
        $script:OwnerBind = "/teams($($script:Ids.Team))"

        # The live option labels the seed definition needs.
        $script:Options = @{
            sprk_lane = @{ Decide = 100000000 }; sprk_subjecttype = @{ Invoice = 100000000 }
            sprk_severity = @{ Info = 100000000 }; sprk_ruletype = @{ SubmissionIntegrity = 100000014 }
        }

        function New-Scenario {
            @{
                userId           = $script:Ids.User
                options          = $script:Options
                businessUnits    = @(
                    @{ businessunitid = $script:Ids.Root; name = 'Spaarke'; _parentbusinessunitid_value = $null }
                    @{ businessunitid = $script:Ids.Customer; name = 'Contoso Legal'; _parentbusinessunitid_value = $script:Ids.Root }
                    @{ businessunitid = $script:Ids.Secure; name = 'Secure Record'; _parentbusinessunitid_value = $script:Ids.Root }
                    @{ businessunitid = $script:Ids.Child; name = 'Contoso Litigation'; _parentbusinessunitid_value = $script:Ids.Customer }
                )
                teams            = @(@{ teamid = $script:Ids.Team; name = $script:TeamName; teamtype = 0; isdefault = $false; _businessunitid_value = $script:Ids.Customer })
                memberships      = @()
                privileges       = @(
                    @{ privilegeid = $script:Ids.AssignPolicy; name = 'prvAssignsprk_policy' }
                    @{ privilegeid = $script:Ids.AssignVersion; name = 'prvAssignsprk_policyversion' }
                )
                userPrivilegeIds = @($script:Ids.AssignPolicy, $script:Ids.AssignVersion)
                policies         = @()
                versions         = @()
            }
        }

        function Copy-Scenario($Db) { $Db | ConvertTo-Json -Depth 32 | ConvertFrom-Json -AsHashtable -Depth 32 }

        # Runs the whole script in a child pwsh against the test double. Returns the exit code, the text, every request, the final state.
        function Invoke-SeedScript([hashtable]$Scenario, [hashtable]$ScriptArgs, [string]$ScriptPath = $script:ScriptPath) {
            $dir = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $dir | Out-Null
            $files = @{ Scenario = 'scenario.json'; Log = 'requests.jsonl'; Out = 'state.json'; Args = 'args.json'; Wrapper = 'run.ps1' }
            $f = @{}; foreach ($k in $files.Keys) { $f[$k] = Join-Path $dir $files[$k] }
            $Scenario | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $f.Scenario
            (@{ DataverseUrl = 'https://fake.crm.dynamics.com' } + $ScriptArgs) | ConvertTo-Json | Set-Content -LiteralPath $f.Args
            @'
param([string]$ScenarioPath, [string]$LogPath, [string]$OutPath, [string]$ArgsPath, [string]$ScriptPath, [string]$HarnessPath)
$ErrorActionPreference = 'Stop'
$global:FakeDb = Get-Content -LiteralPath $ScenarioPath -Raw | ConvertFrom-Json -AsHashtable -Depth 32
$global:FakeLogPath = $LogPath
. $HarnessPath
$splat = @{}
foreach ($e in (Get-Content -LiteralPath $ArgsPath -Raw | ConvertFrom-Json -AsHashtable).GetEnumerator()) { $splat[$e.Key] = $e.Value }
$code = 0
$global:LASTEXITCODE = 0
try { & $ScriptPath @splat; if ($LASTEXITCODE) { $code = $LASTEXITCODE } }
catch { Write-Host "SCRIPT THREW: $($_.Exception.Message)"; $code = 1 }
finally { $global:FakeDb | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $OutPath }
exit $code
'@ | Set-Content -LiteralPath $f.Wrapper
            $text = & $script:Pwsh -NoProfile -NonInteractive -File $f.Wrapper $f.Scenario $f.Log $f.Out $f.Args $ScriptPath $script:Harness 2>&1 | Out-String
            $exit = $LASTEXITCODE
            [pscustomobject]@{
                ExitCode = $exit
                Output   = $text
                Requests = @(if (Test-Path -LiteralPath $f.Log) { Get-Content -LiteralPath $f.Log | ForEach-Object { $_ | ConvertFrom-Json } })
                Db       = if (Test-Path -LiteralPath $f.Out) { Get-Content -LiteralPath $f.Out -Raw | ConvertFrom-Json -AsHashtable -Depth 32 }
            }
        }

        function Get-Writes($Run) { @($Run.Requests | Where-Object { $_.Method -ne 'GET' }) }
        function Get-Creates($Run, [string]$Table) { @($Run.Requests | Where-Object { $_.Method -eq 'POST' -and $_.Uri -like "*/$Table" }) }

        # A chain of $Length business units c0 -> c1 -> ... -> root, none repeating (the walk is bounded at MaxParentWalk).
        function Get-ChainId([int]$Index) { '00000000-0000-4000-8000-{0:x12}' -f (0xE0000 + $Index) }
        function New-ChainScenario([int]$Length) {
            $s = New-Scenario
            $units = @($s.businessUnits | Where-Object { $_.businessunitid -in $script:Ids.Root, $script:Ids.Secure })
            for ($i = 0; $i -lt $Length; $i++) {
                $parent = if ($i -eq $Length - 1) { $script:Ids.Root } else { Get-ChainId ($i + 1) }
                $units += @{ businessunitid = (Get-ChainId $i); name = "Chain $i"; _parentbusinessunitid_value = $parent }
            }
            $s.businessUnits = $units
            $s
        }
    }

    Context 'with the parameter: the apply' {
        BeforeAll {
            $script:Applied = Invoke-SeedScript (New-Scenario) @{ CustomerBusinessUnit = $script:Ids.Customer }
        }

        It 'applies cleanly and the read-back passes' {
            $script:Applied.ExitCode | Should -Be 0 -Because $script:Applied.Output
            $script:Applied.Output | Should -Match 'READ-BACK OK: BILL-SYSTEM-SUBMISSION policy'
            $script:Applied.Output | Should -Match "Owner: created rows belong to team 'Spaarke Restricted Content Owners' \($($script:Ids.Team)\)"
        }

        It 'must fire: the sprk_policy create carries ownerid@odata.bind to the team' {
            $creates = Get-Creates $script:Applied 'sprk_policies'
            $creates.Count | Should -Be 1
            ($creates[0].Body | ConvertFrom-Json).'ownerid@odata.bind' | Should -Be $script:OwnerBind
        }

        It 'must fire: the sprk_policyversion create carries ownerid@odata.bind to the team' {
            $creates = Get-Creates $script:Applied 'sprk_policyversions'
            $creates.Count | Should -Be 1
            ($creates[0].Body | ConvertFrom-Json).'ownerid@odata.bind' | Should -Be $script:OwnerBind
        }

        It 'the created rows read back owned by the team, not the operator' {
            @($script:Applied.Db.policies).Count | Should -Be 1
            @($script:Applied.Db.versions).Count | Should -Be 1
            foreach ($r in @($script:Applied.Db.policies) + @($script:Applied.Db.versions)) { $r._ownerid_value | Should -Be $script:Ids.Team }
        }

        It 'only the two creates bind an owner: the other writes (the current-version pointer) carry none' {
            $others = @(Get-Writes $script:Applied | Where-Object { $_.Method -ne 'POST' })
            $others.Count | Should -BeGreaterThan 0
            foreach ($w in $others) { $w.Body | Should -Not -Match 'ownerid' }
        }

        It 'resolves the team and checks Assign before the first write' {
            $first = [array]::IndexOf(@($script:Applied.Requests.Method), 'POST')
            $before = @($script:Applied.Requests | Select-Object -First $first | ForEach-Object { $_.Uri })
            ($before -join "`n") | Should -Match 'businessunits\?'
            ($before -join "`n") | Should -Match 'teams\?'
            ($before -join "`n") | Should -Match 'teammemberships\?'
            ($before -join "`n") | Should -Match 'RetrieveUserPrivileges'
        }

        It 'reads the team by the C# rule: the configured name, an Owner team (teamtype 0), not a default team, in the customer unit' {
            $q = ($script:Applied.Requests | Where-Object { $_.Uri -match '/teams\?' } | Select-Object -First 1).Uri
            $q | Should -Match ([regex]::Escape("name eq '$($script:TeamName)' and teamtype eq 0 and isdefault eq false and _businessunitid_value eq $($script:Ids.Customer)"))
        }
    }

    Context 'with the parameter: by name, and -WhatIf' {
        It 'a unit named instead of numbered resolves the same team' {
            $run = Invoke-SeedScript (New-Scenario) @{ CustomerBusinessUnit = 'Contoso Legal' }
            $run.ExitCode | Should -Be 0 -Because $run.Output
            foreach ($c in @(Get-Creates $run 'sprk_policies') + @(Get-Creates $run 'sprk_policyversions')) { ($c.Body | ConvertFrom-Json).'ownerid@odata.bind' | Should -Be $script:OwnerBind }
        }

        It 'must fire: -WhatIf shows the owner it would bind, and writes nothing' {
            $run = Invoke-SeedScript (New-Scenario) @{ CustomerBusinessUnit = $script:Ids.Customer; WhatIf = $true }
            $run.ExitCode | Should -Be 0 -Because $run.Output
            Get-Writes $run | Should -BeNullOrEmpty
            ([regex]::Matches($run.Output, "owner = team $($script:Ids.Team)")).Count | Should -BeGreaterOrEqual 2 -Because 'both creates name the owner'
            $run.Output | Should -Match ([regex]::Escape("would write ownerid = /teams($($script:Ids.Team))"))
        }

        It 'a refusal is a refusal in -WhatIf too (a missing team stops the run)' {
            $s = New-Scenario; $s.teams = @()
            $run = Invoke-SeedScript $s @{ CustomerBusinessUnit = $script:Ids.Customer; WhatIf = $true }
            $run.ExitCode | Should -Not -Be 0
            $run.Output | Should -Match 'OwnerTeamNotFound'
        }
    }

    Context 'must not fire: without the parameter nothing changes' {
        BeforeAll {
            $script:Plain = Invoke-SeedScript (New-Scenario) @{}
            $script:PlainWhatIf = Invoke-SeedScript (New-Scenario) @{ WhatIf = $true }
        }

        It 'applies cleanly' {
            $script:Plain.ExitCode | Should -Be 0 -Because $script:Plain.Output
            $script:Plain.Output | Should -Match 'READ-BACK OK: BILL-SYSTEM-SUBMISSION policy'
        }

        It 'no create carries an owner, and the rows are the operator''s (as before)' {
            (Get-Creates $script:Plain 'sprk_policies').Count | Should -Be 1
            (Get-Creates $script:Plain 'sprk_policyversions').Count | Should -Be 1
            foreach ($c in @(Get-Creates $script:Plain 'sprk_policies') + @(Get-Creates $script:Plain 'sprk_policyversions')) { $c.Body | Should -Not -Match 'ownerid' }
            foreach ($r in @($script:Plain.Db.policies) + @($script:Plain.Db.versions)) { $r._ownerid_value | Should -Be $script:Ids.User }
        }

        It 'reads nothing about business units, teams, memberships or privileges, and does not select the owner' {
            $uris = @($script:Plain.Requests | ForEach-Object { $_.Uri })
            ($uris -join "`n") | Should -Not -Match 'businessunits|/teams|teammemberships|privileges|RetrieveUserPrivileges|_ownerid_value'
        }

        It 'prints no owner line, and -WhatIf mentions no owner' {
            $script:Plain.Output | Should -Not -Match '(?i)owner'
            $script:PlainWhatIf.ExitCode | Should -Be 0 -Because $script:PlainWhatIf.Output
            $script:PlainWhatIf.Output | Should -Not -Match '(?i)owner'
            Get-Writes $script:PlainWhatIf | Should -BeNullOrEmpty
        }

        It 'the plain apply is idempotent (a second run writes nothing)' {
            $again = Invoke-SeedScript (Copy-Scenario $script:Plain.Db) @{}
            $again.ExitCode | Should -Be 0 -Because $again.Output
            Get-Writes $again | Should -BeNullOrEmpty
        }
    }

    Context 'must fire: the run stops before any write, with a non-zero exit' {
        BeforeAll {
            function Get-RefusalCase([string]$Case) {
                $s = New-Scenario
                $arg = $script:Ids.Customer
                switch ($Case) {
                    'root' { $arg = $script:Ids.Root }
                    'grandchild' { $arg = $script:Ids.Child }
                    'secure' { $arg = $script:Ids.Secure }
                    'unit-name-unknown' { $arg = 'No Such Unit' }
                    'unit-name-ambiguous' { $s.businessUnits += @{ businessunitid = $script:Ids.Other; name = 'Contoso Legal'; _parentbusinessunitid_value = $script:Ids.Root }; $arg = 'Contoso Legal' }
                    'secure-name-ambiguous' { $s.businessUnits += @{ businessunitid = $script:Ids.Other; name = 'Secure Record'; _parentbusinessunitid_value = $script:Ids.Root } }
                    'chain-loops' { ($s.businessUnits | Where-Object businessunitid -eq $script:Ids.Customer)._parentbusinessunitid_value = $script:Ids.Child; ($s.businessUnits | Where-Object businessunitid -eq $script:Ids.Child)._parentbusinessunitid_value = $script:Ids.Customer }
                    'chain-unreadable' { ($s.businessUnits | Where-Object businessunitid -eq $script:Ids.Customer)._parentbusinessunitid_value = $script:Ids.Missing }
                    'team-missing' { $s.teams = @() }
                    'team-id-is-empty-guid' { $s.teams[0].teamid = [guid]::Empty.ToString() }
                    'chain-too-long' { $s = New-ChainScenario 35; $arg = Get-ChainId 0 }
                    'team-is-default' { $s.teams[0].isdefault = $true }
                    'team-not-owner-type' { $s.teams[0].teamtype = 1 }
                    'team-in-another-unit' { $s.teams[0]._businessunitid_value = $script:Ids.Secure }
                    'team-ambiguous' { $s.teams += @{ teamid = $script:Ids.Team2; name = $script:TeamName; teamtype = 0; isdefault = $false; _businessunitid_value = $script:Ids.Customer } }
                    'team-has-member' { $s.memberships = @(@{ teamid = $script:Ids.Team; systemuserid = $script:Ids.Stranger }) }
                    'no-assign-on-version' { $s.userPrivilegeIds = @($script:Ids.AssignPolicy) }
                    'no-assign-at-all' { $s.userPrivilegeIds = @() }
                    'no-assign-privilege-defined' { $s.privileges = @() }
                }
                @{ Scenario = $s; Arg = $arg }
            }
        }

        It '<Case> -> <Expect>' -ForEach @(
            @{ Case = 'root'; Expect = 'AnswerIsRootBusinessUnit' }
            @{ Case = 'grandchild'; Expect = 'CustomerBusinessUnitNotTopLevel' }
            @{ Case = 'secure'; Expect = 'CustomerBusinessUnitIsSecureRecord' }
            @{ Case = 'unit-name-unknown'; Expect = 'CustomerBusinessUnitNotFound' }
            @{ Case = 'unit-name-ambiguous'; Expect = 'CustomerBusinessUnitAmbiguous' }
            @{ Case = 'secure-name-ambiguous'; Expect = 'SecureBusinessUnitAmbiguous' }
            @{ Case = 'chain-loops'; Expect = 'ParentChainLoops' }
            @{ Case = 'chain-unreadable'; Expect = 'ParentChainUnreadable' }
            @{ Case = 'team-missing'; Expect = 'OwnerTeamNotFound' }
            @{ Case = 'team-id-is-empty-guid'; Expect = 'OwnerTeamNotFound' }
            @{ Case = 'chain-too-long'; Expect = 'ParentChainLoops' }
            @{ Case = 'team-is-default'; Expect = 'OwnerTeamNotFound' }
            @{ Case = 'team-not-owner-type'; Expect = 'OwnerTeamNotFound' }
            @{ Case = 'team-in-another-unit'; Expect = 'OwnerTeamNotFound' }
            @{ Case = 'team-ambiguous'; Expect = 'OwnerTeamAmbiguous' }
            @{ Case = 'team-has-member'; Expect = 'OwnerTeamHasMembers' }
            @{ Case = 'no-assign-on-version'; Expect = 'lacks prvAssignsprk_policyversion' }
            @{ Case = 'no-assign-at-all'; Expect = 'lacks prvAssignsprk_policy, prvAssignsprk_policyversion' }
            @{ Case = 'no-assign-privilege-defined'; Expect = 'lacks prvAssignsprk_policy, prvAssignsprk_policyversion' }
        ) {
            $c = Get-RefusalCase $Case
            $run = Invoke-SeedScript $c.Scenario @{ CustomerBusinessUnit = $c.Arg }
            $run.ExitCode | Should -Not -Be 0 -Because $run.Output
            $run.Output | Should -Match ([regex]::Escape($Expect))
            $run.Output | Should -Match 'Nothing was written'
            Get-Writes $run | Should -BeNullOrEmpty -Because 'a refusal happens before any write'
        }
    }

    Context 'existing rows are never re-owned; -Verify reports owner drift and never writes' {
        BeforeAll {
            # Rows the plain (operator-owned) script created, as an environment would hold them before this change.
            $script:OperatorOwned = $script:Plain.Db
            $script:VerifyArgs = @{ CustomerBusinessUnit = $script:Ids.Customer; Verify = $true }
        }

        It 'must fire: -Verify with the parameter reports the policy and the version owned by the operator as DRIFT, exit 1, no write' {
            $run = Invoke-SeedScript (Copy-Scenario $script:OperatorOwned) $script:VerifyArgs
            $run.ExitCode | Should -Be 1 -Because $run.Output
            Get-Writes $run | Should -BeNullOrEmpty
            $expected = "ownerid live='$($script:Ids.User)' expected='$($script:Ids.Team)'"
            ([regex]::Matches($run.Output, [regex]::Escape($expected))).Count | Should -Be 2
            $run.Output | Should -Match 'policy DRIFT: ownerid'
            $run.Output | Should -Match 'v1 DRIFT: ownerid'
        }

        It 'must fire: one row owned by someone else is the only DRIFT' {
            $s = Copy-Scenario $script:Applied.Db
            ($s.versions | Select-Object -First 1)._ownerid_value = $script:Ids.Stranger
            $run = Invoke-SeedScript $s $script:VerifyArgs
            $run.ExitCode | Should -Be 1 -Because $run.Output
            ([regex]::Matches($run.Output, 'DRIFT')).Count | Should -Be 1
            $run.Output | Should -Match "ownerid live='$($script:Ids.Stranger)' expected='$($script:Ids.Team)'"
        }

        It 'rows owned by the team are IN SYNC, exit 0, and -Verify does not ask for Assign (it never writes)' {
            $s = Copy-Scenario $script:Applied.Db
            $s.userPrivilegeIds = @()
            $run = Invoke-SeedScript $s $script:VerifyArgs
            $run.ExitCode | Should -Be 0 -Because $run.Output
            $run.Output | Should -Match 'policy IN SYNC'
            $run.Output | Should -Match 'v1 IN SYNC'
            $run.Output | Should -Not -Match 'DRIFT'
            ($run.Requests | ForEach-Object { $_.Uri }) -join "`n" | Should -Not -Match 'RetrieveUserPrivileges'
            Get-Writes $run | Should -BeNullOrEmpty
        }

        It 'must not fire: -Verify without the parameter ignores the owner (operator-owned rows are IN SYNC)' {
            $run = Invoke-SeedScript (Copy-Scenario $script:OperatorOwned) @{ Verify = $true }
            $run.ExitCode | Should -Be 0 -Because $run.Output
            $run.Output | Should -Not -Match 'DRIFT|owner'
        }

        It 'must not fire: -Verify with the parameter still refuses an unusable team (a fail-closed case reads the same in every mode)' {
            $s = Copy-Scenario $script:OperatorOwned; $s.teams = @()
            $run = Invoke-SeedScript $s $script:VerifyArgs
            $run.ExitCode | Should -Not -Be 0
            $run.Output | Should -Match 'OwnerTeamNotFound'
        }

        It 'must not fire: an apply with the parameter over existing operator-owned rows writes nothing and re-owns nothing' {
            $run = Invoke-SeedScript (Copy-Scenario $script:OperatorOwned) @{ CustomerBusinessUnit = $script:Ids.Customer }
            $run.ExitCode | Should -Be 0 -Because $run.Output
            Get-Writes $run | Should -BeNullOrEmpty
            foreach ($r in @($run.Db.policies) + @($run.Db.versions)) { $r._ownerid_value | Should -Be $script:Ids.User }
        }
    }

    Context 'a parameter that was passed empty is a refusal, never "not passed"' {
        It '<Mode> with <Label> stops before any request, non-zero' -ForEach @(
            foreach ($mode in 'apply', 'WhatIf', 'Verify') { foreach ($v in @(@{ Label = 'empty'; Value = '' }, @{ Label = 'null'; Value = $null }, @{ Label = 'blank'; Value = '   ' })) { @{ Mode = $mode; Label = $v.Label; Value = $v.Value } } }
        ) {
            $a = @{ CustomerBusinessUnit = $Value }
            if ($Mode -ne 'apply') { $a[$Mode] = $true }
            $run = Invoke-SeedScript (New-Scenario) $a
            $run.ExitCode | Should -Not -Be 0 -Because $run.Output
            $run.Output | Should -Match 'CustomerBusinessUnit was passed but is empty'
            $run.Requests.Count | Should -Be 0
        }
    }

    Context 'the read-back holds created rows to the owner' {
        It 'must fire: a service that drops the owner bind fails the read-back, non-zero' {
            $s = New-Scenario; $s.ignoreOwnerBind = $true
            $run = Invoke-SeedScript $s @{ CustomerBusinessUnit = $script:Ids.Customer }
            $run.ExitCode | Should -Not -Be 0 -Because $run.Output
            $run.Output | Should -Match 'READ-BACK FAIL'
            $run.Output | Should -Match 'ownerid live='
        }

        It 'must not fire: without the parameter the same service behaviour is not judged' {
            $s = New-Scenario; $s.ignoreOwnerBind = $true
            $run = Invoke-SeedScript $s @{}
            $run.ExitCode | Should -Be 0 -Because $run.Output
        }
    }

    Context 'an existing operator-owned policy with a new team-owned version' {
        It 'the apply owns only the new version; -Verify then reports the policy as DRIFT and the version IN SYNC' {
            $s = Copy-Scenario $script:Plain.Db
            $s.versions = @()
            $s.policies[0]._sprk_currentversion_value = $null
            $run = Invoke-SeedScript $s @{ CustomerBusinessUnit = $script:Ids.Customer }
            $run.ExitCode | Should -Be 0 -Because $run.Output
            (Get-Creates $run 'sprk_policies').Count | Should -Be 0
            (Get-Creates $run 'sprk_policyversions').Count | Should -Be 1
            @($run.Db.policies)[0]._ownerid_value | Should -Be $script:Ids.User
            @($run.Db.versions)[0]._ownerid_value | Should -Be $script:Ids.Team
            $verify = Invoke-SeedScript (Copy-Scenario $run.Db) $script:VerifyArgs
            $verify.ExitCode | Should -Be 1 -Because $verify.Output
            $verify.Output | Should -Match 'policy DRIFT: ownerid'
            $verify.Output | Should -Match 'v1 IN SYNC'
        }
    }

    Context 'the Secure Record unit and the parent-chain bound' {
        It 'an environment with no Secure Record unit proceeds (none is it)' {
            $s = New-Scenario
            $s.businessUnits = @($s.businessUnits | Where-Object { $_.businessunitid -ne $script:Ids.Secure })
            $run = Invoke-SeedScript $s @{ CustomerBusinessUnit = $script:Ids.Customer }
            $run.ExitCode | Should -Be 0 -Because $run.Output
            @($run.Db.policies)[0]._ownerid_value | Should -Be $script:Ids.Team
        }

        It 'a chain whose root is the 33rd read is still read to the root (32 units: the unit is just not top level)' {
            $run = Invoke-SeedScript (New-ChainScenario 32) @{ CustomerBusinessUnit = (Get-ChainId 0) }
            $run.ExitCode | Should -Not -Be 0
            $run.Output | Should -Match 'CustomerBusinessUnitNotTopLevel'
        }
    }
}

Describe 'The owner code is a copy of Apply-DoLanePolicies.ps1 (ebilling-r1 task 049, ontology task 171, D-197)' {
    BeforeAll {
        function Read-ScriptAst([string]$Path) {
            $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile($Path, [ref]$null, [ref]$errors)
            if ($errors.Count -gt 0) { throw "$Path does not parse: $($errors[0].Message)" }
            $ast
        }
        # The source text of a function or top-level assignment, whitespace-normalised, so the pin is on the code and not on the layout.
        function Get-FunctionText($Ast, [string]$Name) {
            $fn = $Ast.Find({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $Name }, $true)
            if (-not $fn) { return $null }
            ($fn.Extent.Text -replace '\s+', ' ').Trim()
        }
        function Get-AssignmentText($Ast, [string]$Variable) {
            $assign = $Ast.Find({ param($n) $n -is [System.Management.Automation.Language.AssignmentStatementAst] -and "$($n.Left)" -eq "`$$Variable" }, $false)
            if (-not $assign) { return $null }
            ($assign.Extent.Text -replace '\s+', ' ').Trim()
        }
        $script:SeedAst = Read-ScriptAst $script:ScriptPath
        $script:ApplyAst = Read-ScriptAst $script:ApplyPath
    }

    It '<Name> is the same function in both scripts' -ForEach @(
        @{ Name = 'Get-OwnerSelect' }, @{ Name = 'Get-OwnerDiff' }, @{ Name = 'ConvertTo-ODataLiteral' }, @{ Name = 'Get-CustomerTopBusinessUnit' }
        @{ Name = 'New-OwnerResult' }, @{ Name = 'Resolve-RestrictedContentOwner' }, @{ Name = 'Get-MissingAssignPrivileges' }, @{ Name = 'Get-AuthoredBy' }
    ) {
        $apply = Get-FunctionText $script:ApplyAst $Name
        $apply | Should -Not -BeNullOrEmpty -Because "Apply-DoLanePolicies.ps1 defines $Name"
        Get-FunctionText $script:SeedAst $Name | Should -BeExactly $apply
    }

    It '<Name> is the same value in both scripts' -ForEach @(
        @{ Name = 'RestrictedOwnerTeamName' }, @{ Name = 'SecureBusinessUnitName' }, @{ Name = 'MaxParentWalk' }, @{ Name = 'MemberReadLimit' }, @{ Name = 'AssignPrivileges' }
    ) {
        $apply = Get-AssignmentText $script:ApplyAst $Name
        $apply | Should -Not -BeNullOrEmpty -Because "Apply-DoLanePolicies.ps1 defines `$$Name"
        Get-AssignmentText $script:SeedAst $Name | Should -BeExactly $apply
    }

    It 'the Assign privileges are the two tables the script creates rows in' {
        $text = Get-AssignmentText $script:SeedAst 'AssignPrivileges'
        $text | Should -Match "prvAssignsprk_policy'"
        $text | Should -Match "prvAssignsprk_policyversion'"
    }
}
