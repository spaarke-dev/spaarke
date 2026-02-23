$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

Set-Location $PSScriptRoot
$version = "3.14.0"
$zipPath = "bin\UniversalQuickCreate_v$version.zip"

if (-not (Test-Path "bin")) { New-Item -ItemType Directory -Path "bin" | Out-Null }
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

$zip = [System.IO.Compression.ZipFile]::Open($zipPath, 'Create')
try {
    # Root required files: solution.xml, customizations.xml, [Content_Types].xml
    # Per PCF-DEPLOYMENT-GUIDE.md these three MUST be at root with exact lowercase names

    # solution.xml (lowercase in ZIP)
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
        $zip,
        (Join-Path $PSScriptRoot "src\Other\Solution.xml"),
        "solution.xml",
        [System.IO.Compression.CompressionLevel]::Optimal
    ) | Out-Null

    # customizations.xml (lowercase in ZIP)
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
        $zip,
        (Join-Path $PSScriptRoot "src\Other\Customizations.xml"),
        "customizations.xml",
        [System.IO.Compression.CompressionLevel]::Optimal
    ) | Out-Null

    # [Content_Types].xml - create in-memory
    $contentTypes = @'
<?xml version="1.0" encoding="utf-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="xml" ContentType="application/octet-stream" />
  <Default Extension="js" ContentType="application/octet-stream" />
  <Default Extension="css" ContentType="application/octet-stream" />
  <Default Extension="html" ContentType="application/octet-stream" />
</Types>
'@
    $contentTypesBytes = [System.Text.Encoding]::UTF8.GetBytes($contentTypes)
    $entry = $zip.CreateEntry("[Content_Types].xml", [System.IO.Compression.CompressionLevel]::Optimal)
    $stream = $entry.Open()
    $stream.Write($contentTypesBytes, 0, $contentTypesBytes.Length)
    $stream.Close()

    # PCF Control files
    $controlDir = Join-Path $PSScriptRoot "src\WebResources\sprk_Spaarke.Controls.UniversalDocumentUpload"
    $controlPrefix = "Controls/sprk_Spaarke.Controls.UniversalDocumentUpload"

    @('bundle.js', 'ControlManifest.xml', 'styles.css') | ForEach-Object {
        $srcFile = Join-Path $controlDir $_
        if (Test-Path $srcFile) {
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, $srcFile, "$controlPrefix/$_", [System.IO.Compression.CompressionLevel]::Optimal
            ) | Out-Null
        }
    }

    # CSS subfolder
    $cssFile = Join-Path $controlDir "css\UniversalQuickCreate.css"
    if (Test-Path $cssFile) {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $zip, $cssFile, "$controlPrefix/css/UniversalQuickCreate.css", [System.IO.Compression.CompressionLevel]::Optimal
        ) | Out-Null
    }

    # Web Resources are managed manually in Dataverse (sprk_subgrid_commands.js)
    # to avoid GUID conflicts on import. Only the PCF control is in this solution.

} finally { $zip.Dispose() }
Write-Host "Created: $zipPath"
