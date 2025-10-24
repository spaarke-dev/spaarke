Ribbon Workbench How To Guide

Complete Ribbon Definition:

<RibbonDiffXml>
  <CustomActions>
    <!-- Add button to Documents subgrid -->
    <CustomAction Id="Spaarke.Document.AddMultiple.CustomAction"
                  Location="Mscrm.SubGrid.sprk_document.MainTab.Actions.Controls._children"
                  Sequence="5">
      <CommandUIDefinition>
        <Button Id="Spaarke.Document.AddMultiple.Button"
                Command="Spaarke.Document.AddMultiple.Command"
                LabelText="Add Documents"
                Alt="Add Documents"
                ToolTipTitle="Add Documents"
                ToolTipDescription="Upload multiple documents to this record"
                TemplateAlias="o1"
                Image16by16="/_imgs/ribbon/DocumentAdd_16.png"
                Image32by32="/_imgs/ribbon/DocumentAdd_32.png" />
      </CommandUIDefinition>
    </CustomAction>
  </CustomActions>
  
  <CommandDefinitions>
    <CommandDefinition Id="Spaarke.Document.AddMultiple.Command">
      <EnableRules>
        <EnableRule Id="Spaarke.Document.AddMultiple.EnableRule" />
      </EnableRules>
      <DisplayRules />
      <Actions>
        <JavaScriptFunction Library="$webresource:sprk_subgrid_commands.js"
                           FunctionName="Spaarke_AddMultipleDocuments">
          <!-- CRITICAL: SelectedControl must be first parameter -->
          <CrmParameter Value="SelectedControl" />
        </JavaScriptFunction>
      </Actions>
    </CommandDefinition>
  </CommandDefinitions>
  
  <RuleDefinitions>
    <EnableRules>
      <EnableRule Id="Spaarke.Document.AddMultiple.EnableRule">
        <JavaScriptFunction Library="$webresource:sprk_subgrid_commands.js"
                           FunctionName="Spaarke_EnableAddDocuments">
          <CrmParameter Value="SelectedControl" />
        </JavaScriptFunction>
      </EnableRule>
    </EnableRules>
  </RuleDefinitions>
</RibbonDiffXml>

Step 1: Deploy Web Resource

# Upload sprk_subgrid_commands.js as web resource
# Name: sprk_subgrid_commands
# Type: JavaScript (JScript)
# Publish

Step 2: Add Ribbon

Use Ribbon Workbench or XML editor
Configure for sprk_document entity
Location: SubGrid commands
Apply to all forms

Step 3: Test on Matter Entity

Open existing Matter record
Scroll to Documents subgrid
Verify "+ Add Documents" button appears
Click button → Dialog should open
Check browser console for parameters
Upload files
Verify subgrid refreshes

Step 4: Test on Account Entity

Repeat Step 3 with Account record
Verify containerId retrieved correctly
Verify documents link to Account

Add New Entity

// In ENTITY_CONFIGURATIONS object, add:
"sprk_case": {
    entityLogicalName: "sprk_case",
    containerIdField: "sprk_containerid",
    displayNameFields: ["sprk_casenumber", "sprk_title"],
    entityDisplayName: "Case"
}