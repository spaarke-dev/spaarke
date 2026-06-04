# CLAUDE.md — Agent Framework Knowledge Base R1

> **Project**: agent-framework-knowledge-r1
> **Type**: Knowledge curation + skill activation (no Spaarke runtime changes; reads code to ground commentary)
> **Created**: 2026-06-03

## Project Context

This project brings `knowledge/agent-framework/` from a thin starter (4 samples, 3 docs, stub NOTES.md) up to full **Fluent V9 parity** so Claude Code can author and modify Microsoft Agent Framework code in Spaarke's BFF (`src/server/api/Sprk.Bff.Api/Services/Ai/`) with the same rigor it has for Fluent UI v9 work.

Spaarke is already actively using Agent Framework primitives in production (`SprkChatAgent`, the `IChatClient` middleware pipeline, `AIFunction` tool registration). The work here is to encode that lived experience plus the upstream reference into Claude-loadable patterns.

Canonical plan: [`SPEC.md`](./SPEC.md). Per-task instructions: `tasks/*.poml`. Status: [`TASK-INDEX.md`](./TASK-INDEX.md).

## Key Constraints

1. **No Spaarke runtime edits.** Read `src/server/api/Sprk.Bff.Api/Services/Ai/` to ground commentary; do NOT modify any `.cs` files. Any production refactor (e.g., extracting middleware to a shared package) is a separate project.
2. **No invented URLs or sample content.** Every reference doc has YAML provenance (`source:` + `fetched:`). Every curated sample preserves upstream path structure under `dotnet/samples/` and is byte-identical to the pinned SHA (whitespace-only diffs ignored).
3. **Provenance preserved.** SOURCE.md updated with new upstream commit SHA before adding new curated files. Every 404 / URL substitution is recorded in the doc's frontmatter AND in SOURCE.md GAPs.
4. **Sub-agent write boundary.** Per root CLAUDE.md §3: sub-agents can write to `knowledge/` but NOT to `.claude/`. Pattern files, the SKILL.md, and `.claude/skills/INDEX.md` updates run in main session only.
5. **.NET only.** Mirror the existing topic curation rule — Spaarke's BFF is .NET 8; Python samples are out of scope.
6. **Honest gaps.** If a URL 404s or content is preview-only with no public sample, log a GAP entry in SOURCE.md and proceed. Do NOT pad with low-quality content. Community/MVP material may be sparse (Agent Framework released early 2026) — that's acceptable.
7. **Stub banner removed only when honest.** The `> ⚠️ STUB` banner on NOTES.md is removed only when both §1 (Spaarke architecture fit) and §2 (How we build with it) have substantive content backed by reading Spaarke code — not when filled with TODOs or generic restatements of upstream docs.

## Working Pattern for Each Task

1. Invoke `task-execute` skill with the task POML (per root CLAUDE.md §4 — mandatory task execution protocol)
2. Declare RIGOR LEVEL at task start (most are STANDARD; task 011 is FULL)
3. Read task POML `<knowledge>` files first
4. For curation tasks: re-pull from pinned SHA; preserve upstream path under `knowledge/agent-framework/dotnet/samples/...`
5. For reference-doc snapshot tasks: WebFetch the URL listed in the POML; if 404, find canonical replacement and record substitution; write to `knowledge/agent-framework/docs/<name>.md` with YAML frontmatter
6. For NOTES.md task: read Spaarke code at every cited path; write commentary grounded in observable patterns (Read tool, not guesses)
7. For skill/pattern tasks: work from main session only; do NOT delegate `.claude/` writes to sub-agents
8. Update `TASK-INDEX.md` row + reset `current-task.md` for next task at completion

## Mandatory Sources to Read When Grounding NOTES.md (Task 007)

- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` (if present)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Middleware/AgentTelemetryMiddleware.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Middleware/AgentContentSafetyMiddleware.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Middleware/AgentCostControlMiddleware.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/CompoundIntentDetector.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/*.cs` (tool registration patterns)
- `.claude/adr/ADR-013-ai-architecture.md`
- `.claude/constraints/bff-extensions.md`
- `src/server/api/Sprk.Bff.Api/CLAUDE.md` (Sprk.Bff.Api module conventions)

## Applicable Skills

- `task-execute` — mandatory wrapper for every task (root CLAUDE.md §4)
- `context-handoff` — checkpoint per the proactive checkpointing rules
- `add-reference-to-index` — only if adding the new knowledge to a search index (not in scope by default)
- `adr-check` — task 011 (FULL rigor) runs this against curated commentary
- `code-review` — task 011 (FULL rigor) runs this against the SKILL.md + pattern files

## 🚨 MANDATORY: Task Execution Protocol

When executing tasks in this project, Claude Code MUST invoke the `task-execute` skill. DO NOT read POML files directly and implement manually. See root CLAUDE.md §4 for the full protocol and rigor-level decision tree.

## References

- [Specification](SPEC.md) — canonical plan
- [Task Index](TASK-INDEX.md) — progress tracker
- [Current Task](current-task.md) — active task state
- Quality bar: [`knowledge/fluent-ui-v9/`](../../knowledge/fluent-ui-v9/), [`.claude/skills/fluent-v9-component/SKILL.md`](../../.claude/skills/fluent-v9-component/SKILL.md)
- Architectural anchor: [`.claude/adr/ADR-013-ai-architecture.md`](../../.claude/adr/ADR-013-ai-architecture.md)
- BFF governance: [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md)
- Module conventions: [`src/server/api/Sprk.Bff.Api/CLAUDE.md`](../../src/server/api/Sprk.Bff.Api/CLAUDE.md)
- Refresh procedure (followed on completion): [`knowledge/REFRESH-PROCEDURE.md`](../../knowledge/REFRESH-PROCEDURE.md)
