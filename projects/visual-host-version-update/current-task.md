# Current Task

**Active task**: VHVU-042 — Reconcile drifted duplication (one `VisualType`, one `EventDueDateCard`)
**Status**: not-started
**Phase**: B2
**Next action**: Begin VHVU-042. Recommend fresh context (this session ran 030 + 040 + 041 — big).

### Quick Recovery
| Field | Value |
|---|---|
| Done | Phase A (001–030) + B1 (040 scaffold) + **B2 move (041)** — all committed + build-verified |
| Next | **VHVU-042** reconcile dup → 050 (xhigh self-fetch refactor) → 060 (repoint+verify) → 070 (ADR-012 amend) |
| Gates awaiting owner | 004 (opt deploy), 021 + 031 + 061 (UAT — need deploy + live env) |

### VHVU-041 outcome (2026-07-10) — COMPLETE + VERIFIED
- Moved 13 presentational components + 7 utils + all viz types into `@spaarke/visuals` (git renames). `TrendDirection` inverted into the types barrel (TrendCard re-exports; trendAnalysis imports from `../types`).
- **Directional deviation**: ChartRenderer + the 3 self-fetch visuals (CalendarVisual, DueDateCard, DueDateCardList) STAY in the PCF — ChartRenderer is a host dispatcher consuming webAPI + the trio; moving it would breach NFR-05. Moves in 050/060 after the self-fetch visuals go props-in.
- VisualHost repointed via relative src paths (safe: package is presentational-only, zero internal deps). 3 pervasive utils (logger/cardConfigResolver/trendAnalysis) kept as thin re-export shims at old PCF paths — VHVU-060 finishes the repoint. `@spaarke/visuals` declared as `file:` dep.
- **Verified**: package `tsc --noEmit` green + zero host-coupling; VisualHost `build:prod` green, bundle **761 KiB**, leak-free (PublicClientApplication/SdapClient=0), cleanGuid intact, footer v1.4.36. **ZERO new skew-casts** on the 13 moved components (the @types/react@18 pin worked).

### KEY DECISION — @types/react@18 pin on @spaarke/visuals (VHVU-040 fix, commit 41f64bb9b)
React 18's `ReactNode` ⊂ React 19's, so an @18-typed component is consumable by BOTH the R18 PCF and future R19 code pages with NO TS2786 JSX skew. Typing @19 would reproduce the AiSummaryPopover cast problem ×13. This is why the extraction needed zero casts. **Keep @spaarke/visuals at @types/react@18.**

### VHVU-042 starting notes (next task)
- Reconcile the two drifts the design flagged:
  1. **`VisualType`** — canonical now in `@spaarke/visuals/src/types`. Check `src/client/pcf/VisualHost/control/types/index.ts` (is it a re-export shim or still a full duplicate?) AND any `VisualType` copy in `@spaarke/ui-components`. Collapse to ONE canonical (the @spaarke/visuals one) with re-export shims where needed.
  2. **`EventDueDateCard`** — moved to @spaarke/visuals in 041; a duplicate reportedly also lives in `@spaarke/ui-components`. Confirm + collapse to the @spaarke/visuals one.
- Grep both shared packages + the PCF for the duplicate symbols; verify `build:prod` + both packages' `tsc` stay green after collapsing.

### KNOWN FOLLOW-UP (VHVU-090 / test-diet)
6 moved-component test files (BarChart/DonutChart/LineChart/MetricCard/MiniTable/StatusDistributionBar `.test.tsx`) were repointed to the package path but have a **pre-existing** `@testing-library/react` TS2305 (no `screen`/`fireEvent` export) affecting moved + stayed tests alike. Re-home them into @spaarke/visuals with its own jest harness at test-diet.

## Progress
- [x] Phase A (001–030) committed
- [x] B1 040 scaffold + @18 fix
- [x] B2 041 move committed (c1ec5f8a7)
- [ ] 042 reconcile → 050 (xhigh) → 060 repoint → 070 ADR-012 amend (.claude/ main-session-only)
- [ ] Deploy/UAT (031/061) — owner-gated
- [ ] 090 wrap-up (+ test-diet)

## Notes
- Deploy/UAT tasks are outward-facing → owner go + live env.
- VHVU-070 (ADR-012 amendment) is `.claude/` main-session-only — never a sub-agent.
- Session commits: fda8c31e3 (030), 77899bac1 + 41f64bb9b (040), c1ec5f8a7 (041). Branch ahead of origin/master.
