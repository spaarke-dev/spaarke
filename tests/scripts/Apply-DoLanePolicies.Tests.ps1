# Pester 5. Run: Invoke-Pester tests/scripts/Apply-DoLanePolicies.Tests.ps1
# Ontology task 159 (D-178, FR-49, D-14): advancing a policy's currentVersion in scripts/Apply-DoLanePolicies.ps1 mirrors the product's
# publish order (OntologyAdminAuthoringService, task 033): create the new version, END the previous one (sprk_inforceto = the next
# version's inForceFrom, only when it is empty), then move sprk_currentversion. The current version has no end. Anything else on a
# version row is DRIFT and is never written.
#
# The script talks to a live environment on load, so it is never run here. The decision functions and constants are read out of the
# script's own AST: the code under test is the shipped code, not a copy. The apply order itself (create, end, move) is checked as a
# source order, because running it needs Dataverse.

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:ScriptPath = Join-Path $script:RepoRoot 'scripts' 'Apply-DoLanePolicies.ps1'
    $script:ScriptText = Get-Content -LiteralPath $script:ScriptPath -Raw
    $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($script:ScriptPath, [ref]$null, [ref]$errors)
    if ($errors.Count -gt 0) { throw "The script does not parse: $($errors[0].Message)" }

    foreach ($variable in 'NextStepCodes', 'KindRules', 'MaxHeadline', 'MaxProposedAction', 'MaxShortName') {
        $assign = $ast.Find({ param($n) $n -is [System.Management.Automation.Language.AssignmentStatementAst] -and "$($n.Left)" -eq "`$$variable" }, $false)
        if (-not $assign) { throw "The script no longer defines `$$variable" }
        . ([scriptblock]::Create($assign.Extent.Text))
    }
    foreach ($name in 'Get-Instant', 'Get-NextVersion', 'Get-ExpectedInForceTo', 'Get-VersionEndAction', 'Get-InForceToDiff', 'Test-Definition') {
        $fn = $ast.Find({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name }, $true)
        if (-not $fn) { throw "The script no longer defines $name" }
        . ([scriptblock]::Create($fn.Extent.Text))
    }

    # The lint reads live option maps; here every label the checked-in definitions use is "on the live column".
    $laneMap = @{ Decide = 100000000; Do = 100000001 }
    $subjectMap = @{}; $workMap = @{}; $severityMap = @{}; $ruleTypeMap = @{}
    foreach ($file in 'do-lane.policies.json', 'know-promotion.policies.json') {
        $def = Get-Content -LiteralPath (Join-Path $script:RepoRoot 'config' $file) -Raw | ConvertFrom-Json -Depth 32
        foreach ($p in $def.policies) {
            $subjectMap[$p.subjectType] = 1; $workMap[$p.workType] = 1; $severityMap[$p.severity] = 1
            foreach ($v in $p.versions) { $ruleTypeMap[$v.ruleType] = 1 }
        }
    }

    function Read-Definition([string]$File) {
        Get-Content -LiteralPath (Join-Path $script:RepoRoot 'config' $File) -Raw | ConvertFrom-Json -Depth 32
    }

    # A live version row as the Web API returns it (only the column under test).
    function New-LiveRow($End) { [pscustomobject]@{ sprk_inforceto = $End } }
}

Describe 'Get-ExpectedInForceTo (the shipped know-promotion definition)' {
    BeforeAll {
        $script:Policy = (Read-Definition 'know-promotion.policies.json').policies | Where-Object code -eq 'POL-MATTER-NOBUDGET'
        $script:V1 = $script:Policy.versions | Where-Object versionNumber -eq 1
        $script:V2 = $script:Policy.versions | Where-Object versionNumber -eq 2
        $script:V3 = $script:Policy.versions | Where-Object versionNumber -eq 3
    }

    It 'the superseded v1 ends at v2''s inForceFrom' {
        Get-ExpectedInForceTo $script:Policy $script:V1 | Should -Be '2026-10-22T00:00:00Z'
    }

    It 'the superseded v2 ends at v3''s inForceFrom (D-192)' {
        Get-ExpectedInForceTo $script:Policy $script:V2 | Should -Be '2026-10-28T00:00:00Z'
    }

    It 'the current v3 has no end' {
        Get-ExpectedInForceTo $script:Policy $script:V3 | Should -BeNullOrEmpty
    }

    It 'must not fire: a single-version policy (the Do-lane definitions) never ends a version' {
        foreach ($p in (Read-Definition 'do-lane.policies.json').policies) {
            foreach ($v in $p.versions) { Get-ExpectedInForceTo $p $v | Should -BeNullOrEmpty -Because $p.code }
        }
    }
}

Describe 'Get-VersionEndAction / Get-InForceToDiff (what -Verify and the apply expect)' {
    BeforeAll {
        $script:Policy = (Read-Definition 'know-promotion.policies.json').policies | Where-Object code -eq 'POL-MATTER-NOBUDGET'
        $script:V1 = $script:Policy.versions | Where-Object versionNumber -eq 1
        $script:V2 = $script:Policy.versions | Where-Object versionNumber -eq 2
        $script:V3 = $script:Policy.versions | Where-Object versionNumber -eq 3
    }

    It 'must fire: v1 still open after v2 exists is the one allowed write (Stamp), and -Verify names it' {
        Get-VersionEndAction $script:Policy $script:V1 (New-LiveRow $null) | Should -Be 'Stamp'
        Get-InForceToDiff $script:Policy $script:V1 (New-LiveRow $null) | Should -Be "sprk_inforceto live='' expected='2026-10-22T00:00:00Z'"
    }

    It 'must fire: v2 still open after v3 exists is stamped with v3''s inForceFrom (D-192, the same supersede as v1 -> v2)' {
        Get-VersionEndAction $script:Policy $script:V2 (New-LiveRow $null) | Should -Be 'Stamp'
        Get-InForceToDiff $script:Policy $script:V2 (New-LiveRow $null) | Should -Be "sprk_inforceto live='' expected='2026-10-28T00:00:00Z'"
    }

    It 'must not fire: v1 already ended at the expected instant is in sync (a re-run changes nothing), as text or as a parsed date' {
        Get-VersionEndAction $script:Policy $script:V1 (New-LiveRow '2026-10-22T00:00:00Z') | Should -Be 'None'
        Get-VersionEndAction $script:Policy $script:V1 (New-LiveRow ([datetime]::SpecifyKind([datetime]'2026-10-22', 'Utc'))) | Should -Be 'None'
        Get-InForceToDiff $script:Policy $script:V1 (New-LiveRow '2026-10-22T00:00:00Z') | Should -BeNullOrEmpty
    }

    It 'must not fire: v2 already ended at v3''s inForceFrom is in sync, as text or as a parsed date' {
        Get-VersionEndAction $script:Policy $script:V2 (New-LiveRow '2026-10-28T00:00:00Z') | Should -Be 'None'
        Get-VersionEndAction $script:Policy $script:V2 (New-LiveRow ([datetime]::SpecifyKind([datetime]'2026-10-28', 'Utc'))) | Should -Be 'None'
        Get-InForceToDiff $script:Policy $script:V2 (New-LiveRow '2026-10-28T00:00:00Z') | Should -BeNullOrEmpty
    }

    It 'a different end on a superseded version is DRIFT, never rewritten' {
        Get-VersionEndAction $script:Policy $script:V1 (New-LiveRow '2026-10-23T00:00:00Z') | Should -Be 'Drift'
        Get-InForceToDiff $script:Policy $script:V1 (New-LiveRow '2026-10-23T00:00:00Z') | Should -Match "live='2026-10-23T00:00:00Z' expected='2026-10-22T00:00:00Z'"
        Get-VersionEndAction $script:Policy $script:V2 (New-LiveRow '2026-10-22T00:00:00Z') | Should -Be 'Drift'
    }

    It 'the current version with no end is in sync; with any end it is DRIFT (an ended current version is refused by the nightly job)' {
        Get-VersionEndAction $script:Policy $script:V3 (New-LiveRow $null) | Should -Be 'None'
        Get-VersionEndAction $script:Policy $script:V3 (New-LiveRow '2026-10-28T00:00:00Z') | Should -Be 'Drift'
        Get-InForceToDiff $script:Policy $script:V3 (New-LiveRow '2026-10-28T00:00:00Z') | Should -Match "expected=''"
    }

    It 'idempotent: the applied state (v1 and v2 ended, v3 open) needs no action on any version' {
        Get-VersionEndAction $script:Policy $script:V1 (New-LiveRow '2026-10-22T00:00:00Z') | Should -Be 'None'
        Get-VersionEndAction $script:Policy $script:V2 (New-LiveRow '2026-10-28T00:00:00Z') | Should -Be 'None'
        Get-VersionEndAction $script:Policy $script:V3 (New-LiveRow $null) | Should -Be 'None'
    }

    It 'a Do-lane policy (one version) is in sync open and DRIFT ended' {
        $p = (Read-Definition 'do-lane.policies.json').policies[0]
        Get-VersionEndAction $p $p.versions[0] (New-LiveRow $null) | Should -Be 'None'
        Get-VersionEndAction $p $p.versions[0] (New-LiveRow '2026-10-22T00:00:00Z') | Should -Be 'Drift'
    }
}

Describe 'Test-Definition (lint)' {
    It 'the checked-in definitions lint clean' {
        foreach ($file in 'do-lane.policies.json', 'know-promotion.policies.json') {
            @(Test-Definition (Read-Definition $file)) | Should -BeNullOrEmpty -Because $file
        }
    }

    It 'must fire: a current version that is not the newest is refused (the script only advances)' {
        $def = Read-Definition 'know-promotion.policies.json'
        $def.policies[0].currentVersion = 1
        (@(Test-Definition $def) -join '|') | Should -Match 'currentVersion must be the highest version number'
    }

    It 'must fire: a version that starts before the one it supersedes is refused' {
        $def = Read-Definition 'know-promotion.policies.json'
        ($def.policies[0].versions | Where-Object versionNumber -eq 2).inForceFrom = '2026-10-01T00:00:00Z'
        (@(Test-Definition $def) -join '|') | Should -Match 'v2 inForceFrom must be after v1 inForceFrom'
    }

    It 'must fire: two versions with the same inForceFrom are refused (strictly increasing)' {
        $def = Read-Definition 'know-promotion.policies.json'
        $v1 = $def.policies[0].versions | Where-Object versionNumber -eq 1
        ($def.policies[0].versions | Where-Object versionNumber -eq 2).inForceFrom = $v1.inForceFrom
        (@(Test-Definition $def) -join '|') | Should -Match 'v2 inForceFrom must be after v1 inForceFrom'
    }

    It 'must fire: v3 starting at or before v2 is refused (D-192; strictly increasing across all three versions)' {
        $def = Read-Definition 'know-promotion.policies.json'
        $v2 = $def.policies[0].versions | Where-Object versionNumber -eq 2
        ($def.policies[0].versions | Where-Object versionNumber -eq 3).inForceFrom = $v2.inForceFrom
        (@(Test-Definition $def) -join '|') | Should -Match 'v3 inForceFrom must be after v2 inForceFrom'
    }

    It 'must not fire: a later inForceFrom is accepted (the shipped v1 < v2 < v3)' {
        @(Test-Definition (Read-Definition 'know-promotion.policies.json')) | Should -BeNullOrEmpty
    }

    It 'must fire: a Next step that is not a catalog Next-step code is refused' {
        $def = Read-Definition 'know-promotion.policies.json'
        ($def.policies[0].versions | Where-Object versionNumber -eq 2).decisionPlan.nextSteps = @('mark-complete', 'bogus')
        $text = @(Test-Definition $def) -join '|'
        $text | Should -Match "Next step 'bogus' is not a catalog Next-step code"
        $text | Should -Match "Next step 'mark-complete' is not a catalog Next-step code"
    }

    It 'must not fire: the catalog Next-step codes are accepted, in either lane''s definition' {
        $def = Read-Definition 'know-promotion.policies.json'
        ($def.policies[0].versions | Where-Object versionNumber -eq 2).decisionPlan.nextSteps = @('add-todo', 'create-event', 'send-email', 'assign-work')
        @(Test-Definition $def) | Should -BeNullOrEmpty
        $do = Read-Definition 'do-lane.policies.json'
        $do.policies[0].versions[0].decisionPlan.nextSteps = @('add-todo')
        @(Test-Definition $do) | Should -BeNullOrEmpty
    }

    It 'the Decide-lane lint accepts assign-task (the v2 plan), and still refuses a Do-lane code in a Decide plan' {
        $def = Read-Definition 'know-promotion.policies.json'
        @(Test-Definition $def) | Should -BeNullOrEmpty
        ($def.policies[0].versions | Where-Object versionNumber -eq 2).decisionPlan.actions = @('mark-complete')
        (@(Test-Definition $def) -join '|') | Should -Match "names 'mark-complete', not a Decide-lane catalog code"
    }

    It 'the shipped v3 plan is revise-budget then assign-task with add-todo and assign-work as Next steps, lints clean, and never names create-budget (D-192, D-180)' {
        $def = Read-Definition 'know-promotion.policies.json'
        $p = $def.policies[0]
        $p.currentVersion | Should -Be 3
        $v3 = $p.versions | Where-Object versionNumber -eq 3
        $v3.versionId | Should -Be '00000000-0000-4000-8000-000000006313'
        ($v3.decisionPlan.actions -join ',') | Should -Be 'revise-budget,assign-task'
        ($v3.decisionPlan.nextSteps -join ',') | Should -Be 'add-todo,assign-work'
        ($v3.decisionPlan | ConvertTo-Json -Compress) | Should -Not -Match 'create-budget'
        @(Test-Definition $def) | Should -BeNullOrEmpty
    }

    It 'must fire: a v3 plan that authors create-budget is refused (D-180, the lint does not list it as a Decide-lane code)' {
        $def = Read-Definition 'know-promotion.policies.json'
        ($def.policies[0].versions | Where-Object versionNumber -eq 3).decisionPlan.actions = @('create-budget', 'assign-task')
        (@(Test-Definition $def) -join '|') | Should -Match "names 'create-budget', not a Decide-lane catalog code"
    }
}

Describe 'The apply writes the end of a version once, between creating versions and moving the pointer' {
    It 'the only PATCH of a sprk_policyversion row is the sprk_inforceto end' {
        ([regex]::Matches($script:ScriptText, 'Invoke-Dv PATCH "\$ApiBase/sprk_policyversions')).Count | Should -Be 1
        $script:ScriptText | Should -Match '(?s)Invoke-Dv PATCH "\$ApiBase/sprk_policyversions\(\$\(\$v\.versionId\)\)" @\{ sprk_inforceto = \$expectedEnd \}'
    }

    It 'create, then end, then move the pointer (the product''s publish order)' {
        $create = $script:ScriptText.IndexOf('Invoke-Dv POST "$ApiBase/sprk_policyversions"')
        $end = $script:ScriptText.IndexOf('Invoke-Dv PATCH "$ApiBase/sprk_policyversions')
        $move = $script:ScriptText.IndexOf("'sprk_CurrentVersion@odata.bind'")
        $create | Should -BeGreaterThan 0
        $end | Should -BeGreaterThan $create
        $move | Should -BeGreaterThan $end
    }
}

# ---- Task 171 (D-197): -CustomerBusinessUnit ----------------------------------------------------------------------------------
# With -CustomerBusinessUnit the script owns every policy and version row it CREATES to the memberless restricted-content owner team
# of the customer's top business unit (the rule of Infrastructure/Dataverse/RestrictedContentOwnerTeam.cs). These tests run the WHOLE
# script, exit codes included, in a child pwsh against a test double of the Web API (Apply-DoLanePolicies.Harness.ps1), so they check
# what the script sends and what it refuses; none of them reaches an environment. The dev reference team (not contacted) is
# "Spaarke Restricted Content Owners", 16685363-43d3-f111-84e8-00224820fd20, in BU1.

Describe 'Owner of the rows the script creates (-CustomerBusinessUnit, task 171, D-197)' {
    BeforeAll {
        $script:Pwsh = (Get-Process -Id $PID).Path
        $script:Harness = Join-Path $PSScriptRoot 'Apply-DoLanePolicies.Harness.ps1'
        $script:TeamName = 'Spaarke Restricted Content Owners'
        $script:Ids = @{
            Root = '00000000-0000-4000-8000-0000000b0001'; Customer = '00000000-0000-4000-8000-0000000b0002'
            Secure = '00000000-0000-4000-8000-0000000b0003'; Child = '00000000-0000-4000-8000-0000000b0004'
            Other = '00000000-0000-4000-8000-0000000b0005'; Team = '16685363-43d3-f111-84e8-00224820fd20'
            Team2 = '16685363-43d3-f111-84e8-00224820fd21'; User = '00000000-0000-4000-8000-0000000c0001'
            Stranger = '00000000-0000-4000-8000-0000000c0002'; Missing = '00000000-0000-4000-8000-0000000b00ff'
            AssignPolicy = '00000000-0000-4000-8000-0000000d0001'; AssignVersion = '00000000-0000-4000-8000-0000000d0002'
        }

        # The live option labels every checked-in definition uses (the lint reads them from the environment).
        $script:Options = @{ sprk_lane = @{}; sprk_subjecttype = @{}; sprk_worktype = @{}; sprk_severity = @{}; sprk_ruletype = @{} }
        $script:Options.sprk_lane['Decide'] = 100000000; $script:Options.sprk_lane['Do'] = 100000001
        foreach ($file in 'do-lane.policies.json', 'know-promotion.policies.json') {
            foreach ($p in (Read-Definition $file).policies) {
                $script:Options.sprk_subjecttype[$p.subjectType] = 1; $script:Options.sprk_worktype[$p.workType] = 1; $script:Options.sprk_severity[$p.severity] = 1
                foreach ($v in $p.versions) { $script:Options.sprk_ruletype[$v.ruleType] = 1 }
            }
        }
        $script:DoLane = Read-Definition 'do-lane.policies.json'
        $script:VersionCount = @($script:DoLane.policies | ForEach-Object { $_.versions }).Count

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
        function Invoke-ApplyScript([hashtable]$Scenario, [hashtable]$ScriptArgs, [string]$ScriptPath = $script:ScriptPath) {
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

        function Get-Writes($Run) { @($Run.Requests | Where-Object { $_.Method -ne 'GET' }) }
        function Get-Creates($Run, [string]$Table) { @($Run.Requests | Where-Object { $_.Method -eq 'POST' -and $_.Uri -like "*/$Table" }) }
        $script:OwnerBind = "/teams($($script:Ids.Team))"
    }

    Context 'with the parameter: the apply' {
        BeforeAll {
            $script:Applied = Invoke-ApplyScript (New-Scenario) @{ CustomerBusinessUnit = $script:Ids.Customer }
        }

        It 'applies cleanly and the read-back passes' {
            $script:Applied.ExitCode | Should -Be 0 -Because $script:Applied.Output
            $script:Applied.Output | Should -Match 'READ-BACK OK: every policy and version equals the definition\.'
            $script:Applied.Output | Should -Match "Owner: created rows belong to team 'Spaarke Restricted Content Owners' \($($script:Ids.Team)\)"
        }

        It 'must fire: every sprk_policy create carries ownerid@odata.bind to the team' {
            $creates = Get-Creates $script:Applied 'sprk_policies'
            $creates.Count | Should -Be @($script:DoLane.policies).Count
            foreach ($c in $creates) { ($c.Body | ConvertFrom-Json).'ownerid@odata.bind' | Should -Be $script:OwnerBind }
        }

        It 'must fire: every sprk_policyversion create carries ownerid@odata.bind to the team' {
            $creates = Get-Creates $script:Applied 'sprk_policyversions'
            $creates.Count | Should -Be $script:VersionCount
            foreach ($c in $creates) { ($c.Body | ConvertFrom-Json).'ownerid@odata.bind' | Should -Be $script:OwnerBind }
        }

        It 'the created rows read back owned by the team, not the operator' {
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
            $run = Invoke-ApplyScript (New-Scenario) @{ CustomerBusinessUnit = 'Contoso Legal' }
            $run.ExitCode | Should -Be 0 -Because $run.Output
            foreach ($c in (Get-Creates $run 'sprk_policies') + (Get-Creates $run 'sprk_policyversions')) { ($c.Body | ConvertFrom-Json).'ownerid@odata.bind' | Should -Be $script:OwnerBind }
        }

        It 'must fire: -WhatIf shows the owner it would bind, and writes nothing' {
            $run = Invoke-ApplyScript (New-Scenario) @{ CustomerBusinessUnit = $script:Ids.Customer; WhatIf = $true }
            $run.ExitCode | Should -Be 0 -Because $run.Output
            Get-Writes $run | Should -BeNullOrEmpty
            $run.Output | Should -Match "owner = team $($script:Ids.Team)"
            $run.Output | Should -Match ([regex]::Escape("would write ownerid = /teams($($script:Ids.Team))"))
        }

        It 'a refusal is a refusal in -WhatIf too (a missing team stops the run)' {
            $s = New-Scenario; $s.teams = @()
            $run = Invoke-ApplyScript $s @{ CustomerBusinessUnit = $script:Ids.Customer; WhatIf = $true }
            $run.ExitCode | Should -Not -Be 0
            $run.Output | Should -Match 'OwnerTeamNotFound'
        }
    }

    Context 'must not fire: without the parameter nothing changes' {
        BeforeAll {
            $script:Plain = Invoke-ApplyScript (New-Scenario) @{}
            $script:PlainWhatIf = Invoke-ApplyScript (New-Scenario) @{ WhatIf = $true }
        }

        It 'applies cleanly' {
            $script:Plain.ExitCode | Should -Be 0 -Because $script:Plain.Output
            $script:Plain.Output | Should -Match 'READ-BACK OK: every policy and version equals the definition\.'
        }

        It 'no create carries an owner, and the rows are the operator''s (as before)' {
            foreach ($c in (Get-Creates $script:Plain 'sprk_policies') + (Get-Creates $script:Plain 'sprk_policyversions')) { $c.Body | Should -Not -Match 'ownerid' }
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
            $run = Invoke-ApplyScript $c.Scenario @{ CustomerBusinessUnit = $c.Arg }
            $run.ExitCode | Should -Not -Be 0 -Because $run.Output
            $run.Output | Should -Match ([regex]::Escape($Expect))
            $run.Output | Should -Match 'Nothing was written'
            Get-Writes $run | Should -BeNullOrEmpty -Because 'a refusal happens before any write'
        }
    }

    Context 'existing rows are never re-owned; -Verify reports owner drift and never writes' {
        BeforeAll {
            # Rows the plain (operator-owned) script created, as a customer environment would hold them before this task.
            $script:OperatorOwned = $script:Plain.Db
            $script:VerifyArgs = @{ CustomerBusinessUnit = $script:Ids.Customer; Verify = $true }
        }

        It 'must fire: -Verify with the parameter reports every row owned by the operator as DRIFT, exit 1, no write' {
            $run = Invoke-ApplyScript (Copy-Scenario $script:OperatorOwned) $script:VerifyArgs
            $run.ExitCode | Should -Be 1 -Because $run.Output
            Get-Writes $run | Should -BeNullOrEmpty
            $expected = "ownerid live='$($script:Ids.User)' expected='$($script:Ids.Team)'"
            ([regex]::Matches($run.Output, [regex]::Escape($expected))).Count | Should -Be (@($script:DoLane.policies).Count + $script:VersionCount)
            $run.Output | Should -Match 'policy DRIFT: ownerid'
            $run.Output | Should -Match 'v1 DRIFT: ownerid'
        }

        It 'must fire: one row owned by someone else is the only DRIFT' {
            $s = Copy-Scenario $script:Applied.Db
            ($s.versions | Select-Object -First 1)._ownerid_value = $script:Ids.Stranger
            $run = Invoke-ApplyScript $s $script:VerifyArgs
            $run.ExitCode | Should -Be 1 -Because $run.Output
            ([regex]::Matches($run.Output, 'DRIFT')).Count | Should -Be 1
            $run.Output | Should -Match "ownerid live='$($script:Ids.Stranger)' expected='$($script:Ids.Team)'"
        }

        It 'rows owned by the team are IN SYNC, exit 0, and -Verify does not ask for Assign (it never writes)' {
            $s = Copy-Scenario $script:Applied.Db
            $s.userPrivilegeIds = @()
            $run = Invoke-ApplyScript $s $script:VerifyArgs
            $run.ExitCode | Should -Be 0 -Because $run.Output
            $run.Output | Should -Not -Match 'DRIFT'
            ($run.Requests | ForEach-Object { $_.Uri }) -join "`n" | Should -Not -Match 'RetrieveUserPrivileges'
            Get-Writes $run | Should -BeNullOrEmpty
        }

        It 'must not fire: -Verify without the parameter ignores the owner (operator-owned rows are IN SYNC)' {
            $run = Invoke-ApplyScript (Copy-Scenario $script:OperatorOwned) @{ Verify = $true }
            $run.ExitCode | Should -Be 0 -Because $run.Output
            $run.Output | Should -Not -Match 'DRIFT|owner'
        }

        It 'must not fire: -Verify with the parameter still refuses an unusable team (a fail-closed case reads the same in every mode)' {
            $s = Copy-Scenario $script:OperatorOwned; $s.teams = @()
            $run = Invoke-ApplyScript $s $script:VerifyArgs
            $run.ExitCode | Should -Not -Be 0
            $run.Output | Should -Match 'OwnerTeamNotFound'
        }

        It 'must not fire: an apply with the parameter over existing operator-owned rows writes nothing and re-owns nothing' {
            $run = Invoke-ApplyScript (Copy-Scenario $script:OperatorOwned) @{ CustomerBusinessUnit = $script:Ids.Customer }
            $run.ExitCode | Should -Be 0 -Because $run.Output
            Get-Writes $run | Should -BeNullOrEmpty
            foreach ($r in @($run.Db.policies) + @($run.Db.versions)) { $r._ownerid_value | Should -Be $script:Ids.User }
        }
    }

    Context 'F1: a parameter that was passed empty is a refusal, never "not passed"' {
        It '<Mode> with <Label> stops before any request, non-zero' -ForEach @(
            foreach ($mode in 'apply', 'WhatIf', 'Verify') { foreach ($v in @(@{ Label = 'empty'; Value = '' }, @{ Label = 'null'; Value = $null }, @{ Label = 'blank'; Value = '   ' })) { @{ Mode = $mode; Label = $v.Label; Value = $v.Value } } }
        ) {
            $a = @{ CustomerBusinessUnit = $Value }
            if ($Mode -ne 'apply') { $a[$Mode] = $true }
            $run = Invoke-ApplyScript (New-Scenario) $a
            $run.ExitCode | Should -Not -Be 0 -Because $run.Output
            $run.Output | Should -Match 'CustomerBusinessUnit was passed but is empty'
            $run.Requests.Count | Should -Be 0
        }

        It 'must not fire: leaving the parameter out is still the plain path (exit 0, operator-owned rows)' {
            $script:Plain.ExitCode | Should -Be 0
            $script:Plain.Output | Should -Not -Match 'CustomerBusinessUnit'
        }
    }

    Context 'F2: the read-back holds created rows to the owner' {
        It 'must fire: a service that drops the owner bind fails the read-back, non-zero' {
            $s = New-Scenario; $s.ignoreOwnerBind = $true
            $run = Invoke-ApplyScript $s @{ CustomerBusinessUnit = $script:Ids.Customer }
            $run.ExitCode | Should -Not -Be 0 -Because $run.Output
            $run.Output | Should -Match "READ-BACK FAIL: policy POL-\S+: ownerid live='$($script:Ids.User)' expected='$($script:Ids.Team)'"
            $run.Output | Should -Match "READ-BACK FAIL: version POL-\S+ v1: ownerid live='$($script:Ids.User)'"
        }

        It 'must not fire: without the parameter the same service behaviour is not judged' {
            $s = New-Scenario; $s.ignoreOwnerBind = $true
            (Invoke-ApplyScript $s @{}).ExitCode | Should -Be 0
        }
    }

    Context 'F2: a fault after the policy was created, then a rerun' {
        It 'leaves the policy owned by the team, and the rerun completes without a duplicate' {
            $s = New-Scenario; $s.failNextPost = 'sprk_policyversions'
            $first = Invoke-ApplyScript $s @{ CustomerBusinessUnit = $script:Ids.Customer }
            $first.ExitCode | Should -Not -Be 0 -Because $first.Output
            $first.Output | Should -Match 'injected fault'
            @($first.Db.policies).Count | Should -Be 1
            @($first.Db.policies)[0]._ownerid_value | Should -Be $script:Ids.Team
            @($first.Db.versions).Count | Should -Be 0

            $s2 = Copy-Scenario $first.Db; $s2.failNextPost = $null
            $again = Invoke-ApplyScript $s2 @{ CustomerBusinessUnit = $script:Ids.Customer }
            $again.ExitCode | Should -Be 0 -Because $again.Output
            (Get-Creates $again 'sprk_policies').Count | Should -Be (@($script:DoLane.policies).Count - 1)
            @($again.Db.policies).Count | Should -Be @($script:DoLane.policies).Count
            @($again.Db.policies | ForEach-Object { $_.sprk_policyid } | Select-Object -Unique).Count | Should -Be @($script:DoLane.policies).Count
            @($again.Db.versions).Count | Should -Be $script:VersionCount
            foreach ($r in @($again.Db.policies) + @($again.Db.versions)) { $r._ownerid_value | Should -Be $script:Ids.Team }
        }
    }

    Context 'F2: an existing operator-owned policy with a new team-owned version' {
        It 'the apply owns only the new version; -Verify then reports the policy as DRIFT and the version IN SYNC' {
            $s = Copy-Scenario $script:Plain.Db
            $first = $script:DoLane.policies[0]
            $s.versions = @($s.versions | Where-Object { $_.sprk_policyversionid -ne $first.versions[0].versionId })
            $apply = Invoke-ApplyScript $s @{ CustomerBusinessUnit = $script:Ids.Customer }
            $apply.ExitCode | Should -Be 0 -Because $apply.Output
            (Get-Creates $apply 'sprk_policies').Count | Should -Be 0
            $created = @(Get-Creates $apply 'sprk_policyversions')
            $created.Count | Should -Be 1
            ($created[0].Body | ConvertFrom-Json).'ownerid@odata.bind' | Should -Be $script:OwnerBind
            foreach ($r in @($apply.Db.policies)) { $r._ownerid_value | Should -Be $script:Ids.User }

            $verify = Invoke-ApplyScript (Copy-Scenario $apply.Db) @{ CustomerBusinessUnit = $script:Ids.Customer; Verify = $true }
            $verify.ExitCode | Should -Be 1 -Because $verify.Output
            $verify.Output | Should -Match "$($first.code)\s+policy DRIFT: ownerid"
            $verify.Output | Should -Match "$($first.code)\s+v1 IN SYNC"
        }
    }

    Context 'F2: the Secure Record unit and the parent-chain bound' {
        It 'an environment with no Secure Record unit proceeds (none is it)' {
            $s = New-Scenario
            $s.businessUnits = @($s.businessUnits | Where-Object { $_.businessunitid -ne $script:Ids.Secure })
            $run = Invoke-ApplyScript $s @{ CustomerBusinessUnit = $script:Ids.Customer }
            $run.ExitCode | Should -Be 0 -Because $run.Output
            (Get-Creates $run 'sprk_policies').Count | Should -Be @($script:DoLane.policies).Count
        }

        # The walk reads one unit per step, the root included, for MaxParentWalk + 1 = 33 steps (RestrictedContentOwnerTeam.cs).
        It 'a chain whose root is the 33rd read is still read to the root (32 units: the unit is just not top level)' {
            $run = Invoke-ApplyScript (New-ChainScenario 32) @{ CustomerBusinessUnit = (Get-ChainId 0) }
            $run.ExitCode | Should -Not -Be 0
            $run.Output | Should -Match 'CustomerBusinessUnitNotTopLevel'
        }

        It 'the top unit of that 32-unit chain resolves (no chain refusal; the run goes on to find no team)' {
            $s = New-ChainScenario 32
            $run = Invoke-ApplyScript $s @{ CustomerBusinessUnit = (Get-ChainId 31) }
            $run.Output | Should -Not -Match 'ParentChain'
            $run.Output | Should -Match 'OwnerTeamNotFound'
        }

        It 'one more unit and the root is past the bound: refused as a loop, though nothing repeats (33 units)' {
            $run = Invoke-ApplyScript (New-ChainScenario 33) @{ CustomerBusinessUnit = (Get-ChainId 0) }
            $run.ExitCode | Should -Not -Be 0
            $run.Output | Should -Match 'ParentChainLoops'
        }
    }
}

Describe 'The owner rule is the C# rule (task 171, D-197)' {
    BeforeAll {
        $errors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile($script:ScriptPath, [ref]$null, [ref]$errors)
        foreach ($variable in 'RestrictedOwnerTeamName', 'SecureBusinessUnitName', 'MaxParentWalk', 'MemberReadLimit', 'AssignPrivileges') {
            $assign = $ast.Find({ param($n) $n -is [System.Management.Automation.Language.AssignmentStatementAst] -and "$($n.Left)" -eq "`$$variable" }, $false)
            if (-not $assign) { throw "The script no longer defines `$$variable" }
            . ([scriptblock]::Create($assign.Extent.Text))
        }
        foreach ($name in 'Get-OwnerDiff', 'ConvertTo-ODataLiteral') {
            $fn = $ast.Find({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name }, $true)
            if (-not $fn) { throw "The script no longer defines $name" }
            . ([scriptblock]::Create($fn.Extent.Text))
        }
    }

    It 'the script''s team rule is pinned to the class source (fails in CI when the classes are not checked out)' {
        $dir = Join-Path $script:RepoRoot 'src' 'server' 'api' 'Sprk.Bff.Api' 'Infrastructure' 'Dataverse'
        $missing = @('RestrictedContentOwnerTeam.cs', 'SecureRecordOwnerTeam.cs' | Where-Object { -not (Test-Path -LiteralPath (Join-Path $dir $_)) })
        if ($missing.Count -gt 0) {
            if ($env:CI) { throw "CI: $($missing -join ', ') missing from the checkout; add it to the sparse-checkout of ontology-script-tests." }
            Set-ItResult -Skipped -Because "$($missing -join ', ') is not in this checkout"
            return
        }
        $restricted = Get-Content -LiteralPath (Join-Path $dir 'RestrictedContentOwnerTeam.cs') -Raw
        $secure = Get-Content -LiteralPath (Join-Path $dir 'SecureRecordOwnerTeam.cs') -Raw
        $text = $script:ScriptText

        # names and limits
        [regex]::Match($restricted, 'DefaultOwnerTeamName = "([^"]+)"').Groups[1].Value | Should -Be $RestrictedOwnerTeamName
        [regex]::Match($secure, 'DefaultBusinessUnitName = "([^"]+)"').Groups[1].Value | Should -Be $SecureBusinessUnitName
        [int][regex]::Match($restricted, 'MaxParentWalk = (\d+)').Groups[1].Value | Should -Be $MaxParentWalk
        [int][regex]::Match($restricted, 'MemberReadLimit = (\d+)').Groups[1].Value | Should -Be $MemberReadLimit

        # the team query: Owner team type, not default, in the unit, named; TOP 2
        $ownerType = [int][regex]::Match($secure, 'OwnerTeamType = (\d+)').Groups[1].Value
        $ownerType | Should -Be 0
        foreach ($pair in @(@('NameColumn', 'name'), @('TeamTypeColumn', 'teamtype'), @('IsDefaultColumn', 'isdefault'), @('BusinessUnitColumn', 'businessunitid'))) {
            $secure | Should -Match "$($pair[0]) = `"$($pair[1])`""
        }
        $restricted | Should -Match 'AddCondition\(SecureRecordOwnerTeam\.NameColumn, ConditionOperator\.Equal, OwnerTeamName\(configuration\)\)'
        $restricted | Should -Match 'AddCondition\(SecureRecordOwnerTeam\.TeamTypeColumn, ConditionOperator\.Equal, SecureRecordOwnerTeam\.OwnerTeamType\)'
        $restricted | Should -Match 'AddCondition\(SecureRecordOwnerTeam\.IsDefaultColumn, ConditionOperator\.Equal, false\)'
        $restricted | Should -Match 'AddCondition\(SecureRecordOwnerTeam\.BusinessUnitColumn, ConditionOperator\.Equal, topBusinessUnitId\)'
        $restricted | Should -Match '(?s)ColumnSet = new ColumnSet\("teamid"\),\s*TopCount = 2,'
        $text | Should -Match ([regex]::Escape("teamtype eq $ownerType and isdefault eq false and _businessunitid_value eq `$top&") + [regex]::Escape('`$top=2'))
        # the member read, the parent walk and the empty-id rule
        $restricted | Should -Match 'TopCount = MemberReadLimit'
        $text | Should -Match ([regex]::Escape('teammemberships?`$select=systemuserid&`$filter=teamid eq $teamId&`$top=$MemberReadLimit'))
        $restricted | Should -Match 'new QueryExpression\("businessunit"\) \{ ColumnSet = new ColumnSet\("parentbusinessunitid"\), TopCount = 1'
        $text | Should -Match ([regex]::Escape('businessunitid eq $current&`$top=1'))
        $restricted | Should -Match 'teams\[0\]\.Id == Guid\.Empty'
        $text | Should -Match ([regex]::Escape('[guid]::Empty.ToString()) { return'))
        # the Secure Record unit lookup: by name, TOP 2
        $secure | Should -Match 'new QueryExpression\("businessunit"\) \{ ColumnSet = new ColumnSet\("businessunitid"\), TopCount = 2'
        $text | Should -Match ([regex]::Escape('$SecureBusinessUnitName)''&`$top=2'))
    }

    It 'the Assign privileges are the two tables the script creates rows in' {
        $AssignPrivileges | Should -Be @('prvAssignsprk_policy', 'prvAssignsprk_policyversion')
    }

    It 'Get-OwnerDiff: no owner expected (no parameter) is never a difference' {
        $script:OwnerTeamId = $null
        Get-OwnerDiff ([pscustomobject]@{ _ownerid_value = 'someone' }) | Should -BeNullOrEmpty
    }

    It 'Get-OwnerDiff: the team (any case) is in sync; anyone else, or nobody, is a difference' {
        $script:OwnerTeamId = '16685363-43d3-f111-84e8-00224820fd20'
        Get-OwnerDiff ([pscustomobject]@{ _ownerid_value = '16685363-43D3-F111-84E8-00224820FD20' }) | Should -BeNullOrEmpty
        Get-OwnerDiff ([pscustomobject]@{ _ownerid_value = 'someone' }) | Should -Be "ownerid live='someone' expected='16685363-43d3-f111-84e8-00224820fd20'"
        Get-OwnerDiff ([pscustomobject]@{ _ownerid_value = $null }) | Should -Match "ownerid live=''"
        $script:OwnerTeamId = $null
    }

    It 'ConvertTo-ODataLiteral doubles an apostrophe and URL-escapes the rest' {
        ConvertTo-ODataLiteral "O'Brien & Co" | Should -Be "O%27%27Brien%20%26%20Co"
    }
}
