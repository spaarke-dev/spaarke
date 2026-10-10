# Scripted check for the pack.ps1 stale-bundle guard (task 129). Exit 0 = all cases behave as expected.
# Cases use temp bundle files via pack.ps1 -VerifyOnly -BundlePath; nothing is packed.
$ErrorActionPreference = 'Stop'
$pack = Join-Path $PSScriptRoot 'pack.ps1'
$bullet = [char]0x2022
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("packguard-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
# The version under test is pack.ps1's own, so a version bump does not break this check.
$v = [regex]::Match((Get-Content -LiteralPath $pack -Raw), '\$version = "([^"]+)"').Groups[1].Value
if (-not $v) { throw 'Could not read $version from pack.ps1' }
$cases = @(
    @{ Name = 'fresh bundle (badge string literal)'; Content = "x{className:D.versionBadge},`"v$v $bullet 2026-10-08`"),y"; ExpectPass = $true },
    @{ Name = 'stale 1.4.38 bundle'; Content = "x`"v1.4.38 $bullet 2026-08-02`"y"; ExpectPass = $false },
    @{ Name = 'stale bundle + version in a comment (the K1 trick)'; Content = "x`"v1.4.38 $bullet 2026-08-02`"y/* 1$v */"; ExpectPass = $false },
    @{ Name = "stale bundle + bare $v comment"; Content = "x`"v1.4.38 $bullet 2026-08-02`"y/* $v */"; ExpectPass = $false },
    @{ Name = "longer number v1$v"; Content = "x`"v1$v $bullet 2026-10-08`"y"; ExpectPass = $false }
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
exit 0  # all cases matched; without this the script returns the last child's exit code (the expected-fail case)
