# CLAUDE.md - PCF Controls Module

> **Last Updated**: December 3, 2025
>
> **Purpose**: Module-specific instructions for Power Platform PCF (PowerApps Component Framework) controls.

## Module Overview

This module contains TypeScript/React PCF controls for Dataverse model-driven apps:
- **UniversalQuickCreate** - Document upload with quick create form
- **UniversalDatasetGrid** - Custom dataset grid with actions
- **SpeFileViewer** - SharePoint Embedded file viewer
- **DocumentGrid** / **DocumentViewer** - Document management components

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

### MSAL Authentication Pattern
```typescript
// services/auth/MsalAuthProvider.ts
export class MsalAuthProvider {
    private static instance: MsalAuthProvider;
    private msalInstance: PublicClientApplication | null = null;

    static getInstance(): MsalAuthProvider {
        if (!MsalAuthProvider.instance) {
            MsalAuthProvider.instance = new MsalAuthProvider();
        }
        return MsalAuthProvider.instance;
    }

    async getToken(scopes: string[]): Promise<string> {
        // Silent token acquisition with fallback to interactive
        try {
            const result = await this.msalInstance!.acquireTokenSilent({ scopes });
            return result.accessToken;
        } catch {
            const result = await this.msalInstance!.acquireTokenPopup({ scopes });
            return result.accessToken;
        }
    }
}
```

### API Client Pattern
```typescript
// services/SdapApiClient.ts
export class SdapApiClient {
    constructor(
        private baseUrl: string,
        private getAccessToken: () => Promise<string>,
        private timeout: number = 300000
    ) {}

    async uploadFile(request: FileUploadRequest): Promise<SpeFileMetadata> {
        const token = await this.getAccessToken();
        const response = await fetch(`${this.baseUrl}/obo/containers/${request.containerId}/files`, {
            method: 'PUT',
            headers: { 'Authorization': `Bearer ${token}` },
            body: request.file
        });
        
        if (!response.ok) {
            throw new Error(await this.getErrorMessage(response));
        }
        
        return response.json();
    }
}
```

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
| Use singleton for MSAL | Create multiple MSAL instances |
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

# 5. VERIFY deployment
pac solution list | grep -i "{SolutionName}"
```

> **Full Guide**: See `docs/ai-knowledge/guides/PCF-V9-PACKAGING.md` Part B

## Common Issues

### MSAL Not Initialized
```typescript
// ✅ CORRECT: Wait for initialization
if (!authProvider.isInitializedState()) {
    await authProvider.initialize();
}
const token = await authProvider.getToken(scopes);
```

### Token Expired During Request
```typescript
// ✅ CORRECT: Auto-retry on 401
if (response.status === 401) {
    authProvider.clearCache();
    const newToken = await authProvider.getToken(scopes);
    // Retry with new token
}
```

---

*Refer to root `CLAUDE.md` for repository-wide standards.*
