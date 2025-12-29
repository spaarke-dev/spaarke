# CLAUDE.md - Documentation Traffic Controller

> **Last Updated**: December 25, 2025
>
> **Purpose**: Direct AI to appropriate documentation based on task type.

---

## Quick Navigation

| Need | Location | Action |
|------|----------|--------|
| **Constraints, patterns, skills** | `/.claude/` | ✅ Load by default |
| **Full rationale, procedures, guides** | `/docs/` | ⚠️ Load when deep dive needed |

---

## Two-Tier Documentation Strategy

### Tier 1: AI-Optimized Context (`/.claude/`)

The `/.claude/` folder contains **concise, AI-optimized** content for efficient context loading:

```
.claude/
├── adr/                      # Concise ADRs (~100-150 lines each)
├── constraints/              # MUST/MUST NOT rules by topic
├── patterns/                 # Code patterns and examples
├── protocols/                # AI behavior protocols
├── skills/                   # Skill definitions and workflows
└── templates/                # Project/task templates
```

**Why use `.claude/` first?**
- Optimized for context efficiency
- Contains actionable constraints (what to do)
- Skills provide step-by-step workflows
- Concise ADRs focus on MUST/MUST NOT rules

### Tier 2: Full Reference (`/docs/`)

The `/docs/` folder contains **complete documentation** for deep dives:

```
docs/
├── adr/                      # Full ADRs with history and rationale
├── architecture/             # System architecture docs
├── guides/                   # How-to guides and procedures
├── procedures/               # Process documentation
├── standards/                # Coding and auth standards
└── product-documentation/    # User-facing docs
```

**When to load from `docs/`:**
- Need full rationale behind an architectural decision
- Debugging requires detailed procedure steps
- Evaluating changes to architecture
- User explicitly asks for historical context

---

## Loading Strategy by Task Type

| Task Type | Primary Source | Secondary Source |
|-----------|----------------|------------------|
| Implement feature | `.claude/adr/`, `.claude/patterns/` | `docs/guides/` if stuck |
| Follow a skill | `.claude/skills/{skill}/SKILL.md` | — |
| Understand ADR | `.claude/adr/ADR-XXX.md` | `docs/adr/ADR-XXX-*.md` for full context |
| Debug issue | `docs/procedures/`, `docs/guides/` | — |
| Architecture change | `docs/adr/` (full versions) | — |

---

## ADR Loading Pattern

**For implementation** (constraints):
```
READ .claude/adr/ADR-001-minimal-api.md  # ~100 lines, MUST/MUST NOT rules
```

**For architectural decisions** (full context):
```
READ docs/adr/ADR-001-minimal-api-and-workers.md  # Full history and rationale
```

---

## When Starting a Task

1. **Check `.claude/skills/INDEX.md`** for applicable workflows
2. **Load applicable `.claude/adr/`** files for constraints
3. **Load `.claude/patterns/`** for code examples
4. **Only load `docs/`** if you need full procedures or historical context

---

## See Also

- `/.claude/skills/INDEX.md` - Skill registry and workflows
- `/.claude/adr/` - Concise ADR constraints
- `/docs/adr/INDEX.md` - Full ADR index
- Root `/CLAUDE.md` - Repository-wide instructions

---

*This structure separates actionable AI context from comprehensive reference material.*
