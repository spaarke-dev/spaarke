param(
    [string]$DistHtml    = "$PSScriptRoot/dist/index.html",
    [string]$OutFile     = "$PSScriptRoot/dist/sprk_documentuploadwizard.html",
    [string]$CommandsJs  = "$PSScriptRoot/sprk_subgrid_commands.js",
    [string]$OutCommands = "$PSScriptRoot/dist/sprk_subgrid_commands.js"
)
$ErrorActionPreference = "Stop"

if (-not (Test-Path $DistHtml)) { throw "dist/index.html not found: $DistHtml -- run npm run build first" }

# --- Step 1: Copy inlined HTML to deployment name ---
# vite-plugin-singlefile already inlines all JS/CSS into index.html
Copy-Item $DistHtml $OutFile -Force

$kb = [Math]::Round((Get-Item $OutFile).Length / 1024, 0)
Write-Host "Created: $OutFile ($kb KB)"

# --- Step 2: Copy ribbon command script to dist/ ---
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
