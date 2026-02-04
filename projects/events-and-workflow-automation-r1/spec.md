# Events and Workflow Automation R1 - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-01-31
> **Source**: design.md

## Executive Summary

This project implements an event management system for Spaarke's legal practice management platform. Events represent scheduled activities, deadlines, reminders, and notifications that are associated with core business entities (Matters, Projects, Invoices, Analyses, Accounts, Contacts, Work Assignments, and Budgets). The system provides a centralized view of all events while maintaining entity-specific subgrid views, supported by PCF controls for the user experience.

**Two key platform capabilities are delivered:**

1. **Association Resolver Framework** - Addresses Dataverse's polymorphic lookup limitations where native "Regarding" fields cannot be used in views, filters, or Advanced Find. The solution uses a dual-field strategy: entity-specific lookups (for subgrid filtering) combined with denormalized reference fields (for unified cross-entity views). The AssociationResolver PCF provides a unified record picker that manages this complexity, allowing users to select from any supported entity type while the control handles populating all required fields.

2. **Field Mapping Framework** - An admin-configurable system for defining field-to-field mappings between parent and child records. This replaces Dataverse's limited OOB relationship mappings (1:N only, and only when creating from parent form) with a flexible solution supporting N:1 and N:N scenarios. Three sync modes are supported: one-time (at creation), manual refresh (pull from child), and update related (push from parent to all children).

## Scope

### In Scope

- **Dataverse Data Model**: Event (`sprk_event`), Event Type (`sprk_eventtype`), Event Log (`sprk_eventlog`) tables with full schema
- **Field Mapping Framework**: Admin-configurable field mappings between parent and child entities
  - Field Mapping Profile (`sprk_fieldmappingprofile`) and Field Mapping Rule (`sprk_fieldmappingrule`) tables
  - FieldMappingService shared component for mapping execution
  - Support for N:1 relationships (not just Dataverse OOB 1:N)
  - Sync modes: One-time (initial), Manual Refresh (pull), Update Related (push)
  - Type compatibility validation (Strict mode)
  - Cascading mappings (two-pass execution)
  - Bidirectional mapping support
- **Regarding Record Association**: Dual-field strategy with entity-specific lookups + denormalized reference fields
- **PCF Control: AssociationResolver**: Regarding record selection with multi-entity search + field mapping application
- **PCF Control: RegardingLink**: Clickable link rendering in grid views
- **PCF Control: EventFormController**: Event Type validation and field show/hide logic
- **Native Dataverse Forms**: Field Mapping Profile form with Rules subgrid (no PCF needed)
- **PCF Control: UpdateRelatedButton**: Parent form button to push mappings to child records
- **BFF API Endpoints**: CRUD operations for Events via `/api/v1/events`
- **BFF API Endpoints**: Field mapping operations via `/api/v1/field-mappings`
- **Event Log Tracking**: State transition logging (create, complete, cancel, delete)
- **Seed Data**: Initial Event Type records
- **Model-Driven App Configuration**: Forms, views, subgrids for Event management

### Out of Scope

- **Event Set Implementation**: Deferred to future Workflow Engine project
- **Workflow Automation Engine**: Future project
- **Reminder Due Calculation**: Deferred to Workflow Engine (will use `sprk_remindat` field)
- **Advanced Recurring Events**: Future enhancement
- **External System Integrations**: Not in R1
- **Court Rules Engine Integration**: Future project
- **AI-Assisted Event Suggestions**: Future enhancement

### Affected Areas

- `src/client/pcf/AssociationResolver/` - New PCF control for regarding selection + field mapping
- `src/client/pcf/RegardingLink/` - New PCF control for grid display
- `src/client/pcf/EventFormController/` - New PCF control for form validation
- ~~`src/client/pcf/FieldMappingAdmin/`~~ - **Removed**: Native Dataverse forms used instead
- `src/client/pcf/UpdateRelatedButton/` - New PCF control for push mappings from parent
- `src/client/shared/Spaarke.UI.Components/services/FieldMappingService.ts` - Shared mapping service
- `src/server/api/Sprk.Bff.Api/Features/Events/` - Event API endpoints
- `src/server/api/Sprk.Bff.Api/Features/FieldMappings/` - Field mapping API endpoints
- `src/solutions/SpaarkeCore/` - Dataverse customizations

## Requirements

### Functional Requirements

1. **FR-01**: Users can create Events associated with any supported entity type (Matter, Project, Invoice, Analysis, Account, Contact, Work Assignment, Budget)
   - Acceptance: Event form shows entity picker with search, selected entity populates all regarding fields

2. **FR-02**: Users can view Events in entity-specific subgrids (e.g., "All Events for this Matter")
   - Acceptance: Subgrid on Matter form shows only Events where `sprk_regardingmatter` equals current Matter

3. **FR-03**: Users can view all Events across entities in a unified view with clickable regarding links
   - Acceptance: "All Events" view shows RegardingLink PCF displaying entity name as clickable link

4. **FR-04**: Event Type controls which fields are visible/required on the Event form
   - Acceptance: Selecting Event Type with `sprk_requiresbasedate = Yes` shows and requires Base Date field

5. **FR-05**: System logs state transitions for Events (create, complete, cancel, delete)
   - Acceptance: Event Log records created with correct action, timestamp, and user

6. **FR-06**: Users can filter and sort Events by priority, status, due date, and event type
   - Acceptance: Views support column filtering and sorting on these fields

7. **FR-07**: API provides CRUD operations for Events with proper authorization
   - Acceptance: Authenticated users can create/read/update/delete Events via `/api/v1/events`

8. **FR-08**: Regarding record selection clears previously selected entity lookups
   - Acceptance: Changing from Matter A to Project B clears `sprk_regardingmatter` and populates `sprk_regardingproject`

9. **FR-09**: Admins can configure field mappings between parent and child entities
   - Acceptance: Admin creates FieldMappingProfile "Matter to Event" with rules, mappings apply when Event created from Matter

10. **FR-10**: Field mappings apply automatically when child record is created with parent association
    - Acceptance: Create Event for Matter, mapped fields (e.g., Client) auto-populated from Matter

11. **FR-11**: Users can manually refresh child record fields from parent ("Refresh from Parent")
    - Acceptance: Button on Event form re-applies active mappings, updating fields from current parent values

12. **FR-12**: Users can push field updates from parent to all related children ("Update Related")
    - Acceptance: Button on Matter form updates all Events related to that Matter with current mapped field values

13. **FR-13**: Field mapping configuration validates type compatibility
    - Acceptance: Admin cannot save mapping rule where source/target field types are incompatible

14. **FR-14**: Field mappings support cascading (dependent mappings execute in sequence)
    - Acceptance: If Rule A populates Field X, and Rule B uses Field X as source, both execute correctly (two-pass)

15. **FR-15**: Field mappings support bidirectional configuration
    - Acceptance: Profile with "Bidirectional" direction allows both parent→child and child→parent mapping

### Non-Functional Requirements

- **NFR-01**: PCF controls must render within 200ms on initial load
- **NFR-02**: API endpoints must respond within 500ms for single-record operations
- **NFR-03**: All PCF controls must support dark mode via Fluent UI v9 theming
- **NFR-04**: PCF bundle size must remain under 5MB (using platform-provided libraries)
- **NFR-05**: Event Log must not significantly impact Event save performance (<100ms additional)

## Technical Constraints

### Applicable ADRs

- **ADR-001**: Minimal API pattern required - BFF API with BackgroundService, no Azure Functions
- **ADR-006**: PCF over webresources - All new UI must be PCF controls
- **ADR-021**: Fluent UI v9 Design System - Use `@fluentui/react-components`, dark mode required
- **ADR-022**: PCF Platform Libraries - React 16 APIs only (`ReactDOM.render`), platform-provided libraries

### MUST Rules

- ✅ MUST use Minimal API pattern for `/api/v1/events` endpoints
- ✅ MUST use PCF controls for all custom UI (AssociationResolver, RegardingLink, EventFormController)
- ✅ MUST use React 16 APIs (`ReactDOM.render()`, `unmountComponentAtNode()`)
- ✅ MUST import from `@fluentui/react-components` (Fluent UI v9)
- ✅ MUST declare `platform-library` in PCF manifests for React and Fluent
- ✅ MUST wrap PCF React trees in `FluentProvider` with theme
- ✅ MUST use Fluent design tokens for colors, spacing, typography
- ✅ MUST support light, dark, and high-contrast modes
- ✅ MUST keep validation logic in code (PCF/API), not Dataverse Business Rules
- ✅ MUST use endpoint filters for authorization (not global middleware)

### MUST NOT Rules

- ❌ MUST NOT use React 18+ APIs (`createRoot`, `hydrateRoot`, concurrent features)
- ❌ MUST NOT import from `react-dom/client`
- ❌ MUST NOT use Fluent UI v8 (`@fluentui/react`)
- ❌ MUST NOT hard-code colors (use design tokens)
- ❌ MUST NOT create legacy JavaScript webresources
- ❌ MUST NOT use Dataverse Business Rules for validation
- ❌ MUST NOT use Azure Functions
- ❌ MUST NOT bundle React/Fluent in PCF output

### Existing Patterns to Follow

- See `src/client/pcf/` for existing PCF control patterns
- See `src/server/api/Sprk.Bff.Api/Features/` for Minimal API endpoint patterns
- See `.claude/patterns/api/` for detailed API patterns
- See `.claude/patterns/pcf/` for PCF initialization patterns

## Data Model

### Event Table (`sprk_event`)

| Field | Schema Name | Type | Notes |
|-------|-------------|------|-------|
| Event Name | `sprk_eventname` | Single line text | Primary field |
| Description | `sprk_description` | Multiline text | |
| Event Type | `sprk_eventtype_ref` | Lookup | N:1 to sprk_eventtype |
| Status | `statecode` | Choice | Active (0), Inactive (1) |
| Status Reason | `statuscode` | Choice | Draft (1), Planned, Open, On Hold, Completed (2), Cancelled, Deleted |
| Base Date | `sprk_basedate` | Date only | Event date |
| Due Date | `sprk_duedate` | Date only | |
| Completed Date | `sprk_completeddate` | Date only | |
| Priority | `sprk_priority` | Choice | Low (0), Normal (1), High (2), Urgent (3) |
| Source | `sprk_source` | Choice | User (0), System (1), Workflow (2), External (3) |
| Remind At | `sprk_remindat` | DateTime | For future reminder calculation |
| Related Event | `sprk_relatedevent` | Lookup | N:1 self-reference |
| Related Event Type | `sprk_relatedeventtype` | Choice | Reminder (0), Notification (1), Extension (2) |
| Related Event Offset Type | `sprk_relatedeventoffsettype` | Choice | Hours Before (0), Hours After (1), Days Before (2), Days After (3), Fixed (4) |

### Regarding Fields (on Event)

| Field | Schema Name | Type | Relationship |
|-------|-------------|------|--------------|
| Regarding Account | `sprk_regardingaccount` | Lookup | N:1 to account |
| Regarding Analysis | `sprk_regardinganalysis` | Lookup | N:1 to sprk_analysis |
| Regarding Contact | `sprk_regardingcontact` | Lookup | N:1 to contact |
| Regarding Invoice | `sprk_regardinginvoice` | Lookup | N:1 to sprk_invoice |
| Regarding Matter | `sprk_regardingmatter` | Lookup | N:1 to sprk_matter |
| Regarding Project | `sprk_regardingproject` | Lookup | N:1 to sprk_project |
| Regarding Budget | `sprk_regardingbudget` | Lookup | N:1 to sprk_budget |
| Regarding Work Assignment | `sprk_regardingworkassignment` | Lookup | N:1 to sprk_workassignment |
| Regarding Record Id | `sprk_regardingrecordid` | Single line text | Denormalized ID |
| Regarding Record Name | `sprk_regardingrecordname` | Single line text | Denormalized name |
| Regarding Record Type | `sprk_regardingrecordtype` | Choice | Project (0), Matter (1), Invoice (2), Analysis (3), Account (4), Contact (5), Work Assignment (6), Budget (7) |
| Regarding Record URL | `sprk_regardingrecordurl` | URL | Clickable link to parent record |

### Event Type Table (`sprk_eventtype`)

| Field | Schema Name | Type | Notes |
|-------|-------------|------|-------|
| Name | `sprk_name` | Single line text | Primary field |
| Event Code | `sprk_eventcode` | Single line text | |
| Description | `sprk_description` | Multiline text | |
| Status | `statecode` | Choice | Active (0), Inactive (1) |
| Requires Due Date | `sprk_requiresduedate` | Choice | No (0), Yes (1) |
| Requires Base Date | `sprk_requiresbasedate` | Choice | No (0), Yes (1) |

### Event Log Table (`sprk_eventlog`)

| Field | Schema Name | Type | Notes |
|-------|-------------|------|-------|
| Event Log Name | `sprk_eventlogname` | Single line text | Primary field (auto-generated) |
| Event | `sprk_event` | Lookup | N:1 to sprk_event |
| Action | `sprk_action` | Choice | Created (0), Updated (1), Completed (2), Cancelled (3), Deleted (4) |
| Description | `sprk_description` | Multiline text | Details of change |

## Field Mapping Framework

### Overview

The Field Mapping Framework provides admin-configurable field-to-field mappings between parent and child entities. This replaces Dataverse's limited OOB relationship mappings which only support 1:N relationships and only work when creating child from parent form.

**Key Capabilities:**
- Supports N:1 and N:N relationships (not just 1:N)
- Works regardless of how child record is created
- Three sync modes: One-time, Manual Refresh (pull), Update Related (push)
- Type compatibility validation at configuration time
- Cascading mappings (two-pass execution)
- Bidirectional mapping support

**Note on OOB Dataverse Mappings:** Dataverse automatically populates certain fields (Owner, Business Unit, Created By, etc.) that cannot be disabled. Our custom mappings layer on top and will override OOB mappings for fields we configure.

### Field Mapping Profile Table (`sprk_fieldmappingprofile`)

**Updated Feb 2026**: Source/Target Entity changed to Record Type lookups for admin usability.

| Field | Schema Name | Type | Notes |
|-------|-------------|------|-------|
| Profile Name | `sprk_name` | Single line text | Primary field |
| Source Record Type | `sprk_sourcerecordtype` | Lookup | N:1 to `sprk_recordtype` |
| Target Record Type | `sprk_targetrecordtype` | Lookup | N:1 to `sprk_recordtype` |
| Mapping Direction | `sprk_mappingdirection` | Choice | Parent to Child (0), Child to Parent (1), Bidirectional (2) |
| Sync Mode | `sprk_syncmode` | Choice | One-time (0), Manual Refresh (1) |
| Is Active | (statecode) | State | Active/Inactive via standard state |
| Description | `sprk_description` | Multiline text | Admin notes |

**Note**: The `sprk_recordtype` entity contains `sprk_entitylogicalname` field used by FieldMappingService to resolve entity names.

### Field Mapping Rule Table (`sprk_fieldmappingrule`)

| Field | Schema Name | Type | Notes |
|-------|-------------|------|-------|
| Rule Name | `sprk_name` | Single line text | Primary field |
| Mapping Profile | `sprk_fieldmappingprofile` | Lookup | N:1 to profile (required) |
| Source Field | `sprk_sourcefield` | Single line text | Schema name (e.g., `sprk_matterid`) |
| Target Field | `sprk_targetfield` | Single line text | Schema name (e.g., `sprk_regardingrecordid`) |
| Mapping Type | `sprk_mappingtype` | Choice | Direct Copy (0), Constant (1), Transform (2 - future) |
| Default Value | `sprk_defaultvalue` | Single line text | Used for Constant mapping type |
| Is Required | `sprk_isrequired` | Yes/No | Fail if source is empty |
| Execution Order | `sprk_executionorder` | Whole number | Sequence for dependent mappings |
| Is Active | (statecode) | State | Active/Inactive via standard state |

**Note**: FieldMappingAdmin PCF was removed in favor of native Dataverse forms with subgrids. Profile form shows related Rules in a subgrid.

### Type Compatibility Matrix (Strict Mode)

| Source Type | Compatible Target Types | Notes |
|-------------|------------------------|-------|
| Lookup | Lookup (same entity), Text | Text receives record name |
| Text | Text, Memo | Direct copy |
| Memo | Text, Memo | May truncate for Text |
| OptionSet | OptionSet (same options), Text | Text receives option label |
| Number | Number, Text | Text receives formatted value |
| DateTime | DateTime, Text | Text receives formatted value |
| Boolean | Boolean, Text | Text receives Yes/No |

**Incompatible (blocked in Strict mode):**
- Text → Lookup (requires resolution logic - future)
- Text → OptionSet (requires label matching - future)
- Text → Number (requires parsing - future)

### Sync Mode Behaviors

| Mode | Trigger | Behavior |
|------|---------|----------|
| **One-time** | Child record creation | Apply mappings once at creation, no further sync |
| **Manual Refresh** | User clicks "Refresh from Parent" on child form | Re-fetch parent values, re-apply all active rules |
| **Update Related** | User clicks "Update Related" on parent form | Find all children with this profile, re-apply mappings to each |

### Execution Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  PULL: Child Form (Event)                                                   │
│                                                                             │
│  ┌──────────────────────┐    ┌──────────────────────────────────────────┐  │
│  │  AssociationResolver │───▶│  FieldMappingService (shared component)  │  │
│  │  PCF                 │    │                                          │  │
│  └──────────────────────┘    │  1. Query Profile (source→target)        │  │
│           │                  │  2. Query Active Rules                   │  │
│           │ On record        │  3. Fetch Source Record Values           │  │
│           │ selection        │  4. Validate Type Compatibility          │  │
│           │                  │  5. Apply to Form (Pass 1)               │  │
│           ▼                  │  6. Apply Cascading Rules (Pass 2)       │  │
│  ┌──────────────────────┐    └──────────────────────────────────────────┘  │
│  │  "Refresh from       │                     │                            │
│  │   Parent" Button     │─────────────────────┘                            │
│  └──────────────────────┘                                                  │
└─────────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────────┐
│  PUSH: Parent Form (Matter)                                                 │
│                                                                             │
│  ┌──────────────────────┐    ┌──────────────────────────────────────────┐  │
│  │  UpdateRelatedButton │───▶│  BFF API: POST /api/v1/field-mappings/push│  │
│  │  PCF                 │    │                                          │  │
│  └──────────────────────┘    │  1. Find Profile (source entity)         │  │
│                              │  2. Query Child Records                  │  │
│                              │  3. For Each Child:                      │  │
│                              │     - Fetch Parent Values                │  │
│                              │     - Apply Mappings                     │  │
│                              │     - Update Child Record                │  │
│                              │  4. Return Summary (updated count)       │  │
│                              └──────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────────┘
```

### FieldMappingService Interface

```typescript
// Shared component in @spaarke/ui-components

export interface FieldMappingProfile {
  profileId: string;
  sourceEntity: string;
  targetEntity: string;
  mappingDirection: MappingDirection;
  syncMode: SyncMode;
  rules: FieldMappingRule[];
}

export interface FieldMappingRule {
  ruleId: string;
  sourceField: string;
  sourceFieldType: FieldType;
  targetField: string;
  targetFieldType: FieldType;
  compatibilityMode: CompatibilityMode;
  isRequired: boolean;
  defaultValue?: string;
  isCascadingSource: boolean;
  executionOrder: number;
}

export interface MappingResult {
  success: boolean;
  appliedRules: number;
  skippedRules: number;
  errors: MappingError[];
}

export class FieldMappingService {
  // Get active profile for entity pair
  async getProfile(
    sourceEntity: string,
    targetEntity: string
  ): Promise<FieldMappingProfile | null>;

  // Get all profiles where entity is source (for "Update Related")
  async getProfilesForSource(
    sourceEntity: string
  ): Promise<FieldMappingProfile[]>;

  // Fetch source record field values
  async getSourceValues(
    sourceEntity: string,
    recordId: string,
    fields: string[]
  ): Promise<Record<string, any>>;

  // Validate rule type compatibility (config-time)
  validateMapping(rule: FieldMappingRule): ValidationResult;

  // Apply mappings to form context (runtime - PULL)
  async applyMappings(
    context: ComponentFramework.Context<IInputs>,
    profile: FieldMappingProfile,
    sourceRecordId: string
  ): Promise<MappingResult>;

  // Check if cascading rules need execution
  private getCascadingRules(
    profile: FieldMappingProfile,
    appliedTargetFields: string[]
  ): FieldMappingRule[];
}
```

## PCF Control Specifications

### 1. AssociationResolver PCF

**Purpose**: Provides unified regarding record selection on Event forms

**Manifest Properties**:
- `regardingRecordType` (Bound, OptionSet) - `sprk_regardingrecordtype`
- `regardingRecordId` (Bound, SingleLine.Text) - `sprk_regardingrecordid`
- `regardingRecordName` (Bound, SingleLine.Text) - `sprk_regardingrecordname`

**Entity Configuration**:
```typescript
const ENTITY_CONFIGS: EntityConfig[] = [
  { logicalName: "sprk_matter", displayName: "Matter", regardingField: "sprk_regardingmatter", regardingRecordTypeValue: 1 },
  { logicalName: "sprk_project", displayName: "Project", regardingField: "sprk_regardingproject", regardingRecordTypeValue: 0 },
  { logicalName: "sprk_invoice", displayName: "Invoice", regardingField: "sprk_regardinginvoice", regardingRecordTypeValue: 2 },
  { logicalName: "sprk_analysis", displayName: "Analysis", regardingField: "sprk_regardinganalysis", regardingRecordTypeValue: 3 },
  { logicalName: "account", displayName: "Account", regardingField: "sprk_regardingaccount", regardingRecordTypeValue: 4 },
  { logicalName: "contact", displayName: "Contact", regardingField: "sprk_regardingcontact", regardingRecordTypeValue: 5 },
  { logicalName: "sprk_workassignment", displayName: "Work Assignment", regardingField: "sprk_regardingworkassignment", regardingRecordTypeValue: 6 },
  { logicalName: "sprk_budget", displayName: "Budget", regardingField: "sprk_regardingbudget", regardingRecordTypeValue: 7 }
];
```

**Behavior**:
1. Display dropdown for entity type selection
2. Show search input with type-ahead (min 3 chars)
3. Query Dataverse via WebAPI for matching records
4. On selection: populate all regarding fields + clear other entity lookups
5. **Field Mapping Integration**: After populating regarding fields:
   - Call `FieldMappingService.getProfile(selectedEntity, "sprk_event")`
   - If profile exists and is active, call `FieldMappingService.applyMappings()`
   - Show toast notification: "X fields populated from [Entity Name]"
6. **Refresh from Parent Button**: If profile.syncMode allows manual refresh:
   - Show "Refresh from Parent" button
   - On click: re-apply mappings from current parent record

### 2. RegardingLink PCF

**Purpose**: Renders clickable link to regarding record in grid views

**Manifest Properties**:
- `regardingRecordType` (Bound, OptionSet) - `sprk_regardingrecordtype`
- `regardingRecordId` (Bound, SingleLine.Text) - `sprk_regardingrecordid`
- `regardingRecordName` (Bound, SingleLine.Text) - `sprk_regardingrecordname`

**Behavior**:
1. Read entity type, ID, and name from bound properties
2. Render as Fluent UI Link component
3. On click: navigate to record using `Xrm.Navigation.navigateTo`

### 3. EventFormController PCF

**Purpose**: Validates Event Type requirements and controls field visibility

**Manifest Properties**:
- `eventType` (Bound, Lookup) - `sprk_eventtype_ref`
- `baseDate` (Bound, DateAndTime.DateOnly) - `sprk_basedate`
- `dueDate` (Bound, DateAndTime.DateOnly) - `sprk_duedate`
- `controlMode` (Input, Enum) - "validation" | "display"

**Behavior**:
1. On Event Type change: fetch Event Type record to get `sprk_requiresbasedate`, `sprk_requiresduedate`
2. Show/hide Base Date and Due Date fields based on requirements
3. Validate required fields before save (block save if missing)
4. No Dataverse Business Rules - all logic in TypeScript

### 4. ~~FieldMappingAdmin PCF~~ (REMOVED)

> **Note (Feb 2026)**: This PCF was removed in favor of native Dataverse forms. Admin configuration is done via:
> - Field Mapping Profile form with standard fields
> - Field Mapping Rule subgrid on the Profile form
> - Standard Dataverse validation via Business Rules (if needed)
>
> The complexity of a custom PCF was not justified when native forms work well for admin configuration.

### 5. UpdateRelatedButton PCF

**Purpose**: Push field mappings from parent record to all related child records

**Manifest Properties**:
- `entityLogicalName` (Input, SingleLine.Text) - Current entity logical name
- `recordId` (Input, SingleLine.Text) - Current record GUID (can use `{Id}` token)
- `buttonLabel` (Input, SingleLine.Text) - Custom button text (default: "Update Related")
- `targetEntity` (Input, SingleLine.Text) - Optional: specific target entity, or blank for all

**Behavior**:
1. On load: check if active FieldMappingProfiles exist for the current entity's Record Type
2. If no profiles: hide button or show disabled with tooltip "No field mappings configured"
3. On click:
   - Show confirmation dialog: "Update all related [Target Entity] records with current values?"
   - Call BFF API: `POST /api/v1/field-mappings/push`
   - Show progress indicator during API call
   - Show result toast: "Updated X of Y [Entity] records" or error message

**API Request**:
```typescript
interface PushMappingsRequest {
  sourceEntity: string;
  sourceRecordId: string;
  targetEntity?: string;  // Optional - if omitted, push to all configured targets
}

interface PushMappingsResponse {
  success: boolean;
  targetEntity: string;
  totalRecords: number;
  updatedRecords: number;
  failedRecords: number;
  errors: { recordId: string; error: string }[];
}
```

## API Endpoints

### Base Path: `/api/v1/events`

| Method | Path | Purpose | Authorization |
|--------|------|---------|---------------|
| GET | `/api/v1/events` | List events with filtering | Authenticated |
| GET | `/api/v1/events/{id}` | Get single event | Authenticated |
| POST | `/api/v1/events` | Create event | Authenticated |
| PUT | `/api/v1/events/{id}` | Update event | Authenticated |
| DELETE | `/api/v1/events/{id}` | Delete event | Authenticated |
| POST | `/api/v1/events/{id}/complete` | Mark event complete | Authenticated |
| POST | `/api/v1/events/{id}/cancel` | Cancel event | Authenticated |

### Query Parameters (GET /api/v1/events)

- `regardingType` - Filter by entity type (0-7)
- `regardingId` - Filter by specific entity ID
- `statusCode` - Filter by status
- `eventTypeId` - Filter by event type
- `priority` - Filter by priority
- `dueDateFrom`, `dueDateTo` - Date range filter

### Base Path: `/api/v1/field-mappings`

| Method | Path | Purpose | Authorization |
|--------|------|---------|---------------|
| GET | `/api/v1/field-mappings/profiles` | List all active profiles | Authenticated |
| GET | `/api/v1/field-mappings/profiles/{sourceEntity}/{targetEntity}` | Get profile for entity pair | Authenticated |
| POST | `/api/v1/field-mappings/push` | Push mappings from parent to children | Authenticated |
| POST | `/api/v1/field-mappings/validate` | Validate a mapping rule configuration | Authenticated |

### Push Mappings Request/Response

**Request**: `POST /api/v1/field-mappings/push`
```json
{
  "sourceEntity": "sprk_matter",
  "sourceRecordId": "00000000-0000-0000-0000-000000000000",
  "targetEntity": "sprk_event"
}
```

**Response**:
```json
{
  "success": true,
  "targetEntity": "sprk_event",
  "totalRecords": 15,
  "updatedRecords": 14,
  "failedRecords": 1,
  "errors": [
    {
      "recordId": "11111111-1111-1111-1111-111111111111",
      "error": "Record is read-only"
    }
  ]
}
```

### Validate Mapping Request/Response

**Request**: `POST /api/v1/field-mappings/validate`
```json
{
  "sourceEntity": "sprk_matter",
  "sourceField": "sprk_client",
  "sourceFieldType": 1,
  "targetEntity": "sprk_event",
  "targetField": "sprk_regardingaccount",
  "targetFieldType": 1,
  "compatibilityMode": 0
}
```

**Response**:
```json
{
  "isValid": true,
  "compatibilityLevel": "exact",
  "warnings": [],
  "errors": []
}
```

## Success Criteria

### Event Management

1. [ ] **SC-01**: AssociationResolver PCF allows selection from all 8 entity types - Verify: Manual test on Event form
2. [ ] **SC-02**: RegardingLink PCF displays clickable links in All Events view - Verify: Navigate from grid to record
3. [ ] **SC-03**: EventFormController shows/hides fields based on Event Type - Verify: Select Event Type with `requiresbasedate = Yes`
4. [ ] **SC-04**: Entity subgrids show only relevant Events - Verify: Matter subgrid shows only Matter events
5. [ ] **SC-05**: Event Log captures state transitions - Verify: Complete event, check log record created
6. [ ] **SC-06**: Event API endpoints return proper responses - Verify: Integration tests pass

### Field Mapping Framework

7. [ ] **SC-07**: Admin can create Field Mapping Profile and Rules - Verify: Create "Matter to Event" profile with 3 rules
8. [ ] **SC-08**: Field mappings apply on child record creation - Verify: Create Event for Matter, mapped fields auto-populated
9. [ ] **SC-09**: "Refresh from Parent" button re-applies mappings - Verify: Change Matter field, click refresh on Event, field updates
10. [ ] **SC-10**: "Update Related" button pushes to all children - Verify: Click on Matter, all related Events updated
11. [ ] **SC-11**: Type compatibility validation blocks incompatible rules - Verify: Try to save Text→Lookup in Strict mode, blocked
12. [ ] **SC-12**: Cascading mappings execute correctly - Verify: Rule A populates Field X, Rule B uses Field X as source, both work
13. [ ] **SC-13**: Push API returns accurate counts - Verify: API response matches actual updated records

### General

14. [ ] **SC-14**: All PCF controls support dark mode - Verify: Toggle theme in Dataverse
15. [ ] **SC-15**: PCF bundles use platform libraries - Verify: Bundle size < 1MB each

## Dependencies

### Prerequisites

- Dataverse tables created: `sprk_event`, `sprk_eventtype`, `sprk_eventlog`
- Dataverse tables created: `sprk_fieldmappingprofile`, `sprk_fieldmappingrule`
- Seed Event Type records created
- Entity-specific lookup relationships configured
- Model-driven app forms configured with PCF control placeholders
- Field Mapping admin forms configured

### External Dependencies

- Dataverse WebAPI for PCF queries
- BFF API authentication infrastructure
- Existing entity tables (Matter, Project, Invoice, etc.)

## Owner Clarifications

*Answers captured during design-to-spec interview:*

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| PCF Approach | Single PCF with tabs or separate controls? | Two separate PCFs: AssociationResolver + EventFormController | Clear separation of concerns |
| Validation | Dataverse Business Rules or code-based? | Code-based only, no Business Rules | All validation in EventFormController PCF |
| Event Log | State changes only or all field changes? | State transitions only (create, complete, cancel, delete) | Simpler implementation, no field diff tracking |
| API Pattern | Separate service or extend BFF? | Extend BFF API consistent with existing patterns | `/api/v1/events` endpoints in Sprk.Bff.Api |
| Reminder Calculation | Implement reminder offset logic? | Defer to future Workflow Engine project | Use `sprk_remindat` field, no calculation now |
| Event Set | Implement event grouping? | Defer to future Workflow Engine project | Table exists but not used in R1 |
| Field Mapping Scope | Separate project or part of Events R1? | Part of Events R1 - critical for event creation UX | Field Mapping Framework in scope |
| Field Mapping Implementation | Plugins, workflows, or code? | Code only (PCF + API), no plugins or workflows | FieldMappingService + BFF API endpoints |
| Type Compatibility | Allow Text→Lookup with resolution? | Strict mode only for R1, Resolve mode future | Block incompatible mappings at config time |
| Cascading Mappings | Include dependent mapping execution? | Yes, two-pass execution (simple approach) | Rules can depend on other rule outputs |
| Reverse Mappings | Support child→parent direction? | Yes, bidirectional option on profile | Profile.mappingDirection choice field |
| Change Monitoring | How to sync after initial mapping? | Three options: One-time, Manual Refresh (pull), Update Related (push) | No automatic/scheduled sync in R1 |
| Push Direction | Child refresh only or parent push too? | Both: "Refresh from Parent" on child + "Update Related" on parent | Two PCF controls, one API endpoint |
| OOB Mappings | Use Dataverse relationship mappings? | No - custom framework replaces OOB (which is 1:N only) | OOB fields (Owner, etc.) still apply, we layer on top |

## Assumptions

*Proceeding with these assumptions (owner did not specify):*

- **Concurrent editing**: Assuming last-write-wins (no optimistic concurrency) - affects API design
- **Bulk operations**: Assuming single-record operations only in R1 - affects API batch endpoints
- **Audit logging**: Assuming Dataverse native audit sufficient - affects custom logging scope
- **Permissions**: Assuming Dataverse security roles govern access - affects API authorization
- **Push batch size**: Assuming "Update Related" handles up to 500 child records per call - affects API pagination
- **Lookup entity validation**: Assuming Lookup→Lookup mappings require same target entity (sprk_client→sprk_regardingaccount both reference account) - affects validation logic
- **Mapping admin access**: Assuming Field Mapping Profile/Rule admin restricted to System Administrators - affects security roles

## Unresolved Questions

*No blocking questions remain - all critical items clarified during interview.*

---

*AI-optimized specification. Original: design.md*
