# Current Task State — record-header-and-notepad-r1

> **Last Updated**: 2026-07-04 12:15 (post Phase 6 code-complete)
> **Recovery**: Read "Quick Recovery" section first.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | 090 — R1 Wrap-Up (MANDATORY per CLAUDE.md §7) |
| **Phase** | 5 — Wrap-up (after Phase 6 code-complete) |
| **Progress** | Phase 6 CODE-COMPLETE (6 DEFs; 1 reverted). Ready for wrap-up + `/merge-to-master`. |
| **Status** | pending (task 090 not started) |
| **Next Action** | `/task-execute` on task 090 wrap-up scope: `/code-review` → `/adr-check` → `/test-diet` → `/repo-cleanup` → update README/plan → `notes/lessons-learned.md` → file DEF-01/03/04/05/06-reforward/08 as GH Issues → update `projects/INDEX.md` → mark all TASK-INDEX rows ✅. Then `/merge-to-master`. |

### Phase 6 delivered (this session)

| DEF | Result |
|---|---|
| DEF-07 | ✅ `.claude/patterns/pcf/pcf-build-scaffold.md` — 10 build gotchas captured while fresh |
| DEF-02 | ✅ `notes/matter-form-binding-instructions.md` — maker checklist for post-merge form binding |
| DEF-09 | ✅ Dark-mode support in `MatterHeaderHost.tsx` via shared `themeStorage` + HC listener + Power Apps context signal |
| DEF-06 | ⏸️ Attempted `exports` field on `@spaarke/ui-components/package.json`; reverted — webpack directory-index resolution + pcf-scripts `moduleResolution: "node"` block. Filed as R2B scope. |
| DEF-10 | ✅ Notepad bundle **1,176,060 → 478,612 bytes (59% reduction)**. Root cause: services barrel pulled in `mammoth` docx parser, top-level barrel pulled in Lexical + PDF.js + App Insights. Fix: 6 deep-path imports + 3 Vite alias values changed from `.../index.ts` files to `.../` directories (tsconfig `paths` synced). |
| DEF-11 | ✅ Pivoted from "build sprk_todospage DataGrid Code Page" to "reuse SmartTodo openTodos filter". `handleCheckmarkClick` now emits `action=openTodos&regardingType=<entity>&regardingId=<id>` to launch SmartTodo Code Page pre-filtered to the current record's related to-dos. Reuses shipped R4 FR-34 contract (`src/solutions/SmartTodo/src/hooks/useLaunchContext.ts` `openTodos` branch). |

### MatterHeader PCF: v1.0.11 → v1.0.12

Ship: `src/client/pcf/MatterHeader/Solution/bin/MatterHeaderPcf_v1.0.12.0.zip` (18,126 bytes / 62.4 KiB bundle).

Version bumped in 5 locations: `control/version.ts`, `control/ControlManifest.Input.xml`, `Solution/solution.xml`, `Solution/pack.ps1`, `Solution/Controls/…/ControlManifest.xml`. Description-key captures "(v1.0.12 — DEF-09 dark mode + DEF-11 checkmark→SmartTodo openTodos filter)".

### Notepad ship: 478 KB webresource

`src/solutions/Notepad/dist/notepad.html` — 478,612 bytes (gzipped 144 KB). Upload to `sprk_notepad` webresource + publish.

### Test status (baseline-confirmed, not DEF-10/11 regressions)

- Notepad: **110/112 pass**. 2 failures are pre-existing v1.0.9 memo-state drift; confirmed on baseline via `git stash`.
- Shared lib toolbar: DEF-11 checkmark + annotation tests **pass** with updated assertions (fixed `pageInput.name` → `pageInput.webresourceName` pre-existing broken assertion). 9 sparkle-related failures are pre-existing v1.0.10-era drift (sparkle slot was intentionally moved to shared `<AiSummaryPopover>` component; tests still assert the old hook-emitted-slot API).

### Deferrals to file at wrap-up

| DEF | Reason | Follow-on target |
|---|---|---|
| DEF-01 | Sparkle refresh needs BFF endpoint (R1 NFR-07 forbids BFF) | `ai-record-summary-regeneration-r1` |
| DEF-03 | VisualHost CardChrome migration blocked by R1 CLAUDE.md "MUST NOT modify VisualHost/**" | R2B `record-header-shared-consolidation-r2` |
| DEF-04 | EventDetailSidePane MemoSection adoption blocked by R1 CLAUDE.md "MUST NOT modify EventDetailSidePane/**" | R2B `record-header-shared-consolidation-r2` |
| DEF-05 | Per-entity PCFs (Project/Invoice/Event) each own separate ~80 LOC project per R1 §Scope OS-02 | Three follow-ons: `project-header-r1`, `invoice-header-r1`, `event-header-r1` |
| DEF-06 reforward | `exports` field blocked by pcf-scripts `moduleResolution: "node"` — proper fix ripples repo-wide | R2B `pcf-tsconfig-moduleresolution-bump-r2` |
| DEF-08 | Promote `useSprkMemoRepository` to shared lib blocked by DEF-04 (adopter out of R1) | R2B (after DEF-04) |

---

## After wrap-up: `/merge-to-master`

Full sync (worktree → origin/master → main-repo local master pull) via the merge-to-master skill.

---

## Full State (Detailed)

### Session timeline (2026-07-03 → 2026-07-04)

- 2026-07-03: v1.0.5 → v1.0.11 live QA rounds (10 rounds)
- 2026-07-04 09:00–11:15: Phase 6 execution start — DEF-07, DEF-02, DEF-09 shipped; DEF-06 attempted and reverted
- 2026-07-04 11:15: context-handoff + `/compact`
- 2026-07-04 post-compact: DEF-11 pivot decision — user preferred reusing SmartTodo with matter filter over building a new DataGrid page. Cost collapsed from 6-8 hours to ~30 min.
- 2026-07-04 11:30–12:15: DEF-10 executed (6 imports + 3 Vite aliases + 2 tsconfig paths + 2 Jest mock paths); DEF-11 executed (~10-line handler rewire + 2 test-assertion updates); MatterHeader v1.0.12 packed
- Next: Phase 6 code commit → merge master → task 090 wrap-up → `/merge-to-master`

### Applicable ADRs (from R1 spec)

- ADR-006 PCF over webresources
- ADR-011 Dataset PCF over subgrids
- ADR-012 Shared component library
- ADR-021 Fluent v9 semantic tokens
- ADR-022 PCF platform libraries (React 16/17 boundary)
- ADR-024 Polymorphic resolver pattern (Path C — sprk_memo complies)
- ADR-028 Spaarke Auth v2 (N/A — R1 is host-context only)
- ADR-038 Testing strategy (for test-diet gate at wrap-up)

### Constraints in effect (R1 CLAUDE.md)

MUST:
- `Xrm.WebApi` for Dataverse I/O (NFR-05)
- Fluent v9 semantic tokens (NFR-03)
- React 16/17-safe shared components (NFR-06)
- Honor Notepad launch contract as external API (NFR-09)

MUST NOT:
- Add ANY endpoint to `Sprk.Bff.Api/**` (NFR-07)
- Import `@spaarke/auth` (NFR-05)
- Modify `src/client/pcf/VisualHost/**` (DEF-03 is R2B)
- Modify `src/solutions/EventDetailSidePane/**` (DEF-04 is R2B)
- Use React 18-exclusive APIs in shared library

### Environment state

- Working directory: `c:\code_files\spaarke-wt-record-header-and-notepad-r1`
- Git branch: `work/record-header-and-notepad-r1`
- Latest commit before Phase 6: `4ae56fc04` (merge master into branch, resolved INDEX conflict)
- PR: #545 (draft state)
- Phase 6 to be committed as ONE consolidated commit
- After Phase 6 commit: fetch + merge `origin/master` (incoming: `a91ad1fcf` — unrelated project archive; zero file overlap)

---

*Last edited 2026-07-04 12:15 to reflect Phase 6 code-complete state.*
