# Spaarke MCP r2: Ask tool parity and retiring the Spaarke AI agent - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-11-25
> **Source**: design.md (v0.3, all §9 questions decided by the owner 2026-11-25)

## Executive Summary

Microsoft Copilot users can reach Spaarke today only through r1's five decision tools, or through the legacy "Spaarke AI" declarative agent, which works only inside `@Spaarke AI`. This project removes the legacy agent now. It then gives Copilot Chat and Cowork the agent's useful functions as about 17 MCP tools on the existing BFF MCP server (`/mcp/legal-operations`, one connector), and adds three widgets that reuse Console components: the Daily Briefing, search results and email compose. Every tool runs as the caller, over the same service the REST route uses.

## Scope

### In Scope

- **Legacy agent removal (code).** Delete:
  - `src/solutions/CopilotAgent/`;
  - the `/api/agent/*` gateway (`Api/Agent/`) and `AgentModule`;
  - `infrastructure/bot-service/`;
  - their CI workflow, scripts, tests, ledger rows and doc references.
  The tenant steps (runbook E1/E2) are the owner's.
- **MCP tools** for search, documents, playbooks, playbook runs, analysis, tasks (events), portfolio, the Daily Briefing, the matter AI summary, matter finance and email (§3 of the design, as decided).
- **Durable playbook run status:** write `sprk_playbookrun` / `sprk_playbooknoderun`.
- **The long-running pattern:** start, then check status, for any tool that can't answer within 30 s.
- **Caller-scope work:**
  - extract the events logic into a capability;
  - prove the event reads are caller-scoped;
  - check that record search builds no query from raw text.
- **Three widgets:** Daily Briefing, search results, email compose. Each is one Console component behind a data seam, with a shared MCP transport for widgets.
- **Routing eval** at 10, 15 and all tools.
- **Dataverse MCP:** the admin guidance paragraph in the customer deployment guide, and a `researcher` re-check of the platform facts.

### Out of Scope

- Practice-area agents, the attorney profile and its tools (`specialist-legal-agents-r1`). Its run-record lookups (profile version, parent run) are added by that project on top of FR-13.
- Invoice review and approval in Copilot (EB-D-194 / ontology D-207).
- Request intake by email and forms (`spaarke-intake-pipeline-r1`).
- A second MCP server, a second route or connector, or per-agent connectors (owner Q2).
- Summarising the user's own uploaded files (Copilot does it natively; `summarizeFile` is dropped).
- The legacy agent's chat loop, its duplicate playbook routes, and the identity functions (`getCurrentUser`, `getUserCapabilities`).
- Exposing Dataverse's built-in MCP server to end users (design §4.7).
- Outlook as a host (the connector isn't offered there; r1 Phase 0 T1). Word runs tools without widgets.
- Tenant and admin actions: Registry, Azure Bot, Entra app and Key Vault (owner; CLAUDE.md §9).

### Affected Areas

- `src/server/api/Sprk.Bff.Api/Api/Mcp/`: new tool classes under `LegalOperations/`; `McpToolCatalog`; `McpToolResults` (generalised); new widget resources in `McpModule`.
- `src/server/api/Sprk.Bff.Api/Api/Agent/`, `Infrastructure/DI/AgentModule.cs`, `Program.cs:232`, `Infrastructure/DI/EndpointMappingExtensions.cs:480`, `Configuration/AgentTokenOptions.cs`: delete. Before the delete, move the `AgentEndpoints` constants that `Api/Filters/PlaybookAuthorizationFilter.cs:576,581` uses.
- `src/server/api/Sprk.Bff.Api/Api/Events/EventEndpoints.cs`: extract the logic into an events capability; REST bytes unchanged.
- `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs`: persist run status to Dataverse.
- Services wrapped, not rewritten:
  - `ISemanticSearchService`, `IRecordSearchService`, `IDocumentDataverseService`;
  - `IPlaybookService`, `IPlaybookOrchestrationService`;
  - `RecordCreationService` / `IAnalysisOrchestrationService`;
  - `PortfolioService`, the Daily Briefing services (`Api/Ai/DailyBriefingEndpoints.cs`);
  - the Insights cache (`InsightsPlaybookExecutionCache`, `TopicRegistryTtlLookup`);
  - `IFinanceSummaryService`, `CommunicationService`.
- `src/client/widgets/`: three new single-file widgets, plus a shared MCP transport generalised from `spaarke-decisions/src/lane/decideLaneFetch.ts` and `src/mcp/`.
- `src/client/shared/Spaarke.DailyBriefing.Components/`: add a data seam (it calls `authenticatedFetch` and `Xrm` directly today).
- `src/client/shared/Spaarke.UI.Components/src/components/EmailComposer/` and `Spaarke.AI.Widgets/src/outputs/output-widgets/SearchResultsWidget.tsx`: reuse, adding props only if needed.
- `src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj` and `.github/workflows/publish-bff-artifact.yml`: embed and verify the new widget HTML.
- `.github/workflows/publish-copilot-agent-template.yml`, `scripts/copilot-agent/`, `tests/scripts/CopilotAgentPackage.Tests.ps1`: delete with the agent.
- `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests*.cs` (ledger), `tests/unit/Sprk.Bff.Api.Tests/Api/Agent/`, the contract tests under `tests/integration/contract/Api/Ai/`: update or delete.
- `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`, `docs/architecture/SPAARKE-MCP-SERVER-ARCHITECTURE.md`, and the M365 Copilot guides that describe the agent.

## Requirements

### Functional Requirements

**Legacy removal**

1. **FR-01: Remove the legacy agent code.**
   - Delete `src/solutions/CopilotAgent/`, `Api/Agent/`, `AgentModule` and its registrations, `AgentTokenOptions`, `infrastructure/bot-service/`, the `publish-copilot-agent-template.yml` workflow, `scripts/copilot-agent/` and their tests.
   - Move the constants that `PlaybookAuthorizationFilter` takes from `AgentEndpoints` first.
   - Confirm `HandoffUrlBuilder` is unused by MCP before deleting it.
   - Remove the route-guard ledger rows and contract tests for `/api/agent/*`.
   - Update or delete the docs and skills that describe the agent.
   - Closes #2575; records the decision on #1523.
   - **Acceptance:**
     - `git grep -i "api/agent\|AgentModule\|AgentTokenService\|CopilotAgent"` returns no code hits outside history notes;
     - the BFF builds with 0 warnings;
     - ArchTests and the unit and contract suites pass;
     - the publish-size delta is measured (expected negative);
     - `/api/agent/message` answers 404 on dev.

**Tools.** Every tool:
- is a thin adapter over the existing service, run as the caller (`CompleteDecisionTool` pattern);
- carries `[McpArea("legal-operations")]`, explicit annotations and a title;
- has a "use for / not for" description of ≤ 1,024 characters;
- returns errors as `isError` results with the REST `reasonCode` through the generalised `McpToolResults`.

2. **FR-02: Search.**
   - `spaarke_search_documents` (over `ISemanticSearchService`, with the route's `IRetrievalAccessTrim`).
   - `spaarke_find_records` (over `IRecordSearchService`).
   - Read-only.
   - **Acceptance:**
     - results equal the REST route's results for the same caller and query;
     - a document the caller can't open never appears (must-not-leak test);
     - a test proves no OData or FetchXML is built from raw query text.

3. **FR-03: Documents.**
   - `spaarke_list_documents` (`GET /api/v1/documents` logic).
   - `spaarke_get_document`: metadata plus preview and open links, from `FileAccessEndpoints`.
   - Read-only.
   - **Acceptance:**
     - parity with REST for an allowed document;
     - a document the caller can't read returns a uniform not-found.

4. **FR-04: Playbooks.**
   - `spaarke_list_playbooks`: one list of the caller's and public playbooks, de-duplicated, over `IPlaybookService`.
   - Read-only.
   - **Acceptance:** the result equals the union of the two REST lists, with no duplicates.

5. **FR-05: Playbook runs.**
   - `spaarke_run_playbook` (model-callable, reversible, idempotency key): starts a run and returns a run id, a status summary and a Console link within 5 s. It applies `PlaybookAuthorizationFilter`'s checks through a shared method, not a copy.
   - `spaarke_get_playbook_run` (read-only): returns status and, when complete, the result summary. It reads the durable record (FR-13). A run started by someone else returns a uniform not-found, which is the legacy `StartedByOid` rule.
   - **Acceptance:**
     - start then status round-trips on dev;
     - a run survives a BFF restart (status still readable);
     - another user's run id returns not-found.

6. **FR-06: Analysis.**
   - `spaarke_start_analysis` (model-callable, reversible): creates the analysis through `RecordCreationService` and returns a deep link into the Analysis Workspace. It does not run the analysis in chat.
   - `spaarke_get_analysis` (read-only).
   - **Acceptance:** the created record equals one created through REST; the link opens it.

7. **FR-07: Tasks (events).**
   - Extract the get, list, create and complete logic from `EventEndpoints` statics into an events capability taking the request context (r1 B-1 style). The REST bytes stay unchanged; a frozen-bytes test proves it.
   - Then add `spaarke_list_tasks`, `spaarke_get_task`, `spaarke_create_task` and `spaarke_complete_task`. The two writes are model-callable and reversible, with an idempotency key.
   - **Acceptance:**
     - REST contract tests pass unchanged;
     - must-not-leak tests prove a caller never sees another user's private event (FR-15);
     - a retried create with the same key creates one record.

8. **FR-08: Portfolio.**
   - `spaarke_get_portfolio` over `PortfolioService.GetPortfolioSummaryAsync`. Read-only; text and structured content, no widget.
   - **Acceptance:** parity with `GET /api/workspace/portfolio`.

9. **FR-09: Daily Briefing.**
   - `spaarke_get_briefing` returns the caller's AI Daily Briefing summary, through the `/api/ai/daily-briefing` services (owner, Step 5). It opens the Daily Briefing widget (FR-16).
   - If generation can exceed 30 s, it follows FR-14.
   - **Acceptance:** the briefing equals the one the Console shows the same user that day **with the Console's default preferences** (5 days, 168 hours; owner, 2026-11-26: a user's saved Daily Digest preferences are out of scope, #3232); the widget renders it in Chat and Cowork.

10. **FR-10: Matter AI summary.**
    - `spaarke_get_matter_summary` reads the matter-summary topic from the Insights cache (`InsightsPlaybookExecutionCache` with the `sprk_aitopicregistry` TTL) and returns it with its date. If it is missing or expired, the tool starts an Insights run and returns FR-14's start result; the status tool then returns the summary.
    - No new summary store.
    - **Acceptance:**
      - a cached summary returns in ≤ 3 s and matches the matter form's insight card;
      - an expired one starts a run, and the status tool returns the fresh summary.

11. **FR-11: Matter finance.**
    - `spaarke_get_matter_finance` over `IFinanceSummaryService`. The authorization now held in route filters (`RequireInternalFinanceCaller` and others) moves into the service or a shared policy, so REST and MCP enforce the same rule.
    - ebilling-r1 reviews it.
    - **Acceptance:** a caller the REST route refuses is refused by the tool with the same `reasonCode`.

12. **FR-12: Email.**
    - `spaarke_compose_email` (model-visible) opens the compose widget. It may pre-fill a draft: recipients, subject, body, regarding matter, suggested documents.
    - `spaarke_send_email` is **app-only**. The widget calls it when the user presses Send. It goes through the `CommunicationService` core, as the caller, with its sender-enabled and recipient rules.
    - Scope:
      - To and Cc;
      - subject;
      - rich body;
      - regarding a matter (required);
      - optional attachments chosen from that matter's Spaarke documents;
      - archived to the matter.
    - **Acceptance:**
      - the model can't send: no model-visible send tool, and the send tool is app-only;
      - a sent email is archived to the matter with its attachments;
      - a disabled sender or a refused recipient returns the service's `reasonCode` and sends nothing;
      - the retry idempotency key prevents a double send.

**Cross-cutting**

13. **FR-13: Durable run status.**
    - `PlaybookOrchestrationService` writes `sprk_playbookrun` on start, state changes and completion, and `sprk_playbooknoderun` per node.
    - `GetRunStatusAsync` and `GetRunDetailAsync` read Dataverse when the run isn't in memory.
    - The REST run routes keep their contract.
    - No background worker is added: writes happen on the run's own execution path (ADR-052).
    - **Acceptance:**
      - after a BFF restart, REST and MCP status both return the stored state;
      - a write failure never fails the run (logged, with the outcome code);
      - existing run tests pass.

14. **FR-14: Long-running pattern.**
    - **Rule:** every tool answers within 30 s (owner Q3).
    - A tool whose work can exceed that returns `{runId, status, consoleLink}` at once.
    - A status tool returns progress, or the result when done.
    - The pattern is shared code, used by FR-05, FR-09 (if needed) and FR-10.
    - **Acceptance:**
      - no tool's p95 exceeds 30 s in the latency run (NFR-01);
      - the start-then-status round trip is tested for each user.

15. **FR-15: Caller-scope proofs.** Before a tool over these services is enabled:
    - `IEventDataverseService` (BFF identity plus BFF-applied filter) gets must-not-leak tests, or is changed to read as the caller;
    - record search gets the no-raw-query proof (FR-02).

    unified-access-control-r2 reviews each auth diff.
    - **Acceptance:** the proofs are in the KEEP suites; the review is recorded in the PR.

16. **FR-16: Widgets.**
    - One shared widget MCP transport, generalised from `decideLaneFetch.ts` / `mcpFetch.ts`. The Decisions widget moves onto it with no behaviour change.
    - Three single-file widgets, each one Console component behind a data seam:
      - **Daily Briefing:** `DailyBriefingApp` gains an injected data service. The Console and the standalone page keep working unchanged, and `Xrm` navigation becomes an injected handler.
      - **Search results:** `SearchResultsWidget` (props only).
      - **Email compose:** `EmailComposer`, with an MCP host in place of `createXrmEmailComposeHandlers` / `useXrmEmailHostProps`.
    - Each widget is served as a `ui://spaarke/*.html` resource through `McpModule`, with an empty CSP, and embedded and verified by the publish workflow.
    - **Acceptance:**
      - each renders in Chat and Cowork in light and dark themes;
      - the Console's own uses of the three components pass their existing tests unchanged;
      - each bundle is within NFR-03.

17. **FR-17: Routing eval.** Using r1's method (a prompt set per tool plus unrelated prompts), run at 10, 15 and all tools.
    - **Acceptance:**
      - each tool's positive prompts pick it at ≥ 80%;
      - unrelated prompts invoke a Spaarke tool at ≤ 10%;
      - rates are recorded per tool and per count. A miss is fixed in the tool descriptions, not by splitting the route (owner Q2).

18. **FR-18: Dataverse MCP position.**
    - Add the admin paragraph to `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`: don't allow the Dataverse MCP client for end users in environments running Spaarke, or allow it read-only for named analysts. Give the reasons from design §4.7.
    - A Phase 0 `researcher` check re-verifies the platform facts (endpoint, tools, Business Skills status, metering) against Microsoft Learn.
    - **Acceptance:** the paragraph is merged; the research note is in `notes/`, with dated sources.

### Non-Functional Requirements

- **NFR-01: Latency.** Over 50 calls each on the dev BFF:
  - read tools: p95 ≤ 3 s;
  - search tools: p95 ≤ 5 s;
  - start tools: p95 ≤ 5 s;
  - `spaarke_send_email`: p95 ≤ 15 s;
  - every tool under 30 s.
- **NFR-02: Result size.** Every tool result is < 64 KiB (the Cowork inline cap), tested at realistic maxima (50 search hits, 200 tasks).
- **NFR-03: Widget budget.** Each single-file widget aims for < 1 MB, with a hard ceiling of 3 MB enforced by `scripts/check-bundle-size.mjs`.
- **NFR-04: Publish size and CVEs** (CLAUDE.md §10). Measure the delta against a fresh master build on every BFF task. ≥ +5 MB per task needs justification; 55 MB total triggers review; 60 MB is a hard stop. No new HIGH CVE.
- **NFR-05: Logging** (ADR-015, A10). Log tool name, latency, row count, outcome or reason code and correlation id only. Never log tokens, claims, query text, email bodies or document content.
- **NFR-06: Writes are server-enforced.**
  - Reversible writes (task create/complete, run start, analysis start) are model-callable and validated on the server, with idempotency keys.
  - The irreversible write (email send) is app-only.
  - No behaviour depends on a host consent prompt (A10).
- **NFR-07: Model-visible output is minimal** (ADR-015). Return summaries and slim rows to the model; detail reaches widgets through app-only tools. Privileged or secured fields are never in model-visible text.
- **NFR-08: Protocol conformance.** The `modelcontextprotocol/conformance` suite still passes against `/mcp/legal-operations` with every new tool registered.
- **NFR-09: Accessibility and theming.** Fluent v9 semantic tokens, host light and dark themes, keyboard operation (ADR-021).
- **NFR-10: Kill switch.** `Mcp:Enabled=false` still turns off every tool and widget (ADR-032).

## Technical Constraints

### Applicable ADRs

- **ADR-001:** Minimal API BFF, one pipeline; tools sit on the existing MCP host.
- **ADR-002:** no plugins. Each record invariant keeps one server-side owner: the events capability for task writes, `CommunicationService` for send, `RecordCreationService` for analysis. The client previews only; security fails closed.
- **ADR-004 / ADR-052:** no new background job, queue or hosted service. Playbook runs keep their in-process execution; FR-13 only persists status on that path. A run started from Copilot continues in-process after the start reply, acting as the user, under the ADR-052 amendment (D-030-run-model, owner 2026-11-26).
- **ADR-008:** authorization through policies; tool classes follow `McpToolAuthorization` (an `[Authorize]` allow-list).
- **ADR-010:** feature-module DI (`AddMcpModule`); register new capabilities in the existing modules.
- **ADR-012:** reuse `@spaarke/ui-components` and the existing shared packages; no per-project libraries.
- **ADR-013:** AI surfaces extend the BFF; tools inject no AI-internal types (use `PublicContracts` facades).
- **ADR-015:** minimum text to the model; identifiers, sizes, timings and outcomes only in logs.
- **ADR-019:** ProblemDetails for REST; MCP errors through the generalised `McpToolResults` with the same `reasonCode`.
- **ADR-021 / ADR-050:** Fluent v9, dark mode, semantic tokens; in-frame dialogs use the shared modal shell.
- **ADR-028 (A10):** OAuth at the Spaarke-tenant authority; tools run as the caller (OBO); `systemuser` callers only.
- **ADR-032:** `Mcp:Enabled` null-object kill switch.
- **ADR-038:** KEEP-path tests; mock at module boundaries; no `Mock<HttpMessageHandler>`.

### MUST Rules (from ADRs)

- ✅ MUST run every tool as the caller and return only what the Console would show that caller (ADR-028 A10; FR-15).
- ✅ MUST keep REST response bytes unchanged through every extraction (events, finance authorization, run status), proved by frozen-bytes or contract tests.
- ✅ MUST annotate every tool with an explicit `readOnlyHint`, `destructiveHint` and `idempotentHint`, and a title. Descriptions ≤ 1,024 characters.
- ✅ MUST make `spaarke_send_email` app-only and give it an idempotency key (NFR-06).
- ✅ MUST register tools only through `McpToolCatalog` and widget resources only through `McpModule`'s sanctioned collection; update the route-guard ledger.
- ✅ MUST keep each widget's CSP empty; all data goes through app-only tools.
- ✅ MUST qualify "playbook" by format where it matters in user-facing text (D-209).
- ❌ MUST NOT call Dataverse's built-in MCP server, or any route that bypasses the BFF's rules (design §4.7).
- ❌ MUST NOT add a second MCP route, server or connector (owner Q2).
- ❌ MUST NOT pass the caller's token downstream, or log it or its claims.
- ❌ MUST NOT add a hand-rolled `BackgroundService` (ADR-052).
- ❌ MUST NOT expose invoice review or approval (EB-D-194).
- ❌ MUST NOT copy authorization checks into tools. Move them to one shared method that REST and MCP both call (`PlaybookAuthorizationFilter`, the finance filters).

### Existing Patterns to Follow

- `Api/Mcp/LegalOperations/Decisions/CompleteDecisionTool.cs`: the thin tool adapter over a capability.
- `Api/Mcp/LegalOperations/Decisions/DecisionToolCall.cs`, `PlanAndLookupToolSupport.cs`, `Api/Mcp/McpToolAuthorization.cs`: argument parsing, app-only metadata, caller checks, logging.
- `Services/Signals/IDecisionCapability.cs` / `DecisionCapability.cs`: the capability layer taking the request context; the model for the events extraction.
- `Api/Signals/DecisionResultMapper.cs`: one result mapped to REST and MCP.
- `Api/Agent/AgentEndpoints.cs:461-481` (read before deleting): the start-and-poll shape and its `StartedByOid` owner check.
- `src/client/widgets/spaarke-decisions/`: the widget build, the MCP host bridge, and the injected-service pattern (`App.tsx:38`).
- `Spaarke.AI.Widgets/src/widgets/workspace/WorklistWorkspaceWidget.tsx:71`: the same service behind the Console's transport.
- `docs/architecture/SPAARKE-MCP-SERVER-ARCHITECTURE.md` and `docs/architecture/SPAARKE-AI-SURFACES-AND-PACKAGING.md`.
- Skills: `mcp-tool-handler`, `widget-design`.

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration

design.md now carries this block too (§5.1, added 2026-11-26 on the owner's instruction).

```xml
<hot-path-declaration>
  <bff>Y — Api/Mcp/** (new tools, McpToolResults generalised, widget resources); Api/Agent/** + AgentModule + Program.cs + EndpointMappingExtensions (delete); Api/Events (capability extraction); Api/Filters/PlaybookAuthorizationFilter (shared check); PlaybookOrchestrationService (run persistence); finance authorization move; csproj (widget embeds)</bff>
  <spaarkeai>N — src/solutions/SpaarkeAi/** untouched; shared libraries (Spaarke.DailyBriefing.Components, Spaarke.UI.Components EmailComposer, Spaarke.AI.Widgets) gain seams consumed unchanged by the Console</spaarkeai>
  <ci-workflows>Y — publish-bff-artifact.yml builds and verifies three more widgets; publish-copilot-agent-template.yml deleted</ci-workflows>
  <skill-directives>Y — declarative-agent and mcp-tool-handler skills drop legacy-agent references</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

**Placement justification** (`.claude/constraints/bff-extensions.md`; the ≤ 60 MB publish ceiling applies per task):
- **Tools and capabilities: in the BFF.** They adapt services that exist only in the BFF, on r1's MCP host; a separate host would duplicate caller resolution and the invariants (ADR-013 extraction criteria not met).
- **Widget HTML: embedded resources in the BFF** (1–3 MB each), served through `resources/read` like r1's.
- **Background work: none.** Run persistence happens on the run's own path.
- **Publish size:** the legacy removal is expected to cut size, and three widgets add up to 3–9 MB. Each task measures the delta.

### New Components (§11 three-question gate)

Overlap evidence: `notes/reuse-scan.md`.

| New component | Existing overlap (`notes/reuse-scan.md`) | Can extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| ~17 `spaarke_*` tools (FR-02..FR-12) | The REST routes and services in §1 of the scan; r1's MCP host | They **are** the extension: thin adapters on the existing host and services | With the legacy agent removed, Copilot users can't reach Spaarke search, documents, tasks, playbooks, briefing or matters |
| Events capability (FR-07) | Logic in `EventEndpoints` statics (`:437`, `:651`, `:710`, `:1318`) | Yes: it extracts those statics, as r1's B-1 did; no new rules | A tool would copy endpoint logic, giving task create/complete two owners |
| Run persistence (FR-13) | `sprk_playbookrun` / `sprk_playbooknoderun` tables (exist, 0 rows); in-memory `_activeRuns`; `IJobStatusService` (Office) | Yes: it extends `PlaybookOrchestrationService` and uses the existing tables | A status check after a restart or on another instance returns not-found, so a started run is lost to the user |
| Long-running helper (FR-14) | REST start + status pair; legacy agent `StatusUrl` shape | Yes: shared code over the existing status APIs | Each tool reinvents the start/status contract, and the 30 s rule isn't enforced in one place |
| Generalised `McpToolResults` | `McpToolResults.FromError(DecisionToolError)` (`McpToolResults.cs:20`) | Yes: it generalises the existing helper | Non-decision tools can't return REST-equal `reasonCode` errors |
| Shared widget MCP transport (FR-16) | `decideLaneFetch.ts`, `mcpFetch.ts` (decision-specific) | Yes: generalised from them; the Decisions widget moves onto it | Each widget forks its own transport |
| Daily Briefing data seam | `DailyBriefingApp.tsx:75`, `briefingService.ts:31` (direct `authenticatedFetch` / `Xrm`) | Yes: injected service in the existing component | The briefing can't render in Copilot (sandbox has no network, MSAL or `Xrm`) |
| Three widget entries (`src/client/widgets/*`) | `spaarke-decisions` (one widget) | Same toolchain, new entry points; no component forks | No visual briefing, search list or email compose in Copilot |
| Email MCP host for `EmailComposer` | `createXrmEmailComposeHandlers.ts`, `useXrmEmailHostProps.ts` | Yes: a sibling host implementation of the existing injected interface | The Copilot compose widget can't send without `Xrm` |

## ADR Tensions (per CLAUDE.md §6.5)

> No ADR tensions surfaced at design time. All listed ADRs (above) apply
> without exception. This section may be updated if tensions emerge during
> implementation.

Two items were checked and found compliant (Path C):
- **Model-callable reversible writes** (owner, 2026-11-25) don't conflict with ADR-028 A10. A10 forbids relying on a host consent prompt, and these writes are validated on the server with idempotency keys.
- **Persisting run status** isn't background work under ADR-052. It is written on the run's existing execution path.

One tension resolved by the owner (Path B, 2026-11-26, notes/decisions.md D-030-run-model):
- **A playbook run continues after `spaarke_run_playbook` replies, acting as the user** (FR-05, FR-14). ADR-052 §5 keeps the caller's token inside its request; the amendment adds one narrow exception (shared helper, memory only, capped run length). The amendment is ACCEPTED (owner security sign-off 2026-11-26) and lands with task 030.

One project exception (Path A, owner, 2026-11-26, notes/decisions.md D-owner-answers-2026-11-26 #3; detail notes/008-decisions.md):
- **ADR-028 A10 vs FR-11, finance tool (task 008/033).** A10 says an `/mcp` caller with no `systemuser` row "gets the existing `UserContextRequired` refusal". The finance method returns REST's `sdap.access.deny.internal_callers_only` for that caller, because FR-11 requires the tool to return the same `reasonCode` as REST. FR-11 parity wins for this tool; the code is unchanged. Both answers refuse and expose nothing. Task 033 keeps the method's code. Cite this in the PR; code-review approves it explicitly.

## Success Criteria

1. [ ] Legacy agent code, gateway, bot infrastructure and their CI, tests and docs are gone; the BFF builds clean; `/api/agent/*` returns 404 on dev - Verify by: FR-01 grep, build, test suites, dev probe.
2. [ ] Each FR-02..FR-12 tool returns REST-equal results for the same caller, and refuses what REST refuses - Verify by: per-tool parity and must-not-leak tests, and the dev E2E as the test user.
3. [ ] A playbook run's status survives a BFF restart - Verify by: FR-13 test, and a dev restart during a run.
4. [ ] The model can't send email; the user can, from the widget, and the email is archived to the matter with attachments - Verify by: tool metadata test, and a dev E2E in Chat and Cowork.
5. [ ] Daily Briefing, search results and email compose widgets render in Chat and Cowork, light and dark; the Console's own uses are unchanged - Verify by: widget tests, the Console regression suites, and screenshots in the E2E record.
6. [ ] Routing eval meets FR-17 at 10, 15 and all tools - Verify by: the eval report in `notes/`.
7. [ ] NFR-01 latency and NFR-02 size hold - Verify by: a 50-call run per tool on dev.
8. [ ] The conformance suite passes with every tool registered - Verify by: CI.
9. [ ] The Dataverse MCP admin paragraph is merged, and the platform facts are re-verified - Verify by: the doc diff and the research note.

## Dependencies

### Prerequisites

- **For Phase 0 (legacy removal, spikes, research):** none. It runs in parallel with r1 now (owner Q7).
- **For the tool phases:** r1's open PRs are merged (PR #3146, task 042), because they share `McpModule`, the catalog and the route-guard ledger.
- **Before the removal PR deploys to dev:** the owner has completed runbook E1 (uninstall and delete the "Spaarke AI" Registry rows).
- **Dev deploys:** through unified-access-control-r2's session, with the owner's go per SHA.

### External Dependencies

- Microsoft 365 Copilot Chat and Cowork hosts, and the MCP Apps (`@modelcontextprotocol/ext-apps` 2.0.4) host behaviour.
- `ModelContextProtocol.AspNetCore` as pinned by r1 (no new packages expected).
- Reviews:
  - unified-access-control-r2: every caller path;
  - ebilling-r1: matter finance;
  - ontology-r1: shared-capability extractions, as in r1.

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Parity scope | Accept design §3 dispositions? | Yes (2026-11-25) | 17 tools; `summarizeFile`, gateway and identity functions dropped |
| Route | One route or split? | One connector (2026-11-25) | All tools on `/mcp/legal-operations`; routing misses are fixed in descriptions |
| Long-running | Limit and pattern | 30 s per tool; start + status beyond it (2026-11-25) | FR-14 |
| Legacy removal | When? | Now, with no parallel period (2026-11-25) | FR-01 is Phase 0's first task |
| Email | Include? | Yes, unless large (2026-11-25) | FR-12 |
| Widgets | Reuse Console components? | Yes, one per widget: briefing, search, email compose (2026-11-25) | FR-16 |
| Timing | Run with r1? | Spec now; Phase 0 in parallel; tool phases after r1's PRs (2026-11-25) | Dependencies |
| Dataverse MCP | Position | Dev and admin only; not for end-user surfaces; admin guidance in the deployment guide (2026-11-25) | FR-18, MUST NOT rule |
| Write tools | Model-callable or widget-only? | Split by risk: reversible writes model-callable with server validation and idempotency; send email widget-only (2026-11-25) | FR-05/06/07/12, NFR-06; supersedes design §4.1's app-only default for reversible creates |
| Run status | Durable? | Write `sprk_playbookrun` / `sprk_playbooknoderun` (2026-11-25) | FR-13; specialist-legal-agents D-A18 (7) reuses it |
| Email scope | v1 scope | To/Cc, subject, rich body, regarding a matter (required), the matter's documents as attachments, archived to the matter; the model may pre-fill, only the user sends (2026-11-25) | FR-12 |
| Matter summary | Over 30 s | Cached, else start (2026-11-25) | FR-10 |
| Reuse: briefing | Workspace briefing or AI Daily Briefing? | AI Daily Briefing, with a data seam in its component (2026-11-25) | FR-09, FR-16 |
| Reuse: matter summary cache | Insights cache or new store? | Insights cache; one summary everywhere (2026-11-25) | FR-10 |

## Assumptions

- **Routing bar:** ≥ 80% correct tool on each tool's positive prompts; ≤ 10% Spaarke invocation on unrelated prompts. r1 recorded rates without a bar - affects FR-17.
- **Latency bars:** as NFR-01 (reads 3 s, search 5 s, starts 5 s, send 15 s) - affects tool design and FR-14 use.
- **Daily Briefing timing:** the summary is ready (or cached) within 30 s for a typical user. If not, FR-09 uses FR-14 - affects FR-09.
- **Email size:** if `/project-pipeline` sizes FR-12 plus its widget above about 5 tasks, the owner re-decides inclusion - affects Phase 3.
- **Events:** if the must-not-leak proof fails, r2 changes the read to run as the caller rather than dropping the task tools - affects FR-07 and FR-15.
- **Insights topic:** a matter-summary topic exists in `sprk_aitopicregistry` (the matter form uses one). If the cache holds no plain-text summary field, the tool reads the topic's target field - affects FR-10.
- **Tenant order:** the owner does runbook E1 before the removal deploys to dev. Until then the agent's gateway functions would fail, which is acceptable for a brief window - affects FR-01 deploy.

## Unresolved Questions

- [ ] Add the hot-path declaration block (above) to design.md, where CLAUDE.md §10 and project-pipeline Step 3 look for it - Blocks: nothing; project-pipeline warns until it is there.
- [ ] Which Daily Briefing call the tool returns (`/summarize` text vs the rendered briefing), decided when the seam is built - Blocks: FR-09 tool output shape.
- [ ] Whether `/api/workspace/briefing` (BriefingService) loses its last non-Console caller after FR-01, and can then be retired - Blocks: nothing in r2; a follow-up if it is unused.

---

*AI-optimized specification. Original design: design.md*
