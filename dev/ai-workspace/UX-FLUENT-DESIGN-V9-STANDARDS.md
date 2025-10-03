# Spaarke Fluent UI v9 — AI Vibe Coding Guide
**Purpose:** Teach an AI coding agent exactly how to build consistent, accessible, and performant UIs using Microsoft Fluent UI React v9 across Model‑Driven Apps (PCF), Custom Pages, and standalone SPAs.

_Last updated: 2025‑10‑01_

---

## 1) What to install
Use the converged v9 stack only.

- `@fluentui/react-components` — core components
- `@fluentui/react-icons` — Fluent System Icons
- (Optional) `@fluentui/tokens` — token types, when you need to reference token names directly

```bash
pnpm add @fluentui/react-components @fluentui/react-icons
```

**Do not** import v8 (`@fluentui/react`) components.

---

## 2) App shell and theming (non‑negotiable)
Always render under a single `FluentProvider` at the app root. Theme via design tokens (CSS variables). Nest providers **only** for scoped overrides.

```tsx
import { FluentProvider, webLightTheme } from "@fluentui/react-components";

export default function AppRoot() {
  return (
    <FluentProvider theme={webLightTheme}>
      <AppRoutes />
    </FluentProvider>
  );
}
```

### Spaarke brand themes
Generate light/dark from a 16‑stop brand ramp (centralized module, no scattered hex).

```ts
import { BrandVariants, createLightTheme, createDarkTheme } from "@fluentui/react-components";

export const brand: BrandVariants = {
  10:"#020305",20:"#0b1a33",30:"#102a52",40:"#14386c",50:"#184787",60:"#1c56a2",
  70:"#1f64bc",80:"#2173d7",90:"#2683f2",100:"#4a98ff",110:"#73adff",120:"#99c1ff",
  130:"#b9d3ff",140:"#d2e2ff",150:"#e6eeff",160:"#f3f7ff"
};

export const spaarkeLight = createLightTheme(brand);
export const spaarkeDark  = createDarkTheme(brand);
```

**Rule for agents**
- Never hard‑code colors, spacing, or shadows. Use theme tokens or component props.

---

## 3) Styling: Griffel patterns the agent must use
Use `makeStyles` + `shorthands` + tokens. Compose with `mergeClasses`.

```tsx
import { makeStyles, shorthands, tokens, mergeClasses } from "@fluentui/react-components";

const useStyles = makeStyles({
  root: {
    display: "grid",
    gridTemplateRows: "auto 1fr",
    ...shorthands.gap(tokens.spacingHorizontalM),
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
    backgroundColor: tokens.colorNeutralBackground1,
  },
  error: {
    ...shorthands.border("2px", "solid", tokens.colorPaletteRedBorder1),
    backgroundColor: tokens.colorPaletteRedBackground1,
  }
});

export function Panel({ hasError, children }: { hasError?: boolean; children: React.ReactNode }) {
  const s = useStyles();
  return <section className={mergeClasses(s.root, hasError && s.error)}>{children}</section>;
}
```

**Never**
- Write global CSS targeting Fluent internals.
- Style by DOM selectors; always style via classes you control.

---

## 4) Composition via slots (no DOM spelunking)
Customize components using **slots**, not query selectors.

```tsx
import { Button } from "@fluentui/react-components";
import { ArrowUpload20Regular } from "@fluentui/react-icons";

<Button
  icon={{ children: <ArrowUpload20Regular />, "aria-hidden": true }}
  appearance="primary"
>
  Upload
</Button>
```

---

## 5) Data‑dense UIs — DataGrid recipe
Use `DataGrid` for tables. Support sorting and multiselect out‑of‑the‑box.

```tsx
import {
  DataGrid, DataGridBody, DataGridRow, DataGridCell,
  TableColumnDefinition, createTableColumn, TableCellLayout
} from "@fluentui/react-components";

type Row = { id: string; name: string; status: "Draft" | "Final"; };

const columns: TableColumnDefinition<Row>[] = [
  createTableColumn<Row>({
    columnId: "name",
    compare: (a, b) => a.name.localeCompare(b.name),
    renderHeaderCell: () => "Name",
    renderCell: item => <TableCellLayout truncate>{item.name}</TableCellLayout>
  }),
  createTableColumn<Row>({
    columnId: "status",
    renderHeaderCell: () => "Status",
    renderCell: item => item.status
  })
];

export function DocumentGrid({ items }: { items: Row[] }) {
  return (
    <DataGrid items={items} columns={columns} sortable selectionMode="multiselect" getRowId={i => i.id}>
      <DataGridBody>{({ item }) => (
        <DataGridRow key={item.id}>
          {({ renderCell }) => <DataGridCell>{renderCell(item)}</DataGridCell>}
        </DataGridRow>
      )}</DataGridBody>
    </DataGrid>
  );
}
```

**Large lists:** add windowing (e.g., `@tanstack/react-virtual`) around rows. Estimate row height ≈ 44px.

---

## 6) Forms pattern
Wrap inputs with `Field` for labels, messages, and required indicators.

```tsx
import { Field, Input, Textarea, Combobox, Option } from "@fluentui/react-components";

<Field label="Document name" required validationState="none">
  <Input placeholder="Enter document name" />
</Field>

<Field label="Type">
  <Combobox placeholder="Select type">
    <Option value="contract">Contract</Option>
    <Option value="brief">Brief</Option>
  </Combobox>
</Field>
```

---

## 7) Toolbars and cards
Use `Toolbar` for clustered actions; use `Card` for object summaries.

```tsx
import { Toolbar, ToolbarButton, Card, CardHeader, CardBody, Text } from "@fluentui/react-components";

<Toolbar aria-label="Document actions">
  <ToolbarButton appearance="primary">New</ToolbarButton>
  <ToolbarButton>Delete</ToolbarButton>
</Toolbar>

<Card appearance="filled" size="small">
  <CardHeader header={<Text weight="semibold">Contract.pdf</Text>} />
  <CardBody>Final draft uploaded yesterday</CardBody>
</Card>
```

---

## 8) Accessibility contract for agents
Fluent v9 ships good defaults; you still must:

- Provide visible text or `aria-label` for icon‑only buttons.
- Keep focus order logical; never suppress focus outlines.
- Use `aria-live="polite"` or role="status" to announce async state.
- Maintain WCAG AA contrast (4.5:1 body text, 3:1 large text/components).
- Test keyboard navigation on grids, dialogs, and menus.

Snippet:

```tsx
<Button aria-label="Upload document">
  {/* icon slot here */}
</Button>

<div role="status" aria-live="polite">{statusMessage}</div>
```

---

## 9) Power Platform (PCF) integration
### Theme bridging
In model‑driven apps with modern theming, consume the host’s token theme if available; otherwise fall back to Spaarke theme.

```tsx
export function render(context: ComponentFramework.Context<any>, container: HTMLDivElement) {
  const hostTheme = (context as any).fluentDesignLanguage?.tokenTheme;
  const theme = hostTheme ?? spaarkeLight;

  createRoot(container).render(
    <FluentProvider theme={theme}>
      <DocumentListPCF />
    </FluentProvider>
  );
}
```

### Dataset vs. unbound
- Dataset PCF replaces a subgrid (preferred for lists).
- Unbound PCF uses a placeholder column/section; pass record context via inputs.

---

## 10) Performance rules
- Import only used components (tree‑shake).
- Do not re‑mount `FluentProvider` on every route.
- Memoize row/components (`React.memo`) and handlers (`useCallback`).
- Virtualize long lists. Keep reflow minimal by fixed row heights.
- Avoid heavy inline styles; use generated classes.

---

## 11) Testing utilities
Wrap tests with a provider to ensure tokens and focus behaviors match production.

```tsx
// test-utils.tsx
import { render } from "@testing-library/react";
import { FluentProvider } from "@fluentui/react-components";
import { spaarkeLight } from "../theme/brand";

export const renderWithFluent = (ui: React.ReactElement) =>
  render(<FluentProvider theme={spaarkeLight}>{ui}</FluentProvider>);
```

---

## 12) Agent guardrails (Do / Don’t)
**Do**
- Use `@fluentui/react-components` v9 only.
- Wrap every surface in `FluentProvider`.
- Use tokens via `tokens` import and component props.
- Compose via slots; style via `makeStyles`.
- Build tables with `DataGrid`; add virtualization for large lists.
- Ensure a11y: labels, focus, live regions, contrast.

**Don’t**
- Import v8 components.
- Hard‑code colors, spacing, or shadows.
- Query or mutate DOM to style internals.
- Add global CSS that targets Fluent selectors.
- Render outside a `FluentProvider`.

---

## 13) Scaffolds the agent can start from
### Page scaffold
```tsx
export function Page() {
  return (
    <>
      <Header />
      <main>
        <Toolbar aria-label="Actions">{/* buttons */}</Toolbar>
        <DocumentGrid items={[]} />
      </main>
    </>
  );
}
```

### Reusable “Document List” component
- Inputs: `items`, `onUpload`, `onCreate`
- Uses `Toolbar` + `DataGrid`, icons via slots, no DOM selectors.

---

## 14) Compliance checklist (copy into PR template)
- [ ] Uses v9 components; no v8 imports.
- [ ] Wrapped in a single app‑level `FluentProvider`.
- [ ] All custom CSS via `makeStyles`; tokens only.
- [ ] A11y labels provided; keyboard flow validated.
- [ ] Contrast meets WCAG AA.
- [ ] Lists virtualized when `items.length > 100`.
- [ ] Storybook story for light/dark.
- [ ] Unit test uses `renderWithFluent`.

---

## 15) References (internal)
- Spaarke Fluent UI Guidelines v9 (design spec and examples)
- Theme module: `/theme/brand.ts`
- PCF templates: `/pcf-templates/fluent-v9/`
- Testing helpers: `/test-utils/fluent.tsx`

---

### Rationale
These rules align Spaarke with Fluent 2 tokens, v9 components, Griffel styling, and Power Platform theming so that code generated by an AI agent is production‑ready, accessible, and consistent across apps. 
