<#
.SYNOPSIS
    Pack the SpaarkeLegalWorkspace Dataverse solution into an importable ZIP.
.DESCRIPTION
    Creates a fresh Dataverse solution ZIP using System.IO.Compression (forward slashes).
    NEVER use Compress-Archive - it creates backslashes which break Dataverse import.
.PARAMETER Version
    Override the version string for the output ZIP filename.
    Default: reads version from solution.xml automatically.
.EXAMPLE
    .\pack.ps1
    .\pack.ps1 -Version "1.0.2"
#>

[CmdletBinding()]
param(
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

Set-Location $PSScriptRoot

$SolutionName = "SpaarkeLegalWorkspace"
$ControlFolderName = "sprk_Spaarke.LegalWorkspace"

# Auto-detect version from solution.xml if not provided
if (-not $Version) {
    [xml]$solutionXml = Get-Content "solution.xml" -Raw
    $Version = $solutionXml.ImportExportXml.SolutionManifest.Version
}

$ZipPath = "bin\${SolutionName}_v${Version}.zip"

Write-Host ""
Write-Host "============================================================"
Write-Host "  SpaarkeLegalWorkspace - Solution Packer"
Write-Host "============================================================"
Write-Host "  Solution : $SolutionName"
Write-Host "  Version  : $Version"
Write-Host "  Output   : $ZipPath"
Write-Host "============================================================"
Write-Host ""

# Verify bundle files exist
$ControlDir = Join-Path $PSScriptRoot "Controls\$ControlFolderName"
$RequiredFiles = @("bundle.js", "ControlManifest.xml")
$OptionalFiles = @("styles.css")

Write-Host "[1/3] Verifying build artifacts..."
$Missing = @()
foreach ($f in $RequiredFiles) {
    $path = Join-Path $ControlDir $f
    if (Test-Path $path) {
        $sizeKb = [math]::Round((Get-Item $path).Length / 1024, 1)
        Write-Host "      Found: $f ($sizeKb KB)"
    } else {
        $Missing += $f
        Write-Host "      MISSING: $f" -ForegroundColor Red
    }
}

foreach ($f in $OptionalFiles) {
    $path = Join-Path $ControlDir $f
    if (Test-Path $path) {
        $sizeKb = [math]::Round((Get-Item $path).Length / 1024, 1)
        Write-Host "      Found (optional): $f ($sizeKb KB)"
    } else {
        Write-Host "      Skipped (optional): $f"
    }
}

if ($Missing.Count -gt 0) {
    Write-Host ""
    Write-Host "  ERROR: Missing build artifacts. Run the following first:" -ForegroundColor Red
    Write-Host "    cd src/client/pcf/LegalWorkspace"
    Write-Host "    npm run build"
    Write-Host "    copy out\controls\bundle.js Solution\Controls\$ControlFolderName\"
    Write-Host "    copy out\controls\ControlManifest.xml Solution\Controls\$ControlFolderName\"
    exit 1
}

# Verify bundle size (NFR-02: must be under 5MB)
$BundleSize = (Get-Item (Join-Path $ControlDir "bundle.js")).Length
$BundleSizeKb = [math]::Round($BundleSize / 1024, 1)
Write-Host ""
Write-Host "[2/3] Verifying bundle size (NFR-02: under 5MB)..."
Write-Host "      bundle.js size: $BundleSizeKb KB"

if ($BundleSize -gt 5242880) {
    Write-Host "      ERROR: Bundle exceeds 5MB limit!" -ForegroundColor Red
    exit 1
} else {
    Write-Host "      PASS: Bundle is under 5MB limit."
}

# Pack the solution
Write-Host ""
Write-Host "[3/3] Packing solution..."

if (-not (Test-Path "bin")) {
    New-Item -ItemType Directory -Path "bin" | Out-Null
}

if (Test-Path $ZipPath) {
    Remove-Item $ZipPath -Force
    Write-Host "      Removed existing ZIP."
}

$Zip = [System.IO.Compression.ZipFile]::Open($ZipPath, 'Create')
try {
    # Root solution files
    $RootFiles = @('solution.xml', 'customizations.xml', '[Content_Types].xml')
    foreach ($File in $RootFiles) {
        $FullPath = Join-Path $PSScriptRoot $File
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $Zip,
            $FullPath,
            $File,
            [System.IO.Compression.CompressionLevel]::Optimal
        ) | Out-Null
        Write-Host "      Added: $File"
    }

    # Control bundle files (with forward-slash paths required by Dataverse)
    $EntryPrefix = "Controls/$ControlFolderName/"
    Get-ChildItem -Path $ControlDir -File | Where-Object { $_.Name -ne "README-bundle-files.txt" } | ForEach-Object {
        $EntryName = $EntryPrefix + $_.Name
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $Zip,
            $_.FullName,
            $EntryName,
            [System.IO.Compression.CompressionLevel]::Optimal
        ) | Out-Null
        Write-Host "      Added: $EntryName"
    }
} finally {
    $Zip.Dispose()
}

$ZipSizeKb = [math]::Round((Get-Item $ZipPath).Length / 1024, 1)
Write-Host ""
Write-Host "============================================================"
Write-Host "  Solution packed successfully!"
Write-Host "============================================================"
Write-Host "  Output: $ZipPath ($ZipSizeKb KB)"
Write-Host ""
Write-Host "  Next: Import to Dataverse dev environment:"
Write-Host "    pac solution import --path bin\${SolutionName}_v${Version}.zip --publish-changes"
Write-Host ""
