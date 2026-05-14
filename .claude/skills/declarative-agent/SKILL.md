---
description: Author or modify a declarative agent (manifest, knowledge sources, action plugins)
tags: [declarative-agent, copilot, m365, agent-manifest]
techStack: [m365-copilot, typescript, json]
appliesTo: ["**/appPackage/declarativeAgent.json", "**/appPackage/manifest.json", "**/instruction.txt", "**/*.tsp"]
alwaysApply: false
---

# declarative-agent

> **Category**: Development
> **Last Updated**: 2026-05-14

---

## Purpose

Anchor declarative agent (DA) authoring in current Microsoft Copilot extensibility idioms. A declarative agent is a JSON-manifest-defined agent that runs inside Microsoft 365 Copilot Chat, grounded in knowledge sources (SPE, SharePoint, OneDrive, Foundry IQ, MCP servers) and extended with action plugins. Spaarke's primary DA grounds on matter documents via an SPE container-type, calls Dataverse MCP for records, calls a custom Spaarke MCP for imperative ops, and (when available) uses Foundry IQ knowledge bases for curated content.

Without this skill, generated DAs drift toward training-data patterns that predate the current `behavior_overrides` schema, OAuthPluginVault auth, `OnlyAllowedSources`, and the migration of canonical samples to `pnp/copilot-pro-dev-samples`.

---

## Applies When

- Creating a new declarative agent manifest (`declarativeAgent.json`)
- Modifying instructions, conversation starters, knowledge source bindings, or action plugin bindings
- Wiring an MCP server (Spaarke MCP, Dataverse MCP, Work IQ MCP) into a DA
- Composing knowledge sources (SPE + Dataverse + Foundry IQ + Web)
- Configuring `OnlyAllowedSources` / `behavior_overrides` to scope retrieval
- **NOT applicable** for: server-side agent loops (use `foundry-agent` skill), MCP App widget UI (use `widget-design` skill)

---

## Workflow

### Step 1: Load knowledge context (mandatory)

Read in this order:

1. **`knowledge/declarative-agents/NOTES.md`** — Spaarke's DA shape and where it diverges from generic samples.
2. **`knowledge/m365-copilot/NOTES.md`** — Foundation layer (manifest field semantics, knowledge source wiring).
3. **`knowledge/declarative-agents/declarative-agent-foodbank-mcp/`** — Complete agent with 3 action types: OpenAPI + 2× RemoteMCPServer with OAuthPluginVault auth. Closest pattern to Spaarke's MCP-bound DA.
4. **`knowledge/declarative-agents/declarative-agent-only-allowed-sources/`** — Manifest-level `OnlyAllowedSources` via `behavior_overrides.special_instructions.discourage_model_knowledge`. Plus public Learn MCP with `enable_dynamic_discovery: false`.
5. **`knowledge/declarative-agents/declarative-agent-websearch-scoped/`** — Capability-level `OnlyAllowedSources` via `WebSearch.sites[]`.
6. **`knowledge/m365-copilot/declarative-agent-onedrive-sharepoint/`** — `OneDriveAndSharePoint` knowledge source config (template for SPE container-type binding).
7. **`knowledge/declarative-agents/docs/declarative-agent-manifest.md`** — Manifest spec reference (only if you need a specific field's exact schema).

### Step 2: Determine Spaarke's DA composition for this work

Default Spaarke DA composition (cite in commit message):

| Knowledge source | Purpose | Pattern reference |
|---|---|---|
| **SPE container-type** (SharePoint knowledge source) | Matter documents grounding | `knowledge/m365-copilot/declarative-agent-onedrive-sharepoint/` |
| **Dataverse MCP** (RemoteMCPServer) | Records: matters, contacts, tasks, playbooks | `knowledge/declarative-agents/declarative-agent-foodbank-mcp/` (multi-MCP pattern) |
| **Spaarke MCP server** (RemoteMCPServer) | Document ops, redline, compare, tabular review | `knowledge/declarative-agents/declarative-agent-foodbank-mcp/` |
| **Work IQ MCP** (RemoteMCPServer, when applicable) | Collaboration context — "who's on this matter," "what was discussed" | `knowledge/work-iq/tool-catalog.md` (remote URL pattern) |
| **Foundry IQ KB** (when available) | Curated golden documents — playbooks, exemplar contracts, legal research | `knowledge/foundry-iq/samples/agent-grounding-wiring/` |

### Step 3: Apply Spaarke contracts (ADR-013, ADR-007, ADR-021)

- **ADR-013 (AI Architecture)**: The DA is the *user-facing surface*; the heavy lifting is in BFF tool handlers exposed via Spaarke MCP. Keep instructions clear about delegation.
- **ADR-007 (SpeFileStore facade)**: When the DA's SharePoint knowledge source binds to an SPE container-type, the BFF endpoints the DA reaches MUST go through `SpeFileStore` — no direct Graph SDK types above the facade.
- **ADR-021 (Fluent UI v9)**: Any widgets surfaced by the DA must follow Fluent v9 — load `widget-design` skill before generating widget code.
- **Operational policy** (not yet ADR): bind the SPE knowledge source to the relevant client's container-type. Multi-tenant DA must NOT cross client boundaries — enforce at the BFF authorization layer.

### Step 4: Instruction and conversation-starter authoring

- **Instructions** affect grounding behavior (priority, fallback, retrieval scoping). Be explicit about which knowledge sources to prefer for which question types.
- For Spaarke, default instruction stanza pattern:
  - "When the user asks about a matter, contact, or playbook by name, call Dataverse MCP first."
  - "When the user asks to summarize or compare a document, call Spaarke MCP tools."
  - "When the user asks for legal research or precedent, prefer Foundry IQ knowledge base."
  - "When the user asks who's on a matter or what was discussed, call Work IQ MCP."
- Conversation starters: pick 4–6 that exercise different knowledge sources, not just the dominant one. This signals the agent's full surface to users.

### Step 5: Auth wiring for MCP servers

Use `OAuthPluginVault` (pattern in `declarative-agent-foodbank-mcp/appPackage/`). For the Spaarke MCP server specifically, use the vault entry tied to the BFF's Entra app registration — do NOT inline secrets.

### Step 6: Admin approval awareness

- Document which manifest changes trigger re-approval (knowledge source additions/removals, action plugin changes are major; instruction edits are usually minor).
- For Spaarke production DAs, route changes through the admin approval flow per the deployment runbook (see `docs/guides/`).

---

## Conventions

- DA manifests live under `src/solutions/<workspace>/declarative-agents/` (mirrors PCF/code-page layout per `spaarke-conventions`)
- TypeSpec source (`.tsp`) compiled with `tsp compile` — pattern in `knowledge/m365-copilot/copilot-camp-path-e/RepairServiceAgent/`
- DA app package built and packaged for upload via the M365 Agents Toolkit (formerly Teams Toolkit — renamed; see `knowledge/declarative-agents/SOURCE.md` for the rename note)
- Instruction file: `instruction.txt`, plain markdown body, no fenced code blocks

## Resources

| Resource | Purpose |
|----------|---------|
| `knowledge/declarative-agents/NOTES.md` | Spaarke DA shape, manifest field semantics |
| `knowledge/declarative-agents/declarative-agent-foodbank-mcp/` | Multi-action DA with OpenAPI + 2× MCP servers |
| `knowledge/m365-copilot/declarative-agent-onedrive-sharepoint/` | SP/OneDrive knowledge source binding |
| `knowledge/declarative-agents/docs/declarative-agent-manifest.md` | Manifest field reference |

## Output

When this skill completes, expect:
- A new or modified `declarativeAgent.json` (+ optional TypeSpec source)
- An `instruction.txt` aligned with Spaarke's delegation pattern
- Action plugin manifests wiring MCP servers and/or OpenAPI plugins
- A note in `knowledge/declarative-agents/NOTES.md` if a new pattern emerges (for senior engineer annotation pass)
