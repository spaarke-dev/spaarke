# Task Index — spaarke-dataset-grid-framework-r2

> **Created**: 2026-07-02 by `/task-create`
> **Total tasks**: 21 (across 4 phases + wrap-up)
> **Ships as**: 3 PRs (Phase 1, Phase 2, Phase 3) + wrap-up (Phase 4)

---

## Task Registry

| ID | Title | Phase | Status | Dependencies | Parallel | Rigor |
|---|---|---|---|---|---|---|
| 001 | FR-01 `contentSizing` framework field + tests | 1: Framework | ✅ | none | Wave 1 | FULL |
| 002 | FR-05 `availableViews` allowlist + tests | 1: Framework | ✅ | none | Wave 1 | FULL |
| 003 | FR-07 `pageSize` default → 25 + tests + doc | 1: Framework | ✅ | none | Wave 1 | FULL |
| 004 | FR-06 Config templates + guide reference | 1: Framework | ✅ | none | Wave 1 | STANDARD |
| 005 | FR-08 Unwind `maxHeight` from 6 sections + metadata `clamped` | 1: Framework | ✅ | 001 | Wave 2 | FULL |
| 006 | Phase 1 deploy + regression + PR 1 | 1: Framework | 🔲 | 001,002,003,004,005 | Wave 3 | STANDARD |
| 010 | FR-02 `rowHeight` schema + parser + test | 2: Wizard | ✅ | 006 | Wave 4 | FULL |
| 011 | FR-02 Wizard UI: `rowHeight` input + presets | 2: Wizard | ✅ | 010 | Wave 5 | FULL |
| 012 | FR-03 `SectionInstance` schema + widened `sections` type | 2: Wizard | ✅ | 006 | Wave 4 | FULL |
| 013 | FR-03 Wizard UI: "Advanced" panel | 2: Wizard | ✅ | 011,012 | Wave 6 | FULL |
| 014 | FR-04 `widthPreference` field + set 'full' on 6 widgets | 2: Wizard | ✅ | 006 | Wave 4 | FULL |
| 015 | FR-04 Wizard placement checks + runtime dev-guard | 2: Wizard | ✅ | 013,014 | Wave 7 | FULL |
| 016 | Phase 2 deploy + regression + PR 2 | 2: Wizard | 🔲 | 011,013,015 | Wave 8 | STANDARD |
| 020 | FR-10 Scaffold new shared package | 3: Extraction | ✅ | 016 | Wave 9 | FULL |
| 021 | FR-10 Migrate section registry into shared package | 3: Extraction | ✅ | 020 | Wave 10 | FULL |
| 022 | FR-10 Update SpaarkeAi `vite.config.ts` + deps | 3: Extraction | ✅ | 021 | Wave 11 | FULL |
| 023 | FR-10 Update LegalWorkspace + WorkspaceLayoutWizard consumers | 3: Extraction | ✅ | 021 | Wave 11 | FULL |
| 024 | FR-10 Update `Build-AllClientComponents.ps1` | 3: Extraction | ✅ | 020 | Wave 10 | STANDARD |
| 025 | FR-09 Dual-deploy warning (skill + guide) | 3: Extraction | ✅ | 022,023 | Wave 12 (sequential) | STANDARD |
| 026 | Phase 3 deploy + regression + PR 3 | 3: Extraction | 🔲 | 022,023,024,025 | Wave 13 | STANDARD |
| 090 | Project wrap-up (test-diet, docs, lessons, mark Complete) | 4: Wrap-up | 🔲 | 026 | Wave 14 | FULL |

**Legend**: 🔲 not-started · 🔄 in-progress · ✅ completed · ⚠️ blocked

---

## Parallel Execution Groups

Tasks in the same wave can run simultaneously once prerequisites are met.

| Wave | Tasks | Prerequisite | Files Touched | Safe to Parallelize | Notes |
|---|---|---|---|---|---|
| **1** | 001, 002, 003, 004 | none | Different types + config files (no overlap) | ✅ Yes | Framework additions in parallel |
| **2** | 005 | 001 ✅ | Metadata catalog + 6 section registrations | ⚠️ Internal subagents for 6 identical registration edits (see task 005) | Depends on 001 landing contentSizing |
| **3** | 006 | 001–005 ✅ | Deploy scripts, no source | ✅ Yes (single task) | Phase 1 close: build + deploy + regression + PR 1 |
| **4** | 010, 012, 014 | 006 ✅ | Different type files (no overlap) | ✅ Yes | Schema-first parallel |
| **5** | 011 | 010 ✅ | `WorkspaceLayoutWizard/src/App.tsx` | Serial with 013/015 (same file) | Wizard UI serial |
| **6** | 013 | 011, 012 ✅ | `WorkspaceLayoutWizard/src/App.tsx` | Serial with 011/015 (same file) | Wizard UI serial |
| **7** | 015 | 013, 014 ✅ | `WorkspaceLayoutWizard/src/App.tsx` + `sectionRegistry.ts` | Serial with 011/013 | Wizard UI serial + dev-guard |
| **8** | 016 | 011, 013, 015 ✅ | Deploy scripts | ✅ Yes (single task) | Phase 2 close: build + deploy + regression + PR 2 |
| **9** | 020 | 016 ✅ | New `src/client/shared/{new-package}/` files | ✅ Yes (single task) | Scaffold |
| **10** | 021, 024 | 020 ✅ | Package src vs `scripts/Build-AllClientComponents.ps1` | ✅ Yes (different files) | Migration in parallel with build-script update |
| **11** | 022, 023 | 021 ✅ | SpaarkeAi vs LegalWorkspace / WorkspaceLayoutWizard | ✅ Yes (different apps) | Consumer rewires |
| **12** | 025 | 022, 023 ✅ | `.claude/skills/code-page-deploy/SKILL.md` + `docs/guides/*.md` | ❌ **Main-session-only** | Touches `.claude/` — sub-agent write boundary per CLAUDE.md §3 |
| **13** | 026 | 022, 023, 024, 025 ✅ | Deploy scripts | ✅ Yes (single task) | Phase 3 close: build + deploy + regression + PR 3 |
| **14** | 090 | 026 ✅ | Docs + notes | ✅ Yes (single task) | Wrap-up |

### How to Execute Parallel Groups

1. Check all prerequisites are complete (✅ in Status column)
2. Invoke Skill tool with `skill="task-execute"` for EACH task in the wave — one message, multiple Skill calls
3. Each invocation runs `task-execute` for one task
4. Wait for all to complete before next wave
5. Wave 12 (task 025) is sequential — do NOT dispatch as sub-agent

---

## Critical Path

Longest dependency chain: **001 → 005 → 006 → 010/012/014 → 011/013/015 → 016 → 020 → 021 → 022/023 → 025 → 026 → 090** (13 sequential steps).

**Load-bearing task**: 001 (FR-01 `contentSizing`) — blocks 005 (unwind) which blocks Phase 1 close. Framework fix must land first.

**Highest-risk task**: 022 (FR-10 SpaarkeAi rewire) — touches SpaarkeAi build chain. Regression on Dashboard II is the gate.

---

## Rigor Level Distribution

- **FULL** (13 tasks): 001, 002, 003, 005, 010, 011, 012, 013, 014, 015, 020, 021, 022, 023, 090 — all code implementation
- **STANDARD** (7 tasks): 004, 006, 016, 024, 025, 026 — new files (templates), deploy tasks (multi-step verification), skill/doc edits
- **MINIMAL** (0 tasks) — no doc-only or inventory tasks in this project

---

## Deployment Cadence

- **PR 1** (task 006): after tasks 001–005. Deploys shared-lib + LegalWorkspace + SpaarkeAi + WorkspaceLayoutWizard to spaarkedev1. Regression on 6 widgets + 5 system layouts.
- **PR 2** (task 016): after tasks 010–015. Deploys shared-lib + WorkspaceLayoutWizard. Regression on wizard authoring + back-compat on 8+ published layouts.
- **PR 3** (task 026): after tasks 020–025. Deploys shared-lib + new shared package + LegalWorkspace + SpaarkeAi. Regression: Dashboard II identical pre/post rebuild.

---

## Coordination Dependencies (external)

- **`spaarkeai-compose-r1`** (parallel worktree, `work/spaarkeai-compose-r1`) — adds a Compose workspace layout + section-registry entry. Before Phase 3 kicks off, check that worktree's PR status:
  - If Compose merges FIRST: R2 rebases Phase 3 on top; FR-10 shared-package extraction includes the Compose section
  - If R2 Phase 3 merges FIRST: Compose rebases on the new shared-package structure
  - Coordinate merge order with `spaarkeai-compose-r1` owner before task 020 starts

---

*Task index is the canonical work registry. Update task status (🔲 → 🔄 → ✅) in place as work progresses. `task-execute` skill handles status transitions.*
