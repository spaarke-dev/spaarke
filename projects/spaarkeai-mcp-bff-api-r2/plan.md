# Project Plan: Spaarke MCP r2 — Ask tool parity and retiring the Spaarke AI agent

> **Project**: `spaarkeai-mcp-bff-api-r2` · **Portfolio**: [#3150](https://github.com/spaarke-dev/spaarke/issues/3150) (Epic #421)
> **Status**: Ready for execution (project-pipeline Step 2–3, 2026-11-25)
> **Source**: `spec.md` (18 FRs, 10 NFRs), from `design.md` v0.3. Reuse evidence: `notes/reuse-scan.md`. Legacy inventory: `notes/legacy-agent-inventory-2026-11-25.md`.

---

## 1. Executive Summary

**Purpose.** Retire the legacy "Spaarke AI" declarative agent (usable only inside `@Spaarke AI`) and give Copilot Chat and Cowork its useful functions as about 19 MCP tools on the existing BFF MCP server (`/mcp/legal-operations`, one connector), plus three widgets that reuse Console components: Daily Briefing, search results and email compose. Every tool runs as the caller over the service its REST route already uses.

**Scope.** Legacy agent removal (code, CI, infra, docs, skills); tools for search, documents, playbooks, playbook runs, analysis, tasks, portfolio, Daily Briefing, matter summary, matter finance and email; durable playbook run status (`sprk_playbookrun` / `sprk_playbooknoderun`); the 30 s start-then-status pattern; caller-scope proofs; three widgets on a shared widget MCP transport; routing eval; the Dataverse MCP admin position. Out of scope: practice-area agents, invoice review in Copilot, intake, a second MCP route or connector, `summarizeFile`, identity functions, Dataverse's built-in MCP for end users, Outlook as a host, tenant/admin actions.

**Estimated effort.** 35 tasks, roughly 25–35 working days of task effort; Phase 0 runs now in parallel with r1, Phases 1–3 wait for r1's open PR #3161 and r1 task 042.

---

## 2. Architecture Context

### Design Constraints

- **One MCP host, one connector** (owner Q2): every tool on `/mcp/legal-operations` via `McpToolCatalog` (`[McpArea("legal-operations")]`); no second route, server or connector.
- **Thin adapters as the caller** (ADR-028 A10): tools read the request `HttpContext` through `IHttpContextAccessor`, fail closed with `UserContextRequired`, and call the same service or capability the REST route calls. No copied authorization: shared methods for playbook and finance checks.
- **REST bytes frozen** through every extraction (events capability, finance authorization, run persistence), proved by frozen-bytes or contract tests (model: `SignalRoutesContractBaselineTests`).
- **30 s per tool** (owner Q3); longer work returns `{runId, status, consoleLink}` and a status tool (FR-14).
- **Writes:** reversible writes are model-callable with server validation and idempotency keys; email send is app-only (NFR-06).
- **No background work** (ADR-052): run persistence happens on the run's own execution path.
- **Widgets:** one Console component each, data through app-only tools, empty CSP, < 1 MB target, 3 MB hard ceiling, Fluent v9 semantic tokens, host light/dark (FR-16, NFR-03, NFR-09).
- **BFF hygiene** (CLAUDE.md §10): publish-size delta vs a fresh master build on every BFF task (+5 MB justify / 55 MB review / 60 MB stop); no new HIGH CVE; no new packages expected.

### Key Technical Decisions

| Decision | Rationale | Where |
|---|---|---|
| Remove the legacy agent now, no parallel period | Owner 2026-11-25; avoids double answers | 001, 002, 003, 019 |
| Move `HandoffUrlBuilder` out of `Api/Agent/` instead of deleting it | It is injected by the live chat create-record tool (`Services/Ai/Handlers/DataverseCreateRecordHandler.cs:229`) and many data-mutation tests; MCP uses it only in a comment | 001 |
| Persist run status to existing `sprk_playbookrun` / `sprk_playbooknoderun` | Owner; survives restart and scale-out; reused by specialist-legal-agents D-A18 | 005, 030 |
| Matter summary = complete the `matter-summary` Insights topic (new `sprk_matter` column + `sprk_aitopicregistry` row) | Owner 2026-11-25; one summary everywhere | 012, 032 |
| Briefing = AI Daily Briefing with a data seam | Owner; reuse the Console component | 010, 034 |
| Per-area MCP ledger partial files | Lets tool tasks run in parallel without editing one shared `McpTools` array | 020 |
| Shared widget transport as a source folder under `src/client/widgets/shared/` (not a new npm package, not inside `@spaarke/ui-components`) | Keeps `@modelcontextprotocol/ext-apps` out of Console bundles and avoids a 16th shared package (reuse-governance finding); escalation if adr-check flags ADR-012 | 021 |
| EmailComposer MCP host defines a minimal injected `callTool` interface | `@spaarke/ui-components` stays free of MCP SDK types; 021's transport satisfies it | 011, 021, 040 |

### Discovered Resources

**Applicable ADRs** (concise in `.claude/adr/`; full ADR-038 in `docs/adr/`):
- `ADR-001-minimal-api.md` — one Minimal API pipeline; tools sit on the existing MCP host.
- `ADR-002-thin-plugins.md` — no plugins; one server-side owner per invariant (events capability, `CommunicationService`, `RecordCreationService`); fail closed.
- `ADR-004-job-contract.md` / `ADR-052-workload-placement.md` — no new background work; playbook runs keep in-process execution.
- `ADR-008-endpoint-filters.md` — authorization via filters/policies; shared methods instead of copies.
- `ADR-010-di-minimalism.md` — feature-module DI (`AddMcpModule`, existing modules).
- `ADR-012-shared-components.md` — reuse `@spaarke/ui-components` and existing shared packages.
- `ADR-013-ai-architecture.md` — AI surfaces extend the BFF; tools inject no AI-internal types (use `PublicContracts`).
- `ADR-015-ai-data-governance.md` — minimum text to the model; log ids, sizes, timings, outcomes only.
- `ADR-019-problemdetails.md` — ProblemDetails for REST; MCP errors through `McpToolResults` with the same `reasonCode`.
- `ADR-021-fluent-design-system.md` / `ADR-050-canonical-modal-shell.md` — Fluent v9, semantic tokens, dark mode; shared modal shell.
- `ADR-028-spaarke-auth-architecture.md` — A10 connector plane; tools run as the caller (OBO); `systemuser` callers only.
- `ADR-029-bff-publish-hygiene.md` — publish size and package hygiene.
- `ADR-032-bff-nullobject-kill-switch.md` — `Mcp:Enabled=false` disables every tool and widget.
- `ADR-039-grounded-execution-closed-catalogs.md` — closed catalogs for playbook/scope selection.
- `ADR-043-ai-capability-execution-spine.md` — playbook runs and analysis go through the execution spine.
- `ADR-044-dataverse-guid-canonicalization.md` — guid inputs on tools are canonicalised.
- `ADR-045-communication-architecture.md` — email send through the `CommunicationService` core.
- `docs/adr/ADR-038-testing-strategy.md` — KEEP paths, build-vs-maintain, no `Mock<HttpMessageHandler>`.
- No ADR tensions (spec "ADR Tensions": two Path C checks recorded).

**Applicable Skills** (`.claude/skills/`):
- Building: `mcp-tool-handler` (tool classes), `widget-design` (MCP App widgets), `fluent-v9-component`, `spaarke-conventions`.
- Dataverse: `dataverse-create-schema` (005, 012 columns), `dataverse-deploy`, `dataverse-mcp-usage` (004 and schema verification).
- Review/testing: `adr-check`, `code-review`, `test-diet` (090), `ui-test` (widget and E2E checks), `conflict-check` (before each BFF PR).
- Deploy: `bff-deploy` (019, 029, 039, 049); never raw `az webapp deploy --clean`.
- Other: `context-handoff` (after each deploy), `project-defer-issue-tracking`, `researcher` agent (004; CLAUDE.md §15).

**Knowledge and guides:**
- Primary recipe: `docs/guides/HOW-TO-ADD-AN-MCP-TOOL.md`.
- Architecture: `docs/architecture/SPAARKE-MCP-SERVER-ARCHITECTURE.md`, `docs/architecture/SPAARKE-AI-SURFACES-AND-PACKAGING.md`, `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md`.
- Insights topics: `docs/guides/BUILD-A-NEW-INSIGHT-CARD.md`, `docs/data-model/sprk-playbookconsumer.md`, `docs/guides/ai-guide-consumer-wiring.md`.
- Dataverse MCP facts: `knowledge/dataverse-mcp/` (2026-05; re-checked by 004).
- Constraints: `.claude/constraints/bff-extensions.md`, `azure-deployment.md` ("BFF Publish-Size Per-Task Verification Rule"), `api.md`, `auth.md`, `ai.md`, `testing.md`, `pcf.md`.
- Patterns: `.claude/patterns/api/endpoint-filters.md`, `api/send-email-integration.md`, `auth/obo-flow.md`, `testing/integration-tests.md`, `testing/mocking-patterns.md`, `ui/embedded-widget-sizing.md`, `ui/fluent-v9-theming.md`, `CAPABILITY-INDEX.md`.
- Tenant runbook (owner): `projects/copilot-platform-coordination/notes/removal-runbook.md` on `work/copilot-platform-coordination` (steps E1, E2).

**Canonical code to copy** (paths under `src/server/api/Sprk.Bff.Api/` unless stated):
- Tool adapter: `Api/Mcp/LegalOperations/Decisions/CompleteDecisionTool.cs` (write), `ShowDecisionsTool.cs` (read); helpers `DecisionToolCall.cs`, `PlanAndLookupToolSupport.cs`; `Api/Mcp/McpToolAuthorization.cs`, `Api/Mcp/McpToolResults.cs`, `Api/Mcp/McpToolCatalog.cs`.
- One result to REST and MCP: `Api/Signals/DecisionResultMapper.cs`.
- Capability layer taking the request context: `Services/Signals/IDecisionCapability.cs` / `DecisionCapability.cs` (registered `Infrastructure/DI/SignalsModule.cs:138`).
- Widget resource: `Api/Mcp/LegalOperations/DecisionsWidgetResource.cs`, registered in `Infrastructure/DI/McpModule.cs:78`; csproj `EmbeddedResource` at `Sprk.Bff.Api.csproj:271-279`; CI steps in `.github/workflows/publish-bff-artifact.yml:33-38` (paths) and `:95-156` (build, verify, gate).
- Start-and-poll shape and `StartedByOid` owner check: `Api/Agent/AgentEndpoints.cs:461-481` and `Api/Agent/PlaybookStatusEndpoints.cs` (`AgentPlaybookStatusService`, 30 s threshold). Deleted by 001: read from git at `37f0864afe`.
- Playbook de-duplication: `Api/Agent/AgentEndpoints.cs:62` (`ListPlaybooksAsync`, `:334`, `:357`) — read from `37f0864afe` after 001.
- Widget client: `src/client/widgets/spaarke-decisions/` (`src/App.tsx:38` injected services, `src/lane/decideLaneFetch.ts`, `src/mcp/mcpFetch.ts`, `mcpHost.ts`, `mcpHostContext.ts`, `scripts/check-bundle-size.mjs`, `src/__tests__/`).
- Console transport: `src/client/shared/Spaarke.UI.Components/src/components/Worklist/worklistService.ts:350`; `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/WorklistWorkspaceWidget.tsx:71`.
- Components reused: `src/client/shared/Spaarke.DailyBriefing.Components/src/components/DailyBriefingApp.tsx` (+ `services/briefingService.ts`); `src/client/shared/Spaarke.AI.Widgets/src/outputs/output-widgets/SearchResultsWidget.tsx`; `src/client/shared/Spaarke.UI.Components/src/components/EmailComposer/` (`createXrmEmailComposeHandlers.ts`, `useXrmEmailHostProps.ts`).

**Canonical tests to copy:**
- Read tool: `tests/integration/contract/Api/Mcp/ShowDecisionsToolTests.cs` (host `DecisionToolsTestHost.cs`, `McpTestHost.cs`).
- Write tool: `tests/integration/data-mutation/Mcp/CompleteDecisionToolTests.cs`.
- Auth: `tests/integration/auth/Mcp/McpAuthTests.cs`, `McpCallerPathTests.cs`.
- Size: `tests/integration/regression/Mcp/McpResultSizeTests.cs`; conformance host `tests/integration/Mcp.Conformance.Host/`.
- Ledger: `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Mcp.cs` (`McpTools`), `.Ledger.cs`, `.ProvenByTest.cs`.
- Frozen REST bytes: `tests/integration/contract/Api/Signals/SignalRoutesContractBaselineTests.cs`.
- Widget resource: `tests/integration/contract/Api/Mcp/DecisionsWidgetResourceTests.cs`.

**Scripts:** `scripts/Validate-TaskPoml.ps1`, `scripts/check-task-status-drift.ps1`, `scripts/quality/check-changed-cs-format.sh`, `scripts/Deploy-BffApi.ps1` (via `bff-deploy`), `src/client/widgets/spaarke-decisions/scripts/check-bundle-size.mjs`, `scripts/worktree/Remove-WorktreeSafe.ps1`.

**Dataverse schema findings (discovery, 2026-11-25):**
- `sprk_playbookrun` exists (0 rows) but lacks: correlation/idempotency id, error code, regarding-record lookup, short summary. Confirm `sprk_status` option values and that `sprk_triggeredby` targets `systemuser`. → task 005.
- `sprk_playbooknoderun` lacks started/completed timestamps and a sequence. → task 005.
- ~~Binding row `matter-summary` (SUM-CHAT@v1) has no `sprk_aitopicregistry` row and no target column.~~ **Corrected by task 012 (2026-11-25):** dev already has the registry row `matter-summary/single` targeting `sprk_matter.sprk_recordsummary` (Memo, TTL 60 min) and the persister already writes it; only the row's `sprk_playbookname` key (`chat-summarize` → `matter-summary`) needs the owner's go (`notes/012-matter-summary-topic.md`).

**§11 flags.** None unjustified at project level: every new surface in spec "New Components" cites `notes/reuse-scan.md`. One addition found in discovery, justified in its task: the run-record schema gap columns (005). (The `sprk_matter` summary column first listed for 012 already exists as `sprk_recordsummary`.)

**Hot-path result (HARD WARNING).** BFF is touched by many active projects; the direct overlaps are `spaarkeai-mcp-bff-api-r1` (`Api/Mcp/`, ledger, Decisions widget; PR #3161 open) and `specialist-legal-agents-r1` (future `/mcp/patents`). `design.md` lacks a `<hot-path-declaration>` block; the block is in `spec.md` ("Hot-Path Declaration"). The owner may add it to design.md.

---

## 3. Implementation Approach

### Phase Structure

| Phase | Tasks | Gate |
|---|---|---|
| 0 — removal, prerequisites, seams | 001–012, 019 | None for code; 019 waits for owner E1 and per-SHA go |
| 1 — read tools | 020–026, 029 | r1 PR #3161 and r1 task 042 merged to master and master merged here |
| 2 — long-running, AI, widgets, task writes | 030–036, 039 | Phase 1 tools + Phase 0 prerequisites per task |
| 3 — email, eval, E2E | 040–043, 049 | Phase 2 |
| Wrap-up | 090 | All tasks done or cancelled |

### Critical Path

r1 gate → 020 → 024 → 030 → 032 → 039 → 040 → 049 → 042 / 043 → 090. Feeders: 001 → 007 → 030; 005 → 030; 012 → 032; 011 → 021 → 034 → 035 → 039 / 040; 006 → 025 → 036 → 039. The external r1 gate in front of 020 is the largest schedule risk. (Same as `tasks/TASK-INDEX.md` "Critical Path".)

---

## 4. Phase Breakdown

### Phase 0: Removal, prerequisites and seams (001–012, 019)

**Objectives:** remove the legacy agent; land every prerequisite the tools need (durable runs, events capability, shared auth methods, raw-query proof, schema, component seams) without touching r1 files.

| Task | Deliverable |
|---|---|
| 001 | BFF legacy removal: `Api/Agent/`, `AgentModule`, `AgentTokenOptions`, registrations, tests, ledger rows; `HandoffUrlBuilder` relocated; agent branch removed from `PlaybookAuthorizationFilter.AuthorizeRunAsync` (the only user of the `AgentEndpoints` constants); publish-size delta |
| 002 | Non-BFF removal: `src/solutions/CopilotAgent/`, workflow, scripts, tests, `infrastructure/bot-service/`, docs |
| 003 | `.claude/` references (main session only) |
| 004 | Dataverse MCP research note + deployment-guide admin paragraph (FR-18) |
| 005 | Durable run status + schema gap columns (owner-gated dev schema change) (FR-13) |
| 006 | Events capability extraction + must-not-leak proofs, REST bytes frozen (FR-07 part 1, FR-15) |
| 007 | Playbook authorization shared method (FR-05 prerequisite) |
| 008 | Finance authorization shared method (FR-11 prerequisite) |
| 009 | Record-search raw-query proof (FR-02 / FR-15) |
| 010 | Daily Briefing data seam (FR-16) |
| 011 | EmailComposer MCP host (FR-12 / FR-16) |
| 012 | Matter-summary topic: column, registry row, persister check (owner-gated) (FR-10 prerequisite) |
| 019 | Deploy Phase 0 to dev (owner E1 + per-SHA go) |

**Inputs:** spec, inventory, reuse scan. **Outputs:** removal merged, prerequisites merged. **Dependencies:** none external for code.

### Phase 1: Read tools (020–026, 029)

**Objectives:** the shared tool plumbing and the read tools. **External gate:** r1 PR #3161 + r1 task 042 merged.

| Task | Deliverable |
|---|---|
| 020 | Generalised `McpToolResults`, long-running start/status helper, per-area MCP ledger partial files (FR-14) |
| 021 | Shared widget MCP transport; Decisions widget moved onto it, no behaviour change (FR-16) |
| 022 | `spaarke_search_documents`, `spaarke_find_records` (FR-02) |
| 023 | `spaarke_list_documents`, `spaarke_get_document` (FR-03) |
| 024 | `spaarke_list_playbooks` (FR-04) |
| 025 | `spaarke_list_tasks`, `spaarke_get_task` (FR-07) |
| 026 | `spaarke_get_portfolio` (FR-08) |
| 029 | Deploy Phase 1 to dev; routing-eval run A |

### Phase 2: Long-running work, AI, widgets, task writes (030–036, 039)

| Task | Deliverable |
|---|---|
| 030 | `spaarke_run_playbook`, `spaarke_get_playbook_run` (FR-05) |
| 031 | `spaarke_start_analysis`, `spaarke_get_analysis` (FR-06); exposes `AnalysisAuthorizationFilter`'s create check as a shared method (REST bytes frozen) |
| 032 | `spaarke_get_matter_summary` (FR-10) |
| 033 | `spaarke_get_matter_finance` (FR-11) |
| 034 | `spaarke_get_briefing` + Daily Briefing widget (FR-09, FR-16) |
| 035 | Search results widget (FR-16) |
| 036 | `spaarke_create_task`, `spaarke_complete_task` with idempotency keys (FR-07) |
| 039 | Deploy Phase 2 to dev; FR-13 restart check; routing-eval run B |

### Phase 3: Email, evaluation and E2E (040–043, 049)

| Task | Deliverable |
|---|---|
| 040 | Email compose widget, `spaarke_compose_email` (model-visible), app-only `spaarke_send_email` (FR-12); exposes the `/api/communications/send` filter checks as one shared method (REST bytes frozen) |
| 041 | Routing eval at ≈10, ≈15 and all tools (FR-17) — manual, owner runs prompts |
| 042 | Latency and result-size run (NFR-01, NFR-02) |
| 043 | Dev E2E as the test user in Chat and Cowork |
| 049 | Deploy Phase 3 to dev |

FR-12 sizing check (spec Assumptions): email takes two dedicated tasks (011, 040) plus the shared transport (021) — under the ~5-task threshold, so the owner's inclusion decision stands.

### Wrap-up (090)

Final code-review/adr-check, `/test-diet`, repo-cleanup, final docs (MCP architecture doc tool list, HOW-TO guide), CAPABILITY-INDEX rows (main session), BriefingService retirement follow-up, archive.

---

## 5. Dependencies

### External Dependencies

- `spaarkeai-mcp-bff-api-r1`: PR #3161 and task 042 must merge before Phase 1 (shared `Api/Mcp/`, `McpToolCatalog`, `McpModule`, ledger, Decisions widget). spec.md names PR #3146 — that PR merged; #3161 is the open one.
- Owner: runbook E1 before 019; per-SHA go for every dev deploy; dev Dataverse schema changes (005, 012); restart of dev App Service (039).
- Reviews: unified-access-control-r2 (every caller path: 001, 006, 007, 008, 009, 022–036, 040); ebilling-r1 (008, 033); ontology-r1 (capability and authorization extractions: 006, 007, 008, 031).
- Microsoft 365 Copilot Chat / Cowork hosts; `@modelcontextprotocol/ext-apps` 2.0.4; `ModelContextProtocol.AspNetCore` as pinned by r1.

### Internal Dependencies

- Phase 0 prerequisites feed Phase 1–2 tools (see TASK-INDEX.md dependency column).
- 020 feeds every tool task; 021 feeds every widget task.

---

## 6. Testing Strategy

- **Unit/integration** (ADR-038 KEEP paths): tool contract tests under `tests/integration/contract/Api/Mcp/`, write tools under `tests/integration/data-mutation/Mcp/`, caller-scope proofs under `tests/integration/auth/`, size under `tests/integration/regression/Mcp/`; mock at module boundaries only.
- **Parity:** each tool returns REST-equal results for the same caller; refusal `reasonCode` equals REST.
- **Frozen bytes:** events, finance and run routes keep their REST responses byte-for-byte.
- **Architecture:** route-guard ledger and MCP tool ledger (`RouteAuthorizationGuardTests.Mcp*.cs`) updated per tool.
- **Widgets:** jest tests and bundle gate per widget; Console regression suites of the three reused components unchanged.
- **Conformance:** `modelcontextprotocol/conformance` in CI with every tool registered (NFR-08).
- **Live:** dev deploys with probes; routing eval; 50-call latency/size run; E2E as the test user in Chat and Cowork.

---

## 7. Acceptance Criteria

1. Legacy agent code, gateway, bot infra, CI, tests and docs are gone; BFF builds with 0 warnings; `/api/agent/message` returns 404 on dev (FR-01).
2. Each FR-02..FR-12 tool returns REST-equal results for the same caller and refuses what REST refuses (parity and must-not-leak tests; dev E2E).
3. A playbook run's status survives a BFF restart (FR-13 test; dev restart in 039).
4. The model cannot send email; the user can from the widget; the email is archived to the matter with attachments (FR-12).
5. The three widgets render in Chat and Cowork, light and dark; the Console's own uses are unchanged (FR-16).
6. Routing eval meets FR-17 at ≈10, ≈15 and all tools (`notes/routing-eval.md`).
7. NFR-01 latency and NFR-02 size hold (`notes/latency-size-run.md`).
8. Conformance passes with every tool registered (CI).
9. The Dataverse MCP admin paragraph is merged; platform facts re-verified with dated sources (FR-18).

---

## 8. Risk Register

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| R1 | r1 PR #3161 / task 042 slip delays Phases 1–3 | Medium | High | Phase 0 is independent; Phase 1 tasks carry the external gate |
| R2 | Events reads leak another user's private event (BFF identity + BFF-applied filter) | Medium | High | 006 must-not-leak tests; if they fail, change the read to run as the caller (spec Assumptions) — escalate |
| R3 | Removing `AgentModule` breaks DI for `DataverseCreateRecordHandler` (`HandoffUrlBuilder`) | High if missed | High | 001 relocates and re-registers `HandoffUrlBuilder` before deleting the module |
| R4 | Run persistence write failure fails a run | Low | High | 005: writes are try/log; acceptance test forces a write failure |
| R5 | Dev schema changes (005, 012) collide with other projects' solutions | Low | Medium | Owner go; `dataverse-create-schema`; schema verified via Dataverse MCP read-only |
| R6 | Routing quality drops with ~20 tools on one connector | Medium | Medium | FR-17 staged runs; fixes go in descriptions, not a route split |
| R7 | Widget bundles exceed 3 MB | Medium | Medium | Size gate per widget; single component per widget |
| R8 | Ledger/`McpModule`/csproj contention between tool tasks | Medium | Medium | 020 per-area ledger files; widget tasks 034 → 035 → 040 serialised |
| R9 | Legacy-agent references outside our scope (customer-provisioning T257 "shared Spaarke Copilot Agent app" in control-plane Bicep and deployment guide §7.12) | High | Low | 002 leaves them, files a coordination issue to customer-provisioning-orchestration-r1 |
| R10 | Daily Briefing generation exceeds 30 s | Medium | Medium | 034 uses the FR-14 helper when needed |

---

## 9. Next Steps

1. Main session: commit artifacts, add `projects/INDEX.md` row, sync #3150 (project-pipeline Step 4/4.2).
2. Phase 0 Wave 1: 001, 002, 005, 006, 008, 009 (TASK-INDEX.md "Parallel Execution Plan").
3. Owner: runbook E1; go for 005/012 dev schema changes when those tasks reach that step.

---

## 10. References

- `spec.md`, `design.md`, `notes/reuse-scan.md`, `notes/legacy-agent-inventory-2026-11-25.md`, `tasks/TASK-INDEX.md`, `CLAUDE.md`.
- Sibling project: `projects/spaarkeai-mcp-bff-api-r1/` (plan, tasks, `notes/routing-eval.md`).
- Root `CLAUDE.md` §8.5, §10, §11; `.claude/constraints/bff-extensions.md`; `docs/standards/TEST-ARCHITECTURE.md`.
