# Task Index — AI Procedure Refactoring R1

> **Total Tasks**: 20
> **Parallel Groups**: 7 (A-G)
> **Critical Path**: Group A → B → F → G

## Task Status

### Phase 1: Convert Patterns to Pointers

| # | Task | Status | Group | Dependencies |
|---|------|--------|-------|-------------|
| 001 | Convert `.claude/patterns/api/` (7 files) | ✅ | A | none |
| 002 | Convert `.claude/patterns/auth/` (12 files) | ✅ | A | none |
| 003 | Convert `.claude/patterns/caching/` (3 files) | ✅ | A | none |
| 004 | Convert `.claude/patterns/dataverse/` (5 files) | ✅ | A | none |
| 005 | Convert `.claude/patterns/pcf/` (5 files) | ✅ | A | none |
| 006 | Convert `.claude/patterns/ai/` (3 files) | ✅ | A | none |
| 007 | Convert `.claude/patterns/testing/` (3 files) | ✅ | A | none |
| 008 | Convert `.claude/patterns/webresource/` + `ui/` (5 files) | ✅ | A | none |
| 009 | Update `.claude/patterns/INDEX.md` | ✅ | B | 001-008 |

### Phase 2: Split Architecture Docs

| # | Task | Status | Group | Dependencies |
|---|------|--------|-------|-------------|
| 010 | Audit all 35 architecture files — classify keep/trim/delete | ✅ | C | none |
| 011 | Trim AI architecture docs (5 files, DELETE ai-implementation-reference) | ✅ | D | 010 |
| 012 | Trim BFF/API architecture docs (4 files) | ✅ | D | 010 |
| 013 | Trim UI/frontend architecture docs (6 files) | ✅ | D | 010 |
| 014 | Trim infrastructure architecture docs (7 files) | ✅ | D | 010 |
| 015 | Trim reference/stable architecture docs (8 files) | ✅ | D | 010 |

### Phase 3: Consolidate Guides

| # | Task | Status | Group | Dependencies |
|---|------|--------|-------|-------------|
| 020 | Consolidate 6 playbook guides into 2 | ✅ | E | none |
| 021 | Clean up 3 redirect stubs in docs/standards/ | ✅ | E | none |
| 022 | Audit remaining guides for drift | ✅ | E | none |

### Phase 4: Update CLAUDE.md & Validate

| # | Task | Status | Group | Dependencies |
|---|------|--------|-------|-------------|
| 030 | Update CLAUDE.md + validate all pointers + fix broken refs | ✅ | F | all prior |

### Phase 5: Wrap-Up

| # | Task | Status | Group | Dependencies |
|---|------|--------|-------|-------------|
| 090 | Project wrap-up — README status, lessons-learned, metrics | ✅ | G | 030 |

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| A | 001, 002, 003, 004, 005, 006, 007, 008 | None | 8 independent pattern subdirectories — max parallelism |
| B | 009 | Group A complete | INDEX update needs all patterns done |
| C | 010 | None (can start with Group A) | Architecture audit — read-only classification |
| D | 011, 012, 013, 014, 015 | 010 complete | 5 independent architecture domains |
| E | 020, 021, 022 | None (can start with any group) | Independent guide work |
| F | 030 | Groups A-E complete | Serial validation — CLAUDE.md update + pointer validation + broken ref check |
| G | 090 | Group F complete | Wrap-up |

## Execution Strategy

**Maximum parallelism**: Groups A, C, and E have no dependencies — spawn up to **12 concurrent agents** (8 + 1 + 3).

**Recommended execution order**:
1. Launch Groups A + C + E simultaneously (12 tasks, all independent)
2. When Group A completes → launch Group B (task 009)
3. When Group C (010) completes → launch Group D (5 tasks parallel)
4. When Groups B + D + E all complete → launch Group F (task 030)
5. When Group F completes → launch Group G (task 090)

**Total wall-clock time** (with full parallelism): ~6 hours
**Total effort**: ~20 hours across all tasks

## High-Risk Items

| Task | Risk | Mitigation |
|------|------|-----------|
| 011 | Deleting ai-implementation-reference.md may break references | Grep for filename before deleting |
| 030 | CLAUDE.md is critical infrastructure — bad edit breaks all sessions | Review carefully, test in worktree first |
| 020 | Consolidating 6 guides may lose niche content | Preserve all content in 2 target files, then trim |
