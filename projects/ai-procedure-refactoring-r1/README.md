# AI Procedure Refactoring R1

> **Status**: In Progress
> **Branch**: `work/ai-procedure-refactoring-r1`
> **Created**: 2026-03-31

## Overview

Refactor Spaarke's AI context layer from description-heavy documentation to a pointer-based architecture where code is the source of truth. Converts `.claude/patterns/` to 25-line pointer files, trims `docs/architecture/` to decisions-only, consolidates guides, and updates CLAUDE.md with Architecture Discovery model.

## Graduation Criteria

- [ ] All `.claude/patterns/` files converted to pointer format (max 25 lines)
- [ ] All pointer paths validated (files exist)
- [ ] `docs/architecture/` contains only decisions/constraints
- [ ] `docs/guides/ai-implementation-reference.md` deleted
- [ ] 6 playbook guides consolidated to 2
- [ ] Root CLAUDE.md has Architecture Discovery section
- [ ] No broken references in skills or tasks
- [ ] Pattern lines reduced from ~6,800 to ~1,500
- [ ] Architecture lines reduced from ~20,000 to ~5,000

## Quick Links

- [Implementation Plan](plan.md)
- [Task Index](tasks/TASK-INDEX.md)
- [Specification](spec.md)
- [Design](design.md)
