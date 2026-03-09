# Spaarke Events Module – Design (AI-directed coding)

## 1. Purpose and scope
This document defines the design for Spaarke’s Events module. It is written for Claude Code to convert into a detailed spec.md and implementation plan.

The Events module provides a Dataverse-native way to record and manage operational activity and work items, including Actions, Tasks, Reminders, and additional recommended Event Types. The module is designed to work hand in glove with Spaarke’s Workflow and Orchestration engine, while remaining useful as a standalone capability in Phase 1.

## 2. Architectural constraints and principles
The Events module MUST comply with Spaarke ADR constraints.

- No Power Automate.
- No Dataverse plugins.
- No JavaScript customizations on forms. PCF controls are allowed.
- Prefer minimal APIs plus worker services for async processing.
- Optimize for Dataverse performance and model-driven app usability, including views, subgrids, and show related patterns.
- Use deterministic logic for dates and state transitions. AI may assist in authoring and explanation, but not in compliance-critical calculations.

## 3. Definitions and recommended Event Types
### 3.1 Event Types
Events represent atomic, durable operational facts or work items. Keep Event Types relatively small and stable; use Subtypes and metadata for domain-specific variety.

Core types in scope.
- Action
  - Definition: point-in-time occurrence with an Event Date; typically informational or historical; no due date; may be user or system generated.
  - Distinguishing fields: eventDate required; dueDate null; no work completion required.
- Task
  - Definition: actionable work item with a Due Date and lifecycle state.
  - Distinguishing fields: dueDate required; status lifecycle applies (Open, In Progress, Completed, Cancelled).
  - Task Types: Official, Internal, External, To Do.
- Reminder
  - Definition: a time-based nudge associated to a Task (or occasionally a Milestone or Deadline) that should surface prior to or at a defined time.
  - Distinguishing fields: associated to a Task; supports offset or scheduled timestamp; may be multi-instance.

Additional recommended Event Types.
- Milestone
  - Definition: key lifecycle marker that anchors timelines and calculations; not necessarily a work item.
  - Distinguishing fields: eventDate required; typically no due date; often triggers downstream tasks.
- Deadline
  - Definition: time-critical obligation with stronger compliance semantics than a generic task.
  - Distinguishing fields: dueDate required; calculation provenance should be stored; escalation policies often apply.
  - Implementation option: Task subtype Deadline or separate Event Type. Prefer subtype initially.
- Notification
  - Definition: informational event indicating another event occurred; supports fan-out to audiences.
  - Distinguishing fields: references source event; typically no due date; may store channel metadata.
- StatusChange
  - Definition: auditable record of a state transition on a related record (Matter stage, Contract status, etc.).
  - Distinguishing fields: before and after values; eventDate; system-generated.
- Approval
  - Definition: decision step with explicit outcome states (Approved, Rejected, Needs Changes).
  - Distinguishing fields: approver, decision, decision timestamp; often modeled as Task subtype initially.
- Communication
  - Definition: inbound or outbound interaction that matters to the file (email, call, message).
  - Distinguishing fields: eventDate and time; participants; link to artifact (email or document).
- Meeting
  - Definition: scheduled meeting with start and end and participants; often a subtype of Communication.
- FilingOrService
  - Definition: filing or service events with traceable artifacts; common anchors for deadlines.
- ExceptionAlert
  - Definition: system-generated exception or policy breach surfaced as an operational event (not just a log).

### 3.2 Event Sets
An Event Set is a runtime container that groups related Events created together and enables group operations and future orchestration.

- In Phase 1, Event Sets provide explicit membership and simple manual group operations.
- In later phases, Event Sets will carry orchestration policies and template lineage.

## 4. Phase 1: Minimal build specification
### 4.1 Phase 1 goals
Phase 1 MUST deliver.
- Dataverse data model to support Events, Event Types, and Event Sets.
- A Regarding association approach that works with model-driven views and subgrids.
- Minimal API surface for CRUD plus state transitions.
- Model-driven UX: subgrids, views, and forms.
- No rule engine, no automated orchestration, no external dispatch (Teams, Outlook, email).

### 4.2 Dataverse Data Model (Phase 1) ✅ SCHEMA COMPLETED

> **Reference**: See `Event Related Tables Schema.md` for complete field-level documentation.

#### 4.2.1 sprk_event (Event Table) ✅ COMPLETED

Use one Events table to optimize timeline queries and avoid joins.

**Core Fields:**

| Display Name | Schema Name | Type | Notes |
|--------------|-------------|------|-------|
| Event Name | `sprk_eventname` | Single Line Text | Primary name field (required) |
| Description | `sprk_description` | Multiline Text | Optional |
| Event Type | `sprk_eventtype_ref` | Lookup → sprk_eventtype | Required; determines validation rules |
| Status | `statecode` | Choice | Active (0), Inactive (1) |
| Status Reason | `statuscode` | Choice | Draft (1), Planned, Open, On Hold, Completed (2), Cancelled, Deleted |
| Base Date | `sprk_basedate` | Date Only | Event occurrence date (required for Actions, Milestones) |
| Due Date | `sprk_duedate` | Date Only | Required for Tasks |
| Completed Date | `sprk_completeddate` | Date Only | Set when Status Reason = Completed |
| Priority | `sprk_priority` | Choice | Low (0), Normal (1), High (2), Urgent (3) |
| Source | `sprk_source` | Choice | User (0), System (1), Workflow (2), External (3) |
| Owner | `ownerid` | Owner | Standard Dataverse owner field |

**Related Event Fields (for Reminders, Notifications, Extensions):**

| Display Name | Schema Name | Type | Notes |
|--------------|-------------|------|-------|
| Related Event | `sprk_relatedevent` | Lookup → sprk_event | Self-referential N:1; required when Related Event Type is set |
| Related Event Type | `sprk_relatedeventtype` | Choice | Reminder (0), Notification (1), Extension (2) |
| Related Event Offset Type | `sprk_relatedeventoffsettype` | Choice | Hours Before Due (0), Hours After Due (1), Days Before Due (2), Days After Due (3), Fixed Date/Time (4) |
| Remind At | `sprk_remindat` | DateTime | Calculated or fixed reminder time |

**Regarding Fields:** See Section 4.3.3 for complete Regarding schema.

**Event Set Field (Deferred):**

| Display Name | Schema Name | Type | Notes |
|--------------|-------------|------|-------|
| Event Set | `sprk_eventset` | Lookup → sprk_eventset | ⏸️ Deferred to Workflow Engine project |

**Validation Rules (enforced in API and UI):**
- If Event Type requires Base Date → `sprk_basedate` required
- If Event Type requires Due Date → `sprk_duedate` required
- If Related Event Type is set → `sprk_relatedevent` required
- If Related Event Type = Reminder → offset type/value OR remind at must be set

#### 4.2.2 sprk_eventtype (Event Type Table) ✅ COMPLETED

Reference data table defining Event Types and their validation requirements.

| Display Name | Schema Name | Type | Notes |
|--------------|-------------|------|-------|
| Name | `sprk_name` | Single Line Text | Primary name field |
| Event Code | `sprk_eventcode` | Single Line Text | Unique code (ACTION, TASK, REMINDER, etc.) |
| Description | `sprk_description` | Multiline Text | |
| Status | `statecode` | Choice | Active (0), Inactive (1) |
| Status Reason | `statuscode` | Choice | Active (1), Inactive (2) |
| Requires Due Date | `sprk_requiresduedate` | Choice | No (0), Yes (1) |
| Requires Base Date | `sprk_requiresbasedate` | Choice | No (0), Yes (1) |

**Seed Data:**

| Name | Code | Requires Due Date | Requires Base Date |
|------|------|-------------------|---------------------|
| Action | ACTION | No | Yes |
| Task | TASK | Yes | No |
| Reminder | REMINDER | No | No |
| Milestone | MILESTONE | No | Yes |
| Deadline | DEADLINE | Yes | No |
| Notification | NOTIFICATION | No | No |
| Status Change | STATUSCHANGE | No | Yes |
| Approval | APPROVAL | Yes | No |
| Communication | COMMUNICATION | No | Yes |
| Exception Alert | EXCEPTIONALERT | No | Yes |

#### 4.2.3 sprk_eventlog (Event Log Table) ✅ COMPLETED

Audit and tracking table specific to Events. Provides accessible event history for monitoring and UI displays (supplements but does not replace Dataverse native auditing).

| Display Name | Schema Name | Type | Notes |
|--------------|-------------|------|-------|
| Event Log Name | `sprk_eventlogname` | Single Line Text | Primary name field (auto-generated) |
| Event | `sprk_event` | Lookup → sprk_event | Required; the Event being logged |
| Action | `sprk_action` | Choice | Created (0), Updated (1), Completed (2), Cancelled (3), Deleted (4) |
| Description | `sprk_description` | Multiline Text | Details of the change |

**Usage:**
- API creates Event Log entries on Event state transitions
- Enables Event-specific audit views and reporting
- Accessible via standard Dataverse queries (unlike native audit logs)

#### 4.2.4 Event Sets ⏸️ DEFERRED

> **Note:** Event Set tables (`sprk_eventset`, `sprk_eventsetmember`) exist in schema but are **deferred to the Workflow Engine project**. Phase 1 Events can exist independently without Event Set membership.

The `sprk_eventset` lookup field exists on `sprk_event` for future use but should not be populated in Phase 1.

### 4.3 Regarding Records Association (Phase 1) ✅ DATAVERSE SCHEMA COMPLETED

#### 4.3.1 Problem Statement

Events in Spaarke can be related to multiple record types (Matter, Project, Invoice, Analysis, Account, Contact). Dataverse polymorphic lookups (Customer, Regarding) are not supported in:
- Views and Advanced Find filtering
- "Show related" subgrids on parent forms
- Cross-entity reporting

This prevents unified Event views and standard model-driven app patterns.

#### 4.3.2 Solution Architecture

Use a dual-field strategy combining entity-specific lookups with denormalized reference fields, managed by PCF controls.

```
sprk_event (Event table)
├── Entity-Specific Lookups (for subgrids and native filtering)
│   ├── sprk_regardingproject        → Lookup to Project
│   ├── sprk_regardingmatter         → Lookup to Matter
│   ├── sprk_regardinginvoice        → Lookup to Invoice
│   ├── sprk_regardinganalysis       → Lookup to Analysis
│   ├── sprk_regardingaccount        → Lookup to Account
│   ├── sprk_regardingcontact        → Lookup to Contact
│   ├── sprk_regardingbudget         → Lookup to Budget
│   └── sprk_regardingworkassignment → Lookup to Work Assignment
│
├── Unified Reference Fields (for cross-entity views and PCF display)
│   ├── sprk_regardingrecordtype  → Choice (Project, Matter, Invoice, Analysis, Account, Contact, Budget, Work Assignment)
│   ├── sprk_regardingrecordid    → Text (GUID of related record)
│   └── sprk_regardingrecordname  → Text (display name for UI)
│
└── PCF Controls (manage field synchronization and navigation)
    ├── Association Resolver (form control)
    └── Regarding Link (grid column control)
```

**Invariant Rules (enforced by API and PCF):**
- Exactly one `sprk_Regarding*` lookup is populated per Event
- `sprk_RegardingRecordType` matches the populated lookup's entity type
- `sprk_RegardingRecordId` contains the GUID from the populated lookup
- `sprk_RegardingRecordName` contains the primary name for display

#### 4.3.3 Dataverse Schema ✅ COMPLETED

**Entity-Specific Lookup Fields on sprk_Event:**

| Display Name | Schema Name | Target Entity | Relationship Name |
|--------------|-------------|---------------|-------------------|
| Regarding Project | `sprk_regardingproject` | Project | `sprk_event_RegardingProject_n1` |
| Regarding Matter | `sprk_regardingmatter` | Matter | `sprk_event_RegardingMatter_n1` |
| Regarding Invoice | `sprk_regardinginvoice` | Invoice | `sprk_event_RegardingInvoice_n1` |
| Regarding Analysis | `sprk_regardinganalysis` | Analysis | `sprk_event_RegardingAnalysis_n1` |
| Regarding Account | `sprk_regardingaccount` | Account | `sprk_event_RegardingAccount_n1` |
| Regarding Contact | `sprk_regardingcontact` | Contact | `sprk_event_RegardingContact_n1` |
| Regarding Budget | `sprk_regardingbudget` | Budget | `sprk_event_RegardingBudget_n1` |
| Regarding Work Assignment | `sprk_regardingworkassignment` | Work Assignment | `sprk_event_RegardingWorkAssignment_n1` |

**Unified Reference Fields on sprk_Event:**

| Display Name | Schema Name | Type | Details |
|--------------|-------------|------|---------|
| Regarding Record Type | `sprk_regardingrecordtype` | Choice | See choice values below |
| Regarding Record Id | `sprk_regardingrecordid` | Single Line Text | GUID of related record |
| Regarding Record Name | `sprk_regardingrecordname` | Single Line Text | Display name for UI |

**Choice Values for sprk_regardingrecordtype:** ✅ COMPLETED

| Label | Value |
|-------|-------|
| Project | 0 |
| Matter | 1 |
| Invoice | 2 |
| Analysis | 3 |
| Account | 4 |
| Contact | 5 |
| Work Assignment | 6 |
| Budget | 7 |

#### 4.3.4 PCF Association Resolver (Form Control)

**Component Name:** `sprk_AssociationResolver`

**Purpose:** Provides a unified UI for selecting the related record on Event forms, managing field synchronization automatically. This is the primary PCF control for the Regarding Records feature.

##### 4.3.4.1 Technical Specification

**Control Type:** Field-bound PCF control (StandardControl)

**Technology Stack:**
- React 16 (per ADR-022 PCF Platform Libraries constraint)
- Fluent UI v9 components (per ADR-021 Design System)
- TypeScript
- Dataverse Web API (Xrm.WebApi)

**Primary Binding:** `sprk_regardingrecordtype` (Choice field)

**Additional Bound Fields (read/write via context):**
- `sprk_regardingrecordid` (Text)
- `sprk_regardingrecordname` (Text)
- `sprk_regardingproject` (Lookup)
- `sprk_regardingmatter` (Lookup)
- `sprk_regardinginvoice` (Lookup)
- `sprk_regardinganalysis` (Lookup)
- `sprk_regardingaccount` (Lookup)
- `sprk_regardingcontact` (Lookup)
- `sprk_regardingworkassignment` (Lookup)
- `sprk_regardingbudget` (Lookup)

##### 4.3.4.2 Component Modes

| Mode | Trigger Condition | Behavior | Editable |
|------|-------------------|----------|----------|
| **Picker Mode** | `sprk_regardingrecordtype` is null AND form is editable | Entity type dropdown + record search | Yes |
| **Display Mode** | `sprk_regardingrecordtype` is set AND form is read-only | Clickable link to related record | No |
| **Locked Mode** | `sprk_regardingrecordtype` is set AND form is editable AND `allowEdit` = false | Display with navigation, no edit | No |
| **Edit Mode** | `sprk_regardingrecordtype` is set AND form is editable AND `allowEdit` = true | Display with change option | Yes |

**Mode Detection Logic:**
```typescript
private determineMode(): ControlMode {
  const hasValue = this.regardingRecordType !== null;
  const isEditable = this.context.mode.isControlDisabled === false;
  const allowEdit = this.context.parameters.allowEdit?.raw === true;

  if (!hasValue && isEditable) return ControlMode.Picker;
  if (!hasValue && !isEditable) return ControlMode.Empty;
  if (hasValue && !isEditable) return ControlMode.Display;
  if (hasValue && isEditable && !allowEdit) return ControlMode.Locked;
  if (hasValue && isEditable && allowEdit) return ControlMode.Edit;

  return ControlMode.Display;
}
```

##### 4.3.4.3 Visual Design

**Picker Mode (Create/Edit, No Value Set):**
```
┌─────────────────────────────────────────────────────────────────┐
│ Related To *                                                     │
├─────────────────────────────────────────────────────────────────┤
│ ┌────────────────┐  ┌──────────────────────────────────────────┐│
│ │ Select type  ▼ │  │ Search...                         [🔍]  ││
│ └────────────────┘  └──────────────────────────────────────────┘│
│                                                                  │
│ Search results dropdown (when searching):                        │
│ ┌──────────────────────────────────────────────────────────────┐│
│ │ 📁 2024-001 - Smith v. Jones                                 ││
│ │ 📁 2024-002 - Acme Corp v. Beta Inc                          ││
│ │ 📁 2024-003 - Johnson Estate                                 ││
│ │ ─────────────────────────────────────────────────────────────││
│ │ 🔍 Search for more...                                        ││
│ └──────────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────────┘
```

**Display Mode (Read-Only with Navigation):**
```
┌─────────────────────────────────────────────────────────────────┐
│ Related To                                                       │
├─────────────────────────────────────────────────────────────────┤
│ ┌──────────────────────────────────────────────────────────────┐│
│ │ 📁 Matter: 2024-001 - Smith v. Jones                    [↗] ││
│ └──────────────────────────────────────────────────────────────┘│
│   └─ Clickable link opens record in new tab                     │
└─────────────────────────────────────────────────────────────────┘
```

**Locked Mode (Created from Parent Subgrid):**
```
┌─────────────────────────────────────────────────────────────────┐
│ Related To                                                       │
├─────────────────────────────────────────────────────────────────┤
│ ┌──────────────────────────────────────────────────────────────┐│
│ │ 🔒 Matter: 2024-001 - Smith v. Jones                    [↗] ││
│ └──────────────────────────────────────────────────────────────┘│
│   └─ Pre-populated from parent context, cannot be changed       │
└─────────────────────────────────────────────────────────────────┘
```

**Loading State:**
```
┌─────────────────────────────────────────────────────────────────┐
│ Related To                                                       │
├─────────────────────────────────────────────────────────────────┤
│ ┌──────────────────────────────────────────────────────────────┐│
│ │ [◌◌◌] Loading...                                             ││
│ └──────────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────────┘
```

**Error State:**
```
┌─────────────────────────────────────────────────────────────────┐
│ Related To *                                                     │
├─────────────────────────────────────────────────────────────────┤
│ ┌──────────────────────────────────────────────────────────────┐│
│ │ ⚠️ Please select a related record                            ││
│ └──────────────────────────────────────────────────────────────┘│
│   └─ Validation error styling (red border)                      │
└─────────────────────────────────────────────────────────────────┘
```

##### 4.3.4.4 PCF Manifest (ControlManifest.Input.xml)

```xml
<?xml version="1.0" encoding="utf-8"?>
<manifest>
  <control namespace="Spaarke"
           constructor="AssociationResolver"
           version="1.0.0"
           display-name-key="AssociationResolver"
           description-key="Polymorphic record association control"
           control-type="standard">

    <!-- Primary bound property -->
    <property name="regardingRecordType"
              display-name-key="Regarding Record Type"
              of-type="OptionSet"
              usage="bound"
              required="true" />

    <!-- Configuration properties -->
    <property name="allowedEntityTypes"
              display-name-key="Allowed Entity Types"
              of-type="SingleLine.Text"
              usage="input"
              required="false"
              default-value="project,matter,invoice,analysis,account,contact,workassignment,budget" />

    <property name="allowEdit"
              display-name-key="Allow Edit After Save"
              of-type="TwoOptions"
              usage="input"
              required="false"
              default-value="false" />

    <property name="showEntityTypeSelector"
              display-name-key="Show Entity Type Selector"
              of-type="TwoOptions"
              usage="input"
              required="false"
              default-value="true" />

    <property name="navigationEnabled"
              display-name-key="Enable Navigation Link"
              of-type="TwoOptions"
              usage="input"
              required="false"
              default-value="true" />

    <resources>
      <code path="index.ts" order="1" />
      <platform-library name="React" version="16.14.0" />
      <platform-library name="Fluent" version="9" />
    </resources>
  </control>
</manifest>
```

##### 4.3.4.5 Entity Configuration

Each supported entity type requires configuration for search and display:

| Entity | Logical Name | Primary Field | Search Fields | Icon |
|--------|--------------|---------------|---------------|------|
| Project | `sprk_project` | `sprk_name` | `sprk_name`, `sprk_projectnumber` | 📊 |
| Matter | `sprk_matter` | `sprk_name` | `sprk_name`, `sprk_matternumber` | 📁 |
| Invoice | `sprk_invoice` | `sprk_name` | `sprk_name`, `sprk_invoicenumber` | 💰 |
| Analysis | `sprk_analysis` | `sprk_name` | `sprk_name` | 📈 |
| Account | `account` | `name` | `name`, `accountnumber` | 🏢 |
| Contact | `contact` | `fullname` | `fullname`, `emailaddress1` | 👤 |
| Work Assignment | `sprk_workassignment` | `sprk_name` | `sprk_name` | 📋 |
| Budget | `sprk_budget` | `sprk_name` | `sprk_name` | 💵 |

**Entity Configuration Structure:**
```typescript
interface EntityConfig {
  logicalName: string;
  displayName: string;
  primaryField: string;
  searchFields: string[];
  icon: string;
  regardingField: string;        // e.g., "sprk_regardingmatter"
  regardingRecordTypeValue: number;  // e.g., 1 for Matter
}

const ENTITY_CONFIGS: EntityConfig[] = [
  {
    logicalName: "sprk_project",
    displayName: "Project",
    primaryField: "sprk_name",
    searchFields: ["sprk_name", "sprk_projectnumber"],
    icon: "📊",
    regardingField: "sprk_regardingproject",
    regardingRecordTypeValue: 0
  },
  {
    logicalName: "sprk_matter",
    displayName: "Matter",
    primaryField: "sprk_name",
    searchFields: ["sprk_name", "sprk_matternumber"],
    icon: "📁",
    regardingField: "sprk_regardingmatter",
    regardingRecordTypeValue: 1
  },
  // ... additional entities
];
```

##### 4.3.4.6 Field Synchronization Logic

**On Record Selection:**
```typescript
private async onRecordSelected(
  entityConfig: EntityConfig,
  recordId: string,
  recordName: string
): Promise<void> {
  // 1. Update all fields atomically via form context
  this.context.parameters.regardingRecordType.raw = entityConfig.regardingRecordTypeValue;

  // 2. Update unified reference fields (via Xrm.WebApi or form attribute)
  await this.updateFormAttribute("sprk_regardingrecordid", recordId);
  await this.updateFormAttribute("sprk_regardingrecordname", recordName);

  // 3. Set the entity-specific lookup
  const lookupValue: ComponentFramework.LookupValue = {
    id: recordId,
    name: recordName,
    entityType: entityConfig.logicalName
  };
  await this.updateFormAttribute(entityConfig.regardingField, [lookupValue]);

  // 4. Clear all other entity-specific lookups
  for (const config of ENTITY_CONFIGS) {
    if (config.logicalName !== entityConfig.logicalName) {
      await this.updateFormAttribute(config.regardingField, null);
    }
  }

  // 5. Notify framework of changes
  this.notifyOutputChanged();
}
```

**On Form Load:**
```typescript
public init(
  context: ComponentFramework.Context<IInputs>,
  notifyOutputChanged: () => void,
  state: ComponentFramework.Dictionary
): void {
  // 1. Read current value
  const currentType = context.parameters.regardingRecordType.raw;

  // 2. If value exists, determine display mode
  if (currentType !== null) {
    const entityConfig = this.getEntityConfigByTypeValue(currentType);
    const recordName = this.getFormAttributeValue("sprk_regardingrecordname");
    const recordId = this.getFormAttributeValue("sprk_regardingrecordid");

    // 3. Check for parent context (locked mode)
    const isFromParent = this.detectParentContext();

    this.setState({
      mode: isFromParent ? ControlMode.Locked : this.determineMode(),
      selectedEntity: entityConfig,
      selectedRecordId: recordId,
      selectedRecordName: recordName
    });
  } else {
    // 4. Check form parameters for pre-population
    this.checkFormParametersForPrePopulation();
  }
}
```

**Parent Context Detection:**
```typescript
private detectParentContext(): boolean {
  // Check if opened from a parent subgrid (e.g., Matter → Events subgrid → New)
  const formContext = this.context.mode.contextInfo;

  // Check for parent entity reference in form parameters
  const pageInput = Xrm.Utility.getPageContext()?.input as any;
  if (pageInput?.createFromEntity) {
    return true;
  }

  // Check for pre-populated regarding lookup
  for (const config of ENTITY_CONFIGS) {
    const lookupValue = this.getFormAttributeValue(config.regardingField);
    if (lookupValue && lookupValue.length > 0) {
      return true;
    }
  }

  return false;
}
```

##### 4.3.4.7 Search Implementation

**Debounced Search:**
```typescript
private searchRecords = debounce(async (
  entityConfig: EntityConfig,
  searchText: string
): Promise<SearchResult[]> => {
  if (searchText.length < 2) return [];

  // Build OData filter for search fields
  const filterConditions = entityConfig.searchFields
    .map(field => `contains(${field}, '${searchText}')`)
    .join(" or ");

  const results = await Xrm.WebApi.retrieveMultipleRecords(
    entityConfig.logicalName,
    `?$select=${entityConfig.primaryField}&$filter=${filterConditions}&$top=10&$orderby=${entityConfig.primaryField}`
  );

  return results.entities.map(entity => ({
    id: entity[`${entityConfig.logicalName}id`],
    name: entity[entityConfig.primaryField],
    entityType: entityConfig.logicalName
  }));
}, 300); // 300ms debounce
```

##### 4.3.4.8 Navigation Implementation

```typescript
private openRelatedRecord(): void {
  if (!this.state.selectedRecordId || !this.state.selectedEntity) return;

  Xrm.Navigation.openForm({
    entityName: this.state.selectedEntity.logicalName,
    entityId: this.state.selectedRecordId,
    openInNewWindow: true
  }).catch(error => {
    console.error("Failed to open related record:", error);
    // Show user-friendly error message
    this.setState({ error: "Unable to open record. Please try again." });
  });
}
```

##### 4.3.4.9 Error Handling

| Scenario | Handling | User Message |
|----------|----------|--------------|
| Search API failure | Log error, show inline message | "Unable to search. Please try again." |
| Record not found | Clear selection, show message | "Selected record no longer exists." |
| Navigation failure | Log error, show toast | "Unable to open record." |
| Validation (required) | Highlight field, block save | "Please select a related record." |
| Network timeout | Retry with exponential backoff | "Connection slow. Retrying..." |

##### 4.3.4.10 Accessibility (a11y)

- **Keyboard Navigation:** Tab through entity selector → search input → results
- **ARIA Labels:** All interactive elements have descriptive labels
- **Screen Reader:** Announces selected record, mode changes, errors
- **Focus Management:** Returns focus to appropriate element after selection
- **High Contrast:** Supports Windows High Contrast mode via Fluent UI

```typescript
// Accessibility attributes
<Dropdown
  aria-label="Select entity type"
  aria-required={true}
  aria-invalid={hasError}
  aria-describedby={errorId}
/>

<SearchBox
  aria-label={`Search for ${selectedEntityName}`}
  aria-autocomplete="list"
  aria-controls={resultsListId}
  aria-expanded={showResults}
/>
```

##### 4.3.4.11 Testing Requirements

**Unit Tests:**
- Mode detection logic for all scenarios
- Entity configuration loading
- Field synchronization (mock Xrm.WebApi)
- Search debouncing and filtering
- Parent context detection

**Integration Tests:**
- Create Event from Matter subgrid → Locked mode
- Create Event from global list → Picker mode
- Edit existing Event → Display/Edit mode
- Navigation to related record
- Field value persistence on save

**Manual Test Cases:**
- Verify all 8 entity types searchable
- Verify navigation works for each entity type
- Verify form validation blocks save without selection
- Verify parent pre-population from each supported entity

#### 4.3.5 PCF Regarding Link (Grid Column Control)

**Component Name:** `sprk_RegardingLink`

**Purpose:** Renders the Regarding Record Name as a clickable link in Event views and subgrids, enabling navigation to the related record directly from list views. This control provides the cross-entity navigation capability in unified Event views.

##### 4.3.5.1 Technical Specification

**Control Type:** Field-bound PCF control (StandardControl) for grid/view column rendering

**Technology Stack:**
- React 16 (per ADR-022 PCF Platform Libraries constraint)
- Fluent UI v9 Link component (per ADR-021 Design System)
- TypeScript
- Xrm.Navigation API

**Primary Binding:** `sprk_regardingrecordname` (Text field)

**Required Sibling Columns in View:**
- `sprk_regardingrecordtype` (must be included in view, can be hidden)
- `sprk_regardingrecordid` (must be included in view, can be hidden)

##### 4.3.5.2 Visual Design

**Standard View Display:**
```
┌─────────────────────────────────────────────────────────────────────────┐
│ Events - All Open Tasks                                          [⚙️]  │
├─────────────────────────────────────────────────────────────────────────┤
│ ☑️│ Event Name      │ Due Date   │ Priority │ Related To │ Record      │
│───┼─────────────────┼────────────┼──────────┼────────────┼─────────────│
│ ☐ │ Review contract │ 2024-02-15 │ High     │ Matter     │ 2024-001 ↗  │
│ ☐ │ Send proposal   │ 2024-02-20 │ Normal   │ Project    │ Website  ↗  │
│ ☐ │ Follow up call  │ 2024-02-18 │ Low      │ Contact    │ J. Smith ↗  │
│ ☐ │ Budget review   │ 2024-02-22 │ Normal   │ Budget     │ Q1 2024  ↗  │
│ ☐ │ Assignment task │ 2024-02-25 │ High     │ Work Assgn │ WA-001   ↗  │
└─────────────────────────────────────────────────────────────────────────┘
  └─ "Record" column uses PCF Regarding Link control
```

**Link Rendering States:**

| State | Display | Behavior |
|-------|---------|----------|
| Has value | `Record Name ↗` (blue link) | Clickable, opens record |
| Null/empty | `—` (em-dash, gray) | No action |
| Hover | `Record Name ↗` (underline) | Cursor pointer |
| Focus | `Record Name ↗` (focus ring) | Keyboard accessible |
| Loading | `...` (ellipsis) | During navigation |

**Styling (Fluent UI v9):**
```typescript
const linkStyles: React.CSSProperties = {
  color: tokens.colorBrandForeground1,     // Fluent brand blue
  textDecoration: "none",
  cursor: "pointer",
  display: "inline-flex",
  alignItems: "center",
  gap: "4px"
};

const linkHoverStyles: React.CSSProperties = {
  textDecoration: "underline"
};

const emptyStyles: React.CSSProperties = {
  color: tokens.colorNeutralForeground4,   // Gray for empty
  fontStyle: "italic"
};
```

##### 4.3.5.3 PCF Manifest (ControlManifest.Input.xml)

```xml
<?xml version="1.0" encoding="utf-8"?>
<manifest>
  <control namespace="Spaarke"
           constructor="RegardingLink"
           version="1.0.0"
           display-name-key="RegardingLink"
           description-key="Clickable link to related record in grid views"
           control-type="standard">

    <!-- Primary bound property (displayed text) -->
    <property name="regardingRecordName"
              display-name-key="Regarding Record Name"
              of-type="SingleLine.Text"
              usage="bound"
              required="true" />

    <!-- Configuration properties -->
    <property name="openInNewWindow"
              display-name-key="Open in New Window"
              of-type="TwoOptions"
              usage="input"
              required="false"
              default-value="true" />

    <property name="showIcon"
              display-name-key="Show Navigation Icon"
              of-type="TwoOptions"
              usage="input"
              required="false"
              default-value="true" />

    <property name="emptyText"
              display-name-key="Empty Value Text"
              of-type="SingleLine.Text"
              usage="input"
              required="false"
              default-value="—" />

    <resources>
      <code path="index.ts" order="1" />
      <platform-library name="React" version="16.14.0" />
      <platform-library name="Fluent" version="9" />
    </resources>
  </control>
</manifest>
```

##### 4.3.5.4 Implementation

**Main Component:**
```typescript
import * as React from "react";
import { Link, Spinner, tokens } from "@fluentui/react-components";
import { OpenRegular } from "@fluentui/react-icons";

interface IRegardingLinkProps {
  recordName: string | null;
  recordType: number | null;
  recordId: string | null;
  openInNewWindow: boolean;
  showIcon: boolean;
  emptyText: string;
}

export const RegardingLink: React.FC<IRegardingLinkProps> = ({
  recordName,
  recordType,
  recordId,
  openInNewWindow,
  showIcon,
  emptyText
}) => {
  const [isNavigating, setIsNavigating] = React.useState(false);

  // Handle null/empty values
  if (!recordName || !recordType || !recordId) {
    return (
      <span style={{ color: tokens.colorNeutralForeground4 }}>
        {emptyText}
      </span>
    );
  }

  const handleClick = async (e: React.MouseEvent) => {
    e.preventDefault();
    e.stopPropagation(); // Prevent row selection

    setIsNavigating(true);

    try {
      const entityLogicalName = getEntityLogicalName(recordType);
      await Xrm.Navigation.openForm({
        entityName: entityLogicalName,
        entityId: recordId,
        openInNewWindow: openInNewWindow
      });
    } catch (error) {
      console.error("Navigation failed:", error);
    } finally {
      setIsNavigating(false);
    }
  };

  if (isNavigating) {
    return <Spinner size="tiny" />;
  }

  return (
    <Link
      onClick={handleClick}
      onKeyDown={(e) => e.key === "Enter" && handleClick(e as any)}
      tabIndex={0}
      aria-label={`Open ${recordName}`}
      style={{ display: "inline-flex", alignItems: "center", gap: "4px" }}
    >
      {recordName}
      {showIcon && <OpenRegular fontSize={12} />}
    </Link>
  );
};

// Map regardingRecordType choice value to entity logical name
function getEntityLogicalName(typeValue: number): string {
  const mapping: Record<number, string> = {
    0: "sprk_project",
    1: "sprk_matter",
    2: "sprk_invoice",
    3: "sprk_analysis",
    4: "account",
    5: "contact",
    6: "sprk_workassignment",
    7: "sprk_budget"
  };
  return mapping[typeValue] || "sprk_matter"; // Default fallback
}
```

**PCF Index Class:**
```typescript
export class RegardingLink implements ComponentFramework.StandardControl<IInputs, IOutputs> {
  private container: HTMLDivElement;
  private context: ComponentFramework.Context<IInputs>;

  public init(
    context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    state: ComponentFramework.Dictionary,
    container: HTMLDivElement
  ): void {
    this.container = container;
    this.context = context;
  }

  public updateView(context: ComponentFramework.Context<IInputs>): void {
    this.context = context;

    // Get values from bound field and row context
    const recordName = context.parameters.regardingRecordName?.raw || null;

    // Access sibling columns via formatting context (grid)
    const recordType = this.getSiblingColumnValue("sprk_regardingrecordtype");
    const recordId = this.getSiblingColumnValue("sprk_regardingrecordid");

    // Configuration
    const openInNewWindow = context.parameters.openInNewWindow?.raw ?? true;
    const showIcon = context.parameters.showIcon?.raw ?? true;
    const emptyText = context.parameters.emptyText?.raw || "—";

    ReactDOM.render(
      React.createElement(RegardingLink, {
        recordName,
        recordType,
        recordId,
        openInNewWindow,
        showIcon,
        emptyText
      }),
      this.container
    );
  }

  private getSiblingColumnValue(columnName: string): any {
    // In grid context, access other columns from the same row
    const formatting = this.context.mode as any;
    if (formatting.contextInfo?.entityId) {
      // Read from row data if available
      return formatting.contextInfo[columnName];
    }
    return null;
  }

  public destroy(): void {
    ReactDOM.unmountComponentAtNode(this.container);
  }
}
```

##### 4.3.5.5 View Configuration Requirements

For the PCF control to work correctly, views must include the required columns:

**Required Columns (can be hidden):**
- `sprk_regardingrecordtype` - Choice value for entity type
- `sprk_regardingrecordid` - GUID of related record

**Visible Column with PCF:**
- `sprk_regardingrecordname` - Display name (bind PCF to this column)

**Example View Definition:**
```xml
<view>
  <grid>
    <row>
      <cell name="sprk_eventname" />
      <cell name="sprk_duedate" />
      <cell name="sprk_priority" />
      <cell name="sprk_regardingrecordtype" />
      <cell name="sprk_regardingrecordname">
        <control id="sprk_RegardingLink" />
      </cell>
      <!-- Hidden but required for PCF -->
      <cell name="sprk_regardingrecordid" hidden="true" />
    </row>
  </grid>
</view>
```

##### 4.3.5.6 Error Handling

| Scenario | Handling | Display |
|----------|----------|---------|
| Missing sibling columns | Log warning, show name only | `Record Name` (no link) |
| Invalid record type | Log error, show name only | `Record Name` (no link) |
| Navigation failure | Log error, show toast | Brief error notification |
| Record deleted | Show error state | `Record not found` |

##### 4.3.5.7 Accessibility

- **Keyboard:** Tab to link, Enter to activate
- **ARIA:** `role="link"`, `aria-label` with full record context
- **Focus:** Visible focus indicator (Fluent UI default)
- **Screen Reader:** Announces "Link: [Record Name], opens in new window"

##### 4.3.5.8 Performance Considerations

- **No API calls:** All data comes from view columns (already fetched)
- **Minimal render:** Simple link component, no complex state
- **Event delegation:** Click handlers use event.stopPropagation() to prevent row selection conflicts
- **Lazy navigation:** Only calls Xrm.Navigation on click, not on render

##### 4.3.5.9 Testing Requirements

**Unit Tests:**
- Render with valid data → shows link with icon
- Render with null name → shows empty text
- Click handler → calls Xrm.Navigation with correct params
- Entity type mapping → all 8 types map correctly

**Integration Tests:**
- Add control to view → renders in grid
- Click link → opens correct record
- Keyboard navigation → Enter activates link
- Multiple rows → each link navigates to correct record

**Manual Test Cases:**
- Test navigation for each of the 8 entity types
- Verify link styling matches Fluent UI theme
- Verify behavior when record has been deleted
- Test with very long record names (truncation)
- Test in subgrid context vs main grid

#### 4.3.6 User Experience Flows

**Flow 1: Create Event from Matter Subgrid (Primary Pattern)**

```
Step 1: User on Matter form → clicks "+ New Event" in Events subgrid
        ↓
Step 2: Event quick-create form opens
        - Form parameter passes matterId
        - PCF detects parent context
        - Auto-populates: sprk_RegardingMatter = current Matter
        - PCF syncs all reference fields
        - PCF enters Locked Mode (shows Matter, non-editable)
        ↓
Step 3: User fills Event details (Subject, Type, Due Date)
        ↓
Step 4: User saves → Event appears in Matter's Events subgrid
```

**Flow 2: Create Event from Global Events List**

```
Step 1: User navigates to Events area → clicks "+ New"
        ↓
Step 2: Event form opens with PCF in Picker Mode
        ↓
Step 3: User selects entity type from dropdown
        ┌───────────────────┐
        │ Select type       │
        │ ──────────────────│
        │ Project           │
        │ Matter            │
        │ Invoice           │
        │ Analysis          │
        │ Account           │
        │ Contact           │
        │ Budget            │
        │ Work Assignment   │
        └───────────────────┘
        ↓
Step 4: User searches and selects specific record
        ↓
Step 5: PCF syncs all fields automatically
        ↓
Step 6: User completes form and saves
```

**Flow 3: View Unified Events List with Navigation**

```
View: "All Open Tasks" (cross-entity)

User sees grid with Regarding Link PCF in "Record" column:
┌────────────────────────────────────────────────────────────┐
│ Subject         │ Due Date   │ Type   │ Record            │
│─────────────────┼────────────┼────────┼───────────────────│
│ Review terms    │ Feb 15     │ Matter │ 2024-001 Smith ↗  │
│ Send proposal   │ Feb 18     │ Project│ Website Build ↗   │
└────────────────────────────────────────────────────────────┘

User clicks "2024-001 Smith ↗" → Matter record opens in new tab
```

**Flow 4: Filter Events by Entity Type**

```
View: "All Events" with filtering

User adds filter: "Regarding Record Type equals Matter"
→ View shows only Events related to Matters
→ Can combine with other filters (State = Open, Due Date < Today)
```

#### 4.3.7 View Configuration Examples

**View: All Open Tasks (cross-entity)**
```
Entity: sprk_event
Columns:
  - sprk_eventname (Event Name)
  - sprk_duedate (Due Date)
  - statuscode (Status Reason)
  - sprk_priority (Priority)
  - sprk_regardingrecordtype (display: "Related To")
  - sprk_regardingrecordname (display: "Record") → PCF: sprk_RegardingLink
  - ownerid
Filter:
  - Event Type = Task
  - statecode = Active (0)
  - statuscode = Open
Sort: sprk_duedate ascending
```

**View: Matter Events (entity-specific)**
```
Entity: sprk_event
Columns:
  - sprk_eventname (Event Name)
  - sprk_eventtype_ref (Event Type)
  - sprk_basedate (Base Date)
  - sprk_duedate (Due Date)
  - statuscode (Status Reason)
  - sprk_regardingrecordname (display: "Matter") → PCF: sprk_RegardingLink
Filter:
  - sprk_regardingrecordtype = Matter (1)
Sort: sprk_basedate descending
```

#### 4.3.8 API Validation Rules

**POST /events - Create Event**
```
Request body includes ONE of: matterId, projectId, invoiceId, analysisId, accountId, contactId, budgetId, workAssignmentId

Validation:
1. Exactly one regarding ID must be provided (return 400 if zero or multiple)
2. Validate the referenced record exists in Dataverse
3. Fetch display name from Dataverse

Field population:
- Set sprk_regarding{entity} = provided ID
- Set sprk_regardingrecordtype = corresponding choice value (0-7)
- Set sprk_regardingrecordid = provided ID (as string)
- Set sprk_regardingrecordname = fetched display name
- Clear all other sprk_regarding* lookup fields (ensure null)

Regarding ID to Choice Value mapping:
- projectId → 0
- matterId → 1
- invoiceId → 2
- analysisId → 3
- accountId → 4
- contactId → 5
- workAssignmentId → 6
- budgetId → 7
```

**PATCH /events/{id} - Update Event**
```
Regarding fields are IMMUTABLE after creation.
- If request includes any regarding field changes → return 400 Bad Request
- Reason: Changing association would break audit trail and subgrid integrity
```

**Event Log Creation:**
```
On Event state transitions (create, complete, cancel, delete):
- Create sprk_eventlog record automatically
- Set sprk_event = Event ID
- Set sprk_action = appropriate action code (0-4)
- Set sprk_description = context details (user, timestamp, previous state)
```

#### 4.3.9 Adding New Entity Types (Future)

When a new entity type needs Event association:

1. **Dataverse Changes:**
   - Add `sprk_Regarding{NewEntity}` lookup field to sprk_Event
   - Add "{NewEntity}" option to `sprk_RegardingRecordType` choice
   - Create relationship `sprk_event_Regarding{NewEntity}_n1`

2. **PCF Updates:**
   - Add entity logical name to `allowedEntityTypes` configuration
   - Add entity metadata for search and display

3. **API Updates:**
   - Add `{newEntity}Id` parameter to create/query endpoints
   - Add validation and field population logic

**Estimated effort:** 2-4 hours per new entity type (no architectural changes required)

### 4.4 Minimal API surface (Phase 1)
Implement as Minimal API endpoints or via existing BFF patterns, calling Dataverse Web API.

Endpoints.
- POST /events
  - Create Event
  - Validates required fields per type and Regarding rules.
  - Supports optional eventSetId.
- PATCH /events/{eventId}
  - Update subject, description, eventDate, dueDate, priority, subtype.
- POST /events/{eventId}/complete
  - Sets state to Completed and sets completedOn.
- POST /events/{eventId}/cancel
  - Sets state to Cancelled.
- GET /events
  - Query by.
    - regarding entity (matterId, projectId, contractId, assignmentId)
    - eventSetId
    - types
    - states
    - date ranges (eventDate and dueDate)
  - Must be paged and server-side filtered.

Event Sets.
- POST /eventSets
- GET /eventSets?regarding=...
- GET /eventSets/{id}
- POST /eventSets/{id}/members
- POST /eventSets/{id}/complete
- POST /eventSets/{id}/cancel

Implementation rules.
- Enforce idempotency for complete and cancel.
- Use batch APIs where available for membership operations.
- Add structured logging with correlation IDs.

### 4.5 Model-driven UX (Phase 1)
Minimum UI.
- Matter form.
  - Events subgrid using show related via sprk_regarding_matterid
  - Views: Open Tasks, Upcoming Tasks, Recent Actions, All Events
  - Event Sets subgrid
- Project and Contract forms: equivalent subgrids using their Regarding lookup.
- Event form.
  - PCF Association Resolver
  - type-specific sections via form configuration
- Event Set form.
  - Members subgrid
  - Set state field

### 4.6 Performance requirements (Phase 1)
- Use the single sprk_event table for timeline queries.
- Ensure views and indexing patterns support.
  - each sprk_regarding_* lookup
  - sprk_type
  - sprk_state
  - sprk_duedate
  - sprk_eventdate
  - sprk_eventsetid
- GET /events must not require expanding related records for list views; rely on sprk_related_recordname and Event fields.

## 5. Phase 2 overview: Templates and progressive activation
Phase 2 introduces design-time definitions that drive consistent Event creation while avoiding overwhelming users with the full canonical lifecycle.

Phase 2 capabilities.
- Event Templates defining sets of event definitions and default metadata.
- Event Set lineage to template key and version.
- Progressive materialization: instantiate only always-applicable events and store node state for the rest (NotActivated, Eligible).
- Template cloning and parameterization by jurisdiction, client, and business unit.

## 6. Phase 3 overview: Event Set orchestration policies
Phase 3 introduces deterministic group behaviors without becoming a full BPMN system.

Phase 3 capabilities.
- Orchestration policy primitives.
  - CloseAllOnPrimaryComplete
  - CancelAllOnParentClose
  - PauseRemindersOnHold
  - CreateNextOnComplete
  - SupersedePreviousOnNewSet
  - EscalateOnOverdue
- Cascading group operations for complete and cancel.
- Async processing patterns using queues and Outbox for bulk updates.
- Audit of set transitions and cascades.

## 7. Phase 4 overview: Full rule engine and external dispatch
Phase 4 integrates or implements the full rule engine and external notifications.

Phase 4 capabilities.
- Rule matching by record type, matter type, jurisdiction, business unit, user or group, client, and contact.
- Triggers by field changes, record events, system events, and manual triggers.
- Deterministic calculations with provenance.
- External dispatch via Graph for email, Teams, and Outlook To Do.
- High-performance queueing, caching, and idempotency.

## 8. AI intelligence value with guardrails
AI provides value primarily in authoring and explanation.
- Assist in generating Event Templates and Event Set orchestration policies from natural language.
- Generate test cases and edge cases for template and orchestration behaviors.
- Explain why an event exists based on lineage.
- Summarize Event Set status and next steps.

AI must not produce compliance-critical date calculations at runtime or execute unbounded actions without deterministic validation.

## 9. Open decisions for spec.md
Claude Code should surface these decisions during specification authoring.
- Which entities are in-scope for Regarding in v1.
- Whether Event Sets have their own Regarding lookups or inherit via parent association.
- Whether Event membership is strictly one-to-one with Event Set in v1.
- Whether Reminder fields remain on sprk_event or move to a child table in v2.
- Which Event Types are enabled in Phase 1 versus reserved for future.

## 10. Implementation notes for Claude Code
- Create Dataverse tables and relationships first; verify show related subgrids.
- Build the PCF Association Resolver early, because it drives user creation flows.
- Implement minimal APIs next, then wire model-driven forms, subgrids, and views.
- Add automated tests for.
  - Event type validation
  - Regarding resolution and exactly one lookup rule
  - Complete and cancel idempotency
  - Query paging and filters
