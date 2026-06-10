<#
.SYNOPSIS
    Generates per-entity RibbonDiffXml fragments adding a "Create To Do" button to
    each of the 10 non-Matter parent forms.

.DESCRIPTION
    smart-todo-decoupling-r3 task 040 / Phase 5 - Parent-Form Ribbon (source generator).

    Matter already has the "Create To Do" form ribbon command via UDSS-041
    (infrastructure/dataverse/ribbon/MatterRibbons/customizations.xml). The other
    10 entities do NOT.

    This script writes ribbon-diff XML fragments for each of the remaining 10
    entities to `infrastructure/dataverse/ribbon/<DisplayName>Ribbons/createtodo-button.xml`.
    Each fragment can be merged into the entity's dedicated ribbon solution
    customizations.xml and re-imported via the ribbon-edit skill workflow.

    All fragments use:
    - JavaScript handler: Spaarke.Commands.Wizards.openCreateTodoWizard
        (already deployed via sprk_wizard_commands.js)
    - Passes PrimaryControl - the JS reads entityType + entityId via
        primaryControl.data.entity.* and passes them in the wizard URL query string,
        satisfying the createtodo-launch-contract (task 032).
    - Location: Mscrm.Form.<entity>.MainTab.Actions.Controls._children
    - Sequence: 220 (matches Matter pattern)
    - Display only on existing (saved) records (FormStateRule State="Existing").

    Deployment of these fragments is a follow-up step per the ribbon-edit skill:
    each entity needs a dedicated unmanaged solution containing only its entity
    metadata, the fragment merged into customizations.xml, re-packed and imported.

.PARAMETER OutputRoot
    Destination root (defaults to infrastructure/dataverse/ribbon/).
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$OutputRoot = $null
)

$ErrorActionPreference = "Stop"

if (-not $OutputRoot) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $OutputRoot = Join-Path $repoRoot "infrastructure/dataverse/ribbon"
}

$entityConfig = @(
    @{ entity = 'sprk_project';         displayName = 'Project' }
    @{ entity = 'sprk_event';           displayName = 'Event' }
    @{ entity = 'sprk_communication';   displayName = 'Communication' }
    @{ entity = 'sprk_workassignment';  displayName = 'WorkAssignment' }
    @{ entity = 'sprk_invoice';         displayName = 'Invoice' }
    @{ entity = 'sprk_budget';          displayName = 'Budget' }
    @{ entity = 'sprk_analysis';        displayName = 'Analysis' }
    @{ entity = 'sprk_organization';    displayName = 'Organization' }
    @{ entity = 'contact';              displayName = 'Contact' }
    @{ entity = 'sprk_document';        displayName = 'Document' }
)

function New-CreateTodoRibbonDiffXml {
    param([string]$Entity, [string]$DisplayName)

    $loc = "Mscrm.Form.$Entity.MainTab.Actions.Controls._children"

    return @"
<?xml version="1.0" encoding="utf-8"?>
<!--
  $DisplayName Entity - Create To Do Wizard Button
  smart-todo-decoupling-r3 task 040 / Phase 5 / FR-17

  Adds a "Create To Do" button (Sequence 220) to the $Entity form command bar
  that launches Spaarke.Commands.Wizards.openCreateTodoWizard. The handler
  reads entityType + entityId from primaryControl and passes them in the wizard
  URL query string per createtodo-launch-contract (task 032) - the wizard
  pre-fills initialRegarding to the host record.

  Deployment:
   1. Create dedicated unmanaged ribbon solution: ${DisplayName}Ribbons
      Publisher: Spaarke; include only the $Entity entity metadata.
   2. Export the solution.
   3. Merge the <RibbonDiffXml> below into the solution's customizations.xml
      <RibbonDiffXml> for entity '$Entity'.
   4. Re-pack and import via 'pac solution import --publish-changes'.

  Prerequisite (already met):
    sprk_wizard_commands.js web resource is deployed and exposes
    Spaarke.Commands.Wizards.openCreateTodoWizard (see UDSS-040 history).
-->
<ImportExportXml>
  <Entities>
    <Entity>
      <Name>$Entity</Name>
      <RibbonDiffXml>
        <CustomActions>
          <CustomAction Id="sprk.Wizard.$DisplayName.CreateTodo.CustomAction"
                        Location="$loc"
                        Sequence="220">
            <CommandUIDefinition>
              <Button Id="sprk.Wizard.$DisplayName.CreateTodo.Button"
                      Command="sprk.Wizard.$DisplayName.CreateTodo.Command"
                      LabelText="`$LocLabels:sprk.Wizard.CreateTodo.Label"
                      Alt="`$LocLabels:sprk.Wizard.CreateTodo.Label"
                      ToolTipTitle="`$LocLabels:sprk.Wizard.CreateTodo.ToolTipTitle"
                      ToolTipDescription="`$LocLabels:sprk.Wizard.CreateTodo.ToolTipDescription"
                      Image16by16="/_imgs/ribbon/newrecord16.png"
                      Image32by32="/_imgs/ribbon/newrecord32.png"
                      TemplateAlias="o1" />
            </CommandUIDefinition>
          </CustomAction>
        </CustomActions>
        <Templates>
          <RibbonTemplates Id="Mscrm.Templates"></RibbonTemplates>
        </Templates>
        <CommandDefinitions>
          <CommandDefinition Id="sprk.Wizard.$DisplayName.CreateTodo.Command">
            <EnableRules>
              <EnableRule Id="sprk.Wizard.$DisplayName.Form.EnableRule" />
            </EnableRules>
            <DisplayRules>
              <DisplayRule Id="sprk.Wizard.$DisplayName.Form.DisplayRule" />
            </DisplayRules>
            <Actions>
              <JavaScriptFunction Library="`$webresource:sprk_wizard_commands.js"
                                  FunctionName="Spaarke.Commands.Wizards.openCreateTodoWizard">
                <CrmParameter Value="PrimaryControl" />
              </JavaScriptFunction>
            </Actions>
          </CommandDefinition>
        </CommandDefinitions>
        <RuleDefinitions>
          <TabDisplayRules />
          <DisplayRules>
            <DisplayRule Id="sprk.Wizard.$DisplayName.Form.DisplayRule">
              <FormStateRule State="Existing" />
            </DisplayRule>
          </DisplayRules>
          <EnableRules>
            <EnableRule Id="sprk.Wizard.$DisplayName.Form.EnableRule">
              <FormStateRule State="Existing" Default="true" />
            </EnableRule>
          </EnableRules>
        </RuleDefinitions>
        <LocLabels>
          <LocLabel Id="sprk.Wizard.CreateTodo.Label">
            <Titles>
              <Title languagecode="1033" description="Create To Do" />
            </Titles>
          </LocLabel>
          <LocLabel Id="sprk.Wizard.CreateTodo.ToolTipTitle">
            <Titles>
              <Title languagecode="1033" description="Create To Do" />
            </Titles>
          </LocLabel>
          <LocLabel Id="sprk.Wizard.CreateTodo.ToolTipDescription">
            <Titles>
              <Title languagecode="1033" description="Create a new to-do item linked to this record" />
            </Titles>
          </LocLabel>
        </LocLabels>
      </RibbonDiffXml>
    </Entity>
  </Entities>
</ImportExportXml>
"@
}

Write-Host ''
Write-Host '================================================' -ForegroundColor Cyan
Write-Host ' Generate Create To Do RibbonDiffXml (10 entities)' -ForegroundColor Cyan
Write-Host '================================================' -ForegroundColor Cyan
Write-Host "Output root: $OutputRoot"
Write-Host ''

$results = @()
foreach ($cfg in $entityConfig) {
    $entity = $cfg.entity
    $displayName = $cfg.displayName
    $folderName = "${displayName}Ribbons"
    $folder = Join-Path $OutputRoot $folderName
    $filePath = Join-Path $folder "createtodo-button.xml"

    if (-not (Test-Path $folder)) {
        New-Item -ItemType Directory -Path $folder -Force | Out-Null
        Write-Host "  Created folder: $folder" -ForegroundColor Gray
    }

    $xml = New-CreateTodoRibbonDiffXml -Entity $entity -DisplayName $displayName
    Set-Content -Path $filePath -Value $xml -Encoding UTF8
    Write-Host "  [$entity] -> $filePath" -ForegroundColor Green
    $results += [pscustomobject]@{ Entity = $entity; DisplayName = $displayName; File = $filePath }
}

Write-Host ''
$results | Format-Table -AutoSize Entity, DisplayName, File
Write-Host ''
Write-Host "  Wrote 10 ribbon-diff XML fragments." -ForegroundColor Green
Write-Host "  These are SOURCE artifacts only - deployment requires the ribbon-edit" -ForegroundColor Yellow
Write-Host "  skill workflow (dedicated solution per entity, merge + import + publish)." -ForegroundColor Yellow
Write-Host ''
