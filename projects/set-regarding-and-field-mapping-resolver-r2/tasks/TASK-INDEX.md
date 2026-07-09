# Task Index — Set-Regarding and Field-Mapping Resolver R2

> **Status legend**: 🔲 not-started · 🔄 in-progress/retry · ✅ completed · ⛔ blocked
> **Total**: 16 tasks across 6 phases

## Tasks

| ID | Title | Phase | Status | Deps | Model/Effort | Rigor | Parallel |
|----|-------|-------|--------|------|--------------|-------|----------|
| 001 | Add `sprk_expression` column | 0 | 🔲 | none | sonnet/high | STANDARD | — |
| 002 | BFF read-layer + rule DTO extension | 0 | 🔲 | 001 | sonnet/high | FULL | — |
| 003 | BFF tests + publish-size + push regression | 0 | 🔲 | 002 | sonnet/high | FULL | — |
| 010 | Engine shell + types (context-agnostic) | 1 | 🔲 | 003 | opus/high | FULL | A |
| 011 | Consolidate nav-prop discovery | 1 | 🔲 | none | opus/high | FULL | A |
| 012 | Copy engine — scalar + lookup `@odata.bind` | 1 | 🔲 | 010,011 | sonnet/xhigh | FULL | — |
| 013 | Default + Concat + Template engines | 1 | 🔲 | 012 | sonnet/high | FULL | — |
| 014 | Same-entity support + no-guard test | 1 | 🔲 | 013 | sonnet/high | FULL | — |
| 015 | Engine unit tests (all paths) | 1 | 🔲 | 012,013,014 | sonnet/high | FULL | — |
| 020 | Wire event + matter + project | 2 | 🔲 | 015,011 | sonnet/high | FULL | B |
| 021 | Wire todo + workAssignment | 2 | 🔲 | 015,011 | sonnet/high | FULL | B |
| 022 | Wire invoice + reportCard | 2 | 🔲 | 015,011 | sonnet/high | FULL | B |
| 030 | Cleanup + seed attorney matrix | 3 | 🔲 | 001 | sonnet/high | STANDARD | — |
| 040 | Architecture doc + CLAUDE.md pointer | 4 | 🔲 | 015,022,030 | sonnet/high | MINIMAL | — |
| 041 | Admin authoring guide | 4 | 🔲 | 030 | sonnet/high | MINIMAL | — |
| 090 | Project wrap-up | 5 | 🔲 | 020,021,022,030,040,041 | sonnet/high | FULL | — |

## Dependency Graph (critical path)

```
001 → 002 → 003 → 010 ┐
                 011 ─┼→ 012 → 013 → 014 → 015 → {020,021,022} ┐
001 → 030 ───────────┘                                         ├→ 040 → 090
                                                    030 ───────┴→ 041 ┘
```

Critical path: 001 → 002 → 003 → 010 → 012 → 013 → 014 → 015 → 022 → 040 → 090.

## Parallel Execution Plan

| Wave | Tasks | Prereq | Files | Safe | goal-eligible |
|------|-------|--------|-------|------|---------------|
| P0 (serial) | 001 → 002 → 003 | — | schema, BFF .cs | sequential | NO (schema change + single-file BFF chain) |
| A (parallel, 2) | 010, 011 | 003 (for 010); 011 none | FieldMappingService/Types vs PolymorphicResolver+services | ✅ | NO (2 tasks) |
| Engine (serial) | 012 → 013 → 014 → 015 | 010,011 | FieldMappingService.ts (shared) | sequential | NO (architectural judgment) |
| B (parallel, 3) | 020, 021, 022 | 015, 011 | disjoint service files | ✅ | **YES** |
| Seed (parallel to 1/2) | 030 | 001 | Dataverse data | ✅ | NO (data authoring, judgment on names) |
| Docs | 040 → 041 | 015/022/030 | docs/ (+ root CLAUDE.md in 040) | 040 main-session-only | NO |
| Wrap-up | 090 | all | project artifacts | sequential | NO (gates + live verify) |

**Wave B goal-condition** (if run under `/goal`):
> All of the following hold in this session: (1) tasks 020, 021, 022 each show the shared-lib build + their affected service tests passing via transcript output (`npm test` green); (2) each task's Step 9.5 gates (code-review + adr-check) have been RUN and their findings surfaced; (3) git status shows only the 6 wizard-service files changed. OR: a BLOCKED.md exists under projects/set-regarding-and-field-mapping-resolver-r2/ documenting a root-CLAUDE.md §6 escalation, shown in transcript. Stop after 18 turns if neither state is reached.

**How to execute a parallel wave**: confirm prereqs ✅, then dispatch one task-execute subagent per task in ONE message (max 6 concurrent). Verify the shared-lib build between waves.

## Notes
- **Baseline**: worktree synced to origin/master 2026-07-09 (0 behind); BFF builds green; all 7 wizard services present (dependency satisfied).
- **Hot-path**: BFF=Y (narrow — additive `FieldMappingRuleDto` fields only); registered in `projects/INDEX.md`. No plugins; no new PCF.
