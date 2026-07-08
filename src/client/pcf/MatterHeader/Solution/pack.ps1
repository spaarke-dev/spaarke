# Build + pack MatterHeader PCF solution
# Usage: .\pack.ps1 [-SkipBuild]
#
# Steps:
#   1. Run `npm run build:prod` in the parent PCF folder (unless -SkipBuild)
#   2. Copy out/controls/*/{bundle.js,ControlManifest.xml,styles.css}
#      to Solution/Controls/sprk_Spaarke.Records.MatterHeader/
#   3. Zip solution.xml + customizations.xml + [Content_Types].xml + Controls/*
#      to Solution/bin/MatterHeaderPcf_v1.0.1.0.zip
#
# Requires: Node/npm on PATH. Does NOT require pac CLI (uses System.IO.Compression).

[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

Set-Location $PSScriptRoot
$version = "1.0.20.0"
$solutionName = "MatterHeaderPcf"
$controlSchemaName = "sprk_Spaarke.Records.MatterHeader"
$zipPath = "bin\${solutionName}_v$version.zip"
$pcfRoot = Split-Path $PSScriptRoot -Parent
$controlDest = Join-Path $PSScriptRoot "Controls\$controlSchemaName"

# ----- Step 1: Build -----
if (-not $SkipBuild) {
    Write-Host "Building MatterHeader PCF (production)..." -ForegroundColor Cyan

    if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
        Write-Error "npm not found on PATH. Install Node.js or use -SkipBuild if bundle.js already exists."
        exit 1
    }

    Push-Location $pcfRoot
    try {
        # Clean prior output
        Remove-Item -Recurse -Force "out" -ErrorAction SilentlyContinue

        # Repo convention: build:prod (NOT build) - per AP-1
        npm run build:prod 2>&1 | Out-Host
        if ($LASTEXITCODE -ne 0) {
            Write-Error "npm run build:prod failed with exit code $LASTEXITCODE"
            exit 1
        }
    } finally {
        Pop-Location
    }

    # Verify bundle
    $bundleFile = Get-ChildItem -Path (Join-Path $pcfRoot "out\controls\*\bundle.js") -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $bundleFile) {
        Write-Error "Build succeeded but no bundle.js found under $pcfRoot\out\controls\"
        exit 1
    }
    Write-Host "  Bundle size: $([math]::Round($bundleFile.Length / 1KB, 2)) KB" -ForegroundColor Green

    # ----- Step 2: Copy build output -----
    Write-Host "Copying build output to Solution/..." -ForegroundColor Cyan
    if (-not (Test-Path $controlDest)) {
        New-Item -ItemType Directory -Path $controlDest -Force | Out-Null
    }

    Copy-Item (Join-Path $pcfRoot "out\controls\*\bundle.js") $controlDest -Force
    Write-Host "  Copied: bundle.js"

    Copy-Item (Join-Path $pcfRoot "out\controls\*\ControlManifest.xml") $controlDest -Force
    Write-Host "  Copied: ControlManifest.xml"

    $stylesSrc = Get-ChildItem -Path (Join-Path $pcfRoot "out\controls\*\styles.css") -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($stylesSrc) {
        Copy-Item $stylesSrc.FullName $controlDest -Force
        Write-Host "  Copied: styles.css"
    } else {
        "" | Out-File -FilePath (Join-Path $controlDest "styles.css") -Encoding UTF8 -NoNewline
        Write-Host "  Created: styles.css (empty)"
    }
} else {
    Write-Host "Skipping build (-SkipBuild). Using existing files under $controlDest" -ForegroundColor Yellow
    if (-not (Test-Path (Join-Path $controlDest "bundle.js"))) {
        Write-Error "bundle.js not found at $controlDest. Cannot pack without build output."
        exit 1
    }
}

# ----- Step 3: Pack solution (System.IO.Compression - no pac CLI needed) -----
Write-Host "Packing $solutionName v$version..." -ForegroundColor Cyan

if (-not (Test-Path "bin")) {
    New-Item -ItemType Directory -Path "bin" | Out-Null
}

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

$zip = [System.IO.Compression.ZipFile]::Open($zipPath, 'Create')
try {
    # Add root XML files
    @('solution.xml', 'customizations.xml', '[Content_Types].xml') | ForEach-Object {
        $fullPath = Join-Path $PSScriptRoot $_
        # Use -LiteralPath to handle brackets in filenames
        if (Test-Path -LiteralPath $fullPath) {
            Write-Host "  Adding: $_"
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, $fullPath, $_, [System.IO.Compression.CompressionLevel]::Optimal
            ) | Out-Null
        } else {
            Write-Warning "File not found: $fullPath"
        }
    }

    # Add control files (bundle.js, ControlManifest.xml, styles.css)
    if (Test-Path $controlDest) {
        Get-ChildItem -Path $controlDest -File | ForEach-Object {
            $entryName = "Controls/$controlSchemaName/" + $_.Name
            Write-Host "  Adding: $entryName"
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, $_.FullName, $entryName, [System.IO.Compression.CompressionLevel]::Optimal
            ) | Out-Null
        }
    } else {
        Write-Warning "Controls directory not found: $controlDest"
    }
} finally {
    $zip.Dispose()
}

Write-Host ""
Write-Host "Created: $zipPath" -ForegroundColor Green
Write-Host ""

# Report import command (works whether or not pac is present)
if (Get-Command pac -ErrorAction SilentlyContinue) {
    Write-Host "To import, run:" -ForegroundColor Cyan
    Write-Host "  pac solution import --path `"$((Resolve-Path $zipPath).Path)`" --publish-changes"
} else {
    Write-Host "pac CLI not found on PATH. Install Power Platform CLI to import:" -ForegroundColor Yellow
    Write-Host "  https://learn.microsoft.com/power-platform/developer/cli/introduction"
    Write-Host "Then run:"
    Write-Host "  pac solution import --path `"$((Resolve-Path $zipPath).Path)`" --publish-changes"
}
