# Scripted check for the pack.ps1 stale-bundle guard (task 129). Exit 0 = all cases behave as expected.
# Cases use temp bundle files via pack.ps1 -VerifyOnly -BundlePath; nothing is packed.
$ErrorActionPreference = 'Stop'
$pack = Join-Path $PSScriptRoot 'pack.ps1'
$bullet = [char]0x2022
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("packguard-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
$cases = @(
    @{ Name = 'fresh bundle (badge string literal)'; Content = "x{className:D.versionBadge},`"v1.4.39 $bullet 2026-10-08`"),y"; ExpectPass = $true },
    @{ Name = 'stale 1.4.38 bundle'; Content = "x`"v1.4.38 $bullet 2026-08-02`"y"; ExpectPass = $false },
    @{ Name = 'stale bundle + version in a comment (the K1 trick)'; Content = "x`"v1.4.38 $bullet 2026-08-02`"y/* 11.4.39 */"; ExpectPass = $false },
    @{ Name = 'stale bundle + bare 1.4.39 comment'; Content = "x`"v1.4.38 $bullet 2026-08-02`"y/* 1.4.39 */"; ExpectPass = $false },
    @{ Name = 'longer number v11.4.39'; Content = "x`"v11.4.39 $bullet 2026-10-08`"y"; ExpectPass = $false }
)
$bad = 0
try {
    foreach ($c in $cases) {
        $f = Join-Path $tmp 'bundle.js'
        [IO.File]::WriteAllText($f, $c.Content, (New-Object Text.UTF8Encoding($false)))
        # Manifest / solution.xml checks are real; only the bundle varies. Run in a child pwsh to capture the exit code.
        & pwsh -NoProfile -File $pack -VerifyOnly -BundlePath $f *> $null
        $passed = ($LASTEXITCODE -eq 0)
        if ($passed -ne $c.ExpectPass) { $bad++; Write-Host "FAIL  $($c.Name): guard passed=$passed, expected $($c.ExpectPass)" -ForegroundColor Red }
        else { Write-Host "ok    $($c.Name)" -ForegroundColor Green }
    }
} finally { Remove-Item -Recurse -Force $tmp }
if ($bad) { exit 1 }
