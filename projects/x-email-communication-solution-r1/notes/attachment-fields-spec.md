# Attachment Fields Specification

**Entity**: `sprk_communication`
**Phase**: 4: Attachments + Archival
**Status**: Documentation Complete
**Last Updated**: February 21, 2026

---

## Overview

Two new fields are required on the `sprk_communication` entity to support attachment tracking in the email communication solution:

1. **sprk_hasattachments** - Boolean flag indicating presence of file attachments
2. **sprk_attachmentcount** - Whole number field tracking the count of attachments

These fields are auto-maintained by the BFF CommunicationService and are read-only on Dataverse forms.

---

## Field 1: sprk_hasattachments

### Basic Configuration

| Property | Value |
|----------|-------|
| **Logical Name** | `sprk_hasattachments` |
| **Display Name** | Has Attachments |
| **Attribute Type** | Two Options (Boolean) |
| **Custom Attribute** | Yes |
| **Schema Name** | `sprk_HasAttachments` |

### Field Properties

| Property | Value |
|----------|-------|
| **Required** | No |
| **Default Value** | No (false) |
| **Description** | Flag indicating whether the communication has file attachments |
| **Searchable** | Yes |
| **Auditable** | Yes |

### Option Labels

| Value | Label |
|-------|-------|
| 0 | No |
| 1 | Yes |

### Business Logic

- **Auto-Set by BFF**: When the BFF CommunicationService processes a POST `/api/communications/send` request with attachments, this field is automatically set to `Yes` (true).
- **Default**: All new communication records default to `No` (false).
- **Form Display**: Read-only on all forms; system-managed field updated by backend service only.
- **Data Type Equivalence**: Maps to C# `bool?` (nullable boolean) in backend DTOs.

### Use Cases

- Query communications with attachments: `$filter=sprk_hasattachments eq true`
- Display attachment indicator in communication lists and grids
- Enable conditional logic to show attachment section only when attachments present

---

## Field 2: sprk_attachmentcount

### Basic Configuration

| Property | Value |
|----------|-------|
| **Logical Name** | `sprk_attachmentcount` |
| **Display Name** | Attachment Count |
| **Attribute Type** | Whole Number (Integer) |
| **Custom Attribute** | Yes |
| **Schema Name** | `sprk_AttachmentCount` |

### Field Properties

| Property | Value |
|----------|-------|
| **Required** | No |
| **Default Value** | 0 |
| **Minimum Value** | 0 |
| **Maximum Value** | 150 |
| **Description** | Number of file attachments on this communication |
| **Format** | None (integer display) |
| **IME Mode** | Active |
| **Searchable** | Yes |
| **Auditable** | Yes |

### Business Logic

- **Auto-Set by BFF**: When the BFF CommunicationService processes a POST `/api/communications/send` request, this field is set to the number of attachments included in the request.
- **Maximum Value**: 150 attachments per communication (Microsoft Graph API limit for sendMail).
- **Default**: All new communication records default to `0`.
- **Form Display**: Read-only on all forms; system-managed field updated by backend service only.
- **Data Type Equivalence**: Maps to C# `int` in backend DTOs.

### Constraints

- **Minimum**: 0 (validation enforced by Dataverse field configuration)
- **Maximum**: 150 (Graph API sendMail endpoint limit)
- **Total Attachment Size Limit**: 35 MB per communication (Graph API constraint; BFF enforces validation)

### Use Cases

- Query communications with attachments: `$filter=sprk_attachmentcount gt 0`
- Sort communication list by attachment count
- Display attachment count badge in UI grids and lists
- Enable attachment download summary view ("This communication has 3 attachments")

---

## Form Placement

### Details Tab

**Section Name**: Attachments

**Location**: Below the main communication content sections (Subject, From, To, CC, BCC, Body)

**Field Arrangement**:
```
┌─────────────────────────────────────┐
│ Attachments                         │
├─────────────────────────────────────┤
│ Has Attachments:        [Yes/No]    │
│ Attachment Count:       [number]    │
└─────────────────────────────────────┘
```

### Form Settings

| Setting | Value |
|---------|-------|
| **Tab** | Details |
| **Visibility** | Visible on all forms |
| **Locked** | Yes (prevent accidental field removal) |
| **Read-Only** | Yes (user-facing forms cannot edit) |
| **Show Label** | Yes |
| **Required** | No |

---

## Backend Integration

### CommunicationService Updates

The BFF `CommunicationService` updates these fields when processing email sends:

**Scenario 1: With Attachments**
```
POST /api/communications/send
{
  "to": ["recipient@example.com"],
  "subject": "Document Review",
  "body": "Please review the attached files.",
  "attachments": [
    { "fileName": "proposal.pdf", "content": "..." },
    { "fileName": "budget.xlsx", "content": "..." }
  ]
}

Result:
- sprk_hasattachments = true
- sprk_attachmentcount = 2
```

**Scenario 2: Without Attachments**
```
POST /api/communications/send
{
  "to": ["recipient@example.com"],
  "subject": "Meeting Reminder",
  "body": "Reminder of tomorrow's meeting."
}

Result:
- sprk_hasattachments = false (default)
- sprk_attachmentcount = 0 (default)
```

### Dataverse Plugin Synchronization

When manually updating attachments (if supported in future phases):
- Dataverse plugin should validate attachment constraints before allowing changes
- Plugin must reject requests exceeding 150 attachments or 35 MB total size
- Plugin should emit audit trail events for compliance tracking

---

## Data Validation Rules

### Dataverse Field Validation

| Rule | Enforcement |
|------|-------------|
| `sprk_attachmentcount` >= 0 | Field min value = 0 |
| `sprk_attachmentcount` <= 150 | Field max value = 150 |
| `sprk_hasattachments` is boolean | Two Options type |
| If `sprk_attachmentcount` > 0, then `sprk_hasattachments` = true | BFF enforces in CommunicationService |

### BFF Validation Rules

| Rule | Validation |
|------|------------|
| Attachment count must match array length | Count = attachments[].length |
| Total attachment size must not exceed 35 MB | Sum of file sizes < 35 MB |
| Maximum 150 attachments per communication | Count <= 150 |
| File types permitted | BFF allowlist enforced (not Dataverse) |

---

## Query Examples

### Query Communications With Attachments

```odata
# Get all communications with attachments
GET /api/communications?$filter=sprk_hasattachments eq true

# Get all communications with more than 5 attachments
GET /api/communications?$filter=sprk_attachmentcount gt 5

# Get all communications with 3 or more attachments
GET /api/communications?$filter=sprk_attachmentcount ge 3
```

### Query Communications Without Attachments

```odata
# Get all communications without attachments
GET /api/communications?$filter=sprk_hasattachments eq false

# Get all communications with zero attachments
GET /api/communications?$filter=sprk_attachmentcount eq 0
```

### Combined Queries

```odata
# Get incoming emails with attachments for a specific matter
GET /api/communications?$filter=sprk_direction eq 100000000 AND sprk_hasattachments eq true AND sprk_RegardingMatter eq {matterId}

# Get communications sorted by attachment count (descending)
GET /api/communications?$orderby=sprk_attachmentcount desc
```

---

## Migration & Deployment Notes

### Field Creation Checklist

- [ ] Create `sprk_hasattachments` field (Two Options, default = No)
- [ ] Create `sprk_attachmentcount` field (Whole Number, min=0, max=150, default=0)
- [ ] Add both fields to main communication form in Details tab
- [ ] Set both fields to read-only on form
- [ ] Lock both fields to prevent accidental removal from form
- [ ] Add section header "Attachments" on Details tab
- [ ] Publish all customizations
- [ ] Update solution version (increment patch number)
- [ ] Export solution to version control

### Post-Deployment Validation

- [ ] Verify fields exist in Dataverse using metadata browser
- [ ] Test BFF integration: send communication with attachments
- [ ] Verify `sprk_hasattachments` and `sprk_attachmentcount` are populated
- [ ] Test form display: verify fields are read-only
- [ ] Test OData queries: filter by attachment flag and count
- [ ] Verify audit trail: confirm field changes are auditable

---

## Related Tasks

- **Task 016**: Dataverse schema and field configuration (dependency)
- **Task 031**: Implement attachment storage (blocks)
- **Task 033**: Implement attachment retrieval (blocks)
- **Task 036**: Implement attachment deletion (blocks)

---

## Naming Convention Reference

All fields follow the Spaarke naming convention established in the existing schema:

- **Prefix**: `sprk_` (lowercase)
- **Field Name**: camelCase logical name (e.g., `sprk_hasattachments`)
- **Display Name**: Title Case with spaces (e.g., "Has Attachments")
- **Schema Name**: PascalCase for XRM/SDK usage (e.g., `sprk_HasAttachments`)

See existing fields in `/docs/data-model/sprk_communication-data-schema.md` for consistency reference.

---

## Related Documentation

- [sprk_communication Data Schema](../docs/data-model/sprk_communication-data-schema.md)
- [Email Communication Solution - Design Specification](../projects/email-communication-solution-r1/spec.md)
- [BFF CommunicationService Documentation](../docs/api/services/communication-service.md)
