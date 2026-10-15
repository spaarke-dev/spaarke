#Requires -Version 7.0
<#
.SYNOPSIS
    Removes git worktrees without ever deleting THROUGH a junction or symlink.

.DESCRIPTION
    Unlinks every reparse point (junction, directory symlink, file symlink) inside each worktree as a LINK ONLY,
    never descending into one, and then runs `git worktree remove`. It never falls back to `rm -rf` or
    `Remove-Item -Recurse`: a path git cannot remove is reported for a human.

    Incident (2026-10-13): agents replaced <task-worktree>\src\client\shared\Spaarke.UI.Components\node_modules with
    an NTFS junction to ANOTHER worktree's real node_modules. A bulk cleanup (`git worktree remove --force` plus an
    `rm -rf` fallback) deleted through the junction, emptied the other worktree's node_modules and, through npm's
    own @spaarke/auth and @spaarke/sdap-client junctions, 70 tracked files under Spaarke.Auth / Spaarke.SdapClient.
    The PreToolUse hook scripts/quality/guard-worktree-links.py blocks creating such links and blocks a bare recursive
    delete of a worktree root; this script is the safe way to remove one.

    Refuses (exit 1, nothing touched) when a path is not a registered worktree root, is the main worktree, is itself a
    reparse point, is LOCKED (`git worktree unlock <path>` first; -Force does not override a lock), has a folder that
    cannot be enumerated (fails closed: a link we cannot see is a link we cannot unlink), or (without -Force) has
    uncommitted changes. All paths are validated and every worktree is scanned (read-only) before any link is removed.

    Reports how many links were removed and lists every link whose target resolves OUTSIDE its worktree; those are
    the dangerous ones.

.PARAMETER Path
    One or more worktree root folders.

.PARAMETER Force
    Remove a worktree that has uncommitted changes (they are discarded) and pass --force to `git worktree remove`.

.EXAMPLE
    pwsh scripts/worktree/Remove-WorktreeSafe.ps1 -Path C:\wt094

.EXAMPLE
    pwsh scripts/worktree/Remove-WorktreeSafe.ps1 -Path C:\wt094, C:\wt096 -Force

.EXAMPLE
    pwsh scripts/worktree/Remove-WorktreeSafe.ps1 -Path C:\wt094 -WhatIf   # lists the links and the git call; changes nothing
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][string[]]$Path,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

function ConvertTo-NormalPath([string]$p) {
    $full = [System.IO.Path]::GetFullPath(($p -replace '/', '\'))
    $root = [System.IO.Path]::GetPathRoot($full)
    if ($full.Length -gt $root.Length) { $full = $full.TrimEnd('\') }
    return $full
}

function Get-RegisteredWorktree([string]$dir) {
    # Registered worktree roots, main worktree first. $null when $dir is not inside a git repository.
    $lines = & git -C $dir worktree list --porcelain 2>$null
    if ($LASTEXITCODE -ne 0) { return $null }
    $roots = foreach ($l in $lines) { if ($l -like 'worktree *') { ConvertTo-NormalPath $l.Substring(9) } }
    return , @($roots)
}

function Get-LockedWorktree([string]$dir) {
    # Registered worktree roots that carry a `locked` line in `git worktree list --porcelain`, as root -> reason.
    $locked = @{}
    $lines = & git -C $dir worktree list --porcelain 2>$null
    if ($LASTEXITCODE -ne 0) { return $locked }
    $current = $null
    foreach ($l in $lines) {
        if ($l -like 'worktree *') { $current = ConvertTo-NormalPath $l.Substring(9) }
        elseif ($current -and ($l -eq 'locked' -or $l -like 'locked *')) { $locked[$current] = $l.Substring(6).Trim() }
    }
    return $locked
}

function Test-IsUnder([string]$child, [string]$root) {
    return $child.StartsWith($root.TrimEnd('\') + '\', [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-LinkTargetFull([System.IO.FileSystemInfo]$item) {
    $t = $item.LinkTarget
    if ([string]::IsNullOrEmpty($t)) { return $null }
    if ($t.StartsWith('\??\')) { $t = $t.Substring(4) }
    if (-not [System.IO.Path]::IsPathRooted($t)) { $t = Join-Path (Split-Path $item.FullName -Parent) $t }
    return ConvertTo-NormalPath $t
}

# --- 1. validate every path before touching anything ------------------------------------------------------
$plan = @()
$refusals = @()
foreach ($p in $Path) {
    $full = ConvertTo-NormalPath $p
    if (-not (Test-Path -LiteralPath $full -PathType Container)) { $refusals += "$full : not an existing folder"; continue }
    $registered = Get-RegisteredWorktree $full
    if (-not $registered) { $refusals += "$full : not inside a git repository"; continue }
    $match = @($registered | Where-Object { $_ -ieq $full })
    if ($match.Count -eq 0) { $refusals += "$full : not a registered worktree root (git worktree list)"; continue }
    if ($registered[0] -ieq $full) { $refusals += "$full : is the MAIN worktree; never removed by this script"; continue }
    if ((Get-Item -LiteralPath $full -Force).Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
        $refusals += "$full : the worktree root is itself a link; resolve it by hand"; continue
    }
    $locks = Get-LockedWorktree $full
    $lockKey = @($locks.Keys | Where-Object { $_ -ieq $full })
    if ($lockKey.Count) {
        $why = $locks[$lockKey[0]]
        $refusals += "$full : locked$(if ($why) { " ($why)" }) - run ``git worktree unlock $full`` first (-Force does not override a lock)"; continue
    }
    if (-not $Force) {
        $dirty = & git -C $full status --porcelain 2>&1
        if ($LASTEXITCODE -ne 0) { $refusals += "$full : git status failed: $dirty"; continue }
        if ($dirty) { $refusals += "$full : has uncommitted changes (use -Force to discard them)"; continue }
    }
    $plan += $full
}
if ($refusals) {
    foreach ($r in $refusals) { [Console]::Error.WriteLine("REFUSED  $r") }
    [Console]::Error.WriteLine('Nothing was touched.')
    exit 1
}

# --- 2. scan every worktree (read-only) and list its reparse points, never descending into one -----------------
# A folder we cannot enumerate may hold a link we would miss, and git would then delete through it: refuse before
# anything is touched.
$scans = @()
foreach ($wt in $plan) {
    $links = [System.Collections.Generic.List[object]]::new()
    $stack = [System.Collections.Generic.Stack[System.IO.DirectoryInfo]]::new()
    $stack.Push([System.IO.DirectoryInfo]::new($wt))
    while ($stack.Count -gt 0) {
        $dir = $stack.Pop()
        try { $entries = @($dir.EnumerateFileSystemInfos()) } catch {
            $msg = "cannot enumerate $($dir.FullName): $($_.Exception.Message)"
            Write-Warning $msg
            $refusals += "$wt : $msg"
            continue
        }
        foreach ($e in $entries) {
            $isLink = [bool]($e.Attributes -band [System.IO.FileAttributes]::ReparsePoint)
            $isDir = [bool]($e.Attributes -band [System.IO.FileAttributes]::Directory)
            if (-not $isLink) {
                if ($isDir) { $stack.Push([System.IO.DirectoryInfo]$e) }
                continue
            }
            $target = Get-LinkTargetFull $e
            if (-not $isDir -and -not $target) { continue }   # a cloud placeholder, not a link: git deals with it
            $label = if ($target) { "$($e.FullName) -> $target" } else { "$($e.FullName) -> (unreadable target)" }
            $links.Add([pscustomobject]@{
                Path = $e.FullName; IsDir = $isDir; Label = $label
                Outside = (-not $target -or -not (Test-IsUnder $target $wt))
            })
        }
    }
    $scans += [pscustomobject]@{ Wt = $wt; Links = $links }
}
if ($refusals) {
    foreach ($r in $refusals) { [Console]::Error.WriteLine("REFUSED  $r") }
    [Console]::Error.WriteLine('Nothing was touched.')
    exit 1
}

# --- 3. unlink every reparse point found (the link only, never its target) ---------------------------------------
$failed = $false
foreach ($scan in $scans) {
    $removed = 0
    foreach ($l in $scan.Links) {
        if ($PSCmdlet.ShouldProcess($l.Path, 'Remove link (the target is not touched)')) {
            try {
                if ($l.IsDir) { [System.IO.Directory]::Delete($l.Path, $false) } else { [System.IO.File]::Delete($l.Path) }
                $removed++
            } catch {
                [Console]::Error.WriteLine("FAILED to unlink $($l.Path): $($_.Exception.Message)")
                $failed = $true
                break                                         # no further unlinks in this worktree
            }
        }
    }
    Write-Host "$($scan.Wt) : $removed link(s) removed"
    $outside = @($scan.Links | Where-Object { $_.Outside })
    if ($outside.Count) {
        Write-Host "  links pointing OUTSIDE this worktree ($($outside.Count)) - these would have been deleted through:"
        foreach ($o in $outside) { Write-Host "    $($o.Label)" }
    }
}
if ($failed) {
    [Console]::Error.WriteLine('A link could not be removed; git worktree remove was NOT run. Resolve by hand.')
    exit 1
}

# --- 4. git removes the (now link-free) tree; no fallback delete ------------------------------------------
foreach ($wt in $plan) {
    $gitArgs = [System.Collections.Generic.List[string]]::new()
    $gitArgs.AddRange([string[]]@('worktree', 'remove'))
    if ($Force) { $gitArgs.Add('--force') }
    $gitArgs.Add($wt)
    if ($PSCmdlet.ShouldProcess($wt, "git $($gitArgs -join ' ')")) {
        $mainRoot = (Get-RegisteredWorktree $wt)[0]
        $out = & git -C $mainRoot @gitArgs 2>&1
        if ($LASTEXITCODE -ne 0) {
            [Console]::Error.WriteLine("git worktree remove failed for ${wt}:`n$out")
            [Console]::Error.WriteLine('Not falling back to a recursive delete. Report this path to a human.')
            exit 1
        }
        Write-Host "$wt : worktree removed"
    }
}
exit 0
