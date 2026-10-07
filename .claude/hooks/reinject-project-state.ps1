# SessionStart hook (matcher: compact) — re-injects the active project's working state after compaction.
# Root CLAUDE.md reloads after compaction on its own; the project's current-task.md and its CLAUDE.md
# standing directives do not. Whatever this script writes to stdout is added to Claude's context.
# It must never fail the session: every path exits 0, and errors go to stderr only.

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
$maxChars = 15000

function Write-CappedSection([string]$Title, [string]$Text) {
    if (-not $Text) { return }
    if ($Text.Length -gt $maxChars) {
        $Text = $Text.Substring(0, $maxChars) + "`n… (truncated at $maxChars chars — read the file for the rest)"
    }
    Write-Output "### $Title"
    Write-Output $Text
    Write-Output ""
}

try {
    $branch = (git rev-parse --abbrev-ref HEAD 2>$null)
    if (-not $branch -or $branch -notmatch '^work/(.+)$') { exit 0 }
    $project = $Matches[1]
    $root = (git rev-parse --show-toplevel 2>$null)
    $dir = Join-Path $root "projects/$project"
    if (-not (Test-Path $dir)) { exit 0 }

    Write-Output "## Re-injected after compaction: project '$project' (branch $branch)"
    Write-Output ""

    $task = Join-Path $dir 'current-task.md'
    if (Test-Path $task) { Write-CappedSection "projects/$project/current-task.md" (Get-Content $task -Raw) }

    $claude = Join-Path $dir 'CLAUDE.md'
    if (Test-Path $claude) {
        $content = Get-Content $claude -Raw
        $m = [regex]::Match($content, '(?ms)^## Standing directives & gotchas.*?(?=^## |\z)')
        if ($m.Success) { Write-CappedSection "projects/$project/CLAUDE.md — Standing directives & gotchas" $m.Value }
    }
}
catch {
    [Console]::Error.WriteLine("reinject-project-state: $($_.Exception.Message)")
}
exit 0
