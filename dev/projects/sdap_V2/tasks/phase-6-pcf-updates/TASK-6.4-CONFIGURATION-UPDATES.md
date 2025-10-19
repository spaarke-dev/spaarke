# Task 6.4: Configuration Updates

## Task Prompt (For AI Agent)

```
You are working on Task 6.4 of Phase 6: Configuration Updates

BEFORE starting work:
1. Read this entire task document carefully
2. This task can run in parallel with 6.2-6.3 (no dependencies)
3. Read current EntityDocumentConfig.ts to see existing structure
4. Check current version in ControlManifest.Input.xml and index.ts
5. Update status section with current state

DURING work:
1. Update EntityDocumentConfig.ts:
   - Add relationshipSchemaName field to interface
   - Make entitySetName optional
   - Update sprk_matter config with relationship schema name
   - Add commented examples for Account and Contact
2. Update ControlManifest.Input.xml:
   - Change version from 2.1.0 to 2.2.0
3. Update index.ts:
   - Find all version references (V2.1.0, 2.1.0)
   - Update to V2.2.0, 2.2.0
4. Test compilation: npm run build
5. Update validation checklist as you complete each file

AFTER completing work:
1. Verify all 3 files updated
2. Verify version = 2.2.0 in all locations
3. Verify relationshipSchemaName added to interface
4. Complete success criteria checklist
5. Commit changes with provided commit message
6. Mark task as Complete and proceed to Task 6.5

Your goal: Update configuration to support multi-parent scenarios and increment version to 2.2.0 for deployment tracking.
```

---

## Overview

**Task ID:** 6.4
**Phase:** 6 - PCF Control Document Record Creation Fix
**Duration:** 0.5 days
**Dependencies:** None (can run parallel with 6.2-6.3)
**Status:** Ready to Start

---

## Objective

Update configuration files to support multi-parent scenarios, add relationshipSchemaName to EntityDocumentConfig interface, and increment PCF version to 2.2.0.

---

## Files to Modify

### 1. EntityDocumentConfig.ts
### 2. ControlManifest.Input.xml
### 3. index.ts

---

## File 1: EntityDocumentConfig.ts

**Location:**
```
src/controls/UniversalQuickCreate/UniversalQuickCreate/config/EntityDocumentConfig.ts
```

**Updates:**

```typescript
/**
 * Configuration for creating Document records from parent entities
 * Supports multiple parent entity types (Matter, Account, Contact, etc.)
 */
export interface EntityDocumentConfig {
  /** Parent entity logical name (e.g., 'sprk_matter') */
  entityName: string;

  /** Lookup field name on child entity pointing to parent (e.g., 'sprk_matter') */
  lookupFieldName: string;

  /** Relationship schema name for metadata queries (e.g., 'sprk_matter_document') */
  relationshipSchemaName: string;

  /** Field on parent entity containing SPE Container ID */
  containerIdField: string;

  /** Field on parent entity used for display (e.g., 'sprk_matternumber') */
  displayNameField: string;

  /** Optional: Entity set name (plural). If not provided, queried dynamically via MetadataService */
  entitySetName?: string;
}

/**
 * Configuration map for supported parent entities
 * Key: Parent entity logical name
 */
export const ENTITY_DOCUMENT_CONFIGS: Record<string, EntityDocumentConfig> = {
  // Matter → Document relationship
  'sprk_matter': {
    entityName: 'sprk_matter',
    lookupFieldName: 'sprk_matter',
    relationshipSchemaName: 'sprk_matter_document',
    containerIdField: 'sprk_containerid',
    displayNameField: 'sprk_matternumber',
    entitySetName: 'sprk_matters'  // Optional: can be resolved dynamically
  },

  // Add more parent entities as needed:
  //
  // 'account': {
  //   entityName: 'account',
  //   lookupFieldName: 'sprk_account',
  //   relationshipSchemaName: 'sprk_account_document',
  //   containerIdField: 'sprk_containerid',
  //   displayNameField: 'name',
  //   entitySetName: 'accounts'
  // },
  //
  // 'contact': {
  //   entityName: 'contact',
  //   lookupFieldName: 'sprk_contact',
  //   relationshipSchemaName: 'sprk_contact_document',
  //   containerIdField: 'sprk_containerid',
  //   displayNameField: 'fullname',
  //   entitySetName: 'contacts'
  // }

} as const;
```

**Changes:**
- ✅ Added `relationshipSchemaName` field to interface
- ✅ Made `entitySetName` optional (can be queried dynamically)
- ✅ Added JSDoc comments
- ✅ Added commented examples for future parent entities

---

## File 2: ControlManifest.Input.xml

**Location:**
```
src/controls/UniversalQuickCreate/UniversalQuickCreate/ControlManifest.Input.xml
```

**Update Version:**

Find the `<control>` element and update version:

```xml
<control
  namespace="SpaarkePCF"
  constructor="UniversalQuickCreate"
  version="2.2.0"
  display-name-key="UniversalQuickCreate_Display_Key"
  description-key="UniversalQuickCreate_Desc_Key"
  control-type="standard">
```

**Change:**
```xml
<!-- BEFORE -->
version="2.1.0"

<!-- AFTER -->
version="2.2.0"
```

**Update Description (Optional):**

```xml
<description-key>
  Universal Quick Create control with SharePoint Embedded file upload.
  v2.2.0: Metadata-driven navigation property resolution for multi-parent support.
</description-key>
```

---

## File 3: index.ts

**Location:**
```
src/controls/UniversalQuickCreate/UniversalQuickCreate/index.ts
```

**Update Version Badge:**

Find the version constant or version display logic and update to 2.2.0:

```typescript
// Example version constant (adjust based on actual code)
private readonly VERSION = 'V2.2.0';

// Or if using React component for version badge:
<div className="version-badge">V2.2.0</div>
```

**Search for:**
- `V2.1.0`
- `2.1.0`
- `version` (check all occurrences)

**Replace with:**
- `V2.2.0`
- `2.2.0`

---

## Deployment Steps

### Step 1: Update EntityDocumentConfig.ts

```bash
# Edit the file
code src/controls/UniversalQuickCreate/UniversalQuickCreate/config/EntityDocumentConfig.ts
```

- Add `relationshipSchemaName` field
- Update `sprk_matter` config
- Add commented examples

### Step 2: Update ControlManifest.Input.xml

```bash
# Edit the file
code src/controls/UniversalQuickCreate/UniversalQuickCreate/ControlManifest.Input.xml
```

- Update version to `2.2.0`
- Update description (optional)

### Step 3: Update index.ts

```bash
# Search for version references
grep -n "2\.1\.0\|V2\.1\.0" src/controls/UniversalQuickCreate/UniversalQuickCreate/index.ts
```

- Update all version references to 2.2.0

### Step 4: Verify Changes

```bash
cd src/controls/UniversalQuickCreate
npm run build
```

- No TypeScript errors
- No linting warnings
- Version correctly incremented

### Step 5: Commit Changes

```bash
git add src/controls/UniversalQuickCreate/UniversalQuickCreate/config/EntityDocumentConfig.ts
git add src/controls/UniversalQuickCreate/UniversalQuickCreate/ControlManifest.Input.xml
git add src/controls/UniversalQuickCreate/UniversalQuickCreate/index.ts
git commit -m "feat(pcf): Update configuration for multi-parent support v2.2.0

- Add relationshipSchemaName to EntityDocumentConfig interface
- Make entitySetName optional (can be resolved dynamically)
- Update sprk_matter config with relationship schema name
- Add commented examples for future parent entities (Account, Contact)
- Increment version to 2.2.0 in manifest and index

Task: 6.4 - Phase 6 PCF Updates"
```

---

## Validation Checklist

- [ ] `relationshipSchemaName` added to interface
- [ ] `entitySetName` made optional
- [ ] `sprk_matter` config updated with all required fields
- [ ] Commented examples added for Account and Contact
- [ ] ControlManifest.Input.xml version = 2.2.0
- [ ] index.ts version badge = V2.2.0
- [ ] TypeScript compilation successful
- [ ] No linting errors
- [ ] Changes committed

---

## Testing

### Verify Version After Deployment

After deploying to Dataverse, verify version displays correctly:

1. Open Custom Page with PCF control
2. Check version badge displays "V2.2.0"
3. Check browser console for version log (if implemented)
4. Verify Dataverse shows correct version in solution

---

## Success Criteria

- [ ] All 3 files updated
- [ ] Version incremented to 2.2.0 in all locations
- [ ] Configuration supports multi-parent pattern
- [ ] TypeScript compilation successful
- [ ] Changes committed to repository
- [ ] Ready for Task 6.5 (deployment)

---

## Next Steps

1. Proceed to [TASK-6.5-PCF-DEPLOYMENT.md](./TASK-6.5-PCF-DEPLOYMENT.md)
2. Build and deploy PCF control to Dataverse
3. Verify version update and functionality

---

**Task Owner:** _______________
**Completion Date:** _______________
**Reviewed By:** _______________
**Status:** ⬜ Not Started | ⬜ In Progress | ⬜ Blocked | ⬜ Complete
