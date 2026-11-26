# Spaarke MCP r2: Ask tool parity and retiring the Spaarke AI agent (`spaarkeai-mcp-bff-api-r2`)

> **Portfolio**: [Project #3150](https://github.com/spaarke-dev/spaarke/issues/3150) · Epic: [#421](https://github.com/spaarke-dev/spaarke/issues/421) · [Portfolio board view](https://github.com/users/spaarke-dev/projects/2)
> **Status:** Ready for execution (2026-11-25). Spec done; 35 tasks planned; Phase 0 next. Phase 0 code complete; Phase 1 r1 gate released 2026-11-25 (notes/decisions.md D-r1-gate).
> **Branch:** `work/spaarkeai-mcp-bff-api-r2` · **Worktree:** `C:/code_files/spaarke-wt-spaarkeai-mcp-bff-api-r2`
> **Builds on:** `spaarkeai-mcp-bff-api-r1` (the MCP server in the BFF).

## Quick Links

| File | Purpose |
|---|---|
| [plan.md](plan.md) | Implementation plan, discovered resources, risks |
| [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md) | Task registry, dependencies, parallel waves |
| [spec.md](spec.md) | AI implementation specification (18 FRs, 10 NFRs) |
| [design.md](design.md) | Design v0.3: inventory, dispositions, decisions |
| [CLAUDE.md](CLAUDE.md) | Project operating manual |
| [current-task.md](current-task.md) | Current state and next action |
| [notes/legacy-agent-inventory-2026-11-25.md](notes/legacy-agent-inventory-2026-11-25.md) | Code inventory of the 28 legacy functions (file:line) |
| [notes/reuse-scan.md](notes/reuse-scan.md) | Reuse scan cited by every §11 justification |

## Overview

The legacy "Spaarke AI" Copilot agent exposes 28 functions through an OpenAPI plugin, usable only inside `@Spaarke AI`. This project removes it now and moves the functions that still earn their place to MCP tools on the Spaarke server (`/mcp/legal-operations`), so they work in plain Copilot Chat and Cowork: search, documents, playbooks and runs, analysis, tasks, portfolio, the Daily Briefing, matter summaries, matter finance and email, plus three widgets that reuse Console components.

## Problem Statement

Copilot users reach Spaarke today only through r1's five decision tools, or through the legacy declarative agent, which works only when the user invokes `@Spaarke AI`. Running both risks the same request being answered twice, and the agent's gateway duplicates routes the BFF already owns.

## Proposed Solution

- Delete the legacy agent's code, gateway, bot infrastructure, CI, tests and docs (tenant steps E1/E2 are the owner's).
- Add about 19 MCP tools as thin adapters over the services the REST routes use, run as the caller, with REST-equal results and refusals.
- Persist playbook run status in Dataverse, add a 30 s start-then-status pattern, and prove caller scope for events and record search.
- Add Daily Briefing, search results and email compose widgets, each one Console component behind a data seam on a shared widget MCP transport; send email is app-only.

## Scope

**In scope:** legacy agent removal; tools for search, documents, playbooks, playbook runs, analysis, tasks, portfolio, Daily Briefing, matter summary, matter finance, email; durable run status; long-running pattern; caller-scope proofs; three widgets; routing eval at ≈10, ≈15 and all tools; Dataverse MCP admin guidance and research re-check.

**Out of scope:** practice-area agents and the attorney profile; invoice review/approval in Copilot; intake by email/forms; a second MCP route, server or connector; summarising uploaded files; the agent's chat loop, duplicate playbook routes and identity functions; Dataverse's built-in MCP for end users; Outlook as a host; tenant and admin actions.

## Graduation Criteria

- [ ] `git grep -i "api/agent\|AgentModule\|AgentTokenService\|CopilotAgent"` returns no code hits outside history notes; BFF builds with 0 warnings; ArchTests, unit and contract suites pass; `/api/agent/message` returns 404 on dev.
- [ ] Each FR-02..FR-12 tool returns REST-equal results for the same caller and refuses what REST refuses (parity and must-not-leak tests; dev E2E as the test user).
- [ ] A playbook run's status survives a BFF restart (FR-13 test and dev restart).
- [ ] The model cannot send email; the user can from the widget, and the email is archived to the matter with its attachments.
- [ ] Daily Briefing, search results and email compose widgets render in Chat and Cowork, light and dark; the Console's own uses pass their tests unchanged.
- [ ] Routing eval: each tool's positive prompts pick it at ≥ 80%; unrelated prompts invoke a Spaarke tool at ≤ 10%, at ≈10, ≈15 and all tools.
- [ ] NFR-01 latency (reads p95 ≤ 3 s, search/start ≤ 5 s, send ≤ 15 s, all < 30 s) and NFR-02 size (< 64 KiB) hold over 50 calls per tool on dev.
- [ ] The MCP conformance suite passes in CI with every tool registered.
- [ ] The Dataverse MCP admin paragraph is merged and the platform facts are re-verified with dated sources.
