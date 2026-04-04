<#
.SYNOPSIS
    Build all client-side components in correct dependency order.

.DESCRIPTION
    Orchestrates the build of all Spaarke client components in the required order:
    1. Shared libraries (Spaarke.Auth, Spaarke.SdapClient, Spaarke.UI.Components)
    2. Vite solutions (20 projects in src/solutions/)
    3. Webpack code pages (4 projects in src/client/code-pages/)
    4. PCF controls (src/client/pcf/)
    5. External SPA (src/client/external-spa/)

    Each component runs npm ci followed by npm run build. Shared libraries must build first
    because downstream components depend on them.

.PARAMETER SkipSharedLibs
    Skip the shared library builds (step 1). Use when shared libs are already built
    and you only need to rebuild downstream components.

.PARAMETER Component
    Build only specific components by name. Accepts an array of component names.
    Names match directory names (e.g., "LegalWorkspace", "AnalysisWorkspace", "PCF").
    Special names: "SharedLibs", "PCF", "ExternalSPA".

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
    # Preview what would happen when building PCF controls.
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

# --- Configuration ---
$RepoRoot = (Resolve-Path "$PSScriptRoot\..").Path

# Shared libraries (build order matters)
$SharedLibs = @(
    @{ Name = "Spaarke.Auth";          Path = "$RepoRoot\src\client\shared\Spaarke.Auth" }
    @{ Name = "Spaarke.SdapClient";    Path = "$RepoRoot\src\client\shared\Spaarke.SdapClient" }
    @{ Name = "Spaarke.UI.Components"; Path = "$RepoRoot\src\client\shared\Spaarke.UI.Components" }
)

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
    "TodoDetailSidePane"
    "WorkspaceLayoutWizard"
)

# Webpack code pages (src/client/code-pages/)
$WebpackCodePages = @(
    "AnalysisWorkspace"
    "DocumentRelationshipViewer"
    "PlaybookBuilder"
    "SemanticSearch"
)

# --- Results Tracking ---
$Results = [System.Collections.ArrayList]::new()

function Invoke-ComponentBuild {
    param(
        [string]$Name,
        [string]$BuildPath,
        [string]$Category
    )

    # Filter check: if -Component was specified, only build matching components
    if ($Component -and $Component.Count -gt 0) {
        if ($Name -notin $Component) {
            return
        }
    }

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

    if (-not (Test-Path $BuildPath)) {
        Write-Host "  SKIP  $Name - directory not found: $BuildPath" -ForegroundColor Yellow
        $null = $Results.Add([PSCustomObject]@{
            Component = $Name
            Category  = $Category
            Status    = "SKIPPED"
            Duration  = "0.0s"
            Detail    = "Directory not found"
        })
        return
    }

    if ($PSCmdlet.ShouldProcess("$Name ($BuildPath)", "npm ci then npm run build")) {
        Write-Host "  BUILD $Name" -ForegroundColor Cyan -NoNewline
        Write-Host " - $BuildPath" -ForegroundColor DarkGray

        try {
            Push-Location $BuildPath

            # npm ci
            $ciOutput = & npm ci 2>&1
            if ($LASTEXITCODE -ne 0) {
                throw "npm ci failed (exit code $LASTEXITCODE)`n$($ciOutput | Out-String)"
            }

            # npm run build
            $buildOutput = & npm run build 2>&1
            if ($LASTEXITCODE -ne 0) {
                throw "npm run build failed (exit code $LASTEXITCODE)`n$($buildOutput | Out-String)"
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
Write-Host "Step 4/5: PCF Controls" -ForegroundColor White
Write-Host "--------------------------------------" -ForegroundColor DarkGray
Invoke-ComponentBuild -Name "PCF" -BuildPath "$RepoRoot\src\client\pcf" -Category "PCF Controls"
Write-Host ""

# --- Step 5: External SPA ---
Write-Host "Step 5/5: External SPA" -ForegroundColor White
Write-Host "--------------------------------------" -ForegroundColor DarkGray
Invoke-ComponentBuild -Name "ExternalSPA" -BuildPath "$RepoRoot\src\client\external-spa" -Category "External SPA"
Write-Host ""

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
