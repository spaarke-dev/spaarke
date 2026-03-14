# Task Index -- Code Quality and Assurance R1

> **Total Tasks**: 36
> **Estimated Hours**: 92h
> **Created**: 2026-03-11
> **Completed**: 2026-03-13

## Status Overview

| Status | Symbol | Count |
|--------|--------|-------|
| Pending | :black_square_button: | 0 |
| In Progress | :construction: | 0 |
| Complete | :white_check_mark: | 36 |
| Blocked | :no_entry: | 0 |

---

## Phase 1: Initial Code Quality Audit (22h)

| # | Task | Est | Status | Deps | Blocks |
|---|------|-----|--------|------|--------|
| 001 | Audit BFF API C# Code Quality | 4h | :white_check_mark: | -- | 007, 008, 030 |
| 002 | Audit TypeScript/PCF/Code Pages Quality | 4h | :white_check_mark: | -- | 007, 008, 033, 034 |
| 003 | Audit PowerShell Scripts Quality | 3h | :white_check_mark: | -- | 007, 008, 016 |
| 004 | Audit Test Suite Quality and Coverage | 3h | :white_check_mark: | -- | 007, 008 |
| 005 | Audit Dependency Health and Security | 2h | :white_check_mark: | -- | 007, 008, 032 |
| 006 | Audit Configuration Files Consistency | 2h | :white_check_mark: | -- | 007, 008, 010, 011 |
| 007 | Generate Quality Scorecard and Baseline Metrics | 2h | :white_check_mark: | 001-006 | 008 |
| 008 | Create Prioritized Remediation Plan | 2h | :white_check_mark: | 007 | 030-034 |

## Phase 2: Tooling Foundation (19h)

| # | Task | Est | Status | Deps | Blocks |
|---|------|-----|--------|------|--------|
| 010 | Configure Prettier for TypeScript/React | 2h | :white_check_mark: | 006 | 012, 017, 034 |
| 011 | Stricten ESLint Rules | 3h | :white_check_mark: | 006 | 012, 017, 033 |
| 012 | Configure Husky + lint-staged | 2h | :white_check_mark: | 010, 011 | 017 |
| 013 | Install and Configure CodeRabbit | 2h | :white_check_mark: | -- | 017 |
| 014 | Configure Claude Code Action | 3h | :white_check_mark: | -- | 017 |
| 015 | Configure SonarCloud Integration | 3h | :white_check_mark: | -- | 017, 020 |
| 016 | Configure PSScriptAnalyzer | 2h | :white_check_mark: | 003 | 017, 022 |
| 017 | Verify All Tools Working Together | 2h | :white_check_mark: | 010-016 | 020 |

## Phase 3: Automation Pipelines (16h)

| # | Task | Est | Status | Deps | Blocks |
|---|------|-----|--------|------|--------|
| 020 | Create Nightly Quality Workflow | 4h | :white_check_mark: | 015, 017 | 021, 025, 043 |
| 021 | Create Nightly Review Prompt | 3h | :white_check_mark: | 020 | 043 |
| 022 | Implement PostToolUse Lint Hook | 2h | :white_check_mark: | 011, 016 | 043 |
| 023 | Implement TaskCompleted Quality Gate Hook | 2h | :white_check_mark: | 022 | 043 |
| 024 | Enhance /code-review Skill | 3h | :white_check_mark: | -- | 043 |
| 025 | Implement Weekly Quality Summary | 2h | :white_check_mark: | 020 | 043 |

## Phase 4: Remediation (18h)

| # | Task | Est | Status | Deps | Blocks |
|---|------|-----|--------|------|--------|
| 030 | Refactor Program.cs into Feature Modules | 4h | :white_check_mark: | 001, 008 | 035, 043 |
| 031 | Resolve TODO/FIXME Comments | 3h | :white_check_mark: | 008 | 043 |
| 032 | Fix Dependency Vulnerabilities | 2h | :white_check_mark: | 005, 008 | 043 |
| 033 | Apply ESLint Strictening Fixes | 3h | :white_check_mark: | 008, 011 | 035, 043 |
| 034 | Apply Prettier Formatting (Bulk Pass) | 2h | :white_check_mark: | 008, 010 | 035, 043 |
| 035 | Update sdap-ci.yml with New Quality Checks | 3h | :white_check_mark: | 030, 033, 034 | 040, 043 |
| 036 | Add Quality Badges to README | 1h | :white_check_mark: | 015 | 043 |

## Phase 5: Enforcement & Documentation (17h)

| # | Task | Est | Status | Deps | Blocks |
|---|------|-----|--------|------|--------|
| 040 | Enable Blocking Quality Gates | 3h | :white_check_mark: | 035 | 043 |
| 041 | Update CI/CD Documentation | 2h | :white_check_mark: | 020, 035 | 043 |
| 042 | Create Quality Onboarding Guide | 2h | :white_check_mark: | -- | 043 |
| 043 | End-to-End Quality System Verification | 3h | :white_check_mark: | (many) | 090 |
| 044 | Create Quarterly Audit Runbook | 2h | :white_check_mark: | -- | 090 |
| 045 | Final Procedures Update | 2h | :white_check_mark: | 041 | 090 |
| 090 | Project Wrap-Up | 2h | :white_check_mark: | 043, 044, 045 | -- |

---

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| P1-A | 001, 002, 003, 004, 005, 006 | None | All 6 audit tasks are independent -- ran in parallel |
| P2-A | 010, 011 | 006 complete | Prettier + ESLint config (independent of each other) |
| P2-B | 013, 014, 015 | None | External tool setup (CodeRabbit, Claude Code Action, SonarCloud) -- fully independent |
| P2-C | 016 | 003 complete | PSScriptAnalyzer depends on PowerShell audit findings |
| P3-A | 022, 024 | 011/016 and none | Post-edit hook + code-review skill enhancement -- independent |
| P3-B | 021, 025 | 020 complete | Nightly review prompt + weekly summary -- both extend nightly workflow |
| P4-A | 030, 031, 032 | 008 complete | Program.cs refactor, TODO cleanup, dependency fixes -- independent areas |
| P4-B | 033, 034 | 008 + 010/011 | ESLint fixes + Prettier formatting -- can run in parallel (different tool chains) |
| P5-A | 042, 044 | None | Onboarding guide + quarterly runbook -- independent documentation |

---

## Critical Path

```
001-006 (parallel) -> 007 -> 008 -> 030 -> 035 -> 040 -> 043 -> 090
                                    |-> 031 ----------------------|
                                    |-> 033 -> 035 --------------|
                                    |-> 034 -> 035 --------------|
006 -> 010 -> 012 -> 017 -> 020 -> 021 -----------------> 043
006 -> 011 -> 012 --|         |-> 025 -----------------> 043
```

**Longest path**: 001 -> 007 -> 008 -> 030 -> 035 -> 040 -> 043 -> 090 (8 sequential tasks, ~23h)

---

*Task index for code-quality-and-assurance-r1. Completed: 2026-03-13*
