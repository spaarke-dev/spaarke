# CLAUDE.md — AI Procedure Refactoring R2

> **Project**: ai-procedure-refactoring-r2
> **Type**: Documentation-only (no source code changes)
> **Created**: April 5, 2026

## Project Context

This project creates/restores/verifies 74 documentation files across 5 document types. No source code is modified — all work reads existing code and writes documentation.

## Key Constraints

1. **Documentation only** — do NOT modify any `.cs`, `.ts`, `.tsx`, or other source files
2. **Code is source of truth** — always read current code before documenting; never copy from stale docs
3. **Skill-driven drafting** — every document MUST be drafted using its type's skill:
   - Architecture → `/docs-architecture`
   - Guide → `/docs-guide`
   - Standards → `/docs-standards`
   - Data Model → `/docs-data-model`
   - Procedures → `/docs-procedures`
4. **File path verification** — every path referenced in any doc must resolve to a real file
5. **Known Pitfalls required** — every architecture doc must include a Known Pitfalls section
6. **Evidence-based standards** — standards must cite ADRs, skills, or code as sources; never invent conventions

## Document Type Directories

| Type | Directory |
|------|-----------|
| Architecture | `docs/architecture/` |
| Guide | `docs/guides/` |
| Standards | `docs/standards/` |
| Data Model | `docs/data-model/` |
| Procedures | `docs/procedures/` |

## Working Pattern for Each Task

1. Read the task's prompt from `notes/documentation-requirements.md`
2. Read all source code files referenced in the prompt
3. For over-trimmed docs: run `git log -p -- {file}` to recover removed content
4. Draft using the appropriate `/docs-*` skill structure
5. Verify all file paths resolve
6. Write the document

## Applicable Skills

- `docs-architecture` — `.claude/skills/docs-architecture/SKILL.md`
- `docs-guide` — `.claude/skills/docs-guide/SKILL.md`
- `docs-standards` — `.claude/skills/docs-standards/SKILL.md`
- `docs-data-model` — `.claude/skills/docs-data-model/SKILL.md`
- `docs-procedures` — `.claude/skills/docs-procedures/SKILL.md`

## 🚨 MANDATORY: Task Execution Protocol

When executing tasks in this project, Claude Code MUST invoke the `task-execute` skill. DO NOT read POML files directly and implement manually. See root CLAUDE.md for full protocol.

## References

- [Specification](spec.md)
- [Plan](plan.md)
- [Requirements Table](notes/documentation-requirements.md)
- [Task Index](tasks/TASK-INDEX.md)
