param(
    [string]$IndexHtml = "$PSScriptRoot/index.html",
    [string]$BundleJs  = "$PSScriptRoot/out/bundle.js",
    [string]$OutFile   = "$PSScriptRoot/out/sprk_SprkChatPane.html"
)
$ErrorActionPreference = "Stop"

if (-not (Test-Path $IndexHtml)) { throw "index.html not found: $IndexHtml" }
if (-not (Test-Path $BundleJs))  { throw "bundle.js not found: $BundleJs -- run npm run build first" }

$html   = [System.IO.File]::ReadAllText($IndexHtml)
$bundle = [System.IO.File]::ReadAllText($BundleJs)

# Use literal .Replace() -- avoids regex $ interpretation on JS content
$inlined = $html.Replace('<script src="bundle.js"></script>', "<script>`n$bundle`n</script>")

[System.IO.File]::WriteAllText($OutFile, $inlined, [System.Text.Encoding]::UTF8)

$kb = [Math]::Round((Get-Item $OutFile).Length / 1024, 0)
Write-Host "Created: $OutFile ($kb KB)"
Write-Host ""
Write-Host "Deploy steps:"
Write-Host "  1. Open https://make.powerapps.com > Dataverse > Web resources > New"
Write-Host "  2. Name: sprk_SprkChatPane"
Write-Host "  3. Type: Webpage (HTML)"
Write-Host "  4. Upload: $OutFile"
Write-Host "  5. Save + Publish"
