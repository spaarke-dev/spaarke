# Pack DueDatesWidget Solution
# Uses System.IO.Compression for forward slashes (required for Dataverse import)
# DO NOT use Compress-Archive - it creates backslashes which break import

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

Set-Location $PSScriptRoot
$version = "1.0.8"
$solutionName = "SpaarkeDueDatesWidget"
$zipPath = "bin\${solutionName}_v$version.zip"

Write-Host "Packing $solutionName v$version..."

# Create bin directory if it doesn't exist
if (-not (Test-Path "bin")) {
    New-Item -ItemType Directory -Path "bin" | Out-Null
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
        $filePath = Join-Path $PSScriptRoot $_
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
    $controlsDir = Join-Path $PSScriptRoot 'Controls\sprk_Spaarke.Controls.DueDatesWidget'
    if (Test-Path $controlsDir) {
        Get-ChildItem -Path $controlsDir -File | ForEach-Object {
            # Use forward slashes for Dataverse compatibility
            $entryName = 'Controls/sprk_Spaarke.Controls.DueDatesWidget/' + $_.Name
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
        Write-Host "  Run 'npm run build:prod' first, then copy files to Controls folder"
    }
} finally {
    $zip.Dispose()
}

Write-Host ""
Write-Host "Created: $zipPath" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:"
Write-Host "  1. Verify files in Controls folder are up to date"
Write-Host "  2. Import: pac solution import --path $zipPath --publish-changes"
