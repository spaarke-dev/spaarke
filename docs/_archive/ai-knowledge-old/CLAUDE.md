# AI Knowledge Documentation

> **Last Updated**: December 9, 2025
>
> **Purpose**: Index of authoritative, coding-relevant documentation.

---

## Document Metadata

**All documents in this directory SHOULD include YAML frontmatter for discoverability:**

```yaml
---
title: Brief document title
category: architecture | standards | guides | templates
tags: [tag1, tag2, tag3]  # Keywords for discovery (use standard vocabulary)
techStack: [tech1, tech2]  # Technologies covered (e.g., aspnet-core, dataverse, react)
appliesTo: [scenario1, scenario2]  # When to load this doc
lastUpdated: YYYY-MM-DD
---
```

**Standard Tag Vocabulary** (same as skills):
- **Project:** `project-init`, `project-structure`, `tasks`, `planning`
- **Development:** `api`, `pcf`, `plugin`, `frontend`, `backend`, `dataverse`, `dynamics`
- **Azure/AI:** `azure`, `openai`, `ai`, `embeddings`, `semantic-kernel`
- **Operations:** `deploy`, `git`, `ci-cd`, `devops`, `auth`, `security`
- **Quality:** `testing`, `security`, `performance`, `code-review`, `troubleshooting`

See `.claude/skills/INDEX.md` for complete vocabulary.

---

## Quick Reference

This directory contains **condensed, actionable content** for AI-directed coding. Reference these documents when working on related features.

| Category | Purpose | When to Load |
|----------|---------|--------------|
| `architecture/` | System patterns, boundaries, data flow | Starting new features, debugging auth/API |
| `standards/` | Coding patterns, OAuth/OBO, Dataverse auth | Implementing auth, code review |
| `guides/` | Step-by-step procedures | Specific how-to tasks |
| `templates/` | Project/task scaffolding | Starting projects, creating tasks |

---

## Related: Principles Documentation

**Critical rules are embedded in root CLAUDE.md.** Full documentation is in `docs/reference/`:

| Type | Location | Purpose |
|------|----------|---------|
| **ADRs** | `docs/reference/adr/` | Architecture principles (how the *system* is built) |
| **AIPs** | `docs/reference/protocols/` | AI behavior principles (how the *AI agent* works) |

Both follow the same pattern: critical rules embedded in CLAUDE.md, full docs in `reference/` for detailed context.

---

## Architecture

System design patterns and boundaries. Load when understanding system structure.

| Document | Applies To | Key Content |
|----------|------------|-------------|
| `sdap-overview.md` | Any SDAP work | Architecture layers, data model, component relationships |
| `sdap-auth-patterns.md` | Auth debugging | MSAL.js, OBO flow, ClientSecret, Application User |
| `sdap-pcf-patterns.md` | PCF development | EntityDocumentConfig, upload flow, preview components |
| `sdap-bff-api-patterns.md` | API development | GraphClientFactory, NavMapEndpoints, Redis caching |
| `sdap-troubleshooting.md` | Debugging | AADSTS errors, 401/404/500 responses, common fixes |
| `auth-security-boundaries.md` | Security review | 6 trust zones, token validation, threat mitigations |
| `auth-azure-resources.md` | Config lookup | GUIDs, App registrations, Azure resource inventory |
| `auth-performance-monitoring.md` | Performance | Latency optimization, token caching, App Insights |

### Decision Tree: Architecture

```
What are you working on?
├─ SDAP feature → Load sdap-overview.md first
├─ Auth issue → Load sdap-auth-patterns.md + auth-security-boundaries.md
├─ PCF control → Load sdap-pcf-patterns.md
├─ BFF API → Load sdap-bff-api-patterns.md
└─ Performance → Load auth-performance-monitoring.md
```

---

## Standards

Coding patterns and authentication standards. Load when implementing or reviewing code.

| Document | Applies To | Key Content |
|----------|------------|-------------|
| `oauth-obo-implementation.md` | Implementing OBO | `AcquireTokenOnBehalfOf`, token caching patterns |
| `oauth-obo-errors.md` | OBO debugging | AADSTS50013, AADSTS70011, invalid_grant fixes |
| `oauth-obo-anti-patterns.md` | Code review | Token audience, `.default` scope, caching mistakes |
| `dataverse-oauth-authentication.md` | Dataverse Web API | `ServiceClient`, `DelegatingHandler`, connection strings |

### Decision Tree: OBO Flow

```
OBO flow failing?
├─ AADSTS50013 → Token audience mismatch (oauth-obo-errors.md)
├─ AADSTS70011 → Wrong scope format (oauth-obo-errors.md)
├─ AADSTS65001 → Missing consent (oauth-obo-errors.md)
├─ invalid_grant → User token expired (oauth-obo-errors.md)
└─ Code review → Load oauth-obo-anti-patterns.md
```

---

## Guides

Step-by-step procedures for specific tasks. Load when performing that task.

| Document | Applies To | Key Content |
|----------|------------|-------------|
| `SPAARKE-AI-ARCHITECTURE.md` | AI feature implementation | BFF API extension, services, job handlers, ADR compliance |
| `SPAARKE-SKILLS-PROCEDURE-GUIDE.md` | Claude Code skills | Skill creation, SKILL.md format, discovery |
| `HOW-TO-ADD-SDAP-TO-NEW-ENTITY.md` | Adding entities | Entity configuration, PCF binding |
| `HOW-TO-SETUP-CONTAINERTYPES-AND-CONTAINERS.md` | SPE setup | Container types, registration |
| `DATAVERSE-AUTHENTICATION-GUIDE.md` | Dataverse auth setup | OAuth configuration, app registration |
| `PCF-V9-PACKAGING.md` | PCF deployment | Build, package, platform libraries, bundle size |
| `RIBBON-WORKBENCH-HOW-TO-ADD-BUTTON.md` | Ribbon customization | Adding buttons to Dataverse forms |

> **Skills**: For Dataverse deployment tasks, also load the skill:
> - PCF/solution deployment → `.claude/skills/dataverse-deploy/SKILL.md`
> - Ribbon editing → `.claude/skills/ribbon-edit/SKILL.md`

### Decision Tree: Common Tasks

```
What do you need to do?
├─ Implement AI features → SPAARKE-AI-ARCHITECTURE.md
├─ Add SDAP to new entity → HOW-TO-ADD-SDAP-TO-NEW-ENTITY.md
├─ Create Claude skill → SPAARKE-SKILLS-PROCEDURE-GUIDE.md
├─ Deploy PCF control → .claude/skills/dataverse-deploy/SKILL.md + PCF-V9-PACKAGING.md
├─ Set up SPE containers → HOW-TO-SETUP-CONTAINERTYPES-AND-CONTAINERS.md
├─ Configure ribbon button → .claude/skills/ribbon-edit/SKILL.md
└─ Import/export solutions → .claude/skills/dataverse-deploy/SKILL.md
```

---

## Templates

Scaffolding for projects, tasks, and documentation. Use when creating new items.

| Template | Purpose | When to Use |
|----------|---------|-------------|
| `AI-AGENT-PLAYBOOK.md` | Process design specs | Starting from a design specification |
| `project-README.template.md` | Project documentation | Creating new project README |
| `project-plan.template.md` | Project planning | Breaking down features into phases |
| `task-execution.template.md` | Task tracking | Executing individual tasks |
| `ai-knowledge-article.template.md` | Knowledge articles | Creating new AI knowledge content |

---

## Loading Strategy

### For New Features

1. Load `architecture/sdap-overview.md` for context
2. Load relevant architecture doc for the specific area
3. Load applicable standards (auth patterns if auth-related)
4. Reference guides as needed during implementation

### For Debugging

1. Check `architecture/sdap-troubleshooting.md` first
2. Load error-specific docs (e.g., `oauth-obo-errors.md`)
3. Reference `auth-azure-resources.md` for config verification

### For Code Review

1. Load `standards/oauth-obo-anti-patterns.md`
2. Cross-reference with relevant architecture patterns
3. Verify against ADRs in `/docs/reference/adr/` if architectural concern

---

## What's NOT Here

The following are in `/docs/reference/` and should **not** be loaded unless explicitly requested:

- **ADRs** - Historical decisions (use for "why" questions only)
- **KM-* articles** - Verbose reference material
- **Full architecture guides** - Comprehensive but context-heavy

If you need this content, ask the developer first.

---

## See Also

- `/docs/CLAUDE.md` - Documentation traffic controller
- `/docs/reference/CLAUDE.md` - Reference material (don't load unless asked)
- Root `/CLAUDE.md` - Repository-wide coding standards
