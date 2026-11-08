# Spaarke in Microsoft Copilot and Cowork: MCP server in the BFF (r1) - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-11-05
> **Source**: design.md (owner-approved 2026-11-05, third review round; commit c39828a484, draft PR #2587)
> **Amended**: 2026-11-07 — FR-10 (owner option C): design §8 B-4 (`ICallerContext`) is superseded by this clarification and deferred; see FR-10 and Owner Clarifications.
> **Amended**: 2026-11-08 — FR-04 (owner ruling, option A widened): the privilege flag is derived from communications at read time; see FR-04 and Owner Clarifications.

## Executive Summary

Spaarke becomes a tool provider inside the user's normal Microsoft 365 Copilot Chat and Cowork flow, alongside Work IQ. Spaarke is not rebuilt as a separate Copilot app. One MCP server in the BFF (`/mcp/legal-operations`) exposes the user's **Decide lane** (the Work Items waiting on their decision) and the **Decision wizard** as tools and one MCP App widget. Each tool runs as the signed-in user over the same server-side services the Console uses. v1 proves the pattern end to end in the dev tenant, from listing Work Items to resolving one and writing its Decision Record. Each later capability (billing review first, then Patents and Trademarks) then becomes a small slice that adds tools.

**Terms** (owner, 2026-11-05):
- A **Signal** (`sprk_signal`) produces a **Work Item**, a line in the user's worklist.
- **Resolving** a Work Item writes a **Decision Record** (`sprk_decisionrecord`).
- The v1 grid lists Work Items, not Decision Records.

## Scope

### In Scope

**Server**
- An MCP server endpoint in the BFF at **`/mcp/legal-operations`**:
  - stateless;
  - authenticated per **ADR-028 A10**;
  - rate-limited and size-limited;
  - behind the `Mcp:Enabled` kill switch.
- Four MCP tools plus an app-only paging capability:
  - `spaarke_show_decisions`;
  - `spaarke_get_decision_plan`;
  - `spaarke_search_decision_lookup`;
  - `spaarke_complete_decision`.
- The BFF rework that makes REST and MCP two thin adapters over one decision capability (design §8):
  - B-1: capability layer;
  - B-2: result mapper;
  - B-3: workforce lookup search, with a REST route;
  - B-4: `ICallerContext` for the decision slice — **deferred** (owner, 2026-11-07, option C). MCP tools take the caller from the request `HttpContext` instead (FR-10);
  - B-5: slim list projection;
  - B-6: MCP placement;
  - B-7: tool authorization policies;
  - B-8: route-guard coverage;
  - B-10: `because`.

**Widget**
- One MCP App widget, `ui://spaarke/decisions.html`, in a new `src/client/widgets/spaarke-decisions/` single-file build. It contains:
  - the Decide lane: header with count, category cards, Work Item rows grouped by matter, and **Start review**;
  - the Decision wizard, including every parameter kind (text, choice, money, date, boolean, person, lookup).
- A text-only answer path for hosts that do not render the widget.

**Registration (dev tenant)**
- The Copilot registration, created and configured by this project:
  - the A10 OAuth connector client registration;
  - the federated connector **"Spaarke"**;
  - the plugin package with the **`spaarke-decisions`** skill;
  - a repeatable runbook for each.
- The **Spaarke AI** declarative agent fallback, rebuilt on one MCP action, **only if** Phase 0 test T1 shows no widget in Copilot Chat through the connector.

**Tests and docs**
- Phase 0 platform tests (T0–T14, E1–E5), with their results recorded before Phase A builds on them.
- Docs:
  - terminology and product names in `ontology-component-model.md` §3 and the CLAUDE.md §1.1 table;
  - an MCP architecture doc;
  - a "how to add a tool" guide;
  - a rewrite of the `mcp-tool-handler` skill.

### Out of Scope

- The Do and Know lanes, the Decided history (`DecisionRecordsWidget`), the full Console, `LegalWorkspaceApp`, and the matter workspace.
- Inquiry outcome recording (`/api/v1/inquiries/*`) and the worklist's narrative briefing.
- A model-callable "complete decision" tool (O-14). Only the widget writes.
- Organisational (suggested) prompts (O-18). They belong to the later customer onboarding kit.
- Publishing to the Connectors Gallery / Partner Center, customer stamps, Model 1 B2B guests in practice, Model 2 Entra SSO, Teams and mobile.
- Recording the channel (Copilot vs Console) on the Decision Record (owner, 2026-11-05).
- Switching the Console's browser-side lookup picker to the new server lookup (Assumption).
- Removing the April 2026 surface: `/api/agent/*`, the unused agent services and the `Microsoft.Agents.Hosting.AspNetCore` package (tracked as #2575 and #1523). The one exception is the OpenAPI action, which goes if the agent fallback is adopted.
- The "DEBUG" token logger (`MiddlewarePipelineExtensions.cs:121-158`). It is already filed as #1574 and owned elsewhere; `/mcp/*` sits outside `/api`, so the two don't interact.
- The billing review slice (the next project, design §17). v1 only keeps the capability pattern able to absorb it.

### Affected Areas

**BFF API (`src/server/api/Sprk.Bff.Api/`)**
- `Api/Mcp/` (new): the MCP module, tools, resource and endpoint mapping.
- `Infrastructure/DI/EndpointMappingExtensions.cs`: maps `/mcp/{area}`.
- `Infrastructure/DI/` (new `AddMcpModule`) and `Infrastructure/DI/AuthorizationModule.cs`: the latter only if A10 needs a scheme change.
- `Services/Signals/`, the decision slice. The capability layer calls these unchanged, passing the request `HttpContext` or `ClaimsPrincipal` (option C, FR-10):
  - `DecisionCommitService.cs` (L439, `HttpContext` parameter);
  - `DecisionPlanReadStamp.cs` (L42, `HttpContext` parameter);
  - `Actions/DecisionParameterSources.cs` (L109, `HttpContext` parameter);
  - `SignalReadService.cs` (L317, `ClaimsPrincipal` parameter).
- `Services/Signals/` (no new folder; owned by ontology-platform, which reviews every PR here): the capability layer and lookup search.
- `Api/Signals/`:
  - `DecisionPlanEndpoints.cs`, `DecisionCommitEndpoints.cs` and `SignalReadEndpoints.cs` become thin adapters with an unchanged REST contract;
  - a new lookup-search route.
- `Infrastructure/Dataverse/DataverseUserClient.cs`, `Infrastructure/Authentication/CallerResolution.cs`, `CallerWhoAmIMemo` and `CallerRecordAccessProbe`: **unchanged** (B-4 deferred, FR-10). They keep reading the request through `IHttpContextAccessor`; task 010 verifies that this path holds for MCP tool calls.
- `Sprk.Bff.Api.csproj`:
  - adds `ModelContextProtocol.AspNetCore` 2.3.1;
  - moves `Microsoft.Extensions.AI` 10.3.0 → 10.11.x;
  - embeds the widget HTML as a resource.

**Shared UI and widget**
- `src/client/shared/Spaarke.UI.Components/src/components/Worklist/` (`WorklistLanePanel.tsx`, `laneCountFilters.ts`): may gain a single-lane mount prop and a paging seam. The Console must be unaffected.
- `src/client/shared/Spaarke.UI.Components/src/components/DecisionWizard/` (`DecisionWizardHost.tsx`, `DecisionItemWizard`, `decisionDraft.ts`): may gain the `because` field and lookup seams.
- `src/client/widgets/spaarke-decisions/` (new): Vite + `vite-plugin-singlefile`, deep imports, MCP fetch shims.

**Registration and tests**
- `src/solutions/CopilotAgent/`: only if the T1 fallback is adopted.
- New packaging folder for the plugin package and connector runbook (the exact path is set in plan.md).
- `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.cs`: `MapMcp` coverage and tool annotations.
- `tests/integration/{auth,contract,regression}/**` and `tests/unit/domain/**` (ADR-038 KEEP paths).

**Docs**
- `.claude/skills/mcp-tool-handler/`, `docs/architecture/` (MCP architecture), `docs/guides/` (add a tool; connector runbook).
- `projects/spaarke-ontology-platform-r1/notes/ontology-component-model.md` §3 and root `CLAUDE.md` §1.1.

## Requirements

### Functional Requirements

1. **FR-01 — MCP endpoint `/mcp/legal-operations`.** Stateless MCP server (spec 2026-07-28, `ModelContextProtocol.AspNetCore` 2.3.1). It is mapped through `AddMcpModule` / `MapMcpEndpoints` (ADR-010) with:
   - explicit `.RequireAuthorization()` on the default workforce scheme;
   - a dedicated `mcp` rate-limit policy partitioned per caller;
   - a request-size limit;
   - response buffering disabled on streamed responses.

   Routes take the shape `/mcp/{area}`. Only `legal-operations` is mapped in v1 ("core" is avoided: it means *core record*), and each tool is tagged with its area. Functions within an area are tool groups and skills, not routes.
   - Acceptance:
     - `tools/list` on `/mcp/legal-operations` returns exactly the v1 tools;
     - an unmapped area returns 404;
     - with `Mcp:Enabled=false` (ADR-032 null object), `/mcp/legal-operations` returns 503 ProblemDetails and no tool runs;
     - a request without `Accept: application/json, text/event-stream` gets 406 as the transport defines;
     - a caller over the rate limit gets 429.

2. **FR-02 — Auth per ADR-028 A10.** `/mcp/*` accepts OAuth tokens issued by the Spaarke-tenant authority, with `aud` = the BFF app and scope `user_impersonation`.
   - Each tool resolves the caller to a `systemuser` and calls Dataverse and Graph OBO, as the Console does.
   - Acceptance:
     - an anonymous call returns 401 with a `WWW-Authenticate` challenge that points to the protected-resource metadata;
     - a valid token for a licensed user lists that user's Work Items only;
     - **negative:** a CIAM token is refused (401), an A9 home-directory token is refused (401), and a token that resolves to no `systemuser` gets the existing `UserContextRequired` refusal;
     - no code path passes the caller's token downstream;
     - (unified-access-control-r2 review conditions) `/mcp/*` falls under the authorization FallbackPolicy, with no anonymous route other than `/healthz` and `/ping`; the route guard has a must-fire test on an unauthenticated `/mcp/{area}`; the CIAM and A9 refusals happen server-side **before any tool runs**, and are tested; a failed OBO never falls back to app-only, and any app-only read applies the caller's own record-access check.

3. **FR-03 — `spaarke_show_decisions` (visible to model and app, readOnly).**
   - Input: an optional `category`, a closed enum that mirrors the lane's category cards (`laneCountFilters.ts` after #2579: All / Invoice decisions (`invoiceDecisions`) / Approve or rebudget / Other); no other properties.
   - Output:
     - `content`: a short text summary of counts per category and the top items, each with a Console deep link (subject to FR-04);
     - `structuredContent`: page 1 of the slim rows (FR-05, FR-11);
     - `_meta.ui`: points at `ui://spaarke/decisions.html`.
   - It calls the decision capability's Decide-lane list as the caller (B-1). The read rules in `SignalReadService` are unchanged: the core-record check, and a 503 `too_many_signals` rather than a partial list.
   - Acceptance:
     - for a seeded user, the counts equal the Console's Decide lane for the same user;
     - an invalid `category` value returns an `isError` result that says how to fix the call;
     - the description is ≤ 1,024 characters and phrased "use for / not for".

4. **FR-04 — Privileged Work Items stay out of model-visible text** (owner, 2026-11-05; ADR-015 "minimum text").
   - Every result the model may read (`content` and `structuredContent` of model-visible tools) reduces privilege-flagged Work Items to a neutral line: "N privileged items — open to review". There is no matter name, reason, evidence or party name.
   - **Derivation rule** (owner ruling 2026-11-08, option A widened; task 018). A Decide item is privilege-flagged when either of these communications has `sprk_communication.sprk_privilegeclassification` = Privileged or Potentially Privileged:
     - its subject, when the subject is a `sprk_communication`;
     - any of its witness or evidence communications.
   - The flag is derived at read time, as the caller (`IDataverseUserClient`, no app-only fallback), with one batched read per page. A read fault flags the item; a communication the caller cannot read flags the item (fail closed). None or null is not flagged.
   - `sprk_signal.sprk_privilegeflagged` is not a source: no production code writes it (ontology DI-64). The helper lives in `Services/Signals/` so ontology DI-143 (#2630) can reuse it for the Decision Record snapshot.
   - Privilege never gates a read on its own (owner ruling 2026-07-16): the flag only reduces model-visible text.
   - Full detail for those items reaches the widget only through **app-only** tools (FR-05, FR-06).
   - Acceptance: a test seeds a privileged Work Item with a unique marker string in its reason and matter name. **Negative:** the marker appears in neither `content` nor `structuredContent` of `spaarke_show_decisions`, and the count is still correct. Positive: the app-only page and plan results carry the item's detail.

5. **FR-05 — Paging for long lanes** (owner, 2026-11-05).
   - The first `spaarke_show_decisions` result carries page 1. The widget fetches further pages through an **app-only, readOnly** call. Plan.md decides whether that is a fifth tool or a cursor parameter on an app-only variant; the model-visible tool keeps its closed schema.
   - **Start review** walks the whole lane ("2 of 70").
   - Acceptance:
     - with 200 seeded Work Items, every result is < 64 KiB;
     - paging returns every item exactly once, in the Console's order;
     - Start review reaches item 200;
     - a stale cursor after items are resolved returns the remaining items without duplicates or an error.

6. **FR-06 — `spaarke_get_decision_plan` (app-only, readOnly).**
   - Input: `signalId`.
   - It returns the decision plan, the suppression state and the full Work Item row, resolved as the caller.
   - It **writes the plan-read stamp** (`DecisionPlanReadStamp`, 4 h TTL), which sets the review start for a later commit.
   - Acceptance:
     - for a Signal the caller can read, the plan equals `GET /api/v1/signals/{id}/decision-plan` for the same caller;
     - **negative:** for a Signal outside the caller's core-record access, it returns an `isError` result with the same reason code REST returns, and no stamp is written.

7. **FR-07 — Workforce lookup search (B-3): `spaarke_search_decision_lookup` (app-only, readOnly) plus a REST route.**
   - Input: `signalId`, `parameterCode`, `query`.
   - The **decision plan** supplies each parameter's lookup entity and scoping rule, so the client cannot widen a search. For example, a budget must belong to the matter (D-18).
   - The query is built on the server, read as the caller, capped in results, and fails closed.
   - It follows the external SPA's pattern (`ExternalModuleDataEndpoints.cs`) on the workforce plane without reusing that route.
   - Acceptance: it searches contacts, matter-scoped budgets and organisations for each lookup parameter of the six action types (revise-budget, reassign, add-todo, assign-work, assign-task, inquiry outside firm). **Negative:**
     - a `parameterCode` not in the plan returns `isError`;
     - a budget from another matter never appears;
     - a record the caller cannot read never appears;
     - the external-plane policy is not attached to the route.

8. **FR-08 — `spaarke_complete_decision` (app-only, write, not readOnly).**
   - Input: `signalId` and a `DecisionReview` (the same contract as `POST /api/v1/signals/{id}/decision`).
   - It calls `DecisionCommitService` unchanged: plan re-derivation, `DecisionCallerRights`, as-caller execution, one Decision Record, Signal closure, idempotency on `{oid}:{reviewId}`.
   - The widget sends it only from the wizard's explicit Confirm action.
   - Acceptance:
     - one completion writes one Decision Record and closes the Signal;
     - the same `reviewId` sent twice returns the first outcome and writes nothing new;
     - a commit is validated against the Signal's current plan; the server's plan-read stamp only sets the review start (missing = now; ontology ruling on ISS-004 / #2628, 2026-11-07, D-207). A commit with no plan read is never looser: a system-closed Signal gets `signal_not_open`, and a mismatched action gets `actions_do_not_match_plan`. The MCP commit tool may still require the plan tool to have run first, as a usability guard;
     - a commit by a caller without Write on a subject is refused;
     - nothing in the server relies on a host confirmation prompt.

9. **FR-09 — One result mapper (B-2).** `SignalReadResult` and `DecisionCommitResult` map to ProblemDetails (REST) or to an MCP tool error result (`isError`, a message that says how to fix the call, a `reasonCode`) through one mapper, with one `reasonCode` vocabulary.
   - Acceptance:
     - the REST responses of the existing signal and decision routes are byte-for-byte unchanged against recorded contract tests;
     - every reason code that REST can return has an MCP mapping test;
     - transport errors are never used for domain failures.

10. **FR-10 — Capability layer (B-1) shared by REST and MCP; caller from the request context (B-4 deferred).** *(Amended 2026-11-07, owner option C. This clarification supersedes design §8 B-4; design.md is not edited.)*
    - The list, plan, lookup and commit operations live in a self-authorising decision capability that REST endpoints and MCP tools both call. B-1 only moves the endpoint bodies into capability methods that take the request `HttpContext`, or a `ClaimsPrincipal` where that is enough (the worklist read). It adds no rules. B-2 (the mapper) needs no caller context.
    - MCP tools get the `HttpContext` from `IHttpContextAccessor`. When it is null or the caller is unauthenticated, the tool returns an `isError` result with `UserContextRequired`. Otherwise it calls exactly the decision services the REST endpoints call:
      - `SignalReadService.ReadAsync(set, httpContext.User, ct)`;
      - the `DecisionPlanEndpoints` body: `SignalCoreRecordAccess`, `DecisionPlanService.GetAsync`, `DecisionPlanReadStamp.StampAsync(http)` and `DecisionParameterSources.OptionsAsync(http)`;
      - `DecisionCommitService.CommitAsync(…, http, ct)`.
    - **B-4 `ICallerContext` is deferred** (owner, 2026-11-07, option C). `DataverseUserClient`, `CallerWhoAmIMemo`, `CallerRecordAccessProbe` and every chat/AI path are unchanged. WhoAmI stays at one per commit (T170).
      - Rationale: the `ModelContextProtocol.AspNetCore` 2.3.1 stateless transport runs tool handlers inside the ASP.NET request. `StreamableHttpHandler` passes `context.RequestServices` with `ScopeRequests=false`, and the default `PerSessionExecutionContext=false` keeps `IHttpContextAccessor` working. A scoped caller holder would duplicate what the request already carries.
    - **v1 limitation:** decision capabilities run only inside an HTTP request. This holds while the server stays stateless with `PerSessionExecutionContext=false`. Future off-request scenarios (durable agents, queued continuations, Teams proactive) are blocked by the user-token problem, not by this refactor. `ComposeProfileDispatcher.cs:88/117-134` shows the existing detached-context workaround.
    - Acceptance:
      - MCP tools resolve the caller from the request context; **negative:** with no `HttpContext` or an unauthenticated caller, a tool fails closed with `UserContextRequired` and never runs app-only;
      - REST behaviour is unchanged: task 011's baselines pass byte-for-byte;
      - WhoAmI = 1 per MCP commit (an MCP `spaarke_complete_decision` variant of T170);
      - a test pins the stateless transport and `PerSessionExecutionContext=false`.

11. **FR-11 — Slim list projection (B-5).** `WorklistItemSummary`, a projection of the existing `WorklistItemRow` without `EvidenceRefs` or `Steps`, is used for lists. It keeps #2580's `WorklistInvoiceReview(InvoiceId, LineId)` member. The full row arrives with the plan.
    - Acceptance: a list row is ≤ 600 bytes serialized at the seeded maximum field lengths. The Console's REST list response is unchanged unless it opts in.

12. **FR-12 — Widget: the Decide lane.** `ui://spaarke/decisions.html` (`text/html;profile=mcp-app`, embedded in the BFF) renders:
    - the header ("Decide", count, "Items that need your decision");
    - the category cards with count, oldest age and "done today", where a click filters the rows;
    - Work Item rows grouped by matter, with reason and age;
    - **Start review**.

    It reuses `WorklistLanePanel` and `WorklistService` over an MCP fetch shim. It declares no network origins (`_meta.ui.csp` empty), holds no tokens, and talks only through `callServerTool`. The theme comes from host context (light and dark, ADR-021), with no theme in storage.
    - Acceptance:
      - in Cowork, `/spaarke-decisions` renders the lane with the same counts as the Console;
      - clicking a row opens that Work Item's wizard;
      - loading, empty and error states render;
      - the Console's own worklist is unchanged (its tests pass, with no visual change).
      - billing Work Items (invoice decisions) render only as a link out to the Invoice Review page (`sprk_invoicereviewpage`; the raw `data` value is `invoiceId={guid}` plus `&lineId={guid}` only when the item has a line, with bare lower-case guids, URL-encoded once in the link. These are the rules of #2579's `openInvoiceReview.ts`, which itself calls Xrm `navigateTo` and can't be used in the widget. `lineId` is ignored today and turns on line focus when EB-D-194's optional parameter lands, as confirmed by ontology and ebilling on 2026-11-06). A billing row offers **no decide action**, and the wizard never opens for it, built from the item's `invoiceReview` member. They are never completed in Copilot (EB-D-194). #2580's `invoiceReview` member is on this branch (merged 2026-11-06), so the link is always built.

13. **FR-13 — Widget: the Decision wizard.** `DecisionWizardHost` / `DecisionItemWizard` run through a `ResponseFetchFn` shim mapped onto FR-06, FR-07 and FR-08.
    - The flow is: What was found → one step per planned action → Next steps → Confirm.
    - Every parameter kind works, and person and lookup parameters use an inline type-ahead backed by FR-07.
    - It opens as an in-frame Fluent dialog and requests fullscreen. The fallbacks are master-detail in fullscreen, then `openLink` to the Console's wizard for that item.
    - Related records render as text plus an "Open in Spaarke" link.
    - Acceptance:
      - a licensed dev-tenant user resolves one Work Item of each lookup-bearing action type end to end;
      - after Confirm, the row leaves the lane and the count drops by one;
      - **negative:** a server refusal (rights, stale plan) shows the server's message in the wizard and leaves the Work Item open.

14. **FR-14 — Text path when no widget renders.** When a route does not render the widget, the same `spaarke_show_decisions` call still answers in text (FR-03, subject to FR-04), with Console deep links. Completion then happens in the Console, or in the Spaarke AI agent if FR-17 applies.
    - Acceptance: with the widget suppressed, the answer lists counts and top items with working deep links. No write is possible from the text path.

15. **FR-15 — `because` (B-10).** The wizard sends the user's optional rationale, and the server stores it as it already supports.
    - Acceptance: a rationale entered in the wizard appears on the written Decision Record.

16. **FR-16 — Registration in the dev tenant (O-17, O-19, O-20).**
    - The project creates:
      - the A10 connector client registration (a public client with PKCE if the registration accepts it; otherwise a confidential client whose secret lives only in Microsoft's registration, with human confirmation per CLAUDE.md §9);
      - the federated connector, display name **"Spaarke"**, pointing at `/mcp/legal-operations`, with a description of at most 4,000 characters;
      - the plugin package (app manifest 1.30, `agentConnectors`) with the **`spaarke-decisions`** skill.
    - The owner runs the admin-center step as Global Admin.
    - Each step has a repeatable runbook. Whether the connector can be created by script is recorded.
    - Acceptance:
      - a licensed **non-admin** test user connects once in Settings › Sources;
      - `/spaarke-decisions` (Cowork, and Chat once MC1493223 is live) and a direct prompt ("what decisions are waiting on me in Spaarke?") both call `spaarke_show_decisions`;
      - the runbooks reproduce the setup from scratch.

17. **FR-17 — Spaarke AI agent fallback (conditional on T1).** Only if T1 shows no widget in Copilot Chat through the connector:
    - rebuild `src/solutions/CopilotAgent/` on DA v1.9 / plugin v2.4 / app manifest v1.30 with **one MCP runtime action**;
    - **remove its OpenAPI action**.
    - Acceptance: `@Spaarke AI` in Chat renders the widget and resolves a Work Item. The OpenAPI action is gone, so the two cannot compete for prompts.

18. **FR-18 — Docs, terminology and skills.**
    - Add one CLAUDE.md §1.1 row for "Spaarke for Microsoft 365 Copilot" / "Spaarke", citing `ontology-component-model.md` §1.1. The ontology project wrote that text (commit `1e9d16bdba`). Follow the CLAUDE.md maintainer notes: a `.claude/CHANGELOG.md` entry with the byte delta, the 200-line / 30 KB budget, and `ai-procedure-maintenance` Checklist F.
    - Record the naming conventions (`notes/coordination/2026-11-06-copilot-naming-and-ownership.md` §1) in `mcp-tool-handler` (tool names) and `widget-design` (widget URI, folder, size).
    - Write an MCP architecture doc (`docs/architecture/`) and a "how to add a tool" guide plus a connector runbook (`docs/guides/`).
    - Rewrite `.claude/skills/mcp-tool-handler` to the real conventions.
    - Acceptance: the docs name real files, and `doc-drift-audit` reports no stale references in them.

19. **FR-19 — Phase 0 platform tests recorded before dependent build.** Run T0–T14 and E1–E5 (design §13) in the dev tenant and record each result and its decision in `notes/`. **T5** (OAuth through the connector, then OBO WhoAmI) gates everything, **T0** gates Phase A, and **T1** decides FR-17.
    - Acceptance:
      - every test has a dated result and a decision;
      - if T5 fails, work stops and a follow-up ADR-028 amendment is raised (A10 "no silent fallback").

### Non-Functional Requirements

- **NFR-01 — Latency.** Over 50 calls each on the dev BFF:
  - `spaarke_show_decisions`, page and plan calls: p95 ≤ 3 s;
  - `spaarke_search_decision_lookup`: p95 ≤ 1.5 s;
  - `spaarke_complete_decision`: p95 ≤ 15 s;
  - every tool: under the 30 s host ceiling.
- **NFR-02 — Result size.** Every tool result is < 64 KiB (Cowork inline cap), tested at 200 Work Items.
- **NFR-03 — Widget budget.** Single-file HTML: aim < 1 MB (`widget-design` skill), hard ceiling 3 MB (O-16). Init time is measured in Chat and Cowork (Phase 0 "Size"). A bundle-size test fails the build above budget. If the budget is missed, the fallback is the `decisionDraft.ts` re-render.
- **NFR-04 — Publish size and CVEs (CLAUDE.md §10).** On every BFF task, measure the delta against a fresh master build. A delta of ≥ +5 MB in one task needs justification; ≥ 55 MB total triggers architecture review, and 60 MB is a hard stop. No new HIGH CVE.
- **NFR-05 — Logging (ADR-015, A10).** No bearer token or claim values are logged on `/mcp/*` beyond the standard audit enrichment. No record text or tool arguments appear in logs. Telemetry per call records the tool name, latency, row count, outcome or reason code, and correlation id.
- **NFR-06 — Writes are server-enforced.** Writes go only through `DecisionCommitService`. They need an explicit Confirm, validation against the current plan (the plan-read stamp sets only the review start; missing = now, ISS-004) and an idempotency key. No behaviour depends on a host consent prompt.
- **NFR-07 — Accessibility and theming.** The widget follows Fluent v9 semantic tokens, with no hex or inline colours. It supports host light and dark themes and keyboard operation of the lane and wizard.
- **NFR-08 — AI pipeline regression.** The `Microsoft.Extensions.AI` bump to 10.11.x passes the AI unit and integration suites before merge.
- **NFR-09 — Protocol conformance.** The `modelcontextprotocol/conformance` suite (`--spec-version 2026-07-28`) passes against `/mcp/legal-operations` in CI with a test-token injector.

## Technical Constraints

### Applicable ADRs

- **ADR-001**: Minimal API BFF, one pipeline; no Functions or Durable packages in the BFF.
- **ADR-002**: no plugins. Each record invariant has one server-side owner (`DecisionCommitService`); the client previews only; security fails closed.
- **ADR-008**: authorization through endpoint filters and policies, not global middleware.
- **ADR-010**: feature-module DI (`AddMcpModule`).
- **ADR-012**: reuse `@spaarke/ui-components` (and `@spaarke/visuals` later); no ad-hoc per-project libraries.
- **ADR-013**: AI and agent surfaces extend the BFF unless the extraction criteria are met (they are not; §14). The tools inject no AI-internal types.
- **ADR-015**: send the model only the minimum text; log identifiers, sizes, timings and outcome codes only.
- **ADR-019**: ProblemDetails for REST errors, extended through the B-2 mapper.
- **ADR-021 / ADR-050**: Fluent v9, dark mode, semantic tokens. The wizard's in-frame dialog uses the shared modal shell where it renders inside the frame.
- **ADR-028 (A4, A8, A10)**: auth, as in FR-02.
- **ADR-032**: `Mcp:Enabled` null-object kill switch → 503.
- **ADR-038**: KEEP-path tests; no `Mock<HttpMessageHandler>` / `Mock<IServiceClient>`; mock at module boundaries.

### MUST Rules (from ADRs)

**Auth and data handling**
- ✅ MUST authenticate `/mcp/*` with OAuth at the Spaarke-tenant authority, scope `api://{bffAppId}/user_impersonation`, `aud` = the BFF app (A10).
- ✅ MUST run every tool as the caller (OBO), for `systemuser` callers only (A10).
- ✅ MUST keep `DecisionCommitService` as the single owner of the completion invariant (ADR-002).
- ✅ MUST fail closed on missing caller context, a plan mismatch, or missing rights (ADR-002 WP-6). A missing plan-read stamp means "review starts now", the stricter direction (ISS-004).
- ✅ MUST send the model only the minimum text, and never privileged Work Item detail (ADR-015; FR-04).

**Placement and tooling**
- ✅ MUST register through a feature module and map the endpoint in `EndpointMappingExtensions` (ADR-010).
- ✅ MUST return 503 ProblemDetails through a null object when `Mcp:Enabled` is false (ADR-032).
- ✅ MUST annotate every tool with an explicit `readOnlyHint` and a title, and give each a description of ≤ 1,024 characters.
- ✅ MUST extend the route-authorization ArchTest to `MapMcp` (enforcement ladder: guard).

**MUST NOT**
- ❌ MUST NOT use the connector's Entra SSO option on Model 1, or accept CIAM or A9 home-directory tokens on `/mcp/*` (A10).
- ❌ MUST NOT pass the caller's token downstream, or log it or its claims (A10, ADR-015).
- ❌ MUST NOT rely on a host confirmation prompt for a write (A10).
- ❌ MUST NOT call Dataverse's built-in MCP server or any route that bypasses the BFF's rules.
- ❌ MUST NOT attach the external-plane (`ExternalCollaboration`) policy to any workforce MCP tool or the B-3 route.
- ❌ MUST NOT change `SignalReadService`'s read rules or `DecisionCommitService`'s validation, idempotency or execution order.
- ❌ MUST NOT add a hand-rolled `BackgroundService` or any background work (ADR-052).
- ✅ MUST qualify "playbook" by its format wherever the format matters (JPS playbook, legal playbook, contract-review playbook) in user-facing Copilot text. The concept itself needs no qualifier (owner ruling D-209, 2026-11-06; ontology §3 commit `80b3098373`).
- ✅ MUST use "Finding" as the user word for a **billing** Work Item (an invoice or invoice-line review result) in billing link-outs. Generic rows stay "Work Item". "Finding" is never an entity or code name (owner ruling D-208, 2026-11-06; ontology §3 commit `644904d5c3`).
- ❌ MUST NOT add a second billing-refusal check: the MCP commit calls the extracted capability, so D-207's refusal applies automatically. One MCP test proves a billing Signal returns the mapped refusal and writes nothing.
- ✅ MUST keep the commit invariants byte-for-byte through the B-1 extraction, and state each one in the B-1 PR (ontology's list):
  - the commit order: (1a) readability as the caller → uniform 404; ledger replay; (1c) app-only Signal read; (1c-billing); `PrepareAsync`; (1c′) `CallerMayDecideAsync`; preflight; writes; locks released in `finally`;
  - replay is decided server-side only;
  - D-197: `CheckVersionOwnerAsync` stays where it is;
  - `SignalWriter`, `DecisionRecordWriter` and `DecisionCallerRights` are untouched.

### Existing Patterns to Follow

**Server**
- `src/server/api/Sprk.Bff.Api/Api/Signals/DecisionCommitEndpoints.cs` and `SignalReadEndpoints.cs`: the current endpoint and result-mapping behaviour, which must be preserved.
- `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ExternalModuleDataEndpoints.cs` (`POST /fetch`): a server-built, scoped, fail-closed lookup. Copy the pattern, not the plane.
- `src/server/api/Sprk.Bff.Api/Infrastructure/Authentication/CallerResolution.cs`: caller resolution (it already forbids `HttpContext` overloads).
- `src/server/api/Sprk.Bff.Api/Api/Workspace/WorkspaceMatterEndpoints.cs:196-200`: disabling response buffering for streamed responses.
- `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.cs`: the guard to extend.

**Widget**
- `src/solutions/CreateMatterWizard/vite.config.ts`: the `vite-plugin-singlefile` build.
- `src/solutions/LegalWorkspace/src/sections/worklist.registration.ts:94-105`: how the Console injects fetch and lookup into the worklist and wizard. The widget does the same with MCP shims.
- External samples (MIT):
  - Copilot Studio Kit `mcp-apps/copilot-agent-kit`;
  - `microsoft/power-platform-skills` `plugins/mcp-apps`;
  - `microsoft/DataFactory.MCP` (HTML as an embedded resource);
  - PnP `mcp-cs-customers-secure-server` (auth);
  - `@modelcontextprotocol/ext-apps` 2.0.4 (check API drift from the 1.x samples).

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration

Copied from design.md §16 (`root-CLAUDE-md` = **YES**: FR-18 edits the CLAUDE.md §1.1 product-name table).

```xml
<hot-path-declaration>
  <bff-api>YES — new Api/Mcp/ + AddMcpModule + MapMcpEndpoints; Services/Signals decision capability extraction (capability methods over the request HttpContext/ClaimsPrincipal — amended 2026-11-07, option C); new workforce lookup-search service + route; ICallerContext deferred (DataverseUserClient, CallerWhoAmIMemo unchanged — amended 2026-11-07, option C); AuthorizationModule only if A10 needs a scheme change; csproj package changes. Api/Agent untouched unless the FR-17 fallback is adopted.</bff-api>
  <spaarke-ai>NO — src/solutions/SpaarkeAi/** untouched; shared-lib decision and worklist components may gain props (single-lane, paging, lookup seams), consumed unchanged by the Console</spaarke-ai>
  <ci-workflows>YES — widget build step feeding the BFF embedded resource; MCP conformance run; bundle-size budget; possibly a plugin package build</ci-workflows>
  <skill-directives>YES — rewrite .claude/skills/mcp-tool-handler; update declarative-agent and widget-design skills to current schemas</skill-directives>
  <root-CLAUDE-md>YES — §1.1 product-name table gains the "Spaarke for Microsoft 365 Copilot" family and its "Spaarke" core member (FR-18)</root-CLAUDE-md>
</hot-path-declaration>
```

**Placement Justification** (`.claude/constraints/bff-extensions.md`; ≤ 60 MB publish ceiling applies per task):
- **MCP endpoint and tools: in the BFF.** Every tool runs as the caller over services that exist only in the BFF: `SignalReadService`, `DecisionCommitService` with its Redis idempotency ledger, and `DecisionCallerRights`. A separate deployable would need a second HTTP hop and token exchange, and would either duplicate or proxy the invariant. ADR-013's extraction criteria are not met (Path C).
- **Lookup search (B-3): in the BFF.** It needs the decision plan and the caller's OBO Dataverse client.
- **Widget HTML: embedded resource in the BFF** (O-16, 1–3 MB). It is served inside `resources/read`, not as a framed page.
- **Background work: none.** Every tool finishes in under 30 s; long playbooks stay job-based (ADR-004). ADR-052 does not apply.
- **Publish size:** `ModelContextProtocol.AspNetCore` adds about 1.6 MB, and the widget adds 1–3 MB. Nothing offsets this, because the April package stays (#2575).

### New Components (§11 three-question gate)

| New component | Existing overlap (grep) | Can extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| `/mcp/{area}` endpoint + `AddMcpModule` (`Api/Mcp/`) | April OpenAPI plugin + `/api/agent/*` (`Api/Agent/AgentEndpoints.cs:37-107`); Dataverse's built-in `/api/mcp` | No. OpenAPI actions can't carry MCP App widgets or join a federated connector, and Dataverse MCP can't apply the Signal core-record check (`SignalReadEndpoints.cs:15-19`) | Spaarke has no presence in Copilot or Cowork beside Work IQ, and no route to the legal ISV connector gallery |
| Four `spaarke_*` tools + app-only page call | REST routes for list, plan and commit (`Api/Signals/*`) | They **are** the extension: thin adapters over the capability layer | Copilot can't list or resolve a Work Item |
| Decision capability layer (B-1) | `SignalReadService`, `DecisionCommitService`, plan endpoints | Yes, it extends them: the endpoint bodies move into capability methods that take the request `HttpContext` or `ClaimsPrincipal`, with no new rules (FR-10, option C) | A second host forks caller resolution and result mapping, giving the invariant a second owner |
| Result mapper (B-2) | Endpoint statics (`DecisionCommitEndpoints.cs:60-118`, `SignalReadEndpoints.cs:46-83`) | Yes, it extracts those statics into one mapper | MCP results drift from REST reason codes; Finance already drifted (`errorCode` vs `reasonCode`) |
| `IDecisionLookupSearch` + REST route + tool (B-3) | Console's `Spaarke.AI.Widgets/src/widgets/workspace/worklistLookup.ts` (browser `Xrm.WebApi`); external `ExternalModuleDataEndpoints.cs` `/fetch` (external plane) | No. A browser `Xrm` helper can't run on a server, and the external route's policy must not serve workforce callers | Six action types can't be completed outside the Console, and D-18 stays enforced only in the browser |
| `ICallerContext` (B-4) — **DEFERRED, not built in v1** (owner, 2026-11-07, option C; FR-10) | `CallerResolution`, `CallerWhoAmIMemo`, `IHttpContextAccessor` (85 uses in 46 files) | Yes: the existing `IHttpContextAccessor` path already serves MCP tools, because the stateless transport runs them inside the ASP.NET request | Nothing fails in v1: tools run inside the request. Off-request callers are out of v1 and are blocked by the user-token problem, not by this abstraction (FR-10 v1 limitation) |
| Slim projection `WorklistItemSummary` (B-5) | `WorklistItem` rows (1–2 KB each, with `EvidenceRefs` and `Steps`) | It is a projection of the existing row, not a new model | 30–50 rows exceed Cowork's 64 KiB inline cap |
| `mcp` rate-limit policy | Existing BFF rate-limit policies | It adds one named policy in the existing rate-limit module | One caller's widget can exhaust the shared limit, or `/mcp` runs unthrottled |
| `Mcp:Enabled` option + null object | ADR-032 kill-switch pattern | It follows the pattern | MCP can't be switched off in an environment without a redeploy |
| `src/client/widgets/spaarke-decisions/` | Code-page Vite builds (`CreateMatterWizard`) | Same toolchain, new entry point; no fork of components | No UI to resolve a Work Item in Copilot or Cowork |
| Packages `ModelContextProtocol.AspNetCore` 2.3.1 (+ `Microsoft.Extensions.AI` 10.11) | None for MCP serving (grep `ModelContextProtocol` in csproj: none) | No: the protocol, transport and MCP Apps resource handling are what the SDK provides | Hand-rolling the 2026-07-28 transport and conformance |
| Connector client app registration (A10) | A8 workforce client; A9 SPA client; BFF app | No (A10: separate consent, redirect URIs and revocable credential) | The connector can't obtain a Spaarke-tenant token, so guests fail on Model 1 |
| Plugin package + `spaarke-decisions` skill | April `src/solutions/CopilotAgent/appPackage` (a declarative agent, not `agentConnectors`) | No: a different package type and manifest version (1.30) | No `/` entry point and no documented widget route in Cowork |

## ADR Tensions (per CLAUDE.md §6.5)

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| ADR-028 (A8) | Workforce clients and authority rules for Model 1 | The Copilot / Cowork connector is a new client plane A1–A9 did not cover, and the connector's Entra SSO issues home-directory tokens that break Model 1's guest-`oid` rule | **B — amendment, done** | **ADR-028 A10** written and owner-approved 2026-11-05 (commit c39828a484), merging with this project's code. If T5 fails, a follow-up amendment is required. |
| ADR-013 / `bff-extensions.md` | "MCP as a separate deployable: consider, needs successor ADR" | An MCP server could be its own service | **C — comply** | The tools are a second front door to BFF capabilities, not a bounded engine. Placement in the BFF is justified above. |
| ADR-008 / ADR-010 / ADR-032 | Filter-held checks; module DI; kill switch | Tools are not endpoints, so endpoint filters don't reach them | **C — comply** | B-6 / B-7: per-tool policies reuse existing policies; module registration; null-object kill switch |
| ADR-002 | One server-side owner per invariant; the client previews only | A new host writes decisions | **C — comply** | The same single owner, `DecisionCommitService`, is unchanged; the widget previews and the server decides |

## Success Criteria

1. [ ] Phase 0 recorded: T5, T0 and T1 have results and decisions, and E1–E5 and T13–T14 are recorded - Verify by: `notes/phase0-results.md` reviewed by the owner.
2. [ ] A licensed non-admin dev-tenant user lists their Decide lane and resolves a Work Item end to end **in Cowork** through `/spaarke-decisions`, and a Decision Record is written - Verify by: live run with the record id captured; Console shows the item resolved.
3. [ ] The same in **Copilot Chat**, through the connector widget or (per T1) the Spaarke AI agent fallback - Verify by: live run, record id captured.
4. [ ] A direct prompt with no skill ("what decisions are waiting on me in Spaarke?") invokes `spaarke_show_decisions` - Verify by: E3 and a 20-prompt routing eval with its rate recorded.
5. [ ] Privileged detail never reaches model-visible output (FR-04) - Verify by: integration test with a marker string (negative).
6. [ ] REST contracts unchanged - Verify by: contract tests on the signal, plan and decision routes pass before and after.
7. [ ] Console worklist and wizard unchanged - Verify by: existing shared-lib tests plus a Console smoke against dev.
8. [ ] Auth negatives hold: anonymous, CIAM, A9 home-directory and no-`systemuser` callers are refused - Verify by: `tests/integration/auth/**`.
9. [ ] Protocol conformance and the ArchTest guard are green in CI - Verify by: CI run.
10. [ ] NFR-01 to NFR-04 measured within budget - Verify by: recorded measurements in the task notes.
11. [ ] Runbooks reproduce the connector, plugin and client registration from scratch - Verify by: a second admin follows them in the dev tenant.
12. [ ] Docs and terminology updated (FR-18) - Verify by: `doc-drift-audit` clean.

## Dependencies

### Prerequisites

- ADR-028 A10 (done; on this branch).
- Dev BFF (App Service) and dev tenant with Copilot licences. Cowork is enabled for the test users.
- The owner, as Global Admin, to run the connector admin-center step and approve the plugin deployment (O-20). A second admin may be designated.
- A licensed non-admin test user.
- Seed data: a test user with ≥ 200 Decide-lane Work Items across ≥ 3 matters, including privileged ones and each lookup-bearing action type.
- **Held ontology PRs merged into this branch (owner, 2026-11-06):** #2580 (`9423f37004`, server: 1c-billing at `DecisionCommitService.cs:974-996`, `IsBillingSubject`, the 409 `ontology.decision.decided_in_invoice_review`, `WorklistInvoiceReview` on rows) and #2579 (`ab04492ab7`, client: `invoiceDecisions` card, `openInvoiceReview.ts`, `decisionDisplay` mapping), so every task is planned against that code. Details: `notes/pre-pipeline-context.md`. Ontology's conditions:
  1. **No dev BFF deploy from this branch until ontology's pair has deployed** (it waits for ebilling task 112). Phase 0 live tests (T0–T14, E1–E5) therefore run from a **separate spike branch cut from master**, deployed through the owner's per-SHA go in uac-r2's session. If this branch must deploy sooner, back the merge out first.
  2. The D-207 order and replay guard stay unchanged. The MCP commit goes through the same `DecisionCommitService` entry, never a second route that skips 1c-billing.
  3. Ontology's pair merges to master first. If either branch gets new commits, re-merge (they merge, never rebase).
- **Sequencing with ontology-platform (agreed 2026-11-06):** B-1, B-2 and B-5 build on the PR #2580 code now on this branch (merged 2026-11-06; D-207, it adds the `ontology.decision.decided_in_invoice_review` 409 and `invoiceReview` on worklist items) rather than waiting for master. B-3 and the MCP foundation can start now (B-4 is deferred, FR-10). B-5's `WorklistItemSummary` must keep `invoiceReview`.
- **Reviews:**
  - ontology-platform reviews every PR touching `Services/Signals/**`, `Api/Signals/**` or the worklist route;
  - unified-access-control-r2 reviews every auth diff (FR-02 conditions);
  - task 010 (MCP caller-path verification, auth tag) gets two adversarial passes and unified-access-control-r2's review, and task 170's per-commit WhoAmI count is watched.

### External Dependencies

- Microsoft 365 admin center: Copilot › Connectors ("Connect to MCP server"); the Teams Developer Portal OAuth registration.
- MC1493223 (skills in Chat; rollout October to mid-November 2026) for the `/` skill in Chat. Cowork already supports it.
- MC1476316 (connector writes GA, October 2026).
- `ModelContextProtocol.AspNetCore` 2.3.1; `@modelcontextprotocol/ext-apps` 2.0.4; the `modelcontextprotocol/conformance` suite.
- Microsoft Ignite (17–20 November 2026): re-check widget support for connectors in Chat.

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Goal (O-1) | What is the goal? | Bring Spaarke's legal capabilities into Copilot and Cowork alongside Work IQ; reuse components; don't rebuild Spaarke | Thesis; reuse map |
| v1 write (O-2, O-14) | Read-only or completion? | v1 includes resolving a Work Item in the wizard, with form data; no model-callable complete tool | FR-08 app-only write |
| Registration (O-3, O-23) | Connector, plugin or agent? | Federated connector primary, plugin package too, Spaarke AI agent only if T1 requires it | FR-16, FR-17 |
| Users (O-4) | Which users in v1? | The BFF's own tenant; no guests or other tenants | Dev-tenant scope |
| Modal (O-5) | In-frame dialog fails? | Fallbacks agreed: fullscreen master-detail, then link to the Console | FR-13 |
| Agent (O-6) | If an agent is needed? | Start from the existing "Spaarke AI" agent | FR-17 |
| Rework (O-10) | Bolt on or rework the BFF? | Rework where it is the better technical decision | FR-09, FR-10 |
| Scope (O-11) | Which surface? | The Decide grid (Decide lane) and the Decision wizard; Decided history out | FR-12, FR-13 |
| Auth (O-12, O-24) | Which auth approach? | The production route: OAuth at the Spaarke-tenant authority; ADR-028 path B → A10 | FR-02 |
| Lookups (O-13) | Server lookups in scope? | Yes, including the BFF extension | FR-07 |
| April surface (O-15) | Remove the April Copilot code? | Leave it unless it interferes; filed as #2575 | Out of scope; FR-17 exception |
| Widget size (O-16) | Hosting and budget? | Embedded in the BFF, 3 MB | NFR-03 |
| Connector (O-17) | Who creates it? | This project | FR-16 |
| Entry points (O-18) | @, / or suggested prompts? | The `/` skill first, plus direct prompts; org prompts later | FR-16; out of scope |
| Branding (O-19) | Name for a family of capabilities? | One connector per product area, "Spaarke" + area; core member "Spaarke"; routes `/mcp/{area}` | FR-01, FR-16, FR-18 |
| Admin (O-20) | Who runs the admin steps? | The owner (Global Admin); a second admin can be designated | Dependencies |
| Terms (O-22) | Decision Record vs item? | A Signal produces a Work Item; resolving it writes a Decision Record | Terminology throughout; FR-18 |
| Privilege (spec interview) | What does the model see for privileged Work Items? | A neutral line only ("N privileged items — open to review"); detail only in the widget | FR-04 |
| Long lanes (spec interview) | How are 70+ items handled under 64 KiB? | The widget pages through an app-only call; Start review walks everything | FR-05, NFR-02 |
| Channel (spec interview) | Record that a decision came through Copilot? | No | `DecisionCommitService` unchanged; out of scope |
| Naming (2026-11-06) | Names after the cross-project sweep? | Route `/mcp/legal-operations`; functions are tool groups and skills; skills `spaarke-{area}-{capability}` (core drops the area); widget `ui://spaarke/decisions.html` in `src/client/widgets/spaarke-decisions/`; capability in `Services/Signals/`; "Spaarke AI" kept; "Spaarke Copilot" retired | FR-01, FR-12, FR-16, FR-18; naming note §1 |
| Coordination (2026-11-06) | Cross-project alignment? | Binding; recorded in the naming note §2 | Dependencies (sequencing, reviews) |
| Privilege source (2026-11-08) | What makes a Work Item privilege-flagged, given that `sprk_signal.sprk_privilegeflagged` is never written (ontology DI-64)? | **Option A widened**, as the ontology team proposed: Privileged or Potentially Privileged `sprk_privilegeclassification` on the subject communication or on any witness or evidence communication, derived at read time as the caller, one batched read per page; a fault or an unreadable communication flags; None or null does not | FR-04 amended; task 018 added; 022 depends on 018; `notes/decisions.md` 2026-11-08 |
| Caller context (2026-11-07) | Build `ICallerContext` (B-4) now, or let MCP tools use the request context? | **Option C: defer B-4.** MCP tools read the request `HttpContext` through `IHttpContextAccessor` (the 2.3.1 stateless transport runs them inside the request) and call the same decision services as REST, failing closed with `UserContextRequired` | FR-10 rewritten; design §8 B-4 superseded; task 010 becomes the MCP caller-path verification; task 012 cancelled; `notes/decisions.md` 2026-11-07 |

## Assumptions

- **Deployment**: v1 is deployed to the dev BFF and dev tenant only. `Mcp:Enabled` defaults to `false` and is turned on in dev only. Affects FR-01 configuration and release.
- **Console lookup**: the Console keeps its browser-side picker in v1. Switching it to the B-3 route is a follow-up. Affects FR-07 scope; SpaarkeAi stays untouched.
- **Latency targets**: NFR-01's numbers (3 s / 1.5 s / 15 s p95, 30 s ceiling) are working targets. The owner set none. Affects tool design and the commit path.
- **Page size**: page 1 carries at most 50 slim rows, which keeps it well under 64 KiB at 600 bytes per row. Affects FR-05.
- **Privilege flag source** (superseded 2026-11-08): the assumption that the Signal or its subject carries `sprk_privilegeflagged` did not hold, because nothing writes that column (ontology DI-64). FR-04 now states the owner's derivation rule.
- **Connector client**: a public client with PKCE if the Developer Portal registration accepts one; otherwise one confidential client per stamp (A10). Affects FR-16; decided by T5.
- **Telemetry sink**: the existing App Insights instance and dimensions; no new store. Affects NFR-05.
- **Caller context is request-bound (v1 limitation, option C)**: decision capabilities run only inside an HTTP request. This holds while `/mcp/*` stays stateless with `PerSessionExecutionContext=false`, which task 010 pins with a test. Off-request callers (durable agents, queued continuations, Teams proactive) are out of v1. The user-token problem blocks them, not the deferred `ICallerContext`; `ComposeProfileDispatcher.cs:88/117-134` shows today's detached-context workaround. Affects FR-10 and any later off-request slice.

## Unresolved Questions

- [ ] **T5:** does a connector accept an OAuth registration whose authority is the Spaarke tenant (relevant later when connectors are created in customer tenants), and does it accept a public client? - Blocks: FR-02 / FR-16 build order; A10's open point.
- [ ] **T1:** does the connector render the widget in Copilot Chat? - Blocks: whether FR-17 (agent fallback) is built.
- [ ] **T13:** can SDK 2.3.1 serve different tool sets on two routes in one host? - Blocks: the FR-01 route mechanism (fallback: one route that filters the tool list by path).
- [ ] **T3 / T4:** does the host confirm widget-initiated writes, and per call? - Blocks: nothing, because the server enforces writes (NFR-06). It informs wizard copy.
- [ ] **`WorklistLanePanel` standalone mount:** can it mount outside `WorklistWidget`? - Blocks: FR-12 approach (fallback: a single-lane prop on `WorklistWidget`).
- [ ] **Widget init budget:** does the shimmed wizard (with Lexical `EmailComposer`) fit 3 MB and an acceptable init time? - Blocks: FR-13 approach (fallback: `decisionDraft.ts` re-render).
- [ ] **Billing review in Copilot (next slice):** EB-D-194 / D-207 keep invoice decisions in the Invoice Review app - Blocks: nothing in v1; needs an owner decision before the billing slice.
- [x] **PR #2580 timing:** held until ebilling task 112 deploys Invoice Review to dev - Blocks: B-1, B-2 and B-5 start; billing link-outs in the list. Resolved 2026-11-06: merged into this branch; only the deploy gate remains (Dependencies, condition 1).
- [ ] **Connector creation by script:** is there an API or script for creating connectors? - Blocks: nothing in v1; it informs later customer provisioning.

---

*AI-optimized specification. Original design: design.md*
