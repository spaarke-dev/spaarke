# Event Type Configuration Guide

> **Audience**: System Administrators, Power Platform Administrators
> **Last Updated**: February 2026

## Overview

Event Types in Spaarke control which fields and sections are visible, required, or hidden on the Event form. Each Event Type can have a unique configuration stored in the `sprk_fieldconfigjson` field, allowing you to customize the form experience for different types of events (e.g., Hearings, Filing Deadlines, Regulatory Reviews).

---

## How It Works

When a user selects an Event Type on an Event record:

1. The **EventFormController** PCF control reads the `sprk_fieldconfigjson` field from the selected Event Type
2. The configuration is parsed and applied to the form
3. Fields are shown/hidden and marked required/optional based on the configuration
4. Form sections can be shown or hidden

When the Event Type is cleared, all fields and sections reset to their default states.

---

## Configuration Field

| Entity | Field | Type |
|--------|-------|------|
| Event Type (`sprk_eventtype`) | `sprk_fieldconfigjson` | Multi-line Text |

The field contains a JSON object that defines field and section visibility rules.

---

## JSON Schema

```json
{
  "visibleFields": ["field1", "field2"],
  "hiddenFields": ["field3", "field4"],
  "requiredFields": ["field1"],
  "optionalFields": ["field5"],
  "hiddenSections": ["sectionName"],
  "sectionDefaults": {
    "dates": "expanded",
    "relatedEvent": "collapsed"
  }
}
```

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `visibleFields` | string[] | Fields to explicitly show (overrides hiddenFields) |
| `hiddenFields` | string[] | Fields to hide from the form |
| `requiredFields` | string[] | Fields that must be filled (also makes them visible) |
| `optionalFields` | string[] | Fields that should not be required |
| `hiddenSections` | string[] | Form sections to hide entirely |
| `sectionDefaults` | object | Default collapse states for custom pages (not main forms) |

### Priority Order

When a field appears in multiple lists, this priority applies:

1. **requiredFields** (highest) - Field is visible and required
2. **visibleFields** - Field is visible
3. **optionalFields** - Field is not required
4. **hiddenFields** (lowest) - Field is hidden

---

## Available Fields

The following fields can be controlled via configuration:

| Field Schema Name | Display Name | Default Visible | Default Required |
|-------------------|--------------|-----------------|------------------|
| `sprk_eventname` | Event Name | Yes | Yes |
| `sprk_description` | Description | Yes | No |
| `sprk_basedate` | Base Date | Yes | No |
| `sprk_duedate` | Due Date | Yes | No |
| `sprk_completeddate` | Completed Date | Yes | No |
| `scheduledstart` | Scheduled Start | Yes | No |
| `scheduledend` | Scheduled End | Yes | No |
| `sprk_location` | Location | Yes | No |
| `sprk_remindat` | Remind At | Yes | No |
| `statecode` | Status | Yes | No |
| `statuscode` | Status Reason | Yes | No |
| `sprk_priority` | Priority | Yes | No |
| `sprk_source` | Source | Yes | No |
| `sprk_relatedevent` | Related Event | Yes | No |
| `sprk_relatedeventtype` | Related Event Type | Yes | No |
| `sprk_relatedeventoffsettype` | Related Event Offset Type | Yes | No |

---

## Available Sections

The following form sections can be shown or hidden:

| Section Name | Description | Default Visible |
|--------------|-------------|-----------------|
| `dates` | Contains date-related fields (Base Date, Due Date, etc.) | Yes |
| `relatedEvent` | Contains related event linking fields | Yes |
| `description` | Contains the description field | Yes |
| `history` | Contains event history/timeline (side pane only) | Yes |

**Note**: Section visibility uses the Dataverse `section.setVisible()` API. Collapse state (`sectionDefaults`) only applies to Custom Pages and PCF controls, not main Dataverse forms.

---

## Configuration Examples

### Example 1: Court Hearing

Hearings require a due date and location, but don't need related event fields.

```json
{
  "requiredFields": ["sprk_duedate", "sprk_location"],
  "hiddenFields": ["sprk_relatedevent", "sprk_relatedeventtype", "sprk_relatedeventoffsettype"],
  "hiddenSections": ["relatedEvent"]
}
```

### Example 2: Filing Deadline

Filing deadlines require a due date, hide location, and show the related event section expanded.

```json
{
  "requiredFields": ["sprk_duedate"],
  "hiddenFields": ["sprk_location", "sprk_completeddate"],
  "sectionDefaults": {
    "relatedEvent": "expanded",
    "dates": "expanded"
  }
}
```

### Example 3: Regulatory Review

Regulatory reviews need start/end dates and priority, hide reminder.

```json
{
  "requiredFields": ["scheduledstart", "scheduledend", "sprk_priority"],
  "hiddenFields": ["sprk_remindat", "sprk_basedate"],
  "optionalFields": ["sprk_location"]
}
```

### Example 4: Simple Reminder

Simple reminders only need basic fields, hide most optional fields.

```json
{
  "requiredFields": ["sprk_duedate"],
  "hiddenFields": [
    "sprk_basedate",
    "scheduledstart",
    "scheduledend",
    "sprk_location",
    "sprk_relatedevent",
    "sprk_relatedeventtype",
    "sprk_relatedeventoffsettype"
  ],
  "hiddenSections": ["relatedEvent"]
}
```

### Example 5: Minimal Configuration

No special requirements - use all defaults.

```json
{}
```

---

## How to Configure an Event Type

### Step 1: Navigate to Event Types

1. Open the Spaarke app in Dataverse
2. Navigate to **Settings** > **Event Types**
3. Open an existing Event Type or create a new one

### Step 2: Edit the Field Configuration

1. Find the **Field Configuration JSON** field (`sprk_fieldconfigjson`)
2. Enter valid JSON following the schema above
3. Save the record

### Step 3: Test the Configuration

1. Create a new Event or open an existing one
2. Select the configured Event Type
3. Verify fields and sections appear/hide as expected
4. Clear the Event Type to verify fields reset to defaults

---

## Validation

The system validates the JSON configuration:

- **Invalid JSON**: Ignored (defaults used, warning logged to console)
- **Unknown field names**: Ignored with warning
- **Unknown section names**: Ignored
- **Conflicts** (field in both required and hidden): Required takes precedence

### Checking for Errors

Open browser developer tools (F12) and check the Console tab for warnings:

```
[EventTypeService] Unknown field 'invalid_field' in requiredFields
[FieldVisibilityHandler] Field not found on form: unknown_field
```

---

## Troubleshooting

| Issue | Possible Cause | Solution |
|-------|----------------|----------|
| Configuration not applying | Invalid JSON | Validate JSON syntax |
| Field not hiding | Field not on form | Verify field schema name |
| Section not hiding | Section not found | Check section name spelling |
| Required field not enforced | Field in optionalFields too | Remove from optionalFields |

---

## Related Documentation

- [Events User Guide](events-user-guide.md) - How to create and manage events
- [Event Type Configuration Technical Guide](../guides/EVENT-TYPE-CONFIGURATION.md) - Developer reference

---

*For technical implementation details, see the [Event Type Configuration Technical Guide](../guides/EVENT-TYPE-CONFIGURATION.md).*
