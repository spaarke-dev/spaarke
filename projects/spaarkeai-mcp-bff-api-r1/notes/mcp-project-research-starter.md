# Spaarke MCP server and Copilot Decisions: research starter

> **Status:** research input, 2026-11-05. Not a design. Feed it to `/design-to-spec` for project `spaarkeai-mcp-bff-api-r1`. Moved here from `spaarke-legal-ai-accelerator-r1/notes/`.
> **Sources:**
> - researcher notes `mcp-server-dotnet-bff-copilot-widget-2026-11-05.md`, `copilot-as-spaarke-surface-and-frontier-2026-11-05.md`, `m365-copilot-agent-guest-crosstenant-2026-10-08.md`;
> - `projects/ai-m365-copilot-integration/` (the April 2026 attempt);
> - `projects/customer-provisioning-orchestration-r1/notes/t257-copilot-agent-design.md`;
> - `notes/copilot-as-spaarke-surface-2026-11-05.md` (in this folder);
> - the code paths cited inline.
>
> **Tags:** **C** Microsoft or MCP primary source · **S** secondary · **U** unknown, needs a test · **Repo** verified in code.

---

## 1. Goal and v1 scope

**Goal.** Give attorneys who don't need the full Spaarke app a light Spaarke surface inside Microsoft Copilot. Build it on a Spaarke MCP server in the BFF, so Copilot uses **our API and our rules** rather than Dataverse's built-in MCP server.

**v1 (owner, 2026-11-05):**

| In | Out |
|---|---|
| A list of the user's Decision Records, filtered by class (All / Judgements / Routine / Dismissals), as in the Console tab | The full Console or `LegalWorkspaceApp` |
| Open a record **as a modal**, read-only, as in Spaarke | Making or editing decisions; any write |
| Spaarke **Power Apps–licensed users**, signing in through the Entra tenant that hosts the BFF | Customer workforce or contact users, B2B guests, other tenants (T257) |
| Copilot chat (web and desktop) through a Spaarke declarative agent | Cowork, Word, Outlook, Teams, mobile; federated connector; Connectors Gallery |

## 2. Terms

| Term | Meaning here |
|---|---|
| **Tool** | A named operation on **our** server that Copilot can call mid-conversation. It has a typed input schema and returns a result (text, structured data, and optionally a widget). The logic and data stay on the server; the user's token travels with the call. |
| **MCP server** | The endpoint (`/mcp` on the BFF) that lists our tools and resources and runs them, using the Model Context Protocol |
| **MCP App / widget** | Our UI: a self-contained HTML resource (`ui://…`) that a tool links to. Copilot renders it in a sandboxed frame. It talks to Copilot through a message bridge, never directly to our API with a token. |
| **Declarative agent (DA)** | A "Spaarke" agent in Copilot's agent list: instructions plus actions. Our MCP server is one action. Our tools exist only inside this agent. |
| **Federated connector** | The same MCP server, registered once by a tenant admin. Its tools become available to Copilot generally, including the pane in Word and Outlook. Needs a Copilot licence. Later, not v1. |
| **Skill / playbook** | Accelerator concepts (Cowork instructions and scripts; positions content). Not part of this project. |

## 3. Project shape (recommendation)

**One project that builds the MCP foundation through the Decisions slice.** Not a stand-alone "MCP platform" project with no consumer.

- **Phase A, foundation:**
  - `/mcp` endpoint, stateless, behind the BFF's existing auth;
  - tool conventions;
  - the widget build pipeline (single-file HTML);
  - the test harness;
  - DA packaging and the Entra SSO registration.
- **Phase B, the Decisions slice:**
  - one read tool;
  - the Decisions widget with its modal;
  - a tenant test in Copilot.
- **Later slices are separate small projects** that add tools: matter snapshot, document find, intake, Cowork accelerator tools, a federated connector.

**Why combine:**
- Auth, as-the-user reads, Copilot's limits and widget rendering can only be proven end to end with a real host.
- CLAUDE.md §11 asks each new component for a concrete "cost of doing nothing"; the slice supplies it.
- The April attempt exposed 28 functions, and Copilot misused them. Start with one or two tools.

## 4. Platform facts (November 2026)

### 4.1 MCP specification
- **Current revision 2026-07-28 (C):**
  - stateless by design: no sessions or `initialize`, and `server/discover` is required;
  - MRTR replaces server-initiated elicitation;
  - Tasks became an extension.
  - Proxies must forward `Mcp-Method`, `Mcp-Name` and `MCP-Protocol-Version`.
- **Client registration (C):** dynamic client registration is deprecated in favour of Client ID Metadata Documents. Entra supports neither, so Copilot uses a **static** registration (§4.4).
- **MCP Apps:** the extension `io.modelcontextprotocol/ui` (SEP-1865) is stable at spec 2026-01-26 (C).

### 4.2 .NET SDK
- **Package (C):** `ModelContextProtocol.AspNetCore` **2.3.1** (2026-11-03, a security fix for stateful sessions). 2.0.0 went stable 2026-07-28. Apache-2.0; targets net8, net9 and net10.
- **Stateless is the default** (`HttpServerSessionMode.Stateless`) and recommended (C):
  - tools resolve from `HttpContext.RequestServices`, so scoped OBO and caller-identity services behave as they do in a minimal API endpoint;
  - `ClaimsPrincipal? user` can be injected into a tool method.
- **Auth (C):**
  - `AddMcp()` serves RFC 9728 protected-resource metadata and the `WWW-Authenticate` challenge;
  - `MapMcp("/mcp").RequireAuthorization()`;
  - `AddAuthorizationFilters()` trims `tools/list` per user.
- **Attributes (C):**
  - `[McpServerTool(ReadOnly = true, UseStructuredContent = true)]`, which emits `readOnlyHint` and `structuredContent`;
  - `[McpMeta]`;
  - `CallToolResult.StructuredContent` and `.Meta`.
- **Footprint:** about 1.6 MB of DLLs. It pulls `Microsoft.Extensions.AI.Abstractions` 10.11.0; the BFF references `Microsoft.Extensions.AI` **10.3.0** (Repo: `Sprk.Bff.Api.csproj:82`). Expect a version bump, so measure publish size and check for CVEs (CLAUDE.md §10).
- **Known trap (C, Toolkit issue #15421, closed as fixed 2026-03-05):**
  - the SDK returns **406** unless `Accept` contains both `application/json` and `text/event-stream`;
  - DAs once sent only `application/json`.
  - **Make this the first spike.**

### 4.3 MCP Apps (widgets)

**Server contract (C):**
- the tool declares `_meta.ui.resourceUri = "ui://spaarke/decision-records"`;
- `resources/read` returns MIME `text/html;profile=mcp-app` with fully inline HTML;
- `_meta.ui.csp` declares `connectDomains`, `resourceDomains` and the rest. Declare none: all data goes through `tools/call`.
- Tools for the widget only are marked `visibility: ["app"]`.

**C# helper (C):** `ModelContextProtocol.Extensions.Apps` 2.3.1 is **experimental** (`<NoWarn>MCPEXP003</NoWarn>`). It provides:
- `[McpAppUi(ResourceUri = …, Visibility = [McpUiToolVisibility.App])]`;
- `.WithMcpApps()`;
- `McpUiResourceMeta` for the CSP.

Sample: `csharp-sdk/samples/WeatherAppServer`.

**Widget bridge:** `ui/initialize` → `ui/notifications/initialized` → tool input and tool result. TypeScript helper: `@modelcontextprotocol/ext-apps`. Our reference hook is `knowledge/mcp-apps/trey-research/src/mcpserver/widgets/src/hooks/useMcpApp.tsx`.

**What Copilot supports (C, Learn 2026-09-30):**

| Supported | Not supported |
|---|---|
| `callServerTool`, `sendMessage`, `openLink`, `updateModelContext`, `readOnlyHint` | **`requestModal`**, `onhostcontextchanged`, `destructiveHint`, `frameDomains`, `permissions` |
| `requestDisplayMode`: fullscreen only | — |

**Microsoft's UX guidance (C):** 3–5 focused widgets. "Resembles your entire SaaS product" exceeds scope. Avoid multi-tab shells.

**Modal (U):** Copilot does **not** support a host modal (`requestModal`). Our modal would be a Fluent `Dialog` rendered **inside** the widget frame. Whether that works inline (max about 640 px tall) and in fullscreen is untested. Fallbacks, in order:
1. fullscreen plus an in-frame dialog;
2. master-detail in fullscreen;
3. `openLink` to the Console record.

**Limits:**
- Cowork (C): 64 KiB inline result, 60 widget calls a minute, 10 s init, inline 200–720 px wide and up to 640 px tall.
- Copilot chat: none documented (U).
- Learn's troubleshooting page says to keep `content` small and put widget data in `_meta` (C).

### 4.4 The declarative agent with an MCP action

**Schemas:** DA **v1.9**, plugin **v2.4**, app manifest **v1.30**, all numbered, so no `devPreview` is needed (C). The repo's trey-research sample uses older names (v1.6, `x-mcp_tool_description`); use the current ones.

```json
// declarativeAgent.json
{ "version": "v1.9", "name": "Spaarke", "instructions": "…", "actions": [{ "id": "spaarke", "file": "ai-plugin.json" }] }

// ai-plugin.json
{ "schema_version": "v2.4", "namespace": "spaarke", "functions": [],
  "runtimes": [{ "type": "RemoteMCPServer",
    "spec": { "url": "https://<bff>/mcp" },
    "run_for_functions": ["*"],
    "auth": { "type": "OAuthPluginVault", "reference_id": "<sso-registration-id>" } }] }
```

- **Tool list:** dynamic discovery is the default, and widget tools are picked up automatically. A pinned list uses `mcp_tool_description: { file | tools }` (C).
- **Scaffolding:** Agents Toolkit 6.12 or later, or `wiqd plugin add connector` (alpha) (C).

**Auth: Entra SSO, recommended for same-tenant v1 users (C):**
1. Create the SSO auth config. It yields an Application ID URI (`api://auth-…/<bffAppId>`).
2. Add that URI to the BFF app's `identifierUris`.
3. Add the web redirect `https://teams.microsoft.com/api/platform/v1.0/oAuthConsentRedirect`.
4. Pre-authorize the token store client `ab3be6b7-f5df-413d-ac2d-abf1e3fd9c0b`.
5. The BFF accepts that audience. **This merge already exists** (Repo: `Infrastructure/DI/AuthorizationModule.cs` ~L129–157, `AgentToken:CopilotAudience`; token documented in `appsettings.tokens.md:60`).

The April attempt found SSO "didn't work" and used OAuth (`OAuthPluginVault` with an OAuth registration, redirect `…/oAuthRedirect`). Re-test SSO first, keeping OAuth plus PKCE as the fallback.

**No Microsoft sample combines .NET, Entra and a widget.** The only Entra sample is the Node `expense-submission` (SSO plus OBO), in `microsoft/mcp-interactiveUI-samples`. Expect to be first (C).

### 4.5 Registration models and reach (C)

| Model | Reach | Notes |
|---|---|---|
| **DA action** (`RemoteMCPServer`) — **v1** | Inside the Spaarke agent; widgets in Copilot web and desktop | Teams, Outlook, Word and mobile rendering is undocumented (U). A DA can hold up to 10 actions. |
| Plugin with `agentConnectors` | Cowork | Needs a static tool-description file; credits per call |
| Federated connector | Chat, agents, Cowork, Word, Excel, PowerPoint, Outlook, Researcher | Admin-created or published through the Connectors Gallery (the Harvey, iManage and Litera route). Tools only. Widget rendering is unknown. |

### 4.6 Licensing (C)
- A DA whose actions are MCP tools (and MCP Apps), with **no tenant-data knowledge**, works in Copilot Chat **without a Copilot licence and without metering**.
- A federated connector needs a Copilot licence.
- Cowork needs a spending policy.
- The customer tenant pays, never Spaarke.

### 4.7 Hosting on App Service
- **Stateless:** session affinity can be off (C). Turn Always On on.
- **Time limits:** tool calls should finish **under 30 s** (C, connector guidance). App Service's request limit is about 230 s (S).
- **Headers:** front doors and gateways must pass the MCP headers (§4.1).

### 4.8 Security (C unless marked)
- **Tokens:**
  - validate the audience;
  - never accept or **pass through** a token meant for someone else;
  - OBO (or our caller-identity client) to downstream services is compliant.
- **Scope challenges:** insufficient scope gets a 403 with `WWW-Authenticate` naming every required scope in one challenge.
- **Confused deputy:** applies to OAuth proxies. Don't build one in the BFF.
- **Prompt injection:** Copilot screens tool *definitions*. Nothing is documented for tool *results*, so treat record text as untrusted (judgement).
- **Consent:** tools without `readOnlyHint` prompt for consent on every call; `destructiveHint` is ignored. Whether a write started from a widget prompts is U, and irrelevant for v1, which is read-only.
- **Repo rules:** ADR-028 (auth), CLAUDE.md §9 (every endpoint needs auth), ADR-002/write-path (no writes in v1).

### 4.9 Why not Dataverse's built-in MCP server (C, Learn 2026-11-04)

`/api/mcp` has 16 tools, including `invoke_api` for Custom APIs. It is delegated and respects security roles. But it has:
- no widgets (U);
- no Spaarke task tools;
- no BFF rules;
- no SharePoint Embedded and no AI.

**Decision Records specifically can't be read correctly through it.** Signal read depth is Parent: Child BU, so a direct read can't apply the core-record check. That is why the Console reads only through the BFF route (Repo: `SignalReadEndpoints.cs` remarks, D-15, FR-24).

It could sit beside our server as a second DA action later. **Not in v1:** the April attempt showed Copilot preferring native Dataverse over our tools.

## 5. What Spaarke already has (reuse)

| Asset | Where | Use in v1 |
|---|---|---|
| Decisions read route `GET /api/v1/signals/worklist?set=decided`, reading **as the caller** | `Api/Signals/SignalReadEndpoints.cs`; `Services/Signals/SignalReadService.cs` (`ReadAsync(set, user)`, `FetchAllAsCallerAsync`) | The tool calls `SignalReadService.ReadAsync(WorklistSet.Decided, user)` directly. No new data access. |
| `DecisionRecordsWidget` | `Spaarke.UI.Components/src/components/DecisionRecords/DecisionRecordsWidget.tsx` | The widget's list. It takes a `WorklistService`, so **the data seam already exists**. |
| `createWorklistService({ fetch, baseUrl })` | `…/Worklist/worklistService.ts:340` | Swap `fetch` for an adapter that calls `app.callServerTool(...)` and returns a `Response` built from the tool's structured result |
| Read-only decision wizard (the modal) | `…/DecisionWizard/DecisionWizardHost.tsx`, `decisionWizardLauncher` | Open builds the wizard item from rows already held (`buildWizardItem`), with no second read. Its `Xrm` lookup is only for editing (`DecisionWizard.tsx:63`), so read-only needs no `Xrm`. |
| Copilot audience merge | `Infrastructure/DI/AuthorizationModule.cs` (`AgentToken:CopilotAudience`) | SSO audience acceptance |
| April DA package and deploy script | `src/solutions/CopilotAgent/`, `scripts/Deploy-CopilotAgent.ps1` | Starting point. Replace the OpenAPI action (28 functions, plugin v2.2, `devPreview`) with one MCP runtime on v1.9/v2.4/v1.30. |
| Per-customer agent template | T257 (`customer-provisioning-orchestration-r1`) | Not needed for v1 (same-tenant users). Needed when the scope reaches customer users. |
| MCP Apps samples and docs | `knowledge/mcp-apps/` (trey-research, approvals-box) | Widget bridge hook, build script (`widgets/build.mts`) |
| Skills | `widget-design`, `declarative-agent`, `mcp-tool-handler` | See the drift below |

**Drift to fix in this project.** `.claude/skills/mcp-tool-handler/SKILL.md` describes an existing "Spaarke MCP server" with `AiToolService` and `AiToolEndpoints`. **Neither exists** (Repo, 2026-11-05: no `MapMcp` or MCP package in the BFF). `IAiToolHandler` is a playbook tool interface, not MCP. Rewrite the skill once the real conventions exist.

## 6. Sketch of the v1 architecture (for design, not decided)

```
Copilot chat ──(user picks "Spaarke" agent; SSO token via token store)──▶ BFF  POST /mcp   (stateless, RequireAuthorization)
   │                                                                     ├─ tool  list_decision_records   readOnly, model-visible
   │                                                                     │     → SignalReadService.ReadAsync(Decided, user)   (as caller)
   │                                                                     │     → content: short text summary; structuredContent/_meta: rows
   │                                                                     │     → _meta.ui.resourceUri = ui://spaarke/decision-records
   │                                                                     ├─ tool  refresh_decision_records  readOnly, visibility ["app"]
   │                                                                     └─ resource ui://spaarke/decision-records  (text/html;profile=mcp-app)
   ▼
widget frame: single-file React 18 + Fluent v9 bundle
   DecisionRecordsWidget(service = createWorklistService({ fetch: mcpFetchAdapter }))
   + DecisionWizardHost (read-only modal, in-frame Dialog; fullscreen when opened)
```

**Design points to settle:**
1. **The tool's output contract.** Reuse `WorklistResponse` unchanged (the adapter stays trivial) or slim it for the 64 KiB limit. Also decide paging, and what happens when the list is too large.
2. **Which tool the model sees.** The model-visible tool's description is the routing surface: ≤ 1,024 characters (longer is ignored), with exact "use for / not for" wording.
3. **Bundle budget.** React, Fluent v9 and the Spaarke components in one file, against an undocumented init limit (10 s in Cowork). Tree-shake and measure.
4. **Theme** comes from the host context (no theme-change event in Copilot). **Storage** is per widget origin, so keep nothing sensitive in it.
5. **Where the HTML comes from.** An embedded resource in the BFF or a build artifact. Its size counts toward BFF publish size.
6. **Telemetry:** tool latency, row counts, 403/503 counts, correlation id.

## 7. Repo obligations the design must meet

| Obligation | Source | Draft answer |
|---|---|---|
| Placement justification | CLAUDE.md §10, `.claude/constraints/bff-extensions.md` | Serves live requests with the caller's identity and reuses `SignalReadService`, so it goes in the BFF (ADR-052 B4). No AI-internal types. |
| §11 Existing | CLAUDE.md §11 | Overlaps the April OpenAPI action (`src/solutions/CopilotAgent`), Dataverse `/api/mcp`, and `IAiToolHandler` (playbook tools) |
| §11 Extension | | The OpenAPI action can't carry an MCP App widget (widgets are an MCP extension), and Dataverse MCP can't apply the Signal core-record check. So: a new MCP endpoint that reuses the existing route's service. |
| §11 Cost of doing nothing | | Decision Records can't be shown in Copilot. Attorneys without the full app have no way to see decisions on their matters. |
| Publish size, CVEs | §10 bullets 4–5 | About +1.6 MB of DLLs plus the widget HTML plus a possible `Microsoft.Extensions.AI` bump. Measure against a fresh master build. |
| Tests | §10 bullet 6, ADR-038 | Tool unit tests (as-caller pass-through, 403/503 mapping, `readOnlyHint` present); an MCP protocol test against `/mcp`; widget adapter tests |
| `<hot-path-declaration>` | §10 | Required (BFF). `SpaarkeAi` is untouched unless shared components change. |
| Auth review | CLAUDE.md §6, §8.5 (two verifier passes for auth) | New audience or registration, token store pre-authorization |

## 8. Unknowns and the spikes that settle them (Phase A, in order)

| # | Unknown | Spike |
|---|---|---|
| 1 | The DA's `Accept` header against the SDK's 406 | A hello-world `/mcp` on the dev BFF, called from a DA |
| 2 | SSO audience acceptance (April: "SSO didn't work") | Create the SSO config; check the token's `aud`, `scp` and `azp` reach the BFF |
| 3 | An in-frame Fluent `Dialog`, inline vs fullscreen; whether fullscreen equals "side-by-side" | The widget with a dialog in both modes |
| 4 | Widget HTML size and init time in Copilot chat | Bundles at 1, 3 and 6 MB |
| 5 | Copilot chat's widget-call rate limit | Repeated app-only tool calls |
| 6 | Whether the experimental C# helper's lack of the legacy flat `ui/resourceUri` key matters; whether CSP is read from `resources/list` or `resources/read` | Omit the key, then set both |
| 7 | Whether the model calls the tool with the right arguments (April issue 2) | A 20-prompt eval: wrong-argument rate |
| 8 | Publish-size delta and CVE scan | Per CLAUDE.md §10 |
| 9 | Rendering in Teams, Outlook and mobile (later scope) | Run the same agent in each host |

## 9. Lessons from April 2026 (`projects/ai-m365-copilot-integration/current-task.md`)

| April issue | v1 response |
|---|---|
| OAuth worked; SSO didn't | Re-test SSO (§4.4); OAuth plus PKCE is the fallback |
| BFF lacked OBO for downstream calls | Use routes that already read as the caller (`SignalReadService`) |
| Copilot invented parameters → 400s | One tool with a near-empty input schema; descriptions ≤ 1,024 characters; error results that say how to fix the call |
| A confirmation card on every read | `readOnlyHint` (`ReadOnly = true`) |
| Copilot used native Dataverse | No Dataverse knowledge and no Dataverse MCP action in the agent |
| `devPreview` manifest and BOM upload failures | Numbered schemas; UTF-8 without BOM (deploy script already fixed) |
| The events endpoint had no user filter | Not reused; v1 uses only as-caller routes |

## 10. Owner decisions to raise at design

1. Confirm v1 users: the BFF's own tenant only (no guests).
2. The modal fallback if an in-frame dialog fails: master-detail, or `openLink` to the Console.
3. Agent name, icon and conversation starters; who publishes it (Integrated apps, admin upload).
4. Whether Frontier is turned on in the dev tenant (scoped group) to watch Copilot changes.
5. The next slice after Decisions: matter snapshot, document find, intake, or Cowork accelerator tools.

## 11. Watch at Ignite (17–20 November 2026)
- MCP Apps in Teams, Outlook and mobile.
- Widget rendering for federated connectors.
- `Action.OpenUrlDialog` GA and MCP support.
- BYO-MCP certification (RM576752).
- The C# MCP Apps helper leaving experimental.

## 12. Sources
- NuGet `ModelContextProtocol.AspNetCore` 2.3.1; `github.com/modelcontextprotocol/csharp-sdk` (docs, `samples/WeatherAppServer`)
- modelcontextprotocol.io specification 2026-07-28; `github.com/modelcontextprotocol/ext-apps` specification 2026-01-26
- Learn: Copilot extensibility, MCP plugins and MCP Apps (2026-09-30); plugin authentication (Entra SSO, OAuth); DA schema v1.9 and plugin schema v2.4; Cowork `mcp-apps-support` (2026-11-03); Dataverse MCP (2026-11-04); Copilot prerequisites and licensing
- `github.com/microsoft/mcp-interactiveUI-samples` (`expense-submission`)
- Microsoft 365 Agents Toolkit issue #15421
