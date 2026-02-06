# Temporary script to pack solution
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$solutionDir = $PSScriptRoot
$version = "2.2.0"
$solutionName = "SpaarkeUniversalDatasetGrid"
$zipPath = Join-Path $solutionDir "bin\${solutionName}_v$version.zip"

Write-Host "Packing $solutionName v$version..."
Write-Host "Solution dir: $solutionDir"
Write-Host "Zip path: $zipPath"

# Create bin directory if it doesn't exist
$binDir = Join-Path $solutionDir "bin"
if (-not (Test-Path $binDir)) {
    New-Item -ItemType Directory -Path $binDir | Out-Null
}

# Remove existing zip if present
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

# Create new zip archive
$zip = [System.IO.Compression.ZipFile]::Open($zipPath, 'Create')
try {
    # Add root XML files
    @('solution.xml', 'customizations.xml', '[Content_Types].xml') | ForEach-Object {
        $filePath = Join-Path $solutionDir $_
        if (Test-Path -LiteralPath $filePath) {
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip,
                $filePath,
                $_,
                [System.IO.Compression.CompressionLevel]::Optimal
            ) | Out-Null
            Write-Host "  Added: $_"
        } else {
            Write-Warning "File not found: $_"
        }
    }

    # Add control files from Controls folder
    $controlsDir = Join-Path $solutionDir 'Controls\sprk_Spaarke.UI.Components.UniversalDatasetGrid'
    if (Test-Path $controlsDir) {
        Get-ChildItem -Path $controlsDir -File | ForEach-Object {
            # Use forward slashes for Dataverse compatibility
            $entryName = 'Controls/sprk_Spaarke.UI.Components.UniversalDatasetGrid/' + $_.Name
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip,
                $_.FullName,
                $entryName,
                [System.IO.Compression.CompressionLevel]::Optimal
            ) | Out-Null
            Write-Host "  Added: $entryName"
        }
    } else {
        Write-Warning "Controls directory not found: $controlsDir"
    }
} finally {
    $zip.Dispose()
}

Write-Host ""
Write-Host "Created: $zipPath" -ForegroundColor Green
