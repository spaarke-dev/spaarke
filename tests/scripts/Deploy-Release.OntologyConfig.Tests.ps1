# Pester 5. Run: Invoke-Pester tests/scripts/Deploy-Release.OntologyConfig.Tests.ps1
# Ontology task 164 (D-185, #2285): scripts/Deploy-Release.ps1 applies and verifies the worklist grid configuration
# (scripts/Apply-WorklistGridConfiguration.ps1) after Phase 2 (BFF) and before Phase 3 (SpaarkeMaster import), because the
# SpaarkeMaster import carries the role change that removes policy Read. A configuration failure for an environment
# therefore blocks Phase 3 for that environment, even without -StopOnFailure; an explicit -SkipPhase OntologyConfig does
# not. A FAILED BFF (Phase 2) blocks Phase 2b and Phase 3 too (the old BFF cannot read the new configuration), whatever
# -StopOnFailure says (fix round 1, F1).
#
# Deploy-Release.ps1 deploys on load, so it is never run here. Its functions are read out of the script's own AST, and the
# real per-environment loop (Phases 2 to 3) is cut out of the script and run against fake phase scripts that log their calls
# (no Dataverse, no az, no Azure). The gate is a pure function (Get-OntologyGate) tested as a full truth table.

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:ScriptPath = Join-Path $script:RepoRoot 'scripts' 'Deploy-Release.ps1'
    $script:ScriptText = Get-Content -LiteralPath $script:ScriptPath -Raw
    $errors = $null
    $script:Ast = [System.Management.Automation.Language.Parser]::ParseFile($script:ScriptPath, [ref]$null, [ref]$errors)
    if ($errors.Count -gt 0) { throw "The script does not parse: $($errors[0].Message)" }

    # The output helpers the script's functions use (the real ones only write to the host).
    function Write-Step { param([string]$Message) }
    function Write-Ok { param([string]$Message) }
    function Write-Warn { param([string]$Message) }
    function Write-Fail { param([string]$Message) $script:FailMessages += $Message }
    function Write-Phase { param($a, $b) }
    function Write-Host { param([Parameter(ValueFromRemainingArguments)]$Rest) }   # the loop prints a banner
    function Get-ElapsedString { param($s) '0s' }

    function Get-ScriptFunction([string]$Name) {
        $script:Ast.Find({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $Name }, $true)
    }
    $fn = Get-ScriptFunction 'Invoke-OntologyConfigPhase'
    if ($fn) { . ([scriptblock]::Create($fn.Extent.Text)) }
    $script:PhaseFunction = $fn
    $gateFn = Get-ScriptFunction 'Get-OntologyGate'
    if ($gateFn) { . ([scriptblock]::Create($gateFn.Extent.Text)) }
    $script:GateFunction = $gateFn
    . ([scriptblock]::Create((Get-ScriptFunction 'Test-PhaseSkipped').Extent.Text))

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
    It 'defines Invoke-OntologyConfigPhase and Get-OntologyGate' {
        $script:PhaseFunction | Should -Not -BeNullOrEmpty
        $script:GateFunction | Should -Not -BeNullOrEmpty
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

    It 'the -SkipPhase help lists OntologyConfig' {
        $script:ScriptText | Should -Match 'Valid values: Build, BffApi, OntologyConfig, Solutions'
    }
}

Describe 'Get-OntologyGate: the full truth table (pure function)' {
    # BffFailed SkipConfig ConfigRan ConfigOk -> RunConfig Phase3Allowed Reason
    It 'BffFailed=<Bff> SkipConfig=<Skip> ConfigRan=<Ran> ConfigOk=<Ok> -> RunConfig=<Run> Phase3Allowed=<P3> (<Reason>)' -ForEach @(
        # a failed BFF dominates everything: no Phase 2b, no Phase 3 (F1)
        @{ Bff = $true; Skip = $false; Ran = $false; Ok = $false; Run = $false; P3 = $false; Reason = 'bff-failed' }
        @{ Bff = $true; Skip = $false; Ran = $true; Ok = $true; Run = $false; P3 = $false; Reason = 'bff-failed' }
        @{ Bff = $true; Skip = $true; Ran = $false; Ok = $false; Run = $false; P3 = $false; Reason = 'bff-failed' }
        @{ Bff = $true; Skip = $true; Ran = $true; Ok = $true; Run = $false; P3 = $false; Reason = 'bff-failed' }
        # an explicit -SkipPhase OntologyConfig (BFF fine) neither runs the phase nor blocks Phase 3 (must-not-fire)
        @{ Bff = $false; Skip = $true; Ran = $false; Ok = $false; Run = $false; P3 = $true; Reason = 'skipped' }
        @{ Bff = $false; Skip = $true; Ran = $true; Ok = $true; Run = $false; P3 = $true; Reason = 'skipped' }
        # the phase is due but has not run: Phase 3 is closed (fail closed)
        @{ Bff = $false; Skip = $false; Ran = $false; Ok = $false; Run = $true; P3 = $false; Reason = 'config-not-run' }
        @{ Bff = $false; Skip = $false; Ran = $false; Ok = $true; Run = $true; P3 = $false; Reason = 'config-not-run' }
        # ran and failed: Phase 3 closed
        @{ Bff = $false; Skip = $false; Ran = $true; Ok = $false; Run = $true; P3 = $false; Reason = 'config-failed' }
        # ran and succeeded: the only open path besides an explicit skip
        @{ Bff = $false; Skip = $false; Ran = $true; Ok = $true; Run = $true; P3 = $true; Reason = 'ok' }
    ) {
        $g = Get-OntologyGate -BffFailed $Bff -SkipConfig $Skip -ConfigRan $Ran -ConfigOk $Ok
        $g.RunConfig | Should -Be $Run
        $g.Phase3Allowed | Should -Be $P3
        $g.Reason | Should -Be $Reason
    }

    It 'defaults ConfigRan and ConfigOk to false (the stage-1 call)' {
        $g = Get-OntologyGate -BffFailed $false -SkipConfig $false
        $g.RunConfig | Should -BeTrue
        $g.Phase3Allowed | Should -BeFalse
    }
}

Describe 'the per-environment loop uses the gate (minimal AST checks)' {
    BeforeAll {
        $script:Loop = $script:Ast.Find({ param($n) $n -is [System.Management.Automation.Language.ForStatementAst] -and $n.Extent.Text -match 'targetEnvironments\.Count' -and $n.Extent.Text -match 'Phase 3' }, $true)
        $script:GateCalls = @($script:Loop.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Get-OntologyGate' }, $true))
        $script:Call = $script:Loop.Find({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Invoke-OntologyConfigPhase' }, $true)
        # the outermost if / elseif chain that holds the Phase 3 import
        $script:Phase3 = @($script:Loop.FindAll({ param($n) $n -is [System.Management.Automation.Language.IfStatementAst] -and $n.Extent.Text -match 'Import-SpaarkeMasterPackage' }, $true)) |
            Sort-Object { $_.Extent.Text.Length } -Descending | Select-Object -First 1
    }

    It 'calls Get-OntologyGate twice (before Phase 2b, and after it) and Invoke-OntologyConfigPhase once, all before the Phase 3 chain' {
        $script:Loop | Should -Not -BeNullOrEmpty
        $script:GateCalls.Count | Should -Be 2
        $script:Call | Should -Not -BeNullOrEmpty
        $script:Phase3 | Should -Not -BeNullOrEmpty
        $script:GateCalls[0].Extent.StartOffset | Should -BeLessThan $script:Call.Extent.StartOffset
        $script:GateCalls[1].Extent.StartOffset | Should -BeGreaterThan $script:Call.Extent.StartOffset
        $script:GateCalls[1].Extent.StartOffset | Should -BeLessThan $script:Phase3.Extent.StartOffset
    }

    It 'the first branch of the Phase 3 chain tests only the gate''s Phase3Allowed' {
        $first = $script:Phase3.Clauses[0].Item1.Extent.Text
        $first | Should -Match '\$gate\.Phase3Allowed'
        $first | Should -Not -Match 'StopOnFailure|envFailed'
    }

    It 'no assignment sits between the final gate call and Phase 3 (nothing can override the decision)' {
        $gate2 = $script:GateCalls[1].Extent.EndOffset
        $p3 = $script:Phase3.Extent.StartOffset
        $between = @($script:Loop.FindAll({ param($n) $n -is [System.Management.Automation.Language.AssignmentStatementAst] -and $n.Extent.StartOffset -ge $gate2 -and $n.Extent.EndOffset -le $p3 }, $true))
        $between.Count | Should -Be 0
    }

    It 'the only assignments to $gate are Get-OntologyGate results' {
        $assigns = @($script:Loop.FindAll({ param($n) $n -is [System.Management.Automation.Language.AssignmentStatementAst] -and "$($n.Left)" -eq '$gate' }, $true))
        $assigns.Count | Should -Be 2
        foreach ($a in $assigns) { $a.Right.Extent.Text | Should -Match '^Get-OntologyGate ' }
    }

    It 'the final gate call passes the bare flags (and the stage-1 call passes $bffFailed)' {
        $t = $script:GateCalls[1].Extent.Text
        $t | Should -Match '-BffFailed \$bffFailed\b'
        $t | Should -Match '-ConfigRan \$configRan\b'
        $t | Should -Match '-ConfigOk \$configOk\b'
        $script:GateCalls[0].Extent.Text | Should -Match '-BffFailed \$bffFailed\b'
    }

    It '-WhatIfMode receives the bare $WhatIfPreference variable, and DataverseUrl comes from the environment' {
        $arg = $script:Call.CommandElements
        $idx = 0..($arg.Count - 1) | Where-Object { $arg[$_] -is [System.Management.Automation.Language.CommandParameterAst] -and $arg[$_].ParameterName -eq 'WhatIfMode' }
        $value = $arg[$idx + 1]
        $value | Should -BeOfType [System.Management.Automation.Language.VariableExpressionAst]
        $value.VariablePath.UserPath | Should -Be 'WhatIfPreference'
        $script:Call.Extent.Text | Should -Match 'env\.DataverseUrl'
    }

    It '$bffFailed is reset per environment and set only inside the Phase 2 block' {
        $loopStart = $script:Loop.Extent.StartOffset
        $loopText = $script:Loop.Extent.Text
        $phase2 = $loopText.IndexOf('Write-Phase "Phase 2" "BFF API')
        $phase2b = $loopText.IndexOf('Phase 2b: Ontology configuration')
        $sets = @($script:Loop.FindAll({ param($n) $n -is [System.Management.Automation.Language.AssignmentStatementAst] -and "$($n.Left)" -eq '$bffFailed' }, $true))
        $sets.Count | Should -Be 3     # the reset + the two Phase 2 failure branches
        $sets[0].Right.Extent.Text | Should -Be '$false'
        foreach ($set in $sets[1..2]) {
            $set.Right.Extent.Text | Should -Be '$true'
            ($set.Extent.StartOffset - $loopStart) | Should -BeGreaterThan $phase2
            ($set.Extent.StartOffset - $loopStart) | Should -BeLessThan $phase2b
        }
    }
}

Describe 'the real per-environment loop, run against fake phase scripts (Phases 2 to 3)' {
    # The loop text is cut out of Deploy-Release.ps1 itself (from the top of the loop to the Phase 4 comment), wrapped in a
    # function that supports -WhatIf, and run with fake Deploy-BffApi / Apply-WorklistGridConfiguration /
    # Import-SpaarkeMasterPackage scripts that log their calls. This tests behaviour, not the loop's shape.
    BeforeAll {
        $loop = $script:Ast.Find({ param($n) $n -is [System.Management.Automation.Language.ForStatementAst] -and $n.Extent.Text -match 'targetEnvironments\.Count' -and $n.Extent.Text -match 'Phase 3' }, $true)
        $text = $loop.Extent.Text
        $cut = $text.IndexOf('# ── Phase 4')
        if ($cut -lt 0) { throw 'Phase 4 marker not found in the loop' }
        # The cut sits inside the loop body: record $envFailed (the per-environment failure flag the later phases and the
        # environment status read) at the end of each iteration, so the tests can assert it. Not asserted: only the flag itself.
        $body = $text.Substring(0, $cut) + "`n    `$script:EnvFailedLog += `$envFailed`n}"
        $wrapper = @"
function Invoke-LoopUnderTest {
    [CmdletBinding(SupportsShouldProcess)]
    param(`$targetEnvironments, `$ScriptDir, [string[]]`$SkipPhase = @(), [bool]`$StopOnFailure = `$true)
$body
}
"@
        . ([scriptblock]::Create($wrapper))

        function New-FakeScripts([string]$Dir, [int]$BffExit = 0, [int]$ApplyExit = 0, [int]$VerifyExit = 0, [switch]$ApplyThrows) {
            New-Item -ItemType Directory -Path (Join-Path $Dir 'solution-authoring') -Force | Out-Null
            $log = Join-Path $Dir 'calls.log'
            @"
param(`$Environment, `$ResourceGroupName, `$AppServiceName, `$UseSlotDeploy, `$SkipBuild)
Add-Content -LiteralPath '$log' -Value 'bff'
exit $BffExit
"@ | Set-Content -LiteralPath (Join-Path $Dir 'Deploy-BffApi.ps1') -Encoding utf8
            $throws = if ($ApplyThrows) { '$true' } else { '$false' }
            @"
[CmdletBinding(SupportsShouldProcess)]
param([string]`$DataverseUrl, [switch]`$Verify, [switch]`$RequireRow)
Add-Content -WhatIf:`$false -LiteralPath '$log' -Value ("apply url={0};verify={1};whatIf={2}" -f `$DataverseUrl, [bool]`$Verify, [bool]`$WhatIfPreference)
if (`$Verify) { exit $VerifyExit }
if ($throws) { throw 'apply blew up' }
exit $ApplyExit
"@ | Set-Content -LiteralPath (Join-Path $Dir 'Apply-WorklistGridConfiguration.ps1') -Encoding utf8
            @"
[CmdletBinding(SupportsShouldProcess)]
param([string]`$EnvironmentUrl, [string]`$PackageType)
Add-Content -WhatIf:`$false -LiteralPath '$log' -Value ("import url={0};whatIf={1}" -f `$EnvironmentUrl, [bool]`$WhatIfPreference)
"@ | Set-Content -LiteralPath (Join-Path $Dir 'solution-authoring/Import-SpaarkeMasterPackage.ps1') -Encoding utf8
        }
        function New-Env([string]$Name) {
            [pscustomobject]@{ Name = $Name; DisplayName = $Name; DataverseUrl = "https://$Name.crm.dynamics.com"; AppServiceName = "app-$Name"; ResourceGroup = "rg-$Name"; AppServiceSlot = 'staging'; SolutionPackageType = 'managed' }
        }
    }
    BeforeEach {
        $script:FailMessages = @()
        $script:EnvFailedLog = @()
        $script:Dir = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $script:Dir | Out-Null
    }

    It 'happy path: bff, apply, verify, import, in that order' {
        New-FakeScripts $script:Dir
        Invoke-LoopUnderTest -targetEnvironments @(New-Env a) -ScriptDir $script:Dir -StopOnFailure $true
        Get-Calls $script:Dir | Should -Be @('bff', 'apply url=https://a.crm.dynamics.com;verify=False;whatIf=False', 'apply url=https://a.crm.dynamics.com;verify=True;whatIf=False', 'import url=https://a.crm.dynamics.com;whatIf=False')
        $script:EnvFailedLog | Should -Be @($false)
    }

    It 'F1: a failed BFF with -StopOnFailure $false runs neither Phase 2b nor Phase 3' {
        New-FakeScripts $script:Dir -BffExit 1
        Invoke-LoopUnderTest -targetEnvironments @(New-Env a) -ScriptDir $script:Dir -StopOnFailure $false
        Get-Calls $script:Dir | Should -Be @('bff')
        ($script:FailMessages -join "`n") | Should -Match 'did not run'
        # K4: the message names the BFF as the reason, not the ontology configuration
        ($script:FailMessages -join "`n") | Should -Match 'the BFF \(Phase 2\) failed'
        $script:EnvFailedLog | Should -Be @($true)
    }

    It 'F1: a failed BFF with -StopOnFailure $true runs neither Phase 2b nor Phase 3' {
        New-FakeScripts $script:Dir -BffExit 1
        Invoke-LoopUnderTest -targetEnvironments @(New-Env a) -ScriptDir $script:Dir -StopOnFailure $true
        Get-Calls $script:Dir | Should -Be @('bff')
        $script:EnvFailedLog | Should -Be @($true)
    }

    It 'F1: a missing BFF script counts as a BFF failure (no 2b, no 3)' {
        New-FakeScripts $script:Dir
        Remove-Item -LiteralPath (Join-Path $script:Dir 'Deploy-BffApi.ps1')
        Invoke-LoopUnderTest -targetEnvironments @(New-Env a) -ScriptDir $script:Dir -StopOnFailure $false
        Get-Calls $script:Dir | Should -Be @()
        $script:EnvFailedLog | Should -Be @($true)
    }

    It '-SkipPhase BffApi does not set the BFF flag: 2b and 3 run (must-not-fire)' {
        New-FakeScripts $script:Dir -BffExit 1
        Invoke-LoopUnderTest -targetEnvironments @(New-Env a) -ScriptDir $script:Dir -SkipPhase 'BffApi' -StopOnFailure $false
        Get-Calls $script:Dir | Should -Be @('apply url=https://a.crm.dynamics.com;verify=False;whatIf=False', 'apply url=https://a.crm.dynamics.com;verify=True;whatIf=False', 'import url=https://a.crm.dynamics.com;whatIf=False')
        $script:EnvFailedLog | Should -Be @($false)
    }

    It 'a Phase 2 that -WhatIf declines does not set the BFF flag: apply and import both run in WhatIf mode (must-not-fire, and M2)' {
        New-FakeScripts $script:Dir -BffExit 1
        Invoke-LoopUnderTest -targetEnvironments @(New-Env a) -ScriptDir $script:Dir -StopOnFailure $false -WhatIf
        Get-Calls $script:Dir | Should -Be @('apply url=https://a.crm.dynamics.com;verify=False;whatIf=True', 'import url=https://a.crm.dynamics.com;whatIf=True')
        $script:EnvFailedLog | Should -Be @($false)
    }

    It 'a failing apply blocks Phase 3 even with -StopOnFailure $false' {
        New-FakeScripts $script:Dir -ApplyExit 1
        Invoke-LoopUnderTest -targetEnvironments @(New-Env a) -ScriptDir $script:Dir -StopOnFailure $false
        Get-Calls $script:Dir | Should -Be @('bff', 'apply url=https://a.crm.dynamics.com;verify=False;whatIf=False')
        # K4: a configuration failure keeps the D-185 wording and does not blame the BFF
        ($script:FailMessages -join "`n") | Should -Match 'needs the worklist configuration first \(gate: config-failed\)'
        ($script:FailMessages -join "`n") | Should -Not -Match 'the BFF \(Phase 2\) failed'
        # F3: the environment is recorded as failed, so Phase 6 does not tag the release
        $script:EnvFailedLog | Should -Be @($true)
    }

    It 'F3: a failing apply marks the environment failed with -StopOnFailure $true too' {
        New-FakeScripts $script:Dir -ApplyExit 1
        Invoke-LoopUnderTest -targetEnvironments @(New-Env a) -ScriptDir $script:Dir -StopOnFailure $true
        $script:EnvFailedLog | Should -Be @($true)
    }

    It 'F3: a throwing apply blocks Phase 3 and marks the environment failed (-StopOnFailure $false)' {
        New-FakeScripts $script:Dir -ApplyThrows
        Invoke-LoopUnderTest -targetEnvironments @(New-Env a) -ScriptDir $script:Dir -StopOnFailure $false
        @(Get-Calls $script:Dir | Where-Object { $_ -like 'import*' }).Count | Should -Be 0
        $script:EnvFailedLog | Should -Be @($true)
    }

    It 'a failing verify blocks Phase 3 even with -StopOnFailure $false' {
        New-FakeScripts $script:Dir -VerifyExit 1
        Invoke-LoopUnderTest -targetEnvironments @(New-Env a) -ScriptDir $script:Dir -StopOnFailure $false
        @(Get-Calls $script:Dir | Where-Object { $_ -like 'import*' }).Count | Should -Be 0
        $script:EnvFailedLog | Should -Be @($true)
    }

    It 'F3: a failing verify marks the environment failed with -StopOnFailure $true too' {
        New-FakeScripts $script:Dir -VerifyExit 1
        Invoke-LoopUnderTest -targetEnvironments @(New-Env a) -ScriptDir $script:Dir -StopOnFailure $true
        $script:EnvFailedLog | Should -Be @($true)
    }

    It '-SkipPhase OntologyConfig skips 2b and does not block Phase 3 (must-not-fire)' {
        New-FakeScripts $script:Dir -ApplyExit 1
        Invoke-LoopUnderTest -targetEnvironments @(New-Env a) -ScriptDir $script:Dir -SkipPhase 'OntologyConfig' -StopOnFailure $false
        Get-Calls $script:Dir | Should -Be @('bff', 'import url=https://a.crm.dynamics.com;whatIf=False')
        $script:EnvFailedLog | Should -Be @($false)
    }

    It 'one environment''s failure does not block the next environment''s Phase 2b or 3 (-StopOnFailure $false)' {
        New-FakeScripts $script:Dir
        # the BFF fails for the first environment only
        @"
param(`$Environment, `$ResourceGroupName, `$AppServiceName, `$UseSlotDeploy, `$SkipBuild)
Add-Content -LiteralPath '$(Join-Path $script:Dir 'calls.log')' -Value "bff `$AppServiceName"
if (`$AppServiceName -eq 'app-a') { exit 1 } else { exit 0 }
"@ | Set-Content -LiteralPath (Join-Path $script:Dir 'Deploy-BffApi.ps1') -Encoding utf8
        Invoke-LoopUnderTest -targetEnvironments @((New-Env a), (New-Env b)) -ScriptDir $script:Dir -StopOnFailure $false
        Get-Calls $script:Dir | Should -Be @('bff app-a', 'bff app-b', 'apply url=https://b.crm.dynamics.com;verify=False;whatIf=False', 'apply url=https://b.crm.dynamics.com;verify=True;whatIf=False', 'import url=https://b.crm.dynamics.com;whatIf=False')
        $script:EnvFailedLog | Should -Be @($true, $false)   # the flag is reset per environment
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
