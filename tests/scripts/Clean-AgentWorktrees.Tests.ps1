# tests/scripts/Clean-AgentWorktrees.Tests.ps1
# ---------------------------------------------------------------------------
# Must-fire / must-not-fire controls for scripts/worktree/Clean-AgentWorktrees.ps1 (2026-11-04: 123 sub-agent copies
# under the main checkout's .claude\worktrees hung VS Code / Claude sessions opened there; FAILURE-MODES G-25).
#
# Technique: a real temp git repo whose .claude\worktrees holds one agent copy per rule (finished, locked, dirty,
# unmerged, detached, chained to another agent's branch, merged elsewhere, junction inside), a worktree OUTSIDE
# .claude\worktrees and two unregistered folders. The script runs in a child pwsh (-File) because it `exit`s with a
# code. Pester 5 (5.7.1 in CI).
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

    function Get-Sha([string]$Dir, [string]$Rev = 'HEAD') { (Invoke-Git $Dir @('rev-parse', $Rev)) | Select-Object -Last 1 }

    # One agent copy with one commit of its own (unless -NoCommit). -MergeInto names the branch that "harvests" it.
    function New-AgentCopy { param($f, [string]$Name, [string]$MergeInto, [string]$From, [switch]$Detached, [switch]$NoCommit,
                                   [switch]$Lock, [switch]$Dirty, [switch]$Junction, [switch]$OldCommit, [switch]$OldReflog, [string]$Under)
        $parent = if ($Under) { $Under } else { $f.Base }
        $p = Join-Path $parent $Name
        $add = if ($Detached) { @('worktree', 'add', '-q', '--detach', $p) } else { @('worktree', 'add', '-q', '-b', "task/$Name", $p) }
        if ($From) { $add += $From }
        Invoke-Git $f.Main $add | Out-Null
        if (-not $NoCommit) {
            Set-Content -Path (Join-Path $p "$Name.txt") -Value $Name
            Invoke-Git $p @('add', '-A') | Out-Null
            if ($OldCommit) { $env:GIT_COMMITTER_DATE = '2020-01-01T00:00:00Z'; $env:GIT_AUTHOR_DATE = '2020-01-01T00:00:00Z' }
            try { Invoke-Git $p @('commit', '-q', '-m', "work $Name") | Out-Null }
            finally { Remove-Item Env:GIT_COMMITTER_DATE, Env:GIT_AUTHOR_DATE -ErrorAction SilentlyContinue }
        }
        if ($MergeInto) { Invoke-Git $f.Main @('branch', '-f', $MergeInto, (Get-Sha $p)) | Out-Null }
        if ($OldReflog) {
            $gd = (Invoke-Git $p @('rev-parse', '--path-format=absolute', '--git-dir')) | Select-Object -Last 1
            (Get-Item -LiteralPath (Join-Path ($gd -replace '/', '\') 'logs\HEAD')).LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(-3)
        }
        if ($Junction) { New-Item -ItemType Junction -Path (Join-Path $p 'node_modules') -Target $f.Victim | Out-Null }
        if ($Dirty) { Set-Content -Path (Join-Path $p 'wip.txt') -Value 'wip' }
        if ($Lock) { Invoke-Git $f.Main @('worktree', 'lock', '--reason', 'claude agent test', $p) | Out-Null }
        return $p
    }

    function New-Fixture {
        $root = Join-Path ([IO.Path]::GetTempPath()) ("cawt-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
        $f = @{ Root = $root; Main = Join-Path $root 'main'; Victim = Join-Path $root 'victim'; Denied = @() }
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
        $f.Detached  = New-AgentCopy $f 'agent-detached' -Detached
        # HEAD reachable only from another agent copy's branch (task/agent-unmerged): nobody merged it
        $f.Chained   = New-AgentCopy $f 'agent-chained' -Detached -NoCommit -From 'task/agent-unmerged'
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

    function Set-DenyList([string]$dir, [bool]$deny) {
        $sid = '*' + [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
        if (-not $deny -and -not (Test-Path -LiteralPath $dir)) { return }
        if ($deny) { & icacls $dir /deny "${sid}:(RD)" | Out-Null } else { & icacls $dir /remove:d $sid | Out-Null }
        if ($LASTEXITCODE -ne 0) { throw "icacls failed on $dir" }
    }

    function Remove-Fixture($f) {
        foreach ($d in $f.Denied) { Set-DenyList $d $false }
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

    Context 'idle time is the newest of the commit and the reflog' {
        BeforeAll {
            $script:F = New-Fixture
            $script:Old = New-AgentCopy $script:F 'agent-old' -MergeInto 'harvest/old' -OldCommit -OldReflog
            $script:Backdated = New-AgentCopy $script:F 'agent-backdated' -MergeInto 'harvest/backdated' -OldCommit
            $script:R = Invoke-Clean $script:F -MinIdleHours 12
        }
        AfterAll { Remove-Fixture $script:F }

        It 'removes a copy whose commit and reflog are both old' {
            $script:R.Code | Should -Be 0 -Because $script:R.Out
            Test-Path -LiteralPath $script:Old | Should -BeFalse
        }

        It 'keeps a copy with an old commit but a fresh reflog (checked out or reset recently)' {
            Test-Path -LiteralPath $script:Backdated | Should -BeTrue
            $script:R.Out | Should -Match 'agent-backdated : active'
        }
    }

    Context '-Project removes only copies merged into work/<Project> and not on master' {
        BeforeAll {
            $script:F = New-Fixture
            # another project's work: on master, and reached work/proj through master
            $script:ViaMaster = New-AgentCopy $script:F 'agent-viamaster'
            $v = Get-Sha $script:ViaMaster
            Invoke-Git $script:F.Main @('merge', '-q', '--ff-only', $v) | Out-Null
            $fin = Get-Sha $script:F.Main 'work/proj'
            $tree = Get-Sha $script:F.Main "$fin^{tree}"
            $m = (Invoke-Git $script:F.Main @('commit-tree', $tree, '-p', $fin, '-p', $v, '-m', 'merge master into work/proj')) | Select-Object -Last 1
            Invoke-Git $script:F.Main @('branch', '-f', 'work/proj', $m) | Out-Null
            # a missing project-external worktree must survive: -Project never prunes
            $script:Gone = New-AgentCopy $script:F 'outside-gone' -MergeInto 'harvest/gone' -Under $script:F.Root
            Rename-Item -LiteralPath $script:Gone -NewName 'outside-gone-away'
            $script:R = Invoke-Clean $script:F -Project 'proj'
        }
        AfterAll { Remove-Fixture $script:F }

        It 'removes the work/proj copy' {
            $script:R.Code | Should -Be 0 -Because $script:R.Out
            Test-Path -LiteralPath $script:F.Finished | Should -BeFalse
        }

        It 'keeps the copy merged into work/other' {
            Test-Path -LiteralPath $script:F.Elsewhere | Should -BeTrue
            $script:R.Out | Should -Match 'agent-elsewhere : not merged into work/proj'
        }

        It 'keeps another project''s copy whose work reached work/proj through master' {
            Test-Path -LiteralPath $script:ViaMaster | Should -BeTrue
            $script:R.Out | Should -Match 'agent-viamaster : not merged into work/proj \(or already on master\)'
        }

        It 'does no repo-wide housekeeping: empty orphan kept, nothing pruned' {
            Test-Path -LiteralPath $script:F.EmptyOrphan | Should -BeTrue
            Get-Registered $script:F | Should -Contain ([IO.Path]::GetFullPath($script:Gone))
        }

        It 'refuses a slug with no work/<slug> branch: exit 2, nothing touched' {
            $r = Invoke-Clean $script:F -Project 'no-such-project'
            $r.Code | Should -Be 2 -Because $r.Out
            $r.Out | Should -Match 'No branch work/no-such-project'
            Test-Path -LiteralPath $script:F.Elsewhere | Should -BeTrue
        }
    }

    Context 'prune runs only when every prunable worktree is under .claude\worktrees' {
        BeforeAll { $script:F = New-Fixture }
        AfterAll { Remove-Fixture $script:F }

        It 'skips the prune and names the worktree when a prunable one is outside' {
            $gone = New-AgentCopy $script:F 'outside-gone' -MergeInto 'harvest/gone' -Under $script:F.Root
            Rename-Item -LiteralPath $gone -NewName 'outside-gone-away'
            $agentGone = New-AgentCopy $script:F 'agent-gone' -MergeInto 'harvest/agentgone'
            Rename-Item -LiteralPath $agentGone -NewName 'agent-gone-away'
            $r = Invoke-Clean $script:F
            $r.Code | Should -Be 0 -Because $r.Out
            $r.Out | Should -Match 'prune SKIPPED'
            Get-Registered $script:F | Should -Contain ([IO.Path]::GetFullPath($gone))
            Get-Registered $script:F | Should -Contain ([IO.Path]::GetFullPath($agentGone))
            # put the outside one back: now only the agent copy is prunable, so the prune runs and drops it
            Rename-Item -LiteralPath (Join-Path $script:F.Root 'outside-gone-away') -NewName 'outside-gone'
            Remove-Item -LiteralPath (Join-Path $script:F.Base 'agent-gone-away') -Recurse -Force
            $r2 = Invoke-Clean $script:F
            $r2.Out | Should -Not -Match 'prune SKIPPED'
            Get-Registered $script:F | Should -Not -Contain ([IO.Path]::GetFullPath($agentGone))
            Get-Registered $script:F | Should -Contain ([IO.Path]::GetFullPath($gone))
        }
    }

    Context 'a refused removal is reported with its reason and exit 1' {
        BeforeAll {
            $script:F = New-Fixture
            # an ignored folder git status never lists, but Remove-WorktreeSafe must scan and cannot: it refuses
            $nm = Join-Path $script:F.Elsewhere 'node_modules\blocked'
            New-Item -ItemType Directory -Path $nm | Out-Null
            Set-DenyList $nm $true
            $script:F.Denied = @($nm)
            $script:R = Invoke-Clean $script:F
        }
        AfterAll { Remove-Fixture $script:F }

        It 'exits 1, keeps the copy, and the FAILED line carries the refusal reason' {
            $script:R.Code | Should -Be 1 -Because $script:R.Out
            Test-Path -LiteralPath $script:F.Elsewhere | Should -BeTrue
            $script:R.Out | Should -Match 'FAILED: agent-elsewhere : .*cannot enumerate'
        }

        It 'still removes the other finished copy (one refusal does not block the rest)' {
            Test-Path -LiteralPath $script:F.Finished | Should -BeFalse
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

        It 'keeps a detached copy whose commit is on no branch' {
            Test-Path -LiteralPath (Join-Path $script:F.Detached 'agent-detached.txt') | Should -BeTrue
            $script:R.Out | Should -Match 'agent-detached : work not merged'
        }

        It 'keeps a copy reachable only from another agent copy''s branch' {
            Test-Path -LiteralPath $script:F.Chained | Should -BeTrue
            $script:R.Out | Should -Match 'agent-chained : work not merged'
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
