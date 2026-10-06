<#
.SYNOPSIS
    Sets the SpaarkeGridCustomizer PCF (v1.1.1+) as the customizer control of the editable Power Apps home grids
    of sprk_event and sprk_analysis, so the regarding filing columns cannot be edited inline: the four
    sprk_regarding{core} root columns (owner round 25 item 8) and the raw filing columns the forms hide (owner
    round 38). DRY RUN by default; -Verify checks the result at any time.

.DESCRIPTION
    Task 168 (unified-access-control-r2) locks the four root columns on the child FORMS (owner round 8 item 3,
    Lock-CoreAncestorStampColumnsOnForms.ps1). The sprk_event and sprk_analysis home grids are EDITABLE Power Apps
    grids (EnableEditing=yes), a second place to type a root onto a filed row (task 168 escalation trigger 5).
    Owner round 25 item 8 (2026-10-04, binding): the grid lock EXTENDS the existing src/client/pcf/SpaarkeGridCustomizer
    (cellEditorOverrides cancel editing of the four root columns; cellRendererOverrides mark them read-only), set on
    the two grids' configuration by a dry-run / -Apply / -Verify script. No new PCF and no web-resource handler
    (ADR-006). Every other column keeps inline editing. Owner round 38 widened the customizer's lock (v1.1.1) to the
    same column set the forms hide, from the ONE list config/regarding-filing-columns.json; the configuration this
    script writes is unchanged, and its prerequisite is v1.1.1 so -Verify cannot pass on a v1.1.0 customizer that
    still lets a person type the pair or an intermediate lookup inline.

    The change is to the table's grid configuration (customcontroldefaultconfig.controldescriptionxml): in EVERY
    Microsoft.PowerApps.PowerAppsOneGrid customControl (each form factor) the parameter
        <GridCustomizerControlFullName static="true" type="SingleLine.Text">sprk_Spaarke.Controls.SpaarkeGridCustomizer</GridCustomizerControlFullName>
    is added as the last child of <parameters> (the shape a live, maker-configured grid carries). It is a PURE string
    insertion (never a re-serialization), proven by a parsed comparison in which every original node is unchanged
    and the only new nodes are those parameters. controldescriptionjson is platform-derived (it also carries the
    manifest's default parameters), so it is not written; the read-back after -Apply and -Verify require it to carry
    the customizer too. A table whose XML already names the customizer but whose JSON does not (saved without a
    publish) is PUBLISHED by -Apply (PublishXml regenerates the JSON) and read back; the dry run plans it. A run never
    reports "nothing to do" while a gap remains: a gap no planned change closes stops it with exit 1 (UNRESOLVED).

    The customizer PCF must reach customers with the grid configuration: -Apply also adds the customcontrol to the
    SpaarkeMaster solution (AddSolutionComponent, componenttype 66, as Assemble-SpaarkeMasterSolution.ps1 does) when it
    is not there yet, and -Verify requires it.

    Fail closed (ADR-003). Each of these REFUSES (exit 2), names the table, and writes nothing:
      NO_CONFIG               the table has no grid configuration row (or more than one);
      MANAGED_CONFIG          the configuration is managed (change it in its owning solution);
      NO_POWERAPPS_GRID       the configuration has no Microsoft.PowerApps.PowerAppsOneGrid control;
      OTHER_CUSTOMIZER        a PowerAppsOneGrid control already names ANOTHER customizer (a grid has one customizer;
                              replacing a maker's choice is an owner decision);
      TRANSFORM_PARSE         the configuration XML is not well-formed, or the result changed anything but the added
                              parameters, or still lacks the customizer on a PowerAppsOneGrid control;
      PREREQ_MISSING          (live) the customcontrol sprk_Spaarke.Controls.SpaarkeGridCustomizer is not in the
                              environment, or its version is below 1.1.1 (v1.0.0 does not implement the grid's
                              customizer contract; v1.1.0 locks the four roots only, not the raw filing columns of
                              owner round 38: deploy the PCF first);
      CONFIG_CHANGED          (-Apply) a configuration changed between the scan and its PATCH (or its publish).

    ORDER (main-session live gate): deploy SpaarkeGridCustomizer v1.1.1 (pcf-deploy) -> this script dry run ->
    -Apply -> -Verify -> the grid checks in the task 168 note.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Default: https://spaarkedev1.crm.dynamics.com

.PARAMETER Apply
    Perform the writes. Without a mode switch the script is a READ-ONLY dry run.

.PARAMETER Verify
    Read-only. Exit 0 only when, for every table, every PowerAppsOneGrid control in BOTH controldescriptionxml and
    controldescriptionjson names sprk_Spaarke.Controls.SpaarkeGridCustomizer, the customcontrol is present at
    version 1.1.1 or later and is a SpaarkeMaster component, and no refusal case is present. Otherwise exit 1 naming
    each gap. Any read fault is a FAILED check (exit 1).

.PARAMETER RestoreFrom
    Path to a snapshot written by -Apply. Refuses (exit 2) if the snapshot's environment differs from
    -EnvironmentUrl or if a configuration changed after the apply. Otherwise PATCHes each "before" XML back and
    publishes each table. The SpaarkeMaster membership is additive and is not reverted.

.PARAMETER SelfTest
    Offline (no token, no network): runs the pure transform and refusal checks over every fixture under
    tests/fixtures/grid-customizer/ plus inline cases; exits 1 on any mismatch.

.PARAMETER SnapshotPath
    -Apply only. Default: grid-customizer-snapshot-yyyyMMddHHmmss.json in the current directory.

.PARAMETER Tables
    Tables whose home grid is configured. Default: sprk_event, sprk_analysis (the two editable child grids).
    -Apply refuses any table outside sprk_todo, sprk_event, sprk_communication and sprk_analysis.

.PARAMETER FixturePath
    -SelfTest only. Default: tests/fixtures/grid-customizer.

.EXAMPLE
    .\Set-SpaarkeGridCustomizerOnChildGrids.ps1             # dry run
    .\Set-SpaarkeGridCustomizerOnChildGrids.ps1 -SelfTest   # offline fixtures
    .\Set-SpaarkeGridCustomizerOnChildGrids.ps1 -Apply      # snapshot, PATCH, publish, solution membership, read back
    .\Set-SpaarkeGridCustomizerOnChildGrids.ps1 -Verify     # read-only (exit 0 / 1)
    .\Set-SpaarkeGridCustomizerOnChildGrids.ps1 -RestoreFrom .\grid-customizer-snapshot-20261004120000.json

.NOTES
    Project : unified-access-control-r2
    Task    : 168 (#1107) f1 — owner round 25 item 8 (the editable grids); v1 — owner round 38 (v1.1.1 prerequisite),
              verifier items 3 (pinned no-grid gap) and 4 (publish-only plan; never "nothing to do" with a gap)
    Created : 2026-10-04
    Docs    : projects/unified-access-control-r2/notes/task-168-lock-root-columns-on-forms.md

    OPERATOR-RUN ONLY: -Apply and -RestoreFrom are the main session's manual gate. Requires Azure CLI (`az login`).
    PowerShell 7+.

    Exit codes: 0 = done / nothing to do / dry run complete / VERIFY PASS / SELF-TEST PASS;
                2 = refused (nothing was written); 1 = VERIFY FAIL, SELF-TEST FAIL, or an unexpected error.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",

    [Parameter(Mandatory = $false)]
    [switch]$Apply,

    [Parameter(Mandatory = $false)]
    [switch]$Verify,

    [Parameter(Mandatory = $false)]
    [string]$RestoreFrom,

    [Parameter(Mandatory = $false)]
    [switch]$SelfTest,

    [Parameter(Mandatory = $false)]
    [string]$SnapshotPath,

    [Parameter(Mandatory = $false)]
    [string[]]$Tables = @('sprk_event', 'sprk_analysis'),

    [Parameter(Mandatory = $false)]
    [string]$FixturePath = (Join-Path $PSScriptRoot '..' 'tests' 'fixtures' 'grid-customizer')
)

$ErrorActionPreference = "Stop"

$modeCount = @($Apply.IsPresent, $Verify.IsPresent, (-not [string]::IsNullOrEmpty($RestoreFrom)), $SelfTest.IsPresent) |
    Where-Object { $_ } | Measure-Object | Select-Object -ExpandProperty Count
if ($modeCount -gt 1) {
    throw "-Apply, -Verify, -RestoreFrom and -SelfTest are separate modes; pass at most one."
}

# ============================================================================
# Constants
# ============================================================================

$ChildTables = @('sprk_todo', 'sprk_event', 'sprk_communication', 'sprk_analysis')
$GridControlName = 'Microsoft.PowerApps.PowerAppsOneGrid'
$CustomizerParam = 'GridCustomizerControlFullName'
$CustomizerName = 'sprk_Spaarke.Controls.SpaarkeGridCustomizer'
# v1.1.1 (owner round 38): the lock covers the raw filing columns too; v1.1.0 locked the four roots only.
$MinCustomizerVersion = [version]'1.1.1'
$MasterSolution = 'SpaarkeMaster'
$CustomizerElement = "<$CustomizerParam static=""true"" type=""SingleLine.Text"">$CustomizerName</$CustomizerParam>"

# ============================================================================
# Pure functions (no I/O) — exercised offline by -SelfTest
# ============================================================================

function ConvertTo-ConfigDocument([string]$Xml) {
    $doc = [System.Xml.XmlDocument]::new()
    $doc.PreserveWhitespace = $true
    $doc.XmlResolver = $null
    $doc.LoadXml($Xml)
    return $doc
}

function Test-IsGridControl([System.Xml.XmlNode]$Node) {
    return $Node.get_NodeType() -eq [System.Xml.XmlNodeType]::Element -and $Node.get_LocalName() -ceq 'customControl' -and
        $Node.GetAttribute('name') -ceq $GridControlName
}

<#
    Per PowerAppsOneGrid customControl: formFactor, EnableEditing, the customizer it names (or $null).
#>
function Get-GridXmlState([System.Xml.XmlDocument]$Doc) {
    $out = @()
    foreach ($cc in $Doc.SelectNodes('//customControl')) {
        if (-not (Test-IsGridControl $cc)) { continue }
        $p = $cc.SelectSingleNode('./parameters')
        $cust = if ($p) { $p.SelectSingleNode("./$CustomizerParam") } else { $null }
        $edit = if ($p) { $p.SelectSingleNode('./EnableEditing') } else { $null }
        $out += [pscustomobject]@{
            FormFactor = $cc.GetAttribute('formFactor')
            Editing    = if ($edit) { $edit.get_InnerText().Trim() } else { '' }
            Customizer = if ($cust) { $cust.get_InnerText().Trim() } else { $null }
        }
    }
    return $out
}

<#
    Per PowerAppsOneGrid entry of controldescriptionjson: FormFactor and the customizer it names (or $null).
    Returns $null when the JSON does not parse.
#>
function Get-GridJsonState([string]$Json) {
    try { $j = $Json | ConvertFrom-Json -Depth 50 } catch { return $null }
    $out = @()
    foreach ($c in @($j.CustomControls)) {
        if ($c.Name -cne $GridControlName) { continue }
        $param = @($c.Parameters | Where-Object { $_.Name -ceq $CustomizerParam })
        $out += [pscustomobject]@{
            FormFactor = "$($c.FormFactor)"
            Customizer = if ($param.Count -gt 0) { "$($param[0].Value.Value)".Trim() } else { $null }
        }
    }
    return , $out
}

<#
    The pure refusal checks over one configuration. Returns a list of @{ Code; Detail }.
#>
function Get-GridRefusals([string]$Xml, [bool]$IsManaged = $false) {
    try { $doc = ConvertTo-ConfigDocument $Xml }
    catch { return @(@{ Code = 'TRANSFORM_PARSE'; Detail = "the configuration XML is not well-formed: $($_.Exception.Message)" }) }
    $out = @()
    $grids = @(Get-GridXmlState $doc)
    if ($grids.Count -eq 0) {
        $out += @{ Code = 'NO_POWERAPPS_GRID'; Detail = "no $GridControlName control: the owner's mechanism is the Power Apps grid customizer" }
    }
    foreach ($g in $grids) {
        if ($null -ne $g.Customizer -and $g.Customizer -cne $CustomizerName) {
            $out += @{ Code = 'OTHER_CUSTOMIZER'; Detail = "form factor $($g.FormFactor) already names customizer '$($g.Customizer)'; a grid has one customizer (owner decision)" }
        }
    }
    $complete = $grids.Count -gt 0 -and @($grids | Where-Object { $_.Customizer -cne $CustomizerName }).Count -eq 0
    if ($IsManaged -and -not $complete) {
        $out += @{ Code = 'MANAGED_CONFIG'; Detail = 'managed grid configuration; change it in its owning solution' }
    }
    return $out
}

<#
    The transform. Adds the customizer parameter as the last child of <parameters> in every PowerAppsOneGrid
    customControl that does not name one, as text insertions; a configuration that already names it everywhere comes
    back byte-identical. Positions come from an XmlReader (line/column), never from a text search.
    Returns @{ Xml; Added = @(form factors) }. Call only on a configuration with no refusal.
#>
function Set-GridCustomizerXml([string]$Xml) {
    $lineStarts = [System.Collections.Generic.List[int]]::new(); $lineStarts.Add(0)
    for ($i = 0; $i -lt $Xml.Length; $i++) { if ($Xml[$i] -eq "`n") { $lineStarts.Add($i + 1) } }
    $settings = [System.Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $edits = [System.Collections.Generic.List[object]]::new()   # @{ At; Kind = 'close' | 'empty'; FormFactor }
    $reader = [System.Xml.XmlReader]::Create([System.IO.StringReader]::new($Xml), $settings)
    try {
        # A stack of open elements: @{ Name; Grid (a PowerAppsOneGrid customControl); FormFactor; HasCustomizer }.
        $stack = [System.Collections.Generic.Stack[object]]::new()
        while ($reader.Read()) {
            $li = [System.Xml.IXmlLineInfo]$reader
            if ($reader.NodeType -eq [System.Xml.XmlNodeType]::Element) {
                $isGrid = $reader.LocalName -ceq 'customControl' -and $reader.GetAttribute('name') -ceq $GridControlName
                $entry = @{ Name = $reader.LocalName; Grid = $isGrid; FormFactor = $reader.GetAttribute('formFactor'); HasCustomizer = $false }
                $parent = if ($stack.Count -gt 0) { $stack.Peek() } else { $null }
                if ($reader.LocalName -ceq $CustomizerParam -and $null -ne $parent -and $parent.Name -ceq 'parameters') { $parent.HasCustomizer = $true }
                if ($reader.LocalName -ceq 'parameters' -and $null -ne $parent -and $parent.Grid -and $reader.IsEmptyElement) {
                    $edits.Add(@{ At = $lineStarts[$li.LineNumber - 1] + $li.LinePosition - 2; Kind = 'empty'; FormFactor = $parent.FormFactor })
                }
                if (-not $reader.IsEmptyElement) { $stack.Push($entry) }
            }
            elseif ($reader.NodeType -eq [System.Xml.XmlNodeType]::EndElement) {
                $entry = $stack.Pop()
                $parent = if ($stack.Count -gt 0) { $stack.Peek() } else { $null }
                if ($entry.Name -ceq 'parameters' -and $null -ne $parent -and $parent.Grid -and -not $entry.HasCustomizer) {
                    # For an end tag the column is the name, two past "</".
                    $edits.Add(@{ At = $lineStarts[$li.LineNumber - 1] + $li.LinePosition - 3; Kind = 'close'; FormFactor = $parent.FormFactor })
                }
            }
        }
    }
    finally { $reader.Dispose() }
    foreach ($e in ($edits | Sort-Object { $_.At } -Descending)) {
        if ($e.Kind -eq 'close') {
            if ($Xml.Substring($e.At, 13) -cne '</parameters>') { throw "TRANSFORM_PARSE: no </parameters> at offset $($e.At)" }
            $Xml = $Xml.Insert($e.At, $CustomizerElement)
        }
        else {
            $m = [regex]::new('\G<parameters\s*/>').Match($Xml, $e.At)
            if (-not $m.Success) { throw "TRANSFORM_PARSE: no <parameters /> at offset $($e.At)" }
            $Xml = $Xml.Substring(0, $e.At) + "<parameters>$CustomizerElement</parameters>" + $Xml.Substring($e.At + $m.Length)
        }
    }
    return @{ Xml = $Xml; Added = @($edits | ForEach-Object { "formFactor $($_.FormFactor)" }) }
}

<#
    Parse check: well-formed; every original node still present, in order, with identical attributes and text; the
    ONLY new nodes are customizer parameters (exact attributes and value) appended as the last child of a
    PowerAppsOneGrid control's <parameters>; and every PowerAppsOneGrid control now names the customizer.
#>
function Test-GridTransform([string]$Before, [string]$After) {
    $problems = [System.Collections.Generic.List[string]]::new()
    try { $a = ConvertTo-ConfigDocument $Before } catch { $problems.Add("input not well-formed: $($_.Exception.Message)"); return @($problems) }
    try { $b = ConvertTo-ConfigDocument $After } catch { $problems.Add("TRANSFORM_PARSE: result not well-formed: $($_.Exception.Message)"); return @($problems) }
    $stack = [System.Collections.Generic.Stack[object]]::new()
    $stack.Push(@($a.get_DocumentElement(), $b.get_DocumentElement()))
    while ($stack.Count -gt 0) {
        $pair = $stack.Pop(); $x = $pair[0]; $y = $pair[1]
        $xName = $x.get_LocalName(); $yName = $y.get_LocalName()
        if ($x.get_NodeType() -ne $y.get_NodeType() -or $xName -cne $yName) { $problems.Add("TRANSFORM_PARSE: node $xName became $yName"); continue }
        if ($x.get_NodeType() -ne [System.Xml.XmlNodeType]::Element) {
            if ($x.get_Value() -cne $y.get_Value()) { $problems.Add("TRANSFORM_PARSE: text changed under $($x.get_ParentNode().get_LocalName())") }
            continue
        }
        $ax = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
        $ay = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
        foreach ($at in $x.get_Attributes()) { $ax[$at.get_Name()] = $at.get_Value() }
        foreach ($at in $y.get_Attributes()) { $ay[$at.get_Name()] = $at.get_Value() }
        foreach ($k in (@($ax.Keys) + @($ay.Keys) | Sort-Object -Unique -CaseSensitive)) {
            if (-not $ax.ContainsKey($k) -or -not $ay.ContainsKey($k) -or $ax[$k] -cne $ay[$k]) { $problems.Add("TRANSFORM_PARSE: attribute '$k' changed on <$xName>") }
        }
        $xc = @($x.get_ChildNodes()); $yc = @($y.get_ChildNodes())
        $extra = $yc.Count - $xc.Count
        $allowExtra = ($xName -ceq 'parameters') -and (Test-IsGridControl $x.get_ParentNode()) -and $null -eq $x.SelectSingleNode("./$CustomizerParam")
        if ($extra -eq 1 -and $allowExtra) {
            $new = $yc[$yc.Count - 1]
            $ok = $new.get_NodeType() -eq [System.Xml.XmlNodeType]::Element -and $new.get_LocalName() -ceq $CustomizerParam -and
                @($new.get_Attributes()).Count -eq 2 -and $new.GetAttribute('static') -ceq 'true' -and $new.GetAttribute('type') -ceq 'SingleLine.Text' -and
                @($new.get_ChildNodes()).Count -eq 1 -and $new.get_InnerText() -ceq $CustomizerName
            if (-not $ok) { $problems.Add("TRANSFORM_PARSE: the node added under a grid control's <parameters> is not exactly the customizer parameter") }
            # The original children must be the first $xc.Count children, in order (paired below). Assigned inside
            # the branches: an if-EXPRESSION would unroll a one-element array into a bare XmlElement.
            if ($xc.Count -eq 0) { $yc = @() } else { $yc = @($yc[0..($xc.Count - 1)]) }
        }
        elseif ($extra -ne 0) { $problems.Add("TRANSFORM_PARSE: child count changed under <$xName>"); continue }
        for ($i = 0; $i -lt $xc.Count; $i++) { $stack.Push(@($xc[$i], $yc[$i])) }
    }
    if ($problems.Count -eq 0) {
        $missing = @(Get-GridXmlState $b | Where-Object { $_.Customizer -cne $CustomizerName })
        if ($missing.Count -gt 0) { $problems.Add("TRANSFORM_PARSE: form factor(s) $(($missing | ForEach-Object { $_.FormFactor }) -join ', ') still name no SpaarkeGridCustomizer") }
    }
    return @($problems)
}

function Get-Sha256([string]$Text) {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
    return [System.Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

<#
    The verify predicate over one table's configuration (pure): returns the gaps (empty = locked).
#>
function Get-GridGaps([string]$Xml, [string]$Json) {
    $gaps = @()
    try { $doc = ConvertTo-ConfigDocument $Xml } catch { return @("controldescriptionxml is not well-formed") }
    $xs = @(Get-GridXmlState $doc)
    if ($xs.Count -eq 0) { $gaps += "no $GridControlName control in controldescriptionxml" }
    foreach ($g in $xs) { if ($g.Customizer -cne $CustomizerName) { $gaps += "controldescriptionxml form factor $($g.FormFactor) names no SpaarkeGridCustomizer" } }
    $js = Get-GridJsonState $Json
    if ($null -eq $js) { $gaps += "controldescriptionjson does not parse" }
    else {
        if (@($js).Count -eq 0) { $gaps += "no $GridControlName control in controldescriptionjson" }
        foreach ($g in @($js)) { if ($g.Customizer -cne $CustomizerName) { $gaps += "controldescriptionjson form factor $($g.FormFactor) names no SpaarkeGridCustomizer" } }
    }
    return $gaps
}

<#
    What a dry run or -Apply does with one table's configuration (pure; task 168 v1, verifier item 4):
      'edit'    the XML lacks the customizer on a PowerAppsOneGrid form factor (PATCH, then publish);
      'publish' the XML names it everywhere but a gap remains: controldescriptionjson is platform-derived and is
                regenerated only by a publish (an XML change saved without a publish, or one published before the
                customizer was imported, reads this way), so the table is published and read back;
      'none'    no gap.
    Call only on a configuration with no refusal.
#>
function Get-GridTableAction([string]$Xml, [string]$Json) {
    if (@((Set-GridCustomizerXml $Xml).Added).Count -gt 0) { return 'edit' }
    if (@(Get-GridGaps $Xml $Json).Count -gt 0) { return 'publish' }
    return 'none'
}

<#
    The run's outcome (pure; verifier item 4): 'act' when anything is planned; 'unresolved' when nothing is planned
    but a gap remains (never "nothing to do" while -Verify would fail: the run stops with exit 1); 'nothing' only
    when there is no gap.
#>
function Get-GridRunOutcome([int]$Edits, [bool]$AddToMaster, [int]$Publishes, [int]$Gaps) {
    if ($Edits -gt 0 -or $AddToMaster -or $Publishes -gt 0) { return 'act' }
    if ($Gaps -gt 0) { return 'unresolved' }
    return 'nothing'
}

# ============================================================================
# -SelfTest (offline)
# ============================================================================

if ($SelfTest) {
    Write-Host "Set-SpaarkeGridCustomizerOnChildGrids  —  SELF-TEST (offline; no token, no network)" -ForegroundColor White
    $root = (Resolve-Path -LiteralPath $FixturePath).Path
    $cases = @(Get-ChildItem -LiteralPath $root -Directory | Sort-Object Name)
    if ($cases.Count -eq 0) { Write-Host "SELF-TEST FAIL: no fixture cases under $root" -ForegroundColor Red; exit 1 }
    $failures = 0
    foreach ($case in $cases) {
        $in = [System.IO.File]::ReadAllText((Join-Path $case.FullName 'input.xml'))
        $managedPath = Join-Path $case.FullName 'managed.txt'
        $isManaged = (Test-Path -LiteralPath $managedPath) -and ((Get-Content -LiteralPath $managedPath -Raw).Trim() -ieq 'true')
        $refusals = @(Get-GridRefusals $in $isManaged)
        $ok = $true; $why = ''
        $refusalPath = Join-Path $case.FullName 'expected-refusal.txt'
        if (Test-Path -LiteralPath $refusalPath) {
            $want = (Get-Content -LiteralPath $refusalPath -Raw).Trim()
            $got = @($refusals | ForEach-Object { $_.Code })
            if ($got -notcontains $want) { $ok = $false; $why = "expected refusal $want, got [$($got -join ',')]" } else { $why = "refused $want" }
        }
        elseif ($refusals.Count -gt 0) { $ok = $false; $why = "unexpected refusal(s): $(($refusals | ForEach-Object { $_.Code }) -join ',')" }
        else {
            try {
                $result = Set-GridCustomizerXml $in
                $expected = [System.IO.File]::ReadAllText((Join-Path $case.FullName 'expected.xml'))
                $problems = if ($result.Xml -ceq $in) { @() } else { @(Test-GridTransform $in $result.Xml) }
                # verify.json stands in for the platform-derived controldescriptionjson after the apply.
                $verifyGaps = @(Get-GridGaps $result.Xml (Get-Content -LiteralPath (Join-Path $case.FullName 'verify.json') -Raw))
                if ($problems.Count -gt 0) { $ok = $false; $why = "parse check: $($problems -join '; ')" }
                elseif ($result.Xml -cne $expected) { $ok = $false; $why = 'output differs from expected.xml' }
                elseif ((Set-GridCustomizerXml $result.Xml).Xml -cne $result.Xml) { $ok = $false; $why = 'not idempotent: a second transform changed the output' }
                elseif ($verifyGaps.Count -gt 0) { $ok = $false; $why = "verify predicate: $($verifyGaps -join '; ')" }
                elseif ($result.Added.Count -eq 0) { $why = 'nothing to do (unchanged)' }
                else { $why = "customizer added to $($result.Added -join ', ')" }
            }
            catch { $ok = $false; $why = "transform threw: $($_.Exception.Message)" }
        }
        if ($ok) { Write-Host ("  PASS  {0,-40} {1}" -f $case.Name, $why) -ForegroundColor Green }
        else { $failures++; Write-Host ("  FAIL  {0,-40} {1}" -f $case.Name, $why) -ForegroundColor Red }
    }

    # The parse check and the verify predicate on their own: crafted before/after pairs.
    $base = '<controlDescriptions><controlDescription><customControl id="{E7A81278-8635-4d9e-8D4D-59480B391C5B}"><parameters /></customControl>' +
            "<customControl formFactor=""0"" name=""$GridControlName""><parameters><EnableEditing static=""true"" type=""Enum"">yes</EnableEditing></parameters></customControl>" +
            "<customControl formFactor=""2"" name=""$GridControlName""><parameters><EnableEditing static=""true"" type=""Enum"">yes</EnableEditing></parameters></customControl>" +
            '</controlDescription></controlDescriptions>'
    $good = (Set-GridCustomizerXml $base).Xml
    $pairs = @(
        @{ Name = 'parse: the real transform passes (positive control)'; After = $good; Want = $false },
        @{ Name = 'parse: an original value changed'; After = ([regex]::new('>yes</EnableEditing>')).Replace($good, '>no</EnableEditing>', 1); Want = $true },
        @{ Name = 'parse: an original attribute changed'; After = $good.Replace('formFactor="2"', 'formFactor="1"'); Want = $true },
        @{ Name = 'parse: customizer added to a non-grid control'; After = $good.Replace('<parameters /></customControl>', "<parameters>$CustomizerElement</parameters></customControl>"); Want = $true },
        @{ Name = 'parse: another element added to grid parameters'; After = $good.Replace("$CustomizerElement</parameters>", "<EnableAggregation static=""true"" type=""Enum"">yes</EnableAggregation></parameters>"); Want = $true },
        @{ Name = 'parse: the customizer names another control'; After = $good.Replace(">$CustomizerName<", '>sprk_Other.Customizer<'); Want = $true },
        @{ Name = 'parse: customizer added with other attributes'; After = $good.Replace('<GridCustomizerControlFullName static="true"', '<GridCustomizerControlFullName static="false"'); Want = $true },
        @{ Name = 'parse: one form factor left without it'; After = ([regex]::new([regex]::Escape($CustomizerElement))).Replace($good, '', 1); Want = $true },
        @{ Name = 'parse: result not well-formed'; After = $good.Replace('</controlDescriptions>', ''); Want = $true }
    )
    foreach ($t in $pairs) {
        if ($t.After -ceq $good -and $t.Name -notlike '*positive*') { $failures++; Write-Host ("  FAIL  {0,-40} {1} (the crafted mutation did not apply)" -f 'inline', $t.Name) -ForegroundColor Red; continue }
        $problems = @(Test-GridTransform $base $t.After)
        if (($problems.Count -gt 0) -eq $t.Want) { Write-Host ("  PASS  {0,-40} {1}" -f 'inline', $t.Name) -ForegroundColor Green }
        else { $failures++; Write-Host ("  FAIL  {0,-40} {1} (problems: {2})" -f 'inline', $t.Name, ($problems -join '; ')) -ForegroundColor Red }
    }
    $jsonOk = '{"CustomControls":[{"FormFactor":0,"Name":"' + $GridControlName + '","Parameters":[{"Name":"' + $CustomizerParam + '","Value":{"Usage":"Input","Static":true,"Type":"SingleLine.Text","Value":"' + $CustomizerName + '"}}]},' +
              '{"FormFactor":2,"Name":"' + $GridControlName + '","Parameters":[{"Name":"' + $CustomizerParam + '","Value":{"Usage":"Input","Static":true,"Type":"SingleLine.Text","Value":"' + $CustomizerName + '"}}]}]}'
    $lastParam = '"Parameters":[{"Name":"' + $CustomizerParam + '","Value":{"Usage":"Input","Static":true,"Type":"SingleLine.Text","Value":"' + $CustomizerName + '"}}]}]}'
    $jsonMissing = $jsonOk.Replace($lastParam, '"Parameters":[]}]}')
    $jsonOther = $jsonOk.Replace('"Value":"' + $CustomizerName + '"}}]}]}', '"Value":"x_Other"}}]}]}')
    if ($jsonMissing -ceq $jsonOk -or $jsonOther -ceq $jsonOk) { $failures++; Write-Host "  FAIL  inline verify: a crafted JSON mutation did not apply" -ForegroundColor Red }
    $verifyCases = @(
        @{ Name = 'verify: xml and json both carry it (positive)'; Xml = $good; Json = $jsonOk; Want = $false },
        @{ Name = 'verify: json not regenerated (one factor missing)'; Xml = $good; Json = $jsonMissing; Want = $true },
        @{ Name = 'verify: json names another customizer'; Xml = $good; Json = $jsonOther; Want = $true },
        @{ Name = 'verify: json does not parse'; Xml = $good; Json = '{"CustomControls":['; Want = $true },
        @{ Name = 'verify: json has no grid control'; Xml = $good; Json = '{"CustomControls":[]}'; Want = $true },
        @{ Name = 'verify: xml form factor missing it'; Xml = $base; Json = $jsonOk; Want = $true },
        # Task 168 v1, verifier item 3: the XML half's "no grid control" gap on its own. The JSON is valid and complete,
        # so this gap is the only thing that can fail the case (the -Apply read-back relies on Get-GridGaps alone).
        @{ Name = 'verify: xml has no grid control (json valid)'; Xml = '<controlDescriptions><controlDescription><customControl id="{E7A81278-8635-4d9e-8D4D-59480B391C5B}"><parameters /></customControl></controlDescription></controlDescriptions>'; Json = $jsonOk; Want = $true }
    )
    foreach ($t in $verifyCases) {
        $gaps = @(Get-GridGaps $t.Xml $t.Json)
        if (($gaps.Count -gt 0) -eq $t.Want) { Write-Host ("  PASS  {0,-40} {1}" -f 'inline', $t.Name) -ForegroundColor Green }
        else { $failures++; Write-Host ("  FAIL  {0,-40} {1} (gaps: {2})" -f 'inline', $t.Name, ($gaps -join '; ')) -ForegroundColor Red }
    }
    # Task 168 v1, verifier item 4: a dry run / -Apply never says "nothing to do" while -Verify would fail.
    $actions = @(
        @{ Name = 'action: xml lacks it -> edit'; Got = (Get-GridTableAction $base $jsonMissing); Want = 'edit' },
        @{ Name = 'action: xml has it, json does not -> publish'; Got = (Get-GridTableAction $good $jsonMissing); Want = 'publish' },
        @{ Name = 'action: json names another -> publish'; Got = (Get-GridTableAction $good $jsonOther); Want = 'publish' },
        @{ Name = 'action: xml and json have it -> none'; Got = (Get-GridTableAction $good $jsonOk); Want = 'none' },
        @{ Name = 'outcome: only a publish planned -> act'; Got = (Get-GridRunOutcome 0 $false 1 2); Want = 'act' },
        @{ Name = 'outcome: only master membership -> act'; Got = (Get-GridRunOutcome 0 $true 0 1); Want = 'act' },
        @{ Name = 'outcome: a gap, nothing planned -> unresolved'; Got = (Get-GridRunOutcome 0 $false 0 1); Want = 'unresolved' },
        @{ Name = 'outcome: no gap, nothing planned -> nothing'; Got = (Get-GridRunOutcome 0 $false 0 0); Want = 'nothing' }
    )
    foreach ($t in $actions) {
        if ($t.Got -ceq $t.Want) { Write-Host ("  PASS  {0,-40} {1}" -f 'inline', $t.Name) -ForegroundColor Green }
        else { $failures++; Write-Host ("  FAIL  {0,-40} {1} (got {2})" -f 'inline', $t.Name, $t.Got) -ForegroundColor Red }
    }
    if ($failures -gt 0) { Write-Host "`nSELF-TEST FAIL: $failures case(s)." -ForegroundColor Red; exit 1 }
    Write-Host "`nSELF-TEST PASS: $($cases.Count) fixture case(s) + $($pairs.Count + $verifyCases.Count + $actions.Count) inline check(s)." -ForegroundColor Green
    exit 0
}

# ============================================================================
# Live helpers (the same as Lock-CoreAncestorStampColumnsOnForms.ps1)
# ============================================================================

$BaseUrl = $EnvironmentUrl.TrimEnd('/')
$Token = $null

function Get-DataverseToken {
    $tokenResult = az account get-access-token --resource $BaseUrl --query "accessToken" -o tsv 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Failed to get a token from Azure CLI: $tokenResult. Run 'az login' first." }
    return "$tokenResult".Trim()
}

function Invoke-Dv {
    param([string]$Endpoint, [string]$Method = "GET", [object]$Body = $null, [switch]$AllowNotFound)
    $headers = @{
        "Authorization"    = "Bearer $Token"
        "OData-MaxVersion" = "4.0"
        "OData-Version"    = "4.0"
        "Accept"           = "application/json"
        "Content-Type"     = "application/json; charset=utf-8"
    }
    $params = @{ Uri = "$BaseUrl/api/data/v9.2/$Endpoint"; Method = $Method; Headers = $headers }
    if ($null -ne $Body) { $params.Body = ($Body | ConvertTo-Json -Depth 20) }
    try { return Invoke-RestMethod @params }
    catch {
        $status = $_.Exception.Response.StatusCode.value__
        if ($AllowNotFound -and $status -eq 404) { return $null }
        $detail = $_.Exception.Message
        if ($_.ErrorDetails.Message) {
            $err = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue
            if ($err.error.message) { $detail = $err.error.message }
        }
        throw "API error ($Method $Endpoint): $detail"
    }
}

function Write-Step([string]$Text) { Write-Host ""; Write-Host "== $Text" -ForegroundColor Cyan }
function Write-Plan([string]$Text) { Write-Host "   PLAN  $Text" -ForegroundColor Yellow }
function Write-Done([string]$Text) { Write-Host "   DONE  $Text" -ForegroundColor Green }
function Write-Info([string]$Text) { Write-Host "         $Text" }

function Stop-Refused([string]$Reason) {
    Write-Host ""
    Write-Host "REFUSED: $Reason" -ForegroundColor Red
    Write-Host "Nothing was changed. Resolve the reason, then re-run." -ForegroundColor Red
    exit 2
}

function Get-ConfigNow([string]$ConfigId) {
    return Invoke-Dv -Endpoint "customcontroldefaultconfigs($ConfigId)?`$select=controldescriptionxml,controldescriptionjson"
}

function Publish-Table([string]$Table) {
    $publish = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>"
    Invoke-Dv -Endpoint "PublishXml" -Method POST -Body @{ ParameterXml = $publish } | Out-Null
}

# ============================================================================
# -RestoreFrom
# ============================================================================

if ($RestoreFrom) {
    Write-Host "Set-SpaarkeGridCustomizerOnChildGrids  —  RESTORE from $RestoreFrom" -ForegroundColor White
    Write-Host "Environment: $BaseUrl"
    if (-not (Test-Path -LiteralPath $RestoreFrom)) { Stop-Refused "snapshot '$RestoreFrom' does not exist." }
    try { $snap = Get-Content -LiteralPath $RestoreFrom -Raw -Encoding UTF8 | ConvertFrom-Json }
    catch { Stop-Refused "snapshot '$RestoreFrom' is not valid JSON: $($_.Exception.Message)" }
    if ("$($snap.environmentUrl)".TrimEnd('/') -ine $BaseUrl) { Stop-Refused "the snapshot was taken on '$($snap.environmentUrl)', not '$BaseUrl'." }
    $configs = @($snap.configs)
    if ($configs.Count -eq 0) { Write-Done "the snapshot holds no configuration — nothing to restore."; exit 0 }
    foreach ($c in $configs) {
        if ((Get-Sha256 $c.xmlBefore) -ne $c.sha256Before -or (Get-Sha256 $c.xmlWritten) -ne $c.sha256Written) {
            Stop-Refused "the snapshot entry for $($c.table) ($($c.configid)) does not match its own hashes (corrupt or edited)."
        }
    }
    $Token = Get-DataverseToken
    Write-Step "1. Each configuration must still be exactly what the apply wrote"
    $toRestore = @()
    foreach ($c in $configs) {
        $h = Get-Sha256 ([string](Get-ConfigNow $c.configid).controldescriptionxml)
        if ($h -eq $c.sha256Before) { Write-Info "$($c.table) ($($c.configid)) still holds its 'before' XML — skipped"; continue }
        $expected = if ($c.sha256Stored) { [string]$c.sha256Stored } else { [string]$c.sha256Written }
        if ($h -ne $expected) { Stop-Refused "$($c.table) grid configuration ($($c.configid)) changed after the apply (sha256 $h, expected $expected); restoring would undo that later change." }
        $toRestore += $c
    }
    if ($toRestore.Count -eq 0) { Write-Done "no configuration in the snapshot was written — nothing to restore."; exit 0 }
    Write-Step "2. Restore"
    foreach ($c in $toRestore) {
        Invoke-Dv -Endpoint "customcontroldefaultconfigs($($c.configid))" -Method PATCH -Body @{ controldescriptionxml = [string]$c.xmlBefore } | Out-Null
        Write-Done "$($c.table) grid configuration ($($c.configid)) restored"
    }
    foreach ($t in @($toRestore | ForEach-Object { $_.table } | Sort-Object -Unique)) { Publish-Table $t; Write-Done "published $t" }
    Write-Host ""
    Write-Host "Restored $($toRestore.Count) configuration(s). The SpaarkeMaster membership of the customizer is additive and was kept. Run -Verify: it should now exit 1." -ForegroundColor Green
    exit 0
}

# ============================================================================
# Scan (read-only) — dry run, -Verify and -Apply
# ============================================================================

$Mode = if ($Apply) { "APPLY" } elseif ($Verify) { "VERIFY (read-only)" } else { "DRY RUN (read-only; pass -Apply to perform the writes)" }
Write-Host "Set-SpaarkeGridCustomizerOnChildGrids  —  $Mode" -ForegroundColor White
Write-Host "Environment: $BaseUrl"
Write-Host "Tables     : $($Tables -join ', ')"
Write-Host "Customizer : $CustomizerName (>= $MinCustomizerVersion)"

if ($Apply) {
    $outside = @($Tables | Where-Object { $ChildTables -notcontains $_.ToLowerInvariant() })
    if ($outside.Count -gt 0) { Stop-Refused "-Apply writes only the child tables' grids; not: $($outside -join ', ')." }
}

. (Join-Path $PSScriptRoot 'common/DataverseSolutionMembership.ps1')

$refusals = [System.Collections.Generic.List[object]]::new()
$gaps = [System.Collections.Generic.List[string]]::new()
$edits = [System.Collections.Generic.List[object]]::new()
$publishes = [System.Collections.Generic.List[object]]::new()   # XML complete, JSON not regenerated: publish only
$addToMaster = $null

try {
    $Token = Get-DataverseToken

    Write-Step "Prerequisites"
    $cc = Invoke-Dv -Endpoint "customcontrols?`$select=customcontrolid,name,version&`$filter=name eq '$CustomizerName'"
    if (@($cc.value).Count -eq 0) {
        $refusals.Add(@{ Code = 'PREREQ_MISSING'; Where = $BaseUrl; Detail = "customcontrol $CustomizerName is not in the environment (deploy SpaarkeGridCustomizer v$MinCustomizerVersion first)" })
    }
    else {
        $ccRow = $cc.value[0]
        $ver = $null
        if (-not [version]::TryParse([string]$ccRow.version, [ref]$ver) -or $ver -lt $MinCustomizerVersion) {
            $refusals.Add(@{ Code = 'PREREQ_MISSING'; Where = $BaseUrl; Detail = "customcontrol $CustomizerName is v$($ccRow.version); v$MinCustomizerVersion or later implements the grid's customizer contract and locks every filing column, not only the roots (deploy it first)" })
        }
        else { Write-Info "customcontrol $CustomizerName v$($ccRow.version) present" }
        $sol = Invoke-Dv -Endpoint "solutions?`$select=solutionid,uniquename,ismanaged&`$filter=uniquename eq '$MasterSolution'"
        if (@($sol.value).Count -eq 0) { throw "solution $MasterSolution was not found" }
        # Membership is decided ONE way, through the shared helper (batch-4 integration; SchemaScriptSolutionMembershipGuardTests).
        # A custom control is not a table subcomponent, so only its own row counts ('Direct').
        $dvHeaders = @{ Authorization = "Bearer $Token"; Accept = 'application/json'; 'OData-MaxVersion' = '4.0'; 'OData-Version' = '4.0' }
        $membership = Get-DvSolutionMembership -Api "$BaseUrl/api/data/v9.2" -Headers $dvHeaders -SolutionId $sol.value[0].solutionid
        if ((Test-DvInSolution -Membership $membership -ComponentId $ccRow.customcontrolid) -eq 'Direct') { Write-Info "the customizer is a $MasterSolution component" }
        else {
            $gaps.Add("customcontrol $CustomizerName is not a $MasterSolution component (customers would receive the grid configuration without its customizer)")
            $addToMaster = [string]$ccRow.customcontrolid
            Write-Plan "add customcontrol $CustomizerName to $MasterSolution (componenttype 66)"
        }
    }

    foreach ($table in $Tables) {
        Write-Step "Table $table"
        $cfg = Invoke-Dv -Endpoint "customcontroldefaultconfigs?`$select=customcontroldefaultconfigid,controldescriptionxml,controldescriptionjson,ismanaged&`$filter=primaryentitytypecode eq '$table'"
        $rows = @($cfg.value)
        if ($rows.Count -ne 1) { $refusals.Add(@{ Code = 'NO_CONFIG'; Where = $table; Detail = "$($rows.Count) grid configuration rows (expected exactly 1)" }); continue }
        $row = $rows[0]
        $xml = [string]$row.controldescriptionxml
        $where = "$table grid configuration ($($row.customcontroldefaultconfigid))"
        $r = @(Get-GridRefusals $xml ([bool]$row.ismanaged))
        foreach ($x in $r) { $refusals.Add(@{ Code = $x.Code; Where = $where; Detail = $x.Detail }) }
        if ($r.Count -gt 0) { continue }
        $json = [string]$row.controldescriptionjson
        foreach ($g in (Get-GridGaps $xml $json)) { $gaps.Add("$where : $g") }
        $states = @(Get-GridXmlState (ConvertTo-ConfigDocument $xml))
        Write-Info "Power Apps grid form factors: $(($states | ForEach-Object { "$($_.FormFactor) (EnableEditing=$($_.Editing), customizer=$(if ($_.Customizer) { $_.Customizer } else { 'none' }))" }) -join '; ')"
        $action = Get-GridTableAction $xml $json
        if ($action -eq 'none') { Write-Info "the customizer is set on every form factor (XML and JSON)"; continue }
        if ($action -eq 'publish') {
            # The XML is complete but the platform-derived JSON is not: only a publish regenerates it (verifier item 4).
            Write-Plan "publish $table : controldescriptionxml names $CustomizerName on every form factor but controldescriptionjson does not yet (the platform regenerates it on publish)"
            $publishes.Add([pscustomobject]@{ ConfigId = [string]$row.customcontroldefaultconfigid; Table = $table; Xml = $xml })
            continue
        }
        $result = Set-GridCustomizerXml $xml
        foreach ($p in (Test-GridTransform $xml $result.Xml)) { $refusals.Add(@{ Code = 'TRANSFORM_PARSE'; Where = $where; Detail = $p }) }
        Write-Plan "set $CustomizerName on $($result.Added -join ', ')"
        $edits.Add([pscustomobject]@{ ConfigId = [string]$row.customcontroldefaultconfigid; Table = $table; Before = $xml; After = $result.Xml })
    }
}
catch {
    if ($Verify) {
        Write-Host ""
        Write-Host "VERIFY FAIL: read fault — $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }
    throw
}

Write-Step "Result"
foreach ($r in $refusals) { Write-Host ("   REFUSAL {0}: {1} — {2}" -f $r.Code, $r.Where, $r.Detail) -ForegroundColor Red }

if ($Verify) {
    if ($gaps.Count -eq 0 -and $refusals.Count -eq 0) {
        Write-Host "`nVERIFY PASS: every Power Apps grid control of $($Tables -join ', ') names $CustomizerName (XML and JSON), the customizer is v$MinCustomizerVersion+ and a $MasterSolution component." -ForegroundColor Green
        exit 0
    }
    foreach ($g in $gaps) { Write-Host "   GAP $g" -ForegroundColor Red }
    Write-Host "`nVERIFY FAIL: $($gaps.Count) gap(s), $($refusals.Count) refusal case(s)." -ForegroundColor Red
    exit 1
}

if ($refusals.Count -gt 0) { Stop-Refused "$($refusals.Count) refusal case(s), listed above." }
$outcome = Get-GridRunOutcome $edits.Count ($null -ne $addToMaster) $publishes.Count $gaps.Count
if ($outcome -eq 'unresolved') {
    # Never "nothing to do" while -Verify would fail (task 168 v1, verifier item 4).
    foreach ($g in $gaps) { Write-Host "   GAP $g" -ForegroundColor Red }
    Write-Host "`nUNRESOLVED: $($gaps.Count) gap(s) remain and this script plans no change that closes them; -Verify fails. Nothing was written." -ForegroundColor Red
    exit 1
}
if ($outcome -eq 'nothing') { Write-Host "`nNothing to do: the customizer is set on every grid (XML and JSON) and ships in $MasterSolution." -ForegroundColor Green; exit 0 }
if (-not $Apply) {
    Write-Host "`nDRY RUN complete — no writes were made. $($edits.Count) grid configuration(s) would change$(if ($publishes.Count) { "; $($publishes.Count) table(s) would be published so the platform regenerates controldescriptionjson" })$(if ($addToMaster) { "; the customizer would be added to $MasterSolution" }). Re-run with -Apply." -ForegroundColor White
    exit 0
}

# ============================================================================
# Apply — every check above has passed
# ============================================================================

Write-Step "Snapshot"
if ([string]::IsNullOrEmpty($SnapshotPath)) {
    $SnapshotPath = Join-Path (Get-Location).Path ("grid-customizer-snapshot-{0}.json" -f (Get-Date -Format 'yyyyMMddHHmmss'))
}
$snapshot = [ordered]@{
    script         = 'Set-SpaarkeGridCustomizerOnChildGrids.ps1'
    task           = 'unified-access-control-r2 task 168 f1 (owner round 25 item 8)'
    environmentUrl = $BaseUrl
    createdUtc     = (Get-Date).ToUniversalTime().ToString('o')
    addedToMaster  = $addToMaster
    # Published only (XML unchanged, so nothing to restore): the platform regenerates controldescriptionjson.
    publishedOnly  = @($publishes | ForEach-Object { [ordered]@{ configid = $_.ConfigId; table = $_.Table; sha256Xml = (Get-Sha256 $_.Xml) } })
    configs        = @($edits | ForEach-Object {
            [ordered]@{
                configid = $_.ConfigId; table = $_.Table
                xmlBefore = $_.Before; xmlWritten = $_.After
                sha256Before = (Get-Sha256 $_.Before); sha256Written = (Get-Sha256 $_.After)
            }
        })
}
try {
    $snapshot | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $SnapshotPath -Encoding utf8
    $readBack = Get-Content -LiteralPath $SnapshotPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if (@($readBack.configs).Count -ne $edits.Count) { throw "it holds $(@($readBack.configs).Count) configuration(s), expected $($edits.Count)" }
    foreach ($rc in $readBack.configs) {
        if ((Get-Sha256 $rc.xmlBefore) -ne $rc.sha256Before -or (Get-Sha256 $rc.xmlWritten) -ne $rc.sha256Written) { throw "entry '$($rc.table)' does not round-trip" }
    }
}
catch { Stop-Refused "the snapshot could not be written and read back at '$SnapshotPath': $($_.Exception.Message)" }
Write-Done "snapshot written and read back: $SnapshotPath"

Write-Step "Write"
$written = 0
foreach ($p in $publishes) {
    # A publish-only table: its XML must still be what the scan read, or the publish would ship a change nobody
    # reviewed. Checked before the first write, so a refusal here leaves everything untouched.
    if ([string](Get-ConfigNow $p.ConfigId).controldescriptionxml -cne $p.Xml) {
        Stop-Refused "CONFIG_CHANGED: $($p.Table) grid configuration ($($p.ConfigId)) changed since the scan; nothing was written."
    }
}
foreach ($e in $edits) {
    if ([string](Get-ConfigNow $e.ConfigId).controldescriptionxml -cne $e.Before) {
        Stop-Refused "CONFIG_CHANGED: $($e.Table) grid configuration ($($e.ConfigId)) changed since the scan; $written configuration(s) were already written — restore them with -RestoreFrom '$SnapshotPath' if needed."
    }
    Invoke-Dv -Endpoint "customcontroldefaultconfigs($($e.ConfigId))" -Method PATCH -Body @{ controldescriptionxml = $e.After } | Out-Null
    $written++
    Write-Done "$($e.Table) grid configuration ($($e.ConfigId)) updated"
}
if ($addToMaster) {
    Invoke-Dv -Endpoint 'AddSolutionComponent' -Method POST -Body @{ ComponentId = $addToMaster; ComponentType = 66; SolutionUniqueName = $MasterSolution; AddRequiredComponents = $false } | Out-Null
    Write-Done "customcontrol $CustomizerName added to $MasterSolution"
}
foreach ($t in @(@($edits | ForEach-Object { $_.Table }) + @($publishes | ForEach-Object { $_.Table }) | Sort-Object -Unique)) { Publish-Table $t; Write-Done "published $t" }

Write-Step "Read back"
$still = @()
foreach ($c in $snapshot.configs) {
    $now = Get-ConfigNow $c.configid
    $c['xmlStored'] = [string]$now.controldescriptionxml
    $c['sha256Stored'] = Get-Sha256 ([string]$now.controldescriptionxml)
    foreach ($g in (Get-GridGaps ([string]$now.controldescriptionxml) ([string]$now.controldescriptionjson))) { $still += "$($c.table): $g" }
}
foreach ($p in $publishes) {
    $now = Get-ConfigNow $p.ConfigId
    foreach ($g in (Get-GridGaps ([string]$now.controldescriptionxml) ([string]$now.controldescriptionjson))) { $still += "$($p.Table) (published only): $g" }
}
$snapshot | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $SnapshotPath -Encoding utf8
Write-Done "snapshot updated with the stored configuration XML: $SnapshotPath"
if ($still.Count -gt 0) { throw "Read-back shows gap(s) after the apply: $($still -join '; '). Restore with -RestoreFrom '$SnapshotPath'." }

Write-Host ""
Write-Host "Updated $written grid configuration(s)$(if ($publishes.Count) { "; published $($publishes.Count) table(s) whose JSON had not been regenerated" }). Snapshot: $SnapshotPath" -ForegroundColor Green
Write-Host "Next: -Verify (must exit 0), then the grid checks in the task 168 note." -ForegroundColor Green
exit 0
