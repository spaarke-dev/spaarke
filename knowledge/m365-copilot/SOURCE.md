# SOURCE — `m365-copilot`

Provenance for every curated file in this directory. Re-curators: refresh by re-cloning the source repos at the commit SHAs below, diffing against curated copies, and updating this file.

**Curated**: 2026-05-14
**Refresh owner**: Ralph Schroeder (initial)

---

## Source repos (cloned 2026-05-14)

| Repo | Commit SHA | Clone URL | Notes |
|---|---|---|---|
| `pnp/copilot-pro-dev-samples` | `f70c9087d2dff8abca2d31aaae61e4d2727c7df4` | `https://github.com/pnp/copilot-pro-dev-samples` | **NEW canonical location.** The originally directive-listed repo `OfficeDev/microsoft-365-copilot-samples` (`Microsoft-365-Copilot-Samples`, SHA `3ea462a329c64a9e8226d88d48954f4b4cdd9b75`) was migrated by Microsoft on 2026-01-02 to this PnP community-maintained repo. The OfficeDev repo's `samples/README.md` now contains only the migration notice and will be retired on 2026-01-30. Future refreshes pull from PnP. |
| `microsoft/copilot-camp` | `f0ebf675a85aaee81749d07670a221faf4169b31` | `https://github.com/microsoft/copilot-camp` | Hands-on labs for extending Microsoft 365 Copilot. Path E lab series covers declarative agents end-to-end. |

> GAP: The directive listed `OfficeDev/microsoft-365-copilot-samples` as primary. That repo was verified reachable but is in retirement (migration banner only). We curated from the PnP follow-on, `pnp/copilot-pro-dev-samples`, which Microsoft links from the deprecated repo's README. Update the directive sources list at next senior-engineer pass.

---

## Curated samples

### `declarative-agent-basic/`
**Source**: `pnp/copilot-pro-dev-samples` → `samples/da-RubberDuck/`
**Demonstrates**: The minimum-viable declarative agent — manifest only, no knowledge sources, no actions. Shows the smallest possible `declarativeAgent.json` with `$schema` v1.6, `name`, `description`, `instructions`.

| File | Origin | What it shows |
|---|---|---|
| `README.md` | `samples/da-RubberDuck/README.md` | Sample overview, build/run prerequisites |
| `appPackage/declarativeAgent.json` | `samples/da-RubberDuck/appPackage/declarativeAgent.json` | Minimal v1.6 manifest with `instructions` reference to `instruction.txt` |
| `appPackage/instruction.txt` | `samples/da-RubberDuck/appPackage/instruction.txt` | The agent's system prompt (referenced via `$[file('instruction.txt')]`) |
| `appPackage/manifest.json` | `samples/da-RubberDuck/appPackage/manifest.json` | Teams app manifest that hosts the declarative agent |

### `declarative-agent-onedrive-sharepoint/`
**Source**: `pnp/copilot-pro-dev-samples` → `samples/da-product-support/`
**Demonstrates**: A declarative agent grounded on SharePoint documents via the `OneDriveAndSharePoint` capability with `items_by_url` configuration. Shows `${{DOCUMENTS_URL}}` env-var substitution pattern used by the Agents Toolkit build pipeline. v1.4 manifest schema. (Note: the source sample also ships `.docx` reference documents used by the deployed agent — we omitted them; the manifest is what matters for pattern reference.)

| File | Origin | What it shows |
|---|---|---|
| `README.md` | `samples/da-product-support/README.md` | Sample overview, SharePoint site setup steps, doc upload |
| `appPackage/declarativeAgent.json` | `samples/da-product-support/appPackage/declarativeAgent.json` | v1.4 manifest with `OneDriveAndSharePoint` capability and `items_by_url` binding |
| `appPackage/instruction.txt` | `samples/da-product-support/appPackage/instruction.txt` | Agent persona and grounding instructions for the SharePoint content |
| `appPackage/manifest.json` | `samples/da-product-support/appPackage/manifest.json` | Teams app manifest |

### `declarative-agent-api-plugin/`
**Source**: `pnp/copilot-pro-dev-samples` → `samples/da-todo-tasks-graphapi-plugin/`
**Demonstrates**: A declarative agent that calls Microsoft Graph (no custom backend required) via an OpenAPI-described action plugin. Shows the three-file pattern: `declarativeAgent.json` references `ai-plugin.json` which references an OpenAPI YAML spec. Uses `OAuthPluginVault` runtime auth. v1.3 declarative agent manifest, v2.2 plugin manifest.

| File | Origin | What it shows |
|---|---|---|
| `README.md` | `samples/da-todo-tasks-graphapi-plugin/README.md` | Sample overview, Entra app registration, deployment walkthrough |
| `appPackage/declarativeAgent.json` | same | Declarative agent with `actions[]` array pointing at `ai-plugin.json` |
| `appPackage/ai-plugin.json` | same | API plugin manifest — `functions[]` definitions, `runtimes[].type: OpenApi`, `OAuthPluginVault` auth |
| `appPackage/instruction.txt` | same | System prompt steering the agent toward the To-Do tool |
| `appPackage/manifest.json` | same | Teams app manifest |
| `appPackage/apiSpecificationFile/openapi.yml` | same | The OpenAPI YAML the plugin's `runtimes[0].spec.url` points at — defines `getTasksFromList` and `createTaskInList` operations against `graph.microsoft.com/v1.0/me/todo/lists` |

### `copilot-camp-path-e/`
**Source**: `microsoft/copilot-camp` → `docs/pages/extend-m365-copilot/` and `src/extend-m365-copilot/path-e-lab01-declarative-copilot/RepairServiceAgent/`
**Demonstrates**: The "Path E — Extend Microsoft 365 Copilot" lab series. We curated the walkthrough markdown for the foundational labs (00, 01, 03, 04) and the complete Lab 01 sample app (`RepairServiceAgent`). The lab series covers: TypeSpec-authored declarative agents → adding an API plugin → enhancing the plugin with adaptive cards. Later labs (auth, MCP, MCP apps) live in the same `docs/pages/extend-m365-copilot/` directory in upstream but we didn't snapshot them here — pull them at next refresh if needed.

| File / subdir | Origin | What it shows |
|---|---|---|
| `labs/index.md` | `docs/pages/extend-m365-copilot/index.md` | Lab series landing page, navigation to all labs |
| `labs/00-prerequisites.md` | same dir | Setup requirements (M365 dev tenant, Node, Agents Toolkit, etc.) |
| `labs/01-typespec-declarative-agent.md` | same dir | Lab 1 walkthrough — build a declarative agent with TypeSpec |
| `labs/03-add-declarative-agent.md` | same dir | Lab 3 — add a declarative agent on top of a deployed API |
| `labs/04-enhance-api-plugin.md` | same dir | Lab 4 — enhance the API plugin with adaptive cards |
| `RepairServiceAgent/README.md` | `src/.../RepairServiceAgent/README.md` | Lab 1 sample setup instructions |
| `RepairServiceAgent/AGENTS.md` | same dir | Per-sample AI agent guidance for IDE assistants |
| `RepairServiceAgent/tspconfig.yaml` | same dir | TypeSpec emitter configuration |
| `RepairServiceAgent/m365agents.yml` | same dir | Agents Toolkit (TTK successor) project descriptor |
| `RepairServiceAgent/appPackage/manifest.json` | same dir | Teams app manifest for the compiled agent |
| `RepairServiceAgent/appPackage/adaptiveCards/repair.json` | same dir | Adaptive Card for the repair action response |
| `RepairServiceAgent/appPackage/adaptiveCards/searchIssues.json` | same dir | Adaptive Card for search responses |
| `RepairServiceAgent/src/agent/main.tsp` | same dir | Root TypeSpec file — `@agent` decorator, imports |
| `RepairServiceAgent/src/agent/actions/actions.tsp` | same dir | TypeSpec definitions of the agent's actions (compiles to plugin manifest) |
| `RepairServiceAgent/src/agent/prompts/instructions.tsp` | same dir | TypeSpec-authored agent instructions |

**Files deliberately omitted from `RepairServiceAgent/`**: `package.json`/`package-lock.json` (build harness, large), `.vscode/` settings, `assets/` (screenshots), `env/` (env-var templates with placeholders), `scripts/generate-env.js`, `m365agents.local.yml`. None of these are needed to read the agent's pattern.

---

## Reference docs snapshotted (`docs/`)

| File | Source URL | Fetched |
|---|---|---|
| `docs/overview-declarative-agent.md` | `https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/overview-declarative-agent` | 2026-05-14 |
| `docs/whats-new.md` | `https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/whats-new` | 2026-05-14 |
| `docs/optimize-content-retrieval.md` | `https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/optimize-content-retrieval` | 2026-05-14 |

All three fetched cleanly; no 404s. The `whats-new.md` page is the highest-churn reference doc in this topic — re-snapshot every refresh.
