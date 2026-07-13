# Current Task

**Active task**: VHVU-090 — Project wrap-up (test-diet + relocation DONE; lessons-learned optional)
**Status**: test-diet + test relocation complete; awaiting push/merge of this follow-up
**Phase**: — (wrap-up)
**Next action**: Push this follow-up branch → new PR → merge. (VHVU-061 UAT: owner confirmed v1.4.37 "looks good".)

### VHVU-090 test-diet + relocation (2026-07-13) — DONE
- **`/code-review`**: CLEAN (quality improved; ADR-012/021/028 compliant; 2 pre-existing suggestions — ADR-044 hand-rolled GUID, one 607-line presentational file).
- **`/test-diet`**: report at [`notes/test-diet-report.md`](notes/test-diet-report.md). 0 scaffolding to delete; 0 ambiguous. One finding: 6 chart-component tests were PATH-VIOLATIONs (tested `@spaarke/visuals` components but lived in the PCF).
- **Relocation executed**: bootstrapped a Jest harness in `@spaarke/visuals` (`jest.config.cjs` + `babel.config.cjs` + `jest.setup.ts` + `tsconfig.jest.json`; `.cjs` because the package is `"type": "module"`; added test devDeps incl. `@testing-library/dom` + `scheduler`; `tsconfig.json` excludes tests from the prod `tsc` build). `git mv`'d the 6 tests to `src/components/__tests__/`, fixed imports (`../X` + `../../types`).
- **Verified**: `@spaarke/visuals` **71 tests / 6 suites** pass; VisualHost **60 tests / 3 suites** (CalendarVisual container + 2 services) pass; `@spaarke/visuals` `tsc --noEmit` green. Same 131-test coverage, correctly homed. VisualHost bundle unchanged (no runtime source touched) → no redeploy.
- VisualHost jest `moduleNameMapper` React singletons **retained** — still load-bearing for the `CalendarVisual` container test's cross-package import of the pure component.

### Session status (2026-07-12) — B3/B4/B5 COMPLETE + committed
| Task | State | Commit |
|---|---|---|
| VHVU-050 self-fetch → props-in + ViewDataService split | ✅ | c8f2d159a |
| Test-harness repair (was 0 runnable → 131 pass) | ✅ | 0947d2987 |
| VHVU-060 repoint to @spaarke/visuals + close 2 shims | ✅ | 83b11a4ba |
| VHVU-061 bump v1.4.37 + build + pack | ✅ packed | e676f458e |
| VHVU-070 ADR-012 amendment (concise + full) | ✅ | 738b4a1b1 |

### VHVU-061 — DEPLOY HANDOFF (owner-driven)
- **Packed ZIP**: `src/client/pcf/VisualHost/Solution/bin/VisualHostSolution_v1.4.37.zip` (owner uploads/imports).
- Followed `/pcf-deploy`: 5-location version bump verified; prebuild:prod (ensure-dist-fresh) ran; fresh `build:prod` (762 KiB); built ControlManifest.xml == solution copy; bundle+styles copied; packed via pack.ps1; ZIP verified to contain v1.4.37; no description-key apostrophes. Target env = **SPAARKE DEV 1**.
- Custom-page host: if control shows old version after import → Maker republish (File→Save→Publish) + hard refresh.
- **UAT plan** (behavior-preserving → everything must match v1.4.36): Gate 0 footer v1.4.37; Gate 1 all 12 visual types render; Gate 2 the 3 refactored visuals (Calendar day-detail popover + Copy; DueDateCard date/color; DueDateCardList → event modal 80%×80%); Gate 3 dark mode (ADR-021 tokens); Gate 4 drill-through + CardChrome expand + AI sparkle.
- **Owner sign-off pending** → then 061 ✅.

### Test-harness fix (durable — the "CI issue" the owner flagged)
Root causes (both pre-existing, masked): (1) `@testing-library/react` v16 needs peer `@testing-library/dom` (never installed → TS2305 screen/fireEvent); (2) `@fluentui/react-context-selector` needs `scheduler` (not installed). Fix: installed both + added React/react-dom/scheduler singleton `moduleNameMapper` (pure visuals import @fluentui/React from the sibling package → force single copies, avoid Invalid-hook-call). Result **9/9 suites, 131 tests**. Relocating the moved-component tests into @spaarke/visuals' OWN harness is the VHVU-090 test-diet item.

### KNOWN FOLLOW-UPS for VHVU-090
- **test-diet**: relocate the 6 moved-component tests (BarChart/Donut/Line/MetricCard/MiniTable/StatusDistributionBar) + add pure Calendar/DueDate tests into a jest harness inside @spaarke/visuals (currently they run cross-package from VisualHost).
- **ADR-044 (pre-existing)**: DueDateCard/List containers hand-roll `.replace(/[{}]/g,'')` on GUIDs in FetchXML (relocated verbatim in 050); candidate for `cleanGuid` adoption, out of scope so far.
- **logger facade**: `control/utils/logger.ts` intentionally retained (documented) — NOT a pending shim.

### Guardrails (unchanged)
- Keep @spaarke/visuals at **@types/react@18** (the cast-free pin).
- @spaarke/visuals is presentational-only (no Xrm/WebAPI/FetchXML) — now ADR-sanctioned (ADR-012 amendment, VHVU-070).
- Branch ahead of origin/master; behavior-preserving refactor — v1.4.36 stays live until owner imports v1.4.37.

## Progress
- [x] Phase A (001–031 deployed)
- [x] B1 040 · B2 041/042 · **B3 050** · **B4 060** · **B5 070**
- [x] Test harness repaired (131 tests green)
- [ ] **VHVU-061 owner import + UAT sign-off** (packed, handed off)
- [ ] VHVU-090 wrap-up (+ /test-diet) — after 061

## Notes
- VHVU-070 was `.claude/` main-session-only — done in main session (no sub-agent).
- Deploy/UAT are owner-gated outward-facing steps.
