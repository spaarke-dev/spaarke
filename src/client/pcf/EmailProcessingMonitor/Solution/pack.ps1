# Pack EmailProcessingMonitor Solution
# Uses System.IO.Compression for forward slashes (required for Dataverse import).
# Reads Solution.xml + Customizations.xml from src/Other/ (legacy cdsproj layout)
# and writes them at lowercase root of the zip per Dataverse import rules.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

Set-Location $PSScriptRoot
$version       = "1.1.2"
$solutionName  = "EmailProcessingMonitorSolution"
$zipPath       = "bin/${solutionName}_v$version.zip"
$controlsRoot  = "Controls/sprk_Spaarke.EmailProcessingMonitor"

Write-Host "Packing $solutionName v$version..."

if (-not (Test-Path "bin")) {
    New-Item -ItemType Directory -Path "bin" | Out-Null
}

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

$zip = [System.IO.Compression.ZipFile]::Open($zipPath, 'Create')
try {
    # Solution.xml and Customizations.xml live under src/Other/ in this project;
    # they MUST be at the zip root with lowercase names for Dataverse to accept them.
    @(
        @{ Src = 'src/Other/Solution.xml';        Entry = 'solution.xml' }
        @{ Src = 'src/Other/Customizations.xml';  Entry = 'customizations.xml' }
        @{ Src = '[Content_Types].xml';            Entry = '[Content_Types].xml' }
    ) | ForEach-Object {
        $fullPath = Join-Path $PSScriptRoot $_.Src
        if (Test-Path -LiteralPath $fullPath) {
            Write-Host "  Adding: $($_.Entry)"
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, $fullPath, $_.Entry, [System.IO.Compression.CompressionLevel]::Optimal
            ) | Out-Null
        } else {
            Write-Warning "File not found: $fullPath"
        }
    }

    # Control bundle.js + ControlManifest.xml + css + resx
    $controlsDir = Join-Path $PSScriptRoot $controlsRoot
    if (Test-Path $controlsDir) {
        Get-ChildItem -Path $controlsDir -Recurse -File | ForEach-Object {
            $relative = (Resolve-Path -Relative $_.FullName) -replace '\\','/'
            # Strip the leading './' if present
            if ($relative.StartsWith('./')) { $relative = $relative.Substring(2) }
            Write-Host "  Adding: $relative"
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, $_.FullName, $relative, [System.IO.Compression.CompressionLevel]::Optimal
            ) | Out-Null
        }
    } else {
        Write-Warning "Controls directory not found: $controlsDir"
    }
} finally {
    $zip.Dispose()
}

Write-Host ""
Write-Host "Created: $zipPath" -ForegroundColor Green
Write-Host ""
Write-Host "To import:"
Write-Host "  pac solution import --path `"$((Resolve-Path $zipPath).Path)`" --publish-changes"
