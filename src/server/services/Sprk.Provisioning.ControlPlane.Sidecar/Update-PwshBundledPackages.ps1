#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Build-time only (sidecar Dockerfile, stage 1): replaces NuGet packages that the PowerShell dotnet tool bundles
    with a patched servicing release, when PowerShell itself has not shipped one yet.

.DESCRIPTION
    PowerShell 7.6.7 (the newest tool package on 2026-10-14) still bundles System.Security.Cryptography.Xml and
    System.Security.Cryptography.Pkcs 10.0.10. Both carry fixed HIGH CVEs (CVE-2026-84008, -96958, -96959, -96960;
    fixed in 10.0.13), and the image Trivy gate fails on them. For each package this script:
      1. downloads the pinned version's .nupkg from nuget.org (a signed Microsoft package);
      2. overwrites every copy of the package's assembly under the tool install with its lib/net10.0 build;
      3. rewrites the package's version, path and sha512 in pwsh.deps.json, so the dependency manifest matches the
         files that ship (it is what both the host and the scanner read).
    A package already at or above the pinned version is left alone and says so: remove it from the Dockerfile's
    list once PowerShell ships the fix. Any surprise (no deps.json, no assembly, no lib/net10.0 build) fails the build.

    The Dockerfile's contract check then asserts that pwsh starts, loads each patched assembly at the pinned version,
    and still imports the EXO module.
#>
param(
    [Parameter(Mandatory)][string]$ToolRoot,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string[]]$Packages
)
$ErrorActionPreference = 'Stop'
$target = [version]$Version
# `pwsh -File` passes 'A,B' as ONE string: accept both a list and a comma-separated value.
$Packages = @($Packages | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })

# -Force: on Linux the tool's .store/ directory is HIDDEN (leading dot), and -Recurse skips hidden items without it.
$depsFiles = @(Get-ChildItem -Path $ToolRoot -Recurse -Force -Filter 'pwsh.deps.json' -File)
if ($depsFiles.Count -eq 0) { throw "No pwsh.deps.json under $ToolRoot." }

# One download per package; every manifest (the tool ships unix/ and win/ layouts) and every copy is patched from it.
$extracted = @{}
$listedIn = @{}
function Get-PatchedPackage([string]$Package) {
    if (-not $extracted.ContainsKey($Package)) {
        $lower = $Package.ToLowerInvariant()
        $work = Join-Path ([IO.Path]::GetTempPath()) "$lower.$Version"
        $nupkg = "$work.nupkg"
        Invoke-WebRequest -Uri "https://api.nuget.org/v3-flatcontainer/$lower/$Version/$lower.$Version.nupkg" -OutFile $nupkg
        Expand-Archive -Path $nupkg -DestinationPath $work -Force
        $sha512 = 'sha512-' + [Convert]::ToBase64String([Security.Cryptography.SHA512]::HashData([IO.File]::ReadAllBytes($nupkg)))
        $extracted[$Package] = @{ Dir = $work; Sha512 = $sha512 }
    }
    return $extracted[$Package]
}

foreach ($depsFile in $depsFiles) {
    $depsPath = $depsFile.FullName
    $appDir = $depsFile.DirectoryName
    $deps = Get-Content -Raw -Path $depsPath

    foreach ($package in $Packages) {
        $escaped = [regex]::Escape($package)
        $found = [regex]::Match($deps, "`"$escaped/(\d+\.\d+\.\d+)`"")
        if (-not $found.Success) { continue }   # the win/ layout uses the framework's copy; checked after the loop
        $listedIn[$package] = $true
        $current = $found.Groups[1].Value
        if ([version]$current -ge $target) {
            Write-Output "SKIP $package $current in $depsPath (already >= $Version): drop it from the Dockerfile's patch list."
            continue
        }

        $patched = Get-PatchedPackage $package
        # Each copy is replaced by the build at the SAME relative path in the package: runtimes/<rid>/lib/net10.0/ for a
        # platform-specific copy, lib/net10.0/ for the portable one.
        $copies = @(Get-ChildItem -Path $appDir -Recurse -Force -Filter "$package.dll" -File)
        if ($copies.Count -eq 0) { throw "No $package.dll under $appDir." }
        foreach ($copy in $copies) {
            $relative = [IO.Path]::GetRelativePath($appDir, $copy.FullName).Replace('\', '/')
            $source = if ($relative.StartsWith('runtimes/')) { Join-Path $patched.Dir $relative } else { Join-Path $patched.Dir "lib/net10.0/$package.dll" }
            if (-not (Test-Path $source)) { throw "$package $Version has no build for $relative (looked for $source)." }
            Copy-Item -Path $source -Destination $copy.FullName -Force
        }

        $lower = $package.ToLowerInvariant()
        $deps = $deps -creplace "`"$escaped/$([regex]::Escape($current))`"", "`"$package/$Version`""
        $deps = $deps -creplace "`"$escaped`": `"$([regex]::Escape($current))`"", "`"$package`": `"$Version`""
        $deps = $deps -creplace "`"$([regex]::Escape($lower))/$([regex]::Escape($current))`"", "`"$lower/$Version`""
        $deps = [regex]::Replace($deps, "(`"$escaped/$([regex]::Escape($Version))`": \{[^}]*?`"sha512`": `")sha512-[^`"]+", "`${1}$($patched.Sha512)")
        if ($deps -cmatch "`"$escaped/$([regex]::Escape($current))`"") { throw "$package $current is still referenced in $depsPath." }

        Write-Output "PATCHED $package $current -> $Version in $depsPath ($($copies.Count) assembly file(s))."
    }

    Set-Content -Path $depsPath -Value $deps -NoNewline -Encoding utf8NoBOM
}

$missing = @($Packages | Where-Object { -not $listedIn.ContainsKey($_) })
if ($missing.Count -gt 0) { throw "Not in any pwsh.deps.json under ${ToolRoot}: $($missing -join ', ')." }
