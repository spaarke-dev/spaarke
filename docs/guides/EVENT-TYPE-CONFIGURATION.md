# Event Type Field Configuration - Technical Guide

> **Audience**: Developers, Technical Consultants
> **Last Updated**: February 2026
> **Related ADR**: ADR-012 (Shared Component Library)

## Overview

This guide covers the technical implementation of Event Type field configuration, including the `EventTypeService`, `FieldVisibilityHandler`, and the `IEventTypeFieldConfig` interface.

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                     sprk_eventtype Entity                        │
│                                                                  │
│  sprk_fieldconfigjson: '{"requiredFields": ["sprk_duedate"]}'   │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                    EventTypeService                              │
│                  (@spaarke/ui-components)                        │
│                                                                  │
│  • parseFieldConfigJson() → IEventTypeFieldConfig               │
│  • computeFieldStates() → IComputedFieldStates                  │
│  • getEventTypeFieldConfig() → async WebAPI fetch               │
└─────────────────────────────────────────────────────────────────┘
                              │
              ┌───────────────┼───────────────┐
              ▼               ▼               ▼
┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐
│ EventFormController│ │ EventDetailSidePane│ │ Future Consumers │
│     (PCF)         │ │  (Custom Page)     │ │                  │
│                   │ │                    │ │                  │
│ Uses:             │ │ Uses:              │ │                  │
│ FieldVisibilityHandler│ │ React state for   │ │                  │
│ (Xrm.Page API)    │ │ collapse/visibility│ │                  │
└─────────────────────┘ └─────────────────────┘ └─────────────────────┘
```

---

## Type Definitions

### IEventTypeFieldConfig

```typescript
// Location: @spaarke/ui-components/types/EventTypeConfig

interface IEventTypeFieldConfig {
  /** Fields to explicitly show (overrides hiddenFields) */
  visibleFields?: string[];

  /** Fields to hide from the form */
  hiddenFields?: string[];

  /** Fields that must be filled (also makes them visible) */
  requiredFields?: string[];

  /** Fields that should not be required */
  optionalFields?: string[];

  /** Sections to hide on main Dataverse forms */
  hiddenSections?: string[];

  /** Default collapse states for Custom Pages/PCF controls */
  sectionDefaults?: ISectionDefaults;
}
```

### ISectionDefaults

```typescript
interface ISectionDefaults {
  dates?: "expanded" | "collapsed";
  relatedEvent?: "expanded" | "collapsed";
  description?: "expanded" | "collapsed";
  history?: "expanded" | "collapsed";
}
```

### IComputedFieldStates

```typescript
interface IComputedFieldStates {
  /** Map of field names to their computed states */
  fields: Map<string, IComputedFieldState>;

  /** Map of section names to their computed visibility/collapse states */
  sections: Map<string, IComputedSectionState>;

  /** Section default states (legacy) */
  sectionDefaults: ISectionDefaults;

  /** The source configuration that was applied */
  sourceConfig: IEventTypeFieldConfig | null;
}

interface IComputedFieldState {
  fieldName: string;
  isVisible: boolean;
  requiredLevel: "required" | "recommended" | "none";
  isOverridden: boolean;
}

interface IComputedSectionState {
  sectionName: string;
  isVisible: boolean;
  collapseState: "expanded" | "collapsed";
}
```

---

## EventTypeService

### Location

```
src/client/shared/Spaarke.UI.Components/src/services/EventTypeService.ts
```

### Exports

```typescript
import {
  EventTypeService,
  eventTypeService,           // Singleton instance
  DEFAULT_EVENT_FIELD_STATES, // Default field visibility/requirements
  ALL_EVENT_FIELDS,           // Array of all controllable field names
  DEFAULT_SECTION_STATES,     // Default section collapse states
  ALL_SECTION_NAMES,          // Array of all section names
  getEventTypeFieldConfig,    // Async fetch helper
} from "@spaarke/ui-components";
```

### Usage Examples

#### Parse Configuration JSON

```typescript
const jsonString = '{"requiredFields": ["sprk_duedate"]}';
const config = eventTypeService.parseFieldConfigJson(jsonString);
// Returns: { requiredFields: ["sprk_duedate"] }

// Invalid JSON returns null
const invalid = eventTypeService.parseFieldConfigJson("{bad json}");
// Returns: null (warning logged)
```

#### Compute Field States

```typescript
const config: IEventTypeFieldConfig = {
  requiredFields: ["sprk_duedate"],
  hiddenFields: ["sprk_location"],
  hiddenSections: ["relatedEvent"],
};

const computed = eventTypeService.computeFieldStates(config);

// Check field state
const dueDateState = computed.fields.get("sprk_duedate");
// { fieldName: "sprk_duedate", isVisible: true, requiredLevel: "required", isOverridden: true }

// Check section state
const relatedEventSection = computed.sections.get("relatedEvent");
// { sectionName: "relatedEvent", isVisible: false, collapseState: "collapsed" }
```

#### Fetch Configuration from Dataverse

```typescript
// Works with PCF WebAPI or Xrm.WebApi
const result = await getEventTypeFieldConfig(context.webAPI, eventTypeId);

if (result.success && result.config) {
  const computed = eventTypeService.computeFieldStates(result.config);
  // Apply to form...
}
```

---

## FieldVisibilityHandler

### Location

```
src/client/pcf/EventFormController/handlers/FieldVisibilityHandler.ts
```

### Purpose

Applies computed field states to Dataverse main forms using the Xrm.Page API.

### Key Functions

```typescript
// Field visibility
showField(fieldName: string): boolean;
hideField(fieldName: string): boolean;

// Field requirements
setRequired(fieldName: string): boolean;
setOptional(fieldName: string): boolean;
setRequiredLevel(fieldName: string, level: RequiredLevel): boolean;

// Section visibility (new)
showSection(sectionName: string): boolean;
hideSection(sectionName: string): boolean;
setSectionVisibility(sectionName: string, visible: boolean): boolean;

// Bulk operations
resetToDefaults(): ApplyRulesResult;
applyComputedFieldStates(computed: IComputedFieldStates): ApplyRulesResult;
```

### Usage in EventFormController

```typescript
// In EventFormControllerApp.tsx
import { eventTypeService, getEventTypeFieldConfig } from "@spaarke/ui-components";
import { applyComputedFieldStates, resetToDefaults } from "./handlers/FieldVisibilityHandler";

// When Event Type changes
const handleEventTypeChange = async (eventTypeId: string | null) => {
  if (!eventTypeId) {
    // Reset to defaults when cleared
    resetToDefaults();
    return;
  }

  // Fetch configuration
  const result = await getEventTypeFieldConfig(webApi, eventTypeId);

  if (result.success && result.config) {
    // Compute and apply states
    const computed = eventTypeService.computeFieldStates(result.config);
    applyComputedFieldStates(computed);
  }
};
```

---

## Default States

### Default Field States

```typescript
export const DEFAULT_EVENT_FIELD_STATES: IFieldDefaultStates = {
  sprk_eventname: { visible: true, requiredLevel: "required" },
  sprk_description: { visible: true, requiredLevel: "none" },
  sprk_basedate: { visible: true, requiredLevel: "none" },
  sprk_duedate: { visible: true, requiredLevel: "none" },
  sprk_completeddate: { visible: true, requiredLevel: "none" },
  scheduledstart: { visible: true, requiredLevel: "none" },
  scheduledend: { visible: true, requiredLevel: "none" },
  sprk_location: { visible: true, requiredLevel: "none" },
  sprk_remindat: { visible: true, requiredLevel: "none" },
  statecode: { visible: true, requiredLevel: "none" },
  statuscode: { visible: true, requiredLevel: "none" },
  sprk_priority: { visible: true, requiredLevel: "none" },
  sprk_source: { visible: true, requiredLevel: "none" },
  sprk_relatedevent: { visible: true, requiredLevel: "none" },
  sprk_relatedeventtype: { visible: true, requiredLevel: "none" },
  sprk_relatedeventoffsettype: { visible: true, requiredLevel: "none" },
};
```

### Default Section States

```typescript
export const DEFAULT_SECTION_STATES: ISectionDefaults = {
  dates: "expanded",
  relatedEvent: "collapsed",
  description: "expanded",
  history: "collapsed",
};
```

### All Controllable Sections

```typescript
export const ALL_SECTION_NAMES = [
  "dates",
  "relatedEvent",
  "description",
  "history"
] as const;
```

---

## Section Control: Forms vs Custom Pages

### Main Dataverse Forms

- **Visibility**: Supported via `section.setVisible(true/false)`
- **Collapse State**: NOT supported programmatically
- Use `hiddenSections` in configuration

```typescript
// FieldVisibilityHandler uses Xrm.Page API
formContext.ui.tabs.forEach((tab) => {
  tab.sections.forEach((section) => {
    if (section.getName() === sectionName) {
      section.setVisible(false);
    }
  });
});
```

### Custom Pages & PCF Controls

- **Visibility**: Via React state
- **Collapse State**: Via React state using `sectionDefaults`

```typescript
// Custom Page component
const [sectionStates, setSectionStates] = useState(computed.sectionDefaults);

return (
  <CollapsibleSection
    name="dates"
    defaultExpanded={sectionStates.dates === "expanded"}
  >
    {/* Date fields */}
  </CollapsibleSection>
);
```

---

## Validation

### Configuration Validation

```typescript
const config: IEventTypeFieldConfig = {
  requiredFields: ["sprk_duedate", "unknown_field"],
  hiddenFields: ["sprk_duedate"], // Conflict!
};

const validation = eventTypeService.validateConfig(config);
// {
//   isValid: false,
//   warnings: ["Unknown field 'unknown_field' in requiredFields"],
//   errors: ["Field 'sprk_duedate' is in both hiddenFields and requiredFields"]
// }
```

### Runtime Warnings

Fields or sections not found on the form are logged as warnings:

```
[FieldVisibilityHandler] Field not found on form: unknown_field
[FieldVisibilityHandler] Section not found on form: invalid_section
```

---

## Testing

### Unit Tests

```
src/client/shared/Spaarke.UI.Components/src/services/__tests__/EventTypeService.test.ts
```

Coverage: 80+ tests covering parsing, computation, validation, and edge cases.

### Integration Testing

1. Deploy EventFormController to Dataverse
2. Configure an Event Type with `sprk_fieldconfigjson`
3. Create/open an Event and select the Event Type
4. Verify fields show/hide as configured
5. Clear Event Type and verify reset

---

## Extending the System

### Adding New Controllable Fields

1. Add to `DEFAULT_EVENT_FIELD_STATES` in `EventTypeService.ts`
2. Field will automatically be recognized in configurations
3. Update documentation

### Adding New Sections

1. Add to `ALL_SECTION_NAMES` and `DEFAULT_SECTION_STATES`
2. Update `ISectionDefaults` interface
3. Update documentation

---

## Related Files

| File | Purpose |
|------|---------|
| `src/client/shared/Spaarke.UI.Components/src/types/EventTypeConfig.ts` | Type definitions |
| `src/client/shared/Spaarke.UI.Components/src/services/EventTypeService.ts` | Core service |
| `src/client/pcf/EventFormController/handlers/FieldVisibilityHandler.ts` | Form manipulation |
| `src/client/pcf/EventFormController/EventFormControllerApp.tsx` | PCF integration |

---

## Related Documentation

- [Event Type Configuration Guide](../product-documentation/event-type-configuration-guide.md) - Admin guide
- [ADR-012: Shared Component Library](.claude/adr/ADR-012-shared-component-library.md)

---

*For administrator configuration instructions, see the [Event Type Configuration Guide](../product-documentation/event-type-configuration-guide.md).*
