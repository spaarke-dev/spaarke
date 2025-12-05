# AI Protocols (AIP)

> **Last Updated**: December 4, 2025  
> **Location**: `docs/reference/protocols/` (parallel to `docs/reference/adr/`)  
> **Purpose**: Behavioral principles for AI agents working in the Spaarke codebase

---

## Overview

AI Protocols (AIPs) define **how AI agents should behave** when working in this repository.

| Document Type | Location | Governs | Embedded In |
|---------------|----------|---------|-------------|
| **ADRs** | `docs/reference/adr/` | How the *system* is built | CLAUDE.md constraints table |
| **AIPs** | `docs/reference/protocols/` | How the *AI agent* works | CLAUDE.md execution rules |

Both follow the same pattern:
- **Critical rules embedded** in root CLAUDE.md (always visible to AI)
- **Full documentation** here in `reference/` (for detailed understanding)

---

## Protocol Index

| AIP | Title | Purpose | Embedded In CLAUDE.md |
|-----|-------|---------|----------------------|
| [AIP-001](AIP-001-task-execution.md) | Task Execution | Context management, handoffs | ✅ Context thresholds |
| [AIP-002](AIP-002-poml-format.md) | POML Format | Task file structure | Reference only |
| [AIP-003](AIP-003-human-escalation.md) | Human Escalation | When to request human input | ✅ Escalation triggers |

---

## When to Reference Full AIPs

| Situation | Reference |
|-----------|-----------|
| Need handoff template | AIP-001 §Handoff Protocol |
| Creating/parsing .poml files | AIP-002 (full tag reference) |
| Need escalation format examples | AIP-003 §Examples |
| Making architectural decision | ADRs in `docs/reference/adr/` |

**Note**: Critical rules from AIP-001 and AIP-003 are already in root CLAUDE.md. Reference full AIPs only for detailed guidance.

---

## Protocol Template

When creating new AIPs:

```markdown
# AIP-{NNN}: {Title}

> **Status**: Active | Draft | Superseded
> **Created**: {date}
> **Applies To**: {AI agents, specific skills, etc.}

## Summary
One paragraph describing the protocol.

## Rules
Numbered list of behavioral rules.

## Examples
Concrete examples of applying the rules.

## Rationale
Why these rules exist.
```

---

## Related Documentation

| Resource | Location | Purpose |
|----------|----------|---------|
| ADRs | `docs/adr/` | Architecture decisions |
| Skills | `.claude/skills/` | Workflow definitions |
| Procedures | `docs/reference/procedures/` | Full process documentation |
| Root CLAUDE.md | `/CLAUDE.md` | Embedded critical rules |

---

*Part of Spaarke AI Knowledge Base*
