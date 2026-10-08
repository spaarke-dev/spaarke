<#
.SYNOPSIS
    Export SpaarkeMaster from the authoring environment and write it into git as unpacked source (both package
    types), with no environment-variable values.

.DESCRIPTION
    T218c (ADR-027 §4 amended 2026-10-07: git is the source of record; CI packs the zips — T218d).
      1. pac solution export, unmanaged AND managed, into a work folder (reads the environment; changes nothing).
      2. pac solution unpack --packagetype Both into -OutputFolder (default src/dataverse/solutions/SpaarkeMaster),
         replacing what is there, so the PR diff is the release.
      3. Remove every environmentvariablevalues.json (values are per customer — H7 writes them; a dev value in a
         customer environment is a cross-environment leak) and fail if one survives.

    Prerequisite: `pac auth` selected on the authoring environment (pac auth list / pac auth select). The script
    never creates or changes a pac profile.

.PARAMETER WhatIf
    Print the commands and paths; export nothing, write nothing.

.EXAMPLE
    ./Export-SpaarkeMasterSource.ps1 -WhatIf
.EXAMPLE
    ./Export-SpaarkeMasterSource.ps1
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$SolutionName = 'SpaarkeMaster',
    [string]$OutputFolder = "$PSScriptRoot/../../src/dataverse/solutions/SpaarkeMaster",
    [string]$WorkFolder = (Join-Path ([IO.Path]::GetTempPath()) "spaarkemaster-export-$(Get-Date -Format yyyyMMddHHmmss)")
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SpaarkePackageScope.psm1') -Force
$OutputFolder = [IO.Path]::GetFullPath($OutputFolder)

$unmanagedZip = Join-Path $WorkFolder "$SolutionName.zip"
$managedZip = Join-Path $WorkFolder "${SolutionName}_managed.zip"   # SolutionPackager pairs X.zip with X_managed.zip
$commands = @(
    "pac solution export --name $SolutionName --path `"$unmanagedZip`" --overwrite",
    "pac solution export --name $SolutionName --path `"$managedZip`" --managed --overwrite",
    "pac solution unpack --zipfile `"$unmanagedZip`" --folder `"$OutputFolder`" --packagetype Both --allowDelete --allowWrite --clobber"
)

if ($WhatIfPreference) {
    Write-Host "==> DRY RUN — would run (pac must already be authenticated to the authoring environment):" -ForegroundColor Yellow
    $commands | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkYellow }
    Write-Host "    then remove every environmentvariablevalues.json under $OutputFolder and fail if one survives." -ForegroundColor DarkYellow
    exit 0
}

New-Item -ItemType Directory -Path $WorkFolder -Force | Out-Null
foreach ($cmd in $commands) {
    Write-Host "==> $cmd" -ForegroundColor Cyan
    Invoke-Expression $cmd
    if ($LASTEXITCODE -ne 0) { throw "Failed (exit $LASTEXITCODE): $cmd" }
}

$values = @(Find-EnvironmentVariableValues -Path $OutputFolder)
foreach ($v in $values) {
    Remove-Item -LiteralPath $v -Force
    Write-Host "    removed env-var value: $v" -ForegroundColor DarkGray
}
$left = @(Find-EnvironmentVariableValues -Path $OutputFolder)
if ($left.Count -gt 0) { throw "Environment-variable values remain in the source: $($left -join ', ')" }

$version = ([xml](Get-Content (Join-Path $OutputFolder 'Other/Solution.xml') -Raw)).ImportExportXml.SolutionManifest.Version
Write-Host ''
Write-Host "==> $SolutionName $version unpacked into $OutputFolder (managed + unmanaged; $($values.Count) value file(s) removed)." -ForegroundColor Green
Write-Host '    Review the diff, list removed components in the release note, and open the PR (runbook §3 step 6).'
Remove-Item -Recurse -Force $WorkFolder -ErrorAction SilentlyContinue
