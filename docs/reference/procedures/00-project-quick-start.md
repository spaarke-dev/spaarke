# Project Quick Start Cheat Sheet

> **One-page reference for starting and running AI-directed projects**

---

## Before You Start (Manual Steps)

| # | You Do | Output |
|---|--------|--------|
| 1 | Create project folder | `projects/{project-name}/` |
| 2 | Draft feature request | Word doc, notes, any format |
| 3 | Refine spec with AI assist | `projects/{project-name}/SPEC.md` |

---

## Start the Project

**Prompt Claude Code:**
```
start the project projects/{project-name}
```
or
```
/design-to-project projects/{project-name}
```
or (recommended)
```
/project-pipeline projects/{project-name}
```

---

## What Happens Automatically

| Phase | Claude Code Does | Output |
|-------|------------------|--------|
| Ingest | Read SPEC.md, extract requirements | Summary |
| Context | Identify ADRs, find reusable code | Constraints list |
| Generate | Create project artifacts | README.md, PLAN.md, CLAUDE.md, tasks/ |
| Validate | Cross-reference checklist | Ready confirmation |

---

## What Claude Code Prompts You For

| Prompt | Your Response | When |
|--------|---------------|------|
| "Create work branch?" | `yes` | Before generating artifacts |
| "Create draft PR?" | `yes` | After branch pushed |
| "Ready to implement?" | `go` | After validation |
| "Commit changes?" | `yes` or modify message | After code changes |
| "Run code review?" | `yes` (recommended) | Before commits |

---

## What You Do Manually

- Review generated README.md, PLAN.md, tasks
- Adjust estimates if needed
- Approve/merge PR in GitHub UI

---

## Project Lifecycle Diagram

```
YOU                          CLAUDE CODE                    GITHUB
───                          ───────────                    ──────
Create folder ─────────────►
Draft spec ────────────────►
Refine SPEC.md ────────────►
                             
"start project" ───────────► Phases 1-4 (auto)
                             ◄─── "Create branch?" 
"yes" ─────────────────────► git switch -c work/...
                             ◄─── "Create draft PR?"
"yes" ─────────────────────► gh pr create --draft ────────► Draft PR created
                             ◄─── "Ready to implement?"
"go" ──────────────────────► Execute tasks
                             ◄─── "Commit?"
"yes" ─────────────────────► git commit & push ───────────► PR updated
        ...repeat...
                             
"mark ready" ──────────────► gh pr ready ─────────────────► Ready for review
                                                            
Review in GitHub ◄──────────────────────────────────────── PR ready
Merge ─────────────────────────────────────────────────────► Merged
```

---

## Essential Commands & Skills

### Project Lifecycle
| Command | Purpose |
|---------|---------|
| `/design-to-project projects/{name}` | Full pipeline: spec → implementation |
| `/project-pipeline projects/{name}` | **Recommended**: SPEC → PLAN → tasks with human checkpoints |
| `/project-init projects/{name}` | Create project artifacts only |
| `/task-create {project-name}` | Generate task files from plan |

### Quality & Compliance
| Command | Purpose |
|---------|---------|
| `/code-review` | Review code for security, performance, style |
| `/adr-check` | Validate against Architecture Decision Records |
| `/repo-cleanup` | Repository hygiene audit (end of project) |

### Git Operations
| Command | Purpose |
|---------|---------|
| `/push-to-github` | Commit and push with conventional message |
| `/pull-from-github` | Pull latest from remote |

### Platform Deployment
| Command | Purpose |
|---------|---------|
| `/dataverse-deploy` | Deploy solutions, PCF, web resources |
| `/ribbon-edit` | Edit Dataverse ribbon via solution export |

---

## Key Resources

| Resource | Location | Purpose |
|----------|----------|---------|
| Skills Index | `.claude/skills/INDEX.md` | All available skills |
| ADRs | `docs/reference/adr/` | Architecture constraints |
| Templates | `docs/ai-knowledge/templates/` | Project templates |
| Conventions | `.claude/skills/spaarke-conventions/` | Coding standards |
| AI Knowledge | `docs/ai-knowledge/` | Guides, patterns, standards |

---

## Tips for Effective AI Collaboration

1. **Be specific in prompts** - Include file paths, component names
2. **Review at checkpoints** - Don't let AI run too far without review
3. **Use skills by name** - `/code-review` is clearer than "review this code"
4. **Check context usage** - If >70%, create handoff summary
5. **Commit incrementally** - Small commits are easier to review/revert

---

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Claude doesn't see SPEC.md | Verify path: `projects/{name}/SPEC.md` |
| Tasks seem wrong | Review PLAN.md first, then regenerate tasks |
| Context limit hit | Ask for handoff summary, start new chat |
| PR not updating | Check you're on the work branch |
| ADR violations | Review `docs/reference/adr/` for constraints |

---

## What's Missing? (Future Skills)

| Gap | Potential Skill | Status |
|-----|-----------------|--------|
| BDD test generation | `/generate-tests` | Not yet built |
| Storybook stories | `/create-stories` | Not yet built |
| CI/CD management | `/pipeline-check` | Future (CI/CD project) |
| Dependency updates | `/update-deps` | Not yet built |
| Performance profiling | `/perf-check` | Not yet built |

---

*Last updated: December 2025*
