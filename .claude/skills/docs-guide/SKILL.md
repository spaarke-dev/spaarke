---
description: Draft or update an operational guide for a Spaarke functional module
tags: [documentation, guide]
techStack: []
appliesTo: ["write guide", "update guide", "draft guide", "docs-guide"]
alwaysApply: false
---

# Guide Document Skill

> **Category**: Documentation
> **Last Updated**: April 2026

---

## Purpose

Draft or update an operational guide in `docs/guides/`. Guides describe **how to do things** — configuration procedures, deployment steps, environment setup, functional workflows, and troubleshooting.

**Guides are NOT:**
- Component structure or data flow descriptions (→ use `/docs-architecture`)
- Design decision rationale (→ use `/docs-architecture`)
- Code patterns or conventions (→ `.claude/patterns/` pointers)
- ADR constraints (→ `.claude/adr/`)

---

## When to Use

- "write guide for {feature/procedure}"
- "update guide for {feature/procedure}"
- "document how to {do something}"
- Creating a new guide identified in the documentation requirements table
- Updating a guide with stale procedures

---

## Document Structure (MANDATORY)

Every guide MUST follow this structure. Sections can be omitted if genuinely not applicable, but the order is fixed.

```markdown
# {Feature/Procedure Name} Guide

> **Last Updated**: {date}
> **Purpose**: {one-sentence description of what this guide helps you do}
> **Audience**: {who uses this — developer, admin, operator}

---

## Prerequisites

{What must be in place before following this guide.
Include tool versions, access requirements, environment state.}

- [ ] {Prerequisite 1}
- [ ] {Prerequisite 2}

## Quick Reference

{Table of key values, endpoints, paths, or commands needed for this procedure.
This section should let someone who's done this before skip straight to the action.}

| Item | Value |
|------|-------|
| {Key item} | {Value} |

## Procedure

{Step-by-step instructions. Number every step.
Include expected output or verification after significant steps.
Use bash/powershell code blocks for commands.}

### Step 1: {Action}

{Description of what to do and why.}

```bash
{command}
```

**Expected output**: {what you should see}

### Step 2: {Action}

...

## Configuration

{If the feature has configurable settings, document them here.
Include setting name, location, allowed values, and default.}

| Setting | Location | Default | Description |
|---------|----------|---------|-------------|
| {Name} | {File or UI path} | {Default} | {What it controls} |

## Verification

{How to confirm the procedure succeeded.
Include specific checks, not just "verify it works."}

1. {Check 1} — expected result: {X}
2. {Check 2} — expected result: {Y}

## Troubleshooting

{Common problems and their fixes.
Use symptom → cause → fix format.}

| Symptom | Cause | Fix |
|---------|-------|-----|
| {What you see} | {Why it happens} | {How to fix it} |

## Related

- [{Architecture doc}](../architecture/{file}.md) — technical details of how this works
- [{Other guide}](../guides/{file}.md) — related procedure
```

---

## Drafting Process

### Step 1: Understand the Procedure

```
BEFORE writing ANY content:

1. IDENTIFY the procedure or feature being documented
   - What does the user/developer/admin need to accomplish?
   - What tools, scripts, or UI are involved?

2. READ the relevant code:
   - Scripts in scripts/ that automate this procedure
   - Configuration classes that define settings
   - Entry points that this procedure configures or deploys

3. READ existing related docs:
   - Architecture doc for the subsystem (understand the technical context)
   - Existing deployment skills (.claude/skills/*-deploy/)
   - scripts/README.md for available automation

4. IDENTIFY the audience:
   - Developer (modifying code, running builds)
   - Admin (configuring environments, managing access)
   - Operator (deploying, monitoring, troubleshooting)
```

### Step 2: Draft the Guide

```
WRITE the guide following the mandatory structure above.

RULES:
- Lead with WHAT TO DO, not WHY it was designed this way
- Every step should be actionable — a command, a click, a configuration
- Include verification after significant steps
- Use actual commands, paths, and values (not placeholders where possible)
- Reference architecture docs for "why" — don't explain design in guides
- Keep total length proportional to procedure complexity:
  - Simple (3-5 steps): 100-200 lines
  - Medium (5-10 steps): 200-500 lines
  - Complex (10+ steps, multi-stage): 500-1000 lines
  - Never exceed 1200 lines — split into sub-guides if needed

DO NOT INCLUDE:
- Component structure diagrams (belongs in architecture)
- Design decision rationale (belongs in architecture)
- Data flow descriptions (belongs in architecture)
- Full source code listings
- Historical context or evolution narratives

GUIDELINES FOR VALUES AND PATHS:
- Use actual resource names from CLAUDE.md (not placeholders) for dev environment
- Use {placeholder} syntax for values that vary per environment
- Reference scripts/ instead of writing inline automation
- Point to .claude/skills/ for complex deployment procedures
```

### Step 3: Cross-Reference

```
AFTER writing:

1. VERIFY all commands work (or are syntactically correct)
2. VERIFY all file paths exist
3. VERIFY settings/config keys match the actual options classes
4. CHECK if an architecture doc exists for this subsystem — add cross-reference
5. CHECK if a deployment skill exists — reference it instead of duplicating steps
6. ADD to docs/guides/INDEX.md
```

---

## Updating Existing Guides

When updating (not creating) a guide:

```
1. READ the current guide
2. IDENTIFY what changed:
   - New steps added to the procedure?
   - Steps removed or reordered?
   - Configuration keys/values changed?
   - Tools or scripts changed?
3. UPDATE only the sections that are stale
4. VERIFY commands and paths still work
5. UPDATE the "Last Updated" date
6. DO NOT rewrite sections that are still accurate
```

---

## Handling Environment-Specific Values

```
RULES for environment values:

1. Dev environment values: Use actual values from CLAUDE.md
   Example: "App Service: spe-api-dev-67e2xz"

2. Production/other environments: Use {placeholder} syntax
   Example: "App Service: {app-service-name}"

3. Secrets: NEVER include actual secret values
   Example: "Retrieve from Key Vault: az keyvault secret show --name {secret-name}"

4. URLs that change per environment: Show dev as example, note it varies
   Example: "https://spe-api-dev-67e2xz.azurewebsites.net (dev — varies per environment)"
```

---

## Quality Checklist

Before finalizing any guide:

- [ ] Every step is actionable (command, click, or configuration)
- [ ] Verification steps follow significant actions
- [ ] All file paths resolve to existing files
- [ ] All commands are syntactically correct
- [ ] Configuration keys match actual options classes in code
- [ ] No design rationale or component descriptions (those belong in architecture)
- [ ] Prerequisites are complete — nothing assumed
- [ ] Troubleshooting covers the top 3-5 failure modes
- [ ] Related section cross-references the architecture doc (if one exists)
- [ ] INDEX.md updated with new/modified entry

---

## Examples of Good vs Bad

**Good**: "Run the deployment script from the repo root:\n```powershell\n.\\scripts\\Deploy-BffApi.ps1\n```\nExpected output: Package size ~61 MB, health check passed."

**Bad**: "The BFF API uses a minimal API pattern with endpoint filters for authorization, following ADR-008..." (→ this belongs in an architecture doc)

**Good**: "| `Redis:Enabled` | `appsettings.json` | `false` | Set to `true` in production to use Redis instead of in-memory cache |"

**Bad**: "Redis caching follows ADR-009's Redis-first principle, where the DistributedCacheExtensions class provides GetOrCreate..." (→ this belongs in an architecture doc or pattern pointer)

---

*This skill ensures guides are consistent, actionable, and focused on procedures rather than architectural descriptions.*
