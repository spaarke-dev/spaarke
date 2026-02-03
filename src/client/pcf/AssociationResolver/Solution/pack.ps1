$version = "1.0.2"
$solutionName = "AssociationResolverSolution"
$outputPath = "bin"

# Create output directory
if (-not (Test-Path $outputPath)) {
    New-Item -ItemType Directory -Path $outputPath | Out-Null
}

$zipPath = "$outputPath/${solutionName}_v${version}.zip"

# Remove existing zip if present
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

# Create zip with forward slashes using .NET
Add-Type -AssemblyName System.IO.Compression.FileSystem

$zip = [System.IO.Compression.ZipFile]::Open($zipPath, 'Create')

# Add files with forward slashes
$files = @(
    @{ Source = "solution.xml"; Entry = "solution.xml" },
    @{ Source = "customizations.xml"; Entry = "customizations.xml" },
    @{ Source = "[Content_Types].xml"; Entry = "[Content_Types].xml" },
    @{ Source = "Controls/sprk_Spaarke.Controls.AssociationResolver/ControlManifest.xml"; Entry = "Controls/sprk_Spaarke.Controls.AssociationResolver/ControlManifest.xml" },
    @{ Source = "Controls/sprk_Spaarke.Controls.AssociationResolver/bundle.js"; Entry = "Controls/sprk_Spaarke.Controls.AssociationResolver/bundle.js" },
    @{ Source = "Controls/sprk_Spaarke.Controls.AssociationResolver/styles.css"; Entry = "Controls/sprk_Spaarke.Controls.AssociationResolver/styles.css" }
)

foreach ($file in $files) {
    $sourcePath = Join-Path $PSScriptRoot $file.Source
    # Use -LiteralPath for Test-Path to handle brackets in [Content_Types].xml
    if (Test-Path -LiteralPath $sourcePath) {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $sourcePath, $file.Entry) | Out-Null
        Write-Host "Added: $($file.Entry)"
    } else {
        Write-Warning "File not found: $sourcePath"
    }
}

$zip.Dispose()

Write-Host ""
Write-Host "Solution packed: $zipPath" -ForegroundColor Green
