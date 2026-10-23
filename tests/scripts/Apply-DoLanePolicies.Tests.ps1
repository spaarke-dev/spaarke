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

    foreach ($variable in 'KindRules', 'MaxHeadline', 'MaxProposedAction', 'MaxShortName') {
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
    }

    It 'the superseded v1 ends at v2''s inForceFrom' {
        Get-ExpectedInForceTo $script:Policy $script:V1 | Should -Be '2026-10-22T00:00:00Z'
    }

    It 'the current v2 has no end' {
        Get-ExpectedInForceTo $script:Policy $script:V2 | Should -BeNullOrEmpty
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
    }

    It 'must fire: v1 still open after v2 exists is the one allowed write (Stamp), and -Verify names it' {
        Get-VersionEndAction $script:Policy $script:V1 (New-LiveRow $null) | Should -Be 'Stamp'
        Get-InForceToDiff $script:Policy $script:V1 (New-LiveRow $null) | Should -Be "sprk_inforceto live='' expected='2026-10-22T00:00:00Z'"
    }

    It 'must not fire: v1 already ended at the expected instant is in sync (a re-run changes nothing), as text or as a parsed date' {
        Get-VersionEndAction $script:Policy $script:V1 (New-LiveRow '2026-10-22T00:00:00Z') | Should -Be 'None'
        Get-VersionEndAction $script:Policy $script:V1 (New-LiveRow ([datetime]::SpecifyKind([datetime]'2026-10-22', 'Utc'))) | Should -Be 'None'
        Get-InForceToDiff $script:Policy $script:V1 (New-LiveRow '2026-10-22T00:00:00Z') | Should -BeNullOrEmpty
    }

    It 'a different end on a superseded version is DRIFT, never rewritten' {
        Get-VersionEndAction $script:Policy $script:V1 (New-LiveRow '2026-10-23T00:00:00Z') | Should -Be 'Drift'
        Get-InForceToDiff $script:Policy $script:V1 (New-LiveRow '2026-10-23T00:00:00Z') | Should -Match "live='2026-10-23T00:00:00Z' expected='2026-10-22T00:00:00Z'"
    }

    It 'the current version with no end is in sync; with any end it is DRIFT (an ended current version is refused by the nightly job)' {
        Get-VersionEndAction $script:Policy $script:V2 (New-LiveRow $null) | Should -Be 'None'
        Get-VersionEndAction $script:Policy $script:V2 (New-LiveRow '2026-10-22T00:00:00Z') | Should -Be 'Drift'
        Get-InForceToDiff $script:Policy $script:V2 (New-LiveRow '2026-10-22T00:00:00Z') | Should -Match "expected=''"
    }

    It 'idempotent: the applied state (v1 ended, v2 open) needs no action on either version' {
        Get-VersionEndAction $script:Policy $script:V1 (New-LiveRow '2026-10-22T00:00:00Z') | Should -Be 'None'
        Get-VersionEndAction $script:Policy $script:V2 (New-LiveRow $null) | Should -Be 'None'
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
        (@(Test-Definition $def) -join '|') | Should -Match 'v2 inForceFrom is before v1 inForceFrom'
    }

    It 'the Decide-lane lint accepts assign-task (the v2 plan), and still refuses a Do-lane code in a Decide plan' {
        $def = Read-Definition 'know-promotion.policies.json'
        @(Test-Definition $def) | Should -BeNullOrEmpty
        ($def.policies[0].versions | Where-Object versionNumber -eq 2).decisionPlan.actions = @('mark-complete')
        (@(Test-Definition $def) -join '|') | Should -Match "names 'mark-complete', not a Decide-lane catalog code"
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
