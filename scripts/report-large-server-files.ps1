<#
.SYNOPSIS
    OBSERVATION report of large src/server C# files (never fails — informational only).

.DESCRIPTION
    Replaces the retired GodClassGuardTests LOC ratchet with observation (docs/standards/COMPONENT-COMPLEXITY.md).
    Lists src/server/**/*.cs files at or above -Threshold LOC, largest first, as a prompt to EVALUATE COMPLEXITY —
    not as a verdict. Size alone is not a violation; a large cohesive file can be the right design. Use this to
    prioritize the deliberate decomposition backlog (RED-1 SpeAdmin, RED-2 ChatEndpoints, RED-4-C), NOT to gate PRs.

    Exit code is ALWAYS 0. This is observation, per ADR-038 ("coverage = observation, never a gate"), applied to size.

.PARAMETER Threshold
    LOC at/above which a file is listed as a "look at this" prompt. Default 2000.

.PARAMETER RepoRoot
    Repo root. Defaults to two levels up from this script (scripts/ -> repo root).

.EXAMPLE
    pwsh scripts/report-large-server-files.ps1
    pwsh scripts/report-large-server-files.ps1 -Threshold 1500
#>
[CmdletBinding()]
param(
    [int]$Threshold = 2000,
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

$serverRoot = Join-Path $RepoRoot 'src/server'
if (-not (Test-Path $serverRoot)) { Write-Host "src/server not found at $serverRoot"; exit 0 }

$rows = Get-ChildItem -Path $serverRoot -Recurse -Filter *.cs -File |
    Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' } |
    ForEach-Object {
        [pscustomobject]@{
            LOC  = (Get-Content -LiteralPath $_.FullName | Measure-Object -Line).Lines
            File = $_.FullName.Substring($RepoRoot.Length).TrimStart('\','/').Replace('\','/')
        }
    } |
    Where-Object { $_.LOC -ge $Threshold } |
    Sort-Object LOC -Descending

Write-Host ""
Write-Host "=== Large src/server files (LOC >= $Threshold) — OBSERVATION ONLY, not a gate ==="
Write-Host "    Size is a PROMPT to evaluate complexity/cohesion (docs/standards/COMPONENT-COMPLEXITY.md)."
Write-Host "    A large, single-responsibility file can be the right design. Decompose when RESPONSIBILITIES diverge."
Write-Host ""
if (-not $rows) {
    Write-Host "  (none at/above $Threshold LOC)"
} else {
    $rows | ForEach-Object { "{0,6}  {1}" -f $_.LOC, $_.File } | ForEach-Object { Write-Host $_ }
    Write-Host ""
    Write-Host ("  {0} file(s) >= {1} LOC. These feed the deliberate decomposition backlog — they do NOT block any PR." -f $rows.Count, $Threshold)
}
Write-Host ""
exit 0
