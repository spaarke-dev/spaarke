# sprk_registrationrequest — Ribbon Definition

> **Entity**: `sprk_registrationrequest`
> **Web Resource**: `sprk_/js/registrationribbon.js` (display name: Registration Ribbon Commands)
> **Solution**: DemoRegistration
> **Version**: 1.0.0

---

## Overview

Two ribbon buttons on the `sprk_registrationrequest` entity:

| Button | Location | Action | Enable Condition |
|--------|----------|--------|------------------|
| **Approve Demo Access** | Form + HomepageGrid | Calls BFF `POST /api/registration/requests/{id}/approve` | Form: `sprk_status = 0`; Grid: selection exists |
| **Reject Request** | Form + HomepageGrid | Prompts for reason, calls BFF `POST /api/registration/requests/{id}/reject` | Form: `sprk_status = 0`; Grid: selection exists |

---

## Complete RibbonDiffXml

Paste this XML into the entity's `RibbonDiffXml` node in the solution customizations.xml, or apply via Ribbon Workbench.

```xml
<!--
  Registration Request Ribbon Customization

  Adds "Approve Demo Access" and "Reject Request" buttons to:
  1. Form Command Bar - sprk_registrationrequest main form
  2. HomepageGrid - sprk_registrationrequest list view

  Web Resource: sprk_/js/registrationribbon.js
  API Endpoints:
    POST /api/registration/requests/{id}/approve
    POST /api/registration/requests/{id}/reject

  Version: 1.0.0
-->
<RibbonDiffXml>
  <CustomActions>

    <!-- ================================================================== -->
    <!-- 1. FORM: "Approve Demo Access" Button                              -->
    <!-- ================================================================== -->
    <CustomAction Id="Sprk.Registration.Approve.Form.CustomAction"
                  Location="Mscrm.Form.sprk_registrationrequest.MainTab.Actions.Controls._children"
                  Sequence="10">
      <CommandUIDefinition>
        <Button Id="Sprk.Registration.Approve.Form.Button"
                Command="Sprk.Registration.Approve.Form.Command"
                LabelText="Approve Demo Access"
                Alt="Approve Demo Access"
                ToolTipTitle="Approve Demo Access"
                ToolTipDescription="Approve this request and provision a demo account. Creates an Entra ID user, assigns licenses, and sends a welcome email."
                TemplateAlias="o1"
                Image16by16="/_imgs/ribbon/Approve_16.png"
                Image32by32="/_imgs/ribbon/Approve_32.png"
                ModernImage="Accept" />
      </CommandUIDefinition>
    </CustomAction>

    <!-- ================================================================== -->
    <!-- 2. FORM: "Reject Request" Button                                   -->
    <!-- ================================================================== -->
    <CustomAction Id="Sprk.Registration.Reject.Form.CustomAction"
                  Location="Mscrm.Form.sprk_registrationrequest.MainTab.Actions.Controls._children"
                  Sequence="20">
      <CommandUIDefinition>
        <Button Id="Sprk.Registration.Reject.Form.Button"
                Command="Sprk.Registration.Reject.Form.Command"
                LabelText="Reject Request"
                Alt="Reject Request"
                ToolTipTitle="Reject Request"
                ToolTipDescription="Reject this registration request with a reason."
                TemplateAlias="o1"
                Image16by16="/_imgs/ribbon/Cancel_16.png"
                Image32by32="/_imgs/ribbon/Cancel_32.png"
                ModernImage="Cancel" />
      </CommandUIDefinition>
    </CustomAction>

    <!-- ================================================================== -->
    <!-- 3. GRID: "Approve Demo Access" Button                              -->
    <!-- ================================================================== -->
    <CustomAction Id="Sprk.Registration.Approve.Grid.CustomAction"
                  Location="Mscrm.HomepageGrid.sprk_registrationrequest.MainTab.Actions.Controls._children"
                  Sequence="10">
      <CommandUIDefinition>
        <Button Id="Sprk.Registration.Approve.Grid.Button"
                Command="Sprk.Registration.Approve.Grid.Command"
                LabelText="Approve Demo Access"
                Alt="Approve Demo Access"
                ToolTipTitle="Approve Demo Access"
                ToolTipDescription="Approve selected request(s) and provision demo accounts. Supports multi-select for bulk approval."
                TemplateAlias="o1"
                Image16by16="/_imgs/ribbon/Approve_16.png"
                Image32by32="/_imgs/ribbon/Approve_32.png"
                ModernImage="Accept" />
      </CommandUIDefinition>
    </CustomAction>

    <!-- ================================================================== -->
    <!-- 4. GRID: "Reject Request" Button                                   -->
    <!-- ================================================================== -->
    <CustomAction Id="Sprk.Registration.Reject.Grid.CustomAction"
                  Location="Mscrm.HomepageGrid.sprk_registrationrequest.MainTab.Actions.Controls._children"
                  Sequence="20">
      <CommandUIDefinition>
        <Button Id="Sprk.Registration.Reject.Grid.Button"
                Command="Sprk.Registration.Reject.Grid.Command"
                LabelText="Reject Request"
                Alt="Reject Request"
                ToolTipTitle="Reject Request"
                ToolTipDescription="Reject selected registration request(s) with a reason."
                TemplateAlias="o1"
                Image16by16="/_imgs/ribbon/Cancel_16.png"
                Image32by32="/_imgs/ribbon/Cancel_32.png"
                ModernImage="Cancel" />
      </CommandUIDefinition>
    </CustomAction>

  </CustomActions>

  <CommandDefinitions>

    <!-- ================================================================== -->
    <!-- Command: Approve from Form                                         -->
    <!-- ================================================================== -->
    <CommandDefinition Id="Sprk.Registration.Approve.Form.Command">
      <EnableRules>
        <EnableRule Id="Sprk.Registration.Approve.Form.EnableRule" />
      </EnableRules>
      <DisplayRules />
      <Actions>
        <JavaScriptFunction Library="$webresource:sprk_/js/registrationribbon.js"
                           FunctionName="approveRequestFromForm">
          <CrmParameter Value="PrimaryControl" />
        </JavaScriptFunction>
      </Actions>
    </CommandDefinition>

    <!-- ================================================================== -->
    <!-- Command: Reject from Form                                          -->
    <!-- ================================================================== -->
    <CommandDefinition Id="Sprk.Registration.Reject.Form.Command">
      <EnableRules>
        <EnableRule Id="Sprk.Registration.Reject.Form.EnableRule" />
      </EnableRules>
      <DisplayRules />
      <Actions>
        <JavaScriptFunction Library="$webresource:sprk_/js/registrationribbon.js"
                           FunctionName="rejectRequestFromForm">
          <CrmParameter Value="PrimaryControl" />
        </JavaScriptFunction>
      </Actions>
    </CommandDefinition>

    <!-- ================================================================== -->
    <!-- Command: Approve from Grid (bulk)                                  -->
    <!-- ================================================================== -->
    <CommandDefinition Id="Sprk.Registration.Approve.Grid.Command">
      <EnableRules>
        <EnableRule Id="Sprk.Registration.Approve.Grid.EnableRule" />
      </EnableRules>
      <DisplayRules />
      <Actions>
        <JavaScriptFunction Library="$webresource:sprk_/js/registrationribbon.js"
                           FunctionName="approveRequest">
          <CrmParameter Value="SelectedItemReferences" />
        </JavaScriptFunction>
      </Actions>
    </CommandDefinition>

    <!-- ================================================================== -->
    <!-- Command: Reject from Grid                                          -->
    <!-- ================================================================== -->
    <CommandDefinition Id="Sprk.Registration.Reject.Grid.Command">
      <EnableRules>
        <EnableRule Id="Sprk.Registration.Reject.Grid.EnableRule" />
      </EnableRules>
      <DisplayRules />
      <Actions>
        <JavaScriptFunction Library="$webresource:sprk_/js/registrationribbon.js"
                           FunctionName="rejectRequest">
          <CrmParameter Value="SelectedItemReferences" />
        </JavaScriptFunction>
      </Actions>
    </CommandDefinition>

  </CommandDefinitions>

  <RuleDefinitions>
    <EnableRules>

      <!-- ================================================================ -->
      <!-- Enable Rule: Approve on Form — only when status = Submitted (0)  -->
      <!-- ================================================================ -->
      <EnableRule Id="Sprk.Registration.Approve.Form.EnableRule">
        <CustomRule Library="$webresource:sprk_/js/registrationribbon.js"
                   FunctionName="enableApproveButtonForm"
                   Default="false">
          <CrmParameter Value="PrimaryControl" />
        </CustomRule>
      </EnableRule>

      <!-- ================================================================ -->
      <!-- Enable Rule: Reject on Form — only when status = Submitted (0)   -->
      <!-- ================================================================ -->
      <EnableRule Id="Sprk.Registration.Reject.Form.EnableRule">
        <CustomRule Library="$webresource:sprk_/js/registrationribbon.js"
                   FunctionName="enableRejectButtonForm"
                   Default="false">
          <CrmParameter Value="PrimaryControl" />
        </CustomRule>
      </EnableRule>

      <!-- ================================================================ -->
      <!-- Enable Rule: Approve on Grid — enabled when items selected       -->
      <!-- ================================================================ -->
      <EnableRule Id="Sprk.Registration.Approve.Grid.EnableRule">
        <CustomRule Library="$webresource:sprk_/js/registrationribbon.js"
                   FunctionName="enableApproveButton"
                   Default="false">
          <CrmParameter Value="SelectedItemReferences" />
        </CustomRule>
      </EnableRule>

      <!-- ================================================================ -->
      <!-- Enable Rule: Reject on Grid — enabled when items selected        -->
      <!-- ================================================================ -->
      <EnableRule Id="Sprk.Registration.Reject.Grid.EnableRule">
        <CustomRule Library="$webresource:sprk_/js/registrationribbon.js"
                   FunctionName="enableRejectButton"
                   Default="false">
          <CrmParameter Value="SelectedItemReferences" />
        </CustomRule>
      </EnableRule>

    </EnableRules>
  </RuleDefinitions>
</RibbonDiffXml>
```

---

## Deployment Steps

### Step 1: Deploy Web Resource

1. Open the DemoRegistration solution in Dataverse
2. Add Web Resource:
   - **Name**: `sprk_/js/registrationribbon.js`
   - **Display Name**: Registration Ribbon Commands
   - **Type**: Script (JScript)
   - **Source**: `src/client/webresources/js/sprk_registrationribbon.js`
3. Publish the web resource

### Step 2: Apply Ribbon XML

**Option A: Ribbon Workbench (recommended)**
1. Open Ribbon Workbench in XrmToolBox
2. Load the DemoRegistration solution
3. Select `sprk_registrationrequest` entity
4. Add buttons using the XML definitions above
5. Publish

**Option B: Direct XML edit**
1. Export the DemoRegistration solution (unmanaged)
2. Open `customizations.xml`
3. Find the `<Entity>` node for `sprk_registrationrequest`
4. Replace or add the `<RibbonDiffXml>` content from above
5. Re-import and publish

### Step 3: Test

**Form buttons:**
1. Open a registration request with `sprk_status = Submitted (0)`
2. Verify "Approve Demo Access" and "Reject Request" buttons are visible and enabled
3. Change status to any other value -- verify buttons are disabled
4. Click Approve -- confirm dialog appears, API is called, form refreshes
5. Click Reject -- reason prompt appears, API is called with reason

**Grid buttons:**
1. Navigate to Registration Requests list view
2. Select one or more rows with status = Submitted
3. Verify "Approve Demo Access" and "Reject Request" buttons appear in command bar
4. Test single-select approve
5. Test multi-select approve (bulk)
6. Test reject with reason prompt
7. Verify grid refreshes after operations

---

## Button Behavior Summary

### Approve Demo Access

| Context | CrmParameter | Function | Behavior |
|---------|-------------|----------|----------|
| Form | `PrimaryControl` | `approveRequestFromForm` | Confirm dialog, single API call, form refresh |
| Grid | `SelectedItemReferences` | `approveRequest` | Confirm dialog (shows names), sequential API calls with progress, grid refresh |

### Reject Request

| Context | CrmParameter | Function | Behavior |
|---------|-------------|----------|----------|
| Form | `PrimaryControl` | `rejectRequestFromForm` | Custom reason prompt, single API call, form refresh |
| Grid | `SelectedItemReferences` | `rejectRequest` | Custom reason prompt (shared), sequential API calls with progress, grid refresh |

### Enable Rules

| Context | Function | Condition |
|---------|----------|-----------|
| Form | `enableApproveButtonForm` | `sprk_status === 0` (Submitted) |
| Form | `enableRejectButtonForm` | `sprk_status === 0` (Submitted) |
| Grid | `enableApproveButton` | At least one item selected |
| Grid | `enableRejectButton` | At least one item selected |

> **Note**: Grid enable rules check selection only. Status validation for grid operations
> happens server-side in the BFF API (returns error for non-Submitted records). This avoids
> the complexity of async Xrm.WebApi calls in synchronous enable rules.

---

## Authentication

The JavaScript uses MSAL browser library to acquire a bearer token for the BFF API:

1. **Silent acquisition** -- tries cached token for the current account
2. **SSO silent** -- leverages existing Dataverse session
3. **Popup fallback** -- interactive login if silent methods fail

The token targets the `api://{bffAppId}/SDAP.Access` scope. The BFF API validates the caller has the `Spaarke Registration Admin` role.

---

## Related Files

| File | Purpose |
|------|---------|
| `src/client/webresources/js/sprk_registrationribbon.js` | JavaScript web resource |
| `src/solutions/DemoRegistration/schema-definition.md` | Entity schema (sprk_status values) |
| `src/client/webresources/js/sprk_aichatcontextmap_ribbon.js` | Reference: similar MSAL + BFF pattern |
| `src/client/webresources/ribbon/sprk_aichatcontextmap_ribbon.xml` | Reference: ribbon XML pattern |
