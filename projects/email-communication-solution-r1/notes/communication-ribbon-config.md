# Communication Entity Ribbon Configuration

> **Entity**: `sprk_communication`
> **Web Resource**: `sprk_communication_send` (sprk_communication_send.js)
> **Task**: 022 - Create Send command bar button
> **Date**: 2026-02-21

---

## Overview

This document defines the RibbonDiffXml customization required to add a "Send" button to the `sprk_communication` entity main form command bar. The button calls the BFF `POST /api/communications/send` endpoint via the `sprk_communication_send.js` web resource.

The pattern follows the existing Event entity ribbon customization (`EventRibbonDiffXml.xml`).

---

## Web Resource Registration

| Property | Value |
|----------|-------|
| Name | `sprk_communication_send` |
| Display Name | Communication Send Command |
| Type | Script (JScript) |
| File | `src/solutions/LegalWorkspace/src/WebResources/sprk_communication_send.js` |

---

## RibbonDiffXml

```xml
<!--
  Communication Entity Ribbon Customization

  Buttons added:
  - Main Form: Send (enabled only when statuscode = Draft)

  Web Resource: sprk_communication_send.js

  Communication Status Values (statuscode):
  - 1: Draft, 659490001: Queued, 659490002: Send, 659490003: Delivered,
    659490004: Failed, 659490005: Bounded, 659490006: Recalled

  Deployment:
  1. Upload sprk_communication_send.js as web resource
  2. Use Ribbon Workbench to import this XML
     OR manually add to Communication entity customization XML
  3. Publish customizations

  @see src/solutions/LegalWorkspace/src/WebResources/sprk_communication_send.js
  @see docs/data-model/sprk_communication-data-schema.md
-->
<RibbonDiffXml>
  <CustomActions>

    <!-- Send Button - Main Form Command Bar -->
    <CustomAction Id="sprk.communication.send.CustomAction"
                  Location="Mscrm.Form.sprk_communication.MainTab.Actions.Controls._children"
                  Sequence="10">
      <CommandUIDefinition>
        <Button Id="sprk.communication.send.Button"
                Command="sprk.communication.send.Command"
                LabelText="Send"
                Alt="Send Communication"
                ToolTipTitle="Send Communication"
                ToolTipDescription="Send this communication via email"
                TemplateAlias="o1"
                Image16by16="$webresource:sprk_/icons/send_16.svg"
                Image32by32="$webresource:sprk_/icons/send_32.svg"
                ModernImage="Send" />
      </CommandUIDefinition>
    </CustomAction>

  </CustomActions>

  <CommandDefinitions>

    <!-- Send Command Definition -->
    <CommandDefinition Id="sprk.communication.send.Command">
      <EnableRules>
        <EnableRule Id="sprk.communication.isStatusDraft.EnableRule" />
      </EnableRules>
      <DisplayRules />
      <Actions>
        <JavaScriptFunction Library="$webresource:sprk_communication_send"
                           FunctionName="Sprk.Communication.Send.sendCommunication">
          <CrmParameter Value="PrimaryControl" />
        </JavaScriptFunction>
      </Actions>
    </CommandDefinition>

  </CommandDefinitions>

  <RuleDefinitions>
    <TabDisplayRules />
    <DisplayRules />
    <EnableRules>

      <!-- Enable rule: Communication is in Draft status (statuscode = 1) -->
      <EnableRule Id="sprk.communication.isStatusDraft.EnableRule">
        <CustomRule Library="$webresource:sprk_communication_send"
                    FunctionName="Sprk.Communication.Send.isStatusDraft"
                    Default="false">
          <CrmParameter Value="PrimaryControl" />
        </CustomRule>
      </EnableRule>

    </EnableRules>
  </RuleDefinitions>

</RibbonDiffXml>
```

---

## Component Details

### CustomAction

| Attribute | Value |
|-----------|-------|
| Id | `sprk.communication.send.CustomAction` |
| Location | `Mscrm.Form.sprk_communication.MainTab.Actions.Controls._children` |
| Sequence | `10` (first custom button in the command bar) |

The Location places the button in the main form's Actions section of the command bar. This is the standard location for entity-specific action buttons.

### Button

| Attribute | Value |
|-----------|-------|
| Id | `sprk.communication.send.Button` |
| Command | `sprk.communication.send.Command` |
| LabelText | `Send` |
| TemplateAlias | `o1` (standard form button template) |
| ModernImage | `Send` (Fluent UI icon for Unified Interface) |

### CommandDefinition

| Attribute | Value |
|-----------|-------|
| Id | `sprk.communication.send.Command` |
| EnableRule | `sprk.communication.isStatusDraft.EnableRule` |
| Library | `$webresource:sprk_communication_send` |
| FunctionName | `Sprk.Communication.Send.sendCommunication` |
| CrmParameter | `PrimaryControl` (passes the form context) |

### EnableRule

| Attribute | Value |
|-----------|-------|
| Id | `sprk.communication.isStatusDraft.EnableRule` |
| Library | `$webresource:sprk_communication_send` |
| FunctionName | `Sprk.Communication.Send.isStatusDraft` |
| Default | `false` (button is disabled by default until rule evaluates) |
| CrmParameter | `PrimaryControl` (passes the form context) |

The enable rule calls `Sprk.Communication.Send.isStatusDraft(formContext)` which returns `true` only when `statuscode === 1` (Draft). This ensures:
- Button is **enabled** on new/Draft records
- Button is **disabled** after send (statuscode = Send/Delivered/Failed/etc.)
- Button is **disabled** on inactive records

---

## Deployment Procedure

### Option 1: Ribbon Workbench (Recommended)

1. Open Ribbon Workbench from the XrmToolBox
2. Select the unmanaged solution containing `sprk_communication`
3. Load the `sprk_communication` entity
4. Add the Send button to the Form command bar
5. Configure the command, enable rule, and JavaScript library as above
6. Publish the solution

### Option 2: Manual XML Import

1. Export the unmanaged solution containing `sprk_communication`
2. Extract the `.zip` file
3. Open `customizations.xml`
4. Locate the `<Entity>` element for `sprk_communication`
5. Replace or merge the `<RibbonDiffXml>` section with the XML above
6. Repackage and import the solution
7. Publish all customizations

### Option 3: pac CLI (Power Platform CLI)

```powershell
# Export solution
pac solution export --path ./CommunicationSolution.zip --name LegalWorkspace --overwrite

# Extract to edit ribbon XML
pac solution unpack --zipfile ./CommunicationSolution.zip --folder ./CommunicationSolution

# Edit the entity ribbon XML
# Locate: CommunicationSolution/Entities/sprk_communication/RibbonDiffXml/RibbonDiffXml.xml
# Replace with the XML above

# Repack
pac solution pack --zipfile ./CommunicationSolution.zip --folder ./CommunicationSolution

# Import
pac solution import --path ./CommunicationSolution.zip --publish-changes
```

---

## Web Resource Upload

Before adding the ribbon, upload the web resource:

```powershell
# Using pac CLI
pac webresource push --path "src/solutions/LegalWorkspace/src/WebResources/sprk_communication_send.js" --name "sprk_communication_send" --type "Script (JScript)"
```

Or via the Power Platform maker portal:
1. Navigate to **Solutions** > **LegalWorkspace** (or target solution)
2. Add New > Web Resource
3. Name: `sprk_communication_send`
4. Type: `Script (JScript)`
5. Upload `sprk_communication_send.js`
6. Save and Publish

---

## Icon Resources

The ribbon references icon web resources. If `sprk_/icons/send_16.svg` and `sprk_/icons/send_32.svg` are not available, either:

1. Upload appropriate SVG send icons as web resources, or
2. Use the `ModernImage="Send"` attribute which maps to the built-in Fluent UI Send icon in Unified Interface (no separate SVG needed)
3. Remove the `Image16by16` and `Image32by32` attributes and rely solely on `ModernImage`

---

## Testing Checklist

- [ ] Web resource `sprk_communication_send` is published
- [ ] Send button is visible on the `sprk_communication` main form command bar
- [ ] Button is **enabled** when statuscode = 1 (Draft)
- [ ] Button is **disabled** when statuscode = 659490002 (Send)
- [ ] Button is **disabled** when statuscode = 659490003 (Delivered)
- [ ] Button is **disabled** when statuscode = 659490004 (Failed)
- [ ] Clicking Send on a Draft record collects form data and calls BFF
- [ ] On success: status updates to Send, success notification shown
- [ ] On error: ProblemDetails parsed, error notification shown with title and detail
- [ ] After successful send, button becomes disabled (no longer Draft)

---

## References

- [Event Ribbon Implementation](../../src/solutions/EventCommands/EventRibbonDiffXml.xml) - Pattern reference
- [Ribbon Workbench Guide](../../docs/guides/RIBBON-WORKBENCH-HOW-TO-ADD-BUTTON.md) - How-to guide
- [CommunicationEndpoints.cs](../../src/server/api/Sprk.Bff.Api/Api/CommunicationEndpoints.cs) - BFF API endpoint
- [Data Schema](../../docs/data-model/sprk_communication-data-schema.md) - Entity field reference
