# Task 7.3: Create NavMapClient in PCF Control

**Task ID:** 7.3
**Phase:** 7 (Navigation Property Metadata Service)
**Assignee:** Frontend Developer
**Estimated Duration:** 4-6 hours
**Dependencies:** Task 7.2 (NavMapController deployed and accessible)
**Status:** Not Started

---

## Task Prompt

**IMPORTANT: Before starting this task, execute the following steps:**

1. **Read and validate this task document** against the current codebase state
2. **Verify Task 7.2 is complete:**
   - `/api/pcf/dataverse-navmap` endpoint exists in Spe.Bff.Api
   - Endpoint returns 200 OK with valid NavMapResponse
   - Test with: `curl -H "Authorization: Bearer {token}" https://{bff-url}/api/pcf/dataverse-navmap?v=1`
3. **Review reference documents:**
   - [PHASE-7-OVERVIEW.md](./PHASE-7-OVERVIEW.md) - Architecture and 3-layer fallback pattern
   - [TASK-7.2-CREATE-NAVMAP-CONTROLLER.md](./TASK-7.2-CREATE-NAVMAP-CONTROLLER.md) - Server contract
4. **Confirm current PCF structure:**
   - Location: `src/controls/UniversalQuickCreate/UniversalQuickCreate/`
   - Existing services folder structure
   - Current authentication mechanism (BFF token acquisition)
5. **Update this document** if any assumptions are incorrect or outdated
6. **Commit any documentation updates** before beginning implementation

---

## Objectives

Create a TypeScript client service (`NavMapClient`) in the PCF control that:

1. ✅ Fetches navigation metadata from the BFF `/api/pcf/dataverse-navmap` endpoint
2. ✅ Implements 3-layer fallback pattern for resilience:
   - **Layer 1:** Server API call
   - **Layer 2:** Session storage cache
   - **Layer 3:** Hardcoded fallback (Phase 6 values)
3. ✅ Caches metadata in memory and sessionStorage
4. ✅ Provides type-safe access to navigation properties
5. ✅ Handles errors gracefully with clear logging
6. ✅ Integrates with existing PCF authentication
7. ✅ Compiles without TypeScript errors
8. ✅ Maintains backward compatibility (falls back to Phase 6 behavior if server unavailable)

---

## Architecture Overview

### 3-Layer Fallback Pattern

```
┌─────────────────────────────────────────────────────────┐
│  PCF Control Initialization (index.ts)                  │
│  ┌───────────────────────────────────────────────────┐ │
│  │ 1. Load NavMap on init()                         │ │
│  │    navMapClient.loadNavMap(context)              │ │
│  │    └─> Attempt Layer 1 (server)                  │ │
│  └───────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────┐
│  NavMapClient (services/NavMapClient.ts)                │
│  ┌───────────────────────────────────────────────────┐ │
│  │ Layer 1: Server API                              │ │
│  │ fetch('/api/pcf/dataverse-navmap?v=1')           │ │
│  │ ├─ Success → Cache in memory + sessionStorage   │ │
│  │ │              Return NavMap                     │ │
│  │ └─ Failure (timeout, 500, network)              │ │
│  │              ↓                                    │ │
│  │ Layer 2: Session Storage Cache                   │ │
│  │ sessionStorage.getItem('navmap::v1')             │ │
│  │ ├─ Hit → Parse JSON, cache in memory            │ │
│  │ │         Return NavMap                          │ │
│  │ └─ Miss → ↓                                       │ │
│  │ Layer 3: Hardcoded Fallback                      │ │
│  │ NAVMAP_FALLBACK constant (Phase 6 values)        │ │
│  │ └─ Always available, validated                   │ │
│  │    Log warning, return fallback                  │ │
│  └───────────────────────────────────────────────────┘ │
│  ┌───────────────────────────────────────────────────┐ │
│  │ Public API:                                       │ │
│  │ - loadNavMap(context): Promise<void>             │ │
│  │ - getNavEntry(parentEntity): NavEntry | null     │ │
│  │ - getAllEntries(): Record<string, NavEntry>      │ │
│  │ - isLoaded(): boolean                            │ │
│  └───────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
```

---

## Data Structures

### TypeScript Types (Match Server Contract)

```typescript
/**
 * Navigation metadata entry for a parent entity.
 * Maps to NavEntry record from BFF API.
 */
export interface NavEntry {
  /** Entity set name (e.g., "sprk_matters") */
  entitySet: string;

  /** Lookup attribute name (e.g., "sprk_matter") */
  lookupAttribute: string;

  /** Navigation property name - CASE SENSITIVE (e.g., "sprk_Matter") */
  navProperty: string;

  /** Collection navigation property (for future Option B support) */
  collectionNavProperty?: string;
}

/**
 * Navigation map: parent entity logical name → NavEntry.
 * Example: { "sprk_matter": { entitySet: "sprk_matters", ... } }
 */
export type NavMap = Record<string, NavEntry>;

/**
 * Server response from /api/pcf/dataverse-navmap.
 */
export interface NavMapResponse {
  /** Navigation entries by parent entity */
  parents: NavMap;

  /** API version (e.g., "1") */
  version: string;

  /** ISO 8601 timestamp when metadata was generated */
  generatedAt: string;

  /** Environment name (optional) */
  environment?: string;
}
```

---

## Implementation Steps

### Step 1: Create NavMapClient.ts File

**File:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/services/NavMapClient.ts`

**Purpose:** Centralized client for loading and accessing navigation metadata.

**Implementation:**

```typescript
/**
 * NavMapClient - Client for loading navigation property metadata
 *
 * Implements 3-layer fallback pattern:
 * 1. Server API (/api/pcf/dataverse-navmap)
 * 2. Session storage cache
 * 3. Hardcoded fallback (Phase 6 values)
 *
 * @example
 * const client = new NavMapClient(bffBaseUrl);
 * await client.loadNavMap(context);
 * const navEntry = client.getNavEntry('sprk_matter');
 * if (navEntry) {
 *   const bindingTarget = `${navEntry.navProperty}@odata.bind`;
 * }
 */

import { NavEntry, NavMap, NavMapResponse } from '../types/NavMap';

// ========================================
// LAYER 3: HARDCODED FALLBACK (Phase 6)
// ========================================

/**
 * Fallback navigation map using validated Phase 6 values.
 * Used when server unavailable AND session cache empty.
 *
 * MAINTENANCE: When adding new entities, add entries here after validation.
 */
const NAVMAP_FALLBACK: NavMap = {
  sprk_matter: {
    entitySet: 'sprk_matters',
    lookupAttribute: 'sprk_matter',
    navProperty: 'sprk_Matter',  // ⚠️ CASE SENSITIVE - validated
    collectionNavProperty: 'sprk_matter_document'
  },
  // TODO: Add other entities after validation (Project, Invoice, Account, Contact)
  // sprk_project: { ... },
  // sprk_invoice: { ... },
  // account: { ... },
  // contact: { ... }
};

// ========================================
// CONSTANTS
// ========================================

const NAVMAP_VERSION = '1';
const SESSION_STORAGE_KEY = `navmap::v${NAVMAP_VERSION}`;
const API_ENDPOINT = '/api/pcf/dataverse-navmap';
const API_TIMEOUT_MS = 5000;  // 5 second timeout for server call

// ========================================
// NAVMAP CLIENT
// ========================================

export class NavMapClient {
  private navMap: NavMap | null = null;
  private isLoadedFlag = false;
  private readonly bffBaseUrl: string;

  constructor(bffBaseUrl: string) {
    this.bffBaseUrl = bffBaseUrl.replace(/\/$/, ''); // Remove trailing slash
  }

  /**
   * Load navigation map using 3-layer fallback pattern.
   *
   * @param context - PCF context for authentication
   * @returns Promise that resolves when NavMap is loaded (never rejects)
   */
  public async loadNavMap(
    context: ComponentFramework.Context<any>
  ): Promise<void> {
    console.log('[NavMapClient] Starting NavMap load with 3-layer fallback');

    // Layer 1: Try server API
    const serverNavMap = await this.tryLoadFromServer(context);
    if (serverNavMap) {
      this.navMap = serverNavMap;
      this.isLoadedFlag = true;
      this.saveToSessionStorage(serverNavMap);
      console.log('[NavMapClient] ✅ Layer 1 SUCCESS - Loaded from server');
      return;
    }

    // Layer 2: Try session storage
    const cachedNavMap = this.tryLoadFromSessionStorage();
    if (cachedNavMap) {
      this.navMap = cachedNavMap;
      this.isLoadedFlag = true;
      console.log('[NavMapClient] ✅ Layer 2 SUCCESS - Loaded from session cache');
      return;
    }

    // Layer 3: Use hardcoded fallback (always succeeds)
    this.navMap = NAVMAP_FALLBACK;
    this.isLoadedFlag = true;
    console.warn(
      '[NavMapClient] ⚠️ Layer 3 FALLBACK - Using hardcoded values (Phase 6 behavior)',
      { availableEntities: Object.keys(NAVMAP_FALLBACK) }
    );
  }

  /**
   * Get navigation entry for a parent entity.
   *
   * @param parentEntityLogicalName - Parent entity logical name (e.g., "sprk_matter")
   * @returns NavEntry or null if not found
   */
  public getNavEntry(parentEntityLogicalName: string): NavEntry | null {
    if (!this.isLoadedFlag) {
      console.error('[NavMapClient] getNavEntry called before loadNavMap completed');
      return null;
    }

    const entry = this.navMap?.[parentEntityLogicalName] ?? null;

    if (!entry) {
      console.warn(
        `[NavMapClient] No NavEntry found for parent entity '${parentEntityLogicalName}'`,
        { availableEntities: Object.keys(this.navMap ?? {}) }
      );
    }

    return entry;
  }

  /**
   * Get all navigation entries.
   *
   * @returns Full NavMap (parent entity → NavEntry)
   */
  public getAllEntries(): NavMap {
    return this.navMap ?? {};
  }

  /**
   * Check if NavMap has been loaded.
   *
   * @returns true if loadNavMap has completed
   */
  public isLoaded(): boolean {
    return this.isLoadedFlag;
  }

  /**
   * Get count of available parent entities.
   *
   * @returns Number of entities in NavMap
   */
  public getEntityCount(): number {
    return Object.keys(this.navMap ?? {}).length;
  }

  // ========================================
  // LAYER 1: SERVER API
  // ========================================

  /**
   * Attempt to load NavMap from BFF server.
   *
   * @param context - PCF context for authentication
   * @returns NavMap if successful, null if failed
   */
  private async tryLoadFromServer(
    context: ComponentFramework.Context<any>
  ): Promise<NavMap | null> {
    try {
      console.log('[NavMapClient] Layer 1: Attempting server fetch...');

      // Get BFF access token using existing auth mechanism
      // ASSUMPTION: PCF has method to get BFF token (verify during validation)
      const token = await this.getBffAccessToken(context);
      if (!token) {
        console.warn('[NavMapClient] Layer 1 SKIP - No BFF access token available');
        return null;
      }

      // Fetch with timeout
      const controller = new AbortController();
      const timeoutId = setTimeout(() => controller.abort(), API_TIMEOUT_MS);

      const url = `${this.bffBaseUrl}${API_ENDPOINT}?v=${NAVMAP_VERSION}`;
      const response = await fetch(url, {
        method: 'GET',
        headers: {
          'Authorization': `Bearer ${token}`,
          'Accept': 'application/json'
        },
        signal: controller.signal
      });

      clearTimeout(timeoutId);

      if (!response.ok) {
        console.warn(
          `[NavMapClient] Layer 1 FAILED - Server returned ${response.status}`,
          { url, status: response.status, statusText: response.statusText }
        );
        return null;
      }

      const data: NavMapResponse = await response.json();

      // Validate response structure
      if (!data.parents || typeof data.parents !== 'object') {
        console.error('[NavMapClient] Layer 1 FAILED - Invalid response structure', data);
        return null;
      }

      console.log('[NavMapClient] Layer 1 SUCCESS - Server returned NavMap', {
        entityCount: Object.keys(data.parents).length,
        entities: Object.keys(data.parents),
        version: data.version,
        generatedAt: data.generatedAt
      });

      return data.parents;

    } catch (error) {
      if ((error as Error).name === 'AbortError') {
        console.warn(`[NavMapClient] Layer 1 FAILED - Request timeout after ${API_TIMEOUT_MS}ms`);
      } else {
        console.warn('[NavMapClient] Layer 1 FAILED - Network or parsing error', error);
      }
      return null;
    }
  }

  /**
   * Get BFF access token for API calls.
   *
   * IMPLEMENTATION NOTE: This method needs to integrate with existing
   * PCF authentication mechanism. Common approaches:
   *
   * Option A: Use existing ApiClient service
   *   const apiClient = new ApiClient(context);
   *   return await apiClient.getAccessToken();
   *
   * Option B: Use MSAL directly
   *   const msalInstance = await getMsalInstance(context);
   *   const result = await msalInstance.acquireTokenSilent({...});
   *   return result.accessToken;
   *
   * Option C: Delegate to parent component
   *   Pass token as parameter to loadNavMap() instead
   *
   * TODO: Update this method during Task 7.4 integration
   */
  private async getBffAccessToken(
    context: ComponentFramework.Context<any>
  ): Promise<string | null> {
    try {
      // PLACEHOLDER - Replace with actual implementation
      console.warn('[NavMapClient] getBffAccessToken - Using placeholder, implement in Task 7.4');

      // For now, return null to skip server layer during development
      // In production, this MUST be implemented
      return null;

      // Example implementation (uncomment and adjust):
      // const apiClient = new ApiClient(context);
      // return await apiClient.getAccessToken();

    } catch (error) {
      console.error('[NavMapClient] Failed to get BFF access token', error);
      return null;
    }
  }

  // ========================================
  // LAYER 2: SESSION STORAGE
  // ========================================

  /**
   * Attempt to load NavMap from session storage.
   *
   * @returns NavMap if found and valid, null otherwise
   */
  private tryLoadFromSessionStorage(): NavMap | null {
    try {
      console.log('[NavMapClient] Layer 2: Attempting session storage load...');

      const cached = sessionStorage.getItem(SESSION_STORAGE_KEY);
      if (!cached) {
        console.log('[NavMapClient] Layer 2 MISS - No cached data found');
        return null;
      }

      const navMap: NavMap = JSON.parse(cached);

      // Validate structure
      if (typeof navMap !== 'object' || navMap === null) {
        console.warn('[NavMapClient] Layer 2 INVALID - Cached data malformed');
        sessionStorage.removeItem(SESSION_STORAGE_KEY); // Clean up bad data
        return null;
      }

      console.log('[NavMapClient] Layer 2 HIT - Loaded from cache', {
        entityCount: Object.keys(navMap).length,
        entities: Object.keys(navMap)
      });

      return navMap;

    } catch (error) {
      console.warn('[NavMapClient] Layer 2 FAILED - Error reading session storage', error);
      return null;
    }
  }

  /**
   * Save NavMap to session storage for future requests.
   *
   * @param navMap - NavMap to cache
   */
  private saveToSessionStorage(navMap: NavMap): void {
    try {
      const serialized = JSON.stringify(navMap);
      sessionStorage.setItem(SESSION_STORAGE_KEY, serialized);
      console.log('[NavMapClient] Cached NavMap to session storage', {
        entityCount: Object.keys(navMap).length,
        sizeBytes: serialized.length
      });
    } catch (error) {
      console.warn('[NavMapClient] Failed to cache NavMap to session storage', error);
      // Non-fatal - continue without cache
    }
  }
}
```

---

### Step 2: Create NavMap Type Definitions

**File:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/types/NavMap.ts`

**Purpose:** Shared TypeScript types for navigation metadata (matches server contract).

**Implementation:**

```typescript
/**
 * Type definitions for Navigation Metadata Service (Phase 7).
 *
 * These types match the server-side NavEntry and NavMapResponse from
 * Spe.Bff.Api NavMapController (see TASK-7.2).
 */

/**
 * Navigation metadata entry for a parent entity.
 */
export interface NavEntry {
  /** Entity set name (e.g., "sprk_matters") */
  entitySet: string;

  /** Lookup attribute name (e.g., "sprk_matter") */
  lookupAttribute: string;

  /** Navigation property name - CASE SENSITIVE (e.g., "sprk_Matter") */
  navProperty: string;

  /** Collection navigation property (for future Option B support) */
  collectionNavProperty?: string;
}

/**
 * Navigation map: parent entity logical name → NavEntry.
 */
export type NavMap = Record<string, NavEntry>;

/**
 * Server response from /api/pcf/dataverse-navmap.
 */
export interface NavMapResponse {
  /** Navigation entries by parent entity */
  parents: NavMap;

  /** API version (e.g., "1") */
  version: string;

  /** ISO 8601 timestamp when metadata was generated */
  generatedAt: string;

  /** Environment name (optional) */
  environment?: string;
}
```

---

### Step 3: Update PCF index.ts for NavMapClient Initialization

**File:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/index.ts`

**Purpose:** Initialize NavMapClient during PCF control initialization.

**Changes:**

```typescript
import { NavMapClient } from './services/NavMapClient';

export class UniversalDocumentUpload implements ComponentFramework.StandardControl<IInputs, IOutputs> {
  private _context: ComponentFramework.Context<IInputs>;
  private _navMapClient: NavMapClient;
  // ... other fields

  public async init(
    context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    state: ComponentFramework.Dictionary,
    container: HTMLDivElement
  ): Promise<void> {
    this._context = context;

    // Initialize NavMapClient with BFF base URL
    const bffBaseUrl = this.getBffBaseUrl(); // Get from config or environment
    this._navMapClient = new NavMapClient(bffBaseUrl);

    // Load NavMap in background (non-blocking)
    // Errors are handled internally with fallback
    this._navMapClient.loadNavMap(context).catch(err => {
      console.error('[PCF] NavMap load failed, using fallback', err);
    });

    // ... rest of initialization
  }

  /**
   * Get BFF base URL from configuration.
   *
   * IMPLEMENTATION OPTIONS:
   * 1. From PCF input parameter (configurable in form designer)
   * 2. From global config/environment variable
   * 3. Hardcoded for specific environment
   *
   * TODO: Determine best approach during Task 7.4 integration
   */
  private getBffBaseUrl(): string {
    // PLACEHOLDER - Replace with actual implementation
    // Option 1: From input parameter
    // return this._context.parameters.bffBaseUrl?.raw ?? '';

    // Option 2: From environment detection
    const hostname = window.location.hostname;
    if (hostname.includes('localhost')) {
      return 'https://localhost:7229'; // Local dev
    } else if (hostname.includes('dev')) {
      return 'https://sdap-dev.azurewebsites.net'; // Dev
    } else {
      return 'https://sdap-prod.azurewebsites.net'; // Prod
    }
  }

  // Expose NavMapClient to services (Task 7.4)
  public getNavMapClient(): NavMapClient {
    return this._navMapClient;
  }

  // ... rest of control implementation
}
```

---

### Step 4: Add Unit Tests (Optional but Recommended)

**File:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/tests/NavMapClient.test.ts`

**Purpose:** Validate NavMapClient behavior in isolation.

**Test Scenarios:**

```typescript
import { NavMapClient } from '../services/NavMapClient';
import { NavMap } from '../types/NavMap';

describe('NavMapClient', () => {

  describe('Layer 3 - Hardcoded Fallback', () => {
    it('should use fallback when server unavailable and no cache', async () => {
      const client = new NavMapClient('https://invalid.local');
      const mockContext = {} as any; // Mock PCF context

      await client.loadNavMap(mockContext);

      expect(client.isLoaded()).toBe(true);
      expect(client.getEntityCount()).toBeGreaterThan(0);

      const matterEntry = client.getNavEntry('sprk_matter');
      expect(matterEntry).not.toBeNull();
      expect(matterEntry?.navProperty).toBe('sprk_Matter');
    });
  });

  describe('Layer 2 - Session Storage', () => {
    beforeEach(() => {
      sessionStorage.clear();
    });

    it('should load from session storage if available', async () => {
      const testNavMap: NavMap = {
        sprk_test: {
          entitySet: 'sprk_tests',
          lookupAttribute: 'sprk_test',
          navProperty: 'sprk_Test',
        }
      };

      sessionStorage.setItem('navmap::v1', JSON.stringify(testNavMap));

      const client = new NavMapClient('https://invalid.local');
      await client.loadNavMap({} as any);

      expect(client.isLoaded()).toBe(true);
      const entry = client.getNavEntry('sprk_test');
      expect(entry?.navProperty).toBe('sprk_Test');
    });

    it('should handle corrupted session storage gracefully', async () => {
      sessionStorage.setItem('navmap::v1', 'invalid json{');

      const client = new NavMapClient('https://invalid.local');
      await client.loadNavMap({} as any);

      // Should fall back to hardcoded
      expect(client.isLoaded()).toBe(true);
    });
  });

  describe('Layer 1 - Server API', () => {
    it('should fetch from server when available', async () => {
      // This test requires mocking fetch
      // Implementation depends on test framework (Jest, Vitest, etc.)
    });
  });

  describe('getNavEntry', () => {
    it('should return null for unknown entity', async () => {
      const client = new NavMapClient('https://invalid.local');
      await client.loadNavMap({} as any);

      const entry = client.getNavEntry('sprk_unknown');
      expect(entry).toBeNull();
    });

    it('should return null before loadNavMap called', () => {
      const client = new NavMapClient('https://invalid.local');
      const entry = client.getNavEntry('sprk_matter');
      expect(entry).toBeNull();
    });
  });
});
```

---

## Error Handling

### Error Scenario 1: Server API Timeout

**Symptom:** Server takes >5 seconds to respond

**Handling:**
```typescript
// In tryLoadFromServer()
const controller = new AbortController();
const timeoutId = setTimeout(() => controller.abort(), API_TIMEOUT_MS);

fetch(url, { signal: controller.signal })
  .then(...)
  .catch(error => {
    if (error.name === 'AbortError') {
      console.warn('[NavMapClient] Layer 1 FAILED - Request timeout');
    }
    return null; // Fall through to Layer 2
  });
```

**Expected:** Gracefully fall back to Layer 2 (session cache)

---

### Error Scenario 2: Server Returns 401 Unauthorized

**Symptom:** BFF access token invalid or expired

**Handling:**
```typescript
if (response.status === 401) {
  console.warn('[NavMapClient] Layer 1 FAILED - Unauthorized (token expired?)');
  return null; // Fall through to Layer 2
}
```

**Expected:** Use cached metadata or fallback, don't block user

---

### Error Scenario 3: Server Returns Malformed JSON

**Symptom:** Response structure doesn't match NavMapResponse

**Handling:**
```typescript
const data = await response.json();

if (!data.parents || typeof data.parents !== 'object') {
  console.error('[NavMapClient] Layer 1 FAILED - Invalid response structure', data);
  return null; // Fall through to Layer 2
}
```

**Expected:** Validate response, fall back if invalid

---

### Error Scenario 4: Session Storage Full

**Symptom:** QuotaExceededError when saving to sessionStorage

**Handling:**
```typescript
try {
  sessionStorage.setItem(SESSION_STORAGE_KEY, serialized);
} catch (error) {
  console.warn('[NavMapClient] Failed to cache - storage full?', error);
  // Non-fatal - continue without cache
}
```

**Expected:** Log warning but continue (server will be used on next load)

---

## Testing Checklist

### Before Marking Task Complete:

- [ ] **TypeScript Compilation:** No errors in `npm run build`
- [ ] **File Structure:** NavMapClient.ts and NavMap.ts in correct locations
- [ ] **Layer 3 (Fallback):** loadNavMap() succeeds when server unreachable
- [ ] **Layer 2 (Cache):** loadNavMap() uses sessionStorage if populated
- [ ] **Layer 1 (Server):** loadNavMap() fetches from BFF when available (skip if Task 7.2 not deployed)
- [ ] **Error Handling:** All 4 error scenarios tested
- [ ] **Logging:** Console logs helpful messages for debugging
- [ ] **Type Safety:** All types match server contract (NavEntry, NavMapResponse)
- [ ] **Unit Tests:** Basic tests pass (if implemented)
- [ ] **Integration:** index.ts initializes NavMapClient without errors
- [ ] **Documentation:** Code comments explain 3-layer fallback

---

## Manual Testing Instructions

### Test 1: Layer 3 Fallback (Server Unavailable)

**Setup:**
1. Set BFF URL to invalid address in `getBffBaseUrl()`
2. Clear session storage: `sessionStorage.clear()`

**Execute:**
```typescript
const client = new NavMapClient('https://invalid.local');
await client.loadNavMap(context);

console.log('Loaded:', client.isLoaded()); // Should be true
console.log('Count:', client.getEntityCount()); // Should be >0
console.log('Matter:', client.getNavEntry('sprk_matter')); // Should return NavEntry
```

**Expected:**
- Console shows "Layer 3 FALLBACK" warning
- `isLoaded()` returns true
- `getNavEntry('sprk_matter')` returns valid NavEntry with `navProperty: 'sprk_Matter'`

---

### Test 2: Layer 2 Cache Hit

**Setup:**
1. Manually populate session storage:
```typescript
const testData = {
  sprk_matter: {
    entitySet: 'sprk_matters',
    lookupAttribute: 'sprk_matter',
    navProperty: 'sprk_Matter'
  }
};
sessionStorage.setItem('navmap::v1', JSON.stringify(testData));
```
2. Set BFF URL to invalid address

**Execute:**
```typescript
const client = new NavMapClient('https://invalid.local');
await client.loadNavMap(context);
```

**Expected:**
- Console shows "Layer 2 SUCCESS - Loaded from session cache"
- No "Layer 3 FALLBACK" warning

---

### Test 3: Layer 1 Server Success (Requires Task 7.2 Complete)

**Setup:**
1. Ensure Task 7.2 deployed: `/api/pcf/dataverse-navmap` accessible
2. Implement `getBffAccessToken()` to return valid token
3. Clear session storage

**Execute:**
```typescript
const client = new NavMapClient('https://your-bff-url.azurewebsites.net');
await client.loadNavMap(context);
```

**Expected:**
- Console shows "Layer 1 SUCCESS - Server returned NavMap"
- Session storage populated with fetched data
- `getEntityCount()` matches server response

---

### Test 4: Invalid Entity Lookup

**Execute:**
```typescript
const client = new NavMapClient('https://invalid.local');
await client.loadNavMap(context);

const entry = client.getNavEntry('sprk_unknown_entity');
console.log('Entry:', entry); // Should be null
```

**Expected:**
- Console warning: "No NavEntry found for parent entity 'sprk_unknown_entity'"
- Returns null (not undefined or error)

---

## Validation Checklist

### Code Quality:

- [ ] **No TypeScript errors** in VSCode or build output
- [ ] **No console.log() in production code** (use console.warn/error appropriately)
- [ ] **Consistent naming** (camelCase for variables, PascalCase for types)
- [ ] **JSDoc comments** for all public methods
- [ ] **Error messages** are actionable and include context

### Architecture:

- [ ] **3-layer fallback** implemented correctly
- [ ] **Server layer** isolated (easy to mock/disable)
- [ ] **Fallback values** match Phase 6 (sprk_Matter with capital M)
- [ ] **Session storage** key includes version (`navmap::v1`)
- [ ] **Timeout** enforced on server calls (5 seconds)

### Integration:

- [ ] **index.ts** initializes NavMapClient
- [ ] **getBffBaseUrl()** returns correct URL for environment
- [ ] **getBffAccessToken()** placeholder documented for Task 7.4
- [ ] **Backward compatible** (doesn't break existing upload)

---

## Expected Results vs Actual Results

### Expected After Task 7.3 Complete:

1. ✅ NavMapClient.ts exists and compiles
2. ✅ 3-layer fallback pattern implemented
3. ✅ Layer 3 (hardcoded) always works
4. ✅ Layer 2 (session cache) tested
5. ✅ Layer 1 (server) structure complete (auth pending Task 7.4)
6. ✅ PCF initializes NavMapClient without errors
7. ✅ Console logs show fallback behavior
8. ✅ Type definitions match server contract

### Actual Results (Fill in during testing):

- [ ] TypeScript compilation: ✅ Success / ❌ Errors: ___
- [ ] Layer 3 fallback: ✅ Works / ❌ Issue: ___
- [ ] Layer 2 cache: ✅ Works / ❌ Issue: ___
- [ ] getNavEntry('sprk_matter'): ✅ Returns NavEntry / ❌ Issue: ___
- [ ] Error handling: ✅ Graceful / ❌ Issue: ___

**Notes:**

---

## Commit Message Template

```
feat(pcf): Add NavMapClient with 3-layer fallback for metadata loading

Create client service to load navigation property metadata from BFF
server with resilient fallback pattern.

**Implementation:**
- NavMapClient service with 3-layer fallback:
  Layer 1: Server API (/api/pcf/dataverse-navmap)
  Layer 2: Session storage cache
  Layer 3: Hardcoded fallback (Phase 6 values)
- Type definitions matching server contract (NavEntry, NavMapResponse)
- 5-second timeout on server calls
- Error handling for network, auth, and parsing errors
- Initialize in PCF index.ts (non-blocking background load)

**Behavior:**
- Loads metadata on PCF init()
- Falls back gracefully if server unavailable
- Caches in sessionStorage for performance
- Always succeeds (Layer 3 fallback ensures availability)

**Testing:**
- Layer 3 fallback verified (server down scenario)
- Layer 2 cache hit verified (session storage)
- Type safety validated (TypeScript compilation)
- Error scenarios tested (timeout, 401, malformed JSON)

**Files:**
- NEW: services/NavMapClient.ts
- NEW: types/NavMap.ts
- MODIFIED: index.ts (initialize NavMapClient)

**Next Steps:**
- Task 7.4: Integrate NavMapClient with DocumentRecordService
- Implement getBffAccessToken() for Layer 1 server calls

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude <noreply@anthropic.com>
```

---

## Dependencies for Next Task (7.4)

**Task 7.4 will need:**

1. **NavMapClient instance:** Accessible from DocumentRecordService
2. **Auth implementation:** Complete `getBffAccessToken()` method
3. **BFF URL config:** Finalize `getBffBaseUrl()` approach
4. **Integration point:** Update DocumentRecordService.createDocument() to use NavMapClient

**Handoff checklist:**
- [ ] NavMapClient compiles and initializes
- [ ] Layer 3 fallback verified working
- [ ] Public API documented (getNavEntry, getAllEntries, isLoaded)
- [ ] Task 7.4 developer has access to NavMapClient instance

---

## References

- [PHASE-7-OVERVIEW.md](./PHASE-7-OVERVIEW.md) - Architecture overview
- [TASK-7.2-CREATE-NAVMAP-CONTROLLER.md](./TASK-7.2-CREATE-NAVMAP-CONTROLLER.md) - Server contract
- [TASK-7.4-INTEGRATE-PCF-SERVICES.md](./TASK-7.4-INTEGRATE-PCF-SERVICES.md) - Next task
- Phase 6 config: `config/EntityDocumentConfig.ts` (fallback values source)

---

**Task Created:** 2025-10-20
**Task Owner:** Frontend Developer
**Status:** Not Started
**Blocking:** Task 7.4 (Integration)
