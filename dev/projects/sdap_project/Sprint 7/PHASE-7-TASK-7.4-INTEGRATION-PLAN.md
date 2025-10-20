# Phase 7 - Task 7.4: Integration Plan

**Date:** October 20, 2025
**Status:** Ready to implement
**Estimated Time:** 1-2 hours

---

## Context

**Completed:**
- ✅ Task 7.1: Extended IDataverseService with metadata methods
- ✅ Task 7.2: Created NavMapEndpoints REST API (BFF API)
- ✅ Task 7.3: Created NavMapClient TypeScript service

**Current Task:** Task 7.4 - Integrate metadata discovery in PCF controls

---

## Problem Statement

Currently, the Universal Quick Create PCF control uses **hardcoded** navigation property names from `EntityDocumentConfig.ts`. This causes case-sensitivity issues when creating Document records with `@odata.bind` lookups.

**Current Implementation:**
```typescript
// EntityDocumentConfig.ts
export const sprk_MatterDocumentConfig: EntityDocumentConfig = {
    entityName: 'sprk_matter',
    entitySetName: 'sprk_matters',
    navigationPropertyName: 'sprk_Matter', // HARDCODED (capital M)
    relationshipSchemaName: 'sprk_matter_document'
};
```

**Issue:**
- Manual validation required via PowerShell
- Limited to pre-configured entities only
- Cannot support dynamic multi-entity scenarios
- Case-sensitivity must be manually verified for each entity

**Phase 7 Solution:**
Replace hardcoded config with **dynamic metadata discovery** via NavMapClient → BFF API → Dataverse EntityDefinitions.

---

## Integration Points

### 1. **Universal Quick Create PCF** (Primary Integration)

**File:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/services/DocumentRecordService.ts`

**Current Code (Line 90-115):**
```typescript
private async createSingleDocument(
    file: SpeFileMetadata,
    parentContext: ParentContext,
    formData: FormData
): Promise<CreateResult> {
    try {
        // Get entity configuration
        const config = getEntityDocumentConfig(parentContext.parentEntityName);

        // CRITICAL: Use navigation property from config (case-sensitive!)
        // Example: "sprk_Matter" (capital M) for Matter entity
        // Cannot query metadata dynamically in PCF - context.webAPI doesn't support EntityDefinitions
        const navigationPropertyName = config.navigationPropertyName;

        // Build record payload with correct navigation property
        const payload = this.buildRecordPayload(
            file,
            parentContext,
            formData,
            navigationPropertyName,  // Dynamic from metadata (not hardcoded!)
            config.entitySetName
        );

        // Create record
        const result = await this.context.webAPI.createRecord('sprk_document', payload);

        return { success: true, fileName: file.name, recordId: result.id };
    } catch (error) {
        return { success: false, fileName: file.name, error: error.message };
    }
}
```

**What Needs to Change:**
1. Add NavMapClient as a constructor dependency
2. Query navigation property dynamically using `navMapClient.getLookupNavigation()`
3. Remove dependency on `EntityDocumentConfig.navigationPropertyName`
4. Keep `EntityDocumentConfig.relationshipSchemaName` for the API call

---

## Implementation Steps

### Step 1: Copy NavMapClient to UniversalQuickCreate

**Source:** `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/NavMapClient.ts`

**Destination:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/services/NavMapClient.ts`

**Why:** Each PCF control is standalone and cannot share TypeScript modules directly.

**Action:**
```bash
cp src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/NavMapClient.ts \
   src/controls/UniversalQuickCreate/UniversalQuickCreate/services/NavMapClient.ts
```

### Step 2: Modify DocumentRecordService Constructor

**File:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/services/DocumentRecordService.ts`

**Current:**
```typescript
export class DocumentRecordService {
    private context: ComponentFramework.Context<any>;

    constructor(context: ComponentFramework.Context<any>) {
        this.context = context;
    }
}
```

**New:**
```typescript
import { NavMapClient } from './NavMapClient';

export class DocumentRecordService {
    private context: ComponentFramework.Context<any>;
    private navMapClient: NavMapClient;

    constructor(
        context: ComponentFramework.Context<any>,
        navMapClient: NavMapClient
    ) {
        this.context = context;
        this.navMapClient = navMapClient;
    }
}
```

### Step 3: Update createSingleDocument Method

**File:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/services/DocumentRecordService.ts`

**Replace Lines 90-115 with:**
```typescript
private async createSingleDocument(
    file: SpeFileMetadata,
    parentContext: ParentContext,
    formData: FormData
): Promise<CreateResult> {
    try {
        // Get entity configuration
        const config = getEntityDocumentConfig(parentContext.parentEntityName);
        if (!config) {
            throw new Error(`Unsupported entity type: ${parentContext.parentEntityName}`);
        }

        // PHASE 7: Query navigation property metadata dynamically via BFF API
        // This replaces hardcoded config.navigationPropertyName
        logInfo('DocumentRecordService', `Querying navigation property metadata for ${parentContext.parentEntityName}`);

        const navMetadata = await this.navMapClient.getLookupNavigation(
            'sprk_document',                    // childEntity (always sprk_document)
            config.relationshipSchemaName       // e.g., "sprk_matter_document"
        );

        const navigationPropertyName = navMetadata.navigationPropertyName; // e.g., "sprk_Matter" (capital M)
        const entitySetName = navMetadata.targetEntity + 's';              // e.g., "sprk_matters"

        logInfo('DocumentRecordService', `Using navigation property: ${navigationPropertyName} (source: ${navMetadata.source})`);

        // Build record payload with correct navigation property
        const payload = this.buildRecordPayload(
            file,
            parentContext,
            formData,
            navigationPropertyName,  // Now dynamic from metadata!
            entitySetName
        );

        // Create record using context.webAPI
        const result = await this.context.webAPI.createRecord('sprk_document', payload);

        logInfo('DocumentRecordService', `Created Document record: ${result.id}`);

        return {
            success: true,
            fileName: file.name,
            recordId: result.id
        };

    } catch (error: any) {
        logError('DocumentRecordService', `Failed to create Document for ${file.name}`, error);

        return {
            success: false,
            fileName: file.name,
            error: error.message || 'Unknown error occurred'
        };
    }
}
```

### Step 4: Initialize NavMapClient in UniversalDocumentUploadPCF

**File:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/UniversalDocumentUploadPCF.ts`

**Modify initializeServices Method (Lines 226-244):**

**Current:**
```typescript
private initializeServices(context: ComponentFramework.Context<IInputs>): void {
    const rawApiUrl = context.parameters.sdapApiBaseUrl?.raw || 'spe-api-dev-67e2xz.azurewebsites.net/api';
    const apiBaseUrl = rawApiUrl.startsWith('http://') || rawApiUrl.startsWith('https://')
        ? rawApiUrl
        : `https://${rawApiUrl}`;

    const apiClient = SdapApiClientFactory.create(apiBaseUrl);
    const fileUploadService = new FileUploadService(apiClient);
    this.multiFileService = new MultiFileUploadService(fileUploadService);
    this.documentRecordService = new DocumentRecordService(context);
}
```

**New:**
```typescript
import { NavMapClient } from './services/NavMapClient';

private initializeServices(context: ComponentFramework.Context<IInputs>): void {
    const rawApiUrl = context.parameters.sdapApiBaseUrl?.raw || 'spe-api-dev-67e2xz.azurewebsites.net/api';
    const apiBaseUrl = rawApiUrl.startsWith('http://') || rawApiUrl.startsWith('https://')
        ? rawApiUrl
        : `https://${rawApiUrl}`;

    logInfo('UniversalDocumentUploadPCF', 'Initializing services', { apiBaseUrl });

    // Create API clients
    const apiClient = SdapApiClientFactory.create(apiBaseUrl);
    const navMapClient = new NavMapClient(
        apiBaseUrl,
        () => this.authProvider.getAccessToken() // Reuse same auth as file operations
    );

    // Create services
    const fileUploadService = new FileUploadService(apiClient);
    this.multiFileService = new MultiFileUploadService(fileUploadService);
    this.documentRecordService = new DocumentRecordService(context, navMapClient); // Pass navMapClient!

    logInfo('UniversalDocumentUploadPCF', 'Services initialized (including NavMapClient)');
}
```

### Step 5: Update EntityDocumentConfig (Optional - Backwards Compatibility)

**File:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/config/EntityDocumentConfig.ts`

**Option A: Keep navigationPropertyName as Fallback**
```typescript
export interface EntityDocumentConfig {
    entityName: string;
    entitySetName: string;
    relationshipSchemaName: string;
    navigationPropertyName?: string; // Optional - fallback if API fails
}
```

**Option B: Remove navigationPropertyName Entirely**
```typescript
export interface EntityDocumentConfig {
    entityName: string;
    entitySetName: string;
    relationshipSchemaName: string; // ONLY this is needed for NavMapClient call
}
```

**Recommendation:** Choose Option A for safety during transition period.

---

## Testing Plan

### Unit Testing
1. **Test NavMapClient Integration**
   - Mock NavMapClient in DocumentRecordService tests
   - Verify getLookupNavigation() is called with correct parameters
   - Test fallback behavior if API call fails

2. **Test Document Creation**
   - Create Document with Matter parent (sprk_matter)
   - Create Document with Project parent (sprk_project)
   - Verify navigation property case-sensitivity is correct

### Integration Testing
1. **Deploy to Dev Environment**
   - Deploy updated PCF control to Dataverse dev
   - Test with existing Matter records
   - Test with new entity types (Project, Invoice)

2. **Verify API Calls**
   - Check browser Network tab
   - Verify NavMapClient calls BFF API `/api/navmap/{childEntity}/{relationship}/lookup`
   - Verify response includes correct `navigationPropertyName`

3. **Verify Dataverse Records**
   - Check created Document records have correct Matter lookup
   - Verify `@odata.bind` uses correct case (e.g., `sprk_Matter` not `sprk_matter`)

---

## Rollback Plan

If issues occur:

1. **Revert DocumentRecordService Changes**
   - Remove NavMapClient calls
   - Restore hardcoded `config.navigationPropertyName`

2. **Redeploy Previous PCF Version**
   - Use previous solution version from git history
   - Re-import to Dataverse

3. **Verify Functionality**
   - Test document creation works with hardcoded config
   - Confirm no data loss

---

## Success Criteria

- ✅ NavMapClient successfully queries navigation property metadata
- ✅ Document creation works with dynamically discovered navigation property
- ✅ Case-sensitivity issues resolved (no more `sprk_matter` vs `sprk_Matter` errors)
- ✅ Multi-entity support enabled (Matter, Project, Invoice, etc.)
- ✅ No manual PowerShell validation required
- ✅ Backwards compatible with existing config (if fallback implemented)

---

## Related Files

### Source Files
- `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/NavMapClient.ts` - Template
- `src/controls/UniversalQuickCreate/UniversalQuickCreate/services/DocumentRecordService.ts` - To modify
- `src/controls/UniversalQuickCreate/UniversalQuickCreate/UniversalDocumentUploadPCF.ts` - To modify
- `src/controls/UniversalQuickCreate/UniversalQuickCreate/config/EntityDocumentConfig.ts` - Optional update

### Backend API
- `src/api/Spe.Bff.Api/Api/NavMapEndpoints.cs` - API endpoints (already complete)
- `src/shared/Spaarke.Dataverse/IDataverseService.cs` - Metadata methods (already complete)

### Documentation
- `dev/projects/quickcreate_pcf_component/ARCHITECTURE.md` - Architecture reference
- `PRODUCTION-IMPACT-VERIFICATION.md` - Impact analysis from package upgrades

---

## Notes

- **Authentication:** NavMapClient reuses same MSAL token as file operations (no additional auth setup needed)
- **Caching:** BFF API caches metadata for 15 minutes (fast subsequent calls)
- **Fallback:** BFF API has hardcoded fallback values if Dataverse query fails
- **Error Handling:** NavMapClient provides user-friendly error messages
- **Performance:** Metadata query adds ~100-300ms to document creation (acceptable)

---

**Status:** Ready to implement in next session
**Estimated Completion:** 1-2 hours
**Risk Level:** Low (well-tested API, minimal PCF changes)
