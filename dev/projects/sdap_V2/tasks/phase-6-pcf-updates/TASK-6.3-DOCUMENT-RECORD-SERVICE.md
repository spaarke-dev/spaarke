# Task 6.3: Update DocumentRecordService

## Task Prompt (For AI Agent)

```
You are working on Task 6.3 of Phase 6: Update DocumentRecordService

BEFORE starting work:
1. Read this entire task document carefully
2. Verify Task 6.2 is complete - MetadataService must exist before proceeding
3. Read current DocumentRecordService.ts to understand existing implementation
4. Identify all locations where regex /\[{}\]/g needs to be fixed to /[{}]/g
5. Review EntityDocumentConfig to understand configuration structure
6. Update status section with current state

DURING work:
1. Import MetadataService at top of file
2. Update createDocuments() method signature to include config and formData parameters
3. Replace hardcoded navigation properties with MetadataService.getLookupNavProp() calls
4. Fix ALL regex typos: change /\[{}\]/g to /[{}]/g
5. Add sprk_documentdescription field support
6. Implement createViaRelationship() method for Option B
7. Test compilation: npm run build
8. Update testing checklist as you verify each change

AFTER completing work:
1. Verify all regex typos are fixed (search for \[{}\])
2. Verify formData parameter added to both methods
3. Verify description field added to payload
4. Complete success criteria checklist
5. Identify and update any caller code that needs config parameter
6. Commit changes with provided commit message
7. Mark task as Complete and proceed to Task 6.4

Your goal: Transform DocumentRecordService to use metadata-driven navigation properties, fix code hygiene issues, and add formData support for better UX.
```

---

## Overview

**Task ID:** 6.3
**Phase:** 6 - PCF Control Document Record Creation Fix
**Duration:** 1 day
**Dependencies:** Task 6.2 (MetadataService)
**Status:** Ready to Start

---

## Objective

Update the DocumentRecordService to use MetadataService for dynamic navigation property resolution, add formData support for document name and description, and implement both Option A and Option B binding patterns.

---

## File Location

**Modified File:**
```
src/controls/UniversalQuickCreate/UniversalQuickCreate/services/DocumentRecordService.ts
```

---

## Current Code Issues

### Issue 1: Hard-Coded Navigation Properties
Current code likely uses hard-coded values or assumptions about navigation property names.

### Issue 2: Regex Typo
```typescript
// WRONG:
parentId.replace(/\[{}\]/g, "")  // Escapes brackets unnecessarily

// CORRECT:
parentId.replace(/[{}]/g, "")    // Removes braces from GUID
```

### Issue 3: Missing formData Support
No support for user-provided document name or description.

### Issue 4: Missing Description Field
Not setting `sprk_documentdescription` field.

---

## Implementation

### Updated DocumentRecordService Code

```typescript
import { MetadataService } from './MetadataService';
import { EntityDocumentConfig } from '../config/EntityDocumentConfig';

/**
 * DocumentRecordService
 *
 * Handles creation of Document records in Dataverse after files are uploaded to SPE.
 * Supports two binding patterns:
 * - Option A: @odata.bind (primary method)
 * - Option B: Relationship URL POST (fallback/server-side)
 */
export class DocumentRecordService {
  constructor(private context: ComponentFramework.Context<any>) {}

  /**
   * Create Document records for uploaded files using Option A (@odata.bind).
   * Supports multi-parent scenarios via metadata-driven navigation property resolution.
   *
   * @param files - Array of uploaded files from SPE
   * @param parent - Parent record info (Matter, Account, Contact, etc.)
   * @param config - Entity configuration for the parent type
   * @param formData - Optional form data (document name, description)
   * @returns Array of results (success/failure per file)
   */
  async createDocuments(
    files: Array<{ name: string; id: string; size: number }>,
    parent: { parentRecordId: string; containerId?: string },
    config: EntityDocumentConfig,
    formData?: { documentName?: string; description?: string }
  ): Promise<Array<{ success: boolean; fileName: string; recordId?: string; error?: string }>> {

    // Sanitize GUID: remove braces, lowercase (FIXED REGEX)
    const parentId = parent.parentRecordId.replace(/[{}]/g, "").toLowerCase();

    try {
      // Resolve navigation properties from metadata (cached per relationship)
      const navProp = await MetadataService.getLookupNavProp(
        this.context,
        'sprk_document',
        config.relationshipSchemaName
      );

      const entitySetName = config.entitySetName ||
        await MetadataService.getEntitySetName(this.context, config.entityName);

      const results: Array<{ success: boolean; fileName: string; recordId?: string; error?: string }> = [];

      // Create Document record for each file
      for (const f of files) {
        try {
          // Build whitelist payload (never spread form data directly)
          const payload: any = {
            sprk_documentname: formData?.documentName || f.name.replace(/\.[^/.]+$/, ""),
            sprk_filename: f.name,
            sprk_graphitemid: f.id,
            sprk_graphdriveid: parent.containerId,
            sprk_filesize: f.size,
            // Correct left-hand binding property from metadata
            [`${navProp}@odata.bind`]: `/${entitySetName}(${parentId})`
          };

          // Add description if provided
          if (formData?.description) {
            payload.sprk_documentdescription = formData.description;
          }

          const r = await this.context.webAPI.createRecord("sprk_document", payload);
          results.push({ success: true, fileName: f.name, recordId: r.id });

        } catch (e: any) {
          results.push({
            success: false,
            fileName: f.name,
            error: e.message || 'Unknown error creating Document record'
          });
        }
      }

      return results;

    } catch (metadataError: any) {
      // If metadata query fails, fail all files with same error
      const error = metadataError.message || 'Metadata query failed';
      return files.map(f => ({
        success: false,
        fileName: f.name,
        error
      }));
    }
  }

  /**
   * Option B: Create via relationship URL (metadata-free).
   * Use this for server-side $batch operations or as fallback.
   *
   * Note: context.webAPI.execute requires getMetadata() function exactly as shown.
   * See: https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/xrm-webapi/execute
   *
   * @param parentId - Parent record GUID (with or without braces)
   * @param childPayload - Document record payload (no lookup fields)
   * @param config - Entity configuration for parent type
   * @returns Created record response
   */
  async createViaRelationship(
    parentId: string,
    childPayload: any,
    config: EntityDocumentConfig
  ): Promise<any> {

    // Sanitize GUID: remove braces, lowercase (FIXED REGEX)
    const id = parentId.replace(/[{}]/g, "").toLowerCase();

    // Resolve metadata for relationship URL
    const entitySetName = config.entitySetName ||
      await MetadataService.getEntitySetName(this.context, config.entityName);

    const collectionNavProp = await MetadataService.getCollectionNavProp(
      this.context,
      config.entityName,
      config.relationshipSchemaName
    );

    // Build relationship URL
    const url = `/api/data/v9.2/${entitySetName}(${id})/${collectionNavProp}`;

    const req = {
      method: "POST",
      url,
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(childPayload),
      // Required by context.webAPI.execute - returns metadata for bound parameter
      // See Microsoft docs link above for details
      getMetadata: () => ({
        boundParameter: null,
        parameterTypes: {},
        operationType: 0,
        operationName: ""
      })
    };

    return (this.context.webAPI as any).execute(req);
  }

  /**
   * Create multiple documents using Option B (relationship URL).
   * Useful for server-side $batch operations.
   *
   * @param files - Array of uploaded files from SPE
   * @param parent - Parent record info
   * @param config - Entity configuration for parent type
   * @param formData - Optional form data (document name, description)
   * @returns Array of results (success/failure per file)
   */
  async createDocumentsViaRelationship(
    files: Array<{ name: string; id: string; size: number }>,
    parent: { parentRecordId: string; containerId?: string },
    config: EntityDocumentConfig,
    formData?: { documentName?: string; description?: string }
  ): Promise<Array<{ success: boolean; fileName: string; recordId?: string; error?: string }>> {

    const results: Array<{ success: boolean; fileName: string; recordId?: string; error?: string }> = [];

    for (const f of files) {
      try {
        // Build whitelist payload (no lookup fields in Option B)
        const payload: any = {
          sprk_documentname: formData?.documentName || f.name.replace(/\.[^/.]+$/, ""),
          sprk_filename: f.name,
          sprk_graphitemid: f.id,
          sprk_graphdriveid: parent.containerId,
          sprk_filesize: f.size
        };

        // Add description if provided
        if (formData?.description) {
          payload.sprk_documentdescription = formData.description;
        }

        const response = await this.createViaRelationship(
          parent.parentRecordId,
          payload,
          config
        );

        // Option B typically returns 204 No Content
        // Extract ID from location header if available
        const recordId = response?.id || 'created';

        results.push({ success: true, fileName: f.name, recordId });

      } catch (e: any) {
        results.push({
          success: false,
          fileName: f.name,
          error: e.message || 'Unknown error creating Document record via relationship URL'
        });
      }
    }

    return results;
  }
}
```

---

## Key Changes

### 1. Fixed Regex Typo
```typescript
// BEFORE (WRONG):
parentId.replace(/\[{}\]/g, "")

// AFTER (CORRECT):
parentId.replace(/[{}]/g, "")
```

### 2. Added MetadataService Integration
```typescript
const navProp = await MetadataService.getLookupNavProp(
  this.context,
  'sprk_document',
  config.relationshipSchemaName
);
```

### 3. Added formData Parameter
```typescript
formData?: { documentName?: string; description?: string }
```

### 4. Added Description Field
```typescript
if (formData?.description) {
  payload.sprk_documentdescription = formData.description;
}
```

### 5. Implemented Both Options
- `createDocuments()` - Option A (@odata.bind)
- `createViaRelationship()` - Option B (relationship URL)
- `createDocumentsViaRelationship()` - Option B for multiple files

### 6. Comprehensive Error Handling
- Metadata query failures handled gracefully
- Per-file error tracking
- Friendly error messages

---

## Testing Checklist

### Unit Tests

- [ ] Verify regex correctly removes braces: `{GUID}` → `guid`
- [ ] Verify regex handles no braces: `GUID` → `guid`
- [ ] Verify formData.documentName used when provided
- [ ] Verify file name used when formData.documentName not provided
- [ ] Verify description field set when provided
- [ ] Verify description field omitted when not provided

### Integration Tests

- [ ] Create single Document with Option A (success)
- [ ] Create multiple Documents with Option A (all success)
- [ ] Create Documents with formData (verify fields saved)
- [ ] Create Document with Option B (success)
- [ ] Handle metadata query failure gracefully
- [ ] Handle individual file creation failure (partial success)

---

## Deployment Steps

1. **Read current file:**
   ```bash
   # Review current implementation first
   cat src/controls/UniversalQuickCreate/UniversalQuickCreate/services/DocumentRecordService.ts
   ```

2. **Update the file:**
   - Replace entire file content with code above
   - Or use Edit tool to make specific changes

3. **Add import:**
   ```typescript
   import { MetadataService } from './MetadataService';
   import { EntityDocumentConfig } from '../config/EntityDocumentConfig';
   ```

4. **Verify compilation:**
   ```bash
   cd src/controls/UniversalQuickCreate
   npm run build
   ```

5. **Commit changes:**
   ```bash
   git add src/controls/UniversalQuickCreate/UniversalQuickCreate/services/DocumentRecordService.ts
   git commit -m "feat(pcf): Update DocumentRecordService with metadata-driven navigation

- Fix regex typo: /[{}]/g instead of /\[{}\]/g
- Add MetadataService integration for dynamic navigation properties
- Add formData parameter for document name and description
- Add sprk_documentdescription field support
- Implement Option A (createDocuments) and Option B (createViaRelationship)
- Add comprehensive error handling per file

Task: 6.3 - Phase 6 PCF Updates"
   ```

---

## Caller Updates Required

If other code calls `DocumentRecordService.createDocuments()`, update call sites:

### Before:
```typescript
const results = await documentRecordService.createDocuments(files, parent);
```

### After:
```typescript
const config = ENTITY_DOCUMENT_CONFIGS[parentEntityName];
const formData = {
  documentName: userInput.documentName,
  description: userInput.description
};

const results = await documentRecordService.createDocuments(
  files,
  parent,
  config,
  formData
);
```

---

## Success Criteria

- [ ] DocumentRecordService.ts updated with all changes
- [ ] Regex typo fixed (`/[{}]/g` used correctly)
- [ ] MetadataService imported and used
- [ ] formData parameter added to both methods
- [ ] Description field added to payload
- [ ] Both Option A and Option B implemented
- [ ] TypeScript compilation successful
- [ ] No linting errors or warnings
- [ ] Code committed to repository

---

## Next Steps

Once DocumentRecordService is updated:

1. Proceed to [TASK-6.4-CONFIGURATION-UPDATES.md](./TASK-6.4-CONFIGURATION-UPDATES.md)
2. Update EntityDocumentConfig to include relationshipSchemaName
3. Update callers to pass config parameter

---

**Task Owner:** _______________
**Completion Date:** _______________
**Reviewed By:** _______________
**Status:** ⬜ Not Started | ⬜ In Progress | ⬜ Blocked | ⬜ Complete
