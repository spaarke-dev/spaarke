# ARCHIVED - Incorrect Approach

**Date Archived:** 2025-10-07
**Reason:** Wrong architecture - assumed single Document record with metadata array in field

## Why This Was Wrong:

### Incorrect Assumptions:
1. ❌ **Single Document record** with JSON array in `sprk_fileuploadmetadata` field
2. ❌ **Field-bound PCF control** that writes metadata to a single field
3. ❌ **Quick Create form creates the record** (not the PCF)
4. ❌ **Button inside PCF control body** (not in form footer)

### Actual Requirements:
1. ✅ **Multiple Document records** (one per file)
2. ✅ **PCF control creates records directly** via `context.webAPI.createRecord()`
3. ✅ **PCF bypasses Quick Create save mechanism**
4. ✅ **Custom button in form footer** (replaces standard "Save and Close")

## What Changed:

The user story clarified that:
- Multiple files → Multiple Document records (not single record)
- PCF must orchestrate entire process (upload files + create records)
- Custom button should replace standard button in footer
- No backend plugins (ADR constraint)

## Correct Approach:

See parent folder for revised work items based on:
- TASK-7B-2A architecture (MultiFileUploadService)
- Custom button injection in form footer
- Multiple Document record creation
- Subgrid refresh after completion

## Files in This Archive:

- WORK-ITEM-1-UPDATE-MANIFEST.md - Wrong manifest changes
- WORK-ITEM-2-CREATE-FILEUPLOADPCF.md - Wrong PCF implementation
- WORK-ITEM-3-VERIFY-FILE-UPLOAD-SERVICE.md - Still valid but incomplete
- WORK-ITEM-4-MULTI-FILE-UPLOAD.md - Wrong approach (single record)
- WORK-ITEM-5-CREATE-FILE-UPLOAD-UI.md - Wrong UI (button in wrong place)
- WORK-ITEM-6-CONFIGURE-QUICK-CREATE-FORM.md - Wrong form config
- WORK-ITEM-7-TESTING.md - Some tests still valid
- WORK-ITEM-8-DOCUMENTATION.md - Structure valid but wrong architecture

---

**Status:** Archived
**Replacement:** See revised work items in parent folder
