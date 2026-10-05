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
        Spaarke.SmartTodo.RegardingPreSave.onLoad (pass execution context), as on the To Do main form;
      - (owner round 25 item 8 (a)) visible="false" on the <cell> of every VISIBLE control bound to a raw filing
        column: a pair column (sprk_regardingrecordtype, ...id, ...name, ...url, ...number) or a sprk_regarding*
        lookup of the table that is not one of the four roots. The picker's own host control is excepted; the four
        roots are left to Lock-CoreAncestorStampColumnsOnForms.ps1 (disabled, still visible). A typed pair, or an
        intermediate on a row with no pair, would otherwise re-file the row without the picker. Hidden controls stay
        enabled, so the RegardingResolver's setValue writes on a re-file and a clear are submitted as before. This
        is the ONE attribute change the parse check allows on an original node.
    New ids are derived from the form id (a stable hash), so a re-run produces identical bytes, and a form that is
    already complete is left byte-identical ("nothing to do").

    The To Do main form (round 25 item 8) is a target too: it already hosts the picker and the presave, and gains
    only the hidden cells it lacks. On dev (2026-10-04) those are two: sprk_regardingservicerequest (the cell round 25
    item 8 names) and sprk_regardingagreement (also a sprk_regarding* lookup of sprk_todo with no control; every
    missing one is added, by design).

    The column names come from the ONE list of the regarding filing columns, config/regarding-filing-columns.json
    (owner round 38): its rootColumns, recordTypeColumn, pairTextColumns, lookupPrefix and lookupTypes.
    Lock-CoreAncestorStampColumnsOnForms.ps1 and the SpaarkeGridCustomizer PCF read the same file.

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
      RAW_CONTROL_HOSTS_PCF   a visible raw filing control hosts another named custom control (hiding its cell
                              would hide that control too);
      RAW_CONTROL_NOT_IN_CELL a visible raw filing control whose parent is not a <cell>;
      LIBRARY_SHOWS           (live) a library on the form (or the presave) names a raw filing column of the table
                              AND calls setVisible, so it could re-show a hidden control at runtime;
      WORKFLOW_REFERENCE      (live) a business rule (workflow category 2) on the table, or a business process flow
                              (category 4) on or naming the table, whose xaml or clientdata references a raw filing
                              column of the table: a business rule can show a hidden raw control, and a process-flow
                              step puts the column on the process bar as an editable input. Neither is in the form
                              XML. The mirror of the lock script's WORKFLOW_REFERENCE over the raw filing columns
                              (task 168 v1, verifier item 1); -Verify also names it as a gap;
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
    enabled presave OnLoad handler and its library, carries a control for every pair column and every
    sprk_regarding* lookup of its table, shows NO raw filing control (each one's cell, section or tab is hidden; the
    picker's host excepted), no business rule or business process flow of its table references a raw filing column
    (WORKFLOW_REFERENCE), and no refusal case is present. Otherwise exit 1 naming each gap. Any read fault is a
    FAILED check (exit 1).

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
    Target form ids. Default: six forms: the five named by owner round 19 (event main, event modal, event Assign
    Work, communication Message, analysis main) and the To Do main form (owner round 25 item 8).

.PARAMETER FixturePath
    -SelfTest only. Default: tests/fixtures/form-filing-picker.

.PARAMETER FilingColumnsPath
    The ONE list of the regarding filing columns (owner round 38). Default: config/regarding-filing-columns.json at
    the repository root. An unreadable or incomplete file stops the script.

.EXAMPLE
    .\Add-RegardingFilingPickerToForms.ps1                 # dry run
    .\Add-RegardingFilingPickerToForms.ps1 -SelfTest       # offline fixtures
    .\Add-RegardingFilingPickerToForms.ps1 -Apply          # snapshot, PATCH, publish, read back
    .\Add-RegardingFilingPickerToForms.ps1 -Verify         # read-only (exit 0 / 1)
    .\Add-RegardingFilingPickerToForms.ps1 -RestoreFrom .\filing-picker-snapshot-20261004120000.json

.NOTES
    Project : unified-access-control-r2
    Task    : 168 (#1107) r1 — owner round 19 items 1, 2 and 4; f1 — round 25 item 8 (a); v1 — WORKFLOW_REFERENCE
              (verifier item 1) and the ONE shared list of filing columns (owner round 38)
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
        'd408a721-77d7-f011-8406-7c1e525abd8b',   # sprk_analysis      Analysis main form          (round 19 item 4)
        'eca59df4-1364-f111-ab0c-7ced8ddc4cc6'    # sprk_todo          To Do main form             (round 25 item 8: its missing
                                                  #                    sprk_regardingservicerequest hidden cell)
    ),

    [Parameter(Mandatory = $false)]
    [string]$FixturePath = (Join-Path $PSScriptRoot '..' 'tests' 'fixtures' 'form-filing-picker'),

    [Parameter(Mandatory = $false)]
    [string]$FilingColumnsPath = (Join-Path $PSScriptRoot '..' 'config' 'regarding-filing-columns.json')
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

# The ONE list of the regarding filing columns (owner round 38): config/regarding-filing-columns.json. This script,
# Lock-CoreAncestorStampColumnsOnForms.ps1 and the SpaarkeGridCustomizer PCF all read it; never a second copy here.
function Read-FilingColumns([string]$Path) {
    try { $j = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json }
    catch { throw "FILING_COLUMNS: '$Path' could not be read as JSON: $($_.Exception.Message)" }
    $out = [pscustomobject]@{
        RootColumns      = @($j.rootColumns | ForEach-Object { "$_".Trim().ToLowerInvariant() } | Where-Object { $_ })
        RecordTypeColumn = "$($j.recordTypeColumn)".Trim().ToLowerInvariant()
        PairTextColumns  = @($j.pairTextColumns | ForEach-Object { "$_".Trim().ToLowerInvariant() } | Where-Object { $_ })
        OptionalPairTextColumns = @($j.optionalPairTextColumns | ForEach-Object { "$_".Trim().ToLowerInvariant() } | Where-Object { $_ })
        LookupPrefix     = "$($j.lookupPrefix)".Trim().ToLowerInvariant()
        LookupTypes      = @($j.lookupTypes | ForEach-Object { "$_".Trim() } | Where-Object { $_ })
    }
    if ($out.RootColumns.Count -eq 0 -or $out.PairTextColumns.Count -eq 0 -or -not $out.RecordTypeColumn -or -not $out.LookupPrefix -or $out.LookupTypes.Count -eq 0) {
        throw "FILING_COLUMNS: '$Path' lacks rootColumns, recordTypeColumn, pairTextColumns, lookupPrefix or lookupTypes"
    }
    # Decision round 44: the required pair is DERIVED from the one list. An optional column that is not a pair column
    # is a typo that would silently require (or silently drop) a column — refuse it, naming it.
    $stray = @($out.OptionalPairTextColumns | Where-Object { $out.PairTextColumns -notcontains $_ })
    if ($stray.Count -gt 0) {
        throw "FILING_COLUMNS: '$Path' optionalPairTextColumns names $($stray -join ', '), which is not in pairTextColumns"
    }
    return $out
}
$FilingColumns = Read-FilingColumns $FilingColumnsPath
$RecordTypeColumn = $FilingColumns.RecordTypeColumn
$PairTextColumns = @($FilingColumns.PairTextColumns)
# CoreAncestorResolver.CoreAncestorLookups: the four roots. Lock-CoreAncestorStampColumnsOnForms.ps1 DISABLES their
# controls; this script never touches them. Every OTHER sprk_regarding* lookup and every pair column is a raw filing
# input: typing a pair (or an intermediate on a row with no pair) re-files the row without the picker, so its
# controls are HIDDEN (owner round 25 item 8 (a)), the picker's own host control excepted.
$RootColumns = @($FilingColumns.RootColumns)
$LookupPrefix = $FilingColumns.LookupPrefix
$LookupTypes = @($FilingColumns.LookupTypes)
# The ADR-024 pair the RegardingResolver WRITES on every pick and clear (ResolverWriteHandler): a table missing any of
# these cannot host it (PAIR_INCOMPLETE names the missing column). DERIVED from the one list (decision round 44): the
# record-type column and every pair text column the list does not mark optional (optionalPairTextColumns — the
# columns the picker writes only where they exist, e.g. sprk_regardingrecordnumber). No column is named here.
$RequiredPairColumns = @(@($RecordTypeColumn) + @($PairTextColumns | Where-Object { $FilingColumns.OptionalPairTextColumns -notcontains $_ }))
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
            (($PairTextColumns -contains $_.Name.ToLowerInvariant()) -or ($_.Kind -eq 'Lookup' -and $_.Name.ToLowerInvariant().StartsWith($LookupPrefix, [System.StringComparison]::Ordinal)))
        })
}

<#
    A raw filing column of this table (owner round 25 item 8 (a)): a pair column (sprk_regardingrecordtype and the
    four pair text columns), or a sprk_regarding* LOOKUP of the table that is not one of the four roots. Compared
    case-insensitively and as a whole value; a lookup counts only when the table's live metadata says it is one.
#>
function Test-IsRawFilingColumn([string]$Name, [object[]]$ColumnSpecs) {
    if ([string]::IsNullOrWhiteSpace($Name)) { return $false }
    $n = $Name.Trim().ToLowerInvariant()
    if ($n -ceq $RecordTypeColumn -or $PairTextColumns -contains $n) { return $true }
    if ($RootColumns -contains $n) { return $false }
    return @($ColumnSpecs | Where-Object { $_.Kind -eq 'Lookup' -and $_.Name.ToLowerInvariant() -ceq $n -and $n.StartsWith($LookupPrefix, [System.StringComparison]::Ordinal) }).Count -gt 0
}

<#
    Every raw filing column NAME of the table, whether or not the form has a control for it yet: the record-type
    column, the pair text columns, and each non-root sprk_regarding* lookup of the table. What LIBRARY_SHOWS and
    WORKFLOW_REFERENCE search for.
#>
function Get-RawFilingColumnNames([object[]]$ColumnSpecs) {
    $lookups = @($ColumnSpecs | Where-Object { Test-IsRawFilingColumn $_.Name $ColumnSpecs } | ForEach-Object { $_.Name.ToLowerInvariant() })
    return @(@($RecordTypeColumn) + $PairTextColumns + $lookups | Sort-Object -Unique)
}

<#
    Business rules and business process flows that could show or expose a raw filing column of the table at runtime
    (owner round 25 item 8 (a), "-Verify fails on a visible one"; task 168 v1, verifier item 1). Neither lives in the
    form XML: a business rule (workflow category 2) can show a hidden raw control, and a business process flow step
    (category 4) puts the column on the process bar as an editable input. The mirror of the lock script's
    WORKFLOW_REFERENCE, over the raw filing columns:
      - a business rule whose primaryentity is the table, or a business process flow whose primaryentity is the table
        or whose xaml / clientdata names it (a flow can span tables);
      - whose xaml or clientdata references a raw filing column (whole name, case-insensitive).
    Every other category (a classic workflow, an action) is not a form input and is ignored. Pure; -SelfTest pins it.
    $Workflows: rows with workflowid, name, category, primaryentity, xaml, clientdata.
    Returns a list of @{ Code = 'WORKFLOW_REFERENCE'; Where; Detail; Columns }.
#>
function Get-WorkflowRefusals([object[]]$Workflows, [string]$Table, [string[]]$RawColumns) {
    $out = @()
    foreach ($w in @($Workflows)) {
        if ($null -eq $w) { continue }
        $text = "$($w.xaml)`n$($w.clientdata)"
        $onTable = "$($w.primaryentity)".Trim() -ieq $Table
        $kind = switch ([string]$w.category) { '2' { 'business rule' } '4' { 'business process flow' } default { $null } }
        if ($null -eq $kind) { continue }
        if ($kind -eq 'business rule' -and -not $onTable) { continue }
        if ($kind -eq 'business process flow' -and -not $onTable -and
            -not [regex]::IsMatch($text, "(?<![A-Za-z0-9_])$([regex]::Escape($Table))(?![A-Za-z0-9_])", 'IgnoreCase')) { continue }
        $hits = @($RawColumns | Where-Object { [regex]::IsMatch($text, "(?<![A-Za-z0-9_])$([regex]::Escape($_))(?![A-Za-z0-9_])", 'IgnoreCase') } | Sort-Object -Unique)
        if ($hits.Count -eq 0) { continue }
        $out += @{
            Code    = 'WORKFLOW_REFERENCE'
            Where   = "$Table $kind '$($w.name)' ($($w.workflowid))"
            Detail  = "references raw filing column(s) $($hits -join ', '): a business rule can show a hidden raw control and a process-flow step exposes the column on the process bar, neither in the form XML (owner decision)"
            Columns = $hits
        }
    }
    return $out
}

function Test-ElementHidden([System.Xml.XmlNode]$Node) {
    # Accessor methods, not properties (PowerShell's XML adapter shadows .Name with a name="" attribute).
    $n = $Node.get_ParentNode()
    while ($null -ne $n -and $n.get_NodeType() -eq [System.Xml.XmlNodeType]::Element) {
        $v = $n.GetAttribute('visible')
        if ($v -and $v.Trim() -ieq 'false') { return $true }
        $n = $n.get_ParentNode()
    }
    return $false
}

<#
    The raw filing controls on a parsed form, the picker's host control(s) excepted: @{ Node; Id; Column; Hidden;
    CellIndex (document-order index among all <cell> elements, or -1 when the parent is not a <cell>);
    HostsPcf (the named custom controls it hosts, if any) }.
#>
function Get-RawFilingControls([System.Xml.XmlDocument]$Doc, [object[]]$ColumnSpecs) {
    $hostUids = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $hosted = @{}
    foreach ($cd in $Doc.SelectNodes('//controlDescription')) {
        $for = $cd.GetAttribute('forControl')
        $named = @($cd.SelectNodes('.//customControl') | ForEach-Object { $_.GetAttribute('name') } | Where-Object { $_ } | Sort-Object -Unique)
        if ($named.Count -eq 0 -or [string]::IsNullOrEmpty($for)) { continue }
        if (@($named | Where-Object { $_ -match $PickerPattern }).Count -gt 0) { [void]$hostUids.Add($for) }
        $hosted[$for.ToLowerInvariant()] = $named
    }
    $cells = @($Doc.SelectNodes('//cell'))
    $cellIndex = [System.Collections.Generic.Dictionary[object, int]]::new([System.Collections.Generic.ReferenceEqualityComparer]::Instance)
    for ($i = 0; $i -lt $cells.Count; $i++) { $cellIndex[[object]$cells[$i]] = $i }
    $out = @()
    foreach ($c in $Doc.SelectNodes('//control')) {
        if (-not (Test-IsRawFilingColumn $c.GetAttribute('datafieldname') $ColumnSpecs)) { continue }
        $uid = $c.GetAttribute('uniqueid')
        if ($uid -and $hostUids.Contains($uid)) { continue }   # the picker's host: never hidden
        $parent = $c.get_ParentNode()
        $idx = if ($parent.get_LocalName() -ceq 'cell' -and $cellIndex.ContainsKey([object]$parent)) { $cellIndex[[object]$parent] } else { -1 }
        $out += [pscustomobject]@{
            Node      = $c
            Id        = $c.GetAttribute('id')
            Column    = $c.GetAttribute('datafieldname')
            Hidden    = (Test-ElementHidden $c)
            CellIndex = $idx
            HostsPcf  = if ($uid -and $hosted.ContainsKey($uid.ToLowerInvariant())) { @($hosted[$uid.ToLowerInvariant()]) } else { @() }
        }
    }
    return $out
}

<#
    A form library that could re-show a hidden raw control at runtime: its content names one of the form's raw
    filing columns (case-insensitive) AND calls setVisible. The mirror of the lock script's LIBRARY_UNLOCKS.
#>
function Test-LibraryShows([string]$Content, [string[]]$Columns) {
    if ([string]::IsNullOrEmpty($Content) -or $Content -notmatch 'setVisible') { return $false }
    foreach ($c in $Columns) {
        if ([regex]::IsMatch($Content, "(?<![A-Za-z0-9_])$([regex]::Escape($c))(?![A-Za-z0-9_])", 'IgnoreCase')) { return $true }
    }
    return $false
}

<#
    Edit one <cell ...> start tag so the cell is hidden: rewrite an existing visible value (for example "true") to
    "false", else add visible="false". Only cells of VISIBLE raw controls reach here, so an existing value is never
    "false" (a cell with visible="false" hides its control); the transform is idempotent because a second run finds
    no visible raw control.
#>
function Set-CellTagHidden([string]$Tag) {
    $m = [regex]::Match($Tag, '(?<=\s)visible\s*=\s*(?:"([^"]*)"|''([^'']*)'')', 'IgnoreCase')
    if ($m.Success) {
        return $Tag.Substring(0, $m.Index) + 'visible="false"' + $Tag.Substring($m.Index + $m.Length)
    }
    $close = [regex]::Match($Tag, '\s*/?>$')
    return $Tag.Substring(0, $close.Index) + ' visible="false"' + $Tag.Substring($close.Index)
}

<#
    Hide the <cell> elements at the given document-order indexes, as start-tag text edits (never a
    re-serialization). Positions come from an XmlReader (line/column), as in Get-TopLevelElementSpan; edits run
    from the last offset to the first so the earlier offsets stay valid.
#>
function Hide-CellsByIndex([string]$Xml, [int[]]$Indexes) {
    if (@($Indexes).Count -eq 0) { return $Xml }
    $want = [System.Collections.Generic.HashSet[int]]::new([int[]]@($Indexes))
    $lineStarts = [System.Collections.Generic.List[int]]::new(); $lineStarts.Add(0)
    for ($i = 0; $i -lt $Xml.Length; $i++) { if ($Xml[$i] -eq "`n") { $lineStarts.Add($i + 1) } }
    $settings = [System.Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $offsets = [System.Collections.Generic.List[int]]::new()
    $reader = [System.Xml.XmlReader]::Create([System.IO.StringReader]::new($Xml), $settings)
    try {
        $n = -1
        while ($reader.Read()) {
            if ($reader.NodeType -ne [System.Xml.XmlNodeType]::Element -or $reader.LocalName -cne 'cell') { continue }
            $n++
            if (-not $want.Contains($n)) { continue }
            $li = [System.Xml.IXmlLineInfo]$reader
            $offsets.Add($lineStarts[$li.LineNumber - 1] + $li.LinePosition - 2)
        }
    }
    finally { $reader.Dispose() }
    if ($offsets.Count -ne $want.Count) { throw "TRANSFORM_PARSE: found $($offsets.Count) of $($want.Count) cells to hide" }
    $tagPattern = [regex]::new('\G<cell\b(?:[^>"'']|"[^"]*"|''[^'']*'')*?/?>')
    foreach ($at in ($offsets | Sort-Object -Descending)) {
        $m = $tagPattern.Match($Xml, $at)
        if (-not $m.Success) { throw "TRANSFORM_PARSE: no <cell> start tag at offset $at" }
        $Xml = $Xml.Substring(0, $at) + (Set-CellTagHidden $m.Value) + $Xml.Substring($at + $m.Length)
    }
    return $Xml
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
    $rawVisible = @(Get-RawFilingControls $Doc $ColumnSpecs | Where-Object { -not $_.Hidden })
    return [pscustomobject]@{
        Pickers         = $pickers
        HandlerCount    = $handlers.Count
        HandlerDisabled = @($handlers | Where-Object { $_.GetAttribute('enabled') -ieq 'false' }).Count -gt 0
        HasLibrary      = $library
        Missing         = $missing
        RawVisible      = $rawVisible
    }
}

function Test-FilingComplete([object]$State) {
    return ($State.Pickers.Count -gt 0) -and ($State.HandlerCount -gt 0) -and $State.HasLibrary -and
        ($State.Missing.Count -eq 0) -and ($State.RawVisible.Count -eq 0)
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
    foreach ($r in $state.RawVisible) {
        # Hiding a cell that hosts another PCF would hide that PCF too: an owner decision, never a silent edit.
        if (@($r.HostsPcf).Count -gt 0) {
            $out += @{ Code = 'RAW_CONTROL_HOSTS_PCF'; Detail = "visible raw filing control '$($r.Id)' ($($r.Column)) hosts $(@($r.HostsPcf) -join ', '); hiding it would hide that control too" }
        }
        if ($r.CellIndex -lt 0) {
            $out += @{ Code = 'RAW_CONTROL_NOT_IN_CELL'; Detail = "visible raw filing control '$($r.Id)' ($($r.Column)) is not inside a <cell>, so it cannot be hidden by its cell" }
        }
    }
    $complete = Test-FilingComplete $state
    if ($IsManaged -and -not $complete) {
        $out += @{ Code = 'MANAGED_FORM'; Detail = 'managed form that needs the picker, cells, presave or hidden raw controls; change it in its owning solution' }
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

    # 0. Hide the cell of every VISIBLE raw filing control (owner round 25 item 8 (a)): the pair columns and the
    #    non-root sprk_regarding* lookups, the picker's host excepted. Done first, on the original text; the steps
    #    below re-find their positions in the edited text. Hidden controls stay ENABLED, so the RegardingResolver's
    #    setValue writes on a re-file and a clear are submitted exactly as today.
    $hideIdx = @($state.RawVisible | ForEach-Object { [int]$_.CellIndex } | Sort-Object -Unique)
    if ($hideIdx.Count -gt 0) {
        $xml = Hide-CellsByIndex $xml $hideIdx
        foreach ($r in $state.RawVisible) { $added.Add("hidden cell of control '$($r.Id)' ($($r.Column))") }
    }

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
function Test-ShallowMatch([System.Xml.XmlNode]$X, [System.Xml.XmlNode]$Y, [object[]]$ColumnSpecs = @()) {
    if ($X.get_NodeType() -ne $Y.get_NodeType() -or $X.get_LocalName() -cne $Y.get_LocalName()) { return $false }
    if ($X.get_NodeType() -ne [System.Xml.XmlNodeType]::Element) { return ($X.get_Value() -ceq $Y.get_Value()) }
    $hideOk = Test-AllowedCellHide $X $Y $ColumnSpecs
    $xa = @($X.get_Attributes() | Where-Object { -not ($hideOk -and $_.get_Name() -ceq 'visible') })
    $ya = @($Y.get_Attributes() | Where-Object { -not ($hideOk -and $_.get_Name() -ceq 'visible') })
    if ($xa.Count -ne $ya.Count) { return $false }
    foreach ($at in $xa) {
        $other = $Y.GetAttributeNode($at.get_Name())
        if ($null -eq $other -or $other.get_Value() -cne $at.get_Value()) { return $false }
    }
    return $true
}

<#
    The ONE attribute change the parse check allows on an original node (owner round 25 item 8 (a)): a <cell> that
    holds a raw filing control (not the picker's host) whose visible became exactly "false". Every other attribute
    of that cell must still match (Test-ShallowMatch compares them).
#>
function Test-AllowedCellHide([System.Xml.XmlNode]$X, [System.Xml.XmlNode]$Y, [object[]]$ColumnSpecs) {
    if ($X.get_LocalName() -cne 'cell' -or $Y.get_LocalName() -cne 'cell') { return $false }
    if ($Y.GetAttribute('visible') -cne 'false' -or $X.GetAttribute('visible') -ceq 'false') { return $false }
    $ownerDoc = $Y.get_OwnerDocument()
    $raw = @(Get-RawFilingControls $ownerDoc $ColumnSpecs | Where-Object { [object]::ReferenceEquals($_.Node.get_ParentNode(), $Y) })
    return $raw.Count -gt 0
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
        $hideOk = Test-AllowedCellHide $x $y $ColumnSpecs
        foreach ($k in (@($ax.Keys) + @($ay.Keys) | Sort-Object -Unique -CaseSensitive)) {
            if ($hideOk -and $k -ceq 'visible') { continue }   # the one allowed change: a raw filing cell hidden
            if (-not $ax.ContainsKey($k) -or -not $ay.ContainsKey($k) -or $ax[$k] -cne $ay[$k]) {
                $problems.Add("TRANSFORM_PARSE: attribute '$k' changed on <$xName>")
            }
        }
        # Children: the original children in order, with only allowed additions between them.
        $xc = @($x.get_ChildNodes()); $yc = @($y.get_ChildNodes())
        $i = 0
        foreach ($c in $yc) {
            if ($i -lt $xc.Count -and (Test-ShallowMatch $xc[$i] $c $ColumnSpecs)) {
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
        if ($state.RawVisible.Count -gt 0) { $problems.Add("TRANSFORM_PARSE: raw filing control(s) still visible: $(($state.RawVisible | ForEach-Object { "$($_.Id) ($($_.Column))" }) -join ', ')") }
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
        # A case may carry the table's business rules / process flows (rows as the live workflows query returns
        # them): their refusals join the form's, exactly as in a live run.
        if ($meta.PSObject.Properties.Name -contains 'workflows') {
            $refusals += @(Get-WorkflowRefusals @($meta.workflows) $meta.table (Get-RawFilingColumnNames $specs))
        }
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
                $result = Add-FilingPicker $in $meta.table $meta.formId $specs
                $expected = [System.IO.File]::ReadAllText((Join-Path $case.FullName 'expected.xml'))
                $problems = if ($result.Xml -ceq $in) { @() } else { @(Test-PickerTransform $in $result.Xml $meta.table $meta.formId $specs) }
                if ($problems.Count -gt 0) { $ok = $false; $why = "parse check: $($problems -join '; ')" }
                elseif ($result.Xml -cne $expected) { $ok = $false; $why = 'output differs from expected.xml' }
                elseif ((Add-FilingPicker $result.Xml $meta.table $meta.formId $specs).Xml -cne $result.Xml) { $ok = $false; $why = 'not idempotent: a second transform changed the output' }
                elseif ($result.Added.Count -eq 0) { $why = 'nothing to do (unchanged)' }
                else { $why = "added $($result.Added.Count): $($result.Added -join '; ')" }
            }
            catch { $ok = $false; $why = "transform threw: $($_.Exception.Message)" }
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
    # Cell c2 holds a VISIBLE raw filing control (sprk_regardingrecordid), so the real transform also hides it
    # (owner round 25 item 8 (a)) and the cases below can prove the one allowed attribute change is held tight.
    $base = '<form><tabs><tab name="general"><columns><column><sections><section name="main" id="s1"><rows><row><cell id="c1">' +
            '<control id="sprk_name" datafieldname="sprk_name" /></cell></row><row><cell id="c2">' +
            '<control id="sprk_regardingrecordid" datafieldname="sprk_regardingrecordid" /></cell></row></rows></section></sections></column></columns></tab></tabs>' +
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
        @{ Name = 'parse: result not well-formed'; After = $good.Replace('</form>', ''); Want = $true },
        # The hide (owner round 25 item 8 (a)): exactly visible="false", only on a raw filing control's cell.
        @{ Name = 'parse: a raw filing cell left visible'; After = $good.Replace('<cell id="c2" visible="false">', '<cell id="c2">'); Want = $true },
        @{ Name = 'parse: a non-raw cell hidden'; After = $good.Replace('<cell id="c1">', '<cell id="c1" visible="false">'); Want = $true },
        @{ Name = 'parse: raw cell hidden + another attribute'; After = $good.Replace('<cell id="c2" visible="false">', '<cell id="c2" visible="false" colspan="2">'); Want = $true },
        @{ Name = 'parse: raw cell visible set to a non-false value'; After = $good.Replace('<cell id="c2" visible="false">', '<cell id="c2" visible="False">'); Want = $true }
    )
    foreach ($t in $pairs) {
        $problems = @(Test-PickerTransform $base $t.After 'sprk_event' $fid $specs)
        $bit = $problems.Count -gt 0
        if ($t.After -ceq $good -and $t.Name -notlike '*positive*') { $failures++; Write-Host ("  FAIL  {0,-40} {1} (the crafted mutation did not apply)" -f 'inline', $t.Name) -ForegroundColor Red; continue }
        if ($bit -eq $t.Want) { Write-Host ("  PASS  {0,-40} {1}" -f 'inline', $t.Name) -ForegroundColor Green }
        else { $failures++; Write-Host ("  FAIL  {0,-40} {1} (problems: {2})" -f 'inline', $t.Name, ($problems -join '; ')) -ForegroundColor Red }
    }
    # LIBRARY_SHOWS (live check, pure predicate): a library naming a raw filing column AND calling setVisible.
    $libs = @(
        @{ Name = 'library: setVisible + raw column'; Got = (Test-LibraryShows 'formContext.getControl("sprk_regardingRecordName").setVisible(true);' @('sprk_regardingrecordname')); Want = $true },
        @{ Name = 'library: raw column, no setVisible'; Got = (Test-LibraryShows 'getAttribute("sprk_regardingrecordname").getValue();' @('sprk_regardingrecordname')); Want = $false },
        @{ Name = 'library: setVisible, near-miss column only'; Got = (Test-LibraryShows 'getControl("sprk_regardingrecordnamex").setVisible(true);' @('sprk_regardingrecordname')); Want = $false }
    )
    foreach ($t in $libs) {
        if ($t.Got -eq $t.Want) { Write-Host ("  PASS  {0,-40} {1}" -f 'inline', $t.Name) -ForegroundColor Green }
        else { $failures++; Write-Host ("  FAIL  {0,-40} {1} (got {2})" -f 'inline', $t.Name, $t.Got) -ForegroundColor Red }
    }
    # WORKFLOW_REFERENCE (live check, pure predicate; task 168 v1, verifier item 1). Fixtures 19 and 20 are the two
    # positive shapes; these pin each filter of Get-WorkflowRefusals and the raw column set it searches for.
    $wfSpecs = @($specs) + @([pscustomobject]@{ Name = 'sprk_regardingaccount'; Kind = 'Lookup'; Label = 'Regarding Account' })
    $wfRaw = @(Get-RawFilingColumnNames $wfSpecs)
    $wfRow = { param($category, $entity, $xaml, $clientdata) [pscustomobject]@{ workflowid = 'w1'; name = 'rule'; category = $category; primaryentity = $entity; xaml = $xaml; clientdata = $clientdata } }
    $wfs = @(
        @{ Name = 'workflow: rule on the table shows a pair column'; Rows = @(& $wfRow 2 'sprk_event' '<SetVisibility Field="sprk_regardingRecordId" IsVisible="True" />' ''); Want = $true },
        @{ Name = 'workflow: flow on the table (primaryentity)'; Rows = @(& $wfRow 4 'sprk_event' '' '{"steps":[{"attribute":"sprk_regardingrecordname"}]}'); Want = $true },
        @{ Name = 'workflow: flow naming the table, non-root lookup'; Rows = @(& $wfRow 4 'sprk_x_bpf' '' '{"entity":"sprk_event","attribute":"sprk_regardingaccount"}'); Want = $true },
        @{ Name = 'workflow: rule of another table'; Rows = @(& $wfRow 2 'sprk_todo' '<SetVisibility Field="sprk_regardingrecordid" />' ''); Want = $false },
        @{ Name = 'workflow: flow of another table only'; Rows = @(& $wfRow 4 'sprk_document' '' '{"entity":"sprk_document","attribute":"sprk_regardingrecordid"}'); Want = $false },
        @{ Name = 'workflow: flow names the table only as a prefix'; Rows = @(& $wfRow 4 'sprk_x_bpf' '' '{"entity":"sprk_eventtype","attribute":"sprk_regardingrecordid"}'); Want = $false },
        @{ Name = 'workflow: rule references only a root'; Rows = @(& $wfRow 2 'sprk_event' '<SetVisibility Field="sprk_regardingmatter" />' ''); Want = $false },
        @{ Name = 'workflow: rule references a near-miss column'; Rows = @(& $wfRow 2 'sprk_event' '<SetVisibility Field="sprk_regardingrecordidx" />' ''); Want = $false },
        @{ Name = 'workflow: a classic workflow (category 0)'; Rows = @(& $wfRow 0 'sprk_event' '<SetAttributeValue Field="sprk_regardingrecordid" />' ''); Want = $false }
    )
    foreach ($t in $wfs) {
        $got = @(Get-WorkflowRefusals $t.Rows 'sprk_event' $wfRaw)
        $bit = $got.Count -gt 0 -and @($got | Where-Object { $_.Code -cne 'WORKFLOW_REFERENCE' }).Count -eq 0
        if ($bit -eq $t.Want -and ($t.Want -or $got.Count -eq 0)) { Write-Host ("  PASS  {0,-40} {1}" -f 'inline', $t.Name) -ForegroundColor Green }
        else { $failures++; Write-Host ("  FAIL  {0,-40} {1} (got [{2}])" -f 'inline', $t.Name, (($got | ForEach-Object { $_.Detail }) -join '; ')) -ForegroundColor Red }
    }
    # The raw column set the live checks search for comes from the ONE shared list (owner round 38): the record-type
    # column, every pair text column, and the table's non-root sprk_regarding* lookups; never a root.
    $wantRaw = @(@($RecordTypeColumn) + $PairTextColumns + @('sprk_regardingaccount') | Sort-Object -Unique)
    $rawOk = (($wfRaw -join ',') -ceq ($wantRaw -join ',')) -and @($wfRaw | Where-Object { $RootColumns -contains $_ }).Count -eq 0
    $wfs += @{ Name = 'raw names: pair + non-root lookups, no root' }
    if ($rawOk) { Write-Host ("  PASS  {0,-40} {1}" -f 'inline', 'raw names: pair + non-root lookups, no root') -ForegroundColor Green }
    else { $failures++; Write-Host ("  FAIL  {0,-40} {1} (got [{2}])" -f 'inline', 'raw names: pair + non-root lookups, no root', ($wfRaw -join ', ')) -ForegroundColor Red }
    # Decision round 44: the required pair is DERIVED from the one list — the record-type column plus every pair text
    # column not marked optional — and a table lacking one is refused naming it; a stray optional entry is refused too.
    $reqWant = @(@($RecordTypeColumn) + @($PairTextColumns | Where-Object { $FilingColumns.OptionalPairTextColumns -notcontains $_ }))
    $reqOk = (($RequiredPairColumns -join ',') -ceq ($reqWant -join ',')) -and $FilingColumns.OptionalPairTextColumns.Count -gt 0 -and
        @($RequiredPairColumns | Where-Object { $FilingColumns.OptionalPairTextColumns -contains $_ }).Count -eq 0
    $wfs += @{ Name = 'required pair: derived, optional excluded' }
    if ($reqOk) { Write-Host ("  PASS  {0,-40} {1}" -f 'inline', 'required pair: derived from the list, optional excluded') -ForegroundColor Green }
    else { $failures++; Write-Host ("  FAIL  {0,-40} {1} (got [{2}])" -f 'inline', 'required pair: derived from the list, optional excluded', ($RequiredPairColumns -join ', ')) -ForegroundColor Red }
    foreach ($missing in $RequiredPairColumns) {
        $specs = @(@($RequiredPairColumns + @('sprk_regardingmatter')) | Where-Object { $_ -ne $missing } | ForEach-Object { [pscustomobject]@{ Name = $_ } })
        $got = @(Get-PickerRefusals '<form><tabs /></form>' 'sprk_event' $specs | Where-Object { $_.Code -eq 'PAIR_INCOMPLETE' })
        $wfs += @{ Name = "pair incomplete: $missing" }
        if ($got.Count -eq 1 -and $got[0].Detail -like "*lacks $missing*") { Write-Host ("  PASS  {0,-40} {1}" -f 'inline', "a table lacking $missing is refused, naming it") -ForegroundColor Green }
        else { $failures++; Write-Host ("  FAIL  {0,-40} {1} (got [{2}])" -f 'inline', "a table lacking $missing is refused, naming it", (($got | ForEach-Object { $_.Detail }) -join '; ')) -ForegroundColor Red }
    }
    $strayPath = Join-Path ([System.IO.Path]::GetTempPath()) ("filing-columns-stray-{0}.json" -f [guid]::NewGuid().ToString('N'))
    try {
        $strayJson = Get-Content -LiteralPath $FilingColumnsPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $strayJson.optionalPairTextColumns = @('sprk_regardingrecordnumbr')
        $strayJson | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $strayPath -Encoding UTF8
        $strayMsg = $null
        try { [void](Read-FilingColumns $strayPath) } catch { $strayMsg = $_.Exception.Message }
    }
    finally { Remove-Item -LiteralPath $strayPath -ErrorAction SilentlyContinue }
    $wfs += @{ Name = 'stray optional column refused' }
    if ($strayMsg -like '*optionalPairTextColumns names sprk_regardingrecordnumbr*') { Write-Host ("  PASS  {0,-40} {1}" -f 'inline', 'an optional column outside pairTextColumns is refused, naming it') -ForegroundColor Green }
    else { $failures++; Write-Host ("  FAIL  {0,-40} {1} (got '{2}')" -f 'inline', 'an optional column outside pairTextColumns is refused, naming it', $strayMsg) -ForegroundColor Red }
    if ($failures -gt 0) { Write-Host "`nSELF-TEST FAIL: $failures case(s)." -ForegroundColor Red; exit 1 }
    Write-Host "`nSELF-TEST PASS: $($cases.Count) fixture case(s) + $($pairs.Count + $libs.Count + $wfs.Count) inline check(s)." -ForegroundColor Green
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
    # A lookup per the shared list's lookupTypes (the AttributeType names), as the grid customizer reads them.
    return @($attrs.value | Where-Object {
            ($LookupTypes -ccontains [string]$_.AttributeType -and ([string]$_.LogicalName).ToLowerInvariant().StartsWith($LookupPrefix, [System.StringComparison]::Ordinal)) -or
            ($PairTextColumns -contains ([string]$_.LogicalName).ToLowerInvariant())
        } | ForEach-Object {
            $kind = if ($LookupTypes -ccontains [string]$_.AttributeType) { 'Lookup' } elseif ($_.LogicalName -eq 'sprk_regardingrecordurl') { 'Url' } else { 'Text' }
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
    $libCache = @{}
    foreach ($formId in $Forms) {
        $f = Invoke-Dv -Endpoint "systemforms($formId)?`$select=formid,name,type,objecttypecode,ismanaged,formxml" -AllowNotFound
        if ($null -eq $f) { $refusals.Add(@{ Code = 'FORM_NOT_FOUND'; Where = $formId; Detail = 'no such systemform in this environment' }); continue }
        $table = [string]$f.objecttypecode
        $where = "$table form '$($f.name)' ($($f.formid)) type $($f.type)"
        Write-Step $where
        if ($ChildTables -notcontains $table) { $refusals.Add(@{ Code = 'FORM_NOT_FOUND'; Where = $where; Detail = "not a form of $($ChildTables -join ', ')" }); continue }
        if (-not $specCache.ContainsKey($table)) {
            $specCache[$table] = Get-LiveColumnSpecs $table
            # Business rules and process flows that could show or expose a raw filing column (once per table,
            # whatever the form's own state): WORKFLOW_REFERENCE is a refusal AND a -Verify gap (verifier item 1).
            $wf = Invoke-Dv -Endpoint "workflows?`$select=workflowid,name,category,primaryentity,ismanaged,xaml,clientdata&`$filter=(category eq 2 and primaryentity eq '$table') or category eq 4"
            $wfRows = @($wf.value)
            $wfRefusals = @(Get-WorkflowRefusals $wfRows $table (Get-RawFilingColumnNames $specCache[$table]))
            foreach ($r in $wfRefusals) {
                $refusals.Add(@{ Code = $r.Code; Where = $r.Where; Detail = $r.Detail })
                $gaps.Add("$($r.Where) : references raw filing column(s) $($r.Columns -join ', '), so it could show or expose a hidden raw control")
            }
            Write-Info "$table : read $($wfRows.Count) business rule(s) of the table and process flow(s) of the environment; WORKFLOW_REFERENCE: $($wfRefusals.Count)"
        }
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
        foreach ($r in $state.RawVisible) { $gaps.Add("$where : raw filing control '$($r.Id)' ($($r.Column)) is VISIBLE") }

        # Form libraries that could re-show a hidden raw control at runtime (the presave is scanned too: it is
        # added to every target form). The raw columns are this table's, whether or not the form has them yet.
        $rawColumns = @(Get-RawFilingColumnNames $specs)
        $libNames = @(@($doc.SelectNodes('/form/formLibraries/Library') | ForEach-Object { $_.GetAttribute('name') }) + @($PresaveLibrary) | Where-Object { $_ } | Sort-Object -Unique)
        foreach ($ln in $libNames) {
            if (-not $libCache.ContainsKey($ln)) {
                $lw = Invoke-Dv -Endpoint "webresourceset?`$select=name,content&`$filter=name eq '$($ln -replace "'", "''")'"
                if (@($lw.value).Count -eq 0) { throw "form library '$ln' (used by $where) was not found" }
                $libCache[$ln] = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String([string]$lw.value[0].content))
            }
            if (Test-LibraryShows $libCache[$ln] $rawColumns) {
                $refusals.Add(@{ Code = 'LIBRARY_SHOWS'; Where = "$where library '$ln'"; Detail = 'names a raw filing column and calls setVisible, so it could re-show a hidden raw control (owner decision)' })
            }
        }

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
        Write-Host "`nVERIFY PASS: every target form hosts the RegardingResolver for its table, registers the presave, carries every pair and lookup column, and shows no raw filing control; no business rule or process flow of its table references one." -ForegroundColor Green
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
    if (-not (Test-FilingComplete $state)) { $incomplete += "form '$($sf.name)' ($($sf.formid))" }
}
$snapshot | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $SnapshotPath -Encoding utf8
Write-Done "snapshot updated with the stored form XML: $SnapshotPath"
if ($incomplete.Count -gt 0) { throw "Read-back shows incomplete form(s) after the apply: $($incomplete -join '; '). Restore with -RestoreFrom '$SnapshotPath'." }

Write-Host ""
Write-Host "Updated $written form(s). Snapshot: $SnapshotPath" -ForegroundColor Green
Write-Host "Next: -Verify (must exit 0), then Lock-CoreAncestorStampColumnsOnForms.ps1 dry run / -Apply / -Verify." -ForegroundColor Green
exit 0
