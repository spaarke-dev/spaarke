# design-to-spec

---
description: Transform human design documents into AI-optimized spec.md files
tags: [project-init, design, spec, planning, transformation]
techStack: [all]
appliesTo: ["projects/*/design.md", "projects/*/design.docx", "transform spec", "design to spec"]
alwaysApply: false
---

## Purpose

**Tier 1 Component Skill** - Transforms verbose human design documents into structured, AI-optimized `spec.md` files that the `project-pipeline` skill can consume.

**Key Features**:
- Ingests various design document formats (markdown, Word, PDF, rough notes)
- Extracts and structures requirements, scope, and acceptance criteria
- Enriches with ADR references, file paths, and technical constraints
- Flags ambiguities for human resolution
- Outputs standardized spec.md for project-pipeline consumption

**Why This Skill Exists**:
- Human design docs are narrative-heavy, missing AI-needed context
- AI agents need: explicit constraints, file paths, ADR references, scope boundaries
- Manual spec.md creation is error-prone and time-consuming

## When to Use

- User says "transform spec", "design to spec", or "create AI spec"
- Explicitly invoked with `/design-to-spec {project-path}`
- A design document exists at `projects/{project-name}/design.md` (or `.docx`, `.pdf`)
- Before running `project-pipeline` (this skill feeds into that workflow)

## Input/Output

**Input** (one of):
- `projects/{project-name}/design.md` - Markdown design doc
- `projects/{project-name}/design.docx` - Word document
- `projects/{project-name}/design.pdf` - PDF document
- User-provided text/notes (via conversation)

**Output**:
- `projects/{project-name}/spec.md` - AI-optimized spec file
- Both files kept as project artifacts

## Workflow Position

```
Human Design Document
    │
    ▼
┌──────────────────┐
│  design-to-spec  │  ← THIS SKILL
│  (Tier 1)        │
└──────────────────┘
    │
    ▼
projects/{name}/spec.md (AI-optimized)
    │
    ▼
┌──────────────────┐
│ project-pipeline │  ← NEXT SKILL
│ (Tier 2)         │
└──────────────────┘
    │
    ▼
README.md, PLAN.md, tasks/, etc.
```

---

## Steps

### Step 1: Locate Design Document

**Action:**
```
SEARCH for design document at:
  1. projects/{project-name}/design.md
  2. projects/{project-name}/design.docx
  3. projects/{project-name}/design.pdf
  4. projects/{project-name}/*.md (if only one non-spec markdown)

IF not found:
  → ASK user: "Where is your design document?"
  → Accept: file path, pasted text, or description

IF found:
  → READ document content
  → Report: "Found design document: {filename} ({word-count} words)"
```

---

### Step 2: Extract Core Elements

**Action:**
```
EXTRACT from design document:

1. PURPOSE
   - What problem does this solve?
   - What is the business value?
   - Who are the users/stakeholders?

2. SCOPE
   - What's IN scope (explicit features/changes)
   - What's OUT of scope (explicit exclusions)
   - System boundaries

3. REQUIREMENTS
   - Functional requirements (what it must do)
   - Non-functional requirements (performance, security, etc.)
   - Technical constraints

4. SUCCESS CRITERIA
   - How do we know it's done?
   - Acceptance criteria
   - Quality gates

5. TECHNICAL APPROACH (if present)
   - Architecture decisions
   - Technology choices
   - Integration points

FLAG any missing elements for human input
```

**Output to User:**
```
📋 Extracted from design document:

✅ Found:
   - Purpose: [summary]
   - Scope: {X} features in scope
   - Requirements: {Y} functional, {Z} non-functional
   - Success criteria: {N} criteria identified

⚠️ Missing/Unclear (need your input):
   - [ ] Out-of-scope not explicitly stated
   - [ ] No performance requirements specified
   - [ ] Integration points unclear

Would you like to clarify these now? [Y to clarify / skip to proceed with assumptions]
```

**Wait for User**: `y` (clarify) | `skip` (proceed with gaps noted)

---

### Step 3: Discover Technical Context

**Action:**
```
ANALYZE extracted content for:

1. RESOURCE TYPES
   - API endpoints → Load ADR-001, ADR-008, ADR-010, ADR-019
   - PCF controls → Load ADR-006, ADR-011, ADR-012
   - Plugins → Load ADR-002
   - Storage → Load ADR-005, ADR-007, ADR-009
   - AI features → Load ADR-013, ADR-014, ADR-015, ADR-016
   - Background jobs → Load ADR-004, ADR-017

2. EXISTING CODE AREAS
   - Search codebase for related files
   - Identify patterns to follow
   - Find canonical implementations

3. CONSTRAINTS
   - Extract MUST/MUST NOT from applicable ADRs
   - Load relevant constraint files from .claude/constraints/

4. KNOWLEDGE DOCS
   - Search docs/guides/ for relevant procedures
   - Search docs/adr/ for architectural context

OUTPUT: Technical context summary
```

---

### Step 4: Generate Structured spec.md

**Action:**
```
CREATE: projects/{project-name}/spec.md

FOLLOW template structure:

# {Project Name} - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: {date}
> **Source**: {design-document-filename}

## Executive Summary

{2-3 sentences from extracted PURPOSE}

## Scope

### In Scope
{Bulleted list of features/changes}

### Out of Scope
{Explicit exclusions - ASK if not in design doc}

### Affected Areas
{List of code areas, with file paths where known}
- `src/server/api/...` - {description}
- `src/client/pcf/...` - {description}

## Requirements

### Functional Requirements
{Numbered list with clear acceptance criteria}
1. **FR-01**: {requirement} - Acceptance: {criteria}
2. **FR-02**: {requirement} - Acceptance: {criteria}

### Non-Functional Requirements
{Performance, security, accessibility, etc.}
- **NFR-01**: {requirement}

## Technical Constraints

### Applicable ADRs
{List ADRs that MUST be followed}
- **ADR-001**: Minimal API pattern required
- **ADR-008**: Use endpoint filters for authorization

### MUST Rules (from ADRs)
{Extracted key constraints}
- ✅ MUST use...
- ✅ MUST NOT...

### Existing Patterns to Follow
{Reference to canonical implementations}
- See `src/server/api/.../ExampleEndpoints.cs` for endpoint pattern
- See `.claude/patterns/api/` for detailed patterns

## Success Criteria

{Numbered list with verification method}
1. [ ] {criterion} - Verify by: {method}
2. [ ] {criterion} - Verify by: {method}

## Dependencies

### Prerequisites
{What must exist/be done first}

### External Dependencies
{APIs, services, approvals needed}

## Questions/Clarifications Needed

{Any unresolved items from design doc - KEEP THIS SECTION}
- [ ] {question}
- [ ] {question}

---

*AI-optimized specification. Original design: {filename}*
```

---

### Step 5: Present for Review

**Action:**
```
OUTPUT spec.md content to user

SHOW summary:
  - Word count
  - Number of requirements
  - Number of ADRs referenced
  - Number of unresolved questions

ASK for review:
  "Please review the spec.md. Any changes needed before proceeding to project-pipeline?"
```

**Output to User:**
```
✅ spec.md generated:
   - {X} words
   - {Y} functional requirements
   - {Z} ADRs referenced
   - {N} unresolved questions flagged

📄 File created: projects/{project-name}/spec.md

📋 Next Steps:
   1. Review spec.md for accuracy
   2. Resolve any flagged questions
   3. Run: /project-pipeline {project-name}

[Y to proceed to project-pipeline / edit to make changes / done to finish]
```

**Wait for User**: `y` (proceed to pipeline) | `edit` (make changes) | `done` (stop here)

---

### Step 6: Handoff to project-pipeline (Optional)

**Action:**
```
IF user said 'y':
  → INVOKE project-pipeline projects/{project-name}
  → Handoff message: "Starting project-pipeline with generated spec.md..."

IF user said 'done':
  → OUTPUT: "spec.md ready at projects/{project-name}/spec.md
             Run /project-pipeline {project-name} when ready."
```

---

## spec.md Template

The generated spec.md follows this structure (also saved at `docs/ai-knowledge/templates/spec.template.md`):

```markdown
# {Project Name} - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: {YYYY-MM-DD}
> **Source**: {original-design-document}

## Executive Summary

{Brief description of what this project delivers and why}

## Scope

### In Scope
- {Feature/change 1}
- {Feature/change 2}

### Out of Scope
- {Explicit exclusion 1}
- {Explicit exclusion 2}

### Affected Areas
- `{path}` - {description}

## Requirements

### Functional Requirements
1. **FR-01**: {Requirement} - Acceptance: {Criteria}

### Non-Functional Requirements
- **NFR-01**: {Requirement}

## Technical Constraints

### Applicable ADRs
- **ADR-XXX**: {Brief relevance}

### MUST Rules
- ✅ MUST {constraint}
- ❌ MUST NOT {constraint}

### Existing Patterns
- See `{path}` for {pattern}

## Success Criteria
1. [ ] {Criterion} - Verify: {method}

## Dependencies

### Prerequisites
- {Prerequisite}

### External
- {External dependency}

## Questions/Clarifications
- [ ] {Unresolved question}

---
*AI-optimized specification. Original: {filename}*
```

---

## Error Handling

**If design document not found:**
```
❌ No design document found at projects/{project-name}/

Looking for: design.md, design.docx, design.pdf

Options:
1. Provide file path: "the design is at {path}"
2. Paste content: "here's the design: ..."
3. Create interactively: "help me write the design"
```

**If design doc too vague:**
```
⚠️ Design document lacks sufficient detail for AI implementation.

Missing critical elements:
- [ ] Concrete requirements (what specifically to build)
- [ ] Success criteria (how to verify completion)
- [ ] Scope boundaries (what's NOT included)

Options:
1. 'clarify' - Answer questions to fill gaps
2. 'proceed' - Generate spec.md with assumptions noted
3. 'stop' - Exit and improve design document
```

---

## Integration with Other Skills

This skill feeds into the project lifecycle:

```
design-to-spec (THIS SKILL)
    │
    └─→ Generates spec.md
           │
           ▼
    project-pipeline
           │
           ├─→ project-setup (README, PLAN, CLAUDE.md)
           └─→ task-create (task files)
```

**Skills this skill may invoke:**
- `adr-aware` (implicit) - For ADR discovery during context gathering

**Skills that depend on this skill's output:**
- `project-pipeline` - Consumes spec.md
- `project-setup` - Consumes spec.md (if called directly)

---

## Success Criteria

Skill successful when:
- [ ] Design document ingested and parsed
- [ ] Core elements extracted (purpose, scope, requirements, criteria)
- [ ] Technical context discovered (ADRs, patterns, file paths)
- [ ] spec.md generated with all required sections
- [ ] Unresolved questions clearly flagged
- [ ] User reviewed and approved spec.md
- [ ] Original design document preserved alongside spec.md

---

*For Claude Code: This skill transforms human design documents into structured specs optimized for AI-driven implementation. Always preserve the original design document as a project artifact.*
