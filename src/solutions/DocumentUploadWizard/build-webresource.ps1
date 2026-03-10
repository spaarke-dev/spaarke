param(
    [string]$IndexHtml   = "$PSScriptRoot/index.html",
    [string]$BundleJs    = "$PSScriptRoot/out/bundle.js",
    [string]$OutFile     = "$PSScriptRoot/out/sprk_documentuploadwizard.html",
    [string]$CommandsJs  = "$PSScriptRoot/sprk_subgrid_commands.js",
    [string]$OutCommands = "$PSScriptRoot/out/sprk_subgrid_commands.js"
)
$ErrorActionPreference = "Stop"

if (-not (Test-Path $IndexHtml)) { throw "index.html not found: $IndexHtml" }
if (-not (Test-Path $BundleJs))  { throw "bundle.js not found: $BundleJs -- run npm run build first" }

# --- Step 1: Inline bundle.js into HTML ---
$html   = [System.IO.File]::ReadAllText($IndexHtml)
$bundle = [System.IO.File]::ReadAllText($BundleJs)

# Use literal .Replace() -- avoids regex $ interpretation on JS content
$inlined = $html.Replace('<script src="bundle.js"></script>', "<script>`n$bundle`n</script>")

[System.IO.File]::WriteAllText($OutFile, $inlined, [System.Text.Encoding]::UTF8)

$kb = [Math]::Round((Get-Item $OutFile).Length / 1024, 0)
Write-Host "Created: $OutFile ($kb KB)"

# --- Step 2: Copy ribbon command script to out/ ---
if (Test-Path $CommandsJs) {
    Copy-Item $CommandsJs $OutCommands -Force
    Write-Host "Copied:  $OutCommands"
} else {
    Write-Host "WARNING: sprk_subgrid_commands.js not found at $CommandsJs -- skipping"
}

Write-Host ""
Write-Host "Deploy steps:"
Write-Host "  Web resources to upload in Dataverse:"
Write-Host "    1. sprk_documentuploadwizard  (Webpage HTML) -- $OutFile"
Write-Host "    2. sprk_subgrid_commands       (Script JS)    -- $OutCommands"
Write-Host "  Then: Save + Publish All"
