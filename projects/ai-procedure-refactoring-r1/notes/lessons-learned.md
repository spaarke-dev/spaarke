# Lessons Learned — AI Procedure Refactoring R1

> **Date**: 2026-03-31

## Final Metrics

| Area | Before | After | Reduction | Target |
|------|--------|-------|-----------|--------|
| `.claude/patterns/` total lines | ~6,800 | 1,078 | **84%** | ~1,500 (exceeded) |
| `.claude/patterns/` files | ~38 | 43 (+5 INDEX files) | — | — |
| `docs/architecture/` total lines | ~31,555 | ~7,902 | **75%** | ~5,000 (close) |
| `docs/architecture/` file count | 39 | 33 | 6 deleted | — |
| Playbook guides | 6 files | 2 files | **67%** | 2 files (met) |
| Redirect stubs deleted | 3 | 0 | **100%** | 0 (met) |
| Guides with TODO markers | 0 | 10 | — | — |

## What Worked Well

1. **Parallel execution with subagents** — Group A (8 pattern conversion tasks) ran simultaneously, completing Phase 1 in a fraction of sequential time. Groups C+E launched alongside, maximizing parallelism.

2. **Pointer format is effective** — 25-line max forces conciseness. The When/Read/Constraints/Key Rules structure captures exactly what an AI agent needs without drift-prone implementation details.

3. **Architecture audit before trimming** — Task 010's read-only audit produced a classification table that made tasks 011-015 straightforward. Each trim agent had clear guidance on what to keep vs remove.

4. **Path validation before writing** — Verifying every `Read:` path exists before committing prevented broken references from entering the codebase.

## What Was Harder Than Expected

1. **Subagent permission issues** — Agents couldn't write/edit files due to permission mode. Had to apply their research findings from the main session. Future: ensure auto-accept mode before launching write-heavy agents.

2. **Stale paths in original patterns** — Many canonical file paths in the old pattern files were wrong (e.g., `Api/Documents/` subfolder didn't exist). The refactoring exposed years of accumulated path drift.

3. **Task 020 (playbook consolidation) complexity** — Merging 6 overlapping guides required careful content deduplication. The first agent timed out; retry with streamlined instructions succeeded.

4. **Cross-project references** — Deleted docs were referenced from other project folders (e.g., `spaarke-daily-update-service`). Required repo-wide grep to find and fix.

## Recommendations for Future Maintenance

1. **Pattern files should be validated on commit** — A pre-commit hook or CI check could verify all `Read:` paths in `.claude/patterns/` resolve to existing files.

2. **Architecture docs should never contain code blocks >10 lines** — This was the trimming rule. Enforce via linting or review checklist.

3. **Use pointer format for new documentation** — When adding new patterns, start with the pointer format. Never add inline code examples to pattern files.

4. **Address the 10 TODO-marked guides** — Task 022 identified drift-prone guides. These should be addressed in a follow-up project or as part of the features they describe.

5. **Architecture doc line count target not fully met** — Landed at ~7,902 vs target of ~5,000. The remaining files contain genuine decisions worth keeping. Further reduction would require merging related docs.
