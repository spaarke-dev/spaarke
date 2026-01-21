# Task 013: Table Relationships and Indexes Configuration

> **Status**: Ready for Manual Execution
> **Type**: Dataverse Configuration
> **Location**: Power Apps Maker Portal → Tables

## Overview

Configure relationships between the new Office integration tables (EmailArtifact, AttachmentArtifact, ProcessingJob) and existing Spaarke entities. Set up cascade behaviors and indexes for efficient querying.

## Entity Relationship Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              Association Targets                             │
│                                                                             │
│   ┌──────────┐  ┌───────────┐  ┌───────────┐  ┌─────────┐  ┌─────────┐    │
│   │  Matter  │  │  Project  │  │  Invoice  │  │ Account │  │ Contact │    │
│   │ sprk_mat │  │ sprk_proj │  │ sprk_inv  │  │ account │  │ contact │    │
│   └────┬─────┘  └─────┬─────┘  └─────┬─────┘  └────┬────┘  └────┬────┘    │
│        │              │              │             │             │          │
│        └──────────────┴──────────────┴─────────────┴─────────────┘          │
│                                      │                                       │
│                           Lookup (exactly ONE)                               │
│                                      │                                       │
│                                      ▼                                       │
│   ┌──────────────────────────────────────────────────────────────────┐      │
│   │                       sprk_document (Document)                    │      │
│   │                                                                   │      │
│   │   PK: sprk_documentid                                            │      │
│   │   Fields: sprk_documentname, sprk_filename, sprk_filesize        │      │
│   │   SPE: sprk_graphdriveid, sprk_graphitemid                       │      │
│   │   Association: sprk_matter, sprk_project, sprk_invoice,          │      │
│   │                sprk_account, sprk_contact (exactly ONE set)      │      │
│   └───────────────────────────────┬──────────────────────────────────┘      │
│                                   │                                          │
│              ┌────────────────────┼────────────────────┐                    │
│              │                    │                    │                    │
│              │ CASCADE            │ RESTRICT           │ RESTRICT           │
│              │ DELETE             │ DELETE             │ DELETE             │
│              ▼                    ▼                    ▼                    │
│   ┌──────────────────┐  ┌──────────────────┐  ┌──────────────────┐         │
│   │  EmailArtifact   │  │  ProcessingJob   │  │  AttachmentArtifact│        │
│   │                  │  │                  │  │  (via Document)    │        │
│   │ sprk_document ──►│  │ sprk_document ──►│  │ sprk_document ───►│        │
│   │                  │  │ sprk_initiatedby │  │                    │        │
│   └────────┬─────────┘  └──────────────────┘  └──────────────────┘         │
│            │                                            ▲                   │
│            │ RESTRICT DELETE                            │ CASCADE DELETE    │
│            │                                            │                   │
│            └────────────────────────────────────────────┘                   │
│                                                                             │
│                        sprk_emailartifact ──► AttachmentArtifact            │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Relationship Definitions

### 1. EmailArtifact → Document

| Property | Value |
|----------|-------|
| **Lookup Field** | `sprk_document` |
| **Related Table** | `sprk_document` |
| **Relationship Type** | Many-to-One |
| **Cascade Delete** | **Cascade** (when Document deleted, EmailArtifact is deleted) |
| **Cascade Assign** | Cascade |
| **Cascade Share** | Cascade |
| **Cascade Unshare** | Cascade |
| **Cascade Reparent** | Cascade |
| **Cascade Merge** | NoCascade |

**Rationale**: When a Document is deleted, there's no reason to keep the email artifact metadata.

### 2. AttachmentArtifact → EmailArtifact

| Property | Value |
|----------|-------|
| **Lookup Field** | `sprk_emailartifact` |
| **Related Table** | `sprk_emailartifact` |
| **Relationship Type** | Many-to-One |
| **Cascade Delete** | **Restrict** (cannot delete EmailArtifact if AttachmentArtifacts exist) |
| **Cascade Assign** | Cascade |
| **Cascade Share** | Cascade |
| **Cascade Unshare** | Cascade |
| **Cascade Reparent** | Cascade |
| **Cascade Merge** | NoCascade |

**Rationale**: Prevents orphaned attachments. User must delete/re-associate attachments before deleting email.

### 3. AttachmentArtifact → Document

| Property | Value |
|----------|-------|
| **Lookup Field** | `sprk_document` |
| **Related Table** | `sprk_document` |
| **Relationship Type** | Many-to-One |
| **Cascade Delete** | **Cascade** (when Document deleted, AttachmentArtifact is deleted) |
| **Cascade Assign** | Cascade |
| **Cascade Share** | Cascade |
| **Cascade Unshare** | Cascade |
| **Cascade Reparent** | Cascade |
| **Cascade Merge** | NoCascade |

**Rationale**: AttachmentArtifact is metadata about the saved attachment. If the Document is deleted, the metadata is irrelevant.

### 4. ProcessingJob → Document

| Property | Value |
|----------|-------|
| **Lookup Field** | `sprk_document` |
| **Related Table** | `sprk_document` |
| **Relationship Type** | Many-to-One (nullable) |
| **Cascade Delete** | **Restrict** (cannot delete Document with active jobs) |
| **Cascade Assign** | NoCascade |
| **Cascade Share** | NoCascade |
| **Cascade Unshare** | NoCascade |
| **Cascade Reparent** | NoCascade |
| **Cascade Merge** | NoCascade |

**Rationale**: Prevents deleting documents while processing is in progress. Job completion/failure should be handled before deletion.

### 5. ProcessingJob → User (InitiatedBy)

| Property | Value |
|----------|-------|
| **Lookup Field** | `sprk_initiatedby` |
| **Related Table** | `systemuser` |
| **Relationship Type** | Many-to-One |
| **Cascade Delete** | **Restrict** (cannot delete User with job history) |
| **Cascade Assign** | NoCascade |
| **Cascade Share** | NoCascade |
| **Cascade Unshare** | NoCascade |
| **Cascade Reparent** | NoCascade |
| **Cascade Merge** | NoCascade |

**Rationale**: Audit trail - maintain history of who initiated jobs.

## Index Definitions

### EmailArtifact Indexes

| Index Name | Columns | Purpose |
|------------|---------|---------|
| `idx_emailartifact_headershash` | `sprk_headershash` | Duplicate detection by email headers hash |
| `idx_emailartifact_messageid` | `sprk_internetmessageid` | Lookup by Internet Message-ID |
| `idx_emailartifact_document_hash` | `sprk_document`, `sprk_headershash` | Compound index for duplicate check with specific document |

### AttachmentArtifact Indexes

| Index Name | Columns | Purpose |
|------------|---------|---------|
| `idx_attachmentartifact_email` | `sprk_emailartifact` | Find attachments for an email |
| `idx_attachmentartifact_contentid` | `sprk_contentid` | Lookup by Content-ID for inline images |

### ProcessingJob Indexes

| Index Name | Columns | Purpose |
|------------|---------|---------|
| `idx_processingjob_status_created` | `sprk_status`, `createdon` | Queue queries for pending/in-progress jobs |
| `idx_processingjob_idempotencykey` | `sprk_idempotencykey` | Idempotency check on job submission |
| `idx_processingjob_document` | `sprk_document` | Find jobs for a specific document |
| `idx_processingjob_initiatedby` | `sprk_initiatedby`, `createdon` | User's job history |

## Configuration Steps

### Step 1: Create EmailArtifact → Document Relationship

1. Navigate to: **Power Apps > Tables > EmailArtifact > Relationships**
2. Add new Many-to-One relationship:
   - Related table: `Document (sprk_document)`
   - Lookup column: `sprk_document`
   - Relationship behavior: **Referential, Restrict Delete = No** (Cascade Delete)

### Step 2: Create AttachmentArtifact → EmailArtifact Relationship

1. Navigate to: **Power Apps > Tables > AttachmentArtifact > Relationships**
2. Add new Many-to-One relationship:
   - Related table: `EmailArtifact (sprk_emailartifact)`
   - Lookup column: `sprk_emailartifact`
   - Relationship behavior: **Referential, Restrict Delete = Yes**

### Step 3: Create AttachmentArtifact → Document Relationship

1. Navigate to: **Power Apps > Tables > AttachmentArtifact > Relationships**
2. Add new Many-to-One relationship:
   - Related table: `Document (sprk_document)`
   - Lookup column: `sprk_document`
   - Relationship behavior: **Referential, Restrict Delete = No** (Cascade Delete)

### Step 4: Create ProcessingJob → Document Relationship

1. Navigate to: **Power Apps > Tables > ProcessingJob > Relationships**
2. Add new Many-to-One relationship:
   - Related table: `Document (sprk_document)`
   - Lookup column: `sprk_document`
   - Relationship behavior: **Referential, Restrict Delete = Yes**

### Step 5: Create ProcessingJob → User Relationship

1. Navigate to: **Power Apps > Tables > ProcessingJob > Relationships**
2. Add new Many-to-One relationship:
   - Related table: `User (systemuser)`
   - Lookup column: `sprk_initiatedby`
   - Relationship behavior: **Referential, Restrict Delete = Yes**

### Step 6: Create Indexes

**Note**: Dataverse automatically creates indexes for lookup columns. For additional indexes, use alternate keys or work with the platform team.

**Alternate Keys for Duplicate Detection**:

1. Navigate to: **Power Apps > Tables > EmailArtifact > Keys**
2. Add alternate key:
   - Name: `emailartifact_headershash_key`
   - Columns: `sprk_headershash`

3. Navigate to: **Power Apps > Tables > ProcessingJob > Keys**
4. Add alternate key:
   - Name: `processingjob_idempotencykey`
   - Columns: `sprk_idempotencykey`

## Verification Steps

### Test Cascade Delete (Document → EmailArtifact)

```
1. Create a Document record
2. Create an EmailArtifact record linked to the Document
3. Delete the Document
4. Verify: EmailArtifact is automatically deleted
```

### Test Restrict Delete (EmailArtifact → AttachmentArtifact)

```
1. Create an EmailArtifact record
2. Create an AttachmentArtifact record linked to the EmailArtifact
3. Attempt to delete the EmailArtifact
4. Verify: Delete is blocked with error message
5. Delete the AttachmentArtifact first
6. Verify: EmailArtifact can now be deleted
```

### Test Restrict Delete (ProcessingJob → Document)

```
1. Create a Document record
2. Create a ProcessingJob record (status: InProgress) linked to the Document
3. Attempt to delete the Document
4. Verify: Delete is blocked with error message
5. Update ProcessingJob status to Completed
6. Delete the ProcessingJob (or change Document lookup to null)
7. Verify: Document can now be deleted
```

### Test Lookup Navigation

```
1. Open an EmailArtifact record
2. Click the Document lookup field
3. Verify: Navigates to the related Document record
4. On the Document record, verify Related > EmailArtifacts shows the record
```

## PAC CLI Commands

```powershell
# Export solution containing the relationships
pac solution export --name SpaarkeOfficeIntegration --path ./export --managed false

# Check solution components
pac solution list-components --solution-name SpaarkeOfficeIntegration

# Import solution to another environment
pac solution import --path ./SpaarkeOfficeIntegration.zip --activate-plugins
```

## Related Documentation

- [schema-emailartifact.md](schema-emailartifact.md) - EmailArtifact table schema
- [schema-attachmentartifact.md](schema-attachmentartifact.md) - AttachmentArtifact table schema
- [schema-processingjob.md](schema-processingjob.md) - ProcessingJob table schema
- [DATAVERSE-HOW-TO-CREATE-UPDATE-SCHEMA.md](../../../../docs/guides/DATAVERSE-HOW-TO-CREATE-UPDATE-SCHEMA.md) - Schema guide

## Acceptance Criteria

- [ ] All lookups navigate correctly (both directions)
- [ ] Cascade delete works: Document deletion removes EmailArtifact
- [ ] Cascade delete works: Document deletion removes AttachmentArtifact
- [ ] Restrict delete works: Cannot delete EmailArtifact with AttachmentArtifacts
- [ ] Restrict delete works: Cannot delete Document with active ProcessingJobs
- [ ] Alternate keys created for duplicate detection queries
- [ ] Index queries perform within acceptable latency

---

*Execute these steps in Power Apps Maker Portal after completing Tasks 010, 011, 012 (table creation).*
