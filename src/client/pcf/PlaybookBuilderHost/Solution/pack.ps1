$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

# Resolve script directory robustly (handles various invocation contexts)
$solutionDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
if (-not $solutionDir) { $solutionDir = Get-Location }
$solutionDir = Resolve-Path $solutionDir

$version = "2.20.2"
$binDir = Join-Path $solutionDir 'bin'
$zipPath = Join-Path $binDir "PlaybookBuilderHost_v$version.zip"

# Ensure bin directory exists
if (-not (Test-Path $binDir)) { New-Item -ItemType Directory -Path $binDir | Out-Null }

# Remove existing ZIP if present
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

# Create ZIP with forward slashes (required for Dataverse import)
$zip = [System.IO.Compression.ZipFile]::Open($zipPath, 'Create')

try {
    # Add root XML files
    @('solution.xml', 'customizations.xml', '[Content_Types].xml') | ForEach-Object {
        $srcPath = Join-Path $solutionDir $_
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $srcPath, $_, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        Write-Host "Added: $_"
    }

    # Add Controls folder with forward slashes
    $controlsDir = Join-Path $solutionDir 'Controls\sprk_Spaarke.Controls.PlaybookBuilderHost'
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
