# Task Index — Record Header + Notepad R1

> **Generated**: 2026-07-02 by `/project-pipeline` → `task-create`
> **Total Tasks**: 36 (14 Phase 1 + 6 Phase 2 + 12 Phase 3 + 3 Phase 4 + 1 wrap-up)
> **Spec**: [../spec.md](../spec.md) · **Plan**: [../plan.md](../plan.md) · **CLAUDE.md**: [../CLAUDE.md](../CLAUDE.md)

---

## Status Legend

🔲 not-started · 🔄 in-progress · ⛔ blocked · ✅ completed · ⏸️ deferred

## Task Table

| ID | Title | Phase | Status | Dependencies | Parallel Group | Rigor |
|----|-------|-------|--------|--------------|----------------|-------|
| 001 | Verify sprk_memo schema | 1 | 🔲 | none | — | STANDARD |
| 002 | HeaderToolbar component (FR-01) | 1 | ✅ | none | A | FULL |
| 003 | RecordHeaderShell component (FR-02) | 1 | ✅ | 002 | — | FULL |
| 004 | FieldGrid component (FR-03) | 1 | ✅ | none | A | FULL |
| 005 | TextField renderer (FR-04) | 1 | ✅ | 004 | B | FULL |
| 006 | LookupField renderer (FR-04) | 1 | ✅ | 004 | B | FULL |
| 007 | OptionSetField renderer (FR-04) | 1 | ✅ | 004 | B | FULL |
| 008 | TextareaField renderer (FR-04) | 1 | ✅ | 004 | B | FULL |
| 009 | useRecordFieldValues hook (FR-05) | 1 | ✅ | none | C | FULL |
| 010 | useRelatedCount hook (FR-06) | 1 | ✅ | 001 | — | FULL |
| 011 | toolbarLaunchDefaults constants | 1 | ✅ | none | C | STANDARD |
| 012 | useRecordHeaderToolbarActions hook (FR-07/08/08a/09/10/11) | 1 | ✅ | 009, 010, 011 | — | FULL |
| 013 | Update shared lib exports (index.ts) | 1 | ✅ | 002-012 | — | STANDARD |
| 014 | Shared lib integration test | 1 | ✅ | 013 | — | STANDARD |
| 020 | Verify SmartTodo webresource name | 2 | ✅ | none | — | STANDARD |
| 021 | MatterHeader PCF manifest + class (FR-12) | 2 | ✅ | 013 | — | FULL |
| 022 | MatterHeaderView composition | 2 | ✅ | 021 | — | FULL |
| 023 | Matter solution folder + pack.ps1 | 2 | ✅ | 022 | — | STANDARD |
| 024 | Build + verify bundle (NFR-02, NFR-04) | 2 | 🔲 | 023 | — | STANDARD |
| 025 | Deploy PCF + manual QA | 2 | 🔲 | 024, 020 | — | STANDARD |
| 030 | Notepad Vite scaffold | 3 | ✅ | 011 | — | FULL |
| 031 | Notepad types + deriveTitle utility | 3 | ✅ | 001 | D | STANDARD |
| 032 | useLaunchContext hook (FR-13) | 3 | ✅ | 030 | D | STANDARD |
| 033 | useSprkMemoRepository hook | 3 | ✅ | 001, 031, 032 | — | FULL |
| 034 | MemoList component (FR-16) | 3 | 🔲 | 033 | E | FULL |
| 035 | MemoEditor component (FR-17) | 3 | 🔲 | 033 | E | FULL |
| 036 | CreatedByPopover component (FR-18) | 3 | 🔲 | 033 | E | FULL |
| 037 | NotepadShell integration (FR-14/15/16/18) | 3 | 🔲 | 034, 035, 036 | — | FULL |
| 038 | URL-param error handling verification (FR-13) | 3 | 🔲 | 037 | — | STANDARD |
| 039 | Vite build + deploy as sprk_notepad_page | 3 | 🔲 | 038 | — | STANDARD |
| 040 | Entity-agnostic launch test (FR-19) | 3 | 🔲 | 039 | F | STANDARD |
| 041 | Notepad integration test (round trip) | 3 | 🔲 | 039 | F | STANDARD |
| 050 | Authoring guide skeleton | 4 | 🔲 | 025 | — | MINIMAL |
| 051 | Authoring guide full content | 4 | 🔲 | 050 | — | MINIMAL |
| 052 | Pattern pointer (touches .claude/) | 4 | 🔲 | 051 | — | MINIMAL |
| 090 | Project wrap-up (MANDATORY) | 5 | 🔲 | all | — | FULL |

---

## Parallel Execution Groups

Tasks in the same group can run simultaneously once prerequisites are met.

| Group | Tasks | Prerequisite | Files Touched | Safe to Parallelize |
|-------|-------|--------------|---------------|---------------------|
| **A** | 002, 004 | none | `HeaderToolbar/*`, `RecordHeader/FieldGrid.tsx` | ✅ Different components, no file overlap |
| **B** | 005, 006, 007, 008 | 004 ✅ | `RecordHeader/fields/{Text,Lookup,OptionSet,Textarea}Field.tsx` | ✅ Sibling field renderers, no overlap |
| **C** | 009, 011 | none | `hooks/useRecordFieldValues.ts`, `hooks/toolbarLaunchDefaults.ts` | ✅ Independent files |
| **D** | 031, 032 | 001 ✅ (031) / 030 ✅ (032) | Notepad `types/*` + `utils/*` vs `hooks/useLaunchContext.ts` | ✅ Different files |
| **E** | 034, 035, 036 | 033 ✅ | Notepad `components/{MemoList,MemoEditor,CreatedByPopover}.tsx` | ✅ Sibling components |
| **F** | 040, 041 | 039 ✅ | Test-file paths distinct | ✅ Independent test scenarios |

**How to Execute Parallel Groups:**
1. Verify all prerequisites are ✅
2. Invoke ONE message with MULTIPLE `Skill` tool calls (one per task)
3. Each Skill call runs `task-execute` on a different .poml file
4. Wait for all to complete before starting next group

**Permission boundary note (task 052)**: Task 052 creates `.claude/patterns/ui/record-header-composition.md` — this touches `.claude/` and MUST run in the main session (sub-agents cannot write to `.claude/` per CLAUDE.md §3 Sub-Agent Write Boundary). `parallel-safe=false` is enforced.

---

## Rigor Distribution

| Rigor | Count | Applies to |
|-------|-------|-----------|
| **FULL** | 22 | Code implementation, PCF class, hooks, components |
| **STANDARD** | 11 | Verification, config, integration tests, deploy |
| **MINIMAL** | 3 | Documentation (050, 051, 052) |

At each task, `task-execute` Step 0.5 re-derives rigor from actual characteristics and may override the hint.

---

## Critical Path

```
001 (schema verify) →
  010 (memo count hook) →
  012 (toolbar hook) →
  013 (shared lib exports) →
  021 (PCF class) → 022 → 023 → 024 → 025 (PCF deployed) →
  050 (guide skeleton) → 051 (guide full) → 052 (pattern pointer) →
  090 (wrap-up)
```

The Phase 3 (Notepad) branch runs in parallel with Phase 2 after task 011 lands.

---

## Phase Summary

| Phase | Tasks | Estimated Hours | Deliverables |
|-------|-------|----------------|--------------|
| 1: Shared Library | 14 | 40-52 | HeaderToolbar, RecordHeaderShell, FieldGrid, 4 field renderers, 3 hooks, launchDefaults, exports, integration test |
| 2: MatterHeaderPcf | 6 | 12-16 | Thin PCF, solution ZIP, deployed to dev + QA |
| 3: Notepad code page | 12 | 28-36 | Vite SPA, hooks, 4 components, deployed as `sprk_notepad_page`, entity-agnostic launch verified |
| 4: Documentation | 3 | 6-8 | Authoring guide + pattern pointer |
| 5: Wrap-up | 1 | 2-4 | code-review + adr-check + test-diet + repo-cleanup + lessons-learned + INDEX update |
| **TOTAL** | **36** | **88-116 h** | ~11-15 dev-days at 8h/day |

---

## Next Action

Execute task **001** (`001-verify-sprk-memo-schema.poml`) — it blocks tasks 010, 031, and 033. No parallel execution possible until schema is verified.

Run: `/task-execute projects/record-header-and-notepad-r1/tasks/001-verify-sprk-memo-schema.poml`

Or simply say "work on task 001" and Claude Code will invoke `task-execute` per the CLAUDE.md protocol.
