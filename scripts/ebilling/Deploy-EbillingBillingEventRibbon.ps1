#!/usr/bin/env pwsh
<#
.SYNOPSIS
    ebilling-r1 task 164c: hides the platform New commands on sprk_billingevent (an invoice line is created only by invoice submission and by
    Add line, EB-D-201). DRY RUN by default (zero writes); -Apply imports; -Verify checks.

.DESCRIPTION
    WHY. A line made by hand (form New, Save and New, grid New, subgrid New / Add New) bypasses the submission and Add line owners. The platform
    commands are hidden here; the server refuses a generic create of the table on its own (164a-1). This is the UI half.

    WHAT. The checked-in source of truth is src/dataverse/solutions/SpaarkeMaster/Entities/sprk_BillingEvent/RibbonDiff.xml (HideCustomActions only:
    form NewRecord and SaveAndNew, homepage grid NewRecord, subgrid AddNewStandard and NewRecord, plus the other creating controls: homepage grid
    ImportData / ImportDataFromExcel / ImportDataFromCSV / Copy.Selected, form CopySelected, subgrid CopySelected). This script packs it into the dedicated
    ribbon-only solution BillingEventRibbons (the shell infrastructure/dataverse/ribbon/BillingEventRibbons holds the sprk_billingevent root
    component with behavior 2 = no subcomponents; the RibbonDiff.xml is copied in at pack time, so there is one copy). The import REPLACES the
    table's RibbonDiffXml, which is why the diff is the whole file, not only the hides. So the dry run REFUSES (exit 2) while dev's stored ribbon
    for the table holds anything the checked-in diff does not contain, EXCEPT the four ids in $ReplacedByQ6 below (uac-r2 Q6 (b): the import may
    replace exactly those). Any other id, including any other sprk.SecureChild.* row, still refuses. Scoped publish only: sprk_billingevent, never PublishAllXml
    (scripts/lib/Publish-SolutionComponents.ps1).

    MODES
      (default)  Dry run. Reads the live EFFECTIVE ribbon (RetrieveEntityRibbon) and the stored ribbon customization rows (read-only), checks every
                 HideCustomAction Location is a control that exists in the published ribbon (or is already hidden by this diff), refuses when the
                 table carries a stored customization the checked-in diff does not contain (the import would lose it), packs a scratch zip, runs the
                 import pre-flight (component types, pending collateral on the entity; read-only) and prints the PublishXml ParameterXml.
      -Apply     The same checks, then pac solution import --environment <url> (no publish), then the scoped PublishXml, then the -Verify check with
                 a bounded retry (the effective ribbon lags the publish by up to about 75 s).
      -Verify    Read-only. Exit 0 when none of the hidden controls is in the effective ribbon AND the sibling control Mscrm.Form.sprk_billingevent.Save
                 is (an empty or wrong payload never passes), exit 1 otherwise.

    OPERATOR-RUN ONLY for -Apply (the main session; a live write). Needs pac CLI and the operator's own az CLI identity. No secrets.

.EXAMPLE
    pwsh -File scripts/ebilling/Deploy-EbillingBillingEventRibbon.ps1
    pwsh -File scripts/ebilling/Deploy-EbillingBillingEventRibbon.ps1 -Apply
    pwsh -File scripts/ebilling/Deploy-EbillingBillingEventRibbon.ps1 -Verify
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
$Table = 'sprk_billingevent'
$SolutionName = 'BillingEventRibbons'
$ShellFolder = Join-Path $repo 'infrastructure' 'dataverse' 'ribbon' $SolutionName
$SourceDiff = Join-Path $repo 'src' 'dataverse' 'solutions' 'SpaarkeMaster' 'Entities' 'sprk_BillingEvent' 'RibbonDiff.xml'

# A control that always exists on the table's form ribbon: -Verify and the post-apply check require it, so an empty or wrong payload cannot pass.
$PositiveControl = 'Mscrm.Form.sprk_billingevent.Save'
# replaced by this import per uac-r2 Q6 (b), 2026-11-08; billingevent's SecureChild New was removed in 096b/164a
# Exact, case-sensitive match only (-ccontains): no prefix, no wildcard. Every other live id the checked-in diff does not carry still refuses.
$ReplacedByQ6 = @(
    'sprk.SecureChild.sprk_billingevent.New.Command'
    'sprk.SecureChild.sprk_billingevent.NativeNewAllowed.EnableRule'
    'sprk.SecureChild.sprk_billingevent.NewAvailable.EnableRule'
    'Mscrm.AddNewRecordFromSubGridStandard'
)
# The refusal advice: SecureChild definitions belong to unified-access-control-r2, so they are never merged into the billing-event diff.
function Get-RefusalAdvice([string[]]$Ids) {
    if (@($Ids | Where-Object { $_ -cmatch '^sprk\.SecureChild\.' -or $_ -ceq 'Mscrm.AddNewRecordFromSubGridStandard' }).Count -gt 0) {
        return 'These are SecureChild definitions owned by unified-access-control-r2 and must not be merged into the billing-event diff; ask uac-r2 (the Q6 allow-list covers only four ids).'
    }
    "Merge dev's version into $SourceDiff first (the rollback copy has it)."
}

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
Write-Host "Deploy-EbillingBillingEventRibbon  env: $BaseUrl  mode: $mode"

$ribbon = Get-EffectiveRibbon
$present = Get-StillPresent $ribbon
function Test-PositiveControl([xml]$Ribbon) { [bool]$Ribbon.SelectSingleNode("//*[@Id='$PositiveControl']") }

if ($Verify) {
    if (-not (Test-PositiveControl $ribbon)) {
        Write-Host "VERIFY FAIL: the sibling control $PositiveControl is not in the effective ribbon of $Table, so an empty or wrong payload came back; the hides cannot be judged." -ForegroundColor Red
        exit 1
    }
    if ($present.Count -eq 0) { Write-Host "VERIFY PASS: none of the $($hides.Count) hidden controls is in the effective ribbon of $Table (sibling control $PositiveControl present)." -ForegroundColor Green; exit 0 }
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

# The full stored definitions (diff rows, commands, rules): saved for rollback, and compared with the checked-in diff by CONTENT, so the import
# (which replaces them) loses nothing on dev (ribbon-edit skill: export first).
function Get-All([string]$Url) {
    $all = @(); $n = $Url
    while ($n) { $p = Invoke-RestMethod -Headers $headers -Uri $n; $all += $p.value; $n = $p.'@odata.nextLink' }
    $all
}
$storedDiffs = Get-All "$api/ribbondiffs?`$filter=entity eq '$Table'&`$select=diffid,difftype,rdx"
$storedCommands = Get-All "$api/ribboncommands?`$filter=entity eq '$Table'&`$select=command,commanddefinition"
$storedRules = Get-All "$api/ribbonrules?`$filter=entity eq '$Table'&`$select=ruleid,ruledefinition"

$rollbackDir = Join-Path ([System.IO.Path]::GetTempPath()) ("$SolutionName-rollback-{0}" -f (Get-Date -Format 'yyyyMMddHHmmss'))
New-Item -ItemType Directory -Path $rollbackDir -Force | Out-Null
$ribbon.Save((Join-Path $rollbackDir "$Table-effective-ribbon.xml"))
[pscustomobject]@{ environment = $BaseUrl; table = $Table; savedAt = (Get-Date).ToUniversalTime().ToString('o')
    ribbondiffs = $storedDiffs; ribboncommands = $storedCommands; ribbonrules = $storedRules } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $rollbackDir "$Table-stored-ribbon.json") -Encoding utf8
Write-Host "Rollback copy of dev's ribbon for ${Table}: $rollbackDir (effective ribbon XML + stored diff, command and rule rows)."

# Canonical form: attributes sorted, whitespace-only text dropped, so formatting and attribute order never count as a difference.
function ConvertTo-Canonical([System.Xml.XmlNode]$Node) {
    if ($Node.NodeType -eq [System.Xml.XmlNodeType]::Text) { return [System.Security.SecurityElement]::Escape($Node.Value.Trim()) }
    if ($Node.NodeType -ne [System.Xml.XmlNodeType]::Element) { return '' }
    $attrs = @($Node.Attributes | Where-Object { $_.Name -notlike 'xmlns*' } | Sort-Object Name | ForEach-Object { "$($_.Name)=`"$($_.Value)`"" }) -join ' '
    $kids = @($Node.ChildNodes | ForEach-Object { ConvertTo-Canonical $_ }) -join ''
    "<$($Node.LocalName) $attrs>$kids</$($Node.LocalName)>"
}
# The DEFINITION of an id, never a reference to it (a command names its rules by Id too).
function Find-SourceNode([string]$Id, [string]$Kind) {
    switch ($Kind) {
        'command' { $diffXml.SelectSingleNode("//CommandDefinitions/CommandDefinition[@Id='$Id']") }
        'rule'    { $diffXml.SelectSingleNode("//RuleDefinitions/*/*[@Id='$Id']") }
        default   { $diffXml.SelectSingleNode("//CustomActions/*[@Id='$Id' or @HideActionId='$Id'] | //LocLabels/LocLabel[@Id='$Id']") }
    }
}
# The hides come first, so a refusal below still shows what the diff would hide (the criterion: the dry run lists the hides).
# Every Location must be a control the published ribbon has now, or one this diff already hides (its HideActionId is a stored row).
$unknown = @($hides | Where-Object { -not $ribbon.SelectSingleNode("//*[@Id='$($_.Location)']") -and ($storedIds -notcontains $_.Id) -and ($ReplacedByQ6 -cnotcontains $_.Location) })
Write-Host "Hidden controls in the diff: $($hides.Count); in the effective ribbon now: $($present.Count); already hidden by a stored row: $(@($hides | Where-Object { $storedIds -contains $_.Id }).Count)"
foreach ($h in $hides) {
    $state = if ($present -contains $h.Location) { 'present now (will be hidden)' } elseif ($storedIds -contains $h.Id) { 'already hidden' } else { 'NOT FOUND' }
    Write-Host ("  {0,-70} {1}" -f $h.Location, $state)
}
# The four ids the import may replace (uac-r2 Q6 (b)), as they stand on dev.
$liveIds = @(@($storedIds) + @($storedCommands | ForEach-Object { "$($_.command)" }) + @($storedRules | ForEach-Object { "$($_.ruleid)" }))
foreach ($q in $ReplacedByQ6) {
    $q6state = if ($liveIds -ccontains $q) { 'will be replaced (uac-r2 Q6)' } else { 'not on dev (nothing to replace)' }
    Write-Host ("  {0,-70} {1}" -f $q, $q6state)
}
if (-not (Test-PositiveControl $ribbon)) { Write-Host "WARNING: the sibling control $PositiveControl is not in the effective ribbon: an empty or wrong payload came back." -ForegroundColor Yellow }
$drift = @()
foreach ($d in $storedDiffs) { $s = Find-SourceNode $d.diffid 'diff'; if ($s) { [xml]$x = $d.rdx; if ((ConvertTo-Canonical $x.DocumentElement) -ne (ConvertTo-Canonical $s)) { $drift += [pscustomobject]@{ Id = "$($d.diffid)"; Text = "ribbondiff $($d.diffid)" } } } }
foreach ($c in $storedCommands) { $s = Find-SourceNode $c.command 'command'; if (-not $s) { $drift += [pscustomobject]@{ Id = "$($c.command)"; Text = "command $($c.command) (not in the checked-in diff)" } } else { [xml]$x = $c.commanddefinition; if ((ConvertTo-Canonical $x.DocumentElement) -ne (ConvertTo-Canonical $s)) { $drift += [pscustomobject]@{ Id = "$($c.command)"; Text = "command $($c.command)" } } } }
foreach ($r in $storedRules) { $s = Find-SourceNode $r.ruleid 'rule'; if (-not $s) { $drift += [pscustomobject]@{ Id = "$($r.ruleid)"; Text = "rule $($r.ruleid) (not in the checked-in diff)" } } else { [xml]$x = $r.ruledefinition; if ((ConvertTo-Canonical $x.DocumentElement) -ne (ConvertTo-Canonical $s)) { $drift += [pscustomobject]@{ Id = "$($r.ruleid)"; Text = "rule $($r.ruleid)" } } } }
# Q6 (b): the four allow-listed ids are replaced on purpose; every other difference still refuses.
$drift = @($drift | Where-Object { $ReplacedByQ6 -cnotcontains $_.Id })
Write-Host "Stored definitions compared by content: $($storedDiffs.Count) diff rows, $($storedCommands.Count) commands, $($storedRules.Count) rules; differing from the checked-in diff (outside the Q6 allow-list): $($drift.Count)"
if ($drift.Count -gt 0) {
    Write-Host "REFUSED: dev's stored ribbon for $Table differs from the checked-in diff, and the import would replace it: $(@($drift | ForEach-Object { $_.Text }) -join '; '). $(Get-RefusalAdvice @($drift | ForEach-Object { $_.Id })) Nothing was changed." -ForegroundColor Red
    exit 2
}

if ($unknown.Count -gt 0) {
    Write-Host "REFUSED: not a control id of the published ribbon of ${Table}: $(@($unknown | ForEach-Object { $_.Location }) -join ', '). Nothing was changed." -ForegroundColor Red
    exit 2
}

# The ids the checked-in diff owns: custom actions, hides, labels, commands, rules.
$owned = @($diffXml.SelectNodes('//*[@Id or @HideActionId]') | ForEach-Object { if ($_.GetAttribute('HideActionId')) { $_.GetAttribute('HideActionId') } else { $_.GetAttribute('Id') } })
$lost = @($storedIds | Where-Object { $owned -notcontains $_ -and $ReplacedByQ6 -cnotcontains $_ })
Write-Host "Stored ribbon diff rows for ${Table}: $($rows.Count); not in the checked-in diff (outside the Q6 allow-list): $($lost.Count)"
if ($lost.Count -gt 0) {
    Write-Host "REFUSED: $Table carries stored ribbon customizations the checked-in diff does not contain, and the import would replace them: $($lost -join ', '). $(Get-RefusalAdvice $lost) Nothing was changed." -ForegroundColor Red
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
    $info = Invoke-ImportPreflight -Context $context -ZipPath $zip -SolutionUniqueName $SolutionName -RerunCommand "pwsh scripts/ebilling/Deploy-EbillingBillingEventRibbon.ps1 -Apply"
    Write-Host "Zip: unique name $($info.UniqueName); root entities: $($info.RootEntities -join ', '); fully owned entities: $(@($info.FullEntities).Count); unknown parts: $(@($info.UnknownParts).Count)"
    Write-Host "PublishXml ParameterXml that -Apply would send (scoped to this entity, never PublishAllXml):"
    Write-Host (New-PublishParameterXml -Entities $info.RootEntities)
    Write-Host "DRY RUN: would import $SolutionName and hide $($present.Count) control(s). Nothing was written. Re-run with -Apply." -ForegroundColor Cyan
    exit 0
}

Invoke-ScopedSolutionImport -EnvironmentUrl $BaseUrl -ZipPath $zip -SolutionUniqueName $SolutionName -PacExe $pacExe -Context $context | Out-Null

foreach ($delay in @(0, 15, 30, 45, 60, 30)) {
    if ($delay -gt 0) { Write-Host "The effective ribbon can lag the publish; re-checking in $delay s."; Start-Sleep -Seconds $delay }
    $after = Get-EffectiveRibbon
    $present = Get-StillPresent $after
    $sibling = Test-PositiveControl $after
    if ($present.Count -eq 0 -and $sibling) { Write-Host "APPLIED and VERIFIED: the $($hides.Count) controls are hidden (sibling control $PositiveControl present)." -ForegroundColor Green; exit 0 }
}
if (-not $sibling) { Write-Host "APPLIED, but the sibling control $PositiveControl is not in the effective ribbon after about 3 minutes (empty or wrong payload). Run -Verify later." -ForegroundColor Red; exit 1 }
Write-Host "APPLIED, but still present after about 3 minutes: $($present -join ', '). Run -Verify later; if it still fails, inspect the import." -ForegroundColor Red
exit 1
