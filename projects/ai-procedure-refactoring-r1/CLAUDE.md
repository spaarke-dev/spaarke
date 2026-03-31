# AI Procedure Refactoring R1 — Project Context

## Project Summary
Refactor ~95 drift-prone documentation files into stable pointer-based references. Code is source of truth; docs capture decisions, constraints, and discovery pointers only.

## Key Rules
- Pattern files: max 25 lines, pointer format (When/Read/Constraints/Key Rules)
- Architecture docs: decisions and constraints only, no implementation walkthroughs
- No new documentation files — refactoring only
- Validate all pointer paths before committing
- Check for broken references in skills/tasks before deleting any doc

## Applicable ADRs
- None directly govern documentation structure
- ADR references IN pattern files must remain accurate after refactoring

## Key Files
- `spec.md` — Full specification with 16 FRs
- `plan.md` — Implementation plan with 4 phases
- `design.md` — Design rationale and target formats

## 🚨 MANDATORY: Task Execution Protocol
When executing tasks in this project, Claude Code MUST invoke the `task-execute` skill.
DO NOT read POML files directly and implement manually.
See root CLAUDE.md for full protocol and trigger phrases.
