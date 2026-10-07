<#
.SYNOPSIS
    Merges the ONE authored secure-record child "New" definition (secure-child-new.template.xml) into a FRESH export of
    one CHILD entity's RibbonDiffXml (unified-access-control-r2 task 147 r1; owner round 28 item 2, "E2").

.DESCRIPTION
    Pure file transformation - reads two files, writes one, no Dataverse call. For -Entity:

      1. Instantiates the template ({{entity}} = the child's logical name, {{label}} = its display noun).
      2. Removes every earlier sprk.SecureChild.* node (CustomAction, CommandDefinition, EnableRule, LocLabel) -
         idempotent: a second run yields the same file.
      3. The platform subgrid "+ New" (Mscrm.AddNewRecordFromSubGridStandard): when the export ALREADY overrides it (some
         earlier customisation), only the reference to sprk.SecureChild.<entity>.NativeNewAllowed.EnableRule is added to
         that override - its other rules and actions are kept as they are; otherwise the template's copy (the platform's
         definition plus that one rule) is added.
      4. Appends the template's button, command, enable rules and labels.
      5. Writes -Out and prints the command ids before and after; THROWS if any earlier command id is missing afterwards.

.PARAMETER ExportedRibbonDiff
    The child entity's RibbonDiff.xml from a FRESH export (Entities/<Entity>/RibbonDiff.xml in the unpacked solution).

.PARAMETER Entity
    One of the child tables the script serves (Spaarke.SecureChild.Ribbon.TABLES in sprk_secure_child_ribbon.js).

.PARAMETER Out
    Where to write the merged RibbonDiff.xml (normally back over the unpacked export before packing).

.EXAMPLE
    pwsh ./Merge-SecureChildRibbon.ps1 -ExportedRibbonDiff ./export/Entities/sprk_todo/RibbonDiff.xml `
        -Entity sprk_todo -Out ./export/Entities/sprk_todo/RibbonDiff.xml
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ExportedRibbonDiff,
    [Parameter(Mandatory)]
    [ValidateSet('sprk_todo', 'sprk_event', 'sprk_invoice', 'sprk_reportcard', 'sprk_document', 'sprk_communication', 'sprk_budget',
        'sprk_kpiassessment', 'sprk_billingevent')]
    [string] $Entity,
    [Parameter(Mandatory)] [string] $Out
)

$ErrorActionPreference = 'Stop'

# The display noun on the "New <label>" button, per child table.
$Labels = @{
    sprk_todo          = 'To Do'
    sprk_event         = 'Event'
    sprk_invoice       = 'Invoice'
    sprk_reportcard    = 'Report Card'
    sprk_document      = 'Document'
    sprk_communication = 'Message'
    sprk_budget        = 'Budget'
    sprk_kpiassessment = 'KPI Assessment'
    sprk_billingevent  = 'Billing Event'
}

# Task 147 r1c: Spaarke's OWN subgrid creates of a table that also open a parent-prefilled create (quick create) - they
# carry the same secure rule as the platform "+ New", so under a secure host only the BFF command shows. "+ Add KPI"
# (src/solutions/SpaarkeCore/entities/sprk_matter/RibbonDiff/add-kpi-ribbon.xml, sprk_/scripts/kpi_ribbon_actions.js) opens
# the KPI assessment quick create with the matter / project prefilled. Guarded when the export carries it.
$AlsoGuardedCommands = @{
    sprk_kpiassessment = @('sprk.matter.subgrid.kpi.AddKpiButton.Command', 'sprk.project.subgrid.kpi.AddKpiButton.Command')
}

$NativeCommandId = 'Mscrm.AddNewRecordFromSubGridStandard'
$RuleId = "sprk.SecureChild.$Entity.NativeNewAllowed.EnableRule"

$templatePath = Join-Path $PSScriptRoot 'secure-child-new.template.xml'
$templateText = (Get-Content -Raw -LiteralPath $templatePath).Replace('{{entity}}', $Entity).Replace('{{label}}', $Labels[$Entity])
[xml] $template = $templateText

[xml] $ribbon = Get-Content -Raw -LiteralPath $ExportedRibbonDiff
$root = $ribbon.DocumentElement
if ($root.LocalName -ne 'RibbonDiffXml') {
    throw "Expected a RibbonDiffXml root in $ExportedRibbonDiff, found '$($root.LocalName)'."
}

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

$before = Get-CommandIds $ribbon

$customActions = Get-OrCreateChild $root 'CustomActions'
$commandDefinitions = Get-OrCreateChild $root 'CommandDefinitions'
$ruleDefinitions = Get-OrCreateChild $root 'RuleDefinitions'
$enableRules = Get-OrCreateChild $ruleDefinitions 'EnableRules'
$locLabels = Get-OrCreateChild $root 'LocLabels'

# Idempotent: drop every earlier sprk.SecureChild node first.
foreach ($section in @($customActions, $commandDefinitions, $enableRules, $locLabels)) {
    foreach ($node in @($section.ChildNodes)) {
        if ($node -is [System.Xml.XmlElement] -and $node.GetAttribute('Id').StartsWith('sprk.SecureChild.')) {
            [void] $section.RemoveChild($node)
        }
    }
}

# The platform "+ New": extend an existing override, else add the template's copy.
$existingNative = $commandDefinitions.SelectSingleNode("*[local-name()='CommandDefinition' and @Id='$NativeCommandId']")
$templateNative = $template.DocumentElement.SelectSingleNode(
    "*[local-name()='CommandDefinitions']/*[local-name()='CommandDefinition' and @Id='$NativeCommandId']")
if ($existingNative) {
    $rules = Get-OrCreateChild $existingNative 'EnableRules'
    if (-not $rules.SelectSingleNode("*[local-name()='EnableRule' and @Id='$RuleId']")) {
        $ref = $ribbon.CreateElement('EnableRule', $rules.NamespaceURI)
        $ref.SetAttribute('Id', $RuleId)
        [void] $rules.AppendChild($ref)
    }
    Write-Host "The export already overrides $NativeCommandId; added $RuleId to it (its own rules kept)."
} else {
    [void] $commandDefinitions.AppendChild($ribbon.ImportNode($templateNative, $true))
}

# Spaarke's own parent-prefilled creates of this table (r1c): the same rule reference, the command otherwise untouched.
foreach ($guardedId in @($AlsoGuardedCommands[$Entity])) {
    if (-not $guardedId) { continue }
    $guarded = $commandDefinitions.SelectSingleNode("*[local-name()='CommandDefinition' and @Id='$guardedId']")
    if (-not $guarded) { continue }
    $rules = Get-OrCreateChild $guarded 'EnableRules'
    if (-not $rules.SelectSingleNode("*[local-name()='EnableRule' and @Id='$RuleId']")) {
        $ref = $ribbon.CreateElement('EnableRule', $rules.NamespaceURI)
        $ref.SetAttribute('Id', $RuleId)
        [void] $rules.AppendChild($ref)
    }
    Write-Host "Added $RuleId to Spaarke's own parent-prefilled create $guardedId."
}

$map = @{
    'CustomActions'      = $customActions
    'CommandDefinitions' = $commandDefinitions
    'EnableRules'        = $enableRules
    'LocLabels'          = $locLabels
}
foreach ($sectionName in $map.Keys) {
    $source = $template.DocumentElement.SelectSingleNode("*[local-name()='$sectionName']")
    foreach ($node in @($source.ChildNodes)) {
        if ($node -is [System.Xml.XmlElement] -and $node.GetAttribute('Id') -ne $NativeCommandId) {
            [void] $map[$sectionName].AppendChild($ribbon.ImportNode($node, $true))
        }
    }
}

$settings = New-Object System.Xml.XmlWriterSettings
$settings.Indent = $true
$settings.Encoding = New-Object System.Text.UTF8Encoding($false)
$writer = [System.Xml.XmlWriter]::Create($Out, $settings)
try { $ribbon.Save($writer) } finally { $writer.Dispose() }

[xml] $merged = Get-Content -Raw -LiteralPath $Out
$after = Get-CommandIds $merged
$lost = @($before | Where-Object { $_ -notlike 'sprk.SecureChild.*' -and $after -notcontains $_ })
if ($lost.Count -gt 0) {
    throw "The merge dropped existing command(s): $($lost -join ', '). Nothing should be imported."
}
$native = $merged.SelectSingleNode("//*[local-name()='CommandDefinition' and @Id='$NativeCommandId']")
if (-not $native -or -not $native.SelectSingleNode(".//*[local-name()='EnableRule' and @Id='$RuleId']")) {
    throw "The merged file does not carry $RuleId on $NativeCommandId. Nothing should be imported."
}

Write-Host "Merged the secure-record New commands into $Entity -> $Out"
Write-Host "Commands before: $($before -join ', ')"
Write-Host "Commands after : $($after -join ', ')"
