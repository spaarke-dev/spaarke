# Task 1.4: Configure PCF Control Manifest

**Sprint:** Sprint 5 - Universal Dataset PCF Component
**Phase:** 1 - Project Scaffolding & Foundation
**Estimated Time:** 2 hours
**Prerequisites:** [TASK-1.3-WORKSPACE-LINKING.md](./TASK-1.3-WORKSPACE-LINKING.md)
**Next Task:** [TASK-2.1-CORE-COMPONENT-STRUCTURE.md](./TASK-2.1-CORE-COMPONENT-STRUCTURE.md)

---

## Objective

Configure the PCF control manifest (`ControlManifest.Input.xml`) to define all input properties, output properties, dataset binding, resources, and feature usage. The manifest is the contract between the control and Power Platform.

**Why:** The manifest defines what data the control receives (dataset, configuration properties) and what it returns (selected records, last action). All UI configuration must be declared here for Power Platform to expose it in form designers.

---

## Critical Standards

**MUST READ BEFORE STARTING:**
- [KM-PCF-CONTROL-STANDARDS.md](../../../docs/KM-PCF-CONTROL-STANDARDS.md) - Manifest schema, property types
- [ADR-011: Dataset PCF Over Subgrids](../../../docs/adr/ADR-011-dataset-pcf-over-subgrids.md) - Configuration properties

**Key Rules:**
- ✅ Use semantic property names (not `prop1`, `prop2`)
- ✅ Provide descriptions for all properties (visible in Power Apps Studio)
- ✅ Use enums for fixed choices (viewMode, theme)
- ✅ Mark optional properties with `required="false"`
- ✅ Declare all resources (React, ReactDOM, Fluent UI)

---

## Step 1: Navigate to PCF Project

```bash
cd c:\code_files\spaarke\power-platform\pcf\UniversalDataset
```

---

## Step 2: Replace ControlManifest.Input.xml

**Replace the entire contents of `ControlManifest.Input.xml`:**

```xml
<?xml version="1.0" encoding="utf-8" ?>
<manifest>
  <control namespace="Spaarke" constructor="UniversalDataset" version="1.0.0" display-name-key="UniversalDataset_Display_Key" description-key="UniversalDataset_Desc_Key" control-type="standard">

    <!-- ============================= -->
    <!-- DATASET BINDING               -->
    <!-- ============================= -->
    <data-set name="datasetGrid" display-name-key="Dataset_Display_Key" description-key="Dataset_Desc_Key" />

    <!-- ============================= -->
    <!-- VIEW CONFIGURATION            -->
    <!-- ============================= -->
    <property name="viewMode" display-name-key="ViewMode_Display_Key" description-key="ViewMode_Desc_Key" of-type="Enum" usage="input" required="false" default-value="Grid">
      <value name="Grid" display-name-key="ViewMode_Grid" />
      <value name="Card" display-name-key="ViewMode_Card" />
      <value name="List" display-name-key="ViewMode_List" />
    </property>

    <property name="enableVirtualization" display-name-key="EnableVirtualization_Display_Key" description-key="EnableVirtualization_Desc_Key" of-type="TwoOptions" usage="input" required="false" default-value="true">
      <value name="true" display-name-key="Enabled" />
      <value name="false" display-name-key="Disabled" />
    </property>

    <property name="rowHeight" display-name-key="RowHeight_Display_Key" description-key="RowHeight_Desc_Key" of-type="Whole.None" usage="input" required="false" default-value="48" />

    <!-- ============================= -->
    <!-- COMMAND CONFIGURATION         -->
    <!-- ============================= -->
    <property name="enabledCommands" display-name-key="EnabledCommands_Display_Key" description-key="EnabledCommands_Desc_Key" of-type="SingleLine.Text" usage="input" required="false" default-value="open,create,delete,refresh" />

    <property name="commandConfig" display-name-key="CommandConfig_Display_Key" description-key="CommandConfig_Desc_Key" of-type="Multiple" usage="input" required="false" />

    <property name="showToolbar" display-name-key="ShowToolbar_Display_Key" description-key="ShowToolbar_Desc_Key" of-type="TwoOptions" usage="input" required="false" default-value="true">
      <value name="true" display-name-key="Enabled" />
      <value name="false" display-name-key="Disabled" />
    </property>

    <!-- ============================= -->
    <!-- THEME CONFIGURATION           -->
    <!-- ============================= -->
    <property name="theme" display-name-key="Theme_Display_Key" description-key="Theme_Desc_Key" of-type="Enum" usage="input" required="false" default-value="Auto">
      <value name="Auto" display-name-key="Theme_Auto" />
      <value name="Spaarke" display-name-key="Theme_Spaarke" />
      <value name="Host" display-name-key="Theme_Host" />
    </property>

    <!-- ============================= -->
    <!-- HEADLESS MODE CONFIGURATION   -->
    <!-- ============================= -->
    <property name="headlessMode" display-name-key="HeadlessMode_Display_Key" description-key="HeadlessMode_Desc_Key" of-type="TwoOptions" usage="input" required="false" default-value="false">
      <value name="true" display-name-key="Enabled" />
      <value name="false" display-name-key="Disabled" />
    </property>

    <property name="headlessEntityName" display-name-key="HeadlessEntityName_Display_Key" description-key="HeadlessEntityName_Desc_Key" of-type="SingleLine.Text" usage="input" required="false" />

    <property name="headlessFetchXml" display-name-key="HeadlessFetchXml_Display_Key" description-key="HeadlessFetchXml_Desc_Key" of-type="Multiple" usage="input" required="false" />

    <property name="headlessPageSize" display-name-key="HeadlessPageSize_Display_Key" description-key="HeadlessPageSize_Desc_Key" of-type="Whole.None" usage="input" required="false" default-value="25" />

    <!-- ============================= -->
    <!-- SELECTION CONFIGURATION       -->
    <!-- ============================= -->
    <property name="selectionMode" display-name-key="SelectionMode_Display_Key" description-key="SelectionMode_Desc_Key" of-type="Enum" usage="input" required="false" default-value="Multiple">
      <value name="None" display-name-key="SelectionMode_None" />
      <value name="Single" display-name-key="SelectionMode_Single" />
      <value name="Multiple" display-name-key="SelectionMode_Multiple" />
    </property>

    <!-- ============================= -->
    <!-- OUTPUT PROPERTIES             -->
    <!-- ============================= -->
    <property name="selectedRecordIds" display-name-key="SelectedRecordIds_Display_Key" description-key="SelectedRecordIds_Desc_Key" of-type="SingleLine.Text" usage="output" />

    <property name="lastAction" display-name-key="LastAction_Display_Key" description-key="LastAction_Desc_Key" of-type="SingleLine.Text" usage="output" />

    <!-- ============================= -->
    <!-- RESOURCES                     -->
    <!-- ============================= -->
    <resources>
      <code path="index.ts" order="1" />

      <!-- Localization -->
      <resx path="strings/UniversalDataset.1033.resx" version="1.0.0" />

      <!-- External Libraries -->
      <library name="React" version="18.2.0" order="2" />
      <library name="ReactDOM" version="18.2.0" order="3" />
      <library name="FluentUIReactComponents" version="9.46.2" order="4" />
    </resources>

    <!-- ============================= -->
    <!-- FEATURE USAGE                 -->
    <!-- ============================= -->
    <feature-usage>
      <uses-feature name="Device.captureAudio" required="false" />
      <uses-feature name="Device.captureImage" required="false" />
      <uses-feature name="Device.captureVideo" required="false" />
      <uses-feature name="Device.getBarcodeValue" required="false" />
      <uses-feature name="Device.getCurrentPosition" required="false" />
      <uses-feature name="Device.pickFile" required="true" />
      <uses-feature name="Utility" required="true" />
      <uses-feature name="WebAPI" required="true" />
    </feature-usage>

  </control>
</manifest>
```

---

## Step 3: Create Localization File

**Create directory:**
```bash
mkdir strings
```

**Create `strings\UniversalDataset.1033.resx`:**

```xml
<?xml version="1.0" encoding="utf-8"?>
<root>
  <xsd:schema id="root" xmlns="" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:msdata="urn:schemas-microsoft-com:xml-msdata">
    <xsd:import namespace="http://www.w3.org/XML/1998/namespace" />
    <xsd:element name="root" msdata:IsDataSet="true">
      <xsd:complexType>
        <xsd:choice maxOccurs="unbounded">
          <xsd:element name="data">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" msdata:Ordinal="1" />
                <xsd:element name="comment" type="xsd:string" minOccurs="0" msdata:Ordinal="2" />
              </xsd:sequence>
              <xsd:attribute name="name" type="xsd:string" use="required" />
            </xsd:complexType>
          </xsd:element>
        </xsd:choice>
      </xsd:complexType>
    </xsd:element>
  </xsd:schema>

  <!-- Control Display Names -->
  <data name="UniversalDataset_Display_Key" xml:space="preserve">
    <value>Universal Dataset Grid</value>
    <comment>Display name for the control</comment>
  </data>
  <data name="UniversalDataset_Desc_Key" xml:space="preserve">
    <value>Configurable dataset grid supporting grid, card, and list views with custom commands</value>
    <comment>Description of the control</comment>
  </data>

  <!-- Dataset -->
  <data name="Dataset_Display_Key" xml:space="preserve">
    <value>Dataset</value>
    <comment>Display name for dataset binding</comment>
  </data>
  <data name="Dataset_Desc_Key" xml:space="preserve">
    <value>Dataverse dataset to display</value>
    <comment>Description for dataset binding</comment>
  </data>

  <!-- View Mode -->
  <data name="ViewMode_Display_Key" xml:space="preserve">
    <value>View Mode</value>
  </data>
  <data name="ViewMode_Desc_Key" xml:space="preserve">
    <value>Display mode: Grid (table), Card (tile view), or List (compact list)</value>
  </data>
  <data name="ViewMode_Grid" xml:space="preserve">
    <value>Grid (Table)</value>
  </data>
  <data name="ViewMode_Card" xml:space="preserve">
    <value>Card (Tiles)</value>
  </data>
  <data name="ViewMode_List" xml:space="preserve">
    <value>List (Compact)</value>
  </data>

  <!-- Virtualization -->
  <data name="EnableVirtualization_Display_Key" xml:space="preserve">
    <value>Enable Virtualization</value>
  </data>
  <data name="EnableVirtualization_Desc_Key" xml:space="preserve">
    <value>Render only visible rows for performance (recommended for &gt;100 records)</value>
  </data>

  <!-- Row Height -->
  <data name="RowHeight_Display_Key" xml:space="preserve">
    <value>Row Height (px)</value>
  </data>
  <data name="RowHeight_Desc_Key" xml:space="preserve">
    <value>Height of each row in pixels (default: 48)</value>
  </data>

  <!-- Commands -->
  <data name="EnabledCommands_Display_Key" xml:space="preserve">
    <value>Enabled Commands</value>
  </data>
  <data name="EnabledCommands_Desc_Key" xml:space="preserve">
    <value>Comma-separated list of commands to show (e.g., open,create,delete,refresh,upload)</value>
  </data>
  <data name="CommandConfig_Display_Key" xml:space="preserve">
    <value>Custom Command Configuration (JSON)</value>
  </data>
  <data name="CommandConfig_Desc_Key" xml:space="preserve">
    <value>JSON configuration for custom commands (Custom API, Actions, Functions)</value>
  </data>
  <data name="ShowToolbar_Display_Key" xml:space="preserve">
    <value>Show Toolbar</value>
  </data>
  <data name="ShowToolbar_Desc_Key" xml:space="preserve">
    <value>Display command toolbar above grid</value>
  </data>

  <!-- Theme -->
  <data name="Theme_Display_Key" xml:space="preserve">
    <value>Theme</value>
  </data>
  <data name="Theme_Desc_Key" xml:space="preserve">
    <value>Color theme: Auto (detect host), Spaarke (brand theme), or Host (use Power Platform theme)</value>
  </data>
  <data name="Theme_Auto" xml:space="preserve">
    <value>Auto (Detect)</value>
  </data>
  <data name="Theme_Spaarke" xml:space="preserve">
    <value>Spaarke Brand</value>
  </data>
  <data name="Theme_Host" xml:space="preserve">
    <value>Host Theme</value>
  </data>

  <!-- Headless Mode -->
  <data name="HeadlessMode_Display_Key" xml:space="preserve">
    <value>Headless Mode</value>
  </data>
  <data name="HeadlessMode_Desc_Key" xml:space="preserve">
    <value>Fetch data via Web API instead of dataset binding (for custom pages without dataset)</value>
  </data>
  <data name="HeadlessEntityName_Display_Key" xml:space="preserve">
    <value>Headless Entity Name</value>
  </data>
  <data name="HeadlessEntityName_Desc_Key" xml:space="preserve">
    <value>Logical name of entity to query (e.g., sprk_document)</value>
  </data>
  <data name="HeadlessFetchXml_Display_Key" xml:space="preserve">
    <value>Headless FetchXML Query</value>
  </data>
  <data name="HeadlessFetchXml_Desc_Key" xml:space="preserve">
    <value>FetchXML query to execute (filters, sorting, joins)</value>
  </data>
  <data name="HeadlessPageSize_Display_Key" xml:space="preserve">
    <value>Headless Page Size</value>
  </data>
  <data name="HeadlessPageSize_Desc_Key" xml:space="preserve">
    <value>Number of records to fetch per page (default: 25)</value>
  </data>

  <!-- Selection -->
  <data name="SelectionMode_Display_Key" xml:space="preserve">
    <value>Selection Mode</value>
  </data>
  <data name="SelectionMode_Desc_Key" xml:space="preserve">
    <value>Row selection: None, Single, or Multiple</value>
  </data>
  <data name="SelectionMode_None" xml:space="preserve">
    <value>None (No Selection)</value>
  </data>
  <data name="SelectionMode_Single" xml:space="preserve">
    <value>Single Row</value>
  </data>
  <data name="SelectionMode_Multiple" xml:space="preserve">
    <value>Multiple Rows</value>
  </data>

  <!-- Output Properties -->
  <data name="SelectedRecordIds_Display_Key" xml:space="preserve">
    <value>Selected Record IDs</value>
  </data>
  <data name="SelectedRecordIds_Desc_Key" xml:space="preserve">
    <value>Comma-separated GUIDs of selected records (output property)</value>
  </data>
  <data name="LastAction_Display_Key" xml:space="preserve">
    <value>Last Action</value>
  </data>
  <data name="LastAction_Desc_Key" xml:space="preserve">
    <value>Last command executed (e.g., "delete", "upload") - output property</value>
  </data>

  <!-- Generic -->
  <data name="Enabled" xml:space="preserve">
    <value>Enabled</value>
  </data>
  <data name="Disabled" xml:space="preserve">
    <value>Disabled</value>
  </data>
</root>
```

---

## Step 4: Refresh PCF Types

```bash
# Regenerate TypeScript types from manifest
npm run refreshTypes

# Expected output: "Types refreshed successfully"
```

**This generates:**
- `generated/ManifestTypes.d.ts` - TypeScript interfaces for IInputs/IOutputs

---

## Step 5: Verify Generated Types

```bash
# View generated types
type generated\ManifestTypes.d.ts
```

**You should see:**
```typescript
export interface IInputs {
  datasetGrid: ComponentFramework.PropertyTypes.DataSet;
  viewMode: ComponentFramework.PropertyTypes.EnumProperty<"Grid" | "Card" | "List">;
  enableVirtualization: ComponentFramework.PropertyTypes.TwoOptionsProperty;
  rowHeight: ComponentFramework.PropertyTypes.WholeNumberProperty;
  enabledCommands: ComponentFramework.PropertyTypes.StringProperty;
  commandConfig: ComponentFramework.PropertyTypes.StringProperty;
  showToolbar: ComponentFramework.PropertyTypes.TwoOptionsProperty;
  theme: ComponentFramework.PropertyTypes.EnumProperty<"Auto" | "Spaarke" | "Host">;
  headlessMode: ComponentFramework.PropertyTypes.TwoOptionsProperty;
  headlessEntityName: ComponentFramework.PropertyTypes.StringProperty;
  headlessFetchXml: ComponentFramework.PropertyTypes.StringProperty;
  headlessPageSize: ComponentFramework.PropertyTypes.WholeNumberProperty;
  selectionMode: ComponentFramework.PropertyTypes.EnumProperty<"None" | "Single" | "Multiple">;
}

export interface IOutputs {
  selectedRecordIds?: string;
  lastAction?: string;
}
```

---

## Step 6: Update index.ts to Use Typed Inputs

**Edit `index.ts` to demonstrate reading properties:**

```typescript
import { IInputs, IOutputs } from "./generated/ManifestTypes";
import * as React from "react";
import * as ReactDOM from "react-dom";

export class UniversalDataset implements ComponentFramework.StandardControl<IInputs, IOutputs> {
  private container: HTMLDivElement;
  private context: ComponentFramework.Context<IInputs>;
  private notifyOutputChanged: () => void;
  private selectedRecords: string[] = [];
  private lastAction: string = "";

  public init(
    context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    state: ComponentFramework.Dictionary,
    container: HTMLDivElement
  ): void {
    this.context = context;
    this.notifyOutputChanged = notifyOutputChanged;
    this.container = container;

    console.log("UniversalDataset: Initialized");
  }

  public updateView(context: ComponentFramework.Context<IInputs>): void {
    this.context = context;

    // Read configuration from manifest properties
    const viewMode = context.parameters.viewMode.raw || "Grid";
    const enableVirtualization = context.parameters.enableVirtualization.raw;
    const rowHeight = context.parameters.rowHeight.raw || 48;
    const enabledCommands = context.parameters.enabledCommands.raw || "open,create,delete,refresh";
    const showToolbar = context.parameters.showToolbar.raw;
    const theme = context.parameters.theme.raw || "Auto";
    const selectionMode = context.parameters.selectionMode.raw || "Multiple";

    // Headless mode
    const headlessMode = context.parameters.headlessMode.raw;
    const headlessEntityName = context.parameters.headlessEntityName.raw;
    const headlessFetchXml = context.parameters.headlessFetchXml.raw;
    const headlessPageSize = context.parameters.headlessPageSize.raw || 25;

    // Dataset
    const dataset = context.parameters.datasetGrid;
    const recordCount = dataset.sortedRecordIds?.length || 0;

    // Debug output
    const configInfo = React.createElement(
      "div",
      { style: { padding: "16px", fontFamily: "Segoe UI" } },
      React.createElement("h2", null, "Universal Dataset Control - Configuration"),
      React.createElement("p", null, `View Mode: ${viewMode}`),
      React.createElement("p", null, `Virtualization: ${enableVirtualization ? "Enabled" : "Disabled"}`),
      React.createElement("p", null, `Row Height: ${rowHeight}px`),
      React.createElement("p", null, `Commands: ${enabledCommands}`),
      React.createElement("p", null, `Toolbar: ${showToolbar ? "Visible" : "Hidden"}`),
      React.createElement("p", null, `Theme: ${theme}`),
      React.createElement("p", null, `Selection: ${selectionMode}`),
      React.createElement("p", null, `Headless Mode: ${headlessMode ? "Enabled" : "Disabled"}`),
      React.createElement("p", null, `Records: ${recordCount}`),
      React.createElement("p", { style: { marginTop: "16px", fontStyle: "italic" } }, "Ready for Phase 2 implementation")
    );

    ReactDOM.render(configInfo, this.container);
  }

  public getOutputs(): IOutputs {
    return {
      selectedRecordIds: this.selectedRecords.join(","),
      lastAction: this.lastAction
    };
  }

  public destroy(): void {
    ReactDOM.unmountComponentAtNode(this.container);
  }
}
```

---

## Step 7: Build and Test

```bash
# Build PCF control
npm run build

# Expected output: "Build succeeded"
```

---

## Step 8: Start Test Harness

```bash
# Start PCF test harness
npm start

# Expected output:
# [pcf-start] Starting local server...
# [pcf-start] Server started at http://localhost:8181
```

**Open browser:** Navigate to `http://localhost:8181`

**Expected Result:**
- Control loads
- Shows configuration values (View Mode: Grid, Commands: open,create,delete,refresh, etc.)
- No errors in browser console

**Stop test harness:** Press `Ctrl+C` when done.

---

## Validation Checklist

Execute these commands to verify task completion:

```bash
# 1. Verify manifest exists
cd c:\code_files\spaarke\power-platform\pcf\UniversalDataset
dir ControlManifest.Input.xml
# Should exist

# 2. Verify localization file exists
dir strings\UniversalDataset.1033.resx
# Should exist

# 3. Verify types generated
dir generated\ManifestTypes.d.ts
# Should exist

# 4. Verify manifest has all properties
type ControlManifest.Input.xml | findstr "viewMode"
type ControlManifest.Input.xml | findstr "enabledCommands"
type ControlManifest.Input.xml | findstr "headlessMode"
type ControlManifest.Input.xml | findstr "selectedRecordIds"
# All should match

# 5. Verify build succeeds
npm run build
# Should succeed

# 6. Verify test harness runs
npm start
# Should start server at http://localhost:8181
# Press Ctrl+C to stop
```

---

## Success Criteria

- ✅ Manifest defines dataset binding (`datasetGrid`)
- ✅ Manifest has 13 input properties (viewMode, commands, theme, headless, etc.)
- ✅ Manifest has 2 output properties (selectedRecordIds, lastAction)
- ✅ Localization file exists with all string keys
- ✅ TypeScript types generated (`ManifestTypes.d.ts`)
- ✅ Build succeeds with no errors
- ✅ Test harness loads control and shows configuration
- ✅ No Fluent UI v8 dependencies declared

---

## Deliverables

**Files Created:**
1. `ControlManifest.Input.xml` (complete manifest)
2. `strings/UniversalDataset.1033.resx` (localization)
3. `generated/ManifestTypes.d.ts` (auto-generated types)

**Files Updated:**
1. `index.ts` (reads manifest properties)

---

## Common Issues & Solutions

**Issue:** `npm run refreshTypes` fails with "Manifest not found"
**Solution:** Ensure `ControlManifest.Input.xml` is in project root (same directory as `package.json`)

**Issue:** Build fails with "Invalid manifest schema"
**Solution:** Validate XML syntax - ensure all `<property>` tags have matching closing tags

**Issue:** Test harness shows "Cannot read property 'raw' of undefined"
**Solution:** Property may be undefined if not set - use default values: `context.parameters.viewMode?.raw || "Grid"`

**Issue:** Localization strings not appearing
**Solution:** Verify `.resx` file path in manifest matches actual file location

**Issue:** Generated types missing properties
**Solution:** Run `npm run refreshTypes` after any manifest changes

---

## Property Reference

### Input Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `viewMode` | Enum | Grid | Display mode (Grid/Card/List) |
| `enableVirtualization` | Boolean | true | Enable react-window virtualization |
| `rowHeight` | Number | 48 | Row height in pixels |
| `enabledCommands` | String | open,create,delete,refresh | Comma-separated command list |
| `commandConfig` | String (JSON) | null | Custom command configuration |
| `showToolbar` | Boolean | true | Show command toolbar |
| `theme` | Enum | Auto | Theme selection |
| `headlessMode` | Boolean | false | Enable headless data fetching |
| `headlessEntityName` | String | null | Entity logical name for headless |
| `headlessFetchXml` | String (XML) | null | FetchXML query for headless |
| `headlessPageSize` | Number | 25 | Page size for headless |
| `selectionMode` | Enum | Multiple | Row selection mode |

### Output Properties

| Property | Type | Description |
|----------|------|-------------|
| `selectedRecordIds` | String | Comma-separated GUIDs |
| `lastAction` | String | Last executed command key |

---

## Next Steps

After completing this task:
1. Proceed to [TASK-2.1-CORE-COMPONENT-STRUCTURE.md](./TASK-2.1-CORE-COMPONENT-STRUCTURE.md)
2. Phase 1 (Project Scaffolding) is now COMPLETE
3. Phase 2 will build the core React components

---

**Task Status:** Ready for Execution
**Estimated Time:** 2 hours
**Actual Time:** _________ (fill in after completion)
**Completed By:** _________ (developer name)
**Date:** _________ (completion date)
