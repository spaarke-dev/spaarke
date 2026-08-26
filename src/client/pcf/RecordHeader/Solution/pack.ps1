# Build + pack the Spaarke Record Header PCF solution (RecordHeaderPcf).
# Usage: .\pack.ps1 [-SkipBuild]
#
# Adapted from src/client/pcf/MatterHeader/Solution/pack.ps1 (record-header-and-notepad-r2
# task 033). Identity changed to Spaarke.Records.RecordHeader / RecordHeaderPcf; the
# root-XML ZIP entry names are now lowercased explicitly rather than incidentally
# (pcf-deploy SKILL.md "ZIP Entry Names MUST Be Lowercase" — a mixed-case
# solution.xml entry makes pac reject the ZIP as "The solution file is invalid").
#
# Steps:
#   1. Run `npm run build:prod` in the parent PCF folder (unless -SkipBuild)
#   2. Copy out/controls/*/{bundle.js,ControlManifest.xml,styles.css}
#      to Solution/Controls/sprk_Spaarke.Records.RecordHeader/
#   3. Zip solution.xml + customizations.xml + [Content_Types].xml + Controls/*
#      to Solution/bin/RecordHeaderPcf_v1.1.1.0.zip
#
# Requires: Node/npm on PATH. Does NOT require pac CLI (uses System.IO.Compression).

[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

Set-Location $PSScriptRoot
# ADR-020 version-sync location 5 of 5. Keep in lockstep with:
#   control/ControlManifest.Input.xml, control/version.ts, Solution/solution.xml,
#   Solution/Controls/sprk_Spaarke.Records.RecordHeader/ControlManifest.xml
$version = "1.1.1.0"
$solutionName = "RecordHeaderPcf"
$controlSchemaName = "sprk_Spaarke.Records.RecordHeader"
# ABSOLUTE paths throughout. `Set-Location` updates PowerShell's location but NOT
# the .NET process working directory, so `[System.IO.Compression.ZipFile]::Open`
# resolves a RELATIVE path against wherever the process happened to start —
# which fails with "Could not find a part of the path ...\bin\...zip" whenever
# pack.ps1 is invoked from anywhere but its own folder. (MatterHeader/Solution/
# pack.ps1 carries the same latent bug; it only ever ran from its own folder.)
$binDir = Join-Path $PSScriptRoot "bin"
$zipPath = Join-Path $binDir "${solutionName}_v$version.zip"
$pcfRoot = Split-Path $PSScriptRoot -Parent
$controlDest = Join-Path $PSScriptRoot "Controls\$controlSchemaName"

# ----- Step 1: Build -----
if (-not $SkipBuild) {
    Write-Host "Building RecordHeader PCF (production)..." -ForegroundColor Cyan

    if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
        Write-Error "npm not found on PATH. Install Node.js or use -SkipBuild if bundle.js already exists."
        exit 1
    }

    Push-Location $pcfRoot
    try {
        # Clean prior output
        Remove-Item -Recurse -Force "out" -ErrorAction SilentlyContinue

        # Repo convention: build:prod (NOT build) - per AP-1. `npm run build` is
        # dev mode: no tree-shaking, 5-10x bundle bloat, breaks NFR-02.
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
    Write-Host "  Bundle size: $([math]::Round($bundleFile.Length / 1KB, 2)) KB ($($bundleFile.Length) bytes)" -ForegroundColor Green
    if ($bundleFile.Length -gt 256000) {
        Write-Warning "Bundle exceeds the NFR-02 ceiling of 250 KB (256000 bytes). Do not ship without escalating."
    }

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

if (-not (Test-Path $binDir)) {
    New-Item -ItemType Directory -Path $binDir | Out-Null
}

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

$zip = [System.IO.Compression.ZipFile]::Open($zipPath, 'Create')
try {
    # Add root XML files. ZIP ENTRY NAMES MUST BE LOWERCASE - pac rejects the
    # solution otherwise. `[Content_Types].xml` is the one exception: its entry
    # name is fixed by the OPC spec and keeps its bracketed casing.
    @('solution.xml', 'customizations.xml', '[Content_Types].xml') | ForEach-Object {
        $fullPath = Join-Path $PSScriptRoot $_
        $entryName = if ($_ -eq '[Content_Types].xml') { $_ } else { $_.ToLowerInvariant() }
        # Use -LiteralPath to handle brackets in filenames
        if (Test-Path -LiteralPath $fullPath) {
            Write-Host "  Adding: $entryName"
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, $fullPath, $entryName, [System.IO.Compression.CompressionLevel]::Optimal
            ) | Out-Null
        } else {
            Write-Warning "File not found: $fullPath"
        }
    }

    # Add control files (bundle.js, ControlManifest.xml, styles.css). These keep
    # their casing - the path must match customizations.xml <FileName> exactly.
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
