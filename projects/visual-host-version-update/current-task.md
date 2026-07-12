# Current Task

**Active task**: VHVU-060 — Repoint VisualHost fully to @spaarke/visuals; move ChartRenderer + close util shims; verify build/bundle/drill
**Status**: not-started
**Phase**: B4
**Next action**: Begin VHVU-060. VHVU-050 (self-fetch → props-in refactor) is COMPLETE + build-verified. 060 can now move ChartRenderer + the 3 self-fetch containers' *presentational* halves are already in @spaarke/visuals; 060 finishes the repoint (close the 3 util shims logger/cardConfigResolver/trendAnalysis, move ChartRenderer if NFR-05 allows).

### VHVU-050 outcome (2026-07-12) — COMPLETE + VERIFIED
- **Inverted data flow** for the 3 self-fetch visuals. Each is now a pure presentational component in `@spaarke/visuals` + a thin PCF-side data-fetching container (existing file names kept, so `ChartRenderer` imports are unchanged):
  - `@spaarke/visuals/src/components/CalendarVisual.tsx` (pure; `events` + `detailedEvents` + `fetchError` props) ← container `control/components/CalendarVisual.tsx` (owns fetch + `mapRecordToEvent`, re-exports `ICalendarEvent`).
  - `@spaarke/visuals/src/components/DueDateCard.tsx` (pure; `cardProps` + `loading`/`error` props) ← container `control/components/DueDateCard.tsx` (exports `DueDateCardVisual`).
  - `@spaarke/visuals/src/components/DueDateCardList.tsx` (pure; `cards` + `loading`/`error` + `navigatingId` props) ← container `control/components/DueDateCardList.tsx` (exports `DueDateCardListVisual`; owns `window.Xrm` nav).
- **ViewDataService split** (pure vs executor): pure FetchXML-string helpers → NEW `control/services/fetchXmlBuilders.ts` (`injectContextFilter`, `injectRequiredAttributes`, `applyMaxItems`, `substituteParameters`, `ISubstitutionParams`); executors stay in `ViewDataService.ts`, which **re-exports** the pure helpers so `DataAggregationService` + the containers keep their `from './ViewDataService'` import paths.
- **DIRECTIONAL DEVIATION from POML step 1** (noted per §8.5): POML said move pure FetchXML helpers *into @spaarke/visuals*. Kept them **PCF-side** in `fetchXmlBuilders.ts` instead — the project CLAUDE.md non-negotiable is "@spaarke/visuals is presentational only — no FetchXML", and after the refactor NOTHING in @spaarke/visuals consumes them (only the PCF containers do). Split is still "pure (fetchXmlBuilders) vs executor (ViewDataService)"; goal + acceptance-criteria satisfied without breaching the binding non-negotiable.
- **Verified**: `@spaarke/visuals` `tsc --noEmit` green; the 3 moved visuals grep-clean of webApi/Xrm/FetchXML/ComponentFramework (NFR-05); VisualHost `build:prod` green, bundle **762 KiB**, leak-free (PublicClientApplication=0, SdapClient=0), cleanGuid intact (`trim().toLowerCase()`=1), footer v1.4.36=1; service tests **48/48 pass** (ConfigurationLoader + DataAggregationService — proves the ViewDataService re-export chain).
- **Behavior-preserving** (owner-confirmed constraint): changed WHERE the fetch happens, not WHAT renders. Re-UAT the Calendar day-detail popover + DueDate cards at VHVU-061.

### KNOWN FOLLOW-UPS
- **Test harness (VHVU-090 / test-diet)**: the VisualHost component test suite is blocked by a **pre-existing** `@testing-library/react` TS2305 (`screen`/`fireEvent` not exported) that stops ALL component tests (moved + stayed) before they run — unrelated to this refactor. Pure-visual tests (Calendar/DueDate props-in states) belong in @spaarke/visuals' own jest harness at 090, same bucket as the 6 already-moved component tests. Existing `control/components/__tests__/CalendarVisual.test.tsx` still targets the container (re-exports) and will pass once the harness is fixed.
- **ADR-044 (pre-existing observation)**: the DueDateCard/DueDateCardList containers still hand-roll `.replace(/[{}]/g,'')` on GUIDs inside FetchXML building (relocated verbatim, not new). Behavior-preserving kept it as-is; candidate for a future `cleanGuid` adoption, out of VHVU-050 scope.

### Deployed state (stable — no redeploy for 050)
- **v1.4.36 is live on SPAARKE DEV 1**, UAT PASSED. VHVU-050 is a source refactor with a byte-similar bundle (762 vs 761 KiB); the 061 deploy carries it to dev when owner-gated.

### Quick Recovery
| Field | Value |
|---|---|
| Done | Phase A (001–031 deployed) + B1 (040) + B2 (041, 042) + **B3 (050 self-fetch refactor)** — all committed + build-verified |
| Next | **VHVU-060** repoint + verify → 070 (ADR-012 amend, .claude/ main-session-only) → 061 (deploy+UAT) → 090 (wrap-up + test-diet) |
| Gates awaiting owner | 004 (opt deploy), 021 + 031 + 061 (UAT) |

## Progress
- [x] Phase A (001–031)
- [x] B1 040 scaffold + @18 fix
- [x] B2 041 move + 042 reconcile
- [x] **B3 050 self-fetch → props-in refactor + ViewDataService split**
- [ ] 060 repoint (move ChartRenderer, close 3 util shims) → 070 ADR-012 amend → 061 deploy/UAT → 090 wrap-up (+ test-diet)

## Notes
- Deploy/UAT tasks are outward-facing → owner go + live env.
- VHVU-070 (ADR-012 amendment) is `.claude/` main-session-only — never a sub-agent.
- **Keep @spaarke/visuals at @types/react@18** (the pin that made the extraction cast-free).
