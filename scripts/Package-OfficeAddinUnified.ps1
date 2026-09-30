<#
.SYNOPSIS
    Packages the combined Outlook + Word Spaarke add-in (unified manifest) into admin-center-ready zips.

.DESCRIPTION
    spaarkeai-word-add-in-r1 task 078 (FR-05). Webpack's SpaarkeUnifiedPackagePlugin emits the package parts into
    src/client/office-addins/dist/spaarke/. This script turns them into the two zips the Microsoft 365 admin
    center accepts (Settings -> Integrated apps -> Upload custom apps -> App type "Teams app"):

        spaarke-addin-<version>.zip        PRODUCTION  - hides the live XML add-ins on clients that can run it
        spaarke-addin-<version>-TEST.zip   TEST        - own id, "(TEST)" name, hides NOTHING; upload to "Just me"

    Each zip holds exactly manifest.json + color.png + outline.png at its root.

    Fails (non-zero exit) when:
      - the build output is missing (run the add-in build first);
      - color.png is not 192x192 or outline.png is not 32x32 (app-package validation requires both);
      - the manifest's icons.color / icons.outline do not name the files in the package.

    Output goes OUTSIDE dist/ on purpose: everything in dist/ is published to the public static site.
    *.zip is gitignored repo-wide.

.PARAMETER AddinRoot
    The office-addins package folder. Defaults to src/client/office-addins beside this script's repo root.

.PARAMETER OutputPath
    Where the zips are written. Defaults to <AddinRoot>/app-package.

.EXAMPLE
    # After `npm run build` in src/client/office-addins:
    .\scripts\Package-OfficeAddinUnified.ps1

.NOTES
    Decision and evidence: projects/spaarkeai-word-add-in-r1/notes/078-manifest-decision.md
#>
[CmdletBinding()]
param(
    [string]$AddinRoot = (Join-Path $PSScriptRoot '..\src\client\office-addins'),
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

$AddinRoot = (Resolve-Path $AddinRoot).Path
$source = Join-Path $AddinRoot 'dist/spaarke'
if (-not $OutputPath) { $OutputPath = Join-Path $AddinRoot 'app-package' }

if (-not (Test-Path $source)) {
    throw "No unified package build output at '$source'. Run the add-in build first (npm run build in $AddinRoot)."
}

function Get-PngSize {
    param([string]$Path)
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    # PNG signature (\x89PNG) then the IHDR chunk: width and height are big-endian UInt32 at offsets 16 and 20.
    if ($bytes.Length -lt 24 -or $bytes[1] -ne 0x50 -or $bytes[2] -ne 0x4E -or $bytes[3] -ne 0x47) {
        throw "'$Path' is not a PNG file."
    }
    $width = ([int]$bytes[16] -shl 24) -bor ([int]$bytes[17] -shl 16) -bor ([int]$bytes[18] -shl 8) -bor [int]$bytes[19]
    $height = ([int]$bytes[20] -shl 24) -bor ([int]$bytes[21] -shl 16) -bor ([int]$bytes[22] -shl 8) -bor [int]$bytes[23]
    return [pscustomobject]@{ Width = $width; Height = $height }
}

$requiredIcons = @{ 'color.png' = 192; 'outline.png' = 32 }
foreach ($icon in $requiredIcons.Keys) {
    $iconPath = Join-Path $source $icon
    if (-not (Test-Path $iconPath)) { throw "Missing package icon '$iconPath'." }
    $size = Get-PngSize -Path $iconPath
    $expected = $requiredIcons[$icon]
    if ($size.Width -ne $expected -or $size.Height -ne $expected) {
        throw "Package icon '$icon' is $($size.Width)x$($size.Height); the app package requires ${expected}x${expected}. Regenerate it with generate-icons.mjs."
    }
}

$variants = @(
    @{ Manifest = 'manifest.json'; Suffix = '' },
    @{ Manifest = 'manifest.test.json'; Suffix = '-TEST' }
)

New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null
$results = @()

foreach ($variant in $variants) {
    $manifestPath = Join-Path $source $variant.Manifest
    if (-not (Test-Path $manifestPath)) { throw "Missing '$manifestPath'." }
    $manifest = Get-Content -Raw -Path $manifestPath | ConvertFrom-Json

    foreach ($role in 'color', 'outline') {
        $named = $manifest.icons.$role
        if (-not $requiredIcons.ContainsKey($named)) {
            throw "$($variant.Manifest) icons.$role is '$named', which is not a file in the package (expected color.png / outline.png)."
        }
    }

    $stage = Join-Path ([System.IO.Path]::GetTempPath()) ("spaarke-addin-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage | Out-Null
    try {
        Copy-Item -Path $manifestPath -Destination (Join-Path $stage 'manifest.json')
        foreach ($icon in $requiredIcons.Keys) {
            Copy-Item -Path (Join-Path $source $icon) -Destination (Join-Path $stage $icon)
        }

        $zip = Join-Path $OutputPath ("spaarke-addin-$($manifest.version)$($variant.Suffix).zip")
        Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force

        $results += [pscustomobject]@{
            Package = Split-Path $zip -Leaf
            Id      = $manifest.id
            Version = $manifest.version
            Name    = $manifest.name.short
            HidesXml = [bool]$manifest.extensions[0].alternates
            SizeKB  = [math]::Round((Get-Item $zip).Length / 1KB, 1)
        }
    }
    finally {
        Remove-Item -Recurse -Force -Path $stage -ErrorAction SilentlyContinue
    }
}

$results | Format-Table -AutoSize | Out-String | Write-Host
Write-Host "Written to: $OutputPath"
Write-Host 'Upload: Microsoft 365 admin center -> Settings -> Integrated apps -> Upload custom apps -> App type "Teams app".'
Write-Host 'Test the -TEST package first, assigned to "Just me". See notes/078-manifest-decision.md for the cutover steps.'
