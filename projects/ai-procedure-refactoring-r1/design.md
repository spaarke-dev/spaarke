# AI Procedure Refactoring R1 — Drift-Proof Context Architecture

> **Project**: ai-procedure-refactoring-r1
> **Status**: Design
> **Priority**: High
> **Last Updated**: March 31, 2026

---

## Executive Summary

Refactor Spaarke's AI context layer (`.claude/`, `docs/`) from description-heavy documentation that drifts from code to a pointer-based architecture where code is the source of truth and documentation captures only decisions, constraints, and discovery pointers. This eliminates ~50,000 lines of drift-prone content and replaces it with ~6,500 lines of stable, pointer-based context.

---

## Problem Statement

The current `.claude/` and `docs/` structure contains ~39,000 lines in `.claude/` and ~50,000+ lines in `docs/` across 338 files. An audit revealed:

- **~95 files** contain implementation descriptions that restate what the code does — these drift as code changes
- **~6,800 lines** of inline code examples in `.claude/patterns/` that duplicate actual source files
- **~20,000 lines** in `docs/architecture/` where ~65% describes implementation rather than decisions
- **~30,000 lines** in `docs/guides/` with deployment guides containing versioned commands and configuration that drifts

When Claude reads stale architecture docs and then reads the actual code, it gets conflicting signals and may follow the outdated doc instead of the current code.

---

## Design Principles

### 1. Code Is the Source of Truth
Implementation details live in code, not in markdown. If you want to know how playbooks work, read `PlaybookOrchestrationService.cs` — don't read a document that describes it.

### 2. Docs Capture What Code Cannot
- **Why** a decision was made (ADRs)
- **What NOT to do** (constraints)
- **When** to use a pattern (pointers with context)
- **How** to operate (deployment procedures, not implementation walkthroughs)

### 3. Pointers, Not Descriptions
Instead of describing how a system works in markdown, point to the code entry points and let Claude read the actual implementation:

```markdown
## Playbook System
**When**: Adding playbook types or node executors
**Read**: PlaybookOrchestrationService.cs, INodeExecutor.cs
**Constraints**: ADR-013 (extend BFF), must implement INodeExecutor
```

### 4. Three-Layer Context Model
| Layer | Contains | Stability | Loaded |
|-------|----------|-----------|--------|
| **CLAUDE.md** | Rules, constraints, commands, entry points | Very stable | Always |
| **`.claude/`** | ADRs (constraints), pointers (discovery), skills (procedures) | Stable | Per-task |
| **`docs/`** | Full ADR history, operational procedures, user docs | Reference | Rarely by AI |

---

## Scope

### In Scope (R1)

**Phase 1: Convert Patterns to Pointers**
- Refactor all 30+ `.claude/patterns/` files from inline code to pointer format
- Each pattern file becomes ~15-25 lines: When / Read / Constraints / Key Rules
- Update `.claude/patterns/INDEX.md`
- Validate pointers resolve to actual files

**Phase 2: Split Architecture Docs**
- Audit all 35 `docs/architecture/` files
- For each file: keep decisions/constraints, remove implementation descriptions
- Create pointer files in `.claude/patterns/` for removed implementation content
- Delete `docs/guides/ai-implementation-reference.md` (3,639 lines — code IS the reference)
- Trim architecture files to decisions + "why" only

**Phase 3: Consolidate Guides**
- Audit all 71 `docs/guides/` files
- Keep: operational procedures (deployment steps, admin checklists)
- Convert: implementation walkthroughs → pointers to code
- Consolidate: 6 playbook guides → 2 (JPS authoring + scope configuration)
- Clean up: finish redirect cleanup in `docs/standards/` (3 redirect stubs)
- Archive: deprecated/superseded guides

**Phase 4: Update CLAUDE.md Discovery Model**
- Replace "Documentation" section with "Architecture Discovery" section
- Add system entry points table (BFF, Playbooks, Auth, PCF, Code Pages, etc.)
- Add "Rule: Read Code First, Docs Second" guidance
- Update `.claude/skills/INDEX.md` references if any point to removed docs
- Update task-execute knowledge file references

### Out of Scope
- Changing ADR format or content (already well-structured)
- Modifying skill definitions (`.claude/skills/` — already procedural, not descriptive)
- Changing protocol definitions (`.claude/protocols/` — stable)
- Changing template files (`.claude/templates/` — stable)
- Removing `docs/adr/` full ADRs (these are the historical record)
- Removing user-facing docs (`docs/product-documentation/`)
- Removing data model docs (`docs/data-model/` — reference, low drift)
- Creating new patterns — only converting existing ones

---

## Pattern File Format (Target State)

Each pattern file in `.claude/patterns/` follows this structure:

```markdown
# {Pattern Name}

## When
{1-2 sentences: when to apply this pattern}

## Read These Files
1. `{path/to/entry-point.cs}` — {what it shows}
2. `{path/to/supporting-file.ts}` — {what it shows}
3. `{path/to/example.cs}` — {canonical example to follow}

## Constraints
- ADR-{NNN}: {key constraint}
- MUST {rule}
- MUST NOT {rule}

## Key Rules
- {Rule 1 that isn't obvious from reading the code}
- {Rule 2}
```

**Max 25 lines per pattern file.** If you need more, the pattern is too complex — split it or add more pointer files.

---

## Architecture Doc Format (Target State)

Each architecture doc in `docs/architecture/` should contain ONLY:

```markdown
# {System Name} Architecture

## Decision
{What was decided and why — 2-3 paragraphs max}

## Constraints
- {Constraint 1}
- {Constraint 2}

## Key Components
| Component | Location | Purpose |
|-----------|----------|---------|
| {Name} | `{path}` | {1-line purpose} |

## Related ADRs
- ADR-{NNN}: {title}
```

**No implementation walkthroughs.** No "the service then calls X which returns Y which is mapped to Z." That's what the code says.

---

## Validation

After each phase, validate:
1. **Pointer resolution**: Every `Read:` path in pattern files points to an existing file
2. **No orphaned references**: No skill or task file references a deleted doc
3. **Constraint coverage**: Every ADR constraint is still referenced somewhere (CLAUDE.md, constraints/, or pattern files)
4. **Git grep**: No remaining large blocks of inline C#/TypeScript code in `.claude/patterns/`

---

## Success Criteria

1. [ ] All `.claude/patterns/` files are pointer-based (max 25 lines each)
2. [ ] `docs/architecture/` files contain only decisions/constraints (no implementation walkthroughs)
3. [ ] `docs/guides/ai-implementation-reference.md` deleted
4. [ ] All pointer paths validated (files exist)
5. [ ] No skill or task file references a deleted document
6. [ ] Root CLAUDE.md updated with Architecture Discovery section
7. [ ] Total `.claude/patterns/` reduced from ~6,800 lines to ~1,500
8. [ ] Total `docs/architecture/` reduced from ~20,000 lines to ~5,000
9. [ ] Zero new documentation files created (only refactoring existing)

---

## Risk Mitigation

| Risk | Mitigation |
|------|-----------|
| Deleting a doc that a skill references | Run `grep -r` for doc filename across all `.claude/skills/` before deleting |
| Pattern pointer points to file that gets renamed | Pointer files are 15 lines — easy to update; add to PR review checklist |
| Losing valuable "why" context buried in implementation docs | Phase 2 explicitly extracts decisions before trimming |
| Breaking task-execute knowledge file loading | Audit all `.poml` files' `<knowledge>` sections for references to docs being removed |

---

## Dependencies

### Prerequisites
- None — this is a standalone refactoring project

### Related Projects
- All future projects benefit from drift-proof context
- `spaarke-daily-update-service` — first project to use refactored patterns

---

*Last updated: March 31, 2026*
