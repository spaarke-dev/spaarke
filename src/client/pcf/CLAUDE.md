# CLAUDE.md - PCF Controls Module

> **Last Updated**: January 10, 2026
>
> **Purpose**: Module-specific instructions for Power Platform PCF (PowerApps Component Framework) controls.

## Module Overview

This module contains TypeScript/React PCF controls for Dataverse model-driven apps:
- **UniversalQuickCreate** - Document upload with quick create form
- **UniversalDatasetGrid** - Custom dataset grid with actions
- **SpeFileViewer** - SharePoint Embedded file viewer
- **DocumentGrid** / **DocumentViewer** - Document management components
- **PlaybookBuilderHost** - Node-based visual workflow builder (see special architecture below)

## Key Structure

```
src/client/pcf/
├── package.json            # Root NPM package (workspace)
├── controls.pcfproj        # PCF project definition
├── UniversalQuickCreate/   # Each control in its own folder
│   ├── ControlManifest.Input.xml
│   ├── index.ts            # PCF entry point
│   ├── App.tsx             # React root component
│   ├── components/         # React components
│   ├── services/           # API clients, auth providers
│   ├── types/              # TypeScript interfaces
│   └── utils/              # Utility functions
└── shared/                 # Shared code across controls
    ├── components/
    └── utils/
```

## Architecture Constraints

### From ADR-006: PCF Over Webresources
```typescript
// ✅ CORRECT: Build as PCF control
export class UniversalQuickCreate implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    public init(context: ComponentFramework.Context<IInputs>): void { }
    public updateView(context: ComponentFramework.Context<IInputs>): void { }
    public destroy(): void { }
}

// ❌ WRONG: Don't create new JS webresources for custom UI
```

### From ADR-012: Shared Component Library
```typescript
// ✅ CORRECT: Import from shared library (when available)
import { DataGrid, StatusBadge, formatters } from "@spaarke/ui-components";

// ✅ CORRECT: Use Fluent UI v9 exclusively
import { Button, Input, Spinner } from "@fluentui/react-components";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";

// ❌ WRONG: Don't mix Fluent UI versions
import { DefaultButton } from "@fluentui/react";  // v8 - DON'T USE
```

## Component Patterns

### PCF Entry Point (index.ts)
```typescript
export class MyControl implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private container: HTMLDivElement;
    private context: ComponentFramework.Context<IInputs>;

    public init(
        context: ComponentFramework.Context<IInputs>,
        notifyOutputChanged: () => void,
        state: ComponentFramework.Dictionary,
        container: HTMLDivElement
    ): void {
        this.container = container;
        this.context = context;
    }

    public updateView(context: ComponentFramework.Context<IInputs>): void {
        this.context = context;
        this.render();
    }

    private render(): void {
        ReactDOM.render(
            React.createElement(FluentProvider, { theme: webLightTheme },
                React.createElement(App, {
                    context: this.context,
                    // Pass props from PCF inputs
                })
            ),
            this.container
        );
    }

    public destroy(): void {
        ReactDOM.unmountComponentAtNode(this.container);
    }
}
```

### React Component Pattern
```typescript
// App.tsx
interface IAppProps {
    context: ComponentFramework.Context<IInputs>;
}

export const App: React.FC<IAppProps> = ({ context }) => {
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);

    // Use hooks for data fetching
    const { data, refetch } = useDocuments(context);

    if (loading) return <Spinner label="Loading..." />;
    if (error) return <MessageBar intent="error">{error}</MessageBar>;

    return (
        <div className={styles.container}>
            <CommandBar actions={actions} />
            <DataGrid items={data} />
        </div>
    );
};
```

### Authentication Pattern (Use `@spaarke/auth`)

**Never instantiate `PublicClientApplication` directly in a PCF.** All BFF API token acquisition routes through `@spaarke/auth` so every Spaarke surface shares one auth provider and the SSO binding requirements (`localStorage`, cookie state, tenant-specific authority) are guaranteed.

Bootstrap in the React host component's `useEffect` (NOT the PCF class `init()` — see the "Async Init in ReactControl" rule below):

```typescript
// authInit.ts — adapt the example from SemanticSearchControl
import { initAuth, type IAuthConfig } from '@spaarke/auth';

export async function initializeAuth(
    clientAppId: string,
    bffAppId: string,
    bffApiUrl: string
): Promise<void> {
    const config: IAuthConfig = {
        clientId: clientAppId,
        // authority intentionally omitted — @spaarke/auth resolves tenant-specific via resolveTenantFromXrm()
        redirectUri: resolveClientUrlFromXrm(),
        bffApiScope: `api://${bffAppId}/SDAP.Access`,
        bffBaseUrl: bffApiUrl,
        proactiveRefresh: true,
    };
    await initAuth(config);
}

// Then in the React host component:
const [authReady, setAuthReady] = useState(false);
useEffect(() => {
    initializeAuth(clientAppId, bffAppId, bffApiUrl).then(() => setAuthReady(true));
}, []);

// To call the BFF, use authenticatedFetch (handles token + retry):
import { authenticatedFetch } from '@spaarke/auth';
const response = await authenticatedFetch('/ai/search/...'); // relative path; resolver adds /api/
```

**Why this matters**: `@spaarke/auth` v2 wraps a single `PublicClientApplication` (`BrowserMsalStrategy`) with MSAL's `localStorage` cache (INV-1) for cross-tab/iframe SSO, plus an `InMemoryCache` layer that validates JWT `exp` (5-min buffer) before returning cached tokens. Direct `new PublicClientApplication(...)` in a consumer = isolated MSAL cache = silent SSO failure (INV-7 violation = popup on every tab open).

**Canonical reference**: [`.claude/patterns/auth/spaarke-sso-binding.md`](../../../.claude/patterns/auth/spaarke-sso-binding.md) (INV-1..INV-8) + [`.claude/adr/ADR-028-spaarke-auth-architecture.md`](../../../.claude/adr/ADR-028-spaarke-auth-architecture.md) (function-based contract) + [`docs/guides/auth-deployment-setup.md`](../../../docs/guides/auth-deployment-setup.md) (env-var setup).

**⛔ ANTI-PATTERN — Never do these (ADR-028 violations):**
- Pass `accessToken: string` or `getAccessToken: () => Promise<string>` as a typed prop/constructor arg
- Reference `window.__SPAARKE_BFF_TOKEN__`, `tokenBridge`, `BridgeStrategy`, `XrmStrategy`, `MsalSilentStrategy` (all retired in Phase A)
- Write `fetch(url, { headers: { Authorization: \`Bearer ${token}\` }})` — use `authenticatedFetch` instead
- Instantiate `PublicClientApplication` outside `@spaarke/auth`

### API Client Pattern (Auth v2 — ADR-028)

```typescript
// services/SdapApiClient.ts — accept authenticatedFetch (NOT a token-returning function)
import { authenticatedFetch } from '@spaarke/auth';

export class SdapApiClient {
    constructor(
        private baseUrl: string,
        private timeout: number = 300000
    ) {}

    async uploadFile(request: FileUploadRequest): Promise<SpeFileMetadata> {
        // authenticatedFetch handles bearer header acquisition + 401 retry automatically
        const response = await authenticatedFetch(
            `/obo/containers/${request.containerId}/files`,
            { method: 'PUT', body: request.file }
        );

        if (!response.ok) {
            throw new Error(await this.getErrorMessage(response));
        }

        return response.json();
    }
}
```

> Do NOT pass an `accessToken` prop or a `getAccessToken` callback into your API client — `authenticatedFetch` handles all token acquisition, refresh, and 401-retry. This is the v2 function-based contract (ADR-028).

## TypeScript Guidelines

### Type Everything
```typescript
// ✅ CORRECT: Explicit types
interface IDocumentItem {
    id: string;
    name: string;
    size: number;
    createdOn: Date;
}

const documents: IDocumentItem[] = [];

// ❌ WRONG: Any types
const documents: any[] = [];
```

### Use Type Guards
```typescript
// ✅ CORRECT: Type guard for runtime safety
function isDocumentItem(obj: unknown): obj is IDocumentItem {
    return typeof obj === 'object' && obj !== null && 'id' in obj && 'name' in obj;
}
```

## Error Handling

```typescript
// User-friendly error messages
try {
    await apiClient.uploadFile(file);
} catch (error) {
    if (error instanceof AuthenticationError) {
        setError("Session expired. Please refresh the page.");
    } else if (error instanceof NetworkError) {
        setError("Unable to connect. Please check your connection.");
    } else {
        setError("An unexpected error occurred. Please try again.");
        logger.error('Upload failed', error);
    }
}
```

## Build Commands

```bash
# Install dependencies
npm install

# Build controls
npm run build

# Start dev server (test harness)
npm run start

# Build for production
npm run build -- --mode production

# Push to Dataverse (requires pac auth)
pac pcf push --publisher-prefix sprk
```

## Testing Guidelines

```typescript
// Component test pattern
describe('DocumentGrid', () => {
    it('renders documents', () => {
        const documents = [{ id: '1', name: 'Test.pdf' }];
        const { getByText } = render(<DocumentGrid items={documents} />);
        expect(getByText('Test.pdf')).toBeInTheDocument();
    });

    it('handles empty state', () => {
        const { getByText } = render(<DocumentGrid items={[]} />);
        expect(getByText('No documents found')).toBeInTheDocument();
    });
});
```

## ControlManifest Configuration

```xml
<?xml version="1.0" encoding="utf-8"?>
<manifest>
  <control namespace="Spaarke" constructor="UniversalQuickCreate" version="1.0.0" display-name-key="Universal Quick Create" description-key="Document upload with quick create">
    <property name="sampleProperty" display-name-key="Sample Property" description-key="Sample property description" of-type="SingleLine.Text" usage="bound" required="false" />
    <resources>
      <code path="index.ts" order="1"/>
      <platform-library name="React" version="18.2.0"/>
      <platform-library name="Fluent" version="9"/>
    </resources>
  </control>
</manifest>
```

## Do's and Don'ts

| ✅ DO | ❌ DON'T |
|-------|----------|
| Use Fluent UI v9 components | Mix Fluent v8 and v9 |
| Type all props and state | Use `any` type |
| Use shared component library | Duplicate components |
| Handle loading and error states | Show raw errors to users |
| Use logger utility for debugging | Use `console.log` in production |
| Clean up in `destroy()` method | Leave event listeners attached |
| Bootstrap via `initAuth()` from `@spaarke/auth` then consume via `useAuth()` / `authenticatedFetch` (ADR-028) | Instantiate `PublicClientApplication` directly, wire your own MSAL singleton, or pass `accessToken: string` props |
| **Include version footer in UI** | Deploy without visible version |

## Version Footer Requirement (MANDATORY)

**Every PCF control MUST display a version footer** in the UI footer area:

```typescript
// Footer pattern - REQUIRED for all PCF controls
<span className={styles.versionText}>
    v3.2.4 • Built 2025-12-09
</span>
```

### Why This Is Required

1. **Instant verification** - Users and devs can confirm which version is running without dev tools
2. **Deployment validation** - After deployment, hard refresh and check footer matches expected version
3. **Support debugging** - When issues are reported, know exactly which version is in use

### Version Update Checklist

When releasing a PCF update, update version in **4 locations**:

| Location | File | Example |
|----------|------|---------|
| 1. Source Manifest | `ControlManifest.Input.xml` | `version="3.2.4"` |
| 2. UI Footer | Component `.tsx` | `v3.2.4 • Built 2025-12-09` |
| 3. Solution Manifest | `solution.xml` or `Other/Solution.xml` | `<Version>3.2.4</Version>` |
| 4. Solution Control Manifest | `Controls/{...}/ControlManifest.xml` | `version="3.2.4"` |

### Deployment Workflow

For production releases, use full solution workflow (NOT `pac pcf push`):

```bash
# 1. Build
npm run build

# 2. Update versions in all 4 locations (manual)

# 3. Copy bundle to solution folder
cp out/controls/control/bundle.js \
   infrastructure/dataverse/ribbon/temp/{Solution}_extracted/Controls/{...}/

# 4. Pack and import
pac solution pack --zipfile Solution_vX.Y.Z.zip --folder {Solution}_extracted
pac solution import --path Solution_vX.Y.Z.zip --force-overwrite --publish-changes

# 5. CUSTOM PAGE REPUBLISH (if PCF is in Custom Page)
# CRITICAL: Open Custom Page in Power Apps Maker (make.powerapps.com)
#           → Apps → Open Custom Page → Edit
#           → File → Save → File → Publish
# Then run: pac solution publish-all

# 6. CLEAR BROWSER CACHE
# Users MUST hard refresh (Ctrl+Shift+R) the Spaarke application
# to clear cached PCF version

# 7. VERIFY deployment
pac solution list | grep -i "{SolutionName}"
# Check footer in browser shows new version
```

> **Full Guide**: See `docs/guides/PCF-DEPLOYMENT-GUIDE.md`

## PlaybookBuilderHost Architecture (Special Case)

The **PlaybookBuilderHost** control uses a unique architecture due to React Flow requirements:

### Architecture Overview (v2.0)

```
PlaybookBuilderHost/
├── control/
│   ├── index.ts                    # PCF entry point (React 16 APIs)
│   ├── ControlManifest.Input.xml   # v2.0.0
│   ├── PlaybookBuilderHost.tsx     # Main React component
│   ├── components/
│   │   ├── BuilderLayout.tsx       # Main layout with palette + canvas + properties
│   │   ├── Canvas/
│   │   │   └── Canvas.tsx          # React Flow canvas
│   │   ├── Nodes/                  # Custom node components
│   │   │   ├── AiAnalysisNode.tsx
│   │   │   ├── ConditionNode.tsx
│   │   │   └── ...
│   │   └── Properties/             # Node configuration panels
│   │       ├── PropertiesPanel.tsx
│   │       ├── NodePropertiesForm.tsx
│   │       └── ScopeSelector.tsx
│   └── stores/
│       ├── canvasStore.ts          # Zustand store for nodes/edges
│       └── scopeStore.ts           # Zustand store for skills/knowledge/tools
```

### Key Differences from Standard PCF Controls

| Aspect | Standard PCF | PlaybookBuilderHost |
|--------|-------------|---------------------|
| React Flow | N/A | `react-flow-renderer` v10.3.17 (bundled) |
| State Management | React hooks / context | Zustand stores |
| Canvas Library | N/A | React Flow with custom nodes |
| Complexity | Single component | Multi-component with drag-drop |

### Why react-flow-renderer v10?

Per ADR-022, PCF must use React 16 APIs. The modern `@xyflow/react` (v12+) requires React 18:

```typescript
// ❌ NOT COMPATIBLE: @xyflow/react v12+ requires React 18
import { ReactFlow } from '@xyflow/react';

// ✅ COMPATIBLE: react-flow-renderer v10 works with React 16
import ReactFlow from 'react-flow-renderer';
import 'react-flow-renderer/dist/style.css';
```

### Migration History

- **v1.x**: Used iframe + postMessage bridge to separate React 18 SPA
- **v2.0**: Direct PCF rendering with react-flow-renderer v10

The v2.0 architecture eliminates:
- iframe complexity
- postMessage communication
- Dual deployment (PCF + SPA)
- CSP configuration for iframe source

## Common Issues

### MSAL Not Initialized (Auth v2 — ADR-028)
```typescript
// ✅ CORRECT: Wait for v2 bootstrap (initAuth from @spaarke/auth)
import { initAuth, useAuth } from '@spaarke/auth';

const [authReady, setAuthReady] = useState(false);
useEffect(() => {
    initAuth({ clientId, tenantId, bffBaseUrl, bffApiScope })
        .then(() => setAuthReady(true));
}, []);

if (!authReady) return <Spinner label="Initializing auth..." />;

// Inside components — use the hook:
const { getAccessToken } = useAuth();
```

### Token Expired During Request (Auth v2 — ADR-028)
```typescript
// ✅ CORRECT: authenticatedFetch handles 401 retry automatically
import { authenticatedFetch } from '@spaarke/auth';

const response = await authenticatedFetch('/api/...', { method: 'POST', body });
// 401 → InMemoryCache invalidated → strategy.acquire() re-tries → retry the request
// No manual cache-clearing needed. Manual `authProvider.clearCache()` bypasses
// the v2 InMemoryCache exp-validation behavior.
```

---

*Refer to root `CLAUDE.md` for repository-wide standards.*
