# Current Task State — spaarke-intake-pipeline-r1

> **Last Updated**: 2026-11-25 (project created)
> **Phase**: design draft. No spec, no tasks. Do NOT run `/design-to-spec` until the owner resolves `design.md` §13.

## Quick Recovery
| Field | Value |
|---|---|
| **Active work** | Owner discussion of `design.md` v0.1 |
| **Next action** | Walk the owner through §13 (orchestration host, NDA engine host, invoice timing, sending identity, NDA standard master, email door scope); record each answer in design.md |
| **Uncommitted** | none |

## Decided (owner)
- 2026-11-24: `sprk_servicerequest` is the request entity (no `sprk_legalrequest`; legal-front-door-r1 not going forward). An Inquiry is a different ontology concept.
- 2026-11-25: own project (split from specialist-legal-agents-r1); run record = extend `sprk_processingjob`; don't force paths onto one model where it isn't the right technical answer; document and clean up the code-review findings (design §10).

## Standing directives
- Never `git stash`; never `python3` in Bash; every Agent launch names a model.
- Don't edit other projects' worktrees (accelerator, ebilling, ontology, mcp-r1, specialist-legal-agents); coordinate by message.
- Accelerator constraint: never put `answer-key/` or the real NDAs anywhere an agent can read.
