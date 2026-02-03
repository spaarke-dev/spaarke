Add-Type -AssemblyName System.IO.Compression.FileSystem

$zipPath = "bin/AssociationResolverSolution_v1.0.1.zip"

# Ensure bin directory exists
if (-not (Test-Path "bin")) {
    New-Item -ItemType Directory -Path "bin" | Out-Null
}

# Remove existing zip
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

$zip = [System.IO.Compression.ZipFile]::Open($zipPath, 'Create')

# Add Content_Types.xml with brackets in archive name
$contentTypesEntry = $zip.CreateEntry('[Content_Types].xml')
$contentTypesStream = $contentTypesEntry.Open()
$contentTypesBytes = [System.IO.File]::ReadAllBytes("Content_Types.xml")
$contentTypesStream.Write($contentTypesBytes, 0, $contentTypesBytes.Length)
$contentTypesStream.Close()
Write-Host "Added: [Content_Types].xml"

# Add solution.xml
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, "solution.xml", "solution.xml") | Out-Null
Write-Host "Added: solution.xml"

# Add customizations.xml
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, "customizations.xml", "customizations.xml") | Out-Null
Write-Host "Added: customizations.xml"

# Add control files
$controlPath = "Controls/sprk_Spaarke.Controls.AssociationResolver"
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, "$controlPath/ControlManifest.xml", "$controlPath/ControlManifest.xml") | Out-Null
Write-Host "Added: $controlPath/ControlManifest.xml"

[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, "$controlPath/bundle.js", "$controlPath/bundle.js") | Out-Null
Write-Host "Added: $controlPath/bundle.js"

[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, "$controlPath/styles.css", "$controlPath/styles.css") | Out-Null
Write-Host "Added: $controlPath/styles.css"

$zip.Dispose()

Write-Host ""
Write-Host "Solution packed successfully: $zipPath" -ForegroundColor Green
Write-Host "Size: $((Get-Item $zipPath).Length) bytes"
