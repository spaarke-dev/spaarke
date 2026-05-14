> ⚠️ STUB — senior engineer review pending

Curation source-of-truth: `SOURCE.md` + `docs/`. This file is reserved for project-specific commentary that Spaarke engineers add through actual implementation experience. Do **not** fabricate insight. Each section below has a `_TODO_` hint to keep stubs honest.

---

## What Work IQ is (and isn't)

_TODO: Confirm and refine the boundary statement after first hands-on use. The current draft from the public docs:_

- **Is**: A real-time work-context layer over Microsoft 365 — Graph data + signals + semantic index + Copilot's chat synthesis — exposed to agents as a catalog of MCP servers (one per workload + one omnibus `mcp_M365Copilot`). Personalizes Copilot responses with collaboration and activity signals across Mail, Calendar, Teams, SharePoint, OneDrive, Word, Dataverse.
- **Isn't**: A queryable knowledge base you ingest content into. Not a substrate for storing Spaarke's matter or document data. Not a vector index you control. Not a long-running-task surface (Chat API explicitly excludes long tasks). Spaarke owns its data in Dataverse + SharePoint Embedded; Work IQ reads/synthesizes over the user's own M365 tenant content via their delegated identity.

_TODO: After first integration, add the concrete signals we've actually observed Work IQ surface — were people, projects, recent-meetings signals helpful for matter context? Did fileUri grounding work as advertised? Capture surprises._

## Work IQ MCP vs Foundry IQ knowledge bases

_TODO: After actually wiring both surfaces into a Spaarke agent, document the practical decision rules. Draft from the docs:_

| Dimension | Work IQ MCP | Foundry IQ knowledge bases |
|---|---|---|
| Data source | The user's own M365 tenant (Mail, Calendar, Teams, SharePoint, OneDrive, Word, Dataverse) | Curated knowledge artifacts indexed into Foundry |
| Permissions model | User's delegated identity — auto-respects M365 ACLs, sensitivity labels, DLP | Foundry-managed; tenant + project scoped |
| Freshness | Real-time (semantic index latency aside) | Build-time / refresh-cadence indexed |
| Best for | "What did we discuss," "who's on this matter," "what's been happening" | "What does our regulatory policy say," "what's our standard clause for X" |
| Cost model | M365 Copilot license per user (mandatory) | Foundry compute + storage |

_TODO: Add the specific Spaarke composition pattern after we've actually built one. The directive's framing: **"Spaarke agent uses Work IQ for collab context AND Foundry IQ for curated knowledge AND Dataverse MCP for records AND SharePoint knowledge source for matter docs — four retrieval surfaces."** Confirm or revise._

## Licensing prerequisites

_TODO: Confirm against actual Spaarke licensing inventory. From the docs:_

- **Microsoft 365 Copilot license** required **per consuming end user** for every Work IQ MCP server. No exceptions documented.
- **M365 E3 or E5** (or equivalent) is the prerequisite subscription for Copilot.
- This is the **same license** required for the Microsoft 365 Copilot REST APIs (Retrieval, Chat, Search, Meeting Insights, etc.) — Work IQ MCP and Copilot APIs share the licensing surface.
- Agent registration (Entra app) is separate — admin consent to per-server scopes like `WorkIQ-MailServer`, `McpServers.Teams.All`.

_TODO: Capture Spaarke's actual licensing decision — do all Spaarke users have M365 Copilot, or is Work IQ a premium feature? If premium, what's the gating UX in our agents?_

## Preview status and naming evolution

_TODO: Track this section closely — preview surfaces evolve. Current state as of 2026-05-14:_

- **All Work IQ MCP servers are in public preview.** Subject to supplemental terms; not for production.
- **Naming history** (partially confirmed by the docs):
  - **"Agent 365 MCP"** — earlier framing where MCP servers were branded under the Agent 365 control plane name.
  - **"Work IQ MCP"** — current branding. Microsoft Learn pages under both `microsoft-365/copilot/extensibility/work-iq` and `microsoft-agent-365/mcp-server-reference/*` use this name consistently as of 2026-04 onward.
  - **Agent 365 ≠ Work IQ**: Agent 365 is the control plane (registry, governance, observability — Defender + Entra + Purview integration). Work IQ is the intelligence layer + MCP catalog. Agent 365 hosts Work IQ; they're separate concepts.
- **Server ID naming convention**: `mcp_<Workload>` (e.g. `mcp_M365Copilot`, `mcp_MailTools`, `mcp_CalendarTools`, `mcp_TeamsServer`). Tool naming convention: `mcp_<Server>_graph_<workload>_<verb>` for Graph-API-backed tools.
- **The note on every reference page**: *"Existing connections that use previous versions of Microsoft MCP servers, such as Microsoft Teams MCP server, remain supported. For all new connections, use the latest Work IQ MCP servers, such as Work IQ Teams."* — implies a v1 → v2 migration path. Old servers still work; new code should target Work IQ servers.

_TODO: Track GA dates as they're announced. Refresh `tool-catalog.md` to drop "(preview)" markers when GA happens._

## When the Spaarke agent should call Work IQ MCP

_TODO: Replace this section with concrete decision rules from actual project work. Initial draft from the directive:_

Call Work IQ MCP for questions that require **real-time M365 collaboration context**:

- "What did we discuss in last week's meeting about Matter 1234?" — Work IQ Calendar + Teams + Copilot
- "Who's been working on this client?" — Work IQ User + Mail + Calendar
- "What's the latest on this matter?" — Work IQ Copilot (omnibus, synthesizes across surfaces)
- "Send a status email to the team" — Work IQ Mail (action, not just retrieval)

Do **NOT** call Work IQ MCP for:

- Matter / document data stored in Spaarke's Dataverse — use Dataverse MCP
- Documents stored in Spaarke's SharePoint Embedded container — use SPE APIs / SharePoint knowledge source
- Curated knowledge (policies, playbooks, templates) — use Foundry IQ
- Anything requiring a deterministic schema response — Work IQ returns synthesized prose, not records

_TODO: Refine these rules after first 2-3 features ship. Add the specific tools we end up using most. Note any retrieval-quality issues that pushed us to a different surface._

## The four-retrieval-surface composition pattern

_TODO: This is the architectural pattern the directive flags as the project-level insight. Validate by actually building a Spaarke agent that uses all four. Initial sketch:_

The Spaarke agent composes four distinct retrieval surfaces, each suited to different data and freshness needs:

```
Spaarke Agent
├── Work IQ MCP        → collab context (real-time, user-scoped, M365)
│                          • Work IQ Copilot (omnibus chat synthesis)
│                          • Work IQ Mail / Calendar / Teams / User
├── Foundry IQ         → curated knowledge bases (build-time, project-scoped)
│                          • Spaarke policies, regulatory refs, standard clauses
├── Dataverse MCP      → systems-of-record (real-time, Spaarke schema)
│                          • Matter records, parties, deadlines, configurations
└── SharePoint KS      → matter documents (real-time, SPE container-scoped)
                           • Matter files, working documents
```

_TODO: Sequence rules. Which surface does the agent check first? Cost considerations — Work IQ Copilot is the most expensive (it's a full Copilot turn). Failure-mode handling — if Work IQ returns nothing, do we fall back to Dataverse, or vice versa?_

_TODO: Citation aggregation. Each surface returns citations in different formats. Document the unification pattern._

## Open questions

_TODO: Add as they come up during implementation. Initial seeds:_

- Can a Foundry agent simultaneously consume both Work IQ MCP servers and a custom Spaarke MCP server? Docs say yes; verify in practice.
- What's the OBO token lifetime when calling Work IQ MCP via Spaarke's BFF? Does the BFF need a refresh strategy, or does each MCP turn handle its own?
- Do all Work IQ MCP servers support agent-mode auth (not just delegated)? Some servers may be delegated-only.
- What happens when the user's M365 Copilot license is removed mid-conversation? Graceful degradation pattern?
- Tool call observability — what's the actual structure of the Defender Advanced Hunting log entries for Work IQ MCP calls? Useful for our own audit trail?

## Pitfalls to watch for

_TODO: Populate as we hit them._

- **`mcp_M365Copilot` is a heavy hammer.** The catalog directive instructs the model to invoke it as a fallback whenever no workload-specific tool fits. This can dramatically increase Copilot consumption. The Spaarke agent's tool selection prompt should prefer workload-specific tools (Mail, Calendar, Teams) and only fall back to the omnibus tool when no workload tool can answer.
- **MCP server v1 → Work IQ v2 migration is implicit, not automatic.** New code should target `mcp_*` Work IQ servers; do not start on old "Microsoft Teams MCP server" etc.
- **Preview surface = breaking changes.** Snapshot rules of behavior from the docs **with dates** in case the docs change.
