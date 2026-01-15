$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

Set-Location $PSScriptRoot
$version = "2.13.1"
$zipPath = "bin\PlaybookBuilderHost_v$version.zip"

# Ensure bin directory exists
if (-not (Test-Path "bin")) { New-Item -ItemType Directory -Path "bin" | Out-Null }

# Remove existing ZIP if present
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

# Create ZIP with forward slashes (required for Dataverse import)
$zip = [System.IO.Compression.ZipFile]::Open($zipPath, 'Create')

try {
    # Add root XML files
    @('solution.xml', 'customizations.xml', '[Content_Types].xml') | ForEach-Object {
        $srcPath = Join-Path $PSScriptRoot $_
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $srcPath, $_, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        Write-Host "Added: $_"
    }

    # Add Controls folder with forward slashes
    $controlsDir = Join-Path $PSScriptRoot 'Controls\sprk_Spaarke.Controls.PlaybookBuilderHost'
    Get-ChildItem -Path $controlsDir | ForEach-Object {
        $entryName = 'Controls/sprk_Spaarke.Controls.PlaybookBuilderHost/' + $_.Name
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $entryName, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        Write-Host "Added: $entryName"
    }
} finally {
    $zip.Dispose()
}

Write-Host ""
Write-Host "Created: $zipPath"
Write-Host "Size: $((Get-Item $zipPath).Length) bytes"
