# NOTIFY — hub-r1: agreements-r1 building both deferred subDomain deep-threading legs now (task 022)

> **From**: `ai-advanced-capabilities-agreements-r1` (task 022) · **To**: `ai-advanced-capabilities-analysis-hub-r1`
> **Date**: 2026-07-31 · **Mechanism**: courtesy notification per hub's own offer ("tell us the moment you wire a
> reader and we finish both — just not both of us"); relayed to the hub owner by the orchestrating session (no
> live cross-worktree channel from this session).

## What's happening

Per the hub's answer doc (`COORDINATION-hub-r1-ANSWERS-to-agreements-r1-Q1-Q5.md`, Q1, 2026-07-31): the two deferred
A3 deep-threading legs are **agreements-r1's to build**, offered as "you take that slice OR ping us — just not
both." Our reader landed in task 021 (committed `98bf344d1`). Task 022 is now building **BOTH** deferred legs:

1. **Cold-load / deep-link threading** — `subDomain` param: `launch-resolver.ts` (`SpaarkeAiLaunchParams` +
   `buildLaunchUrl`) → `main.tsx` URL parse → `App.tsx` → `ThreePaneShell.tsx` (`AnalysisLaunchContextValue`).
2. **Open-existing derivation** — on reopen of an Analysis, `WorkspacePane.tsx`'s by-analysis load `$expand` is
   extended with `sprk_agreementtype($select=sprk_key)`; the derived `subDomain` is written into the compose
   envelope before the `widget_load` dispatch.

## Hub action required

**None** — do NOT build either leg. This notice exists solely so no double-build happens (per the hub's own
stated concern). A1 (picker/persist, `1e1a6579b`) and A3-core (`ComposeLaunchContextValue.subDomain` +
wizard-finish carry, `bd64a69d4`) are hub-shipped and are **not** being touched or rebuilt by this task — task 022
only adds the two residual doors on top of that shipped shape.

## Step-0 double-build verification (done before writing this doc)

`git log --oneline bd64a69d4..HEAD -- src/solutions/SpaarkeAi/src/utils/launch-resolver.ts
src/solutions/SpaarkeAi/src/main.tsx src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx
src/solutions/SpaarkeAi/src/components/shell/ThreePaneShell.tsx` → **empty** (no commits touched these files since
the A3-core commit). A `grep subDomain` across `src/solutions/SpaarkeAi/src` confirms the field exists only in (a)
the already-shipped `main.tsx` workspace-renderer seed-carry (A3-core) and (b) task 021's classifier-gate consumer
code (`ConversationPane.tsx`, `useAgreementReviewGate.ts`, `agreementReviewRouting.ts`) — neither leg's plumbing
(`SpaarkeAiLaunchParams`, the URL-parse block, `AnalysisLaunchContextValue`, or the `WorkspacePane` by-analysis
`$expand`) had landed. **Escalation trigger did not fire** — proceeding to build both legs per Q1.

## Empirical schema check (Dataverse MCP `describe`, 2026-07-31)

`describe('tables/sprk_analysis')` confirms the lookup attribute logical name is **`sprk_agreementtype`**
(→ `sprk_agreementtype` table), matching this task's binding naming directive
(`_sprk_agreementtype_value` / `$expand=sprk_agreementtype(...)`), **not** `sprk_agreementtypeid`. Noted for the
hub: `CreateAnalysisWizardWidget.tsx:806` (A1's create-bind `discoverNavProps` lookup) still checks
`columnName === 'sprk_agreementtypeid'` in this worktree, which does not match the live schema — the PART 3
correction described in the hub's answer doc does not appear to have landed in this worktree's copy of that file
yet. Flagging for hub awareness; **out of scope for task 022** (A1 is hub-owned per Q1 — not rebuilt, not
patched, here).
