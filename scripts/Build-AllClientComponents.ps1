<#
.SYNOPSIS
    Build all client-side components in correct dependency order.

.DESCRIPTION
    Orchestrates the build of all Spaarke client components in the required order:
    1. Shared libraries (14 packages in src/client/shared/, in dependency order -- the $SharedLibs list
       below is authoritative; it includes Spaarke.DailyBriefing.Components)
       NOTE: Spaarke.LegalWorkspace is intentionally excluded -- it is a source-only lib (tsc --noEmit)
       with @spaarke peerDependencies; type-check happens via the consumer's tsc pass.
    2. Vite solutions (19 projects in src/solutions/)
    3. Webpack code pages (3 projects in src/client/code-pages/)
    4. PCF controls (src/client/pcf/*) - ONE PCF AT A TIME, in production mode (`npm run build:prod`)
    5. External SPA (src/client/external-spa/)

    Each component runs `npm install --legacy-peer-deps --no-audit --no-fund` (only when needed --
    see the in-line "Install dependencies" comment for the trigger logic) followed by
    `npm run build`. Shared libraries must build first because downstream components depend on them.

    PCF controls (Step 4) are different, mirroring .github/workflows/pcf-build-prod-nightly.yml:
    every git-tracked src/client/pcf/<name>/package.json that declares a `build:prod` script is a
    PCF; each one ALWAYS gets `npm install` and then `npm run build:prod`, and is judged from its
    OUTPUT by scripts/PcfBuildResult.psm1 (pcf-scripts exits 0 when webpack fails). Each PCF is its
    own row in the summary, so a failure names the control. Finding zero PCFs is a FAILED row, as is
    a package.json that cannot be parsed. A -Component name that matches nothing (e.g. PCF/Nope) is
    also a FAILED row, so a typo cannot exit 0.
    (Until 2026-10 this step ran one aggregate dev-mode `npm run build` over all controls at
    src/client/pcf; that never worked from a clean checkout - TS5083 on the controls' relative
    tsconfig `extends`, then out-of-memory building every control in one process.)

.PARAMETER SkipSharedLibs
    Skip the shared library builds (step 1). Use when shared libs are already built
    and you only need to rebuild downstream components.

.PARAMETER Component
    Build only specific components by name. Accepts an array of component names.
    Names match directory names (e.g., "LegalWorkspace", "SemanticSearch", "PCF").
    Special names: "SharedLibs", "PCF", "ExternalSPA".
    "PCF" selects every PCF; "PCF/<folder>" (e.g. "PCF/VisualHost") selects one. Bare PCF folder
    names are NOT accepted, because some collide with code pages (DocumentRelationshipViewer).
    "PCF" does not build the shared libraries the PCFs import; on a clean checkout use
    -Component SharedLibs,PCF.

.EXAMPLE
    .\Build-AllClientComponents.ps1
    # Full build of all client components in dependency order.

.EXAMPLE
    .\Build-AllClientComponents.ps1 -SkipSharedLibs
    # Build everything except shared libraries (assumes they are already built).

.EXAMPLE
    .\Build-AllClientComponents.ps1 -Component LegalWorkspace, SmartTodo
    # Build only the LegalWorkspace and SmartTodo solutions.

.EXAMPLE
    .\Build-AllClientComponents.ps1 -Component PCF -WhatIf
    # Preview what would happen when building PCF controls (lists every discovered PCF).

.EXAMPLE
    .\Build-AllClientComponents.ps1 -Component SharedLibs, PCF/VisualHost
    # Build the shared libraries, then only the VisualHost PCF (production mode).
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [switch]$SkipSharedLibs,

    [string[]]$Component
)

$ErrorActionPreference = "Stop"

# Normalize -Component: when called via pwsh -File, comma-separated values arrive as a single string
if ($Component) {
    $Component = $Component | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ }
}
# (The "SharedLibs"/"PCF"/"ExternalSPA" shortcut expansions are applied below, AFTER $SharedLibs is defined.)

# --- Configuration ---
$RepoRoot = (Resolve-Path "$PSScriptRoot\..").Path
Import-Module (Join-Path $PSScriptRoot "PcfBuildResult.psm1") -Force   # PCF build result from output (pcf-scripts exits 0 on failure)

# Shared libraries (build order matters -- downstream deps must come after their dependencies)
#
# Spaarke.DailyBriefing.Components is now INCLUDED (standalone build restored 2026-07-08 by
# spaarke-daily-update-service-r5, superseding PR #506). Its `@spaarke/*` deps are declared as
# `file:` dependencies + tsconfig `paths` (mirroring Spaarke.AI.Widgets), so `tsc --noEmit`
# type-checks standalone. Re-added to the gate so future standalone breaks are caught in CI
# rather than silently shipped (the 2026-06-28 TS2307 failure was the peer-only-deps root cause,
# now fixed in the lib's package.json/tsconfig.json).
#
# Spaarke.LegalWorkspace is intentionally EXCLUDED on the same basis (added 2026-07-02 by
# spaarke-dataset-grid-framework-r2 task 024 / FR-10). Task 020 scaffolded the package; task 021
# populated src/index.ts as a RE-EXPORT barrel of files that stay under src/solutions/LegalWorkspace/src/.
# Its `@spaarke/*` deps are peerDependencies and `npm run build` is `tsc --noEmit`; standalone
# type-check fails with TS2307 "Cannot find module '@spaarke/*'" -- verified in task 020. Type-check
# is performed by each consumer's tsc pass (SpaarkeAi, LegalWorkspace, WorkspaceLayoutWizard).
$SharedLibs = @(
    @{ Name = "Spaarke.Auth";                 Path = "$RepoRoot\src\client\shared\Spaarke.Auth" }
    @{ Name = "Spaarke.Notifications";        Path = "$RepoRoot\src\client\shared\Spaarke.Notifications" }        # depends on Auth (peer + file: devDep -> builds standalone). Added 2026-07-21 by spaarke-notification-spine-r1 (task 021 shipped @spaarke/notifications + the SpaarkeAi file: dep but omitted this build-orchestration entry -> fresh-master SpaarkeAi builds failed on the unbuilt lib).
    @{ Name = "Spaarke.SdapClient";           Path = "$RepoRoot\src\client\shared\Spaarke.SdapClient" }
    @{ Name = "Spaarke.AI.Context";           Path = "$RepoRoot\src\client\shared\Spaarke.AI.Context" }           # type-only, no @spaarke/* deps (auth dep dropped 2026-10-03, reuse audit C-14)
    @{ Name = "Spaarke.AI.Outputs";           Path = "$RepoRoot\src\client\shared\Spaarke.AI.Outputs" }
    @{ Name = "Spaarke.DocumentOperations";   Path = "$RepoRoot\src\client\shared\Spaarke.DocumentOperations" }   # depends on Auth (added 2026-06-29 by spaarkeai-compose-r1 task 030)
    @{ Name = "Spaarke.UI.Components";        Path = "$RepoRoot\src\client\shared\Spaarke.UI.Components" }        # depends on Auth, SdapClient
    # Events.Components and SmartTodo.Components MUST come AFTER UI.Components (moved 2026-10-04,
    # spaarke-ontology-platform-r1 task 094). Events.Components imports UI.Components SOURCE by relative
    # path (CalendarWorkspaceWidget -> ../Spaarke.UI.Components/src/...), so tsc needs UI.Components'
    # node_modules; SmartTodo.Components path-maps @spaarke/ui-components to ../Spaarke.UI.Components/dist.
    # Listed before it, both failed on every clean checkout (TS2307 'react' / '@spaarke/ui-components'),
    # and Step 1's fail-fast stopped the whole build. A developer machine with UI.Components already
    # installed and built hides this.
    @{ Name = "Spaarke.Events.Components";    Path = "$RepoRoot\src\client\shared\Spaarke.Events.Components" }    # depends on UI.Components (source + node_modules)
    @{ Name = "Spaarke.SmartTodo.Components"; Path = "$RepoRoot\src\client\shared\Spaarke.SmartTodo.Components" } # depends on UI.Components (dist)
    @{ Name = "Spaarke.Communication.Components"; Path = "$RepoRoot\src\client\shared\Spaarke.Communication.Components" } # depends on Auth + UI.Components; consumed by AI.Widgets (file: dep + dist paths-map) -> MUST build BEFORE AI.Widgets. Added 2026-08-11 by email-communication-intelligence-r2 task 063 (stale-dist fix; same class as Spaarke.Notifications line above -- its own `prebuild` also rebuilds Auth+UI.Components for standalone safety).
    @{ Name = "Spaarke.Visuals";              Path = "$RepoRoot\src\client\shared\Spaarke.Visuals" }              # no @spaarke/* deps; `build` is `tsc --noEmit`. Consumed FROM SOURCE (main: ./src/index.ts) by PCF VisualHost via a file: dep, so its OWN node_modules must exist: webpack/ts-loader resolve its bare imports (@fluentui/*) from its real path, never from VisualHost's node_modules. Added 2026-10-04 by task 092 (PR #1123) - same reason Spaarke.Communication.Components is here for the Communication PCFs.
    @{ Name = "Spaarke.AI.Widgets";           Path = "$RepoRoot\src\client\shared\Spaarke.AI.Widgets" }           # depends on UI.Components, AI.Outputs
    @{ Name = "Spaarke.DailyBriefing.Components"; Path = "$RepoRoot\src\client\shared\Spaarke.DailyBriefing.Components" } # depends on Auth + UI.Components; standalone build restored 2026-07-08 (supersedes PR #506)
    @{ Name = "Spaarke.Compose.Components";   Path = "$RepoRoot\src\client\shared\Spaarke.Compose.Components" }   # depends on Auth + DocumentOperations + AI.Widgets (PaneEventBus); MUST build AFTER AI.Widgets -- re-ordered 2026-06-29 by spaarkeai-compose-r1 task 045 W4 when AI.Widgets dep was added
)

# Expand the "SharedLibs" special shortcut into the actual lib names so the filter at line ~113 matches.
if ($Component -and "SharedLibs" -in $Component) {
    $Component = @($Component | Where-Object { $_ -ne "SharedLibs" }) + ($SharedLibs | ForEach-Object { $_.Name })
}

# Vite solutions (src/solutions/ - each has vite.config.ts)
$ViteSolutions = @(
    "AllDocuments"
    "CalendarSidePane"
    "CreateEventWizard"
    "CreateMatterWizard"
    "CreateProjectWizard"
    "CreateTodoWizard"
    "CreateWorkAssignmentWizard"
    "DailyBriefing"
    "DocumentUploadWizard"
    "EventDetailSidePane"
    "EventsPage"
    "FindSimilarCodePage"
    "LegalWorkspace"
    "PlaybookLibrary"
    "Reporting"
    "SmartTodo"
    "SpeAdminApp"
    "SummarizeFilesWizard"
    "WorkspaceLayoutWizard"
)

# Webpack code pages (src/client/code-pages/)
$WebpackCodePages = @(
    "DocumentRelationshipViewer"
    "PlaybookBuilder"
    "SemanticSearch"
)

# --- Results Tracking ---
$Results = [System.Collections.ArrayList]::new()
# -Component names that selected at least one component (used to fail on a name that matches nothing).
$MatchedFilters = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

function Invoke-ComponentBuild {
    param(
        [string]$Name,
        [string]$BuildPath,
        [string]$Category,
        # Names that select this component under -Component (default: just $Name).
        [string[]]$SelectBy,
        # npm script to run (PCF controls use build:prod).
        [string]$NpmScript = 'build',
        # Always run npm install, ignoring the node_modules freshness check (PCF controls).
        [switch]$AlwaysInstall
    )

    if (-not $SelectBy) { $SelectBy = @($Name) }

    # Filter check: if -Component was specified, only build matching components
    if ($Component -and $Component.Count -gt 0) {
        $hit = @($SelectBy | Where-Object { $_ -in $Component })
        if ($hit.Count -eq 0) {
            return
        }
        foreach ($h in $hit) { $null = $MatchedFilters.Add($h) }
    }

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

    # -LiteralPath everywhere: a folder named like Br[1] is a wildcard to plain -Path and would be
    # reported as not found.
    if (-not (Test-Path -LiteralPath $BuildPath)) {
        # A PCF was discovered from git ls-files, so a missing folder is a real failure, not a skip.
        $missingStatus = if ($Category -eq 'PCF Controls') { "FAILED" } else { "SKIPPED" }
        $missingColor = if ($missingStatus -eq 'FAILED') { 'Red' } else { 'Yellow' }
        Write-Host "  $($missingStatus.Substring(0,4))  $Name - directory not found: $BuildPath" -ForegroundColor $missingColor
        $null = $Results.Add([PSCustomObject]@{
            Component = $Name
            Category  = $Category
            Status    = $missingStatus
            Duration  = "0.0s"
            Detail    = "Directory not found"
        })
        return
    }

    if ($PSCmdlet.ShouldProcess("$Name ($BuildPath)", "install then npm run $NpmScript")) {
        Write-Host "  BUILD $Name" -ForegroundColor Cyan -NoNewline
        Write-Host " - $BuildPath" -ForegroundColor DarkGray

        try {
            Push-Location -LiteralPath $BuildPath

            # Install dependencies.
            # NOTE (2026-05-13): Many Vite solutions have drifted package-lock.json
            # files relative to current registry transitive versions (e.g. fluentui
            # 9.2.15 -> 9.2.16, tabster 8.7.0 -> 8.8.0). `npm ci` fails on these
            # because it requires the lock file to exactly satisfy package.json's
            # caret ranges. Strategy:
            #   1. If node_modules is missing, run `npm install --legacy-peer-deps`
            #      (resolves loosely; deterministic-enough for build).
            #   2. If node_modules exists BUT package.json is newer (added/changed
            #      a sibling file: dep), re-run install so symlinks get created.
            #      [2026-06-28 fix: prior "skip if node_modules exists" optimization
            #      left stale state when package.json gained a new `file:..` ref
            #      between branches -- SpaarkeAi missed daily-briefing-components.]
            #   3. Otherwise skip install entirely and build directly.
            #   4. Tracked separately: scheduled regeneration of locks once we have
            #      bandwidth to deploy-verify the transitive upgrades. Until then,
            #      do NOT call `npm ci` here.
            $nodeModulesPath = Join-Path $BuildPath "node_modules"
            $packageJsonPath = Join-Path $BuildPath "package.json"
            $needsInstall = $false
            if ($AlwaysInstall) {
                # PCF controls: always install, like the nightly PCF workflow.
                $needsInstall = $true
                Write-Host "        installing..." -ForegroundColor DarkGray
            }
            elseif (-not (Test-Path -LiteralPath $nodeModulesPath)) {
                $needsInstall = $true
                Write-Host "        installing (no node_modules)..." -ForegroundColor DarkGray
            }
            elseif ((Test-Path -LiteralPath $packageJsonPath) -and (Get-Item -LiteralPath $packageJsonPath).LastWriteTime -gt (Get-Item -LiteralPath $nodeModulesPath).LastWriteTime) {
                $needsInstall = $true
                Write-Host "        installing (package.json newer than node_modules -- sibling-dep drift)..." -ForegroundColor DarkGray
            }
            if ($needsInstall) {
                # Localize $ErrorActionPreference inside the script block so
                # benign stderr emissions from npm don't become terminating
                # exceptions under the script's outer 'Stop' preference. See
                # 2026-06-11 incident: every Vite solution was marked FAILED in
                # batch because rollup's "/* #__PURE__ */" warnings were
                # captured via 2>&1 and promoted to terminating errors, even
                # though $LASTEXITCODE was 0 and the build succeeded.
                $installOutput = & {
                    $ErrorActionPreference = 'Continue'
                    npm install --legacy-peer-deps --no-audit --no-fund 2>&1
                }
                if ($LASTEXITCODE -ne 0) {
                    throw "npm install failed (exit code $LASTEXITCODE)`n$($installOutput | Out-String)"
                }
            }

            # npm run <script> (same localized $ErrorActionPreference rationale)
            $buildOutput = & {
                $ErrorActionPreference = 'Continue'
                npm run $NpmScript 2>&1
            }
            if ($LASTEXITCODE -ne 0) {
                throw "npm run $NpmScript failed (exit code $LASTEXITCODE)`n$($buildOutput | Out-String)"
            }
            # pcf-scripts EXITS 0 WHEN THE WEBPACK BUILD FAILS, so for PCF builds the exit code above
            # proves nothing. Judge the result from the output (rule shared with
            # scripts/Invoke-PcfBuildProd.ps1 and the nightly CI workflow).
            if ($Category -eq 'PCF Controls') {
                $pcfResult = Get-PcfBuildResult -Output $buildOutput -ExitCode $LASTEXITCODE
                if (-not $pcfResult.Succeeded) {
                    throw "PCF build $($pcfResult.Status.ToLower()) ($($pcfResult.Reason)) although npm exited $LASTEXITCODE`n$($pcfResult.Excerpt -join "`n")"
                }
            }

            $stopwatch.Stop()
            $duration = "{0:F1}s" -f $stopwatch.Elapsed.TotalSeconds
            Write-Host "  PASS  $Name ($duration)" -ForegroundColor Green
            $null = $Results.Add([PSCustomObject]@{
                Component = $Name
                Category  = $Category
                Status    = "SUCCESS"
                Duration  = $duration
                Detail    = ""
            })
        }
        catch {
            $stopwatch.Stop()
            $duration = "{0:F1}s" -f $stopwatch.Elapsed.TotalSeconds
            Write-Host "  FAIL  $Name ($duration)" -ForegroundColor Red
            Write-Host "        $($_.Exception.Message)" -ForegroundColor Red
            $null = $Results.Add([PSCustomObject]@{
                Component = $Name
                Category  = $Category
                Status    = "FAILED"
                Duration  = $duration
                Detail    = $_.Exception.Message
            })
        }
        finally {
            Pop-Location
        }
    }
    else {
        # WhatIf mode
        $null = $Results.Add([PSCustomObject]@{
            Component = $Name
            Category  = $Category
            Status    = "WHATIF"
            Duration  = "-"
            Detail    = ""
        })
    }
}

# --- Display Header ---
$totalStopwatch = [System.Diagnostics.Stopwatch]::StartNew()

Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  Client Component Build Orchestrator" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  Repository:      $RepoRoot"
Write-Host "  Skip Shared:     $SkipSharedLibs"
if ($Component -and $Component.Count -gt 0) {
    Write-Host "  Filter:          $($Component -join ', ')"
}
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ""

# --- Step 1: Shared Libraries ---
if (-not $SkipSharedLibs) {
    Write-Host "Step 1/5: Shared Libraries" -ForegroundColor White
    Write-Host "--------------------------------------" -ForegroundColor DarkGray
    foreach ($lib in $SharedLibs) {
        Invoke-ComponentBuild -Name $lib.Name -BuildPath $lib.Path -Category "Shared Library"
    }

    # Fail fast: if any shared lib failed, downstream builds will also fail
    $sharedFailures = $Results | Where-Object { $_.Category -eq "Shared Library" -and $_.Status -eq "FAILED" }
    if ($sharedFailures) {
        Write-Host ""
        Write-Host "  FATAL: Shared library build failed. Downstream builds depend on these." -ForegroundColor Red
        Write-Host "         Fix shared library errors before continuing." -ForegroundColor Red
        Write-Host ""
        Write-Host "  Failed libraries:" -ForegroundColor Red
        foreach ($f in $sharedFailures) {
            Write-Host "    - $($f.Component)" -ForegroundColor Red
        }
        Write-Host ""
        exit 1
    }
    Write-Host ""
}
else {
    Write-Host "Step 1/5: Shared Libraries - SKIPPED (-SkipSharedLibs)" -ForegroundColor Yellow
    Write-Host ""
}

# --- Step 2: Vite Solutions ---
Write-Host "Step 2/5: Vite Solutions ($($ViteSolutions.Count) projects)" -ForegroundColor White
Write-Host "--------------------------------------" -ForegroundColor DarkGray
foreach ($sln in $ViteSolutions) {
    Invoke-ComponentBuild -Name $sln -BuildPath "$RepoRoot\src\solutions\$sln" -Category "Vite Solution"
}
Write-Host ""

# --- Step 3: Webpack Code Pages ---
Write-Host "Step 3/5: Webpack Code Pages ($($WebpackCodePages.Count) projects)" -ForegroundColor White
Write-Host "--------------------------------------" -ForegroundColor DarkGray
foreach ($cp in $WebpackCodePages) {
    Invoke-ComponentBuild -Name $cp -BuildPath "$RepoRoot\src\client\code-pages\$cp" -Category "Webpack Code Page"
}
Write-Host ""

# --- Step 4: PCF Controls ---
# One PCF at a time, production mode, same discovery rules as .github/workflows/pcf-build-prod-nightly.yml:
# a git-tracked src/client/pcf/<name>/package.json (exactly one level deep) with a build:prod script;
# an unparseable package.json is an error in both, never a silent drop. (The old single aggregate
# dev-mode `npm run build` at src/client/pcf never worked from a clean checkout and ran out of memory;
# nothing consumes its src/client/pcf/out output, so it is gone.)
$pcfStepSelected = (-not $Component) -or [bool]($Component | Where-Object { $_ -eq 'PCF' -or $_ -like 'PCF/*' })
if ($pcfStepSelected) {
    $pcfFolders = @()
    $pcfDiscoveryErrors = @()
    $pcfPackageJsons = @(git -C $RepoRoot ls-files -- 'src/client/pcf/*/package.json' |
        Where-Object { $_ -match '^src/client/pcf/[^/]+/package\.json$' })
    foreach ($rel in $pcfPackageJsons) {
        $dir = Split-Path (Join-Path $RepoRoot $rel) -Parent
        try {
            $scripts = (Get-Content -LiteralPath (Join-Path $dir 'package.json') -Raw | ConvertFrom-Json).scripts
        }
        catch {
            $pcfDiscoveryErrors += "PCF/$(Split-Path $dir -Leaf): package.json could not be parsed: $($_.Exception.Message)"
            continue
        }
        if ($scripts -and $scripts.'build:prod') { $pcfFolders += $dir }
    }

    Write-Host "Step 4/5: PCF Controls ($($pcfFolders.Count) discovered, npm run build:prod each)" -ForegroundColor White
    Write-Host "--------------------------------------" -ForegroundColor DarkGray

    foreach ($err in $pcfDiscoveryErrors) {
        Write-Host "  FAIL  $err" -ForegroundColor Red
        $null = $Results.Add([PSCustomObject]@{
            Component = ($err -split ':')[0]
            Category  = "PCF Controls"
            Status    = "FAILED"
            Duration  = "-"
            Detail    = $err
        })
    }

    if ($pcfFolders.Count -eq 0) {
        # Zero PCFs is a discovery bug, not an empty repo: fail loudly rather than report a vacuous green.
        Write-Host "  FAIL  No PCF with a build:prod script found under src/client/pcf (git ls-files)." -ForegroundColor Red
        $null = $Results.Add([PSCustomObject]@{
            Component = "PCF (discovery)"
            Category  = "PCF Controls"
            Status    = "FAILED"
            Duration  = "-"
            Detail    = "No git-tracked src/client/pcf/*/package.json with a build:prod script"
        })
    }

    foreach ($dir in $pcfFolders) {
        $leaf = Split-Path $dir -Leaf
        Invoke-ComponentBuild -Name "PCF/$leaf" -SelectBy "PCF", "PCF/$leaf" -BuildPath $dir `
            -Category "PCF Controls" -NpmScript "build:prod" -AlwaysInstall
    }
}
else {
    Write-Host "Step 4/5: PCF Controls - not selected by -Component" -ForegroundColor DarkGray
}
Write-Host ""

# --- Step 5: External SPA ---
Write-Host "Step 5/5: External SPA" -ForegroundColor White
Write-Host "--------------------------------------" -ForegroundColor DarkGray
Invoke-ComponentBuild -Name "ExternalSPA" -BuildPath "$RepoRoot\src\client\external-spa" -Category "External SPA"
Write-Host ""

# --- Unmatched -Component names ---
# A name that selected nothing (typo, or a PCF/<name> that does not exist) must not exit 0.
if ($Component -and $Component.Count -gt 0) {
    $sharedNames = @($SharedLibs | ForEach-Object { $_.Name })
    foreach ($req in $Component) {
        if ($MatchedFilters.Contains($req)) { continue }
        if ($SkipSharedLibs -and $req -in $sharedNames) { continue }   # skipped on purpose
        Write-Host "  FAIL  -Component '$req' matched no component" -ForegroundColor Red
        $null = $Results.Add([PSCustomObject]@{
            Component = "filter:$req"
            Category  = "Filter"
            Status    = "FAILED"
            Duration  = "-"
            Detail    = "-Component '$req' matched no component"
        })
    }
}

# --- Summary ---
$totalStopwatch.Stop()
$totalDuration = "{0:F1}s" -f $totalStopwatch.Elapsed.TotalSeconds

Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  Build Summary" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ""

if ($Results.Count -eq 0) {
    Write-Host "  No components matched the filter criteria." -ForegroundColor Yellow
}
else {
    # Column widths
    $nameWidth = ($Results | ForEach-Object { $_.Component.Length } | Measure-Object -Maximum).Maximum
    if ($nameWidth -lt 9) { $nameWidth = 9 }
    $catWidth = ($Results | ForEach-Object { $_.Category.Length } | Measure-Object -Maximum).Maximum
    if ($catWidth -lt 8) { $catWidth = 8 }

    # Header
    $header = "  {0,-$nameWidth}  {1,-$catWidth}  {2,-8}  {3}" -f "Component", "Category", "Status", "Duration"
    Write-Host $header -ForegroundColor White
    Write-Host ("  " + ("-" * ($nameWidth + $catWidth + 20))) -ForegroundColor DarkGray

    foreach ($r in $Results) {
        $color = switch ($r.Status) {
            "SUCCESS" { "Green" }
            "FAILED"  { "Red" }
            "SKIPPED" { "Yellow" }
            "WHATIF"  { "DarkGray" }
            default   { "White" }
        }
        $line = "  {0,-$nameWidth}  {1,-$catWidth}  {2,-8}  {3}" -f $r.Component, $r.Category, $r.Status, $r.Duration
        Write-Host $line -ForegroundColor $color
    }
}

Write-Host ""

$successCount = ($Results | Where-Object { $_.Status -eq "SUCCESS" }).Count
$failCount = ($Results | Where-Object { $_.Status -eq "FAILED" }).Count
$skipCount = ($Results | Where-Object { $_.Status -eq "SKIPPED" }).Count

Write-Host "  Total: $($Results.Count) components | " -NoNewline
Write-Host "$successCount succeeded" -ForegroundColor Green -NoNewline
if ($failCount -gt 0) {
    Write-Host " | $failCount failed" -ForegroundColor Red -NoNewline
}
if ($skipCount -gt 0) {
    Write-Host " | $skipCount skipped" -ForegroundColor Yellow -NoNewline
}
Write-Host " | $totalDuration total"
Write-Host ""

# Exit with error if any builds failed
if ($failCount -gt 0) {
    Write-Host "  Build completed with errors." -ForegroundColor Red
    exit 1
}
else {
    Write-Host "  All builds completed successfully." -ForegroundColor Green
    exit 0
}
