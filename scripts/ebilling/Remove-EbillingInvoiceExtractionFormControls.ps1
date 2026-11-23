<#
.SYNOPSIS
    eBilling R1 task 194 (ISS-045) - takes the retired extraction controls (sprk_extractionstatus, sprk_extractedjson) off the live
    sprk_invoice main and quick-create forms. Guarded, idempotent, dev only.

.DESCRIPTION
    Task 050b deleted the legacy invoice extraction path: nothing writes sprk_invoice.sprk_extractionstatus or sprk_extractedjson any
    more, yet the invoice main form (eBilling section) and the quick-create form still show them, so users see an "Extraction Status"
    that never changes. The SpaarkeMaster source of both forms and the eBilling section in Deploy-EbillingFormsViews.ps1 no longer name
    them (task 194). This script brings the LIVE dev forms in step, and it is the ONLY way this task does it: do NOT re-run
    Deploy-EbillingFormsViews.ps1 to remove the main-form control. In Section mode that script rebuilds the whole section_ebilling and
    would drop the qv_billingexport quick view (a pre-existing defect, filed separately), so it is not a safe tool for this change.

    The COLUMNS and the sprk_extractionstatus option set are NOT touched (deleting them is a separate, destructive owner decision).
    Nothing but the two controls (their cell, and the row, section and column they leave empty) is removed from the two forms.

    EXPECTED FORMS, pinned by form id (an unexpected shape refuses; it is never guessed around):
      sprk_invoice  type 2 (main)         93aa1c69-0406-f111-8406-7c1e525abd8b  'Invoice main form'
      sprk_invoice  type 7 (quick create) c5e5807d-e20c-f111-8342-7c1e520aa4df  'Invoice quick create form'
    Only these two forms are ever edited. Any OTHER sprk_invoice form (of any type) that names a control is a blocker (exit 2): it is
    reported, never edited.

    MODES
      (none)       READ-ONLY report: per form, which of the two controls it shows, what would be removed, whether the shape is expected,
                   and the pending (unpublished) changes found on sprk_invoice; then the verdict (READY, BLOCKED with the reasons, or
                   ALREADY CLEAN). Nothing is written.
      -Apply       Edits the form(s) that show a control, then publishes sprk_invoice (entity-scoped, through the shared
                   scripts/lib/Publish-SolutionComponents.ps1 procedure, never tenant-wide), then verifies. Every refusal happens before
                   the first write (exit 2, nothing changed):
                     (a) the environment is not dev (checked before anything else, before any Dataverse call);
                     (b) an expected form is missing;
                     (c) a form shows a control in a place the script does not edit (not in a cell of a row of a section: a header,
                         a form tab it would leave without columns), or still names a control after the clean-up (an event handler);
                     (d) another sprk_invoice form names a control;
                     (e) a target form has UNPUBLISHED changes that are not exactly the edit this run would write (publishing would
                         overwrite someone's work; -AllowPendingCollateral does NOT override this);
                     (f) another pending (unpublished) form or view of sprk_invoice would go live with the publish (D-83), unless
                         -AllowPendingCollateral is given.
      -Apply -WhatIf   The same refusals and census (read-only GETs), and "What if:" lines for every write; nothing is written.
      -Verify      READ-ONLY. Exit 0 only when neither control is on either form (and both forms were found); exit 1 otherwise.
                   -Apply ends with the same check.

    IDEMPOTENT. A second -Apply finds both forms clean, writes nothing, verifies and exits 0. A run stopped between the edit and the
    publish is re-run to continue: the edit is then pending (unpublished) on the form, it equals what this run would write, so it is
    taken as a resume, never as foreign work.

    UNPUBLISHED EDITS. The plain read of systemforms is the published layer and the new XML is built from it. The pending check lists
    everything unpublished on sprk_invoice WITHOUT excluding the two target forms; for a target form with pending changes the
    unpublished formxml is read (RetrieveUnpublished) and compared, after XML normalisation, with the text this run would write. Equal
    = a resume; anything else = refuse. The same check runs again right before the publish (a change that appears during the run is a
    plain error, exit 1, nothing published by that step). The comparisons are case-STRICT (a case-only change, e.g. of a label, is
    someone else's edit, not this run's text).

    IF DATAVERSE NORMALISES THE FORM XML ON SAVE. After each PATCH the script reads the unpublished text back and uses THAT as the text the
    pre-publish re-check expects, so the first live -Apply is not stopped (exit 1, after the writes) by a harmless normalisation. The
    pre-WRITE check of a re-run still compares the pending text with the text this run would write (the script cannot know how Dataverse
    would normalise it): after a stop before the publish, a pending text that Dataverse normalised differently is refused (exit 2,
    "has unpublished changes that are not the edit this script would write"). That mismatch means the pending text is not byte-for-byte
    this script's edit; inspect the form, and publish the pending edit in the form designer, which clears it, then re-run.

    EXIT CODES. 0 = done, report or verified. 1 = an error, or verification failed, or a stop AFTER the first write (re-run to resume).
    2 = refused BEFORE the first write (nothing changed). An ERROR before the first write (e.g. a failed read of the unpublished text)
    stays exit 1 (it is an error, not a refusal decision) and says "Nothing was written".

    Web API calls: the reads and the form edits go through EbillingSchema.psm1 (Consistency: Strong, #2833). The pending-collateral
    reads and PublishXml go through the shared scripts/lib/Publish-SolutionComponents.ps1, whose context carries no Consistency header
    of its own: Get-PublishContext sets Consistency: Strong on it (F4). PublishXml is wrapped in the module's bounded transient retry
    (Invoke-WithTransientRetry).
.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else dev. -Apply refuses anything but spaarkedev1.crm.dynamics.com.

.PARAMETER Apply
    Perform the form edit and the publish. Without it the script only reports.

.PARAMETER Verify
    Check that neither control is on either form (read-only; exit 1 while one remains).

.PARAMETER AllowPendingCollateral
    With -Apply: go on although other pending (unpublished) forms or views of sprk_invoice would be published with it (D-83). Give it
    only after the owner of those changes agrees.

.EXAMPLE
    pwsh scripts/ebilling/Remove-EbillingInvoiceExtractionFormControls.ps1
    pwsh scripts/ebilling/Remove-EbillingInvoiceExtractionFormControls.ps1 -Apply -WhatIf
    pwsh scripts/ebilling/Remove-EbillingInvoiceExtractionFormControls.ps1 -Apply -EnvironmentUrl https://spaarkedev1.crm.dynamics.com
    pwsh scripts/ebilling/Remove-EbillingInvoiceExtractionFormControls.ps1 -Verify

.NOTES
    Project: ebilling-r1, task 194. Exit codes: 0 = done / report / verified, 1 = error, verification failed, or stopped after the first
    write (re-run), 2 = refused before the first write. Other environments get the change through a SpaarkeMaster import.
#>

using module .\EbillingSchema.psm1

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = $(if ($env:DATAVERSE_URL) { $env:DATAVERSE_URL } else { 'https://spaarkedev1.crm.dynamics.com' }),
    [switch]$Apply,
    [switch]$Verify,
    [switch]$AllowPendingCollateral
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
# The shared D-83 procedure: Get-EntityPublishCollateral, Invoke-PublishXml (the only publish call the repo allows), Get-DataverseApiContext.
. (Join-Path $PSScriptRoot '..' 'lib' 'Publish-SolutionComponents.ps1')
$DryRun = [bool]$WhatIfPreference
$DevHost = 'spaarkedev1.crm.dynamics.com'
$script:WriteStarted = $false

$Table = 'sprk_invoice'
$Controls = @('sprk_extractionstatus', 'sprk_extractedjson')

# The two forms this script cleans, pinned by form id (the ids of the SpaarkeMaster source and of dev), with their type (2 main,
# 7 quick create). No other form is ever edited.
$ExpectedForms = @(
    @{ Id = '93aa1c69-0406-f111-8406-7c1e525abd8b'; Name = 'Invoice main form'; Type = 2; Label = 'main' }
    @{ Id = 'c5e5807d-e20c-f111-8342-7c1e520aa4df'; Name = 'Invoice quick create form'; Type = 7; Label = 'quick create' }
)

# A mention of one of the two columns in form text: the bare name. The name must not continue into a longer one.
$RetiredPattern = '(?i)(?<![A-Za-z0-9])sprk_(?:extractionstatus|extractedjson)(?![A-Za-z0-9])'

# ---------------------------------------------------------------------------------------------------------------
# Pre-flight (pure)
# ---------------------------------------------------------------------------------------------------------------

# Everything that makes -Apply refuse before any Dataverse call. Empty = go on.
function Get-PreflightRefusals([bool]$ApplyRequested, [string]$Url) {
    $r = [System.Collections.Generic.List[string]]::new()
    if (-not $ApplyRequested) { return $r.ToArray() }
    $hostName = try { ([uri]$Url).Host } catch { '' }
    if ($hostName -ne $DevHost) { $r.Add("-Apply is for dev only ($DevHost), not '$Url'") }
    return $r.ToArray()
}

# ---------------------------------------------------------------------------------------------------------------
# Pure transforms (XML text in, text out)
# ---------------------------------------------------------------------------------------------------------------

function Get-MentionCount([string]$Text) {
    if ([string]::IsNullOrEmpty($Text)) { return 0 }
    return [regex]::Matches($Text, $RetiredPattern).Count
}

# The distinct retired column names a text names, in the script's order.
function Get-MentionedControls([string]$Text) {
    if ([string]::IsNullOrEmpty($Text)) { return @() }
    return @($Controls | Where-Object { [regex]::IsMatch($Text, "(?i)(?<![A-Za-z0-9])$_(?![A-Za-z0-9])") })
}

# After a column was removed from a tab, the remaining columns share 100 % (the first takes the remainder): 2 columns = 50 / 50.
# Left alone when a width is not a plain percentage.
function Set-EvenColumnWidths($ColumnsNode) {
    $cols = @($ColumnsNode.SelectNodes('column'))
    if ($cols.Count -eq 0) { return }
    foreach ($c in $cols) { if ($c.GetAttribute('width') -notmatch '^\d+%$') { return } }
    $base = [math]::Floor(100 / $cols.Count)
    $first = 100 - ($base * ($cols.Count - 1))
    for ($i = 0; $i -lt $cols.Count; $i++) { $cols[$i].SetAttribute('width', ('{0}%' -f $(if ($i -eq 0) { $first } else { $base }))) }
}

# Removes each cell whose control is bound to one of the two columns, and the row, section and column it leaves empty. Problems = the
# shapes the script will not edit (the caller refuses on any); Remaining = mentions of the columns left in the result (an event handler,
# a header cell, anything else): the caller refuses on > 0.
function ConvertTo-FormWithoutRetiredControls([string]$FormXml) {
    $removed = [System.Collections.Generic.List[string]]::new()
    $problems = [System.Collections.Generic.List[string]]::new()
    try { $doc = [xml]$FormXml }
    catch { return [pscustomobject]@{ Xml = $FormXml; Removed = @(); Problems = @("the form xml does not parse: $($_.Exception.Message)"); Remaining = (Get-MentionCount $FormXml) } }
    if ($doc.DocumentElement.LocalName -ne 'form') { $problems.Add("the form xml root is <$($doc.DocumentElement.LocalName)>, not <form>") }
    $touched = [System.Collections.Generic.List[object]]::new()
    $xpath = '//control[' + (($Controls | ForEach-Object { "@datafieldname='$_'" }) -join ' or ') + ']'
    foreach ($ctl in @($doc.SelectNodes($xpath))) {
        $name = $ctl.GetAttribute('datafieldname')
        $cell = $ctl.SelectSingleNode('ancestor::cell[1]')
        $row = if ($cell) { $cell.ParentNode } else { $null }
        $rows = if ($row) { $row.ParentNode } else { $null }
        $section = if ($rows) { $rows.ParentNode } else { $null }
        if (-not $cell -or $row.LocalName -ne 'row' -or $rows.LocalName -ne 'rows' -or -not $section -or $section.LocalName -ne 'section') {
            $problems.Add("control $name is not in a cell of a row of a section (a header or another shape the script does not edit)")
            continue
        }
        [void]$row.RemoveChild($cell)
        $removed.Add("control $name")
        if ($row.SelectNodes('cell').Count -ne 0) { continue }
        [void]$rows.RemoveChild($row)
        $removed.Add('an emptied row')
        if ($rows.SelectNodes('row').Count -ne 0) { continue }
        # The section held nothing else: it goes, and so does a column left without sections.
        $sections = $section.ParentNode
        $column = if ($sections) { $sections.ParentNode } else { $null }
        [void]$sections.RemoveChild($section)
        $removed.Add("an emptied section ($($section.GetAttribute('name')))")
        if ($sections.SelectNodes('section').Count -ne 0) { continue }
        if (-not $column -or $column.LocalName -ne 'column') { $problems.Add("the section of control $name is not inside a column"); continue }
        $columns = $column.ParentNode
        [void]$columns.RemoveChild($column)
        $removed.Add('an emptied column')
        if (-not $touched.Contains($columns)) { $touched.Add($columns) }
    }
    foreach ($columns in $touched) {
        if ($columns.SelectNodes('column').Count -eq 0) { $problems.Add('removing the controls would leave a tab without columns'); continue }
        Set-EvenColumnWidths $columns
    }
    $xml = $doc.OuterXml
    return [pscustomobject]@{ Xml = $xml; Removed = $removed.ToArray(); Problems = $problems.ToArray(); Remaining = (Get-MentionCount $xml) }
}

# ---------------------------------------------------------------------------------------------------------------
# Census (read-only: everything here only GETs)
# ---------------------------------------------------------------------------------------------------------------

function Read-Dv([string]$Path) { return Invoke-Dv -Path $Path }

function ConvertTo-FormId([string]$Id) { return ([string]$Id).Trim('{', '}').ToLowerInvariant() }

# The same XML in a comparable shape: parsed and re-serialised, so insignificant whitespace between elements does not matter.
function ConvertTo-CanonicalXml([string]$Text) {
    if ([string]::IsNullOrWhiteSpace($Text)) { return '' }
    try { return ([xml]$Text).OuterXml } catch { return $Text }
}

function Get-State {
    $rows = @((Read-Dv "systemforms?`$select=name,formid,type,formxml&`$filter=objecttypecode eq '$Table'").value)
    # Pinned by id AND type: a form with the id but another type is not the expected form.
    $expected = @(foreach ($e in $ExpectedForms) {
        $hits = @($rows | Where-Object { (ConvertTo-FormId $_.formid) -eq (ConvertTo-FormId $e.Id) -and [int]$_.type -eq $e.Type })
        [pscustomobject]@{ Id = $e.Id; Name = $e.Name; Type = $e.Type; Label = $e.Label; Found = $hits.Count }
    })
    $forms = [System.Collections.Generic.List[object]]::new()
    $others = [System.Collections.Generic.List[object]]::new()
    foreach ($f in $rows) {
        $mentioned = @(Get-MentionedControls $f.formxml)
        if ($mentioned.Count -eq 0) { continue }
        $id = ConvertTo-FormId $f.formid
        $isTarget = @($ExpectedForms | Where-Object { ((ConvertTo-FormId $_.Id) -eq $id) -and ($_.Type -eq [int]$f.type) }).Count -eq 1
        if (-not $isTarget) { $others.Add([pscustomobject]@{ Id = $f.formid; Name = $f.name; Type = $f.type; Controls = $mentioned }); continue }
        $res = ConvertTo-FormWithoutRetiredControls $f.formxml
        $forms.Add([pscustomobject]@{
                Id = $f.formid; Name = $f.name; Type = [int]$f.type; Controls = $mentioned
                NewXml = $res.Xml; Removed = $res.Removed; Problems = $res.Problems; Remaining = $res.Remaining
            })
    }
    return [pscustomobject]@{ Expected = $expected; Forms = $forms.ToArray(); OtherForms = $others.ToArray() }
}

function Write-State($State) {
    foreach ($e in $State.Expected) { Write-Host "$Table $($e.Label) form '$($e.Name)' ($($e.Id)): $(if ($e.Found -eq 1) { 'found' } else { 'NOT FOUND' })" -ForegroundColor $(if ($e.Found -eq 1) { 'Cyan' } else { 'Red' }) }
    Write-Host "forms showing a retired control : $(@($State.Forms).Count)"
    foreach ($f in @($State.Forms)) {
        Write-Host "  form '$($f.Name)' ($($f.Id), type $($f.Type)): shows $($f.Controls -join ', '); would remove $($f.Removed -join ', '); mentions left $($f.Remaining)"
        foreach ($p in @($f.Problems)) { Write-Host "    problem: $p" -ForegroundColor Red }
    }
    Write-Host "other forms naming one (never edited; blockers): $(@($State.OtherForms).Count)"
    foreach ($o in @($State.OtherForms)) { Write-Host "  form '$($o.Name)' ($($o.Id), type $($o.Type)): $($o.Controls -join ', ')" -ForegroundColor Red }
}

# ---------------------------------------------------------------------------------------------------------------
# Pending (unpublished) changes of sprk_invoice (D-83). An entity publish carries ALL of them.
# ---------------------------------------------------------------------------------------------------------------

# The token context of the shared procedure (az CLI identity), made on first use. The shared context carries no Consistency header
# of its own: it is set here, so the pending-collateral reads and the PublishXml POST are strongly consistent too (F4, #2833).
function Get-PublishContext {
    if (-not $script:PublishContext) {
        $script:PublishContext = Get-DataverseApiContext -EnvironmentUrl $EnvironmentUrl
        $script:PublishContext.Headers['Consistency'] = 'Strong'
    }
    return $script:PublishContext
}

# The id inside the trailing parentheses of a collateral line ("systemforms: name (id)").
function Get-CollateralItemId([string]$Line) {
    if ($Line -match '\(([^()]+)\)\s*$') { return (ConvertTo-FormId $Matches[1]) }
    return ''
}

# Read only. Every pending item of sprk_invoice, the two target forms NOT excluded:
#   Own     = a target form that needs the edit and has unpublished changes that are NOT exactly the text this run writes (the unpublished
#             formxml is read and compared after XML normalisation). Publishing would overwrite them: never overridable.
#   Foreign = everything else that is pending (another form or view, or a target form that needs no edit): it would go live with the
#             publish. -AllowPendingCollateral may accept these.
# A target form whose pending text equals this run's text is a resume (or the edit this run just made): not reported.
function Get-PendingReport($State) {
    $own = [System.Collections.Generic.List[string]]::new()
    $foreign = [System.Collections.Generic.List[string]]::new()
    $targets = @{}
    foreach ($f in @($State.Forms)) { $targets[(ConvertTo-FormId $f.Id)] = $f }
    foreach ($line in @(Get-EntityPublishCollateral -Context (Get-PublishContext) -Entity $Table)) {
        $id = Get-CollateralItemId $line
        if (-not $targets.ContainsKey($id)) { $foreign.Add($line); continue }
        $f = $targets[$id]
        $unpublished = Read-Dv "systemforms($($f.Id))/Microsoft.Dynamics.CRM.RetrieveUnpublished()?`$select=formxml"
        # What this run expects to find pending: the text read back right after its own PATCH when there is one (K2: Dataverse may
        # normalise form XML on save), else the text it would write. Case-STRICT: a case-only edit is someone else's edit (-cne).
        $expected = if ($f.PSObject.Properties['WrittenXml']) { $f.WrittenXml } else { $f.NewXml }
        if ((ConvertTo-CanonicalXml $unpublished.formxml) -cne (ConvertTo-CanonicalXml $expected)) {
            $own.Add("form '$($f.Name)' ($($f.Id)) has unpublished changes that are not the edit this script would write (publishing would overwrite them): $line")
        }
    }
    return [pscustomobject]@{ Own = $own.ToArray(); Foreign = $foreign.ToArray() }
}

function Write-PendingReport($Pending) {
    $n = @($Pending.Own).Count + @($Pending.Foreign).Count
    Write-Host "pending (unpublished) items on ${Table}: $(if ($n -eq 0) { 'none' } else { $n })" -ForegroundColor $(if ($n -eq 0) { 'Cyan' } else { 'Yellow' })
    foreach ($x in @($Pending.Own)) { Write-Host "  OVERWRITE RISK  $x" -ForegroundColor Red }
    foreach ($x in @($Pending.Foreign)) { Write-Host "  would go live   $x" -ForegroundColor Yellow }
}

# ---------------------------------------------------------------------------------------------------------------
# Verdicts (pure: a state in, strings out)
# ---------------------------------------------------------------------------------------------------------------

# True when nothing is left to do: both expected forms exist and no form shows a control.
function Test-AlreadyClean($State) {
    foreach ($e in $State.Expected) { if ($e.Found -ne 1) { return $false } }
    return (@($State.Forms).Count + @($State.OtherForms).Count) -eq 0
}

# The reasons -Apply must refuse. Empty = READY (or already clean). $Pending = the Get-PendingReport result (when forms need the edit).
function Get-ApplyBlockers($State, $Pending = $null, [bool]$AllowPending = $false) {
    $b = [System.Collections.Generic.List[string]]::new()
    foreach ($e in $State.Expected) { if ($e.Found -ne 1) { $b.Add("expected $($e.Label) form '$($e.Name)' ($($e.Id), type $($e.Type)) was not found on ${Table}") } }
    foreach ($f in @($State.Forms)) {
        foreach ($p in @($f.Problems)) { $b.Add("form '$($f.Name)': $p") }
        if ($f.Remaining -gt 0) { $b.Add("form '$($f.Name)' would still name a retired control after the clean-up ($($f.Remaining) mention(s): an event handler or another place the script does not edit): edit it by hand") }
    }
    foreach ($o in @($State.OtherForms)) { $b.Add("form '$($o.Name)' ($($o.Id), type $($o.Type)) names $($o.Controls -join ', ') but is not one of the two forms this script edits: edit it by hand") }
    if ($Pending) {
        foreach ($x in @($Pending.Own)) { $b.Add($x) }
        if (-not $AllowPending) { foreach ($x in @($Pending.Foreign)) { $b.Add("pending (unpublished) item would go live with the publish (D-83; -AllowPendingCollateral after the owner agrees): $x") } }
    }
    return $b.ToArray()
}

# What -Verify (and the end of -Apply) reports as still there. Empty = clean.
function Get-VerifyProblems($State) {
    $p = [System.Collections.Generic.List[string]]::new()
    foreach ($e in $State.Expected) { if ($e.Found -ne 1) { $p.Add("expected $($e.Label) form '$($e.Name)' ($($e.Id)) was not found, so nothing can be confirmed") } }
    foreach ($f in @($State.Forms)) { $p.Add("form '$($f.Name)' still names $($f.Controls -join ', ')") }
    foreach ($o in @($State.OtherForms)) { $p.Add("form '$($o.Name)' ($($o.Id)) still names $($o.Controls -join ', ')") }
    return $p.ToArray()
}

# ---------------------------------------------------------------------------------------------------------------
# Writes (only reached through -Apply after the refusals are empty; each asks Confirm-Write first, so -WhatIf writes nothing)
# ---------------------------------------------------------------------------------------------------------------

function Confirm-Write([string]$Target, [string]$Action) { return $PSCmdlet.ShouldProcess($Target, $Action) }

function Edit-Forms($State) {
    $edited = 0
    foreach ($f in @($State.Forms)) {
        if (-not (Confirm-Write "form '$($f.Name)' ($($f.Id))" "PATCH formxml: remove $($f.Removed -join ', ')")) { continue }
        $script:WriteStarted = $true
        Invoke-Dv -Path "systemforms($($f.Id))" -Method PATCH -Body @{ formxml = $f.NewXml } | Out-Null
        $edited++; Write-Host "  EDITED   form '$($f.Name)': $($f.Removed -join ', ')" -ForegroundColor Green
        # K2: read the unpublished text back. It is what the pre-publish re-check expects, so a Dataverse that normalises form XML on
        # save does not stop the first run (exit 1) after the writes.
        $back = Read-Dv "systemforms($($f.Id))/Microsoft.Dynamics.CRM.RetrieveUnpublished()?`$select=formxml"
        if (-not [string]::IsNullOrWhiteSpace($back.formxml)) { $f | Add-Member -NotePropertyName WrittenXml -NotePropertyValue $back.formxml -Force }
    }
    if (@($State.Forms).Count -eq 0) { Write-Host '  nothing to edit: no form shows a retired control' -ForegroundColor Yellow }
    return $edited
}

# The only publish: sprk_invoice, entity-scoped, through the shared procedure. The pending items are re-read right before the publish; they
# were checked up front, so anything found here appeared during the run: a plain error (exit 1, partly applied), never an exit-2
# "nothing changed". The script's own edit (the target forms now pending with exactly this run's text) is not reported.
function Publish-Invoice($State, [bool]$AllowPending) {
    if (-not (Confirm-Write $Table 'PublishXml (entity-scoped, through scripts/lib/Publish-SolutionComponents.ps1, never tenant-wide)')) { return }
    $pending = Get-PendingReport $State
    if (@($pending.Own).Count -gt 0) {
        throw "Refusing to publish ${Table}: a target form changed during the run and publishing would overwrite it: $(@($pending.Own) -join '; '). Nothing was published by this step; the form edit is applied, so check the form and re-run to resume."
    }
    if (@($pending.Foreign).Count -gt 0 -and -not $AllowPending) {
        throw "Refusing to publish ${Table}: $(@($pending.Foreign).Count) other pending form(s) / view(s) appeared and would go live too: $(@($pending.Foreign) -join '; '). Nothing was published by this step; the form edit is applied, so re-run to resume after the owner of those changes agrees (-AllowPendingCollateral)."
    }
    # The shared Invoke-PublishXml has no retry of its own: a publish lock or throttling is retried with the module's transient test and
    # backoff (bounded); any other error fails at once.
    Invoke-WithTransientRetry { Invoke-PublishXml -Context (Get-PublishContext) -ParameterXml "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>" }
    Write-Host "  published $Table" -ForegroundColor Green
}

# ---------------------------------------------------------------------------------------------------------------
# Run (a function so the Pester tests can drive it against a test double of the Dataverse calls)
# ---------------------------------------------------------------------------------------------------------------

function Invoke-Cleanup([bool]$ApplyRequested, [bool]$VerifyRequested, [bool]$DryRunMode, [bool]$AllowPending, [string]$Url) {
    # First, before anything is initialised or read: -Apply is for dev only.
    $refusals = @(Get-PreflightRefusals $ApplyRequested $Url)
    if ($refusals.Count -gt 0) { throw [EscalationException]::new("Refusing to apply: $($refusals.Count) reason(s): $($refusals -join '; '). Nothing was changed.") }
    $null = Initialize-EbillingSchema -EnvironmentUrl $Url -SolutionUniqueName 'SpaarkeMaster' -DryRun:$DryRunMode -Entity $Table

    $state = Get-State
    Write-State $state
    # The pending check matters only when a publish would follow (something to edit); the two target forms are NOT excluded.
    $pending = [pscustomobject]@{ Own = @(); Foreign = @() }
    if (@($state.Forms).Count -gt 0) { $pending = Get-PendingReport $state }
    if (@($state.Forms).Count -gt 0) { Write-PendingReport $pending }
    $blockers = @(Get-ApplyBlockers $state $pending $AllowPending)
    $clean = Test-AlreadyClean $state
    Write-Host ''
    if ($clean) { Write-Host 'VERDICT: ALREADY CLEAN - neither control is on the invoice main or quick-create form' -ForegroundColor Yellow }
    elseif ($blockers.Count -eq 0) { Write-Host 'VERDICT: READY' -ForegroundColor Green }
    else { Write-Host 'VERDICT: BLOCKED' -ForegroundColor Red; foreach ($x in $blockers) { Write-Host "  - $x" -ForegroundColor Red } }

    if (-not $ApplyRequested -and -not $VerifyRequested) {
        Write-Host ''
        Write-Host 'Report only. Nothing was changed. To take the controls off the live forms: -Apply (add -WhatIf for a dry run).' -ForegroundColor Cyan
        return
    }

    if ($ApplyRequested) {
        Write-Host ''
        Write-Host 'Apply' -ForegroundColor Cyan
        if ($clean) { Write-Host '  nothing to do: already clean' -ForegroundColor Yellow }
        else {
            if ($blockers.Count -gt 0) { throw [EscalationException]::new("Refusing to apply: $($blockers.Count) reason(s): $($blockers -join '; '). Nothing was changed.") }
            if (@($pending.Foreign).Count -gt 0) { Write-Host "  -AllowPendingCollateral: publishing $(@($pending.Foreign).Count) other pending item(s) of $Table too" -ForegroundColor Yellow }

            Write-Host '1. Forms' -ForegroundColor Cyan
            $null = Edit-Forms $state
            Write-Host '2. Publish' -ForegroundColor Cyan
            Publish-Invoice $state $AllowPending
        }
    }

    if ($VerifyRequested -or ($ApplyRequested -and -not $DryRunMode)) {
        Write-Host ''
        Write-Host 'Verify' -ForegroundColor Cyan
        $problems = @()
        for ($i = 0; $i -lt 4; $i++) {
            $problems = @(Get-VerifyProblems (Get-State))
            if ($problems.Count -eq 0) { break }
            # A just-published form can read stale once; -Verify on its own does not wait.
            if ($ApplyRequested) { Start-Sleep -Seconds 5 } else { break }
        }
        if ($problems.Count -gt 0) { throw "Verification failed: $($problems -join '; ')" }
        Write-Host '  neither sprk_extractionstatus nor sprk_extractedjson is on the invoice main or quick-create form' -ForegroundColor Green
    }
}

# What the error handler tells the operator: after the first write the run is partly applied (re-run to resume); before it, nothing was
# written (an error in a read, e.g. the pending read, changes nothing). $script:WriteStarted is set right before the first PATCH.
function Get-ErrorTrailer {
    if ($script:WriteStarted) { return 'Steps already done stay done; re-run to continue (every step skips work already done).' }
    return 'Nothing was written (the error happened before the first write).'
}

# ---------------------------------------------------------------------------------------------------------------
# Entry
# ---------------------------------------------------------------------------------------------------------------

Write-Host ''
Write-Host "eBilling task 194 - take the retired extraction controls off the $Table forms" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Mode: $(if ($Apply -and $DryRun) { 'APPLY -WhatIf (no writes; read-only GETs still run)' } elseif ($Apply) { 'APPLY' } elseif ($Verify) { 'VERIFY (read-only)' } else { 'REPORT (read-only)' })"
Write-Host ''

try {
    Invoke-Cleanup ([bool]$Apply) ([bool]$Verify) $DryRun ([bool]$AllowPendingCollateral) $EnvironmentUrl
}
catch [EscalationException] {
    Write-Host ''
    Write-Host "ESCALATION - REFUSED: $($_.Exception.Message)" -ForegroundColor Red
    exit 2
}
catch {
    Write-Host ''
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host (Get-ErrorTrailer) -ForegroundColor Red
    exit 1
}

Write-Host ''
if ($Apply -and $DryRun) { Write-Host 'WhatIf run: nothing was written (read-only GETs against Dataverse did run).' -ForegroundColor Cyan }
exit 0
