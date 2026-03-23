# PCF Control Initialization Pattern

> **Domain**: PCF / Control Lifecycle
> **Last Validated**: 2026-01-20
> **Source ADRs**: ADR-006, ADR-012, ADR-022

> **CRITICAL**: Use React 16 APIs (`ReactDOM.render`). Do NOT use React 18 `createRoot`. See [ADR-022](../../adr/ADR-022-pcf-platform-libraries.md).

---

## Canonical Implementations

| File | Purpose |
|------|---------|
| `src/client/pcf/UniversalDatasetGrid/control/index.ts` | Standard PCF lifecycle |
| `src/client/pcf/UniversalQuickCreate/control/index.ts` | Dialog-style control |
| `src/client/pcf/SpeFileViewer/control/index.ts` | State machine pattern |

---

## Standard Lifecycle Pattern

```typescript
import * as React from "react";
import * as ReactDOM from "react-dom";  // NOT react-dom/client
import { FluentProvider } from "@fluentui/react-components";

export class ControlName implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private container: HTMLDivElement | null = null;
    private context: ComponentFramework.Context<IInputs>;
    private notifyOutputChanged: () => void;

    constructor() { }

    public init(
        context: ComponentFramework.Context<IInputs>,
        notifyOutputChanged: () => void,
        state: ComponentFramework.Dictionary,
        container: HTMLDivElement
    ): void {
        this.context = context;
        this.container = container;
        this.notifyOutputChanged = notifyOutputChanged;

        // Enable responsive container sizing
        context.mode.trackContainerResize(true);

        // Render React tree (React 16 pattern)
        this.renderComponent();
    }

    public updateView(context: ComponentFramework.Context<IInputs>): void {
        this.context = context;
        this.renderComponent();
    }

    public getOutputs(): IOutputs {
        return { /* output properties */ };
    }

    public destroy(): void {
        // React 16: unmountComponentAtNode (NOT root.unmount())
        if (this.container) {
            ReactDOM.unmountComponentAtNode(this.container);
            this.container = null;
        }
    }

    private renderComponent(): void {
        if (!this.container) return;

        // React 16: ReactDOM.render (NOT createRoot().render())
        ReactDOM.render(
            React.createElement(
                FluentProvider,
                { theme: this.resolveTheme() },
                React.createElement(RootComponent, {
                    context: this.context,
                    notifyOutputChanged: this.notifyOutputChanged
                })
            ),
            this.container
        );
    }
}
```

---

## Key Principles

### 1. React 16 Render Pattern
- Use `ReactDOM.render()` in `init()` and `updateView()` - NOT `createRoot()`
- Use `ReactDOM.unmountComponentAtNode()` in `destroy()` - NOT `root.unmount()`
- React 16 re-renders efficiently (no need to track root object)

### 2. Container Resize Tracking
```typescript
context.mode.trackContainerResize(true);
```
- Enables responsive layouts
- PCF re-calls `updateView()` on resize
- Access dimensions: `context.mode.allocatedWidth/Height`

### 3. Context is Transient
- Never store `context` in React state
- Always pass as prop (changes each `updateView()`)
- Access latest values via `context.parameters.*`

### 4. Output Property Updates
```typescript
// In React component
const handleChange = (value: string) => {
    outputValueRef.current = value;
    notifyOutputChanged(); // Triggers framework to call getOutputs()
};
```

---

## Root Component Props Interface

```typescript
interface RootComponentProps {
    context: ComponentFramework.Context<IInputs>;
    notifyOutputChanged: () => void;

    // Control-specific props
    webApi?: ComponentFramework.WebApi;
    config?: ControlConfiguration;
}
```

---

## FluentProvider Wrapper

Always wrap React tree in FluentProvider:

```typescript
<FluentProvider theme={theme}>
    <ErrorBoundary>
        <RootComponent {...props} />
    </ErrorBoundary>
</FluentProvider>
```

- Import from `@fluentui/react-components`
- Pass resolved theme (see `theme-management.md`)
- Include ErrorBoundary for resilience

---

## Version Footer Pattern

Include version in control footer for debugging:

```tsx
<span className={styles.versionText}>
    v{CONTROL_VERSION} • Built {BUILD_DATE}
</span>
```

Update version in **4 locations** when releasing:
1. `ControlManifest.Input.xml` - `version="X.Y.Z"`
2. UI footer component
3. Solution `solution.xml` - `<Version>`
4. Solution `ControlManifest.xml`

---

---

## Async Initialization (Auth, Config) — CRITICAL

> **Validated**: 2026-03-22 via SemanticSearchControl v1.1.22

### The Problem: `notifyOutputChanged()` does NOT trigger re-render for ReactControl

For `ComponentFramework.ReactControl` (the virtual control type), `notifyOutputChanged()` is designed to signal that **output property values changed** — it triggers `getOutputs()`. It does **not** reliably trigger `updateView()` when the control has no two-way bound field.

**Symptom**: Auth completes (logs confirm), `notifyOutputChanged()` is called, but the component stays in loading state forever. `updateView()` is never called again.

**Root cause**: The PCF class's `init()` does async work, sets a flag, calls `notifyOutputChanged()` — but the framework ignores it because there's no bound field watching for output changes.

### The Fix: Async init inside React component via `useEffect` + `useState`

Move all async initialization (auth, config fetching) into the React component. React's own reconciler handles the re-render — no PCF framework involvement needed.

```typescript
// ✅ CORRECT — in the React component (SemanticSearchControl.tsx, RelatedDocumentCount.tsx)
const [isAuthInitialized, setIsAuthInitialized] = useState(false);
const [resolvedApiBaseUrl, setResolvedApiBaseUrl] = useState('');

useEffect(() => {
  let cancelled = false;

  // Capture values at mount — context.webAPI and parameters are stable
  const webApi = context.webAPI;
  const manifestApiBaseUrl = context.parameters.apiBaseUrl?.raw ?? '';
  // ... other manifest params

  let dataverseUrl: string;
  try {
    dataverseUrl = typeof Xrm !== 'undefined'
      ? Xrm.Utility.getGlobalContext().getClientUrl()
      : window.location.origin;
  } catch { dataverseUrl = window.location.origin; }

  const doAuth = async () => {
    const apiBaseUrl = manifestApiBaseUrl || (await getApiBaseUrl(webApi));
    const tenantId = await getEnvironmentVariable(webApi, 'sprk_TenantId') || '';
    const clientAppId = await getEnvironmentVariable(webApi, 'sprk_MsalClientId') || '';
    const bffAppId = await getEnvironmentVariable(webApi, 'sprk_BffApiAppId') || '';

    await initializeAuth(tenantId, clientAppId, bffAppId, apiBaseUrl, dataverseUrl);

    if (!cancelled) {
      setResolvedApiBaseUrl(apiBaseUrl);
      setIsAuthInitialized(true);  // ← triggers React re-render automatically
    }
  };

  doAuth().catch(err => {
    if (!cancelled) console.error('[ControlName] Auth initialization failed:', err);
  });

  return () => { cancelled = true; };
}, []); // Run once on mount — parameters are stable for control lifetime
```

```typescript
// ❌ WRONG — in index.ts PCF class
private async resolveAndInitAuth(): void {
  await initializeAuth(...);
  this._authInitialized = true;
  this.notifyOutputChanged(); // ← does NOT trigger updateView() for read-only ReactControl
}
```

### Rules

| Rule | Reason |
|------|--------|
| Auth MUST be in React `useEffect` + `useState` | Only React state changes guarantee re-render |
| `notifyOutputChanged()` is for output field binding only | Not a general re-render trigger |
| Empty `[]` deps is intentional | Auth runs once; `context.webAPI` is stable |
| Always use `cancelled` flag | Prevents setState after unmount |
| `context` values captured at mount time | Avoids stale closure issues |

### Canonical Implementations

| File | Notes |
|------|-------|
| `src/client/pcf/SemanticSearchControl/SemanticSearchControl/SemanticSearchControl.tsx` | Full env-var resolution + auth |
| `src/client/pcf/RelatedDocumentCount/RelatedDocumentCount/RelatedDocumentCount.tsx` | Same pattern, simpler config |

---

## Version Footer Pattern

Include version in control footer for debugging:

```tsx
<span className={styles.versionText}>
    v{CONTROL_VERSION} • Built {BUILD_DATE}
</span>
```

Update version in **5 locations** when releasing:
1. `ControlManifest.Input.xml` - `version="X.Y.Z"` (cache key — update FIRST)
2. UI footer component
3. Solution `solution.xml` - `<Version>`
4. Solution `ControlManifest.xml`
5. `Solution/pack.ps1` - `$version`

---

## Related Patterns

- [Theme Management](theme-management.md) - Theme resolution
- [Error Handling](error-handling.md) - ErrorBoundary integration

---

**Lines**: ~200
