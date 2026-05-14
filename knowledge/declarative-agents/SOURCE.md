# SOURCE — `declarative-agents`

Provenance for every curated file in this directory. Re-curators: refresh by re-cloning the source repos at the commit SHAs below, diffing against curated copies, and updating this file.

**Curated**: 2026-05-14
**Refresh owner**: Ralph Schroeder (initial)

---

## Relationship to `knowledge/m365-copilot/`

This topic is the **deeper companion** to `knowledge/m365-copilot/`. The sibling already curated three foundational PnP samples (`da-RubberDuck`, `da-product-support`, `da-todo-tasks-graphapi-plugin`) plus a TypeSpec lab (`RepairServiceAgent`). To avoid duplication, the samples below were deliberately chosen to demonstrate **patterns the sibling does not cover**:

| Pattern | `knowledge/m365-copilot/` | `knowledge/declarative-agents/` (this folder) |
|---|---|---|
| Minimal manifest | ✅ `declarative-agent-basic/` | not curated |
| Single SharePoint scoping | ✅ `declarative-agent-onedrive-sharepoint/` | not curated |
| Single OpenAPI plugin (Graph) | ✅ `declarative-agent-api-plugin/` | not curated |
| TypeSpec authoring | ✅ `copilot-camp-path-e/` (RepairServiceAgent) | not curated |
| **Multi-plugin: OpenAPI + multiple Remote MCP Servers** | not curated | ✅ `declarative-agent-foodbank-mcp/` |
| **`behavior_overrides.special_instructions.discourage_model_knowledge` (OnlyAllowedSources behavior)** | not curated | ✅ `declarative-agent-only-allowed-sources/` |
| **Microsoft Learn Remote MCP Server (no auth)** | not curated | ✅ `declarative-agent-only-allowed-sources/` (also covers this) |
| **Multi-capability + bounded WebSearch scoping** | not curated | ✅ `declarative-agent-websearch-scoped/` |

Senior engineers reviewing both folders should treat them as a single curated body, not as duplicates.

---

## Source repos (cloned 2026-05-14)

| Repo | Commit SHA | Clone URL | Notes |
|---|---|---|---|
| `pnp/copilot-pro-dev-samples` | `f70c9087d2dff8abca2d31aaae61e4d2727c7df4` | `https://github.com/pnp/copilot-pro-dev-samples` | Same repo `m365-copilot/` pulled from. Selected three *different* samples here. See "Directive override" below. |

### Directive override

The setup directive listed `OfficeDev/microsoft-365-copilot-samples` as the primary source for this topic. **That repo was migrated by Microsoft on 2026-01-02 to `pnp/copilot-pro-dev-samples`** and the OfficeDev repo will be retired 2026-01-30. We pulled from PnP as documented in `knowledge/m365-copilot/SOURCE.md`. Update the directive's source list at next senior-engineer pass.

The directive also listed `microsoft/teams-toolkit` as a secondary source. **That URL returns 404** — Teams Toolkit was renamed/rehomed to the "Microsoft 365 Agents Toolkit" project distributed via the VS Code marketplace; the GitHub repo does not exist under that canonical name. We did not substitute a replacement because the patterns the directive wanted (knowledge bindings, MCP server tool bindings, OnlyAllowedSources) were already cleanly demonstrated by PnP samples. See "Gaps" below.

The directive also suggested MVP samples via `gh search`. We searched `garrytrinder`, `waldekmastykarz`, `bobgerman` — no hits matching `declarative` in their public org repos at search time. A broader `gh search repos "declarative agent MCP"` surfaced `microsoft/MCAPSTechConnect26-LAB485` (cloned at `b213e7995230d0bb2246f569b1c871c40391c0b2` for inspection) but it is a lab walkthrough — no novel sample code worth curating beyond the PnP samples already selected.

---

## Curated samples

### `declarative-agent-foodbank-mcp/`

**Source**: `pnp/copilot-pro-dev-samples` → `samples/da-foodbank-friend/`
**Demonstrates**: A **complete declarative agent** combining three action types in one manifest:
1. An external **OpenAPI** plugin (no-auth GiveFood API for UK food bank lookup)
2. A **Remote MCP Server** plugin for SharePoint Lists (OAuth-protected, `agent365.svc.cloud.microsoft/agents/servers/mcp_SharepointListsTools`)
3. A **Remote MCP Server** plugin for Outlook Calendar (OAuth-protected, `agent365.svc.cloud.microsoft/agents/servers/mcp_CalendarTools`)

Schema versions: declarative agent `v1.6`, plugin manifests `v2.4`. This is the canonical reference for the directive's requirement "one agent that uses an MCP server (custom MCP, not just built-in tools)". It also satisfies "one complete agent with full manifest, instructions, conversation starters, knowledge sources, action plugins" — the manifest pulls all the action wiring together.

| File | Origin | What it shows |
|---|---|---|
| `README.md` | `samples/da-foodbank-friend/README.md` | Sample overview, Entra app registration, OAuth client registration in Teams Developer Portal, SharePoint list setup |
| `appPackage/declarativeAgent.json` | same | Three-action manifest: `givefood_api`, `sharepoint_mcp`, `outlook_mcp`, with conversation starters |
| `appPackage/instruction.txt` | same | Multi-tool agent prompt orchestrating GiveFood → Outlook → SharePoint |
| `appPackage/manifest.json` | same | Teams app manifest (v1.25) with `copilotAgents.declarativeAgents[]` entry |
| `appPackage/ai-plugin-givefood.json` | same | Plugin manifest with `runtimes[].type: OpenApi`, `auth.type: None` |
| `appPackage/ai-plugin-sharepoint.json` | same | Plugin manifest with `runtimes[].type: RemoteMCPServer`, `auth.type: OAuthPluginVault`, MCP server URL |
| `appPackage/ai-plugin-outlook.json` | same | Plugin manifest with `runtimes[].type: RemoteMCPServer`, `auth.type: OAuthPluginVault`, MCP server URL |
| `appPackage/apiSpecificationFile/givefood-openapi.yaml` | same | OpenAPI 3.0 spec for the GiveFood UK API, referenced by `ai-plugin-givefood.json` |

**Files deliberately omitted from `da-foodbank-friend/`**: `assets/` (screenshots, GIF), `env/` (env-var templates with placeholders), `m365agents.yml` (Agents Toolkit build descriptor — included in `declarative-agent-only-allowed-sources/` for reference; not needed in every sample), `appPackage/color.png` / `outline.png` (Teams app icons).

### `declarative-agent-only-allowed-sources/`

**Source**: `pnp/copilot-pro-dev-samples` → `samples/da-sharepoint-data-manager/`
**Demonstrates**: The directive's required pattern "one agent that demonstrates the `OnlyAllowedSources` behavior." The pattern manifests in the JSON as:

```json
"behavior_overrides": {
    "special_instructions": {
        "discourage_model_knowledge": true
    }
}
```

This is the manifest-level equivalent of the Agent Builder UI toggle "Only use specified sources" (commonly referenced in Microsoft documentation and product UX as "OnlyAllowedSources"). It forces the model to ground in declared sources rather than answering from general knowledge.

The sample combines that behavior override with three knowledge surfaces:
1. The `OneDriveAndSharePoint` capability (open-scoped)
2. The `GraphicArt` capability (for SharePoint architecture diagrams)
3. A **Remote MCP Server** plugin pointing at the **public Microsoft Learn MCP** (`https://learn.microsoft.com/api/mcp`, no auth, `enable_dynamic_discovery: false`)

Schema versions: declarative agent `v1.5`, plugin manifest `v2.1`.

| File | Origin | What it shows |
|---|---|---|
| `README.md` | `samples/da-sharepoint-data-manager/README.md` | Sample overview, MS Learn MCP integration walkthrough |
| `appPackage/declarativeAgent.json` | same | `behavior_overrides.special_instructions.discourage_model_knowledge: true`, multi-capability composition |
| `appPackage/instruction.txt` | same | Agent persona, organizational guidance, MS Learn citation instructions |
| `appPackage/ai-plugin.json` | same | Plugin manifest pointing at `https://learn.microsoft.com/api/mcp` — public, no-auth Remote MCP Server |
| `appPackage/manifest.json` | same | Teams app manifest |

### `declarative-agent-websearch-scoped/`

**Source**: `pnp/copilot-pro-dev-samples` → `samples/da-BlogPostHelper/`
**Demonstrates**: The second flavor of OnlyAllowedSources: **manifest-level source scoping** via `WebSearch.sites[]`. Where `discourage_model_knowledge` controls *whether* the model uses general knowledge, `sites[]` controls *which* external sources Web Search may visit. This is the simplest "only allowed sources" expression — Microsoft Learn's Web search object docs explicitly note: "If you omit this property, the agent can search all sites. The array can't contain more than four items."

Combines `WebSearch` (scoped to a single blog domain) + `GraphicArt` + `CodeInterpreter` — no actions, no MCP, pure capabilities-driven grounding. Useful as the minimal "scoped-grounding" reference.

Schema version: declarative agent `v1.2`. (The sample predates `v1.6`; the `WebSearch.sites` field has been stable across schemas.)

| File | Origin | What it shows |
|---|---|---|
| `README.md` | `samples/da-BlogPostHelper/README.md` | Sample overview, deployment walkthrough |
| `appPackage/declarativeAgent.json` | same | `WebSearch.sites[]` scoping, multi-capability stack |
| `appPackage/instruction.txt` | same | Agent persona for blog-post helper grounded in one site |
| `appPackage/manifest.json` | same | Teams app manifest |

---

## Reference docs snapshotted (`docs/`)

| File | Source URL | Fetched |
|---|---|---|
| `docs/declarative-agent-manifest.md` | `https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/declarative-agent-manifest` | 2026-05-14 |
| `docs/agent-builder-add-knowledge.md` | `https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/agent-builder-add-knowledge` | 2026-05-14 |
| `docs/declarative-agent-architecture.md` | `https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/declarative-agent-architecture` | 2026-05-14 |

All three fetched cleanly; no 404s. The manifest doc states the **current latest schema is v1.7**, and the page Microsoft serves at the `/declarative-agent-manifest` URL describes **v1.6** (it forwards to `declarative-agent-manifest-1.6` in the canonical URL). At next refresh, also fetch `declarative-agent-manifest-1.7` to capture the latest field set.

---

## Gaps

- **`microsoft/teams-toolkit` returns 404** (directive-listed). Replaced de facto by the "Microsoft 365 Agents Toolkit" VS Code extension; the underlying GitHub project is not at the directive URL. No samples lost — all the Agents Toolkit-produced patterns the directive cared about (manifest structure, plugin wiring, `m365agents.yml` build descriptor) are visible in the PnP samples above, which themselves use Agents Toolkit.
- **No MVP-authored samples curated.** `gh search` against the suggested MVP usernames (`garrytrinder`, `waldekmastykarz`, `bobgerman`) returned no `declarative`-named repos. We did not exhaust the MVP space — a more thorough search at next refresh could surface community patterns (especially around MCP tool composition and admin-approval automation). Suggested searches for next refresh: `gh search repos "declarative agent" --owner pnp --limit 50`, plus the Office 365 Engineering Tooling MVP list at `https://mvp.microsoft.com/`.
- **Embedded knowledge sample not curated.** The directive's NOTES.md guidance includes "knowledge source types: ... embedded files". The Microsoft Learn manifest doc notes embedded knowledge is **not yet available** as of the v1.6 schema. No PnP sample exercises this capability today.
- **Admin approval re-approval triggers not directly demonstrated in code.** No sample manifests this concern — it is purely a tenant-admin / Teams Admin Center concern. Annotate from operations experience when reviewing.
