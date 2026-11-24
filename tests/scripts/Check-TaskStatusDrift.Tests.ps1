# Pester 5. Run: Import-Module Pester -RequiredVersion 5.7.1; Invoke-Pester tests/scripts/Check-TaskStatusDrift.Tests.ps1
# ebilling-r1 ISS-047 (#3042). scripts/check-task-status-drift.ps1 is the task-completion gate. Its index parser read the row
# `| iss028 | ... |` as status marker `iss` + id `028` (so the iss rows overwrote the real 028/040 rows) and did not recognise ids
# such as 084h-1, 095f-2e2a, 130-0 at all (their POMLs read as "no index row"). These controls pin ONE id grammar for both
# row layouts: must-fire (prefixed and hyphenated ids parse whole and pair with their POMLs, a real drift on them is reported)
# and must-not-fire (a plain id is unchanged, an annotation row such as `| 090-note |` is not an id, a clean pair exits 0).
# Technique: the functions are read out of the script's syntax tree (the script exits when dot-sourced); the end-to-end cases
# copy the real script into a temp repo shaped like projects/<name>/tasks and run it in a child pwsh. No network.

BeforeAll {
    $script:ScriptPath = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path 'scripts' 'check-task-status-drift.ps1'
    $script:Pwsh = (Get-Process -Id $PID).Path
    $tokens = $null; $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($script:ScriptPath, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) { throw "The script does not parse: $($errors[0].Message)" }
    foreach ($fn in $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
        . ([scriptblock]::Create($fn.Extent.Text))
    }

    # A temp repo: scripts/check-task-status-drift.ps1 (the real one) + projects/<name>/tasks/{TASK-INDEX.md, *.poml}.
    function New-DriftRepo {
        param([string] $Name, [string[]] $IndexRows, [hashtable] $Pomls)
        $root = Join-Path ([IO.Path]::GetTempPath()) ("drift-" + [guid]::NewGuid().ToString('N'))
        $tasks = Join-Path $root "projects/$Name/tasks"
        New-Item -ItemType Directory -Path $tasks, (Join-Path $root 'scripts') -Force | Out-Null
        Copy-Item -LiteralPath $script:ScriptPath -Destination (Join-Path $root 'scripts') -Force
        $header = @('# Task index', '', '| Task | Status | Title | Rigor |', '|---|---|---|---|')
        Set-Content -LiteralPath (Join-Path $tasks 'TASK-INDEX.md') -Value ($header + $IndexRows) -Encoding UTF8
        foreach ($id in $Pomls.Keys) {
            $file = ($id -replace '[^A-Za-z0-9.-]', '_') + '.poml'
            Set-Content -LiteralPath (Join-Path $tasks $file) -Encoding UTF8 -Value "<task id=`"$id`"><metadata><status>$($Pomls[$id])</status></metadata></task>"
        }
        return $root
    }

    function Invoke-Drift {
        param([string] $Root, [string] $Name)
        $out = & $script:Pwsh -NoProfile -File (Join-Path $Root 'scripts/check-task-status-drift.ps1') -Project $Name 2>&1 | Out-String
        return [pscustomobject]@{ Code = $LASTEXITCODE; Text = $out }
    }

    function Get-Markers {
        param([string[]] $Rows)
        $root = New-DriftRepo -Name 'p' -IndexRows $Rows -Pomls @{}
        try { return Get-IndexMarkers -IndexPath (Join-Path $root 'projects/p/tasks/TASK-INDEX.md') }
        finally { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

Describe 'Get-IndexMarkers: one id grammar (ISS-047)' {
    Context 'must fire: letter-prefixed and hyphenated ids parse whole' {
        It 'reads `| iss028 | ... |` as id iss028, not marker iss + id 028' {
            $m = Get-Markers @('| iss028 | ✅ [done] | t | STANDARD |')
            $m.ContainsKey('iss028') | Should -BeTrue
            $m.ContainsKey('028') | Should -BeFalse
        }
        It 'keeps the real 028 row when an iss028 row follows it (no overwrite)' {
            $m = Get-Markers @('| 028 | 🔄 [open work] | real | STANDARD |', '| iss028 | ✅ [done] | issue | STANDARD |')
            $m['028'] | Should -Match '🔄'
            $m['iss028'] | Should -Match '\[done\]'
        }
        It 'reads a glued status-first row `| ✅iss028 | ... |` as id iss028' {
            $m = Get-Markers @('| ✅iss028 | t | x | y |')
            $m.ContainsKey('iss028') | Should -BeTrue
            $m.ContainsKey('028') | Should -BeFalse
        }
        It 'reads a spaced status-first row `| ✅ [done] iss028 | ... |` as id iss028' {
            $m = Get-Markers @('| ✅ [done] iss028 | t | x | y |')
            $m.Keys | Should -Contain 'iss028'
            $m.Keys | Should -Not -Contain '028'
        }
        It 'reads a letter suffix on a prefixed id (iss034a)' {
            (Get-Markers @('| iss034a | ✅ [done] | t | x |')).Keys | Should -Contain 'iss034a'
        }
        It 'parses the hyphenated shapes 084h-1, 095f-2e2a, 130-0, 130-0b, 095d-2b-3' {
            $m = Get-Markers @(
                '| 084h-1 | 🔄 | t | x |', '| 095f-2e2a | ✅ | t | x |', '| 130-0 | ✅ | t | x |',
                '| 130-0b | ✅ | t | x |', '| 095d-2b-3 | ✅ | t | x |')
            $m.Count | Should -Be 5
            foreach ($id in '084h-1', '095f-2e2a', '130-0', '130-0b', '095d-2b-3') { $m.Keys | Should -Contain $id }
            $m['084h-1'] | Should -Match '🔄'
        }
        It 'does not split a hyphenated id that has a three-digit segment (084h-100)' {
            $m = Get-Markers @('| 084h-100 | ✅ | t | x |')
            $m.Keys | Should -Contain '084h-100'
            $m.Keys | Should -Not -Contain '100'
        }
        It 'parses a bold hyphenated id (**084h-1**)' {
            (Get-Markers @('| **084h-1** | ✅ | t | x |')).Keys | Should -Contain '084h-1'
        }
    }

    Context 'must not fire: everything that parsed before still parses the same' {
        It 'a plain id in the id-first layout is still the plain id' {
            $m = Get-Markers @('| 028 | ✅ [done] | t | STANDARD |')
            $m.Keys | Should -Contain '028'
            $m.Count | Should -Be 1
        }
        It 'a plain id with a suffix or a fraction is unchanged (102a, 081.5)' {
            $m = Get-Markers @('| 102a | ✅ | t | x |', '| 081.5 | 🔲 [open] | t | x |')
            $m.Keys | Should -Contain '102a'
            $m.Keys | Should -Contain '081.5'
        }
        It 'a status-first row with a plain id still parses (marker + id in one cell)' {
            $m = Get-Markers @('| ✅ [done] 001 | t | x | y |', '| 🔲 002 | t | x | y |')
            $m['001'] | Should -Match '\[done\]'
            $m.Keys | Should -Contain '002'
        }
        It 'a bold plain id with the status in a later cell still reads that status' {
            $m = Get-Markers @('| **001** | t | ✅ [done] | y |')
            $m['001'] | Should -Match '\[done\]'
        }
        It 'a dependency list (two ids in the first cell) is still not a status row' {
            $m = Get-Markers @('| 055, 064 | 105 | ✅ | y |', '| 001 | ✅ [done] | t | x |')
            $m.Keys | Should -Not -Contain '055'
            $m.Keys | Should -Not -Contain '064'
            $m.Count | Should -Be 1
        }
        It 'an annotation row whose hyphen tail is letters only (`| 090-note |`) is not an id' {
            $m = Get-Markers @('| 090 | ✅ [done] | wrap-up | x |', '| 090-note | wrap-up artifacts DONE ✅ | y | z |')
            $m.Keys | Should -Not -Contain '090-note'
            $m.Count | Should -Be 1
        }
        It 'a prose line that is not a table row yields nothing' {
            (Get-Markers @('iss028 is mentioned here ✅', '')).Count | Should -Be 0
        }
    }
}

Describe 'check-task-status-drift.ps1 end to end (ISS-047)' {
    It 'pairs iss-prefixed and hyphen-suffixed POMLs with their rows: clean pair exits 0' {
        $root = New-DriftRepo -Name 'p' -IndexRows @(
            '| 028 | ✅ [done] | t | x |', '| iss028 | ✅ [done] | t | x |',
            '| 084h-1 | ✅ [done] | t | x |', '| 095f-2e2a | ✅ [done] | t | x |', '| 130-0 | 🔲 [open] | t | x |') `
            -Pomls @{ '028' = 'completed'; 'iss028' = 'completed'; '084h-1' = 'completed'; '095f-2e2a' = 'completed'; '130-0' = 'not-started' }
        try {
            $r = Invoke-Drift -Root $root -Name 'p'
            $r.Text | Should -Match 'No drift'
            $r.Code | Should -Be 0
        }
        finally { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
    }
    It 'an iss row no longer overwrites its plain twin: 028 stays in progress, iss028 done, no phantom drift' {
        $root = New-DriftRepo -Name 'p' -IndexRows @('| 028 | 🔄 [wip] | t | x |', '| iss028 | ✅ [done] | t | x |') `
            -Pomls @{ '028' = 'in-progress'; 'iss028' = 'completed' }
        try {
            $r = Invoke-Drift -Root $root -Name 'p'
            $r.Code | Should -Be 0
        }
        finally { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
    }
    It 'still reports a real drift on a prefixed id (index done, POML not-started) and exits 1' {
        $root = New-DriftRepo -Name 'p' -IndexRows @('| 001 | ✅ [done] | t | x |', '| iss029 | ✅ [done] | t | x |') `
            -Pomls @{ '001' = 'completed'; 'iss029' = 'not-started' }
        try {
            $r = Invoke-Drift -Root $root -Name 'p'
            $r.Code | Should -Be 1
            $r.Text | Should -Match 'iss029\s+POML=''not-started''.*POML is behind'
        }
        finally { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
    }
    It 'still reports a real drift on a hyphenated id (index done, POML not-started) and exits 1' {
        $root = New-DriftRepo -Name 'p' -IndexRows @('| 001 | ✅ [done] | t | x |', '| 084h-1 | ✅ [done] | t | x |') `
            -Pomls @{ '001' = 'completed'; '084h-1' = 'not-started' }
        try {
            $r = Invoke-Drift -Root $root -Name 'p'
            $r.Code | Should -Be 1
            $r.Text | Should -Match '084h-1\s+POML=''not-started''.*POML is behind'
        }
        finally { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
    }
    It 'still reports a hyphenated POML with no index row as unpaired (the set check is intact)' {
        $root = New-DriftRepo -Name 'p' -IndexRows @('| 001 | ✅ [done] | t | x |') `
            -Pomls @{ '001' = 'completed'; '130-0' = 'completed' }
        try {
            $r = Invoke-Drift -Root $root -Name 'p'
            $r.Code | Should -Be 1
            $r.Text | Should -Match '130-0\s+INDEX has no row'
        }
        finally { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
    }
}
