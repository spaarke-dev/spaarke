---
description: Draft or update an architecture document for a Spaarke subsystem
tags: [documentation, architecture]
techStack: []
appliesTo: ["write architecture doc", "update architecture doc", "draft architecture", "docs-architecture"]
alwaysApply: false
---

# Architecture Document Skill

> **Category**: Documentation
> **Last Updated**: April 2026

---

## Purpose

Draft or update an architecture document in `docs/architecture/`. Architecture documents describe **how systems work technically** — component structures, data flows, integration patterns, design decisions, and constraints.

**Architecture docs are NOT:**
- Setup/configuration procedures (→ use `/docs-guide`)
- Step-by-step how-tos (→ use `/docs-guide`)
- API reference or schema docs (→ code is the source)
- Inline code examples (→ `.claude/patterns/` pointers handle this)

---

## When to Use

- "write architecture doc for {subsystem}"
- "update architecture doc for {subsystem}"
- "document the {subsystem} architecture"
- Creating a new architecture doc identified in the documentation requirements table
- Restoring depth to an over-trimmed architecture doc

---

## Document Structure (MANDATORY)

Every architecture document MUST follow this structure. Sections can be omitted if genuinely not applicable, but the order is fixed.

```markdown
# {Subsystem Name} Architecture

> **Last Updated**: {date}
> **Purpose**: {one-sentence description of what this subsystem does}

---

## Overview

{2-3 paragraphs: what this subsystem is, why it exists, what problem it solves.
Include the key design decision that shaped the architecture.}

## Component Structure

{Table or diagram showing the key files/classes and their responsibilities.
Use abbreviated paths from repo root. Show relationships between components.}

| Component | Path | Responsibility |
|-----------|------|---------------|
| {Name} | `src/.../File.cs` | {What it does} |

## Data Flow

{Describe the primary data flow(s) through the subsystem.
Use numbered steps showing how a request/event moves through components.
Include decision points and branching paths.}

1. {Entry point} receives {input}
2. {Component} processes by {doing what}
3. {Decision}: if X → path A, else → path B
4. {Output} is returned/stored/emitted

## Integration Points

{How this subsystem connects to other subsystems.
What it depends on, what depends on it.}

| Direction | Subsystem | Interface | Notes |
|-----------|-----------|-----------|-------|
| Depends on | {other} | {class/endpoint} | {why} |
| Consumed by | {other} | {class/endpoint} | {why} |

## Design Decisions

{Key architectural choices and their rationale.
Reference ADRs where applicable.}

| Decision | Choice | Rationale | ADR |
|----------|--------|-----------|-----|
| {What was decided} | {What was chosen} | {Why} | ADR-XXX |

## Constraints

{MUST/MUST NOT rules that govern this subsystem.
These are the rules Claude Code must follow when modifying this code.}

- **MUST**: {constraint}
- **MUST NOT**: {constraint}
- **MUST**: {constraint}

## Related

- [Pattern pointer](../../.claude/patterns/{domain}/{file}.md) — {what it covers}
- [ADR-XXX](../../.claude/adr/ADR-XXX.md) — {constraint}
- [{Guide}](../guides/{file}.md) — operational procedures for this subsystem
```

---

## Drafting Process

### Step 1: Read the Code First

```
BEFORE writing ANY content:

1. READ all source files for the subsystem
   - Entry points (endpoints, workers, services)
   - Key classes and their relationships
   - Configuration/options classes
   - Tests (for behavioral expectations)

2. READ existing related docs:
   - .claude/patterns/{domain}/ pointer files
   - .claude/adr/ relevant ADRs
   - .claude/constraints/ relevant constraint files
   - Any existing architecture doc being updated

3. MAP the component structure:
   - List all files with their responsibilities
   - Identify the primary data flow(s)
   - Identify integration points with other subsystems
```

### Step 2: Draft the Document

```
WRITE the document following the mandatory structure above.

RULES:
- Lead with WHAT and WHY, not HOW-TO
- Describe component relationships, not implementation details
- Include file paths so Claude can navigate to the code
- Reference ADRs for constraints — don't restate them in full
- Keep total length proportional to subsystem complexity:
  - Simple (1-3 files): 80-120 lines
  - Medium (4-10 files): 120-200 lines
  - Complex (10+ files): 200-400 lines
  - Never exceed 400 lines — split into sub-documents if needed

DO NOT INCLUDE:
- Full code blocks (use pattern pointers instead)
- Configuration values that change per environment
- Step-by-step procedures (belongs in guides)
- Version numbers or deployment status
- Package versions or dependency lists
```

### Step 3: Cross-Reference

```
AFTER writing:

1. VERIFY all file paths in the doc exist in the codebase
2. VERIFY ADR references are correct
3. CHECK if a .claude/patterns/ pointer should be created or updated
4. CHECK if the doc should be referenced from CLAUDE.md entry points
5. ADD to docs/architecture/INDEX.md
```

---

## Updating Existing Documents

When updating (not creating) an architecture doc:

```
1. READ the current document
2. READ the current code
3. IDENTIFY discrepancies:
   - Components added/removed since last update
   - Data flows that changed
   - Decisions that were revisited
4. UPDATE only the sections that are stale
5. UPDATE the "Last Updated" date
6. DO NOT rewrite sections that are still accurate
```

---

## Quality Checklist

Before finalizing any architecture document:

- [ ] Every file path resolves to an existing file
- [ ] Component table covers all key files in the subsystem
- [ ] Data flow describes at least one primary path end-to-end
- [ ] Design decisions include rationale (not just "we chose X")
- [ ] Constraints are actionable MUST/MUST NOT rules
- [ ] No step-by-step procedures (those belong in guides)
- [ ] No inline code blocks longer than 5 lines
- [ ] Length is proportional to complexity (see Step 2 rules)
- [ ] INDEX.md updated with new/modified entry

---

## Examples of Good vs Bad

**Good**: "The ScopeResolverService chains three resolution strategies: explicit scope → inherited scope → fallback catalog. This ensures every analysis has at least a base scope even if the user hasn't configured one."

**Bad**: "To configure scopes, first open the Playbook Builder, then click Settings, then select the scope from the dropdown..." (→ this belongs in a guide)

**Good**: "Email processing uses a deduplication key (`messageId + mailboxId`) stored in Redis with 24h TTL to prevent reprocessing the same message across polling intervals and webhook notifications."

**Bad**: "```csharp\npublic async Task ProcessEmailAsync(EmailMessage msg) {\n  var key = $\"{msg.MessageId}:{msg.MailboxId}\";\n  ...\n}\n```" (→ this belongs in a pattern pointer or is just code)

---

*This skill ensures architecture documents are consistent, code-grounded, and focused on technical understanding rather than operational procedures.*
