<#
.SYNOPSIS
    Locks sprk_accesspermission on the To Do, Event, Communication and Document forms while the record has a parent, with
    an "Access permission is inherited from ..." notification (owner rounds 81 and 84) - and, task 175 (owner round 87), a
    FLOOR lock on the Work Assignment and Project main forms while the record is filed under a matter or project.
    DRY RUN by default; -Verify checks the result at any time.

.DESCRIPTION
    Owner round 81 (2026-10-08, binding): a To Do, Event, Communication or Document WITH a parent shows the parent's Access
    Permission (the most restrictive across its parents) and the field is locked with "inherited from X"; a record WITHOUT
    a parent keeps and edits its own value. Round 84 widens it: "if a child has a parent then the access cannot be changed
    manually (e.g. its locked)". The BFF writes the value (CoreAncestorResolver, the shared stamp path) and keeps it in step
    (SecureChildReconciliationJob); the form only locks and labels, through the form library sprk_accesspermission_inherited
    (src/client/webresources/js/sprk_accesspermission_inherited.js), which decides "has a parent" from the server's own
    filing lookups (pinned by ParentLineageTests.FormLibraryParentLookups_MatchTheServerMap).

    Task 175 (owner round 87: "the parent sets a FLOOR"): a work assignment or project filed under a matter or project
    (its typed regarding lookups or the polymorphic regarding pair - the server's SecureRootInheritance filing, pinned in
    the library's ROOT_PARENT_LOOKUPS / PAIR_PARENT_TABLES) inherits Secure and Access Permission and may never be looser,
    but may be made stricter. There the same library (1.2.0) keeps sprk_accesspermission editable, puts back a value looser
    than the parents' floor, disables sprk_issecure while the record is filed, and says whether the value is inherited or
    set on the record; an unreadable parent locks both (fail safe).

    The lock is applied at run time (the field is editable on a parentless record), so per form this script only:
      - registers the library sprk_accesspermission_inherited and its OnLoad handler
        Spaarke.AccessPermissionInherited.onLoad (pass execution context), on EVERY form of the target tables (see
        $TableFormTypes: Main (type 2) and Quick Create (type 7) for To Do, Event, Communication and Document; Main only
        for Work Assignment and Project) that SHOWS sprk_accesspermission - a control bound to it, or a custom control
        parameter bound to it (the TrackingFieldTrio pill on the To Do, Event, Work Assignment and Project main forms);
      - on the forms named by -AddControlForms that do not show the column yet (default: the Communication "Message main
        form" b58ec3d8 and the "Document main form" 9088d6a4 - owner round 81: "a permission control on these child
        records, but NOT TrackingFieldTrio on Communication"), adds a plain choice control for it in a new section
        "sprk_access_permission", first in the first visible tab. (No plain control is added to a Work Assignment or
        Project form: their main forms show the column through the TrackingFieldTrio pill.)
    The change is ADDITIVE ONLY, made as string insertions into the form XML (never a re-serialization), and proven by a
    parsed comparison in which every original node is still present, in order, with identical attributes and text. New ids
    are derived from the form id (a stable hash), so a re-run writes identical bytes and a complete form is left
    byte-identical ("nothing to do").

    Fail closed (ADR-003). Each of these REFUSES (exit 2), names the form, and writes nothing:
      FORM_NOT_FOUND     an -AddControlForms id is not in the environment, or is not a target form type of one of the
                         target tables;
      MANAGED_FORM       a form that needs a change is managed (change it in its owning solution);
      HANDLER_DISABLED   the OnLoad handler is registered but disabled (a maker's choice this script does not override);
      NO_VISIBLE_TAB     a control must be added and the form has no visible tab with a <sections> element;
      COLUMN_MISSING     (live) a target table has no sprk_accesspermission (sprk_document: run
                         Set-DocumentAccessPermissionSchema.ps1 -Apply first);
      PREREQ_MISSING     (live) the web resource sprk_accesspermission_inherited is not in the environment;
      TRANSFORM_PARSE    the result is not well-formed, removed or changed an original node, added anything but the
                         elements above, or is still incomplete;
      FORM_CHANGED       (-Apply) a form's XML changed between the scan and its PATCH.

    ORDER (live steps run by the main session, each dry run -> -Apply -> -Verify):
      1. Set-DocumentAccessPermissionSchema.ps1 (sprk_document.sprk_accesspermission in source and in SpaarkeCore);
      2. deploy the web resource: scripts/Deploy-WebResourceInline.ps1 -DataverseUrl <environment>
         -WebResourceName sprk_accesspermission_inherited
         -FilePath src/client/webresources/js/sprk_accesspermission_inherited.js -WebResourceType 3;
      3. THIS script.
    No other formxml writer may run against the environment at the same time (task 168 rule: whole-document rewrites).

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Default: https://spaarkedev1.crm.dynamics.com

.PARAMETER Apply
    Snapshot, PATCH, publish, read back. Without a mode switch the script is a READ-ONLY dry run.

.PARAMETER Verify
    Read-only. Exit 0 only when: every target form (see $TableFormTypes) that shows sprk_accesspermission
    registers the library and an ENABLED OnLoad handler; every -AddControlForms form shows the column; the web resource
    exists and its content equals the checked-in library byte for byte; and no refusal case is present. Otherwise exit 1
    naming each gap - a form that shows the field without the lock (a "re-opened" control) is a gap. Any read fault is a
    FAILED check (exit 1).

.PARAMETER RestoreFrom
    Path to a snapshot written by -Apply. Refuses (exit 2) if the snapshot's environment differs from -EnvironmentUrl or if
    a form changed after the apply. Otherwise puts each form's "before" XML back and publishes each table.

.PARAMETER SelfTest
    Offline (no token, no network): runs the pure transform, state and refusal checks over the fixtures under
    tests/fixtures/form-access-permission-lock/ plus inline parse-check cases; exits 1 on any mismatch.

.PARAMETER AddControlForms
    Form ids that get a plain sprk_accesspermission control when they do not show it. Default: the Communication
    "Message main form" and the "Document main form" (spaarkedev1).

.PARAMETER SnapshotPath
    -Apply only. Default: accesspermission-lock-snapshot-yyyyMMddHHmmss.json in the current directory.

.PARAMETER FixturePath
    -SelfTest only. Default: tests/fixtures/form-access-permission-lock.

.PARAMETER LibraryPath
    -Verify only: the checked-in library the deployed web resource must equal. Default: the repository's copy.

.EXAMPLE
    .\Set-InheritedAccessPermissionFormLock.ps1                 # dry run
    .\Set-InheritedAccessPermissionFormLock.ps1 -SelfTest       # offline fixtures
    .\Set-InheritedAccessPermissionFormLock.ps1 -Apply          # snapshot, PATCH, publish, read back
    .\Set-InheritedAccessPermissionFormLock.ps1 -Verify         # read-only (exit 0 / 1)
    .\Set-InheritedAccessPermissionFormLock.ps1 -RestoreFrom .\accesspermission-lock-snapshot-20261008120000.json

.NOTES
    Project : unified-access-control-r2
    Task    : 173 (#1423) - owner rounds 81 and 84; 175 - work assignment and project (owner round 84)
    Created : 2026-10-08
    Docs    : projects/unified-access-control-r2/notes/task-173-child-access-permission.md

    OPERATOR-RUN ONLY: -Apply and -RestoreFrom are the main session's manual gate. Requires Azure CLI (`az login`) with
    a principal that can customize the environment. PowerShell 7+.

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
    [string[]]$AddControlForms = @('b58ec3d8-0982-f111-8076-7ced8ddc4cc6', '9088d6a4-4cf2-f011-8406-7c1e520aa4df'),

    [Parameter(Mandatory = $false)]
    [string]$SnapshotPath,

    [Parameter(Mandatory = $false)]
    [string]$FixturePath,

    [Parameter(Mandatory = $false)]
    [string]$LibraryPath
)

$ErrorActionPreference = "Stop"

$modeCount = @($Apply.IsPresent, $Verify.IsPresent, (-not [string]::IsNullOrEmpty($RestoreFrom)), $SelfTest.IsPresent) |
    Where-Object { $_ } | Measure-Object | Select-Object -ExpandProperty Count
if ($modeCount -gt 1) { throw "-Apply, -Verify, -RestoreFrom and -SelfTest are separate modes; pass at most one." }

$RepoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrEmpty($FixturePath)) { $FixturePath = Join-Path $RepoRoot 'tests/fixtures/form-access-permission-lock' }
if ([string]::IsNullOrEmpty($LibraryPath)) { $LibraryPath = Join-Path $RepoRoot 'src/client/webresources/js/sprk_accesspermission_inherited.js' }

$Column = 'sprk_accesspermission'
# The target tables and, per table, the form types the library is registered on (2 = Main, 7 = Quick Create). Task 175
# adds the Work Assignment and Project MAIN forms (the TrackingFieldTrio pill shows the column there).
$TableFormTypes = [ordered]@{
    'sprk_todo'           = @(2, 7)
    'sprk_event'          = @(2, 7)
    'sprk_communication'  = @(2, 7)
    'sprk_document'       = @(2, 7)
    'sprk_workassignment' = @(2)
    'sprk_project'        = @(2)
}
$Tables = @($TableFormTypes.Keys)
$RootTables = @('sprk_workassignment', 'sprk_project')   # the library's ROOT_PARENT_LOOKUPS tables (task 175)
$Library = 'sprk_accesspermission_inherited'
$OnLoadFunction = 'Spaarke.AccessPermissionInherited.onLoad'
$SectionName = 'sprk_access_permission'
$ClassIdOptionSet = '{3EF39988-22BB-4F0B-BBBE-64B5A3748AEE}'

# ============================================================================
# Pure functions (no I/O) - exercised offline by -SelfTest
# ============================================================================

function New-StableGuid([string]$Seed) {
    # A deterministic id per (form, purpose): a re-run writes identical bytes, so the transform is idempotent.
    $bytes = [System.Security.Cryptography.MD5]::HashData([System.Text.Encoding]::UTF8.GetBytes("uac-r2-173|$Seed"))
    return '{' + ([guid]::new($bytes)).ToString() + '}'
}

function ConvertTo-FormDocument([string]$FormXml) {
    $doc = [System.Xml.XmlDocument]::new()
    $doc.PreserveWhitespace = $true
    $doc.XmlResolver = $null
    $doc.LoadXml($FormXml)
    return $doc
}

function Get-Sha256([string]$Text) {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
    return [System.Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

<#
    What a form holds of the lock: whether it SHOWS the column (a control bound to it, or a custom control parameter
    bound to it), the library, and the OnLoad handlers (form-level onload only; a cell's own events are never read).
#>
function Get-LockState([System.Xml.XmlDocument]$Doc) {
    $controls = @($Doc.SelectNodes('//control') | Where-Object { $_.GetAttribute('datafieldname') -ieq $Column })
    $params = @($Doc.SelectNodes('//customControl/parameters/*') | Where-Object {
            $_.GetAttribute('static') -ine 'true' -and $_.get_InnerText().Trim() -ieq $Column })
    $handlers = @($Doc.SelectNodes('/form/events/event') |
        Where-Object { $_.GetAttribute('name') -ieq 'onload' -and [string]::IsNullOrEmpty($_.GetAttribute('attribute')) } |
        ForEach-Object { $_.SelectNodes('.//Handler') } |
        Where-Object { $_.GetAttribute('functionName') -ceq $OnLoadFunction })
    $library = @($Doc.SelectNodes('/form/formLibraries/Library') | Where-Object { $_.GetAttribute('name') -ieq $Library }).Count -gt 0
    return [pscustomobject]@{
        Shows           = ($controls.Count + $params.Count) -gt 0
        Controls        = $controls.Count
        BoundParameters = $params.Count
        HasLibrary      = $library
        HandlerCount    = $handlers.Count
        HandlerDisabled = @($handlers | Where-Object { $_.GetAttribute('enabled') -ieq 'false' }).Count -gt 0
    }
}

function Test-LockComplete([object]$State) {
    return $State.Shows -and $State.HasLibrary -and $State.HandlerCount -gt 0 -and -not $State.HandlerDisabled
}

<#
    The pure refusal checks over one target form. $AddControl: the form must SHOW the column (an -AddControlForms form).
    Returns a list of @{ Code; Detail }.
#>
function Get-LockRefusals([string]$FormXml, [bool]$AddControl, [bool]$IsManaged = $false) {
    $refusals = [System.Collections.Generic.List[object]]::new()
    try { $doc = ConvertTo-FormDocument $FormXml }
    catch { $refusals.Add(@{ Code = 'TRANSFORM_PARSE'; Detail = "the form XML is not well-formed: $($_.Exception.Message)" }); return @($refusals) }
    $state = Get-LockState $doc
    if ($state.HandlerDisabled) {
        $refusals.Add(@{ Code = 'HANDLER_DISABLED'; Detail = "$OnLoadFunction is registered but disabled" })
    }
    $needsControl = $AddControl -and -not $state.Shows
    if ($needsControl) {
        $tab = @($doc.SelectNodes('/form/tabs/tab') | Where-Object { $_.GetAttribute('visible') -ine 'false' -and $_.SelectSingleNode('./columns/column/sections') })
        if ($tab.Count -eq 0) { $refusals.Add(@{ Code = 'NO_VISIBLE_TAB'; Detail = 'no visible tab with a <sections> element to place the control in' }) }
    }
    $needsChange = ($state.Shows -or $needsControl) -and -not (Test-LockComplete $state)
    if ($IsManaged -and ($needsChange -or $needsControl)) {
        $refusals.Add(@{ Code = 'MANAGED_FORM'; Detail = 'the form is managed; change it in its owning solution' })
    }
    return @($refusals)
}

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
                $span = @{ Start = $lineStarts[$li.LineNumber - 1] + $li.LinePosition - 2; IsEmpty = $reader.IsEmptyElement; CloseStart = -1 }
                if ($span.IsEmpty) { return $span }
            }
            elseif ($reader.NodeType -eq [System.Xml.XmlNodeType]::EndElement -and $null -ne $span) {
                $span.CloseStart = $lineStarts[$li.LineNumber - 1] + $li.LinePosition - 3
                return $span
            }
        }
        return $span
    }
    finally { $reader.Dispose() }
}

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

function Get-FreeControlId([System.Xml.XmlDocument]$Doc, [string]$Base) {
    $taken = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($c in $Doc.SelectNodes('//control')) { [void]$taken.Add($c.GetAttribute('id')) }
    if (-not $taken.Contains($Base)) { return $Base }
    for ($n = 1; ; $n++) { if (-not $taken.Contains("${Base}_$n")) { return "${Base}_$n" } }
}

<#
    The transform. Returns @{ Xml; Added = @(what was added) }. A complete form comes back byte-identical. Call only on a
    form with no refusal (Get-LockRefusals).
#>
function Add-InheritedAccessPermissionLock([string]$FormXml, [string]$FormId, [bool]$AddControl) {
    $doc = ConvertTo-FormDocument $FormXml
    $state = Get-LockState $doc
    $added = [System.Collections.Generic.List[string]]::new()
    $xml = $FormXml
    $fid = $FormId.Trim('{', '}').ToLowerInvariant()

    # 1. A plain choice control, in a section of its own, first in the first visible tab (an -AddControlForms form only).
    if ($AddControl -and -not $state.Shows) {
        $tabs = @($doc.SelectNodes('/form/tabs/tab'))
        $k = -1
        for ($i = 0; $i -lt $tabs.Count; $i++) {
            if ($tabs[$i].GetAttribute('visible') -ine 'false' -and $tabs[$i].SelectSingleNode('./columns/column/sections')) { $k = $i; break }
        }
        if ($k -lt 0) { throw "NO_VISIBLE_TAB" }
        $controlId = Get-FreeControlId $doc $Column
        $section = "<section name=""$SectionName"" id=""$((New-StableGuid "$fid|section").Trim('{', '}'))"" IsUserDefined=""0"" locklevel=""0"" showlabel=""false"" showbar=""false"" layout=""varwidth"" celllabelalignment=""Left"" celllabelposition=""Left"" columns=""1"" labelwidth=""115"">" +
            '<labels><label description="ACCESS" languagecode="1033" /></labels><rows><row>' +
            "<cell id=""$(New-StableGuid "$fid|cell")"" locklevel=""0"" colspan=""1"" rowspan=""1""><labels><label description=""Access Permission"" languagecode=""1033"" /></labels>" +
            "<control id=""$controlId"" classid=""$ClassIdOptionSet"" datafieldname=""$Column"" disabled=""false"" /></cell></row></rows></section>"
        $tabsStart = [regex]::Match($xml, '<tabs\b').Index
        $tabMatch = [regex]::Matches($xml.Substring($tabsStart), '<tab\b')[$k]
        $tabAt = $tabsStart + $tabMatch.Index
        $open = [regex]::Match($xml.Substring($tabAt), '<sections\s*>')
        if (-not $open.Success) { throw "NO_VISIBLE_TAB" }
        $xml = $xml.Insert($tabAt + $open.Index + $open.Length, $section)
        $added.Add("control '$controlId' for $Column (section $SectionName)")
        $state = Get-LockState (ConvertTo-FormDocument $xml)
    }

    if (-not $state.Shows) { return @{ Xml = $xml; Added = @($added) } }

    # 2. The OnLoad handler.
    if ($state.HandlerCount -eq 0) {
        $handler = "<Handler functionName=""$OnLoadFunction"" libraryName=""$Library"" handlerUniqueId=""$(New-StableGuid "$fid|handler")"" enabled=""true"" parameters="""" passExecutionContext=""true"" />"
        $events = Get-TopLevelElementSpan $xml 'events'
        $onload = $null
        if ($null -ne $events -and -not $events.IsEmpty) {
            $region = $xml.Substring($events.Start, $events.CloseStart - $events.Start)
            foreach ($m in [regex]::Matches($region, '<event\b(?:[^>"'']|"[^"]*"|''[^'']*'')*>')) {
                $name = [regex]::Match($m.Value, '\bname\s*=\s*"([^"]*)"').Groups[1].Value
                if ($name -ieq 'onload' -and $m.Value -notmatch '\battribute\s*=') { $onload = @{ Index = $events.Start + $m.Index; Length = $m.Length; Value = $m.Value }; break }
            }
        }
        if ($onload -and $onload.Value.EndsWith('/>')) {
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
        $added.Add("OnLoad handler $OnLoadFunction")
    }

    # 3. The library.
    if (-not $state.HasLibrary) {
        $xml = Add-IntoContainer $xml 'formLibraries' "<Library name=""$Library"" libraryUniqueId=""$(New-StableGuid "$fid|library")"" />" @()
        $added.Add("form library $Library")
    }
    return @{ Xml = $xml; Added = @($added) }
}

<#
    Same node type and name; for an element, the same attributes (names and values, case-sensitive); for text, the same
    value. An original node is paired with a result node only when they match this way, so a changed original shows up as
    "missing" plus "unexpected", never as a silent pairing - and an added element of the same name (our section beside the
    form's own sections) is never mistaken for an original.
#>
function Test-ShallowMatch([System.Xml.XmlNode]$X, [System.Xml.XmlNode]$Y) {
    if ($X.get_NodeType() -ne $Y.get_NodeType() -or $X.get_LocalName() -cne $Y.get_LocalName()) { return $false }
    if ($X.get_NodeType() -ne [System.Xml.XmlNodeType]::Element) { return $X.get_Value() -ceq $Y.get_Value() }
    $ax = @($X.get_Attributes() | ForEach-Object { "$($_.get_Name())=$($_.get_Value())" } | Sort-Object -CaseSensitive)
    $ay = @($Y.get_Attributes() | ForEach-Object { "$($_.get_Name())=$($_.get_Value())" } | Sort-Object -CaseSensitive)
    return ($ax -join "`n") -ceq ($ay -join "`n")
}

<#
    Parse check (additive only): the result is well-formed; every original node is still present, in order, with identical
    attributes and text; every new node is one this script adds (our section, our Handler / onload event / Library, or a
    container holding only those); and the lock is complete. Returns a list of problems (empty = OK).
#>
function Test-LockTransform([string]$Before, [string]$After) {
    $problems = [System.Collections.Generic.List[string]]::new()
    try { $a = ConvertTo-FormDocument $Before } catch { $problems.Add("input not well-formed: $($_.Exception.Message)"); return @($problems) }
    try { $b = ConvertTo-FormDocument $After } catch { $problems.Add("TRANSFORM_PARSE: result not well-formed: $($_.Exception.Message)"); return @($problems) }

    $isAllowed = $null
    $isAllowed = {
        param($n)
        if ($n.get_NodeType() -ne [System.Xml.XmlNodeType]::Element) { return $false }
        switch -CaseSensitive ($n.get_LocalName()) {
            'section' { return ($n.GetAttribute('name') -ceq $SectionName) }
            'Handler' { return ($n.GetAttribute('functionName') -ceq $OnLoadFunction -and $n.GetAttribute('libraryName') -ceq $Library) }
            'Library' { return ($n.GetAttribute('name') -ceq $Library) }
            { $_ -in @('events', 'formLibraries', 'Handlers', 'event') } {
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
        $state = Get-LockState $b
        if ($state.Shows -and -not (Test-LockComplete $state)) { $problems.Add("TRANSFORM_PARSE: the lock is incomplete after the transform") }
    }
    return @($problems)
}

# ============================================================================
# -SelfTest (offline)
# ============================================================================

if ($SelfTest) {
    Write-Host "Set-InheritedAccessPermissionFormLock  -  SELF-TEST (offline; no token, no network)" -ForegroundColor White
    $cases = Get-Content -LiteralPath (Join-Path $FixturePath 'cases.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $failures = 0
    $count = 0
    function Report([string]$Name, [bool]$Ok, [string]$Detail = '') {
        $script:count++
        if ($Ok) { Write-Host ("  PASS  {0}" -f $Name) -ForegroundColor Green }
        else { $script:failures++; Write-Host ("  FAIL  {0} {1}" -f $Name, $Detail) -ForegroundColor Red }
    }
    foreach ($c in $cases.cases) {
        $xml = Get-Content -LiteralPath (Join-Path $FixturePath $c.fixture) -Raw -Encoding UTF8
        $refusals = @(Get-LockRefusals $xml ([bool]$c.addControl) ([bool]$c.managed) | ForEach-Object { $_.Code })
        $wantRefusals = @($c.refusals)
        Report "$($c.name): refusals [$($wantRefusals -join ',')]" ((@($refusals) -join ',') -eq ($wantRefusals -join ',')) "(got [$($refusals -join ',')])"
        if ($wantRefusals.Count -gt 0) { continue }
        $result = Add-InheritedAccessPermissionLock $xml $c.formId ([bool]$c.addControl)
        $wantAdded = @($c.added)
        Report "$($c.name): adds [$($wantAdded -join '; ')]" ((@($result.Added) -join '; ') -eq ($wantAdded -join '; ')) "(got [$($result.Added -join '; ')])"
        $problems = @(Test-LockTransform $xml $result.Xml)
        Report "$($c.name): parse check" ($problems.Count -eq 0) ($problems -join '; ')
        $again = Add-InheritedAccessPermissionLock $result.Xml $c.formId ([bool]$c.addControl)
        Report "$($c.name): a second run is byte-identical" ($again.Xml -ceq $result.Xml -and $again.Added.Count -eq 0)
        $state = Get-LockState (ConvertTo-FormDocument $result.Xml)
        Report "$($c.name): complete = $($c.complete)" ((Test-LockComplete $state) -eq [bool]$c.complete)
    }

    # Inline parse-check cases over the first complete-able fixture: what the parse check must refuse.
    $base = Get-Content -LiteralPath (Join-Path $FixturePath 'todo-main-tracking-trio.xml') -Raw -Encoding UTF8
    $good = (Add-InheritedAccessPermissionLock $base '{00000000-0000-0000-0000-000000000173}' $false).Xml
    $inline = @(
        @{ Name = 'parse: a foreign library added'; After = $good.Replace("<Library name=""$Library""", '<Library name="sprk_other" libraryUniqueId="{1}" /><Library name="' + $Library + '"') },
        @{ Name = 'parse: an original attribute changed'; After = $good.Replace('name="sprk_todo_regarding_presave"', 'name="sprk_todo_regarding_presave2"') },
        @{ Name = 'parse: an original node removed'; After = [regex]::Replace($good, '<Library name="sprk_todo_regarding_presave"[^>]*/>', '') },
        @{ Name = 'parse: not well-formed'; After = $good.Replace('</form>', '') }
    )
    foreach ($p in $inline) { Report $p.Name (@(Test-LockTransform $base $p.After).Count -gt 0) }

    # The checked-in library declares what this script registers.
    $lib = Get-Content -LiteralPath $LibraryPath -Raw -Encoding UTF8
    Report 'library declares Spaarke.AccessPermissionInherited.onLoad' ($lib -match 'Spaarke\.AccessPermissionInherited\s*=' -and $lib -match 'ns\.onLoad\s*=')

    # Task 175: every table this script registers the library on is one the library locks - PARENT_LOOKUPS (the four
    # child tables) or ROOT_PARENT_LOOKUPS (work assignment, project) - and the root tables are exactly the latter.
    function Get-LibraryMapKeys([string]$Source, [string]$Marker) {
        $m = [regex]::Match($Source, "/\*\s*${Marker}:BEGIN[^\n]*\n\s*ns\.$Marker\s*=\s*(?<json>\{.*?\});\s*/\*\s*${Marker}:END", 'Singleline')
        if (-not $m.Success) { return $null }
        return @(($m.Groups['json'].Value | ConvertFrom-Json -AsHashtable).Keys)
    }
    $childKeys = Get-LibraryMapKeys $lib 'PARENT_LOOKUPS'
    $rootKeys = Get-LibraryMapKeys $lib 'ROOT_PARENT_LOOKUPS'
    $libraryTables = @($childKeys) + @($rootKeys)
    Report 'library maps every target table (PARENT_LOOKUPS + ROOT_PARENT_LOOKUPS)' ($null -ne $childKeys -and $null -ne $rootKeys -and @($Tables | Where-Object { $libraryTables -notcontains $_ }).Count -eq 0)
    Report 'the root tables are the library ROOT_PARENT_LOOKUPS tables' ((@($rootKeys | Sort-Object) -join ',') -eq (@($RootTables | Sort-Object) -join ','))
    Report 'the root tables are registered on Main forms only' (@($RootTables | Where-Object { ($TableFormTypes[$_] -join ',') -ne '2' }).Count -eq 0)

    if ($failures -gt 0) { Write-Host "`nSELF-TEST FAIL: $failures of $count check(s)." -ForegroundColor Red; exit 1 }
    Write-Host "`nSELF-TEST PASS: $count check(s)." -ForegroundColor Green
    exit 0
}

# ============================================================================
# Live helpers
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

# ============================================================================
# -RestoreFrom
# ============================================================================

if ($RestoreFrom) {
    Write-Host "Set-InheritedAccessPermissionFormLock  -  RESTORE from $RestoreFrom" -ForegroundColor White
    Write-Host "Environment: $BaseUrl"
    if (-not (Test-Path -LiteralPath $RestoreFrom)) { Stop-Refused "snapshot '$RestoreFrom' does not exist." }
    try { $snap = Get-Content -LiteralPath $RestoreFrom -Raw -Encoding UTF8 | ConvertFrom-Json }
    catch { Stop-Refused "snapshot '$RestoreFrom' is not valid JSON: $($_.Exception.Message)" }
    if ("$($snap.environmentUrl)".TrimEnd('/') -ine $BaseUrl) { Stop-Refused "the snapshot was taken on '$($snap.environmentUrl)', not '$BaseUrl'." }
    $forms = @($snap.forms)
    if ($forms.Count -eq 0) { Write-Done "the snapshot holds no forms - nothing to restore."; exit 0 }
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
        if ($h -eq $f.sha256Before) { Write-Info "form '$($f.name)' ($($f.formid)) still holds its 'before' XML - skipped"; continue }
        $expected = if ($f.sha256Stored) { [string]$f.sha256Stored } else { [string]$f.sha256Written }
        if ($h -ne $expected) {
            Stop-Refused "form '$($f.name)' ($($f.formid), $($f.table)) changed after the apply (sha256 $h, expected $expected); restore any later formxml change first with its own snapshot."
        }
        $toRestore += $f
    }
    if ($toRestore.Count -eq 0) { Write-Done "no form in the snapshot was written - nothing to restore."; exit 0 }
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
# Scan (read-only) - dry run, -Verify and -Apply
# ============================================================================

$Mode = if ($Apply) { "APPLY" } elseif ($Verify) { "VERIFY (read-only)" } else { "DRY RUN (read-only; pass -Apply to perform the writes)" }
Write-Host "Set-InheritedAccessPermissionFormLock  -  $Mode" -ForegroundColor White
Write-Host "Environment: $BaseUrl"
Write-Host "Library    : $Library ($OnLoadFunction)"

$addIds = @($AddControlForms | ForEach-Object { $_.Trim('{', '}').ToLowerInvariant() })
$refusals = [System.Collections.Generic.List[object]]::new()
$gaps = [System.Collections.Generic.List[string]]::new()
$edits = [System.Collections.Generic.List[object]]::new()

try {
    $Token = Get-DataverseToken

    Write-Step "Prerequisites"
    foreach ($t in $Tables) {
        $attr = Invoke-Dv -Endpoint "EntityDefinitions(LogicalName='$t')/Attributes(LogicalName='$Column')?`$select=LogicalName" -AllowNotFound
        if ($null -eq $attr) { $refusals.Add(@{ Code = 'COLUMN_MISSING'; Where = $t; Detail = "$t has no $Column" }) }
        else { Write-Info "$t.$Column present" }
    }
    $wr = Invoke-Dv -Endpoint "webresourceset?`$select=webresourceid,name,content&`$filter=name eq '$Library'"
    if (@($wr.value).Count -eq 0) { $refusals.Add(@{ Code = 'PREREQ_MISSING'; Where = $BaseUrl; Detail = "web resource $Library is not in the environment" }) }
    else {
        Write-Info "web resource $Library present"
        if ($Verify) {
            $deployed = [System.Convert]::FromBase64String([string]$wr.value[0].content)
            $local = [System.IO.File]::ReadAllBytes($LibraryPath)
            $same = [System.Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($deployed)) -eq
                [System.Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($local))
            if (-not $same) { $gaps.Add("web resource $Library differs from the checked-in $LibraryPath (deploy it, then publish)") }
            else { Write-Info "web resource $Library equals the checked-in library" }
        }
    }

    Write-Step "Forms"
    foreach ($t in $Tables) {
        $typeFilter = ($TableFormTypes[$t] | ForEach-Object { "type eq $_" }) -join ' or '
        $forms = Invoke-Dv -Endpoint "systemforms?`$select=formid,name,type,ismanaged,formxml,formactivationstate&`$filter=objecttypecode eq '$t' and ($typeFilter)"
        foreach ($f in @($forms.value)) {
            $id = ([string]$f.formid).ToLowerInvariant()
            $addControl = $addIds -contains $id
            $xml = [string]$f.formxml
            $state = Get-LockState (ConvertTo-FormDocument $xml)
            if (-not $state.Shows -and -not $addControl) { continue }
            $label = "$t '$($f.name)' ($id, type $($f.type))"
            foreach ($r in (Get-LockRefusals $xml $addControl ([bool]$f.ismanaged))) {
                $refusals.Add(@{ Code = $r.Code; Where = $label; Detail = $r.Detail })
            }
            if (Test-LockComplete $state) { Write-Info "$label locked"; continue }
            if ($Verify) {
                $why = if (-not $state.Shows) { "does not show $Column" } elseif ($state.HandlerDisabled) { 'its OnLoad handler is disabled' } else { 'shows the field without the lock (library or OnLoad handler missing)' }
                $gaps.Add("$label $why"); continue
            }
            $edits.Add([pscustomobject]@{ FormId = $id; Table = $t; Name = [string]$f.name; Type = [int]$f.type; Before = $xml; AddControl = $addControl })
        }
    }
    $seen = @($edits | ForEach-Object { $_.FormId })
    foreach ($a in $addIds) {
        $f = Invoke-Dv -Endpoint "systemforms($a)?`$select=formid,objecttypecode,type" -AllowNotFound
        if ($null -eq $f -or $Tables -notcontains [string]$f.objecttypecode -or $TableFormTypes[[string]$f.objecttypecode] -notcontains [int]$f.type) {
            $refusals.Add(@{ Code = 'FORM_NOT_FOUND'; Where = $a; Detail = 'not a target form type of the target tables in this environment' })
        }
        elseif ($RootTables -contains [string]$f.objecttypecode) {
            $refusals.Add(@{ Code = 'FORM_NOT_FOUND'; Where = $a; Detail = "a $($f.objecttypecode) form gets no plain control (the TrackingFieldTrio pill shows the column there)" })
        }
    }
}
catch {
    if ($Verify) { Write-Host "`nVERIFY FAIL: read fault - $($_.Exception.Message)" -ForegroundColor Red; exit 1 }
    throw
}

if ($refusals.Count -gt 0) {
    foreach ($r in $refusals) { Write-Host ("   {0,-16} {1}: {2}" -f $r.Code, $r.Where, $r.Detail) -ForegroundColor Red }
    if ($Verify) { Write-Host "`nVERIFY FAIL: $($refusals.Count) refusal case(s) present." -ForegroundColor Red; exit 1 }
    Stop-Refused "$($refusals.Count) refusal case(s) above."
}

if ($Verify) {
    if ($gaps.Count -eq 0) { Write-Host "`nVERIFY PASS" -ForegroundColor Green; exit 0 }
    foreach ($g in $gaps) { Write-Host "   GAP   $g" -ForegroundColor Red }
    Write-Host "`nVERIFY FAIL: $($gaps.Count) gap(s)." -ForegroundColor Red
    exit 1
}

Write-Step "Plan"
foreach ($e in $edits) {
    $result = Add-InheritedAccessPermissionLock $e.Before $e.FormId $e.AddControl
    $problems = @(Test-LockTransform $e.Before $result.Xml)
    if ($problems.Count -gt 0) { Stop-Refused "TRANSFORM_PARSE on form '$($e.Name)' ($($e.FormId)): $($problems -join '; ')" }
    $e | Add-Member -NotePropertyName After -NotePropertyValue $result.Xml
    foreach ($a in $result.Added) { Write-Plan "$($e.Table) '$($e.Name)' ($($e.FormId)): $a" }
}
if ($edits.Count -eq 0) { Write-Host "`nNothing to do: every form that shows $Column is locked." -ForegroundColor Green; exit 0 }
if (-not $Apply) { Write-Host "`nDRY RUN complete - $($edits.Count) form(s) would be updated. Re-run with -Apply." -ForegroundColor White; exit 0 }

# ============================================================================
# Apply - every check above has passed
# ============================================================================

Write-Step "Snapshot"
if ([string]::IsNullOrEmpty($SnapshotPath)) {
    $SnapshotPath = Join-Path (Get-Location).Path ("accesspermission-lock-snapshot-{0}.json" -f (Get-Date -Format 'yyyyMMddHHmmss'))
}
$snapshot = [ordered]@{
    script         = 'Set-InheritedAccessPermissionFormLock.ps1'
    task           = 'unified-access-control-r2 tasks 173/175 (owner rounds 81/84)'
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
        Stop-Refused "FORM_CHANGED: form '$($e.Name)' ($($e.FormId)) changed since the scan; $written form(s) were already written - restore them with -RestoreFrom '$SnapshotPath' if needed."
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
    if (-not (Test-LockComplete (Get-LockState (ConvertTo-FormDocument $stored)))) { $incomplete += "form '$($sf.name)' ($($sf.formid))" }
}
$snapshot | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $SnapshotPath -Encoding utf8
Write-Done "snapshot updated with the stored form XML: $SnapshotPath"
if ($incomplete.Count -gt 0) { throw "Read-back shows incomplete form(s) after the apply: $($incomplete -join '; '). Restore with -RestoreFrom '$SnapshotPath'." }

Write-Host ""
Write-Host "Updated $written form(s). Snapshot: $SnapshotPath" -ForegroundColor Green
Write-Host "Next: -Verify (must exit 0)." -ForegroundColor Green
exit 0
