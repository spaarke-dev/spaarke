<#
.SYNOPSIS
    Validate markdown links — broken local file refs and (optionally) HTTP HEAD
    checks for external URLs. Tier 2 advisory check (CI/CD remediation r1, FR-A03).

.DESCRIPTION
    Scans .md files in the given directory (recursively) for two link patterns:

      1. LOCAL link: `[text](path/to/file)` where `path/to/file` does NOT start
         with `http`, `https`, `#`, or `mailto:`. Validated by resolving against
         the markdown file's parent directory (or against the repo root if the
         path is absolute, i.e. starts with `/`).

      2. EXTERNAL link: `[text](https?://...)`. Validated via HTTP HEAD request
         (10s timeout). Skipped entirely when `-NoNetwork` is passed.

    Anchor-only fragments (`[text](path#section)`) drop the fragment before
    validating the file. Inline-code spans (`` `[text](x)` ``) are ignored.
    Reference-style links (`[text][ref]` + `[ref]: url`) are NOT validated in
    this version — covers ~99% of Spaarke docs which use inline links.

    Exits 0 if all links resolve. Exits 1 if ANY broken link is found.

.PARAMETER Path
    Directory to scan (recursively). Default: current directory.

.PARAMETER NoNetwork
    Skip external HTTP HEAD validation. Local file links still checked.

.PARAMETER ExcludePattern
    Regex of paths to exclude (matched against full path). Default excludes
    node_modules, .git, dist, bin, obj, publish, TestResults, .archive.

.PARAMETER MaxExternalChecks
    Cap on external HEAD requests (default 200) to bound runtime on large repos.
    Once exceeded, remaining external URLs are reported as "skipped" (not failures).

.EXAMPLE
    pwsh ./scripts/validate-markdown-links.ps1 -Path projects/ci-cd-unit-test-remediation-r1

.EXAMPLE
    pwsh ./scripts/validate-markdown-links.ps1 -Path docs -NoNetwork

.EXAMPLE
    pwsh ./scripts/validate-markdown-links.ps1 -Path .
    # Validates entire repo (use -NoNetwork in CI to bound runtime)

.NOTES
    Author: Spaarke CI/CD (ci-cd-unit-test-remediation-r1 task CICD-044)
    Tier:   2 advisory (non-blocking; surfaces broken docs in PR comments)
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $false, Position = 0)]
    [string]$Path = '.',

    [switch]$NoNetwork,

    [string]$ExcludePattern = '(?i)[\\/](node_modules|\.git|dist|bin|obj|publish|TestResults|\.archive)([\\/]|$)',

    [int]$MaxExternalChecks = 200
)

$ErrorActionPreference = 'Stop'

# Resolve scan root
if (-not (Test-Path -LiteralPath $Path)) {
    Write-Error "Path does not exist: $Path"
    exit 2
}
$scanRoot = (Resolve-Path -LiteralPath $Path).Path

# Repo-root resolution for absolute-path link verification (paths starting with `/`).
# We climb until we find a .git directory or the filesystem root.
function Get-RepoRoot {
    param([string]$StartPath)
    $cur = Get-Item -LiteralPath $StartPath
    if (-not $cur.PSIsContainer) { $cur = $cur.Parent }
    while ($cur) {
        if (Test-Path -LiteralPath (Join-Path $cur.FullName '.git')) {
            return $cur.FullName
        }
        if (-not $cur.Parent) { return $cur.FullName }  # filesystem root
        $cur = $cur.Parent
    }
    return $StartPath
}
$repoRoot = Get-RepoRoot -StartPath $scanRoot

Write-Host "================================================"
Write-Host "Markdown Link Validator"
Write-Host "  Scan root: $scanRoot"
Write-Host "  Repo root: $repoRoot"
Write-Host "  Network:   $((-not $NoNetwork))"
Write-Host "  Max ext:   $MaxExternalChecks"
Write-Host "================================================"

# Collect .md files (filtered)
$mdFiles = Get-ChildItem -LiteralPath $scanRoot -Filter '*.md' -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch $ExcludePattern }

Write-Host "Found $($mdFiles.Count) .md files"

# Patterns
# Inline link [text](target) — non-greedy text, parens not allowed in target
# (which excludes most images-with-parens-in-url cases; acceptable trade-off)
$linkRegex = [regex]'\[(?<text>[^\]]+)\]\((?<target>[^)\s]+)(?:\s+"[^"]*")?\)'

# Inline-code span detector — used to drop spans before scanning
$inlineCodeRegex = [regex]'`[^`]+`'

# Fenced-code block detector — drop entire ```...``` blocks
$fencedCodeRegex = [regex]'(?ms)^```[\s\S]*?^```'

$results = [System.Collections.Generic.List[object]]::new()
$externalCheckCount = 0
$externalCache = @{}  # URL -> result (avoid duplicate HEAD)

foreach ($file in $mdFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw -ErrorAction SilentlyContinue
    if (-not $content) { continue }

    # Strip fenced code blocks then inline code spans (keeps line/column rough but accurate enough)
    $stripped = $fencedCodeRegex.Replace($content, { param($m) ' ' * $m.Length })
    $stripped = $inlineCodeRegex.Replace($stripped, { param($m) ' ' * $m.Length })

    foreach ($match in $linkRegex.Matches($stripped)) {
        $target = $match.Groups['target'].Value.Trim()

        # Strip surrounding angle brackets if present: <url>
        if ($target.StartsWith('<') -and $target.EndsWith('>')) {
            $target = $target.Substring(1, $target.Length - 2)
        }

        # Skip mailto and pure-anchor links
        if ($target.StartsWith('mailto:') -or $target.StartsWith('#')) {
            continue
        }

        $isExternal = $target -match '^https?://'
        $status = 'ok'
        $reason = ''

        if ($isExternal) {
            if ($NoNetwork) {
                continue  # skip silently
            }
            if ($externalCheckCount -ge $MaxExternalChecks) {
                continue  # cap hit; silently skip remainder
            }
            $externalCheckCount++

            if ($externalCache.ContainsKey($target)) {
                $cached = $externalCache[$target]
                $status = $cached.Status
                $reason = $cached.Reason
            } else {
                try {
                    $resp = Invoke-WebRequest -Uri $target -Method Head -TimeoutSec 10 -UseBasicParsing -ErrorAction Stop -MaximumRedirection 5
                    if ($resp.StatusCode -ge 200 -and $resp.StatusCode -lt 400) {
                        $status = 'ok'
                    } else {
                        $status = 'broken'
                        $reason = "HTTP $($resp.StatusCode)"
                    }
                } catch {
                    # Some servers reject HEAD; retry once with GET (range 0-0) before failing.
                    try {
                        $resp = Invoke-WebRequest -Uri $target -Method Get -TimeoutSec 10 -UseBasicParsing -ErrorAction Stop -MaximumRedirection 5 -Headers @{Range='bytes=0-0'}
                        if ($resp.StatusCode -ge 200 -and $resp.StatusCode -lt 400) {
                            $status = 'ok'
                        } else {
                            $status = 'broken'
                            $reason = "HTTP $($resp.StatusCode)"
                        }
                    } catch {
                        $status = 'broken'
                        $reason = $_.Exception.Message -replace '\s+', ' '
                        if ($reason.Length -gt 80) { $reason = $reason.Substring(0, 80) + '...' }
                    }
                }
                $externalCache[$target] = @{ Status = $status; Reason = $reason }
            }
        } else {
            # Local link — drop fragment then resolve
            $pathPart = ($target -split '#', 2)[0]
            if ([string]::IsNullOrWhiteSpace($pathPart)) { continue }  # was anchor-only

            if ($pathPart.StartsWith('/')) {
                $resolved = Join-Path $repoRoot $pathPart.TrimStart('/').Replace('/', [IO.Path]::DirectorySeparatorChar)
            } else {
                $resolved = Join-Path $file.Directory.FullName $pathPart.Replace('/', [IO.Path]::DirectorySeparatorChar)
            }

            if (-not (Test-Path -LiteralPath $resolved)) {
                $status = 'broken'
                $reason = 'file not found'
            }
        }

        if ($status -eq 'broken') {
            $results.Add([pscustomobject]@{
                File   = (Resolve-Path -LiteralPath $file.FullName -Relative)
                Target = $target
                Kind   = if ($isExternal) { 'external' } else { 'local' }
                Reason = $reason
            })
        }
    }
}

Write-Host ""
Write-Host "================================================"
Write-Host "Results"
Write-Host "================================================"
Write-Host "  Files scanned:       $($mdFiles.Count)"
Write-Host "  External checks run: $externalCheckCount"
Write-Host "  Broken links found:  $($results.Count)"
Write-Host ""

if ($results.Count -gt 0) {
    $results | Format-Table -AutoSize File, Kind, Target, Reason | Out-Host
    Write-Host ""
    Write-Host "::error::Markdown link validation FAILED — $($results.Count) broken link(s) found"
    exit 1
}

Write-Host "All markdown links resolved successfully" -ForegroundColor Green
exit 0
