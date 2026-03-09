# Event Form Configuration Guide

> **Last Updated**: 2026-02-01
> **Project**: Events and Workflow Automation R1
> **Task**: 035 - Configure Event form with all controls

---

## Overview

This document describes the placement and configuration of PCF controls on the `sprk_event` form. All five PCF controls are now built and ready for binding:

| Control | Purpose | Placement |
|---------|---------|-----------|
| **AssociationResolver** | Entity type selection + record search | Event Main Form - Regarding Section |
| **EventFormController** | Event Type driven field visibility | Event Main Form - Header/Type Section |
| **RegardingLink** | Read-only hyperlink to parent record | All Events View (grid column) |
| **UpdateRelatedButton** | Push field values to child events | Parent Entity Forms (Matter, Project, etc.) |
| **FieldMappingAdmin** | Admin configuration of field mappings | Field Mapping Rule Form (admin-only) |

---

## Main Form Tab Layout

### Form: sprk_event Main Form

#### Header Section

| Control | Bound Field | Purpose |
|---------|-------------|---------|
| **Standard** | `sprk_eventname` | Primary name field (text input) |
| **Status Badge** | `statuscode` | Visual status indicator |

#### Tab: General

##### Section: Regarding Record (Top of Form)

This section enables users to select the parent record (Matter, Project, etc.) that this Event relates to.

| Control | Bound Field(s) | Purpose | Configuration |
|---------|----------------|---------|---------------|
| **AssociationResolver** | `sprk_regardingrecordtype` (primary) | Entity type selection + record search | See "AssociationResolver Configuration" below |

**Hidden Fields** (populated by AssociationResolver, not visible on form):
- `sprk_regardingrecordid` - GUID of selected record
- `sprk_regardingrecordname` - Display name of selected record
- `sprk_regardingmatter` - Lookup (populated when entity type = Matter)
- `sprk_regardingproject` - Lookup (populated when entity type = Project)
- `sprk_regardinginvoice` - Lookup (populated when entity type = Invoice)
- `sprk_regardinganalysis` - Lookup (populated when entity type = Analysis)
- `sprk_regardingaccount` - Lookup (populated when entity type = Account)
- `sprk_regardingcontact` - Lookup (populated when entity type = Contact)
- `sprk_regardingworkassignment` - Lookup (populated when entity type = Work Assignment)
- `sprk_regardingbudget` - Lookup (populated when entity type = Budget)

##### Section: Event Details

| Control | Bound Field | Purpose | Visibility |
|---------|-------------|---------|------------|
| **EventFormController** | `sprk_eventtype_ref` | Event Type selection + field visibility control | Always visible |
| Standard Text | `sprk_subject` | Event subject/title | Always visible, always required |
| Standard Memo | `sprk_description` | Event description | Always visible |
| Standard DateTime | `sprk_basedate` | Base date (event date) | Controlled by Event Type (`sprk_requiresbasedate`) |
| Standard DateTime | `sprk_duedate` | Due date | Controlled by Event Type (`sprk_requiresduedate`) |
| Standard DateTime | `sprk_scheduledstart` | Meeting start time | Shown for Meeting event types |
| Standard DateTime | `sprk_scheduledend` | Meeting end time | Shown for Meeting event types |
| Standard Text | `sprk_location` | Meeting/court location | Shown for Meeting, Court Date event types |

##### Section: Status & Priority

| Control | Bound Field | Purpose |
|---------|-------------|---------|
| Standard OptionSet | `statuscode` | Status Reason (Draft, Planned, Open, On Hold, Completed, Cancelled) |
| Standard OptionSet | `sprk_priority` | Priority (Low, Normal, High, Urgent) |
| Standard OptionSet | `sprk_source` | Source (User, System, Workflow, External) |

##### Section: Actions (Bottom of Form)

| Control | Purpose | Configuration |
|---------|---------|---------------|
| **Refresh from Parent** button | Re-apply field mappings from current parent record | Built into AssociationResolver |

#### Tab: Related

| Subgrid | Related Entity | Relationship |
|---------|----------------|--------------|
| Event Logs | `sprk_eventlog` | Events related to this Event |

---

## AssociationResolver Configuration

### Control Manifest Binding

```xml
<control>
  <name>Spaarke.AssociationResolver</name>
  <property name="regardingRecordType" type="OptionSet" of="sprk_regardingrecordtype"/>
  <property name="regardingRecordId" type="SingleLine.Text" of="sprk_regardingrecordid"/>
  <property name="regardingRecordName" type="SingleLine.Text" of="sprk_regardingrecordname"/>
  <!-- Output Properties (bound to hidden fields) -->
  <property name="regardingMatter" type="Lookup.Simple" of="sprk_regardingmatter"/>
  <property name="regardingProject" type="Lookup.Simple" of="sprk_regardingproject"/>
  <property name="regardingInvoice" type="Lookup.Simple" of="sprk_regardinginvoice"/>
  <property name="regardingAnalysis" type="Lookup.Simple" of="sprk_regardinganalysis"/>
  <property name="regardingAccount" type="Lookup.Simple" of="sprk_regardingaccount"/>
  <property name="regardingContact" type="Lookup.Simple" of="sprk_regardingcontact"/>
  <property name="regardingWorkAssignment" type="Lookup.Simple" of="sprk_regardingworkassignment"/>
  <property name="regardingBudget" type="Lookup.Simple" of="sprk_regardingbudget"/>
</control>
```

### Behavior

1. **Entity Type Dropdown**: User selects from 8 supported entity types
2. **Record Search**: Type-ahead search (min 3 characters) queries Dataverse
3. **Selection**: Populates all regarding fields + clears other entity lookups
4. **Field Mapping**: Automatically applies field mappings from parent after selection
5. **Refresh Button**: Shows "Refresh from Parent" button if sync mode allows manual refresh

### Entity Configuration

| Entity | Display Name | sprk_regardingrecordtype Value | Regarding Lookup Field |
|--------|--------------|-------------------------------|------------------------|
| `sprk_project` | Project | 0 | `sprk_regardingproject` |
| `sprk_matter` | Matter | 1 | `sprk_regardingmatter` |
| `sprk_invoice` | Invoice | 2 | `sprk_regardinginvoice` |
| `sprk_analysis` | Analysis | 3 | `sprk_regardinganalysis` |
| `account` | Account | 4 | `sprk_regardingaccount` |
| `contact` | Contact | 5 | `sprk_regardingcontact` |
| `sprk_workassignment` | Work Assignment | 6 | `sprk_regardingworkassignment` |
| `sprk_budget` | Budget | 7 | `sprk_regardingbudget` |

---

## EventFormController Configuration

### Control Manifest Binding

```xml
<control>
  <name>Spaarke.EventFormController</name>
  <property name="eventType" type="Lookup.Simple" of="sprk_eventtype_ref"/>
  <property name="baseDate" type="DateAndTime.DateOnly" of="sprk_basedate"/>
  <property name="dueDate" type="DateAndTime.DateOnly" of="sprk_duedate"/>
  <property name="controlMode" type="Enum" static="true">validation</property>
</control>
```

### Behavior

1. **Event Type Change**: Fetches Event Type record to read field requirements
2. **Field Visibility**: Shows/hides fields based on Event Type configuration:
   - If `sprk_requiresbasedate = Yes` → Show Base Date field
   - If `sprk_requiresduedate = Yes` → Show Due Date field
3. **Validation**: Validates required fields before form save
4. **No Business Rules**: All logic in TypeScript (per ADR constraint)

### Field Visibility Rules

| Event Type | Base Date | Due Date | Scheduled Start/End | Location |
|------------|-----------|----------|---------------------|----------|
| Meeting | Show | Optional | Show | Show |
| Deadline | Optional | Show | Hide | Hide |
| Reminder | Show | Optional | Hide | Hide |
| Court Date | Show | Show | Show | Show |
| Follow-up | Optional | Show | Hide | Hide |
| (Default) | Optional | Optional | Hide | Hide |

---

## RegardingLink Configuration (View Column)

### Purpose

Displays a clickable hyperlink to the regarding record in grid views (e.g., "All Events" view).

### Control Manifest Binding

```xml
<control>
  <name>Spaarke.RegardingLink</name>
  <property name="regardingRecordType" type="OptionSet" of="sprk_regardingrecordtype"/>
  <property name="regardingRecordId" type="SingleLine.Text" of="sprk_regardingrecordid"/>
  <property name="regardingRecordName" type="SingleLine.Text" of="sprk_regardingrecordname"/>
</control>
```

### Behavior

1. **Render**: Displays `sprk_regardingrecordname` as a Fluent UI Link
2. **Click**: Navigates to record using `Xrm.Navigation.navigateTo`
3. **Entity Type**: Uses `sprk_regardingrecordtype` to determine target entity

### View Configuration

**View: All Events**

| Column | Field | Width | Control |
|--------|-------|-------|---------|
| Name | `sprk_eventname` | 200px | Default |
| Regarding | `sprk_regardingrecordname` | 200px | **RegardingLink PCF** |
| Event Type | `sprk_eventtype_ref` | 150px | Default |
| Due Date | `sprk_duedate` | 100px | Default |
| Status | `statuscode` | 100px | Default |
| Priority | `sprk_priority` | 80px | Default |

---

## UpdateRelatedButton Configuration (Parent Forms)

### Purpose

Allows users to push field mappings from a parent record (e.g., Matter) to all related child Events.

### Control Manifest Binding

```xml
<control>
  <name>Spaarke.UpdateRelatedButton</name>
  <property name="entityLogicalName" type="SingleLine.Text" static="true">{Entity Logical Name}</property>
  <property name="recordId" type="SingleLine.Text" of="{primaryidfield}"/>
  <property name="buttonLabel" type="SingleLine.Text" static="true">Update Related Events</property>
  <property name="targetEntity" type="SingleLine.Text" static="true">sprk_event</property>
  <property name="apiBaseUrl" type="SingleLine.Text" static="true">/api/v1</property>
</control>
```

### Placement on Parent Forms

| Parent Entity | Form | Section | Static Properties |
|---------------|------|---------|-------------------|
| `sprk_matter` | Main Form | Actions Section | `entityLogicalName="sprk_matter"`, `targetEntity="sprk_event"` |
| `sprk_project` | Main Form | Actions Section | `entityLogicalName="sprk_project"`, `targetEntity="sprk_event"` |
| `account` | Main Form | Related Section | `entityLogicalName="account"`, `targetEntity="sprk_event"` |
| `contact` | Main Form | Related Section | `entityLogicalName="contact"`, `targetEntity="sprk_event"` |
| `sprk_invoice` | Main Form | Actions Section | `entityLogicalName="sprk_invoice"`, `targetEntity="sprk_event"` |
| `sprk_analysis` | Main Form | Actions Section | `entityLogicalName="sprk_analysis"`, `targetEntity="sprk_event"` |
| `sprk_workassignment` | Main Form | Actions Section | `entityLogicalName="sprk_workassignment"`, `targetEntity="sprk_event"` |
| `sprk_budget` | Main Form | Actions Section | `entityLogicalName="sprk_budget"`, `targetEntity="sprk_event"` |

### Behavior

1. **Load**: Checks if active Field Mapping Profiles exist for this entity
2. **Hidden**: If no profiles configured, button is hidden
3. **Click**: Shows confirmation dialog, then calls BFF API
4. **API Call**: `POST /api/v1/field-mappings/push`
5. **Result**: Shows toast with update count

---

## FieldMappingAdmin Configuration (Admin Form)

### Purpose

Validates field mapping rule configuration and ensures type compatibility.

### Form: sprk_fieldmappingrule Main Form

This control is placed on the Field Mapping Rule admin form, not the Event form.

### Control Manifest Binding

```xml
<control>
  <name>Spaarke.FieldMappingAdmin</name>
  <property name="sourceFieldType" type="OptionSet" of="sprk_sourcefieldtype"/>
  <property name="targetFieldType" type="OptionSet" of="sprk_targetfieldtype"/>
  <property name="compatibilityMode" type="OptionSet" of="sprk_compatibilitymode"/>
</control>
```

### Form Layout

| Section | Fields | Purpose |
|---------|--------|---------|
| Rule Definition | Source Field, Source Field Type | Define source field |
| Rule Definition | Target Field, Target Field Type | Define target field |
| Validation | **FieldMappingAdmin PCF** | Real-time compatibility validation |
| Options | Compatibility Mode, Is Required, Execution Order | Rule options |

### Behavior

1. **Load**: Validates current source/target type compatibility
2. **Change**: Re-validates on field type change
3. **Incompatible (Strict)**: Shows warning, blocks save
4. **Indicator**: Shows compatibility status:
   - `Compatible` - Types can be mapped
   - `Requires Resolve Mode` - Would need type resolution (future)
   - `Incompatible` - Cannot be mapped

---

## Form Designer Steps (Manual Configuration)

### Step 1: Open Event Form

1. Navigate to Power Apps Maker Portal
2. Go to Tables > Event (`sprk_event`)
3. Open Main Form in form designer

### Step 2: Configure Regarding Record Section

1. Add new section at top of General tab: "Regarding Record"
2. Add `sprk_regardingrecordtype` field to section
3. Select field, open Properties panel
4. In Components section, select "AssociationResolver" PCF
5. Configure output properties to bind to hidden lookup fields

### Step 3: Configure Event Details Section

1. Add `sprk_eventtype_ref` field to Event Details section
2. Select field, open Properties panel
3. In Components section, select "EventFormController" PCF
4. Configure visibility rules for date fields

### Step 4: Configure All Events View

1. Navigate to Views > All Events
2. Add `sprk_regardingrecordname` column
3. Select column, open Properties panel
4. In Components section, select "RegardingLink" PCF

### Step 5: Save and Publish

1. Save form changes
2. Publish customizations
3. Test in model-driven app

---

## Testing Checklist

### Event Form Tests

- [ ] Create new Event - AssociationResolver appears in Regarding section
- [ ] Select entity type - Dropdown shows all 8 entity types
- [ ] Search records - Type-ahead returns matching records
- [ ] Select record - All regarding fields populated, others cleared
- [ ] Field mapping - Mapped fields auto-populated from parent
- [ ] Change Event Type - Field visibility updates correctly
- [ ] Required validation - Missing required fields block save
- [ ] Refresh from Parent - Re-applies mappings, updates fields

### View Tests

- [ ] All Events view - RegardingLink column shows record names
- [ ] Click link - Navigates to correct parent record
- [ ] Different entity types - Links work for all 8 entity types

### Parent Form Tests (UpdateRelatedButton)

- [ ] Button visible - Shows on parent forms with active profiles
- [ ] Button hidden - Hidden when no profiles configured
- [ ] Click button - Confirmation dialog appears
- [ ] Confirm push - API called, all child Events updated
- [ ] Result toast - Shows correct update count

### Admin Form Tests (FieldMappingAdmin)

- [ ] Compatible types - Shows green checkmark
- [ ] Incompatible types - Shows error, blocks save
- [ ] Change type - Validation updates in real-time

---

## Related Documentation

- [spec.md](../spec.md) - Full specification with PCF control details
- [plan.md](../plan.md) - Implementation plan and phases
- [PCF-V9-PACKAGING.md](../../../docs/guides/PCF-V9-PACKAGING.md) - PCF deployment guide
- [dataverse-deploy skill](../../../.claude/skills/dataverse-deploy/SKILL.md) - Deployment procedures

---

*This configuration guide documents the final Event form layout for the Events and Workflow Automation R1 project.*
