#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Task 154 (unified-access-control-r2): makes the No Access list manageable in the model-driven app. Rebuilds the
    sprk_noaccessentry main form and registers its library, completes the "Active No Access Entries" view, adds the
    NO ACCESS subgrids to the Organization and Contact main forms, and adds a "No Access Entries" site map entry.
    DRY RUN by default.

.DESCRIPTION
    WHAT IT CHANGES (each step is skipped when the live state already matches)
      1. Quick create. sprk_noaccessentry must have quick create OFF, so a subgrid "+ New" opens the main form, which
         carries the shape check and the post-save enforcement. It is switched off when found on.
      2. The view "Active No Access Entries" (the table's default public view, generated with the table): active rows
         only, with the subject and object columns. It is the view the three subgrids and the site map entry open.
      3. The sprk_noaccessentry main form: SUBJECT (contact / organization / user), OBJECT (organization, or record
         type + record id), REASON; status and author in the header. Libraries: sprk_/scripts/bff_auth.js, then
         sprk_/scripts/noaccessentry_postsave.js, with ONE OnLoad handler, Spaarke.NoAccessEntry.onLoad, which
         registers OnSave (shape check), OnPostSave (task 143's enforcement) and every OnChange.
      4. The Organization main form the app exposes: a NO ACCESS section after MEMBERS, on the SYSTEM ACCESS tab, with
         "Ethical walls on this organization" (sprk_objectorganization = this) and "This organization denied"
         (sprk_subjectorganization = this).
      5. The Contact main form the app exposes: a NO ACCESS section after ORGANIZATIONS with "This contact denied"
         (sprk_subjectcontact = this).
      6. The app's site map: a "No Access Entries" subarea after "Access Permission Grants", which requires Read on
         sprk_noaccessentry, and the table as an app component.
      Then it publishes what it changed.

    WHAT IT REFUSES (exit 2, nothing written)
      - The library sprk_/scripts/noaccessentry_postsave.js on the server is not version 1.1.0 or later (deploy it first
        with scripts/Deploy-WebResourceInline.ps1), or sprk_/scripts/bff_auth.js is missing.
      - sprk_noaccessentry has more than one main form, or the app exposes more than one main form of Organization or
        Contact (task 154 escalation (f)).
      - The live entry form shows a field the new layout would drop.
      - The SYSTEM ACCESS / MEMBERS sections, or the site map's Access Permission Grants entry, are not where expected.

    MODES
      (default)    Dry run: prints the plan. Zero writes.
      -Apply       Writes a snapshot of every form, view and site map it will change (scripts/logs/), then writes and
                   publishes, then reads back.
      -Verify      Read-only. Exit 0 when every step is in place, exit 1 naming each gap.
      -RestoreFrom Writes a snapshot's form, view and site map content back and publishes.

    OPERATOR-RUN ONLY. Run by the main session after the PR merges, in this order:
      1. scripts/Deploy-WebResourceInline.ps1 -DataverseUrl <env> -WebResourceName sprk_/scripts/noaccessentry_postsave.js
            -FilePath src/solutions/webresources/sprk_noaccessentry_postsave.js -WebResourceType 3
      2. this script (dry run), then -Apply, then -Verify
      3. scripts/Set-NoAccessEntryRolePrivileges.ps1 (owner decision O2) - dry run, -Apply, -Verify

.PARAMETER EnvironmentUrl
    Dataverse environment URL.

.PARAMETER OrganizationFormName
    The Spaarke Organization main form to edit. It must be one of the main forms the app exposes.

.PARAMETER ContactFormName
    The Spaarke Contact main form to edit. It must be one of the main forms the app exposes. On spaarkedev1 the app also
    exposes two Power Pages profile forms ("Profile Web Form (Enhanced)", "... - Japanese"); those are portal forms and
    are never edited here (task 154 escalation (f), reported to the owner).

.PARAMETER PlanOut
    Dry run only: a folder to write each planned form, view and site map XML to, for review before -Apply.

.PARAMETER AppUniqueName
    The model-driven app whose site map lists Access Permission Grants and Organizations. On spaarkedev1 exactly one
    app qualifies: sprk_MatterManagement ("Matter Management"), task 154 step 1.

.EXAMPLE
    pwsh -File scripts/Deploy-NoAccessEntryForms.ps1
    pwsh -File scripts/Deploy-NoAccessEntryForms.ps1 -Apply
    pwsh -File scripts/Deploy-NoAccessEntryForms.ps1 -Verify
#>

[CmdletBinding()]
param(
    [string]$EnvironmentUrl = 'https://spaarkedev1.crm.dynamics.com',
    [string]$AppUniqueName = 'sprk_MatterManagement',
    [string]$OrganizationFormName = 'Organization main form',
    [string]$ContactFormName = 'Contact main form',
    [switch]$Apply,
    [switch]$Verify,
    [string]$RestoreFrom,
    [string]$SnapshotDir = (Join-Path $PSScriptRoot 'logs'),
    [string]$PlanOut
)

$ErrorActionPreference = 'Stop'
$BaseUrl = $EnvironmentUrl.TrimEnd('/')
$modes = @($Apply.IsPresent, $Verify.IsPresent, -not [string]::IsNullOrEmpty($RestoreFrom)) | Where-Object { $_ }
if (@($modes).Count -gt 1) { throw '-Apply, -Verify and -RestoreFrom are separate modes; pass at most one.' }

$Table = 'sprk_noaccessentry'
$ViewName = 'Active No Access Entries'
$AuthLibrary = 'sprk_/scripts/bff_auth.js'
$FormLibrary = 'sprk_/scripts/noaccessentry_postsave.js'
$OnLoadHandler = 'Spaarke.NoAccessEntry.onLoad'
$MinLibraryVersion = [version]'1.1.0'
$SectionName = 'section_noaccess'

$ClassText = '{4273EDBD-AC1D-40D3-9FB2-095C621B552D}'
$ClassLookup = '{270BD3DB-D9AF-4782-9025-509E298DEC0A}'
$ClassMemo = '{E0DECE4B-6FC8-4A8F-A065-082708572369}'
$ClassPicklist = '{3EF39988-22BB-4F0B-BBBE-64B5A3748AEE}'
$ClassDateTime = '{5B773807-9FB2-42DB-97C3-7A91EFF8ADFF}'
$ClassSubgrid = '{E7A81278-8635-4D9E-8D4D-59480B391C5B}'

# ---------------------------------------------------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------------------------------------------------

function Get-DataverseToken {
    $t = az account get-access-token --resource $BaseUrl --query accessToken -o tsv 2>&1
    if ($LASTEXITCODE -ne 0 -or -not $t) { throw "No Dataverse token for $BaseUrl ($t). Run 'az login' first." }
    return "$t".Trim()
}

function Invoke-Dv {
    param([string]$Endpoint, [string]$Method = 'GET', [object]$Body = $null, [hashtable]$ExtraHeaders = @{})
    $headers = @{
        Authorization      = "Bearer $Token"
        'OData-MaxVersion' = '4.0'
        'OData-Version'    = '4.0'
        Accept             = 'application/json'
        'Content-Type'     = 'application/json; charset=utf-8'
    }
    foreach ($k in $ExtraHeaders.Keys) { $headers[$k] = $ExtraHeaders[$k] }
    $params = @{ Uri = "$BaseUrl/api/data/v9.2/$Endpoint"; Method = $Method; Headers = $headers }
    if ($null -ne $Body) {
        $json = if ($Body -is [string]) { $Body } else { $Body | ConvertTo-Json -Depth 30 }
        $params.Body = [Text.Encoding]::UTF8.GetBytes($json)
    }
    try { return Invoke-RestMethod @params }
    catch {
        $detail = $_.Exception.Message
        if ($_.ErrorDetails.Message) {
            $err = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue
            if ($err.error.message) { $detail = $err.error.message }
        }
        throw "API error ($Method $Endpoint): $detail"
    }
}

function Stop-Refused([string]$Reason) {
    Write-Host ''
    Write-Host "REFUSED: $Reason" -ForegroundColor Red
    Write-Host 'Nothing was changed.' -ForegroundColor Red
    exit 2
}

function Write-Step([string]$Text) { Write-Host ''; Write-Host "== $Text" -ForegroundColor Cyan }
function Write-Ok([string]$Text) { Write-Host "   ok    $Text" -ForegroundColor Green }
function Write-Plan([string]$Text) { Write-Host "   PLAN  $Text" -ForegroundColor Yellow }
function Write-Gap([string]$Text) { $script:Gaps.Add($Text); Write-Host "   GAP   $Text" -ForegroundColor Red }

function New-StableGuid([string]$Seed) {
    # A deterministic id per purpose: a re-run writes identical XML, so the transform is idempotent.
    $bytes = [System.Security.Cryptography.MD5]::HashData([Text.Encoding]::UTF8.GetBytes("uac-r2-154|$Seed"))
    return '{' + ([guid]::new($bytes)).ToString() + '}'
}

function ConvertTo-XmlDoc([string]$Xml) {
    $doc = [System.Xml.XmlDocument]::new()
    $doc.PreserveWhitespace = $true
    $doc.XmlResolver = $null
    $doc.LoadXml($Xml)
    return $doc
}

function Get-Braced([string]$Id) { return '{' + $Id.Trim('{', '}').ToUpperInvariant() + '}' }

# ---------------------------------------------------------------------------------------------------------------------
# Target XML
# ---------------------------------------------------------------------------------------------------------------------

function Get-ViewFetchXml {
    return '<fetch version="1.0" mapping="logical"><entity name="sprk_noaccessentry">' +
    '<attribute name="sprk_noaccessentryid" /><attribute name="sprk_name" />' +
    '<attribute name="sprk_subjectcontact" /><attribute name="sprk_subjectorganization" /><attribute name="sprk_subjectsystemuser" />' +
    '<attribute name="sprk_objectorganization" /><attribute name="sprk_objectrecordtype" /><attribute name="sprk_objectrecordid" />' +
    '<attribute name="createdby" /><attribute name="createdon" />' +
    '<order attribute="createdon" descending="true" />' +
    '<filter type="and"><condition attribute="statecode" operator="eq" value="0" /></filter>' +
    '</entity></fetch>'
}

$ViewColumns = @('sprk_name', 'sprk_subjectcontact', 'sprk_subjectorganization', 'sprk_subjectsystemuser',
    'sprk_objectorganization', 'sprk_objectrecordtype', 'sprk_objectrecordid', 'createdby', 'createdon')

function Get-ViewLayoutXml([int]$ObjectTypeCode) {
    $widths = @{ sprk_name = 250; sprk_objectrecordid = 150; createdon = 125 }
    $cells = ($ViewColumns | ForEach-Object {
            $w = if ($widths.ContainsKey($_)) { $widths[$_] } else { 150 }
            "<cell name=""$_"" width=""$w"" />"
        }) -join ''
    return "<grid name=""resultset"" object=""$ObjectTypeCode"" jump=""sprk_name"" select=""1"" icon=""1"" preview=""1"">" +
    "<row name=""result"" id=""sprk_noaccessentryid"">$cells</row></grid>"
}

function Get-FieldCell([string]$Field, [string]$Label, [string]$ClassId, [int]$RowSpan = 1) {
    $cell = New-StableGuid "entry-cell|$Field"
    return "<row><cell id=""$cell"" showlabel=""true"" locklevel=""0"" colspan=""1"" rowspan=""$RowSpan"">" +
    "<labels><label description=""$Label"" languagecode=""1033"" /></labels>" +
    "<control id=""$Field"" classid=""$ClassId"" datafieldname=""$Field"" disabled=""false"" /></cell></row>"
}

function Get-EntrySection([string]$Name, [string]$Label, [bool]$ShowLabel, [string]$Rows) {
    $id = New-StableGuid "entry-section|$Name"
    $show = if ($ShowLabel) { 'true' } else { 'false' }
    return "<section name=""$Name"" id=""$id"" IsUserDefined=""0"" locklevel=""0"" showlabel=""$show"" showbar=""false"" " +
    "layout=""varwidth"" celllabelalignment=""Left"" celllabelposition=""Left"" columns=""1"" labelwidth=""140"">" +
    "<labels><label description=""$Label"" languagecode=""1033"" /></labels><rows>$Rows</rows></section>"
}

function Get-HeaderCell([string]$Field, [string]$Label, [string]$ClassId) {
    $cell = New-StableGuid "entry-header|$Field"
    return "<cell id=""$cell"" showlabel=""true"" locklevel=""0"" colspan=""1"" rowspan=""1"">" +
    "<labels><label description=""$Label"" languagecode=""1033"" /></labels>" +
    "<control id=""header_$Field"" classid=""$ClassId"" datafieldname=""$Field"" disabled=""true"" /></cell>"
}

function Get-EntryFormXml {
    $name = Get-EntrySection 'section_entry' 'ENTRY' $false (Get-FieldCell 'sprk_name' 'Name' $ClassText)
    $subject = Get-EntrySection 'section_subject' 'SUBJECT - WHO IS DENIED (EXACTLY ONE)' $true (
        (Get-FieldCell 'sprk_subjectcontact' 'Contact' $ClassLookup) +
        (Get-FieldCell 'sprk_subjectorganization' 'Organization' $ClassLookup) +
        (Get-FieldCell 'sprk_subjectsystemuser' 'User' $ClassLookup))
    $object = Get-EntrySection 'section_object' 'OBJECT - AN ORGANIZATION, OR ONE RECORD' $true (
        (Get-FieldCell 'sprk_objectorganization' 'Organization' $ClassLookup) +
        (Get-FieldCell 'sprk_objectrecordtype' 'Record Type (opens the record picker)' $ClassLookup) +
        (Get-FieldCell 'sprk_objectrecordid' 'Record Id' $ClassText))
    $reason = Get-EntrySection 'section_reason' 'REASON' $true (
        (Get-FieldCell 'sprk_reason' 'Reason' $ClassMemo 3) + '<row /><row />')

    $tab = "<tab name=""tab_general"" id=""$(New-StableGuid 'entry-tab')"" IsUserDefined=""0"" locklevel=""0"" " +
    "showlabel=""false"" expanded=""true"" verticallayout=""true""><labels><label description=""General"" languagecode=""1033"" /></labels>" +
    "<columns><column width=""100%""><sections>$name$subject$object$reason</sections></column></columns></tab>"

    $header = "<header id=""$(New-StableGuid 'entry-header')"" celllabelposition=""Top"" columns=""111"" labelwidth=""115"" " +
    "celllabelalignment=""Left""><rows><row>" +
    (Get-HeaderCell 'statecode' 'Status' $ClassPicklist) +
    (Get-HeaderCell 'modifiedby' 'Author (last modified by)' $ClassLookup) +
    (Get-HeaderCell 'modifiedon' 'Modified On' $ClassDateTime) +
    '</row></rows></header>'

    $libraries = "<formLibraries><Library name=""$AuthLibrary"" libraryUniqueId=""$(New-StableGuid 'entry-lib-auth')"" />" +
    "<Library name=""$FormLibrary"" libraryUniqueId=""$(New-StableGuid 'entry-lib-form')"" /></formLibraries>"
    $events = '<events><event name="onload" application="false" active="false"><Handlers>' +
    "<Handler functionName=""$OnLoadHandler"" libraryName=""$FormLibrary"" handlerUniqueId=""$(New-StableGuid 'entry-onload')"" " +
    'enabled="true" parameters="" passExecutionContext="true" /></Handlers></event></events>'

    return "<form><tabs>$tab</tabs>$header$libraries$events</form>"
}

$EntryFields = @('sprk_name', 'sprk_subjectcontact', 'sprk_subjectorganization', 'sprk_subjectsystemuser',
    'sprk_objectorganization', 'sprk_objectrecordtype', 'sprk_objectrecordid', 'sprk_reason')

function Get-SubgridCell([string]$ControlId, [string]$Label, [string]$Relationship, [string]$ViewId) {
    $view = Get-Braced $ViewId
    return "<row><cell id=""$(New-StableGuid "cell|$ControlId")"" showlabel=""true"" locklevel=""0"" colspan=""1"" rowspan=""4"" auto=""false"">" +
    "<labels><label description=""$Label"" languagecode=""1033"" /></labels>" +
    "<control indicationOfSubgrid=""true"" id=""$ControlId"" classid=""$ClassSubgrid""><parameters>" +
    '<RecordsPerPage>5</RecordsPerPage><AutoExpand>Fixed</AutoExpand><EnableQuickFind>false</EnableQuickFind>' +
    '<EnableViewPicker>false</EnableViewPicker><EnableChartPicker>false</EnableChartPicker><ChartGridMode>Grid</ChartGridMode>' +
    "<RelationshipName>$Relationship</RelationshipName><TargetEntityType>$Table</TargetEntityType>" +
    "<ViewId>$view</ViewId><ViewIds>$view</ViewIds></parameters></control></cell></row><row /><row /><row />"
}

function Get-NoAccessSection([string]$FormKey, [string]$Rows) {
    return "<section name=""$SectionName"" id=""$(New-StableGuid "section|$FormKey")"" IsUserDefined=""0"" locklevel=""0"" " +
    'showlabel="true" showbar="false" layout="varwidth" celllabelalignment="Left" celllabelposition="Top" columns="1" labelwidth="115">' +
    "<labels><label description=""NO ACCESS"" languagecode=""1033"" /></labels><rows>$Rows</rows></section>"
}

# ---------------------------------------------------------------------------------------------------------------------
# Form inspection
# ---------------------------------------------------------------------------------------------------------------------

function Get-Subgrids([System.Xml.XmlDocument]$Doc) {
    return @($Doc.SelectNodes("//control[@classid]") | Where-Object {
            $_.GetAttribute('classid').ToUpperInvariant() -eq $ClassSubgrid
        } | ForEach-Object {
            [pscustomobject]@{
                Node         = $_
                Id           = $_.GetAttribute('id')
                Relationship = $_.SelectSingleNode('parameters/RelationshipName').InnerText
                Target       = $_.SelectSingleNode('parameters/TargetEntityType').InnerText
                ViewId       = "$($_.SelectSingleNode('parameters/ViewId').InnerText)".Trim('{', '}').ToLowerInvariant()
            }
        })
}

function Get-EntryFormGaps([string]$FormXml) {
    $gaps = [System.Collections.Generic.List[string]]::new()
    $doc = ConvertTo-XmlDoc $FormXml
    foreach ($f in $EntryFields) {
        if (-not $doc.SelectSingleNode("/form/tabs//control[@datafieldname='$f']")) { $gaps.Add("field $f is not on the form") }
    }
    $libs = @($doc.SelectNodes('/form/formLibraries/Library') | ForEach-Object { $_.GetAttribute('name') })
    if (($libs -join '|') -ne "$AuthLibrary|$FormLibrary") {
        $gaps.Add("libraries are [$($libs -join ', ')], expected [$AuthLibrary, $FormLibrary] in that order")
    }
    $onload = @($doc.SelectNodes("/form/events/event[@name='onload']/Handlers/Handler"))
    $ok = $onload.Count -eq 1 -and $onload[0].GetAttribute('functionName') -eq $OnLoadHandler -and
    $onload[0].GetAttribute('libraryName') -eq $FormLibrary -and $onload[0].GetAttribute('passExecutionContext') -eq 'true' -and
    $onload[0].GetAttribute('enabled') -eq 'true'
    if (-not $ok) { $gaps.Add("OnLoad must have exactly one handler, $OnLoadHandler from $FormLibrary, passing the execution context") }
    $other = @($doc.SelectNodes("/form/events/event[@name!='onload']/Handlers/Handler"))
    if ($other.Count -gt 0) { $gaps.Add("form-level handlers other than OnLoad are registered ($($other.Count)); the library registers its own") }
    return , $gaps
}

function Get-SubgridGaps([string]$FormXml, [hashtable[]]$Expected, [string]$ViewId) {
    $gaps = [System.Collections.Generic.List[string]]::new()
    $grids = Get-Subgrids (ConvertTo-XmlDoc $FormXml)
    foreach ($e in $Expected) {
        $g = @($grids | Where-Object { $_.Target -eq $Table -and $_.Relationship -eq $e.Relationship })
        if ($g.Count -eq 0) { $gaps.Add("no '$($e.Label)' subgrid ($($e.Relationship))"); continue }
        if ($g[0].ViewId -ne $ViewId.ToLowerInvariant()) { $gaps.Add("'$($e.Label)' subgrid opens view $($g[0].ViewId), expected '$ViewName' $ViewId") }
    }
    return , $gaps
}

# Inserts the NO ACCESS section after the section holding the subgrid on $AnchorTarget. Returns the new XML.
function Add-NoAccessSection([string]$FormXml, [string]$AnchorTarget, [string]$FormKey, [string]$Rows) {
    $doc = ConvertTo-XmlDoc $FormXml
    $anchor = @(Get-Subgrids $doc | Where-Object { $_.Target -eq $AnchorTarget })
    if ($anchor.Count -ne 1) { return $null }
    $section = $anchor[0].Node.SelectSingleNode('ancestor::section[1]')
    if (-not $section) { return $null }
    if ($section.ParentNode.SelectSingleNode("section[@name='$SectionName']")) {
        # A NO ACCESS section exists but lacks a subgrid: replace it, so the run converges.
        $section.ParentNode.RemoveChild($section.ParentNode.SelectSingleNode("section[@name='$SectionName']")) | Out-Null
    }
    $frag = $doc.CreateDocumentFragment()
    $frag.InnerXml = Get-NoAccessSection $FormKey $Rows
    $section.ParentNode.InsertAfter($frag, $section) | Out-Null
    return $doc.OuterXml
}

function Get-AppForm([string]$Entity, [string]$FormName) {
    $forms = @((Invoke-Dv "systemforms?`$filter=objecttypecode eq '$Entity' and type eq 2 and formactivationstate eq 1&`$select=formid,name,formxml,ismanaged").value)
    $inApp = @($forms | Where-Object { $AppFormIds -contains "$($_.formid)".ToLowerInvariant() })
    if ($AppFormIds.Count -eq 0) { $inApp = $forms } # an app with no form list exposes every main form
    $named = @($inApp | Where-Object { $_.name -eq $FormName })
    $others = @($inApp | Where-Object { $_.name -ne $FormName } | ForEach-Object name)
    if ($others.Count -gt 0) {
        Write-Host "   note  the app also exposes these $Entity main forms, which are NOT edited (escalation (f)): $($others -join ', ')" -ForegroundColor DarkYellow
    }
    if ($named.Count -ne 1) {
        Stop-Refused "the app exposes $($named.Count) active main forms of $Entity named '$FormName' (it exposes: $(($inApp | ForEach-Object name) -join ', ')). Pass the right -$(if ($Entity -eq 'contact') { 'Contact' } else { 'Organization' })FormName."
    }
    return $named[0]
}

# ---------------------------------------------------------------------------------------------------------------------
# Start
# ---------------------------------------------------------------------------------------------------------------------

$Token = Get-DataverseToken
$Gaps = [System.Collections.Generic.List[string]]::new()
$org = (Invoke-Dv 'organizations?$select=name').value[0].name
$modeText = if ($Verify) { 'VERIFY (read-only)' } elseif ($Apply) { 'APPLY' } elseif ($RestoreFrom) { "RESTORE from $RestoreFrom" } else { 'DRY RUN (no writes)' }
Write-Host "Deploy-NoAccessEntryForms (task 154)  env: $BaseUrl (org '$org')  mode: $modeText" -ForegroundColor White

if ($RestoreFrom) {
    if (-not (Test-Path -LiteralPath $RestoreFrom)) { Stop-Refused "snapshot '$RestoreFrom' does not exist." }
    $snap = Get-Content -LiteralPath $RestoreFrom -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($f in @($snap.forms)) { Invoke-Dv "systemforms($($f.formid))" 'PATCH' @{ formxml = $f.formxml } | Out-Null; Write-Ok "form $($f.name) restored" }
    if ($snap.view) {
        Invoke-Dv "savedqueries($($snap.view.savedqueryid))" 'PATCH' @{ fetchxml = $snap.view.fetchxml; layoutxml = $snap.view.layoutxml } | Out-Null
        Write-Ok 'view restored'
    }
    if ($snap.sitemap) { Invoke-Dv "sitemaps($($snap.sitemap.sitemapid))" 'PATCH' @{ sitemapxml = $snap.sitemap.sitemapxml } | Out-Null; Write-Ok 'site map restored' }
    $publish = "<importexportxml><entities><entity>$Table</entity><entity>sprk_organization</entity><entity>contact</entity></entities>" +
    $(if ($snap.sitemap) { "<sitemaps><sitemap>{$($snap.sitemap.sitemapid)}</sitemap></sitemaps>" } else { '' }) + '</importexportxml>'
    Invoke-Dv 'PublishXml' 'POST' @{ ParameterXml = $publish } | Out-Null
    Write-Ok 'published. The quick-create setting and the app component are not reverted by a restore.'
    exit 0
}

# ---- Prerequisites ----------------------------------------------------------------------------------------------------
Write-Step 'Prerequisites'
$meta = Invoke-Dv "EntityDefinitions(LogicalName='$Table')?`$select=MetadataId,ObjectTypeCode,IsQuickCreateEnabled"
Write-Ok "table $Table (type code $($meta.ObjectTypeCode))"

$rels = @{}
foreach ($r in (Invoke-Dv "EntityDefinitions(LogicalName='$Table')/ManyToOneRelationships?`$select=SchemaName,ReferencingAttribute").value) {
    $rels[$r.ReferencingAttribute] = $r.SchemaName
}
foreach ($a in 'sprk_objectorganization', 'sprk_subjectorganization', 'sprk_subjectcontact') {
    if (-not $rels[$a]) { Stop-Refused "no relationship for $Table.$a." }
}
Write-Ok "relationships: $($rels['sprk_objectorganization']), $($rels['sprk_subjectorganization']), $($rels['sprk_subjectcontact'])"

foreach ($lib in $AuthLibrary, $FormLibrary) {
    $wr = @((Invoke-Dv "webresourceset?`$filter=name eq '$lib'&`$select=webresourceid,content").value)
    if ($wr.Count -ne 1) {
        if ($Verify) { Write-Gap "web resource $lib is not in the environment"; continue }
        Stop-Refused "web resource $lib is not in the environment. Deploy it first (scripts/Deploy-WebResourceInline.ps1)."
    }
    if ($lib -eq $FormLibrary) {
        $text = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($wr[0].content))
        $m = [regex]::Match($text, 'version:\s*"(\d+\.\d+\.\d+)"')
        if (-not $m.Success -or [version]$m.Groups[1].Value -lt $MinLibraryVersion) {
            if ($Verify) { Write-Gap "$lib on the server is version '$($m.Groups[1].Value)', expected $MinLibraryVersion or later"; continue }
            Stop-Refused "$lib on the server is version '$($m.Groups[1].Value)'; the form needs $MinLibraryVersion or later (the shape check and the record picker). Deploy src/solutions/webresources/sprk_noaccessentry_postsave.js first."
        }
        Write-Ok "$lib version $($m.Groups[1].Value)"
    }
    else { Write-Ok "$lib present" }
}

$app = @((Invoke-Dv "appmodules?`$filter=uniquename eq '$AppUniqueName'&`$select=appmoduleid,appmoduleidunique,name").value)
if ($app.Count -ne 1) { Stop-Refused "expected one app '$AppUniqueName'; found $($app.Count) (task 154 escalation (c))." }
$app = $app[0]
$components = @((Invoke-Dv "appmodulecomponents?`$filter=_appmoduleidunique_value eq $($app.appmoduleidunique)&`$select=componenttype,objectid").value)
$AppFormIds = @($components | Where-Object componenttype -eq 60 | ForEach-Object { "$($_.objectid)".ToLowerInvariant() })
$tableInApp = @($components | Where-Object { $_.componenttype -eq 1 -and "$($_.objectid)" -eq "$($meta.MetadataId)" }).Count -gt 0
Write-Ok "app $($app.name) ($AppUniqueName)"

$sitemap = @((Invoke-Dv "sitemaps?`$filter=sitemapnameunique eq '$AppUniqueName'&`$select=sitemapid,sitemapxml").value)
if ($sitemap.Count -ne 1) { Stop-Refused "expected one site map '$AppUniqueName'; found $($sitemap.Count)." }
$sitemap = $sitemap[0]

$views = @((Invoke-Dv "savedqueries?`$filter=returnedtypecode eq '$Table' and querytype eq 0 and name eq '$ViewName'&`$select=savedqueryid,fetchxml,layoutxml,isdefault").value)
if ($views.Count -ne 1) { Stop-Refused "expected one public view '$ViewName' on $Table; found $($views.Count)." }
$view = $views[0]
$ViewId = "$($view.savedqueryid)".ToLowerInvariant()

$entryForms = @((Invoke-Dv "systemforms?`$filter=objecttypecode eq '$Table' and type eq 2&`$select=formid,name,formxml").value)
if ($entryForms.Count -ne 1) { Stop-Refused "$Table has $($entryForms.Count) main forms; expected one (escalation (f))." }
$entryForm = $entryForms[0]
$orgForm = Get-AppForm 'sprk_organization' $OrganizationFormName
$contactForm = Get-AppForm 'contact' $ContactFormName
Write-Ok "forms: entry '$($entryForm.name)', organization '$($orgForm.name)', contact '$($contactForm.name)'"

# ---- Plan --------------------------------------------------------------------------------------------------------------
$writes = [ordered]@{}

Write-Step '1. Quick create off'
if ($meta.IsQuickCreateEnabled) {
    if ($Verify) { Write-Gap 'quick create is ON' } else { Write-Plan 'switch quick create OFF'; $writes.quickCreate = $true }
}
else { Write-Ok 'quick create is off' }

Write-Step "2. View '$ViewName'"
$wantFetch = Get-ViewFetchXml
$wantLayout = Get-ViewLayoutXml $meta.ObjectTypeCode
$liveFetch = $view.fetchxml -replace '\s+savedqueryid="[^"]*"', ''
$fetchDoc = ConvertTo-XmlDoc $liveFetch
$liveCols = @($fetchDoc.SelectNodes('//attribute') | ForEach-Object { $_.GetAttribute('name') })
$liveActiveOnly = [bool]$fetchDoc.SelectSingleNode("//filter/condition[@attribute='statecode' and @operator='eq' and @value='0']")
$layoutCols = @((ConvertTo-XmlDoc $view.layoutxml).SelectNodes('//cell') | ForEach-Object { $_.GetAttribute('name') })
$viewOk = $liveActiveOnly -and (@($ViewColumns | Where-Object { $liveCols -notcontains $_ -or $layoutCols -notcontains $_ }).Count -eq 0) -and $view.isdefault
if ($viewOk) { Write-Ok 'active rows only, every column present, default view' }
elseif ($Verify) { Write-Gap "view is not complete (active only: $liveActiveOnly; columns: $($layoutCols -join ', '); default: $($view.isdefault))" }
else { Write-Plan "set the columns ($($ViewColumns -join ', ')), active rows only, default view"; $writes.view = $true }

Write-Step '3. No Access entry main form'
$entryGaps = Get-EntryFormGaps $entryForm.formxml
if ($entryGaps.Count -eq 0) { Write-Ok 'layout, libraries and OnLoad handler in place' }
else {
    foreach ($g in $entryGaps) { if ($Verify) { Write-Gap "entry form: $g" } else { Write-Plan "entry form: $g" } }
    $liveFields = @((ConvertTo-XmlDoc $entryForm.formxml).SelectNodes('//control[@datafieldname]') | ForEach-Object { $_.GetAttribute('datafieldname') } |
        Where-Object { $EntryFields -notcontains $_ -and $_ -notin @('statecode', 'modifiedby', 'modifiedon') } | Sort-Object -Unique)
    if ($liveFields.Count -gt 0 -and -not $Verify) {
        Stop-Refused "the live entry form shows field(s) the new layout would drop: $($liveFields -join ', '). Add them to Get-EntryFormXml first."
    }
    if (-not $Verify) { $writes.entryForm = Get-EntryFormXml }
}

Write-Step "4. Organization form '$($orgForm.name)'"
$orgExpected = @(
    @{ Relationship = $rels['sprk_objectorganization']; Label = 'Ethical walls on this organization' },
    @{ Relationship = $rels['sprk_subjectorganization']; Label = 'This organization denied' })
$orgGaps = Get-SubgridGaps $orgForm.formxml $orgExpected $ViewId
if ($orgGaps.Count -eq 0) { Write-Ok 'NO ACCESS section with both subgrids' }
else {
    foreach ($g in $orgGaps) { if ($Verify) { Write-Gap "organization form: $g" } else { Write-Plan "organization form: $g" } }
    if (-not $Verify) {
        $orgDoc = ConvertTo-XmlDoc $orgForm.formxml
        if (-not $orgDoc.SelectSingleNode("//section[@name='General_section_systemaccess']")) {
            Stop-Refused "the Organization form has no SYSTEM ACCESS section (General_section_systemaccess)."
        }
        $rows = (Get-SubgridCell 'subgrid_noaccess_walls' 'Ethical walls on this organization' $rels['sprk_objectorganization'] $ViewId) +
        (Get-SubgridCell 'subgrid_noaccess_org_denied' 'This organization denied' $rels['sprk_subjectorganization'] $ViewId)
        # MEMBERS is the section holding the sprk_contactorganization subgrid, on the SYSTEM ACCESS tab.
        $newXml = Add-NoAccessSection $orgForm.formxml 'sprk_contactorganization' 'org' $rows
        if (-not $newXml) { Stop-Refused 'the Organization form has no single MEMBERS subgrid (sprk_contactorganization) to place the section after.' }
        $sameTab = (ConvertTo-XmlDoc $newXml).SelectSingleNode("//tab[.//section[@name='General_section_systemaccess'] and .//section[@name='$SectionName']]")
        if (-not $sameTab) { Stop-Refused 'MEMBERS is not on the SYSTEM ACCESS tab; the placement rule cannot be met.' }
        $writes.orgForm = $newXml
    }
}

Write-Step "5. Contact form '$($contactForm.name)'"
$contactExpected = @(@{ Relationship = $rels['sprk_subjectcontact']; Label = 'This contact denied' })
$contactGaps = Get-SubgridGaps $contactForm.formxml $contactExpected $ViewId
if ($contactGaps.Count -eq 0) { Write-Ok 'NO ACCESS section with its subgrid' }
else {
    foreach ($g in $contactGaps) { if ($Verify) { Write-Gap "contact form: $g" } else { Write-Plan "contact form: $g" } }
    if (-not $Verify) {
        $rows = Get-SubgridCell 'subgrid_noaccess_contact_denied' 'This contact denied' $rels['sprk_subjectcontact'] $ViewId
        # The contact's organization memberships (ORGANIZATIONS) are the sprk_contactorganization subgrid.
        $newXml = Add-NoAccessSection $contactForm.formxml 'sprk_contactorganization' 'contact' $rows
        if (-not $newXml) { Stop-Refused 'the Contact form has no single ORGANIZATIONS subgrid (sprk_contactorganization) to place the section after.' }
        $writes.contactForm = $newXml
    }
}

Write-Step "6. Site map '$AppUniqueName'"
$smDoc = ConvertTo-XmlDoc $sitemap.sitemapxml
$existingSub = $smDoc.SelectSingleNode("//SubArea[@Entity='$Table']")
$hasPrivilege = $existingSub -and $existingSub.SelectSingleNode("Privilege[@Entity='$Table' and @Privilege='Read']")
if ($existingSub -and $hasPrivilege) { Write-Ok 'No Access Entries subarea present, requires Read' }
elseif ($Verify) { Write-Gap 'site map has no No Access Entries subarea that requires Read' }
else {
    $anchor = $smDoc.SelectSingleNode("//SubArea[@Entity='sprk_externalrecordaccess']")
    if (-not $anchor) { Stop-Refused "the site map lists no Access Permission Grants (sprk_externalrecordaccess) subarea to place the entry after." }
    if ($existingSub) { $existingSub.ParentNode.RemoveChild($existingSub) | Out-Null }
    $frag = $smDoc.CreateDocumentFragment()
    $frag.InnerXml = "<SubArea Id=""subarea_sprk_noaccessentry"" Icon=""/_imgs/imagestrips/transparent_spacer.gif"" Entity=""$Table"" " +
    'Client="All,Outlook,OutlookLaptopClient,OutlookWorkstationClient,Web" AvailableOffline="true" PassParams="false" Sku="All,OnPremise,Live,SPLA">' +
    '<Titles><Title LCID="1033" Title="No Access Entries" /></Titles>' +
    "<Privilege Entity=""$Table"" Privilege=""Read"" /></SubArea>"
    $anchor.ParentNode.InsertAfter($frag, $anchor) | Out-Null
    Write-Plan 'add the No Access Entries subarea after Access Permission Grants (requires Read on the table)'
    $writes.sitemap = $smDoc.OuterXml
}
if ($tableInApp) { Write-Ok "$Table is an app component" }
elseif ($Verify) { Write-Gap "$Table is not a component of the app" }
else { Write-Plan "add $Table to the app's components"; $writes.appComponent = $true }

# ---- Verify / dry run end ----------------------------------------------------------------------------------------------
if ($Verify) {
    Write-Host ''
    if ($Gaps.Count -eq 0) { Write-Host 'VERIFY PASS: every task-154 form, view and site map step is in place.' -ForegroundColor Green; exit 0 }
    Write-Host "VERIFY FAIL: $($Gaps.Count) gap(s)." -ForegroundColor Red
    exit 1
}
if ($writes.Count -eq 0) { Write-Host "`nNothing to change." -ForegroundColor Green; exit 0 }
if (-not $Apply) {
    if ($PlanOut) {
        New-Item -ItemType Directory -Force -Path $PlanOut | Out-Null
        foreach ($k in $writes.Keys) {
            if ($writes[$k] -is [string]) { Set-Content -LiteralPath (Join-Path $PlanOut "$k.xml") -Value $writes[$k] -Encoding UTF8 }
        }
        if ($writes.view) {
            Set-Content -LiteralPath (Join-Path $PlanOut 'view-fetch.xml') -Value $wantFetch -Encoding UTF8
            Set-Content -LiteralPath (Join-Path $PlanOut 'view-layout.xml') -Value $wantLayout -Encoding UTF8
        }
        Write-Host "Planned XML written to $PlanOut"
    }
    Write-Host "`nDRY RUN: $($writes.Count) change(s) planned: $($writes.Keys -join ', '). Re-run with -Apply." -ForegroundColor Cyan
    exit 0
}

# ---- Apply -------------------------------------------------------------------------------------------------------------
Write-Step 'Apply'
New-Item -ItemType Directory -Force -Path $SnapshotDir | Out-Null
$snapPath = Join-Path $SnapshotDir ("noaccess-forms-snapshot-{0}.json" -f (Get-Date -Format 'yyyyMMddHHmmss'))
@{
    environment = $BaseUrl
    forms       = @($entryForm, $orgForm, $contactForm | ForEach-Object { @{ formid = $_.formid; name = $_.name; formxml = $_.formxml } })
    view        = @{ savedqueryid = $view.savedqueryid; fetchxml = $view.fetchxml; layoutxml = $view.layoutxml }
    sitemap     = @{ sitemapid = $sitemap.sitemapid; sitemapxml = $sitemap.sitemapxml }
} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $snapPath -Encoding UTF8
Write-Ok "snapshot: $snapPath (restore with -RestoreFrom)"

if ($writes.quickCreate) {
    $def = Invoke-Dv "EntityDefinitions(LogicalName='$Table')"
    foreach ($annotation in @($def.PSObject.Properties.Name | Where-Object { $_ -like '@*' })) { $def.PSObject.Properties.Remove($annotation) }
    $def.IsQuickCreateEnabled = $false
    Invoke-Dv "EntityDefinitions(LogicalName='$Table')" 'PUT' $def @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null
    Write-Ok 'quick create switched off'
}
if ($writes.view) {
    Invoke-Dv "savedqueries($ViewId)" 'PATCH' @{ fetchxml = $wantFetch; layoutxml = $wantLayout } | Out-Null
    if (-not $view.isdefault) { Invoke-Dv "savedqueries($ViewId)" 'PATCH' @{ isdefault = $true } | Out-Null }
    Write-Ok 'view updated'
}
if ($writes.entryForm) { Invoke-Dv "systemforms($($entryForm.formid))" 'PATCH' @{ formxml = $writes.entryForm } | Out-Null; Write-Ok 'entry form written' }
if ($writes.orgForm) { Invoke-Dv "systemforms($($orgForm.formid))" 'PATCH' @{ formxml = $writes.orgForm } | Out-Null; Write-Ok 'organization form written' }
if ($writes.contactForm) { Invoke-Dv "systemforms($($contactForm.formid))" 'PATCH' @{ formxml = $writes.contactForm } | Out-Null; Write-Ok 'contact form written' }
if ($writes.sitemap) { Invoke-Dv "sitemaps($($sitemap.sitemapid))" 'PATCH' @{ sitemapxml = $writes.sitemap } | Out-Null; Write-Ok 'site map written' }
if ($writes.appComponent) {
    Invoke-Dv 'AddAppComponents' 'POST' @{
        AppId      = $app.appmoduleid
        Components = @(@{ '@odata.type' = 'Microsoft.Dynamics.CRM.entity'; entityid = $meta.MetadataId })
    } | Out-Null
    Write-Ok "$Table added to the app"
}

$publish = "<importexportxml><entities><entity>$Table</entity><entity>sprk_organization</entity><entity>contact</entity></entities>" +
"<sitemaps><sitemap>{$($sitemap.sitemapid)}</sitemap></sitemaps><appmodules><appmodule>{$($app.appmoduleid)}</appmodule></appmodules></importexportxml>"
Invoke-Dv 'PublishXml' 'POST' @{ ParameterXml = $publish } | Out-Null
Write-Ok 'published'
Write-Host ''
Write-Host 'APPLIED. Now run this script with -Verify, then the manual live gate (task 154 criterion 11).' -ForegroundColor Green
exit 0
