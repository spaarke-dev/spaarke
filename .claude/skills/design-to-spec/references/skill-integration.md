# design-to-spec — position in the project lifecycle

Background for readers of the skill. Nothing here is needed to run a step; the rules it implies are inline in
`SKILL.md` (Constraints, Step 6).

## Workflow position

```
use-case-to-design (optional, Tier 0)  → design.md
        │
        ▼
Human design document (design.md / .docx / .pdf / notes)
        │
        ▼
┌──────────────────┐
│  design-to-spec  │  ← this skill (Tier 1 component, developer-facing)
└──────────────────┘
        │
        ▼
projects/{name}/spec.md (AI-optimized)
        │
        ▼
┌──────────────────┐
│ project-pipeline │  ← next skill (Tier 2 orchestrator)
└──────────────────┘
        │
        ├─→ project-setup (README, plan.md, CLAUDE.md)
        └─→ task-create (task files)
        ▼
README.md, plan.md, tasks/, …
```

## Skills involved

| Skill | Relationship |
|---|---|
| `adr-aware` | Used implicitly in Step 3 to load ADR constraints |
| `researcher` (subagent) | Optional, for Microsoft / AI platform questions that come up mid-spec (root CLAUDE.md §15) |
| `devops-project-sync` | Called at the end of Step 4 when the project has a portfolio pointer block |
| `project-pipeline` | Consumes spec.md; optionally invoked by Step 6 |
| `project-setup` | Consumes spec.md, but only as called by project-pipeline |
| `task-create` | Needs plan.md, which only exists after project-pipeline Step 2 |

## Why not call the component skills directly

After design-to-spec, run `/project-pipeline projects/{project-name}`. It performs comprehensive resource discovery
(ADRs, skills, knowledge docs, code patterns), artifact generation (README, plan.md, CLAUDE.md), task decomposition,
branch creation and GitHub registration, then starts task 001.

```bash
# ❌ WRONG - missing resource discovery and tasks
/project-setup projects/{project-name}

# ❌ WRONG - can't create tasks without plan.md
/task-create projects/{project-name}
```

Component skills are called BY project-pipeline, not by developers.

## Portfolio hook history

The end-of-Step-4 portfolio sync was added on 2026-06-23 by spaarke-devops-project-tracking-r1 (task 030, FR-16).
It is silent on success, warns on failure and never blocks design-to-spec (that project's NFR-03). See
`.claude/skills/devops-project-sync/SKILL.md`.
