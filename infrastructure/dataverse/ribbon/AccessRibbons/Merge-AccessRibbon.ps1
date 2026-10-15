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

.PARAMETER ShareCommandXml
    Task 114 (owner round 67 amendment 4(a)): a file holding the PLATFORM's form Share CommandDefinition(s) for -Entity,
    exactly as the live effective ribbon has them (Set-AccessRibbon.ps1 reads them with RetrieveEntityRibbon: the
    commands of Mscrm.Form.<entity>.Permissions.Sharing - the Unified Interface Share - and, when present,
    Mscrm.Form.<entity>.Permissions.SharingNonRefresh in the legacy Permissions flyout). The merge copies each into the
    RibbonDiff (replacing an earlier copy), strips any sprk.Access.* rule reference from it, and appends
    sprk.Access.<entity>.ShareAllowed.EnableRule - so Share keeps every platform rule and is also hidden on a Restricted
    record. Without it the Share rule is NOT applied (a warning says so); never hand-author the platform command.

.PARAMETER GridShareCommandXml
    Task 114 follow-up: a file holding the PLATFORM's GRID and SUBGRID Share CommandDefinition(s) for -Entity, as the live
    effective ribbon has them (the commands of Mscrm.HomepageGrid.<entity>.Sharing and Mscrm.SubGrid.<entity>.Sharing - one
    definition when both buttons use the same command). Each is copied with sprk.Access.<entity>.ShareAllowedSelection
    .EnableRule appended: Share is hidden when ANY selected row is Restricted. A command shared with the FORM button is
    refused (one copy cannot carry both rules). Without it the grid Share rule is NOT applied (a warning says so).

.PARAMETER SecureTransitionDeployed
    Task 150 (UX amendment acceptance (b); owner R3b / F7): include "Make Secure". Pass it ONLY when the target
    environment's BFF carries task 148's provisioning transition (existing children follow the record), round 26 item
    3's file relocation (task 166's DocumentContainerRelocator, wired at integration) AND its scheduled backstop (round 46
    item 2: task 147's SecureChildReconciliationJob settling pending Make Secure relocations, writes on), which ship in
    the same release as Make Secure. Without it every sprk.Access.<entity>.MakeSecure.*
    node is removed from the instantiated template, so Make Secure is absent while Update Access and Remove Secure ship.

.EXAMPLE
    pwsh ./Merge-AccessRibbon.ps1 -ExportedRibbonDiff ./export/Entities/sprk_Project/RibbonDiff.xml `
        -Entity sprk_project -Out ./export/Entities/sprk_Project/RibbonDiff.xml -SecureTransitionDeployed
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ExportedRibbonDiff,
    [Parameter(Mandatory)] [ValidateSet('sprk_project', 'sprk_matter', 'sprk_workassignment')] [string] $Entity,
    [Parameter(Mandatory)] [string] $Out,
    [string] $ShareCommandXml,
    [string] $GridShareCommandXml,
    [switch] $SecureTransitionDeployed
)

$ErrorActionPreference = 'Stop'

$templatePath = Join-Path $PSScriptRoot 'access-group.template.xml'
$templateText = (Get-Content -Raw -LiteralPath $templatePath).Replace('{{entity}}', $Entity)
[xml] $template = $templateText

# Task 150: Make Secure is release-gated (acceptance (b)) - removed here unless the transition is deployed.
$makeSecurePrefix = "sprk.Access.$Entity.MakeSecure."
$makeSecureButtonId = "sprk.Access.$Entity.Form.MakeSecure"
if (-not $SecureTransitionDeployed) {
    $gated = @($template.SelectNodes('//*[@Id]') | Where-Object {
            $id = $_.GetAttribute('Id')
            $id.StartsWith($makeSecurePrefix) -or $id -eq $makeSecureButtonId
        })
    # Five: the menu button, the command, the command's reference to its enable rule, the enable rule, the label.
    if (@($gated).Count -ne 5) {
        throw "Expected 5 Make Secure nodes in the template (button, command, rule reference, rule, label), found $(@($gated).Count)."
    }
    foreach ($node in $gated) {
        [void] $node.ParentNode.RemoveChild($node)
    }
}

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

# Task 114 (owner round 67 amendment 4(a) + follow-up): the PLATFORM's Share commands, copied from the live ribbon, each
# with a sprk.Access rule appended - the FORM command (ShareAllowed: this record is Restricted) and the GRID / SUBGRID
# command(s) (ShareAllowedSelection: any selected row is Restricted). Never hand-authored: the copy keeps the platform's
# rules. A re-run replaces each copy (idempotent).
function Add-PlatformCommandRule([string] $path, [string] $ruleId, [string] $what) {
    [xml] $doc = Get-Content -Raw -LiteralPath $path
    $commands = @($doc.SelectNodes('//*[local-name()="CommandDefinition"]'))
    if ($commands.Count -eq 0) { throw "No CommandDefinition in $path." }
    $ids = @()
    foreach ($command in $commands) {
        $id = $command.GetAttribute('Id')
        if (-not $id -or $id.StartsWith('sprk.')) { throw "'$id' is not a platform command ($what)." }
        if ($ids -contains $id) { continue }
        $ids += $id

        foreach ($node in @($commandDefinitions.ChildNodes)) {
            if ($node -is [System.Xml.XmlElement] -and $node.GetAttribute('Id') -eq $id) {
                [void] $commandDefinitions.RemoveChild($node)
            }
        }

        $copy = $ribbon.ImportNode($command, $true)
        $rules = $copy.SelectSingleNode("*[local-name()='EnableRules']")
        if (-not $rules) {
            $rules = $ribbon.CreateElement('EnableRules', $copy.NamespaceURI)
            [void] $copy.PrependChild($rules)
        }
        foreach ($rule in @($rules.ChildNodes)) {
            if ($rule -is [System.Xml.XmlElement] -and $rule.GetAttribute('Id').StartsWith('sprk.Access.')) {
                [void] $rules.RemoveChild($rule) # the live copy of a previous run's override
            }
        }
        $newRule = $ribbon.CreateElement('EnableRule', $copy.NamespaceURI)
        $newRule.SetAttribute('Id', $ruleId)
        [void] $rules.AppendChild($newRule)
        [void] $commandDefinitions.AppendChild($copy)
    }
    return $ids
}

$shareRuleId = "sprk.Access.$Entity.ShareAllowed.EnableRule"
$gridShareRuleId = "sprk.Access.$Entity.ShareAllowedSelection.EnableRule"
$shareCommandIds = @()
$gridShareCommandIds = @()
if ($ShareCommandXml) {
    $shareCommandIds = @(Add-PlatformCommandRule $ShareCommandXml $shareRuleId "the form Share command")
}
else {
    Write-Warning ("No -ShareCommandXml: the platform's FORM Share command is NOT hidden on Restricted $Entity records. " +
        "Set-AccessRibbon.ps1 -Apply reads it from the live ribbon; never hand-author it.")
}
if ($GridShareCommandXml) {
    [xml] $gridDoc = Get-Content -Raw -LiteralPath $GridShareCommandXml
    $overlap = @($gridDoc.SelectNodes('//*[local-name()="CommandDefinition"]') |
        ForEach-Object { $_.GetAttribute('Id') } | Where-Object { $shareCommandIds -contains $_ })
    if ($overlap.Count -gt 0) {
        throw "The form and grid Share buttons share command '$($overlap -join ', ')': one copy cannot carry both rules. Nothing was merged."
    }
    $gridShareCommandIds = @(Add-PlatformCommandRule $GridShareCommandXml $gridShareRuleId "the grid / subgrid Share command")
}
else {
    Write-Warning ("No -GridShareCommandXml: the platform's GRID / SUBGRID Share command is NOT hidden for Restricted $Entity rows. " +
        "Set-AccessRibbon.ps1 -Apply reads it from the live ribbon; never hand-author it.")
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

$menuItems = @(([xml] (Get-Content -Raw -LiteralPath $Out)).SelectNodes(
        "//*[local-name()='Button' and starts-with(@Id, 'sprk.Access.$Entity.Form.')]") |
    ForEach-Object { $_.GetAttribute('Id').Substring("sprk.Access.$Entity.Form.".Length) })

Write-Host "Merged the Access group into $Entity -> $Out"
Write-Host "Commands before: $($before -join ', ')"
Write-Host "Commands after : $($after -join ', ')"
Write-Host "Access menu    : $($menuItems -join ', ')$(if (-not $SecureTransitionDeployed) { '   (Make Secure withheld: -SecureTransitionDeployed not given)' })"
Write-Host "Share command  : $(if ($shareCommandIds.Count) { "$($shareCommandIds -join ', ') + $shareRuleId (hidden on Restricted records)" } else { 'NOT changed (no -ShareCommandXml)' })"
Write-Host "Grid Share     : $(if ($gridShareCommandIds.Count) { "$($gridShareCommandIds -join ', ') + $gridShareRuleId (hidden when any selected row is Restricted)" } else { 'NOT changed (no -GridShareCommandXml)' })"
