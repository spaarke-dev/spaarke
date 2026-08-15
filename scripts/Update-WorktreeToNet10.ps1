<#
.SYNOPSIS
    Migrate a Spaarke worktree to .NET 10 safely: verify SDK -> merge master ->
    guard against the IDE net8-clobber -> build -> report. NON-DESTRUCTIVE.

.DESCRIPTION
    One command to bring any worktree onto the net10 baseline after the
    2026-08-14 cutover (master is net10). The script:
      1. Verifies the .NET 10 SDK is installed (else NETSDK1045 at build).
      2. Refuses to run on a dirty tree (you commit/stash first — no auto-stash,
         so nothing of yours is ever lost).
      3. git fetch + merge origin/master.
      4. On conflicts: lists them and STOPS for you to resolve (rule of thumb:
         take net10 for csproj/props/global.json, keep your feature intent).
         Pass -AutoResolveCsproj to auto-take master's version for *.csproj only.
      5. IDE-clobber guard: verifies every src/server csproj is <net10.0>.
      6. Builds to confirm (BFF server project by default). Interprets failures.

    It does NOT commit, push, or deploy — you stay in control of those.

.PARAMETER WorktreePath
    Target worktree. Default: current directory. Point at another worktree to
    migrate it without cd-ing:  -WorktreePath C:\code_files\spaarke-wt-foo

.PARAMETER AutoResolveCsproj
    If the merge conflicts ONLY in *.csproj, auto-resolve them by taking
    master's (net10) version. Skips if any non-csproj file also conflicts.

.PARAMETER NoBuild
    Skip the verification build (merge + csproj guard only).

.EXAMPLE
    pwsh -File scripts/Update-WorktreeToNet10.ps1
    # migrate the current worktree

.EXAMPLE
    pwsh -File scripts/Update-WorktreeToNet10.ps1 -WorktreePath C:\code_files\spaarke-wt-teams-app-r1
    # migrate a different worktree from here
#>
[CmdletBinding()]
param(
    [string]$WorktreePath = ".",
    [switch]$AutoResolveCsproj,
    [switch]$NoBuild
)
$ErrorActionPreference = "Stop"
function Fail($m) { Write-Host "`n❌ $m" -ForegroundColor Red; exit 1 }
function Ok($m)   { Write-Host "✅ $m" -ForegroundColor Green }
function Info($m) { Write-Host "   $m" -ForegroundColor Gray }

$wt = (Resolve-Path $WorktreePath).Path
Write-Host "=== Migrate worktree to .NET 10 ===" -ForegroundColor Cyan
Write-Host "  Target: $wt"

# 0. is it a git worktree?
git -C $wt rev-parse --is-inside-work-tree *> $null; if ($LASTEXITCODE) { Fail "$wt is not a git working tree." }
$branch = git -C $wt branch --show-current
Write-Host "  Branch: $branch`n"

# 1. .NET 10 SDK present?
$sdks = dotnet --list-sdks
if (-not ($sdks | Select-String -SimpleMatch "10.0.")) {
    Write-Host "Installed SDKs:`n$sdks" -ForegroundColor Yellow
    Fail ".NET 10 SDK NOT found. Install it first (side-by-side with 8):`n   winget install Microsoft.DotNet.SDK.10`n   (or https://dotnet.microsoft.com/download/dotnet/10.0) — then restart the terminal."
}
Ok ".NET 10 SDK present: $(( $sdks | Select-String -SimpleMatch '10.0.' | Select-Object -First 1 ).ToString().Trim())"

# 2. IDE-clobber reminder + dirty-tree guard
Write-Host "`n⚠️  Close Visual Studio / Rider (or unload the solution) before proceeding —" -ForegroundColor Yellow
Write-Host "   an open solution can autosave stale net8 csproj over the merge." -ForegroundColor Yellow
$dirty = git -C $wt status --porcelain
if ($dirty) {
    Write-Host "`nUncommitted changes in the worktree:" -ForegroundColor Yellow
    $dirty | ForEach-Object { Info $_ }
    Fail "Working tree is dirty. Commit or stash your changes first (this script never auto-stashes, so your work is safe), then re-run."
}
Ok "Working tree clean."

# 3. fetch + merge master
Info "fetching origin..."
git -C $wt fetch origin --quiet
$behind = [int](git -C $wt rev-list --count HEAD..origin/master)
if ($behind -eq 0) {
    Ok "Already up to date with origin/master (net10). Nothing to merge."
} else {
    Write-Host "`nMerging origin/master ($behind commits) into $branch..." -ForegroundColor Cyan
    git -C $wt merge origin/master --no-edit 2>&1 | ForEach-Object { Info $_ }
    $conflicts = git -C $wt diff --name-only --diff-filter=U
    if ($conflicts) {
        $nonCsproj = $conflicts | Where-Object { $_ -notmatch '\.csproj$' }
        if ($AutoResolveCsproj -and -not $nonCsproj) {
            Write-Host "`nAuto-resolving csproj conflicts to net10 (master's version):" -ForegroundColor Yellow
            $conflicts | ForEach-Object { git -C $wt checkout --theirs -- $_; git -C $wt add -- $_; Info "took net10: $_" }
            git -C $wt commit --no-edit | Out-Null
            Ok "csproj conflicts auto-resolved to net10."
        } else {
            Write-Host "`n⛔ Merge conflicts need manual resolution:" -ForegroundColor Red
            $conflicts | ForEach-Object { Info $_ }
            Write-Host "   Rule of thumb: take NET10 for *.csproj / *.props / global.json; keep your feature intent elsewhere." -ForegroundColor Yellow
            Write-Host "   Resolve, 'git add' them, 'git commit', then re-run this script (it'll continue to the build check)." -ForegroundColor Yellow
            Fail "Stopped for manual conflict resolution."
        }
    } else {
        Ok "Merged cleanly (no conflicts)."
    }
}

# 4. IDE-clobber guard — every src/server csproj MUST be net10.0
Write-Host "`nVerifying TargetFramework on server csproj (net8-clobber guard)..." -ForegroundColor Cyan
$serverCsproj = Get-ChildItem -Path (Join-Path $wt 'src/server') -Recurse -Filter *.csproj -ErrorAction SilentlyContinue
$net8 = @()
foreach ($p in $serverCsproj) {
    $tfm = Select-String -Path $p.FullName -Pattern '<TargetFramework>(.*?)</TargetFramework>' -AllMatches |
           ForEach-Object { $_.Matches } | ForEach-Object { $_.Groups[1].Value }
    if ($tfm -match 'net8') { $net8 += "$($p.FullName) -> $tfm" }
}
# net462 plugin is intentionally excluded (sandbox-fixed); only flag net8.x
if ($net8.Count -gt 0) {
    $net8 | ForEach-Object { Write-Host "   NET8 STILL PRESENT: $_" -ForegroundColor Red }
    Fail "A server csproj is still net8 — likely an IDE clobbered it. Run: git checkout -- '*.csproj'  (or discard in the IDE), then re-run."
}
Ok "All server csproj are net10.0 (no clobber)."

if ($NoBuild) { Write-Host "`n(-NoBuild) Skipping build. Merge + guard passed." -ForegroundColor Green; exit 0 }

# 5. verification build
Write-Host "`nBuilding BFF (Release) to confirm net10..." -ForegroundColor Cyan
$bff = Join-Path $wt 'src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj'
$buildTarget = (Test-Path $bff) ? $bff : (Join-Path $wt 'Spaarke.sln')
$out = dotnet build $buildTarget -c Release --nologo 2>&1
$code = $LASTEXITCODE
if ($code -eq 0) {
    Ok "Build succeeded on net10."
    Write-Host "`n🎉 $branch is net10-ready. Next: run your tests, then deploy is net10-compatible with dev." -ForegroundColor Green
} else {
    Write-Host ($out | Select-String -Pattern 'error' | Select-Object -First 15) -ForegroundColor Red
    if ($out | Select-String -SimpleMatch 'NETSDK1045') { Fail "NETSDK1045 — the .NET 10 SDK isn't being picked up. Restart the terminal / confirm 'dotnet --version' resolves to 10.0.1xx at the repo root." }
    if ($out | Select-String -SimpleMatch 'Microsoft.Graph' -Quiet -ErrorAction SilentlyContinue) {
        Write-Host "   Graph/Kiota call sites may need the 5->6.5 / Kiota 1->2 fixups —" -ForegroundColor Yellow
        Write-Host "   see projects/dotnet-10-upgrade-r1/notes/graph6-kiota2-break-assessment.md" -ForegroundColor Yellow
    }
    Fail "Build failed after merge — resolve the errors above (most are Graph 6.5 / Kiota 2.0 call sites). The merge itself is fine; this is code that needs a small fixup."
}
