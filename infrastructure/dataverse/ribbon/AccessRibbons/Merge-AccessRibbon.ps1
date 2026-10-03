<#
.SYNOPSIS
    Merges the ONE authored "Access" group (access-group.template.xml) into a FRESH export of one entity's form
    RibbonDiffXml - the mechanical per-entity generation the task-142 UX amendment requires.

.DESCRIPTION
    unified-access-control-r2 task 142 (owner round 3 R3 "Update Access"; round 3b UX disposition row 4: one Access
    group, one source file, one script). The per-entity copies Dataverse needs are GENERATED from the one template,
    never hand-edited:

      1. Instantiates the template for -Entity ({{entity}} = sprk_project | sprk_matter | sprk_workassignment).
      2. Loads -ExportedRibbonDiff - the entity's RibbonDiff.xml from a FRESH solution export (the form ribbons are split
         across several checked-in sources - Push Updates, the wizard buttons, Create To Do, Dark Mode - so the merge
         must start from what is live, never from one checked-in file; the work-assignment ribbon is not in source
         control at all).
      3. Removes every earlier sprk.Access.* node (CustomAction, CommandDefinition, EnableRule, LocLabel), so running it
         twice yields the same file (idempotent), then appends the template's nodes to the matching sections, creating a
         section only when the export has none.
      4. Writes -Out and prints the commands it found on the form before and after (the AFTER list must contain the
         BEFORE list plus sprk.Access.* - the import must not drop an existing command).

    Pure file transformation: reads two files, writes one. No Dataverse call.

.PARAMETER ExportedRibbonDiff
    Path to the entity's RibbonDiff.xml from a fresh export (Entities/<Entity>/RibbonDiff.xml in the unpacked solution).

.PARAMETER Entity
    The entity LOGICAL name: sprk_project, sprk_matter or sprk_workassignment.

.PARAMETER Out
    Where to write the merged RibbonDiff.xml (normally back over the unpacked export before packing).

.EXAMPLE
    pwsh ./Merge-AccessRibbon.ps1 -ExportedRibbonDiff ./export/Entities/sprk_Project/RibbonDiff.xml `
        -Entity sprk_project -Out ./export/Entities/sprk_Project/RibbonDiff.xml
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ExportedRibbonDiff,
    [Parameter(Mandatory)] [ValidateSet('sprk_project', 'sprk_matter', 'sprk_workassignment')] [string] $Entity,
    [Parameter(Mandatory)] [string] $Out
)

$ErrorActionPreference = 'Stop'

$templatePath = Join-Path $PSScriptRoot 'access-group.template.xml'
$templateText = (Get-Content -Raw -LiteralPath $templatePath).Replace('{{entity}}', $Entity)
[xml] $template = $templateText

[xml] $ribbon = Get-Content -Raw -LiteralPath $ExportedRibbonDiff
$root = $ribbon.DocumentElement
if ($root.LocalName -ne 'RibbonDiffXml') {
    throw "Expected a RibbonDiffXml root in $ExportedRibbonDiff, found '$($root.LocalName)'."
}

function Get-CommandIds([xml] $doc) {
    @($doc.SelectNodes('//*[local-name()="CommandDefinition"]') | ForEach-Object { $_.GetAttribute('Id') }) | Sort-Object
}

$before = Get-CommandIds $ribbon

function Get-OrCreateChild([System.Xml.XmlElement] $parent, [string] $name) {
    $child = $parent.SelectSingleNode("*[local-name()='$name']")
    if (-not $child) {
        $child = $parent.OwnerDocument.CreateElement($name, $parent.NamespaceURI)
        [void] $parent.AppendChild($child)
    }
    return $child
}

$customActions = Get-OrCreateChild $root 'CustomActions'
$commandDefinitions = Get-OrCreateChild $root 'CommandDefinitions'
$ruleDefinitions = Get-OrCreateChild $root 'RuleDefinitions'
$enableRules = Get-OrCreateChild $ruleDefinitions 'EnableRules'
$locLabels = Get-OrCreateChild $root 'LocLabels'

# Idempotent: drop every earlier Access node before adding the current template's.
foreach ($section in @($customActions, $commandDefinitions, $enableRules, $locLabels)) {
    foreach ($node in @($section.ChildNodes)) {
        if ($node -is [System.Xml.XmlElement] -and $node.GetAttribute('Id').StartsWith('sprk.Access.')) {
            [void] $section.RemoveChild($node)
        }
    }
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
        if ($node -is [System.Xml.XmlElement]) {
            [void] $map[$sectionName].AppendChild($ribbon.ImportNode($node, $true))
        }
    }
}

$settings = New-Object System.Xml.XmlWriterSettings
$settings.Indent = $true
$settings.Encoding = New-Object System.Text.UTF8Encoding($false)
$writer = [System.Xml.XmlWriter]::Create($Out, $settings)
try { $ribbon.Save($writer) } finally { $writer.Dispose() }

$after = Get-CommandIds ([xml] (Get-Content -Raw -LiteralPath $Out))
$lost = @($before | Where-Object { $_ -notlike 'sprk.Access.*' -and $after -notcontains $_ })
if ($lost.Count -gt 0) {
    throw "The merge dropped existing command(s): $($lost -join ', '). Nothing should be imported."
}

Write-Host "Merged the Access group into $Entity -> $Out"
Write-Host "Commands before: $($before -join ', ')"
Write-Host "Commands after : $($after -join ', ')"
