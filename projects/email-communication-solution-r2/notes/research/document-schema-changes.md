# Dataverse Schema Changes: sprk_document Entity

## Overview

This document describes the schema changes required to the `sprk_document` entity to support the new Communication entity-based architecture in the Email Communication Solution R2.

**Timeline**: Pre-launch clean cutover (no production data to migrate)
**Risk Level**: Low (fresh environment, no legacy data)

---

## Change 1: Add sprk_communication Lookup Field

### Field Definition
| Property | Value |
|----------|-------|
| **Display Name** | Communication |
| **Field Name** | `sprk_communication` |
| **Data Type** | Lookup (EntityReference) |
| **Target Entity** | `sprk_communication` |
| **Required Level** | Optional |
| **Searchable** | Yes |
| **Description** | Links archived .eml documents to their source communication record |

### Purpose
Replaces the legacy `sprk_email` lookup (which pointed to Dataverse `email` activity records). The new `sprk_communication` lookup points to the new `sprk_communication` entity, which is the primary record for email communications in the Email Communication Solution.

### Relationship Type
- **N:1 Relationship** (many documents → one communication)
- **Relationship Name**: `sprk_document_sprk_communication`
- **Cascade Delete**: No (communication can exist without archived document)

### Code References
Already implemented:
- `IncomingCommunicationProcessor.ArchiveEmlAsync()` — Sets `["sprk_communication"]` to the EntityReference ✅
- `CommunicationService.ArchiveToSpeAsync()` — Creates and updates this field ✅

---

## Change 2: Remove sprk_email Lookup Field

### Field to Remove
| Property | Value |
|----------|-------|
| **Display Name** | Email |
| **Field Name** | `sprk_email` |
| **Data Type** | Lookup (EntityReference) |
| **Target Entity** | `email` (Dataverse activity) |
| **Status** | DEPRECATED — being replaced by `sprk_communication` |

### Removal Reason
The original architecture used Dataverse `email` activity records as the source for archived documents. The new architecture uses the custom `sprk_communication` entity instead, which provides better control and flexibility.

### Risk Assessment
**Risk Level**: Low ✅
- **Pre-launch cutover**: No production data exists with this field populated
- **Test environment only**: All existing test records can be recreated
- **Code impact**: No code references `sprk_email` field in the current codebase

### Pre-Removal Verification
Before removing this field, confirm:
1. No sprk_document records have `sprk_email` populated: `SELECT COUNT(*) FROM sprk_document WHERE sprk_email IS NOT NULL`
2. No forms or views reference this field
3. No plug-ins or workflows depend on this field

---

## Form Updates

### Main Form: sprk_document Main Form

**Changes**:
1. **Remove**: `sprk_email` lookup control
2. **Add**: `sprk_communication` lookup control
   - Place in same position as removed field (or slightly below Communications section)
   - Show as optional lookup
   - Display communication type and subject for context

**Form Section**: Communications (or create if not exists)
- sprk_communication lookup control
- Read-only: sprk_sourcetype (shows "CommunicationArchive" if from email)
- Read-only: Created On date

**Tab Layout**:
```
Tab: General
  Section: Document Information
    - sprk_name (text)
    - sprk_documenttype (option set)
  Section: Communications
    - sprk_communication (NEW lookup)
    - sprk_sourcetype (read-only)

Tab: Source Details (if exists, else remove)
  Section: Removed Fields
    - [REMOVE sprk_email lookup]
```

### Quick View Form Updates
If a Quick View form exists for sprk_document, update to show:
- sprk_communication (with subgrid if needed)
- Remove sprk_email

---

## View Updates

### Views to Update

#### 1. "Communication Documents" View
**Filter**: Where `sprk_communication` is not null
**Columns to Display**:
- sprk_name (Primary)
- sprk_documenttype
- sprk_communication
- Created On
- Modified On

**Sort**: Created On (descending)

#### 2. "All Documents" View
**Add Column**: `sprk_communication`
**Remove Column**: `sprk_email` (if exists)
**Position**: After sprk_documenttype

#### 3. Views to Remove or Archive
- Any views filtering on `sprk_email` exclusively
- Examples: "Email Activity Documents" if it relied only on email lookup

---

## Existing sprk_document Fields (Reference)

These fields remain unchanged:

| Field | Type | Purpose |
|-------|------|---------|
| `sprk_name` | Text (Primary Name) | Display name of the document |
| `sprk_documenttype` | OptionSet | General, Invoice, Communication (marks document type) |
| `sprk_sourcetype` | OptionSet | Manual, CommunicationArchive (marks source of record) |
| `sprk_speitemid` | Text | SharePoint Embedded item ID |
| `sprk_spedriveid` | Text | SharePoint Embedded drive/container ID |
| **`sprk_communication`** | **Lookup (NEW)** | **Links to sprk_communication entity** |

---

## Code Changes Required

After schema changes are deployed to Dataverse:

### 1. IncomingCommunicationProcessor.ArchiveEmlAsync()
**Status**: ✅ Already uses new field
**Code**: Sets `["sprk_communication"]` EntityReference
**Action**: Verify no changes needed

```csharp
// Already correct:
documentData["sprk_communication"] = new EntityReference("sprk_communication", communicationId);
```

### 2. CommunicationService.ArchiveToSpeAsync()
**Status**: ⚠️ Verify alignment
**Action**: Confirm this method also uses `sprk_communication` field when creating/updating documents

### 3. Remove Old References
Search codebase for `sprk_email` field references:
```bash
grep -r "sprk_email" src/server/api/
grep -r "\"sprk_email\"" src/
```

**Action**: If any references exist, update to use `sprk_communication` instead

---

## Implementation Steps (Make.powerapps.com)

### Step 1: Add sprk_communication Lookup

1. Navigate to **Power Apps** > **Solutions**
2. Open **Spaarke** solution (or relevant solution)
3. Find **sprk_document** entity
4. Click **+ New** > **Lookup field**
5. Configure:
   - Display Name: `Communication`
   - Field Name: `sprk_communication`
   - Related Table: `sprk_communication`
   - Required Level: `Optional`
6. Click **Save**
7. Publish customizations

### Step 2: Update Main Form

1. Still in sprk_document entity, click **Forms**
2. Open **Main Form**
3. Add a new section under "Communications" tab (create tab if needed)
4. Add the `sprk_communication` lookup control to this section
5. Set control properties:
   - Label: "Communication"
   - Required Level: "Optional"
6. Save and publish form

### Step 3: Update Views

1. In sprk_document entity, click **Views**
2. For each view listed above:
   - Edit view
   - Add `sprk_communication` column
   - Remove `sprk_email` column if exists
   - Update filters if needed
   - Save and publish
3. Archive or delete any views that only filtered on `sprk_email`

### Step 4: Remove sprk_email Lookup (After Verification)

**Only perform if pre-removal verification passed:**

1. In sprk_document entity, find **sprk_email** field
2. Click the field to edit
3. Check **Remove from entity**
4. Confirm deletion of any form controls and views that used this field
5. Click **Save**
6. Publish all customizations

---

## Verification Checklist

After deployment, verify:

- [ ] sprk_communication lookup field exists on sprk_document
- [ ] sprk_communication is visible on Main Form in Communications section
- [ ] sprk_communication appears in all relevant views
- [ ] sprk_email field no longer exists on entity
- [ ] sprk_email removed from Main Form
- [ ] sprk_email removed from all views
- [ ] All existing documents with sourcetype="CommunicationArchive" can be queried by sprk_communication
- [ ] New documents created via IncomingCommunicationProcessor populate sprk_communication correctly
- [ ] No code references to sprk_email field remain

---

## Rollback Plan

If issues arise during or after schema change:

1. **Before removal** (sprk_email still exists):
   - Restore views/forms from solution import
   - Revert sprk_communication lookup field removal
   - Publish customizations

2. **After removal** (sprk_email already deleted):
   - Import solution backup with original schema
   - Re-test thoroughly in dev environment
   - Notify team of delay

---

## Timeline

| Phase | Timeline |
|-------|----------|
| Schema changes documented | ✅ Complete |
| Deployment planning | In progress |
| Dev environment testing | Pre-launch |
| Staging environment deployment | Pre-launch |
| Production deployment | Post-UAT approval |

---

**Status**: Schema changes documented and ready for Dataverse deployment
**Last Updated**: 2026-03-09
**Owner**: Email Communication Solution R2 Team
