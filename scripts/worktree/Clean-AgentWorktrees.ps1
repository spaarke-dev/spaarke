<#
.SYNOPSIS
    Removes FINISHED sub-agent worktrees under <main checkout>\.claude\worktrees, and nothing else.

.DESCRIPTION
    Claude Code creates one worktree per isolated sub-agent under the MAIN checkout's .claude\worktrees, whichever
    worktree the parent session runs in. Its own sweep keeps every copy with unpushed commits (agent task branches are
    merged locally into work/<project>, never pushed) and waits for cleanupPeriodDays, so copies pile up: 123 on
    2026-11-04, about 3 million files, and VS Code / Claude sessions opened in the main checkout hung (FAILURE-MODES G-25).

    A copy is removed only when ALL of these hold:
      1. It is a registered worktree directly under .claude\worktrees and is NOT locked (Claude Code locks a copy
         while its agent runs).
      2. It has been idle for at least -MinIdleHours: the newest of its HEAD commit time and its reflog write time.
      3. Its HEAD commit is reachable from a branch that is not its own and not another agent copy's branch, i.e. the
         parent session has merged the work. With -Project, that branch must be work/<Project> (local or origin) and
         the commit must not already be on master (so another project's copies, whose work reached work/<Project>
         through master, are not taken).
      4. `git status` succeeds without warnings and shows no uncommitted or untracked changes.

    Removal goes through Remove-WorktreeSafe.ps1 in a child pwsh, one path per call. That script re-checks the lock and
    the dirty state at removal time, unlinks junctions without following them (G-20) and never falls back to a recursive
    delete. The copy's branch is kept, so no commit on a branch is lost.

    Without -Project the script also removes EMPTY unregistered folders under .claude\worktrees (non-recursive delete),
    lists any other unregistered folder for a human, and runs `git worktree prune` ONLY when every prunable entry is
    under .claude\worktrees. A prunable worktree elsewhere (a project worktree on a missing drive, a detached C:\wt*)
    is listed instead, because pruning it would drop its admin files, and for a detached worktree its only ref.
    With -Project none of that runs: only that project's merged copies are touched.

    Known limits: locks are read once at the start (Remove-WorktreeSafe and git re-check them at removal time); a
    stale lock from a dead process counts as running; ignored files and detached-HEAD reflog entries in a removed copy
    are not kept.

.PARAMETER Project
    Only remove copies whose work is merged into work/<Project>. Used by /devops-project-archive. Exits 2 if neither
    refs/heads/work/<Project> nor refs/remotes/origin/work/<Project> exists.

.PARAMETER MinIdleHours
    Minimum idle time before a copy is eligible. Default 12, so a parent session still in a fix round keeps its copies.

.PARAMETER RepoRoot
    Any folder inside the repository (main checkout or any worktree). Default: the current folder.

.EXAMPLE
    pwsh scripts/worktree/Clean-AgentWorktrees.ps1 -WhatIf              # report only
.EXAMPLE
    pwsh scripts/worktree/Clean-AgentWorktrees.ps1                      # remove every finished copy
.EXAMPLE
    pwsh scripts/worktree/Clean-AgentWorktrees.ps1 -Project ebilling-r1 # archive time: this project's copies only
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$Project,
    [ValidateRange(0, 8760)][int]$MinIdleHours = 12,
    [string]$RepoRoot = (Get-Location).Path
)

$ErrorActionPreference = 'Stop'

$common = & git -C $RepoRoot rev-parse --path-format=absolute --git-common-dir 2>$null
if ($LASTEXITCODE -ne 0 -or -not $common) { Write-Output "Not inside a git repository: $RepoRoot"; exit 2 }
$main = Split-Path -Parent ([IO.Path]::GetFullPath(($common -replace '/', '\')))
$base = Join-Path $main '.claude\worktrees'
if (-not (Test-Path -LiteralPath $base)) { Write-Output "No $base - nothing to do."; exit 0 }
$safe = Join-Path $PSScriptRoot 'Remove-WorktreeSafe.ps1'
$pwsh = (Get-Process -Id $PID).Path

if ($Project) {
    $projectRefs = @("refs/heads/work/$Project", "refs/remotes/origin/work/$Project")
    $found = @($projectRefs | Where-Object { & git -C $main rev-parse --verify -q $_ 2>$null | Out-Null; $LASTEXITCODE -eq 0 })
    if (-not $found) { Write-Output "No branch work/$Project (local or origin) - check the project slug."; exit 2 }
}

# --- registered worktrees -------------------------------------------------------------------------------------------
$porcelain = & git -C $main worktree list --porcelain 2>$null
if ($LASTEXITCODE -ne 0) { Write-Output 'git worktree list failed - nothing touched.'; exit 2 }
$worktrees = [Collections.Generic.List[hashtable]]::new()
$cur = $null
foreach ($line in $porcelain) {
    if ($line -like 'worktree *') {
        $cur = @{ Path = [IO.Path]::GetFullPath(($line.Substring(9) -replace '/', '\')); Head = $null; Branch = $null; Locked = $false; Prunable = $false }
        $worktrees.Add($cur)
    }
    elseif ($null -eq $cur) { continue }
    elseif ($line -like 'HEAD *') { $cur.Head = $line.Substring(5) }
    elseif ($line -like 'branch *') { $cur.Branch = $line.Substring(7) }
    elseif ($line -eq 'locked' -or $line -like 'locked *') { $cur.Locked = $true }
    elseif ($line -eq 'prunable' -or $line -like 'prunable *') { $cur.Prunable = $true }
}
$prefix = $base.TrimEnd('\') + '\'
function Test-UnderBase([string]$p) {
    $p.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -and -not $p.Substring($prefix.Length).Contains('\')
}
$agentCopies = @($worktrees | Where-Object { Test-UnderBase $_.Path })
$agentBranches = @($agentCopies | Where-Object { $_.Branch } | ForEach-Object { $_.Branch })

function Test-Harvested([hashtable]$wt) {
    $refs = @(& git -C $main for-each-ref --contains $wt.Head --format='%(refname)' refs/heads refs/remotes 2>$null)
    if ($LASTEXITCODE -ne 0) { return $false }
    if ($Project) {
        $onMaster = $refs | Where-Object { $_ -in @('refs/heads/master', 'refs/remotes/origin/master') }
        return (-not $onMaster) -and [bool]($refs | Where-Object { $_ -in $projectRefs })
    }
    # another agent copy's branch does not count: that copy may be removed, and nobody merged the work
    return [bool]($refs | Where-Object { $_ -notin $agentBranches -and $_ -notlike 'refs/remotes/*/HEAD' })
}

function Get-IdleTime([hashtable]$wt) {
    # hours since the copy last moved: newest of the HEAD commit and the reflog (checkout, commit, reset)
    $ct = & git -C $wt.Path log -1 --format=%ct 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $ct) { return $null }
    $newest = [DateTimeOffset]::FromUnixTimeSeconds([long]$ct).UtcDateTime
    $gitDir = & git -C $wt.Path rev-parse --path-format=absolute --git-dir 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $gitDir) { return $null }
    $reflog = Join-Path ($gitDir -replace '/', '\') 'logs\HEAD'
    if (Test-Path -LiteralPath $reflog) {
        $t = (Get-Item -LiteralPath $reflog -Force).LastWriteTimeUtc
        if ($t -gt $newest) { $newest = $t }
    }
    return ([DateTime]::UtcNow - $newest).TotalHours
}

# --- classify --------------------------------------------------------------------------------------------------------
$kept = [Collections.Generic.List[string]]::new()
$eligible = [Collections.Generic.List[string]]::new()
foreach ($wt in $agentCopies) {
    $name = Split-Path -Leaf $wt.Path
    if (-not (Test-Path -LiteralPath $wt.Path)) { continue }   # missing folder: handled by the prune step below
    if ($wt.Locked) { $kept.Add("$name : locked (agent running)"); continue }
    if (-not $wt.Head) { $kept.Add("$name : no HEAD"); continue }
    $idle = Get-IdleTime $wt
    if ($null -eq $idle) { $kept.Add("$name : cannot read its last commit"); continue }
    if ($idle -lt $MinIdleHours) { $kept.Add(("$name : active {0:N1}h ago (< {1}h)" -f $idle, $MinIdleHours)); continue }
    if (-not (Test-Harvested $wt)) {
        $what = if ($Project) { "not merged into work/$Project (or already on master)" } else { 'work not merged into any other branch' }
        $kept.Add("$name : $what"); continue
    }
    $status = @(& git -C $wt.Path status --porcelain 2>&1)
    if ($LASTEXITCODE -ne 0) { $kept.Add("$name : git status failed"); continue }
    $warn = @($status | Where-Object { $_ -is [Management.Automation.ErrorRecord] })
    if ($warn) { $kept.Add("$name : git status warned ($($warn[0].ToString().Trim()))"); continue }
    if ($status) { $kept.Add("$name : uncommitted changes ($($status.Count))"); continue }
    $eligible.Add($wt.Path)
}

# --- remove: one path per child call (one path, so -File binds it correctly), so a refusal never blocks the rest ----
$removed = 0; $failed = [Collections.Generic.List[string]]::new()
foreach ($p in $eligible) {
    $a = @('-NoProfile', '-NonInteractive', '-File', $safe, '-Path', $p)
    if ($WhatIfPreference) { $a += '-WhatIf' }
    $out = & $pwsh @a 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { $failed.Add("$(Split-Path -Leaf $p) : $($out.Trim() -replace '\s+', ' ')") }
    elseif (-not $WhatIfPreference) { $removed++ }
}

# --- repo-wide housekeeping: only without -Project -------------------------------------------------------------------
$orphansRemoved = 0; $orphansLeft = [Collections.Generic.List[string]]::new(); $pruneNote = $null
if (-not $Project) {
    $registered = @($worktrees | ForEach-Object { $_.Path })
    foreach ($d in (Get-ChildItem -LiteralPath $base -Directory -Force)) {
        if ($registered -contains $d.FullName) { continue }
        if ($d.Attributes -band [IO.FileAttributes]::ReparsePoint) { $orphansLeft.Add("$($d.Name) : is a link"); continue }
        if (Get-ChildItem -LiteralPath $d.FullName -Force | Select-Object -First 1) { $orphansLeft.Add("$($d.Name) : not registered and not empty"); continue }
        if ($PSCmdlet.ShouldProcess($d.FullName, 'Remove empty unregistered folder')) {
            [IO.Directory]::Delete($d.FullName, $false); $orphansRemoved++
        }
    }
    # `git worktree prune` is repo-wide: run it only when nothing outside .claude\worktrees is prunable
    $outside = @($worktrees | Where-Object { $_.Prunable -and -not (Test-UnderBase $_.Path) } | ForEach-Object { $_.Path })
    if ($outside) { $pruneNote = "git worktree prune SKIPPED: prunable worktree(s) outside .claude\worktrees, for a human: $($outside -join '; ')" }
    elseif ($PSCmdlet.ShouldProcess($main, 'git worktree prune')) { & git -C $main worktree prune 2>&1 | Out-Null }
}

# --- report ------------------------------------------------------------------------------------------------------------
$scope = if ($Project) { " (project $Project)" } else { '' }
$n = if ($WhatIfPreference) { $eligible.Count } else { $removed }
$verb = if ($WhatIfPreference) { 'would remove' } else { 'removed' }
Write-Output ("Agent copies{0}: {1} registered under {2}; {3} {4}; {5} kept; {6} failed." -f $scope, $agentCopies.Count, $base, $verb, $n, $kept.Count, $failed.Count)
if ($WhatIfPreference) { $eligible | ForEach-Object { "  would remove: $(Split-Path -Leaf $_)" } }
$kept | ForEach-Object { "  kept: $_" }
$failed | ForEach-Object { "  FAILED: $_" }
if (-not $Project -and -not $WhatIfPreference) { Write-Output "Empty unregistered folders removed: $orphansRemoved" }
$orphansLeft | ForEach-Object { "  left for a human: $_" }
if ($pruneNote) { Write-Output "  $pruneNote" }
if ($failed.Count) { exit 1 }
exit 0
