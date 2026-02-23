<#
.SYNOPSIS
    Full packaging pipeline for the LegalWorkspace PCF control — build, verify, pack, and deploy.

.DESCRIPTION
    Automates the complete PCF solution packaging workflow per PCF-V9-PACKAGING.md and
    the PCF-DEPLOYMENT-GUIDE.md. Handles:
      1. npm run build (production build)
      2. Bundle size verification (NFR-02: < 5MB)
      3. Copy fresh build artifacts to Solution folder
      4. Pack solution ZIP using System.IO.Compression (forward slashes — required)
      5. (Optional) Import solution to Dataverse dev environment

    ADR-022: Uses UNMANAGED solution only (never managed in dev).
    ADR-022: Platform libraries (React, Fluent) are externalized — not bundled.
    ADR-006: PCF output must be a valid Dataverse PCF artifact.
    NFR-02:  Bundle must be under 5MB.

.PARAMETER Environment
    Target Dataverse environment URL. Default: https://spaarkedev1.crm.dynamics.com

.PARAMETER SkipBuild
    Skip npm run build step (use existing out/ folder). Useful for pack-only iteration.

.PARAMETER PackOnly
    Pack the ZIP only — skip build and deploy steps.

.PARAMETER Deploy
    After packing, import the solution to the target Dataverse environment.
    Requires active pac auth (run `pac auth create` first if not authenticated).

.PARAMETER SkipCpmDisable
    Skip disabling Directory.Packages.props before pac commands.
    Only use if CPM is not configured for this repo.

.EXAMPLE
    .\Package-LegalWorkspace.ps1
    # Build, verify size, copy artifacts, pack ZIP. No deploy.

.EXAMPLE
    .\Package-LegalWorkspace.ps1 -Deploy
    # Full pipeline: build, verify, pack, and import to spaarkedev1.

.EXAMPLE
    .\Package-LegalWorkspace.ps1 -SkipBuild -Deploy
    # Pack existing build and deploy (faster for iterative deploys).

.EXAMPLE
    .\Package-LegalWorkspace.ps1 -PackOnly
    # Pack ZIP from artifacts already in Solution/Controls/ folder.

.NOTES
    Version bump checklist (ALL 4 locations must match before running):
      1. src/client/pcf/LegalWorkspace/ControlManifest.Input.xml   (version attribute)
      2. src/client/pcf/LegalWorkspace/index.ts                     (CONTROL_VERSION constant)
      3. src/client/pcf/LegalWorkspace/Solution/solution.xml        (Version element)
      4. src/client/pcf/LegalWorkspace/Solution/Controls/.../ControlManifest.xml

    If any location is out of sync, solution import will succeed but the wrong
    version may be registered in Dataverse. Always verify the UI footer after import.
#>

[CmdletBinding()]
param(
    [string]$Environment = "https://spaarkedev1.crm.dynamics.com",
    [switch]$SkipBuild,
    [switch]$PackOnly,
    [switch]$Deploy,
    [switch]$SkipCpmDisable
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

# ---- Paths ----------------------------------------------------------------

$ScriptDir    = Split-Path $MyInvocation.MyCommand.Path -Parent
$RepoRoot     = Split-Path $ScriptDir -Parent
$PcfDir       = Join-Path $RepoRoot "src\client\pcf\LegalWorkspace"
$SolutionDir  = Join-Path $PcfDir "Solution"
$ControlDir   = Join-Path $SolutionDir "Controls\sprk_Spaarke.Controls.LegalWorkspace"
$OutDir       = Join-Path $PcfDir "out\controls\control"
$CpmProps     = Join-Path $RepoRoot "Directory.Packages.props"

$SolutionName = "SpaarkeLegalWorkspace"
$ControlName  = "sprk_Spaarke.Controls.LegalWorkspace"

# Read version from solution.xml
[xml]$SolutionXml = Get-Content (Join-Path $SolutionDir "solution.xml") -Raw
$Version = $SolutionXml.ImportExportXml.SolutionManifest.Version
$ZipPath = Join-Path $SolutionDir "bin\${SolutionName}_v${Version}.zip"

# ---- Banner ---------------------------------------------------------------

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  LegalWorkspace PCF — Solution Packaging Pipeline" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Control   : $ControlName" -ForegroundColor White
Write-Host "  Solution  : $SolutionName" -ForegroundColor White
Write-Host "  Version   : $Version" -ForegroundColor White
Write-Host "  PCF Dir   : $PcfDir" -ForegroundColor White
Write-Host "  Output    : $ZipPath" -ForegroundColor White
if ($Deploy) {
    Write-Host "  Target    : $Environment" -ForegroundColor White
}
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""

$Step = 1

# ---- Step 1: Version Consistency Check ------------------------------------

Write-Host "[$Step/6] Checking version consistency across all 4 locations..." -ForegroundColor Yellow
$Step++

$Versions = @{}

# Location 1: ControlManifest.Input.xml
[xml]$ManifestInput = Get-Content (Join-Path $PcfDir "ControlManifest.Input.xml") -Raw
$Versions['ControlManifest.Input.xml'] = $ManifestInput.manifest.control.version

# Location 2: index.ts (CONTROL_VERSION constant)
$IndexContent = Get-Content (Join-Path $PcfDir "index.ts") -Raw
if ($IndexContent -match 'CONTROL_VERSION\s*=\s*"([0-9.]+)"') {
    $Versions['index.ts'] = $Matches[1]
} else {
    Write-Host "      WARNING: Could not parse CONTROL_VERSION from index.ts" -ForegroundColor Yellow
    $Versions['index.ts'] = "(undetected)"
}

# Location 3: Solution/solution.xml
$Versions['Solution/solution.xml'] = $Version

# Location 4: Solution/Controls/.../ControlManifest.xml
[xml]$ControlManifest = Get-Content (Join-Path $ControlDir "ControlManifest.xml") -Raw
$Versions['Solution/Controls/.../ControlManifest.xml'] = $ControlManifest.manifest.control.version

$AllMatch = $true
foreach ($loc in $Versions.GetEnumerator()) {
    $match = ($loc.Value -eq $Version)
    $icon = if ($match) { "OK" } else { "MISMATCH" }
    $color = if ($match) { "Green" } else { "Red" }
    Write-Host ("      {0,-50} {1,-12} [{2}]" -f $loc.Key, $loc.Value, $icon) -ForegroundColor $color
    if (-not $match) { $AllMatch = $false }
}

if (-not $AllMatch) {
    Write-Host ""
    Write-Host "  ERROR: Version mismatch detected!" -ForegroundColor Red
    Write-Host "  All 4 locations must contain version $Version." -ForegroundColor Yellow
    Write-Host "  Update mismatched files before packaging." -ForegroundColor Yellow
    exit 1
}

Write-Host "      All 4 version locations match: $Version" -ForegroundColor Green
Write-Host ""

# ---- Step 2: Build (npm run build) ----------------------------------------

if (-not $SkipBuild -and -not $PackOnly) {
    Write-Host "[$Step/6] Building PCF control (production mode)..." -ForegroundColor Yellow
    $Step++

    Push-Location $PcfDir
    try {
        # Clean previous output
        if (Test-Path "out") {
            Remove-Item "out" -Recurse -Force
            Write-Host "      Cleaned previous build output." -ForegroundColor Gray
        }
        if (Test-Path "bin") {
            Remove-Item "bin" -Recurse -Force
        }

        # Build
        Write-Host "      Running: npm run build" -ForegroundColor Gray
        npm run build
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  ERROR: npm run build failed (exit code $LASTEXITCODE)" -ForegroundColor Red
            exit $LASTEXITCODE
        }
        Write-Host "      Build succeeded." -ForegroundColor Green
    } finally {
        Pop-Location
    }
} else {
    Write-Host "[$Step/6] Skipping build (-SkipBuild or -PackOnly)..." -ForegroundColor Gray
    $Step++
}
Write-Host ""

# ---- Step 3: Bundle Size Verification (NFR-02) ----------------------------

Write-Host "[$Step/6] Verifying bundle size (NFR-02: < 5MB)..." -ForegroundColor Yellow
$Step++

if (-not $PackOnly) {
    $BundlePath = Join-Path $OutDir "bundle.js"
    if (-not (Test-Path $BundlePath)) {
        Write-Host "  ERROR: bundle.js not found at $BundlePath" -ForegroundColor Red
        Write-Host "  Run build first (remove -SkipBuild or -PackOnly)." -ForegroundColor Yellow
        exit 1
    }

    $BundleBytes = (Get-Item $BundlePath).Length
    $BundleMb    = [math]::Round($BundleBytes / 1MB, 3)

    Write-Host "      bundle.js size: $BundleMb MB" -ForegroundColor White

    if ($BundleBytes -gt 5MB) {
        Write-Host "      FAIL: Bundle exceeds 5MB (NFR-02)!" -ForegroundColor Red
        Write-Host "      Review bundle-optimization.md and reduce bundle size before packaging." -ForegroundColor Yellow
        exit 1
    } else {
        Write-Host "      PASS: Bundle is $BundleMb MB — under 5MB limit." -ForegroundColor Green
    }
} else {
    Write-Host "      Skipping size check (-PackOnly). Verify $ControlDir\bundle.js manually." -ForegroundColor Gray
}
Write-Host ""

# ---- Step 4: Copy Fresh Build to Solution Folder --------------------------

if (-not $PackOnly) {
    Write-Host "[$Step/6] Copying build artifacts to Solution folder..." -ForegroundColor Yellow
    $Step++

    $FilesToCopy = @("bundle.js", "ControlManifest.xml", "styles.css")
    foreach ($File in $FilesToCopy) {
        $Src = Join-Path $OutDir $File
        $Dst = Join-Path $ControlDir $File
        if (Test-Path $Src) {
            Copy-Item $Src $Dst -Force
            $SizeMb = [math]::Round((Get-Item $Dst).Length / 1KB, 1)
            Write-Host "      Copied: $File ($SizeMb KB)" -ForegroundColor Green
        } elseif ($File -eq "styles.css") {
            # styles.css may be empty or absent for some PCF controls — not a fatal error
            Write-Host "      NOTE: styles.css not found in out/ — creating empty placeholder." -ForegroundColor Yellow
            "" | Set-Content $Dst -Encoding UTF8
        } else {
            Write-Host "      ERROR: $File not found in out/controls/control/" -ForegroundColor Red
            exit 1
        }
    }

    Write-Host "      Artifacts copied to $ControlDir" -ForegroundColor Green
    Write-Host ""
} else {
    $Step++
}

# ---- Step 5: Pack Solution ZIP --------------------------------------------

Write-Host "[$Step/6] Packing solution ZIP..." -ForegroundColor Yellow
$Step++

$BinDir = Join-Path $SolutionDir "bin"
if (-not (Test-Path $BinDir)) {
    New-Item -ItemType Directory -Path $BinDir | Out-Null
}
if (Test-Path $ZipPath) {
    Remove-Item $ZipPath -Force
    Write-Host "      Removed existing ZIP." -ForegroundColor Gray
}

$Zip = [System.IO.Compression.ZipFile]::Open($ZipPath, 'Create')
try {
    # Root solution XML files
    foreach ($File in @('solution.xml', 'customizations.xml', '[Content_Types].xml')) {
        $FilePath = Join-Path $SolutionDir $File
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $Zip, $FilePath, $File,
            [System.IO.Compression.CompressionLevel]::Optimal
        ) | Out-Null
        Write-Host "      Packed: $File" -ForegroundColor Gray
    }

    # Control bundle files — CRITICAL: must use forward slashes in entry names
    $EntryPrefix = "Controls/$ControlName/"
    Get-ChildItem -Path $ControlDir -File | ForEach-Object {
        $EntryName = $EntryPrefix + $_.Name
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $Zip, $_.FullName, $EntryName,
            [System.IO.Compression.CompressionLevel]::Optimal
        ) | Out-Null
        Write-Host "      Packed: $EntryName" -ForegroundColor Gray
    }
} finally {
    $Zip.Dispose()
}

$ZipSizeMb = [math]::Round((Get-Item $ZipPath).Length / 1KB, 1)
Write-Host "      Created: $ZipPath ($ZipSizeMb KB)" -ForegroundColor Green
Write-Host ""

# ---- Step 6: Deploy to Dataverse (Optional) -------------------------------

if ($Deploy) {
    Write-Host "[$Step/6] Importing solution to Dataverse dev environment..." -ForegroundColor Yellow

    # Disable CPM to prevent NU1008 error during pac commands
    $CpmDisabled = $false
    if (-not $SkipCpmDisable -and (Test-Path $CpmProps)) {
        $CpmDisabledPath = "$CpmProps.disabled"
        Rename-Item $CpmProps $CpmDisabledPath -Force
        $CpmDisabled = $true
        Write-Host "      CPM disabled (Directory.Packages.props renamed)." -ForegroundColor Gray
    }

    try {
        # Verify pac auth
        Write-Host "      Checking pac authentication..." -ForegroundColor Gray
        $AuthList = pac auth list --json 2>&1 | ConvertFrom-Json
        $ActiveAuth = $AuthList | Where-Object { $_.IsActive -eq $true } | Select-Object -First 1

        if (-not $ActiveAuth) {
            Write-Host "  ERROR: No active pac auth. Run: pac auth create --url $Environment" -ForegroundColor Red
            exit 1
        }
        Write-Host "      Authenticated to: $($ActiveAuth.Url)" -ForegroundColor Green

        # Import solution
        Write-Host "      Importing $ZipPath..." -ForegroundColor Gray
        Write-Host "      This may take 1-5 minutes..." -ForegroundColor Gray
        pac solution import --path $ZipPath --publish-changes --force-overwrite

        if ($LASTEXITCODE -ne 0) {
            Write-Host "  ERROR: Solution import failed." -ForegroundColor Red
            Write-Host "  Check the import log above for specific errors." -ForegroundColor Yellow
            exit $LASTEXITCODE
        }

        Write-Host "      Solution imported successfully." -ForegroundColor Green

        # Verify
        Write-Host "      Verifying solution in environment..." -ForegroundColor Gray
        pac solution list | Select-String -Pattern "$SolutionName" -SimpleMatch

    } finally {
        # Restore CPM
        if ($CpmDisabled -and (Test-Path "$CpmProps.disabled")) {
            Rename-Item "$CpmProps.disabled" $CpmProps -Force
            Write-Host "      CPM restored (Directory.Packages.props renamed back)." -ForegroundColor Gray
        }
    }
} else {
    Write-Host "[$Step/6] Skipping deploy (add -Deploy flag to import to Dataverse)." -ForegroundColor Gray
}

# ---- Summary --------------------------------------------------------------

Write-Host ""
Write-Host "============================================================" -ForegroundColor Green
Write-Host "  Packaging Complete!" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
Write-Host "  Solution  : $SolutionName v$Version" -ForegroundColor White
Write-Host "  ZIP       : $ZipPath" -ForegroundColor White
Write-Host ""
if (-not $Deploy) {
    Write-Host "  To import to Dataverse:" -ForegroundColor White
    Write-Host "    pac solution import --path `"$ZipPath`" --publish-changes" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  Or run with -Deploy flag:" -ForegroundColor White
    Write-Host "    scripts\Package-LegalWorkspace.ps1 -Deploy" -ForegroundColor Gray
}
Write-Host ""
Write-Host "  Post-import steps:" -ForegroundColor White
Write-Host "    1. Open Custom Page in make.powerapps.com -> Save -> Publish" -ForegroundColor Gray
Write-Host "    2. Run: pac solution publish-all" -ForegroundColor Gray
Write-Host "    3. Hard refresh browser (Ctrl+Shift+R)" -ForegroundColor Gray
Write-Host "    4. Verify UI footer shows v$Version" -ForegroundColor Gray
Write-Host "============================================================" -ForegroundColor Green
Write-Host ""
