# C-4 (Task 052) — WorkspaceRenderer Interface Design

> **Goal**: Define a minimum-viable seam between `WorkspaceLayoutWidget` (in
> `@spaarke/ai-widgets`) and the concrete renderer implementation
> (`LegalWorkspaceApp` in `src/solutions/LegalWorkspace/`). Zero behavioral
> change today — the interface is shaped to exactly fit what
> `LegalWorkspaceApp` already accepts.

---

## Design tenets (binding)

1. **Minimal surface** — accept only what `LegalWorkspaceApp` already requires.
   No speculative fields (no lifecycle hooks, no event callbacks, no plugin
   primitives).
2. **Context-agnostic** (ADR-012) — the interface lives in `@spaarke/ui-components`
   and depends only on platform abstractions (`IWebApi`-like shape).
3. **Function-based auth ADR-028** — no token snapshots in props. Auth state
   is resolved by the renderer's own bootstrap (LegalWorkspace already does
   this via `setLegalWorkspaceRuntimeConfig` + its embedded `useWorkspaceLayouts`
   call chain). The interface NEVER carries a `token` or `accessToken` field.
4. **No registry coupling** — `WorkspaceLayoutWidget` accepts `renderer?: WorkspaceRenderer`
   as an OPTIONAL prop; when absent, resolves from a tiny `getDefaultWorkspaceRenderer()`
   accessor (set at app bootstrap by the host). Avoids dynamic-import lazy
   registry overhead because there is exactly ONE default renderer today.
5. **React 19 standard types** — `React.ComponentType<WorkspaceRendererProps>`
   so both class and function components satisfy the contract.

---

## Chosen shape

```ts
// @spaarke/ui-components/src/workspace/WorkspaceRenderer.ts

import type * as React from "react";

/**
 * Minimal Xrm.WebApi shape consumed by workspace renderers. Mirrors what
 * `LegalWorkspaceApp` already accepts today. Concrete consumers may narrow
 * further via a more specific `IWebApi` type, but the interface stays loose
 * so renderers in other contexts (PCF, future hosts) can satisfy it.
 *
 * `unknown` would be too loose for type-safe consumption; `any` violates lint.
 * Using `Record<string, unknown>` for the property bag keeps the contract
 * portable without leaking platform types.
 */
export interface WorkspaceRendererWebApi {
  retrieveMultipleRecords: (
    entityLogicalName: string,
    options?: string,
    maxPageSize?: number
  ) => Promise<{ entities: Record<string, unknown>[] }>;
  retrieveRecord?: (
    entityLogicalName: string,
    id: string,
    options?: string
  ) => Promise<Record<string, unknown>>;
  createRecord?: (
    entityLogicalName: string,
    data: Record<string, unknown>
  ) => Promise<{ id: string; entityType: string }>;
  updateRecord?: (
    entityLogicalName: string,
    id: string,
    data: Record<string, unknown>
  ) => Promise<{ id: string; entityType: string }>;
  deleteRecord?: (
    entityLogicalName: string,
    id: string
  ) => Promise<{ id: string; entityType: string }>;
}

/**
 * Props passed by `WorkspaceLayoutWidget` (or any future host widget) to a
 * `WorkspaceRenderer`. Designed to mirror `ILegalWorkspaceAppProps` exactly
 * so the existing renderer (`LegalWorkspaceApp`) implements this interface
 * with zero shape changes.
 */
export interface WorkspaceRendererProps {
  /** Renderer-specific version identifier (debug/footer/diagnostics). */
  version: string;
  /** Reserved layout dimension hint; widgets pass 0 for unconstrained. */
  allocatedWidth: number;
  /** Reserved layout dimension hint; widgets pass 0 for unconstrained. */
  allocatedHeight: number;
  /** Dataverse WebApi reference resolved by the host (frame-walk-aware). */
  webApi: WorkspaceRendererWebApi;
  /** Current Dataverse user GUID (curly braces stripped). */
  userId: string;
  /** Layout GUID for deep-link / initial-state activation. */
  initialWorkspaceId?: string;
  /**
   * `true` when the renderer is hosted inside another shell (e.g. SpaarkeAi
   * workspace tab). Hosts expect the renderer to suppress internal chrome
   * (page header, footer, outer FluentProvider, theme-sync side effects)
   * when `embedded` is `true`.
   */
  embedded?: boolean;
}

/**
 * Public contract: any React component that satisfies `WorkspaceRendererProps`
 * can serve as a workspace renderer. `LegalWorkspaceApp` is the default
 * registered renderer today (R4 task 052).
 */
export type WorkspaceRenderer = React.ComponentType<WorkspaceRendererProps>;
```

## Default-renderer accessor (in @spaarke/ui-components)

```ts
// @spaarke/ui-components/src/workspace/defaultWorkspaceRenderer.ts

import type { WorkspaceRenderer } from "./WorkspaceRenderer";

let _default: WorkspaceRenderer | null = null;

/**
 * Register a default workspace renderer. Call once at host bootstrap (e.g.
 * SpaarkeAi `main.tsx`). Subsequent calls overwrite — last writer wins. The
 * first registration is sufficient for most hosts; tests may call again with
 * `clearDefaultWorkspaceRenderer()` between cases.
 */
export function setDefaultWorkspaceRenderer(renderer: WorkspaceRenderer): void {
  _default = renderer;
}

/**
 * Read the registered default workspace renderer.
 * Returns `null` when no renderer has been registered yet — callers should
 * fall back to a placeholder UI.
 */
export function getDefaultWorkspaceRenderer(): WorkspaceRenderer | null {
  return _default;
}

/**
 * Clear the registered default renderer. Test-only.
 */
export function clearDefaultWorkspaceRenderer(): void {
  _default = null;
}
```

## How LegalWorkspaceApp satisfies the interface

`ILegalWorkspaceAppProps` already matches `WorkspaceRendererProps` exactly:
- `version: string` ✓
- `allocatedWidth: number` ✓
- `allocatedHeight: number` ✓
- `webApi: IWebApi` ✓ (LW's `IWebApi` is a *narrower* superset of
  `WorkspaceRendererWebApi`, so it remains assignable — covariant on a
  function-property bag is technically tricky, but since `WorkspaceRendererWebApi`
  uses `Record<string, unknown>` for entity payloads, `IWebApi` will satisfy
  it after a `satisfies` check in the wrapper)
- `userId: string` ✓
- `initialWorkspaceId?: string` ✓
- `embedded?: boolean` ✓

Verification: `LegalWorkspaceApp satisfies WorkspaceRenderer` will compile
without changes to `LegalWorkspaceApp` internals. We only need to register it
in the default-renderer slot.

## WorkspaceLayoutWidget changes

Before (line 46 + lines 173-185):

```tsx
import { LegalWorkspaceApp } from "@spaarke/legal-workspace";

// ...

return (
  <div className={styles.root} data-testid="workspace-layout-widget-root">
    <LegalWorkspaceApp
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

After:

```tsx
import {
  getDefaultWorkspaceRenderer,
  type WorkspaceRenderer,
} from "@spaarke/ui-components";

// ...

interface WorkspaceLayoutWidgetExtraProps {
  /** Optional injected renderer; falls back to default registered renderer. */
  renderer?: WorkspaceRenderer;
}

// ... inside the component, near the existing webApi/empty-state branch:

const Renderer = renderer ?? getDefaultWorkspaceRenderer();

if (!Renderer) {
  return (
    <div className={styles.root} data-testid="workspace-layout-widget-no-renderer">
      <div className={styles.emptyState}>
        <Text size={300}>
          No workspace renderer registered. Call setDefaultWorkspaceRenderer()
          at app bootstrap.
        </Text>
      </div>
    </div>
  );
}

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

**Note**: The `@spaarke/legal-workspace` direct import is removed.

## Default registration site

`SpaarkeAi/src/main.tsx` (the only host today) registers
`LegalWorkspaceApp` as the default at bootstrap, BEFORE rendering the React
tree:

```ts
import { LegalWorkspaceApp } from "@spaarke/legal-workspace";
import { setDefaultWorkspaceRenderer } from "@spaarke/ui-components";

setDefaultWorkspaceRenderer(LegalWorkspaceApp);
```

This preserves the existing direct dependency (SpaarkeAi already imports
`@spaarke/legal-workspace`), but moves the binding from the *widget* (a
shared-lib concern) to the *host* (a solution-specific concern). The widget
is now context-agnostic per ADR-012.

## Alternatives considered (rejected)

| Approach | Reason rejected |
|---|---|
| Add to `WorkspaceWidgetRegistry` (lazy-load alongside other widgets) | Over-engineered: workspace renderers are NOT pluggable in the same way ad-hoc workspace widgets are. There is exactly ONE default today, and the host knows it at compile time. |
| Pure prop injection (no default accessor) | Would require every consumer of `WorkspaceLayoutWidget` to pass the renderer — but the widget is currently registered into `WorkspaceWidgetRegistry` and resolved by type string. The host can't easily inject props at the registry-resolution boundary. The default-accessor pattern is the seam. |
| Abstract class / inheritance | Speculative; no consumer needs lifecycle hooks today. React component type is sufficient. |
| Render-prop function (vs ComponentType) | Less ergonomic — `React.ComponentType` allows JSX usage `<Renderer ... />` which mirrors current code. |
| Renderer registry with multiple keys (e.g. by layout type) | Speculative; today every layout uses LegalWorkspaceApp. Adding multi-key resolution is future work, not today's seam. |

## Test plan

1. **Default registered**: After `main.tsx` runs, `getDefaultWorkspaceRenderer()`
   returns `LegalWorkspaceApp`. Tested via a unit test that imports both
   the accessor and a stub renderer.
2. **Widget resolves default**: Unit test mounts `WorkspaceLayoutWidget` after
   registering a stub renderer; verifies stub is rendered with the same
   props `LegalWorkspaceApp` would have received.
3. **Injected renderer wins**: When `renderer={Stub}` is passed, the widget
   uses `Stub` even if a different default is registered.
4. **No renderer registered**: Widget renders the "no renderer" empty state
   (graceful degradation; matches the "no Xrm" pattern).

---

*Generated 2026-05-26 for task 052 interface design.*
