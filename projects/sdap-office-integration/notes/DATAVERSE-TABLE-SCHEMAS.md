# Dataverse Table Schemas for Office Integration

> **Created**: 2026-01-21
> **Purpose**: Manual table creation guide for Power Apps maker portal

---

## Table 1: EmailArtifact (sprk_emailartifact)

**Display Name**: Email Artifact
**Plural Name**: Email Artifacts
**Description**: Stores email metadata and body snapshots for emails saved from Outlook

### Fields

| Logical Name | Display Name | Type | Max Length | Required | Searchable | Notes |
|--------------|--------------|------|------------|----------|------------|-------|
| `sprk_name` | Name | Single Line Text | 400 | Yes | Yes | **Primary Name** - Auto-generated from Subject + Date |
| `sprk_subject` | Subject | Single Line Text | 400 | No | Yes | Email subject line |
| `sprk_sender` | Sender | Single Line Text | 320 | No | Yes | Email address of sender |
| `sprk_recipients` | Recipients | Multiple Lines of Text | 10000 | No | No | JSON array of recipient objects |
| `sprk_ccrecipients` | CC Recipients | Multiple Lines of Text | 10000 | No | No | JSON array of CC recipient objects |
| `sprk_sentdate` | Sent Date | Date and Time | - | No | No | When email was sent |
| `sprk_receiveddate` | Received Date | Date and Time | - | No | No | When email was received |
| `sprk_messageid` | Message ID | Single Line Text | 256 | No | No | Internet message ID from headers |
| `sprk_internetheadershash` | Headers Hash | Single Line Text | 64 | No | No | SHA256 hash for duplicate detection |
| `sprk_conversationid` | Conversation ID | Single Line Text | 256 | No | No | Email conversation/thread ID |
| `sprk_importance` | Importance | Choice | - | No | No | Values: Low (0), Normal (1), High (2) |
| `sprk_hasattachments` | Has Attachments | Yes/No | - | No | No | Boolean flag |
| `sprk_bodypreview` | Body Preview | Multiple Lines of Text | 2000 | No | Yes | First 2000 chars of email body |
| `sprk_document` | Document | Lookup | - | No | No | Lookup to Document (sprk_document) |

### Indexes

Create the following indexes for performance:

1. **Index on sprk_messageid** (for duplicate detection queries)
2. **Index on sprk_internetheadershash** (for duplicate detection queries)

### Creation Steps (Power Apps)

1. Go to https://make.powerapps.com
2. Select **Spaarke Dev** environment
3. Go to **Tables** → Click **New table** → **Add columns and data**
4. Set table properties:
   - Display name: `Email Artifact`
   - Plural name: `Email Artifacts`
   - Primary column: Keep default "Name"
5. Add all columns listed above
6. Click **Save table**
7. After table is created, go to **Settings** → **Indexes** → Add the two indexes

---

## Table 2: AttachmentArtifact (sprk_attachmentartifact)

**Display Name**: Attachment Artifact
**Plural Name**: Attachment Artifacts
**Description**: Tracks email attachments saved as separate documents

### Fields

| Logical Name | Display Name | Type | Max Length | Required | Searchable | Notes |
|--------------|--------------|------|------------|----------|------------|-------|
| `sprk_name` | Name | Single Line Text | 260 | Yes | Yes | **Primary Name** - Original filename |
| `sprk_originalfilename` | Original Filename | Single Line Text | 260 | No | Yes | Filename from email |
| `sprk_contenttype` | Content Type | Single Line Text | 100 | No | No | MIME type (e.g., application/pdf) |
| `sprk_size` | Size | Whole Number | - | No | No | File size in bytes |
| `sprk_contentid` | Content ID | Single Line Text | 256 | No | No | For inline attachments (embedded images) |
| `sprk_isinline` | Is Inline | Yes/No | - | No | No | True for embedded images in HTML |
| `sprk_emailartifact` | Email Artifact | Lookup | - | No | No | Lookup to EmailArtifact (sprk_emailartifact) |
| `sprk_document` | Document | Lookup | - | No | No | Lookup to Document (sprk_document) |

### Relationships

| Relationship | Type | Related Table | Cascade Behavior |
|--------------|------|---------------|------------------|
| `sprk_emailartifact` → EmailArtifact | N:1 | sprk_emailartifact | Restrict Delete |
| `sprk_document` → Document | N:1 | sprk_document | Restrict Delete |

### Creation Steps (Power Apps)

1. Go to **Tables** → Click **New table** → **Add columns and data**
2. Set table properties:
   - Display name: `Attachment Artifact`
   - Plural name: `Attachment Artifacts`
   - Primary column: Keep default "Name"
3. Add all columns listed above
4. When adding lookup columns:
   - **sprk_emailartifact**: Related table = `sprk_emailartifact`, Relationship = Many-to-One
   - **sprk_document**: Related table = `sprk_document`, Relationship = Many-to-One
5. Click **Save table**

---

## Table 3: ProcessingJob (sprk_processingjob)

**Display Name**: Processing Job
**Plural Name**: Processing Jobs
**Description**: Tracks async processing jobs for document uploads and email saves (ADR-004 compliant)

### Fields

| Logical Name | Display Name | Type | Max Length | Required | Searchable | Notes |
|--------------|--------------|------|------------|----------|------------|-------|
| `sprk_name` | Name | Single Line Text | 100 | Yes | Yes | **Primary Name** - Auto-generated job ID (GUID) |
| `sprk_jobtype` | Job Type | Choice | - | No | No | See choice values below |
| `sprk_status` | Status | Choice | - | Yes | No | See choice values below |
| `sprk_stages` | Stages | Multiple Lines of Text | 10000 | No | No | JSON array of stage definitions |
| `sprk_currentstage` | Current Stage | Single Line Text | 100 | No | No | Name of currently executing stage |
| `sprk_stagestatus` | Stage Status | Multiple Lines of Text | 10000 | No | No | JSON object tracking each stage's status |
| `sprk_progress` | Progress | Whole Number | - | No | No | 0-100 percentage |
| `sprk_starteddate` | Started Date | Date and Time | - | No | No | When job began processing |
| `sprk_completeddate` | Completed Date | Date and Time | - | No | No | When job finished (success or failure) |
| `sprk_errorcode` | Error Code | Single Line Text | 50 | No | No | Error code if failed (e.g., OFFICE_001) |
| `sprk_errormessage` | Error Message | Multiple Lines of Text | 2000 | No | No | Detailed error message |
| `sprk_retrycount` | Retry Count | Whole Number | - | No | No | Number of retry attempts |
| `sprk_idempotencykey` | Idempotency Key | Single Line Text | 64 | No | No | SHA256 hash for duplicate prevention |
| `sprk_correlationid` | Correlation ID | Single Line Text | 36 | No | No | GUID for distributed tracing |
| `sprk_initiatedby` | Initiated By | Lookup | - | No | No | Lookup to User (systemuser) |
| `sprk_document` | Document | Lookup | - | No | No | Lookup to Document (sprk_document) |
| `sprk_payload` | Payload | Multiple Lines of Text | 50000 | No | No | JSON input data for the job |
| `sprk_result` | Result | Multiple Lines of Text | 50000 | No | No | JSON output data from the job |

### Choice Values

**sprk_jobtype** (Job Type):
- 0 = Document Save
- 1 = Email Save
- 2 = Share Links
- 3 = Quick Create
- 4 = Profile Summary
- 5 = Indexing
- 6 = Deep Analysis

**sprk_status** (Status):
- 0 = Pending
- 1 = In Progress
- 2 = Completed
- 3 = Failed
- 4 = Cancelled

### Indexes

Create the following indexes:

1. **Index on sprk_idempotencykey** (for duplicate job prevention)
2. **Index on sprk_status** (for active job queries)

### Relationships

| Relationship | Type | Related Table | Cascade Behavior |
|--------------|------|---------------|------------------|
| `sprk_initiatedby` → User | N:1 | systemuser | Restrict Delete |
| `sprk_document` → Document | N:1 | sprk_document | Restrict Delete |

### Creation Steps (Power Apps)

1. Go to **Tables** → Click **New table** → **Add columns and data**
2. Set table properties:
   - Display name: `Processing Job`
   - Plural name: `Processing Jobs`
   - Primary column: Keep default "Name"
3. Add all columns listed above
4. For Choice columns (`sprk_jobtype` and `sprk_status`):
   - Click **+ New** → **Choice**
   - Add each value with its label and numeric value
5. When adding lookup columns:
   - **sprk_initiatedby**: Related table = `User`, Relationship = Many-to-One
   - **sprk_document**: Related table = `sprk_document`, Relationship = Many-to-One
6. Click **Save table**
7. Go to **Settings** → **Indexes** → Add the two indexes

---

## Table Relationships Summary

```
Document (sprk_document)
    ↑ 1:N relationship
    ├─ EmailArtifact (sprk_emailartifact)
    │      ↑ 1:N relationship
    │      └─ AttachmentArtifact (sprk_attachmentartifact)
    │             └─ also links back to Document (N:1)
    └─ ProcessingJob (sprk_processingjob)

User (systemuser)
    ↑ 1:N relationship
    └─ ProcessingJob (sprk_processingjob)
```

---

## Security Roles

After creating the tables, update security roles:

1. Go to **Settings** → **Users + permissions** → **Security roles**
2. Select **Basic User** role (or your custom user role)
3. Go to **Custom Entities** tab
4. Grant the following permissions:

| Table | Create | Read | Write | Delete | Append | Append To |
|-------|--------|------|-------|--------|--------|-----------|
| Email Artifact | User | User | User | User | User | User |
| Attachment Artifact | User | User | User | User | User | User |
| Processing Job | User | User | User | None | User | User |

**Note**: Users should not be able to delete ProcessingJobs (for audit trail).

---

## Verification Checklist

After creation, verify:

- [ ] All 3 tables appear in **Tables** list
- [ ] All fields have correct data types
- [ ] All lookups are configured
- [ ] All indexes are created
- [ ] Security roles are updated
- [ ] Tables are added to your solution

---

## Add Tables to Solution

1. Go to **Solutions** → Open your solution
2. Click **Add existing** → **Table**
3. Select all 3 tables:
   - Email Artifact
   - Attachment Artifact
   - Processing Job
4. Click **Next**
5. Choose **Include all components** (fields, relationships, forms, views)
6. Click **Add**

---

**Last Updated**: 2026-01-21
**Reference**: Tasks 010, 011, 012 in `projects/sdap-office-integration/tasks/`
