# Pester 5. Run: Invoke-Pester tests/scripts/Deploy-Release.OntologyConfig.Tests.ps1
# Ontology task 164 (D-185, #2285): scripts/Deploy-Release.ps1 applies and verifies the worklist grid configuration
# (scripts/Apply-WorklistGridConfiguration.ps1) after Phase 2 (BFF) and before Phase 3 (SpaarkeMaster import), because the
# SpaarkeMaster import carries the role change that removes policy Read. If roles land before the configuration, every user
# gets an empty worklist. A configuration failure for an environment therefore blocks Phase 3 for that environment, even
# without -StopOnFailure; an explicit -SkipPhase OntologyConfig does not.
#
# Deploy-Release.ps1 deploys on load, so it is never run here. The phase function is read out of the script's own AST and
# run against a fake Apply script (no Dataverse, no az). The phase order and the Phase 3 gate are checked on the AST.

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:ScriptPath = Join-Path $script:RepoRoot 'scripts' 'Deploy-Release.ps1'
    $script:ScriptText = Get-Content -LiteralPath $script:ScriptPath -Raw
    $errors = $null
    $script:Ast = [System.Management.Automation.Language.Parser]::ParseFile($script:ScriptPath, [ref]$null, [ref]$errors)
    if ($errors.Count -gt 0) { throw "The script does not parse: $($errors[0].Message)" }

    # The output helpers the phase function uses (the real ones only write to the host).
    function Write-Step { param([string]$Message) }
    function Write-Ok { param([string]$Message) }
    function Write-Warn { param([string]$Message) }
    function Write-Fail { param([string]$Message) $script:FailMessages += $Message }

    $fn = $script:Ast.Find({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Invoke-OntologyConfigPhase' }, $true)
    if ($fn) { . ([scriptblock]::Create($fn.Extent.Text)) }
    $script:PhaseFunction = $fn

    # A stand-in for Apply-WorklistGridConfiguration.ps1: records how it was called; exits as the test says.
    function New-FakeApply([string]$Dir, [int]$ApplyExit = 0, [int]$VerifyExit = 0, [switch]$ApplyThrows) {
        $log = Join-Path $Dir 'calls.log'
        $path = Join-Path $Dir 'Apply-WorklistGridConfiguration.ps1'
        $throws = if ($ApplyThrows) { '$true' } else { '$false' }
        @"
[CmdletBinding(SupportsShouldProcess)]
param([string]`$DataverseUrl, [switch]`$Verify, [switch]`$RequireRow)
Add-Content -WhatIf:`$false -LiteralPath '$log' -Value ("url={0};verify={1};requireRow={2};whatIf={3}" -f `$DataverseUrl, [bool]`$Verify, [bool]`$RequireRow, [bool]`$WhatIfPreference)
if (`$Verify) { exit $VerifyExit }
if ($throws) { throw 'apply blew up' }
exit $ApplyExit
"@ | Set-Content -LiteralPath $path -Encoding utf8
        $path
    }
    function Get-Calls([string]$Dir) {
        $log = Join-Path $Dir 'calls.log'
        if (Test-Path -LiteralPath $log) { @(Get-Content -LiteralPath $log) } else { @() }
    }
}

Describe 'Deploy-Release.ps1 declares the phase' {
    It 'defines Invoke-OntologyConfigPhase' {
        $script:PhaseFunction | Should -Not -BeNullOrEmpty
    }

    It 'OntologyConfig is in the -SkipPhase ValidateSet' {
        $param = $script:Ast.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'SkipPhase' }
        $validate = $param.Attributes | Where-Object { $_.TypeName.Name -eq 'ValidateSet' }
        $values = $validate.PositionalArguments | ForEach-Object { $_.Value }
        $values | Should -Contain 'OntologyConfig'
        # the existing phases are still there
        $values | Should -Contain 'BffApi'
        $values | Should -Contain 'Solutions'
    }
}

Describe 'the phase sits between Phase 2 and Phase 3 in the per-environment loop' {
    BeforeAll {
        $script:Loop = $script:Ast.Find({ param($n) $n -is [System.Management.Automation.Language.ForStatementAst] -and $n.Extent.Text -match 'targetEnvironments\.Count' -and $n.Extent.Text -match 'Phase 3' }, $true)
        $script:Call = if ($script:Loop) {
            $script:Loop.Find({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Invoke-OntologyConfigPhase' }, $true)
        }
    }

    It 'calls Invoke-OntologyConfigPhase inside the per-environment loop' {
        $script:Loop | Should -Not -BeNullOrEmpty
        $script:Call | Should -Not -BeNullOrEmpty
    }

    It 'runs after the Phase 2 (BFF) block and before the Phase 3 (SpaarkeMaster) block' {
        $bff = $script:ScriptText.IndexOf('Write-Phase "Phase 2" "BFF API')
        $call = $script:Call.Extent.StartOffset
        $solutions = $script:ScriptText.IndexOf('Write-Phase "Phase 3" "SpaarkeMaster (')
        $bff | Should -BeGreaterThan 0
        $call | Should -BeGreaterThan $bff
        $solutions | Should -BeGreaterThan $call
    }

    It 'passes the environment''s DataverseUrl and the common -WhatIf preference' {
        $text = $script:Call.Extent.Text
        $text | Should -Match 'env\.DataverseUrl'
        $text | Should -Match '\$WhatIfPreference'
    }

    It 'resets the failure flag at the top of each environment (one environment''s failure never blocks the next)' {
        $reset = $script:Loop.Find({ param($n) $n -is [System.Management.Automation.Language.AssignmentStatementAst] -and "$($n.Left)" -eq '$ontologyConfigFailed' -and "$($n.Right)" -match 'false' }, $true)
        $reset | Should -Not -BeNullOrEmpty
        $reset.Extent.StartOffset | Should -BeLessThan $script:Call.Extent.StartOffset
    }

    It 'is named Phase 2b' {
        $script:ScriptText | Should -Match 'Phase 2b'
    }
}

Describe 'Phase 3 is gated on the configuration phase (D-185 fail-closed)' {
    BeforeAll {
        $script:Loop = $script:Ast.Find({ param($n) $n -is [System.Management.Automation.Language.ForStatementAst] -and $n.Extent.Text -match 'targetEnvironments\.Count' -and $n.Extent.Text -match 'Phase 3' }, $true)
        # The outermost if / elseif chain that holds the Phase 3 import.
        $script:Phase3 = @($script:Loop.FindAll({ param($n) $n -is [System.Management.Automation.Language.IfStatementAst] -and $n.Extent.Text -match 'Import-SpaarkeMasterPackage' }, $true)) |
            Sort-Object { $_.Extent.Text.Length } -Descending | Select-Object -First 1
        $script:Call = $script:Loop.Find({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Invoke-OntologyConfigPhase' }, $true)
    }

    It 'the first branch of the Phase 3 chain tests the failure flag, with no -StopOnFailure term' {
        $script:Phase3 | Should -Not -BeNullOrEmpty
        $first = $script:Phase3.Clauses[0].Item1.Extent.Text
        $first | Should -Match '\$ontologyConfigFailed'
        $first | Should -Not -Match 'StopOnFailure'
    }

    It 'the gate says why: the import carries the D-185 role change and needs the worklist configuration first' {
        $body = $script:Phase3.Clauses[0].Item2.Extent.Text
        $body | Should -Match 'SpaarkeMaster import carries the D-185 role change and needs the worklist configuration first'
    }

    It 'a failure of the configuration phase sets the flag before Phase 3 (must-fire)' {
        $set = $script:Loop.Find({ param($n) $n -is [System.Management.Automation.Language.AssignmentStatementAst] -and "$($n.Left)" -eq '$ontologyConfigFailed' -and "$($n.Right)" -match 'true' }, $true)
        $set | Should -Not -BeNullOrEmpty
        $set.Extent.StartOffset | Should -BeGreaterThan $script:Call.Extent.StartOffset
        $set.Extent.StartOffset | Should -BeLessThan $script:Phase3.Extent.StartOffset
    }

    It 'the explicit -SkipPhase OntologyConfig branch warns about Phase 3 and does NOT set the flag (must-not-fire)' {
        # The skip test is an elseif clause of the Phase 2b chain: find the clause, not a statement.
        $chain = $script:Loop.Find({ param($n) $n -is [System.Management.Automation.Language.IfStatementAst] -and ($n.Clauses | Where-Object { $_.Item1.Extent.Text -match "Test-PhaseSkipped 'OntologyConfig'" }) }, $true)
        $chain | Should -Not -BeNullOrEmpty
        $clause = $chain.Clauses | Where-Object { $_.Item1.Extent.Text -match "Test-PhaseSkipped 'OntologyConfig'" } | Select-Object -First 1
        $body = $clause.Item2.Extent.Text
        $body | Should -Match 'Write-Warn'
        $body | Should -Match 'Phase 3'
        $body | Should -Not -Match 'ontologyConfigFailed'
        $body | Should -Not -Match 'envFailed'
    }
}

Describe 'Invoke-OntologyConfigPhase (run against a fake Apply script)' {
    BeforeEach {
        $script:FailMessages = @()
        $script:Dir = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $script:Dir | Out-Null
    }

    It 'applies, then verifies with -Verify -RequireRow, and succeeds' {
        $apply = New-FakeApply $script:Dir
        $ok = Invoke-OntologyConfigPhase -ApplyScript $apply -DataverseUrl 'https://x.crm.dynamics.com' -WhatIfMode:$false
        $ok | Should -BeTrue
        $calls = @(Get-Calls $script:Dir)
        $calls.Count | Should -Be 2
        $calls[0] | Should -Be 'url=https://x.crm.dynamics.com;verify=False;requireRow=False;whatIf=False'
        $calls[1] | Should -Be 'url=https://x.crm.dynamics.com;verify=True;requireRow=True;whatIf=False'
    }

    It 'under -WhatIf the apply runs in WhatIf mode and the verify is skipped' {
        $apply = New-FakeApply $script:Dir
        $ok = Invoke-OntologyConfigPhase -ApplyScript $apply -DataverseUrl 'https://x.crm.dynamics.com' -WhatIfMode:$true
        $ok | Should -BeTrue
        $calls = @(Get-Calls $script:Dir)
        $calls.Count | Should -Be 1
        $calls[0] | Should -Be 'url=https://x.crm.dynamics.com;verify=False;requireRow=False;whatIf=True'
    }

    It 'a non-zero exit from the apply fails the phase and the verify never runs' {
        $apply = New-FakeApply $script:Dir -ApplyExit 1
        $ok = Invoke-OntologyConfigPhase -ApplyScript $apply -DataverseUrl 'https://x.crm.dynamics.com' -WhatIfMode:$false
        $ok | Should -BeFalse
        @(Get-Calls $script:Dir).Count | Should -Be 1
        $script:FailMessages.Count | Should -BeGreaterThan 0
    }

    It 'an exception from the apply fails the phase' {
        $apply = New-FakeApply $script:Dir -ApplyThrows
        Invoke-OntologyConfigPhase -ApplyScript $apply -DataverseUrl 'https://x.crm.dynamics.com' -WhatIfMode:$false | Should -BeFalse
    }

    It 'a non-zero exit from the verify (DRIFT, ABSENT, query error) fails the phase' {
        $apply = New-FakeApply $script:Dir -VerifyExit 1
        Invoke-OntologyConfigPhase -ApplyScript $apply -DataverseUrl 'https://x.crm.dynamics.com' -WhatIfMode:$false | Should -BeFalse
        @(Get-Calls $script:Dir).Count | Should -Be 2
    }

    It 'a missing script fails the phase (fail closed)' {
        Invoke-OntologyConfigPhase -ApplyScript (Join-Path $script:Dir 'nope.ps1') -DataverseUrl 'https://x.crm.dynamics.com' -WhatIfMode:$false | Should -BeFalse
    }

    It 'a stale $LASTEXITCODE from an earlier phase does not fail a good run' {
        $apply = New-FakeApply $script:Dir
        $global:LASTEXITCODE = 5
        try {
            Invoke-OntologyConfigPhase -ApplyScript $apply -DataverseUrl 'https://x.crm.dynamics.com' -WhatIfMode:$false | Should -BeTrue
        } finally { $global:LASTEXITCODE = 0 }
    }

    It 'returns a single boolean (no pipeline noise from the child script)' {
        $apply = New-FakeApply $script:Dir
        $r = @(Invoke-OntologyConfigPhase -ApplyScript $apply -DataverseUrl 'https://x.crm.dynamics.com' -WhatIfMode:$false)
        $r.Count | Should -Be 1
        $r[0] | Should -BeOfType [bool]
    }
}
