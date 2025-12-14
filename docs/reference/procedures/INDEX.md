# Spaarke Software Development Procedures

> **Version**: 2.0 | **Last Updated**: December 4, 2025

This folder contains modular reference documents for Spaarke's software development procedures.

## Document Map

### Quick Reference (Start Here)

| Document | Purpose | Size |
|----------|---------|------|
| [00-project-quick-start.md](00-project-quick-start.md) | **Cheat sheet** - one-page project lifecycle reference | ~150 lines |
| [07-quick-start.md](07-quick-start.md) | Step-by-step from design spec to completion | ~180 lines |
| [09-repo-and-github-process.md](09-repo-and-github-process.md) | Branching + commit cadence + PR flow (prevents lost work/drift) | ~120 lines |

### For All Audiences

| Document | Purpose | Size |
|----------|---------|------|
| [01-overview.md](01-overview.md) | Introduction, roles, lifecycle overview | ~300 lines |

### For Product Managers

| Document | Purpose | Size |
|----------|---------|------|
| [02-stage-0-discovery.md](02-stage-0-discovery.md) | Discovery & research process | ~100 lines |
| [03-stages-1-3-planning.md](03-stages-1-3-planning.md) | Feature request → design spec | ~350 lines |

### For AI Agents (Load These)

| Document | Purpose | When to Load |
|----------|---------|--------------|
| [04-ai-execution-protocol.md](04-ai-execution-protocol.md) | Task execution protocol, human-in-the-loop triggers | **Every task session** |
| [05-poml-reference.md](05-poml-reference.md) | POML tag definitions | When reading/writing .poml files |
| [06-context-engineering.md](06-context-engineering.md) | Context thresholds, handoff protocol | When context > 50% |

### Quick Reference

| Document | Purpose | Size |
|----------|---------|------|
| [08-stage-checklists.md](08-stage-checklists.md) | Checkbox lists for each stage | ~120 lines |

---

## Usage by Role

### Product Managers
1. Start with **01-overview.md** to understand the full lifecycle
2. For new features, use **02-stage-0-discovery.md** and **03-stages-1-3-planning.md**
3. Use **08-stage-checklists.md** to verify completeness at gates

### Software Engineers
1. Review **01-overview.md** for role responsibilities
2. Use **07-quick-start.md** when starting AI-directed development
3. Reference **03-stages-1-3-planning.md** for design spec requirements
4. Reference **08-stage-checklists.md** for stage completion

### AI Agents
**Always load** at task start:
```
docs/reference/procedures/04-ai-execution-protocol.md
```

**Load as needed**:
- `05-poml-reference.md` - When parsing or generating task files
- `06-context-engineering.md` - When context usage > 50%

---

## Related Documentation

| Resource | Location | Purpose |
|----------|----------|---------|
| ADRs | `docs/reference/adr/` | Architecture constraints |
| Skills | `.claude/skills/` | AI workflow definitions |
| Templates | `docs/ai-knowledge/templates/` | Project/task templates |
| Root CLAUDE.md | `/CLAUDE.md` | Repository-wide AI context |

---

*See [../SPAARKE-SOFTWARE-DEVELOPMENT-PROCEDURES.md](../SPAARKE-SOFTWARE-DEVELOPMENT-PROCEDURES.md) for the complete consolidated document.*
