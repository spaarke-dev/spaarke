# Pack VisualHost Solution
#
# Guard (task 129): the committed bundle.js has shipped STALE before (PR #1413 bumped the manifest to
# 1.4.39 while bundle.js was still the 1.4.38 build), so a pack without a fresh
# `scripts/Invoke-PcfBuildProd.ps1` + copy of out/controls/control/bundle.js produced a zip labelled
# 1.4.39 that carried 1.4.38 code. Packing now fails unless the Solution manifest, solution.xml and
# bundle.js all carry $version. `-VerifyOnly` runs the guard without creating a zip.
param([switch]$VerifyOnly, [string]$BundlePath)
$version = "1.4.40"
$solutionName = "VisualHostSolution"
$controlName = "sprk_Spaarke.Visuals.VisualHost"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$binDir = "$scriptDir/bin"

# Stale-bundle guard
$bundlePath = if ($BundlePath) { $BundlePath } else { "$scriptDir/Controls/$controlName/bundle.js" }
$manifestPath = "$scriptDir/Controls/$controlName/ControlManifest.xml"
$solutionXmlPath = "$scriptDir/solution.xml"
$problems = @()
if ((Get-Content -LiteralPath $manifestPath -Raw) -notmatch ('<control [^>]*version="' + [regex]::Escape($version) + '"')) {
    $problems += "ControlManifest.xml does not declare version $version"
}
if ((Get-Content -LiteralPath $solutionXmlPath -Raw) -notmatch ('<Version>' + [regex]::Escape($version) + '</Version>')) {
    $problems += "solution.xml does not declare <Version>$version</Version>"
}
# The version must appear as the control's own version-badge string literal ("v<version> <bullet> <date>",
# VisualHostRoot.tsx). A bare substring would pass on a comment or a longer number such as 11.4.39.
$badge = '"v' + [regex]::Escape($version) + ' ' + [char]0x2022
if (-not (Select-String -LiteralPath $bundlePath -Pattern $badge -Encoding utf8 -Quiet)) {
    $problems += "bundle.js does not contain the version badge string literal ""v$version <bullet>"" (stale build). Run scripts/Invoke-PcfBuildProd.ps1 -PcfPath src/client/pcf/VisualHost and copy out/controls/control/bundle.js to Solution/Controls/$controlName/bundle.js"
}
if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "FAIL  $_" -ForegroundColor Red }
    exit 1
}
Write-Host "OK    manifest, solution.xml and bundle.js all carry $version" -ForegroundColor Green
if ($VerifyOnly) { exit 0 }

# Ensure bin directory exists
if (!(Test-Path $binDir)) {
    New-Item -ItemType Directory -Path $binDir -Force | Out-Null
}

# Create ZIP
Add-Type -AssemblyName System.IO.Compression.FileSystem

$zipPath = "$binDir/${solutionName}_v${version}.zip"
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

$zip = [System.IO.Compression.ZipFile]::Open($zipPath, 'Create')

# Add Content_Types with brackets in archive name (save temp file without brackets)
$contentTypesXml = @"
<?xml version="1.0" encoding="utf-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="xml" ContentType="application/octet-stream" />
  <Default Extension="js" ContentType="application/octet-stream" />
  <Default Extension="css" ContentType="application/octet-stream" />
</Types>
"@
$tempFile = "$scriptDir/Content_Types.xml"
$contentTypesXml | Out-File -FilePath $tempFile -Encoding utf8


$contentTypesEntry = $zip.CreateEntry('[Content_Types].xml')
$contentTypesStream = $contentTypesEntry.Open()
$contentTypesBytes = [System.IO.File]::ReadAllBytes($tempFile)
$contentTypesStream.Write($contentTypesBytes, 0, $contentTypesBytes.Length)
$contentTypesStream.Close()
Remove-Item $tempFile -Force

# Add other files
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, "$scriptDir/solution.xml", "solution.xml") | Out-Null
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, "$scriptDir/customizations.xml", "customizations.xml") | Out-Null
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, "$scriptDir/Controls/$controlName/ControlManifest.xml", "Controls/$controlName/ControlManifest.xml") | Out-Null
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, "$scriptDir/Controls/$controlName/bundle.js", "Controls/$controlName/bundle.js") | Out-Null
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, "$scriptDir/Controls/$controlName/styles.css", "Controls/$controlName/styles.css") | Out-Null

$zip.Dispose()

$size = (Get-Item $zipPath).Length
Write-Host "Created: $zipPath ($size bytes)" -ForegroundColor Green
