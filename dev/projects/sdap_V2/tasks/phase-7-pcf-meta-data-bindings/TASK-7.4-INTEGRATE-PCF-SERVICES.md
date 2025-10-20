# Task 7.4: Integrate NavMapClient with DocumentRecordService

**Task ID:** 7.4
**Phase:** 7 (Navigation Property Metadata Service)
**Assignee:** Frontend Developer
**Estimated Duration:** 2-4 hours
**Dependencies:** Task 7.3 (NavMapClient created and tested)
**Status:** Not Started

---

## Task Prompt

**IMPORTANT: Before starting this task, execute the following steps:**

1. **Read and validate this task document** against the current codebase state
2. **Verify Task 7.3 is complete:**
   - NavMapClient.ts exists and compiles
   - Layer 3 fallback tested and working
   - Types defined in types/NavMap.ts
   - index.ts initializes NavMapClient
3. **Review current DocumentRecordService:**
   - Location: `src/controls/UniversalQuickCreate/UniversalQuickCreate/services/DocumentRecordService.ts`
   - Current implementation (Phase 6): Uses `config.navigationPropertyName` directly
   - Understand how `createDocument()` constructs payload
4. **Review reference documents:**
   - [PHASE-7-OVERVIEW.md](./PHASE-7-OVERVIEW.md) - Integration requirements
   - [TASK-7.3-CREATE-NAVMAP-CLIENT.md](./TASK-7.3-CREATE-NAVMAP-CLIENT.md) - NavMapClient API
5. **Verify backward compatibility requirement:** Phase 6 behavior must still work if NavMapClient unavailable
6. **Update this document** if any assumptions are incorrect or outdated
7. **Commit any documentation updates** before beginning implementation

---

## Objectives

Integrate NavMapClient with DocumentRecordService to enable dynamic navigation property resolution:

1. ✅ Update DocumentRecordService to accept NavMapClient instance
2. ✅ Modify `createDocument()` to use NavMapClient.getNavEntry() for navigation properties
3. ✅ Maintain backward compatibility (fall back to config if NavMapClient unavailable)
4. ✅ Add clear error messages for unsupported parent entities
5. ✅ Update index.ts to pass NavMapClient to DocumentRecordService
6. ✅ Implement `getBffAccessToken()` for server metadata calls (if not deferred)
7. ✅ Test with multiple parent entities (Matter, Project, etc.)
8. ✅ Verify TypeScript compilation succeeds
9. ✅ Ensure no breaking changes to existing upload functionality

---

## Architecture Overview

### Before (Phase 6) - Hardcoded from Config

```typescript
// DocumentRecordService.ts (Phase 6)
const config = ENTITY_DOCUMENT_CONFIGS[parentEntityName];
const navigationPropertyName = config.navigationPropertyName; // "sprk_Matter"

if (!navigationPropertyName) {
  throw new Error(`Navigation property not configured for ${parentEntityName}`);
}

const payload = {
  sprk_documentname: file.name,
  // ... other fields
  [`${navigationPropertyName}@odata.bind`]: `/${entitySetName}(${id})`
};

await context.webAPI.createRecord('sprk_document', payload);
```

**Limitation:** Requires manual validation and config update for each new parent entity

---

### After (Phase 7) - Dynamic from NavMapClient

```typescript
// DocumentRecordService.ts (Phase 7)
const navEntry = this.navMapClient?.getNavEntry(parentEntityName);

if (!navEntry) {
  // Fallback to config (backward compatibility)
  const config = ENTITY_DOCUMENT_CONFIGS[parentEntityName];
  navEntry = {
    entitySet: config.entitySetName,
    navProperty: config.navigationPropertyName ?? config.lookupFieldName
  };
}

const payload = {
  sprk_documentname: file.name,
  // ... other fields
  [`${navEntry.navProperty}@odata.bind`]: `/${navEntry.entitySet}(${id})`
};

await context.webAPI.createRecord('sprk_document', payload);
```

**Benefits:**
- Server metadata automatically updates (5 min cache TTL)
- No manual validation needed for new entities
- Graceful fallback to config if server unavailable

---

## Implementation Steps

### Step 1: Update DocumentRecordService Constructor

**File:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/services/DocumentRecordService.ts`

**Changes:**

```typescript
import { NavMapClient } from './NavMapClient';
import { NavEntry } from '../types/NavMap';

export class DocumentRecordService {
  private readonly context: ComponentFramework.Context<any>;
  private readonly navMapClient?: NavMapClient; // Optional for backward compatibility

  constructor(
    context: ComponentFramework.Context<any>,
    navMapClient?: NavMapClient  // Optional parameter
  ) {
    this.context = context;
    this.navMapClient = navMapClient;

    if (!navMapClient) {
      console.warn(
        '[DocumentRecordService] NavMapClient not provided, using config-based fallback (Phase 6 behavior)'
      );
    }
  }

  // ... rest of class
}
```

**Why Optional:** Maintains backward compatibility if NavMapClient initialization fails or is not available

---

### Step 2: Add Helper Method for Navigation Entry Resolution

**File:** `DocumentRecordService.ts`

**New Method:**

```typescript
/**
 * Resolve navigation entry for a parent entity.
 *
 * Precedence:
 * 1. NavMapClient (server metadata with cache)
 * 2. EntityDocumentConfig (hardcoded fallback)
 *
 * @param parentEntityName - Parent entity logical name (e.g., "sprk_matter")
 * @returns NavEntry with entitySet and navProperty
 * @throws Error if entity not found in either NavMapClient or config
 */
private resolveNavigationEntry(parentEntityName: string): NavEntry {
  // Try NavMapClient first (Phase 7 - server metadata)
  if (this.navMapClient?.isLoaded()) {
    const navEntry = this.navMapClient.getNavEntry(parentEntityName);

    if (navEntry) {
      console.log(
        `[DocumentRecordService] Using server metadata for '${parentEntityName}'`,
        { navProperty: navEntry.navProperty, entitySet: navEntry.entitySet }
      );
      return navEntry;
    } else {
      console.warn(
        `[DocumentRecordService] Parent entity '${parentEntityName}' not found in server metadata, trying config fallback`
      );
    }
  }

  // Fallback to config (Phase 6 - hardcoded values)
  const config = ENTITY_DOCUMENT_CONFIGS[parentEntityName];

  if (!config) {
    throw new Error(
      `Parent entity '${parentEntityName}' not supported. ` +
      `Available entities: ${Object.keys(ENTITY_DOCUMENT_CONFIGS).join(', ')}. ` +
      `Please configure this entity in EntityDocumentConfig or contact your administrator.`
    );
  }

  if (!config.navigationPropertyName) {
    throw new Error(
      `Navigation property not configured for entity '${parentEntityName}'. ` +
      `Please update EntityDocumentConfig with 'navigationPropertyName' field.`
    );
  }

  console.log(
    `[DocumentRecordService] Using config fallback for '${parentEntityName}'`,
    { navProperty: config.navigationPropertyName, entitySet: config.entitySetName }
  );

  return {
    entitySet: config.entitySetName,
    lookupAttribute: config.lookupFieldName,
    navProperty: config.navigationPropertyName
  };
}
```

---

### Step 3: Update createDocument() Method

**File:** `DocumentRecordService.ts`

**Current Implementation (Phase 6):**

```typescript
public async createDocument(
  file: UploadedFile,
  parentRecordId: string,
  parentEntityName: string,
  containerId: string,
  formData: DocumentFormData
): Promise<string> {
  try {
    const config = ENTITY_DOCUMENT_CONFIGS[parentEntityName];
    if (!config) {
      throw new Error(`Entity ${parentEntityName} not configured`);
    }

    const navigationPropertyName = config.navigationPropertyName;
    if (!navigationPropertyName) {
      throw new Error(`Navigation property not configured for ${parentEntityName}`);
    }

    const sanitizedGuid = this.sanitizeGuid(parentRecordId);
    const entitySetName = config.entitySetName;

    const payload = {
      sprk_documentname: file.name,
      sprk_filename: file.name,
      sprk_filesize: file.size,
      sprk_graphitemid: file.id,
      sprk_graphdriveid: containerId,
      sprk_documentdescription: formData.description,
      [`${navigationPropertyName}@odata.bind`]: `/${entitySetName}(${sanitizedGuid})`
    };

    const result = await this.context.webAPI.createRecord('sprk_document', payload);
    return result.id;
  } catch (error) {
    console.error('[DocumentRecordService] Create failed:', error);
    throw error;
  }
}
```

---

**New Implementation (Phase 7):**

```typescript
public async createDocument(
  file: UploadedFile,
  parentRecordId: string,
  parentEntityName: string,
  containerId: string,
  formData: DocumentFormData
): Promise<string> {
  try {
    console.log('[DocumentRecordService] Creating document record', {
      fileName: file.name,
      parentEntity: parentEntityName,
      parentId: parentRecordId
    });

    // Resolve navigation entry (server metadata → config fallback)
    const navEntry = this.resolveNavigationEntry(parentEntityName);

    // Sanitize parent record ID
    const sanitizedGuid = this.sanitizeGuid(parentRecordId);

    // Construct @odata.bind binding target
    // Example: "sprk_Matter@odata.bind" : "/sprk_matters(guid)"
    const bindingProperty = `${navEntry.navProperty}@odata.bind`;
    const bindingTarget = `/${navEntry.entitySet}(${sanitizedGuid})`;

    console.log('[DocumentRecordService] Payload construction', {
      bindingProperty,
      bindingTarget,
      navPropertyCase: navEntry.navProperty // Log case for debugging
    });

    // Construct document payload
    const payload = {
      sprk_documentname: file.name,
      sprk_filename: file.name,
      sprk_filesize: file.size,
      sprk_graphitemid: file.id,
      sprk_graphdriveid: containerId,
      sprk_documentdescription: formData.description,
      [bindingProperty]: bindingTarget
    };

    // Create document record in Dataverse
    const result = await this.context.webAPI.createRecord('sprk_document', payload);

    console.log('[DocumentRecordService] Document created successfully', {
      documentId: result.id,
      fileName: file.name,
      parentEntity: parentEntityName
    });

    return result.id;

  } catch (error) {
    console.error('[DocumentRecordService] Create document failed', {
      error,
      fileName: file.name,
      parentEntity: parentEntityName,
      parentId: parentRecordId
    });

    // Re-throw with enhanced error message
    if (error instanceof Error) {
      throw new Error(
        `Failed to create document record: ${error.message}. ` +
        `File: ${file.name}, Parent: ${parentEntityName}`
      );
    }

    throw error;
  }
}
```

**Key Changes:**
1. Replace direct config access with `resolveNavigationEntry()`
2. Use `navEntry.navProperty` (case-sensitive) instead of `config.navigationPropertyName`
3. Use `navEntry.entitySet` instead of `config.entitySetName`
4. Enhanced logging for debugging case sensitivity issues
5. Better error messages with context

---

### Step 4: Update PCF index.ts to Pass NavMapClient

**File:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/index.ts`

**Current Initialization (Phase 6):**

```typescript
this._documentService = new DocumentRecordService(context);
```

---

**New Initialization (Phase 7):**

```typescript
export class UniversalDocumentUpload implements ComponentFramework.StandardControl<IInputs, IOutputs> {
  private _navMapClient: NavMapClient;
  private _documentService: DocumentRecordService;
  // ... other fields

  public async init(
    context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    state: ComponentFramework.Dictionary,
    container: HTMLDivElement
  ): Promise<void> {
    this._context = context;

    // Initialize NavMapClient
    const bffBaseUrl = this.getBffBaseUrl();
    this._navMapClient = new NavMapClient(bffBaseUrl);

    // Load NavMap in background (non-blocking)
    this._navMapClient.loadNavMap(context).catch(err => {
      console.error('[PCF] NavMap load failed, will use config fallback', err);
    });

    // Initialize DocumentRecordService with NavMapClient
    this._documentService = new DocumentRecordService(context, this._navMapClient);

    // ... rest of initialization
  }

  // ... rest of control
}
```

**Why Pass in Constructor:** Ensures DocumentRecordService has access to NavMapClient from the start

---

### Step 5: Implement getBffAccessToken() in NavMapClient (If Not Deferred)

**File:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/services/NavMapClient.ts`

**Current Placeholder:**

```typescript
private async getBffAccessToken(
  context: ComponentFramework.Context<any>
): Promise<string | null> {
  console.warn('[NavMapClient] getBffAccessToken - Using placeholder');
  return null; // Skip server layer for now
}
```

---

**Implementation Options:**

#### **Option A: Reuse Existing ApiClient (Recommended)**

If PCF already has an ApiClient service for BFF communication:

```typescript
import { ApiClient } from './ApiClient'; // Adjust import path

private async getBffAccessToken(
  context: ComponentFramework.Context<any>
): Promise<string | null> {
  try {
    const apiClient = new ApiClient(context);
    return await apiClient.getAccessToken();
  } catch (error) {
    console.error('[NavMapClient] Failed to get BFF access token', error);
    return null;
  }
}
```

---

#### **Option B: Inline MSAL Token Acquisition**

If PCF uses MSAL directly:

```typescript
import { PublicClientApplication } from '@azure/msal-browser';

private async getBffAccessToken(
  context: ComponentFramework.Context<any>
): Promise<string | null> {
  try {
    const msalConfig = {
      auth: {
        clientId: 'your-client-id', // From environment or config
        authority: 'https://login.microsoftonline.com/your-tenant-id'
      }
    };

    const msalInstance = new PublicClientApplication(msalConfig);
    await msalInstance.initialize();

    const accounts = msalInstance.getAllAccounts();
    if (accounts.length === 0) {
      console.warn('[NavMapClient] No MSAL accounts available');
      return null;
    }

    const result = await msalInstance.acquireTokenSilent({
      scopes: ['api://your-bff-client-id/.default'], // BFF API scope
      account: accounts[0]
    });

    return result.accessToken;

  } catch (error) {
    console.error('[NavMapClient] MSAL token acquisition failed', error);
    return null;
  }
}
```

---

#### **Option C: Defer to Separate Service (Dependency Injection)**

Pass token acquisition function as parameter:

```typescript
// NavMapClient constructor
constructor(
  bffBaseUrl: string,
  getAccessToken?: () => Promise<string | null>
) {
  this.bffBaseUrl = bffBaseUrl;
  this.getAccessToken = getAccessToken ?? (() => Promise.resolve(null));
}

// In index.ts
const navMapClient = new NavMapClient(
  bffBaseUrl,
  async () => await this._apiClient.getAccessToken()
);
```

---

**Recommendation:**

- **Use Option A** if ApiClient already exists (DRY principle)
- **Use Option C** for better testability and separation of concerns
- **Use Option B** only if no existing auth abstraction

**Decision Point:** Choose implementation based on current PCF architecture review

---

### Step 6: Add Configuration for BFF Base URL

**File:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/ControlManifest.Input.xml`

**Add Input Parameter (Optional):**

```xml
<property name="bffBaseUrl"
          display-name-key="BFF Base URL"
          description-key="Base URL for Backend for Frontend API (e.g., https://sdap-prod.azurewebsites.net)"
          of-type="SingleLine.Text"
          usage="input"
          required="false" />
```

**Why Optional:** Allows admin to override URL, but can fall back to environment detection

---

**Update getBffBaseUrl() in index.ts:**

```typescript
private getBffBaseUrl(): string {
  // Option 1: Use input parameter if provided
  const configuredUrl = this._context.parameters.bffBaseUrl?.raw;
  if (configuredUrl) {
    console.log('[PCF] Using configured BFF URL:', configuredUrl);
    return configuredUrl;
  }

  // Option 2: Detect from hostname
  const hostname = window.location.hostname;

  if (hostname.includes('localhost')) {
    return 'https://localhost:7229';
  } else if (hostname.includes('dev')) {
    return 'https://sdap-dev.azurewebsites.net';
  } else {
    return 'https://sdap-prod.azurewebsites.net';
  }
}
```

---

## Error Handling

### Error Scenario 1: Parent Entity Not Supported

**Symptom:** User tries to upload to an entity not in NavMapClient or config

**Code:**
```typescript
// In resolveNavigationEntry()
if (!navEntry && !config) {
  throw new Error(
    `Parent entity '${parentEntityName}' not supported. ` +
    `Available entities: ${Object.keys(ENTITY_DOCUMENT_CONFIGS).join(', ')}. ` +
    `Please configure this entity or contact your administrator.`
  );
}
```

**Expected:**
- User sees clear error message listing available entities
- Upload UI shows error, doesn't crash
- Console logs include entity name for debugging

---

### Error Scenario 2: NavMapClient Not Loaded Yet

**Symptom:** User uploads before NavMapClient.loadNavMap() completes

**Code:**
```typescript
// In resolveNavigationEntry()
if (this.navMapClient && !this.navMapClient.isLoaded()) {
  console.warn(
    '[DocumentRecordService] NavMapClient not loaded yet, using config fallback'
  );
}
```

**Expected:**
- Falls back to config immediately (no blocking wait)
- Upload continues without delay
- Console warning logged for monitoring

---

### Error Scenario 3: Navigation Property Case Mismatch

**Symptom:** Server returns lowercase but Dataverse expects PascalCase

**Code:**
```typescript
// In createDocument()
console.log('[DocumentRecordService] Payload construction', {
  bindingProperty: `${navEntry.navProperty}@odata.bind`,
  navPropertyCase: navEntry.navProperty // Log exact case
});
```

**Expected:**
- Console logs show exact case being used
- If error occurs, logs help debug case sensitivity issue
- Server metadata should already have correct case (from EntityDefinitions)

---

### Error Scenario 4: createRecord Fails with "Undeclared Property"

**Symptom:** Dataverse rejects payload due to incorrect navigation property

**Code:**
```typescript
catch (error) {
  console.error('[DocumentRecordService] Create document failed', {
    error,
    bindingProperty: `${navEntry.navProperty}@odata.bind`,
    navProperty: navEntry.navProperty,
    entitySet: navEntry.entitySet,
    parentEntity: parentEntityName
  });

  throw new Error(
    `Failed to create document record: ${error.message}. ` +
    `Check navigation property case: '${navEntry.navProperty}'. ` +
    `File: ${file.name}, Parent: ${parentEntityName}`
  );
}
```

**Expected:**
- Error message includes navigation property name and case
- Console logs show exact payload sent
- Helps identify if issue is server metadata or config

---

## Testing Checklist

### Before Marking Task Complete:

- [ ] **TypeScript Compilation:** No errors in `npm run build`
- [ ] **Backward Compatibility:** Upload still works if NavMapClient.loadNavMap() fails
- [ ] **Multi-Entity Support:** Test with at least 2 different parent entities (Matter + Project)
- [ ] **Error Messages:** Clear, actionable errors for unsupported entities
- [ ] **Logging:** Console logs show which source used (server metadata vs config)
- [ ] **Case Sensitivity:** Navigation property case matches Dataverse metadata
- [ ] **getBffAccessToken:** Implemented or documented as deferred
- [ ] **BFF URL Config:** getBffBaseUrl() returns correct URL for environment
- [ ] **No Breaking Changes:** Existing Phase 6 uploads still work

---

## Manual Testing Instructions

### Test 1: Upload with Server Metadata (Layer 1)

**Prerequisites:**
- Task 7.2 deployed: `/api/pcf/dataverse-navmap` accessible
- `getBffAccessToken()` implemented and returns valid token

**Setup:**
1. Clear session storage: `sessionStorage.clear()`
2. Open PCF control on Matter form
3. Open browser console

**Execute:**
1. Select file and upload
2. Observe console logs

**Expected:**
```
[NavMapClient] Layer 1 SUCCESS - Loaded from server
[DocumentRecordService] Using server metadata for 'sprk_matter'
[DocumentRecordService] Document created successfully
```

**Verify:**
- Document record created in Dataverse
- Navigation property binding correct (sprk_Matter)

---

### Test 2: Upload with Config Fallback (Layer 3)

**Prerequisites:**
- Set BFF URL to invalid address in `getBffBaseUrl()`
- Clear session storage

**Execute:**
1. Repeat upload test

**Expected:**
```
[NavMapClient] Layer 3 FALLBACK - Using hardcoded values
[DocumentRecordService] Using config fallback for 'sprk_matter'
[DocumentRecordService] Document created successfully
```

**Verify:**
- Upload still works (backward compatibility maintained)
- Navigation property from config used

---

### Test 3: Upload to Multiple Parent Entities

**Prerequisites:**
- Add Project entity to NavMapClient fallback (or server metadata if available)

**Execute:**
1. Upload to Matter entity
2. Upload to Project entity

**Expected:**
- Both uploads succeed
- Console shows correct entity set and navigation property for each

---

### Test 4: Unsupported Entity Error

**Setup:**
1. Attempt upload to entity NOT in NavMapClient or config (e.g., "account" if not configured)

**Expected:**
```
Error: Parent entity 'account' not supported.
Available entities: sprk_matter, sprk_project, sprk_invoice.
Please configure this entity or contact your administrator.
```

**Verify:**
- Error message clear and actionable
- Lists available entities
- Doesn't crash UI

---

### Test 5: NavMapClient Not Loaded Yet

**Setup:**
1. Modify `loadNavMap()` to add 5 second delay (for testing)
2. Upload immediately after opening form

**Expected:**
- Upload proceeds without waiting
- Config fallback used
- Console warning: "NavMapClient not loaded yet, using config fallback"

---

## Validation Checklist

### Code Quality:

- [ ] **No TypeScript errors** in VSCode or build output
- [ ] **Consistent naming** (camelCase variables, PascalCase types)
- [ ] **JSDoc comments** for public methods
- [ ] **Error messages** include context and available options
- [ ] **Console logs** use appropriate levels (log, warn, error)

### Architecture:

- [ ] **Separation of concerns:** resolveNavigationEntry() isolated
- [ ] **Dependency injection:** NavMapClient passed to constructor
- [ ] **Backward compatibility:** Config fallback always available
- [ ] **Fail-safe:** Errors don't crash upload, show clear messages

### Integration:

- [ ] **index.ts** initializes DocumentRecordService with NavMapClient
- [ ] **NavMapClient** loaded in background (non-blocking)
- [ ] **createDocument()** uses resolveNavigationEntry()
- [ ] **getBffAccessToken()** implemented or deferred with documentation

---

## Expected Results vs Actual Results

### Expected After Task 7.4 Complete:

1. ✅ DocumentRecordService accepts NavMapClient
2. ✅ createDocument() uses server metadata if available
3. ✅ Falls back to config if server unavailable
4. ✅ Upload works with multiple parent entities
5. ✅ Clear error messages for unsupported entities
6. ✅ TypeScript compiles without errors
7. ✅ No breaking changes to Phase 6 functionality

### Actual Results (Fill in during testing):

- [ ] Server metadata used: ✅ Yes / ❌ No (reason: ___)
- [ ] Config fallback works: ✅ Yes / ❌ No (issue: ___)
- [ ] Matter entity upload: ✅ Success / ❌ Error: ___
- [ ] Project entity upload: ✅ Success / ❌ Error: ___
- [ ] Unsupported entity error: ✅ Clear message / ❌ Issue: ___
- [ ] Backward compatibility: ✅ Maintained / ❌ Broken: ___

**Notes:**

---

## Commit Message Template

```
feat(pcf): Integrate NavMapClient with DocumentRecordService for dynamic metadata

Update DocumentRecordService to use NavMapClient for navigation property
resolution with config fallback for resilience.

**Implementation:**
- DocumentRecordService accepts optional NavMapClient in constructor
- New method: resolveNavigationEntry(parentEntity)
  - Tries NavMapClient (server metadata) first
  - Falls back to EntityDocumentConfig if unavailable
- Updated createDocument() to use dynamic navigation properties
- Enhanced error messages for unsupported entities
- Pass NavMapClient from index.ts to DocumentRecordService

**Backward Compatibility:**
- NavMapClient optional (maintains Phase 6 behavior if unavailable)
- Config fallback ensures uploads always work
- No changes to existing upload API or UI

**Behavior:**
- Server metadata used when available (5 min cache TTL)
- Config fallback used if server down or entity not in metadata
- Clear error if entity not supported by either source
- Detailed logging for debugging case sensitivity

**Testing:**
- Upload with server metadata verified (Layer 1)
- Upload with config fallback verified (Layer 3)
- Multiple parent entities tested (Matter, Project)
- Unsupported entity error message validated
- Backward compatibility confirmed (Phase 6 still works)

**Files:**
- MODIFIED: services/DocumentRecordService.ts (use NavMapClient)
- MODIFIED: index.ts (pass NavMapClient to DocumentRecordService)
- MODIFIED: services/NavMapClient.ts (implement getBffAccessToken if not deferred)
- MODIFIED: ControlManifest.Input.xml (add bffBaseUrl parameter if needed)

**Next Steps:**
- Task 7.5: Comprehensive testing and validation
- Monitor cache hit rate and server metadata accuracy

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude <noreply@anthropic.com>
```

---

## Dependencies for Next Task (7.5)

**Task 7.5 will need:**

1. **Working integration:** DocumentRecordService using NavMapClient
2. **Multiple entities:** At least 2 parent entities configured and tested
3. **All layers tested:** Server, cache, and config fallback
4. **Error scenarios:** Validated with clear messages
5. **Test data:** Multiple parent records available for testing

**Handoff checklist:**
- [ ] Integration complete and compiling
- [ ] Basic upload test passing (at least Matter entity)
- [ ] Fallback mechanism verified
- [ ] Console logs clear for debugging
- [ ] Ready for comprehensive QA testing

---

## References

- [PHASE-7-OVERVIEW.md](./PHASE-7-OVERVIEW.md) - Architecture overview
- [TASK-7.3-CREATE-NAVMAP-CLIENT.md](./TASK-7.3-CREATE-NAVMAP-CLIENT.md) - NavMapClient API
- [TASK-7.5-TESTING-VALIDATION.md](./TASK-7.5-TESTING-VALIDATION.md) - Next task
- Phase 6 implementation: `services/DocumentRecordService.ts` (current)
- Config reference: `config/EntityDocumentConfig.ts`

---

**Task Created:** 2025-10-20
**Task Owner:** Frontend Developer
**Status:** Not Started
**Blocking:** Task 7.5 (Testing)
