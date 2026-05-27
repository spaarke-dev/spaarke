/**
 * @spaarke/legal-workspace — barrel export
 *
 * Public surface for consumers that need to embed the LegalWorkspace
 * experience inside another shell (e.g. SpaarkeAi's `WorkspaceLayoutWidget`
 * which renders a chosen workspace layout inside a workspace pane tab).
 *
 * Round 4 Fix 4 (2026-05-21):
 *   This barrel was added so SpaarkeAi can `import { LegalWorkspaceApp } from
 *   "@spaarke/legal-workspace"` instead of copying 30+ files / ~10K LOC of
 *   section factories + DataverseService + FeedTodoSync context. The operator's
 *   reuse principle taken to its logical conclusion: don't copy factories —
 *   reuse the whole working app as a single widget.
 *
 * Standalone LegalWorkspace's runtime entry is `main.tsx` → `App.tsx`
 * (NOT this barrel), so adding these exports does NOT change the standalone
 * bundle's behaviour or size (FR-25 / NFR-10).
 */

export { LegalWorkspaceApp } from "./LegalWorkspaceApp";
export type { ILegalWorkspaceAppProps } from "./LegalWorkspaceApp";

// ---------------------------------------------------------------------------
// R4 task 052 (C-4): WorkspaceRenderer interface binding
//
// `LegalWorkspaceApp` satisfies the `WorkspaceRenderer` contract from
// `@spaarke/ui-components` (its prop shape `ILegalWorkspaceAppProps` mirrors
// `WorkspaceRendererProps` exactly). This re-export provides a `WorkspaceRenderer`
// -typed binding so host bootstraps (e.g. SpaarkeAi `main.tsx`) can register
// it as the default renderer without import-site type assertions:
//
//   import { LegalWorkspaceRenderer } from "@spaarke/legal-workspace";
//   import { setDefaultWorkspaceRenderer } from "@spaarke/ui-components";
//   setDefaultWorkspaceRenderer(LegalWorkspaceRenderer);
//
// The binding is the SAME component as `LegalWorkspaceApp` (no wrapping, no
// behavioural change — Risk R-4: zero observable diff). The named export
// `LegalWorkspaceApp` is preserved for callers that already import it directly
// (notably `WorkspaceLayoutWidget` until its C-4 refactor lands in parallel).
// ---------------------------------------------------------------------------

import { LegalWorkspaceApp as _LegalWorkspaceApp } from "./LegalWorkspaceApp";
import type { WorkspaceRenderer } from "@spaarke/ui-components";

/**
 * `LegalWorkspaceApp` re-exported under its `WorkspaceRenderer` contract.
 * Use this binding (or `LegalWorkspaceApp` directly) when registering as the
 * default renderer in a host bootstrap. The runtime behaviour is identical
 * to `LegalWorkspaceApp`.
 *
 * # Type assertion rationale
 *
 * `ILegalWorkspaceAppProps` is a STRUCTURAL SUPERSET of `WorkspaceRendererProps`:
 * the prop shapes match name-for-name, but LegalWorkspace's `IWebApi`
 * makes `retrieveRecord`/`createRecord`/`updateRecord`/`deleteRecord`
 * REQUIRED, while `WorkspaceRendererWebApi` makes them OPTIONAL. Function-
 * parameter contravariance therefore prevents a direct type assignment.
 *
 * The double-cast (`as unknown as WorkspaceRenderer`) is safe at runtime
 * because:
 *   1. `WorkspaceLayoutWidget` (the only caller today) always passes a
 *      frame-walked `Xrm.WebApi` reference, which exposes ALL methods.
 *   2. Future hosts that supply a narrower `webApi` MUST still provide
 *      `retrieveRecord` etc. if their concrete renderer is `LegalWorkspaceApp`
 *      — this is a documented host contract, not a runtime check.
 *
 * Tightening `WorkspaceRendererProps.webApi.retrieveRecord` to required
 * would over-fit the interface to LegalWorkspace's needs and break the
 * "minimal-viable interface" spec constraint. Tightening
 * `IWebApi.retrieveRecord` to optional would cascade through dozens of
 * LegalWorkspace call sites that rely on its non-null contract. The
 * boundary cast is the least-invasive seam.
 */
export const LegalWorkspaceRenderer = _LegalWorkspaceApp as unknown as WorkspaceRenderer;

/**
 * Round 4 Fix 4.1 (2026-05-21): `setRuntimeConfig` exposed so embedding shells
 * (SpaarkeAi) can initialize LegalWorkspace's runtime-config singleton
 * BEFORE rendering `<LegalWorkspaceApp embedded />`.
 *
 * Why this is required:
 *   LegalWorkspace has its OWN `runtimeConfig` singleton (separate from
 *   SpaarkeAi's). Code paths that ran during embedded rendering — e.g.
 *   `getBffBaseUrl()` called from `WorkspaceGrid`'s navigateTo handlers and
 *   the `useWorkspaceLayouts` BFF fetch — would throw "[LegalWorkspace]
 *   Runtime config not initialized" because SpaarkeAi's `main.tsx` only
 *   initialized SpaarkeAi's own singleton.
 *
 *   Option A from the fix plan: SpaarkeAi's bootstrap also calls
 *   `setRuntimeConfig(...)` from `@spaarke/legal-workspace` with the SAME
 *   resolved config so both singletons agree on `bffBaseUrl` / `scope` /
 *   `clientId` / `tenantId`. The two singletons remain distinct in-process
 *   instances — they just hold equivalent values.
 *
 * Standalone LegalWorkspace continues to call its own internal
 * `setRuntimeConfig` from its `main.tsx` — this re-export does not change
 * that path or the standalone bundle's behaviour (FR-25 / NFR-10
 * byte-identical).
 */
export { setRuntimeConfig as setLegalWorkspaceRuntimeConfig } from "./config/runtimeConfig";
