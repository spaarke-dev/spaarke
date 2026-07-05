# Current Task State — record-header-and-notepad-r1

> **Last Updated**: 2026-07-04 22:05 (post v1.0.18 UAT sign-off, entering wrap-up)
> **Recovery**: Read "Quick Recovery" section first.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | 090 — R1 Wrap-Up (in progress) |
| **Phase** | 5 — Wrap-up |
| **Progress** | Docs updated ✅ · pattern doc shipped ✅ · UAT rounds 11–18 sign-off ✅. Remaining: `/code-review` → `/test-diet` → `/merge-to-master`. |
| **Status** | in-progress |
| **Next Action** | Invoke `/code-review` on the Phase 6 scope. Then `/test-diet` (BINDING per CLAUDE.md §7). Then `/merge-to-master`. |

### Live-QA versions shipped and accepted

| Version | Change | User acceptance |
|---|---|---|
| v1.0.11 | Phase 5 close-out (10 QA rounds) | ✅ 2026-07-03 |
| v1.0.12 | Phase 6 shipping build (DEF-09 dark mode + DEF-11 Part 1 checkmark→SmartTodo openTodos) | ✅ 2026-07-04 |
| v1.0.13 | UAT round 11 attempt: bump CounterBadge size=medium + remove Notepad "i" popover | 🟡 popover fix accepted; badge fix ineffective (see v1.0.16) |
| v1.0.14 | UAT round 11b: reanchor badge inside button corner to prevent clipping | 🟡 accepted for structure; still no visible badges (root cause below) |
| v1.0.15 | Diagnostic build: console.info logs the badge count state on every render | ✅ produced the diagnostic that surfaced the root cause |
| **v1.0.16** | **ROOT CAUSE fix**: `Xrm.WebApi` does NOT expose `@odata.count` — was reading a missing property. Rewrote `useRelatedCount` to count `entities.length` client-side with `$top=100` cap. Test mocks fixed (were fabricating `@odata.count`). | ✅ counts started rendering |
| v1.0.17 | Badge back to size="small" at upper-right corner (v1.0.13's "medium" swallowed the icon) | ✅ badges look right at counter dimensions |
| **v1.0.18** | Toolbar `iconSlots`: `gap: XS→S` (4px between icons) + `paddingInlineEnd: M` (8px inset from trailing edge) | ✅ 2026-07-04 final sign-off |

### Deployables (all in place)

- **MatterHeader PCF**: `src/client/pcf/MatterHeader/Solution/bin/MatterHeaderPcf_v1.0.18.0.zip` (v1.0.18)
- **Notepad**: `src/solutions/Notepad/dist/notepad.html` (442 KB, ships DEF-10 + Notepad "i" popover removal from v1.0.13)
- **SmartTodo**: `src/solutions/SmartTodo/dist/smarttodo.html` (1.76 MB, ships DEF-11 Part 2 openTodos consumer + Part 3 Filter enhancement)

### Phase 6 delivery (all shipped)

- ✅ DEF-07 pattern doc (`.claude/patterns/pcf/pcf-build-scaffold.md`)
- ✅ DEF-02 maker checklist (`notes/matter-form-binding-instructions.md`)
- ✅ DEF-09 dark-mode support (MatterHeaderHost via shared `themeStorage`)
- ⏸️ DEF-06 exports field — attempted, reverted, filed as R2B (pcf-scripts moduleResolution ripples)
- ✅ DEF-10 Notepad bundle 1.17 MB → 442 KB (62% reduction; 478 KB → 442 KB after CreatedByPopover removed)
- ✅ DEF-11 checkmark → SmartTodo openTodos filter — **3 parts total**:
  - Part 1: launch payload rewire (`useRecordHeaderToolbarActions` emits `action=openTodos&regardingType=X&regardingId=Y`)
  - Part 2: SmartTodo consumer wiring — R4 FR-34 shipped the parser but never the consumer; wired `launchContext.regardingFilter` through `TodoProvider` → `useTodoItems` → `DataverseService.getActiveTodos` → `buildTodoItemsQuery` OData clause
  - Part 3: Kanban Filter enhancement — search now matches `sprk_regardingrecordname` + `sprk_regardingrecordnumber` in addition to name/description; placeholder renamed `Search…` → `Filter…`

### UAT extension delivered (also shipped)

- ✅ Notepad "i" info popover removed from `NotepadShell.tsx` (metadata was redundant with MemoList card display)
- ✅ Toolbar CounterBadge rendering fixed (v1.0.16 real root cause — see live-QA table above)
- ✅ Badge sizing + placement + gap refinements (v1.0.17 + v1.0.18)
- ✅ **NEW pattern doc**: `.claude/patterns/pcf/xrm-webapi-related-count.md` — captures the `@odata.count` gotcha + badge sizing rules so future PCFs don't rediscover the trap. Cross-referenced from INDEX, `dataverse-queries.md`, and `HeaderToolbar/README.md`.

### Files touched during Phase 6 + wrap-up

Aggregated modification list (Phases 1–5 are in prior commits `1fd4eebbd` and earlier):

- `.claude/patterns/pcf/pcf-build-scaffold.md` (NEW, DEF-07)
- `.claude/patterns/pcf/xrm-webapi-related-count.md` (NEW, wrap-up)
- `.claude/patterns/pcf/INDEX.md` (2× new rows)
- `.claude/patterns/pcf/dataverse-queries.md` (+ `@odata.count` warning)
- `projects/record-header-and-notepad-r1/README.md` (status → Complete + graduation checklist)
- `projects/record-header-and-notepad-r1/plan.md` (status → Complete)
- `projects/record-header-and-notepad-r1/plan-extension.md` (Phase 6 row updates)
- `projects/record-header-and-notepad-r1/tasks/TASK-INDEX.md` (all Phase 6 rows ✅ except DEF-06 ⏸️)
- `projects/record-header-and-notepad-r1/notes/lessons-learned.md` (+ Phase 6 addendum, 6 new lessons)
- `projects/record-header-and-notepad-r1/notes/matter-form-binding-instructions.md` (NEW, DEF-02)
- `src/client/pcf/MatterHeader/control/*` — dark mode wiring (DEF-09) + all 5 version-bump locations
- `src/client/pcf/MatterHeader/Solution/*` — 5 version-bump locations + packed bundle
- `src/client/shared/Spaarke.UI.Components/src/hooks/useRelatedCount.ts` — v1.0.16 root-cause fix
- `src/client/shared/Spaarke.UI.Components/src/hooks/useRelatedCount.test.ts` — mock shape corrected
- `src/client/shared/Spaarke.UI.Components/src/hooks/useRecordHeaderToolbarActions.ts` — DEF-11 Part 1 + v1.0.15 diagnostic revert
- `src/client/shared/Spaarke.UI.Components/src/components/HeaderToolbar/HeaderToolbar.tsx` — badge sizing/positioning + gap/padding
- `src/client/shared/Spaarke.UI.Components/src/components/HeaderToolbar/README.md` — badge-counts section
- `src/solutions/Notepad/**` — DEF-10 (6 deep-path imports + Vite aliases + tsconfig paths) + CreatedByPopover removal
- `src/solutions/SmartTodo/src/services/queryHelpers.ts` — DEF-11 Part 2 + Part 3 (regarding filter + 2 SELECT fields)
- `src/solutions/SmartTodo/src/services/DataverseService.ts` — DEF-11 Part 2 param
- `src/solutions/SmartTodo/src/hooks/useTodoItems.ts` — DEF-11 Part 2 option
- `src/solutions/SmartTodo/src/components/SmartToDo.tsx` — DEF-11 Part 2 wiring + Part 3 filter extend
- `src/solutions/SmartTodo/src/components/Header/Header.tsx` — DEF-11 Part 3 placeholder rename
- `src/solutions/SmartTodo/src/types/entities.ts` — DEF-11 Part 3 field

### Test status heading into `/code-review` + `/test-diet`

- **useRelatedCount**: 11/11 pass (mocks corrected in v1.0.16)
- **useRecordHeaderToolbarActions**: DEF-11 checkmark + annotation assertions updated (pass). 9 pre-existing v1.0.10-era sparkle-slot drift baseline-confirmed as not-Phase-6.
- **HeaderToolbar**: all pass (no changes to test contract in v1.0.13→v1.0.18 styling tweaks)
- **Notepad**: 107/109 pass. 2 failures = pre-existing v1.0.9 memo-state drift, baseline-confirmed. 3 CreatedByPopover-shell tests DELETED (component removed from shell; unit tests retained on disk).
- **SmartTodo**: 61/61 pass (across queryHelpers, useTodoItems, SmartToDo, useLaunchContext).

### Deferrals to file at `/code-review` step or during merge-to-master

| DEF | Rationale (blocked by R1 CLAUDE.md constraint) | Follow-on |
|---|---|---|
| DEF-01 | Sparkle refresh needs BFF endpoint (NFR-07 forbids) | `ai-record-summary-regeneration-r1` |
| DEF-03 | R1 CLAUDE.md "MUST NOT modify VisualHost/**" | R2B `record-header-shared-consolidation-r2` |
| DEF-04 | R1 CLAUDE.md "MUST NOT modify EventDetailSidePane/**" | R2B `record-header-shared-consolidation-r2` |
| DEF-05 | Per-entity PCFs each own separate ~80 LOC project per §Scope OS-02 | Three follow-ons: `project-header-r1`, `invoice-header-r1`, `event-header-r1` |
| DEF-06 reforward | pcf-scripts `moduleResolution: "node"` blocks; proper fix ripples repo-wide | R2B `pcf-tsconfig-moduleresolution-bump-r2` |
| DEF-08 | Promote `useSprkMemoRepository` to shared lib blocked by DEF-04 (adopter out of R1) | R2B (after DEF-04) |

---

## After wrap-up: `/merge-to-master`

Full sync (worktree branch → origin/master → main-repo local master pull) via the merge-to-master skill.

---

## Full State (Detailed)

### Applicable ADRs (from R1 spec)

- ADR-006 PCF over webresources
- ADR-011 Dataset PCF over subgrids (principle only)
- ADR-012 Shared component library
- ADR-021 Fluent v9 semantic tokens
- ADR-022 PCF platform libraries (React 16/17 boundary)
- ADR-024 Polymorphic resolver pattern (sprk_memo complies — Path C)
- ADR-028 Spaarke Auth v2 (N/A — R1 is host-context only)
- ADR-038 Testing strategy (drives the `/test-diet` step)

### Constraints in effect (R1 CLAUDE.md)

MUST:
- `Xrm.WebApi` for Dataverse I/O (NFR-05)
- Fluent v9 semantic tokens (NFR-03)
- React 16/17-safe shared components (NFR-06)
- Honor Notepad launch contract as external API (NFR-09)

MUST NOT:
- Add ANY endpoint to `Sprk.Bff.Api/**` (NFR-07)
- Import `@spaarke/auth` (NFR-05)
- Modify `src/client/pcf/VisualHost/**`
- Modify `src/solutions/EventDetailSidePane/**`
- Use React 18-exclusive APIs in shared library

### Environment state

- Working directory: `c:\code_files\spaarke-wt-record-header-and-notepad-r1`
- Git branch: `work/record-header-and-notepad-r1`
- Latest commit: `a9bda2674` (v1.0.18 spacing tweak)
- PR: [#545](https://github.com/spaarke-dev/spaarke/pull/545) — draft. Ready for review sign-off after `/code-review` + `/test-diet`.

### Recovery commands (verify state after any interruption)

```bash
git -C c:/code_files/spaarke-wt-record-header-and-notepad-r1 log --oneline -5
git -C c:/code_files/spaarke-wt-record-header-and-notepad-r1 status --short
cat src/client/pcf/MatterHeader/control/version.ts    # → CONTROL_VERSION = '1.0.18'
cat projects/record-header-and-notepad-r1/README.md | head -20  # status Complete
```

---

*Last edited 2026-07-04 22:05 to reflect UAT round 18 sign-off + close-out entering `/code-review` step.*
