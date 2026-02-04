Add-Type -AssemblyName System.IO.Compression.FileSystem

$zipPath = "bin/AssociationResolverSolution_v1.0.1.zip"

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

$zip = [System.IO.Compression.ZipFile]::Open($zipPath, 'Create')

$files = @{
    "solution.xml" = "solution.xml"
    "customizations.xml" = "customizations.xml"
    "[Content_Types].xml" = "[Content_Types].xml"
    "Controls/sprk_Spaarke.Controls.AssociationResolver/ControlManifest.xml" = "Controls/sprk_Spaarke.Controls.AssociationResolver/ControlManifest.xml"
    "Controls/sprk_Spaarke.Controls.AssociationResolver/bundle.js" = "Controls/sprk_Spaarke.Controls.AssociationResolver/bundle.js"
    "Controls/sprk_Spaarke.Controls.AssociationResolver/styles.css" = "Controls/sprk_Spaarke.Controls.AssociationResolver/styles.css"
}

foreach ($entry in $files.GetEnumerator()) {
    $sourcePath = $entry.Key
    $entryName = $entry.Value

    if (Test-Path $sourcePath) {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $sourcePath, $entryName) | Out-Null
        Write-Host "Added: $entryName"
    } else {
        Write-Warning "Not found: $sourcePath"
    }
}

$zip.Dispose()
Write-Host "Solution packed: $zipPath" -ForegroundColor Green
