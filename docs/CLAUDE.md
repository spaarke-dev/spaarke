# CLAUDE.md - Documentation Traffic Controller

> **Last Updated**: December 4, 2025
>
> **Purpose**: Direct AI to appropriate documentation based on task type.

---

## Quick Navigation

| Need | Location | Action |
|------|----------|--------|
| **Coding patterns, standards, guides** | `/docs/ai-knowledge/` | ✅ Reference freely |
| **Historical decisions, deep research** | `/docs/reference/` | ⚠️ Ask before loading |

---

## Documentation Structure

```
docs/
├── CLAUDE.md                     # This file - traffic controller
├── ai-knowledge/                 # ✅ ACTIVELY REFERENCE
│   ├── CLAUDE.md                 # Index of coding-relevant content
│   ├── architecture/             # System patterns and boundaries
│   ├── standards/                # Coding standards and auth patterns
│   ├── guides/                   # How-to guides
│   └── templates/                # Project and task templates
└── reference/                    # ⚠️ DO NOT LOAD UNLESS ASKED
    ├── CLAUDE.md                 # "Stay out" instructions
    ├── adr/                      # Architecture Decision Records
    ├── research/                 # KM-* knowledge management articles
    └── articles/                 # Full architecture guides (verbose)
```

---

## Rules for AI-Directed Coding

### ✅ DO Reference `/docs/ai-knowledge/`

This directory contains **condensed, actionable content** optimized for coding tasks:

- **Architecture patterns** - Security boundaries, API patterns, SDAP overview
- **Coding standards** - OAuth/OBO patterns, Dataverse authentication
- **How-to guides** - Step-by-step procedures for common tasks
- **Templates** - Project plans, task execution, knowledge articles

**Load these documents proactively** when working on related features.

### ⚠️ DO NOT Reference `/docs/reference/` Unless Asked

This directory contains **background material** not needed for most coding tasks:

- **ADRs** - Historical decisions (the *why*, not the *what*)
- **KM-* articles** - Verbose reference documentation
- **Full architecture guides** - Comprehensive but context-heavy

**Why avoid?**
1. Consumes context window unnecessarily
2. May contain outdated details
3. Duplicates content already condensed in `ai-knowledge/`

**When to reference:**
- Developer explicitly asks for historical context
- Debugging requires deep-dive into specific technology
- Evaluating architectural changes

---

## When Starting a Task

1. **Check `/docs/ai-knowledge/CLAUDE.md`** for relevant articles
2. **Load applicable architecture/standards/guides** into context
3. **Proceed with implementation** using patterns from loaded docs
4. **Only reference `/docs/reference/`** if explicitly directed

---

## See Also

- `/docs/ai-knowledge/CLAUDE.md` - Index of coding-relevant content
- `/docs/reference/CLAUDE.md` - Instructions for reference material
- Root `/CLAUDE.md` - Repository-wide coding standards

---

*This structure separates actionable coding context from historical reference material.*
