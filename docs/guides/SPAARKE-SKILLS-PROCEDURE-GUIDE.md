# Spaarke AI-Directed Coding: Multi-Skill Framework

> **Last Updated**: December 4, 2025  
> **Applies To**: Claude Code skill design, creation, and usage

---

## Overview

This document defines Spaarke's approach to designing, creating, and using Claude Code skills. Skills are structured bundles of instructions and resources that Claude loads dynamically to perform specific workflows with consistency and quality. Our goal is to encode team expertise into reusable, discoverable skills that accelerate development while maintaining standards.

A skill prepares Claude to solve a problem rather than solving it directly. When invoked, Claude loads the skill's instructions into context, gains access to bundled scripts and references, and proceeds with enriched understanding of how to approach the task.

Skills differ from simple slash commands in that they:
- Provide richer context through multiple files
- Can include executable scripts and reference documentation
- Are discovered automatically based on conversational context rather than requiring explicit invocation

---

## Skill Architecture

### File Structure

Each skill is an independent folder containing a SKILL.md file and optional supporting resources:

```
.claude/skills/
└── [skill-name]/
    ├── SKILL.md              # Core instructions (required, under 5k words)
    ├── scripts/              # Executable automation (optional)
    │   └── *.sh, *.py
    ├── references/           # Documentation loaded into context (optional)
    │   └── *.md
    └── assets/               # Templates and static files (optional)
        └── *.md, *.json
```

### SKILL.md Format

```yaml
---
description: Brief description for skill discovery
allowed-tools:
  - bash
  - read
  - edit
---
```

```markdown
# Skill Name

## Purpose
What this skill accomplishes and when to use it.

## Workflow
Step-by-step process Claude should follow.

## Conventions
Standards and patterns to apply.

## Resources
References to bundled scripts, references, and assets.

## Examples
Example inputs and expected outputs.
```

The `description` field is critical—Claude uses it to match incoming requests to the appropriate skill.

---

## How Skills Are Invoked

Skills are invoked through natural conversation—Claude matches your request to the appropriate skill based on the description and context.

### Automatic Discovery

When you describe a task, Claude scans available skills and loads the one whose description best matches your intent. This happens transparently:

```
Developer: "I need to set up a new project for the payment gateway refactor"
Claude: [loads project-init skill] "I'll initialize a new project folder..."

Developer: "Review this authentication module for security issues"
Claude: [loads code-review skill] "I'll review the code against our security checklist..."

Developer: "Break down the plan into tasks"
Claude: [loads task-create skill] "I'll decompose plan.md into numbered task files..."
```

The skill's `description` field drives this matching, which is why writing clear, action-oriented descriptions matters.

### Explicit Invocation

You can also reference a skill directly by name for precision:

```
Developer: "Use the api-design skill to help me design the webhook endpoints"
Claude: [loads api-design skill] "I'll follow our API design conventions..."

Developer: "Apply spaarke-conventions to the files I just created"
Claude: [loads spaarke-conventions skill] "I'll review against our standards..."
```

### What Happens When a Skill Loads

When Claude invokes a skill:

1. **SKILL.md loads into context** - The core instructions become part of Claude's working memory for this task
2. **Resources become accessible** - Scripts, references, and assets in the skill folder can be read and executed
3. **Tool permissions apply** - Only the tools listed in `allowed-tools` are available during skill execution
4. **Workflow begins** - Claude follows the steps defined in the skill's Workflow section

### Chaining Skills

Skills can be used sequentially within a session. A typical project lifecycle might flow:

```
1. "Start a new project for auth refactor"     → project-init
2. "Break this into tasks"                      → task-create
3. [developer works on implementation]
4. "Review the token service code"              → code-review
5. "Generate tests for the middleware"          → test-generation
6. "Create a PR for this work"                  → pr-workflow
```

Each skill loads fresh—they don't persist across invocations, keeping context clean.

### When Skills Don't Match

If no skill matches your request, Claude proceeds with its general capabilities plus any context from CLAUDE.md files. Skills enhance specific workflows; they don't replace Claude's baseline abilities.

If Claude selects the wrong skill, redirect explicitly:

```
Developer: "No, use the api-design skill instead"
Claude: [switches to api-design skill] "I'll approach this as an API design task..."
```

---

## Spaarke Skill Categories

We organize skills into four categories aligned with our development workflow:

### Project & Planning Skills

Skills for project initialization, task decomposition, and progress tracking. These integrate with our `/projects` directory structure and task file conventions.

### Development Skills

Skills for code generation, refactoring, and implementation. These encode Spaarke coding standards, architectural patterns, and technology-specific conventions.

### Quality & Review Skills

Skills for code review, testing, security scanning, and compliance checking. These reference our quality standards and automate verification steps.

### Operations Skills

Skills for git workflows, CI/CD integration, deployment, and release management. These encode our branching strategy and operational procedures.

---

## Planned Skills

| Skill | Category | Purpose |
|-------|----------|---------|
| `project-init` | Project | Initialize new project folder with README, CLAUDE.md, plan.md, and task structure |
| `task-create` | Project | Decompose plan.md into numbered task files with acceptance criteria |
| `spaarke-conventions` | Development | Apply Spaarke coding standards, naming, and architectural patterns |
| `api-design` | Development | Design REST/GraphQL APIs following Spaarke patterns |
| `code-review` | Quality | Comprehensive review for security, performance, and style |
| `test-generation` | Quality | Generate unit/integration tests following our testing conventions |
| `pr-workflow` | Operations | Create branches, commits, and PRs following git conventions |
| `release-prep` | Operations | Prepare releases with changelog, version bumps, and checks |

---

## Skill Design Principles

### Concise Core, Rich References

Keep SKILL.md under 5,000 words to avoid overwhelming context. Move detailed checklists, patterns, and examples into the `references/` directory where Claude can access them as needed.

### Actionable Instructions

Write instructions as concrete steps Claude can execute, not abstract guidelines. Include specific file paths, command examples, and expected outputs.

### Discoverable Descriptions

Write descriptions that match how developers naturally phrase requests. "Initialize a new project" should trigger `project-init`; "review this code" should trigger `code-review`.

### Self-Contained Resources

Bundle everything the skill needs—scripts, templates, reference docs. Don't assume Claude has access to external documentation or remembers context from other sessions.

### Composable Workflows

Design skills to work independently but compose well. A developer might use `project-init`, then `task-create`, then `spaarke-conventions` across a single project lifecycle.

---

## Skill Creation Process

### Step 1: Identify the Workflow

Document the manual process: what triggers it, what steps are involved, what decisions are made, what outputs are produced.

### Step 2: Design the Structure

Determine what belongs in SKILL.md versus references, what scripts could automate steps, and what templates would help.

### Step 3: Write SKILL.md

Start with the frontmatter (description, allowed-tools), then write Purpose, Workflow, Conventions, and Resources sections.

### Step 4: Bundle Resources

Create supporting files in `scripts/`, `references/`, and `assets/`. Reference them explicitly in SKILL.md.

### Step 5: Test and Refine

Use the skill in real workflows, note where Claude deviates or struggles, and refine instructions iteratively.

---

## Skill Creation Efficiency

For efficient skill creation, we use a layered approach: a simple template for quick starts, plus a reference guide for deeper customization.

### Template File

The file `.claude/skills/_templates/SKILL-TEMPLATE.md` serves as a copy-and-modify starting point:

```yaml
---
description: [Brief phrase matching how developers will request this]
allowed-tools:
  - bash
  - read
  - edit
---
```

```markdown
# [Skill Name]

## Purpose
[1-2 sentences: what this skill accomplishes and when to use it]

## Workflow
1. [First step Claude should take]
2. [Second step]
3. [Continue as needed]

## Conventions
- [Pattern or standard to follow]
- [Another convention]

## Resources
- `scripts/[name].sh` - [what it does]
- `references/[name].md` - [what it contains]
- `assets/[name].md` - [template for what]

## Examples
**Input:** [Example request that triggers this skill]

**Output:** [What Claude should produce]
```

### Starter Directory Structure

The folder `.claude/skills/_templates/skill-starter/` provides a copyable structure:

```
.claude/skills/_templates/
├── SKILL-TEMPLATE.md           # Copy for new SKILL.md
└── skill-starter/              # Copy entire folder for new skill
    ├── SKILL.md
    ├── scripts/
    │   └── .gitkeep
    ├── references/
    │   └── .gitkeep
    └── assets/
        └── .gitkeep
```

### Creation Workflow

To add a new skill:

```bash
# Copy the starter structure
cp -r .claude/skills/_templates/skill-starter .claude/skills/[new-skill-name]

# Edit the SKILL.md
code .claude/skills/[new-skill-name]/SKILL.md

# Add supporting resources as needed
# Commit when ready
```

### Skill Creation Checklist

When creating a new skill, verify:

- [ ] Description matches natural language requests
- [ ] **YAML frontmatter includes tags, techStack, appliesTo** (see `.claude/skills/INDEX.md` for standard vocabulary)
- [ ] SKILL.md is under 5,000 words
- [ ] Workflow steps are concrete and executable
- [ ] All referenced scripts/resources exist
- [ ] `allowed-tools` includes only what's needed
- [ ] Tested with representative requests
- [ ] **Skill added to `.claude/skills/INDEX.md`** with tags listed

**Tagging Requirements:**
- Use standard tag vocabulary from `.claude/skills/INDEX.md`
- Include relevant technology stack values (aspnet-core, react, dataverse, etc.)
- Specify file patterns or scenarios where skill applies
- Only set `alwaysApply: true` for universal skills like conventions

---

## Integration with Repository Structure

Skills reference and reinforce our repository conventions:

```
spaarke/
├── .claude/
│   ├── skills/                       # All skills live here
│   │   ├── _templates/               # Creation resources (not loaded as skills)
│   │   │   ├── SKILL-TEMPLATE.md
│   │   │   └── skill-starter/
│   │   ├── project-init/             # Actual skills
│   │   ├── task-create/
│   │   ├── code-review/
│   │   └── ...
│   ├── commands/                     # Simple slash commands
│   └── settings.json                 # Shared settings
├── projects/                         # Skills create/manage project folders
│   └── [project-name]/
│       ├── CLAUDE.md                 # project-init creates this
│       ├── README.md
│       ├── plan.md                   # project-init creates this
│       └── tasks/                    # task-create populates this
│           ├── _index.md
│           └── 01-[task-name].md
├── src/                              # Development skills work here
├── tests/                            # test-generation skill outputs here
└── CLAUDE.md                         # Root context for all skills
```

The `_templates/` folder uses an underscore prefix to sort it separately and signal it's not a real skill—Claude will ignore it during discovery.

---

## Skill Discovery Locations

Claude discovers skills from these locations:

| Location | Scope | Committed |
|----------|-------|-----------|
| `.claude/skills/` | Project-specific, shared with team | Yes |
| `~/.claude/skills/` | Personal, all projects | No |

For Spaarke, all team skills should live in `.claude/skills/` and be committed to the repository.

---

## Instructions for AI Agent

When creating skills for this repository:

1. Read the root CLAUDE.md to understand Spaarke conventions before creating any skills
2. Follow the file structure defined in this document exactly
3. Copy `.claude/skills/_templates/skill-starter/` as the starting point for each new skill
4. Keep SKILL.md concise; move detailed content to `references/`
5. Write descriptions that enable automatic discovery based on natural language requests
6. Include concrete examples and expected outputs in the Workflow and Examples sections
7. Reference Spaarke conventions and link to relevant documentation in `/docs`
8. Test skills by simulating the described workflow with representative requests
9. Commit skills to `.claude/skills/` for team access

### Priority Order for Initial Skills

Create skills in this order to establish the core workflow:

1. **project-init** - Enables new project creation
2. **task-create** - Enables task decomposition from plans
3. **spaarke-conventions** - Establishes coding standards baseline
4. **code-review** - Enables quality checks
5. **pr-workflow** - Enables git workflow automation

Remaining skills can be added as workflows mature.

---

## Resources

- [Anthropic Claude Code Best Practices](https://www.anthropic.com/engineering/claude-code-best-practices)
- [Claude Code Documentation](https://docs.anthropic.com/en/docs/claude-code)
- [Spaarke Development Procedures](/docs/development-procedures.md)
- [Spaarke Repository Structure](/docs/repository-structure.md)
