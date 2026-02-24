$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

Set-Location $PSScriptRoot
$version = "1.0.43"
$solutionName = "SpaarkeSemanticSearch"
$zipPath = "bin\${solutionName}_v$version.zip"

Write-Host "Packing $solutionName v$version..."

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
    $controlsDir = Join-Path $PSScriptRoot 'Controls\sprk_Sprk.SemanticSearchControl'
    if (Test-Path $controlsDir) {
        Get-ChildItem -Path $controlsDir -File | ForEach-Object {
            $entryName = 'Controls/sprk_Sprk.SemanticSearchControl/' + $_.Name
            Write-Host "  Adding: $entryName"
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, $_.FullName, $entryName, [System.IO.Compression.CompressionLevel]::Optimal
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
Write-Host "To import, run:" -ForegroundColor Cyan
Write-Host "  pac solution import --path `"$((Resolve-Path $zipPath).Path)`" --publish-changes"
