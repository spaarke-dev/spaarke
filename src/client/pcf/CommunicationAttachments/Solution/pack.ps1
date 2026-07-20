$version = "1.0.0"
$solutionName = "CommunicationAttachmentsSolution"
# Anchor output to the script folder so the pack works regardless of the caller's
# working directory (.NET ZipFile uses the PROCESS cwd, which PowerShell's `cd`
# does not update — anchoring to $PSScriptRoot avoids that mismatch).
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
  @{ Source = "Controls/sprk_Spaarke.Controls.CommunicationAttachments/ControlManifest.xml"; Entry = "Controls/sprk_Spaarke.Controls.CommunicationAttachments/ControlManifest.xml" },
  @{ Source = "Controls/sprk_Spaarke.Controls.CommunicationAttachments/bundle.js"; Entry = "Controls/sprk_Spaarke.Controls.CommunicationAttachments/bundle.js" },
  @{ Source = "Controls/sprk_Spaarke.Controls.CommunicationAttachments/styles.css"; Entry = "Controls/sprk_Spaarke.Controls.CommunicationAttachments/styles.css" }
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
