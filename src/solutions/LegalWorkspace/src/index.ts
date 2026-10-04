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
// Renderer registration — read before embedding
//
// `LegalWorkspaceRenderer` (a WorkspaceRenderer-typed alias of
// `LegalWorkspaceApp`) was DELETED 2026-10-03 (reuse audit C-6): zero call
// sites, and its docstring prescribed
// `setDefaultWorkspaceRenderer(LegalWorkspaceRenderer)` — a route that BYPASSES
// the composition SpaarkeAi depends on. The live host
// (`src/solutions/SpaarkeAi/src/main.tsx`) registers `SpaarkeAiWorkspaceRenderer`:
// a wrapper rendering `<LegalWorkspaceApp sections={createLegalWorkspaceSectionRegistry(...)} />`
// inside a tab-scoped `ComposeLaunchContext`. A new embedding host should follow
// that wrapper pattern. `LegalWorkspaceApp` satisfies `WorkspaceRenderer`
// structurally, so no typed alias is needed.
// ---------------------------------------------------------------------------

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
