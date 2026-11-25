# Spaarke MCP r2: Ask tool parity and retiring the Spaarke AI agent (`spaarkeai-mcp-bff-api-r2`)

> **Portfolio**: [Project #3150](https://github.com/spaarke-dev/spaarke/issues/3150) · Epic: [#421](https://github.com/spaarke-dev/spaarke/issues/421) · [Portfolio board view](https://github.com/users/spaarke-dev/projects/2)
> **Status:** Design draft (v0.1, 2026-11-25). No spec or tasks yet. Next: owner resolves `design.md` §9, then `/design-to-spec`.
> **Branch:** `work/spaarkeai-mcp-bff-api-r2` · **Worktree:** `C:/code_files/spaarke-wt-spaarkeai-mcp-bff-api-r2`
> **Builds on:** `spaarkeai-mcp-bff-api-r1` (the MCP server in the BFF).

The legacy "Spaarke AI" Copilot agent exposes 28 functions through an OpenAPI plugin, usable only inside `@Spaarke AI`. This project moves the ones that still earn their place to MCP tools on the Spaarke server, so they work in plain Copilot Chat and Cowork. About 17 tools: search, documents, playbooks, tasks, briefing, portfolio, matter summaries. Then it retires the agent and its `/api/agent/*` gateway.

| File | Purpose |
|---|---|
| [design.md](design.md) | Design draft: problem, inventory and dispositions, cross-cutting design, phasing, open questions |
| [notes/legacy-agent-inventory-2026-11-25.md](notes/legacy-agent-inventory-2026-11-25.md) | Code inventory of the 28 functions (file:line) |
| [current-task.md](current-task.md) | Current state and next action |
