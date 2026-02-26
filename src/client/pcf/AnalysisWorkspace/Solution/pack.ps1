$version = "1.3.5"
$solutionName = "AnalysisWorkspaceSolution"
$controlName = "sprk_Spaarke.Controls.AnalysisWorkspace"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$controlsDir = "$scriptDir/Controls/$controlName"
$cssDir = "$controlsDir/css"
$outDir = "$scriptDir/../out/controls/control"
$binDir = "$scriptDir/bin"

Write-Host "Packing $solutionName v$version" -ForegroundColor Cyan

# Copy build output to Solution/Controls
New-Item -ItemType Directory -Path $controlsDir -Force | Out-Null
New-Item -ItemType Directory -Path $cssDir -Force | Out-Null
New-Item -ItemType Directory -Path $binDir -Force | Out-Null

Copy-Item "$outDir/bundle.js" "$controlsDir/" -Force
Copy-Item "$outDir/ControlManifest.xml" "$controlsDir/" -Force
if (Test-Path "$outDir/css") {
    Copy-Item "$outDir/css/*" "$cssDir/" -Force
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

# Add CSS files
$cssFiles = Get-ChildItem "$cssDir/*.css" -ErrorAction SilentlyContinue
foreach ($cssFile in $cssFiles) {
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $cssFile.FullName, "Controls/$controlName/css/$($cssFile.Name)") | Out-Null
}

$zip.Dispose()

$size = (Get-Item $zipPath).Length
Write-Host "Created: $zipPath ($([math]::Round($size/1KB, 1)) KB)" -ForegroundColor Green
