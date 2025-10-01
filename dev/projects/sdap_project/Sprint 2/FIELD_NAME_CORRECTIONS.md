# Field Name Corrections - Sprint 2

**Date**: September 30, 2025
**Status**: ✅ COMPLETED

## Overview

During Task 1.3 implementation, it was discovered that the Dataverse entity field names in the actual deployed environment differ from what was originally planned. All task documentation and code have been updated to reflect the **actual Dataverse schema**.

---

## Field Name Mappings

### Document Entity (sprk_document)

| Original (Incorrect) | Actual (Corrected) | Notes |
|---------------------|-------------------|-------|
| `sprk_name` | `sprk_documentname` | Standard Dataverse primary name field pattern |
| `sprk_status` | `statuscode` | Standard Dataverse status reason field |
| N/A | `statecode` | Standard Dataverse state field (Active/Inactive) |

### Status Code Values

| Status | Value | Field |
|--------|-------|-------|
| Draft | 1 | statuscode |
| Active | 421500001 | statuscode |
| Processing | 421500002 | statuscode |
| Error | 2 | statuscode |
| Active | 0 | statecode |
| Inactive | 1 | statecode |

---

## Files Updated

### ✅ Core Implementation Files

1. **src/shared/Spaarke.Dataverse/DataverseService.cs**
   - Line 113: `sprk_name` → `sprk_documentname`
   - Line 116: `sprk_status` → `statuscode`
   - Line 140: Added `statecode` to ColumnSet
   - Line 153: Field retrieval updated
   - Line 162: Status mapping updated
   - Line 187: Update operations corrected
   - Line 211: Status update corrected
   - Lines 249-273: GetDocumentsByContainerAsync corrected

2. **src/api/Spe.Bff.Api/Api/DataverseDocumentsEndpoints.cs**
   - Created NEW - Uses correct field names throughout
   - All API endpoints use `sprk_documentname` and `statuscode`

### ✅ Task Documentation Files

3. **Task-2.1-Thin-Plugin-Implementation.md**
   - Line 220: `sprk_name` → `sprk_documentname`
   - Line 227: `sprk_status` → `statuscode` (added `statecode`)
   - Line 468: Test code corrected
   - Line 476: Test code corrected
   - Line 526: Load test code corrected

4. **Task-2.2-Background-Service-Implementation.md**
   - Line 326: Switch case `sprk_name` → `sprk_documentname`
   - Lines 338-339: Switch case `sprk_status` → `statuscode`/`statecode`
   - Line 408-409: ExtractNewValue/OldValue corrected
   - Lines 477-478: Status extraction corrected
   - Line 936: Test event data corrected

5. **Task-3.1-Model-Driven-App-Configuration.md**
   - Lines 154-157: Form fields corrected (added `statecode`)
   - Line 210: View attribute `sprk_name` → `sprk_documentname`
   - Line 219: Filter condition `sprk_status` → `statuscode` with correct value (421500001 for Active)
   - Line 225: Grid jump field corrected
   - Line 227: Grid cell corrected
   - Lines 247-263: "All Documents" view corrected

6. **Task-3.2-JavaScript-File-Management-Integration.md**
   - ✅ No corrections needed - No field references

---

## Testing Impact

### Areas That Need Validation

1. **DataverseService Operations**
   - Create document operations
   - Retrieve document operations
   - Update document operations
   - Query operations by container

2. **API Endpoints** (Task 1.3 - COMPLETED)
   - ✅ All CRUD endpoints tested
   - ✅ Field mappings validated
   - ✅ Build successful

3. **Plugin Event Capture** (Task 2.1 - PENDING)
   - Event field extraction
   - Service Bus message payloads
   - Test operations

4. **Background Processing** (Task 2.2 - PENDING)
   - Event deserialization
   - Field change detection
   - Status change handling

5. **Power Platform UI** (Task 3.1 - PENDING)
   - Form configurations
   - View definitions
   - FetchXML queries

---

## Key Takeaways

### What Changed
- **Field naming follows standard Dataverse patterns** rather than custom conventions
- **Status management uses standard `statecode`/`statuscode`** instead of custom `sprk_status`
- **Document name uses `sprk_documentname`** following Dataverse primary name field conventions

### Why This Matters
- **Consistency**: All code now matches actual Dataverse schema
- **Reliability**: No runtime field mapping errors
- **Maintainability**: Follows Power Platform best practices

### Verification Steps
1. ✅ Validated actual Dataverse entity schema via solution export
2. ✅ Updated DataverseService core implementation
3. ✅ Updated all task documentation files
4. ✅ Built and tested API successfully
5. ✅ Verified no breaking changes in remaining tasks

---

## Next Steps

When implementing remaining tasks:

1. **Task 2.1 (Thin Plugin)**: Field names already corrected
2. **Task 2.2 (Background Service)**: Field names already corrected
3. **Task 3.1 (Model-Driven App)**: Field names already corrected
4. **Task 3.2 (JavaScript Integration)**: No corrections needed

All future code should use:
- `sprk_documentname` (not `sprk_name`)
- `statuscode` and `statecode` (not `sprk_status`)

---

**Status**: ✅ All corrections complete and documented