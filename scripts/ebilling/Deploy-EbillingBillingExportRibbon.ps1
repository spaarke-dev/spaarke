#!/usr/bin/env pwsh
<#
.SYNOPSIS
    ebilling-r1: hides the platform New and Delete commands on sprk_billingexport (an AP export batch is created only by Export to AP,
    POST /api/finance/export-batches). DRY RUN by default (zero writes); -Apply imports; -Verify checks.

.DESCRIPTION
    WHY. A batch made by hand (+ New) has no invoices, no files and no claim. Core User keeps Create at Deep, because the BFF's OBO create needs it,
    so the platform commands are hidden here and the server refuses a hand-made batch on its own (ApExportDeliveryService). This is the UI half.

    WHAT. The checked-in source of truth is src/dataverse/solutions/SpaarkeMaster/Entities/sprk_billingexport/RibbonDiff.xml (the four Billing Export
    form commands plus the HideCustomActions). This script packs it into the dedicated ribbon-only solution BillingExportRibbons (the shell
    infrastructure/dataverse/ribbon/BillingExportRibbons holds the sprk_billingexport root component with behavior 2 = no subcomponents; the
    RibbonDiff.xml is copied in at pack time, so there is one copy). The import REPLACES the table's RibbonDiffXml, which is why the diff is the
    whole file, not only the hides. Scoped publish only: sprk_billingexport, never PublishAllXml (scripts/lib/Publish-SolutionComponents.ps1).

    MODES
      (default)  Dry run. Reads the live EFFECTIVE ribbon (RetrieveEntityRibbon) and the stored ribbon customization rows (read-only), checks every
                 HideCustomAction Location is a control that exists in the published ribbon (or is already hidden by this diff), refuses when the
                 table carries a stored customization the checked-in diff does not contain (the import would lose it), packs a scratch zip, runs the
                 import pre-flight (component types, pending collateral on the entity; read-only) and prints the PublishXml ParameterXml.
      -Apply     The same checks, then pac solution import --environment <url> (no publish), then the scoped PublishXml, then the -Verify check with
                 a bounded retry (the effective ribbon lags the publish by up to about 75 s).
      -Verify    Read-only. Exit 0 when none of the hidden controls is in the effective ribbon, exit 1 otherwise.

    OPERATOR-RUN ONLY for -Apply (the main session; a live write). Needs pac CLI and the operator's own az CLI identity. No secrets.

.EXAMPLE
    pwsh -File scripts/ebilling/Deploy-EbillingBillingExportRibbon.ps1
    pwsh -File scripts/ebilling/Deploy-EbillingBillingExportRibbon.ps1 -Apply
    pwsh -File scripts/ebilling/Deploy-EbillingBillingExportRibbon.ps1 -Verify
#>

[CmdletBinding()]
param(
    [string]$EnvironmentUrl = 'https://spaarkedev1.crm.dynamics.com',
    [switch]$Apply,
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$repo = Join-Path $PSScriptRoot '..' '..'
. (Join-Path $repo 'scripts' 'lib' 'Publish-SolutionComponents.ps1')
if ($Apply -and $Verify) { throw '-Apply and -Verify are separate modes (-Apply verifies itself).' }
$BaseUrl = $EnvironmentUrl.TrimEnd('/')
$Table = 'sprk_billingexport'
$SolutionName = 'BillingExportRibbons'
$ShellFolder = Join-Path $repo 'infrastructure' 'dataverse' 'ribbon' $SolutionName
$SourceDiff = Join-Path $repo 'src' 'dataverse' 'solutions' 'SpaarkeMaster' 'Entities' $Table 'RibbonDiff.xml'

$api = "$BaseUrl/api/data/v9.2"
$token = az account get-access-token --resource $BaseUrl --query accessToken -o tsv 2>$null
if (-not $token) { throw "No Dataverse token for $BaseUrl. Run 'az login' and retry." }
$headers = @{ Authorization = "Bearer $token"; Accept = 'application/json'; 'OData-Version' = '4.0' }

function Get-EffectiveRibbon {
    $r = Invoke-RestMethod -Headers $headers -Uri "$api/RetrieveEntityRibbon(EntityName='$Table',RibbonLocationFilter=Microsoft.Dynamics.CRM.RibbonLocationFilters'All')"
    $bytes = [Convert]::FromBase64String($r.CompressedEntityXml)
    $zip = [System.IO.Compression.ZipArchive]::new([System.IO.MemoryStream]::new($bytes))
    $reader = [System.IO.StreamReader]::new($zip.Entries[0].Open())
    try { [xml]$reader.ReadToEnd() } finally { $reader.Dispose(); $zip.Dispose() }
}

[xml]$diffXml = Get-Content -LiteralPath $SourceDiff -Raw
$hides = @($diffXml.SelectNodes('//CustomActions/HideCustomAction') | ForEach-Object { [pscustomobject]@{ Id = $_.GetAttribute('HideActionId'); Location = $_.GetAttribute('Location') } })
if ($hides.Count -eq 0) { throw "$SourceDiff hides nothing." }

function Get-StillPresent([xml]$Ribbon) {
    @($hides | Where-Object { $Ribbon.SelectSingleNode("//*[@Id='$($_.Location)']") } | ForEach-Object { $_.Location })
}

$mode = if ($Verify) { 'VERIFY (read-only)' } elseif ($Apply) { 'APPLY' } else { 'DRY RUN (no writes)' }
Write-Host "Deploy-EbillingBillingExportRibbon  env: $BaseUrl  mode: $mode"

$ribbon = Get-EffectiveRibbon
$present = Get-StillPresent $ribbon

if ($Verify) {
    if ($present.Count -eq 0) { Write-Host "VERIFY PASS: none of the $($hides.Count) hidden controls is in the effective ribbon of $Table." -ForegroundColor Green; exit 0 }
    Write-Host "VERIFY FAIL: still in the effective ribbon: $($present -join ', '). Run again in a few minutes if an import just ran." -ForegroundColor Red
    exit 1
}

# Stored customization rows (ribboncustomizations + ribbondiffs): what the import would REPLACE.
$rows = @()
$next = "$api/ribbondiffs?`$filter=entity eq '$Table'&`$select=diffid,difftype"
while ($next) {
    $page = Invoke-RestMethod -Headers $headers -Uri $next
    $rows += $page.value
    $next = $page.'@odata.nextLink'
}
$storedIds = @($rows | ForEach-Object { "$($_.diffid)" })

# Every Location must be a control the published ribbon has now, or one this diff already hides (its HideActionId is a stored row).
$unknown = @($hides | Where-Object { -not $ribbon.SelectSingleNode("//*[@Id='$($_.Location)']") -and ($storedIds -notcontains $_.Id) })
Write-Host "Hidden controls in the diff: $($hides.Count); in the effective ribbon now: $($present.Count); already hidden by a stored row: $(@($hides | Where-Object { $storedIds -contains $_.Id }).Count)"
foreach ($h in $hides) {
    $state = if ($present -contains $h.Location) { 'present now (will be hidden)' } elseif ($storedIds -contains $h.Id) { 'already hidden' } else { 'NOT FOUND' }
    Write-Host ("  {0,-70} {1}" -f $h.Location, $state)
}
if ($unknown.Count -gt 0) {
    Write-Host "REFUSED: not a control id of the published ribbon of ${Table}: $(@($unknown | ForEach-Object { $_.Location }) -join ', '). Nothing was changed." -ForegroundColor Red
    exit 2
}

# The ids the checked-in diff owns: custom actions, hides, labels, commands, rules.
$owned = @($diffXml.SelectNodes('//*[@Id or @HideActionId]') | ForEach-Object { if ($_.GetAttribute('HideActionId')) { $_.GetAttribute('HideActionId') } else { $_.GetAttribute('Id') } })
$lost = @($storedIds | Where-Object { $owned -notcontains $_ })
Write-Host "Stored ribbon diff rows for ${Table}: $($rows.Count); not in the checked-in diff: $($lost.Count)"
if ($lost.Count -gt 0) {
    Write-Host "REFUSED: $Table carries stored ribbon customizations the checked-in diff does not contain, and the import would replace them: $($lost -join ', '). Merge them into $SourceDiff first. Nothing was changed." -ForegroundColor Red
    exit 2
}

# Stage the shell with the one RibbonDiff.xml, pack it.
$stage = Join-Path ([System.IO.Path]::GetTempPath()) ("$SolutionName-{0}" -f (Get-Date -Format 'yyyyMMddHHmmss'))
Copy-Item -LiteralPath $ShellFolder -Destination $stage -Recurse
Copy-Item -LiteralPath $SourceDiff -Destination (Join-Path $stage 'Entities' $Table 'RibbonDiff.xml') -Force
$zip = "$stage.zip"
# `pac` must resolve to the Power Platform CLI executable, not a bash shim (task 154, 2026-10-08).
$pacExe = (Get-Command pac -CommandType Application -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -in '.cmd', '.exe', '.bat' } | Select-Object -First 1).Source
if (-not $pacExe) { throw 'pac CLI not found: need pac.cmd or pac.exe on PATH.' }
& $pacExe solution pack --zipfile $zip --folder $stage --packagetype Unmanaged
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $zip)) { throw "pac solution pack failed ($LASTEXITCODE)." }
Write-Host "Packed: $zip"

$context = Get-DataverseApiContext -EnvironmentUrl $BaseUrl
if (-not $Apply) {
    # Read-only: the component-type check and the pending-collateral check the import would run, and the publish it would send.
    $info = Invoke-ImportPreflight -Context $context -ZipPath $zip -SolutionUniqueName $SolutionName -RerunCommand "pwsh scripts/ebilling/Deploy-EbillingBillingExportRibbon.ps1 -Apply"
    Write-Host "Zip: unique name $($info.UniqueName); root entities: $($info.RootEntities -join ', '); fully owned entities: $(@($info.FullEntities).Count); unknown parts: $(@($info.UnknownParts).Count)"
    Write-Host "PublishXml ParameterXml that -Apply would send (scoped to this entity, never PublishAllXml):"
    Write-Host (New-PublishParameterXml -Entities $info.RootEntities)
    Write-Host "DRY RUN: would import $SolutionName and hide $($present.Count) control(s). Nothing was written. Re-run with -Apply." -ForegroundColor Cyan
    exit 0
}

Invoke-ScopedSolutionImport -EnvironmentUrl $BaseUrl -ZipPath $zip -SolutionUniqueName $SolutionName -PacExe $pacExe -Context $context | Out-Null

foreach ($delay in @(0, 15, 30, 45, 60, 30)) {
    if ($delay -gt 0) { Write-Host "The effective ribbon can lag the publish; re-checking in $delay s."; Start-Sleep -Seconds $delay }
    $present = Get-StillPresent (Get-EffectiveRibbon)
    if ($present.Count -eq 0) { Write-Host "APPLIED and VERIFIED: the $($hides.Count) controls are hidden." -ForegroundColor Green; exit 0 }
}
Write-Host "APPLIED, but still present after about 3 minutes: $($present -join ', '). Run -Verify later; if it still fails, inspect the import." -ForegroundColor Red
exit 1
