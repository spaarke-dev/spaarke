---
name: agentconnectors-plugin-package-2026-11-20
description: Exact shape of an M365 app package (manifest 1.30 agentConnectors + agentSkills) exposing a remote MCP server to Copilot Chat and Cowork; spike package check; install path; Q&A risk; post-Ignite delta (Learn, 2026-11-20)
metadata:
  type: reference
---

**Question.** Exact current shape of the M365 "plugin package" with `agentConnectors` for a remote MCP server (task 040 / spike T2, T14); does it attach to the Dev Portal connector; skills; install; Ignite delta.

**Status labels.** C = Learn/primary (retrieved 2026-11-20). U = unknown.

## Schema (C)
- Newest GA app manifest = **1.30**. Official schema `https://developer.microsoft.com/json-schemas/teams/v1.30/MicrosoftTeams.schema.json` (the `/en-us/json-schemas/...` form also 200; Learn uses both). `v1.31` URL = 404. `devPreview` exists (adds `toolSource.plugin|localMcpServer|openApiConnector`, `ConnectionVault`). 1.30 `manifestVersion` is `const "1.30"`; root `additionalProperties:false`.
- Root required: manifestVersion, version, id, developer(name, websiteUrl, privacyUrl, termsOfUseUrl), name(short<=30), description, icons(color 192x192, outline 32x32), accentColor.
- `agentConnectors[]`: **max 10**; item required `id` (<=64, unique) + `displayName` (<=128); `description` <=4000; `toolSource.remoteMcpServer` { `mcpServerUrl` (https, <=2048, required), `mcpToolDescription`{`file`} (optional from 1.29; REQUIRED at 1.28), `authorization`{`type` None|OAuthPluginVault|ApiKeyPluginVault|DynamicClientRegistration|AzureKeyVault, `referenceId` <=128, required unless None, forbidden when None (validator)} }.
- `agentSkills[]`: max 20, `{folder}` <=256, 1.28+. Skill `name` kebab-case 1-64, must equal folder name (ASKILL-P006/P007), `description` 1-1024; Cowork companion files 20/skill, 5 MB/file, 10 MB total.
- Package: ZIP with manifest.json + icons at ROOT; `skills/<name>/SKILL.md`; optional tool-description JSON. No secrets; referenceId is an id, not a secret.

## Conflicts / risks
- Cowork + `mcpToolDescription`: configure-plugin-mcp-servers and WIQD reference say Cowork STILL REQUIRES the file even at 1.29+; cowork-plugin-development says Cowork discovers dynamically and ignores it. Keep the file (safe both ways).
- Q&A 5919386 (Jun-Aug 2026): Cowork + OAuthPluginVault authenticates but tool calls fail ("unable to reach connector"); same refId works in DA. One tenant fixed by a TRAILING SPACE in the Dev Portal client ID; second tenant unresolved; no MS answer.
- Dev Portal OAuth registration must be **Any Teams app** (app-bound = 404 on every call). Base URL "should correspond" to MCP url; whether one registration covers two routes (/mcp/legal-operations + /mcp/spike-area-b) = U.
- Chat host support for package `agentConnectors` and widget rendering via that route = U (no per-host matrix). DCR-omit-authorization works only in Cowork, not Chat. API key auth not available in Cowork.

## Install (C)
- Admin: M365 admin center > Agents > Tools > Registry > Upload > choose manifest/zip > review components (MCP servers, skills) > Scope users (All / specific) > Install. Then MCP server registration appears under Agents > Tools > Requests (preview): Approve + grant Entra tenant consent (AI Admin or Global Admin). Org-level: Agents > Settings > Agent and plugin access must allow "built by your organization"; per-plugin users: Agents > Tools > Plugins > Users. Older path still in Cowork doc: Manage apps > Upload custom app > ... > Add agent.
- No-admin test routes: Cowork Customize (+) > Plugins > Upload plugin > share Only you / specific users (Org-wide needs admin); `atk install --file-path x.zip --scope Personal`; Teams upload custom app.
- User: Cowork Home > Plugins (Added/Discover/Shared with me), Sources & Skills toggle, first use = Connect/sign-in once per connector; admin cannot sign in for user. Chat: skills need Copilot Premium; Chat user steps for package connectors not documented (U).
- Propagation: federated settings up to 10 min; MCP server visible in Copilot Studio up to 30 min.

## Q3 attach vs duplicate (C+U)
Federated connector (admin center Copilot > Connectors > Your connections) and package `agentConnectors` are DIFFERENT artifacts (submit-federated-connector: "aren't interchangeable"); only the OAuth registration (referenceId) is shared. Package does not attach to an existing connector; expect a second Source (cf. T13). Learn also calls ISV connectors "federated connectors delivered as M365 app packages" (convergence), so whether an uploaded package also shows under Your connections = U.

## Ignite delta (C/secondary)
Plugins+skills governance GA (Copilot + Cowork); Plugin Catalog launched Nov 17 (secondary); Learn plugins hub (learn.microsoft.com/plugins) + WIQD recommended route published 2026-11-16..19; schema 1.30 pages unchanged since 2026-10-28; Business plugins = Frontier; MCP certification legacy path through 2026-11-18, new = Partner Center "Apps and Agents for M365 and Copilot" (preview). Book of News has no agentConnectors/federated-connector/MCP Apps item.

## Sources
learn.microsoft.com/en-us/plugins/{configure-plugin-mcp-servers, package-plugin, deploy-plugin, build-copilot-chat-skills, plugin-type-connectors, plugin-type-mcp-servers, plugin-authentication-oauth, choose-plugin-components}; /en-us/microsoft-365/extensibility/schema/{root-agent-connectors, ...-remote-mcp-server, ...-authorization, root-agent-skills}; /en-us/copilot/cowork/{cowork-plugin-development, cowork-plugins, whats-new}; /en-us/microsoft-agent-365/admin/manage-plugins-skills-mcp-servers; /en-us/microsoft-365/copilot/connectors/{manage-federated-connectors, submit-federated-connector}; microsoft.github.io/wiqd/getting-started/plugin-reference; learn Q&A 5919386; news.microsoft.com/events/ignite/2026/book-of-news; downloaded 1.30 schema JSON.
