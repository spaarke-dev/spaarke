param(
    [Parameter(Mandatory=$true)]
    [string]$ControlName,

    [Parameter(Mandatory=$true)]
    [string]$ControlPath,

    [Parameter(Mandatory=$true)]
    [string]$DisplayName,

    [Parameter(Mandatory=$true)]
    [string]$Description
)

$namespace = "Spaarke.Controls"
$fullControlName = "sprk_$namespace.$ControlName"
$solutionName = "${ControlName}Solution"
$version = "1.0.1"

Write-Host "Creating solution for: $ControlName" -ForegroundColor Cyan

# Create directory structure
$solutionDir = "$ControlPath/Solution"
$controlsDir = "$solutionDir/Controls/$fullControlName"
$binDir = "$solutionDir/bin"

New-Item -ItemType Directory -Path $controlsDir -Force | Out-Null
New-Item -ItemType Directory -Path $binDir -Force | Out-Null

# Copy control files
$outDir = "$ControlPath/out/controls"
Copy-Item "$outDir/bundle.js" "$controlsDir/" -Force
Copy-Item "$outDir/ControlManifest.xml" "$controlsDir/" -Force
Copy-Item "$outDir/styles.css" "$controlsDir/" -Force -ErrorAction SilentlyContinue

# Create solution.xml
$solutionXml = @"
<?xml version="1.0" encoding="utf-8"?>
<ImportExportXml version="9.2.24014.198" SolutionPackageVersion="9.2" languagecode="1033" generatedBy="CRMPackager" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <SolutionManifest>
    <UniqueName>$solutionName</UniqueName>
    <LocalizedNames>
      <LocalizedName description="$DisplayName Solution" languagecode="1033" />
    </LocalizedNames>
    <Descriptions>
      <Description description="$Description" languagecode="1033" />
    </Descriptions>
    <Version>$version</Version>
    <Managed>0</Managed>
    <Publisher>
      <UniqueName>Spaarke</UniqueName>
      <LocalizedNames>
        <LocalizedName description="Spaarke" languagecode="1033" />
      </LocalizedNames>
      <Descriptions>
        <Description description="Spaarke Solutions Publisher" languagecode="1033" />
      </Descriptions>
      <EMailAddress xsi:nil="true" />
      <SupportingWebsiteUrl xsi:nil="true" />
      <CustomizationPrefix>sprk</CustomizationPrefix>
      <CustomizationOptionValuePrefix>65949</CustomizationOptionValuePrefix>
      <Addresses>
        <Address>
          <AddressNumber>1</AddressNumber>
          <AddressTypeCode>1</AddressTypeCode>
        </Address>
        <Address>
          <AddressNumber>2</AddressNumber>
          <AddressTypeCode>1</AddressTypeCode>
        </Address>
      </Addresses>
    </Publisher>
    <RootComponents>
      <RootComponent type="66" schemaName="$fullControlName" behavior="0" />
    </RootComponents>
    <MissingDependencies />
  </SolutionManifest>
</ImportExportXml>
"@
$solutionXml | Out-File -FilePath "$solutionDir/solution.xml" -Encoding utf8

# Create customizations.xml
$customizationsXml = @"
<?xml version="1.0" encoding="utf-8"?>
<ImportExportXml xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <Entities />
  <Roles />
  <Workflows />
  <FieldSecurityProfiles />
  <Templates />
  <EntityMaps />
  <EntityRelationships />
  <OrganizationSettings />
  <optionsets />
  <CustomControls>
    <CustomControl>
      <Name>$fullControlName</Name>
      <FileName>/Controls/$fullControlName/ControlManifest.xml</FileName>
    </CustomControl>
  </CustomControls>
  <SolutionPluginAssemblies />
  <EntityDataProviders />
  <Languages>
    <Language>1033</Language>
  </Languages>
</ImportExportXml>
"@
$customizationsXml | Out-File -FilePath "$solutionDir/customizations.xml" -Encoding utf8

# Create [Content_Types].xml (save without brackets first)
$contentTypesXml = @"
<?xml version="1.0" encoding="utf-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="xml" ContentType="application/octet-stream" />
  <Default Extension="js" ContentType="application/octet-stream" />
  <Default Extension="css" ContentType="application/octet-stream" />
</Types>
"@
$contentTypesXml | Out-File -FilePath "$solutionDir/Content_Types.xml" -Encoding utf8

# Create ZIP
Add-Type -AssemblyName System.IO.Compression.FileSystem

$zipPath = "$binDir/${solutionName}_v${version}.zip"
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

$zip = [System.IO.Compression.ZipFile]::Open($zipPath, 'Create')

# Add Content_Types with brackets in archive name
$contentTypesEntry = $zip.CreateEntry('[Content_Types].xml')
$contentTypesStream = $contentTypesEntry.Open()
$contentTypesBytes = [System.IO.File]::ReadAllBytes("$solutionDir/Content_Types.xml")
$contentTypesStream.Write($contentTypesBytes, 0, $contentTypesBytes.Length)
$contentTypesStream.Close()

# Add other files
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, "$solutionDir/solution.xml", "solution.xml") | Out-Null
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, "$solutionDir/customizations.xml", "customizations.xml") | Out-Null
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, "$controlsDir/ControlManifest.xml", "Controls/$fullControlName/ControlManifest.xml") | Out-Null
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, "$controlsDir/bundle.js", "Controls/$fullControlName/bundle.js") | Out-Null

# Add styles.css if it exists
if (Test-Path "$controlsDir/styles.css") {
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, "$controlsDir/styles.css", "Controls/$fullControlName/styles.css") | Out-Null
}

$zip.Dispose()

$size = (Get-Item $zipPath).Length
Write-Host "Created: $zipPath ($size bytes)" -ForegroundColor Green
