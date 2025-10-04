# Dataset Component Manifest Configuration

## Complete Manifest Structure
```xml
<?xml version="1.0" encoding="utf-8"?>
<manifest>
  <control namespace="Spaarke"
           constructor="UniversalDataset"
           version="1.0.0"
           display-name-key="Universal_Dataset_Display"
           description-key="Universal_Dataset_Description"
           control-type="dataset">
    <!-- Primary Dataset Binding -->
    <data-set name="dataset"
              display-name-key="Dataset"
              cds-data-set-options="DisplayCommandBar:true;DisplayViewSelector:false">
    </data-set>

    <!-- Mode Configuration -->
    <property name="componentMode"
              display-name-key="Component_Mode"
              of-type="Enum"
              usage="input"
              required="false"
              default-value="Auto">
      <value name="Auto">Auto Detect</value>
      <value name="Dataset">Dataset Bound</value>
      <value name="Headless">Headless</value>
    </property>

    <!-- Entity Configuration -->
    <property name="entityName"
              display-name-key="Entity_Name"
              of-type="SingleLine.Text"
              usage="input"
              required="false"
              description-key="Override_auto_detected_entity"/>

    <property name="configKey"
              display-name-key="Configuration_Key"
              of-type="SingleLine.Text"
              usage="input"
              required="false"
              description-key="Reference_to_stored_configuration"/>

    <!-- Data Access (Headless Mode) -->
    <property name="tableName"
              display-name-key="Table_Name"
              of-type="SingleLine.Text"
              usage="input"
              required="false"/>
    <property name="viewId"
              display-name-key="View_ID"
              of-type="SingleLine.Text"
              usage="input"
              required="false"/>
    <property name="fetchXml"
              display-name-key="Fetch_XML"
              of-type="Multiple"
              usage="input"
              required="false"/>
    <property name="parentTable"
              display-name-key="Parent_Table"
              of-type="SingleLine.Text"
              usage="input"
              required="false"/>
    <property name="parentRecordId"
              display-name-key="Parent_Record_ID"
              of-type="SingleLine.Text"
              usage="input"
              required="false"/>
    <property name="relationshipName"
              display-name-key="Relationship_Name"
              of-type="SingleLine.Text"
              usage="input"
              required="false"/>

    <!-- Rendering Configuration -->
    <property name="viewMode"
              display-name-key="View_Mode"
              of-type="Enum"
              usage="input"
              required="false"
              default-value="Grid">
      <value name="Grid">Grid</value>
      <value name="Card">Card</value>
      <value name="List">List</value>
      <value name="Kanban">Kanban</value>
      <value name="Gallery">Gallery</value>
    </property>

    <property name="columnBehavior"
              display-name-key="Column_Behavior"
              of-type="Multiple"
              usage="input"
              required="false"
              description-key="JSON_column_rendering_overrides"/>

    <property name="density"
              display-name-key="Density"
              of-type="Enum"
              usage="input"
              required="false"
              default-value="Standard">
      <value name="Compact">Compact</value>
      <value name="Standard">Standard</value>
      <value name="Comfortable">Comfortable</value>
    </property>

    <property name="groupBy"
              display-name-key="Group_By"
              of-type="SingleLine.Text"
              usage="input"
              required="false"/>

    <!-- Command Configuration -->
    <property name="enabledCommands"
              display-name-key="Enabled_Commands"
              of-type="Multiple"
              usage="input"
              required="false"
              default-value="open,create,delete,refresh"/>
    <property name="commandConfig"
              display-name-key="Command_Configuration"
              of-type="Multiple"
              usage="input"
              required="false"
              description-key="JSON_command_definitions"/>
    <property name="primaryAction"
              display-name-key="Primary_Action"
              of-type="Enum"
              usage="input"
              required="false"
              default-value="Open">
      <value name="Open">Open Record</value>
      <value name="Select">Select</value>
      <value name="QuickView">Quick View</value>
      <value name="None">None</value>
    </property>

    <!-- Display Options -->
    <property name="showToolbar"
              display-name-key="Show_Toolbar"
              of-type="TwoOptions"
              usage="input"
              required="false"
              default-value="true"/>
    <property name="showSearch"
              display-name-key="Show_Search"
              of-type="TwoOptions"
              usage="input"
              required="false"
              default-value="true"/>
    <property name="showPaging"
              display-name-key="Show_Paging"
              of-type="TwoOptions"
              usage="input"
              required="false"
              default-value="true"/>
    <property name="pageSize"
              display-name-key="Page_Size"
              of-type="Whole.None"
              usage="input"
              required="false"
              default-value="25"/>
    <property name="emptyStateText"
              display-name-key="Empty_State_Text"
              of-type="SingleLine.Text"
              usage="input"
              required="false"
              default-value="No records found"/>

    <!-- Appearance -->
    <property name="title"
              display-name-key="Title"
              of-type="SingleLine.Text"
              usage="input"
              required="false"/>
    <property name="accentColor"
              display-name-key="Accent_Color"
              of-type="SingleLine.Text"
              usage="input"
              required="false"/>

    <!-- Output Properties -->
    <property name="selectedRecordIds"
              display-name-key="Selected_Records"
              of-type="Multiple"
              usage="output"/>
    <property name="totalRecordCount"
              display-name-key="Total_Count"
              of-type="Whole.None"
              usage="output"/>
    <property name="lastAction"
              display-name-key="Last_Action"
              of-type="SingleLine.Text"
              usage="output"/>

    <!-- Resources -->
    <resources>
      <code path="index.ts" order="1"/>
      <platform-library name="React" version="18.2.0" />
      <platform-library name="Fluent" version="9.46.2" />
      <css path="css/UniversalDataset.css" order="2"/>
      <resx path="strings/UniversalDataset.1033.resx" version="1.0.0"/>
    </resources>

    <!-- Feature Usage -->
    <feature-usage>
      <uses-feature name="WebAPI" required="true"/>
      <uses-feature name="Navigation" required="true"/>
      <uses-feature name="Device.pickFile" required="false"/>
    </feature-usage>
  </control>
</manifest>
```

## Property Usage Guide
### Entity Detection Priority
1. Explicit `entityName` property (highest)
2. Dataset binding entity type
3. Parent form context
4. `tableName` property (headless mode)

### Configuration Resolution Order
1. Inline properties (highest priority)
2. Config entity via `configKey`
3. Environment variables
4. Entity metadata defaults
5. Component defaults (lowest)

## AI Coding Prompt
Produce `ControlManifest.Input.xml` that matches this spec:
- Control type `dataset`, primary dataset named `dataset` with command bar hidden by default.
- Inputs: `componentMode`, `entityName`, `configKey`, `tableName`, `viewId`, `fetchXml`, `parentTable`, `parentRecordId`, `relationshipName`, `viewMode`, `columnBehavior`, `density`, `groupBy`, `enabledCommands`, `commandConfig`, `primaryAction`, `showToolbar`, `showSearch`, `showPaging`, `pageSize`, `emptyStateText`, `title`, `accentColor`.
- Outputs: `selectedRecordIds`, `totalRecordCount`, `lastAction`.
- Resources: React 18 + Fluent v9 (no v8), CSS placeholder, resx strings.
- Feature usage: WebAPI, Navigation, optional Device.pickFile.
Pin versions, add localized display/description keys, and include comments explaining usage and default precedence rules.
