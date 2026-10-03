# Task 010 (C-1) — deviations from the POML

> Task: `tasks/010-c1-remove-dead-briefing-hooks.poml`. Rigor FULL. Executed 2026-10-03.

## 1. Verification before deletion (per the escalation trigger + the project's own
   "assert nothing about the mechanism without grepping it" constraint)

Grepped the three hook names (`useBriefingNotifications`, `useBriefingNarration`,
`useBriefingActions`) repo-wide before touching anything. Findings:

- **No live call site anywhere in production code.** The only places the hooks were
  actually *invoked* were their own dedicated unit test files (`renderHook(() =>
  useBriefingXxx(...))`) — i.e. each hook tested itself in isolation. `DailyBriefingApp.tsx`
  imports only `useBriefingRender`, `useInlineTodoCreate`, `useBriefingPreferences` from
  `../hooks` — never the three dead ones. The escalation trigger ("if any hook has a live
  call site, STOP") did not fire.
- **Correction to `reuse-verification-2026-10-02.md` §8.1**: that note states the three
  hooks are "Not in the barrel" (not exported from
  `Spaarke.DailyBriefing.Components/src/index.ts`). This is **false** — the package's
  top-level `index.ts` has `export * from './hooks'`, and `hooks/index.ts` explicitly
  re-exported all three by name (with a comment calling them "Legacy ... retained for
  back-compat"). They WERE part of the package's public barrel surface. This makes the
  misdirection hazard larger than §8.1 estimated (severity was downgraded 🔴→🟡 partly on
  the strength of the "not in the barrel" claim), though it does not change the task's
  disposition — delete them regardless. Recorded here rather than silently corrected in
  that note, since `reuse-verification-2026-10-02.md` is a dated evidence record, not a
  living doc; a later pass can decide whether to annotate it.

## 2. Scope grew beyond "any now-unreferenced helper" in `notificationService.ts`

The POML anticipated deleting "the three hooks and any now-unreferenced helper in
notificationService.ts that existed only to serve them" (background section names only
`markBriefingChecked` / `markBriefingRemoved` / `extendBriefingTtl` as the backing writes).

Verifying call sites of every exported function in `notificationService.ts` found that
**the entire module was dead**, not just the three write helpers: `fetchNotifications`,
`fetchAndGroupNotifications`, `groupByCategory`, `computeTimeWindowIso`,
`filterByDueWithinDays`, `buildDisabledChannelsFilter`, and `markAllBriefingsChecked` were
all consumed *only* by `useBriefingNotifications.ts` / `useBriefingActions.ts` (plus their
own tests) — nothing in the live `/render` path (`briefingService.fetchBriefingLive` →
`useBriefingRender`) ever called into `notificationService.ts`. Deleted the whole file
rather than leaving an orphaned module with zero production callers.

Consequently also deleted (not named in the POML's `<outputs>`, but a direct, necessary
consequence):
- `test/notificationService.test.ts` (tested only the now-deleted module)
- `test/DailyBriefingApp.smoke.test.tsx` and `test/CountReconciliation.smoke.test.tsx` —
  both suites were **already `describe.skip(...)`'d** since the R7 Wave 12 cutover
  (2026-06-30), tracked as a deferred full-rewrite ("DEF in restart doc §13"). Both
  `jest.mock('../src/services/notificationService', ...)` the now-deleted module and
  `import` directly from it, so ts-jest would fail to resolve them regardless of the
  `.skip`. Since they contributed zero passing tests today and test exactly the retired
  appnotification→notificationService→useBriefingActions mechanism, deleting them (rather
  than rewriting a skipped suite) was the smallest correct change. If the deferred rewrite
  is still wanted, it needs a new project — the interim mock pattern these files used no
  longer has a module to mock.

## 3. AC1 ("repo-wide grep ... zero hits outside git history") — scope decision

Taken completely literally, AC1 would require editing ~65 files across roughly a dozen
other projects' `tasks/*.poml`, `spec.md`, `design.md`, and `notes/*.md` — all historical
records of **other, mostly-completed projects** (`spaarke-daily-update-service` through
`-r5`, `spaarke-notification-spine-r1`, `spaarkeai-assistant-enhancements-r1`,
`spaarke-ai-platform-unification-r3/r7`, `unified-access-control-r2`,
`spaarke-ai-architecture-redesign-r1`). Rewriting another project's historical task/spec
files is out of this task's scope and risks exactly the "scope expansion beyond task
boundaries" escalation condition in root CLAUDE.md §6. **Decision**: AC1 is read as scoped
to this project's own files, live source (`src/`), and live docs (`docs/`) — not other
projects' historical archives, which are left untouched (analogous to git history).

Within that reading, fully satisfied:
- `src/` — zero literal hits of the three hook names remain **except** inside explanatory
  comments I added myself, stating that the hook was deleted (e.g. "the now-deleted
  `useBriefingNotifications`"). Scrubbing those too would remove the only breadcrumb
  explaining why a referenced mechanism no longer exists at that call site, which seems
  counter to the task's own spirit (misdirection, not silence, is the hazard). Left as-is;
  flagging so the main session can make the opposite call if it disagrees.
- `docs/architecture/SPAARKEAI-COMPONENT-MODEL.md` and
  `docs/guides/BUILD-A-NEW-NARRATIVE-OUTPUT-CONSUMER.md` — corrected (both stated the dead
  hooks as current fact, not historically).
- **Left untouched, deliberately**: `docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md`
  line 182 — a dated "R2 update (project `spaarke-daily-update-service-r2`, 2026-06-18)"
  changelog-style entry describing what was true *at that date*; rewriting it would be
  revisionist in a doc that is otherwise a sequence of dated amendment blocks.
- **Left untouched, out of scope**: `src/server/api/Sprk.Bff.Api/Api/Ai/DailyBriefingEndpoints.cs:571`
  and `tests/unit/Sprk.Bff.Api.Tests/Api/Ai/DailyBriefingResponseShapeTests.cs` — both
  reference `useBriefingNarration.ts` only as a JSON-response-shape compatibility note
  ("the widget parser at `useBriefingNarration.ts` consumes the exact same JSON shape"),
  not as the appnotification-dismiss misdirection AC2 is actually about. Neither file is in
  the POML's `<relevant-files>` / `<outputs>`, and editing BFF code triggers the full CLAUDE.md
  §10 BFF Hygiene chain (publish-size measurement, etc.) for a comment-only change — judged
  disproportionate for this XS task. Flagging as a candidate follow-up, not filed as a
  GitHub Issue since it's a minor stale-reference comment, not a behavior defect.

## 4. `jest.config.cjs` coverage list

Replaced the two deleted-hook entries in `collectCoverageFrom` with `useBriefingRender.ts`
(the live hook, which already has dedicated coverage via
`useBriefingRender.isEmptyResponse.test.ts`) rather than just deleting the lines outright.

## 5. Pre-existing baseline test failures (not introduced by this task)

`npm test` on `Spaarke.DailyBriefing.Components` after this task's changes: **18 suites, 3
failed / 15 passed; 131 tests, 1 failed / 130 passed.** All three failures were verified to
be pre-existing and unrelated:
- `test/emailShareDraft.test.ts` — imports `buildEmailActivityRecord` from
  `DailyBriefingApp.tsx`, which does not export it. `DailyBriefingApp.tsx` was not in git's
  modified set for this change beyond my own header-comment edit (no export changed).
- `test/legalWorkspaceSectionRegistry.test.ts` — fails to compile because
  `../../../solutions/LegalWorkspace/src/sectionRegistry.ts:326` references a
  `widthPreference` property that doesn't exist on its inferred type. `sectionRegistry.ts`
  is untouched by this task (not in `git status`).
- `test/ActivityNotesSection.callbacks.test.tsx` — one assertion expects `onKeep` called
  with ttl `604800`, receives `0`. `ActivityNotesSection.tsx` is untouched by this task.

None of these three files are among the three deleted hooks, the deleted
`notificationService.ts`, or any file this task edited. `npm run build` (`tsc --noEmit`)
is fully clean (zero errors) after this task's changes, confirming no dangling import from
the deletions. Not fixed — out of scope for C-1; flagging for the main session to decide
whether to file as GitHub Issues per CLAUDE.md §5.0 if not already tracked elsewhere.
