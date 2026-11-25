# Spaarke MCP r2: Ask tool parity and retiring the Spaarke AI agent (`spaarkeai-mcp-bff-api-r2`)

> **Status:** Design draft v0.1, 2026-11-25. No spec or tasks yet. Next: the owner resolves §9, then `/design-to-spec`.
> **Priority:** high (owner, 2026-11-25).
> **Builds on:** `spaarkeai-mcp-bff-api-r1`, the MCP server in the BFF (`/mcp/legal-operations`, 5 decision tools, the Decide widget). r1 is in its live-E2E and wrap-up tasks.
> **Model doc:** `docs/architecture/SPAARKE-AI-SURFACES-AND-PACKAGING.md` (PR #3147). This project implements its Ask row for general Spaarke data.

---

## 1. Problem

Inside Microsoft Copilot, without an agent, Spaarke can do one thing today: show and complete decisions (5 MCP tools).

The legacy **"Spaarke AI"** declarative agent (`src/solutions/CopilotAgent/`) offers **28 functions** through an OpenAPI plugin: search, documents, playbooks, tasks, briefing, portfolio, matter summaries, email. But:
- it works only inside `@Spaarke AI`, not in plain Chat or Cowork, and not beside Work IQ in the user's normal flow;
- under the agent-creation rule, a general "ask Spaarke" agent is an anti-pattern: data, search and CRUD are tools on the connector, and agents exist only for practice areas;
- it can't render MCP Apps widgets, and every change to a function means redeploying the app package;
- its gateway code (`Api/Agent/`) carries known debt: #2575 (unregistered handler, unused services), #1574, #1523.

So Ask in Copilot needs these functions as **MCP tools**. Once they exist, the agent's tenant registration can be retired.

## 2. Goals and non-goals

**Goals**
1. **Parity:** every agent function that still earns its place becomes an MCP tool on the Spaarke server, run as the caller.
2. **Retire the legacy agent:** remove the tenant registration, then remove `src/solutions/CopilotAgent/` and the `/api/agent/*` gateway (closing #2575).
3. **Routing quality:** Copilot picks the right Spaarke tool from plain prompts, and leaves unrelated prompts alone. Measured with r1's routing-eval method, extended to the new tools.
4. **Long-running work:** a tool pattern for work longer than Copilot's ~30-second limit (playbook runs, analysis).
5. **Widgets only where they earn it** (for example a search result list or the briefing); text plus structured content otherwise.

**Non-goals**
- Practice-area agents, the attorney profile and its tools: `specialist-legal-agents-r1`.
- Invoice review and approval in Copilot: blocked by EB-D-194 / ontology D-207 (decided only in the Invoice Review app; r1 design §17).
- Request intake (email, forms): `spaarke-intake-pipeline-r1`. It uses BFF services, not MCP.
- A second MCP server or per-agent connectors (model doc, Constraints).

## 3. Inventory: the 28 legacy functions and proposed dispositions

Source: code inventory of master on 2026-11-25 (`notes/legacy-agent-inventory-2026-11-25.md`). Every function has a live BFF endpoint; none is covered by MCP yet. Paths are under `src/server/api/Sprk.Bff.Api/Api/`.

| Group | Functions (method + path) | Proposed tool(s) | R/W | Notes and blockers |
|---|---|---|---|---|
| Search | `semanticSearch` (POST `/api/ai/search`), `searchRecords` (POST `/api/ai/search/records`) | `spaarke_search_documents`, `spaarke_find_records` | R | Free-text query: confirm `RecordSearchService` never builds OData or FetchXML from raw text |
| Documents | `listDocuments`, `getDocument`, `getDocumentPreviewUrl` | `spaarke_list_documents`, `spaarke_get_document` (includes the preview and open links) | R | — |
| File summary | `summarizeFile` (multipart upload, SSE) | **Drop** | — | Copilot summarises the user's own files natively (decision order step 1: Microsoft owns it) |
| Playbooks | `listChatPlaybooks`, `listPublicPlaybooks` | `spaarke_list_playbooks` (one tool) | R | Two lists in the agent, one in the tool |
| Playbook runs | `executePlaybook`, `getPlaybookRunStatus`, `getPlaybookRunDetail` | `spaarke_run_playbook` (start, return a run id), `spaarke_get_playbook_run` | W / R | Streams today (`PlaybookRunEndpoints.cs:197`), so it can exceed 30 s: needs the long-running pattern (§4.2) |
| Analysis | `createAnalysis`, `getAnalysis` | `spaarke_start_analysis` (returns a deep link into the Analysis Workspace), `spaarke_get_analysis` | W / R | A hand-off to the Console, not an in-chat result |
| Tasks and events | `listEvents`, `getEvent`, `createEvent`, `completeEvent` | `spaarke_list_tasks`, `spaarke_get_task`, `spaarke_create_task`, `spaarke_complete_task` | R / W | `IEventDataverseService` runs as the BFF's identity with a BFF-applied caller filter (`EventEndpoints.cs:29`): verify it is caller-scoped before exposing it (§4.3) |
| Workspace | `getPortfolio`, `getBriefing` | `spaarke_get_portfolio`, `spaarke_get_briefing` | R | Widget candidates |
| Matter | `getMatterAiSummary` (SSE), `getMatterFinanceSummary` | `spaarke_get_matter_summary`, `spaarke_get_matter_finance` | R | AI summary needs a non-streaming variant. Finance authorization is held in route filters (`RequireInternalFinanceCaller` and others), so the capability layer must absorb it (r1 §17) |
| Email | `sendCommunication` (POST `/api/communications/send`) | **Owner decision (Q4)** | W | Sends real email, so it is irreversible. Options: app-only from a widget with explicit confirmation; draft-only; or defer |
| Agent gateway | `handleAgentMessage`, `listAgentPlaybooks`, `runAgentPlaybook`, `getAgentPlaybookStatus` | **Drop** | — | The agent's own chat loop and duplicate playbook routes. In Copilot, Copilot is the orchestrator |
| Identity | `getCurrentUser`, `getUserCapabilities` | **Drop** | — | Copilot knows the user; capabilities need a `containerId` and serve the UI |

**Net result:** about 17 new tools (13 read, 4 write) and 9 functions dropped, plus `sendCommunication` pending Q4.

## 4. Cross-cutting design

### 4.1 Same pattern as r1

Each tool is an adapter over the service the REST route calls (r1 B-1 capability layer), run as the caller, with results mapped through r1's mapper (B-2) and `isError` results. Each tool has:
- a `spaarke_{verb}_{object}` name and an `[McpArea]` tag;
- explicit annotations;
- a "use for / not for" description of at most 1,024 characters.

Write tools are app-only unless the owner decides otherwise (r1 O-14). Logic now held in endpoint handlers or route filters moves into the service, so that REST and MCP enforce the same rules.

### 4.2 Long-running work

Copilot calls time out at about 30 seconds. Options:
- **A — start and poll (recommended):** the tool starts the run and returns a run id plus a Console link; a status tool reports progress. This matches today's REST pair.
- **B — MCP-native long-running support**, if the SDK version and Copilot both support it. Needs a spike.
- **C — synchronous with a timeout and a "still running" result.** Fragile.

### 4.3 Caller scoping

Each tool must return only what the caller may see, under the same rules as the Console. Services that read as the BFF's own identity and filter afterwards (events) need a proof (a must-not-leak test) before they are exposed. Auth diffs go to unified-access-control-r2 for review, as in r1.

### 4.4 One route or several

The r2 tools could all go on `/mcp/legal-operations`, giving about 22 tools on one connector.
- **Risk:** Copilot's tool choice gets worse as the list grows, and the connector has one 4,000-character description.
- **Option:** split by area (for example decisions and tasks vs documents and search). But every route is another connector for each customer to create and Connect.
- **Recommendation:** keep one route; run the routing eval at 10, 15 and all tools; split only if the eval shows it is needed (Q2).

### 4.5 Retiring the legacy agent

**Decided (owner, 2026-11-25): remove it now, not after parity.** It isn't needed in the interim, and running it beside the new MCP tools risks the same request being answered twice. Order:

1. **Tenant, now (owner/admin actions).** These are steps E1 and E2 of copilot-platform-coordination's removal runbook (`projects/copilot-platform-coordination/notes/removal-runbook.md` on `work/copilot-platform-coordination`):
   - E1: uninstall and delete both "Spaarke AI" Registry rows;
   - E2: after E1, delete the Azure Bot `spaarke-bot-dev`, its OAuth connection and Entra app `f257a0a9…`. Any Key Vault secret for that app is a separate step under CLAUDE.md §9.
2. **Code, as Phase 0's first task.** Remove:
   - `src/solutions/CopilotAgent/`;
   - the `/api/agent/*` gateway (`Api/Agent/`) and `AgentModule` registrations;
   - `infrastructure/bot-service/`.

   This closes #2575, and #1523 (`AgentTokenService`) is decided with it. The 28-function checklist is preserved in `notes/legacy-agent-inventory-2026-11-25.md`, so the code is no longer needed as a reference. If `specialist-legal-agents-r1` later builds a Spaarke runtime agent (its G9), it needs a new Agents SDK front door anyway: the April handler was never a live Bot endpoint.
3. Users get the MCP tools as each phase ships; there is no parallel period.

## 5. Component justification (CLAUDE.md §11), summary

| New surface | Existing | Extension? | Cost of doing nothing |
|---|---|---|---|
| ~17 MCP tools | The REST routes and services above; r1's MCP host | Yes: each tool is an adapter on the existing host and service, with no new service where one exists | With the legacy agent removed (§4.5), Copilot users can't reach Spaarke data beyond decisions |
| Long-running pattern (§4.2) | REST start + status pair | Reuse the pair | Playbook and analysis tools time out in Copilot |
| Caller-scope fixes (§4.3) | `IEventDataverseService` filters | Extend the service | A tool could return rows the caller may not see |

BFF hygiene (`.claude/constraints/bff-extensions.md`): the tools sit on the existing MCP host, so expect no new packages; measure the publish-size delta per task as usual. Hot-path declaration: `Api/Mcp/**`, `AuthorizationModule.cs` (only if needed), the services in §3.

## 6. Dependencies and coordination

| With | What |
|---|---|
| `spaarkeai-mcp-bff-api-r1` | Must reach wrap-up first (tasks 042 live E2E, 090). r2 starts from master after r1 merges |
| unified-access-control-r2 | Auth review of every tool's caller path; dev deploys run through its session (owner per-SHA go) |
| ebilling-r1 | `getMatterFinanceSummary` authorization; invoice decisions stay out (EB-D-194) |
| `specialist-legal-agents-r1` | Practice-area routes (`/mcp/patents`, …) and the profile tool are theirs. r2 keeps the host conventions they will use |
| copilot-platform-coordination | Tenant registration changes (retiring Spaarke AI), inventory rows |

## 7. Phasing (proposal)

- **Phase 0, legacy removal and spikes:**
  - remove the legacy agent code, gateway and bot infrastructure (§4.5 step 2; BFF change: tests, publish-size delta, auth review);
  - long-running pattern (§4.2);
  - routing eval at increasing tool counts (§4.4);
  - events caller-scope proof (§4.3);
  - `RecordSearchService` query check.
- **Phase 1, read tools:** search, documents, records, playbook list, tasks (read), portfolio, briefing.
- **Phase 2, long-running and AI:** playbook run and status, analysis start and status, matter summary, matter finance.
- **Phase 3, writes:** create and complete task; email per Q4.

## 8. ADR tensions

- **ADR-028 (auth):** tools run as the caller under A10. No new auth route is expected; any app-only read needs the caller's own record-access check (r1 FR-02 conditions).
- **ADR-013 (AI placement):** tools live in the BFF on the existing MCP host. No tension.
- None found that needs path A or B yet.

## 9. Open questions for the owner

1. **Scope of parity:** accept the dispositions in §3, especially dropping `summarizeFile`, the agent gateway functions and the identity functions?
2. **Route split:** one route measured by eval (recommended), or decide a split now?
3. **Long-running pattern:** A, start and poll (recommended), or spike B first?
4. **Email:** expose `sendCommunication` as an app-only widget write, draft-only, or defer?
5. **Widgets:** which tools get one in r2: briefing, search results, task list?
6. ~~**Agent removal order**~~ **Decided 2026-11-25: remove now** (§4.5).
7. **Timing:** start the design-to-spec now and execution after r1's wrap-up (recommended), or wait for r1 to merge first?
