# SKILL.md Template

> **Purpose**: Copy this template when creating a new Claude Code skill.
> **Usage**: `cp -r .claude/skills/_templates/skill-starter .claude/skills/[new-skill-name]`

---

## Template

Copy everything below this line into your new `SKILL.md`:

---

```yaml
---
description: [Brief phrase (5-10 words) matching how developers will naturally request this skill]
alwaysApply: false
---
```

# [Skill Name]

> **Category**: [Project | Development | Quality | Operations]  
> **Last Updated**: [Date]

---

## Purpose

[1-2 sentences: What this skill accomplishes and when to use it. Be specific about the problem it solves.]

---

## Applies When

- [Trigger condition 1 - when should Claude load this skill?]
- [Trigger condition 2]
- [NOT applicable when... (optional - clarify boundaries)]

---

## Workflow

### Step 1: [Action Name]

[Detailed instructions for first step. Include:]
- What to check or gather
- Expected inputs/outputs
- Commands to run if applicable

### Step 2: [Action Name]

[Continue with concrete, executable steps...]

### Step 3: [Action Name]

[Each step should be unambiguous - Claude should know exactly what to do]

---

## Conventions

- [Pattern or standard to follow during this skill]
- [Naming convention, file location, or format requirement]
- [Reference to ADRs or coding standards if applicable]

---

## Resources

Bundled files in this skill folder:

| Resource | Purpose |
|----------|---------|
| `scripts/[name].sh` | [What this script automates] |
| `scripts/[name].ps1` | [PowerShell equivalent if needed] |
| `references/[name].md` | [Reference documentation loaded into context] |
| `assets/[template].md` | [Template file to copy/use] |

---

## Output Format

[Define the expected output structure. Examples:]

```markdown
## [Skill Name] Report

**Scope:** [what was processed]

### Results
- [Result category 1]
- [Result category 2]

### Next Steps
1. [Action item]
2. [Action item]
```

---

## Examples

### Example 1: [Common use case]

**Input:**
```
Developer: "[Example request that triggers this skill]"
```

**Output:**
```
[Expected output or behavior]
```

### Example 2: [Edge case or variant]

**Input:**
```
Developer: "[Alternative phrasing or scenario]"
```

**Output:**
```
[Expected output or behavior]
```

---

## Error Handling

| Situation | Response |
|-----------|----------|
| [Missing input] | [Ask user for clarification] |
| [Invalid state] | [Report error and suggest fix] |
| [Ambiguous request] | [Ask user to specify] |

---

## Related Skills

- `[related-skill-1]` - [How it relates]
- `[related-skill-2]` - [How it relates]

---

## Tips for AI

- [Specific instruction for Claude when executing this skill]
- [Common pitfall to avoid]
- [Quality check to perform before completing]
