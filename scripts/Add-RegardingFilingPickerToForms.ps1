<#
.SYNOPSIS
    Puts the filing picker (the RegardingResolver PCF), the hidden pair and lookup cells and the presave OnLoad
    registration on the owner-named child forms, so their root columns can then be locked (owner round 19,
    items 1, 2 and 4). DRY RUN by default; -Verify checks the result at any time.

.DESCRIPTION
    Task 168 (unified-access-control-r2) locks the four sprk_regarding{core} root columns on the child forms
    (owner round 8 item 3). On five forms that lock could not be applied as delivered, and owner round 19
    (2026-10-04, binding) decided how to complete them:

      item 1  sprk_event 90d2eff7 "Event modal form", sprk_event 835b8ee8 "Event Assign Work main form" and
              sprk_communication b58ec3d8 "Message main form" show the roots as VISIBLE, EDITABLE controls and
              host no filing picker. They get the RegardingResolver, hidden cells for the pair and the lookups,
              and the presave registration; THEN Lock-CoreAncestorStampColumnsOnForms.ps1 locks the roots.
      item 2  sprk_event eaf22dcb "Event main form" hosts the RegardingResolver but does not register the presave,
              so a CREATE saves without the chosen lookup and the stamps. It gets the presave registration and
              the hidden pair and lookup cells.
      item 4  sprk_analysis d408a721 "Analysis main form" gets the picker, hidden cells and presave (then the
              lock), AFTER Add-AnalysisRegardingRecordUrlColumn.ps1 has added sprk_regardingrecordurl: the
              picker writes that column on every re-file and clear, so this script refuses the form while the
              table lacks it (PAIR_INCOMPLETE).

    Per target form the change is ADDITIVE ONLY, made as string insertions into the form XML (never a
    re-serialization), and proven by a parsed comparison in which every original node is still present, in order,
    with identical attributes and text:
      - a picker section "sprk_filing_picker" (first section of the first visible tab) holding one control bound to
        sprk_regardingrecordtype that hosts sprk_Spaarke.Controls.RegardingResolver with entity = the table
        (the configuration of the live To Do and Event main forms) — only when the form hosts no RegardingResolver;
      - a HIDDEN section "sprk_filing_hidden" with a cell for every pair column (sprk_regardingrecordid, ...name,
        ...url, ...number) and every sprk_regarding* lookup of the table that has NO control on the form — the
        presave stages values only onto attributes that are on the form (presave header, SRFR-043);
      - the form library sprk_todo_regarding_presave and the OnLoad handler
        Spaarke.SmartTodo.RegardingPreSave.onLoad (pass execution context), as on the To Do main form.
    New ids are derived from the form id (a stable hash), so a re-run produces identical bytes, and a form that is
    already complete is left byte-identical ("nothing to do").

    Fail closed (ADR-003). Each of these REFUSES (exit 2), names the form, and writes nothing:
      FORM_NOT_FOUND          a target form id is not in the environment, or belongs to another table;
      MANAGED_FORM            a target form that needs a change is managed (change it in its owning solution);
      PAIR_INCOMPLETE         the table lacks sprk_regardingrecordtype, ...id, ...name or ...url (round 19 item 4:
                              run Add-AnalysisRegardingRecordUrlColumn.ps1 -Apply first);
      PICKER_MISCONFIGURED    a RegardingResolver already on the form names another entity, or is hosted by a
                              control not bound to sprk_regardingrecordtype;
      PRESAVE_DISABLED        the presave OnLoad handler is registered but disabled (a maker's choice this script
                              does not override);
      NO_VISIBLE_TAB          no visible tab with a <sections> element to place the picker in;
      PREREQ_MISSING          (live) the web resource sprk_todo_regarding_presave or the customcontrol
                              sprk_Spaarke.Controls.RegardingResolver is not in the environment;
      TRANSFORM_PARSE         the result is not well-formed, removed or changed an original node, added anything
                              but the elements above, or is still incomplete;
      FORM_CHANGED            (-Apply) a form's XML changed between the scan and its PATCH.

    ORDER (owner round 19, live steps run by the main session, each dry run -> -Apply -> -Verify):
      1. deploy sprk_todo_regarding_presave v1.4.0;
      2. Add-AnalysisRegardingRecordUrlColumn.ps1 (sprk_analysis gains sprk_regardingrecordurl);
      3. THIS script;
      4. Lock-CoreAncestorStampColumnsOnForms.ps1 (its NO_FILING_PICKER / PICKER_WITHOUT_PRESAVE refusals clear
         once step 3 has run).

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Default: https://spaarkedev1.crm.dynamics.com

.PARAMETER Apply
    Perform the writes. Without a mode switch the script is a READ-ONLY dry run.

.PARAMETER Verify
    Read-only. Exit 0 only when every target form hosts the RegardingResolver for its table, registers the
    enabled presave OnLoad handler and its library, and carries a control for every pair column and every
    sprk_regarding* lookup of its table, and no refusal case is present. Otherwise exit 1 naming each gap. Any read
    fault is a FAILED check (exit 1).

.PARAMETER RestoreFrom
    Path to a snapshot written by -Apply. Refuses (exit 2) if the snapshot's environment differs from
    -EnvironmentUrl or if a form changed after the apply (for example by the lock script: restore the lock first,
    with its own snapshot). Otherwise puts each form's "before" XML back and publishes each table.

.PARAMETER SelfTest
    Offline (no token, no network): runs the pure transform and refusal checks over every fixture under
    tests/fixtures/form-filing-picker/ plus inline parse-check cases; exits 1 on any mismatch.

.PARAMETER SnapshotPath
    -Apply only. Default: filing-picker-snapshot-yyyyMMddHHmmss.json in the current directory.

.PARAMETER Forms
    Target form ids. Default: the five forms named by owner round 19.

.PARAMETER FixturePath
    -SelfTest only. Default: tests/fixtures/form-filing-picker.

.EXAMPLE
    .\Add-RegardingFilingPickerToForms.ps1                 # dry run
    .\Add-RegardingFilingPickerToForms.ps1 -SelfTest       # offline fixtures
    .\Add-RegardingFilingPickerToForms.ps1 -Apply          # snapshot, PATCH, publish, read back
    .\Add-RegardingFilingPickerToForms.ps1 -Verify         # read-only (exit 0 / 1)
    .\Add-RegardingFilingPickerToForms.ps1 -RestoreFrom .\filing-picker-snapshot-20261004120000.json

.NOTES
    Project : unified-access-control-r2
    Task    : 168 (#1107) r1 — owner round 19 items 1, 2 and 4
    Created : 2026-10-04
    Docs    : projects/unified-access-control-r2/notes/task-168-lock-root-columns-on-forms.md

    OPERATOR-RUN ONLY: -Apply and -RestoreFrom are the main session's manual gate. No other formxml writer
    (Lock-CoreAncestorStampColumnsOnForms.ps1, task 138's Retire-CommunicationAccessPermission.ps1,
    Deploy-TodoSubgridsToElevenParentForms.ps1) may run against the same environment at the same time.
    Requires Azure CLI (`az login`). PowerShell 7+.

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
    [string[]]$Forms = @(
        'eaf22dcb-9aff-f011-8406-7c1e525abd8b',   # sprk_event         Event main form             (round 19 item 2)
        '90d2eff7-6703-f111-8407-7ced8d1dc988',   # sprk_event         Event modal form            (round 19 item 1)
        '835b8ee8-ba1d-f111-88b3-7ced8d1dc988',   # sprk_event         Event Assign Work main form (round 19 item 1)
        'b58ec3d8-0982-f111-8076-7ced8ddc4cc6',   # sprk_communication Message main form          (round 19 item 1)
        'd408a721-77d7-f011-8406-7c1e525abd8b'    # sprk_analysis      Analysis main form          (round 19 item 4)
    ),

    [Parameter(Mandatory = $false)]
    [string]$FixturePath = (Join-Path $PSScriptRoot '..' 'tests' 'fixtures' 'form-filing-picker')
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
$RecordTypeColumn = 'sprk_regardingrecordtype'
# The ADR-024 pair the RegardingResolver writes on every pick and clear: a table missing any of these cannot host it.
$RequiredPairColumns = @('sprk_regardingrecordtype', 'sprk_regardingrecordid', 'sprk_regardingrecordname', 'sprk_regardingrecordurl')
$PairTextColumns = @('sprk_regardingrecordid', 'sprk_regardingrecordname', 'sprk_regardingrecordurl', 'sprk_regardingrecordnumber')
$PickerName = 'sprk_Spaarke.Controls.RegardingResolver'
$PickerPattern = '(?i)(^|_)Spaarke\.Controls\.RegardingResolver$'
$PresaveLibrary = 'sprk_todo_regarding_presave'
$PresaveOnLoad = 'Spaarke.SmartTodo.RegardingPreSave.onLoad'
$PickerSectionName = 'sprk_filing_picker'
$HiddenSectionName = 'sprk_filing_hidden'
$ClassIdLookup = '{270BD3DB-D9AF-4782-9025-509E298DEC0A}'
$ClassIdText = '{4273EDBD-AC1D-40D3-9FB2-095C621B552D}'
$ClassIdUrl = '{71716B6C-711E-476C-8AB8-5D11542BFB47}'
$ClassIdCustom = '{F9A8A302-114E-466A-B582-6771B2AE0D92}'

# ============================================================================
# Pure functions (no I/O) — exercised offline by -SelfTest
# ============================================================================

function New-StableGuid([string]$Seed) {
    # A deterministic id per (form, purpose): a re-run writes identical bytes, so the transform is idempotent.
    $bytes = [System.Security.Cryptography.MD5]::HashData([System.Text.Encoding]::UTF8.GetBytes("uac-r2-168|$Seed"))
    return '{' + ([guid]::new($bytes)).ToString() + '}'
}

function ConvertTo-FormDocument([string]$FormXml) {
    $doc = [System.Xml.XmlDocument]::new()
    $doc.PreserveWhitespace = $true
    $doc.XmlResolver = $null
    $doc.LoadXml($FormXml)
    return $doc
}

function ConvertTo-XmlAttributeText([string]$Text) {
    return [System.Security.SecurityElement]::Escape($Text)
}

<#
    Column specs: @{ Name; Kind = 'Lookup' | 'Text' | 'Url'; Label }. The cells this form needs: every pair text
    column and every sprk_regarding* lookup of the table except sprk_regardingrecordtype (the picker binds that).
#>
function Get-NeededColumns([object[]]$ColumnSpecs) {
    return @($ColumnSpecs | Where-Object {
            $_.Name -ine $RecordTypeColumn -and
            (($PairTextColumns -contains $_.Name.ToLowerInvariant()) -or ($_.Kind -eq 'Lookup' -and $_.Name -like 'sprk_regarding*'))
        })
}

<#
    What the form carries today. Pickers: each RegardingResolver controlDescription with its forControl, the entity
    parameter values and the datafieldname of the control it is hosted by.
#>
function Get-FilingState([System.Xml.XmlDocument]$Doc, [object[]]$ColumnSpecs) {
    $pickers = @()
    foreach ($cd in $Doc.SelectNodes('//controlDescription')) {
        $named = @($cd.SelectNodes('.//customControl') | Where-Object { $_.GetAttribute('name') -match $PickerPattern })
        if ($named.Count -eq 0) { continue }
        $for = $cd.GetAttribute('forControl')
        $hostCtl = @($Doc.SelectNodes('//control') | Where-Object { [string]::Equals($_.GetAttribute('uniqueid'), $for, [System.StringComparison]::OrdinalIgnoreCase) })
        $entities = @($named | ForEach-Object { $e = $_.SelectSingleNode('./parameters/entity'); if ($e) { $e.get_InnerText().Trim() } else { '' } } | Sort-Object -Unique)
        $pickers += [pscustomobject]@{
            ForControl = $for
            Entities   = $entities
            HostColumn = if ($hostCtl.Count -gt 0) { $hostCtl[0].GetAttribute('datafieldname') } else { $null }
        }
    }
    $handlers = @($Doc.SelectNodes('/form/events/event') |
        Where-Object { $_.GetAttribute('name') -ieq 'onload' -and [string]::IsNullOrEmpty($_.GetAttribute('attribute')) } |
        ForEach-Object { $_.SelectNodes('.//Handler') } |
        Where-Object { $_.GetAttribute('functionName') -ceq $PresaveOnLoad })
    $library = @($Doc.SelectNodes('/form/formLibraries/Library') | Where-Object { $_.GetAttribute('name') -ieq $PresaveLibrary }).Count -gt 0
    $bound = @{}
    foreach ($c in $Doc.SelectNodes('//control')) {
        $d = $c.GetAttribute('datafieldname'); if ($d) { $bound[$d.ToLowerInvariant()] = $true }
    }
    $missing = @(Get-NeededColumns $ColumnSpecs | Where-Object { -not $bound.ContainsKey($_.Name.ToLowerInvariant()) })
    return [pscustomobject]@{
        Pickers         = $pickers
        HandlerCount    = $handlers.Count
        HandlerDisabled = @($handlers | Where-Object { $_.GetAttribute('enabled') -ieq 'false' }).Count -gt 0
        HasLibrary      = $library
        Missing         = $missing
    }
}

<#
    The pure refusal checks over one target form. Returns a list of @{ Code; Detail }.
#>
function Get-PickerRefusals([string]$FormXml, [string]$Table, [object[]]$ColumnSpecs, [bool]$IsManaged = $false) {
    try { $doc = ConvertTo-FormDocument $FormXml }
    catch { return @(@{ Code = 'TRANSFORM_PARSE'; Detail = "the form XML is not well-formed: $($_.Exception.Message)" }) }
    $out = @()
    $names = @($ColumnSpecs | ForEach-Object { $_.Name.ToLowerInvariant() })
    $absent = @($RequiredPairColumns | Where-Object { $names -notcontains $_ })
    if ($absent.Count -gt 0) {
        $out += @{ Code = 'PAIR_INCOMPLETE'; Detail = "$Table lacks $($absent -join ', '); the RegardingResolver writes the whole ADR-024 pair on every pick and clear (owner round 19 item 4: run Add-AnalysisRegardingRecordUrlColumn.ps1 -Apply first)" }
    }
    $state = Get-FilingState $doc $ColumnSpecs
    foreach ($p in $state.Pickers) {
        $wrong = @($p.Entities | Where-Object { $_ -ine $Table })
        if ($wrong.Count -gt 0 -or $p.Entities.Count -eq 0) {
            $out += @{ Code = 'PICKER_MISCONFIGURED'; Detail = "the RegardingResolver on control $($p.ForControl) names entity [$($p.Entities -join ', ')], not $Table" }
        }
        if ($p.HostColumn -ine $RecordTypeColumn) {
            $out += @{ Code = 'PICKER_MISCONFIGURED'; Detail = "the RegardingResolver on control $($p.ForControl) is hosted by a control bound to '$($p.HostColumn)', not $RecordTypeColumn" }
        }
    }
    if ($state.HandlerDisabled) {
        $out += @{ Code = 'PRESAVE_DISABLED'; Detail = "$PresaveOnLoad is registered but disabled; a maker disabled it, so this script does not re-enable it (owner decision)" }
    }
    $complete = ($state.Pickers.Count -gt 0) -and ($state.HandlerCount -gt 0) -and $state.HasLibrary -and ($state.Missing.Count -eq 0)
    if ($IsManaged -and -not $complete) {
        $out += @{ Code = 'MANAGED_FORM'; Detail = 'managed form that needs the picker, cells or presave; change it in its owning solution' }
    }
    if ($state.Pickers.Count -eq 0 -or $state.Missing.Count -gt 0) {
        $tab = @($doc.SelectNodes('/form/tabs/tab') | Where-Object { $_.GetAttribute('visible') -ine 'false' -and $_.SelectSingleNode('./columns/column/sections') })
        if ($tab.Count -eq 0) { $out += @{ Code = 'NO_VISIBLE_TAB'; Detail = 'no visible tab with a <sections> element to place the picker in' } }
    }
    return $out
}

function Get-CellXml([object]$Spec, [string]$ControlId, [string]$CellId) {
    $class = switch ($Spec.Kind) { 'Lookup' { $ClassIdLookup } 'Url' { $ClassIdUrl } default { $ClassIdText } }
    $label = ConvertTo-XmlAttributeText $Spec.Label
    return "<row><cell id=""$CellId"" locklevel=""0"" colspan=""1"" rowspan=""1""><labels><label description=""$label"" languagecode=""1033"" /></labels>" +
        "<control id=""$ControlId"" classid=""$class"" datafieldname=""$($Spec.Name)"" disabled=""false"" /></cell></row>"
}

function Get-PickerControlDescriptionXml([string]$Uid, [string]$Table, [object[]]$ColumnSpecs) {
    $names = @($ColumnSpecs | ForEach-Object { $_.Name.ToLowerInvariant() })
    $params = "<regardingRecordType type=""Lookup.Simple"">$RecordTypeColumn</regardingRecordType>" +
        "<entity type=""SingleLine.Text"" static=""true"">$Table</entity>"
    if ($names -contains 'sprk_regardingrecordnumber') { $params += '<regardingRecordNumberField type="SingleLine.Text">sprk_regardingrecordnumber</regardingRecordNumberField>' }
    $params += '<regardingRecordNameField type="SingleLine.Text">sprk_regardingrecordname</regardingRecordNameField>' +
        '<title type="SingleLine.Text" static="true">RELATED RECORD</title>' +
        '<showVersionFooter type="TwoOptions" static="true">true</showVersionFooter>'
    $default = "<customControl id=""$ClassIdLookup""><parameters><datafieldname>$RecordTypeColumn</datafieldname></parameters></customControl>"
    $xml = "<controlDescription forControl=""$Uid"">$default$default"
    foreach ($ff in @('0', '2', '1')) { $xml += "<customControl name=""$PickerName"" formFactor=""$ff""><parameters>$params</parameters></customControl>" }
    return $xml + '</controlDescription>'
}

function Get-FreeControlId([System.Xml.XmlDocument]$Doc, [string]$Base, [System.Collections.Generic.HashSet[string]]$Taken) {
    foreach ($c in $Doc.SelectNodes('//control')) { [void]$Taken.Add($c.GetAttribute('id').ToLowerInvariant()) }
    if (-not $Taken.Contains($Base.ToLowerInvariant())) { [void]$Taken.Add($Base.ToLowerInvariant()); return $Base }
    for ($i = 1; ; $i++) {
        $id = "$Base$i"
        if (-not $Taken.Contains($id.ToLowerInvariant())) { [void]$Taken.Add($id.ToLowerInvariant()); return $id }
    }
}

<#
    Where a TOP-LEVEL element (a direct child of <form>) sits in the text: @{ Start = offset of its "<";
    IsEmpty = self-closing; CloseStart = offset of its "</Name>" (or -1) }, or $null when the form has none.
    Positions come from an XmlReader (line/column), never from a text search: a cell may carry its own nested
    <events> (the live Analysis main form's web-resource cell does), and a text search would find that one.
#>
function Get-TopLevelElementSpan([string]$Xml, [string]$Name) {
    $lineStarts = [System.Collections.Generic.List[int]]::new(); $lineStarts.Add(0)
    for ($i = 0; $i -lt $Xml.Length; $i++) { if ($Xml[$i] -eq "`n") { $lineStarts.Add($i + 1) } }
    $settings = [System.Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [System.Xml.XmlReader]::Create([System.IO.StringReader]::new($Xml), $settings)
    try {
        $span = $null
        while ($reader.Read()) {
            if ($reader.Depth -ne 1 -or $reader.LocalName -cne $Name) { continue }
            $li = [System.Xml.IXmlLineInfo]$reader
            if ($reader.NodeType -eq [System.Xml.XmlNodeType]::Element -and $null -eq $span) {
                # LinePosition is the 1-based column of the name, one past "<".
                $span = @{ Start = $lineStarts[$li.LineNumber - 1] + $li.LinePosition - 2; IsEmpty = $reader.IsEmptyElement; CloseStart = -1 }
                if ($span.IsEmpty) { return $span }
            }
            elseif ($reader.NodeType -eq [System.Xml.XmlNodeType]::EndElement -and $null -ne $span) {
                # For an end tag the column is the name, two past "</".
                $span.CloseStart = $lineStarts[$li.LineNumber - 1] + $li.LinePosition - 3
                return $span
            }
        }
        return $span
    }
    finally { $reader.Dispose() }
}

<#
    Insert $Content into a TOP-LEVEL container element of the form: before its </Name>, or by opening a
    self-closing <Name … />, or (when absent) as a new <Name>…</Name> before the first top-level element named in
    $Before, else before </form>.
#>
function Add-IntoContainer([string]$Xml, [string]$Name, [string]$Content, [string[]]$Before) {
    $span = Get-TopLevelElementSpan $Xml $Name
    if ($null -ne $span -and -not $span.IsEmpty) { return $Xml.Insert($span.CloseStart, $Content) }
    if ($null -ne $span) {
        $tagEnd = $Xml.IndexOf('/>', $span.Start, [System.StringComparison]::Ordinal) + 2
        $opened = [regex]::Replace($Xml.Substring($span.Start, $tagEnd - $span.Start), '\s*/>$', '>')
        return $Xml.Substring(0, $span.Start) + $opened + $Content + "</$Name>" + $Xml.Substring($tagEnd)
    }
    $at = -1
    foreach ($b in $Before) {
        $other = Get-TopLevelElementSpan $Xml $b
        if ($null -ne $other) { $at = $other.Start; break }
    }
    if ($at -lt 0) { $at = $Xml.LastIndexOf('</form>', [System.StringComparison]::Ordinal) }
    return $Xml.Insert($at, "<$Name>$Content</$Name>")
}

<#
    The transform. Returns @{ Xml; Added = @(what was added) }. A complete form comes back byte-identical.
    Call only on a form with no refusal (Get-PickerRefusals).
#>
function Add-FilingPicker([string]$FormXml, [string]$Table, [string]$FormId, [object[]]$ColumnSpecs) {
    $doc = ConvertTo-FormDocument $FormXml
    $state = Get-FilingState $doc $ColumnSpecs
    $added = [System.Collections.Generic.List[string]]::new()
    $xml = $FormXml
    $taken = [System.Collections.Generic.HashSet[string]]::new()
    $fid = $FormId.Trim('{', '}').ToLowerInvariant()

    # 1. Sections: the picker (only when none is hosted) and the hidden cells, first in the first visible tab.
    $sections = ''
    if ($state.Pickers.Count -eq 0) {
        $uid = New-StableGuid "$fid|picker-control"
        $controlId = Get-FreeControlId $doc $RecordTypeColumn $taken
        $sections += "<section name=""$PickerSectionName"" id=""$((New-StableGuid "$fid|picker-section").Trim('{', '}'))"" IsUserDefined=""0"" locklevel=""0"" showlabel=""false"" showbar=""false"" layout=""varwidth"" celllabelalignment=""Left"" celllabelposition=""Top"" columns=""1"" labelwidth=""115"">" +
            '<labels><label description="RELATED RECORD" languagecode="1033" /></labels><rows><row>' +
            "<cell id=""$(New-StableGuid "$fid|picker-cell")"" locklevel=""0"" colspan=""1"" rowspan=""1"" showlabel=""false""><labels><label description=""RELATED RECORD"" languagecode=""1033"" /></labels>" +
            "<control id=""$controlId"" classid=""$ClassIdCustom"" datafieldname=""$RecordTypeColumn"" uniqueid=""$uid"" /></cell></row></rows></section>"
        $added.Add("picker control '$controlId' (RegardingResolver, entity=$Table)")
    }
    if ($state.Missing.Count -gt 0) {
        $rows = ''
        foreach ($spec in $state.Missing) {
            $cid = Get-FreeControlId $doc $spec.Name $taken
            $rows += Get-CellXml $spec $cid (New-StableGuid "$fid|cell|$($spec.Name.ToLowerInvariant())")
            $added.Add("hidden cell $($spec.Name)")
        }
        $sections += "<section name=""$HiddenSectionName"" id=""$((New-StableGuid "$fid|hidden-section").Trim('{', '}'))"" IsUserDefined=""0"" locklevel=""0"" showlabel=""false"" showbar=""false"" layout=""varwidth"" celllabelalignment=""Left"" celllabelposition=""Top"" columns=""1"" labelwidth=""115"" visible=""false"">" +
            "<labels><label description=""FILING (HIDDEN)"" languagecode=""1033"" /></labels><rows>$rows</rows></section>"
    }
    if ($sections) {
        $tabs = @($doc.SelectNodes('/form/tabs/tab'))
        $k = -1
        for ($i = 0; $i -lt $tabs.Count; $i++) {
            if ($tabs[$i].GetAttribute('visible') -ine 'false' -and $tabs[$i].SelectSingleNode('./columns/column/sections')) { $k = $i; break }
        }
        if ($k -lt 0) { throw "NO_VISIBLE_TAB" }
        $tabsStart = [regex]::Match($xml, '<tabs\b').Index
        $tabMatch = [regex]::Matches($xml.Substring($tabsStart), '<tab\b')[$k]
        $tabAt = $tabsStart + $tabMatch.Index
        $open = [regex]::Match($xml.Substring($tabAt), '<sections\s*>')
        if (-not $open.Success) { throw "NO_VISIBLE_TAB" }
        $xml = $xml.Insert($tabAt + $open.Index + $open.Length, $sections)
    }

    # 2. The picker's control description.
    if ($state.Pickers.Count -eq 0) {
        $xml = Add-IntoContainer $xml 'controlDescriptions' (Get-PickerControlDescriptionXml (New-StableGuid "$fid|picker-control") $Table $ColumnSpecs) @('events', 'formLibraries')
    }

    # 3. The presave OnLoad handler.
    if ($state.HandlerCount -eq 0) {
        $handler = "<Handler functionName=""$PresaveOnLoad"" libraryName=""$PresaveLibrary"" handlerUniqueId=""$(New-StableGuid "$fid|presave-handler")"" enabled=""true"" parameters="""" passExecutionContext=""true"" />"
        $events = Get-TopLevelElementSpan $xml 'events'
        $onload = $null
        if ($null -ne $events -and -not $events.IsEmpty) {
            # Only the form-level <events>: a cell's own nested <events> is never touched.
            $region = $xml.Substring($events.Start, $events.CloseStart - $events.Start)
            foreach ($m in [regex]::Matches($region, '<event\b(?:[^>"'']|"[^"]*"|''[^'']*'')*>')) {
                $name = [regex]::Match($m.Value, '\bname\s*=\s*"([^"]*)"').Groups[1].Value
                if ($name -ieq 'onload' -and $m.Value -notmatch '\battribute\s*=') { $onload = @{ Index = $events.Start + $m.Index; Length = $m.Length; Value = $m.Value }; break }
            }
        }
        if ($onload -and $onload.Value.EndsWith('/>')) {
            # A self-closing <event name="onload" … />: open it, keeping every attribute byte-identical.
            $opened = [regex]::Replace($onload.Value, '\s*/>$', '>') + "<Handlers>$handler</Handlers></event>"
            $xml = $xml.Substring(0, $onload.Index) + $opened + $xml.Substring($onload.Index + $onload.Length)
        }
        elseif ($onload) {
            $end = $xml.IndexOf('</event>', $onload.Index, [System.StringComparison]::Ordinal)
            $segment = $xml.Substring($onload.Index, $end - $onload.Index)
            $hClose = $segment.LastIndexOf('</Handlers>', [System.StringComparison]::Ordinal)
            if ($hClose -ge 0) { $xml = $xml.Insert($onload.Index + $hClose, $handler) }
            else {
                $hSelf = [regex]::Match($segment, '<Handlers\s*/>')
                if ($hSelf.Success) {
                    $xml = $xml.Substring(0, $onload.Index + $hSelf.Index) + "<Handlers>$handler</Handlers>" + $xml.Substring($onload.Index + $hSelf.Index + $hSelf.Length)
                }
                else { $xml = $xml.Insert($end, "<Handlers>$handler</Handlers>") }
            }
        }
        else {
            $xml = Add-IntoContainer $xml 'events' "<event name=""onload"" application=""false"" active=""false""><Handlers>$handler</Handlers></event>" @('formLibraries')
        }
        $added.Add("OnLoad handler $PresaveOnLoad")
    }

    # 4. The presave library.
    if (-not $state.HasLibrary) {
        $xml = Add-IntoContainer $xml 'formLibraries' "<Library name=""$PresaveLibrary"" libraryUniqueId=""$(New-StableGuid "$fid|presave-library")"" />" @()
        $added.Add("form library $PresaveLibrary")
    }
    return @{ Xml = $xml; Added = @($added) }
}

<#
    Parse check (additive only). The result is well-formed; every original node is still present, in order, with
    identical attributes and text; every new node is one this script adds (the two sections, the picker's
    controlDescription, the presave Handler / onload event / Library, or a container holding only those); and the
    form is now complete. Returns a list of problems (empty = OK).
#>
<#
    Same node type and name; for an element, the same attributes (names and values, case-sensitive); for text, the
    same value. An original node is paired with a result node only when they match this way, so a changed original
    shows up as "missing" plus "unexpected", never as a silent pairing.
#>
function Test-ShallowMatch([System.Xml.XmlNode]$X, [System.Xml.XmlNode]$Y) {
    if ($X.get_NodeType() -ne $Y.get_NodeType() -or $X.get_LocalName() -cne $Y.get_LocalName()) { return $false }
    if ($X.get_NodeType() -ne [System.Xml.XmlNodeType]::Element) { return ($X.get_Value() -ceq $Y.get_Value()) }
    $xa = @($X.get_Attributes()); $ya = @($Y.get_Attributes())
    if ($xa.Count -ne $ya.Count) { return $false }
    foreach ($at in $xa) {
        $other = $Y.GetAttributeNode($at.get_Name())
        if ($null -eq $other -or $other.get_Value() -cne $at.get_Value()) { return $false }
    }
    return $true
}

function Test-PickerTransform([string]$Before, [string]$After, [string]$Table, [string]$FormId, [object[]]$ColumnSpecs) {
    $problems = [System.Collections.Generic.List[string]]::new()
    try { $a = ConvertTo-FormDocument $Before } catch { $problems.Add("input not well-formed: $($_.Exception.Message)"); return @($problems) }
    try { $b = ConvertTo-FormDocument $After } catch { $problems.Add("TRANSFORM_PARSE: result not well-formed: $($_.Exception.Message)"); return @($problems) }
    $pickerUid = New-StableGuid "$($FormId.Trim('{', '}').ToLowerInvariant())|picker-control"

    $isAllowed = $null
    $isAllowed = {
        param($n)
        if ($n.get_NodeType() -ne [System.Xml.XmlNodeType]::Element) { return $false }
        switch -CaseSensitive ($n.get_LocalName()) {
            'section' { return ($n.GetAttribute('name') -ceq $PickerSectionName -or $n.GetAttribute('name') -ceq $HiddenSectionName) }
            'controlDescription' { return ($n.GetAttribute('forControl') -ieq $pickerUid) }
            'Handler' { return ($n.GetAttribute('functionName') -ceq $PresaveOnLoad -and $n.GetAttribute('libraryName') -ceq $PresaveLibrary) }
            'Library' { return ($n.GetAttribute('name') -ceq $PresaveLibrary) }
            { $_ -in @('controlDescriptions', 'events', 'formLibraries', 'Handlers', 'event') } {
                if ($n.get_LocalName() -ceq 'event' -and $n.GetAttribute('name') -ine 'onload') { return $false }
                $kids = @($n.get_ChildNodes())
                if ($kids.Count -eq 0) { return $false }
                foreach ($c in $kids) { if (-not (& $isAllowed $c)) { return $false } }
                return $true
            }
            default { return $false }
        }
    }

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
            if (-not $ax.ContainsKey($k) -or -not $ay.ContainsKey($k) -or $ax[$k] -cne $ay[$k]) {
                $problems.Add("TRANSFORM_PARSE: attribute '$k' changed on <$xName>")
            }
        }
        # Children: the original children in order, with only allowed additions between them.
        $xc = @($x.get_ChildNodes()); $yc = @($y.get_ChildNodes())
        $i = 0
        foreach ($c in $yc) {
            if ($i -lt $xc.Count -and (Test-ShallowMatch $xc[$i] $c)) {
                $stack.Push(@($xc[$i], $c)); $i++
            }
            elseif (& $isAllowed $c) { continue }
            else { $problems.Add("TRANSFORM_PARSE: unexpected <$($c.get_LocalName())> added under <$xName>") }
        }
        if ($i -lt $xc.Count) { $problems.Add("TRANSFORM_PARSE: $($xc.Count - $i) original node(s) missing under <$xName>") }
    }
    if ($problems.Count -eq 0) {
        $state = Get-FilingState $b $ColumnSpecs
        $ok = @($state.Pickers | Where-Object { $_.HostColumn -ieq $RecordTypeColumn -and @($_.Entities | Where-Object { $_ -ine $Table }).Count -eq 0 -and $_.Entities.Count -gt 0 })
        if ($ok.Count -eq 0) { $problems.Add("TRANSFORM_PARSE: no RegardingResolver for $Table after the transform") }
        if ($state.HandlerCount -eq 0) { $problems.Add("TRANSFORM_PARSE: no $PresaveOnLoad handler after the transform") }
        if (-not $state.HasLibrary) { $problems.Add("TRANSFORM_PARSE: no $PresaveLibrary library after the transform") }
        if ($state.Missing.Count -gt 0) { $problems.Add("TRANSFORM_PARSE: still no control for $(($state.Missing | ForEach-Object { $_.Name }) -join ', ')") }
    }
    return @($problems)
}

function Get-Sha256([string]$Text) {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
    return [System.Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function ConvertTo-ColumnSpecs([object[]]$Raw) {
    return @($Raw | ForEach-Object { [pscustomobject]@{ Name = [string]$_.name; Kind = [string]$_.kind; Label = [string]$_.label } })
}

# ============================================================================
# -SelfTest (offline)
# ============================================================================

if ($SelfTest) {
    Write-Host "Add-RegardingFilingPickerToForms  —  SELF-TEST (offline; no token, no network)" -ForegroundColor White
    $root = (Resolve-Path -LiteralPath $FixturePath).Path
    $cases = @(Get-ChildItem -LiteralPath $root -Directory | Sort-Object Name)
    if ($cases.Count -eq 0) { Write-Host "SELF-TEST FAIL: no fixture cases under $root" -ForegroundColor Red; exit 1 }
    $failures = 0
    foreach ($case in $cases) {
        $meta = Get-Content -LiteralPath (Join-Path $case.FullName 'case.json') -Raw | ConvertFrom-Json
        $specs = ConvertTo-ColumnSpecs $meta.columns
        $in = [System.IO.File]::ReadAllText((Join-Path $case.FullName 'input.xml'))
        $refusals = @(Get-PickerRefusals $in $meta.table $specs ([bool]$meta.managed))
        $ok = $true; $why = ''
        $refusalPath = Join-Path $case.FullName 'expected-refusal.txt'
        if (Test-Path -LiteralPath $refusalPath) {
            $want = (Get-Content -LiteralPath $refusalPath -Raw).Trim()
            $got = @($refusals | ForEach-Object { $_.Code })
            if ($got -notcontains $want) { $ok = $false; $why = "expected refusal $want, got [$($got -join ',')]" } else { $why = "refused $want" }
        }
        elseif ($refusals.Count -gt 0) { $ok = $false; $why = "unexpected refusal(s): $(($refusals | ForEach-Object { $_.Code }) -join ',')" }
        else {
            $result = Add-FilingPicker $in $meta.table $meta.formId $specs
            $expected = [System.IO.File]::ReadAllText((Join-Path $case.FullName 'expected.xml'))
            $again = (Add-FilingPicker $result.Xml $meta.table $meta.formId $specs).Xml
            $problems = if ($result.Xml -ceq $in) { @() } else { @(Test-PickerTransform $in $result.Xml $meta.table $meta.formId $specs) }
            if ($problems.Count -gt 0) { $ok = $false; $why = "parse check: $($problems -join '; ')" }
            elseif ($result.Xml -cne $expected) { $ok = $false; $why = 'output differs from expected.xml' }
            elseif ($again -cne $result.Xml) { $ok = $false; $why = 'not idempotent: a second transform changed the output' }
            elseif ($result.Added.Count -eq 0) { $why = 'nothing to do (unchanged)' }
            else { $why = "added $($result.Added.Count): $($result.Added -join '; ')" }
        }
        if ($ok) { Write-Host ("  PASS  {0,-40} {1}" -f $case.Name, $why) -ForegroundColor Green }
        else { $failures++; Write-Host ("  FAIL  {0,-40} {1}" -f $case.Name, $why) -ForegroundColor Red }
    }

    # The parse check on its own: crafted before/after pairs. Every non-refusal fixture is also compared with
    # expected.xml, so only these cases prove that the parse check bites without one (as in a live run).
    $fid = '11111111-2222-3333-4444-555555555555'
    $specs = @(
        [pscustomobject]@{ Name = 'sprk_regardingrecordtype'; Kind = 'Lookup'; Label = 'Regarding Record Type' },
        [pscustomobject]@{ Name = 'sprk_regardingrecordid'; Kind = 'Text'; Label = 'Regarding Record Id' },
        [pscustomobject]@{ Name = 'sprk_regardingrecordname'; Kind = 'Text'; Label = 'Regarding Record Name' },
        [pscustomobject]@{ Name = 'sprk_regardingrecordurl'; Kind = 'Url'; Label = 'Regarding Record URL' },
        [pscustomobject]@{ Name = 'sprk_regardingmatter'; Kind = 'Lookup'; Label = 'Regarding Matter' }
    )
    $base = '<form><tabs><tab name="general"><columns><column><sections><section name="main" id="s1"><rows><row><cell id="c1">' +
            '<control id="sprk_name" datafieldname="sprk_name" /></cell></row></rows></section></sections></column></columns></tab></tabs>' +
            '<controlDescriptions /></form>'
    $good = (Add-FilingPicker $base 'sprk_event' $fid $specs).Xml
    $pairs = @(
        @{ Name = 'parse: the real transform passes (positive control)'; After = $good; Want = $false },
        @{ Name = 'parse: an original attribute changed'; After = $good.Replace('<section name="main" id="s1">', '<section name="main" id="s2">'); Want = $true },
        @{ Name = 'parse: an original node removed'; After = $good.Replace('<control id="sprk_name" datafieldname="sprk_name" />', ''); Want = $true },
        @{ Name = 'parse: an unexpected element added'; After = $good.Replace('</form>', '<Navigation /></form>'); Want = $true },
        @{ Name = 'parse: a foreign section added'; After = $good.Replace('<section name="main" id="s1">', '<section name="sprk_rogue" id="r1" /><section name="main" id="s1">'); Want = $true },
        @{ Name = 'parse: a foreign library added'; After = $good.Replace("<Library name=""$PresaveLibrary""", '<Library name="sprk_other" libraryUniqueId="{1}" /><Library name="' + $PresaveLibrary + '"'); Want = $true },
        @{ Name = 'parse: picker names another entity'; After = $good.Replace('>sprk_event</entity>', '>sprk_todo</entity>'); Want = $true },
        @{ Name = 'parse: a needed hidden cell dropped'; After = [regex]::Replace($good, '<row><cell id="[^"]*" locklevel="0" colspan="1" rowspan="1"><labels><label description="Regarding Matter"[^/]*/></labels><control[^>]*/></cell></row>', ''); Want = $true },
        @{ Name = 'parse: presave handler dropped'; After = [regex]::Replace($good, '<events>.*</events>', ''); Want = $true },
        @{ Name = 'parse: result not well-formed'; After = $good.Replace('</form>', ''); Want = $true }
    )
    foreach ($t in $pairs) {
        $problems = @(Test-PickerTransform $base $t.After 'sprk_event' $fid $specs)
        $bit = $problems.Count -gt 0
        if ($t.After -ceq $good -and $t.Name -notlike '*positive*') { $failures++; Write-Host ("  FAIL  {0,-40} {1} (the crafted mutation did not apply)" -f 'inline', $t.Name) -ForegroundColor Red; continue }
        if ($bit -eq $t.Want) { Write-Host ("  PASS  {0,-40} {1}" -f 'inline', $t.Name) -ForegroundColor Green }
        else { $failures++; Write-Host ("  FAIL  {0,-40} {1} (problems: {2})" -f 'inline', $t.Name, ($problems -join '; ')) -ForegroundColor Red }
    }
    if ($failures -gt 0) { Write-Host "`nSELF-TEST FAIL: $failures case(s)." -ForegroundColor Red; exit 1 }
    Write-Host "`nSELF-TEST PASS: $($cases.Count) fixture case(s) + $($pairs.Count) inline check(s)." -ForegroundColor Green
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

function Get-FormXmlNow([string]$FormId) {
    $f = Invoke-Dv -Endpoint "systemforms($FormId)?`$select=formxml"
    return [string]$f.formxml
}

function Publish-Table([string]$Table) {
    $publish = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>"
    Invoke-Dv -Endpoint "PublishXml" -Method POST -Body @{ ParameterXml = $publish } | Out-Null
}

function Get-LiveColumnSpecs([string]$Table) {
    $attrs = Invoke-Dv -Endpoint "EntityDefinitions(LogicalName='$Table')/Attributes?`$select=LogicalName,AttributeType,DisplayName"
    return @($attrs.value | Where-Object {
            ($_.AttributeType -eq 'Lookup' -and $_.LogicalName -like 'sprk_regarding*') -or ($PairTextColumns -contains $_.LogicalName)
        } | ForEach-Object {
            $kind = if ($_.AttributeType -eq 'Lookup') { 'Lookup' } elseif ($_.LogicalName -eq 'sprk_regardingrecordurl') { 'Url' } else { 'Text' }
            $label = [string]$_.DisplayName.UserLocalizedLabel.Label
            if ([string]::IsNullOrWhiteSpace($label)) { $label = $_.LogicalName }
            [pscustomobject]@{ Name = $_.LogicalName; Kind = $kind; Label = $label }
        } | Sort-Object Name)
}

# ============================================================================
# -RestoreFrom
# ============================================================================

if ($RestoreFrom) {
    Write-Host "Add-RegardingFilingPickerToForms  —  RESTORE from $RestoreFrom" -ForegroundColor White
    Write-Host "Environment: $BaseUrl"
    if (-not (Test-Path -LiteralPath $RestoreFrom)) { Stop-Refused "snapshot '$RestoreFrom' does not exist." }
    try { $snap = Get-Content -LiteralPath $RestoreFrom -Raw -Encoding UTF8 | ConvertFrom-Json }
    catch { Stop-Refused "snapshot '$RestoreFrom' is not valid JSON: $($_.Exception.Message)" }
    if ("$($snap.environmentUrl)".TrimEnd('/') -ine $BaseUrl) { Stop-Refused "the snapshot was taken on '$($snap.environmentUrl)', not '$BaseUrl'." }
    $forms = @($snap.forms)
    if ($forms.Count -eq 0) { Write-Done "the snapshot holds no forms — nothing to restore."; exit 0 }
    foreach ($f in $forms) {
        if ((Get-Sha256 $f.formXmlBefore) -ne $f.sha256Before -or (Get-Sha256 $f.formXmlWritten) -ne $f.sha256Written) {
            Stop-Refused "the snapshot entry for form '$($f.name)' ($($f.formid)) does not match its own hashes (corrupt or edited)."
        }
    }
    $Token = Get-DataverseToken
    Write-Step "1. Each form must still be exactly what the apply wrote"
    $toRestore = @()
    foreach ($f in $forms) {
        $h = Get-Sha256 (Get-FormXmlNow $f.formid)
        if ($h -eq $f.sha256Before) { Write-Info "form '$($f.name)' ($($f.formid)) still holds its 'before' XML — skipped"; continue }
        $expected = if ($f.sha256Stored) { [string]$f.sha256Stored } else { [string]$f.sha256Written }
        if ($h -ne $expected) {
            Stop-Refused "form '$($f.name)' ($($f.formid), $($f.table)) changed after the apply (sha256 $h, expected $expected); if the lock script ran since, restore it first with its own snapshot."
        }
        $toRestore += $f
    }
    if ($toRestore.Count -eq 0) { Write-Done "no form in the snapshot was written — nothing to restore."; exit 0 }
    Write-Step "2. Restore"
    foreach ($f in $toRestore) {
        Invoke-Dv -Endpoint "systemforms($($f.formid))" -Method PATCH -Body @{ formxml = [string]$f.formXmlBefore } | Out-Null
        Write-Done "form '$($f.name)' ($($f.formid)) restored"
    }
    foreach ($t in @($toRestore | ForEach-Object { $_.table } | Sort-Object -Unique)) { Publish-Table $t; Write-Done "published $t" }
    Write-Host ""
    Write-Host "Restored $($toRestore.Count) form(s). Run -Verify: it should now exit 1." -ForegroundColor Green
    exit 0
}

# ============================================================================
# Scan (read-only) — dry run, -Verify and -Apply
# ============================================================================

$Mode = if ($Apply) { "APPLY" } elseif ($Verify) { "VERIFY (read-only)" } else { "DRY RUN (read-only; pass -Apply to perform the writes)" }
Write-Host "Add-RegardingFilingPickerToForms  —  $Mode" -ForegroundColor White
Write-Host "Environment: $BaseUrl"
Write-Host "Forms      : $($Forms -join ', ')"

$refusals = [System.Collections.Generic.List[object]]::new()
$gaps = [System.Collections.Generic.List[string]]::new()
$edits = [System.Collections.Generic.List[object]]::new()

try {
    $Token = Get-DataverseToken

    Write-Step "Prerequisites"
    $wr = Invoke-Dv -Endpoint "webresourceset?`$select=webresourceid,name&`$filter=name eq '$PresaveLibrary'"
    if (@($wr.value).Count -eq 0) { $refusals.Add(@{ Code = 'PREREQ_MISSING'; Where = $BaseUrl; Detail = "web resource $PresaveLibrary is not in the environment" }) }
    else { Write-Info "web resource $PresaveLibrary present" }
    $cc = Invoke-Dv -Endpoint "customcontrols?`$select=customcontrolid,name&`$filter=name eq '$PickerName'"
    if (@($cc.value).Count -eq 0) { $refusals.Add(@{ Code = 'PREREQ_MISSING'; Where = $BaseUrl; Detail = "customcontrol $PickerName is not in the environment" }) }
    else { Write-Info "customcontrol $PickerName present" }

    $specCache = @{}
    foreach ($formId in $Forms) {
        $f = Invoke-Dv -Endpoint "systemforms($formId)?`$select=formid,name,type,objecttypecode,ismanaged,formxml" -AllowNotFound
        if ($null -eq $f) { $refusals.Add(@{ Code = 'FORM_NOT_FOUND'; Where = $formId; Detail = 'no such systemform in this environment' }); continue }
        $table = [string]$f.objecttypecode
        $where = "$table form '$($f.name)' ($($f.formid)) type $($f.type)"
        Write-Step $where
        if ($ChildTables -notcontains $table) { $refusals.Add(@{ Code = 'FORM_NOT_FOUND'; Where = $where; Detail = "not a form of $($ChildTables -join ', ')" }); continue }
        if (-not $specCache.ContainsKey($table)) { $specCache[$table] = Get-LiveColumnSpecs $table }
        $specs = $specCache[$table]
        $xml = [string]$f.formxml
        $formRefusals = @(Get-PickerRefusals $xml $table $specs ([bool]$f.ismanaged))
        foreach ($r in $formRefusals) { $refusals.Add(@{ Code = $r.Code; Where = $where; Detail = $r.Detail }) }
        if ($formRefusals.Count -gt 0) { continue }

        $doc = ConvertTo-FormDocument $xml
        $state = Get-FilingState $doc $specs
        if ($state.Pickers.Count -eq 0) { $gaps.Add("$where : no RegardingResolver") }
        if ($state.HandlerCount -eq 0) { $gaps.Add("$where : $PresaveOnLoad not registered") }
        if (-not $state.HasLibrary) { $gaps.Add("$where : library $PresaveLibrary not on the form") }
        foreach ($m in $state.Missing) { $gaps.Add("$where : no control for $($m.Name)") }

        $result = Add-FilingPicker $xml $table ([string]$f.formid) $specs
        if ($result.Added.Count -eq 0) { Write-Info "complete — nothing to do"; continue }
        foreach ($p in (Test-PickerTransform $xml $result.Xml $table ([string]$f.formid) $specs)) {
            $refusals.Add(@{ Code = 'TRANSFORM_PARSE'; Where = $where; Detail = $p })
        }
        foreach ($a in $result.Added) { Write-Plan "add $a" }
        $edits.Add([pscustomobject]@{ FormId = [string]$f.formid; Table = $table; Name = $f.name; Type = $f.type; Before = $xml; After = $result.Xml })
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
        Write-Host "`nVERIFY PASS: every target form hosts the RegardingResolver for its table, registers the presave, and carries every pair and lookup column." -ForegroundColor Green
        exit 0
    }
    foreach ($g in $gaps) { Write-Host "   GAP $g" -ForegroundColor Red }
    Write-Host "`nVERIFY FAIL: $($gaps.Count) gap(s), $($refusals.Count) refusal case(s)." -ForegroundColor Red
    exit 1
}

if ($refusals.Count -gt 0) { Stop-Refused "$($refusals.Count) refusal case(s), listed above." }
if ($edits.Count -eq 0) { Write-Host "`nNothing to do: every target form is complete." -ForegroundColor Green; exit 0 }
if (-not $Apply) {
    Write-Host "`nDRY RUN complete — no writes were made. $($edits.Count) form(s) would change. Re-run with -Apply." -ForegroundColor White
    exit 0
}

# ============================================================================
# Apply — every check above has passed
# ============================================================================

Write-Step "Snapshot"
if ([string]::IsNullOrEmpty($SnapshotPath)) {
    $SnapshotPath = Join-Path (Get-Location).Path ("filing-picker-snapshot-{0}.json" -f (Get-Date -Format 'yyyyMMddHHmmss'))
}
$snapshot = [ordered]@{
    script         = 'Add-RegardingFilingPickerToForms.ps1'
    task           = 'unified-access-control-r2 task 168 r1 (owner round 19)'
    environmentUrl = $BaseUrl
    createdUtc     = (Get-Date).ToUniversalTime().ToString('o')
    forms          = @($edits | ForEach-Object {
            [ordered]@{
                formid = $_.FormId; table = $_.Table; name = $_.Name; type = $_.Type
                formXmlBefore = $_.Before; formXmlWritten = $_.After
                sha256Before = (Get-Sha256 $_.Before); sha256Written = (Get-Sha256 $_.After)
            }
        })
}
try {
    $snapshot | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $SnapshotPath -Encoding utf8
    $readBack = Get-Content -LiteralPath $SnapshotPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if (@($readBack.forms).Count -ne $edits.Count) { throw "it holds $(@($readBack.forms).Count) form(s), expected $($edits.Count)" }
    foreach ($rf in $readBack.forms) {
        if ((Get-Sha256 $rf.formXmlBefore) -ne $rf.sha256Before -or (Get-Sha256 $rf.formXmlWritten) -ne $rf.sha256Written) { throw "entry '$($rf.name)' does not round-trip" }
    }
}
catch { Stop-Refused "the snapshot could not be written and read back at '$SnapshotPath': $($_.Exception.Message)" }
Write-Done "snapshot written and read back: $SnapshotPath"

Write-Step "Write"
$written = 0
foreach ($e in $edits) {
    if ((Get-FormXmlNow $e.FormId) -cne $e.Before) {
        Stop-Refused "FORM_CHANGED: form '$($e.Name)' ($($e.FormId)) changed since the scan; $written form(s) were already written — restore them with -RestoreFrom '$SnapshotPath' if needed."
    }
    Invoke-Dv -Endpoint "systemforms($($e.FormId))" -Method PATCH -Body @{ formxml = $e.After } | Out-Null
    $written++
    Write-Done "form '$($e.Name)' ($($e.FormId)) updated"
}
foreach ($t in @($edits | ForEach-Object { $_.Table } | Sort-Object -Unique)) { Publish-Table $t; Write-Done "published $t" }

Write-Step "Read back"
$incomplete = @()
foreach ($sf in $snapshot.forms) {
    $stored = Get-FormXmlNow $sf.formid
    $sf['formXmlStored'] = $stored
    $sf['sha256Stored'] = Get-Sha256 $stored
    $state = Get-FilingState (ConvertTo-FormDocument $stored) $specCache[$sf.table]
    if ($state.Pickers.Count -eq 0 -or $state.HandlerCount -eq 0 -or -not $state.HasLibrary -or $state.Missing.Count -gt 0) { $incomplete += "form '$($sf.name)' ($($sf.formid))" }
}
$snapshot | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $SnapshotPath -Encoding utf8
Write-Done "snapshot updated with the stored form XML: $SnapshotPath"
if ($incomplete.Count -gt 0) { throw "Read-back shows incomplete form(s) after the apply: $($incomplete -join '; '). Restore with -RestoreFrom '$SnapshotPath'." }

Write-Host ""
Write-Host "Updated $written form(s). Snapshot: $SnapshotPath" -ForegroundColor Green
Write-Host "Next: -Verify (must exit 0), then Lock-CoreAncestorStampColumnsOnForms.ps1 dry run / -Apply / -Verify." -ForegroundColor Green
exit 0
