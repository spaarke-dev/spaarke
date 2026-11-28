<#
.SYNOPSIS
    Merges the native-create surface rules (native-create-surfaces.json) into ONE table's RibbonDiff.xml: hides the OOB Assign
    command and steers the platform "New" to the product's create surface (field-mapping-server-write-path-r1 task 051, FR-24).
    Hiding a command is a UX steer, not enforcement: the server write path and the repair pass own every invariant (ADR-002).

.DESCRIPTION
    Pure file transformation: reads the config and a RibbonDiff.xml (a FRESH export, or the SpaarkeMaster copy), writes -Out, calls
    no environment. For -Entity:

      1. ASSIGN. Adds a HideCustomAction for each OOB Assign button the table's ribbon carries:
           Mscrm.Form.<t>.Assign, Mscrm.HomepageGrid.<t>.Assign, Mscrm.SubGrid.<t>.Assign
         (HideActionId "sprk.<Location>.Hide", the convention of every hide in the repo). Hides the command only: prvAssign is
         NOT removed from any role. Applies to every table in the config.
      2. NEW, nativeNew = "redirected". Hides the platform New on the grid (Mscrm.HomepageGrid.<t>.NewRecord) and on the form
         (Mscrm.Form.<t>.NewRecord and Mscrm.Form.<t>.SaveAndNew), and adds a Spaarke "+New <label>" button on the form (and on
         the grid when gridButton = "add") whose command calls the table's standalone launcher and carries
         sprk.CreatePrivilege.<t>.DisplayRule (task 180). An existing grid wizard button is left as it is.
      3. NEW, nativeNew = "hidden-outright". Ensures the five platform New hides an invoice-family table carries (form New, form
         Save & New, grid New, subgrid Add New Standard, subgrid New). No replacement button.
      4. nativeNew = "kept": no form or subgrid New change (the entry's reason and ownerQuestion say why). With homeGridNew = "hidden"
         (owner OQ-1, 2026-11-26: a budget / KPI assessment is created only in its parent matter's context) the platform HOME-grid New
         (Mscrm.HomepageGrid.<t>.NewRecord) is hidden, with no replacement.
      5. RETIRED BUTTONS. A table with mustBeAbsent (the KPI subgrid's "Add KPI Assessment", removed in task 066) has each listed button,
         its command (<button id>.Command) and its labels (<button id>.*) removed, so a fresh export that still carries the button
         comes out without it. A create-privilege rule the command used is left (an unused rule is harmless).
      Where a redirected table has gridButton = "none" (the report card) the home grid has no New at all; the form button opens the
      table's wizard filed under the parent the form names.

    Idempotent: every earlier sprk.NativeCreate.* node is removed first and a hide whose Location is already hidden is not added
    twice, so a second run yields the same file. The merged file is written to a temporary file beside -Out, checked, and only then
    moved into place: a failed check THROWS and leaves -Out exactly as it was (it is never half-written or left unchecked).

.PARAMETER RibbonDiff
    The table's RibbonDiff.xml.
.PARAMETER Entity
    A table of native-create-surfaces.json.
.PARAMETER Out
    Where to write the merged file (normally back over -RibbonDiff).
.PARAMETER Config
    Defaults to native-create-surfaces.json beside this script.

.EXAMPLE
    pwsh ./Merge-NativeCreateRibbon.ps1 -RibbonDiff ./export/Entities/sprk_todo/RibbonDiff.xml -Entity sprk_todo -Out ./export/Entities/sprk_todo/RibbonDiff.xml
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $RibbonDiff,
    [Parameter(Mandatory)] [string] $Entity,
    [Parameter(Mandatory)] [string] $Out,
    [string] $Config = (Join-Path $PSScriptRoot 'native-create-surfaces.json')
)

$ErrorActionPreference = 'Stop'

$cfg = Get-Content -Raw -LiteralPath $Config | ConvertFrom-Json
$entry = @($cfg.tables | Where-Object { $_.table -eq $Entity }) | Select-Object -First 1
if (-not $entry) { throw "$Entity is not a table of $Config." }

[xml] $ribbon = Get-Content -Raw -LiteralPath $RibbonDiff
$root = $ribbon.DocumentElement
if ($root.LocalName -ne 'RibbonDiffXml') { throw "Expected a RibbonDiffXml root in $RibbonDiff, found '$($root.LocalName)'." }

function Get-CommandIds([xml] $doc) {
    @($doc.SelectNodes('//*[local-name()="CommandDefinition"]') | ForEach-Object { $_.GetAttribute('Id') }) | Sort-Object
}
function Get-OrCreateChild([System.Xml.XmlElement] $parent, [string] $name) {
    $child = $parent.SelectSingleNode("*[local-name()='$name']")
    if (-not $child) {
        $child = $parent.OwnerDocument.CreateElement($name, $parent.NamespaceURI)
        [void] $parent.AppendChild($child)
    }
    return $child
}
function Add-Fragment([System.Xml.XmlElement] $parent, [string] $xml) {
    $fragment = New-Object System.Xml.XmlDocument
    $fragment.LoadXml($xml)
    [void] $parent.AppendChild($parent.OwnerDocument.ImportNode($fragment.DocumentElement, $true))
}
function Esc([string] $text) { [System.Security.SecurityElement]::Escape($text) }

$before = Get-CommandIds $ribbon
$t = $Entity
$customActions = Get-OrCreateChild $root 'CustomActions'
$commandDefinitions = Get-OrCreateChild $root 'CommandDefinitions'
$ruleDefinitions = Get-OrCreateChild $root 'RuleDefinitions'
$displayRules = Get-OrCreateChild $ruleDefinitions 'DisplayRules'
$locLabels = Get-OrCreateChild $root 'LocLabels'

# Idempotent: drop every earlier sprk.NativeCreate node first.
foreach ($section in @($customActions, $commandDefinitions, $locLabels)) {
    foreach ($node in @($section.ChildNodes)) {
        if ($node -is [System.Xml.XmlElement] -and $node.GetAttribute('Id').StartsWith('sprk.NativeCreate.')) { [void] $section.RemoveChild($node) }
    }
}

# Retired buttons (config mustBeAbsent, task 066): remove the button's CustomAction, its command (<button id>.Command) and its labels
# (<button id>.*). A no-op when the ribbon does not carry them, so it is idempotent. A rule the command used (for example a
# create-privilege DisplayRule) is left in place: an unreferenced rule is harmless and another command may share it.
$retiredButtons = @(@($entry.mustBeAbsent) | Where-Object { $_ } | ForEach-Object { [string]$_ })
$retiredCommands = @($retiredButtons | ForEach-Object { "$_.Command" })
foreach ($buttonId in $retiredButtons) {
    foreach ($node in @($customActions.SelectNodes("*[local-name()='CustomAction'][.//*[local-name()='Button' and @Id='$buttonId']]"))) { [void] $customActions.RemoveChild($node) }
    foreach ($node in @($commandDefinitions.SelectNodes("*[local-name()='CommandDefinition' and @Id='$buttonId.Command']"))) { [void] $commandDefinitions.RemoveChild($node) }
    foreach ($node in @($locLabels.SelectNodes("*[local-name()='LocLabel' and starts-with(@Id, '$buttonId.')]"))) { [void] $locLabels.RemoveChild($node) }
}

# A hide, once per Location (an existing hide of the same Location, under any id, is kept and not repeated).
$hidden = New-Object System.Collections.Generic.List[string]
function Add-Hide([string] $location) {
    foreach ($existing in @($customActions.SelectNodes("*[local-name()='HideCustomAction']"))) {
        if ($existing.GetAttribute('Location') -eq $location) { return }
    }
    $hide = $ribbon.CreateElement('HideCustomAction', $customActions.NamespaceURI)
    $hide.SetAttribute('HideActionId', "sprk.$location.Hide")
    $hide.SetAttribute('Location', $location)
    [void] $customActions.AppendChild($hide)
    $hidden.Add($location)
}

# 1. Assign.
foreach ($surface in 'Form', 'HomepageGrid', 'SubGrid') { Add-Hide "Mscrm.$surface.$t.Assign" }

# 2 / 3. New.
$added = @()
if ($entry.nativeNew -eq 'redirected') {
    Add-Hide "Mscrm.HomepageGrid.$t.NewRecord"
    Add-Hide "Mscrm.Form.$t.NewRecord"
    Add-Hide "Mscrm.Form.$t.SaveAndNew"

    $label = $entry.label
    $fn = $entry.launcher.function
    $lib = $entry.launcher.library
    $commandId = "sprk.NativeCreate.$t.New.Command"
    $ruleId = "sprk.CreatePrivilege.$t.DisplayRule"

    Add-Fragment $commandDefinitions @"
<CommandDefinition Id="$commandId">
  <EnableRules />
  <DisplayRules>
    <DisplayRule Id="$ruleId" />
  </DisplayRules>
  <Actions>
    <JavaScriptFunction FunctionName="$fn" Library="`$webresource:$lib" />
  </Actions>
</CommandDefinition>
"@
    if (-not $displayRules.SelectSingleNode("*[local-name()='DisplayRule' and @Id='$ruleId']")) {
        Add-Fragment $displayRules "<DisplayRule Id=`"$ruleId`"><EntityPrivilegeRule EntityName=`"$t`" PrivilegeType=`"Create`" PrivilegeDepth=`"Basic`" /></DisplayRule>"
    }

    $surfaces = @(@{ Name = 'Form'; Location = "Mscrm.Form.$t.MainTab.Actions.Controls._children" })
    if ($entry.gridButton -eq 'add') { $surfaces += @{ Name = 'Grid'; Location = "Mscrm.HomepageGrid.$t.MainTab.Actions.Controls._children" } }
    foreach ($s in $surfaces) {
        $id = "sprk.NativeCreate.$t.$($s.Name).New"
        Add-Fragment $customActions @"
<CustomAction Id="$id.CustomAction" Location="$($s.Location)" Sequence="1">
  <CommandUIDefinition>
    <Button Id="$id" Command="$commandId" Alt="`$LocLabels:sprk.NativeCreate.$t.New.Alt" LabelText="`$LocLabels:sprk.NativeCreate.$t.New.LabelText" ToolTipTitle="`$LocLabels:sprk.NativeCreate.$t.New.ToolTipTitle" ToolTipDescription="`$LocLabels:sprk.NativeCreate.$t.New.ToolTipDescription" Image32by32="/_imgs/ribbon/newrecord32.png" Image16by16="/_imgs/ribbon/newrecord16.png" ModernImage="CommandBarNewIcon" Sequence="1" TemplateAlias="o1" />
  </CommandUIDefinition>
</CustomAction>
"@
        $added += $id
    }
    $text = @{
        Alt = "+New $label"; LabelText = "+New $label"; ToolTipTitle = "New $label"
        ToolTipDescription = "Create a new $label in Spaarke, so it is filed, owned and mapped by the server"
    }
    foreach ($key in 'Alt', 'LabelText', 'ToolTipTitle', 'ToolTipDescription') {
        Add-Fragment $locLabels "<LocLabel Id=`"sprk.NativeCreate.$t.New.$key`"><Titles><Title description=`"$(Esc $text[$key])`" languagecode=`"1033`" /></Titles></LocLabel>"
    }
}
elseif ($entry.nativeNew -eq 'kept' -and $entry.homeGridNew -eq 'hidden') {
    Add-Hide "Mscrm.HomepageGrid.$t.NewRecord"
}
elseif ($entry.nativeNew -eq 'hidden-outright') {
    foreach ($location in "Mscrm.Form.$t.NewRecord", "Mscrm.Form.$t.SaveAndNew", "Mscrm.HomepageGrid.$t.NewRecord",
        "Mscrm.SubGrid.$t.AddNewStandard", "Mscrm.SubGrid.$t.NewRecord") { Add-Hide $location }
}

$settings = New-Object System.Xml.XmlWriterSettings
$settings.Indent = $true
$settings.Encoding = New-Object System.Text.UTF8Encoding($false)
# Write beside -Out, check, then move into place: -Out is never replaced by a file that failed its checks.
$temp = "$Out.$([guid]::NewGuid().ToString('N')).tmp"
try {
    $writer = [System.Xml.XmlWriter]::Create($temp, $settings)
    try { $ribbon.Save($writer) } finally { $writer.Dispose() }

    [xml] $merged = Get-Content -Raw -LiteralPath $temp
    $after = Get-CommandIds $merged
    $lost = @($before | Where-Object { $_ -notlike 'sprk.NativeCreate.*' -and $retiredCommands -notcontains $_ -and $after -notcontains $_ })
    if ($lost.Count -gt 0) { throw "The merge dropped existing command(s): $($lost -join ', '). Nothing was written; -Out is unchanged." }
    foreach ($surface in 'Form', 'HomepageGrid', 'SubGrid') {
        if (-not $merged.SelectSingleNode("//*[local-name()='HideCustomAction' and @Location='Mscrm.$surface.$t.Assign']")) {
            throw "The merged file does not hide Mscrm.$surface.$t.Assign. Nothing was written; -Out is unchanged."
        }
    }
    foreach ($buttonId in $retiredButtons) {
        if ($merged.SelectSingleNode("//*[local-name()='Button' and @Id='$buttonId']")) {
            throw "The merged file still carries the retired button $buttonId. Nothing was written; -Out is unchanged."
        }
    }
    if ($entry.nativeNew -eq 'redirected' -and -not $merged.SelectSingleNode("//*[local-name()='CommandDefinition' and @Id='sprk.NativeCreate.$t.New.Command']")) {
        throw "The merged file does not carry sprk.NativeCreate.$t.New.Command. Nothing was written; -Out is unchanged."
    }
    Move-Item -LiteralPath $temp -Destination $Out -Force
}
finally {
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force }
}

Write-Host "$Entity -> $Out : nativeNew=$($entry.nativeNew); hides added: $(if ($hidden.Count) { $hidden -join ', ' } else { 'none (already present)' }); buttons: $(if ($added) { $added -join ', ' } else { 'none' })"
