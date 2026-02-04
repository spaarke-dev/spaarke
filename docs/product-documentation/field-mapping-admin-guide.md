# Events and Workflow Automation - Field Mapping Administrator Guide

## Table of Contents
1. [Introduction](#introduction)
2. [Overview of Field Mapping Framework](#overview)
3. [Creating a Field Mapping Profile](#creating-profile)
4. [Adding Field Mapping Rules](#adding-rules)
5. [Understanding Type Compatibility](#type-compatibility)
6. [Sync Modes Explained](#sync-modes)
7. [Using Update Related](#update-related)
8. [Type Compatibility Reference](#reference-table)
9. [Troubleshooting](#troubleshooting)
10. [Best Practices](#best-practices)

---

## Introduction {#introduction}

This guide explains how to configure the **Field Mapping Framework** in the Events and Workflow Automation system. The Field Mapping Framework allows administrators to automatically populate fields on Event records based on values from parent records (such as Matters, Projects, Invoices, etc.).

### Who Should Read This Guide?

This guide is for **Spaarke administrators** who configure the system, including:
- System administrators
- Business analysts responsible for data quality
- Power Users who support the user community

### What Is Field Mapping?

Field Mapping is an automated process that copies field values from a parent record (like a Matter) to a child record (like an Event). For example, you might want to automatically copy the "Client" field from a Matter to all Events created for that Matter.

**Key Benefits:**
- **Data Consistency**: Ensures child records inherit correct values from parent records
- **User Efficiency**: Users don't need to manually enter data that already exists elsewhere
- **Accuracy**: Reduces data entry errors by automating field population
- **Flexibility**: Administrators can configure mappings without requesting developer assistance

---

## Overview of Field Mapping Framework {#overview}

The Field Mapping Framework consists of three main components:

### 1. Field Mapping Profiles
A **Profile** defines a relationship between two entity types and specifies how fields should be mapped between them.

**Example Profile:** "Matter to Event"
- Maps fields FROM a Matter record
- Maps fields TO an Event record
- Specifies when mapping occurs (at creation, on refresh, or push)

### 2. Field Mapping Rules
**Rules** are the individual field-to-field mappings within a profile. Each rule maps one field from the source entity to one field on the target entity.

**Example Rules within the "Matter to Event" profile:**
- Map Matter "Client" → Event "Account" field
- Map Matter "Base Date" → Event "Base Date" field
- Map Matter "Priority" → Event "Priority" field

### 3. Sync Modes
**Sync Mode** determines *when* the mapping is applied:
- **One-time**: When the Event is first created from a Matter
- **Manual Refresh**: When the user clicks "Refresh from Parent" button on the Event form
- **Update Related**: When an administrator clicks "Update Related" on the Matter form to push current values to all related Events

---

## Creating a Field Mapping Profile {#creating-profile}

Follow these steps to create a new Field Mapping Profile:

### Step 1: Navigate to Field Mapping Profiles

1. In the Spaarke Model-Driven App, go to **Settings > Administration**
2. Select **Field Mapping Profiles**
3. Click **+ New**

### Step 2: Fill in Profile Details

| Field | Description | Example |
|-------|-----------|---------|
| **Profile Name** | Display name for this profile | "Matter to Event" |
| **Source Entity** | The parent entity (where fields are copied FROM) | Matter |
| **Target Entity** | The child entity (where fields are copied TO) | Event |
| **Mapping Direction** | One-way or bidirectional mapping | Parent to Child |
| **Sync Mode** | When mappings apply | One-time |
| **Is Active** | Enable/disable this profile | Yes |
| **Description** | Notes about this profile | "Copies key Matter fields to new Events" |

### Step 3: Save the Profile

Click **Save** to create the profile. You will now add field mapping rules to this profile.

### Example: Creating a "Matter to Event" Profile

```
Profile Name:       Matter to Event
Source Entity:      Matter (sprk_matter)
Target Entity:      Event (sprk_event)
Mapping Direction:  Parent to Child
Sync Mode:          One-time
Is Active:          Yes
Description:        Automatically copies Matter information to Events created for that Matter
```

---

## Adding Field Mapping Rules {#adding-rules}

After creating a profile, you add individual field mapping rules. Rules define which fields map to which fields.

### Step 1: Open the Field Mapping Profile

1. Navigate to **Settings > Administration > Field Mapping Profiles**
2. Select the profile you created (e.g., "Matter to Event")
3. In the **Field Mapping Rules** section, click **+ New Field Mapping Rule**

### Step 2: Configure the Rule

| Field | Description | How to Complete |
|-------|-----------|-----------------|
| **Rule Name** | Descriptive name for this rule | "Copy Matter Client to Event Account" |
| **Source Field** | Field to copy FROM (on Matter) | Select the Matter field logical name |
| **Source Field Type** | Data type of source field | System will auto-detect (e.g., Lookup) |
| **Target Field** | Field to copy TO (on Event) | Select the Event field logical name |
| **Target Field Type** | Data type of target field | System will auto-detect and validate compatibility |
| **Compatibility Mode** | Validation level | Use "Strict" for R1 |
| **Is Required** | Fail if source is empty? | Set to Yes if mapping is mandatory |
| **Default Value** | Value to use if source is empty | Leave blank for optional, or enter a default |
| **Execution Order** | Sequence for dependent mappings | Usually 1 for first rule |
| **Is Active** | Enable/disable this rule | Yes |

### Step 3: Verify Compatibility

When you select the source and target fields, the system automatically validates that they are compatible types. If you see a **compatibility error**, refer to the [Type Compatibility Reference](#reference-table) section below.

### Step 4: Save the Rule

Click **Save** to add the rule to the profile.

### Example: Adding Rules to the "Matter to Event" Profile

**Rule 1: Copy Client**
```
Rule Name:          Copy Matter Client to Event Account
Source Field:       Account (Lookup on Matter)
Target Field:       Account (Lookup on Event)
Compatibility Mode: Strict
Is Required:        Yes
Execution Order:    1
Is Active:          Yes
```

**Rule 2: Copy Matter Name**
```
Rule Name:          Copy Matter Name to Event Description
Source Field:       Matter Name (Text on Matter)
Target Field:       Description (Memo on Event)
Compatibility Mode: Strict
Is Required:        No
Default Value:      (leave blank)
Execution Order:    2
Is Active:          Yes
```

**Rule 3: Copy Priority**
```
Rule Name:          Copy Priority from Matter to Event
Source Field:       Priority (OptionSet on Matter)
Target Field:       Priority (OptionSet on Event)
Compatibility Mode: Strict
Is Required:        No
Execution Order:    3
Is Active:          Yes
```

---

## Understanding Type Compatibility {#type-compatibility}

Not all field types can be mapped to each other. The system uses **Strict Mode** compatibility checking, which means:

- Only certain source-to-target field type combinations are allowed
- If you try to map incompatible types, the system will show an error
- You must choose compatible fields or create a workaround

### Why Compatibility Matters

**Example of Compatible Mapping:**
- Copying a Text field to another Text field ✅ Works

**Example of Incompatible Mapping:**
- Copying a Lookup field directly to a Text field ❌ Would lose the record reference
  - *Workaround*: Use a Text field that contains the record name instead

### Compatibility Rules in Strict Mode

| Source Field Type | Can Map To | Examples | Cannot Map To |
|------------------|-----------|----------|--------------|
| **Lookup** | Lookup (same entity), Text | Matter.Client → Event.Account | OptionSet, Number |
| **Text** | Text, Memo | Matter.Description → Event.Description | Lookup, OptionSet |
| **Memo** | Text, Memo | Matter.Notes → Event.Notes | Lookup, OptionSet |
| **OptionSet** | OptionSet (same options), Text | Matter.Status → Event.Status | Lookup, Number |
| **Number** | Number, Text | Matter.Budget → Event.Amount | Lookup, OptionSet |
| **DateTime** | DateTime, Text | Matter.CreatedDate → Event.Date | Lookup, OptionSet |
| **Boolean** | Boolean, Text | Matter.IsActive → Event.IsActive | Lookup, Number |

### When You See a Compatibility Error

If you try to save a rule with incompatible types, you will see an error message:

> **"Type Compatibility Error: Source field type [Type A] cannot be mapped to target field type [Type B]"**

**What to Do:**

1. **Check the compatibility table above** to understand why they're incompatible
2. **Choose a different target field** with a compatible type
3. **Use Text as a fallback** if the target is a lookup:
   - Text fields can receive values from most source types
   - The value will be the record name or formatted value
   - You will lose the ability to use the field in relationships or filters

---

## Sync Modes Explained {#sync-modes}

Sync Mode determines **when** field mappings are applied to Event records.

### Sync Mode 1: One-Time (At Creation)

**When It Applies:** When a new Event is created and associated with a parent record (e.g., Matter)

**Behavior:**
- Mapping rules run automatically at the moment of Event creation
- Fields are populated from the parent record's current values
- If parent fields change later, the Event fields are NOT automatically updated
- Users would need to use "Refresh from Parent" to get updated values

**Use Case:**
- You want Events to capture a "snapshot" of parent record values at creation time
- Data is unlikely to change frequently on the parent record

**Example Scenario:**
1. Matter "ABC Corp vs XYZ Inc" has Priority = "High"
2. User creates a new Event for this Matter
3. The Event automatically gets Priority = "High" (from mapping)
4. Later, the Matter Priority changes to "Normal"
5. The Event still shows Priority = "High" (one-time mapping does not update)
6. User can click "Refresh from Parent" to sync to the new "Normal" priority

### Sync Mode 2: Manual Refresh (Pull from Parent)

**When It Applies:** When a user clicks "Refresh from Parent" button on an Event form

**Behavior:**
- User manually triggers a sync operation
- All active mapping rules are re-applied
- Current parent values are fetched and copied to the Event
- User decides when to refresh (pull) updated values from the parent

**Use Case:**
- Parent fields change frequently
- Users want flexibility to update Events manually when parent data changes
- You want to avoid automatic updates that might overwrite user-entered data

**Example Scenario:**
1. Event has Priority = "High" (from initial mapping)
2. Matter Priority is updated to "Urgent"
3. User opens the Event form
4. User clicks the "Refresh from Parent" button
5. The Event is updated with Priority = "Urgent"
6. Other fields updated by mappings are also refreshed

### Sync Mode 3: Update Related (Push from Parent)

**When It Applies:** When an administrator clicks "Update Related" button on a parent record form (e.g., Matter form)

**Behavior:**
- Administrator initiates the sync from the parent record
- All child records (Events) linked to this parent are identified
- Mapping rules are applied to ALL matching children at once
- All children receive the updated values from the current parent

**Use Case:**
- Parent field changed and you need to immediately push that change to all Events
- You want a "bulk update" operation for field changes
- Critical data (like Status or Priority) needs consistency across all related Events

**Example Scenario:**
1. Matter "ABC Corp vs XYZ Inc" is updated with Priority = "Urgent"
2. There are 10 Events already created for this Matter
3. Administrator (or Power User with permissions) clicks "Update Related" on the Matter form
4. The system finds all 10 Events for this Matter
5. All 10 Events are updated with Priority = "Urgent"
6. Users see the updated priority when they open their Events

---

## Using Update Related {#update-related}

The "Update Related" feature is a button on parent record forms (like Matter) that allows you to push field updates to all related Events.

### Availability

The "Update Related" button appears on:
- Matter form
- Project form
- Invoice form
- Any other entity with a Field Mapping Profile configured

### How to Use Update Related

1. **Open the parent record form** (e.g., a Matter)
2. **Update the fields** you want to push (e.g., change Priority to "Urgent")
3. **Save the record** to ensure the new values are stored
4. **Click the "Update Related" button** in the ribbon/command bar
5. **Confirm the operation** when prompted

### What Happens Next

- The system identifies all child records (Events) mapped to this parent
- A background process applies all active mapping rules to each child record
- Each field is updated according to its rule, respecting:
  - Whether the rule is marked "Is Active"
  - Type compatibility constraints
  - Any conditional logic (required fields, defaults)
- A summary is shown with the number of records updated

### Example: Pushing a Priority Change

**Scenario:**
A Matter's priority level changed, and you need this reflected in all related Events immediately.

**Steps:**
1. Open the Matter record
2. Change the "Priority" field from "High" to "Urgent"
3. Click **Save**
4. Click **Update Related** button
5. System updates all 15 Events created for this Matter with Priority = "Urgent"
6. All users see the updated priority the next time they open their Events

---

## Type Compatibility Reference {#reference-table}

This section provides a complete reference for which field types can be mapped to each other in Strict Mode.

### Full Compatibility Matrix

| Source Type | Lookup | Text | Memo | OptionSet | Number | DateTime | Boolean |
|-------------|--------|------|------|-----------|--------|----------|---------|
| **Lookup** | ✅ | ✅ | — | ❌ | ❌ | ❌ | ❌ |
| **Text** | ❌ | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| **Memo** | ❌ | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| **OptionSet** | ❌ | ✅ | — | ✅ | ❌ | ❌ | ❌ |
| **Number** | ❌ | ✅ | — | ❌ | ✅ | ❌ | ❌ |
| **DateTime** | ❌ | ✅ | — | ❌ | ❌ | ✅ | ❌ |
| **Boolean** | ❌ | ✅ | — | ❌ | ❌ | ❌ | ✅ |

**Legend:**
- ✅ = Compatible - mapping will work
- ❌ = Not compatible - mapping is blocked
- — = Not applicable / inefficient

### Examples by Source Type

#### From Lookup Fields
```
✅ ALLOWED:
  Matter.Client (Lookup) → Event.Account (Lookup)
  Matter.Client (Lookup) → Event.Description (Text)

❌ BLOCKED:
  Matter.Client (Lookup) → Event.Priority (OptionSet)
  Matter.Client (Lookup) → Event.Amount (Number)
```

#### From Text Fields
```
✅ ALLOWED:
  Matter.Description (Text) → Event.Description (Text)
  Matter.Description (Text) → Event.Notes (Memo)
  Matter.MatterName (Text) → Event.Description (Memo)

❌ BLOCKED:
  Matter.Code (Text) → Event.Account (Lookup)
  Matter.Code (Text) → Event.Status (OptionSet)
```

#### From OptionSet Fields
```
✅ ALLOWED:
  Matter.Status (OptionSet) → Event.Status (OptionSet)
  Matter.Status (OptionSet) → Event.Notes (Text)

❌ BLOCKED:
  Matter.Status (OptionSet) → Event.Amount (Number)
  Matter.Status (OptionSet) → Event.Client (Lookup)
```

#### From Number Fields
```
✅ ALLOWED:
  Matter.Budget (Number) → Event.Amount (Number)
  Matter.Budget (Number) → Event.Description (Text)

❌ BLOCKED:
  Matter.Budget (Number) → Event.Client (Lookup)
  Matter.Budget (Number) → Event.Status (OptionSet)
```

#### From DateTime Fields
```
✅ ALLOWED:
  Matter.CreatedDate (DateTime) → Event.BaseDate (DateTime)
  Matter.CreatedDate (DateTime) → Event.Notes (Text)

❌ BLOCKED:
  Matter.CreatedDate (DateTime) → Event.Priority (OptionSet)
```

#### From Boolean Fields
```
✅ ALLOWED:
  Matter.IsActive (Boolean) → Event.IsActive (Boolean)
  Matter.IsActive (Boolean) → Event.Notes (Text)

❌ BLOCKED:
  Matter.IsActive (Boolean) → Event.Amount (Number)
```

---

## Troubleshooting {#troubleshooting}

### Field Mapping Not Being Applied

**Symptom:** You created a mapping profile and rules, but fields are not being populated on new Events.

**Possible Causes & Solutions:**

1. **Profile is not active**
   - Solution: Go to the profile and ensure "Is Active" = Yes

2. **Rule is not active**
   - Solution: Go to each rule in the profile and ensure "Is Active" = Yes

3. **Source field is empty on the parent record**
   - Solution: Check the parent record (Matter, Project, etc.) and verify the source field has a value
   - If source field is empty, the mapping will not populate anything unless a default value is set

4. **Events were created before the profile was created**
   - Solution: For "One-time" mappings, new Events created AFTER the profile will get the mappings. Existing Events will not
   - Workaround: Use the "Update Related" button to apply mappings to existing Events

5. **Parent-child relationship not recognized**
   - Solution: Verify the Event is correctly associated with the parent record:
     - For Matter Events: the "Regarding Matter" field must be set
     - For Project Events: the "Regarding Project" field must be set
     - The "Regarding Record Type" field should match the parent type

### Type Validation Error When Saving a Rule

**Symptom:** "Type Compatibility Error: Source field type [Type A] cannot be mapped to target field type [Type B]"

**Solution:**

1. Check the [Type Compatibility Reference](#reference-table) above
2. The source and target field types are not compatible
3. Choose a different target field that IS compatible with the source type
4. Alternatively, if targeting a Lookup field, use a Text field instead:
   - Text fields can receive values from any source type
   - The value will be the formatted representation (e.g., record name, option label, etc.)

### Mapping Overwrites User Data

**Symptom:** When you click "Refresh from Parent" or "Update Related", it overwrites values that users manually entered.

**Solution:**

This is expected behavior - mappings are designed to copy values from parent to child, which may overwrite previous values.

**Prevention:**
- Design mappings for fields that should always reflect parent values
- Don't map fields that users are expected to customize
- Document which fields are auto-populated via mapping so users understand the behavior
- Use "One-time" sync mode if you only want initial population at creation
- Use "Manual Refresh" if users should control when sync happens

### Required Field Validation Error

**Symptom:** When creating an Event, you see an error "Required field missing" even though a mapping profile should populate it.

**Possible Causes:**

1. **Source field is empty on the parent record**
   - The mapping rule has "Is Required" = Yes, but the source field is empty
   - Solution: Populate the source field on the parent record first

2. **Mapping hasn't executed yet**
   - For "One-time" mappings, ensure the Event is being created while associated with the parent
   - Solution: Create the Event through the parent record's related view or form

3. **Default value not set**
   - If you have "Is Required" = Yes, consider setting a "Default Value" in the rule
   - This provides a fallback if the source is empty

---

## Best Practices {#best-practices}

### When Planning Your Mappings

1. **Start with High-Value Fields**
   - Identify which fields are most frequently accessed and most critical for consistency
   - Map those fields first to demonstrate value

2. **Plan the Sync Mode**
   - Choose "One-time" for fields that are snapshots (e.g., priority at creation time)
   - Choose "Manual Refresh" for fields that users might need to refresh
   - Choose "Update Related" for fields that should be immediately consistent across all children

3. **Document Dependencies**
   - If you have cascading mappings (Rule B depends on Rule A), document this
   - Set appropriate "Execution Order" values
   - Test to ensure rules execute in the correct sequence

4. **Test Before Production**
   - Create a test mapping in a development environment first
   - Test all three scenarios:
     - Create a child record and verify initial mappings
     - Update parent and use "Refresh from Parent" on child
     - Update parent and use "Update Related" from parent
   - Verify that field values match expectations

### Naming Conventions

Use clear, descriptive names for profiles and rules:

**Good Profile Names:**
- "Matter to Event"
- "Project to Event"
- "Account Synchronization"

**Good Rule Names:**
- "Copy Matter Client to Event Account"
- "Copy Priority from Matter to Event"
- "Sync Status from Parent"

**Avoid:**
- Generic names like "Rule 1" or "Mapping A"
- Unclear names like "Field Copy"
- Names that don't indicate source and target

### Monitoring and Maintenance

1. **Regularly review active profiles**
   - Are all profiles still being used?
   - Are there any disabled profiles that could be removed?

2. **Monitor for errors**
   - If users report missing or incorrect data, check the mapping profile
   - Verify that rules are still active and compatible

3. **Document your mappings**
   - Add descriptions to profiles and rules
   - Update documentation when you make changes
   - Share the mapping strategy with the user community

### Common Mapping Scenarios

#### Scenario 1: Client Information Inheritance
**Goal:** When creating an Event for a Matter, automatically populate the Client field

**Solution:**
- Profile: "Matter to Event"
- Rule: Matter.Client (Lookup) → Event.Account (Lookup)
- Sync Mode: One-time

#### Scenario 2: Priority Synchronization
**Goal:** When Matter priority changes, immediately update all related Events

**Solution:**
- Profile: "Matter to Event"
- Rule: Matter.Priority (OptionSet) → Event.Priority (OptionSet)
- Sync Mode: One-time (for creation) + Use "Update Related" button when priority changes

#### Scenario 3: Due Date Inheritance
**Goal:** Events inherit due dates from their parent Matter, but users can override

**Solution:**
- Profile: "Matter to Event"
- Rule: Matter.DueDate (DateTime) → Event.DueDate (DateTime)
- Set "Is Required" = No (allows override)
- Sync Mode: Manual Refresh (allows users to refresh if needed)

---

## Getting Help

If you need additional assistance with Field Mapping configuration:

1. **Consult the full Events and Workflow Automation User Guide** for end-user documentation
2. **Contact your system administrator** for configuration questions
3. **Escalate to the development team** if you encounter errors or need to configure complex mappings
4. **Review the Event Log** to track which mappings have been applied to specific records

---

*Last Updated: February 1, 2026*
