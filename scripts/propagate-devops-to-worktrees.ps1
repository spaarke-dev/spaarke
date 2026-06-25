<#
.SYNOPSIS
    Propagate master into existing Spaarke worktrees so they pick up the /devops-* skills and hooks.

.DESCRIPTION
    After spaarke-devops-project-tracking-r1 merged to master, each existing worktree's feature branch
    still lacks the new .claude/skills/devops-*/ files and the Portfolio Hook sections injected into
    9 existing skills. This script iterates over all spaarke-wt-* worktrees and offers to merge
    origin/master into each one's branch.

    Per worktree:
      1. Skip if dirty (uncommitted changes) unless -Force is passed
      2. Skip if the worktree is already up-to-date with master
      3. Show diff stat (files changed by merge)
      4. Prompt before merging (unless -Auto)
      5. Run `git fetch origin && git merge origin/master --no-edit` from inside the worktree
      6. Report

    The main repo at c:/code_files/spaarke and this project's worktree are skipped automatically.
    Locked agent worktrees (.claude/worktrees/agent-*) are also skipped.

.PARAMETER Auto
    Merge into every eligible worktree without prompting per worktree.

.PARAMETER Force
    Override the "skip if dirty" safety. Dangerous — uncommitted changes may end up merged
    with master changes in confusing ways.

.PARAMETER DryRun
    Show what would happen without executing any merges.

.PARAMETER WorktreeFilter
    Substring filter — only consider worktrees whose path contains this string. e.g. -WorktreeFilter "smart-todo"

.EXAMPLE
    .\scripts\propagate-devops-to-worktrees.ps1
    Interactive: prompts per worktree.

.EXAMPLE
    .\scripts\propagate-devops-to-worktrees.ps1 -DryRun
    Shows what would happen across all eligible worktrees.

.EXAMPLE
    .\scripts\propagate-devops-to-worktrees.ps1 -Auto
    Merges into every eligible worktree without prompting.

.EXAMPLE
    .\scripts\propagate-devops-to-worktrees.ps1 -WorktreeFilter smart-todo -Auto
    Auto-merge into worktrees whose path contains "smart-todo".

.NOTES
    Run from the main repo OR from this worktree. The script invokes `git -C <path>` so it's
    location-independent.

    The script never auto-pushes the merged branches — you control when to push each one.
#>

[CmdletBinding()]
param(
    [switch]$Auto,
    [switch]$Force,
    [switch]$DryRun,
    [string]$WorktreeFilter
)

# Find the main repo root (so we can run worktree list from there)
$mainRepo = (& git rev-parse --git-common-dir 2>$null | Split-Path -Parent)
if (-not $mainRepo -or -not (Test-Path $mainRepo)) {
    Write-Error "Could not locate main repo. Run from a Spaarke worktree."
    exit 1
}

Write-Host ""
Write-Host "=== Propagate master into worktrees ===" -ForegroundColor Cyan
Write-Host "Main repo: $mainRepo"
if ($DryRun) { Write-Host "Mode: DRY RUN" -ForegroundColor Yellow }
if ($Auto)   { Write-Host "Mode: AUTO (no per-worktree prompts)" -ForegroundColor Yellow }
if ($Force)  { Write-Host "Mode: FORCE (will merge into dirty worktrees too)" -ForegroundColor Red }
Write-Host ""

# Enumerate worktrees
$worktreesRaw = & git -C $mainRepo worktree list --porcelain
$worktrees = @()
$current = $null
foreach ($line in $worktreesRaw) {
    if ($line -match '^worktree (.+)$') {
        if ($current) { $worktrees += $current }
        $current = @{ path = $matches[1] }
    } elseif ($line -match '^branch refs/heads/(.+)$') {
        $current.branch = $matches[1]
    } elseif ($line -match '^locked') {
        $current.locked = $true
    }
}
if ($current) { $worktrees += $current }

# Filter to active project worktrees
$eligible = $worktrees | Where-Object {
    $_.path -like '*spaarke-wt-*' -and             # only spaarke-wt-* paths
    -not $_.locked -and                              # not locked agent worktrees
    $_.path -notlike '*spaarke-devops-project-tracking-r1*' -and  # not THIS worktree
    -not [string]::IsNullOrEmpty($_.branch)          # has a branch
}

if ($WorktreeFilter) {
    $eligible = $eligible | Where-Object { $_.path -like "*$WorktreeFilter*" }
}

if ($eligible.Count -eq 0) {
    Write-Host "No eligible worktrees found." -ForegroundColor Yellow
    exit 0
}

Write-Host "Eligible worktrees: $($eligible.Count)" -ForegroundColor Green
Write-Host ""

# Fetch master fresh, once
Write-Host "Fetching origin/master..." -ForegroundColor Gray
& git -C $mainRepo fetch origin master 2>&1 | Out-Null

# Process each
$results = @()
$skipped = 0
$merged = 0
$failed = 0

foreach ($wt in $eligible) {
    $path   = $wt.path
    $branch = $wt.branch
    Write-Host ""
    Write-Host "----------------------------------------------------------------" -ForegroundColor DarkGray
    Write-Host "Worktree: $path" -ForegroundColor White
    Write-Host "Branch:   $branch" -ForegroundColor Gray

    # 1. Check dirty
    $dirty = & git -C $path status --porcelain
    if ($dirty -and -not $Force) {
        Write-Host "  SKIP: working tree dirty ($($dirty.Count) files). Use -Force to override." -ForegroundColor Yellow
        $skipped++
        continue
    }

    # 2. Fetch + check if behind master
    & git -C $path fetch origin master 2>&1 | Out-Null
    $behind = (& git -C $path rev-list --count HEAD..origin/master).Trim()
    if ([int]$behind -eq 0) {
        Write-Host "  SKIP: already up-to-date with origin/master" -ForegroundColor Gray
        $skipped++
        continue
    }

    # 3. Show diff stat (which files would change)
    $diffStat = & git -C $path diff HEAD origin/master --stat 2>&1 | Select-Object -Last 1
    Write-Host "  Behind master by $behind commits"
    Write-Host "  Change summary: $diffStat" -ForegroundColor Gray

    if ($DryRun) {
        Write-Host "  DRY RUN: would run 'git -C `"$path`" merge origin/master --no-edit'" -ForegroundColor Yellow
        $skipped++
        continue
    }

    # 4. Prompt if not Auto
    if (-not $Auto) {
        $answer = Read-Host "  Merge origin/master into $branch ? [y/N]"
        if ($answer -ne 'y' -and $answer -ne 'Y') {
            Write-Host "  SKIPPED by user" -ForegroundColor Yellow
            $skipped++
            continue
        }
    }

    # 5. Merge
    Write-Host "  Merging..." -NoNewline
    $output = & git -C $path merge origin/master --no-edit 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host " OK" -ForegroundColor Green
        $merged++
        $results += [pscustomobject]@{ Worktree = (Split-Path $path -Leaf); Branch = $branch; Status = 'merged'; Note = '' }
    } else {
        Write-Host " FAILED" -ForegroundColor Red
        Write-Host "    $($output | Out-String)" -ForegroundColor Red
        Write-Host "    Worktree LEFT in conflict state. Resolve manually:" -ForegroundColor Red
        Write-Host "      cd `"$path`""
        Write-Host "      # resolve conflicts, then:"
        Write-Host "      git add ."
        Write-Host "      git commit --no-edit"
        $failed++
        $results += [pscustomobject]@{ Worktree = (Split-Path $path -Leaf); Branch = $branch; Status = 'CONFLICT'; Note = 'manual resolve required' }
    }
}

# Summary
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "Summary: $merged merged, $skipped skipped, $failed failed" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan

if ($results.Count -gt 0) {
    Write-Host ""
    $results | Format-Table -AutoSize
}

if ($merged -gt 0) {
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Green
    Write-Host "  - The merged worktrees now have all /devops-* skills + hooks + docs available locally."
    Write-Host "  - Push each merged branch when ready (script does NOT auto-push):"
    Write-Host "      cd <worktree-path> && git push"
}

if ($failed -gt 0) {
    Write-Host ""
    Write-Host "Conflicts in $failed worktree(s) need manual resolution." -ForegroundColor Yellow
    exit 1
}
