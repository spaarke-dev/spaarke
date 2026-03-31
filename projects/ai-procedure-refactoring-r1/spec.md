# AI Procedure Refactoring R1 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-03-31
> **Source**: design.md

## Executive Summary

Refactor Spaarke's AI context layer from description-heavy documentation that drifts from code to a pointer-based architecture. Convert ~95 drift-prone files across `.claude/patterns/`, `docs/architecture/`, and `docs/guides/` into stable pointer-based references where code is the source of truth and documentation captures only decisions, constraints, and discovery pointers.

## Scope

### In Scope
- Convert 30+ `.claude/patterns/` files from inline code examples to pointer format (max 25 lines each)
- Audit and trim 35 `docs/architecture/` files — keep decisions, remove implementation descriptions
- Audit and consolidate 71 `docs/guides/` files — keep procedures, convert implementation walkthroughs to pointers
- Delete `docs/guides/ai-implementation-reference.md` (3,639 lines of duplicated code)
- Consolidate 6 playbook guides into 2
- Clean up redirect stubs in `docs/standards/`
- Update root CLAUDE.md with Architecture Discovery section and system entry points
- Validate all pointer paths resolve to existing files
- Validate no skill/task references broken by removals

### Out of Scope
- ADR content changes (`.claude/adr/`, `docs/adr/` — already well-structured)
- Skill definition changes (`.claude/skills/` — procedural, not descriptive)
- Protocol changes (`.claude/protocols/` — stable)
- Template changes (`.claude/templates/` — stable)
- User-facing docs (`docs/product-documentation/`)
- Data model docs (`docs/data-model/`)
- Creating new patterns — only refactoring existing ones
- Legacy commands cleanup (`.claude/commands/` — low priority, not causing drift)

### Affected Areas
- `.claude/patterns/**/*.md` — all 30+ pattern files refactored
- `.claude/patterns/INDEX.md` — updated to reflect new format
- `docs/architecture/*.md` — 35 files audited, ~20 trimmed or deleted
- `docs/guides/*.md` — 71 files audited, ~30 trimmed/consolidated/archived
- `docs/standards/*.md` — 3 redirect stubs cleaned up
- `CLAUDE.md` (root) — Documentation section replaced with Architecture Discovery
- `.claude/skills/INDEX.md` — references updated if pointing to removed docs

## Requirements

### Functional Requirements

1. **FR-01**: Convert all `.claude/patterns/api/` files (7 files) to pointer format — Acceptance: Each file is max 25 lines with When/Read/Constraints/Key Rules structure; all `Read:` paths validated
2. **FR-02**: Convert all `.claude/patterns/auth/` files (12 files) to pointer format — Acceptance: Same as FR-01
3. **FR-03**: Convert all `.claude/patterns/caching/` files (3 files) to pointer format — Acceptance: Same as FR-01
4. **FR-04**: Convert all `.claude/patterns/dataverse/` files (5 files) to pointer format — Acceptance: Same as FR-01
5. **FR-05**: Convert all `.claude/patterns/pcf/` files (5 files) to pointer format — Acceptance: Same as FR-01
6. **FR-06**: Convert all `.claude/patterns/ai/` files (3 files) to pointer format — Acceptance: Same as FR-01
7. **FR-07**: Convert all `.claude/patterns/testing/` files (3 files) to pointer format — Acceptance: Same as FR-01
8. **FR-08**: Convert all `.claude/patterns/webresource/` and `.claude/patterns/ui/` files (5 files) to pointer format — Acceptance: Same as FR-01
9. **FR-09**: Audit all 35 `docs/architecture/` files and classify as: keep (decision/constraint), trim (remove implementation), or delete (pure implementation restatement) — Acceptance: Classification documented in audit spreadsheet; each "trim" file reduced to decisions-only
10. **FR-10**: Delete `docs/guides/ai-implementation-reference.md` — Acceptance: File deleted; no remaining references in skills or tasks
11. **FR-11**: Consolidate 6 playbook guides into 2 files (JPS authoring + scope configuration) — Acceptance: 2 files remain; content preserved; old files archived or deleted
12. **FR-12**: Clean up 3 redirect stubs in `docs/standards/` — Acceptance: Stubs deleted; no broken references
13. **FR-13**: Update root `CLAUDE.md` with Architecture Discovery section — Acceptance: Section includes system entry points table, "Read Code First" rule, and loading strategy
14. **FR-14**: Validate all pointer paths across all refactored pattern files — Acceptance: Automated grep confirms all `Read:` paths resolve to existing files
15. **FR-15**: Validate no skill or task file references broken docs — Acceptance: `grep -r` across `.claude/skills/`, `projects/*/tasks/` confirms no references to deleted files
16. **FR-16**: Update `.claude/patterns/INDEX.md` to reflect new pointer format — Acceptance: INDEX accurately lists all pattern files with updated descriptions

### Non-Functional Requirements
- **NFR-01**: No pattern file exceeds 25 lines (enforced by validation)
- **NFR-02**: No architecture doc contains inline code blocks longer than 10 lines (short examples for constraints are OK; implementation walkthroughs are not)
- **NFR-03**: Total `.claude/patterns/` line count reduced from ~6,800 to ~1,500 (78% reduction)
- **NFR-04**: Total `docs/architecture/` line count reduced from ~20,000 to ~5,000 (75% reduction)
- **NFR-05**: Zero new documentation files created — this is purely a refactoring project

## Technical Constraints

### Applicable ADRs
- No ADRs directly govern documentation structure — this is an internal tooling refactoring
- ADR references IN pattern files must remain accurate (validate after refactoring)

### MUST Rules
- MUST preserve all decision/constraint content from architecture docs (don't lose "why")
- MUST validate pointer paths before committing (automated grep check)
- MUST check for broken references in skills and tasks before deleting any doc
- MUST NOT create new documentation files (refactor only)
- MUST NOT modify ADR files (`.claude/adr/`, `docs/adr/`)
- MUST NOT modify skill definitions (`.claude/skills/*/SKILL.md`)
- MUST NOT modify protocol files (`.claude/protocols/`)

### Existing Patterns to Follow
- Current pointer-style patterns already exist in some newer pattern files — use as template
- `.claude/constraints/` files are already in the correct format (rules only, no implementation)

### Key Reference Documents
- `docs/architecture/playbook-architecture.md` — canonical example of file needing refactoring
- `docs/architecture/AI-ARCHITECTURE.md` — example of file with good decision content mixed with implementation
- `docs/guides/PLAYBOOK-DESIGN-GUIDE.md` — reference for playbook guide consolidation
- `docs/guides/PLAYBOOK-JPS-PROMPT-SCHEMA-GUIDE.md` — reference for JPS guide consolidation
- `docs/guides/PLAYBOOK-SCOPE-CONFIGURATION-GUIDE.md` — reference for scope guide consolidation
- `docs/guides/PLAYBOOK-BUILDER-GUIDE.md` — reference for builder guide consolidation
- `docs/guides/PLAYBOOK-PRE-FILL-INTEGRATION-GUIDE.md` — reference for pre-fill guide consolidation
- `docs/guides/JPS-AUTHORING-GUIDE.md` — reference for JPS authoring guide consolidation

## Success Criteria

1. [ ] All 30+ `.claude/patterns/` files converted to pointer format (max 25 lines) — Verify: `wc -l` on each file
2. [ ] All `Read:` paths in pattern files resolve to existing files — Verify: automated grep/validation script
3. [ ] `docs/architecture/` files contain only decisions/constraints — Verify: no inline code blocks >10 lines
4. [ ] `docs/guides/ai-implementation-reference.md` deleted — Verify: file doesn't exist
5. [ ] 6 playbook guides consolidated to 2 — Verify: file count in `docs/guides/PLAYBOOK-*`
6. [ ] 3 redirect stubs in `docs/standards/` cleaned up — Verify: no 3-line redirect files remain
7. [ ] Root CLAUDE.md has Architecture Discovery section — Verify: section exists with entry points table
8. [ ] No broken references in skills or tasks — Verify: grep for deleted filenames returns 0 results
9. [ ] Total `.claude/patterns/` lines ~1,500 (from ~6,800) — Verify: `find .claude/patterns -name "*.md" | xargs wc -l`
10. [ ] Total `docs/architecture/` lines ~5,000 (from ~20,000) — Verify: `find docs/architecture -name "*.md" | xargs wc -l`
11. [ ] Zero new documentation files created — Verify: git diff shows only modifications and deletions

## Dependencies

### Prerequisites
- None — standalone refactoring project

### External Dependencies
- None — all work is internal to the repository

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Playbook type field | Does sprk_playbooktype already exist? | Yes — values 0-4 (AIAnalysis, Workflow, Notification, Rules, Hybrid) | No schema changes needed |
| Pattern format | Max lines per pattern file? | 25 lines | Enforced by NFR-01 |
| Architecture doc format | What stays in architecture docs? | Decisions, constraints, component tables only — no implementation walkthroughs | Guides Phase 2 trimming |
| Legacy commands | Clean up `.claude/commands/`? | Out of scope for R1 — low priority | Not included in tasks |
| Data model docs | Refactor `docs/data-model/`? | Out of scope — reference material, low drift | Not included in tasks |

## Assumptions

- **Pointer validation**: Assumes source code file paths are stable enough that pointers won't immediately break. If a major refactoring moves files, pointer files are 15-25 lines and easy to update.
- **Skill references**: Assumes skills reference docs by filename, not by content — so renaming/deleting a doc is detectable via grep.
- **No active projects depend on deleted docs**: Will validate via grep before each deletion.

## Unresolved Questions

None — all design decisions resolved during audit session.

---

*AI-optimized specification. Original: design.md*
