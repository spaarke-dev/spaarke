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
