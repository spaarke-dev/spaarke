# CLAUDE.md - Documentation Traffic Controller

> **Last Updated**: March 31, 2026
>
> **Purpose**: Direct AI to appropriate documentation. Code is the source of truth — read code first, docs second.

---

## Architecture Discovery Hierarchy

| Priority | Source | What It Contains |
|----------|--------|-----------------|
| 1. **Code** | `src/` | Implementation (source of truth) |
| 2. **Patterns** | `.claude/patterns/` | 25-line pointer files → which code to read |
| 3. **ADR Constraints** | `.claude/adr/` | MUST/MUST NOT rules (~100 lines each) |
| 4. **Architecture** | `docs/architecture/` | Decisions and rationale only (no implementation) |
| 5. **Guides** | `docs/guides/` | Operational procedures (deploy, configure) |
| 6. **Full ADRs** | `docs/adr/` | Complete history and alternatives considered |

---

## docs/ Directory Structure

```
docs/
├── adr/                      # Full ADRs with history and rationale
├── architecture/             # Architecture decisions (trimmed to decisions-only)
├── guides/                   # Operational procedures and how-to guides
├── procedures/               # Process documentation
├── standards/                # Coding and auth standards
├── data-model/               # Dataverse entity schemas
├── product-documentation/    # User-facing docs
└── enhancements/             # Enhancement proposals
```

---

## Loading Strategy by Task Type

| Task Type | Primary Source | Secondary Source |
|-----------|----------------|------------------|
| Implement feature | Code + `.claude/patterns/` + `.claude/adr/` | `docs/architecture/` for "why" |
| Deploy | `.claude/skills/{deploy-skill}/SKILL.md` | `docs/guides/` for procedures |
| Understand ADR | `.claude/adr/ADR-XXX.md` | `docs/adr/ADR-XXX-*.md` for full context |
| Debug issue | Code first, then `docs/guides/` | — |
| Architecture change | `docs/adr/` (full versions) | `docs/architecture/` for current decisions |

---

## See Also

- Root `/CLAUDE.md` — Repository-wide instructions and Architecture Discovery section
- `/.claude/patterns/INDEX.md` — Pattern file index (pointer format)
- `/.claude/skills/INDEX.md` — Skill registry and workflows
