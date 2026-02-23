# sprk_communication Model-Driven Form Configuration

> **Project**: email-communication-solution-r1
> **Phase**: 3: Communication Application
> **Status**: Documentation
> **Date**: 2026-02-21

---

## Overview

This document describes the model-driven form for the `sprk_communication` entity. The form supports two distinct modes:

1. **Compose Mode** (new record, Draft status) — for creating and drafting new communications
2. **Read Mode** (sent record, non-Draft status) — for viewing sent communications with all fields read-only and tracking details visible

The form uses the **AssociationResolver PCF control** for flexible multi-entity association (same pattern as `sprk_event`).

---

## Form Structure

### Main Form: Communication

**Entity**: `sprk_communication`
**Display Name**: Communication
**Type**: Main form
**Supported Actions**: Create, Update, Read

#### Tab Layout

The form is organized into two tabs:

1. **Compose** — Active when composing new communications (Draft status)
2. **Details** — Active when viewing sent communications (non-Draft status)
3. **Tracking** — Visible only for sent communications (hidden when status = Draft)

---

## Header Section

The form header displays key metadata visible in all modes:

| Field | Logical Name | Display Name | Control | Behavior |
|-------|------------|--------------|---------|----------|
| Name | `sprk_name` | Name | Text (auto-generated) | **Compose**: Read-only, auto-populated by business rule as "{Type}: {Subject}". **Read**: Read-only display. |
| Type | `sprk_communiationtype` | Communication Type | Choice dropdown | **Compose**: Editable, default value = Email (100000000). **Read**: Read-only. |
| Status Reason | `statuscode` | Status Reason | Status field | **Compose**: Displayed as "Draft". **Read**: Displays actual status (Send, Delivered, Failed, Queued, Bounded, Recalled). |
| From | `sprk_from` | From | Text | **Compose**: Editable (may be pre-populated from config). **Read**: Read-only, displays sender mailbox. |

**Header Field Widths** (responsive layout):
- Row 1: Type [50%] | Status [50%]
- Row 2: From [100%]

---

## Tab 1: Compose

**Visibility Rule**: Show when `statuscode == 1` (Draft)

### Section 1.1: Association

**Display Name**: "Link to Record"
**Collapsible**: Yes
**Description**: "Select the entity and record this communication is associated with"

| Field | Logical Name | Display Name | Control | Requirements | Notes |
|-------|------------|--------------|---------|--------------|-------|
| Record Type | `sprk_regardingrecordtype` | Record Type | Lookup to `sprk_recordtype_ref` | Required | Drives AssociationResolver control entity selector. Targets: sprk_matter, sprk_organization, contact, sprk_project, sprk_analysis, sprk_budget, sprk_invoice, sprk_workassignment |
| **AssociationResolver PCF** | (bound to sprk_regardingrecordtype) | — | PCF Control | Required | **Configuration**: Bind to `sprk_regardingrecordtype`. Place below the Record Type lookup. Control handles: entity selection, record lookup, and denormalized field auto-population. |
| Record Name | `sprk_regardingrecordname` | Record Name | Text (read-only) | Optional | Auto-populated by AssociationResolver after record selection. Denormalized for quick view filtering. |
| Record ID | `sprk_regardingrecordid` | Record ID | Text (read-only) | Optional | Auto-populated by AssociationResolver. Stores GUID of selected record. |
| Record URL | `sprk_regardingrecordurl` | Record URL | URL field (read-only) | Optional | Auto-populated by AssociationResolver. Clickable link to parent record. |
| Association Count | `sprk_associationcount` | Association Count | Whole number (read-only) | Optional | Currently 1 (primary only). Future: updated when multi-record association is added. |

**Entity-Specific Lookup Fields** (mapped by AssociationResolver):

| Entity | Lookup Field | Logical Name | Display |
|--------|-------------|-------------|---------|
| Matter | `sprk_regardingmatter` | Regarding Matter | Hidden (mapped internally) |
| Organization | `sprk_regardingorganization` | Regarding Organization | Hidden (mapped internally) |
| Contact/Person | `sprk_regardingperson` | Regarding Person | Hidden (mapped internally) |
| Project | `sprk_regardingproject` | Regarding Project | Hidden (mapped internally) |
| Analysis | `sprk_regardinganalysis` | Regarding Analysis | Hidden (mapped internally) |
| Budget | `sprk_regardingbudget` | Regarding Budget | Hidden (mapped internally) |
| Invoice | `sprk_regardinginvoice` | Regarding Invoice | Hidden (mapped internally) |
| Work Assignment | `sprk_regardingworkassignment` | Regarding Work Assignment | Hidden (mapped internally) |

**AssociationResolver PCF Configuration**:
- **Lookup Bindings**: Map above 8 entity-specific lookups
- **Display Pattern**: Show entity type selector (dropdown), then record lookup
- **Auto-populate**: Denormalized fields on record selection
- **Supports multi-select**: No (primary association only for Phase 3)

### Section 1.2: Email Details

**Display Name**: "Email Details"
**Collapsible**: No
**Description**: "Compose the email message"

| Field | Logical Name | Display Name | Control | Requirements | Notes |
|-------|------------|--------------|---------|--------------|-------|
| To | `sprk_to` | To | Text (2000 char) | Required | Semicolon or comma-delimited recipient email addresses. Placeholder: "recipient@example.com; another@example.com". **Validation**: Comma or semicolon separated; email format validation (client-side PCF or Dataverse validation). |
| CC | `sprk_cc` | CC | Text (2000 char) | Optional | Semicolon-delimited CC recipients. Can be empty. |
| BCC | `sprk_bcc` | BCC | Text (2000 char) | Optional | Semicolon-delimited BCC recipients. Visible only in Draft/compose mode. |
| Subject | `sprk_subject` | Subject | Text (2000 char) | Required | Email subject. Used in business rule to auto-generate `sprk_name`. |
| Body | `sprk_body` | Body | Multiline Text (100,000 char) | Required | Rich HTML body editor. Format determined by `sprk_bodyformat`. Display as rich text editor (HTML WYSIWYG). |
| Body Format | `sprk_bodyformat` | Body Format | Choice dropdown | Optional | Options: PlainText (100000000), HTML (100000001). Default: HTML (100000001). Hidden field (controls backend rendering). |

**Field Widths** (responsive):
- Row 1: To [60%] | CC [40%]
- Row 2: BCC [100%]
- Row 3: Subject [100%]
- Row 4: Body [100%] (tall, min 300px)
- Row 5: Body Format [Hidden]

### Section 1.3: Attachments

**Display Name**: "Attachments"
**Collapsible**: Yes
**Description**: "Attach documents from your matter or project" (optional)

| Field | Logical Name | Display Name | Control | Notes |
|-------|------------|--------------|---------|-------|
| Has Attachments | `sprk_hasattachments` | Has Attachments | Boolean (hidden) | Auto-updated when attachment records created/deleted. |
| Attachment Count | `sprk_attachmentcount` | Attachment Count | Whole number (read-only, hidden) | Auto-updated count. |
| **Document Attachment Subgrid** | `sprk_communicationattachment` | Attachments | Subgrid (read-write in Draft) | **Phase 4 Implementation**: Subgrid showing linked `sprk_document` records. Allow add/remove in Draft mode. Columns: Document Name, File Type, Size. Features: "Add Existing" (lookup picker), "Remove" button. |

**Subgrid Configuration** (Phase 4):
- **Entity**: `sprk_communicationattachment` (intersection)
- **Columns**: Document Name, Document Type, Size (read-only)
- **Allow Create**: Yes (in Draft mode)
- **Allow Delete**: Yes (in Draft mode)
- **Pre-filter**: Link to associated entity (e.g., if Matter is selected, show only documents on that matter)

**Current Phase 3 Note**: Subgrid implementation deferred to Phase 4. For now, show a placeholder or "Coming Soon" message in Compose mode.

---

## Tab 2: Details

**Visibility Rule**: Show when `statuscode != 1` (not Draft)

This tab is visible only after the communication has been sent (status ≠ Draft). All fields are read-only.

### Section 2.1: Email Content

**Display Name**: "Email Content"
**Collapsible**: No

| Field | Logical Name | Display Name | Control | Notes |
|-------|------------|--------------|---------|-------|
| To | `sprk_to` | To | Text (read-only) | Display recipients |
| CC | `sprk_cc` | CC | Text (read-only) | Display CC recipients |
| Subject | `sprk_subject` | Subject | Text (read-only) | Display email subject |
| Body | `sprk_body` | Body | Multiline Text (read-only) | Render as HTML if `sprk_bodyformat = HTML` |

### Section 2.2: Tracking

**Display Name**: "Tracking Details"
**Collapsible**: Yes
**Visibility Rule**: Show when `statuscode != 1` (all non-Draft statuses)

| Field | Logical Name | Display Name | Control | Notes |
|-------|------------|--------------|---------|-------|
| Direction | `sprk_direction` | Direction | Choice (read-only) | Display: Outgoing (100000001) or Incoming (100000000) |
| Communication Type | `sprk_communiationtype` | Communication Type | Choice (read-only) | Display: Email, Teams Message, SMS, Notification |
| Correlation ID | `sprk_correlationid` | Correlation ID | Text (read-only) | Tracing ID from caller (e.g., "create-matter-abc123") |
| Graph Message ID | `sprk_graphmessageid` | Graph Message ID | Text (read-only) | Message ID from Microsoft Graph for delivery tracking |
| Sent At | `sprk_sentat` | Sent At | DateTime (read-only) | UTC timestamp when sent. Format: "Feb 20, 2026 2:30 PM" |
| Sent By | `sprk_sentby` | Sent By | Lookup to systemuser (read-only) | User who initiated the send |
| Error Message | `sprk_errormessage` | Error Message | Multiline Text (read-only) | Populated only if status = Failed. Display error details. |
| Retry Count | `sprk_retrycount` | Retry Count | Whole number (read-only) | Number of send attempts for failed/queued messages |

**Field Widths**:
- Row 1: Direction [50%] | Communication Type [50%]
- Row 2: Correlation ID [100%]
- Row 3: Graph Message ID [100%]
- Row 4: Sent At [50%] | Sent By [50%]
- Row 5: Error Message [100%]
- Row 6: Retry Count [20%]

---

## Business Rules

### Business Rule 1: Auto-Generate Communication Name

**Name**: BR_Communication_AutoGenerateName
**Scope**: Entity
**Status**: Active
**Trigger**: When record is created OR when Type or Subject changes

**Condition**:
```
sprk_communiationtype != null AND sprk_subject != null
```

**Action**: Set Value
- **Field**: `sprk_name`
- **Value**: `{sprk_communiationtypeName}: {sprk_subject}`

**Example Output**:
- Input: Type = Email (100000000), Subject = "New Matter: Smith v. Jones"
- Output: `sprk_name` = "Email: New Matter: Smith v. Jones"

**Implementation Notes**:
- Dataverse choice fields auto-populate the `{name}Name` virtual field with the choice label
- Business rule uses the virtual field for the display label (not the numeric choice value)
- Maximum length: 850 characters (sprk_name field max). Subject max is 2000, so truncation may occur for very long subjects. Consider shortening subject in business rule if needed.

### Business Rule 2: Hide Tracking Section in Draft

**Name**: BR_Communication_HideTrackingInDraft
**Scope**: Form
**Status**: Active
**Trigger**: Form load, Status change

**Visibility Rule**: Hide Tracking section when `statuscode == 1` (Draft)

**Implementation**:
- Form XML: Set section visibility to hidden by condition
- **FetchXML Condition**:
  ```xml
  <condition attribute="statuscode" operator="ne" value="1" />
  ```

### Business Rule 3: Lock Fields After Send

**Name**: BR_Communication_LockFieldsAfterSend
**Scope**: Entity
**Status**: Active (optional, for data integrity)
**Trigger**: When statuscode changes to non-Draft value

**Action**: Disable (read-only) all compose fields:
- `sprk_to`
- `sprk_cc`
- `sprk_bcc`
- `sprk_subject`
- `sprk_body`
- `sprk_regardingmatter` (and all entity-specific lookups)
- Attachment removal (in Phase 4 subgrid)

**Implementation**:
- Form-level visibility rules or use form JavaScript (PCF)
- **FetchXML Condition**:
  ```xml
  <condition attribute="statuscode" operator="ne" value="1" />
  ```
- When condition is true: set form fields to disabled state

---

## Default Values

Set the following default values on form load (new record):

| Field | Logical Name | Default Value | Type |
|-------|------------|--------------|------|
| Communication Type | `sprk_communiationtype` | 100000000 (Email) | Choice |
| Direction | `sprk_direction` | 100000001 (Outgoing) | Choice |
| Status Reason | `statuscode` | 1 (Draft) | Status |
| Body Format | `sprk_bodyformat` | 100000001 (HTML) | Choice |

---

## Form Validation

### Client-Side Validation (Form JavaScript/PCF)

**On Save (Compose Mode)**:

1. **Required Fields**:
   - `sprk_to` (To): Error message: "At least one recipient (To, CC, or BCC) is required"
   - `sprk_subject` (Subject): Error message: "Subject is required"
   - `sprk_body` (Body): Error message: "Email body is required"
   - `sprk_regardingrecordtype` (Record Type): Error message: "Please link this communication to a record (Matter, Project, etc.)"

2. **Email Format Validation**:
   - `sprk_to`, `sprk_cc`, `sprk_bcc`: Semicolon-delimited addresses
   - Validate each address: RFC 5322 basic email format (PCF validator)
   - Error message: "Invalid email address: {address}"

3. **Body Length**:
   - Max 100,000 characters
   - Warn if approaching limit (e.g., ">95,000 characters")

**On Save (Read Mode)**:
- All fields locked to prevent accidental modification
- Save button disabled (only form admin can edit sent records)

---

## Form Commands & Actions

### Compose Mode Command Bar

**When**: statuscode = 1 (Draft)

| Command | Label | Icon | Action | Notes |
|---------|-------|------|--------|-------|
| Send | "Send" | Send/Mail icon | Call BFF `POST /api/communications/send` | After send, refresh form; status changes to Queued/Send; locks all fields. |
| SaveDraft | "Save Draft" | Save icon | Standard Dataverse save | Persist Draft without sending |
| Cancel | "Cancel" | X icon | Navigate back / close form | Confirm unsaved changes |

**Send Command Implementation** (Form JavaScript or PCF):

```javascript
// Pseudo-code for Send command
async function sendCommunication() {
  // Validate all required fields first
  if (!validateComposeForm()) return;

  // Collect form data
  const request = {
    type: 'email',
    to: form.getAttributeValue('sprk_to').split(/[;,]/).map(a => a.trim()),
    cc: form.getAttributeValue('sprk_cc')?.split(/[;,]/).map(a => a.trim()) || [],
    bcc: form.getAttributeValue('sprk_bcc')?.split(/[;,]/).map(a => a.trim()) || [],
    subject: form.getAttributeValue('sprk_subject'),
    body: form.getAttributeValue('sprk_body'),
    bodyFormat: form.getAttributeValue('sprk_bodyformat') === 100000001 ? 'html' : 'text',
    associations: [
      {
        entity: recordTypeLogicalName, // From lookup
        id: recordId, // From AssociationResolver
        name: form.getAttributeValue('sprk_regardingrecordname'),
        role: 'primary'
      }
    ],
    archiveToSpe: true,
    containerId: getContextValue('containerId'), // From matter context if applicable
    initiatedBy: getCurrentUserGuid(),
    correlationId: generateCorrelationId()
  };

  // Call BFF
  try {
    const response = await authenticatedFetch(
      `${getBffBaseUrl()}/api/communications/send`,
      { method: 'POST', body: JSON.stringify(request) }
    );

    if (response.ok) {
      const result = await response.json();
      // Update form with response data
      form.getAttributeValue('sprk_graphmessageid').setValue(result.graphMessageId);
      form.getAttributeValue('sprk_sentat').setValue(new Date(result.sentAt));
      form.getAttributeValue('statuscode').setValue(Xrm.Page.context.getQueryStringParameters().status || 2); // Send status
      form.save();
      showNotification('success', 'Communication sent successfully');
    } else {
      const error = await response.json();
      showNotification('error', `Send failed: ${error.message}`);
    }
  } catch (err) {
    showNotification('error', `Send failed: ${err.message}`);
  }
}
```

### Read Mode Command Bar

**When**: statuscode != 1 (sent, delivered, failed, etc.)

| Command | Label | Icon | Action | Notes |
|---------|-------|------|--------|-------|
| OpenRegarding | "Open Record" | Open/Link icon | Navigate to associated record | Open the linked Matter, Project, etc. |
| ViewArchived | "View Archived" | Document icon | Open archived .eml document in SPE | If archived document exists |
| Edit | "Edit" | Edit icon | Disabled (form is locked) | Only for system admin/form customizer |

---

## Form Sections Summary

```
┌─────────────────────────────────────────────────────────┐
│                COMPOSE TAB (Draft Mode)                 │
├─────────────────────────────────────────────────────────┤
│                                                          │
│  Header:                                                │
│  ┌────────────────────────────────────────────────────┐ │
│  │ Type: [Email ▼]        Status: Draft               │ │
│  │ From: [legal-notifications@firm.com]              │ │
│  └────────────────────────────────────────────────────┘ │
│                                                          │
│  ── Association (Collapsible) ─────────────────────── │
│  │ Record Type: [Matter ▼]                            │ │
│  │ [AssociationResolver PCF - entity & record lookup] │ │
│  │ Record Name: [Smith v. Jones]                      │ │
│  │ Record ID: [a1b2c3d4-...]  (read-only)            │ │
│  │ Record URL: [Link]          (read-only)            │ │
│  └────────────────────────────────────────────────────┘ │
│                                                          │
│  ── Email Details ───────────────────────────────────── │
│  │ To:      [client@example.com; another@...]        │ │
│  │ CC:      [partner@lawfirm.com]                    │ │
│  │ BCC:     [internal@firm.com]                      │ │
│  │ Subject: [New Matter: Smith v. Jones             ] │ │
│  │                                                    │ │
│  │ Body: [Rich HTML editor ──────────────────────]  │ │
│  │       │ Dear Client,                             │ │
│  │       │ We are pleased to confirm...             │ │
│  │       └──────────────────────────────────────────┘ │
│  └────────────────────────────────────────────────────┘ │
│                                                          │
│  ── Attachments (Collapsible) ────────────────────── │
│  │ [+ Add Document]                                  │ │
│  │ 📄 Engagement Letter.pdf (245 KB)        [Remove] │ │
│  │ 📄 NDA Draft.docx (128 KB)               [Remove] │ │
│  └────────────────────────────────────────────────────┘ │
│                                                          │
│  [Send]  [Save Draft]  [Cancel]                         │
└─────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────┐
│                DETAILS TAB (Read Mode)                   │
├─────────────────────────────────────────────────────────┤
│                                                          │
│  Header:                                                │
│  ┌────────────────────────────────────────────────────┐ │
│  │ Type: Email  Status: Sent  From: legal-notif@...  │ │
│  └────────────────────────────────────────────────────┘ │
│                                                          │
│  ── Association ───────────────────────────────────── │
│  │ Record Type: Matter (read-only)                   │ │
│  │ Record Name: Smith v. Jones [Open Link]          │ │
│  │ Record ID: a1b2c3d4-... (read-only)              │ │
│  └────────────────────────────────────────────────────┘ │
│                                                          │
│  ── Email Content ────────────────────────────────── │
│  │ To: client@example.com; another@example.com      │ │
│  │ CC: partner@lawfirm.com                          │ │
│  │ Subject: New Matter: Smith v. Jones              │ │
│  │ Body: [Rendered HTML view]                       │ │
│  └────────────────────────────────────────────────────┘ │
│                                                          │
│  ── Tracking Details (Collapsible) ──────────────── │
│  │ Direction: Outgoing                              │ │
│  │ Communication Type: Email                        │ │
│  │ Correlation ID: create-matter-abc123             │ │
│  │ Graph Message ID: AAMk...                        │ │
│  │ Sent At: Feb 20, 2026 2:30 PM                    │ │
│  │ Sent By: Ralph Schroeder                         │ │
│  │ Error Message: (empty)                           │ │
│  │ Retry Count: 0                                   │ │
│  └────────────────────────────────────────────────────┘ │
│                                                          │
│  [Open Record]  [View Archived]                         │
└─────────────────────────────────────────────────────────┘
```

---

## Phase 3 vs. Phase 4 Features

### Phase 3 (Current)
- ✅ Compose form with Type, Association, Email Details
- ✅ Read form with Email Content and Tracking Details
- ✅ Business rules for auto-naming and field locking
- ✅ AssociationResolver PCF control for single-entity association
- ✅ Send button (calls BFF `POST /api/communications/send`)
- ⏳ Attachment subgrid (placeholder only)

### Phase 4 (Deferred)
- 📋 Full attachment subgrid implementation
- 📋 Document picker for attaching existing SPE documents
- 📋 Attachment removal from Draft communications
- 📋 Display attachment list in Read mode with "Open in SPE" links

---

## FetchXML for Form Visibility

### Show Compose Tab (Draft Only)

```xml
<fetch>
  <entity name="sprk_communication">
    <attribute name="sprk_communicationid" />
    <filter>
      <condition attribute="statuscode" operator="eq" value="1" />
    </filter>
  </entity>
</fetch>
```

### Show Details Tab (Non-Draft)

```xml
<fetch>
  <entity name="sprk_communication">
    <attribute name="sprk_communicationid" />
    <filter>
      <condition attribute="statuscode" operator="ne" value="1" />
    </filter>
  </entity>
</fetch>
```

### Show Tracking Section (Sent Communications)

```xml
<fetch>
  <entity name="sprk_communication">
    <attribute name="sprk_communicationid" />
    <filter>
      <condition attribute="statuscode" operator="in">
        <value>659490002</value>  <!-- Send -->
        <value>659490003</value>  <!-- Delivered -->
        <value>659490004</value>  <!-- Failed -->
        <value>659490005</value>  <!-- Bounded -->
        <value>659490006</value>  <!-- Recalled -->
      </condition>
    </filter>
  </entity>
</fetch>
```

---

## Field Visibility Matrix

| Field | Compose Mode (Draft=1) | Read Mode (Non-Draft) | Notes |
|-------|:--------------------:|:--------------------:|-------|
| sprk_name | Read-only | Read-only | Auto-generated |
| sprk_communiationtype | Editable | Read-only | |
| statuscode | Display | Display | |
| sprk_from | Editable | Read-only | |
| **ASSOCIATION SECTION** | | | |
| sprk_regardingrecordtype | Required, Editable | Read-only | Drives AssociationResolver |
| AssociationResolver PCF | Visible | Hidden | Phase 3 only in Compose |
| sprk_regardingmatter (et al) | Hidden | Hidden | Mapped by AssociationResolver |
| sprk_regardingrecordname | Read-only (auto) | Read-only | Auto-populated |
| sprk_regardingrecordid | Read-only (auto) | Read-only | Auto-populated |
| sprk_regardingrecordurl | Read-only (auto) | Read-only | Auto-populated |
| sprk_associationcount | Read-only (auto) | Read-only | Always 1 for Phase 3 |
| **EMAIL DETAILS SECTION** | | | |
| sprk_to | Required, Editable | Read-only | |
| sprk_cc | Editable | Read-only | |
| sprk_bcc | Editable | Read-only | Hidden in Read mode |
| sprk_subject | Required, Editable | Read-only | |
| sprk_body | Required, Editable | Read-only (HTML render) | |
| sprk_bodyformat | Hidden (default HTML) | Hidden | |
| **ATTACHMENTS SECTION** | | | |
| sprk_hasattachments | Read-only (auto) | Read-only | |
| sprk_attachmentcount | Read-only (auto) | Read-only | |
| Attachment Subgrid | Phase 4 | Phase 4 | Placeholder in Phase 3 |
| **TRACKING SECTION** | | | |
| sprk_direction | Hidden | Read-only | |
| sprk_correlationid | Hidden | Read-only | |
| sprk_graphmessageid | Hidden | Read-only | |
| sprk_sentat | Hidden | Read-only | |
| sprk_sentby | Hidden | Read-only | |
| sprk_errormessage | Hidden | Read-only | Only if status=Failed |
| sprk_retrycount | Hidden | Read-only | |

---

## Form Customization Checklist

### Implementation Steps

- [ ] Create main form "Communication" in the `sprk_communication` entity
- [ ] Add header section with Type, Status, From fields
- [ ] Create "Compose" tab with sections:
  - [ ] Association (with AssociationResolver PCF control)
  - [ ] Email Details (To, CC, BCC, Subject, Body, BodyFormat)
  - [ ] Attachments (placeholder subgrid for Phase 4)
- [ ] Create "Details" tab with sections:
  - [ ] Email Content (To, CC, Subject, Body in read-only mode)
  - [ ] Tracking (Direction, Type, Correlation ID, Graph Message ID, Sent At, Sent By, Error, Retry Count)
- [ ] Create Business Rule: BR_Communication_AutoGenerateName
  - [ ] Trigger: Create or change Type/Subject
  - [ ] Action: Set sprk_name = "{Type}: {Subject}"
- [ ] Create Form JavaScript to handle Send button:
  - [ ] Validate required fields (To/CC/BCC, Subject, Body, Associated record)
  - [ ] Call BFF `POST /api/communications/send`
  - [ ] Update form with response (Graph Message ID, Sent At)
  - [ ] Refresh form to show Details tab
- [ ] Set form default values:
  - [ ] sprk_communiationtype = 100000000 (Email)
  - [ ] sprk_direction = 100000001 (Outgoing)
  - [ ] statuscode = 1 (Draft)
  - [ ] sprk_bodyformat = 100000001 (HTML)
- [ ] Set form section visibility rules:
  - [ ] Show Compose tab when statuscode == 1
  - [ ] Show Details tab when statuscode != 1
  - [ ] Hide Tracking section when statuscode == 1
- [ ] Configure form commands:
  - [ ] Hide/disable standard Save button in Read mode (optional)
  - [ ] Add Send button command (Compose mode only)
  - [ ] Add Open Record button command (Read mode)
  - [ ] Add View Archived button command (Read mode, if archived document exists)
- [ ] Test form behaviors:
  - [ ] New record defaults to Draft with Compose tab active
  - [ ] Type + Subject auto-populate sprk_name
  - [ ] Send button calls BFF and updates form
  - [ ] After send, Details tab shows tracking information
  - [ ] All compose fields are read-only in Read mode
  - [ ] Email body renders as HTML in Read mode

---

## Notes & Future Enhancements

### Phase 4: Attachment Subgrid

Replace the placeholder Attachments section with a fully functional subgrid:

```xml
<control id="attachment-subgrid" classid="{3EED6B9D-5C6E-4F7D-AC59-76DADB2B9B7C}">
  <parameters>
    <ViewId><!-- sprk_communicationattachment view GUID --></ViewId>
    <RelationshipName>sprk_communication_attachment</RelationshipName>
    <TargetEntityType>sprk_communicationattachment</TargetEntityType>
    <AllowAddNewRecords>true</AllowAddNewRecords>
    <AllowDelete>true</AllowDelete>
  </parameters>
</control>
```

Configuration:
- Enable "Add Existing" button (document picker)
- Pre-filter documents to associated entity (if Matter selected, show only Matter documents)
- Display: Document Name, Type, Size
- Actions: Open in SPE, Remove from communication

### Multi-Record Association (Phase 6+)

In the future, a `sprk_communicationassociation` child entity will allow linking one communication to multiple records. The form will add:
- Second subgrid for related associations
- Role field: Primary, Related, CC, Billing
- No changes to primary association pattern

### Form Localization

All field labels, section headers, and placeholder text should be localization-ready:
- Use Dataverse Display Names for labels (auto-localized)
- Use resource strings for form JavaScript messages
- No hard-coded English text in form logic

---

## References

- **Entity Schema**: `/docs/data-model/sprk_communication-data-schema.md`
- **Design Document**: `/projects/email-communication-solution-r1/design.md`
- **AssociationResolver PCF**: Production control, reused from `sprk_event` form
- **BFF Communication Endpoints**: `CommunicationEndpoints.cs` (POST /api/communications/send)
- **Dataverse Choice Values**:
  - sprk_communiationtype: Email=100000000, Teams=100000001, SMS=100000002, Notification=100000003
  - statuscode: Draft=1, Queued=659490001, Send=659490002, Delivered=659490003, Failed=659490004, Bounded=659490005, Recalled=659490006
  - sprk_direction: Incoming=100000000, Outgoing=100000001
  - sprk_bodyformat: PlainText=100000000, HTML=100000001

---

*Last Updated: 2026-02-21*
