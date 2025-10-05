# Universal Dataset Grid – Compliance Assessment & Remediation Guide

_Date: 2025-10-05_
_Target folders_: `spaarke/src/controls/UniversalDatasetGrid/**`
_Author_: Internal code review

---

## 1. Executive Summary

| Area                               | Status            | Notes                                                                                         |
|------------------------------------|-------------------|----------------------------------------------------------------------------------------------|
| PCF lifecycle & structure          | ⚠ Needs refactor | Mixing legacy React renderers with React 18, re-creating DOM on every update, limited dataset abstractions |
| Fluent UI v9 compliance (ADR-021)* | ⚠ Non-compliant  | Only command bar uses Fluent components; grid/toolbar fall back to raw HTML & inline styles |
| Accessibility & theming            | ⚠ Weak           | Hard-coded colors/fonts, no responsive theming, limited keyboard support                     |
| Configuration & linting            | ✅ Acceptable     | Fluent packages present, Microsoft lint rules enabled, manifest properly references resources |

\*ADR reference placeholder—the actual ADR number may differ; confirm in repository docs.

---

## 2. Key Findings

### 2.1 PCF Control Architecture Gaps

1. **Mixed React renderers**  
   `ThemeProvider` uses `ReactDOM.createRoot` (React 18) while the command bar still calls `ReactDOM.render`/`unmountComponentAtNode`. This mismatch can double-mount components, leak memory, and breaks modern React best practices.
2. **Manual DOM rebuilds**  
   `updateView` clears and rebuilds the entire DOM tree every time Power Apps triggers it. PCF guidance recommends maintaining a mounted component and pushing in new props for performance and accessibility.
3. **Dataset operations**  
   There is no use of dataset paging, virtualization, or PCF dataset APIs beyond raw iteration. Large data sets will render slowly and degrade host app responsiveness.
4. **Selection & outputs**  
   Selection toggles call `notifyOutputChanged()` aggressively and re-render via `updateView`, causing flicker and redundant updates.
5. **Theme awareness**  
   The control ignores host theme changes (`context.mode` and `context.fluentDesignLanguage`). Hard-coded fonts (`Segoe UI`) and colors bypass theme tokens.

### 2.2 Fluent UI v9 ADR Violations

1. **Grid UI**  
   The core grid/toolbar uses plain HTML tables and buttons. ADR requires Fluent UI v9 components (e.g., `DataGrid`, `Checkbox`, `Toolbar`, `Menu`).
2. **Token usage**  
   Inline styles with literal colors `#f3f2f1`/`#323130` conflict with the Fluent design system and break dark/high-contrast mode compatibility.
3. **Theming**  
   `ThemeProvider` always renders `webLightTheme`; there’s no adaptation or token overrides for host theme variants.
4. **Command bar separation**  
   Even though `CommandBar.tsx` uses Fluent components, it is outside the main React tree, making state management clumsy and preventing hooks/context sharing.

---

## 3. Recommended Remediation Plan

| Step | Focus Area | Action Items |
|------|------------|--------------|
| 1 | React/PCF lifecycle | Rewrite the control to mount a single React tree via `createRoot` from `ThemeProvider`, removing legacy `ReactDOM.render`. Use React state for dataset, selection, and commands. |
| 2 | Fluent UI adoption | Implement the grid and toolbar with Fluent UI v9 primitives (`DataGrid`, `Toolbar`, `Button`, `Checkbox`). Replace inline CSS with tokens. |
| 3 | Theming | Hook `ThemeProvider` into Power Apps theming (`context`) and adjust Fluent tokens dynamically (`webLightTheme`, `webDarkTheme`, or brand variants). |
| 4 | Dataset handling | Leverage `context.parameters.dataset` paging, sorting, and virtualization techniques to avoid full DOM rebuilds. Pass dataset info into React and render only changes. |
| 5 | Accessibility & telemetry | Remove excessive console logging; add error boundaries or telemetry integration. Ensure keyboard navigation works (Fluent components cover this). |
| 6 | Configuration/Docs | Update developer docs explaining how to run/test the control, new React entry point, and lint rules enforcing Fluent usage (e.g., ESLint rule banning raw `<table>`). |

---

## 4. Code Transformation Examples

### 4.1 Enter a Single React Root

**Before** (`CommandBar.tsx`, `index.ts`):

```tsx
legacyReactDOM.render(
  React.createElement(CommandBarComponent, { ... }),
  this.container
);
```

**After** (`index.tsx` entry):

```tsx
import { createRoot } from 'react-dom/client';
import { FluentProvider } from '@fluentui/react-components';
import { UniversalDatasetGridRoot } from './UniversalDatasetGridRoot';

export class UniversalDatasetGrid implements ComponentFramework.StandardControl<IInputs, IOutputs> {
  private root: Root | null = null;

  public init(context, notifyOutputChanged, state, container) {
    this.root = createRoot(container);
    this.root.render(
      <FluentProvider theme={resolveTheme(context)}>
        <UniversalDatasetGridRoot context={context} notifyOutputChanged={notifyOutputChanged} />
      </FluentProvider>
    );
  }

  public updateView(context) {
    if (!this.root) { return; }
    this.root.render(
      <FluentProvider theme={resolveTheme(context)}>
        <UniversalDatasetGridRoot context={context} notifyOutputChanged={this.notifyOutputChanged} />
      </FluentProvider>
    );
  }

  public destroy() {
    this.root?.unmount();
    this.root = null;
  }
}
```

### 4.2 Fluent UI Toolbar & Grid

**Before** (excerpt from `renderMinimalGrid`):

```ts
const toolbar = document.createElement("div");
const refreshBtn = this.createButton("Refresh", () => this.context.parameters.dataset.refresh());
```

**After** (React component using Fluent v9):

```tsx
import { Toolbar, ToolbarButton, DataGrid, DataGridHeader, DataGridRow, Checkbox } from '@fluentui/react-components';

export const UniversalDatasetGridRoot: React.FC<Props> = ({ context }) => {
  const [selectedIds, setSelectedIds] = React.useState(context.parameters.dataset.getSelectedRecordIds() ?? []);

  const rows = React.useMemo(() => {
    const dataset = context.parameters.dataset;
    return dataset.sortedRecordIds.map(id => dataset.records[id]);
  }, [context.parameters.dataset]);

  return (
    <DataGrid
      items={rows}
      selectionMode="multiselect"
      selectedItems={selectedIds}
      onSelectionChange={(event, data) => {
        setSelectedIds(Array.from(data.selectedItems, item => item.getRecordId()));
        context.parameters.dataset.setSelectedRecordIds(selectedIds);
      }}
      columns={[
        // Define Fluent columns using dataset metadata
      ]}
      header={<CustomHeader columns={context.parameters.dataset.columns} />}
      focusMode="composite"
      aria-label="Universal dataset grid"
    >
      <Toolbar>
        <ToolbarButton appearance="primary" onClick={() => context.parameters.dataset.refresh()}>Refresh</ToolbarButton>
        <ToolbarButton
          appearance="secondary"
          disabled={selectedIds.length === 0}
          onClick={() => setSelectedIds([])}
        >
          Clear selection
        </ToolbarButton>
      </Toolbar>

      <DataGridHeader>
        {context.parameters.dataset.columns.map(column => (
          <DataGridRow key={column.name} columnId={column.name}>
            {column.displayName}
          </DataGridRow>
        ))}
      </DataGridHeader>

      {/* Render rows via Fluent's render pattern */}
    </DataGrid>
  );
};
```

### 4.3 Theme Resolution

```ts
function resolveTheme(context: ComponentFramework.Context<IInputs>): Theme {
  const base = context.fluentDesignLanguage?.name?.includes('Dark')
    ? webDarkTheme
    : webLightTheme;

  // Optional: extend tokens based on Power Apps colors
  return {
    ...base,
    colorBrandForeground1: context.fluentDesignLanguage?.palette?.primary ?? base.colorBrandForeground1,
  };
}
```

### 4.4 ESLint Rule to Enforce Fluent Components

Add to `eslint.config.mjs`:

```javascript
rules: {
  '@typescript-eslint/no-unused-vars': 'off',
  'react/no-unknown-property': 'error',
  'no-restricted-syntax': [
    'error',
    {
      selector: "JSXOpeningElement[name.name='table']",
      message: 'Use Fluent UI DataGrid instead of raw <table>',
    },
  ],
},
```

---

## 5. Implementation Checklist

- [ ] Consolidate React rendering (single root).
- [ ] Create React components using Fluent v9 for toolbar, grid, command bar, dialogs.
- [ ] Map PCF dataset to Fluent `DataGrid` with virtualization/paging.
- [ ] Implement `resolveTheme` and subscribe to Power Apps theme changes.
- [ ] Replace inline CSS with Fluent tokens (`tokens.spacingXXX`, `tokens.colorNeutralForeground1`).
- [ ] Introduce TypeScript state management for selection/commands; reduce `notifyOutputChanged` calls.
- [ ] Update ESLint to block non-Fluent primitives and run `npm run lint`.
- [ ] Document the new architecture (`README.md` or control-specific doc).
- [ ] Add automated test(s) if possible (e.g., React Testing Library) to check the Fluent tree mounts correctly.

---

## 6. Next Steps & Ownership

- **Implementation**: Assign to PCF engineering squad (see AI-generated task breakdown for Sprint 5).
- **Reviews**: Pair with the UI/UX lead for Fluent compliance validation and with the Power Platform architect for PCF lifecycle checks.
- **Docs**: Update internal wiki with the new control architecture and coding standards.
- **Timeline**: Suggest a two-sprint timebox (Sprint 5 for infrastructure refactor, Sprint 6 for dataset/theming enhancements).

---

## 7. References

- [Power Apps Component Framework Documentation](https://learn.microsoft.com/power-apps/developer/component-framework/)
- [Microsoft Fluent UI React Components v9](https://react.fluentui.dev/)
- ADRs located under `docs/adr/` (notably Fluent UI compliance ADR)
- `eslint.config.mjs` for lint setup
- `@microsoft/eslint-plugin-power-apps` for PCF-specific linting
