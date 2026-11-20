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

    Dot-folders (.claude/**, .github/**) are collected with Get-ChildItem -Force.
    Without -Force, PowerShell on Linux treats dot-prefixed folders as hidden and
    skips them, so the CI runner silently never scanned .claude/** or .github/**
    while a Windows run did (found 2026-11-11, #1717: 64 hidden broken links).

    EXTERNAL VERDICTS (2026-11-11, #1717)
    -------------------------------------
    An external link is BROKEN (fails the run) on 404/410/5xx or no response
    (DNS failure, TLS failure, connection refused).
    It is UNVERIFIABLE (listed in the report, does not fail) when the checker,
    an anonymous bot, cannot tell whether a human reader can open it:

      | Case                                    | Why it is not evidence of a dead link |
      |-----------------------------------------|----------------------------------------|
      | HTTP 401, 403, 429                      | Sign-in wall, bot blocking (e.g. openai.com) or rate limiting. |
      | Timed out twice (10 s, then 20 s)       | A slow third-party host. It is retried once; a dead host fails fast with DNS/TLS/refused instead. (#2720 review: one slow pricing page turned the run red at random.) |
      | URL starts with an -UnverifiableUrlPrefixes entry | Private-by-design pages. Default: the spaarke-dev GitHub Projects boards (the portfolio board, project #2), which return 404 to anyone not signed in. |

    Keep the allowlist short and specific: add a prefix only for pages that are
    private by design, with the reason in this table.

.PARAMETER MaxExternalChecks
    Cap on external requests to DISTINCT URLs (default 200) to bound runtime on large repos;
    a repeated URL is answered from the cache and does not count.

    ROTATION (2026-11-12): the governed corpus holds far more distinct external URLs than the
    cap (1,752 on 2026-11-12). Previously the cap took the first 200 in scan order, so the same
    200 were checked every run and the rest were never checked at all, without a warning. Now
    the distinct URLs are sorted and each run checks a window of -MaxExternalChecks of them that
    moves by one window per UTC day, so every URL is checked once per ceil(URLs / cap) days
    (9 days at 1,752 / 200). URLs outside today's window are NOT failures; the summary prints
    how many there are and the cycle length, and a ::warning:: line surfaces it in CI.
    The 200 cap stays because the advisory job has a 5-minute budget.

.PARAMETER RotationDay
    The rotation day index (default: whole days since 1970-01-01 UTC). Pass a number to reproduce
    a particular day's window.

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

    [int]$MaxExternalChecks = 200,

    # See ROTATION under -MaxExternalChecks in the header.
    [int]$RotationDay = [int][Math]::Floor(([DateTime]::UtcNow - [DateTime]::new(1970, 1, 1, 0, 0, 0, [DateTimeKind]::Utc)).TotalDays),

    # Private-by-design URL prefixes: reported as unverifiable, never requested.
    # See EXTERNAL VERDICTS in the header for the reason behind each entry.
    [string[]]$UnverifiableUrlPrefixes = @(
        'https://github.com/users/spaarke-dev/projects/',
        'https://github.com/orgs/spaarke-dev/projects/'
    ),

    # Internal: used by the self-test's child run so it does not recurse.
    [switch]$SkipSelfTest
)

$UnverifiableStatusCodes = @(401, 403, 429)

# Follow an https -> http redirect as a browser does. Without this, PowerShell 7.4+ throws
# InvalidOperationException and a live page (e.g. griffel.js.org, which redirects to http and back)
# was reported broken. Older PowerShell follows such redirects anyway and lacks the parameter.
$redirectArgs = @{}
if ((Get-Command Invoke-WebRequest).Parameters.ContainsKey('AllowInsecureRedirect')) { $redirectArgs['AllowInsecureRedirect'] = $true }

# One definition of "which files are scanned", shared by the scan and the self-test.
# -Force: see the dot-folder note in the header.
function Get-CandidateMarkdownFiles([string]$Root) {
    Get-ChildItem -LiteralPath $Root -Filter '*.md' -Recurse -File -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch $ExcludePattern }
}

function Get-StatusVerdict([int]$Code) {
    if ($Code -ge 200 -and $Code -lt 400) { return 'ok' }
    if ($UnverifiableStatusCodes -contains $Code) { return 'unverifiable' }
    return 'broken'
}

function Test-UnverifiableUrl([string]$Url) {
    foreach ($prefix in $UnverifiableUrlPrefixes) {
        if ($Url.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

function Get-ErrorStatusCode($ErrorRecord) {
    $response = $ErrorRecord.Exception.Response
    if ($response -and $response.StatusCode) { return [int]$response.StatusCode }
    return $null
}

# True when the request got no response because it ran out of time (as opposed to DNS, TLS or a
# refused connection). PowerShell 7 reports an HttpClient timeout as a TaskCanceledException whose
# inner exception is a TimeoutException.
function Test-IsTimeoutError($ErrorRecord) {
    $ex = $ErrorRecord.Exception
    while ($ex) {
        if ($ex -is [System.TimeoutException] -or $ex -is [System.OperationCanceledException]) { return $true }
        $ex = $ex.InnerException
    }
    return $false
}

# Which of the distinct external URLs this run checks (ROTATION in the header). $Urls must be sorted
# so the window is stable between machines; it moves by $Cap each day and wraps at the end.
function Get-RotationWindow([string[]]$Urls, [int]$Cap, [int]$Day) {
    $set = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $n = $Urls.Count
    if ($n -eq 0 -or $Cap -le 0) { return , $set }
    if ($n -le $Cap) { foreach ($u in $Urls) { [void]$set.Add($u) }; return , $set }
    $start = [int](([long]$Day * $Cap) % $n)
    for ($i = 0; $i -lt $Cap; $i++) { [void]$set.Add($Urls[($start + $i) % $n]) }
    return , $set
}

# Self-test (must-fire / must-not-fire), root CLAUDE.md §16: a platform quirk or a refactor must not
# silently stop dot-folders being scanned, or start failing runs on unverifiable links.
function Invoke-SelfTest {
    foreach ($case in @(@{ Code = 200; Want = 'ok' }, @{ Code = 301; Want = 'ok' }, @{ Code = 404; Want = 'broken' },
                        @{ Code = 410; Want = 'broken' }, @{ Code = 500; Want = 'broken' }, @{ Code = 401; Want = 'unverifiable' },
                        @{ Code = 403; Want = 'unverifiable' }, @{ Code = 429; Want = 'unverifiable' })) {
        $got = Get-StatusVerdict $case.Code
        if ($got -ne $case.Want) { throw "Self-test failed: HTTP $($case.Code) classified '$got', expected '$($case.Want)'." }
    }
    if (-not (Test-UnverifiableUrl 'https://github.com/users/spaarke-dev/projects/2')) { throw 'Self-test failed: the private spaarke-dev project board is not on the unverifiable allowlist.' }
    if (Test-UnverifiableUrl 'https://github.com/users/someone-else/projects/2') { throw 'Self-test failed: the allowlist matches a project board outside spaarke-dev.' }
    if (Test-UnverifiableUrl 'https://github.com/spaarke-dev/spaarke') { throw 'Self-test failed: the allowlist matches a non-project spaarke-dev URL.' }
    # Timeout vs dead host: a timeout is retried and then unverifiable; DNS/TLS/refused stays broken.
    $timeout = [System.Management.Automation.ErrorRecord]::new(
        [System.Threading.Tasks.TaskCanceledException]::new('The request was canceled due to the configured HttpClient.Timeout of 10 seconds elapsing.', [System.TimeoutException]::new('timeout')),
        'selftest', 'OperationTimeout', $null)
    $dns = [System.Management.Automation.ErrorRecord]::new(
        [System.Net.Http.HttpRequestException]::new('No such host is known.'), 'selftest', 'ConnectionError', $null)
    if (-not (Test-IsTimeoutError $timeout)) { throw 'Self-test failed (must-fire): an HttpClient timeout was not recognised as a timeout.' }
    if (Test-IsTimeoutError $dns) { throw 'Self-test failed (must-not-fire): a DNS failure was classified as a timeout and would be hidden.' }
    # Rotation: ceil(n / cap) consecutive days cover every URL (from any start day); one day checks exactly cap.
    $urls = @('a', 'b', 'c', 'd', 'e', 'f', 'g')
    $covered = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($day in 20404..20406) { foreach ($u in (Get-RotationWindow $urls 3 $day)) { [void]$covered.Add($u) } }
    if ($covered.Count -ne $urls.Count) { throw "Self-test failed (must-fire): 3 days of rotation at cap 3 covered $($covered.Count) of 7 URLs; some links would never be checked." }
    if ((Get-RotationWindow $urls 3 20404).Count -ne 3) { throw 'Self-test failed (must-not-fire): one rotation window does not hold exactly cap URLs.' }
    if ((Get-RotationWindow $urls 10 20404).Count -ne 7) { throw 'Self-test failed: under the cap, not every URL is checked.' }

    # End to end, offline: a child run over a throwaway repo with a dot-folder.
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ('mdlink-selftest-' + [guid]::NewGuid().ToString('N'))
    try {
        New-Item -ItemType Directory -Force -Path (Join-Path $tmp '.git'), (Join-Path $tmp '.dotdir') | Out-Null
        Set-Content -LiteralPath (Join-Path $tmp '.dotdir/broken.md') -Value '[gone](does-not-exist.md)'
        # On Windows a dot-folder is not hidden by name; mark it so the control reproduces Linux semantics.
        if ($IsWindows) { $d = Get-Item -LiteralPath (Join-Path $tmp '.dotdir'); $d.Attributes = $d.Attributes -bor [IO.FileAttributes]::Hidden }
        Set-Content -LiteralPath (Join-Path $tmp 'private.md') -Value '[board](https://github.com/users/spaarke-dev/projects/2)'
        # An indented fence holding an odd backtick, then a broken link, then a code span.
        Set-Content -LiteralPath (Join-Path $tmp 'fence.md') -Value @('1. Step', '   ```bash', '   echo `x', '   ```', '[gone](missing-after-fence.md)', 'Run `foo`.')

        # Must fire: the broken link inside the dot-folder fails the run.
        $out = & pwsh -NoProfile -File $PSCommandPath -Path $tmp -SkipSelfTest 2>&1 | Out-String
        if ($LASTEXITCODE -ne 1 -or $out -notmatch 'does-not-exist\.md') {
            throw "Self-test failed (must-fire): a broken link in a dot-folder did not fail the run (exit $LASTEXITCODE). Dot-folders such as .claude/** would be skipped silently."
        }
        # Must fire: a broken link after an indented fence is still seen (backticks do not pair across lines).
        if ($out -notmatch 'missing-after-fence\.md') {
            throw 'Self-test failed (must-fire): a broken link after an indented code fence was hidden by backtick pairing across lines.'
        }
        # Must not fire: an allowlisted private URL is listed as unverifiable and the run passes.
        Remove-Item -LiteralPath (Join-Path $tmp '.dotdir/broken.md'), (Join-Path $tmp 'fence.md')
        $out = & pwsh -NoProfile -File $PSCommandPath -Path $tmp -SkipSelfTest 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0 -or $out -notmatch 'unverifiable.*spaarke-dev/projects/2') {
            throw "Self-test failed (must-not-fire): an allowlisted private URL failed the run or was not listed (exit $LASTEXITCODE)."
        }
        # Must fire: a URL outside the day's window is reported as not checked (and not requested -
        # .invalid never resolves, so a request would fail the run).
        Set-Content -LiteralPath (Join-Path $tmp 'external.md') -Value '[x](https://example.invalid/not-requested)'
        $out = & pwsh -NoProfile -File $PSCommandPath -Path $tmp -MaxExternalChecks 0 -SkipSelfTest 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0 -or $out -notmatch 'External links NOT checked this run: 1 ') {
            throw "Self-test failed (must-fire): a URL outside the rotation window was requested or not reported (exit $LASTEXITCODE). Skipped links would be silent again."
        }
    } finally {
        Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$ErrorActionPreference = 'Stop'

if (-not $SkipSelfTest) { Invoke-SelfTest }

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
$mdFiles = @(Get-CandidateMarkdownFiles $scanRoot)

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

# Inline-code span detector — used to drop spans before scanning. One line only: an unpaired
# backtick (e.g. inside an indented fence the old fence pattern missed) used to pair with a backtick
# lines later and blank every link in between - 14 broken links were hidden that way (2026-11-12).
$inlineCodeRegex = [regex]'`[^`\r\n]+`'

# Fenced-code block detector — drop entire ```...``` / ~~~...~~~ blocks, including indented ones
# (fences inside list items).
$fencedCodeRegex = [regex]'(?ms)^[ \t]*(```|~~~)[\s\S]*?^[ \t]*\1'

$results = [System.Collections.Generic.List[object]]::new()
$unverifiable = [System.Collections.Generic.List[object]]::new()
$externalCheckCount = 0
$externalSkipped = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$externalCache = @{}  # URL -> result (avoid duplicate HEAD)

# Pass 1: collect every link, so the external cap can choose WHICH distinct URLs to check
# (ROTATION in the header) instead of taking whichever come first in scan order.
$fileLinks = [System.Collections.Generic.List[object]]::new()
$distinctExternal = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($file in $mdFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw -ErrorAction SilentlyContinue
    if (-not $content) { continue }

    # Strip fenced code blocks then inline code spans (keeps line/column rough but accurate enough)
    $stripped = $fencedCodeRegex.Replace($content, { param($m) ' ' * $m.Length })
    $stripped = $inlineCodeRegex.Replace($stripped, { param($m) ' ' * $m.Length })

    $targets = [System.Collections.Generic.List[string]]::new()
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
        $targets.Add($target)
        if ($target -match '^https?://' -and -not (Test-UnverifiableUrl $target)) { [void]$distinctExternal.Add($target) }
    }
    if ($targets.Count) { $fileLinks.Add([pscustomobject]@{ File = $file; Targets = $targets }) }
}

$sortedExternal = [string[]]@($distinctExternal)
[Array]::Sort($sortedExternal, [StringComparer]::Ordinal)
$checkThisRun = Get-RotationWindow $sortedExternal $MaxExternalChecks $RotationDay

# Pass 2: validate.
foreach ($entry in $fileLinks) {
    $file = $entry.File
    foreach ($target in $entry.Targets) {
        $isExternal = $target -match '^https?://'
        $status = 'ok'
        $reason = ''

        if ($isExternal) {
            if ($NoNetwork) {
                continue  # skip silently
            }
            if (Test-UnverifiableUrl $target) {
                # Private by design — never requested, always listed (EXTERNAL VERDICTS in the header).
                $status = 'unverifiable'
                $reason = 'private by design (allowlisted prefix)'
            } elseif ($externalCache.ContainsKey($target)) {
                # A repeated URL is answered from the cache and does not count against the cap.
                $cached = $externalCache[$target]
                $status = $cached.Status
                $reason = $cached.Reason
            } elseif (-not $checkThisRun.Contains($target)) {
                [void]$externalSkipped.Add($target)
                continue  # outside today's rotation window; counted and reported in the summary
            } else {
                $externalCheckCount++
                try {
                    $resp = Invoke-WebRequest -Uri $target -Method Head -TimeoutSec 10 -UseBasicParsing -ErrorAction Stop -MaximumRedirection 5 @redirectArgs
                    $status = Get-StatusVerdict $resp.StatusCode
                    if ($status -ne 'ok') { $reason = "HTTP $($resp.StatusCode)" }
                } catch {
                    # Some servers reject HEAD; retry once with GET (range 0-0) before failing.
                    try {
                        $resp = Invoke-WebRequest -Uri $target -Method Get -TimeoutSec 10 -UseBasicParsing -ErrorAction Stop -MaximumRedirection 5 @redirectArgs -Headers @{Range='bytes=0-0'}
                        $status = Get-StatusVerdict $resp.StatusCode
                        if ($status -ne 'ok') { $reason = "HTTP $($resp.StatusCode)" }
                    } catch {
                        $code = Get-ErrorStatusCode $_
                        if ($null -ne $code) {
                            $status = Get-StatusVerdict $code
                            $reason = "HTTP $code"
                        } elseif (Test-IsTimeoutError $_) {
                            # Slow host: one more try with a longer timeout before calling it unverifiable.
                            try {
                                $resp = Invoke-WebRequest -Uri $target -Method Get -TimeoutSec 20 -UseBasicParsing -ErrorAction Stop -MaximumRedirection 5 @redirectArgs -Headers @{Range='bytes=0-0'}
                                $status = Get-StatusVerdict $resp.StatusCode
                                if ($status -ne 'ok') { $reason = "HTTP $($resp.StatusCode)" }
                            } catch {
                                $code = Get-ErrorStatusCode $_
                                if ($null -ne $code) {
                                    $status = Get-StatusVerdict $code
                                    $reason = "HTTP $code"
                                } elseif (Test-IsTimeoutError $_) {
                                    $status = 'unverifiable'
                                    $reason = 'timed out twice (10 s, 20 s)'
                                } else {
                                    $status = 'broken'
                                    $reason = $_.Exception.Message -replace '\s+', ' '
                                    if ($reason.Length -gt 80) { $reason = $reason.Substring(0, 80) + '...' }
                                }
                            }
                        } else {
                            $status = 'broken'   # no response at all (DNS, TLS, connection refused)
                            $reason = $_.Exception.Message -replace '\s+', ' '
                            if ($reason.Length -gt 80) { $reason = $reason.Substring(0, 80) + '...' }
                        }
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

        if ($status -eq 'unverifiable') {
            $unverifiable.Add([pscustomobject]@{
                File   = (Resolve-Path -LiteralPath $file.FullName -Relative)
                Target = $target
                Reason = $reason
            })
        } elseif ($status -eq 'broken') {
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
Write-Host "  External checks run: $externalCheckCount (distinct URLs; cap $MaxExternalChecks)"
if ($externalSkipped.Count -gt 0) {
    $cycleDays = if ($MaxExternalChecks -gt 0) { [Math]::Ceiling($sortedExternal.Count / $MaxExternalChecks) } else { 'never (cap 0)' }
    Write-Host "  External links NOT checked this run: $($externalSkipped.Count) of $($sortedExternal.Count) distinct URLs (rotation day $RotationDay; every URL is checked once per $cycleDays day(s))"
    Write-Host "::warning title=Markdown Link Validator::$($externalSkipped.Count) of $($sortedExternal.Count) distinct external URLs were not checked this run (cap $MaxExternalChecks, daily rotation; full cycle $cycleDays day(s))."
}
Write-Host "  Broken links found:  $($results.Count)"
Write-Host "  Unverifiable links:  $($unverifiable.Count) (listed, not failing - EXTERNAL VERDICTS in the script header)"
Write-Host ""

if ($unverifiable.Count -gt 0) {
    Write-Host "Unverifiable (an anonymous checker cannot tell; a signed-in reader may open them):"
    foreach ($u in $unverifiable) {
        Write-Host ("  {0}`n      -> [unverifiable] {1}  ({2})" -f $u.File, $u.Target, $u.Reason)
    }
    Write-Host ""
}

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
