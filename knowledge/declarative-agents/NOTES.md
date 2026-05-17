> ⚠️ STUB — senior engineer review pending

# NOTES — `declarative-agents`

Project-specific commentary on Microsoft 365 declarative agents as they apply to Spaarke. Annotate from real-world project experience; do not fabricate insight.

Section structure:

- **§1. How this fits Spaarke's architecture** — when to reach for this, role/composition with other surfaces, what it replaces or composes with, preview/cost/licensing implications, decision criteria
- **§2. How we build with it** — manifest/code shape, auth wiring, gotchas, Spaarke divergence from canonical samples, code review checklist

Both sections required for "done"; honest TODOs are fine for what isn't yet known. When annotating, remove the `⚠️ STUB` banner above only after both §1 and §2 have substantive content (or honest TODOs).

This is the deeper companion to `knowledge/m365-copilot/NOTES.md`. Foundational concepts (what a declarative agent is, basic capabilities, schema version overview) live there. This file focuses on **manifest-level depth, knowledge bindings, tool bindings, admin approval flow, and the Spaarke-specific agent shape**.

---

## 1. How this fits Spaarke's architecture

## Knowledge source types — what to bind, where, and why

The capabilities array enumerates which Microsoft 365 knowledge surfaces the agent can ground in. The full taxonomy from the v1.6 schema:

| Capability | Spaarke applicability | Notes |
|---|---|---|
| `WebSearch` | _TODO_ | Max 4 sites. URL must be ≤ 2 path segments, no query params. See `declarative-agent-websearch-scoped/`. |
| `OneDriveAndSharePoint` | **Primary** for Spaarke's SPE-grounded agent | `items_by_sharepoint_ids` for stable bindings; `items_by_url` for ad-hoc. Spaarke's SharePoint Embedded containers expose document libraries here — _TODO: confirm exact ID surface (containerId → list_id mapping)_. |
| `GraphConnectors` | _TODO_ | Use case if Spaarke later indexes external systems via Copilot connectors. |
| `GraphicArt` | _TODO_ | Visual diagrams / architecture sketches in agent responses. Several curated samples include this for SPE/SharePoint org diagrams. |
| `CodeInterpreter` | _TODO_ | Python execution. Spaarke probably doesn't need this in user-facing M365 Copilot DAs — server-side analytics live in BFF. |
| `Dataverse` | **Primary** for Spaarke | The `host_name` + `skill` + `tables[]` triple. `skill` ID is found by publishing a stub DA in Copilot Studio and reading the downloaded `declarativeAgent.json`. _TODO: list the Spaarke Dataverse tables we expose to the DA — engagements, communications, matters?_ |
| `TeamsMessages` | _TODO_ | Max 5 chat URLs. Probably not in scope. |
| `Email` | _TODO_ | Shared mailbox or group mailboxes (max 25). _TODO: Spaarke matter-related shared mailboxes?_ |
| `People` | _TODO_ | `include_related_content: true` pulls cross-context (docs, emails, Teams messages between agent user and referenced person). Useful for matter team lookups; permission-sensitive. |
| `ScenarioModels` | _TODO_ | Task-specific models. Not aware of Spaarke usage. |
| `Meetings` | _TODO_ | Max 5 meeting IDs. _TODO: matter-related recurring meetings?_ |
| `EmbeddedKnowledge` | _Not yet available_ in production per Microsoft Learn (May 2026). Watch for GA. |

_TODO: Document the exact capability composition Spaarke ships in production — capability array, scoping IDs, conditional combinations._

## Spaarke's declarative agent shape

The intended Spaarke shape per the directive's NOTES.md guidance:

> Three knowledge sources (SPE via SharePoint knowledge source, Dataverse MCP, Foundry IQ knowledge base), tool bindings to Spaarke MCP server, Work IQ MCP for collaboration context.

Mapping into manifest fields:

| Spaarke knowledge source | Manifest mechanism | Notes |
|---|---|---|
| **SPE container documents** | `capabilities[name: OneDriveAndSharePoint].items_by_url[]` (or `.items_by_sharepoint_ids[]`) pointing at the SPE-backed SharePoint site/library | _TODO: confirm SPE containers surface through the OneDriveAndSharePoint capability as a regular SharePoint library, or whether a specialized capability is needed. Read `knowledge/sharepoint-embedded/` after curation._ |
| **Dataverse (Spaarke entities)** | `capabilities[name: Dataverse].knowledge_sources[]` with Spaarke `host_name`, generated `skill`, and table allow-list | _TODO: list the Spaarke entities (engagement, communication, matter, etc.) we expose. Cross-reference with `knowledge/dataverse-mcp/`._ |
| **Foundry IQ knowledge base** | _TODO: how Foundry IQ is exposed to a DA — as a Copilot Connector (`GraphConnectors`)? As a custom MCP server? Read `knowledge/foundry-iq/` after curation._ |
| **Spaarke MCP server** (tool bindings) | `actions[]` entry pointing at plugin manifest with `runtimes[].type: RemoteMCPServer` and `auth.type: OAuthPluginVault` | Pattern matches `declarative-agent-foodbank-mcp/appPackage/ai-plugin-sharepoint.json`. _TODO: capture the actual Spaarke MCP endpoint and required Entra app scopes._ |
| **Work IQ MCP** (collaboration context) | Same as above — another `actions[]` entry, `RemoteMCPServer` runtime, Microsoft-hosted on `agent365.svc.cloud.microsoft` | _TODO: Spaarke's Work IQ scopes — `McpServers.*.All` set required from Entra app permissions._ |

Plus likely additions: `behavior_overrides.special_instructions.discourage_model_knowledge: true` (Spaarke's domain is grounded, not generative); `conversation_starters[]` aligned with matter/document/engagement vocabulary; a `disclaimer` referencing legal/practice-management context.

_TODO: Once Spaarke publishes a real DA manifest, anchor this section in the actual file rather than this aspirational shape._

## Admin approval flow (process)

_TODO: Document end-to-end. Anchored to current Teams Admin Center / Microsoft 365 Admin Center flow as of refresh date._

- **First-time publish path**: Agents Toolkit / TTK CLI → Teams Developer Portal → Teams Admin Center approval queue → tenant-wide or group-scoped policy → end-user availability.
- **Re-approval triggers** (educated guess pending verification — _TODO_):
  - Manifest **permissions** changes (new Graph scope, new `permissions[]` value).
  - Plugin **runtime endpoint** changes (new MCP server URL, new OpenAPI host).
  - **Auth** changes (`OAuthPluginVault` reference_id changes, scope additions).
  - Adding/removing **capabilities** that touch personal/protected data (`People`, `Email`, `TeamsMessages`).
  - Adding **embedded files** with sensitivity labels.
  - _Not triggering re-approval_ (educated guess): instruction text edits, conversation starter changes, behavior_overrides tweaks. _Verify._
- **Spaarke ALM hooks**: _TODO: how does our deployment pipeline detect changes that will require re-approval and surface them to release management?_

---

## 2. How we build with it

## Manifest structure field by field (declarative agent v1.6 / v1.7)

_TODO: Walk the manifest root fields one at a time — `version`, `id`, `name`, `description`, `instructions`, `capabilities`, `conversation_starters`, `actions`, `behavior_overrides`, `disclaimer`, `sensitivity_label`, `worker_agents`, `user_overrides` — annotating which fields Spaarke uses, which we set programmatically vs. by hand, and which limits hit us in practice. See `docs/declarative-agent-manifest.md` for the canonical reference._

- **`version` / `$schema`**: _TODO: which schema version is Spaarke standardizing on for the SPE-grounded agent? v1.6 is what the curated samples use; v1.7 is the latest per Microsoft Learn._
- **`name` / `description`**: _TODO: naming convention for Spaarke-published agents in tenant catalog — does it follow our Teams app naming standards?_
- **`instructions`**: _TODO: the 8,000-char limit. How we split instructions vs. tool-side `description_for_model`. Spaarke's pattern: short manifest `instructions`, push detailed steering into plugin `description_for_model` and `function.description`._
- **`capabilities` ordering**: _TODO: does ordering affect grounding precedence? The data-flow diagram in `docs/declarative-agent-architecture.md` says grounding is sequential — confirm whether the array order is a hint to the orchestrator._
- **`behavior_overrides`**: see [§OnlyAllowedSources](#onlyallowedsources-behavior--how-instructions-affect-grounding).
- **`worker_agents`** (preview, v1.6+): _TODO: relevance to Spaarke's multi-agent composition. Likely not in scope until Foundry Agent Service is wired._
- **`user_overrides`** (v1.6+): _TODO: Spaarke policy on letting end users toggle Web Search / Teams Messages off. Recommended default?_

---

### Out-of-manifest knowledge sources (via actions)

The manifest also carries `actions[]` — plugin manifest references. Plugins extend grounding beyond Microsoft capabilities through:

- **OpenAPI runtimes** — for external REST APIs. See `declarative-agent-foodbank-mcp/appPackage/ai-plugin-givefood.json`.
- **RemoteMCPServer runtimes** — for MCP-protocol tool servers (Microsoft-hosted or third-party). See both `declarative-agent-foodbank-mcp/` (auth via `OAuthPluginVault`) and `declarative-agent-only-allowed-sources/` (no auth, public MS Learn MCP).
- _TODO: LocalPlugin / function plugin runtimes if Spaarke ever uses them._

The directive's NOTES.md guidance notes "knowledge source types: ... MCP servers" — in the manifest, MCP servers are surfaced via the **actions array → plugin manifest → `runtimes[].type: RemoteMCPServer`** pathway. They are not first-class capabilities.

---

## OnlyAllowedSources behavior — how instructions affect grounding

The directive's required pattern "OnlyAllowedSources" maps to **two distinct mechanisms** in the manifest. Conflating them is a common mistake. Senior engineers should disambiguate when annotating:

### Mechanism 1: Source list scoping (manifest-level allow list)

Set in the **capability** itself — restricts *which* sources the agent may ground in:

- `WebSearch.sites[]` (max 4) — see `declarative-agent-websearch-scoped/appPackage/declarativeAgent.json`
- `OneDriveAndSharePoint.items_by_url[]` and `.items_by_sharepoint_ids[]`
- `GraphConnectors.connections[]` and `.items_by_*`
- `TeamsMessages.urls[]` (max 5)
- `Meetings.items_by_id[]` (max 5)
- `Email.folders[]`
- `Dataverse.knowledge_sources[].tables[]`

If you **omit** these scoping arrays, the agent gets organization-wide access for that capability. If you **populate** them, the agent is scoped to the allow list.

### Mechanism 2: General-knowledge fallback control (`discourage_model_knowledge`)

Set in `behavior_overrides.special_instructions.discourage_model_knowledge: true` — controls *whether the LLM may answer from its general knowledge* when grounding finds nothing. See `declarative-agent-only-allowed-sources/appPackage/declarativeAgent.json`.

> When set to `true`, "the agent doesn't use model knowledge when generating responses." When grounding misses, the agent should respond with a fallback message rather than make something up from training data.

Microsoft Learn's `agent-builder-add-knowledge.md` (curated as `docs/agent-builder-add-knowledge.md`) calls this the **"Only use specified sources"** toggle and notes the important limitation: "Agent Builder in Microsoft 365 Copilot doesn't support *blocking* general AI knowledge from your agent's responses. For stricter control over knowledge sources, use Copilot Studio."

### Effect of `instructions` text on grounding

_TODO: Empirically describe how the natural-language `instructions` field interacts with the structural controls above. From the curated samples (`declarative-agent-only-allowed-sources/appPackage/instruction.txt`), the pattern is:_

1. _Manifest scopes capabilities (Mechanism 1)._
2. _`discourage_model_knowledge: true` blocks general-knowledge fallback (Mechanism 2)._
3. _Instructions reinforce: "Always cite Microsoft Learn", "If you can't find an answer in the documents, say so"._
4. _Plugin `description_for_model` and `function.description` fields steer the **selection** of which tool to call._

The model's grounding behavior is the composition of all four — none alone is sufficient. _TODO: annotate Spaarke's recipe._

---

## Common pitfalls

_TODO: Annotate from project experience. Likely candidates based on the curated samples and reference docs:_

- _Pitfall: SharePoint URL too deep. `items_by_url` rejects URLs more than two path segments (`/a/b/c` invalid). Always test the URL shape at agent-build time._
- _Pitfall: Web search domain limit. Max 4 sites. Beyond that you need a Copilot Connector._
- _Pitfall: Conflating `instructions` text guidance with `discourage_model_knowledge`. Instructions alone don't block training-data fallback — you need the behavior override._
- _Pitfall: MCP server `OAuthPluginVault` `reference_id` mismatch. The Teams Developer Portal OAuth Client Registration key must exactly match `${{OAUTH_REFERENCE_ID}}` in the env file. Off-by-one whitespace breaks the action with a silent 401._
- _Pitfall: Re-approval surprises. Adding a capability or new scope after rollout can pull the agent into the admin approval queue mid-flight and disable it for end users._

---

## References within this knowledge base

- Foundational: `knowledge/m365-copilot/`
- SPE knowledge source mechanics: `knowledge/sharepoint-embedded/` (Spaarke's substrate — _TODO: verify SPE→DA binding once curated_)
- Dataverse capability: `knowledge/dataverse-mcp/` (Spaarke's structured data)
- Foundry IQ binding: `knowledge/foundry-iq/`
- Server-side coordination (Agent Framework loops invoking DA tools): `knowledge/agent-framework/`, `knowledge/foundry-agent-service/`
