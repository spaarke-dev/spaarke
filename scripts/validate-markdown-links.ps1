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
    Regex of paths to exclude (matched against full path).

    SCAN CORPUS (revised 2026-08-28, task CICD-093 / issue #849)
    ------------------------------------------------------------
    The validator governs documentation that is INTENDED TO STAY CURRENT — the
    docs a human or an agent navigates to find out how the system works today:

        root *.md (CLAUDE.md, README.md)   docs/**        .claude/** (see below)
        knowledge/**                       src/**         tests/**
        scripts/**   infrastructure/**     .github/**

    Everything below is excluded, each for a stated reason. A validator whose
    corpus is mostly documents nobody intends to keep link-current reports
    findings nobody can triage, which is operationally the same as reporting
    nothing (#849).

    | Excluded                | Why |
    |-------------------------|-----|
    | node_modules, dist, bin,| Build output and vendored code. Not authored here. |
    | obj, publish, TestResults|    |
    | .git                    | Not documentation. |
    | projects/**             | Historical project records — specs, designs, task POMLs, notes. Their links legitimately rot as the repo moves on; that is what an archival record does. 3,643 of 4,581 tracked .md files (79.5%) live here, and they dominated the old 1,212-finding report. |
    | .claude/worktrees/**    | Gitignored, transient full-repo copies for parallel agent sessions. 117,311 .md files on a typical dev machine — enough to bury the real signal and to convince a developer the tool is broken. Zero tracked. |
    | .claude/archive/**      | The reversibility archive: content is preserved BY DATE precisely because it is superseded. Previously NOT excluded — the old `\.archive` alternation matches a segment literally named `.archive`, which `.claude/archive` is not. |
    | provisioning-runs/**,   | Generated run records / reports, not authored documentation. |
    | reports/**              |     |

    Pass an explicit -ExcludePattern to override, or -Path to scan one subtree
    (e.g. `-Path projects/my-project` still works and ignores this default).

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

    # Junk / build output. Matched ANYWHERE in the path — these are never authored docs
    # no matter where they appear.
    [string]$ExcludePattern = '(?i)[\\/](node_modules|\.git|dist|bin|obj|publish|TestResults|\.archive)([\\/]|$)',

    # Archival + transient documentation areas. Matched against the path RELATIVE TO THE REPO
    # ROOT, anchored at the start — NOT anywhere in the path. That distinction is load-bearing:
    # an unanchored `projects` would match the absolute path of every file when the caller runs
    # `-Path projects/some-project` (a documented example below), silently scanning zero files.
    # See the SCAN CORPUS table in the header for why each entry is here.
    [string]$ExcludeFromRootPattern = '(?i)^(projects|provisioning-runs|reports|\.claude[\\/](worktrees|archive))([\\/]|$)',

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

# If the caller explicitly pointed -Path INTO an archival area, honour that: they asked for it
# by name, so the root-anchored exclusions are suppressed for this run. Without this, the
# documented `-Path projects/<name>` usage would scan nothing.
$scanRootRel = $scanRoot.Substring([Math]::Min($repoRoot.Length, $scanRoot.Length)).TrimStart('\', '/')
$explicitlyInsideExcludedArea = $scanRootRel -and ($scanRootRel -match $ExcludeFromRootPattern)

Write-Host "================================================"
Write-Host "Markdown Link Validator"
Write-Host "  Scan root: $scanRoot"
Write-Host "  Repo root: $repoRoot"
Write-Host "  Network:   $((-not $NoNetwork))"
Write-Host "  Max ext:   $MaxExternalChecks"
if ($explicitlyInsideExcludedArea) {
    Write-Host "  Corpus:    EXPLICIT -Path inside an archival area — root exclusions suppressed"
} else {
    Write-Host "  Corpus:    current documentation (see SCAN CORPUS in script header)"
}
Write-Host "================================================"

# Collect .md files (filtered)
$mdFiles = Get-ChildItem -LiteralPath $scanRoot -Filter '*.md' -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch $ExcludePattern }

$foundBeforeCorpusFilter = $mdFiles.Count

if (-not $explicitlyInsideExcludedArea) {
    $mdFiles = $mdFiles | Where-Object {
        $rel = $_.FullName.Substring([Math]::Min($repoRoot.Length, $_.FullName.Length)).TrimStart('\', '/')
        $rel -notmatch $ExcludeFromRootPattern
    }
}

$excludedByCorpus = $foundBeforeCorpusFilter - $mdFiles.Count

Write-Host "Found $($mdFiles.Count) .md files in the governed corpus"
if ($excludedByCorpus -gt 0) {
    # Never let a scope reduction look like a clean scan. Say what was dropped.
    Write-Host "  ($excludedByCorpus file(s) excluded as archival/transient — see SCAN CORPUS in script header)"
}

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

            # Percent-decode before resolving. Markdown links to paths containing spaces are
            # commonly written `Sprint%202/...`, which Test-Path would never match literally —
            # a false "file not found" on a link that actually resolves. (Both current instances
            # in this repo turn out to be genuinely broken either way, so this changes no count
            # today; it is here so the next one is not misreported.)
            if ($pathPart -match '%[0-9A-Fa-f]{2}') {
                try { $pathPart = [System.Uri]::UnescapeDataString($pathPart) } catch { }
            }

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
    # Per-area rollup FIRST — 200+ raw rows is a wall of text; the rollup tells a reader
    # where the debt actually is before they scroll. (Format-Table was truncating paths to
    # the console width, which made the raw rows unusable as well as unreadable.)
    Write-Host "Broken links by area:"
    $results |
        Group-Object { ($_.File -replace '^\.[\\/]', '' -split '[\\/]')[0] } |
        Sort-Object Count -Descending |
        ForEach-Object { Write-Host ("    {0,5}  {1}" -f $_.Count, $_.Name) }
    Write-Host ""

    Write-Host "Detail:"
    foreach ($r in $results) {
        # One line per finding, never truncated — a path you cannot read is a finding you
        # cannot fix.
        Write-Host ("  {0}`n      -> [{1}] {2}  ({3})" -f $r.File, $r.Kind, $r.Target, $r.Reason)
    }

    Write-Host ""
    Write-Host "::error::Markdown link validation FAILED — $($results.Count) broken link(s) found"
    exit 1
}

Write-Host "All markdown links resolved successfully" -ForegroundColor Green
exit 0
