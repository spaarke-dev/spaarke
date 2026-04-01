# ai-procedure-maintenance

---
description: Maintain and update AI coding procedures when new elements are added
tags: [maintenance, adr, constraints, patterns, protocols, skills]
techStack: [all]
appliesTo: ["new ADR", "new pattern", "new constraint", "new skill", "procedure update"]
alwaysApply: false
---

## Purpose

Ensure the AI coding procedure ecosystem stays current and consistent when new elements are introduced. This skill provides a **checklist-driven approach** to propagating updates across all affected files.

**Problem Solved**: Without this procedure, adding a new ADR might update `docs/adr/` but miss `.claude/adr/`, constraints, patterns, skill mappings, INDEX files, and root CLAUDE.md references.

---

## When to Use

Invoke this skill when:
- Creating a new ADR
- Adding a new constraint file
- Creating a new pattern file
- Adding a new protocol (AIP)
- Creating or significantly modifying a skill
- Updating root CLAUDE.md with new procedures
- Discovering that existing files have inconsistent references

**Trigger Phrases**:
- "update AI procedures for new ADR"
- "add new pattern to the system"
- "maintain AI coding procedures"
- "propagate ADR changes"

---

## Maintenance Checklists by Element Type

### Checklist A: New ADR

When creating a new Architecture Decision Record:

```
□ 1. CREATE full ADR
   Location: docs/adr/ADR-{NNN}-{slug}.md
   Follow template in docs/adr/

□ 2. CREATE concise ADR (AI-optimized version)
   Location: .claude/adr/ADR-{NNN}-{slug}.md
   Target: 100-150 lines
   Focus: MUST/MUST NOT rules, key rules
   Remove: Historical context, alternatives considered (keep in full version)

□ 3. UPDATE ADR indexes
   - docs/adr/INDEX.md - Add row to ADR table
   - .claude/adr/INDEX.md - Add row to concise ADR table

□ 4. UPDATE relevant constraint file
   Location: .claude/constraints/{domain}.md
   Action: Add constraint rules derived from ADR

   Domain mapping:
   - API/endpoints → api.md
   - PCF/frontend → pcf.md
   - Plugins → plugins.md
   - Auth → auth.md
   - Caching → data.md
   - AI features → ai.md
   - Background jobs → jobs.md
   - Testing → testing.md

□ 5. UPDATE constraint INDEX
   Location: .claude/constraints/INDEX.md
   Action: Update line count, add ADR reference if new file created

□ 6. CREATE pattern file (if ADR introduces code patterns)
   Location: .claude/patterns/{domain}/{pattern-name}.md
   Content: Pointer format � When/Read/Constraints/Key Rules (~25 lines, no inline code)

□ 7. UPDATE pattern INDEX
   Location: .claude/patterns/{domain}/INDEX.md
   Action: Add new pattern to table

□ 8. UPDATE skill mappings (CRITICAL)
   Files to update:
   - .claude/skills/adr-aware/SKILL.md
     → Rule 1 table: Add resource type → ADR mapping
     → Resource Type to Context Files Mapping table
   - .claude/skills/task-execute/SKILL.md
     → Step 4a: Tag → constraint mapping
     → Step 4b: Tag → pattern mapping
     → Step 5: ADR loading section
   - .claude/skills/task-create/SKILL.md
     → Step 3.4: Tag-to-Knowledge Mapping table
     → Step 3.5: ADR mapping section

□ 9. UPDATE root CLAUDE.md (if ADR adds critical constraint)
   Location: /CLAUDE.md
   Section: "Architecture Decision Records (ADRs)" table
   Action: Add summary row for high-impact ADRs

□ 10. UPDATE CROSS-REFERENCE-MAP.md
   Location: /CROSS-REFERENCE-MAP.md
   Action: Add entries showing ADR → files relationship

□ 11. VERIFY consistency
   Run: grep for ADR number across all .claude/ and CLAUDE.md files
   Ensure: All references use correct paths
```

### Checklist B: New Constraint File

When creating a new constraint file for a domain:

```
□ 1. CREATE constraint file
   Location: .claude/constraints/{domain}.md
   Structure:
   - Purpose section
   - MUST rules (positive constraints)
   - MUST NOT rules (negative constraints)
   - Quick reference table
   - ADR references

□ 2. UPDATE constraint INDEX
   Location: .claude/constraints/INDEX.md
   Add: New row with file, ADRs covered, line count

□ 3. CREATE corresponding pattern directory
   Location: .claude/patterns/{domain}/
   Add: INDEX.md listing available patterns

□ 4. UPDATE skill mappings
   - .claude/skills/adr-aware/SKILL.md → Rule 2, Resource Type table
   - .claude/skills/task-execute/SKILL.md → Step 4a table
   - .claude/skills/task-create/SKILL.md → Step 3.4 table

□ 5. ADD standard tag for domain
   Location: .claude/skills/INDEX.md (Standard Tag Vocabulary)
   Location: .claude/TAGGING-EXAMPLES.md (if applicable)

□ 6. VERIFY pattern INDEX exists
   Location: .claude/patterns/INDEX.md
   Add: New domain pattern directory reference
```

### Checklist C: New Pattern File

When adding a code pattern:

```
□ 1. CREATE pattern file
   Location: .claude/patterns/{domain}/{pattern-name}.md
   Content:
   - Brief description
   - When to use
   - Read: pointers to canonical source files
   - ADR references
   - Related patterns

□ 2. UPDATE domain pattern INDEX
   Location: .claude/patterns/{domain}/INDEX.md
   Add: New row with pattern name, purpose, ADR refs

□ 3. UPDATE root pattern INDEX
   Location: .claude/patterns/INDEX.md
   Verify: Domain directory is listed

□ 4. UPDATE skill mappings (if pattern should be auto-loaded)
   - .claude/skills/adr-aware/SKILL.md → Rule 2c pattern loading
   - .claude/skills/task-execute/SKILL.md → Step 4b table
   - .claude/skills/task-create/SKILL.md → Step 3.4 Patterns column

□ 5. ADD to CROSS-REFERENCE-MAP.md
   Location: /CROSS-REFERENCE-MAP.md
   Add: Pattern file with ADR relationships
```

### Checklist D: New Protocol (AIP)

When creating a new AI Interaction Protocol:

```
□ 1. CREATE protocol file
   Location: .claude/protocols/AIP-{NNN}-{slug}.md
   Follow template in .claude/protocols/INDEX.md

□ 2. UPDATE protocol INDEX
   Location: .claude/protocols/INDEX.md
   Add: New row with AIP number, title, purpose

□ 3. EMBED critical rules in root CLAUDE.md
   Location: /CLAUDE.md
   Section: Appropriate section based on protocol topic
   Action: Add summary of critical rules with reference to full AIP

□ 4. UPDATE skill files (if protocol affects skill behavior)
   Identify: Which skills should follow this protocol
   Add: Reference to protocol in skill's "Related" section
```

### Checklist E: New Skill

When creating a new skill:

```
□ 1. CREATE skill directory and files
   Location: .claude/skills/{skill-name}/
   Files:
   - SKILL.md (required)
   - references/ (optional)
   - scripts/ (optional)
   - assets/ (optional)

□ 2. ADD YAML frontmatter to SKILL.md
   Required fields:
   - description: Brief phrase (5-10 words)
   - tags: [tag1, tag2, ...]
   - techStack: [tech1, tech2, ...]
   - appliesTo: [patterns or scenarios]
   - alwaysApply: true/false

□ 3. UPDATE skills INDEX
   Location: .claude/skills/INDEX.md
   Add: Row to Available Skills table
   Add: Entry in appropriate category section
   Update: Skill Flow diagram if orchestrator/component

□ 4. UPDATE SKILL-INTERACTION-GUIDE.md (if skill changes workflow)
   Location: .claude/skills/SKILL-INTERACTION-GUIDE.md
   Add: Skill to interaction matrix
   Update: Workflow diagrams if needed

□ 5. UPDATE root CLAUDE.md
   Location: /CLAUDE.md
   Section: "AI Agent Skills" → Trigger Phrases table
   Add: Trigger phrase → skill mapping

□ 6. ADD slash command (if user-invocable)
   Location: /CLAUDE.md → Slash Commands table
   Add: /{skill-name} with description

□ 7. VERIFY tags use standard vocabulary
   Reference: .claude/skills/INDEX.md → Standard Tag Vocabulary
   Reference: .claude/TAGGING-EXAMPLES.md
```

### Checklist F: Update Root CLAUDE.md

When modifying the main instruction file:

```
□ 1. IDENTIFY all places that might need parallel updates
   - .claude/skills/INDEX.md (if skill-related)
   - .claude/skills/SKILL-INTERACTION-GUIDE.md (if workflow)
   - docs/CLAUDE.md (if documentation-related)
   - Project-specific CLAUDE.md files

□ 2. VERIFY path references are correct
   - Use .claude/protocols/ (not docs/reference/protocols/)
   - Use .claude/adr/ for concise ADRs
   - Use docs/adr/ for full ADRs

□ 3. UPDATE "Last Updated" dates
   All affected files should have current date

□ 4. RUN consistency check
   Grep for old paths that might have been left behind
```

---

## Cross-Reference Verification

After completing any checklist, run these verification steps:

```
1. PATH CONSISTENCY CHECK
   Search for old/wrong paths:
   - "docs/reference/adr" → Should be ".claude/adr/" or "docs/adr/"
   - "docs/ai-knowledge" → Directory removed; content in .claude/
   - "docs/reference/protocols" → Should be ".claude/protocols/"

2. INDEX COMPLETENESS CHECK
   Verify each INDEX.md includes all files in directory:
   - .claude/adr/INDEX.md vs actual ADR files
   - .claude/constraints/INDEX.md vs actual constraint files
   - .claude/patterns/*/INDEX.md vs actual pattern files
   - .claude/skills/INDEX.md vs actual skill directories

3. MAPPING CONSISTENCY CHECK
   Verify skill mappings are consistent:
   - adr-aware Rule 1 table matches task-execute Step 4a
   - adr-aware Resource Type table matches task-create Step 3.4
   - All constraint files are referenced in at least one skill

4. DATE CONSISTENCY CHECK
   All modified files should have "Last Updated" matching today's date
```

---

## Quick Reference: File Locations

| Element Type | Primary Location | Index File | Skills to Update |
|--------------|------------------|------------|------------------|
| ADR (Full) | `docs/adr/` | `docs/adr/INDEX.md` | — |
| ADR (Concise) | `.claude/adr/` | `.claude/adr/INDEX.md` | adr-aware, task-execute, task-create |
| Constraint | `.claude/constraints/` | `.claude/constraints/INDEX.md` | adr-aware, task-execute, task-create |
| Pattern | `.claude/patterns/{domain}/` | `.claude/patterns/{domain}/INDEX.md` | adr-aware, task-execute, task-create |
| Protocol | `.claude/protocols/` | `.claude/protocols/INDEX.md` | (varies by protocol) |
| Skill | `.claude/skills/{name}/` | `.claude/skills/INDEX.md` | SKILL-INTERACTION-GUIDE, root CLAUDE.md |

---

## Example: Adding ADR-023

**Scenario**: Adding a new ADR for "API Rate Limiting"

```
Step 1: Create docs/adr/ADR-023-api-rate-limiting.md (full version)
Step 2: Create .claude/adr/ADR-023-api-rate-limiting.md (concise, ~120 lines)
Step 3: Add to docs/adr/INDEX.md and .claude/adr/INDEX.md
Step 4: Add rules to .claude/constraints/api.md:
        - "MUST implement rate limiting on public endpoints"
        - "MUST use sliding window algorithm"
Step 5: Update .claude/constraints/INDEX.md (update api.md line count)
Step 6: Create .claude/patterns/api/rate-limiting.md in pointer format (When/Read/Constraints/Key Rules)
Step 7: Add to .claude/patterns/api/INDEX.md
Step 8: Update skill mappings:
        - adr-aware: Add "Rate Limiting" → ADR-023 to Rule 1
        - task-execute: Add rate-limiting pattern to Step 4b
        - task-create: Add to Step 3.4 api row
Step 9: Add to CLAUDE.md ADR table (if high-impact)
Step 10: Add to CROSS-REFERENCE-MAP.md
Step 11: Grep verify: Search "ADR-023" appears consistently
```

---

## Automation Opportunity

**Future Enhancement**: Create a script that:
1. Scans all INDEX.md files for completeness
2. Checks path references for correctness
3. Validates skill mapping consistency
4. Reports missing cross-references

Location: `scripts/Validate-AIProcedures.ps1` (to be created)

---

## Related Skills

- **adr-aware**: Consumes ADR mappings this skill maintains
- **task-execute**: Uses constraint/pattern mappings
- **task-create**: Uses knowledge mapping tables
- **repo-cleanup**: Can validate procedure file structure

---

*Last updated: December 25, 2025*
