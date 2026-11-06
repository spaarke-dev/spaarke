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
      3. Its HEAD commit is reachable from a branch other than its own and other than another agent's
         worktree-agent-* branch, i.e. the parent session has merged the work. With -Project, that branch must be
         work/<Project> (local or origin).
      4. `git status` succeeds and shows no uncommitted or untracked changes.

    Removal goes through Remove-WorktreeSafe.ps1, one path per call. That script re-checks the lock and the dirty state
    at removal time, unlinks junctions without following them (G-20) and never falls back to a recursive delete. The
    copy's branch is kept, so no commit is lost.

    Unregistered folders under .claude\worktrees: an EMPTY one is removed with a non-recursive delete; anything else is
    listed for a human. Worktrees outside .claude\worktrees (project worktrees, C:\wt*) are never considered.

.PARAMETER Project
    Only remove copies whose work is merged into work/<Project>. Used by /devops-project-archive.

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

# --- registered worktrees -------------------------------------------------------------------------------------------
$worktrees = [Collections.Generic.List[hashtable]]::new()
$cur = $null
foreach ($line in (& git -C $main worktree list --porcelain)) {
    if ($line -like 'worktree *') {
        $cur = @{ Path = [IO.Path]::GetFullPath(($line.Substring(9) -replace '/', '\')); Head = $null; Branch = $null; Locked = $false }
        $worktrees.Add($cur)
    }
    elseif ($null -eq $cur) { continue }
    elseif ($line -like 'HEAD *') { $cur.Head = $line.Substring(5) }
    elseif ($line -like 'branch *') { $cur.Branch = $line.Substring(7) }
    elseif ($line -eq 'locked' -or $line -like 'locked *') { $cur.Locked = $true }
}
$prefix = $base.TrimEnd('\') + '\'
$agentCopies = @($worktrees | Where-Object {
        $_.Path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -and
        -not $_.Path.Substring($prefix.Length).Contains('\')
    })

function Test-Harvested([hashtable]$wt) {
    $refs = @(& git -C $main for-each-ref --contains $wt.Head --format='%(refname)' refs/heads refs/remotes 2>$null)
    if ($LASTEXITCODE -ne 0) { return $false }
    if ($Project) {
        return [bool]($refs | Where-Object { $_ -eq "refs/heads/work/$Project" -or $_ -eq "refs/remotes/origin/work/$Project" })
    }
    return [bool]($refs | Where-Object { $_ -ne $wt.Branch -and $_ -notlike 'refs/heads/worktree-agent-*' -and $_ -notlike 'refs/remotes/*/HEAD' })
}

function Get-IdleTime([hashtable]$wt) {
    # hours since the copy last moved
    $ct = & git -C $wt.Path log -1 --format=%ct 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $ct) { return $null }
    $newest = [DateTimeOffset]::FromUnixTimeSeconds([long]$ct).UtcDateTime
    $gitDir = & git -C $wt.Path rev-parse --path-format=absolute --git-dir 2>$null
    if ($gitDir) {
        $reflog = Join-Path ($gitDir -replace '/', '\') 'logs\HEAD'
        if (Test-Path -LiteralPath $reflog) {
            $t = (Get-Item -LiteralPath $reflog -Force).LastWriteTimeUtc
            if ($t -gt $newest) { $newest = $t }
        }
    }
    return ([DateTime]::UtcNow - $newest).TotalHours
}

# --- classify --------------------------------------------------------------------------------------------------------
$kept = [Collections.Generic.List[string]]::new()
$eligible = [Collections.Generic.List[string]]::new()
foreach ($wt in $agentCopies) {
    $name = Split-Path -Leaf $wt.Path
    if (-not (Test-Path -LiteralPath $wt.Path)) { continue }   # missing folder: `git worktree prune` below handles it
    if ($wt.Locked) { $kept.Add("$name : locked (agent running)"); continue }
    if (-not $wt.Head) { $kept.Add("$name : no HEAD"); continue }
    $idle = Get-IdleTime $wt
    if ($null -eq $idle) { $kept.Add("$name : cannot read its last commit"); continue }
    if ($idle -lt $MinIdleHours) { $kept.Add(("$name : active {0:N1}h ago (< {1}h)" -f $idle, $MinIdleHours)); continue }
    if (-not (Test-Harvested $wt)) {
        $what = if ($Project) { "not merged into work/$Project" } else { 'work not merged into any other branch' }
        $kept.Add("$name : $what"); continue
    }
    $status = & git -C $wt.Path status --porcelain 2>$null
    if ($LASTEXITCODE -ne 0) { $kept.Add("$name : git status failed"); continue }
    if ($status) { $kept.Add("$name : uncommitted changes ($(@($status).Count))"); continue }
    $eligible.Add($wt.Path)
}

# --- remove (one path per call, so one refusal never blocks the rest) -----------------------------------------------
$removed = 0; $failed = [Collections.Generic.List[string]]::new()
foreach ($p in $eligible) {
    $out = & $safe -Path $p -WhatIf:$WhatIfPreference *>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { $failed.Add("$(Split-Path -Leaf $p) : $($out.Trim() -replace '\s+', ' ')") }
    elseif (-not $WhatIfPreference) { $removed++ }
}

# --- unregistered folders ---------------------------------------------------------------------------------------------
$registered = @($worktrees | ForEach-Object { $_.Path })
$orphansRemoved = 0; $orphansLeft = [Collections.Generic.List[string]]::new()
foreach ($d in (Get-ChildItem -LiteralPath $base -Directory -Force)) {
    if ($registered -contains $d.FullName) { continue }
    if ($d.Attributes -band [IO.FileAttributes]::ReparsePoint) { $orphansLeft.Add("$($d.Name) : is a link"); continue }
    if (Get-ChildItem -LiteralPath $d.FullName -Force | Select-Object -First 1) { $orphansLeft.Add("$($d.Name) : not registered and not empty"); continue }
    if ($PSCmdlet.ShouldProcess($d.FullName, 'Remove empty unregistered folder')) {
        [IO.Directory]::Delete($d.FullName, $false); $orphansRemoved++
    }
}
if ($PSCmdlet.ShouldProcess($main, 'git worktree prune')) { & git -C $main worktree prune 2>&1 | Out-Null }

# --- report ------------------------------------------------------------------------------------------------------------
$verb = if ($WhatIfPreference) { 'would remove' } else { 'removed' }
Write-Output ("Agent copies: {0} registered under {1}; {2} {3}; {4} kept; {5} failed." -f $agentCopies.Count, $base, $verb, $(if ($WhatIfPreference) { $eligible.Count } else { $removed }), $kept.Count, $failed.Count)
if ($WhatIfPreference) { $eligible | ForEach-Object { "  would remove: $(Split-Path -Leaf $_)" } }
$kept | ForEach-Object { "  kept: $_" }
$failed | ForEach-Object { "  FAILED: $_" }
if (-not $WhatIfPreference) { Write-Output "Empty unregistered folders removed: $orphansRemoved" }
$orphansLeft | ForEach-Object { "  left for a human: $_" }
if ($failed.Count) { exit 1 }
exit 0
