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
