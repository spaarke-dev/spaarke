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
    $root = (git rev-parse --show-toplevel 2>$null)
    if (-not $root) { exit 0 }
    $branch = (git rev-parse --abbrev-ref HEAD 2>$null)

    # Identify the project: the work/<project> branch first, then the spaarke-wt-<project> worktree folder.
    $candidates = @()
    if ($branch -match '^work/(.+)$') { $candidates += $Matches[1] }
    $leaf = Split-Path $root -Leaf
    if ($leaf -match '^spaarke-wt-(.+)$') { $candidates += $Matches[1] }
    $project = $null
    foreach ($c in $candidates) {
        if (Test-Path (Join-Path $root "projects/$c")) { $project = $c; break }
    }
    if (-not $project) { exit 0 }
    $dir = Join-Path $root "projects/$project"

    Write-Output "## Re-injected after compaction: project '$project' (branch $branch)"
    Write-Output ""

    $task = Join-Path $dir 'current-task.md'
    if (Test-Path $task) { Write-CappedSection "projects/$project/current-task.md" (Get-Content $task -Raw) }

    $claude = Join-Path $dir 'CLAUDE.md'
    if (Test-Path $claude) {
        $content = Get-Content $claude -Raw
        # Sections that carry standing rules: "Standing directives & gotchas" (current projects) and the
        # template's "2. Binding rules", "3. Owner directives and standing decisions", "6. Gotchas".
        $pattern = '(?ms)^## (?:\d+\.\s*)?(?:Standing directives|Owner directives|Binding rules|Gotchas)[^\n]*\n.*?(?=^## |\z)'
        $sections = ([regex]::Matches($content, $pattern) | ForEach-Object { $_.Value }) -join "`n"
        if ($sections) { Write-CappedSection "projects/$project/CLAUDE.md — standing rules and gotchas" $sections }
    }
}
catch {
    [Console]::Error.WriteLine("reinject-project-state: $($_.Exception.Message)")
}
exit 0
