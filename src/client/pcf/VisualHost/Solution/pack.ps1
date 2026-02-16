# Pack VisualHost Solution
$version = "1.2.49"
$solutionName = "VisualHostSolution"
$controlName = "sprk_Spaarke.Visuals.VisualHost"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$binDir = "$scriptDir/bin"

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
