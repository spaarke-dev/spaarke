$version = "1.2.6"
$solutionName = "ScopeConfigEditorSolution"
$controlName = "sprk_Sprk.ScopeConfigEditor"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$controlsDir = "$scriptDir/Controls/$controlName"
$outDir = "$scriptDir/../out/controls/ScopeConfigEditor"
$binDir = "$scriptDir/bin"

Write-Host "Packing $solutionName v$version" -ForegroundColor Cyan

# Copy build output to Solution/Controls
New-Item -ItemType Directory -Path $controlsDir -Force | Out-Null
New-Item -ItemType Directory -Path $binDir -Force | Out-Null

Copy-Item "$outDir/bundle.js" "$controlsDir/" -Force
Copy-Item "$outDir/ControlManifest.xml" "$controlsDir/" -Force
if (Test-Path "$outDir/styles.css") {
    Copy-Item "$outDir/styles.css" "$controlsDir/" -Force
}

# Create ZIP
Add-Type -AssemblyName System.IO.Compression.FileSystem

$zipPath = "$binDir/${solutionName}_v${version}.zip"
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

$zip = [System.IO.Compression.ZipFile]::Open($zipPath, 'Create')

# Add [Content_Types].xml with brackets in archive name
$contentTypesEntry = $zip.CreateEntry('[Content_Types].xml')
$contentTypesStream = $contentTypesEntry.Open()
$contentTypesBytes = [System.IO.File]::ReadAllBytes("$scriptDir/Content_Types.xml")
$contentTypesStream.Write($contentTypesBytes, 0, $contentTypesBytes.Length)
$contentTypesStream.Close()

# Add solution.xml and customizations.xml (lowercase entry names)
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, "$scriptDir/solution.xml", "solution.xml") | Out-Null
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, "$scriptDir/customizations.xml", "customizations.xml") | Out-Null

# Add control files
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, "$controlsDir/ControlManifest.xml", "Controls/$controlName/ControlManifest.xml") | Out-Null
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, "$controlsDir/bundle.js", "Controls/$controlName/bundle.js") | Out-Null

if (Test-Path "$controlsDir/styles.css") {
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, "$controlsDir/styles.css", "Controls/$controlName/styles.css") | Out-Null
}

$zip.Dispose()

$size = (Get-Item $zipPath).Length
Write-Host "Created: $zipPath ($([math]::Round($size/1KB, 1)) KB)" -ForegroundColor Green
