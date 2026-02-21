# Communication Form - Attachment Picker Configuration

> **Project**: email-communication-solution-r1
> **Phase**: 4: Attachments + Archival
> **Status**: Design Documentation
> **Date**: 2026-02-21
> **Related Tasks**: 030, 031, 032, 033, 036

---

## Overview

This document specifies the configuration and implementation of the document attachment picker for the `sprk_communication` model-driven form. The attachment picker enables users to select documents from SharePoint Embedded (SPE) and attach them to outbound communications (emails, Teams messages, etc.) before sending.

The attachment picker is implemented as a **Dataverse subgrid** bound to the `sprk_communicationattachment` intersection entity, allowing users to manage a many-to-many relationship between communications and documents.

---

## Functional Requirements

### FR-1: Attachment Subgrid Display
- Subgrid displays all `sprk_communicationattachment` records linked to the active communication
- Shows document name, attachment type (File/InlineImage), document file size
- Only visible and editable in Draft status (statuscode = 1)
- Read-only view in sent communications (statuscode != 1)

### FR-2: Add Existing Documents
- "Add Existing" button opens a lookup dialog to search and select `sprk_document` records
- Filter logic: restrict to documents linked to the same associated entity (e.g., if communication is linked to Matter "Smith v. Jones", show only documents on that matter)
- Support multi-select to add multiple documents in one operation
- Auto-populate `sprk_attachmenttype` (File by default, InlineImage for image documents)

### FR-3: Remove Attachments
- "Remove" button (bulk or row-level) removes attachment records from Draft communications
- Disabled in Read mode (non-Draft status)
- Cascade delete handled by `sprk_communicationattachment` delete relationship

### FR-4: Quick View Form
- Clicking an attachment row shows a quick view form with:
  - Document name (`sprk_name`)
  - Attachment type (`sprk_attachmenttype`)
  - Document link (clickable to open in SPE)
  - Created on / Last modified
- Quick view is read-only in all contexts

### FR-5: Header Attachment Count Badge
- Display `sprk_attachmentcount` field in form header for quick reference
- Show attachment count badge (e.g., "Attachments: 3")
- Update in real-time as attachments are added/removed

---

## Subgrid Configuration

### Entity and Relationship

| Property | Value |
|----------|-------|
| **Intersection Entity** | `sprk_communicationattachment` |
| **Parent Entity** | `sprk_communication` |
| **Relationship Name** | `sprk_communicationattachment_communication` |
| **Cardinality** | N:1 (many attachments per communication) |
| **Delete Behavior** | Cascade Delete (when communication deleted, all attachments removed) |

### Form Placement

| Property | Value |
|----------|-------|
| **Tab** | Compose |
| **Section** | Attachments |
| **Section Display Name** | "Attachments" |
| **Visibility Rule** | Show when statuscode = 1 (Draft) |
| **Collapsible** | Yes |
| **Show Header** | Yes |
| **Height** | Auto (min 250px for 3-5 attachments) |

### Subgrid Column Configuration

Display the following columns in the order specified:

| Column Name | Logical Name | Data Type | Width | Sortable | Filterable | Notes |
|-------------|-------------|-----------|-------|----------|-----------|-------|
| Document Name | `sprk_name` | Text | 50% | Yes | Yes | Primary display; auto-populated from document name |
| Attachment Type | `sprk_attachmenttype` | Choice | 20% | Yes | Yes | File or InlineImage |
| Document Link | `sprk_document` | Lookup | 30% | No | No | Clickable link to open document record |

**Note**: `sprk_attachmentcount` and `sprk_hasattachments` fields are not displayed in the subgrid; they are managed automatically by the BFF service.

### Button Configuration

#### Standard Subgrid Buttons (Enabled)

| Button | Action | Availability | Notes |
|--------|--------|--------------|-------|
| Add Existing | Open lookup dialog to add documents | Draft mode only | Opens filtered document picker |
| Remove | Delete selected attachment record(s) | Draft mode only | Bulk remove or single row |
| Refresh | Reload subgrid from server | Always | Standard Dataverse refresh |

#### Custom/Hidden Buttons

| Button | Status | Reason |
|--------|--------|--------|
| Add New | Hidden | BFF service creates attachments; users cannot manually create |
| Open Record | Customizable | Optional: Allow users to open document record in new window |

### Add Existing Configuration

The "Add Existing" button opens a standard Dataverse lookup dialog configured as follows:

#### Lookup Entity
- **Entity**: `sprk_document`
- **Multi-select**: Yes (allow selecting multiple documents at once)
- **Create New**: No (users cannot create documents from lookup; navigate to document entity instead)

#### Filter Logic

The lookup must apply a filter to show only documents relevant to the associated entity. This is the key functional difference from a basic subgrid.

**Filter Scenarios**:

1. **If communication is linked to a Matter**:
   ```odata
   Filter: sprk_container = {Matter.ContainerId}
   Display: Only documents in the Matter's container
   ```
   - Prevents attaching unrelated documents
   - Matches documents by SPE container (documents inherit container from their associated Matter)

2. **If communication is linked to a Project**:
   ```odata
   Filter: sprk_container = {Project.ContainerId}
   Display: Only documents in the Project's container
   ```

3. **If communication is linked to Organization or Contact (no container)**:
   ```odata
   Filter: (no container filter applied)
   Display: All documents in organization
   Action: Show warning: "Consider linking to a Matter or Project for more focused document selection"
   ```

4. **If no associated entity selected**:
   ```odata
   Filter: (no filter)
   Display: All documents in organization
   Action: Show notification: "Link this communication to a record first to filter documents"
   ```

**Implementation Note**: The container-based filter requires that:
- Matter entity has a `sprk_containerid` lookup to the SPE container
- Project entity has a `sprk_containerid` lookup (if projects use SPE containers)
- Documents are linked to containers via `sprk_containerid` field

If container navigation is not available, fall back to simple all-documents filter.

#### Lookup Configuration XML (Form Customization)

```xml
<control id="attachment-lookup" classid="{3EED6B9D-5C6E-4F7D-AC59-76DADB2B9B7C}">
  <parameters>
    <!-- Subgrid parameter -->
    <ViewId>{SUBGRID_VIEW_GUID}</ViewId>
    <RelationshipName>sprk_communicationattachment_communication</RelationshipName>
    <TargetEntityType>sprk_communicationattachment</TargetEntityType>
    <AllowAddNewRecords>true</AllowAddNewRecords>
    <AllowDelete>true</AllowDelete>

    <!-- Add Existing lookup configuration -->
    <LookupEntityName>sprk_document</LookupEntityName>
    <MultiSelect>true</MultiSelect>
    <CreateNewRecord>false</CreateNewRecord>

    <!-- Column definitions -->
    <ViewColumns>
      <ViewColumn Name="sprk_name" />
      <ViewColumn Name="sprk_attachmenttype" />
      <ViewColumn Name="sprk_document" />
    </ViewColumns>
  </parameters>
</control>
```

---

## Quick View Form Configuration

### Quick View Form: Communication Attachment Details

| Property | Value |
|----------|-------|
| **Entity** | `sprk_communicationattachment` |
| **Display Name** | Communication Attachment Details |
| **Form Type** | Quick View |
| **Mode** | Read-only (all fields displayed as read-only) |

### Quick View Fields

| Field | Logical Name | Display | Notes |
|-------|-------------|---------|-------|
| Document Name | `sprk_name` | Text (read-only) | Primary display field |
| Document Link | `sprk_document` | Lookup (read-only, clickable) | Opens document in same window or new tab |
| Attachment Type | `sprk_attachmenttype` | Choice (read-only) | File or InlineImage |
| Created On | `createdon` | DateTime (read-only) | System-managed timestamp |
| Modified On | `modifiedon` | DateTime (read-only) | System-managed timestamp |

### Quick View Form Body

```
┌──────────────────────────────────────────────────┐
│ Communication Attachment Details                 │
├──────────────────────────────────────────────────┤
│ Document Name:      Engagement Letter.pdf        │
│ Document Link:      [Open in SPE]               │
│ Attachment Type:    File                         │
│ Created On:         Feb 20, 2026 3:15 PM        │
│ Modified On:        Feb 20, 2026 3:15 PM        │
└──────────────────────────────────────────────────┘
```

---

## Form Visibility Rules

### Attachments Section Visibility

| Condition | Visibility |
|-----------|------------|
| statuscode = 1 (Draft) | **Visible and Editable** (full CRUD on subgrid) |
| statuscode != 1 (Sent, Delivered, Failed, etc.) | **Visible but Read-Only** (subgrid shows attachments, no add/remove buttons) |

**FetchXML Visibility Rule**:
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

### Subgrid Button Visibility (Draft Mode Only)

| Button | Visible | Enabled | Notes |
|--------|---------|---------|-------|
| Add Existing | Draft only | Draft only | Click opens lookup dialog |
| Remove | Draft only | Draft only | Select row, click Remove |
| Refresh | Always | Always | Standard refresh |
| Open Record | Always | Always | Customizable; opens document |

---

## Header Section - Attachment Count Badge

### Form Header Configuration

Add `sprk_attachmentcount` to the form header for quick visual reference:

| Property | Value |
|----------|-------|
| **Field** | `sprk_attachmentcount` |
| **Display Name** | Attachments |
| **Control Type** | Whole Number (read-only) |
| **Placement** | Form header (below Status field) |
| **Format** | Show as simple number with prefix: "Attachments: {count}" |

**Header Layout After Update**:
```
┌────────────────────────────────────────┐
│ Type: Email ▼     Status: Draft        │
│ From: legal@firm.com                  │
│ Attachments: 2                         │
└────────────────────────────────────────┘
```

### Header Field Behavior

- **Compose Mode**: Display attachment count (read-only, auto-updated)
- **Read Mode**: Display attachment count (read-only)
- **Real-time Update**: When attachments are added/removed in subgrid, header count updates immediately
- **Visual Indicator**: Could show attachment icon + count badge for emphasis

---

## Workflow: Adding Attachments

### Step-by-Step User Experience (Draft Mode)

1. **User opens Communication form** (new draft record or existing draft)
2. **User navigates to Compose tab → Attachments section**
3. **User clicks [+ Add Existing] button**
4. **System opens lookup dialog** for `sprk_document` records
5. **System applies filter** based on associated entity:
   - If Matter selected: Filter to documents in Matter's container
   - If Project selected: Filter to documents in Project's container
   - Otherwise: Show all documents
6. **User selects one or more documents** (checkbox multi-select)
7. **User clicks [Add] button in lookup dialog**
8. **System creates `sprk_communicationattachment` records**:
   - For each selected document, BFF/plugin creates an attachment record
   - `sprk_name` auto-populated from document name
   - `sprk_communication` set to current communication GUID
   - `sprk_document` set to selected document GUID
   - `sprk_attachmenttype` determined from document MIME type (default: File)
9. **Subgrid refreshes** automatically, showing new attachment rows
10. **Header attachment count updates** to reflect new total

### Remove Attachment Workflow (Draft Mode)

1. **User selects one or more attachment rows** in subgrid
2. **User clicks [Remove] button**
3. **System prompts for confirmation**: "Delete 1 attachment?"
4. **User confirms deletion**
5. **System deletes `sprk_communicationattachment` record(s)**
6. **Subgrid refreshes** automatically
7. **Header attachment count updates**

---

## Data Model Context

### Intersection Entity Fields

The `sprk_communicationattachment` entity is an intersection table with these key fields:

| Field | Type | Purpose | Managed By |
|-------|------|---------|-----------|
| `sprk_name` | Text | Document name (display only) | BFF service (auto-populated) |
| `sprk_communication` | Lookup | Parent communication | BFF service (set on create) |
| `sprk_document` | Lookup | Attached document | Form user (selected via picker) |
| `sprk_attachmenttype` | Choice | File or InlineImage | BFF service (determined by MIME type) |

**Relationship Details**:
- **sprk_communication**: N:1 with Cascade Delete behavior
  - Deleting a communication deletes all its attachments
  - No orphaned attachment records can exist
- **sprk_document**: N:1 with Cascade Restrict behavior
  - Prevents deletion of documents that are actively attached
  - User sees error: "This document is in use and cannot be deleted"

### Communication Entity Fields (Phase 4)

Two new fields track attachment state on `sprk_communication`:

| Field | Type | Purpose | Managed By |
|-------|------|---------|-----------|
| `sprk_hasattachments` | Boolean | Has attachments? | BFF service (auto-set on send) |
| `sprk_attachmentcount` | Whole Number (0-150) | Attachment count | BFF service (auto-set on send) |

**Note**: These fields are auto-set by the BFF **after send** (in CommunicationService), not during form editing. They represent the final state of attachments included in the sent email.

---

## Constraints and Limitations

### Dataverse Constraints

1. **Max 150 Attachments**: Graph API sendMail limit
   - Dataverse field `sprk_attachmentcount` has max value = 150
   - Form validation (if needed): Warn when adding attachment that would exceed limit

2. **Max 35 MB Total Size**: Graph API sendMail limit
   - BFF validates total attachment size before send
   - Form validation: Could show aggregate file size in subgrid footer
   - Recommendation: Show warning in Attachments section if total > 25 MB

3. **Cascade Delete**: Deleting a communication auto-deletes all attachments
   - No manual cleanup needed
   - Subgrid "Remove" button handles user-initiated deletions

4. **Document Read Lock**: Document cannot be deleted while attached
   - Cascade Restrict prevents deletion of attached documents
   - Users must remove attachment records first (via subgrid Remove button)

### Form Behavior Constraints

1. **Draft Only**: Add/Remove buttons disabled after send (statuscode != 1)
2. **Associated Entity Required**: Users should link communication to a record before attaching documents
   - Filter logic depends on associated entity (Matter, Project, etc.)
   - Show guidance message if no associated entity selected
3. **No Manual Attachment Creation**: Users cannot manually create attachment records
   - Only use the subgrid "Add Existing" button
   - BFF service creates attachments during send (Task 032)
   - Intersection table is service-managed

---

## Integration with BFF Service

### Send Process (CommunicationService)

When a communication is sent via `POST /api/communications/send`:

1. **BFF retrieves attachment records** linked to the communication
2. **For each attachment**, BFF calls `SpeFileStore.DownloadAsync()` to get file content
3. **Files converted to base64** and attached to Graph sendMail payload
4. **Graph sends email** with attachments to recipients
5. **On success**, BFF creates/updates communication record with:
   - `sprk_hasattachments = true` (if any attachments)
   - `sprk_attachmentcount = {number of attachments}`
6. **On failure**, BFF returns error; communication status = Draft (no attachments created yet)

**Important**: Attachment records exist in Dataverse **before send**. The BFF uses these records to hydrate the Graph sendMail request.

### Archive Process (Task 032)

After successful send:
1. **BFF generates .eml file** from email content
2. **Creates `sprk_document` record** with:
   - SourceType = CommunicationArchive
   - DocumentType = Communication
   - Path = `/communications/{commId:N}/{fileName}.eml`
3. **Archive document is created in SPE**, separate from user-attached documents

---

## Implementation Checklist

### Form Customization

- [ ] **Add subgrid control** to Attachments section (Compose tab)
  - Entity: `sprk_communicationattachment`
  - Relationship: `sprk_communicationattachment_communication`
  - Allow Add: Yes (Add Existing button)
  - Allow Delete: Yes (Remove button)
  - Allow Create: No (manual create not supported)

- [ ] **Configure subgrid columns**:
  - [ ] `sprk_name` (Document Name) - 50% width
  - [ ] `sprk_attachmenttype` (Attachment Type) - 20% width
  - [ ] `sprk_document` (Document Link) - 30% width

- [ ] **Add quick view form** for `sprk_communicationattachment`:
  - [ ] Display Name: "Communication Attachment Details"
  - [ ] Fields: sprk_name, sprk_document, sprk_attachmenttype, createdon, modifiedon
  - [ ] All read-only

- [ ] **Configure subgrid visibility**:
  - [ ] Show when statuscode = 1 (Draft)
  - [ ] Use FetchXML condition in form XML

- [ ] **Configure button visibility**:
  - [ ] Add Existing: Draft mode only
  - [ ] Remove: Draft mode only
  - [ ] Open Record: Always (optional custom button)

- [ ] **Add `sprk_attachmentcount` to form header**:
  - [ ] Field: `sprk_attachmentcount`
  - [ ] Display Name: "Attachments"
  - [ ] Read-only
  - [ ] Format: "Attachments: {count}" (custom label)

- [ ] **Set default values** on new record:
  - [ ] `sprk_hasattachments = false`
  - [ ] `sprk_attachmentcount = 0`

### Form Logic and Validation

- [ ] **Implement "Add Existing" filter logic**:
  - [ ] If Matter: Filter documents by Matter container
  - [ ] If Project: Filter documents by Project container
  - [ ] If Organization/Contact: No filter (show all docs)
  - [ ] If no associated entity: Show guidance message

- [ ] **Form validation** (optional):
  - [ ] Warn if attachment count > 100
  - [ ] Warn if total attachment size > 25 MB
  - [ ] Block add if count would exceed 150

- [ ] **Real-time updates**:
  - [ ] Attachment count badge updates when subgrid refreshed
  - [ ] `sprk_hasattachments` and `sprk_attachmentcount` updated by form script on row add/remove

### Testing

- [ ] **Unit Tests** (BFF):
  - [ ] Create attachment record with valid communication + document
  - [ ] Fail to create with invalid communication GUID
  - [ ] Fail to create with invalid document GUID
  - [ ] Attachment count correctly calculated

- [ ] **Form Tests**:
  - [ ] Add single document (Draft mode)
  - [ ] Add multiple documents at once
  - [ ] Remove single attachment
  - [ ] Remove all attachments
  - [ ] Verify subgrid not editable in Read mode
  - [ ] Verify header attachment count updates
  - [ ] Verify quick view form opens and displays correctly

- [ ] **Integration Tests**:
  - [ ] Send communication with attachments → sprk_attachmentcount updated
  - [ ] Send communication without attachments → sprk_attachmentcount = 0
  - [ ] After send, attachment subgrid in Read mode
  - [ ] Cannot add/remove attachments after send
  - [ ] Filter logic works (Matter container, Project container, etc.)

---

## Related Tasks and Dependencies

| Task | Title | Dependency | Status |
|------|-------|-----------|--------|
| 030 | Add attachment fields to communication entity | Required | Completed |
| 031 | Create sprk_communicationattachment intersection entity | Required | Completed |
| 032 | Implement attachment download and send | Required | In Progress |
| 033 | Implement attachment archival | Related | Pending |
| 036 | Add attachment picker to form | This task | In Progress |

---

## Alternative Approaches Considered

### Approach 1: Custom PCF Control (Rejected)
- **Pros**: Full control over UX, custom filtering, real-time validation
- **Cons**: Requires PCF development, testing complexity, maintenance burden
- **Decision**: Use native Dataverse subgrid + form logic instead

### Approach 2: Separate Attachment Wizard (Rejected)
- **Pros**: Standalone multi-step process, clear scope
- **Cons**: Navigates away from form, disrupts compose workflow
- **Decision**: Inline subgrid with "Add Existing" button keeps workflow focused

### Approach 3: Advanced Power Automate Flow for Document Selection (Rejected)
- **Pros**: Reusable logic, low-code solution
- **Cons**: Latency, complex trigger logic, hard to debug
- **Decision**: Native subgrid with simple Filter logic in form code

---

## Future Enhancements (Post Phase 4)

### Phase 5+: Multi-Entity Association
- Add `sprk_communicationassociation` child entity for secondary associations
- Extend attachment picker to show documents from all associated entities (not just primary)
- Example: Matter + Project communications can include documents from both containers

### Phase 5+: Inline Image Support
- UX for selecting inline images (sprk_attachmenttype = InlineImage)
- WYSIWYG editor integration: drag-drop images directly into body
- Auto-create attachment records for inline images

### Phase 6+: Bulk Attachment Management
- Import attachments from shared folder (external source)
- Bulk remove: select multiple, remove in batch
- Attachment reordering (for email display priority)

---

## Configuration Examples

### Example 1: Matter-Linked Communication

**Scenario**: User creates communication linked to Matter "Smith v. Jones"

```
Communication: "Email: New Matter Meeting"
Associated Entity: Matter (Smith v. Jones)
Matter Container ID: {container-guid-001}

Add Existing Documents:
- Filter: sprk_document.sprk_container = {container-guid-001}
- Results: 5 documents
  1. Engagement Letter.pdf (245 KB)
  2. NDA Draft.docx (128 KB)
  3. Case Overview.pptx (512 KB)
  4. Financial Proposal.xlsx (89 KB)
  5. Timeline.pdf (45 KB)

User selects: 1, 2, 3 (Engagement Letter, NDA Draft, Case Overview)
System creates 3 sprk_communicationattachment records
Subgrid shows: [Engagement Letter.pdf] [File] [link]
              [NDA Draft.docx] [File] [link]
              [Case Overview.pptx] [File] [link]
Attachment Count: 3
```

### Example 2: No Associated Entity

**Scenario**: User creates communication without linking to record first

```
Communication: "Email: Status Update"
Associated Entity: (none selected)

User clicks [+ Add Existing]:
- System applies no filter (no container)
- Filter logic shows guidance: "Link this communication to a record for relevant documents"
- User can:
  a) Cancel, go back, link to Matter/Project, then add documents
  b) Proceed and see all documents in organization (less ideal)

Results: All documents (100+)
User can still select specific documents if desired
```

---

## Notes

- This document focuses on **form configuration and user experience**
- **BFF service integration** is documented separately in CommunicationService (Task 032)
- **Entity schema** is documented in `communicationattachment-entity-schema.md`
- **Form XML** will be generated/exported from Dataverse after configuration is complete
- For implementation details on container-based filtering, see Matter entity definition and SPE container linking

---

## Related Documentation

- [sprk_communicationattachment Entity Schema](communicationattachment-entity-schema.md)
- [Communication Form Configuration](communication-form-config.md)
- [Attachment Fields Specification](attachment-fields-spec.md)
- [Email Communication Solution - Specification](../spec.md)
- [Entity Operations Pattern](./.claude/patterns/dataverse/entity-operations.md)

---

*Last Updated: February 21, 2026*
*Created for Task 036: Add document attachment picker to communication form*
