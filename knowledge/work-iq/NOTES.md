> ⚠️ STUB — senior engineer review pending

# NOTES — work-iq

Project-specific commentary on work-iq. Annotate from real Spaarke project experience; don't fabricate. Section structure:

- **§1. How this fits Spaarke's architecture** — when to reach for this, role/composition with other surfaces, what it replaces or composes with, preview/cost/licensing implications, decision criteria
- **§2. How we build with it** — manifest/code shape, auth wiring, gotchas, Spaarke divergence from canonical samples, code review checklist

Both sections required for "done"; honest TODOs are fine for what isn't yet known. When annotating, remove the `⚠️ STUB` banner above only after both §1 and §2 have substantive content (or honest TODOs).

Curation source-of-truth: `SOURCE.md` + `docs/`. This file is reserved for project-specific commentary that Spaarke engineers add through actual implementation experience. Do **not** fabricate insight. Each section below has a `_TODO_` hint to keep stubs honest.

---

## 1. How this fits Spaarke's architecture

## What Work IQ is (and isn't)

_TODO: Confirm and refine the boundary statement after first hands-on use. The current draft from the public docs:_

- **Is**: A real-time work-context layer over Microsoft 365 — Graph data + signals + semantic index + Copilot's chat synthesis — exposed to agents as a catalog of MCP servers (one per workload + one omnibus `mcp_M365Copilot`). Personalizes Copilot responses with collaboration and activity signals across Mail, Calendar, Teams, SharePoint, OneDrive, Word, Dataverse.
- **Isn't**: A queryable knowledge base you ingest content into. Not a substrate for storing Spaarke's matter or document data. Not a vector index you control. Not a long-running-task surface (Chat API explicitly excludes long tasks). Spaarke owns its data in Dataverse + SharePoint Embedded; Work IQ reads/synthesizes over the user's own M365 tenant content via their delegated identity.

> **Refresh 2026-07-14** (`email-communication-solution-r4` task 076, per DEC-7): Work IQ reached **GA on 2026-06-16** (source: [Work IQ overview](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/work-iq/), `ms.date: 2026-06-16`; [Microsoft 365 blog, 2026-06-02](https://www.microsoft.com/en-us/microsoft-365/blog/2026/06/02/announcing-the-new-work-iq-apis/)). Docs are **mixed GA/preview** — the overview + core API pages are GA-worded, but several sub-pages (API quickstart, MCP overview, Foundry integration) still carry "(preview)" labels as of this refresh; treat per-surface GA claims with that caveat.
>
> **Work IQ is OUT OF SCOPE as a classifier for Spaarke R4** (`email-communication-solution-r4`, DEC-7 / spec requirement #12). It is **delegated-only** — [Learn: permissions](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/work-iq/permissions) confirms **app-only/application auth is explicitly NOT supported**; every call is scoped to a signed-in user's identity (`WorkIQAgent.Ask`, OBO only). R4's email-association classification runs **app-only** (background job, no signed-in user), is consumption-billed via **Copilot Credits** rather than a per-seat cost, and returns synthesized/grounded prose rather than a deterministic schema — a poor fit for deterministic, batch, app-only classification regardless of GA status. R4's classifier is the **Association Engine** (deterministic rungs 0–3 + semantic rung 4 + JPS AI rung 5) operating over the normalized communication envelope, not Work IQ.

_TODO: After first integration, add the concrete signals we've actually observed Work IQ surface — were people, projects, recent-meetings signals helpful for matter context? Did fileUri grounding work as advertised? Capture surprises._

## Context API — GA component, agent-facing (correction, 2026-07-14)

_Correction to prior framing._ The R4 project design doc (`email-communication-solution-r4/design.md` §4.2, DEC-7) characterized the "Context API" as a *future* user-facing augmentation. Research for task 076 found this is **not accurate**: the Context API is a **real, GA (2026-06-16) component of the Work IQ API surface**, documented alongside Chat, Tools, and Workspaces (source: [Learn: Work IQ API overview](https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/work-iq/api-overview)). Two corrections:

1. **Not future** — it ships today, at GA, as part of the Work IQ API surface.
2. **Not user-facing** — it's **agent/server-facing**: it "aggregates the content Copilot would use... returns context in a format designed for agent consumption" (no synthesized prose, unlike Copilot Chat). It's the opposite of user-facing.

The part of the design doc's reasoning that **does** hold: the Context API is still **delegated-only** (same auth constraint as the rest of Work IQ — see boundary note above), so it is **not** usable as an app-only classifier substrate for R4 regardless of this correction. **Net effect on DEC-7: no change to the scope decision** — Work IQ (including the Context API) remains out of scope as R4's classifier; the Association Engine is the classifier. Only the *characterization* of the Context API changes (agent-facing GA component, not a future user-facing feature). Flagging here so a future project revisiting Work IQ doesn't inherit the "future/user-facing" mischaracterization.

_TODO: If a future non-R4 project considers the Context API for agent-facing (not app-only-classifier) use cases, fetch `api-overview.md` in full and add a proper doc snapshot to `docs/`._

## Work IQ MCP vs Foundry IQ knowledge bases

_TODO: After actually wiring both surfaces into a Spaarke agent, document the practical decision rules. Draft from the docs:_

| Dimension | Work IQ MCP | Foundry IQ knowledge bases |
|---|---|---|
| Data source | The user's own M365 tenant (Mail, Calendar, Teams, SharePoint, OneDrive, Word, Dataverse) | Curated knowledge artifacts indexed into Foundry |
| Permissions model | User's delegated identity — auto-respects M365 ACLs, sensitivity labels, DLP | Foundry-managed; tenant + project scoped |
| Freshness | Real-time (semantic index latency aside) | Build-time / refresh-cadence indexed |
| Best for | "What did we discuss," "who's on this matter," "what's been happening" | "What does our regulatory policy say," "what's our standard clause for X" |
| Cost model | Usage-based / Copilot Credits, decoupled from M365 Copilot seat licensing (GA 2026-06-16 — see § Licensing prerequisites below; was per-user-license as of the 2026-05-14 preview-era curation) | Foundry compute + storage |

_TODO: Add the specific Spaarke composition pattern after we've actually built one. The directive's framing: **"Spaarke agent uses Work IQ for collab context AND Foundry IQ for curated knowledge AND Dataverse MCP for records AND SharePoint knowledge source for matter docs — four retrieval surfaces."** Confirm or revise._

## Licensing prerequisites

> **Updated 2026-07-14** (GA refresh, task 076): the per-user-license model below is **stale (2026-05-14 preview-era assumption)**. As of GA (2026-06-16), Work IQ billing moved to **usage-based / Copilot Credits, decoupled from Microsoft 365 Copilot seat licensing** for custom/agent callers — a user's existing M365 Copilot license covers usage inside first-party Copilot experiences, but agents/apps calling Work IQ (our hypothetical scenario, had we used it) are billed per Copilot Credit regardless of the caller's M365 Copilot license; unlicensed users/agents are billed by consumption too. Exact Copilot Credit pricing was **not independently verified** against `aka.ms/WorkIQ/licensing` in this refresh (third-party sources cite ~$0.01/credit, 25,000 credits ≈ $200/tenant/mo — confirm before relying on a number).
>
> This does **not** change R4's DEC-7 exclusion — the blocker was never per-seat cost, it's the **delegated-only auth model** (app-only/application auth is explicitly not supported — see boundary note above) plus the prose-oriented output shape.

_TODO: Confirm against actual Spaarke licensing inventory. From the (now-superseded, 2026-05-14) docs:_

- ~~**Microsoft 365 Copilot license** required **per consuming end user** for every Work IQ MCP server. No exceptions documented.~~ **Superseded — see GA update above.**
- **M365 E3 or E5** (or equivalent) is the prerequisite subscription for Copilot experiences that surface Work IQ natively (Copilot Chat, Word, Excel, etc.) — still applies to those first-party surfaces.
- Agent registration (Entra app) is separate — admin consent to per-server scopes like `WorkIQ-MailServer`, `McpServers.Teams.All`, plus the GA delegated scope `WorkIQAgent.Ask` (app ID URI `api://workiq.svc.cloud.microsoft`).

_TODO: Capture Spaarke's actual licensing decision if a future (non-R4) project revisits Work IQ for a delegated, user-facing scenario._

## Preview status and naming evolution

> **Updated 2026-07-14**: **Work IQ reached GA on 2026-06-16** (see boundary note in "What Work IQ is (and isn't)" above). The bullets below describe the 2026-05-14 preview state and are retained for history — GA supersedes the "all in public preview" claim, but the docs remain **mixed**: the overview + core API-overview pages are GA-worded, while API quickstart, the Work IQ MCP overview, and Foundry-integration sub-pages still carried "(preview)" labels as of this refresh. `tool-catalog.md`'s preview/licensing banner has been updated accordingly; a full per-server preview-label audit is deferred to the next full monthly refresh (see `SOURCE.md` for the interim-refresh scope note).

_TODO: Track this section closely — preview surfaces evolve. Current state as of 2026-05-14 (superseded by GA 2026-06-16 above; retained for history):_

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

## Open questions (architectural)

_TODO: Add as they come up during implementation. Initial seeds:_

- Can a Foundry agent simultaneously consume both Work IQ MCP servers and a custom Spaarke MCP server? Docs say yes; verify in practice.

---

## 2. How we build with it

## Pitfalls to watch for

_TODO: Populate as we hit them._

- **`mcp_M365Copilot` is a heavy hammer.** The catalog directive instructs the model to invoke it as a fallback whenever no workload-specific tool fits. This can dramatically increase Copilot consumption. The Spaarke agent's tool selection prompt should prefer workload-specific tools (Mail, Calendar, Teams) and only fall back to the omnibus tool when no workload tool can answer.
- **MCP server v1 → Work IQ v2 migration is implicit, not automatic.** New code should target `mcp_*` Work IQ servers; do not start on old "Microsoft Teams MCP server" etc.
- **Preview surface = breaking changes.** Snapshot rules of behavior from the docs **with dates** in case the docs change.

## Open questions (implementation)

_TODO: Add as they come up during implementation. Initial seeds:_

- What's the OBO token lifetime when calling Work IQ MCP via Spaarke's BFF? Does the BFF need a refresh strategy, or does each MCP turn handle its own?
- Do all Work IQ MCP servers support agent-mode auth (not just delegated)? Some servers may be delegated-only.
- What happens when the user's M365 Copilot license is removed mid-conversation? Graceful degradation pattern?
- Tool call observability — what's the actual structure of the Defender Advanced Hunting log entries for Work IQ MCP calls? Useful for our own audit trail?
