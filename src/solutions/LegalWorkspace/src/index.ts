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
 * # Type assignment (R4 task 072 / A.2, 2026-05-27)
 *
 * `ILegalWorkspaceAppProps` and `WorkspaceRendererProps` are now structurally
 * equivalent name-for-name AND method-for-method on `webApi`. The previous
 * variance mismatch (LegalWorkspace required methods; `WorkspaceRendererWebApi`
 * made them optional) was removed in task 072 by tightening
 * `WorkspaceRendererWebApi` so all 5 Dataverse-WebApi methods are REQUIRED.
 *
 * Operator architectural decision 2026-05-27 (Path 2a): LegalWorkspace IS the
 * dashboard renderer; new dashboard pieces are added INSIDE that library, not
 * as separate renderers. The "loose-interface flexibility for many renderers"
 * use case that motivated the previous all-optional shape was fictional. The
 * cast `as unknown as WorkspaceRenderer` was static-type debt for that
 * fictional flexibility and has been removed.
 *
 * TypeScript now accepts the assignment directly via structural typing — no
 * cast, no wrapper component, no runtime adapter.
 */
export const LegalWorkspaceRenderer: WorkspaceRenderer = _LegalWorkspaceApp;

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

/**
 * Section registry composition factory (R2 Option D, 2026-06-18).
 *
 * Post-Option D: the legacy `setLegalWorkspaceDailyBriefingNotificationLoader`
 * setter is gone. Embedding consumers (SpaarkeAi) build a custom registry via
 *   `createLegalWorkspaceSectionRegistry({ dailyBriefing: { loadNotificationContext } })`
 * and pass it to `<LegalWorkspaceApp sections={...} />` via a wrapper renderer
 * registered through the existing `setDefaultWorkspaceRenderer` slot.
 *
 * Standalone LegalWorkspace uses `SECTION_REGISTRY` (the no-options default) —
 * byte-identical behavior preserved (FR-25 / NFR-10).
 *
 * See `projects/spaarke-daily-update-service-r2/notes/option-d-registry-as-composition.md`
 * for the full design rationale and cookbook for adding a new widget.
 */
export {
  SECTION_REGISTRY,
  createLegalWorkspaceSectionRegistry,
  getSectionById,
  getSectionsByCategory,
} from "./sectionRegistry";
export type { LegalWorkspaceSectionRegistryOptions } from "./sectionRegistry";

// Ergonomic re-export so consumers building a custom registry can type their
// own `sections` prop without re-importing from `@spaarke/ui-components`.
export type { SectionRegistration } from "@spaarke/ui-components";
