$ErrorActionPreference = 'Stop'

Set-Location $PSScriptRoot

# Clean and rebuild
Write-Host "Cleaning previous build..."
Remove-Item -Recurse -Force out, bin -ErrorAction SilentlyContinue

Write-Host "Building PCF control..."
npm run build 2>&1 | Out-Host

# Verify build output exists
$bundlePath = Get-ChildItem -Path "out\controls\*\bundle.js" -ErrorAction SilentlyContinue
if (-not $bundlePath) {
    Write-Error "Build failed - no bundle.js found in out\controls\"
    exit 1
}

Write-Host "Build successful!" -ForegroundColor Green
Write-Host "Bundle size: $([math]::Round($bundlePath.Length / 1MB, 2)) MB"

# Copy files to Solution/Controls folder
Write-Host ""
Write-Host "Copying files to Solution folder..."

$controlDest = "Solution\Controls\sprk_Spaarke.Controls.DocumentRelationshipViewer"

# Ensure destination exists
if (-not (Test-Path $controlDest)) {
    New-Item -ItemType Directory -Path $controlDest -Force | Out-Null
}

# Copy bundle.js
Copy-Item "out\controls\*\bundle.js" $controlDest -Force
Write-Host "  Copied: bundle.js"

# Copy ControlManifest.xml
Copy-Item "out\controls\*\ControlManifest.xml" $controlDest -Force
Write-Host "  Copied: ControlManifest.xml"

# Copy or create styles.css
$stylesSrc = Get-ChildItem -Path "out\controls\*\styles.css" -ErrorAction SilentlyContinue
if ($stylesSrc) {
    Copy-Item $stylesSrc.FullName $controlDest -Force
    Write-Host "  Copied: styles.css"
} else {
    "" | Out-File -FilePath "$controlDest\styles.css" -Encoding UTF8 -NoNewline
    Write-Host "  Created: styles.css (empty)"
}

Write-Host ""
Write-Host "Files in Solution folder:" -ForegroundColor Cyan
Get-ChildItem $controlDest | Format-Table Name, @{N='Size';E={if($_.Length -gt 1MB){"$([math]::Round($_.Length / 1MB, 2)) MB"}else{"$([math]::Round($_.Length / 1KB, 2)) KB"}}}
