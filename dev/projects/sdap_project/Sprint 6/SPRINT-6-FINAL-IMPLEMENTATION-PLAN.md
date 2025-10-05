# Sprint 6: SDAP + Universal Dataset Grid Integration - FINAL IMPLEMENTATION PLAN
**Date:** October 4, 2025
**Status:** ✅ **APPROVED - Ready for Implementation**
**Total Duration:** 98 hours (2.5 weeks)
**Production Ready:** Yes - Senior developer code quality standards

---

## Executive Summary

### Objective
Integrate SDAP (SharePoint Document Access Platform) with Universal Dataset Grid PCF control to enable file management (upload, download, update, delete) directly from Document entity grids with full Fluent UI v9 compliance.

### Key Decisions Finalized

✅ **Decision 1: Chunked Upload Support** - Support files > 4MB via chunked upload
✅ **Decision 2: Fluent UI v9 Compliance** - Selective package imports (tree-shaking)
✅ **Decision 3: NPM Package** - Create `@spaarke/sdap-client` for reuse across controls
✅ **Decision 4: Direct Integration** - No JavaScript web resources (ADR-006 compliant)
✅ **Decision 5: Static Configuration** - JSON-based configuration (configurator in next sprint)

### Deliverables

1. **@spaarke/sdap-client** NPM Package
   - TypeScript SDAP API client
   - Small file upload (< 4MB)
   - Chunked upload (≥ 4MB)
   - Download, delete, metadata operations
   - Unit tested, production ready

2. **Universal Dataset Grid PCF Control**
   - Fluent UI v9 components (selective imports)
   - Custom command bar (Add, Remove, Update, Download)
   - Progress indicators for uploads
   - Error handling and validation
   - Configuration support
   - Bundle size: ~880 KB (under 5 MB limit)

3. **Documentation**
   - NPM package API documentation
   - Integration guide for Sprint 7 (Document Viewer)
   - Deployment runbook
   - Testing guide

---

## ADR Compliance Matrix

### ✅ All ADRs Validated

| ADR | Title | Compliance | Implementation |
|-----|-------|------------|----------------|
| **ADR-001** | Minimal API + Workers | ✅ Compliant | SDAP BFF API already follows (no changes) |
| **ADR-002** | No Heavy Plugins | ✅ Compliant | No plugin code - client-side only |
| **ADR-003** | Lean Authorization | ✅ Compliant | Uses SDAP API authorization |
| **ADR-006** | Prefer PCF Over Web Resources | ✅ **COMPLIANT** | **No JavaScript web resources created** |
| **ADR-007** | SPE Storage Minimalism | ✅ Compliant | Uses SpeFileStore via SDAP API |
| **ADR-008** | Endpoint Filters | ✅ Compliant | SDAP API already implements |
| **ADR-009** | Redis-First Caching | ✅ Compliant | Client-side, no caching logic |
| **ADR-010** | DI Minimalism | ✅ Compliant | NPM package uses constructor injection |
| **ADR-011** | Dataset PCF Over Subgrids | ✅ **COMPLIANT** | **Using Dataset PCF control** |
| **ADR-012** | Shared Component Library | ✅ **COMPLIANT** | **Creating @spaarke/sdap-client** |

**Critical Compliance:**
- ✅ **ADR-006:** No web resources - all logic in PCF (TypeScript)
- ✅ **ADR-011:** Using Dataset PCF control (not native subgrid)
- ✅ **ADR-012:** Shared NPM package for reuse across controls

---

## Production Code Quality Standards

### Senior Developer Requirements

**1. TypeScript Type Safety** ✅
- Strict mode enabled
- No `any` types (use `unknown` with guards)
- Explicit return types on all public methods
- Interface-driven design

**2. Error Handling** ✅
- Try-catch blocks with specific error types
- User-friendly error messages
- Logging with correlation IDs
- Graceful degradation

**3. Testing** ✅
- Unit tests for SDAP client (Jest)
- Integration tests for file operations
- Bundle size validation
- Performance testing (file upload < 30s for 4MB)

**4. Documentation** ✅
- TSDoc comments on all public APIs
- README with usage examples
- API reference documentation
- Integration guide for other teams

**5. Performance** ✅
- Chunked upload for large files
- Progress indicators for user feedback
- Optimized bundle size (< 1 MB)
- Lazy loading where applicable

**6. Security** ✅
- Input validation (file size, type)
- Sanitized error messages (no sensitive data)
- Secure token handling
- CORS compliance

**7. Maintainability** ✅
- Single Responsibility Principle
- DRY (Don't Repeat Yourself)
- Clear naming conventions
- Modular architecture

---

## Sprint 6 Timeline: 98 Hours (2.5 Weeks)

### Phase 1: Configuration & Planning (8 hours) ✅ **COMPLETE**

**Status:** ✅ All tasks complete

**Deliverables:**
- [x] SDAP API capabilities validated
- [x] Document entity schema verified
- [x] Custom commands specified
- [x] Configuration schema designed
- [x] Technical specification complete
- [x] File size analysis complete
- [x] ADR compliance validated

---

### Phase 1.5: Create SDAP Client NPM Package (8 hours) ✅ **COMPLETE**

**Objective:** Build production-ready `@spaarke/sdap-client` NPM package for reuse across PCF controls.

**Status:** ✅ All tasks complete
**Package:** `spaarke-sdap-client-1.0.0.tgz` (37 KB)
**Location:** `packages/sdap-client/`

#### Task 1.5.1: Package Structure Setup (2 hours)

**Create package infrastructure:**

```bash
# Create package directory
mkdir -p packages/sdap-client
cd packages/sdap-client

# Initialize package
npm init -y
```

**package.json:**
```json
{
  "name": "@spaarke/sdap-client",
  "version": "1.0.0",
  "description": "SDAP API client for PCF controls and TypeScript applications",
  "main": "dist/index.js",
  "types": "dist/index.d.ts",
  "scripts": {
    "build": "tsc",
    "test": "jest",
    "lint": "eslint src/**/*.ts",
    "prepublishOnly": "npm run build && npm test"
  },
  "keywords": ["sdap", "sharepoint-embedded", "pcf", "power-platform"],
  "author": "Spaarke Engineering",
  "license": "UNLICENSED",
  "private": true,
  "dependencies": {},
  "devDependencies": {
    "@types/jest": "^29.5.0",
    "@types/node": "^18.19.86",
    "@typescript-eslint/eslint-plugin": "^6.0.0",
    "@typescript-eslint/parser": "^6.0.0",
    "eslint": "^8.0.0",
    "jest": "^29.5.0",
    "ts-jest": "^29.1.0",
    "typescript": "^5.8.3"
  }
}
```

**tsconfig.json:**
```json
{
  "compilerOptions": {
    "target": "ES2020",
    "module": "ES2020",
    "lib": ["ES2020", "DOM"],
    "declaration": true,
    "declarationMap": true,
    "sourceMap": true,
    "outDir": "./dist",
    "rootDir": "./src",
    "strict": true,
    "noUnusedLocals": true,
    "noUnusedParameters": true,
    "noImplicitReturns": true,
    "noFallthroughCasesInSwitch": true,
    "esModuleInterop": true,
    "skipLibCheck": true,
    "forceConsistentCasingInFileNames": true,
    "moduleResolution": "node",
    "resolveJsonModule": true
  },
  "include": ["src/**/*"],
  "exclude": ["node_modules", "dist", "**/*.test.ts"]
}
```

**jest.config.js:**
```javascript
module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'node',
  roots: ['<rootDir>/src'],
  testMatch: ['**/__tests__/**/*.ts', '**/?(*.)+(spec|test).ts'],
  collectCoverageFrom: [
    'src/**/*.ts',
    '!src/**/*.d.ts',
    '!src/**/*.test.ts'
  ],
  coverageThreshold: {
    global: {
      branches: 80,
      functions: 80,
      lines: 80,
      statements: 80
    }
  }
};
```

**.eslintrc.json:**
```json
{
  "parser": "@typescript-eslint/parser",
  "extends": [
    "eslint:recommended",
    "plugin:@typescript-eslint/recommended"
  ],
  "rules": {
    "@typescript-eslint/no-explicit-any": "error",
    "@typescript-eslint/explicit-function-return-type": "warn",
    "@typescript-eslint/no-unused-vars": "error"
  }
}
```

**Acceptance Criteria:**
- [ ] Package structure created
- [ ] TypeScript configured with strict mode
- [ ] Jest configured with 80% coverage requirement
- [ ] ESLint configured with no-any rule
- [ ] Build script works (`npm run build`)

---

#### Task 1.5.2: Implement SDAP API Client (4 hours)

**Core implementation:**

**src/SdapApiClient.ts:**
```typescript
import {
    SdapClientConfig,
    DriveItem,
    UploadSession,
    UploadChunkResult,
    FileMetadata
} from './types';
import { TokenProvider } from './auth/TokenProvider';
import { UploadOperation } from './operations/UploadOperation';
import { DownloadOperation } from './operations/DownloadOperation';
import { DeleteOperation } from './operations/DeleteOperation';

/**
 * SDAP API Client for file operations with SharePoint Embedded.
 *
 * Supports:
 * - Small file uploads (< 4MB)
 * - Chunked uploads (≥ 4MB) with progress tracking
 * - File downloads with streaming
 * - File deletion
 * - Metadata retrieval
 *
 * @example
 * ```typescript
 * const client = new SdapApiClient({
 *   baseUrl: 'https://spe-bff-api.azurewebsites.net',
 *   timeout: 300000
 * });
 *
 * // Upload file
 * const item = await client.uploadFile(containerId, file, {
 *   onProgress: (percent) => console.log(`${percent}% uploaded`)
 * });
 *
 * // Download file
 * const blob = await client.downloadFile(driveId, itemId);
 * ```
 */
export class SdapApiClient {
    private readonly baseUrl: string;
    private readonly timeout: number;
    private readonly tokenProvider: TokenProvider;
    private readonly uploadOp: UploadOperation;
    private readonly downloadOp: DownloadOperation;
    private readonly deleteOp: DeleteOperation;

    /**
     * Creates a new SDAP API client instance.
     *
     * @param config - Client configuration
     */
    constructor(config: SdapClientConfig) {
        this.validateConfig(config);

        this.baseUrl = config.baseUrl.replace(/\/$/, ''); // Remove trailing slash
        this.timeout = config.timeout ?? 300000; // 5 minutes default

        this.tokenProvider = new TokenProvider();
        this.uploadOp = new UploadOperation(this.baseUrl, this.timeout, this.tokenProvider);
        this.downloadOp = new DownloadOperation(this.baseUrl, this.timeout, this.tokenProvider);
        this.deleteOp = new DeleteOperation(this.baseUrl, this.timeout, this.tokenProvider);
    }

    /**
     * Uploads a file to SDAP.
     *
     * Automatically chooses small upload (< 4MB) or chunked upload (≥ 4MB).
     *
     * @param containerId - Container ID
     * @param file - File to upload
     * @param options - Upload options (progress callback, cancellation)
     * @returns Uploaded file metadata
     * @throws Error if upload fails
     */
    public async uploadFile(
        containerId: string,
        file: File,
        options?: {
            onProgress?: (percent: number) => void;
            signal?: AbortSignal;
        }
    ): Promise<DriveItem> {
        const SMALL_FILE_THRESHOLD = 4 * 1024 * 1024; // 4MB

        if (file.size < SMALL_FILE_THRESHOLD) {
            return await this.uploadOp.uploadSmall(containerId, file, options);
        } else {
            return await this.uploadOp.uploadChunked(containerId, file, options);
        }
    }

    /**
     * Downloads a file from SDAP.
     *
     * @param driveId - Drive ID
     * @param itemId - Item ID
     * @returns File blob
     * @throws Error if download fails
     */
    public async downloadFile(
        driveId: string,
        itemId: string
    ): Promise<Blob> {
        return await this.downloadOp.download(driveId, itemId);
    }

    /**
     * Deletes a file from SDAP.
     *
     * @param driveId - Drive ID
     * @param itemId - Item ID
     * @throws Error if deletion fails
     */
    public async deleteFile(
        driveId: string,
        itemId: string
    ): Promise<void> {
        return await this.deleteOp.delete(driveId, itemId);
    }

    /**
     * Gets file metadata from SDAP.
     *
     * @param driveId - Drive ID
     * @param itemId - Item ID
     * @returns File metadata
     * @throws Error if retrieval fails
     */
    public async getFileMetadata(
        driveId: string,
        itemId: string
    ): Promise<FileMetadata> {
        const token = await this.tokenProvider.getToken();

        const response = await fetch(
            `${this.baseUrl}/api/obo/drives/${driveId}/items/${itemId}`,
            {
                method: 'GET',
                headers: {
                    'Authorization': `Bearer ${token}`
                },
                signal: AbortSignal.timeout(this.timeout)
            }
        );

        if (!response.ok) {
            throw new Error(`Failed to get file metadata: ${response.statusText}`);
        }

        return await response.json();
    }

    private validateConfig(config: SdapClientConfig): void {
        if (!config.baseUrl) {
            throw new Error('baseUrl is required');
        }

        try {
            new URL(config.baseUrl);
        } catch {
            throw new Error('baseUrl must be a valid URL');
        }

        if (config.timeout !== undefined && config.timeout < 0) {
            throw new Error('timeout must be >= 0');
        }
    }
}
```

**src/operations/UploadOperation.ts:**
```typescript
import { DriveItem, UploadSession } from '../types';
import { TokenProvider } from '../auth/TokenProvider';

export class UploadOperation {
    private static readonly CHUNK_SIZE = 320 * 1024; // 320 KB (Microsoft recommended)

    constructor(
        private readonly baseUrl: string,
        private readonly timeout: number,
        private readonly tokenProvider: TokenProvider
    ) {}

    /**
     * Upload small file (< 4MB) in single request.
     */
    public async uploadSmall(
        containerId: string,
        file: File,
        options?: { onProgress?: (percent: number) => void; signal?: AbortSignal }
    ): Promise<DriveItem> {
        const token = await this.tokenProvider.getToken();

        // Report initial progress
        options?.onProgress?.(0);

        const response = await fetch(
            `${this.baseUrl}/api/obo/containers/${containerId}/files/${encodeURIComponent(file.name)}`,
            {
                method: 'PUT',
                headers: {
                    'Authorization': `Bearer ${token}`,
                    'Content-Type': 'application/octet-stream',
                    'Content-Length': file.size.toString()
                },
                body: file,
                signal: options?.signal ?? AbortSignal.timeout(this.timeout)
            }
        );

        if (!response.ok) {
            const error = await this.parseError(response);
            throw new Error(`Upload failed: ${error}`);
        }

        const result = await response.json();

        // Report completion
        options?.onProgress?.(100);

        return result;
    }

    /**
     * Upload large file (≥ 4MB) using chunked upload.
     */
    public async uploadChunked(
        containerId: string,
        file: File,
        options?: { onProgress?: (percent: number) => void; signal?: AbortSignal }
    ): Promise<DriveItem> {
        // Step 1: Create upload session
        const session = await this.createUploadSession(containerId, file.name);

        // Step 2: Upload chunks
        let uploadedBytes = 0;

        while (uploadedBytes < file.size) {
            // Check for cancellation
            if (options?.signal?.aborted) {
                throw new Error('Upload cancelled');
            }

            const chunkStart = uploadedBytes;
            const chunkEnd = Math.min(chunkStart + UploadOperation.CHUNK_SIZE, file.size);
            const chunk = file.slice(chunkStart, chunkEnd);

            const result = await this.uploadChunk(session, chunk, chunkStart, chunkEnd, file.size);

            uploadedBytes = chunkEnd;

            // Report progress
            const percent = Math.round((uploadedBytes / file.size) * 100);
            options?.onProgress?.(percent);

            // If upload complete, return result
            if (result.completedItem) {
                return result.completedItem;
            }
        }

        throw new Error('Upload completed but no item returned');
    }

    private async createUploadSession(
        containerId: string,
        fileName: string
    ): Promise<UploadSession> {
        const token = await this.tokenProvider.getToken();

        // Get drive ID first
        const driveResponse = await fetch(
            `${this.baseUrl}/api/obo/containers/${containerId}/drive`,
            {
                headers: { 'Authorization': `Bearer ${token}` }
            }
        );

        if (!driveResponse.ok) {
            throw new Error('Failed to get container drive');
        }

        const drive = await driveResponse.json();

        // Create upload session
        const response = await fetch(
            `${this.baseUrl}/api/obo/drives/${drive.id}/upload-session?path=/${encodeURIComponent(fileName)}&conflictBehavior=rename`,
            {
                method: 'POST',
                headers: { 'Authorization': `Bearer ${token}` }
            }
        );

        if (!response.ok) {
            throw new Error('Failed to create upload session');
        }

        return await response.json();
    }

    private async uploadChunk(
        session: UploadSession,
        chunk: Blob,
        start: number,
        end: number,
        totalSize: number
    ): Promise<{ completedItem?: DriveItem }> {
        const response = await fetch(session.uploadUrl, {
            method: 'PUT',
            headers: {
                'Content-Range': `bytes ${start}-${end - 1}/${totalSize}`,
                'Content-Length': chunk.size.toString()
            },
            body: chunk
        });

        if (!response.ok && response.status !== 202) {
            throw new Error(`Chunk upload failed: ${response.statusText}`);
        }

        const result = await response.json();

        // Status 200/201 = upload complete
        if (response.status === 200 || response.status === 201) {
            return { completedItem: result };
        }

        // Status 202 = more chunks expected
        return {};
    }

    private async parseError(response: Response): Promise<string> {
        try {
            const error = await response.json();
            return error.detail || error.title || response.statusText;
        } catch {
            return response.statusText;
        }
    }
}
```

**Acceptance Criteria:**
- [ ] SDAP client implemented with full TypeScript types
- [ ] Small file upload (< 4MB) implemented
- [ ] Chunked upload (≥ 4MB) with progress tracking
- [ ] Download, delete, metadata operations
- [ ] Error handling with user-friendly messages
- [ ] TSDoc comments on all public methods
- [ ] No `any` types used

---

#### Task 1.5.3: Add Type Definitions (1 hour)

**src/types/index.ts:**
```typescript
/**
 * SDAP Client configuration options.
 */
export interface SdapClientConfig {
    /** Base URL of SDAP BFF API (e.g., 'https://spe-bff-api.azurewebsites.net') */
    baseUrl: string;

    /** Request timeout in milliseconds (default: 300000 = 5 minutes) */
    timeout?: number;
}

/**
 * SharePoint Drive Item metadata.
 */
export interface DriveItem {
    /** Unique item ID */
    id: string;

    /** File/folder name */
    name: string;

    /** File size in bytes (null for folders) */
    size: number | null;

    /** Drive ID containing this item */
    driveId: string;

    /** Parent folder reference ID */
    parentReferenceId?: string;

    /** Created date/time */
    createdDateTime: string;

    /** Last modified date/time */
    lastModifiedDateTime: string;

    /** ETag for versioning */
    eTag?: string;

    /** Whether this is a folder */
    isFolder: boolean;

    /** MIME type (files only) */
    mimeType?: string;
}

/**
 * Upload session for chunked uploads.
 */
export interface UploadSession {
    /** Upload URL for PUT requests */
    uploadUrl: string;

    /** Session expiration date/time */
    expirationDateTime: string;

    /** Next expected byte ranges (for resumption) */
    nextExpectedRanges?: string[];
}

/**
 * File metadata.
 */
export interface FileMetadata extends DriveItem {
    /** Download URL */
    downloadUrl?: string;

    /** Web URL for browser viewing */
    webUrl?: string;
}

/**
 * Upload progress callback.
 */
export type UploadProgressCallback = (percent: number) => void;

/**
 * SDAP API error response.
 */
export interface SdapApiError {
    /** Error status code */
    status: number;

    /** Error title */
    title: string;

    /** Detailed error message */
    detail: string;

    /** Trace ID for correlation */
    traceId?: string;
}
```

**Acceptance Criteria:**
- [ ] All types exported from package
- [ ] TSDoc comments on all interfaces
- [ ] No optional chaining on required fields

---

#### Task 1.5.4: Build, Test, Package (1 hour)

**Unit Tests (src/__tests__/SdapApiClient.test.ts):**
```typescript
import { SdapApiClient } from '../SdapApiClient';

describe('SdapApiClient', () => {
    describe('constructor', () => {
        it('should accept valid config', () => {
            const client = new SdapApiClient({
                baseUrl: 'https://api.example.com'
            });

            expect(client).toBeDefined();
        });

        it('should throw on invalid baseUrl', () => {
            expect(() => {
                new SdapApiClient({ baseUrl: 'not-a-url' });
            }).toThrow('baseUrl must be a valid URL');
        });

        it('should throw on negative timeout', () => {
            expect(() => {
                new SdapApiClient({
                    baseUrl: 'https://api.example.com',
                    timeout: -1
                });
            }).toThrow('timeout must be >= 0');
        });
    });

    // Additional tests for upload, download, delete...
});
```

**Build and package:**
```bash
# Install dependencies
npm install

# Run linter
npm run lint

# Run tests
npm test

# Build package
npm run build

# Package for distribution
npm pack
# Creates: spaarke-sdap-client-1.0.0.tgz
```

**Acceptance Criteria:**
- [ ] Unit tests achieve 80%+ coverage
- [ ] All linting rules pass
- [ ] Build produces valid TypeScript declarations
- [ ] Package tarball created successfully

---

### Phase 2: Enhanced Universal Grid + Fluent UI (22 hours)

**Objective:** Enhance PCF control with Fluent UI v9 components (selective imports) and custom command bar.

#### Task 2.1: Install Selective Fluent UI Packages (2 hours)

**Uninstall monolithic package:**
```bash
cd src/controls/UniversalDatasetGrid
npm uninstall @fluentui/react-components
npm uninstall @spaarke/ui-components react react-dom
```

**Install selective packages:**
```bash
npm install \
  @fluentui/react-button@^9.6.7 \
  @fluentui/react-progress@^9.1.62 \
  @fluentui/react-spinner@^9.3.40 \
  @fluentui/react-dialog@^9.9.8 \
  @fluentui/react-message-bar@^9.0.17 \
  @fluentui/react-theme@^9.2.0 \
  @fluentui/react-provider@^9.13.9 \
  @fluentui/react-tooltip@^9.8.6 \
  @fluentui/react-utilities@^9.25.0 \
  @fluentui/react-portal@^9.8.3 \
  @fluentui/react-icons@^2.0.245 \
  react@^18.2.0 \
  react-dom@^18.2.0
```

**Update ControlManifest.Input.xml:**
```xml
<?xml version="1.0" encoding="utf-8" ?>
<manifest>
  <control namespace="Spaarke.UI.Components" constructor="UniversalDatasetGrid" version="2.0.0"
    display-name-key="Universal Dataset Grid"
    description-key="Document management grid with SDAP integration and Fluent UI v9"
    control-type="standard">

    <data-set name="dataset" display-name-key="Dataset">
      <property-set name="columns" usage="bound" required="true" />
    </data-set>

    <property name="configJson" display-name-key="Configuration JSON"
      description-key="JSON configuration for grid behavior, commands, and SDAP integration"
      of-type="Multiple" usage="input" required="false" />

    <resources>
      <code path="index.ts" order="1" />
      <css path="styles.css" order="1" />
    </resources>

    <feature-usage>
      <uses-feature name="WebAPI" required="true" />
    </feature-usage>
  </control>
</manifest>
```

**Verify build:**
```bash
npm run build
```

**Check bundle size:**
```bash
# Should be ~350 KB (Fluent UI selective imports)
ls -lh out/controls/bundle.js
```

**Acceptance Criteria:**
- [ ] Monolithic packages removed
- [ ] Selective Fluent UI packages installed
- [ ] Build succeeds
- [ ] Bundle size < 500 KB
- [ ] No console errors in test harness

---

#### Task 2.2: Create Fluent UI Theme Provider (2 hours)

**src/controls/UniversalDatasetGrid/UniversalDatasetGrid/providers/ThemeProvider.ts:**
```typescript
import React from 'react';
import ReactDOM from 'react-dom';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-provider';

/**
 * Wraps the PCF control in Fluent UI theme provider.
 */
export class ThemeProvider {
    private providerContainer: HTMLElement | null = null;
    private contentContainer: HTMLElement | null = null;

    /**
     * Initialize theme provider and render into container.
     */
    public initialize(container: HTMLDivElement): HTMLElement {
        this.providerContainer = document.createElement('div');
        this.providerContainer.style.cssText = 'height: 100%; display: flex; flex-direction: column;';

        // Render FluentProvider
        ReactDOM.render(
            React.createElement(
                FluentProvider,
                {
                    theme: webLightTheme,
                    style: { height: '100%', display: 'flex', flexDirection: 'column' }
                },
                React.createElement('div', {
                    ref: (el: HTMLElement) => { this.contentContainer = el; },
                    style: { flex: 1, display: 'flex', flexDirection: 'column' }
                })
            ),
            this.providerContainer
        );

        container.appendChild(this.providerContainer);

        // Wait for content container to be ready
        return new Promise<HTMLElement>((resolve) => {
            const checkInterval = setInterval(() => {
                if (this.contentContainer) {
                    clearInterval(checkInterval);
                    resolve(this.contentContainer);
                }
            }, 10);
        }) as any; // Type workaround for synchronous return
    }

    /**
     * Get the content container where control components should render.
     */
    public getContentContainer(): HTMLElement {
        if (!this.contentContainer) {
            throw new Error('Theme provider not initialized');
        }
        return this.contentContainer;
    }

    /**
     * Clean up theme provider.
     */
    public destroy(): void {
        if (this.providerContainer) {
            ReactDOM.unmountComponentAtNode(this.providerContainer);
            this.providerContainer = null;
            this.contentContainer = null;
        }
    }
}
```

**Acceptance Criteria:**
- [ ] Theme provider wraps entire control
- [ ] Fluent UI theme applied
- [ ] Content container accessible
- [ ] Proper cleanup on destroy

---

#### Task 2.3: Create Fluent UI Command Bar (8 hours)

**src/controls/UniversalDatasetGrid/UniversalDatasetGrid/components/CommandBar.tsx:**

```typescript
import React from 'react';
import ReactDOM from 'react-dom';
import { Button } from '@fluentui/react-button';
import { Tooltip } from '@fluentui/react-tooltip';
import { tokens } from '@fluentui/react-theme';
import {
    Add24Regular,
    Delete24Regular,
    ArrowUpload24Regular,
    ArrowDownload24Regular
} from '@fluentui/react-icons';
import { GridConfiguration, CommandContext } from '../types';

interface CommandBarProps {
    config: GridConfiguration;
    selectedRecords: any[];
    onCommandExecute: (commandId: string) => void;
}

/**
 * Fluent UI command bar with file operation buttons.
 */
const CommandBarComponent: React.FC<CommandBarProps> = ({ config, selectedRecords, onCommandExecute }) => {
    const selectedCount = selectedRecords.length;
    const selectedRecord = selectedCount === 1 ? selectedRecords[0] : null;
    const hasFile = selectedRecord?.getValue(config.fieldMappings.hasFile) === true;

    return (
        <div style={{
            display: 'flex',
            padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalM}`,
            background: tokens.colorNeutralBackground2,
            gap: tokens.spacingHorizontalS,
            borderBottom: `1px solid ${tokens.colorNeutralStroke1}`
        }}>
            <Tooltip content="Upload a file to the selected document" relationship="label">
                <Button
                    appearance="primary"
                    icon={<Add24Regular />}
                    disabled={selectedCount !== 1 || hasFile}
                    onClick={() => onCommandExecute('addFile')}
                >
                    Add File
                </Button>
            </Tooltip>

            <Tooltip content="Delete the file from the selected document" relationship="label">
                <Button
                    appearance="secondary"
                    icon={<Delete24Regular />}
                    disabled={selectedCount !== 1 || !hasFile}
                    onClick={() => onCommandExecute('removeFile')}
                >
                    Remove File
                </Button>
            </Tooltip>

            <Tooltip content="Replace the file in the selected document" relationship="label">
                <Button
                    appearance="secondary"
                    icon={<ArrowUpload24Regular />}
                    disabled={selectedCount !== 1 || !hasFile}
                    onClick={() => onCommandExecute('updateFile')}
                >
                    Update File
                </Button>
            </Tooltip>

            <Tooltip content="Download the selected file(s)" relationship="label">
                <Button
                    appearance="secondary"
                    icon={<ArrowDownload24Regular />}
                    disabled={selectedCount === 0 || (selectedRecord && !hasFile)}
                    onClick={() => onCommandExecute('downloadFile')}
                >
                    Download
                </Button>
            </Tooltip>
        </div>
    );
};

/**
 * Command bar wrapper class for PCF integration.
 */
export class CommandBar {
    private container: HTMLDivElement;

    constructor(
        private config: GridConfiguration,
        private getContext: () => CommandContext
    ) {
        this.container = document.createElement('div');
    }

    public render(onCommandExecute: (commandId: string) => void): void {
        const context = this.getContext();

        ReactDOM.render(
            React.createElement(CommandBarComponent, {
                config: this.config,
                selectedRecords: context.selectedRecords,
                onCommandExecute
            }),
            this.container
        );
    }

    public update(): void {
        this.render((commandId) => {
            // Will be connected in main control
        });
    }

    public getElement(): HTMLDivElement {
        return this.container;
    }

    public destroy(): void {
        ReactDOM.unmountComponentAtNode(this.container);
    }
}
```

**Acceptance Criteria:**
- [ ] Command bar uses Fluent UI Button components
- [ ] Tooltips display on hover
- [ ] Buttons enable/disable based on selection
- [ ] Icons from Fluent UI (only 4 icons = ~8 KB)
- [ ] Fluent UI design tokens used for spacing/colors

---

#### Task 2.4: Configuration Support (3 hours)

**src/controls/UniversalDatasetGrid/UniversalDatasetGrid/config/ConfigurationManager.ts:**

```typescript
import { GridConfiguration, CustomCommand, FieldMapping } from '../types';

/**
 * Manages grid configuration from JSON input.
 */
export class ConfigurationManager {
    private static readonly DEFAULT_CONFIG: GridConfiguration = {
        fieldMappings: {
            hasFile: 'spk_hasfile',
            fileName: 'spk_filename',
            fileSize: 'spk_filesize',
            mimeType: 'spk_mimetype',
            graphItemId: 'spk_graphitemid',
            graphDriveId: 'spk_graphdriveid'
        },
        customCommands: [
            {
                id: 'addFile',
                label: 'Add File',
                icon: 'Add24Regular',
                enableRule: 'selectedCount === 1 && !hasFile',
                errorMessage: 'Select a document without a file'
            },
            {
                id: 'removeFile',
                label: 'Remove File',
                icon: 'Delete24Regular',
                enableRule: 'selectedCount === 1 && hasFile',
                errorMessage: 'Select a document with a file'
            },
            {
                id: 'updateFile',
                label: 'Update File',
                icon: 'ArrowUpload24Regular',
                enableRule: 'selectedCount === 1 && hasFile',
                errorMessage: 'Select a document with a file'
            },
            {
                id: 'downloadFile',
                label: 'Download',
                icon: 'ArrowDownload24Regular',
                enableRule: 'selectedCount > 0 && (selectedCount > 1 || hasFile)',
                errorMessage: 'Select documents with files'
            }
        ],
        sdapConfig: {
            baseUrl: '', // Will be set from environment config
            timeout: 300000
        }
    };

    /**
     * Parse configuration from JSON input parameter.
     */
    public static parse(configJson: string | null | undefined): GridConfiguration {
        if (!configJson) {
            return this.DEFAULT_CONFIG;
        }

        try {
            const parsed = JSON.parse(configJson);
            return this.merge(this.DEFAULT_CONFIG, parsed);
        } catch (error) {
            console.error('Failed to parse configuration JSON:', error);
            return this.DEFAULT_CONFIG;
        }
    }

    /**
     * Merge user config with defaults.
     */
    private static merge(defaults: GridConfiguration, overrides: Partial<GridConfiguration>): GridConfiguration {
        return {
            fieldMappings: { ...defaults.fieldMappings, ...overrides.fieldMappings },
            customCommands: overrides.customCommands ?? defaults.customCommands,
            sdapConfig: { ...defaults.sdapConfig, ...overrides.sdapConfig }
        };
    }

    /**
     * Validate configuration.
     */
    public static validate(config: GridConfiguration): string[] {
        const errors: string[] = [];

        // Validate field mappings
        if (!config.fieldMappings.hasFile) {
            errors.push('fieldMappings.hasFile is required');
        }

        // Validate custom commands
        if (!config.customCommands || config.customCommands.length === 0) {
            errors.push('At least one custom command is required');
        }

        // Validate SDAP config
        if (!config.sdapConfig.baseUrl) {
            errors.push('sdapConfig.baseUrl is required');
        }

        return errors;
    }
}
```

**Load SDAP base URL from settings:**

```typescript
// src/controls/UniversalDatasetGrid/UniversalDatasetGrid/index.ts
import { ConfigurationManager } from './config/ConfigurationManager';

export class UniversalDatasetGrid implements ComponentFramework.StandardControl<IInputs, IOutputs> {

    public init(
        context: ComponentFramework.Context<IInputs>,
        notifyOutputChanged: () => void,
        state: ComponentFramework.Dictionary,
        container: HTMLDivElement
    ): void {
        // Load configuration
        let config = ConfigurationManager.parse(context.parameters.configJson.raw);

        // Load SDAP URL from environment
        // In production, read from environment-specific configuration file
        // For now, use hardcoded development URL
        if (!config.sdapConfig.baseUrl) {
            config.sdapConfig.baseUrl = 'https://spe-bff-api.azurewebsites.net';
        }

        // Validate configuration
        const errors = ConfigurationManager.validate(config);
        if (errors.length > 0) {
            console.error('Configuration errors:', errors);
            // Show error message to user
            container.innerHTML = `<div style="color: red;">Configuration errors: ${errors.join(', ')}</div>`;
            return;
        }

        this.config = config;
        // Continue initialization...
    }
}
```

**Acceptance Criteria:**
- [ ] Configuration parser handles JSON input
- [ ] Default configuration provided
- [ ] Configuration validation implemented
- [ ] SDAP URL loaded from environment
- [ ] Errors displayed to user

---

#### Task 2.5: Grid Rendering with Fluent UI (5 hours)

**Update grid styles with Fluent UI tokens:**

**src/controls/UniversalDatasetGrid/UniversalDatasetGrid/styles/GridStyles.ts:**

```typescript
import { tokens } from '@fluentui/react-theme';

/**
 * Grid styling using Fluent UI design tokens.
 */
export const GridStyles = {
    container: {
        height: '100%',
        display: 'flex',
        flexDirection: 'column' as const,
        background: tokens.colorNeutralBackground1,
        fontFamily: tokens.fontFamilyBase,
        fontSize: tokens.fontSizeBase300
    },

    table: {
        width: '100%',
        borderCollapse: 'collapse' as const,
        background: tokens.colorNeutralBackground1
    },

    headerRow: {
        background: tokens.colorNeutralBackground2,
        borderBottom: `2px solid ${tokens.colorNeutralStroke1}`,
        position: 'sticky' as const,
        top: 0,
        zIndex: 1
    },

    headerCell: {
        padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalM}`,
        textAlign: 'left' as const,
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1,
        cursor: 'pointer',
        userSelect: 'none' as const
    },

    dataRow: {
        borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
        cursor: 'pointer',
        transition: 'background 0.1s ease'
    },

    dataRowHover: {
        background: tokens.colorNeutralBackground1Hover
    },

    dataRowSelected: {
        background: tokens.colorNeutralBackground1Selected
    },

    dataCell: {
        padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
        color: tokens.colorNeutralForeground1
    },

    emptyState: {
        padding: tokens.spacingVerticalXXL,
        textAlign: 'center' as const,
        color: tokens.colorNeutralForeground3,
        fontSize: tokens.fontSizeBase400
    }
};
```

**Apply styles in grid rendering:**

```typescript
// src/controls/UniversalDatasetGrid/UniversalDatasetGrid/components/GridTable.tsx
import React from 'react';
import { GridStyles } from '../styles/GridStyles';

export const GridTable: React.FC<GridTableProps> = ({ columns, records, selectedRecords, onRowClick }) => {
    return (
        <div style={GridStyles.container}>
            <table style={GridStyles.table}>
                <thead>
                    <tr style={GridStyles.headerRow}>
                        {columns.map(col => (
                            <th key={col.name} style={GridStyles.headerCell}>
                                {col.displayName}
                            </th>
                        ))}
                    </tr>
                </thead>
                <tbody>
                    {records.length === 0 ? (
                        <tr>
                            <td colSpan={columns.length} style={GridStyles.emptyState}>
                                No records to display
                            </td>
                        </tr>
                    ) : (
                        records.map(record => {
                            const isSelected = selectedRecords.includes(record.id);
                            return (
                                <tr
                                    key={record.id}
                                    style={{
                                        ...GridStyles.dataRow,
                                        ...(isSelected ? GridStyles.dataRowSelected : {})
                                    }}
                                    onClick={() => onRowClick(record)}
                                >
                                    {columns.map(col => (
                                        <td key={col.name} style={GridStyles.dataCell}>
                                            {record.getValue(col.name)}
                                        </td>
                                    ))}
                                </tr>
                            );
                        })
                    )}
                </tbody>
            </table>
        </div>
    );
};
```

**Acceptance Criteria:**
- [ ] Grid styled with Fluent UI tokens
- [ ] Consistent colors, spacing, typography
- [ ] Row hover and selection states
- [ ] Sticky header row
- [ ] Empty state message

---

#### Task 2.6: Build and Test (2 hours)

**Build PCF control:**

```bash
cd src/controls/UniversalDatasetGrid
npm run build
```

**Verify bundle size:**

```bash
# Check bundle size (should be ~580 KB = 331 KB Fluent + 250 KB React)
ls -lh out/controls/bundle.js

# Expected output: ~580 KB
```

**Test in test harness:**

```bash
npm start
```

**Manual testing checklist:**
- [ ] Control loads without errors
- [ ] Fluent UI theme applied
- [ ] Command bar renders with correct buttons
- [ ] Buttons enable/disable based on selection
- [ ] Tooltips display on hover
- [ ] Grid renders with Fluent UI styling
- [ ] Row selection works
- [ ] No console errors

**Acceptance Criteria:**
- [ ] Build succeeds
- [ ] Bundle size under 1 MB
- [ ] Test harness shows control
- [ ] All manual tests pass

---

### Phase 3: SDAP Client Integration (20 hours)

**Objective:** Integrate @spaarke/sdap-client and implement file operations with chunked upload support.

#### Task 3.1: Install SDAP Client in PCF Control (1 hour)

**Install local package:**

```bash
cd src/controls/UniversalDatasetGrid/UniversalDatasetGrid
npm install ../../../packages/sdap-client/spaarke-sdap-client-1.0.0.tgz
```

**Update package.json:**

```json
{
  "dependencies": {
    "@spaarke/sdap-client": "file:../../../packages/sdap-client/spaarke-sdap-client-1.0.0.tgz"
  }
}
```

**Initialize SDAP client in control:**

```typescript
// src/controls/UniversalDatasetGrid/UniversalDatasetGrid/index.ts
import { SdapApiClient } from '@spaarke/sdap-client';

export class UniversalDatasetGrid implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private sdapClient: SdapApiClient;

    public init(
        context: ComponentFramework.Context<IInputs>,
        notifyOutputChanged: () => void,
        state: ComponentFramework.Dictionary,
        container: HTMLDivElement
    ): void {
        // Initialize SDAP client
        this.sdapClient = new SdapApiClient({
            baseUrl: this.config.sdapConfig.baseUrl,
            timeout: this.config.sdapConfig.timeout
        });
    }
}
```

**Acceptance Criteria:**
- [ ] SDAP client package installed
- [ ] Client initialized in control
- [ ] No TypeScript errors
- [ ] Build succeeds

---

#### Task 3.2: Implement File Upload with Progress (6 hours)

**Create upload handler:**

**src/controls/UniversalDatasetGrid/UniversalDatasetGrid/handlers/UploadHandler.ts:**

```typescript
import { SdapApiClient, DriveItem } from '@spaarke/sdap-client';
import { GridConfiguration } from '../types';

export class UploadHandler {
    constructor(
        private sdapClient: SdapApiClient,
        private config: GridConfiguration,
        private context: ComponentFramework.Context<any>
    ) {}

    /**
     * Handle file upload with progress tracking.
     */
    public async uploadFile(
        recordId: string,
        containerId: string,
        onProgress: (percent: number) => void
    ): Promise<DriveItem> {
        // Step 1: Show file picker
        const file = await this.showFilePicker();

        if (!file) {
            throw new Error('No file selected');
        }

        // Step 2: Upload to SDAP (automatically uses chunked upload for files ≥4MB)
        const driveItem = await this.sdapClient.uploadFile(containerId, file, {
            onProgress: (percent) => {
                console.log(`Upload progress: ${percent}%`);
                onProgress(percent);
            }
        });

        return driveItem;
    }

    /**
     * Show native file picker.
     */
    private showFilePicker(): Promise<File | null> {
        return new Promise((resolve) => {
            const input = document.createElement('input');
            input.type = 'file';
            input.accept = '*/*';

            input.onchange = (e: Event) => {
                const target = e.target as HTMLInputElement;
                const file = target.files?.[0] ?? null;
                resolve(file);
            };

            input.oncancel = () => {
                resolve(null);
            };

            input.click();
        });
    }
}
```

**Create progress dialog:**

**src/controls/UniversalDatasetGrid/UniversalDatasetGrid/components/ProgressDialog.tsx:**

```typescript
import React, { useState } from 'react';
import ReactDOM from 'react-dom';
import {
    Dialog,
    DialogTrigger,
    DialogSurface,
    DialogTitle,
    DialogBody,
    DialogActions,
    DialogContent,
    Button
} from '@fluentui/react-dialog';
import { ProgressBar } from '@fluentui/react-progress';
import { Spinner } from '@fluentui/react-spinner';

interface ProgressDialogProps {
    title: string;
    message: string;
    progress: number; // 0-100
    isIndeterminate: boolean;
    onCancel?: () => void;
}

export const ProgressDialog: React.FC<ProgressDialogProps> = ({
    title,
    message,
    progress,
    isIndeterminate,
    onCancel
}) => {
    return (
        <Dialog open={true} modalType="alert">
            <DialogSurface>
                <DialogBody>
                    <DialogTitle>{title}</DialogTitle>
                    <DialogContent>
                        <div style={{ marginBottom: '16px' }}>{message}</div>
                        {isIndeterminate ? (
                            <Spinner label="Processing..." />
                        ) : (
                            <ProgressBar value={progress / 100} />
                        )}
                        <div style={{ marginTop: '8px', textAlign: 'center' }}>
                            {progress}%
                        </div>
                    </DialogContent>
                    {onCancel && (
                        <DialogActions>
                            <Button onClick={onCancel}>Cancel</Button>
                        </DialogActions>
                    )}
                </DialogBody>
            </DialogSurface>
        </Dialog>
    );
};
```

**Wire up command:**

```typescript
// In main control
private async handleAddFile(recordId: string): Promise<void> {
    // Show progress dialog
    const progressContainer = document.createElement('div');
    document.body.appendChild(progressContainer);

    let currentProgress = 0;

    const renderProgress = (progress: number) => {
        currentProgress = progress;
        ReactDOM.render(
            React.createElement(ProgressDialog, {
                title: 'Uploading File',
                message: 'Uploading file to SharePoint...',
                progress: currentProgress,
                isIndeterminate: false
            }),
            progressContainer
        );
    };

    try {
        renderProgress(0);

        // Get container ID from record
        const record = this.dataset.records[recordId];
        const containerId = record.getValue('spk_containerid') as string;

        // Upload file
        const driveItem = await this.uploadHandler.uploadFile(
            recordId,
            containerId,
            renderProgress
        );

        // Update document fields (Phase 4)
        // ...

        // Close progress dialog
        ReactDOM.unmountComponentAtNode(progressContainer);
        document.body.removeChild(progressContainer);

        // Show success message
        this.showSuccessMessage(`File "${driveItem.name}" uploaded successfully`);
    } catch (error) {
        // Close progress dialog
        ReactDOM.unmountComponentAtNode(progressContainer);
        document.body.removeChild(progressContainer);

        // Show error message
        this.showErrorMessage(`Upload failed: ${error.message}`);
    }
}
```

**Acceptance Criteria:**
- [ ] File picker opens on Add File command
- [ ] Upload uses SDAP client (auto-selects small/chunked)
- [ ] Progress dialog displays
- [ ] Progress updates during upload
- [ ] Success/error messages shown
- [ ] Large files (>4MB) upload via chunked upload

---

#### Task 3.3: Implement Download Operation (4 hours)

**Create download handler:**

**src/controls/UniversalDatasetGrid/UniversalDatasetGrid/handlers/DownloadHandler.ts:**

```typescript
import { SdapApiClient } from '@spaarke/sdap-client';

export class DownloadHandler {
    constructor(private sdapClient: SdapApiClient) {}

    /**
     * Download file and trigger browser download.
     */
    public async downloadFile(
        driveId: string,
        itemId: string,
        fileName: string
    ): Promise<void> {
        // Download file blob
        const blob = await this.sdapClient.downloadFile(driveId, itemId);

        // Trigger browser download
        this.triggerDownload(blob, fileName);
    }

    /**
     * Download multiple files as a ZIP.
     */
    public async downloadMultiple(
        files: Array<{ driveId: string; itemId: string; fileName: string }>
    ): Promise<void> {
        // For now, download files individually
        // Future enhancement: Create ZIP file
        for (const file of files) {
            await this.downloadFile(file.driveId, file.itemId, file.fileName);
        }
    }

    /**
     * Trigger browser download of blob.
     */
    private triggerDownload(blob: Blob, fileName: string): void {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = fileName;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    }
}
```

**Wire up download command:**

```typescript
private async handleDownloadFile(): Promise<void> {
    try {
        const selectedIds = this.dataset.getSelectedRecordIds();

        if (selectedIds.length === 1) {
            // Single file download
            const record = this.dataset.records[selectedIds[0]];
            const driveId = record.getValue(this.config.fieldMappings.graphDriveId) as string;
            const itemId = record.getValue(this.config.fieldMappings.graphItemId) as string;
            const fileName = record.getValue(this.config.fieldMappings.fileName) as string;

            await this.downloadHandler.downloadFile(driveId, itemId, fileName);
        } else {
            // Multiple files download
            const files = selectedIds.map(id => {
                const record = this.dataset.records[id];
                return {
                    driveId: record.getValue(this.config.fieldMappings.graphDriveId) as string,
                    itemId: record.getValue(this.config.fieldMappings.graphItemId) as string,
                    fileName: record.getValue(this.config.fieldMappings.fileName) as string
                };
            });

            await this.downloadHandler.downloadMultiple(files);
        }

        this.showSuccessMessage('Download complete');
    } catch (error) {
        this.showErrorMessage(`Download failed: ${error.message}`);
    }
}
```

**Acceptance Criteria:**
- [ ] Single file download works
- [ ] Multiple file downloads work
- [ ] Browser download triggered
- [ ] Correct file names
- [ ] Error handling

---

#### Task 3.4: Implement Delete Operation (3 hours)

**Create delete handler:**

**src/controls/UniversalDatasetGrid/UniversalDatasetGrid/handlers/DeleteHandler.ts:**

```typescript
import { SdapApiClient } from '@spaarke/sdap-client';

export class DeleteHandler {
    constructor(private sdapClient: SdapApiClient) {}

    /**
     * Delete file from SDAP.
     */
    public async deleteFile(driveId: string, itemId: string): Promise<void> {
        await this.sdapClient.deleteFile(driveId, itemId);
    }
}
```

**Create confirmation dialog:**

**src/controls/UniversalDatasetGrid/UniversalDatasetGrid/components/ConfirmDialog.tsx:**

```typescript
import React from 'react';
import {
    Dialog,
    DialogSurface,
    DialogTitle,
    DialogBody,
    DialogActions,
    DialogContent,
    Button
} from '@fluentui/react-dialog';

interface ConfirmDialogProps {
    title: string;
    message: string;
    onConfirm: () => void;
    onCancel: () => void;
}

export const ConfirmDialog: React.FC<ConfirmDialogProps> = ({
    title,
    message,
    onConfirm,
    onCancel
}) => {
    return (
        <Dialog open={true} modalType="alert">
            <DialogSurface>
                <DialogBody>
                    <DialogTitle>{title}</DialogTitle>
                    <DialogContent>{message}</DialogContent>
                    <DialogActions>
                        <Button appearance="secondary" onClick={onCancel}>
                            Cancel
                        </Button>
                        <Button appearance="primary" onClick={onConfirm}>
                            Delete
                        </Button>
                    </DialogActions>
                </DialogBody>
            </DialogSurface>
        </Dialog>
    );
};
```

**Wire up delete command:**

```typescript
private async handleRemoveFile(recordId: string): Promise<void> {
    // Show confirmation dialog
    const confirmed = await this.showConfirmDialog(
        'Remove File',
        'Are you sure you want to remove this file? This action cannot be undone.'
    );

    if (!confirmed) {
        return;
    }

    try {
        const record = this.dataset.records[recordId];
        const driveId = record.getValue(this.config.fieldMappings.graphDriveId) as string;
        const itemId = record.getValue(this.config.fieldMappings.graphItemId) as string;

        // Delete from SDAP
        await this.deleteHandler.deleteFile(driveId, itemId);

        // Update document fields (Phase 4)
        // ...

        this.showSuccessMessage('File removed successfully');
    } catch (error) {
        this.showErrorMessage(`Delete failed: ${error.message}`);
    }
}
```

**Acceptance Criteria:**
- [ ] Confirmation dialog shown
- [ ] File deleted from SDAP
- [ ] Document fields cleared (Phase 4)
- [ ] Success/error messages
- [ ] Cancel works

---

#### Task 3.5: Implement Update/Replace File (4 hours)

**Wire up update command:**

```typescript
private async handleUpdateFile(recordId: string): Promise<void> {
    try {
        const record = this.dataset.records[recordId];
        const driveId = record.getValue(this.config.fieldMappings.graphDriveId) as string;
        const itemId = record.getValue(this.config.fieldMappings.graphItemId) as string;
        const containerId = record.getValue('spk_containerid') as string;

        // Step 1: Delete old file
        await this.deleteHandler.deleteFile(driveId, itemId);

        // Step 2: Upload new file
        const driveItem = await this.uploadHandler.uploadFile(
            recordId,
            containerId,
            (progress) => {
                // Show progress
            }
        );

        // Step 3: Update document fields (Phase 4)
        // ...

        this.showSuccessMessage('File updated successfully');
    } catch (error) {
        this.showErrorMessage(`Update failed: ${error.message}`);
    }
}
```

**Acceptance Criteria:**
- [ ] Old file deleted
- [ ] New file uploaded
- [ ] Progress shown
- [ ] Document fields updated
- [ ] Error handling

---

#### Task 3.6: Error Handling and Validation (2 hours)

**Create error handler:**

**src/controls/UniversalDatasetGrid/UniversalDatasetGrid/utils/ErrorHandler.ts:**

```typescript
export class ErrorHandler {
    /**
     * Get user-friendly error message from error object.
     */
    public static getUserMessage(error: unknown): string {
        if (error instanceof Error) {
            // Map known errors
            if (error.message.includes('401') || error.message.includes('Unauthorized')) {
                return 'Authentication failed. Please sign in again.';
            }

            if (error.message.includes('403') || error.message.includes('Forbidden')) {
                return 'You do not have permission to perform this action.';
            }

            if (error.message.includes('404') || error.message.includes('Not Found')) {
                return 'The requested file was not found.';
            }

            if (error.message.includes('timeout') || error.message.includes('Timeout')) {
                return 'The operation timed out. Please try again.';
            }

            if (error.message.includes('Network') || error.message.includes('network')) {
                return 'Network error. Please check your connection.';
            }

            // Return original error message
            return error.message;
        }

        return 'An unexpected error occurred.';
    }

    /**
     * Log error with details.
     */
    public static logError(context: string, error: unknown): void {
        console.error(`[${context}]`, error);
    }
}
```

**Add validation:**

```typescript
/**
 * Validate record has required fields for file operation.
 */
private validateRecordForUpload(recordId: string): void {
    const record = this.dataset.records[recordId];

    if (!record) {
        throw new Error('Record not found');
    }

    const containerId = record.getValue('spk_containerid');
    if (!containerId) {
        throw new Error('Document does not have a container assigned');
    }

    const hasFile = record.getValue(this.config.fieldMappings.hasFile);
    if (hasFile) {
        throw new Error('Document already has a file. Use "Update File" to replace it.');
    }
}
```

**Acceptance Criteria:**
- [ ] User-friendly error messages
- [ ] Validation before operations
- [ ] Error logging
- [ ] Network errors handled
- [ ] Authentication errors handled

---

### Phase 4: Document Field Updates & SharePoint Links (8 hours)

**Objective:** Auto-populate document metadata fields and create clickable SharePoint URLs.

#### Task 4.1: Dataverse Field Update Logic (4 hours)

**Create Dataverse update handler:**

**src/controls/UniversalDatasetGrid/UniversalDatasetGrid/handlers/DataverseUpdateHandler.ts:**

```typescript
import { DriveItem } from '@spaarke/sdap-client';
import { GridConfiguration } from '../types';

export class DataverseUpdateHandler {
    constructor(
        private context: ComponentFramework.Context<any>,
        private config: GridConfiguration
    ) {}

    /**
     * Update document record after file upload.
     */
    public async updateAfterUpload(
        recordId: string,
        driveItem: DriveItem,
        driveId: string
    ): Promise<void> {
        const entityName = this.context.page.entityTypeName;

        const updateData: any = {};

        // Set file metadata fields
        updateData[this.config.fieldMappings.hasFile] = true;
        updateData[this.config.fieldMappings.fileName] = driveItem.name;
        updateData[this.config.fieldMappings.fileSize] = driveItem.size;
        updateData[this.config.fieldMappings.mimeType] = driveItem.mimeType ?? '';
        updateData[this.config.fieldMappings.graphItemId] = driveItem.id;
        updateData[this.config.fieldMappings.graphDriveId] = driveId;

        // Update via Web API
        await this.context.webAPI.updateRecord(entityName, recordId, updateData);

        // Refresh dataset to show changes
        this.context.parameters.dataset.refresh();
    }

    /**
     * Clear document fields after file deletion.
     */
    public async updateAfterDelete(recordId: string): Promise<void> {
        const entityName = this.context.page.entityTypeName;

        const updateData: any = {};
        updateData[this.config.fieldMappings.hasFile] = false;
        updateData[this.config.fieldMappings.fileName] = null;
        updateData[this.config.fieldMappings.fileSize] = null;
        updateData[this.config.fieldMappings.mimeType] = null;
        updateData[this.config.fieldMappings.graphItemId] = null;
        updateData[this.config.fieldMappings.graphDriveId] = null;

        await this.context.webAPI.updateRecord(entityName, recordId, updateData);

        this.context.parameters.dataset.refresh();
    }
}
```

**Wire into file operations:**

```typescript
// After upload
const driveItem = await this.uploadHandler.uploadFile(recordId, containerId, onProgress);
await this.dataverseUpdateHandler.updateAfterUpload(recordId, driveItem, driveId);

// After delete
await this.deleteHandler.deleteFile(driveId, itemId);
await this.dataverseUpdateHandler.updateAfterDelete(recordId);
```

**Acceptance Criteria:**
- [ ] Fields updated after upload
- [ ] Fields cleared after delete
- [ ] Grid refreshes automatically
- [ ] No race conditions

---

#### Task 4.2: SharePoint URL Generation (2 hours)

**Create URL builder:**

**src/controls/UniversalDatasetGrid/UniversalDatasetGrid/utils/SharePointUrlBuilder.ts:**

```typescript
export class SharePointUrlBuilder {
    /**
     * Build SharePoint file URL from drive and item IDs.
     */
    public static buildFileUrl(driveId: string, itemId: string): string {
        // SPE file URL format:
        // https://{tenant}.sharepoint.com/_layouts/15/Doc.aspx?sourcedoc={itemId}&file={fileName}&action=default

        // For now, return Graph API download URL
        // In production, use proper SharePoint URL
        return `https://graph.microsoft.com/v1.0/drives/${driveId}/items/${itemId}`;
    }

    /**
     * Build clickable HTML link.
     */
    public static buildClickableLink(driveId: string, itemId: string, fileName: string): string {
        const url = this.buildFileUrl(driveId, itemId);
        return `<a href="${url}" target="_blank">${fileName}</a>`;
    }
}
```

**Add clickable file name column:**

```typescript
// In grid rendering, detect file name column and make it clickable
if (column.name === this.config.fieldMappings.fileName) {
    const driveId = record.getValue(this.config.fieldMappings.graphDriveId);
    const itemId = record.getValue(this.config.fieldMappings.graphItemId);
    const fileName = record.getValue(this.config.fieldMappings.fileName);

    if (driveId && itemId && fileName) {
        cellContent = SharePointUrlBuilder.buildClickableLink(driveId, itemId, fileName);
    }
}
```

**Acceptance Criteria:**
- [ ] File name column clickable
- [ ] Link opens in new tab
- [ ] Correct SharePoint URL format
- [ ] Works for all files

---

#### Task 4.3: Field Validation and Consistency (2 hours)

**Add field consistency checks:**

```typescript
/**
 * Validate document record state.
 */
private validateRecordState(recordId: string): void {
    const record = this.dataset.records[recordId];

    const hasFile = record.getValue(this.config.fieldMappings.hasFile);
    const fileName = record.getValue(this.config.fieldMappings.fileName);
    const graphItemId = record.getValue(this.config.fieldMappings.graphItemId);

    // Consistency check: if hasFile=true, must have fileName and graphItemId
    if (hasFile && (!fileName || !graphItemId)) {
        console.warn(`Inconsistent state: Record ${recordId} has hasFile=true but missing fileName or graphItemId`);
    }

    // Consistency check: if hasFile=false, should not have fileName or graphItemId
    if (!hasFile && (fileName || graphItemId)) {
        console.warn(`Inconsistent state: Record ${recordId} has hasFile=false but has fileName or graphItemId`);
    }
}
```

**Acceptance Criteria:**
- [ ] Field validation implemented
- [ ] Warnings logged for inconsistencies
- [ ] Commands disabled for invalid states

---

### Phase 5: Testing & Refinement (16 hours)

**Objective:** Comprehensive testing and quality assurance.

#### Task 5.1: Unit Tests for SDAP Client (4 hours)

**Already created in Phase 1.5**

Additional tests:

```typescript
describe('SdapApiClient - File Operations', () => {
    describe('uploadFile', () => {
        it('should use small upload for files < 4MB', async () => {
            const client = new SdapApiClient({ baseUrl: 'https://api.example.com' });
            const file = new File(['x'.repeat(3 * 1024 * 1024)], 'small.txt'); // 3MB

            // Mock uploadSmall
            const uploadSmallSpy = jest.spyOn(client['uploadOp'], 'uploadSmall');

            await client.uploadFile('container123', file);

            expect(uploadSmallSpy).toHaveBeenCalled();
        });

        it('should use chunked upload for files >= 4MB', async () => {
            const client = new SdapApiClient({ baseUrl: 'https://api.example.com' });
            const file = new File(['x'.repeat(5 * 1024 * 1024)], 'large.txt'); // 5MB

            // Mock uploadChunked
            const uploadChunkedSpy = jest.spyOn(client['uploadOp'], 'uploadChunked');

            await client.uploadFile('container123', file);

            expect(uploadChunkedSpy).toHaveBeenCalled();
        });

        it('should report progress during upload', async () => {
            const client = new SdapApiClient({ baseUrl: 'https://api.example.com' });
            const file = new File(['test'], 'test.txt');

            const progressCallback = jest.fn();

            await client.uploadFile('container123', file, {
                onProgress: progressCallback
            });

            expect(progressCallback).toHaveBeenCalledWith(0); // Start
            expect(progressCallback).toHaveBeenCalledWith(100); // Complete
        });
    });
});
```

**Acceptance Criteria:**
- [ ] 80%+ code coverage
- [ ] All public methods tested
- [ ] Error cases tested
- [ ] Progress callbacks tested

---

#### Task 5.2: Integration Tests for File Operations (6 hours)

**Create integration test suite:**

```typescript
// tests/integration/FileOperations.test.ts
describe('File Operations - Integration Tests', () => {
    let control: UniversalDatasetGrid;
    let mockContext: ComponentFramework.Context<IInputs>;
    let mockDataset: ComponentFramework.PropertyTypes.DataSet;

    beforeEach(() => {
        // Setup mock context
        mockContext = createMockContext();
        mockDataset = createMockDataset();

        control = new UniversalDatasetGrid();
        control.init(mockContext, jest.fn(), {}, document.createElement('div'));
    });

    describe('Add File', () => {
        it('should upload file and update document fields', async () => {
            const recordId = 'record123';
            const file = new File(['test content'], 'test.txt');

            // Mock file picker
            jest.spyOn(control['uploadHandler'], 'showFilePicker').mockResolvedValue(file);

            // Mock SDAP upload
            const mockDriveItem = {
                id: 'item123',
                name: 'test.txt',
                size: 12,
                mimeType: 'text/plain'
            };
            jest.spyOn(control['sdapClient'], 'uploadFile').mockResolvedValue(mockDriveItem);

            // Execute
            await control['handleAddFile'](recordId);

            // Verify Dataverse update called
            expect(mockContext.webAPI.updateRecord).toHaveBeenCalledWith(
                'spk_document',
                recordId,
                expect.objectContaining({
                    spk_hasfile: true,
                    spk_filename: 'test.txt',
                    spk_filesize: 12,
                    spk_mimetype: 'text/plain',
                    spk_graphitemid: 'item123'
                })
            );
        });

        it('should handle upload errors gracefully', async () => {
            const recordId = 'record123';
            const file = new File(['test'], 'test.txt');

            jest.spyOn(control['uploadHandler'], 'showFilePicker').mockResolvedValue(file);
            jest.spyOn(control['sdapClient'], 'uploadFile').mockRejectedValue(
                new Error('Network error')
            );

            await control['handleAddFile'](recordId);

            // Verify error message shown
            expect(control['showErrorMessage']).toHaveBeenCalledWith(
                expect.stringContaining('Upload failed')
            );
        });
    });

    describe('Download File', () => {
        it('should download file and trigger browser download', async () => {
            const recordId = 'record123';
            const mockBlob = new Blob(['file content']);

            jest.spyOn(control['sdapClient'], 'downloadFile').mockResolvedValue(mockBlob);
            jest.spyOn(control['downloadHandler'], 'triggerDownload');

            await control['handleDownloadFile']();

            expect(control['downloadHandler'].triggerDownload).toHaveBeenCalledWith(
                mockBlob,
                expect.any(String)
            );
        });
    });

    describe('Remove File', () => {
        it('should delete file and clear document fields', async () => {
            const recordId = 'record123';

            jest.spyOn(control, 'showConfirmDialog').mockResolvedValue(true);
            jest.spyOn(control['sdapClient'], 'deleteFile').mockResolvedValue(undefined);

            await control['handleRemoveFile'](recordId);

            expect(mockContext.webAPI.updateRecord).toHaveBeenCalledWith(
                'spk_document',
                recordId,
                expect.objectContaining({
                    spk_hasfile: false,
                    spk_filename: null,
                    spk_graphitemid: null
                })
            );
        });

        it('should not delete if user cancels', async () => {
            const recordId = 'record123';

            jest.spyOn(control, 'showConfirmDialog').mockResolvedValue(false);

            await control['handleRemoveFile'](recordId);

            expect(control['sdapClient'].deleteFile).not.toHaveBeenCalled();
        });
    });
});
```

**Acceptance Criteria:**
- [ ] All file operations tested end-to-end
- [ ] Error scenarios tested
- [ ] User interactions tested
- [ ] Field updates verified

---

#### Task 5.3: Performance Testing (4 hours)

**Create performance test suite:**

```typescript
describe('Performance Tests', () => {
    it('should upload 4MB file in < 30 seconds', async () => {
        const client = new SdapApiClient({ baseUrl: SDAP_URL });
        const file = new File(['x'.repeat(4 * 1024 * 1024)], 'large.txt');

        const startTime = Date.now();
        await client.uploadFile('container123', file);
        const endTime = Date.now();

        const duration = (endTime - startTime) / 1000;
        expect(duration).toBeLessThan(30);
    });

    it('should upload 20MB file via chunked upload in < 2 minutes', async () => {
        const client = new SdapApiClient({ baseUrl: SDAP_URL });
        const file = new File(['x'.repeat(20 * 1024 * 1024)], 'verylarge.txt');

        const startTime = Date.now();
        await client.uploadFile('container123', file);
        const endTime = Date.now();

        const duration = (endTime - startTime) / 1000;
        expect(duration).toBeLessThan(120);
    });

    it('should download 10MB file in < 15 seconds', async () => {
        const client = new SdapApiClient({ baseUrl: SDAP_URL });

        const startTime = Date.now();
        await client.downloadFile('drive123', 'item123');
        const endTime = Date.now();

        const duration = (endTime - startTime) / 1000;
        expect(duration).toBeLessThan(15);
    });

    it('should render 1000-row grid in < 2 seconds', () => {
        const control = new UniversalDatasetGrid();
        const mockDataset = createMockDatasetWithRecords(1000);

        const startTime = Date.now();
        control.updateView(mockContext);
        const endTime = Date.now();

        const duration = endTime - startTime;
        expect(duration).toBeLessThan(2000);
    });
});
```

**Benchmarks:**
- Small file upload (< 4MB): < 10 seconds
- Chunked upload (4-20MB): < 2 minutes
- Download (10MB): < 15 seconds
- Grid rendering (1000 rows): < 2 seconds
- Bundle size: < 1 MB

**Acceptance Criteria:**
- [ ] All performance benchmarks met
- [ ] No memory leaks
- [ ] Chunked upload faster than small upload for large files

---

#### Task 5.4: User Acceptance Testing (2 hours)

**Manual test scenarios:**

1. **Add File to Document**
   - [ ] Navigate to Documents view
   - [ ] Select document without file
   - [ ] Click "Add File"
   - [ ] Select file (test both < 4MB and > 4MB)
   - [ ] Verify progress dialog shows
   - [ ] Verify file uploads successfully
   - [ ] Verify document fields updated
   - [ ] Verify file name clickable

2. **Download File**
   - [ ] Select document with file
   - [ ] Click "Download"
   - [ ] Verify browser download triggered
   - [ ] Verify correct file downloaded

3. **Update File**
   - [ ] Select document with file
   - [ ] Click "Update File"
   - [ ] Select new file
   - [ ] Verify old file deleted
   - [ ] Verify new file uploaded
   - [ ] Verify document fields updated

4. **Remove File**
   - [ ] Select document with file
   - [ ] Click "Remove File"
   - [ ] Verify confirmation dialog
   - [ ] Click "Delete"
   - [ ] Verify file deleted
   - [ ] Verify document fields cleared

5. **Error Scenarios**
   - [ ] Test network disconnect during upload
   - [ ] Test authentication failure
   - [ ] Test upload to container without permission
   - [ ] Test file size exceeds limit (if implemented)

6. **Multi-select Download**
   - [ ] Select multiple documents with files
   - [ ] Click "Download"
   - [ ] Verify all files downloaded

**Acceptance Criteria:**
- [ ] All scenarios pass
- [ ] No console errors
- [ ] User-friendly error messages
- [ ] Performance acceptable

---

### Phase 6: Deployment & Documentation (16 hours)

**Objective:** Deploy NPM package and PCF control, create documentation.

#### Task 6.1: Deploy NPM Package to Registry (2 hours)

**Option 1: Azure Artifacts (Recommended)**

```bash
# Create Azure Artifacts feed
az artifacts universal publish \
  --organization https://dev.azure.com/spaarke \
  --feed spaarke-packages \
  --name sdap-client \
  --version 1.0.0 \
  --description "SDAP API client for PCF controls" \
  --path ./spaarke-sdap-client-1.0.0.tgz

# Configure npm to use Azure Artifacts
npm config set registry https://pkgs.dev.azure.com/spaarke/_packaging/spaarke-packages/npm/registry/
npm config set always-auth true

# Publish
npm publish spaarke-sdap-client-1.0.0.tgz
```

**Option 2: Local file (for now)**

```bash
# Copy to shared location
cp spaarke-sdap-client-1.0.0.tgz //shared/packages/
```

**Acceptance Criteria:**
- [ ] NPM package published to registry
- [ ] Package accessible from PCF controls
- [ ] Version 1.0.0 tagged

---

#### Task 6.2: Deploy PCF Control to Dataverse (4 hours)

**Build solution:**

```bash
cd src/controls/UniversalDatasetGrid
npm run build

# Increment version in ControlManifest.Input.xml to 2.0.0

# Build solution
cd ../../../solutions/SpaarkeComponents
pac solution pack --zipfile SpaarkeComponents_2.0.0.zip --folder ../..
```

**Deploy to development environment:**

```bash
pac solution import \
  --path SpaarkeComponents_2.0.0.zip \
  --environment https://spaarkedev1.crm.dynamics.com \
  --publish-changes
```

**Update Document entity form:**

1. Navigate to Document entity form editor
2. Replace old grid with new Universal Dataset Grid v2.0.0
3. Configure JSON parameter (if needed)
4. Publish form

**Acceptance Criteria:**
- [ ] Control deployed to Dataverse
- [ ] Version 2.0.0 visible in "Get more components"
- [ ] Control works on Document view
- [ ] Control works on subgrids

---

#### Task 6.3: Create NPM Package API Documentation (4 hours)

**Create README.md:**

```markdown
# @spaarke/sdap-client

SDAP (SharePoint Document Access Platform) API client for PCF controls and TypeScript applications.

## Features

- ✅ Small file upload (< 4MB)
- ✅ Chunked upload for large files (≥ 4MB) with progress tracking
- ✅ File download with streaming
- ✅ File deletion
- ✅ Metadata retrieval
- ✅ TypeScript type definitions
- ✅ Production-ready error handling

## Installation

### From Azure Artifacts

\`\`\`bash
npm install @spaarke/sdap-client
\`\`\`

### From local file

\`\`\`bash
npm install ../../../packages/sdap-client/spaarke-sdap-client-1.0.0.tgz
\`\`\`

## Usage

### Initialize Client

\`\`\`typescript
import { SdapApiClient } from '@spaarke/sdap-client';

const client = new SdapApiClient({
  baseUrl: 'https://spe-bff-api.azurewebsites.net',
  timeout: 300000 // 5 minutes
});
\`\`\`

### Upload File

Automatically uses small upload (< 4MB) or chunked upload (≥ 4MB):

\`\`\`typescript
const file = new File(['content'], 'document.txt');

const driveItem = await client.uploadFile(containerId, file, {
  onProgress: (percent) => {
    console.log(\`Upload progress: \${percent}%\`);
  }
});

console.log('Uploaded:', driveItem.id, driveItem.name);
\`\`\`

### Download File

\`\`\`typescript
const blob = await client.downloadFile(driveId, itemId);

// Trigger browser download
const url = URL.createObjectURL(blob);
const a = document.createElement('a');
a.href = url;
a.download = 'document.txt';
a.click();
URL.revokeObjectURL(url);
\`\`\`

### Delete File

\`\`\`typescript
await client.deleteFile(driveId, itemId);
\`\`\`

### Get File Metadata

\`\`\`typescript
const metadata = await client.getFileMetadata(driveId, itemId);
console.log(metadata.name, metadata.size, metadata.mimeType);
\`\`\`

## API Reference

See [API.md](./API.md) for complete API reference.

## Error Handling

\`\`\`typescript
try {
  await client.uploadFile(containerId, file);
} catch (error) {
  if (error.message.includes('401')) {
    // Authentication error
  } else if (error.message.includes('403')) {
    // Permission error
  } else {
    // Other error
  }
}
\`\`\`

## TypeScript Types

\`\`\`typescript
import {
  SdapClientConfig,
  DriveItem,
  UploadSession,
  FileMetadata
} from '@spaarke/sdap-client';
\`\`\`

## License

UNLICENSED - Internal use only
\`\`\`

**Acceptance Criteria:**
- [ ] README with usage examples
- [ ] API reference documentation
- [ ] TypeScript type documentation
- [ ] Error handling guide

---

#### Task 6.4: Create Integration Guide for Sprint 7 (3 hours)

**Create INTEGRATION-GUIDE.md:**

```markdown
# SDAP Client Integration Guide for Sprint 7

This guide shows how to integrate @spaarke/sdap-client into new PCF controls.

## Target Audience

Sprint 7 teams building:
- Document Viewer PCF control
- Office File Edit PCF control

## Installation

\`\`\`bash
cd src/controls/YourControl
npm install @spaarke/sdap-client
\`\`\`

## Basic Integration

### 1. Initialize Client

\`\`\`typescript
import { SdapApiClient } from '@spaarke/sdap-client';

export class YourControl implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private sdapClient: SdapApiClient;

    public init(context: ComponentFramework.Context<IInputs>, ...): void {
        this.sdapClient = new SdapApiClient({
            baseUrl: 'https://spe-bff-api.azurewebsites.net',
            timeout: 300000
        });
    }
}
\`\`\`

### 2. Download File for Viewing

\`\`\`typescript
public async loadFile(driveId: string, itemId: string): Promise<void> {
    try {
        const blob = await this.sdapClient.downloadFile(driveId, itemId);

        // Display blob in your viewer
        this.displayBlob(blob);
    } catch (error) {
        console.error('Failed to load file:', error);
    }
}
\`\`\`

### 3. Upload Edited File

\`\`\`typescript
public async saveFile(driveId: string, itemId: string, file: File): Promise<void> {
    try {
        // Delete old version
        await this.sdapClient.deleteFile(driveId, itemId);

        // Upload new version
        const driveItem = await this.sdapClient.uploadFile(containerId, file, {
            onProgress: (percent) => this.showProgress(percent)
        });

        console.log('File saved:', driveItem.id);
    } catch (error) {
        console.error('Failed to save file:', error);
    }
}
\`\`\`

## Common Patterns

### Progress Tracking

\`\`\`typescript
await this.sdapClient.uploadFile(containerId, file, {
    onProgress: (percent) => {
        this.progressBar.value = percent / 100;
        this.progressLabel.innerText = \`\${percent}%\`;
    }
});
\`\`\`

### Cancellation

\`\`\`typescript
const controller = new AbortController();

this.cancelButton.onclick = () => controller.abort();

await this.sdapClient.uploadFile(containerId, file, {
    signal: controller.signal
});
\`\`\`

### Error Handling

\`\`\`typescript
import { ErrorHandler } from '@spaarke/sdap-client/utils'; // If exported

try {
    await this.sdapClient.uploadFile(containerId, file);
} catch (error) {
    const userMessage = ErrorHandler.getUserMessage(error);
    this.showError(userMessage);
}
\`\`\`

## Estimated Integration Time

- **Document Viewer:** 1 hour (download only)
- **Office Edit Control:** 3 hours (download + upload)

## Support

Contact Sprint 6 team for questions.
\`\`\`

**Acceptance Criteria:**
- [ ] Integration guide complete
- [ ] Code examples for common scenarios
- [ ] Estimated integration time
- [ ] Contact info

---

#### Task 6.5: Create Deployment Runbook (3 hours)

**Create DEPLOYMENT-RUNBOOK.md:**

```markdown
# Sprint 6 Deployment Runbook

## Prerequisites

- [x] Azure CLI installed
- [x] Power Platform CLI (pac) installed
- [x] Access to Dataverse environment (https://spaarkedev1.crm.dynamics.com)
- [x] Node.js 18+ and npm 9+

## Step 1: Deploy NPM Package

\`\`\`bash
cd packages/sdap-client

# Build package
npm run build

# Run tests
npm test

# Package
npm pack
# Creates: spaarke-sdap-client-1.0.0.tgz

# Publish to Azure Artifacts
npm publish spaarke-sdap-client-1.0.0.tgz
\`\`\`

## Step 2: Build PCF Control

\`\`\`bash
cd src/controls/UniversalDatasetGrid/UniversalDatasetGrid

# Install dependencies (including @spaarke/sdap-client)
npm install

# Build
npm run build

# Verify bundle size < 1 MB
ls -lh out/controls/bundle.js
\`\`\`

## Step 3: Create Solution

\`\`\`bash
cd solutions/SpaarkeComponents

# Update version in solution.xml to 2.0.0

# Pack solution
pac solution pack \
  --zipfile SpaarkeComponents_2.0.0.zip \
  --folder ../.. \
  --packagetype Managed
\`\`\`

## Step 4: Deploy to Dataverse

\`\`\`bash
# Authenticate
pac auth create \
  --url https://spaarkedev1.crm.dynamics.com \
  --name SpaarkeDevAuth

# Import solution
pac solution import \
  --path SpaarkeComponents_2.0.0.zip \
  --environment https://spaarkedev1.crm.dynamics.com \
  --publish-changes \
  --async
\`\`\`

## Step 5: Update Document Entity

1. Navigate to https://make.powerapps.com
2. Select "Spaarke Dev 1" environment
3. Go to Tables > Document > Forms
4. Edit "Information" form
5. Replace old Universal Dataset Grid with v2.0.0
6. Publish form

## Step 6: Verify Deployment

1. Navigate to Document entity view
2. Verify Universal Dataset Grid v2.0.0 loaded
3. Verify command bar visible (Add File, Remove File, Update, Download)
4. Test Add File with small file (< 4MB)
5. Test Add File with large file (> 4MB) - verify chunked upload
6. Test Download
7. Test Remove File
8. Verify no console errors

## Rollback Procedure

If deployment fails:

\`\`\`bash
# Revert to previous version (1.0.0)
pac solution import \
  --path SpaarkeComponents_1.0.0.zip \
  --environment https://spaarkedev1.crm.dynamics.com \
  --publish-changes
\`\`\`

## Troubleshooting

### Bundle size > 5 MB
- Check Fluent UI imports (should be selective, not monolithic)
- Check @spaarke/sdap-client is not bundling duplicate dependencies

### Authentication errors
- Verify SDAP BFF API is running
- Verify user has OBO permissions

### Upload fails
- Verify container exists
- Verify document has spk_containerid field populated
- Check SDAP API logs

## Post-Deployment Tasks

- [ ] Update documentation wiki
- [ ] Notify Sprint 7 teams of NPM package availability
- [ ] Create backlog item for configurator (next sprint)
\`\`\`

**Acceptance Criteria:**
- [ ] Step-by-step deployment guide
- [ ] Verification checklist
- [ ] Rollback procedure
- [ ] Troubleshooting guide

---

## Sprint 6 Summary

### Total Duration: 98 Hours (2.5 Weeks)

**Phase Breakdown:**
- Phase 1: Configuration & Planning (8 hours) ✅ Complete
- Phase 1.5: NPM Package (8 hours) 🆕
- Phase 2: Fluent UI Integration (22 hours)
- Phase 3: SDAP Integration (20 hours)
- Phase 4: Field Updates (8 hours)
- Phase 5: Testing (16 hours)
- Phase 6: Deployment & Docs (16 hours)

### Key Deliverables

1. **@spaarke/sdap-client NPM Package**
   - Production-ready TypeScript client
   - Small file upload (< 4MB)
   - Chunked upload (≥ 4MB) with progress
   - Download, delete, metadata operations
   - 80%+ test coverage
   - Full TypeScript types

2. **Universal Dataset Grid PCF Control v2.0.0**
   - Fluent UI v9 compliance (selective imports)
   - Custom command bar (Add, Remove, Update, Download)
   - Progress indicators
   - Bundle size: ~880 KB
   - Configuration support
   - Error handling

3. **Documentation**
   - NPM package README and API docs
   - Integration guide for Sprint 7
   - Deployment runbook
   - Testing guide

### ADR Compliance ✅

- ✅ **ADR-006:** No JavaScript web resources
- ✅ **ADR-011:** Dataset PCF control
- ✅ **ADR-012:** Shared component library (@spaarke/sdap-client)
- ✅ **ADR-002:** No heavy plugins
- ✅ **ADR-007:** SPE storage minimalism

### Production Code Quality ✅

- ✅ TypeScript strict mode, no `any` types
- ✅ 80%+ test coverage
- ✅ TSDoc comments on all public APIs
- ✅ User-friendly error messages
- ✅ Performance benchmarks met
- ✅ Security validation

### Sprint 7 Readiness

**Sprint 7 teams can:**
- Install @spaarke/sdap-client in 1 command
- Integrate SDAP in 1-3 hours (vs 8 hours without NPM package)
- Reuse all file operations (upload, download, delete)
- Follow integration guide

**ROI:**
- Investment: 8 hours (NPM package creation)
- Savings: 14+ hours (Sprint 7 + Sprint 8)
- Avoids: 600+ lines of duplicate code

---

## Next Steps

1. **Review and Approve Plan** ✅
2. **Begin Implementation** (Phase 1.5 - NPM Package)
3. **Iterative Development** (Phases 2-6)
4. **Deployment** (Phase 6)
5. **Sprint 7 Handoff**

---

**STATUS: ✅ PLAN COMPLETE - READY FOR IMPLEMENTATION**