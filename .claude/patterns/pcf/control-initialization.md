# PCF Control Initialization Pattern

> **Domain**: PCF / Control Lifecycle
> **Last Validated**: 2025-12-19
> **Source ADRs**: ADR-006, ADR-012

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
export class ControlName implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private container: HTMLDivElement;
    private context: ComponentFramework.Context<IInputs>;
    private _root: ReactDOM.Root | null = null;
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

        // Create React root ONCE
        this._root = ReactDOM.createRoot(container);
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
        if (this._root) {
            this._root.unmount();
            this._root = null;
        }
    }

    private renderComponent(): void {
        this._root?.render(
            React.createElement(
                FluentProvider,
                { theme: this.resolveTheme() },
                React.createElement(RootComponent, {
                    context: this.context,
                    notifyOutputChanged: this.notifyOutputChanged
                })
            )
        );
    }
}
```

---

## Key Principles

### 1. Single React Root
- Create `ReactDOM.Root` once in `init()`
- Re-render in `updateView()` (don't recreate root)
- Unmount in `destroy()` to prevent memory leaks

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

## Related Patterns

- [Theme Management](theme-management.md) - Theme resolution
- [Error Handling](error-handling.md) - ErrorBoundary integration

---

**Lines**: ~130
