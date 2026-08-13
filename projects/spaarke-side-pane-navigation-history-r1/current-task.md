# Current Task — spaarke-side-pane-navigation-history-r1

> Tracks the ACTIVE task only. History lives in `tasks/TASK-INDEX.md` + per-task POMLs.

## Status: Wave F COMPLETE (18/21) — full Navigator feature-complete; next = Deploy wave (086 env-mutating, OWNER-GATED)
- ✅ 001 GO · ✅ 010 xrmContext · ✅ 011 host+registry · ✅ 020 schema · ✅ 021 deployed to spaarkedev1 · ✅ 030 capture engine · ✅ **040 NavigatorPane code page** (Vite singlefile, 5/5 tests, registers NavigatorBody against SprkSidePaneHost)
- Spine: framework host → live sprk_navitem → capture engine → docked pane shell. All buildable + tested; deploy of the pane is task 086.

## Wave D COMPLETE (12/21): 041 ✅ 042 ✅ 050 ✅ 052 ✅(owner-only*) 060 ✅
Ran sequentially (shared NavigatorPane build/cache). Full Navigator surface works: Recent(Viewed/Edited), Pinned(Records/Monitored), Views. 70 tests pass in NavigatorPane.

## Wave E COMPLETE (14/21): 031 ✅ 051 ✅
Serialized (031 modifies @spaarke/ui-components; 051 rebuilds it via NavigatorPane) — different source files, shared build artifact.
- **031 retention**: `deleteHistoryItemsOlderThan(ownerId, cutoff)` in navItemRepository (owner+History-scoped OData filter `_ownerid_value eq {ownerId} and sprk_type eq 100000000 and sprk_lastvisited lt {iso}`); called inline in the capture tick AFTER a successful history upsert; prune failure routed through onError (non-fatal, never stops the poll). `HISTORY_RETENTION_DAYS=30`. Pins never pruned. 10/10 navigatorCaptureService tests. Gates clean (1 accepted warning: no $top batching on prune — documented). notes/task-031-retention.md.
- **051 bookmarks**: urlParse.ts (discriminated union record|view|weblink|reject; keys on etn/id/pagetype/viewid across ?query + #fragment) + bookmarkService.ts (`pinCurrentPage`/`addBookmark`) + Bookmarks group in PinnedTab. Logical targets navigate via Xrm.Navigation (view passes viewId); weblinks `window.open(url,'_blank','noopener')`. Additive lib extension: `CreatePinItemInput.source?` (defaults Manual — existing callers unaffected) + new `createWeblinkPinItem`. 99 NavigatorPane tests (70 prior + 29 new). tsc clean, Vite build verified. Gates clean. notes/task-051-bookmarks.md.
  - Deviation: record-branch bookmarks resolve real primary name (mirrors capture resolveDisplayName); view-name kept generic (ViewService.getViewById resolves system views only, not personal userquery). Documented.

## ⏳ OPEN DECISION (non-blocking) — 052 Monitored "assigned to me"
- 052 shipped **owner-scoped only** (Path A) — `sprk_monitor` is on 7 UserOwned entities but "assigned to me" has no uniform field.
- User asked re the **membership service** (`@spaarke/ui-components/services/membership.ts`, `createMembershipResolver`): it DOES resolve "assigned to me" (ADR-034 metadata-driven, `roles` filter) BUT is **BFF-backed** (`GET /api/users/me/memberships/{entityType}`, Auth v2 OBO) → conflicts with the project's NO-BFF MUST (NFR-01/NFR-02).
- **Awaiting user's call**: Path A (keep owner-only, strict NO-BFF) vs Path B (scoped read-only BFF exception, wire the membership resolver). Doesn't block Waves E/F/deploy.

## Wave F COMPLETE (18/21): 085 ✅ 081 ✅ 080 ✅ 070 ✅
Serialized (all share the @spaarke/ui-components→NavigatorPane build; NavigatorPane jest resolves the lib from dist/, so concurrent lib builds would race). Order: 085(lib stub)→081(verify)→080(security)→070(search).
- **085 stub (FR-13)**: StubContributor + test-only registration under SidePane/__stub__/; ZERO host-code changes (SprkSidePaneHost untouched) — framework extends by registration only. 16/16 SidePane tests. notes/task-085-stub.md.
- **081 retention-verify (FR-05 / SC-7)**: end-to-end verify test drives startNavigatorCapture through a page change → asserts >30d history pruned, pin survives, other user untouched. PASS, no defect. Corrected POML's stale ref (logic is in @spaarke/ui-components, not a NavigatorPane retentionService). notes/task-081-retention-verification.md.
- **080 security-trim (FR-12/NFR-04, SECURITY-SENSITIVE)**: securityTrimService (accessible|denied|transient); denied rows NEVER enter React state (leak-free by construction, no flash). 403/404→trim; network/timeout/5xx→keep (fail-safe default transient). Weblink/EntityList exempt (no record-name surface). Monitored group not extra-trimmed (live sprk_monitor query already server-side trimmed). 125 tests. Gates 0-critical. ⚠️ FLAG FOR OWNER REVIEW at PR/deploy. notes/task-080-security-trimming.md.
- **070 search (FR-11)**: QuickSwitcher + navigatorSearchIndex (module-scoped useSyncExternalStore, fed by tabs' already-trimmed rows — trim preserved) + liveSearchService. Ctrl/Cmd+K accelerator; local-first fuzzy → escalate-on-miss to Xrm.WebApi/ViewService; Enter navigates (Xrm.Navigation for logical, window.open noopener for weblink). 139 tests. Gates ran; fixed OData $filter injection-encoding + ARIA activedescendant in-task. notes/task-070-search.md.
  - Known/documented (informational, not blocking): weblink nav inconsistency RecentTab.openUrl vs QuickSwitcher window.open; Pinned/Views locally searchable only after first visit of that tab (miss escalates to live — correct per FR-11).

## Deploy wave (NEXT — OWNER-GATED, env-mutating)
- **086 Deploy NavigatorPane + wire bootstrap** — Vite build → deploy webresource(s) to spaarkedev1; reuse the LIVE dormant `sprk.Global.SidePaneManager` ribbon hook (recreate `sprk_SidePaneManager` WR — likely NO ribbon import; see notes/task-001-spike-report.md). ALSO finalize 021 security roles (owner picks target end-user role for owner-scoped sprk_navitem CRUD) + 2-user isolation test. deps: all 040-080 ✅.
- **087 UI-test pass** (ui-test skill, light+dark). **090 wrap** (/test-diet + lessons-learned + archive).
- ⏳ Still-open 052 "assigned to me" A/B decision (owner) — non-blocking; 052 shipped owner-only.
- Deploy: 086 (deploy NavigatorPane + wire bootstrap — reuse the LIVE sprk_SidePaneManager ribbon hook) → 087 UI-test. Wrap: 090.
- ⏸ **021 follow-up** (non-blocking): owner-scoped end-user role wiring + 2-user isolation test — operator picks target role; System Admin has access now. See notes/task-021-deploy-result.md.

## sprk_navitem option-set integers (for capture repo, task 030)
- sprk_type: 100000000=History, 100000001=Pin · sprk_source: 100000000=Captured, 100000001=Manual · sprk_pagetype: 100000000=entityrecord, 100000001=entitylist, 100000002=custom, 100000003=weblink

## Key carry-forward (from spike 001 + Wave A)
- Capture (030): **never cache Xrm — re-read `window.top.Xrm` each poll**; primary signal = top-window URL (`pagetype/etn/id`), getPageContext as cross-check; poll 1.5s; WebApi `retrieveMultipleRecords`, `_modifiedby_value eq {userId}` for FR-03 Edited.
- 010 widened `xrmContext.ts`: `SidePanesApi.getPane`, `SidePane.select()`, `PageInput.webresourceName`, `data: string|Record`, 3-frame `getXrm()` (never throws). 28/28 tests, tsc clean.
- 020: `sprk_navitem` UserOwned; `sprk_targetid` = String(100) per `sprk_todo.sprk_regardingrecordid` precedent (revisit at 030). Deploy script idempotent, option-sets-before-picklists.
- **086 (auto-launch) de-risked**: spaarkedev1 app ribbon STILL has the dormant `sprk.Global.SidePaneManager` command+enable-rule → `Spaarke.SidePaneManager.initialize`/`$webresource:sprk_SidePaneManager`. Recreate that WR to reactivate — likely NO ribbon import. See `notes/task-001-spike-report.md`.

## Next waves
- After 011 (+021): **Wave C** = 030 (capture) ‖ 040 (NavigatorPane code page). Then D (tabs/pins/views), E, F, deploy, wrap.

## Context recovery pointers
- [`plan.md`](plan.md) · [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) · [`CLAUDE.md`](CLAUDE.md) · [`spec.md`](spec.md) · spike harness in `spike/` (deployed to spaarkedev1)

## Decisions log
- 2026-08-12 Path B (owner). 2026-08-13 OQ-9 resolved; SpaarkeAi widget deferred (owner). Task 001 GO. Spike harness = no-build HTML (ADR-006 spike exception §6.5-A). Wave A (010,020) done via parallel sonnet subagents.
