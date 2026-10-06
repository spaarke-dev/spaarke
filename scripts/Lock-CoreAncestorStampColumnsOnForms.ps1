<#
.SYNOPSIS
    Makes the four sprk_regarding{core} root columns READ-ONLY on the to-do, event, communication and analysis
    forms (owner round 8 item 3). DRY RUN by default; -Verify checks the lock at any time.

.DESCRIPTION
    Owner round 8 item 3 (unified-access-control-r2, 2026-10-03, binding): keep task 156's rule (the regarding
    pair decides; a root column on a filed row is a COPY) AND remove the form input that lets a person type a
    root directly onto a filed row. Every form control bound to sprk_regardingproject, sprk_regardingmatter,
    sprk_regardingworkassignment or sprk_regardingservicerequest on sprk_todo, sprk_event, sprk_communication
    and sprk_analysis gets disabled="true". No control is removed: the presave bridge and the RegardingResolver
    stage values onto these attributes on CREATE (sprk_todo_regarding_presave.js header), and getAttribute
    returns null for a column that is not on the form. The presave (v1.4.0) sets the submit mode of every
    lookup it stages to "always", so a disabled control cannot drop a staged value from the INSERT.

    The repo holds no source for these forms, so this script IS the durable record of the change (the same
    convention as matter-analyses-tab.xml and scripts/Retire-CommunicationAccessPermission.ps1): a Web API
    systemforms PATCH of formxml, then PublishXml for the table.

    Scope: every systemform of the scanned tables with type 2 (Main), 7 (Quick Create) or 12 (Main -
    Interactive experience), whatever its activation state. A control matches when its datafieldname equals one
    of the four columns, compared case-insensitively and as a WHOLE value (sprk_regardingmatterstatus is not a
    match), and the column exists on the table (read live). Hidden and header controls are included. A form of
    any other type with such a control is reported, never edited.

    The transform is a PURE string function over the formxml: it edits the text of each targeted <control>
    start tag only (adds disabled="true", or rewrites a disabled="false" to "true"; an existing "true" is left
    alone) and never re-serializes the document. The result is then parsed, and a node-by-node comparison
    proves that nothing but the targeted controls' disabled attribute changed.

    Fail closed (ADR-003). Each of these REFUSES (exit 2), names the form or component, and writes nothing:
      MANAGED_FORM            a managed form with a control bound to a locked column;
      HOSTED_CUSTOM_CONTROL   a locked-column control that hosts a custom control (a controlDescription whose
                              forControl is the control's uniqueid and that names a customControl);
      BOUND_PARAMETER         a named custom control anywhere on the form with a locked column as a parameter
                              value (a PCF that could write the column through another control);
      NO_FILING_PICKER        (task 168 escalation trigger 1) a VISIBLE, EDITABLE locked-column control on a
                              form that hosts neither the RegardingResolver nor CommunicationConnections:
                              locking it would remove the only form input that files the record under a root.
                              Owner round 19 item 1: Add-RegardingFilingPickerToForms.ps1 adds the picker first;
      PICKER_WITHOUT_PRESAVE  (trigger 3) a form that hosts the RegardingResolver AND a locked-column control
                              but does not register Spaarke.SmartTodo.RegardingPreSave.onLoad as an ENABLED
                              handler of the FORM-LEVEL OnLoad event (/form/events; a cell's own nested
                              <events>, another OnLoad handler, or the handler on another event does not count);
      FORM_UNREADABLE         an in-scope form (type 2, 7 or 12) whose formxml comes back empty;
      WORKFLOW_REFERENCE      a business rule (workflow category 2) or business process flow (category 4)
                              whose xaml or clientdata references a locked column;
      LIBRARY_UNLOCKS         a form library whose content names a locked column AND calls setDisabled;
      TRANSFORM_INCOMPLETE    a transform result in which a locked-column control is still not disabled;
      TRANSFORM_PARSE         a transform result that is not well-formed or changed anything else;
      NO_MAIN_FORM            a scanned table with zero Main (type 2) forms (a scan that sees no main form
                              proves nothing);
      FORM_CHANGED            (-Apply) a form whose formxml changed between the scan and its PATCH.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Default: https://spaarkedev1.crm.dynamics.com

.PARAMETER Apply
    Perform the writes. Without a mode switch the script is a READ-ONLY dry run that prints, per form, the
    controls it would lock and every refusal it finds.

.PARAMETER Verify
    Read-only check. Exit 0 only when every control bound to an existing locked column on every in-scope form
    is disabled AND no refusal case is present; otherwise exit 1 naming each gap. Any read fault is a FAILED
    check (exit 1), never "0 unlocked controls".

.PARAMETER RestoreFrom
    Path to a snapshot written by -Apply. Puts each form's "before" formxml back and publishes each table.
    Refuses (exit 2, nothing written) if the snapshot's environment URL differs from -EnvironmentUrl, or if any
    form's CURRENT formxml no longer hashes to the snapshot's "after" hash (the form changed after the apply,
    for example by task 138's retirement script, and a restore would undo that change).

.PARAMETER SelfTest
    Offline (no token, no network): runs the pure transform and the pure refusal checks over every fixture
    under tests/fixtures/form-lock-core-ancestor/, prints one line per case, exits 1 on any mismatch.

.PARAMETER SnapshotPath
    -Apply only. Where to write the snapshot. Default: form-lock-snapshot-yyyyMMddHHmmss.json in the current
    directory.

.PARAMETER Tables
    Tables to scan. Default: the four child tables. -Apply refuses any table outside those four (the dry run and
    -Verify are read-only and accept any table).

.PARAMETER FixturePath
    -SelfTest only. Default: tests/fixtures/form-lock-core-ancestor next to this repository's scripts folder.

.PARAMETER FilingColumnsPath
    The ONE list of the regarding filing columns (owner round 38): its rootColumns are the locked columns.
    Default: config/regarding-filing-columns.json at the repository root. Add-RegardingFilingPickerToForms.ps1 and
    the SpaarkeGridCustomizer PCF read the same file; an unreadable or incomplete file stops the script.

.EXAMPLE
    .\Lock-CoreAncestorStampColumnsOnForms.ps1                          # dry run: plan + refusals, no writes
    .\Lock-CoreAncestorStampColumnsOnForms.ps1 -SelfTest                # offline fixture run
    .\Lock-CoreAncestorStampColumnsOnForms.ps1 -Verify                  # read-only lock check (exit 0 / 1)
    .\Lock-CoreAncestorStampColumnsOnForms.ps1 -Apply                   # snapshot, PATCH, publish
    .\Lock-CoreAncestorStampColumnsOnForms.ps1 -Apply -Tables sprk_todo # one table only
    .\Lock-CoreAncestorStampColumnsOnForms.ps1 -RestoreFrom .\form-lock-snapshot-20261003120000.json

.NOTES
    Project : unified-access-control-r2
    Task    : 168 (#1107) — lock the root columns on the child forms (owner round 8 item 3)
    Created : 2026-10-03
    Docs    : projects/unified-access-control-r2/notes/task-168-lock-root-columns-on-forms.md
              docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md (registry row I-1)

    OPERATOR-RUN ONLY. The task agent ran the dry run, -Verify and -SelfTest; -Apply and -RestoreFrom are the
    main session's manual gate (task 168 step 7), AFTER the presave v1.4.0 is deployed. No other formxml-writing
    script (task 138's Retire-CommunicationAccessPermission.ps1, Deploy-TodoSubgridsToElevenParentForms.ps1) may
    run against the same environment at the same time: each reads a form's XML and writes the whole document.
    Requires Azure CLI (`az login`) with a principal that can customize the environment. PowerShell 7+.

    Exit codes: 0 = done / nothing to do / dry run complete / VERIFY PASS / SELF-TEST PASS;
                2 = refused (see the printed reason; nothing was written);
                1 = VERIFY FAIL, SELF-TEST FAIL, or an unexpected error.
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
    [string[]]$Tables = @('sprk_todo', 'sprk_event', 'sprk_communication', 'sprk_analysis'),

    [Parameter(Mandatory = $false)]
    [string]$FixturePath = (Join-Path $PSScriptRoot '..' 'tests' 'fixtures' 'form-lock-core-ancestor'),

    [Parameter(Mandatory = $false)]
    [string]$FilingColumnsPath = (Join-Path $PSScriptRoot '..' 'config' 'regarding-filing-columns.json')
)

$ErrorActionPreference = "Stop"

# Modes are exclusive. Checked before ANY read.
$modeCount = @($Apply.IsPresent, $Verify.IsPresent, (-not [string]::IsNullOrEmpty($RestoreFrom)), $SelfTest.IsPresent) |
    Where-Object { $_ } | Measure-Object | Select-Object -ExpandProperty Count
if ($modeCount -gt 1) {
    throw "-Apply, -Verify, -RestoreFrom and -SelfTest are separate modes; pass at most one."
}

# ============================================================================
# Constants
# ============================================================================

# CoreAncestorResolver.CoreAncestorLookups (task 156): the four core-ancestor lookups. Read from the ONE list of the
# regarding filing columns (owner round 38), never a second copy here.
function Read-FilingColumnRoots([string]$Path) {
    try { $j = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json }
    catch { throw "FILING_COLUMNS: '$Path' could not be read as JSON: $($_.Exception.Message)" }
    $roots = @($j.rootColumns | ForEach-Object { "$_".Trim().ToLowerInvariant() } | Where-Object { $_ })
    if ($roots.Count -eq 0) { throw "FILING_COLUMNS: '$Path' names no rootColumns" }
    return $roots
}
$LockedColumns = @(Read-FilingColumnRoots $FilingColumnsPath)
# CoreAncestorResolver.StampSourceColumns: the only tables that carry a stamp copy.
$ChildTables = @('sprk_todo', 'sprk_event', 'sprk_communication', 'sprk_analysis')
$InScopeFormTypes = @(2, 7, 12)
$FilingPickerPattern = '(?i)(^|_)Spaarke\.Controls\.(RegardingResolver|CommunicationConnections)$'
$RegardingResolverPattern = '(?i)(^|_)Spaarke\.Controls\.RegardingResolver$'
$PresaveOnLoad = 'Spaarke.SmartTodo.RegardingPreSave.onLoad'

# A whole <control ...> start tag (quoted attribute values may contain '>').
$ControlTagPattern = '<control\b(?:[^>"'']|"[^"]*"|''[^'']*'')*?/?>'

# ============================================================================
# Pure functions (no I/O) — exercised offline by -SelfTest
# ============================================================================

function Get-TagAttribute([string]$Tag, [string]$Name) {
    $m = [regex]::Match($Tag, "(?<=\s)$([regex]::Escape($Name))\s*=\s*(?:""([^""]*)""|'([^']*)')", 'IgnoreCase')
    if (-not $m.Success) { return $null }
    if ($m.Groups[1].Success) { return $m.Groups[1].Value }
    return $m.Groups[2].Value
}

function Test-IsLockedColumn([string]$Value, [string[]]$Columns) {
    if ([string]::IsNullOrEmpty($Value)) { return $false }
    foreach ($c in $Columns) {
        # Whole value, case-insensitive: sprk_RegardingMatter matches; sprk_regardingmatterstatus does not.
        if ([string]::Equals($Value.Trim(), $c, [System.StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

function Set-ControlTagDisabled([string]$Tag) {
    $m = [regex]::Match($Tag, '(?<=\s)disabled\s*=\s*(?:"([^"]*)"|''([^'']*)'')', 'IgnoreCase')
    if ($m.Success) {
        $value = if ($m.Groups[1].Success) { $m.Groups[1].Value } else { $m.Groups[2].Value }
        if ($value.Trim() -ieq 'true') { return $Tag }   # already locked: byte-identical (idempotent)
        return $Tag.Substring(0, $m.Index) + 'disabled="true"' + $Tag.Substring($m.Index + $m.Length)
    }
    # No disabled attribute: add one after the last attribute, keeping the tag's own closing whitespace.
    $close = [regex]::Match($Tag, '\s*/?>$')
    return $Tag.Substring(0, $close.Index) + ' disabled="true"' + $Tag.Substring($close.Index)
}

<#
    The transform. Edits only the text of each targeted <control> start tag; every other byte is unchanged.
    Returns @{ Xml; Locked = @(control ids newly locked); AlreadyLocked = @(ids) }.
#>
function Lock-FormXml([string]$FormXml, [string[]]$Columns) {
    $locked = [System.Collections.Generic.List[string]]::new()
    $already = [System.Collections.Generic.List[string]]::new()
    $sb = [System.Text.StringBuilder]::new()
    $pos = 0
    foreach ($m in [regex]::Matches($FormXml, $ControlTagPattern)) {
        $tag = $m.Value
        if (-not (Test-IsLockedColumn (Get-TagAttribute $tag 'datafieldname') $Columns)) { continue }
        $newTag = Set-ControlTagDisabled $tag
        $id = Get-TagAttribute $tag 'id'
        if ($newTag -ceq $tag) { $already.Add($id) } else { $locked.Add($id) }
        [void]$sb.Append($FormXml, $pos, $m.Index - $pos)
        [void]$sb.Append($newTag)
        $pos = $m.Index + $m.Length
    }
    [void]$sb.Append($FormXml, $pos, $FormXml.Length - $pos)
    return @{ Xml = $sb.ToString(); Locked = @($locked); AlreadyLocked = @($already) }
}

function ConvertTo-FormDocument([string]$FormXml) {
    $doc = [System.Xml.XmlDocument]::new()
    $doc.PreserveWhitespace = $true
    $doc.XmlResolver = $null
    $doc.LoadXml($FormXml)
    return $doc
}

function Test-ElementHidden([System.Xml.XmlNode]$Node) {
    # Accessor methods, not properties: see the note in Test-LockTransform.
    $n = $Node.get_ParentNode()
    while ($null -ne $n -and $n.get_NodeType() -eq [System.Xml.XmlNodeType]::Element) {
        $v = $n.GetAttribute('visible')
        if ($v -and $v.Trim() -ieq 'false') { return $true }
        $n = $n.get_ParentNode()
    }
    return $false
}

<#
    Locked-column controls on a parsed form: id, hidden, disabled, uniqueid, header.
#>
function Get-LockedControls([System.Xml.XmlDocument]$Doc, [string[]]$Columns) {
    $out = @()
    foreach ($c in $Doc.SelectNodes('//control')) {
        if (-not (Test-IsLockedColumn $c.GetAttribute('datafieldname') $Columns)) { continue }
        $d = $c.GetAttribute('disabled')
        $out += [pscustomobject]@{
            Id       = $c.GetAttribute('id')
            Column   = $c.GetAttribute('datafieldname')
            UniqueId = $c.GetAttribute('uniqueid')
            Hidden   = (Test-ElementHidden $c)
            Disabled = ($d -and $d.Trim() -ieq 'true')
            Header   = $c.GetAttribute('id').StartsWith('header_', [System.StringComparison]::OrdinalIgnoreCase)
        }
    }
    return $out
}

function Get-NamedCustomControls([System.Xml.XmlNode]$Scope) {
    return @($Scope.SelectNodes('.//customControl') | Where-Object { -not [string]::IsNullOrEmpty($_.GetAttribute('name')) })
}

<#
    The pure refusal checks over one form. Returns a list of @{ Code; Detail }.
    $FormType is the systemform type; $IsManaged its managed flag.
#>
function Get-FormRefusals([string]$FormXml, [string[]]$Columns, [int]$FormType = 2, [bool]$IsManaged = $false) {
    $refusals = @()
    try { $doc = ConvertTo-FormDocument $FormXml }
    catch { return @(@{ Code = 'TRANSFORM_PARSE'; Detail = "the form XML is not well-formed: $($_.Exception.Message)" }) }

    $controls = @(Get-LockedControls $doc $Columns)
    $descriptions = @($doc.SelectNodes('//controlDescription'))

    $unlocked = @($controls | Where-Object { -not $_.Disabled })
    if ($IsManaged -and $unlocked.Count -gt 0) {
        $refusals += @{ Code = 'MANAGED_FORM'; Detail = "managed form with $($unlocked.Count) unlocked locked-column control(s); change it in its owning solution (task 168 escalation trigger 4)" }
    }

    foreach ($c in $controls) {
        if ([string]::IsNullOrEmpty($c.UniqueId)) { continue }
        foreach ($cd in $descriptions) {
            $for = $cd.GetAttribute('forControl')
            if (-not [string]::Equals($for, $c.UniqueId, [System.StringComparison]::OrdinalIgnoreCase)) { continue }
            $named = @(Get-NamedCustomControls $cd | ForEach-Object { $_.GetAttribute('name') } | Sort-Object -Unique)
            if ($named.Count -gt 0) {
                $refusals += @{ Code = 'HOSTED_CUSTOM_CONTROL'; Detail = "control '$($c.Id)' ($($c.Column)) hosts custom control $($named -join ', ')" }
            }
        }
    }

    foreach ($cd in $descriptions) {
        foreach ($cc in (Get-NamedCustomControls $cd)) {
            foreach ($p in $cc.SelectNodes('.//parameters//*')) {
                $kids = $p.get_ChildNodes()
                if ($kids.get_Count() -eq 1 -and $kids.Item(0).get_NodeType() -eq [System.Xml.XmlNodeType]::Text -and
                    (Test-IsLockedColumn $p.get_InnerText() $Columns)) {
                    $refusals += @{ Code = 'BOUND_PARAMETER'; Detail = "custom control $($cc.GetAttribute('name')) (forControl $($cd.GetAttribute('forControl'))) names $($p.get_InnerText()) as parameter '$($p.get_LocalName())'" }
                }
            }
        }
    }

    if ($InScopeFormTypes -contains $FormType) {
        $pickers = @(Get-NamedCustomControls $doc | Where-Object { $_.GetAttribute('name') -match $FilingPickerPattern })
        $resolvers = @($pickers | Where-Object { $_.GetAttribute('name') -match $RegardingResolverPattern })
        $openVisible = @($controls | Where-Object { -not $_.Hidden -and -not $_.Disabled })
        if ($openVisible.Count -gt 0 -and $pickers.Count -eq 0) {
            $refusals += @{ Code = 'NO_FILING_PICKER'; Detail = "visible editable control(s) $(($openVisible | ForEach-Object { $_.Id }) -join ', ') and no RegardingResolver or CommunicationConnections on the form (task 168 trigger 1; owner round 19 item 1: run Add-RegardingFilingPickerToForms.ps1 first)" }
        }
        if ($resolvers.Count -gt 0 -and $controls.Count -gt 0) {
            # FORM-LEVEL OnLoad only (/form/events, the path Add-RegardingFilingPickerToForms.ps1 uses): a cell may
            # carry its own nested <events> (the live Analysis main form's web-resource cell does), and a handler
            # there is not a form OnLoad registration (task 168 f1, verifier item 5; fixture 15 pins it).
            $presave = @($doc.SelectNodes('/form/events/event') | Where-Object { $_.GetAttribute('name') -ieq 'onload' } |
                ForEach-Object { $_.SelectNodes('.//Handler') } |
                Where-Object { $_.GetAttribute('functionName') -ceq $PresaveOnLoad -and $_.GetAttribute('enabled') -ine 'false' })
            if ($presave.Count -eq 0) {
                $refusals += @{ Code = 'PICKER_WITHOUT_PRESAVE'; Detail = "the form hosts the RegardingResolver and locked-column control(s) but does not register $PresaveOnLoad (task 168 trigger 3; owner round 19 item 2: run Add-RegardingFilingPickerToForms.ps1 first)" }
            }
        }
    }
    return $refusals
}

<#
    Parse check: the result is well-formed and differs from the input ONLY in the disabled attribute of the
    locked-column controls, each of which is now disabled="true". Returns a list of problems (empty = OK).
#>
function Test-LockTransform([string]$Before, [string]$After, [string[]]$Columns) {
    $problems = [System.Collections.Generic.List[string]]::new()
    try { $a = ConvertTo-FormDocument $Before } catch { $problems.Add("input not well-formed: $($_.Exception.Message)"); return @($problems) }
    try { $b = ConvertTo-FormDocument $After } catch { $problems.Add("TRANSFORM_PARSE: result not well-formed: $($_.Exception.Message)"); return @($problems) }

    # NB: .NET accessor METHODS throughout (get_LocalName(), get_ChildNodes() ...). PowerShell's XML adapter lets
    # an attribute or child element shadow a property of the same name — `$section.Name` returns the section's
    # name="..." ATTRIBUTE, not "section" — so property syntax would compare the wrong things.
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
        $targeted = ($xName -ceq 'control') -and (Test-IsLockedColumn $x.GetAttribute('datafieldname') $Columns)
        # Ordinal (case-sensitive) maps: a PowerShell @{} would fold attribute-name case.
        $ax = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
        $ay = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
        foreach ($at in $x.get_Attributes()) { $ax[$at.get_Name()] = $at.get_Value() }
        foreach ($at in $y.get_Attributes()) { $ay[$at.get_Name()] = $at.get_Value() }
        foreach ($k in (@($ax.Keys) + @($ay.Keys) | Sort-Object -Unique -CaseSensitive)) {
            if ($targeted -and $k -ceq 'disabled') { continue }
            if (-not $ax.ContainsKey($k) -or -not $ay.ContainsKey($k) -or $ax[$k] -cne $ay[$k]) {
                $problems.Add("TRANSFORM_PARSE: attribute '$k' changed on <$xName id='$($x.GetAttribute('id'))'>")
            }
        }
        if ($targeted) {
            $dis = $y.GetAttribute('disabled')
            if (-not ($dis -and $dis.Trim() -ieq 'true')) { $problems.Add("TRANSFORM_INCOMPLETE: control '$($y.GetAttribute('id'))' is still not disabled") }
            if (@($y.get_Attributes() | Where-Object { $_.get_Name() -ieq 'disabled' }).Count -gt 1) { $problems.Add("TRANSFORM_PARSE: duplicate disabled on '$($y.GetAttribute('id'))'") }
        }
        $xc = $x.get_ChildNodes(); $yc = $y.get_ChildNodes()
        if ($xc.get_Count() -ne $yc.get_Count()) { $problems.Add("TRANSFORM_PARSE: child count changed under <$xName>"); continue }
        for ($i = 0; $i -lt $xc.get_Count(); $i++) { $stack.Push(@($xc.Item($i), $yc.Item($i))) }
    }
    return @($problems)
}

<#
    The parse check as refusals: each problem from Test-LockTransform becomes TRANSFORM_INCOMPLETE (a targeted
    control left enabled) or TRANSFORM_PARSE (anything else). Pure; the live scan and -SelfTest both use it, so the
    code mapping a live run refuses on is the one -SelfTest proves.
#>
function Get-TransformRefusals([string]$Before, [string]$After, [string[]]$Columns) {
    $out = @()
    foreach ($p in (Test-LockTransform $Before $After $Columns)) {
        $code = if ($p -like 'TRANSFORM_INCOMPLETE*') { 'TRANSFORM_INCOMPLETE' } else { 'TRANSFORM_PARSE' }
        $out += @{ Code = $code; Detail = $p }
    }
    return $out
}

<#
    A form the scan cannot read. An in-scope form (type 2, 7 or 12) whose formxml comes back empty is a refusal
    (FORM_UNREADABLE), so -Verify fails and -Apply refuses: an unread form proves nothing about its controls
    (ADR-003; POML: "any fault while reading forms ... is a FAILED check"). A form of another type is only
    reported, as it would be with XML. Returns @{ Code; Detail } or $null. Pure; -SelfTest pins it.
#>
function Get-FormReadRefusal([string]$FormXml, [int]$FormType) {
    if (-not [string]::IsNullOrWhiteSpace($FormXml)) { return $null }
    if ($InScopeFormTypes -notcontains $FormType) { return $null }
    return @{ Code = 'FORM_UNREADABLE'; Detail = "type $FormType form returned no formxml; an unread form proves nothing (task 168 f1, verifier item 7)" }
}

function Test-WorkflowReferencesLocked([string]$Xaml, [string]$ClientData, [string[]]$Columns) {
    $text = "$Xaml`n$ClientData"
    foreach ($c in $Columns) {
        if ([regex]::IsMatch($text, "(?<![A-Za-z0-9_])$([regex]::Escape($c))(?![A-Za-z0-9_])", 'IgnoreCase')) { return $true }
    }
    return $false
}

function Test-LibraryUnlocks([string]$Content, [string[]]$Columns) {
    if ([string]::IsNullOrEmpty($Content) -or $Content -notmatch 'setDisabled') { return $false }
    foreach ($c in $Columns) {
        if ($Content.IndexOf($c, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) { return $true }
    }
    return $false
}

function Get-Sha256([string]$Text) {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
    return [System.Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

# ============================================================================
# -SelfTest (offline)
# ============================================================================

if ($SelfTest) {
    Write-Host "Lock-CoreAncestorStampColumnsOnForms  —  SELF-TEST (offline; no token, no network)" -ForegroundColor White
    $root = (Resolve-Path -LiteralPath $FixturePath).Path
    $cases = @(Get-ChildItem -LiteralPath $root -Directory | Sort-Object Name)
    if ($cases.Count -eq 0) { Write-Host "SELF-TEST FAIL: no fixture cases under $root" -ForegroundColor Red; exit 1 }
    $failures = 0
    foreach ($case in $cases) {
        $inputPath = Join-Path $case.FullName 'input.xml'
        $expectedPath = Join-Path $case.FullName 'expected.xml'
        $refusalPath = Join-Path $case.FullName 'expected-refusal.txt'
        $metaPath = Join-Path $case.FullName 'form-type.txt'
        $formType = if (Test-Path -LiteralPath $metaPath) { [int](Get-Content -LiteralPath $metaPath -Raw).Trim() } else { 2 }
        # managed.txt ("true"/"false") stands in for systemform.ismanaged, so MANAGED_FORM is proven offline too.
        $managedPath = Join-Path $case.FullName 'managed.txt'
        $isManaged = (Test-Path -LiteralPath $managedPath) -and ((Get-Content -LiteralPath $managedPath -Raw).Trim() -ieq 'true')
        $in = [System.IO.File]::ReadAllText($inputPath)
        $refusals = @(Get-FormRefusals $in $LockedColumns $formType $isManaged)
        $result = Lock-FormXml $in $LockedColumns
        $problems = @(Test-LockTransform $in $result.Xml $LockedColumns)
        $ok = $true; $why = ''
        if (Test-Path -LiteralPath $refusalPath) {
            $want = (Get-Content -LiteralPath $refusalPath -Raw).Trim()
            $got = @($refusals | ForEach-Object { $_.Code })
            if ($got -notcontains $want) { $ok = $false; $why = "expected refusal $want, got [$($got -join ',')]" }
            else { $why = "refused $want" }
        }
        else {
            $expected = [System.IO.File]::ReadAllText($expectedPath)
            $again = (Lock-FormXml $result.Xml $LockedColumns).Xml
            if ($refusals.Count -gt 0) { $ok = $false; $why = "unexpected refusal(s): $(($refusals | ForEach-Object { $_.Code }) -join ',')" }
            elseif ($problems.Count -gt 0) { $ok = $false; $why = "parse check: $($problems -join '; ')" }
            elseif ($result.Xml -cne $expected) { $ok = $false; $why = "output differs from expected.xml" }
            elseif ($again -cne $result.Xml) { $ok = $false; $why = "not idempotent: a second transform changed the output" }
            elseif ($result.Locked.Count -eq 0) { $why = "nothing to do (unchanged)" }
            else { $why = "locked $($result.Locked -join ', ')" }
        }
        if ($ok) { Write-Host ("  PASS  {0,-44} {1}" -f $case.Name, $why) -ForegroundColor Green }
        else { $failures++; Write-Host ("  FAIL  {0,-44} {1}" -f $case.Name, $why) -ForegroundColor Red }
    }
    # The two non-form pure checks, inline.
    $inline = @(
        @{ Name = 'library: setDisabled + locked column'; Got = (Test-LibraryUnlocks 'formContext.getControl("sprk_regardingmatter").setDisabled(false);' $LockedColumns); Want = $true },
        @{ Name = 'library: locked column, no setDisabled'; Got = (Test-LibraryUnlocks 'attr = getAttribute("sprk_regardingmatter");' $LockedColumns); Want = $false },
        @{ Name = 'library: setDisabled, other column'; Got = (Test-LibraryUnlocks 'getControl("sprk_regardingrecordtype").setDisabled(true);' $LockedColumns); Want = $false },
        @{ Name = 'workflow: references sprk_RegardingProject'; Got = (Test-WorkflowReferencesLocked '<Set Field="sprk_RegardingProject"/>' '' $LockedColumns); Want = $true },
        @{ Name = 'workflow: near-miss sprk_regardingmatterstatus'; Got = (Test-WorkflowReferencesLocked '' '{"f":"sprk_regardingmatterstatus"}' $LockedColumns); Want = $false }
    )
    foreach ($t in $inline) {
        if ($t.Got -eq $t.Want) { Write-Host ("  PASS  {0,-44} {1}" -f 'inline', $t.Name) -ForegroundColor Green }
        else { $failures++; Write-Host ("  FAIL  {0,-44} {1} (got {2})" -f 'inline', $t.Name, $t.Got) -ForegroundColor Red }
    }
    # The parse check on its own (task 168 r1, verifier item 2). Every non-refusal fixture is ALSO compared
    # byte-for-byte with expected.xml, so the fixtures alone never prove that the parse check bites. In a live run
    # there is no expected.xml and the parse check is the only guard against a transform bug, so each refusal is
    # pinned here on a crafted before/after pair, through the same Get-TransformRefusals a live run uses.
    $pb = '<form><tabs><tab name="t"><labels><label description="General" /></labels><note>abc</note>' +
          '<control id="sprk_regardingmatter" datafieldname="sprk_regardingmatter" />' +
          '<control id="other" datafieldname="sprk_name" visible="true" /></tab></tabs></form>'
    $pLocked = $pb.Replace('datafieldname="sprk_regardingmatter" />', 'datafieldname="sprk_regardingmatter" disabled="true" />')
    $pairs = @(
        @{ Name = 'parse: a correct lock passes (positive control)'; After = $pLocked; Want = '' },
        @{ Name = 'parse: targeted control left enabled'; After = $pb; Want = 'TRANSFORM_INCOMPLETE' },
        @{ Name = 'parse: a non-target attribute changed'; After = $pLocked.Replace('visible="true"', 'visible="false"'); Want = 'TRANSFORM_PARSE' },
        @{ Name = 'parse: an attribute added to a non-target'; After = $pLocked.Replace('id="other"', 'id="other" disabled="true"'); Want = 'TRANSFORM_PARSE' },
        @{ Name = 'parse: text changed'; After = $pLocked.Replace('<note>abc</note>', '<note>abd</note>'); Want = 'TRANSFORM_PARSE' },
        @{ Name = 'parse: an element added'; After = $pLocked.Replace('</tab>', '<control id="extra" /></tab>'); Want = 'TRANSFORM_PARSE' },
        # A form that already carries a stray Disabled= (another attribute to XML): adding disabled="true" leaves
        # every other attribute unchanged, so only the duplicate check sees the second spelling.
        @{ Name = 'parse: duplicate disabled (disabled + Disabled)'; Before = $pb.Replace('datafieldname="sprk_regardingmatter" />', 'datafieldname="sprk_regardingmatter" Disabled="false" />'); After = $pb.Replace('datafieldname="sprk_regardingmatter" />', 'datafieldname="sprk_regardingmatter" Disabled="false" disabled="true" />'); Want = 'TRANSFORM_PARSE' },
        @{ Name = 'parse: result not well-formed'; After = $pLocked.Replace('</form>', ''); Want = 'TRANSFORM_PARSE' }
    )
    foreach ($t in $pairs) {
        $before = if ($t.ContainsKey('Before')) { $t.Before } else { $pb }
        $codes = @(Get-TransformRefusals $before $t.After $LockedColumns | ForEach-Object { $_.Code } | Sort-Object -Unique)
        $got = $codes -join ','
        if ($got -ceq $t.Want) { Write-Host ("  PASS  {0,-44} {1}" -f 'inline', $t.Name) -ForegroundColor Green }
        else { $failures++; Write-Host ("  FAIL  {0,-44} {1} (want [{2}], got [{3}])" -f 'inline', $t.Name, $t.Want, $got) -ForegroundColor Red }
    }
    $inline += $pairs
    # An in-scope form returned with no formxml (task 168 f1, verifier item 7): FORM_UNREADABLE for types 2, 7 and
    # 12, so -Verify cannot pass on a form it never read; another type is only reported.
    $reads = @(
        @{ Name = 'read: empty formxml, Main (2)'; Xml = ''; Type = 2; Want = 'FORM_UNREADABLE' },
        @{ Name = 'read: empty formxml, Quick Create (7)'; Xml = $null; Type = 7; Want = 'FORM_UNREADABLE' },
        @{ Name = 'read: blank formxml, Main - Interactive (12)'; Xml = "  `r`n"; Type = 12; Want = 'FORM_UNREADABLE' },
        @{ Name = 'read: empty formxml, Quick View (6) only reported'; Xml = ''; Type = 6; Want = '' },
        @{ Name = 'read: a Main form with XML (positive control)'; Xml = $pb; Type = 2; Want = '' }
    )
    foreach ($t in $reads) {
        $r = Get-FormReadRefusal $t.Xml $t.Type
        $got = if ($null -eq $r) { '' } else { $r.Code }
        if ($got -ceq $t.Want) { Write-Host ("  PASS  {0,-44} {1}" -f 'inline', $t.Name) -ForegroundColor Green }
        else { $failures++; Write-Host ("  FAIL  {0,-44} {1} (want [{2}], got [{3}])" -f 'inline', $t.Name, $t.Want, $got) -ForegroundColor Red }
    }
    $inline += $reads
    if ($failures -gt 0) { Write-Host "`nSELF-TEST FAIL: $failures case(s)." -ForegroundColor Red; exit 1 }
    Write-Host "`nSELF-TEST PASS: $($cases.Count) fixture case(s) + $($inline.Count) inline check(s)." -ForegroundColor Green
    exit 0
}

# ============================================================================
# Live helpers (copied from Retire-CommunicationAccessPermission.ps1)
# ============================================================================

$BaseUrl = $EnvironmentUrl.TrimEnd('/')
$Token = $null

function Get-DataverseToken {
    $tokenResult = az account get-access-token --resource $BaseUrl --query "accessToken" -o tsv 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to get a token from Azure CLI: $tokenResult. Run 'az login' first."
    }
    return "$tokenResult".Trim()
}

function Invoke-Dv {
    param(
        [string]$Endpoint,
        [string]$Method = "GET",
        [object]$Body = $null,
        [switch]$AllowNotFound
    )

    $headers = @{
        "Authorization"    = "Bearer $Token"
        "OData-MaxVersion" = "4.0"
        "OData-Version"    = "4.0"
        "Accept"           = "application/json"
        "Content-Type"     = "application/json; charset=utf-8"
    }
    $params = @{ Uri = "$BaseUrl/api/data/v9.2/$Endpoint"; Method = $Method; Headers = $headers }
    if ($null -ne $Body) { $params.Body = ($Body | ConvertTo-Json -Depth 20) }

    try {
        return Invoke-RestMethod @params
    }
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
    Write-Host "Nothing was changed. Resolve the reason (or obtain the owner decision it names), then re-run." -ForegroundColor Red
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

# ============================================================================
# -RestoreFrom (its own checks, then its writes)
# ============================================================================

if ($RestoreFrom) {
    Write-Host "Lock-CoreAncestorStampColumnsOnForms  —  RESTORE from $RestoreFrom" -ForegroundColor White
    Write-Host "Environment: $BaseUrl"
    if (-not (Test-Path -LiteralPath $RestoreFrom)) { Stop-Refused "snapshot '$RestoreFrom' does not exist." }
    try { $snap = Get-Content -LiteralPath $RestoreFrom -Raw -Encoding UTF8 | ConvertFrom-Json }
    catch { Stop-Refused "snapshot '$RestoreFrom' is not valid JSON: $($_.Exception.Message)" }
    if ("$($snap.environmentUrl)".TrimEnd('/') -ine $BaseUrl) {
        Stop-Refused "the snapshot was taken on '$($snap.environmentUrl)', not '$BaseUrl'."
    }
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
        $now = Get-FormXmlNow $f.formid
        $h = Get-Sha256 $now
        if ($h -eq $f.sha256Before) {
            # An apply stopped by FORM_CHANGED part-way never wrote this form: nothing to put back.
            Write-Info "form '$($f.name)' ($($f.formid)) still holds its 'before' XML — skipped"
            continue
        }
        # sha256Stored is the form as Dataverse returned it right after the apply (it may normalize what it is
        # given); a snapshot written before that re-read falls back to the hash of what was sent.
        $expected = if ($f.sha256Stored) { [string]$f.sha256Stored } else { [string]$f.sha256Written }
        if ($h -ne $expected) {
            Stop-Refused "form '$($f.name)' ($($f.formid), $($f.table)) changed after the apply (sha256 $h, expected $expected); restoring would undo that later change."
        }
        Write-Info "form '$($f.name)' ($($f.formid)) unchanged since the apply"
        $toRestore += $f
    }
    if ($toRestore.Count -eq 0) { Write-Done "no form in the snapshot was written — nothing to restore."; exit 0 }

    Write-Step "2. Restore"
    foreach ($f in $toRestore) {
        Invoke-Dv -Endpoint "systemforms($($f.formid))" -Method PATCH -Body @{ formxml = [string]$f.formXmlBefore } | Out-Null
        Write-Done "form '$($f.name)' ($($f.formid)) restored"
    }
    foreach ($t in @($toRestore | ForEach-Object { $_.table } | Sort-Object -Unique)) {
        Publish-Table $t
        Write-Done "published $t"
    }
    foreach ($f in $toRestore) {
        if ((Get-Sha256 (Get-FormXmlNow $f.formid)) -ne $f.sha256Before) {
            Write-Host "   WARN  form '$($f.name)' ($($f.formid)) does not hash to its snapshot 'before' after the restore (the platform may normalize form XML); run -Verify." -ForegroundColor Yellow
        }
    }
    Write-Host ""
    Write-Host "Restored $($toRestore.Count) form(s). Run -Verify: it should now exit 1 (controls unlocked again)." -ForegroundColor Green
    exit 0
}

# ============================================================================
# Scan (read-only) — dry run, -Verify and -Apply all start here
# ============================================================================

$Mode = if ($Apply) { "APPLY" } elseif ($Verify) { "VERIFY (read-only)" } else { "DRY RUN (read-only; pass -Apply to perform the writes)" }
Write-Host "Lock-CoreAncestorStampColumnsOnForms  —  $Mode" -ForegroundColor White
Write-Host "Environment: $BaseUrl"
Write-Host "Tables     : $($Tables -join ', ')"
Write-Host "Columns    : $($LockedColumns -join ', ')"

if ($Apply) {
    $outside = @($Tables | Where-Object { $ChildTables -notcontains $_.ToLowerInvariant() })
    if ($outside.Count -gt 0) { Stop-Refused "-Apply writes only the four child tables; not: $($outside -join ', ')." }
}

$refusals = [System.Collections.Generic.List[object]]::new()   # @{ Code; Where; Detail }
$gaps = [System.Collections.Generic.List[string]]::new()        # unlocked controls (Verify)
$edits = [System.Collections.Generic.List[object]]::new()       # forms to PATCH

try {
    $Token = Get-DataverseToken
    foreach ($table in $Tables) {
        Write-Step "Table $table"

        # Which locked columns exist here (live metadata; never assumed).
        $attrs = Invoke-Dv -Endpoint "EntityDefinitions(LogicalName='$table')/Attributes?`$select=LogicalName"
        $present = @($LockedColumns | Where-Object { $c = $_; @($attrs.value | Where-Object { $_.LogicalName -ieq $c }).Count -gt 0 })
        $absent = @($LockedColumns | Where-Object { $present -notcontains $_ })
        Write-Info "columns present: $(if ($present.Count) { $present -join ', ' } else { '(none)' })"
        if ($absent.Count -gt 0) { Write-Info "columns absent (reported, not an error): $($absent -join ', ')" }

        # Business rules and business process flows.
        $wf = Invoke-Dv -Endpoint "workflows?`$select=workflowid,name,category,ismanaged,xaml,clientdata&`$filter=(category eq 2 and primaryentity eq '$table') or category eq 4"
        $wfScanned = 0
        foreach ($w in $wf.value) {
            if ($w.category -eq 4 -and -not ("$($w.xaml)`n$($w.clientdata)" -match "(?i)(?<![A-Za-z0-9_])$table(?![A-Za-z0-9_])")) { continue }
            $wfScanned++
            if ($present.Count -gt 0 -and (Test-WorkflowReferencesLocked $w.xaml $w.clientdata $present)) {
                $kind = if ($w.category -eq 2) { 'business rule' } else { 'business process flow' }
                $refusals.Add(@{ Code = 'WORKFLOW_REFERENCE'; Where = "$table $kind '$($w.name)' ($($w.workflowid))"; Detail = 'references a locked column (task 168 escalation trigger 4)' })
            }
        }
        Write-Info "business rules / process flows scanned: $wfScanned"

        # Forms.
        $forms = Invoke-Dv -Endpoint "systemforms?`$select=formid,name,type,formactivationstate,ismanaged,formxml&`$filter=objecttypecode eq '$table'"
        $mainCount = @($forms.value | Where-Object { $_.type -eq 2 }).Count
        if ($mainCount -eq 0) {
            $refusals.Add(@{ Code = 'NO_MAIN_FORM'; Where = $table; Detail = 'zero Main (type 2) forms returned; a scan that sees no main form proves nothing' })
        }
        $libraries = @{}
        foreach ($f in $forms.value) {
            $xml = [string]$f.formxml
            $where = "$table form '$($f.name)' ($($f.formid)) type $($f.type)"
            $unreadable = Get-FormReadRefusal $xml ([int]$f.type)
            if ($null -ne $unreadable) { $refusals.Add(@{ Code = $unreadable.Code; Where = $where; Detail = $unreadable.Detail }); continue }
            if ([string]::IsNullOrWhiteSpace($xml)) { Write-Info "REPORT ONLY (type $($f.type) is out of scope): $where returned no formxml"; continue }
            try { $doc = ConvertTo-FormDocument $xml }
            catch { $refusals.Add(@{ Code = 'TRANSFORM_PARSE'; Where = $where; Detail = "live form XML is not well-formed: $($_.Exception.Message)" }); continue }
            $controls = @(Get-LockedControls $doc $present)

            if ($InScopeFormTypes -notcontains [int]$f.type) {
                if ($controls.Count -gt 0) { Write-Info "REPORT ONLY (type $($f.type) is out of scope): $where has $(($controls | ForEach-Object { $_.Id }) -join ', ')" }
                continue
            }

            foreach ($lib in $doc.SelectNodes('//formLibraries/Library')) { $libraries[$lib.GetAttribute('name')] = $where }

            foreach ($r in (Get-FormRefusals $xml $present ([int]$f.type) ([bool]$f.ismanaged))) {
                $refusals.Add(@{ Code = $r.Code; Where = $where; Detail = $r.Detail })
            }
            foreach ($c in $controls | Where-Object { -not $_.Disabled }) {
                $gaps.Add("$where : control '$($c.Id)' ($($c.Column)$(if ($c.Hidden) { ', hidden' })$(if ($c.Header) { ', header' })) is not disabled")
            }

            $result = Lock-FormXml $xml $present
            if ($result.Locked.Count -eq 0) {
                $state = if ($controls.Count -eq 0) { 'no locked-column control' } else { "already locked: $($result.AlreadyLocked -join ', ')" }
                Write-Info "form '$($f.name)' ($($f.formid)) type $($f.type) active=$($f.formactivationstate): $state"
                continue
            }
            foreach ($r in (Get-TransformRefusals $xml $result.Xml $present)) {
                $refusals.Add(@{ Code = $r.Code; Where = $where; Detail = $r.Detail })
            }
            Write-Plan "form '$($f.name)' ($($f.formid)) type $($f.type) active=$($f.formactivationstate): lock $($result.Locked -join ', ')"
            $edits.Add([pscustomobject]@{ FormId = $f.formid; Table = $table; Name = $f.name; Type = $f.type; Before = $xml; After = $result.Xml })
        }

        # Form libraries that could unlock a column at runtime.
        foreach ($name in $libraries.Keys) {
            $wr = Invoke-Dv -Endpoint "webresourceset?`$select=webresourceid,name,content&`$filter=name eq '$($name -replace "'", "''")'"
            if (@($wr.value).Count -eq 0) { throw "form library '$name' (used by $($libraries[$name])) was not found" }
            $content = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String([string]$wr.value[0].content))
            if (Test-LibraryUnlocks $content $present) {
                $refusals.Add(@{ Code = 'LIBRARY_UNLOCKS'; Where = "$table library '$name'"; Detail = 'names a locked column and calls setDisabled (task 168 escalation trigger 4)' })
            }
        }
        Write-Info "form libraries scanned: $($libraries.Count)"

        # Editable grids — REPORTED (task 168 escalation trigger 5 is the owner's call; not a form).
        $grid = Invoke-Dv -Endpoint "customcontroldefaultconfigs?`$select=controldescriptionxml&`$filter=primaryentitytypecode eq '$table'"
        foreach ($g in $grid.value) {
            if ("$($g.controldescriptionxml)" -match '<EnableEditing[^>]*>\s*yes\s*</EnableEditing>') {
                Write-Info "REPORT: the table's home grid is an EDITABLE grid (Power Apps grid, EnableEditing=yes) — task 168 trigger 5; owner round 25 item 8: Set-SpaarkeGridCustomizerOnChildGrids.ps1 sets the SpaarkeGridCustomizer (v1.1.1: the roots and the raw filing columns not editable, owner round 38) on it, and its -Verify checks it; not changed by this script"
            }
        }
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

# ============================================================================
# Report
# ============================================================================

Write-Step "Result"
foreach ($r in $refusals) { Write-Host ("   REFUSAL {0}: {1} — {2}" -f $r.Code, $r.Where, $r.Detail) -ForegroundColor Red }

if ($Verify) {
    if ($gaps.Count -eq 0 -and $refusals.Count -eq 0) {
        Write-Host "`nVERIFY PASS: every control bound to a locked column on every in-scope form of $($Tables -join ', ') is disabled, and no refusal case is present." -ForegroundColor Green
        exit 0
    }
    foreach ($g in $gaps) { Write-Host "   UNLOCKED $g" -ForegroundColor Red }
    Write-Host "`nVERIFY FAIL: $($gaps.Count) unlocked control(s), $($refusals.Count) refusal case(s)." -ForegroundColor Red
    exit 1
}

if ($refusals.Count -gt 0) { Stop-Refused "$($refusals.Count) refusal case(s), listed above." }

if ($edits.Count -eq 0) {
    Write-Host ""
    Write-Host "Nothing to do: every locked-column control on every in-scope form is already disabled." -ForegroundColor Green
    exit 0
}

if (-not $Apply) {
    Write-Host ""
    Write-Host "DRY RUN complete — no writes were made. $($edits.Count) form(s) would change. Re-run with -Apply." -ForegroundColor White
    exit 0
}

# ============================================================================
# Apply — every check above has passed
# ============================================================================

Write-Step "Snapshot"
if ([string]::IsNullOrEmpty($SnapshotPath)) {
    $SnapshotPath = Join-Path (Get-Location).Path ("form-lock-snapshot-{0}.json" -f (Get-Date -Format 'yyyyMMddHHmmss'))
}
$snapshot = [ordered]@{
    script         = 'Lock-CoreAncestorStampColumnsOnForms.ps1'
    task           = 'unified-access-control-r2 task 168'
    environmentUrl = $BaseUrl
    createdUtc     = (Get-Date).ToUniversalTime().ToString('o')
    forms          = @($edits | ForEach-Object {
            [ordered]@{
                formid         = $_.FormId
                table          = $_.Table
                name           = $_.Name
                type           = $_.Type
                formXmlBefore  = $_.Before
                formXmlWritten = $_.After
                sha256Before   = (Get-Sha256 $_.Before)
                sha256Written  = (Get-Sha256 $_.After)
            }
        })
}
try {
    $snapshot | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $SnapshotPath -Encoding utf8
    $readBack = Get-Content -LiteralPath $SnapshotPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if (@($readBack.forms).Count -ne $edits.Count) { throw "it holds $(@($readBack.forms).Count) form(s), expected $($edits.Count)" }
    foreach ($rf in $readBack.forms) {
        if ((Get-Sha256 $rf.formXmlBefore) -ne $rf.sha256Before -or (Get-Sha256 $rf.formXmlWritten) -ne $rf.sha256Written) {
            throw "entry '$($rf.name)' does not round-trip"
        }
    }
}
catch {
    Stop-Refused "the snapshot could not be written and read back at '$SnapshotPath': $($_.Exception.Message)"
}
Write-Done "snapshot written and read back: $SnapshotPath"

Write-Step "Re-read every form before the first PATCH"
foreach ($e in $edits) {
    if ((Get-FormXmlNow $e.FormId) -cne $e.Before) {
        Stop-Refused "FORM_CHANGED: form '$($e.Name)' ($($e.FormId)) changed since the scan. Re-run (no form was written)."
    }
}
Write-Info "$($edits.Count) form(s) unchanged since the scan"

Write-Step "Write"
$written = 0
foreach ($e in $edits) {
    # Re-read immediately before this PATCH (another formxml writer may have run meanwhile).
    if ((Get-FormXmlNow $e.FormId) -cne $e.Before) {
        Stop-Refused "FORM_CHANGED: form '$($e.Name)' ($($e.FormId)) changed since the scan; $written form(s) were already written — restore them with -RestoreFrom '$SnapshotPath' if needed."
    }
    Invoke-Dv -Endpoint "systemforms($($e.FormId))" -Method PATCH -Body @{ formxml = $e.After } | Out-Null
    $written++
    Write-Done "form '$($e.Name)' ($($e.FormId)) locked"
}
foreach ($t in @($edits | ForEach-Object { $_.Table } | Sort-Object -Unique)) {
    Publish-Table $t
    Write-Done "published $t"
}

Write-Step "Read back"
# Record each form as Dataverse now returns it (it may normalize what it was given), so -RestoreFrom can tell
# "unchanged since the apply" from "changed by someone else" — and prove the lock actually landed.
$stillOpen = @()
foreach ($sf in $snapshot.forms) {
    $stored = Get-FormXmlNow $sf.formid
    $sf['formXmlStored'] = $stored
    $sf['sha256Stored'] = Get-Sha256 $stored
    $cols = @($LockedColumns)
    $open = @(Get-LockedControls (ConvertTo-FormDocument $stored) $cols | Where-Object { -not $_.Disabled })
    if ($open.Count -gt 0) { $stillOpen += "form '$($sf.name)' ($($sf.formid)): $(($open | ForEach-Object { $_.Id }) -join ', ')" }
}
$snapshot | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $SnapshotPath -Encoding utf8
Write-Done "snapshot updated with the stored form XML: $SnapshotPath"
if ($stillOpen.Count -gt 0) {
    throw "Read-back shows control(s) still not disabled after the apply: $($stillOpen -join '; '). Restore with -RestoreFrom '$SnapshotPath'."
}

Write-Host ""
Write-Host "Locked $written form(s). Snapshot: $SnapshotPath" -ForegroundColor Green
Write-Host "Next: run -Verify (must exit 0). A second -Apply reports 'nothing to do'." -ForegroundColor Green
exit 0
