# Task 081 — on-branch cleanup (C-8, C-11, C-13, C-17): progress at the 2026-10-04 restart

> Branch `ontology/081-cleanup`, worktree `C:\wt081`, WIP head **`89b5230f9`** (pushed; no PR, by design: it merges
> into `docs/ontology-platform-design`). Recorded by the main session from the agent's stop report, because the
> agent was stopped before writing its own notes.

## Status: code complete; three things left before 081 is ✅

1. One **clean, uncontended** SpaarkeAi jest run. Pristine baseline (isolated): 18 failed / 121 suites. Two "after"
   runs under heavy load: 24 and 36 failed, all `Exceeded timeout of 5000ms`, none touching edited files. A clean
   "after" rerun: **1 failed / 121** (`HardSlashExecutor.test.ts`, untouched). Needs one more clean confirmation.
2. Independent code-review + adr-check (FULL rigor; every substantial task in this project has had real review
   findings), then merge the branch into `docs/ontology-platform-design`.
3. POML 081 status + `<completion>`, TASK-INDEX ✅, drift check. Remove the stray baseline worktree
   `C:\wt081-base` (`git worktree remove C:\wt081-base`).

## What was done

| Item | Result |
|---|---|
| **C-8** one Xrm frame-walk | Canonical `getXrm()` in `Spaarke.UI.Components/src/utils/xrmContext.ts`. Deleted `services/xrmGlobal.ts` (a second `getXrm`). Converged 3 EmailComposer files, `PolymorphicPicker.tsx` (kept `getXrmForPicker` as a one-line ALIAS of `getXrm`, because 3 Communication.Components files import that name), `SummarizeAnalysisStep.tsx`, `WorkspaceLayoutWidget.tsx`, `launchCreate.ts`. Widened `XrmNavigation.navigateTo` to accept real `NavigateToOptions` (a type gap it surfaced). |
| **C-11** one EmptyState | New `Spaarke.UI.Components/src/components/EmptyState.tsx`. Migrated DailyBriefing, `SmartToDo.tsx`, `PlaybookGalleryWidget.tsx`, and a 4th found on recount: `LegalWorkspace/NotificationPanel/EmptyState.tsx`. **Not migrated:** `SmartTodoWidget.tsx`'s two inline empty states (outside the audit's named three — follow-up candidate) and the PCF `SemanticSearchControl/EmptyState.tsx` (materially different, React-16 platform-library boundary). |
| **C-13** one relative-time formatter | New `Spaarke.UI.Components/src/utils/relativeTime.ts`, from `62277d50a:ChatSessionCard.tsx:30-61`, fixing the hard-coded `'en'` (now `navigator.language`) and elapsed-24h day buckets (now `daysBetweenLocalMidnight` from `dateLocal.ts`, task 084). Five live copies converged (recounted): `LegalWorkspace/utils/formatRelativeTime.ts` (deleted), `NotificationPanel/notificationTypes.ts`, `DailyBriefing/TldrSection.tsx`, `SpaarkeAi/HistoryOverlay.tsx`, `SpaarkeAi/ManageWorkspacesPane.tsx`. |
| **C-17** due-date tiers 3/7/10 | SmartTodo `todoScoring.ts` and both `dueLabelUtils.ts` already used 3/7/10. `Spaarke.Visuals/EventDueDateCard.tsx` used 3/5 → **3/7** (it has only 3 badge colours, so no 10 boundary). **Not converged:** `LegalWorkspace/FeedItemCard.tsx` `deriveUrgencyTier` (3/10) — a different concept (3-state card-accent colour), left as a reviewed judgment call. |

## ⚠️ User-visible behaviour changes (call out in the review and the eventual PR)

1. **LegalWorkspace feed "Due:" text**: the old `formatRelativeTime` returned **"just now" for ANY future time**, so a
   future due date read "Due: just now". It now says "in N days" (a bug fix).
2. **Event due-date card colour**: the amber band moves from ≤5 days to **≤7 days** (owner's 3/7/10 decision).
3. Relative times now follow the **browser language** instead of always English.

## Baseline vs after (agent's numbers)

| Package | Baseline | After |
|---|---|---|
| Spaarke.UI.Components | 10–11 failed / 232 suites (flaky set) | 7 failed, same named suites; `tsc` clean |
| Spaarke.AI.Widgets | 1 failed (OOM, flaky) / 41; 654/654 tests | identical; `tsc` clean |
| Spaarke.DailyBriefing.Components | 3 failed / 18 (pre-existing) | identical (its test mock needed `formatRelativeTime` + `EmptyState`) |
| Spaarke.Communication.Components | 2 failed / 34 | identical |
| Spaarke.Visuals | 6/6, 71/71 | identical |
| Spaarke.SmartTodo.Components | 7/7, 73/73 | identical |
| CommunicationActions PCF | 3/3, 22/22 | identical, after a new `test-mocks/spaarke-ui-components.ts` (ESM dist was not transformable by jest) |
| SpaarkeAi | 18 failed / 121 (isolated) | see "Status" item 1 |
| LegalWorkspace | no jest runner; pre-existing `tsc` noise | none on touched lines |

Note: several of these "pre-existing" failures (UI.Components, DailyBriefing, AI.Widgets) are the ones PR #1123
fixes; after #1123 merges and master is merged here, re-baseline.
