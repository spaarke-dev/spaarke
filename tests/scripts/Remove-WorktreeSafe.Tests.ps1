# tests/scripts/Remove-WorktreeSafe.Tests.ps1
# ---------------------------------------------------------------------------
# Must-fire / must-not-fire controls for scripts/worktree/Remove-WorktreeSafe.ps1 (incident 2026-10-13: a bulk
# worktree cleanup deleted THROUGH a node_modules junction into another worktree's tracked files).
#
# Technique: a real temp git repo with `git worktree add`, plus a separate "victim" folder the junctions point at.
# The script is run in a child pwsh (-File) because it `exit`s with a code. Written for Pester 5+ (BeforeAll scope).
# Run: Invoke-Pester tests/scripts/Remove-WorktreeSafe.Tests.ps1
# ---------------------------------------------------------------------------

BeforeAll {
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:Script = Join-Path $repoRoot 'scripts/worktree/Remove-WorktreeSafe.ps1'
    $script:Pwsh = (Get-Process -Id $PID).Path

    function Invoke-Git { param([string]$Dir, [string[]]$GitArgs)
        $out = & git -C $Dir -c user.name=t -c user.email=t@example.com @GitArgs 2>&1
        if ($LASTEXITCODE -ne 0) { throw "git $GitArgs failed: $out" }
        return $out
    }

    function New-Fixture {
        $root = Join-Path ([System.IO.Path]::GetTempPath()) ("rwsafe-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
        $f = @{
            Root   = $root
            Main   = Join-Path $root 'main'
            Wt     = Join-Path $root 'wt'
            Victim = Join-Path $root 'victim'
            Victim2 = Join-Path $root 'victim2'
        }
        New-Item -ItemType Directory -Path $f.Main, $f.Victim, $f.Victim2 | Out-Null
        Invoke-Git $f.Main @('init', '-q', '-b', 'main') | Out-Null
        Set-Content -Path (Join-Path $f.Main 'tracked.txt') -Value 'hello'
        Set-Content -Path (Join-Path $f.Main '.gitignore') -Value 'node_modules/'
        Invoke-Git $f.Main @('add', '-A') | Out-Null
        Invoke-Git $f.Main @('commit', '-q', '-m', 'init') | Out-Null
        Invoke-Git $f.Main @('worktree', 'add', '-q', '-b', 'task', $f.Wt) | Out-Null
        Set-Content -Path (Join-Path $f.Victim 'precious.txt') -Value 'keep me'
        Set-Content -Path (Join-Path $f.Victim2 'precious2.txt') -Value 'keep me too'
        # worktree\node_modules -> victim, and victim\nested -> victim2 (a link inside the link's target)
        New-Item -ItemType Junction -Path (Join-Path $f.Wt 'node_modules') -Target $f.Victim | Out-Null
        New-Item -ItemType Junction -Path (Join-Path $f.Victim 'nested') -Target $f.Victim2 | Out-Null
        return $f
    }

    function Invoke-Safe { param([string[]]$Paths, [switch]$Force, [switch]$WhatIf)
        $a = @('-NoProfile', '-NonInteractive', '-File', $script:Script, '-Path') + ($Paths -join ',')
        if ($Force) { $a += '-Force' }
        if ($WhatIf) { $a += '-WhatIf' }
        $out = & $script:Pwsh @a 2>&1 | Out-String
        return @{ Code = $LASTEXITCODE; Out = $out }
    }

    function Get-WorktreePaths($mainDir) {
        @(& git -C $mainDir worktree list --porcelain | Where-Object { $_ -like 'worktree *' } | ForEach-Object { $_.Substring(9) -replace '/', '\' })
    }

    function Get-ScriptCode {
        # the script minus block comments and full-line comments (the help text names the forbidden commands)
        $t = Get-Content -LiteralPath $script:Script -Raw
        $t = [regex]::Replace($t, '(?s)<#.*?#>', '')
        return (($t -split "`r?`n" | Where-Object { $_.TrimStart() -notlike '#*' }) -join "`n")
    }

    function Find-ForbiddenDelete([string]$code) {
        # patterns that would let the script delete THROUGH a link, or fall back to a recursive delete
        $patterns = @('(?i)-Recurse', '(?i)\bDelete\([^)]*\$true', '(?i)\brm\s+-[a-z]*r', '(?i)\brd\s+/s', '(?i)\brmdir\s+/s')
        return @($patterns | Where-Object { $code -match $_ })
    }

    function Set-DenyList([string]$dir, [bool]$deny) {
        # deny (or restore) "list folder" for the current user, so enumerating $dir throws UnauthorizedAccessException
        $sid = '*' + [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
        if (-not $deny -and -not (Test-Path -LiteralPath $dir)) { return }
        if ($deny) { & icacls $dir /deny "${sid}:(RD)" | Out-Null } else { & icacls $dir /remove:d $sid | Out-Null }
        if ($LASTEXITCODE -ne 0) { throw "icacls failed on $dir" }
    }

    function Remove-Fixture($f) {
        # test cleanup only: unlink junctions first so nothing is deleted through them
        foreach ($j in @((Join-Path $f.Wt 'node_modules'), (Join-Path $f.Victim 'nested'))) {
            if (Test-Path -LiteralPath $j) { [System.IO.Directory]::Delete($j, $false) }
        }
        Remove-Item -LiteralPath $f.Root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Describe 'Remove-WorktreeSafe.ps1' {
    Context 'must fire: unlinks junctions, never deletes through them' {
        BeforeAll { $script:F = New-Fixture }
        AfterAll { Remove-Fixture $script:F }

        It '-WhatIf changes nothing' {
            $r = Invoke-Safe -Paths $script:F.Wt -WhatIf
            $r.Code | Should -Be 0
            Test-Path -LiteralPath $script:F.Wt | Should -BeTrue
            (Get-Item -LiteralPath (Join-Path $script:F.Wt 'node_modules') -Force).LinkType | Should -Be 'Junction'
            Test-Path -LiteralPath (Join-Path $script:F.Victim 'precious.txt') | Should -BeTrue
            @(Get-WorktreePaths $script:F.Main).Count | Should -Be 2
        }

        It 'removes the worktree and leaves both victim files, including the one behind the nested junction' {
            $r = Invoke-Safe -Paths $script:F.Wt
            $r.Code | Should -Be 0 -Because $r.Out
            Test-Path -LiteralPath $script:F.Wt | Should -BeFalse
            Test-Path -LiteralPath (Join-Path $script:F.Victim 'precious.txt') | Should -BeTrue
            Test-Path -LiteralPath (Join-Path $script:F.Victim2 'precious2.txt') | Should -BeTrue
            (Get-WorktreePaths $script:F.Main | Where-Object { $_ -ieq $script:F.Wt }) | Should -BeNullOrEmpty
            $r.Out | Should -Match 'OUTSIDE'
            $r.Out | Should -Match '1 link\(s\) removed'
        }
    }

    Context 'must not fire: refuses and touches nothing' {
        BeforeAll { $script:F = New-Fixture }
        AfterAll { Remove-Fixture $script:F }

        It 'refuses the main worktree' {
            $r = Invoke-Safe -Paths $script:F.Main
            $r.Code | Should -Not -Be 0
            $r.Out | Should -Match 'MAIN worktree'
            Test-Path -LiteralPath (Join-Path $script:F.Main 'tracked.txt') | Should -BeTrue
        }

        It 'refuses an unregistered folder (and a folder inside a worktree)' {
            $r = Invoke-Safe -Paths $script:F.Victim
            $r.Code | Should -Not -Be 0
            Test-Path -LiteralPath (Join-Path $script:F.Victim 'precious.txt') | Should -BeTrue
            $sub = Join-Path $script:F.Wt 'sub'
            New-Item -ItemType Directory -Path $sub | Out-Null
            $r2 = Invoke-Safe -Paths $sub
            $r2.Code | Should -Not -Be 0
            $r2.Out | Should -Match 'not a registered worktree root'
            Test-Path -LiteralPath $sub | Should -BeTrue
            Remove-Item -LiteralPath $sub
        }

        It 'refuses a dirty worktree without -Force and leaves the worktree and its junction intact' {
            Set-Content -Path (Join-Path $script:F.Wt 'untracked.txt') -Value 'wip'
            $r = Invoke-Safe -Paths $script:F.Wt
            $r.Code | Should -Not -Be 0
            $r.Out | Should -Match 'uncommitted changes'
            Test-Path -LiteralPath (Join-Path $script:F.Wt 'untracked.txt') | Should -BeTrue
            (Get-Item -LiteralPath (Join-Path $script:F.Wt 'node_modules') -Force).LinkType | Should -Be 'Junction'
            Test-Path -LiteralPath (Join-Path $script:F.Victim 'precious.txt') | Should -BeTrue
        }

        It 'validates every path first: one bad path means the good one is untouched too' {
            $r = Invoke-Safe -Paths @($script:F.Victim, $script:F.Wt) -Force
            $r.Code | Should -Not -Be 0
            Test-Path -LiteralPath $script:F.Wt | Should -BeTrue
            (Get-Item -LiteralPath (Join-Path $script:F.Wt 'node_modules') -Force).LinkType | Should -Be 'Junction'
        }

        It '-Force removes the dirty worktree, still without touching the victim' {
            $r = Invoke-Safe -Paths $script:F.Wt -Force
            $r.Code | Should -Be 0 -Because $r.Out
            Test-Path -LiteralPath $script:F.Wt | Should -BeFalse
            Test-Path -LiteralPath (Join-Path $script:F.Victim 'precious.txt') | Should -BeTrue
            Test-Path -LiteralPath (Join-Path $script:F.Victim2 'precious2.txt') | Should -BeTrue
        }
    }

    Context 'never falls back: a path git cannot remove is left for a human' {
        BeforeAll { $script:F = New-Fixture }
        AfterAll {
            & git -C $script:F.Main worktree unlock $script:F.Wt 2>$null | Out-Null
            Remove-Fixture $script:F
        }

        It 'refuses a locked worktree even with -Force, before unlinking anything' {
            Invoke-Git $script:F.Main @('worktree', 'lock', '--reason', 'test lock', $script:F.Wt) | Out-Null
            $r = Invoke-Safe -Paths $script:F.Wt -Force
            $r.Code | Should -Not -Be 0 -Because $r.Out
            $r.Out | Should -Match 'locked'
            $r.Out | Should -Match 'git worktree unlock'
            Test-Path -LiteralPath $script:F.Wt | Should -BeTrue
            Test-Path -LiteralPath (Join-Path $script:F.Victim 'precious.txt') | Should -BeTrue
            Test-Path -LiteralPath (Join-Path $script:F.Victim2 'precious2.txt') | Should -BeTrue
            (Get-Item -LiteralPath (Join-Path $script:F.Wt 'node_modules') -Force).LinkType | Should -Be 'Junction'
        }

        It 'removes the worktree once it is unlocked (the refusal was the lock, nothing else)' {
            Invoke-Git $script:F.Main @('worktree', 'unlock', $script:F.Wt) | Out-Null
            $r = Invoke-Safe -Paths $script:F.Wt -Force
            $r.Code | Should -Be 0 -Because $r.Out
            Test-Path -LiteralPath $script:F.Wt | Should -BeFalse
            Test-Path -LiteralPath (Join-Path $script:F.Victim 'precious.txt') | Should -BeTrue
        }
    }

    Context 'fails closed when a folder cannot be enumerated' {
        BeforeAll {
            $script:F = New-Fixture
            $script:Blocked = Join-Path $script:F.Wt 'blocked'
            New-Item -ItemType Directory -Path $script:Blocked | Out-Null
            Set-DenyList $script:Blocked $true
        }
        AfterAll {
            Set-DenyList $script:Blocked $false
            Remove-Fixture $script:F
        }

        It 'touches nothing: no unlink, no git worktree remove, non-zero exit' {
            $r = Invoke-Safe -Paths $script:F.Wt -Force
            $r.Code | Should -Not -Be 0 -Because $r.Out
            $r.Out | Should -Match 'cannot enumerate'
            Test-Path -LiteralPath $script:F.Wt | Should -BeTrue
            (Get-Item -LiteralPath (Join-Path $script:F.Wt 'node_modules') -Force).LinkType | Should -Be 'Junction'
            Test-Path -LiteralPath (Join-Path $script:F.Victim 'precious.txt') | Should -BeTrue
            Test-Path -LiteralPath (Join-Path $script:F.Victim2 'precious2.txt') | Should -BeTrue
            @(Get-WorktreePaths $script:F.Main).Count | Should -Be 2
        }
    }

    Context 'static control: the script has no recursive-delete fallback' {
        It 'the real script contains none of the forbidden spellings' {
            Find-ForbiddenDelete (Get-ScriptCode) | Should -BeNullOrEmpty
        }

        It 'the check itself fires on each forbidden spelling (must-fire control)' {
            foreach ($bad in @('Remove-Item -LiteralPath $p -Recurse -Force', '[System.IO.Directory]::Delete($p, $true)',
                               '[IO.Directory]::Delete($p,$true)', 'rm -rf $p', 'rd /s /q $p', 'rmdir /s /q $p')) {
                Find-ForbiddenDelete $bad | Should -Not -BeNullOrEmpty -Because $bad
            }
            Find-ForbiddenDelete '[System.IO.Directory]::Delete($e.FullName, $false)' | Should -BeNullOrEmpty
        }
    }
}
