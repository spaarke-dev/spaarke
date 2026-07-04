# Plan Extension — Phase 6 (DEF absorption)

> **Added**: 2026-07-04, after 10 rounds of live QA (v1.0.0 → v1.0.11)
> **Rationale**: User "reasonable scope in R1" directive — DEFs 02, 06, 07, 09, 10, 11 fold into R1 rather than becoming R2.
> **Remaining DEFs (01, 03, 04, 05, 08)**: constrained out of R1 by CLAUDE.md (§NFR-07 BFF prohibition, §"MUST NOT modify VisualHost/EventDetailSidePane", §Scope OS-02 per-entity separation). Filed as GitHub Issues via `/project-defer-issue-tracking` during task 090 wrap-up; scoped as R2 projects post-merge.

---

## Phase 6 tasks

| ID | DEF | Title | Estimate | Scope note |
|---|---|---|---|---|
| 060 | DEF-07 | PCF build-scaffold pattern doc | 2–3 h | Author `.claude/patterns/pcf/pcf-build-scaffold.md` capturing the 5 build gotchas this project uncovered (control/ subfolder placement, contextInfo type-cast idiom, tsconfig.json copy from SemanticSearchControl, ajv v8 devDep hoist, featureconfig.json + webpack.config.js + deep-path imports triad). Memory decay risk — do first while fresh. |
| 061 | DEF-02 | Matter form binding maker instructions | 30 min | Write `notes/matter-form-binding-instructions.md` — step-by-step for adding `MatterHeaderPcf` to Matter form's header section via the maker portal. Cannot code this (needs Dataverse form XML update via maker access); can only author the checklist. |
| 062 | DEF-09 | Dark-mode theme support | 4–6 h | Port `VisualHost/control/providers/ThemeProvider.ts` (~152 LOC) into `src/client/pcf/MatterHeader/control/providers/`. Wire into `MatterHeaderHost.tsx` replacing `webLightTheme` unconditional with `resolveTheme(context)` + listener. Existing FluentProvider wrap remains; theme prop becomes dynamic. |
| ~~063~~ | ~~DEF-06~~ | ~~`exports` field on `@spaarke/ui-components/package.json`~~ | ~~4–8 h~~ | **Reverted 2026-07-04 during Phase 6 execution.** Attempted `exports` map with `./*` wildcard fallback. Webpack (which reads `exports`) mishandled directory-index resolution (`./dist/hooks` → `./dist/hooks.js` instead of `./dist/hooks/index.js`). Enumerating every downstream subpath is beyond R1 scope. Real fix requires `moduleResolution: "bundler"` in `pcf-scripts/tsconfig_base.json` (which ripples across every PCF in the repo) plus a fully-enumerated `exports` map — proper R2 project. Filed as DEF-06 reforward in R2B scope. |
| 064 | DEF-10 | Notepad SPA bundle-size perf | 1–2 d | Current: 1.17 MB single-file HTML (330 KB gzipped). Target: ≤300 KB uncompressed / ≤100 KB gzipped for the modal-open critical path. Approach: audit dependency graph with `rollup-plugin-visualizer`, drop unused Fluent v9 imports, chunk-split, lazy-load CreatedByPopover, verify `vite-plugin-singlefile` still produces one deployable HTML. |
| 065 | DEF-11 | Checkmark → `sprk_todospage` DataGrid Code Page | 2–3 d | Build a new Vite Code Page hosting `<DataGridPageShell configId="…" useUrlParentContext={{key:"matterId"}} />` per [DataGrid Code-Page Host Contract](../../docs/guides/DATAGRID-CODE-PAGE-HOST-CONTRACT.md). Author a `sprk_gridconfiguration` record for `sprk_todo` with `behavior.parentContextFilter.attribute = "sprk_regardingmatter"` + `parentContextKey = "matterId"`. Rewire `handleCheckmarkClick` in `useRecordHeaderToolbarActions` to launch `sprk_todospage` with `?data=matterId=<recordId>`. Requires Deploy-TodosPage.ps1 deploy script. |

**Phase 6 total: ~5–8 dev-days.**

---

## Execution order (dependency-aware)

```
DEF-07 (docs, 2-3h)         ─── independent ───┐
DEF-02 (docs, 30m)          ─── independent ───┤
                                                ├──► task 090 wrap-up ──► merge R1
DEF-09 (dark mode, 4-6h)    ─── independent ───┤
                                                │
DEF-06 (exports, 4-8h)      ── enables ──► DEF-10 + DEF-11 use clean imports
                                                │
DEF-10 (Notepad perf, 1-2d) ─── independent ───┤
                                                │
DEF-11 (DataGrid todos, 2-3d) ─── independent ─┘
```

Recommended sequence:
1. DEF-07 first — docs-only, no code risk, captures fresh knowledge
2. DEF-02 immediately after (30 min doc)
3. DEF-09 dark mode — small, verified by rebuild
4. DEF-06 exports — do BEFORE 10 + 11 so their new imports use clean paths
5. DEF-10 Notepad perf
6. DEF-11 DataGrid todos (biggest — proper feature)
7. Task 090 wrap-up
8. Merge R1 to master

---

## What is NOT in Phase 6 (deferred to R2 + follow-ons)

The remaining DEFs are structurally forbidden by R1 CLAUDE.md — not preference-driven:

| DEF | Constraint | Follow-on target |
|---|---|---|
| DEF-01 Sparkle refresh → BFF regen endpoint | R1 NFR-07 forbids BFF endpoints | `ai-record-summary-regeneration-r1` (BFF + AI service work) |
| DEF-03 VisualHost CardChrome migration | R1 CLAUDE.md "MUST NOT modify VisualHost/**" | R2B `record-header-shared-consolidation-r2` |
| DEF-04 EventDetailSidePane MemoSection adoption | R1 CLAUDE.md "MUST NOT modify EventDetailSidePane/**" | R2B `record-header-shared-consolidation-r2` |
| DEF-05 Per-entity PCFs (Project, Invoice, Event) | R1 §Scope OS-02 — each is own ~80 LOC project | 3 independent follow-ons: `project-header-r1`, `invoice-header-r1`, `event-header-r1` |
| DEF-08 Promote `useSprkMemoRepository` to shared lib | Blocked by DEF-04 (the adopter is out of R1) | R2B `record-header-shared-consolidation-r2` (after DEF-04) |

---

## Impact on R1 shipping timeline

- Original R1: 88–116 h (~11–15 dev-days)
- Phase 6 extension: +40–64 h (~5–8 dev-days)
- **Revised R1 total: ~16–23 dev-days**

Merge to master delayed by ~1 week vs. the "ship clean now" alternative — accepted per user "reasonable scope" directive.
