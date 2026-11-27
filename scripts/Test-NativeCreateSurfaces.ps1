#!/usr/bin/env pwsh
#Requires -Version 7.0
<#
.SYNOPSIS
    -Verify: reports, per mapping-target table, whether the native create surfaces steer users to the product's create surface -
    quick create off, the OOB Assign hidden, the platform New hidden and replaced by the product's launcher where the table is
    user-creatable (field-mapping-server-write-path-r1 task 051, FR-24). Hiding a command is a UX steer, not enforcement: the
    server write path and the repair pass own every invariant (ADR-002).
    READ-ONLY, idempotent, no secrets (the operator's own az CLI identity).

.DESCRIPTION
    The table list and each table's expected state are infrastructure/dataverse/ribbon/NativeCreateRibbons/native-create-surfaces.json
    (the 22 sprk_ tables of RecordTableRules.All the task 050 decision names, plus sprk_procedureversion). For each table it reads:

      QUICK CREATE   GET EntityDefinitions(LogicalName='<t>')?$select=IsQuickCreateEnabled. Expected false on every table.
      ASSIGN         RetrieveEntityRibbon(<t>, All): the EFFECTIVE ribbon, i.e. the platform ribbon with every solution's RibbonDiff
                     applied, so a hidden button is absent. The three OOB buttons Mscrm.Form.<t>.Assign, Mscrm.HomepageGrid.<t>.Assign
                     and Mscrm.SubGrid.<t>.Assign must all be ABSENT.
      NEW            nativeNew = redirected: Mscrm.HomepageGrid.<t>.NewRecord, Mscrm.Form.<t>.NewRecord and Mscrm.Form.<t>.SaveAndNew
                     must be ABSENT and the Spaarke button sprk.NativeCreate.<t>.Form.New PRESENT.
                     nativeNew = hidden-outright: the five platform New buttons (those three plus Mscrm.SubGrid.<t>.AddNewStandard and
                     Mscrm.SubGrid.<t>.NewRecord) must be ABSENT.
                     nativeNew = redirected also requires the table's HOME-grid replacement button (the config's gridButtonId, where it
                     has one) PRESENT: the platform home-grid New is hidden, so that button is the home grid's one New. A table with
                     gridButton = none (the report card, created only in its matter's context) has no home-grid New at all.
                     nativeNew = kept: the form and subgrid New are reported, never a failure (an owner decision, see the config's
                     ownerQuestion). With homeGridNew = hidden (budget, KPI assessment: created only in a matter's context, owner OQ-1)
                     Mscrm.HomepageGrid.<t>.NewRecord must be ABSENT.
      ALSO PRESENT   A table whose config entry has alsoPresent (the Event form's Add Memo, which creates through the BFF) must show
                     those buttons; a missing one FAILS and is named.
      RETIRED        A table whose config entry has mustBeAbsent (the KPI subgrid's "Add KPI Assessment" button, removed in task 066: the subgrid's
                     "+ New" is its one create) must NOT show those buttons; one still shown FAILS and is named.
      SUBGRID NEW RULE  A table the secure-child script serves (the config's subgridRule = true) carries the enable rule
                     sprk.SecureChild.<t>.NativeNewAllowed.EnableRule on the platform subgrid "+ New". The rule must PASS THE TABLE as a
                     StringParameter: without it the script gets table = undefined and never consults its redirect (NATIVE_NEW_REDIRECTED),
                     so the platform "+ New" is never steered. The effective ribbon does show the rule's parameters, so this IS checked here
                     (a table whose rule is missing or passes no table FAILS and is named). The repo-side check is the ArchTest
                     SecureChildNewCommandAgreementTests (every SpaarkeMaster secure-child rule passes its own table).

    DETECTION LIMITS (what a PASS does and does not prove):
      - It checks that the subgrid rule passes the table, not what the script then does with it (the Jest suite secureChildRibbonScript
        runs the script).
      - It reads the classic ribbon as the platform computes it. The model-driven app's modern command bar honours a hidden
        classic button, but a modern command (an appaction row) that names Assign is not visible here: confirm in the app (UI test
        plan in notes/task-051-notes.md).
      - It proves the Assign COMMAND is hidden, not that nobody can assign: the prvAssign privilege stays on the roles (owner Q12),
        and an API or a Power Automate flow can still assign. The server-side owner (RecordOwnershipResolver) is what makes a re-assign
        harmless; this check is the UX layer only.
      - It reads one environment at one moment; a later import can change it. Re-run after every import.
      - A read that fails (HTTP error, an empty or unreadable ribbon) is NOT a pass: that table is reported UNKNOWN and fails.

    EXIT CODE: 0 only if every table passes; 1 if any table fails or is unknown (the failing tables are named); 2 on a usage or
    authentication error.

.PARAMETER Verify
    Run the check against -EnvironmentUrl.

.PARAMETER EnvironmentUrl
    e.g. https://spaarkedev1.crm.dynamics.com. Needed with -Verify.

.PARAMETER InputJson
    Offline mode: judge a captured observation instead of reading an environment (no network, no token). Shape:
    { "tables": { "<t>": { "quickCreate": "off"|"on", "present": [ "<ribbon button id found in the effective ribbon>", ... ],
                           "newRule": "table"|"no-table"|"absent" } } }
    "present" lists which of the checked button ids exist; "newRule" is the state of the subgrid New rule (read only for a table with
    subgridRule = true). A table missing from the file is UNKNOWN. Used by the Pester tests
    (a passing fixture and failing fixtures) and to re-judge a saved capture.

.PARAMETER Config
    Defaults to the config beside the ribbon merge script.

.PARAMETER SaveObservation
    With -Verify: also write what was observed to this path, in the -InputJson shape (so a later offline re-check is possible).

.EXAMPLE
    pwsh ./scripts/Test-NativeCreateSurfaces.ps1 -Verify -EnvironmentUrl https://spaarkedev1.crm.dynamics.com

.EXAMPLE
    pwsh ./scripts/Test-NativeCreateSurfaces.ps1 -InputJson ./tests/scripts/fixtures/native-create-fail-assign.json
#>
[CmdletBinding()]
param(
    [switch] $Verify,
    [string] $EnvironmentUrl,
    [string] $InputJson,
    [string] $Config = (Join-Path $PSScriptRoot '..\infrastructure\dataverse\ribbon\NativeCreateRibbons\native-create-surfaces.json'),
    [string] $SaveObservation
)

$ErrorActionPreference = 'Stop'

function Get-CheckedIds {
    # The ribbon button ids a table's row is judged on, and the ones that must be present.
    param([Parameter(Mandatory)] $Entry)
    $t = $Entry.table
    $mustBeAbsent = @("Mscrm.Form.$t.Assign", "Mscrm.HomepageGrid.$t.Assign", "Mscrm.SubGrid.$t.Assign")
    $mustBePresent = @()
    $retired = @()
    $newIds = @("Mscrm.HomepageGrid.$t.NewRecord", "Mscrm.Form.$t.NewRecord", "Mscrm.Form.$t.SaveAndNew")
    $reported = $newIds
    if ($Entry.alsoPresent) { $mustBePresent += @($Entry.alsoPresent | ForEach-Object { [string]$_ }) }
    if ($Entry.mustBeAbsent) { $retired += @($Entry.mustBeAbsent | ForEach-Object { [string]$_ }) }
    if ($Entry.nativeNew -eq 'redirected') {
        $mustBeAbsent += $newIds
        $mustBePresent += "sprk.NativeCreate.$t.Form.New"
        if ($Entry.gridButtonId) { $mustBePresent += [string]$Entry.gridButtonId }
    }
    elseif ($Entry.nativeNew -eq 'kept' -and $Entry.homeGridNew -eq 'hidden') {
        $mustBeAbsent += "Mscrm.HomepageGrid.$t.NewRecord"
        $reported = @("Mscrm.Form.$t.NewRecord", "Mscrm.Form.$t.SaveAndNew")
    }
    elseif ($Entry.nativeNew -eq 'hidden-outright') {
        $mustBeAbsent += $newIds + @("Mscrm.SubGrid.$t.AddNewStandard", "Mscrm.SubGrid.$t.NewRecord")
    }
    [pscustomobject]@{ MustBeAbsent = $mustBeAbsent; MustBePresent = $mustBePresent; Retired = $retired; Reported = $reported; NewIds = $newIds }
}

function Get-TableVerdict {
    # Pure: an expected entry and an observation ($null = not observed) -> a verdict row. No I/O.
    param([Parameter(Mandatory)] $Entry, $Observation)
    $ids = Get-CheckedIds $Entry
    $row = [ordered]@{ Table = $Entry.table; QuickCreate = 'unknown'; Assign = 'unknown'; New = $Entry.nativeNew; Verdict = 'UNKNOWN'; Problems = @() }
    if ($null -eq $Observation) { $row.Problems = @('not observed'); return [pscustomobject]$row }

    $qc = [string]$Observation.quickCreate
    $row.QuickCreate = $qc
    $present = @($Observation.present)
    $problems = @()
    if ($qc -ne 'off') { $problems += "quick create is $(if ($qc) { $qc } else { 'unreadable' }) (expected off)" }

    $assignIds = @($ids.MustBeAbsent | Where-Object { $_ -like '*.Assign' })
    $assignShown = @($assignIds | Where-Object { $present -contains $_ })
    $row.Assign = if ($assignShown.Count -eq 0) { 'hidden' } else { 'SHOWN' }
    foreach ($id in $assignShown) { $problems += "Assign still shown: $id" }

    foreach ($id in @($ids.MustBeAbsent | Where-Object { $_ -notlike '*.Assign' })) {
        if ($present -contains $id) { $problems += "platform New still shown: $id" }
    }
    foreach ($id in $ids.Retired) {
        if ($present -contains $id) { $problems += "a retired Spaarke button is still shown: $id" }
    }
    foreach ($id in $ids.MustBePresent) {
        if ($present -notcontains $id) { $problems += "the redirect button is missing: $id" }
    }
    if ($Entry.subgridRule -eq $true) {
        $rule = [string]$Observation.newRule
        if ($rule -eq 'no-table') { $problems += "the subgrid New rule sprk.SecureChild.$($Entry.table).NativeNewAllowed.EnableRule passes no table (the script never consults its redirect)" }
        elseif ($rule -eq 'absent') { $problems += "the subgrid New rule sprk.SecureChild.$($Entry.table).NativeNewAllowed.EnableRule is missing" }
        elseif ($rule -ne 'table') { $problems += 'the subgrid New rule was not observed' }
    }
    if ($Entry.nativeNew -eq 'kept') {
        $row.New = "kept ($($Entry.ownerQuestion): " + $(if (@($ids.Reported | Where-Object { $present -contains $_ }).Count) { 'platform New shown' } else { 'platform New hidden' }) + ')'
    }
    $row.Problems = $problems
    $row.Verdict = if ($problems.Count -eq 0) { 'PASS' } else { 'FAIL' }
    return [pscustomobject]$row
}

function Write-Matrix {
    param($Rows)
    $Rows | ForEach-Object {
        [pscustomobject]@{ Table = $_.Table; QuickCreate = $_.QuickCreate; Assign = $_.Assign; New = $_.New; Verdict = $_.Verdict }
    } | Format-Table -AutoSize | Out-String -Width 220 | Write-Host
}

if (-not $Verify -and -not $InputJson) {
    [Console]::Error.WriteLine('Pass -Verify -EnvironmentUrl <url> to check an environment, or -InputJson <file> to judge a capture.')
    exit 2
}
if ($Verify -and $InputJson) { [Console]::Error.WriteLine('-Verify and -InputJson are alternatives.'); exit 2 }
if ($Verify -and [string]::IsNullOrWhiteSpace($EnvironmentUrl)) { [Console]::Error.WriteLine('-Verify needs -EnvironmentUrl.'); exit 2 }

$cfg = Get-Content -Raw -LiteralPath $Config | ConvertFrom-Json
$observations = @{}

if ($InputJson) {
    $fixture = Get-Content -Raw -LiteralPath $InputJson | ConvertFrom-Json
    foreach ($p in $fixture.tables.PSObject.Properties) { $observations[$p.Name] = $p.Value }
}
else {
    $EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
    $token = az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv 2>$null
    if (-not $token) { [Console]::Error.WriteLine("No Dataverse token for $EnvironmentUrl. Run 'az login' and retry."); exit 2 }
    $headers = @{ Authorization = "Bearer $token"; Accept = 'application/json'; 'OData-MaxVersion' = '4.0'; 'OData-Version' = '4.0' }
    $api = "$EnvironmentUrl/api/data/v9.2"
    Add-Type -AssemblyName System.IO.Compression

    function Get-RibbonIds([string] $table) {
        $r = Invoke-RestMethod -Headers $headers -Method Get -Uri "$api/RetrieveEntityRibbon(EntityName='$table',RibbonLocationFilter=Microsoft.Dynamics.CRM.RibbonLocationFilters'All')"
        $bytes = [Convert]::FromBase64String($r.CompressedEntityXml)
        $zip = [System.IO.Compression.ZipArchive]::new([System.IO.MemoryStream]::new($bytes))
        $found = New-Object System.Collections.Generic.HashSet[string]
        $newRule = 'absent'
        try {
            foreach ($entry in $zip.Entries) {
                $reader = [System.IO.StreamReader]::new($entry.Open())
                try { $text = $reader.ReadToEnd() } finally { $reader.Dispose() }
                if ($text.TrimStart().StartsWith('<')) {
                    foreach ($m in [regex]::Matches($text, 'Id="([^"]+)"')) { [void] $found.Add($m.Groups[1].Value) }
                    # The subgrid New rule's definition (not the self-closing reference on the command): does it pass the table?
                    $rule = [regex]::Match($text, '<EnableRule Id="sprk\.SecureChild\.' + [regex]::Escape($table) + '\.NativeNewAllowed\.EnableRule">(?<body>.*?)</EnableRule>', 'Singleline')
                    if ($rule.Success) {
                        $newRule = if ($rule.Groups['body'].Value -match ('<StringParameter\s+Value="' + [regex]::Escape($table) + '"')) { 'table' } else { 'no-table' }
                    }
                }
            }
        }
        finally { $zip.Dispose() }
        if ($found.Count -eq 0) { throw "the ribbon for $table came back empty" }
        return [pscustomobject]@{ Ids = $found; NewRule = $newRule }
    }

    foreach ($entry in $cfg.tables) {
        $t = $entry.table
        try {
            $meta = Invoke-RestMethod -Headers $headers -Method Get -Uri "$api/EntityDefinitions(LogicalName='$t')?`$select=IsQuickCreateEnabled"
            $qc = if ($meta.IsQuickCreateEnabled -eq $false) { 'off' } elseif ($meta.IsQuickCreateEnabled -eq $true) { 'on' } else { '' }
            $ribbon = Get-RibbonIds $t
            $checked = Get-CheckedIds $entry
            $present = @(($checked.MustBeAbsent + $checked.MustBePresent + $checked.Retired + $checked.Reported) | Select-Object -Unique | Where-Object { $ribbon.Ids.Contains($_) })
            $observations[$t] = [pscustomobject]@{ quickCreate = $qc; present = $present; newRule = $ribbon.NewRule }
        }
        catch {
            Write-Warning "$t : read failed ($($_.Exception.Message)); reported UNKNOWN."
        }
    }
    if ($SaveObservation) {
        $out = [ordered]@{ environment = $EnvironmentUrl; tables = [ordered]@{} }
        foreach ($e in $cfg.tables) { if ($observations.ContainsKey($e.table)) { $out.tables[$e.table] = $observations[$e.table] } }
        $out | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $SaveObservation -Encoding utf8
    }
}

$rows = @($cfg.tables | ForEach-Object { Get-TableVerdict -Entry $_ -Observation $observations[$_.table] })
Write-Matrix $rows
$bad = @($rows | Where-Object { $_.Verdict -ne 'PASS' })
foreach ($r in $bad) { Write-Host ("  {0} {1}: {2}" -f $r.Verdict, $r.Table, ($r.Problems -join '; ')) }
if ($bad.Count -eq 0) {
    Write-Host "VERIFY native create surfaces: PASS ($($rows.Count) tables)."
    exit 0
}
Write-Host "VERIFY native create surfaces: NOT MET - $($bad.Count) of $($rows.Count) table(s): $(($bad | ForEach-Object { $_.Table }) -join ', ')"
exit 1
