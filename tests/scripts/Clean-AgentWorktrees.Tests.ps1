# tests/scripts/Clean-AgentWorktrees.Tests.ps1
# ---------------------------------------------------------------------------
# Must-fire / must-not-fire controls for scripts/worktree/Clean-AgentWorktrees.ps1 (2026-11-04: 123 sub-agent copies
# under the main checkout's .claude\worktrees hung VS Code / Claude sessions opened there; FAILURE-MODES G-25).
#
# Technique: a real temp git repo whose .claude\worktrees holds one agent copy per rule (finished, locked, dirty,
# unmerged, merged elsewhere, junction inside), plus a worktree OUTSIDE .claude\worktrees and two unregistered folders.
# The script runs in a child pwsh (-File) because it `exit`s with a code. Pester 5+.
# Run: Invoke-Pester tests/scripts/Clean-AgentWorktrees.Tests.ps1
# ---------------------------------------------------------------------------

BeforeAll {
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:Script = Join-Path $repoRoot 'scripts/worktree/Clean-AgentWorktrees.ps1'
    $script:Pwsh = (Get-Process -Id $PID).Path

    function Invoke-Git { param([string]$Dir, [string[]]$GitArgs)
        $out = & git -C $Dir -c user.name=t -c user.email=t@example.com @GitArgs 2>&1
        if ($LASTEXITCODE -ne 0) { throw "git $GitArgs failed: $out" }
        return $out
    }

    # One agent copy on branch task/<name> with one commit of its own. -MergeInto names the branch that "harvests" it.
    function New-AgentCopy { param($f, [string]$Name, [string]$MergeInto, [switch]$Lock, [switch]$Dirty, [switch]$Junction, [string]$Under)
        $parent = if ($Under) { $Under } else { $f.Base }
        $p = Join-Path $parent $Name
        Invoke-Git $f.Main @('worktree', 'add', '-q', '-b', "task/$Name", $p) | Out-Null
        Set-Content -Path (Join-Path $p "$Name.txt") -Value $Name
        Invoke-Git $p @('add', '-A') | Out-Null
        Invoke-Git $p @('commit', '-q', '-m', "work $Name") | Out-Null
        if ($MergeInto) {
            $sha = (Invoke-Git $p @('rev-parse', 'HEAD')) | Select-Object -Last 1
            Invoke-Git $f.Main @('branch', '-f', $MergeInto, $sha) | Out-Null
        }
        if ($Junction) { New-Item -ItemType Junction -Path (Join-Path $p 'node_modules') -Target $f.Victim | Out-Null }
        if ($Dirty) { Set-Content -Path (Join-Path $p 'wip.txt') -Value 'wip' }
        if ($Lock) { Invoke-Git $f.Main @('worktree', 'lock', '--reason', 'claude agent test', $p) | Out-Null }
        return $p
    }

    function New-Fixture {
        $root = Join-Path ([IO.Path]::GetTempPath()) ("cawt-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
        $f = @{ Root = $root; Main = Join-Path $root 'main'; Victim = Join-Path $root 'victim' }
        $f.Base = Join-Path $f.Main '.claude\worktrees'
        New-Item -ItemType Directory -Path $f.Main, $f.Victim | Out-Null
        Invoke-Git $f.Main @('init', '-q', '-b', 'master') | Out-Null
        Set-Content -Path (Join-Path $f.Main '.gitignore') -Value ".claude/worktrees/`nnode_modules/"
        Invoke-Git $f.Main @('add', '-A') | Out-Null
        Invoke-Git $f.Main @('commit', '-q', '-m', 'init') | Out-Null
        New-Item -ItemType Directory -Path $f.Base | Out-Null
        Set-Content -Path (Join-Path $f.Victim 'precious.txt') -Value 'keep me'

        $f.Finished  = New-AgentCopy $f 'agent-finished' -MergeInto 'work/proj' -Junction
        $f.Elsewhere = New-AgentCopy $f 'agent-elsewhere' -MergeInto 'work/other'
        $f.Locked    = New-AgentCopy $f 'agent-locked' -MergeInto 'harvest/locked' -Lock
        $f.Dirty     = New-AgentCopy $f 'agent-dirty' -MergeInto 'harvest/dirty' -Dirty
        $f.Unmerged  = New-AgentCopy $f 'agent-unmerged'
        $f.Outside   = New-AgentCopy $f 'outside-wt' -MergeInto 'harvest/outside' -Under $root
        $f.EmptyOrphan = Join-Path $f.Base 'agent-emptyorphan'
        $f.PartialOrphan = Join-Path $f.Base 'agent-partialorphan'
        New-Item -ItemType Directory -Path $f.EmptyOrphan, (Join-Path $f.PartialOrphan 'src') | Out-Null
        Set-Content -Path (Join-Path $f.PartialOrphan 'src\left.txt') -Value 'half deleted'
        return $f
    }

    function Invoke-Clean { param($f, [int]$MinIdleHours = 0, [string]$Project, [switch]$WhatIf)
        $a = @('-NoProfile', '-NonInteractive', '-File', $script:Script, '-RepoRoot', $f.Main, '-MinIdleHours', "$MinIdleHours")
        if ($Project) { $a += @('-Project', $Project) }
        if ($WhatIf) { $a += '-WhatIf' }
        $out = & $script:Pwsh @a 2>&1 | Out-String
        return @{ Code = $LASTEXITCODE; Out = $out }
    }

    function Get-Registered($f) {
        @(& git -C $f.Main worktree list --porcelain | Where-Object { $_ -like 'worktree *' } | ForEach-Object { [IO.Path]::GetFullPath(($_.Substring(9) -replace '/', '\')) })
    }

    function Remove-Fixture($f) {
        & git -C $f.Main worktree unlock $f.Locked 2>$null | Out-Null
        $j = Join-Path $f.Finished 'node_modules'
        if (Test-Path -LiteralPath $j) { [IO.Directory]::Delete($j, $false) }   # unlink before the recursive cleanup
        Remove-Item -LiteralPath $f.Root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Describe 'Clean-AgentWorktrees.ps1' {
    Context 'must not fire: -WhatIf and the idle window change nothing' {
        BeforeAll { $script:F = New-Fixture }
        AfterAll { Remove-Fixture $script:F }

        It '-WhatIf lists the finished copies and touches nothing' {
            $before = Get-Registered $script:F
            $r = Invoke-Clean $script:F -WhatIf
            $r.Code | Should -Be 0 -Because $r.Out
            $r.Out | Should -Match 'would remove: agent-finished'
            $r.Out | Should -Match 'would remove: agent-elsewhere'
            @(Get-Registered $script:F).Count | Should -Be $before.Count
            Test-Path -LiteralPath $script:F.EmptyOrphan | Should -BeTrue
            (Get-Item -LiteralPath (Join-Path $script:F.Finished 'node_modules') -Force).LinkType | Should -Be 'Junction'
        }

        It 'keeps copies that were active inside -MinIdleHours (default 12)' {
            $r = Invoke-Clean $script:F -MinIdleHours 12
            $r.Code | Should -Be 0 -Because $r.Out
            $r.Out | Should -Match 'agent-finished : active'
            Test-Path -LiteralPath $script:F.Finished | Should -BeTrue
            Test-Path -LiteralPath $script:F.Elsewhere | Should -BeTrue
        }
    }

    Context '-Project removes only copies merged into work/<Project>' {
        BeforeAll { $script:F = New-Fixture }
        AfterAll { Remove-Fixture $script:F }

        It 'removes the work/proj copy and keeps the one merged into work/other' {
            $r = Invoke-Clean $script:F -Project 'proj'
            $r.Code | Should -Be 0 -Because $r.Out
            Test-Path -LiteralPath $script:F.Finished | Should -BeFalse
            Test-Path -LiteralPath $script:F.Elsewhere | Should -BeTrue
            $r.Out | Should -Match 'agent-elsewhere : not merged into work/proj'
        }
    }

    Context 'must fire and must not fire, full run' {
        BeforeAll {
            $script:F = New-Fixture
            $script:R = Invoke-Clean $script:F
            $script:Reg = Get-Registered $script:F
        }
        AfterAll { Remove-Fixture $script:F }

        It 'exits 0' { $script:R.Code | Should -Be 0 -Because $script:R.Out }

        It 'removes finished copies (merged into another branch, clean, unlocked)' {
            Test-Path -LiteralPath $script:F.Finished | Should -BeFalse
            Test-Path -LiteralPath $script:F.Elsewhere | Should -BeFalse
            $script:Reg | Should -Not -Contain ([IO.Path]::GetFullPath($script:F.Finished))
        }

        It 'keeps the branch of a removed copy (no commit is lost)' {
            & git -C $script:F.Main rev-parse --verify -q 'refs/heads/task/agent-finished' | Out-Null
            $LASTEXITCODE | Should -Be 0
        }

        It 'does not delete through a junction inside a removed copy' {
            Test-Path -LiteralPath (Join-Path $script:F.Victim 'precious.txt') | Should -BeTrue
        }

        It 'keeps a locked copy (agent running)' {
            Test-Path -LiteralPath (Join-Path $script:F.Locked 'agent-locked.txt') | Should -BeTrue
            $script:R.Out | Should -Match 'agent-locked : locked'
        }

        It 'keeps a copy with uncommitted changes' {
            Test-Path -LiteralPath (Join-Path $script:F.Dirty 'wip.txt') | Should -BeTrue
            $script:R.Out | Should -Match 'agent-dirty : uncommitted changes'
        }

        It 'keeps a copy whose work is on no other branch' {
            Test-Path -LiteralPath $script:F.Unmerged | Should -BeTrue
            $script:R.Out | Should -Match 'agent-unmerged : work not merged'
        }

        It 'never considers a worktree outside .claude\worktrees, even when finished' {
            Test-Path -LiteralPath (Join-Path $script:F.Outside 'outside-wt.txt') | Should -BeTrue
            $script:Reg | Should -Contain ([IO.Path]::GetFullPath($script:F.Outside))
        }

        It 'removes an empty unregistered folder and leaves a non-empty one for a human' {
            Test-Path -LiteralPath $script:F.EmptyOrphan | Should -BeFalse
            Test-Path -LiteralPath (Join-Path $script:F.PartialOrphan 'src\left.txt') | Should -BeTrue
            $script:R.Out | Should -Match 'left for a human: agent-partialorphan'
        }

        It 'never touches the main checkout' {
            Test-Path -LiteralPath (Join-Path $script:F.Main '.gitignore') | Should -BeTrue
            $script:Reg[0] | Should -Be ([IO.Path]::GetFullPath($script:F.Main))
        }
    }
}
