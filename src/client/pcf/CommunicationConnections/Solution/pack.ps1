$version = "1.3.0"
$solutionName = "CommunicationConnectionsSolution"
# Anchor output to the script dir — .NET compression APIs resolve relative paths
# against the PROCESS cwd (not PowerShell's Set-Location), so use an absolute path.
$outputPath = Join-Path $PSScriptRoot "bin"
if (-not (Test-Path $outputPath)) { New-Item -ItemType Directory -Path $outputPath | Out-Null }
$zipPath = Join-Path $outputPath "${solutionName}_v${version}.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::Open($zipPath, 'Create')
$files = @(
  @{ Source = "solution.xml"; Entry = "solution.xml" },
  @{ Source = "customizations.xml"; Entry = "customizations.xml" },
  @{ Source = "[Content_Types].xml"; Entry = "[Content_Types].xml" },
  @{ Source = "Controls/sprk_Spaarke.Controls.CommunicationConnections/ControlManifest.xml"; Entry = "Controls/sprk_Spaarke.Controls.CommunicationConnections/ControlManifest.xml" },
  @{ Source = "Controls/sprk_Spaarke.Controls.CommunicationConnections/bundle.js"; Entry = "Controls/sprk_Spaarke.Controls.CommunicationConnections/bundle.js" },
  @{ Source = "Controls/sprk_Spaarke.Controls.CommunicationConnections/styles.css"; Entry = "Controls/sprk_Spaarke.Controls.CommunicationConnections/styles.css" }
)
foreach ($file in $files) {
  $sourcePath = Join-Path $PSScriptRoot $file.Source
  if (Test-Path -LiteralPath $sourcePath) {
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $sourcePath, $file.Entry) | Out-Null
    Write-Host "Added: $($file.Entry)"
  } else { Write-Warning "File not found: $sourcePath" }
}
$zip.Dispose()
Write-Host "Solution packed: $zipPath" -ForegroundColor Green
