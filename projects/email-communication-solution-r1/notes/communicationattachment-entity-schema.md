# sprk_communicationattachment Entity Schema

**Last Updated**: February 21, 2026

## Entity Overview

The `sprk_communicationattachment` entity serves as an intersection table linking `sprk_communication` records to their attached `sprk_document` records from SharePoint Embedded (SPE). This entity enables communications (emails, Teams messages, etc.) to include file attachments and inline images sourced from the SPE file store.

### Entity Definition

| Property | Value |
|----------|-------|
| **Display Name** | Communication Attachment |
| **Plural Display Name** | Communication Attachments |
| **Logical Name** | `sprk_communicationattachment` |
| **Ownership Type** | Organization-owned |
| **Primary Name Field** | `sprk_name` |
| **Notes** | This entity does not exist yet and must be created manually in Dataverse. Unmanaged solution recommended for development/testing. |

---

## Fields (Custom Attributes)

### 1. sprk_name
**Primary Name Field**

| Property | Value |
|----------|-------|
| **Display Name** | Name |
| **Logical Name** | `sprk_name` |
| **Schema Name** | `sprk_Name` |
| **Attribute Type** | Single Line of Text |
| **Format** | Text |
| **Max Length** | 200 characters |
| **Required Level** | Business Required |
| **Description** | Auto-populated from the attached document name. Used for display and identification in communications. |
| **User Can Edit** | No (managed by BFF service) |

**Notes**:
- This field is automatically populated by the BFF API when creating attachments.
- Provides a human-readable reference to the attachment without requiring lookup resolution.
- Example: "Financial_Report_Q4_2025.pdf"

---

### 2. sprk_communication
**Parent Communication Lookup**

| Property | Value |
|----------|-------|
| **Display Name** | Communication |
| **Logical Name** | `sprk_communication` |
| **Schema Name** | `sprk_Communication` |
| **Attribute Type** | Lookup (N:1 Relationship) |
| **Target Entity** | `sprk_communication` |
| **Required Level** | Business Required |
| **Description** | The parent communication record to which this document is attached. Each attachment must belong to exactly one communication. |
| **User Can Edit** | No (set by BFF service) |

**Relationship Details**:
- **Relationship Name**: `sprk_communicationattachment_communication`
- **Cardinality**: N:1 (many attachments per communication)
- **Delete Behavior**: Cascade Delete
  - When a `sprk_communication` record is deleted, all associated `sprk_communicationattachment` records are automatically deleted.
  - This ensures no orphaned attachments remain after communication deletion.

**Navigation Properties**:
- From `sprk_communication`: Access attachments via the 1:N relationship navigation property

---

### 3. sprk_document
**Attached Document Lookup**

| Property | Value |
|----------|-------|
| **Display Name** | Document |
| **Logical Name** | `sprk_document` |
| **Schema Name** | `sprk_Document` |
| **Attribute Type** | Lookup (N:1 Relationship) |
| **Target Entity** | `sprk_document` |
| **Required Level** | Business Required |
| **Description** | The document from SharePoint Embedded that is attached to this communication. References the sprk_document entity representing the file in SPE. |
| **User Can Edit** | No (set by BFF service) |

**Relationship Details**:
- **Relationship Name**: `sprk_communicationattachment_document`
- **Cardinality**: N:1 (many attachments can reference the same document)
  - Multiple communications can reference the same document without creating duplicates.
  - Supports scenarios where a document is shared across multiple communications.
- **Delete Behavior**: Cascade Restrict
  - Prevents deletion of a `sprk_document` while it is still attached to any `sprk_communication`.
  - Ensures referential integrity: you cannot delete a document if it's actively used in a communication.
  - Error message shown to user: "This document is currently attached to one or more communications and cannot be deleted."

**Navigation Properties**:
- From `sprk_document`: Access all communications using this document via the 1:N relationship navigation property

---

### 4. sprk_attachmenttype
**Attachment Type Choice**

| Property | Value |
|----------|-------|
| **Display Name** | Attachment Type |
| **Logical Name** | `sprk_attachmenttype` |
| **Schema Name** | `sprk_AttachmentType` |
| **Attribute Type** | Choice (Picklist) |
| **Required Level** | Business Required |
| **Default Value** | File (100000000) |
| **Description** | Indicates how the document should be treated in the communication: as a file attachment or as an inline image. |

**Options**:

| Display Value | Logical Value | Use Case |
|---------------|---------------|----------|
| File | 100000000 | Standard file attachment (PDF, Word, Excel, etc.). Downloadable from communication view. |
| InlineImage | 100000001 | Inline image embedded in the communication body (JPEG, PNG, GIF, etc.). Rendered directly in email/Teams message. |

**Notes**:
- File attachments: User can download/save the document
- Inline images: Rendered directly in communication content (email body, Teams message body)
- BFF service determines type based on document MIME type
- Default to File if type cannot be determined

---

## Standard System Fields

The following system-managed fields are automatically maintained by Dataverse:

| Field Name | Type | Description |
|-----------|------|-------------|
| `sprk_communicationattachmentid` | Uniqueidentifier | Primary key; unique identifier for the attachment record. |
| `createdby` | Lookup | User who created the attachment record (system-set by BFF service). |
| `createdon` | DateTime | Timestamp when the attachment was created. |
| `modifiedby` | Lookup | User who last modified the attachment record. |
| `modifiedon` | DateTime | Timestamp of the last modification. |
| `ownerid` | Owner | Organization owner (all records owned by organization). |
| `statecode` | State | Active/Inactive status (always Active in normal operation). |
| `statuscode` | Status | Status reason (typically Active). |
| `versionnumber` | BigInt | Version number for optimistic concurrency. |

---

## Relationships

### 1. N:1 to sprk_communication (Parent)

```
sprk_communicationattachment.sprk_communication → sprk_communication
```

| Property | Value |
|----------|-------|
| **Relationship Name** | `sprk_communicationattachment_communication` |
| **Cardinality** | N:1 (Many attachments per communication) |
| **Delete Behavior** | **Cascade Delete** |
| **Restrict Edit** | No |

**Cascade Delete Impact**:
- When a communication is deleted, all of its attachments are automatically removed.
- No orphaned attachment records can exist without a parent communication.

---

### 2. N:1 to sprk_document (Referenced Document)

```
sprk_communicationattachment.sprk_document → sprk_document
```

| Property | Value |
|----------|-------|
| **Relationship Name** | `sprk_communicationattachment_document` |
| **Cardinality** | N:1 (Many attachments can reference the same document) |
| **Delete Behavior** | **Cascade Restrict** |
| **Restrict Edit** | No |

**Cascade Restrict Impact**:
- Prevents deletion of a document that is actively attached to a communication.
- Users cannot accidentally delete documents that are in use.
- Error message: "This document is currently attached to one or more communications and cannot be deleted."
- Document can only be deleted after removing all attachment records that reference it.

---

## Security and Access Control

### Ownership Model

- **Ownership Type**: Organization-owned
- **Owner**: Organization (no individual user or team ownership)
- **Implication**: All users with appropriate privileges can access all attachment records
  - Access is not restricted by record ownership
  - Security is managed at the entity and field level via security roles

### Security Role Inheritance

The `sprk_communicationattachment` entity **inherits security roles and privileges from the parent `sprk_communication` entity**:

| Operation | Requirement | Notes |
|-----------|------------|-------|
| **Read** | User must have **Read** privilege on `sprk_communication` | Users can read attachments only if they can read the parent communication. |
| **Create** | Managed by BFF API service | Manual creation by users is not supported; only the BFF service creates attachments. |
| **Update** | Managed by BFF API service | Manual updates not supported; only system/service updates occur. |
| **Delete** | Managed by BFF API service or cascade from communication deletion | Manual deletion by users is not supported. |

### Privilege Assignment Workflow

1. **Security role** (e.g., "Sales Manager") includes **Read** privilege on `sprk_communication` entity
2. Users with this role gain **Read** access to all `sprk_communication` records they are assigned to
3. Those users automatically gain **Read** access to all `sprk_communicationattachment` records attached to those communications
4. **No explicit privilege configuration needed** on `sprk_communicationattachment` — inheritance provides the needed access

### Data Sharing Consideration

- If a communication is shared with a user (via record sharing), the user can read the communication and all of its attachments
- Attachment visibility is transitive through the parent communication record

---

## Field Requirements Summary

| Field | Required | Editable by Users | Managed by |
|-------|----------|------------------|-----------|
| `sprk_name` | Yes | No | BFF API (auto-populated from document) |
| `sprk_communication` | Yes | No | BFF API (set when attachment created) |
| `sprk_document` | Yes | No | BFF API (set when attachment created) |
| `sprk_attachmenttype` | Yes | No | BFF API (determined by document MIME type) |

---

## Creation Workflow (BFF Service)

This entity is managed entirely by the BFF API service. Users do not manually create attachments.

### Typical Creation Flow

1. User attaches a document from SPE to a draft communication via the BFF API
2. BFF API (Sprk.Bff.Api) calls the Dataverse Web API to create a `sprk_communicationattachment` record with:
   - `sprk_name` ← Document file name from SPE
   - `sprk_communication` ← Communication record ID
   - `sprk_document` ← Document record ID
   - `sprk_attachmenttype` ← Determined by document MIME type
3. Dataverse creates the record with system fields (id, createdby, createdon, etc.)
4. Record is now visible in the communication's attachment subgrid

---

## Deletion Workflow

### Direct Deletion (Cascade Restrict on Document)

When attempting to delete a `sprk_document`:
- Dataverse checks for any `sprk_communicationattachment` records referencing it
- If found: **Deletion blocked** with error message
- If none: Deletion succeeds

### Indirect Deletion (Cascade Delete from Communication)

When deleting a `sprk_communication`:
- Dataverse automatically deletes all `sprk_communicationattachment` records with `sprk_communication` = this communication
- All associated attachment records are removed in cascading fashion
- Document records are **not** deleted (only the relationship is severed)

---

## Usage Examples

### Query All Attachments for a Communication

**REST API (Power Platform Web API)**:
```http
GET /api/data/v9.2/sprk_communicationattachments?$filter=sprk_communication/sprk_communicationid eq (COMMUNICATION_ID)&$select=sprk_name,sprk_attachmenttype
```

**Result**: All attachments linked to a specific communication with their names and types.

---

### Query All Communications Using a Specific Document

**REST API**:
```http
GET /api/data/v9.2/sprk_communicationattachments?$filter=sprk_document/sprk_documentid eq (DOCUMENT_ID)&$expand=sprk_communication
```

**Result**: All communications that reference a specific document.

---

### Prevent Document Deletion (Cascade Restrict Check)

**REST API (attempting DELETE)**:
```http
DELETE /api/data/v9.2/sprk_documents(DOCUMENT_ID)
```

**If attachments exist**:
- Response: HTTP 400 Bad Request
- Error message: "This document is currently attached to one or more communications and cannot be deleted."
- Action: Remove all attachments referencing this document first (via BFF API), then retry deletion.

---

## Configuration Checklist for Manual Entity Creation

When manually creating the `sprk_communicationattachment` entity in Dataverse, follow this checklist:

### Entity Creation
- [ ] Display Name: "Communication Attachment"
- [ ] Plural Name: "Communication Attachments"
- [ ] Logical Name: `sprk_communicationattachment`
- [ ] Ownership: Organization-owned
- [ ] Primary Name Field: `sprk_name`

### Field Creation
- [ ] Create `sprk_name` (Single Line of Text, Max 200, Business Required)
- [ ] Create `sprk_communication` (Lookup to sprk_communication, Business Required)
- [ ] Create `sprk_document` (Lookup to sprk_document, Business Required)
- [ ] Create `sprk_attachmenttype` (Choice: File=100000000, InlineImage=100000001, Default=File, Business Required)

### Relationship Configuration
- [ ] Create N:1 relationship from sprk_communicationattachment to sprk_communication
  - Relationship Name: `sprk_communicationattachment_communication`
  - Delete Behavior: **Cascade Delete**
- [ ] Create N:1 relationship from sprk_communicationattachment to sprk_document
  - Relationship Name: `sprk_communicationattachment_document`
  - Delete Behavior: **Cascade Restrict**

### Security Role Setup
- [ ] Add `sprk_communicationattachment` to solution
- [ ] Assign Read privilege to relevant security roles (inherited from sprk_communication)
- [ ] Verify no manual Create/Update/Delete privileges needed (service-managed)

### Solution Publication
- [ ] Save all customizations
- [ ] Publish the unmanaged solution
- [ ] Verify entity appears in Dataverse

---

## Constraints and Limitations

1. **Service-Managed Only**: This entity is managed exclusively by the BFF API service. No manual user operations are supported.

2. **No Plugin Logic**: Per ADR-002, thin plugins only. This entity is a simple intersection table and requires no plugin logic.

3. **Cascade Restrict on Document**: Documents cannot be deleted while attached. Users must remove attachments first.

4. **Cascade Delete on Communication**: Removing a communication automatically removes all its attachments.

5. **Organization Ownership**: All records are owned by the organization, not individual users. Access control is via the parent communication record security.

---

## Related Entities

| Related Entity | Relationship | Purpose |
|----------------|-------------|---------|
| `sprk_communication` | 1:N Parent | The communication record containing the attachments |
| `sprk_document` | 1:N Referenced Document | The SPE document being attached |

---

## Testing Considerations

### Unit Test Scenarios
1. Create attachment with valid communication + document → Record created successfully
2. Create attachment with invalid communication ID → Error (foreign key violation)
3. Create attachment with invalid document ID → Error (foreign key violation)
4. Create attachment without sprk_attachmenttype → Defaults to File (100000000)

### Integration Test Scenarios
1. Delete communication → All attachments cascade deleted
2. Delete document with active attachments → Error with restrict message
3. Delete document after removing all attachments → Succeeds
4. Query attachments by communication ID → Correct records returned
5. Access control: User without read access to communication → Cannot read attachments

### Performance Considerations
- Ensure indexes on `sprk_communication` and `sprk_document` lookups for fast filtering
- Expected attachment count per communication: 5-10 average, <20 max
- Expect query performance under 100ms for attachment subgrid display

---

## Notes

- This documentation was generated for Task 031 of the email-communication-solution-r1 project
- The entity schema is ready for manual creation in the Dataverse environment
- The BFF API (Sprk.Bff.Api) contains the implementation logic for attachment management
- See the email-communication-solution project specification for additional context on the attachment feature
