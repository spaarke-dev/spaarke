# design-to-project - ARCHIVED

> **⚠️ THIS SKILL IS ARCHIVED**
>
> **Use instead**: [`project-pipeline`](../project-pipeline/SKILL.md) (RECOMMENDED)
>
> **Archived date**: December 18, 2024

---

## Quick Migration

| Old Command | New Command |
|-------------|-------------|
| `/design-to-project projects/{name}` | `/project-pipeline projects/{name}` ⭐ |
| Need just artifacts | `/project-setup projects/{name}` |

---

## Why Was This Archived?

This skill was superseded by a cleaner two-tier architecture:

1. **project-setup** - Component skill for artifact generation
2. **project-pipeline** - Orchestrator skill for full pipeline (RECOMMENDED)

**Problems solved**:
- Eliminated duplication between design-to-project and project-init
- Clear separation: artifacts (project-setup) vs orchestration (project-pipeline)
- Better human-in-loop UX
- No automatic task execution (Phase 5 removed)

---

## Full Documentation

See archived skill documentation with complete migration guide:
- **Archived location**: [.claude/skills/_archived/design-to-project/](../_archived/design-to-project/)
- **ARCHIVED.md**: [Full archival explanation](../_archived/design-to-project/ARCHIVED.md)
- **Original SKILL.md**: [Historical reference](../_archived/design-to-project/SKILL.md)

---

## Recommended Reading

- [project-pipeline SKILL.md](../project-pipeline/SKILL.md) - **Start here**
- [project-setup SKILL.md](../project-setup/SKILL.md) - For advanced users
- [SKILL-INTERACTION-GUIDE.md](../SKILL-INTERACTION-GUIDE.md) - Full skill ecosystem guide
- [INDEX.md](../INDEX.md) - Skills registry

---

*For Claude Code: If a user requests /design-to-project, recommend /project-pipeline instead and explain the migration.*
