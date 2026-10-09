#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Task 154 (unified-access-control-r2), verifier pass 1 finding F2: hides "Add Existing" on every sprk_noaccessentry
    subgrid. DRY RUN by default; -Apply imports; -Verify checks.

.DESCRIPTION
    WHY. "Add Existing" on the NO ACCESS subgrids (Organization: "Ethical walls on this organization", "This organization
    denied"; Contact: "This contact denied") re-points an EXISTING entry's subject or object lookup at the host record
    directly. That write skips the entry form, so it skips the form's shape check (exactly one subject, exactly one object)
    and the post-save enforcement. An administrator could turn a working wall into a two-subject or two-object entry,
    which the server rule treats as malformed (it walls nothing), while the subgrid still lists it as a wall. "+ New"
    stays: it opens the main form with the lookup filled in.

    HOW. The platform buttons Mscrm.SubGrid.sprk_noaccessentry.AddExistingStandard (1:N) and .AddExistingAssoc (N:N) are
    hidden with HideCustomAction, the precedent of AnalysisRibbons (sprk_analysis) and EventCommands (sprk_event). The
    ribbon lives in a dedicated ribbon-only solution checked in UNPACKED at
    infrastructure/dataverse/ribbon/NoAccessEntryRibbons (the table is a shell root component, behavior 2, so the
    import touches nothing but this table's ribbon; .claude/skills/ribbon-edit).

    MODES
      (default)  Dry run. Reads the live EFFECTIVE ribbon (RetrieveEntityRibbon, read-only), reports whether the two
                 buttons are present, and refuses when the table already carries any ribbon customization that is not
                 exactly the checked-in diff (read from the stored ribboncustomizations / ribbondiffs rows, which also
                 show hide-only diffs and other prefixes; the import replaces the table's RibbonDiffXml, so it would be
                 lost - merge it into the checked-in RibbonDiff.xml first). Then packs the solution into a temp file to
                 prove it packs. Zero writes to Dataverse.
      -Apply     The same checks, then pac solution pack + pac solution import --environment <url> (not published tenant-wide), then a
                 scoped PublishXml of exactly this solution's components (scripts/lib/Publish-SolutionComponents.ps1),
                 then the -Verify check with a bounded retry (the effective ribbon lags the publish; Set-AccessRibbon.ps1
                 measured up to about 75 s on spaarkedev1).
      -Verify    Read-only. Exit 0 when neither button is in the effective ribbon, exit 1 otherwise.

    OPERATOR-RUN ONLY (the main session, after the PR merges). Order: after scripts/Deploy-NoAccessEntryForms.ps1 -Apply
    (which adds the subgrids), before the live gate. Needs pac CLI and the operator's own az CLI identity. No secrets.

.EXAMPLE
    pwsh -File scripts/Deploy-NoAccessEntryRibbon.ps1
    pwsh -File scripts/Deploy-NoAccessEntryRibbon.ps1 -Apply
    pwsh -File scripts/Deploy-NoAccessEntryRibbon.ps1 -Verify
#>

[CmdletBinding()]
param(
    [string]$EnvironmentUrl = 'https://spaarkedev1.crm.dynamics.com',
    [switch]$Apply,
    [switch]$Verify,
    [string]$SolutionFolder = (Join-Path $PSScriptRoot '..' 'infrastructure' 'dataverse' 'ribbon' 'NoAccessEntryRibbons')
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib' 'Publish-SolutionComponents.ps1')
if ($Apply -and $Verify) { throw '-Apply and -Verify are separate modes (-Apply verifies itself).' }
$BaseUrl = $EnvironmentUrl.TrimEnd('/')
$Table = 'sprk_noaccessentry'
$HiddenButtons = @("Mscrm.SubGrid.$Table.AddExistingStandard", "Mscrm.SubGrid.$Table.AddExistingAssoc")

$token = az account get-access-token --resource $BaseUrl --query accessToken -o tsv 2>$null
if (-not $token) { throw "No Dataverse token for $BaseUrl. Run 'az login' and retry." }
$headers = @{ Authorization = "Bearer $token"; Accept = 'application/json'; 'OData-Version' = '4.0' }

function Get-EffectiveRibbon {
    $uri = "$BaseUrl/api/data/v9.2/RetrieveEntityRibbon(EntityName='$Table',RibbonLocationFilter=Microsoft.Dynamics.CRM.RibbonLocationFilters'All')"
    $r = Invoke-RestMethod -Headers $headers -Uri $uri
    $bytes = [Convert]::FromBase64String($r.CompressedEntityXml)
    $zip = [System.IO.Compression.ZipArchive]::new([System.IO.MemoryStream]::new($bytes))
    $reader = [System.IO.StreamReader]::new($zip.Entries[0].Open())
    try { [xml]$reader.ReadToEnd() } finally { $reader.Dispose(); $zip.Dispose() }
}

function Get-PresentButtons([xml]$Ribbon) {
    @($HiddenButtons | Where-Object { $Ribbon.SelectSingleNode("//Button[@Id='$_']") })
}

$mode = if ($Verify) { 'VERIFY (read-only)' } elseif ($Apply) { 'APPLY' } else { 'DRY RUN (no writes)' }
Write-Host "Deploy-NoAccessEntryRibbon (task 154)  env: $BaseUrl  mode: $mode"

$ribbon = Get-EffectiveRibbon
$present = Get-PresentButtons $ribbon

if ($Verify) {
    if ($present.Count -eq 0) { Write-Host 'VERIFY PASS: Add Existing is hidden on sprk_noaccessentry subgrids.' -ForegroundColor Green; exit 0 }
    Write-Host "VERIFY FAIL: still in the effective ribbon: $($present -join ', '). Run again in a few minutes if an import just ran." -ForegroundColor Red
    exit 1
}

if ($present.Count -eq 0) { Write-Host 'Add Existing is already hidden; nothing to import.' -ForegroundColor Green; exit 0 }
Write-Host "Present now: $($present -join ', ')" -ForegroundColor Yellow

$diff = Join-Path $SolutionFolder "Entities/$Table/RibbonDiff.xml"
[xml]$diffXml = Get-Content -LiteralPath $diff -Raw
foreach ($b in $HiddenButtons) {
    if (-not $diffXml.SelectSingleNode("//HideCustomAction[@Location='$b']")) { throw "$diff does not hide $b." }
}
# The checked-in diff, as (HideActionId -> Location). It holds nothing but these HideCustomActions.
$expected = @{}
foreach ($h in $diffXml.SelectNodes('//HideCustomAction')) { $expected[$h.GetAttribute('HideActionId')] = $h.GetAttribute('Location') }

# The import REPLACES the table's RibbonDiffXml, so any customization already on the table would be lost. Read what is
# there from the stored customization rows (ribboncustomizations + ribbondiffs), not the effective ribbon: a hide-only
# diff, or one under another prefix, leaves no trace in the effective ribbon (task 154 verifier pass 2, F4).
$api = "$BaseUrl/api/data/v9.2"
$customizations = @((Invoke-RestMethod -Headers $headers -Uri "$api/ribboncustomizations?`$filter=entity eq '$Table'&`$select=ribboncustomizationid").value)
$rows = @()
$next = "$api/ribbondiffs?`$filter=entity eq '$Table'&`$select=diffid,difftype,rdx"
while ($next) {
    $page = Invoke-RestMethod -Headers $headers -Uri $next
    $rows += $page.value
    $next = $page.'@odata.nextLink'
}
$unexpected = @(foreach ($row in $rows) {
        $ok = $false
        if ($expected.ContainsKey("$($row.diffid)") -and $row.rdx) {
            try {
                [xml]$rdx = "<r>$($row.rdx)</r>"
                $node = $rdx.r.FirstChild
                $ok = $rdx.r.ChildNodes.Count -eq 1 -and $node.LocalName -eq 'HideCustomAction' -and
                $node.GetAttribute('HideActionId') -eq "$($row.diffid)" -and $node.GetAttribute('Location') -eq $expected["$($row.diffid)"]
            }
            catch { $ok = $false }
        }
        if (-not $ok) { "$($row.diffid) [$($row.difftype)]" }
    })
Write-Host "Stored ribbon customization rows for ${Table}: $($customizations.Count); diff rows: $($rows.Count)"
if ($unexpected.Count -gt 0) {
    Write-Host "REFUSED: $Table already carries ribbon customizations that are not the checked-in diff, and this import would replace them: $($unexpected -join ', '). Merge them into $diff first. Nothing was changed." -ForegroundColor Red
    exit 2
}

$zip = Join-Path ([System.IO.Path]::GetTempPath()) ("NoAccessEntryRibbons-{0}.zip" -f (Get-Date -Format 'yyyyMMddHHmmss'))
# `pac` must resolve to the Power Platform CLI executable. Under Git Bash the PATH can put a bash shim named `pac`
# first; PowerShell cannot run it and leaves $LASTEXITCODE untouched, so a pack/import that never ran looked successful
# (task 154 dev apply, 2026-10-08).
$pacExe = (Get-Command pac -CommandType Application -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -in '.cmd', '.exe', '.bat' } | Select-Object -First 1).Source
if (-not $pacExe) { throw 'pac CLI not found: need pac.cmd or pac.exe on PATH.' }
& $pacExe solution pack --zipfile $zip --folder $SolutionFolder --packagetype Unmanaged
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $zip)) { throw "pac solution pack failed ($LASTEXITCODE)." }
Write-Host "Packed: $zip"

if (-not $Apply) {
    Write-Host "DRY RUN: would import NoAccessEntryRibbons (hides $($present -join ', ')). Re-run with -Apply." -ForegroundColor Cyan
    exit 0
}

$publishContext = @{ Api = $api; Headers = ($headers + @{ 'Content-Type' = 'application/json' }) }
Invoke-ScopedSolutionImport -EnvironmentUrl $BaseUrl -ZipPath $zip -SolutionUniqueName 'NoAccessEntryRibbons' -PacExe $pacExe -Context $publishContext | Out-Null

foreach ($delay in @(0, 15, 30, 45, 60, 30)) {
    if ($delay -gt 0) { Write-Host "The effective ribbon can lag the publish; re-checking in $delay s."; Start-Sleep -Seconds $delay }
    $present = Get-PresentButtons (Get-EffectiveRibbon)
    if ($present.Count -eq 0) { Write-Host 'APPLIED and VERIFIED: Add Existing is hidden. Add the live-gate check (no Add Existing on the three subgrids).' -ForegroundColor Green; exit 0 }
}
Write-Host "APPLIED, but still present after about 3 minutes: $($present -join ', '). Run -Verify later; if it still fails, inspect the import." -ForegroundColor Red
exit 1
