# Build Vite solutions by running `npm run build` directly.
# Skips `npm ci` (Build-AllClientComponents.ps1 uses it; fails on lock-file drift).
# Auto-runs `npm install --no-audit --no-fund` only when node_modules is missing.
param(
    [Parameter(Mandatory)]
    [string[]]$Solutions,
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$ResultsCsvPath
)

$ErrorActionPreference = 'Continue'

# When invoked via pwsh -File, comma-separated values arrive as one string.
$Solutions = $Solutions | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ }

$results = @()
foreach ($name in $Solutions) {
    $dir = Join-Path $RepoRoot "src/solutions/$name"
    if (-not (Test-Path $dir)) {
        Write-Host "SKIP  $name - directory not found" -ForegroundColor Yellow
        $results += [PSCustomObject]@{ Solution = $name; Status = 'SKIP'; Duration = 0; DistSizeKB = 0; Error = 'dir missing' }
        continue
    }
    if (-not (Test-Path (Join-Path $dir 'node_modules'))) {
        Write-Host "INSTALL $name (no node_modules)" -ForegroundColor Yellow
        Push-Location $dir
        & npm install --no-audit --no-fund 2>&1 | Out-Null
        $installCode = $LASTEXITCODE
        Pop-Location
        if ($installCode -ne 0) {
            Write-Host "FAIL  $name - npm install failed" -ForegroundColor Red
            $results += [PSCustomObject]@{ Solution = $name; Status = 'FAIL'; Duration = 0; DistSizeKB = 0; Error = 'npm install failed' }
            continue
        }
    }

    Write-Host "BUILD $name" -ForegroundColor Cyan
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    Push-Location $dir
    $buildLog = & npm run build 2>&1 | Out-String
    $code = $LASTEXITCODE
    Pop-Location
    $sw.Stop()
    $dur = [math]::Round($sw.Elapsed.TotalSeconds, 1)

    if ($code -ne 0) {
        Write-Host "FAIL  $name ($dur s)" -ForegroundColor Red
        $tail = ($buildLog -split "`r?`n" | Select-Object -Last 6) -join "`n"
        Write-Host $tail -ForegroundColor DarkRed
        $results += [PSCustomObject]@{ Solution = $name; Status = 'FAIL'; Duration = $dur; DistSizeKB = 0; Error = $tail }
        continue
    }

    # Look for the largest .html or .js file in dist/
    $dist = Join-Path $dir 'dist'
    $size = 0
    if (Test-Path $dist) {
        $sizes = Get-ChildItem $dist -Recurse -File -Include '*.html','*.js' -ErrorAction SilentlyContinue |
            Sort-Object Length -Descending | Select-Object -First 1
        if ($sizes) { $size = [math]::Round($sizes.Length / 1KB) }
    }

    Write-Host "PASS  $name ($dur s, $size KB)" -ForegroundColor Green
    $results += [PSCustomObject]@{ Solution = $name; Status = 'PASS'; Duration = $dur; DistSizeKB = $size; Error = '' }
}

Write-Host ''
Write-Host '================ BUILD SUMMARY ================'
$results | Format-Table -AutoSize Solution, Status, Duration, DistSizeKB
if ($ResultsCsvPath) { $results | Export-Csv -Path $ResultsCsvPath -NoTypeInformation -Force }
$failCount = ($results | Where-Object { $_.Status -eq 'FAIL' }).Count
Write-Host "Failures: $failCount" -ForegroundColor $(if ($failCount -eq 0) { 'Green' } else { 'Red' })
