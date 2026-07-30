# Task 063 — Env Deletion Deferred to Task 071

**Date**: 2026-07-29
**Task**: 063 - Delete web resources + `AnalysisWorkspace/` tree + deploy script + reconcile casing

## What was done in task 063

1. Deleted `src/client/code-pages/AnalysisWorkspace/` (whole tree, 51 files: `launcher/`, `src/`, `build-webresource.ps1`, config).
2. Deleted `scripts/Deploy-AnalysisWorkspace.ps1`.
3. Fixed a real dangling reference found during code-review: `scripts/Build-AllClientComponents.ps1` still listed
   `"AnalysisWorkspace"` in its `$WebpackCodePages` array — this would have broken the script post-deletion. Removed
   the entry + updated the `.PARAMETER Component` doc-comment example.
4. Updated two doc-comment mentions that pointed at now-deleted files/examples (`scripts/README.md`,
   `scripts/Deploy-SpaarkeAi.ps1` — the latter referenced "Follows the same pattern as Deploy-AnalysisWorkspace.ps1").
5. Verified (grep, exact-name, case-sensitive) that `sprk_analysisworkspace`, `sprk_AnalysisWorkspace`,
   `sprk_AnalysisWorkspaceLauncher`, `sprk_analysisworkspace_8bc0b` have **zero** matches across `src/`, `scripts/`,
   `infra/`.
6. Verified the 5 KEEP shared-lib files (ThreePaneLayout, useTwoPanelLayout, SprkChatBridge, renderMarkdown,
   StreamingWriteHarness) all live in `src/client/shared/Spaarke.UI.Components/` with real consumers outside the
   deleted tree (SpaarkeAi, SmartTodo, SprkChat, etc.) — none was deleted.
7. Built `Spaarke.UI.Components` (`npm run build` → clean `tsc`) and typechecked `SpaarkeAi` (surface gate: 0
   surface-owned errors) to prove nothing imported the deleted tree.

## Why the Dataverse environment artifacts were NOT deleted in this task

The task's fallback clause explicitly allows: *"via the dataverse MCP / dv-solution tooling if accessible, ELSE
produce precise deletion steps for task 071 (deploy) and document them in your notes."*

The `mcp__dataverse__delete_record` tool requires **explicit live human approval per call** ("Proceed solely on
explicit user consent... only after asking the user if they are ok with the deletion and you have received an
affirmative response"). This task ran autonomously (no live user available to grant that per-call consent), so
deletion was deferred to task 071 rather than silently skipped or force-run without consent.

## Precise deletion steps for task 071

Environment: `spaarkedev1` (`https://spaarkedev1.crm.dynamics.com`)

Looked up via Dataverse MCP `read_query`. **Note**: no `sprk_AnalysisWorkspaceLauncher` web resource exists in this
environment — the deployed bundle was published under a different naming convention
(`cc_Spaarke.Controls.AnalysisWorkspace/*`), not the `launcher/sprk_AnalysisWorkspaceLauncher.js` name the retired
repo tree used locally.

| # | Artifact | Table | Record ID (GUID) |
|---|---|---|---|
| 1 | `sprk_analysisworkspace` (Webpage/HTML) | `webresource` | `ba85d6be-6413-f111-8343-7c1e520aa4df` |
| 2 | `cc_Spaarke.Controls.AnalysisWorkspace/bundle.js` (Script) | `webresource` | `c8c1a3cc-8d64-48ca-b3ed-ff7d645f8358` |
| 3 | `cc_Spaarke.Controls.AnalysisWorkspace/css/AnalysisWorkspace.css` (Style Sheet) | `webresource` | `24dc8d05-45d9-4dde-bc98-2cb61a588a87` |
| 4 | `sprk_analysisworkspace_8bc0b` (dead Custom Canvas Page, displayname "Analysis Workspace") | `canvasapp` | `ff384002-a0ea-4253-a2a0-88ae3c49108b` |

**Recommended execution order for task 071**:
1. Confirm nothing in the target solution's managed layer still references these 4 records (re-run the same
   `read_query` lookups below to reconfirm IDs haven't drifted since 2026-07-29).
2. Delete the `canvasapp` record first (#4) — it is the custom page that (per task 061) is a dead navigation branch
   with zero live entry points.
3. Delete the 3 `webresource` records (#1–#3) via `pac` (`pac webresource delete` / solution-aware removal) or the
   `mcp__dataverse__delete_record` tool **with explicit human approval** — do NOT force through automation without
   the live consent the tool requires.
4. Publish customizations after deletion.
5. Re-run the verification queries below; all 4 should return zero rows.

**Verification queries** (re-run before + after deletion):
```sql
SELECT webresourceid, name, displayname, webresourcetype FROM webresource WHERE name LIKE '%AnalysisWorkspace%'
SELECT canvasappid, name, displayname, canvasapptype FROM canvasapp WHERE name LIKE '%analysisworkspace%'
```

## Keep-list verification (step 1 of task 063)

Confirmed via `Grep` that each of the 5 keep-list files has a surviving consumer OUTSIDE
`src/client/code-pages/AnalysisWorkspace/`:

| File | Location | Surviving consumer(s) |
|---|---|---|
| ThreePaneLayout | `Spaarke.UI.Components/src/components/ThreePaneLayout/` | `SpaarkeAi/src/components/shell/ThreePaneShell.tsx`, `SpaarkeAi/src/App.tsx` |
| useTwoPanelLayout | `Spaarke.UI.Components/src/hooks/useTwoPanelLayout.ts` | `SmartTodo` (vite.config.ts / tsconfig references) |
| SprkChatBridge | `Spaarke.UI.Components/src/services/SprkChatBridge.ts` | `SprkChat.tsx`, `useSseStream.ts`, `useDocumentStreamConsumer.ts`, 3 test suites |
| renderMarkdown | `Spaarke.UI.Components/src/services/renderMarkdown.ts` | `SprkChatMessageRenderer.tsx`, `SprkChatMessage.tsx`, `CommunicationTimeline/MessageRow.tsx` |
| StreamingWriteHarness | `Spaarke.UI.Components/src/__test-harness__/StreamingWriteHarness.tsx` | Standalone manual-debug harness (self-contained; doc-comment cross-references the now-deleted `streaming-e2e.test.ts` as historical provenance only, not an import) |

No escalation trigger fired — none of the 5 keep-list files lived only inside the deleted tree.

## Build verification

- `src/client/shared/Spaarke.UI.Components`: `npm run build` (`tsc`) → clean, no errors.
- `src/solutions/SpaarkeAi`: `npm run typecheck` (`scripts/tsc-surface-gate.mjs`) → 70 pre-existing shared-lib errors
  (deferred to Phase B, unrelated to this change), 0 surface-owned errors.

## Quality gates (Step 9.5)

- Code review: Clean. One real dangling reference found and fixed (`Build-AllClientComponents.ps1`).
- ADR check: Compliant (ADR-006). Deferring environment deletion to 071 is the task's own authorized fallback, not
  a silent gap — no live code path points at the dead resources (061 already repointed all launch points).
