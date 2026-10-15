# Task 081 — on-branch cleanup (C-8, C-11, C-13, C-17): progress at the 2026-10-04 restart

> Branch `ontology/081-cleanup`, worktree `C:\wt081`, WIP head **`89b5230f9`** (pushed; no PR, by design: it merges
> into `docs/ontology-platform-design`). Recorded by the main session from the agent's stop report, because the
> agent was stopped before writing its own notes.

## 2026-10-05: second independent review FAILED; round 3 moves 081 to its own PR to master

Rework `51fb2287a` (≈60 more Xrm walks converged; F2–F5, F7–F13 verified true) failed review round 2 on:
**H1** colours still differ per surface (only "overdue" matched); **H2** VisualHost parses DateOnly as UTC
(today → overdue in US zones); **H3** the branch carries 31 ontology commits, so it cannot be a master PR as is;
**H4** ~20 touched packages never built or tested (no `node_modules` in the worktree). Plus M1 inconsistent
frame order, M2 capability checks lost, M3 command-bar walk shallower, L1–L6.

**Owner decision 2026-10-05 — one due-urgency palette everywhere = SmartTodo's**: overdue red · 0–3 days dark
orange · 4–7 yellow · 8–10 grey · beyond that no badge. The event due-date card loses green. One tier function
(`dueUrgencyForDays`) in `Spaarke.UI.Components/src/utils/dateLocal.ts`; Visuals keeps no boundary copy.

**Coordinator decisions**: window-first frame order, walking the full parent chain (bounded) with an optional
capability requirement; compact relative-time style is deterministic English, not ICU-dependent.

Round 3 cherry-picks the three task commits onto `fix/ui-duplication-cleanup-081` off `origin/master`
(worktree `C:\wt081m`), fixes everything, builds and tests EVERY touched package, and opens the PR.

## 2026-10-05: independent review FAILED; rework dispatched

The independent review (code-review + adr-check, opus) of `89b5230f9` returned **FAIL**. The rework agent's record
replaces this section when it reports.

| # | Sev | Finding | Coordinator decision |
|---|---|---|---|
| F1 | High | AC-1 false: ~16 frame-walks remain, incl. `DailyBriefingApp.tsx:300-318` (the POML's FIRST named target, and buggy: a cross-origin parent throws inside the `??` chain so `top` is never tried) | Converge all; list any that cannot, with evidence |
| F2 | High | `WorkspaceLayoutWidget` stopped writing `window.Xrm`; `WorkspaceGrid.tsx` (5 handlers) + `ActionCardHandlers.ts:86` read only `window.Xrm` → Summarize Files / Playbook Library can silently no-op in embedded LegalWorkspace | Convert readers to `getXrm()`; no global write; fix the embedded-mode contract doc |
| F3 | High | 28–29 days → "this month"; 13 days → "last week" | Fix bucketing + boundary tests |
| F4 | High | DateOnly `sprk_duedate` through an elapsed-time formatter: today → "Due: 14 hours ago" in US zones | Day-granular due label via `parseDueDate` + the existing due-label helper; never hours |
| F5 | Medium | Clock skew → "in 3 seconds"; FR-07 relies on "just now" | \|diff\| < 60 s → "just now", both directions |
| F6 | Medium | AC-4 false: `FeedItemCard.deriveUrgencyTier` 3/10; `EventDueDateCard` day 3 amber vs most-urgent in SmartTodo | Converge onto the canonical tier source |
| F7 | Medium | The audit's U5 (inline local-midnight day-diff) copies untouched (7 sites) | Migrate to `daysBetweenLocalMidnight` |
| F8 | Medium | Shared `EmptyState` enlarges compact sites; `ActivityFeed/EmptyState.tsx` missed | `size: 'compact'`; migrate ActivityFeed |
| F9 | Medium | No tests for `relativeTime.ts` / `EmptyState.tsx`; two mocks hide the real code | Tests + boundary mocks |
| F10–F13 | Low | Untrue header comment; `navigator.language` mixes languages ("Modified vor 5 Minuten"); unrelated lockfile churn; misleading `@deprecated` | Comment fixed; **locale param, default `'en'`** (UI strings are English); revert churn; fix comment |
| Esc. | — | The deleted LegalWorkspace formatter was compact on purpose ("5m ago") to fit the feed's right column | Shared formatter gains a compact style, used where the replaced copy was compact |

**User-visible change #3 above ("relative times follow the browser language") is withdrawn** by the F11 decision.

## Status at the 2026-10-04 restart: code complete; three things left before 081 is ✅

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


## Final record (2026-10-07)

**MERGED** as `14a5e229c` (PR #1309), owner-approved. Rounds 5-9 after the round-4 review: R4-1..R4-9 fixed; round-5 review PASS-WITH-FINDINGS (R5-1..R5-9: analyzer silent passes, guard not enforced, package-only test install, eslint directives, `||` sites, pinning tests, main.tsx, nameResolution metadata source, wizard userId frame skip) fixed in round 6; round-6 review PASS-WITH-FINDINGS (anonymous fns/IIFE/getters/dynamic import silent) fixed in round 7 + owner decision to make the guard a BLOCKING Tier 1 job; round-7 review FAIL on B1 (ci-router docs_only skipped all Tier 1 for client+doc PRs, pre-existing) - owner approved fixing the router (every-file-is-docs) in round 8 with analyzer M1/M2/m1-m5; final review PASS-WITH-FINDINGS (3,000-file cap, CHANGELOG conflict, nits) fixed in round 9. Master merges after #1312 and #1302 (TodoSection.tsx deletion kept). Guard 259/0/0 at merge.

