$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$zipPath = "bin\PlaybookBuilderHost_v2.2.0.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -LiteralPath "solution.xml","customizations.xml","[Content_Types].xml","Controls" -DestinationPath $zipPath
Write-Host "Created $zipPath"
