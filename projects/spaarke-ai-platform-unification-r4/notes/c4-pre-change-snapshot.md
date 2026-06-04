# C-4 (Task 052) — Pre-change Behavioral Snapshot

> **Risk R-4 mitigation**: zero behavioral change between pre- and post-refactor.
> **Approach**: Build-verify only (deploy deferred per parent guardrails). This
> snapshot captures the **structural** contract `WorkspaceLayoutWidget` has with
> `LegalWorkspaceApp` today so the interface design can mirror it exactly.

---

## Current pipeline (pre-refactor)

```
WorkspacePaneMenu → dispatches `widget_load` → 'workspace' channel
  → WorkspaceTabManager creates tab
    → WorkspaceLayoutWidget (registered in SpaarkeAi via WorkspaceWidgetRegistry)
      → renders <LegalWorkspaceApp embedded ... />
        → FeedTodoSyncProvider
          → WorkspaceGrid
            → useWorkspaceLayouts(initialWorkspaceId)
            → section factories (5)
```

## Direct import (the coupling C-4 removes)

`WorkspaceLayoutWidget.tsx`, line 46:
```ts
import { LegalWorkspaceApp } from "@spaarke/legal-workspace";
```

This direct import is the seam C-4 introduces. Post-refactor, `WorkspaceLayoutWidget`
imports the `WorkspaceRenderer` interface from `@spaarke/ui-components` and
resolves the concrete renderer via:
1. Injected `renderer?` prop (if provided), OR
2. Renderer registry (with `LegalWorkspaceApp` registered as default)

## Props passed to LegalWorkspaceApp today

From `WorkspaceLayoutWidget.tsx` lines 175-183 (the call site):

```tsx
<LegalWorkspaceApp
  version="embedded"
  allocatedWidth={0}
  allocatedHeight={0}
  webApi={webApi}              // resolved via locateXrm() frame walk
  userId={userId}              // resolved via locateXrm() + Utility.getGlobalContext
  initialWorkspaceId={data.layoutId}
  embedded                     // skips PageHeader, footer, FluentProvider, theme sync
/>
```

## ILegalWorkspaceAppProps interface today

```ts
export interface ILegalWorkspaceAppProps {
  version: string;
  allocatedWidth: number;
  allocatedHeight: number;
  webApi: IWebApi;
  userId: string;
  initialWorkspaceId?: string;
  embedded?: boolean;
}
```

## Mount-source contract (what the renderer needs)

- `webApi: IWebApi` — Dataverse WebApi reference from frame-walked Xrm
- `userId: string` — current user GUID (curly-braces stripped)
- `initialWorkspaceId?: string` — layout GUID for deep-link
- `embedded: boolean` — mode flag (true when rendered inside SpaarkeAi shell)
- `version: string` — bundle identifier (debug/footer use)
- `allocatedWidth/Height: number` — placeholder dimensions (always 0 in embedded mode)

## Xrm frame-walk semantics (currently inside WorkspaceLayoutWidget)

`locateXrm()` checks window → window.parent → window.top for `Xrm.WebApi`.
This logic is widget-owned, NOT renderer-owned. The renderer receives `webApi`
+ `userId` as plain props — it does NOT walk frames itself. This means the
`WorkspaceRenderer` interface MUST accept `webApi` + `userId` as props (the
renderer cannot resolve them).

## Empty-state behavior (no Xrm context)

Lines 161-171: if `webApi` is null after frame-walk, the widget renders an
empty-state Text element instead of mounting `LegalWorkspaceApp`. This logic
remains in `WorkspaceLayoutWidget` post-refactor (the renderer never sees the
"no Xrm" condition).

## Theme + provider ownership

In embedded mode, LegalWorkspaceApp:
- Skips its internal `<FluentProvider>` (lines 183-188) — SpaarkeAi's shell wraps
- Skips theme sync side effects (lines 103-120) — SpaarkeAi owns theme lifecycle
- Skips `<PageHeader>` + footer (lines 146-156, 169-175)

The renderer interface MUST inherit `embedded: boolean` so future renderers
respect the same provider-ownership contract.

---

## What zero-behavioral-change means structurally

Post-refactor, `WorkspaceLayoutWidget`'s render branch (lines 173-185 today)
becomes:

```tsx
const Renderer = renderer ?? resolveWorkspaceRenderer();
return (
  <div className={styles.root} data-testid="workspace-layout-widget-root">
    <Renderer
      version="embedded"
      allocatedWidth={0}
      allocatedHeight={0}
      webApi={webApi}
      userId={userId}
      initialWorkspaceId={data.layoutId}
      embedded
    />
  </div>
);
```

Where `Renderer` defaults to `LegalWorkspaceApp` (wrapped to satisfy the
`WorkspaceRenderer` interface).

**Identical** to current behavior because:
1. The default-resolved renderer IS `LegalWorkspaceApp`.
2. Props passed are identical.
3. The `<div>` wrapper + empty-state branches are unchanged.
4. No new context, providers, or side effects.

---

## ADR compliance check (interface design)

- **ADR-012**: Interface lives in `@spaarke/ui-components` (shared lib).
  `LegalWorkspaceApp` (the concrete renderer) stays in `src/solutions/LegalWorkspace/`.
  This is OK because the implementation is consumed via injection, NOT imported
  by the widget itself (the widget imports the *interface* from ui-components).
- **ADR-021**: Interface is a TS type — no UI surface, no token concerns.
  Concrete renderers (LegalWorkspaceApp) already comply.
- **ADR-022**: React 19 component type — interface uses
  `React.ComponentType<WorkspaceRendererProps>` for max compatibility.
- **ADR-028**: Interface MUST NOT require token snapshots. `webApi` is the
  Dataverse WebApi reference (not a token); `authenticatedFetch` is NOT in the
  interface — the renderer resolves auth via its own bootstrap (LegalWorkspace
  already initializes via `setLegalWorkspaceRuntimeConfig` per the existing
  embedded-mode contract).

---

*Generated 2026-05-26 for task 052 pre-change snapshot. Code-only pre-change
record; no live UI capture per parent guardrails (build-verify only, deploy deferred).*
