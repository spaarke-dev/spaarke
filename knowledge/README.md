# Spaarke Microsoft Platform Knowledge Base

> **Purpose**: Curated, agent-readable reference tree for the Microsoft AI platform pieces Spaarke depends on. Loaded on-demand by Claude Code skills so the agent produces code that follows current platform idioms — not patterns from its training-data cutoff.
>
> **Established**: 2026-05-14 · **Refresh cadence**: monthly (first business day) · **Log**: [`REFRESH-LOG.md`](./REFRESH-LOG.md)

---

## Directory conventions

Each topic folder contains:

| Artifact | Required? | Purpose |
|---|---|---|
| `SOURCE.md` | Required | Provenance — source repo URLs, commit SHAs, date pulled, what each curated file demonstrates. **The thing that makes the knowledge base trustworthy six months from now.** |
| `NOTES.md` | Required | Project-specific commentary — what the pattern teaches, how Spaarke applies it, what to modify, common pitfalls. Stub files are honestly marked `> ⚠️ STUB — senior engineer review pending` until annotated. |
| `<sample-name>/` | One or more | Curated sample code, copied (not submoduled) from source repos. 1–3 examples per pattern — no whole-repo dumps. |
| `docs/` (optional) | When needed | Snapshotted Microsoft Learn pages as markdown (for content that changes between refreshes). |

**No separation between Microsoft and community sources** — both go in the same topic folder, sorted by topic.

## Topics

| Topic | What it covers |
|---|---|
| [`m365-copilot/`](./m365-copilot/) | M365 Copilot extensibility foundation — declarative agents, knowledge sources, capabilities |
| [`mcp-apps/`](./mcp-apps/) | MCP Apps for rich UI inside Copilot Chat — widget patterns, side-by-side and inline modes |
| [`declarative-agents/`](./declarative-agents/) | Declarative agent manifest deep-dive — knowledge bindings, tool bindings, approval flow |
| [`agent-framework/`](./agent-framework/) | Microsoft Agent Framework 1.0 — server-side agent loops in the BFF |
| [`foundry-agent-service/`](./foundry-agent-service/) | Foundry Agent Service — durable workflows with HITL gates, A2A, MCP tool binding |
| [`foundry-iq/`](./foundry-iq/) | Foundry IQ — managed knowledge layer for grounded retrieval |
| [`work-iq/`](./work-iq/) | Work IQ MCP — M365 collaboration context layer (preview) |
| [`dataverse-mcp/`](./dataverse-mcp/) | Dataverse as MCP server — built-in tools, Business Skills, App MCP |
| [`sharepoint-embedded/`](./sharepoint-embedded/) | SPE — document storage substrate, agent grounding via SharePoint knowledge source |
| [`azure-ai-search/`](./azure-ai-search/) | Azure AI Search — application-code retrieval, vector + hybrid + semantic |
| [`cosmos-gremlin/`](./cosmos-gremlin/) | Cosmos DB Gremlin — graph layer for the Insights Engine; partitioning, RU sizing, migration triggers |
| [`azure-functions-isv/`](./azure-functions-isv/) | Azure Functions for ISV multi-tenant scenarios — Flex Consumption, per-tenant Bicep + UAMI |
| [`dataverse-sync/`](./dataverse-sync/) | Dataverse → AI Search sync patterns — webhook vs Service Bus vs change-tracking, idempotent indexing |
| [`foundry-memory-patterns/`](./foundry-memory-patterns/) | Foundry agent memory + tool patterns (reference) — for the custom BFF Insights Agent design |
| [`github-mcp/`](./github-mcp/) | GitHub MCP server — runtime currency and long-tail API research |

## How Claude Code uses this

Skills in `.claude/skills/` reference these folders. When a task triggers a relevant skill (e.g., creating an MCP tool handler, designing a widget, drafting a declarative agent), the skill instructs the agent to read specific files in the relevant `knowledge/<topic>/` directory **before** generating code. This is what keeps agent output aligned with current platform idioms rather than the training-data cutoff.

Skills wired to this knowledge base (see Phase 3 of the setup project):

- `.claude/skills/mcp-tool-handler/` → `mcp-apps/`, `foundry-agent-service/`
- `.claude/skills/declarative-agent/` → `declarative-agents/`, `m365-copilot/`
- `.claude/skills/foundry-agent/` → `foundry-agent-service/`, `foundry-iq/`, `agent-framework/`
- `.claude/skills/dataverse-mcp-usage/` → `dataverse-mcp/`
- `.claude/skills/spe-integration/` → `sharepoint-embedded/`
- `.claude/skills/widget-design/` → `mcp-apps/`

## Refresh cadence

- **Monthly** (first business day of the month) — re-clone source repos, diff against curated copy, update samples and `SOURCE.md`. See `REFRESH-PROCEDURE.md` (created in Phase 6).
- **Interim updates** — when Microsoft ships a notable platform change between refreshes, append a `> interim update <date>` entry to `REFRESH-LOG.md` immediately; formalize at next refresh.
- **Refresh owner**: Ralph Schroeder (initial). ~2–4 hours per monthly refresh once the procedure is established.

## Rules for contributors

1. **Preserve provenance.** Every curated file traceable via `SOURCE.md`.
2. **Honest stubs.** A `NOTES.md` that says "TODO: annotate" is more useful than one pretending to have insight it doesn't.
3. **Don't bloat.** 1–3 examples per pattern. No whole-repo dumps.
4. **No `.gitignore`.** This tree is tracked, reviewed in PRs like any other artifact.
5. **One topic at a time, fully.** Complete `SOURCE.md` + samples + stub `NOTES.md` for a topic before moving to the next.
