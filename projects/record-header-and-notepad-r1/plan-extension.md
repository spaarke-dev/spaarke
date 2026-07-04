# Plan Extension — Phase 6 (DEF absorption)

> **Added**: 2026-07-04, after 10 rounds of live QA (v1.0.0 → v1.0.11)
> **Status**: ✅ COMPLETE (2026-07-04) — 5 of 6 DEFs shipped; DEF-06 attempted and reverted (blocked by pcf-scripts moduleResolution). DEF-11 grew to 3 parts once QA revealed the R4 FR-34 consumer was unwired.
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
| ~~065~~ | DEF-11 | ~~Checkmark → `sprk_todospage` DataGrid Code Page~~ | ~~2–3 d~~ | **PIVOTED 2026-07-04 during Phase 6 execution** — user preferred "open the Smart To Do (as it is now) but filtered by the matter" over a new DataGrid page. Full interactive Kanban beats a read-only grid; extends existing surface instead of introducing a new one (CLAUDE.md §11). Delivered as three sub-parts: |
| 065.1 | DEF-11 Part 1 | Launch payload rewire | 30 min ✅ | `handleCheckmarkClick` in `@spaarke/ui-components/hooks/useRecordHeaderToolbarActions.ts` emits `data: "action=openTodos&regardingType=<entity>&regardingId=<id>"` — reuses the R4 FR-34 `openTodos` launch contract that shipped in `SmartTodo/src/hooks/useLaunchContext.ts`. Test assertions updated (fixed pre-existing `pageInput.name` → `pageInput.webresourceName` bug + new payload shape). MatterHeader PCF bumped to v1.0.12 covering DEF-09 + DEF-11 Part 1. |
| 065.2 | DEF-11 Part 2 | SmartTodo consumer wiring | 2 h ✅ | **Live QA surprise**: v1.0.12 launched correctly but Kanban rendered unfiltered. Investigation: SmartTodo's `useLaunchContext.ts` PARSES the openTodos contract fully but NOTHING consumed the result — R4 shipped half the contract. Stale "R4 task 030" pointer comment in `SmartTodoApp.tsx:512` misled us into thinking it was wired. Fix: threaded `launchContext.regardingFilter` from `SmartToDo.tsx` → `useTodoItems` → `DataverseService.getActiveTodos` → `buildTodoItemsQuery`. Added `_sprk_regarding<X>_value eq <guid>` OData clause (AND'd with ownership + status). |
| 065.3 | DEF-11 Part 3 | Kanban Filter enhancement | 30 min ✅ | Extended Kanban's substring filter to also match `sprk_regardingrecordname` + `sprk_regardingrecordnumber` (ADR-024 resolver text fields). Renamed placeholder "Search…" → "Filter…" so label matches actual client-side substring-inclusion behavior. Users can now type a matter name/number in the Filter input and see just that matter's todos — without needing to drill from the Matter form. |

**Phase 6 total: ~1 dev-day actual (much less than 5–8 d estimate).** The DEF-11 pivot away from a new DataGrid page + reuse of SmartTodo was the biggest scope reduction; the biggest surprise was the R4 FR-34 half-shipped contract adding Part 2.

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
