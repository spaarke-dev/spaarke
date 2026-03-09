# Task Index — WizardShell Extraction

## Status Legend
- :white_large_square: Pending
- :arrow_forward: In Progress
- :white_check_mark: Complete

## Phase 1: Shell Foundation

| # | Task | Status | Deps |
|---|------|--------|------|
| 001 | Create WizardShell type definitions | :white_large_square: | — |
| 002 | Create WizardShell reducer | :white_large_square: | 001 |
| 003 | Create WizardSuccessScreen component | :white_large_square: | 001 |
| 004 | Create WizardShell component | :white_large_square: | 001, 002, 003 |
| 005 | Create barrel exports | :white_large_square: | 001-004 |

## Phase 2: Refactor CreateMatter

| # | Task | Status | Deps |
|---|------|--------|------|
| 010 | Refactor CreateMatter WizardDialog to use WizardShell | :white_large_square: | 004, 005 |
| 011 | Clean up CreateMatter wizardTypes.ts | :white_large_square: | 010 |
| 012 | Delete SuccessConfirmation.tsx | :white_large_square: | 010 |

## Phase 3: Documentation

| # | Task | Status | Deps |
|---|------|--------|------|
| 020 | Update custom dialogs pattern documentation | :white_large_square: | 004 |
| 021 | Update SPAARKE-UX-MANAGEMENT architecture docs | :white_large_square: | 004 |

## Phase 4: Verify

| # | Task | Status | Deps |
|---|------|--------|------|
| 030 | Build and functional verification | :white_large_square: | 010-012, 020-021 |

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| A | 002, 003 | 001 complete | Reducer and SuccessScreen are independent |
| B | 011, 012 | 010 complete | Cleanup tasks are independent |
| C | 020, 021 | 004 complete | Doc updates are independent |

## Summary
- **Total tasks**: 10
- **Estimated effort**: ~8 hours
- **Critical path**: 001 → 002 → 004 → 010 → 030
