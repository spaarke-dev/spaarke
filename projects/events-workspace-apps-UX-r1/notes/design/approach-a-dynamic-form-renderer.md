# Approach A: Dynamic Form Renderer for Event Detail Side Pane

> **Date**: 2026-02-19
> **Status**: Design
> **Branch**: work/events-workspace-apps-UX-r1
> **Supersedes**: event-detail-sidepane-rebuild.md (Approach B — hide from defaults)

---

## Executive Summary

Replace the hardcoded section components in EventDetailSidePane with a **dynamic form renderer** driven by JSON configuration stored on each Event Type record (`sprk_fieldconfigjson`). The JSON IS the form definition — it declares which sections exist, which fields appear, and in what order. If it's not in the JSON, it doesn't render.

Additionally, add two **fixed sections** at the bottom of every side pane:
- **Memo** — related `sprk_memo` entity (one per Event, + Add Memo)
- **To Do** — related `sprk_eventtodo` entity (one per Event, + Add To Do)

---

## Architecture Overview

```
┌─────────────────────────────────────────┐
│ FIXED CHROME (not config-driven)        │
│ ┌─────────────────────────────────────┐ │
│ │ HeaderSection                       │ │
│ │ Name (inline edit) │ Type Badge     │ │
│ │ Description (multiline)             │ │
│ │ Status (statcode badge)             │ │
│ │ Close [X]                           │ │
│ └─────────────────────────────────────┘ │
│ ┌─────────────────────────────────────┐ │
│ │ StatusReasonBar                     │ │
│ │ Segmented buttons (always present)  │ │
│ └─────────────────────────────────────┘ │
├─────────────────────────────────────────┤
│ CONFIG-DRIVEN SECTIONS (from JSON)      │
│ ┌─────────────────────────────────────┐ │
│ │ Section: "Status & Dates"       [▼] │ │
│ │  Due Date        [date picker]      │ │
│ │  Final Due Date  [date picker]      │ │
│ │  Completed Date  [date picker]      │ │
│ │  Completed By    [lookup: contact]  │ │
│ └─────────────────────────────────────┘ │
│ ┌─────────────────────────────────────┐ │
│ │ Section: "Priority"             [▼] │ │
│ │  Priority        [choice dropdown]  │ │
│ │  Effort          [choice dropdown]  │ │
│ └─────────────────────────────────────┘ │
│ ┌─────────────────────────────────────┐ │
│ │ Section: "Assigned"             [▼] │ │
│ │  Assigned To     [lookup: contact]  │ │
│ │  Assigned Atty   [lookup: contact]  │ │
│ │  Assigned Para   [lookup: contact]  │ │
│ │  Owner           [lookup: user]     │ │
│ └─────────────────────────────────────┘ │
├─────────────────────────────────────────┤
│ FIXED SECTIONS (not config-driven)      │
│ ┌─────────────────────────────────────┐ │
│ │ MemoSection                     [▼] │ │
│ │  memo text (editable)               │ │
│ │  OR: [+ Add Memo]                   │ │
│ └─────────────────────────────────────┘ │
│ ┌─────────────────────────────────────┐ │
│ │ TodoSection                     [▼] │ │
│ │  ☐ To-do name    Due: Feb 20       │ │
│ │    Assigned: J. Smith               │ │
│ │  OR: [+ Add To Do]                  │ │
│ └─────────────────────────────────────┘ │
├─────────────────────────────────────────┤
│ FIXED CHROME                            │
│ │ Footer (Save │ Messages │ Version)  │ │
└─────────────────────────────────────────┘
```

### What's Fixed vs Config-Driven

| Area | Driven By | Rationale |
|------|-----------|-----------|
| HeaderSection (Name, Description, Status badge, Close) | Fixed code | Present on every Event Type, same layout |
| StatusReasonBar (segmented buttons) | Fixed code | Same status options across all Event Types |
| Config sections (Status/Dates, Priority, Assigned, etc.) | **JSON config** | Varies per Event Type |
| MemoSection | Fixed code | Present on every Event Type, same behavior |
| TodoSection | Fixed code | Present on every Event Type, same behavior |
| Footer (Save, messages) | Fixed code | Same across all Event Types |

---

## JSON Schema Specification

### Schema Definition

```typescript
/**
 * Root configuration stored in sprk_eventtype.sprk_fieldconfigjson
 */
interface IFormConfig {
  /** Schema version for forward compatibility */
  version: 1;
  /** Ordered array of sections to render */
  sections: ISectionConfig[];
}

/**
 * A section in the side pane form
 */
interface ISectionConfig {
  /** Unique section identifier */
  id: string;
  /** Display title for the section header */
  title: string;
  /** Whether section is collapsible (default: true) */
  collapsible?: boolean;
  /** Default expanded state (default: true) */
  defaultExpanded?: boolean;
  /** Ordered array of fields in this section */
  fields: IFieldConfig[];
}

/**
 * A field within a section
 */
interface IFieldConfig {
  /** Dataverse logical name (e.g., "sprk_duedate") */
  name: string;
  /** Field type — determines which renderer component to use */
  type: "text" | "date" | "datetime" | "choice" | "lookup" | "url" | "multiline";
  /** Display label */
  label: string;
  /** Whether field is required (default: false) */
  required?: boolean;
  /** Whether field is read-only regardless of record permissions (default: false) */
  readOnly?: boolean;
  /** Lookup targets — required when type is "lookup" */
  targets?: string[];
}
```

### Design Decisions

**Why no `options` array for choice fields?**
Choice field options (optionset values + labels) are fetched from Dataverse entity metadata at runtime via `Xrm.Utility.getEntityMetadata()`. This means:
- Single source of truth (Dataverse optionset definition)
- JSON stays clean — just `type: "choice"` and the renderer fetches labels
- Adding/removing optionset values in Dataverse automatically reflects in the side pane

**Why `version: 1`?**
Forward compatibility. If we change the schema shape in the future, the renderer can detect the version and handle migration.

**Why ordered arrays?**
The order of sections and fields in the JSON IS the render order. Moving a field in the JSON moves it in the UI. No separate "order" property needed.

**Fallback when JSON is missing/empty:**
When an Event Type has no `sprk_fieldconfigjson` value (null or empty), the renderer uses a **generic fallback config** (defined in code, not in Dataverse). This shows a minimal useful form.

---

## Field Renderer Components

Each `type` value maps to a specific renderer component:

| Type | Component | Renders As | Dataverse Field Types |
|------|-----------|-----------|----------------------|
| `text` | `TextFieldRenderer` | Fluent UI `Input` | Single line text, auto-number |
| `multiline` | `MultilineFieldRenderer` | Fluent UI `Textarea` | Multi-line text (sprk_description) |
| `date` | `DateFieldRenderer` | Fluent UI `DatePicker` | Date Only fields |
| `datetime` | `DateTimeFieldRenderer` | `DatePicker` + `Input[type=time]` | Date and Time fields |
| `choice` | `ChoiceFieldRenderer` | Fluent UI `Dropdown` | Optionset / Choice fields |
| `lookup` | `LookupFieldRenderer` | Persona display + Search button | Lookup fields (opens `Xrm.Utility.lookupObjects`) |
| `url` | `UrlFieldRenderer` | Fluent UI `Input` + clickable `Link` | URL fields |

### Generic FieldRenderer (dispatcher)

```tsx
interface FieldRendererProps {
  config: IFieldConfig;
  value: unknown;
  onChange: (fieldName: string, value: unknown) => void;
  disabled: boolean;
  metadata?: IFieldMetadata; // Optionset labels, etc.
}

const FieldRenderer: React.FC<FieldRendererProps> = ({ config, ...props }) => {
  switch (config.type) {
    case "text":      return <TextFieldRenderer {...props} />;
    case "multiline": return <MultilineFieldRenderer {...props} />;
    case "date":      return <DateFieldRenderer {...props} />;
    case "datetime":  return <DateTimeFieldRenderer {...props} />;
    case "choice":    return <ChoiceFieldRenderer {...props} metadata={props.metadata} />;
    case "lookup":    return <LookupFieldRenderer {...props} targets={config.targets} />;
    case "url":       return <UrlFieldRenderer {...props} />;
    default:          return <TextFieldRenderer {...props} />; // Safe fallback
  }
};
```

### SectionRenderer

```tsx
interface SectionRendererProps {
  config: ISectionConfig;
  values: Record<string, unknown>;
  onChange: (fieldName: string, value: unknown) => void;
  disabled: boolean;
  metadata: Map<string, IFieldMetadata>;
}

const SectionRenderer: React.FC<SectionRendererProps> = ({
  config, values, onChange, disabled, metadata,
}) => {
  return (
    <CollapsibleSection
      title={config.title}
      defaultExpanded={config.defaultExpanded ?? true}
      collapsible={config.collapsible ?? true}
    >
      {config.fields.map((field) => (
        <FieldRenderer
          key={field.name}
          config={field}
          value={values[field.name]}
          onChange={onChange}
          disabled={disabled || field.readOnly}
          metadata={metadata.get(field.name)}
        />
      ))}
    </CollapsibleSection>
  );
};
```

### FormRenderer (top-level)

```tsx
interface FormRendererProps {
  config: IFormConfig;
  values: Record<string, unknown>;
  onChange: (fieldName: string, value: unknown) => void;
  disabled: boolean;
}

const FormRenderer: React.FC<FormRendererProps> = ({
  config, values, onChange, disabled,
}) => {
  // Fetch field metadata (optionset labels) on mount
  const metadata = useFieldMetadata(config);

  return (
    <>
      {config.sections.map((section) => (
        <SectionRenderer
          key={section.id}
          config={section}
          values={values}
          onChange={onChange}
          disabled={disabled}
          metadata={metadata}
        />
      ))}
    </>
  );
};
```

---

## Lookup Implementation

### Using Xrm.Utility.lookupObjects

```typescript
/**
 * Open the standard Dataverse lookup dialog
 * Works from web resources via window.parent.Xrm
 */
async function openLookupDialog(
  targets: string[],
  currentValue?: { id: string; name: string; entityType: string }
): Promise<{ id: string; name: string; entityType: string } | null> {
  const xrm = getXrm();
  if (!xrm?.Utility?.lookupObjects) {
    console.error("[LookupField] Xrm.Utility.lookupObjects not available");
    return null;
  }

  const lookupOptions = {
    defaultEntityType: targets[0],
    entityTypes: targets,
    allowMultiSelect: false,
    // defaultViewId could be added per-field if needed
  };

  try {
    const result = await xrm.Utility.lookupObjects(lookupOptions);
    if (result && result.length > 0) {
      return {
        id: result[0].id.replace(/[{}]/g, ""),
        name: result[0].name,
        entityType: result[0].entityType,
      };
    }
    return null; // User cancelled
  } catch (error) {
    console.error("[LookupField] Lookup dialog error:", error);
    return null;
  }
}
```

### LookupFieldRenderer Component

Displays as:
- **Has value**: Persona/text display + clear button + change button
- **No value**: "Not set" text + Search button
- **Read-only**: Persona/text display only (no buttons)

The lookup stores both the GUID and formatted name in the record values:
- `_sprk_assignedto_value` = GUID
- `_sprk_assignedto_value@OData.Community.Display.V1.FormattedValue` = display name

For saving (PATCH), lookup fields use the `@odata.bind` format:
```json
{ "sprk_assignedto@odata.bind": "/contacts(guid-here)" }
```

---

## Metadata Hook: useFieldMetadata

Fetches optionset labels and lookup display names from Dataverse metadata. Called once when the form config loads.

```typescript
/**
 * Fetch field metadata (optionset labels, etc.) for all choice fields in the config
 */
function useFieldMetadata(config: IFormConfig): Map<string, IFieldMetadata> {
  // 1. Extract all unique field names where type === "choice"
  // 2. Call Xrm.Utility.getEntityMetadata("sprk_event", fieldNames)
  // 3. Parse OptionSet.Options into { value: number, label: string }[]
  // 4. Return Map<fieldName, { options: { value, label }[] }>
  // 5. Cache result (metadata doesn't change during session)
}
```

---

## Memo Section (Fixed — Not Config-Driven)

### Entity: sprk_memo (TBD — confirm entity name)

| Field | Type | Purpose |
|-------|------|---------|
| sprk_name | text | Memo title (auto-generated or optional) |
| sprk_text | multiline | Memo content |
| sprk_regardingevent | lookup:sprk_event | Parent event |
| ownerid | lookup:user | Who created/owns the memo |
| createdon | datetime | When created (system field) |

### Behavior

1. **On Event load**: Query `sprk_memo?$filter=_sprk_regardingevent_value eq {eventId}&$top=1&$orderby=createdon desc`
2. **Memo exists**: Show memo text in editable textarea, auto-save on blur or with Event save
3. **No memo**: Show `[+ Add Memo]` button
4. **Add Memo**: Click creates a new `sprk_memo` record linked to the Event, then shows the editable textarea
5. **Save**: Memo is saved independently from Event fields (separate `Xrm.WebApi.updateRecord` call)

### UI Layout

```
┌─────────────────────────────────────┐
│ 📝 Memo                        [▼] │
│ ┌─────────────────────────────────┐ │
│ │ This matter requires special    │ │
│ │ attention due to the complex    │ │
│ │ jurisdictional issues...        │ │
│ └─────────────────────────────────┘ │
│ Saved · 2 min ago                   │
└─────────────────────────────────────┘

OR (no memo):

┌─────────────────────────────────────┐
│ 📝 Memo                        [▼] │
│                                     │
│ No memo for this event              │
│ [+ Add Memo]                        │
│                                     │
└─────────────────────────────────────┘
```

---

## To Do Section (Fixed — Not Config-Driven)

### Entity: sprk_eventtodo (already created)

| Field | Logical Name | Type | Purpose |
|-------|-------------|------|---------|
| Name | sprk_name | text | To-do title |
| Regarding Event | sprk_regardingevent | lookup:sprk_event | Parent event |
| Assigned To | sprk_assignedto | lookup:contact | Who needs to do it |
| Due Date | sprk_duedate | date | When it's due |
| Status | statecode | status | Active (0) / Inactive (1) |
| Status Reason | statuscode | status | Open / Completed |
| Graph Task ID | sprk_graphtaskid | text | Microsoft To Do task ID (future sync) |
| Graph Synced At | sprk_graphsyncedat | datetime | Last sync timestamp (future) |

### Behavior

1. **On Event load**: Query `sprk_eventtodo?$filter=_sprk_regardingevent_value eq {eventId}&$top=1&$orderby=createdon desc`
2. **To-do exists**: Show to-do card with checkbox, name, assigned to, due date
3. **No to-do**: Show `[+ Add To Do]` button
4. **Add To Do**: Click creates a new `sprk_eventtodo` record linked to the Event
5. **Toggle complete**: Checkbox toggles `statecode` (Active → Inactive)
6. **Edit fields**: Inline edit of name, assigned to (lookup), due date
7. **Save**: To-do is saved independently from Event fields

### UI Layout

```
┌─────────────────────────────────────┐
│ ☑ To Do                        [▼] │
│ ┌─────────────────────────────────┐ │
│ │ ☐ Review contract for errors   │ │
│ │   📅 Due: Feb 20, 2026         │ │
│ │   👤 Jonathan Smith             │ │
│ └─────────────────────────────────┘ │
└─────────────────────────────────────┘

OR (no to-do):

┌─────────────────────────────────────┐
│ ☑ To Do                        [▼] │
│                                     │
│ No to-do for this event             │
│ [+ Add To Do]                       │
│                                     │
└─────────────────────────────────────┘

OR (completed):

┌─────────────────────────────────────┐
│ ☑ To Do                        [▼] │
│ ┌─────────────────────────────────┐ │
│ │ ☑ Review contract for errors   │ │
│ │   ✓ Completed Feb 18           │ │
│ │   👤 Jonathan Smith             │ │
│ └─────────────────────────────────┘ │
└─────────────────────────────────────┘
```

---

## JSON Configs per Event Type

### Generic Fallback (no JSON or unknown Event Type)

```json
{
  "version": 1,
  "sections": [
    {
      "id": "dates",
      "title": "Dates",
      "fields": [
        { "name": "sprk_duedate", "type": "date", "label": "Due Date" },
        { "name": "sprk_completeddate", "type": "date", "label": "Completed Date" },
        { "name": "sprk_completedby", "type": "lookup", "label": "Completed By", "targets": ["contact"] }
      ]
    },
    {
      "id": "assigned",
      "title": "Assigned",
      "fields": [
        { "name": "sprk_assignedto", "type": "lookup", "label": "Assigned To", "targets": ["contact"] },
        { "name": "sprk_assignedattorney", "type": "lookup", "label": "Assigned Attorney", "targets": ["contact"] },
        { "name": "sprk_assignedparalegal", "type": "lookup", "label": "Assigned Paralegal", "targets": ["contact"] },
        { "name": "ownerid", "type": "lookup", "label": "Owner", "targets": ["systemuser", "team"], "readOnly": true }
      ]
    }
  ]
}
```

---

### Task (124f5fc9-98ff-f011-8406-7c1e525abd8b)

```json
{
  "version": 1,
  "sections": [
    {
      "id": "dates",
      "title": "Dates",
      "fields": [
        { "name": "sprk_duedate", "type": "date", "label": "Due Date" },
        { "name": "sprk_finalduedate", "type": "date", "label": "Final Due Date" },
        { "name": "sprk_completeddate", "type": "date", "label": "Completed Date" },
        { "name": "sprk_completedby", "type": "lookup", "label": "Completed By", "targets": ["contact"] }
      ]
    },
    {
      "id": "priority",
      "title": "Priority",
      "fields": [
        { "name": "sprk_priority", "type": "choice", "label": "Priority" },
        { "name": "sprk_effort", "type": "choice", "label": "Effort" }
      ]
    },
    {
      "id": "assigned",
      "title": "Assigned",
      "fields": [
        { "name": "sprk_assignedto", "type": "lookup", "label": "Assigned To", "targets": ["contact"] },
        { "name": "sprk_assignedattorney", "type": "lookup", "label": "Assigned Attorney", "targets": ["contact"] },
        { "name": "sprk_assignedparalegal", "type": "lookup", "label": "Assigned Paralegal", "targets": ["contact"] },
        { "name": "ownerid", "type": "lookup", "label": "Owner", "targets": ["systemuser", "team"], "readOnly": true }
      ]
    }
  ]
}
```

---

### Action (5a1c56c3-98ff-f011-8406-7c1e525abd8b)

```json
{
  "version": 1,
  "sections": [
    {
      "id": "dates",
      "title": "Dates",
      "fields": [
        { "name": "sprk_basedate", "type": "date", "label": "Base Date" },
        { "name": "sprk_completeddate", "type": "date", "label": "Completed Date" },
        { "name": "sprk_completedby", "type": "lookup", "label": "Completed By", "targets": ["contact"] }
      ]
    },
    {
      "id": "assigned",
      "title": "Assigned",
      "fields": [
        { "name": "sprk_assignedto", "type": "lookup", "label": "Assigned To", "targets": ["contact"] },
        { "name": "sprk_assignedattorney", "type": "lookup", "label": "Assigned Attorney", "targets": ["contact"] },
        { "name": "sprk_assignedparalegal", "type": "lookup", "label": "Assigned Paralegal", "targets": ["contact"] },
        { "name": "ownerid", "type": "lookup", "label": "Owner", "targets": ["systemuser", "team"], "readOnly": true }
      ]
    }
  ]
}
```

---

### Milestone (b86d712b-99ff-f011-8406-7c1e525abd8b)

```json
{
  "version": 1,
  "sections": [
    {
      "id": "dates",
      "title": "Dates",
      "fields": [
        { "name": "sprk_duedate", "type": "date", "label": "Due Date" },
        { "name": "sprk_finalduedate", "type": "date", "label": "Final Due Date" },
        { "name": "sprk_completeddate", "type": "date", "label": "Completed Date" },
        { "name": "sprk_completedby", "type": "lookup", "label": "Completed By", "targets": ["contact"] }
      ]
    },
    {
      "id": "priority",
      "title": "Priority",
      "fields": [
        { "name": "sprk_priority", "type": "choice", "label": "Priority" },
        { "name": "sprk_effort", "type": "choice", "label": "Effort" }
      ]
    },
    {
      "id": "assigned",
      "title": "Assigned",
      "fields": [
        { "name": "sprk_assignedto", "type": "lookup", "label": "Assigned To", "targets": ["contact"] },
        { "name": "sprk_assignedattorney", "type": "lookup", "label": "Assigned Attorney", "targets": ["contact"] },
        { "name": "sprk_assignedparalegal", "type": "lookup", "label": "Assigned Paralegal", "targets": ["contact"] },
        { "name": "ownerid", "type": "lookup", "label": "Owner", "targets": ["systemuser", "team"], "readOnly": true }
      ]
    }
  ]
}
```

---

### Meeting (8fb9b5a7-99ff-f011-8406-7c1e525abd8b)

```json
{
  "version": 1,
  "sections": [
    {
      "id": "meeting",
      "title": "Meeting Details",
      "fields": [
        { "name": "sprk_meetingtype", "type": "choice", "label": "Meeting Type" },
        { "name": "sprk_meetingdate", "type": "date", "label": "Meeting Date" },
        { "name": "sprk_meetinglink", "type": "url", "label": "Meeting Link" }
      ]
    },
    {
      "id": "assigned",
      "title": "Assigned",
      "fields": [
        { "name": "sprk_assignedto", "type": "lookup", "label": "Assigned To", "targets": ["contact"] },
        { "name": "sprk_assignedattorney", "type": "lookup", "label": "Assigned Attorney", "targets": ["contact"] },
        { "name": "sprk_assignedparalegal", "type": "lookup", "label": "Assigned Paralegal", "targets": ["contact"] },
        { "name": "ownerid", "type": "lookup", "label": "Owner", "targets": ["systemuser", "team"], "readOnly": true }
      ]
    }
  ]
}
```

---

### Email (f6e75ae8-ad0d-f111-8342-7ced8d1dc988)

```json
{
  "version": 1,
  "sections": [
    {
      "id": "email",
      "title": "Email Details",
      "fields": [
        { "name": "sprk_regardingemail", "type": "lookup", "label": "Regarding Email", "targets": ["email"] },
        { "name": "sprk_emaildate", "type": "text", "label": "Email Date" },
        { "name": "sprk_emailfrom", "type": "text", "label": "From" },
        { "name": "sprk_emailto", "type": "text", "label": "To" }
      ]
    },
    {
      "id": "assigned",
      "title": "Assigned",
      "fields": [
        { "name": "sprk_assignedto", "type": "lookup", "label": "Assigned To", "targets": ["contact"] },
        { "name": "sprk_assignedattorney", "type": "lookup", "label": "Assigned Attorney", "targets": ["contact"] },
        { "name": "sprk_assignedparalegal", "type": "lookup", "label": "Assigned Paralegal", "targets": ["contact"] },
        { "name": "ownerid", "type": "lookup", "label": "Owner", "targets": ["systemuser", "team"], "readOnly": true }
      ]
    }
  ]
}
```

---

### Approval (1ab1c782-99ff-f011-8406-7c1e525abd8b)

```json
{
  "version": 1,
  "sections": [
    {
      "id": "approval",
      "title": "Approval Details",
      "fields": [
        { "name": "sprk_approveddate", "type": "date", "label": "Approved Date" },
        { "name": "sprk_approvedby", "type": "lookup", "label": "Approved By", "targets": ["contact"] }
      ]
    },
    {
      "id": "assigned",
      "title": "Assigned",
      "fields": [
        { "name": "sprk_assignedto", "type": "lookup", "label": "Assigned To", "targets": ["contact"] },
        { "name": "sprk_assignedattorney", "type": "lookup", "label": "Assigned Attorney", "targets": ["contact"] },
        { "name": "sprk_assignedparalegal", "type": "lookup", "label": "Assigned Paralegal", "targets": ["contact"] },
        { "name": "ownerid", "type": "lookup", "label": "Owner", "targets": ["systemuser", "team"], "readOnly": true }
      ]
    }
  ]
}
```

---

### Phone Call (23300069-af0d-f111-8342-7ced8d1dc988)

```json
{
  "version": 1,
  "sections": [
    {
      "id": "details",
      "title": "Details",
      "fields": [
        { "name": "sprk_completeddate", "type": "date", "label": "Completed Date" },
        { "name": "sprk_completedby", "type": "lookup", "label": "Completed By", "targets": ["contact"] }
      ]
    },
    {
      "id": "assigned",
      "title": "Assigned",
      "fields": [
        { "name": "sprk_assignedto", "type": "lookup", "label": "Assigned To", "targets": ["contact"] },
        { "name": "sprk_assignedattorney", "type": "lookup", "label": "Assigned Attorney", "targets": ["contact"] },
        { "name": "sprk_assignedparalegal", "type": "lookup", "label": "Assigned Paralegal", "targets": ["contact"] },
        { "name": "ownerid", "type": "lookup", "label": "Owner", "targets": ["systemuser", "team"], "readOnly": true }
      ]
    }
  ]
}
```

---

## Component Architecture (Refactored App.tsx)

### Before (current — hardcoded sections)

```tsx
<HeaderSection ... />
<StatusSection ... />           // always
<KeyFieldsSection ... />        // always (Due Date, Priority, Owner)
{isSectionVisible("dates") && <DatesSection ... />}
{isSectionVisible("relatedEvent") && <RelatedEventSection ... />}
{isSectionVisible("description") && <DescriptionSection ... />}
{isSectionVisible("history") && <HistorySection ... />}
<Footer ... />
```

### After (Approach A — dynamic renderer)

```tsx
<HeaderSection ... />           // fixed chrome
<StatusReasonBar ... />         // fixed chrome (segmented buttons)
<FormRenderer                   // config-driven
  config={formConfig}
  values={currentValues}
  onChange={handleFieldChange}
  disabled={isDisabled}
/>
<MemoSection                    // fixed section
  eventId={eventId}
  disabled={isDisabled}
/>
<TodoSection                    // fixed section
  eventId={eventId}
  disabled={isDisabled}
/>
<Footer ... />                  // fixed chrome
```

### Components to Create (New)

| Component | Purpose |
|-----------|---------|
| `FormRenderer` | Reads JSON config, renders sections + fields |
| `SectionRenderer` | Renders a single collapsible section with its fields |
| `FieldRenderer` | Dispatches to type-specific renderers |
| `TextFieldRenderer` | Fluent UI Input |
| `MultilineFieldRenderer` | Fluent UI Textarea |
| `DateFieldRenderer` | Fluent UI DatePicker |
| `DateTimeFieldRenderer` | DatePicker + time Input |
| `ChoiceFieldRenderer` | Fluent UI Dropdown (options from metadata) |
| `LookupFieldRenderer` | Persona display + Xrm.Utility.lookupObjects |
| `UrlFieldRenderer` | Input + clickable Link |
| `MemoSection` | Related memo display/edit |
| `TodoSection` | Related to-do display/edit |
| `useFieldMetadata` | Fetch optionset labels from Dataverse |
| `useRelatedRecord` | Generic hook for fetching/creating related records (Memo, ToDo) |

### Components to Keep (Modified)

| Component | Changes |
|-----------|---------|
| `HeaderSection` | Keep as-is (fixed chrome) |
| `StatusReasonBar` | Extract from current StatusSection (segmented buttons only) |
| `Footer` | Keep as-is (fixed chrome) |
| `UnsavedChangesDialog` | Keep as-is |
| `CollapsibleSection` | Keep as-is (used by SectionRenderer) |

### Components to Remove

| Component | Reason |
|-----------|--------|
| `KeyFieldsSection` | Replaced by config-driven fields |
| `DatesSection` | Replaced by config-driven fields |
| `DescriptionSection` | Replaced by config-driven fields (multiline type) |
| `RelatedEventSection` | Replaced by LookupFieldRenderer |
| `HistorySection` | Not in OOB form field data (can add to JSON later if needed) |
| `StatusSection` | Split — status reason bar stays, rest moves to config |

---

## Data Flow: Loading an Event

```
1. Side pane opens with eventId + eventTypeId from URL params
2. Parallel requests:
   a. Fetch Event record (Xrm.WebApi.retrieveRecord)
   b. Fetch Event Type config (sprk_fieldconfigjson from sprk_eventtype)
   c. Fetch related Memo (filter by _sprk_regardingevent_value)
   d. Fetch related ToDo (filter by _sprk_regardingevent_value)
   e. Fetch record access (RetrievePrincipalAccess)
3. Parse JSON config → IFormConfig
4. Fetch field metadata for choice fields (Xrm.Utility.getEntityMetadata)
5. Render:
   - HeaderSection with event data
   - StatusReasonBar with statuscode
   - FormRenderer with config + event values
   - MemoSection with memo data
   - TodoSection with todo data
   - Footer
```

---

## Data Flow: Saving an Event

```
1. User edits field(s) in config-driven sections
2. Dirty fields computed (compare original vs current)
3. User clicks Save (or Save in unsaved changes dialog)
4. Build PATCH payload:
   a. Regular fields: { "sprk_duedate": "2026-02-20" }
   b. Lookup fields: { "sprk_assignedto@odata.bind": "/contacts(guid)" }
   c. Choice fields: { "sprk_priority": 100000002 }
   d. Clear lookup: { "sprk_assignedto@odata.bind": null }
5. Save Event record (Xrm.WebApi.updateRecord)
6. Save Memo if changed (separate updateRecord call)
7. Save ToDo if changed (separate updateRecord call)
8. On success:
   - Update original record snapshot
   - Clear sessionStorage
   - Send EVENT_SAVED via BroadcastChannel (grid refresh)
9. On error:
   - Preserve user edits
   - Show error with Retry/Discard
```

---

## OData $select Construction

The `$select` fields for the Event record must include ALL fields that could appear in ANY Event Type's config. This is built dynamically from the config JSON:

```typescript
function buildSelectFields(config: IFormConfig): string {
  const fields = new Set<string>();

  // Always needed (fixed chrome)
  fields.add("sprk_eventname");
  fields.add("sprk_description");
  fields.add("statecode");
  fields.add("statuscode");
  fields.add("_sprk_eventtype_value");
  fields.add("_ownerid_value");

  // Add all fields from config sections
  for (const section of config.sections) {
    for (const field of section.fields) {
      if (field.type === "lookup") {
        // Lookup fields need the navigation property format
        fields.add(`_${field.name}_value`);
      } else {
        fields.add(field.name);
      }
    }
  }

  return Array.from(fields).join(",");
}
```

**Important**: Since we don't know the config until after the Event Type is fetched, the loading sequence is:
1. Fetch Event record with a broad $select (all known Event fields)
2. Fetch Event Type config (parallel)
3. Render based on config

Alternatively, fetch the Event Type config FIRST (it's a small record), then fetch the Event with a targeted $select. This is more efficient but adds sequential latency.

**Recommendation**: Use a broad $select on the first load (all known fields — ~25 fields is still small). This avoids the waterfall.

---

## Implementation Task Breakdown

| Task | Description | Effort |
|------|-------------|--------|
| **112** | Define TypeScript types for IFormConfig, ISectionConfig, IFieldConfig | 1 hr |
| **113** | Build FieldRenderer dispatcher + TextFieldRenderer + DateFieldRenderer | 2-3 hrs |
| **114** | Build ChoiceFieldRenderer with useFieldMetadata hook | 2-3 hrs |
| **115** | Build LookupFieldRenderer with Xrm.Utility.lookupObjects | 3-4 hrs |
| **116** | Build UrlFieldRenderer + MultilineFieldRenderer + DateTimeFieldRenderer | 2 hrs |
| **117** | Build SectionRenderer + FormRenderer | 2 hrs |
| **118** | Refactor useEventTypeConfig to return IFormConfig (Approach A) | 2-3 hrs |
| **119** | Build MemoSection with useRelatedRecord hook | 3-4 hrs |
| **120** | Build TodoSection with useRelatedRecord hook | 3-4 hrs |
| **121** | Refactor App.tsx to use FormRenderer + MemoSection + TodoSection | 3-4 hrs |
| **122** | Fix Priority optionset values (100000000-100000003 not 1-3) | 1 hr |
| **123** | Wire lookup save format (@odata.bind) into eventService | 2 hrs |
| **124** | Build & test all 7 Event Type configs + generic fallback | 2-3 hrs |
| **125** | End-to-end testing: all Event Types, lookups, memo, todo | 3-4 hrs |

**Estimated total: ~30-40 hours**

---

## Event Type Form GUID Updates

The `eventConfig.ts` file currently maps Event Types to OOB form GUIDs. With Approach A, these mappings are no longer needed for the side pane (the JSON config replaces form selection). However, two new Event Types need to be added:

| Event Type | GUID | Status |
|------------|------|--------|
| Email | f6e75ae8-ad0d-f111-8342-7ced8d1dc988 | NEW — not in current mapping |
| Phone Call | 23300069-af0d-f111-8342-7ced8d1dc988 | NEW — not in current mapping |

These need entries in `EVENT_TYPE_FORM_MAPPINGS` if we keep that for full-form navigation, and their GUIDs need to be recognized by the side pane opener.

---

*Last updated: 2026-02-19*
