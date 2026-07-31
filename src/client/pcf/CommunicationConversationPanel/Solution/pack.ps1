$version = "1.10.0"
$solutionName = "CommunicationConversationPanelSolution"
# Output is anchored to the SCRIPT dir (not the caller's CWD) so the zip always
# lands in Solution/bin/ regardless of where pack.ps1 is invoked from — the
# recurring "zip saved in the wrong place" bug (round-8 UAT). Sources below already
# use $PSScriptRoot; the output now matches.
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
  @{ Source = "Controls/sprk_Spaarke.Controls.CommunicationConversationPanel/ControlManifest.xml"; Entry = "Controls/sprk_Spaarke.Controls.CommunicationConversationPanel/ControlManifest.xml" },
  @{ Source = "Controls/sprk_Spaarke.Controls.CommunicationConversationPanel/bundle.js"; Entry = "Controls/sprk_Spaarke.Controls.CommunicationConversationPanel/bundle.js" },
  @{ Source = "Controls/sprk_Spaarke.Controls.CommunicationConversationPanel/styles.css"; Entry = "Controls/sprk_Spaarke.Controls.CommunicationConversationPanel/styles.css" }
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
