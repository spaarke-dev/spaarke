# Test diet report — spaarke-side-pane-navigation-history-r1

**Run date**: 2026-08-15
**Branch**: work/side-pane-navigation-history-r1
**Scope**: Jest test files added/modified during the project (frontend — NavigatorPane solution + shared-lib navigator services)

> **Framework note**: this project's tests are **Jest/React (frontend)**, not C#/xUnit. ADR-038 §7's 17-ban classifier and its 6 KEEP-path taxonomy (`tests/integration/**`, `tests/unit/domain/**`) are C#-server-oriented. The *principle* (behavior-over-scaffolding; no wiring/DI-registration/ctor-null/mock-only/mirror tests; name-with-scenario) is applied here; the C# **path** rule is N/A — co-located `src/**/__tests__/*.test.ts(x)` beside source is the correct, standard Jest/React convention and is NOT a path violation.

## Summary

| Class | Count (files) | Action |
|---|---|---|
| MAINTAIN (behavioral, keep in place) | 17 | confirmed |
| SCAFFOLDING (DELETE candidate) | 0 | — |
| AMBIGUOUS (reviewer judgment) | 1 (soft note) | see below |
| PATH-VIOLATION | 0 | co-located Jest is correct |
| **Total test files touched** | **17** (173 test methods) | — |

## Delete commands

_None._ No test file matched a B1–B17 scaffolding pattern. These are behavioral tests: URL parsing (`urlParse`), navigation routing (`recordNavigation`, `QuickSwitcher`), security classification (`securityTrimService` — 403/404 vs transient), capture poll semantics (`navigatorCaptureService`), pin/rename/retention CRUD behavior, render states + gestures (tab tests), and registrar idempotency (`ensureNavigatorSidePane`). None are DI-registration, constructor-null, mock-only, mirror, or coverage-filler tests.

## Path-move commands

_None._ Co-located Jest tests beside their source are the correct frontend convention (not the C# `tests/**` taxonomy).

## Ambiguous — reviewer judgment

| File | Ambiguity | Suggestion |
|---|---|---|
| `src/solutions/NavigatorPane/src/services/__tests__/editedByMeService.test.ts` (10 tests) | The UAT redesign removed the Recent→Edited toggle, so the tested function `listEditedByMe` is no longer wired to any UI. HOWEVER the module is **not** dead: `liveSearchService.ts` imports its `CORE_ENTITY_SET` export. So the file cannot be deleted wholesale, and the tests still exercise working, exported behavior. | **KEEP as-is** for close-out (no churn). IF the team later decides to fully retire the Edited derivation: extract `CORE_ENTITY_SET` to a shared constants module, delete `listEditedByMe` + `editedByMeService.test.ts`, and repoint `liveSearchService`. Not required for this project. |

## Maintain — confirmed (no action)

| File | Why maintain |
|---|---|
| `services/navigator/__tests__/navigatorCaptureService.test.ts` | Capture poll behavior: page-change detection, dedupe/bump upsert, prune-on-write, never-cache-Xrm (task-001 spike lesson). Regression-critical. |
| `utils/__tests__/ensureNavigatorSidePane.test.ts` | Registrar idempotency + retry-backoff + never-throws contract (real behavior, not DI wiring). |
| `NavigatorPane/__tests__/NavigatorBody.test.tsx` | Tab switching + capture-start-on-mount (the bug this closes). |
| `components/__tests__/QuickSwitcher.test.tsx` | Local fuzzy match, live escalation, keyboard accelerator, navigation. |
| `services/__tests__/bookmarkService.test.ts` | Pin-this-page + Add-bookmark URL parse/dedupe/weblink + friendly errors. |
| `services/__tests__/monitoredService.test.ts` | Access-based scope (platform security trim), monitor-flag filter, read-only, per-entity failure isolation. |
| `services/__tests__/navItemRepository.rename.test.ts` | Inline rename (`UPDATE sprk_displayname`) behavior. |
| `services/__tests__/pinService.test.ts` | Pin/unpin/dedupe; never writes `sprk_monitor` (project HARD MUST-NOT). |
| `services/__tests__/recordNavigation.test.ts` | sprk_communication → Email code-page routing contract. |
| `services/__tests__/retention.verify.test.ts` | 30-day prune verification (seed old row → capture → confirm deletion). |
| `services/__tests__/securityTrimService.test.ts` | 403/404 (drop) vs transient (retain) classification — security-critical (FR-12/NFR-04). |
| `services/__tests__/urlParse.test.ts` | Pure MDA-URL-shape parser, many-case table. |
| `services/__tests__/editedByMeService.test.ts` | (see AMBIGUOUS) behavioral `modifiedby=me` derivation; module still exports live `CORE_ENTITY_SET`. |
| `tabs/__tests__/BookmarksTab.test.tsx` | Render, inline rename, pin gestures, generic-name enrichment. |
| `tabs/__tests__/MonitoredTab.test.tsx` | Render, read-only lens, access-based rows. |
| `tabs/__tests__/RecentTab.test.tsx` | Render, star, navigate, read-time trim. |
| `tabs/__tests__/ViewsTab.test.tsx` | View grouping by entity + click-to-open. |

## Count delta

- Test files touched during project: **17**
- MAINTAIN: **17** · SCAFFOLDING: **0** · AMBIGUOUS: **1 (soft — keep)** · PATH-VIOLATION: **0**
- Net post-diet expected count: **173 test methods, unchanged** (no reviewer-confirmed deletes required).

## Verdict

Clean diet — the project's test suite is behavioral throughout; nothing to delete or move. One soft note (`editedByMeService`) documents a future retirement path if the Edited derivation is ever fully removed. No `git rm` / `git mv` required.

## Industry citation

Build-vs-maintain criteria per ADR-038 §7 (Beck "delete the scaffolding"; Feathers characterization-vs-behavior; Google test-sizes; DHH less-tests). 17-ban classifier B1–B17 applied by principle to the Jest/React suite.
