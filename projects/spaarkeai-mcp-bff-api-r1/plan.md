# Project Plan: Spaarke in Microsoft Copilot and Cowork — MCP Server in the BFF (r1)

> **Last Updated**: 2026-11-06
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md) · **Design**: [design.md](design.md) · **Operating manual**: [CLAUDE.md](CLAUDE.md)

---

## 1. Executive Summary

**Purpose**: Put Spaarke's Decide lane and Decision wizard inside Microsoft 365 Copilot Chat and Cowork, alongside Work IQ, through one MCP server in the BFF that runs every tool as the signed-in user over the same services the Console uses. v1 proves the pattern end to end in the dev tenant so later capabilities become small slices that add tools.

**Scope**:
- `/mcp/legal-operations` in the BFF: stateless, ADR-028 A10 auth, `mcp` rate-limit policy, size limit, `Mcp:Enabled` kill switch
- Four `spaarke_*` tools plus an app-only paging tool, over a transport-free decision capability (B-1 to B-10)
- Widget `ui://spaarke/decisions.html` (Decide lane + Decision wizard), embedded in the BFF
- Dev-tenant registration: A10 connector client, federated connector "Spaarke", plugin package with `spaarke-decisions`, runbooks; Spaarke AI agent fallback only if T1 requires it
- Phase 0 platform tests (T0–T14, E1–E5); docs, terminology and skill rewrites

**Timeline**: no dates (owner controls the calendar) | **Estimated effort**: 28–40 working days across about 24 tasks. Phase 0 elapsed time depends on owner admin-center steps; Phase D's dev deploy depends on ontology's #2580/#2579 pair deploying first.

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-001**: Minimal API BFF, one pipeline; no Functions or Durable packages in the BFF.
- **ADR-002**: no plugins; one server-side owner per invariant (`DecisionCommitService`); the client previews only; security fails closed.
- **ADR-008**: authorization through endpoint filters and policies; tools get per-tool policies (B-7).
- **ADR-010**: feature-module DI — `AddMcpModule` / `MapMcpEndpoints` in `EndpointMappingExtensions`.
- **ADR-012**: reuse `@spaarke/ui-components`; no per-project component libraries.
- **ADR-013**: AI and agent surfaces extend the BFF unless extraction criteria are met (they are not); tools inject no AI-internal types.
- **ADR-015**: minimum text to the model; log identifiers, sizes, timings and outcome codes only.
- **ADR-016**: rate limits on AI-facing surfaces — dedicated `mcp` policy partitioned per caller.
- **ADR-019**: ProblemDetails for REST errors, produced through the B-2 mapper.
- **ADR-021 / ADR-050**: Fluent v9 semantic tokens, dark mode; shared modal shell for the in-frame dialog.
- **ADR-028 (A4, A8, A10)**: Spaarke-tenant OAuth, `user_impersonation`, OBO as the caller, `systemuser` only; CIAM and A9 tokens refused on `/mcp/*`.
- **ADR-029**: BFF publish hygiene — publish-size delta and CVE check per BFF task.
- **ADR-032**: `Mcp:Enabled` null-object kill switch → 503 ProblemDetails.
- **ADR-038**: KEEP-path tests; mock at module boundaries; no `Mock<HttpMessageHandler>` / `Mock<IServiceClient>`.
- **ADR-052**: no background work; every tool finishes under 30 s.

**From Spec**:
- `SignalReadService` read rules and `DecisionCommitService` validation, idempotency and execution order are unchanged; commit invariants move byte-for-byte through B-1 and are stated in the PR.
- REST responses of the signal, plan and decision routes stay byte-for-byte unchanged (FR-09).
- Privileged Work Items reach model-visible output only as a neutral count line (FR-04).
- Only the widget writes (app-only `spaarke_complete_decision`); nothing relies on a host confirmation prompt (NFR-06).
- No second billing-refusal check; D-207 applies through the extracted capability (one MCP test).

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| MCP host in the BFF (ADR-013 Path C) | Tools need caller OBO, the Redis ledger and `DecisionCallerRights`, which exist only in the BFF | New `Api/Mcp/`; publish size grows ~1.6 MB + widget 1–3 MB |
| One decision capability for REST and MCP (B-1, B-2, B-4) | A second host would fork caller resolution and result mapping | `Services/Signals/` extraction; endpoints become thin adapters |
| Paging (FR-05): a **fifth app-only, readOnly tool** with a cursor (working name `spaarke_list_decision_page`, final name in task 022 per the naming note) | The model-visible `spaarke_show_decisions` keeps its closed schema; an app-only tool is invisible to the model | Task 022 delivers both tools |
| Plugin package folder: `src/solutions/CopilotPlugins/spaarke/` (app manifest 1.30, `agentConnectors`, `skills/spaarke-decisions/`); runbooks in `docs/guides/` | Spec delegates the path to plan.md; mirrors `src/solutions/CopilotAgent/` without overloading it | Task 040 creates it; task 040 may move it if the build tooling requires |
| Phase 0 on a master-based spike branch `spike/spaarkeai-mcp-phase0` (never merged) | Ontology condition 1: no dev BFF deploy from this branch until their pair deploys | Task 001 cuts it in its own worktree |
| Widget reuses `WorklistLanePanel` by deep import + `buildLaneView` | `WorklistLanePanel` is not exported from the Worklist barrel | Task 031; fallback is a single-lane prop on `WorklistWidget` |

### Discovered Resources

**Applicable ADRs** (`.claude/adr/`, full history in `docs/adr/`):
- `ADR-001-minimal-api.md` — one Minimal API pipeline, no Functions in the BFF
- `ADR-002-thin-plugins.md` — no plugins; single server-side invariant owner; fail closed
- `ADR-008-endpoint-filters.md` — authorization via filters/policies, not global middleware
- `ADR-010-di-minimalism.md` — feature-module DI (`Add{X}Module` / `Map{X}Endpoints`)
- `ADR-012-shared-components.md` — reuse `@spaarke/ui-components`
- `ADR-013-ai-architecture.md` — AI/agent surfaces extend the BFF; extraction criteria
- `ADR-015-ai-data-governance.md` — minimum text to models; log ids/sizes/timings only
- `ADR-016-ai-rate-limits.md` — per-caller rate limiting on AI-facing surfaces
- `ADR-019-problemdetails.md` — ProblemDetails error contract
- `ADR-021-fluent-design-system.md` — Fluent v9, semantic tokens, dark mode
- `ADR-028-spaarke-auth-architecture.md` — A8 workforce client, A9, **A10 Copilot/Cowork connector plane**
- `ADR-029-bff-publish-hygiene.md` — publish-size and package hygiene
- `ADR-032-bff-nullobject-kill-switch.md` — options + null object → 503
- `docs/adr/ADR-038-testing-strategy.md` — KEEP paths, build-vs-maintain, mocking boundaries
- `ADR-050-canonical-modal-shell.md` — shared modal shell
- `ADR-052-workload-placement.md` — no new background work; live caller work in the BFF

**Applicable Skills** (`.claude/skills/`):
- `mcp-tool-handler/` — implement MCP tool handlers (rewritten by task 051)
- `widget-design/` — MCP App widget design (updated by task 051)
- `declarative-agent/` — agent manifest work for the FR-17 fallback (updated by task 051)
- `fluent-v9-component/` — Fluent v9 authoring for the widget
- `adr-check/`, `code-review/` — quality gates (auth tasks: two adversarial passes)
- `bff-deploy/` — dev BFF deploy (task 041, gated)
- `conflict-check/` — before each BFF PR (`Services/Signals/**`, `AuthorizationModule.cs` are shared)
- `test-diet/` — project-close test reconciliation (090)
- `ui-test/` — browser checks of the widget and Console smoke
- `ai-procedure-maintenance/` — Checklist F for the root CLAUDE.md row (050)
- `doc-drift-audit/` — FR-18 acceptance (052)

**Patterns** (`.claude/patterns/`):
- `api/*` — `endpoint-definition.md`, `endpoint-filters.md`, `error-handling.md`, `service-registration.md`, `resilience.md`
- `auth/obo-flow.md`, `auth/oauth-scopes.md` (also `auth/dataverse-obo.md`)
- `provisioning/null-object-kill-switch-anti-pattern.md` — kill-switch pitfalls
- `testing/*` — `integration-tests.md`, `mocking-patterns.md`, `unit-test-structure.md`
- `ui/fluent-v9-*` (component authoring, theming, host visual fit, portal gotcha, React version boundaries), `ui/modal-shell.md`, `ui/embedded-widget-sizing.md`

**Knowledge** (`knowledge/`):
- `knowledge/mcp-apps/` — `approvals-box`, `trey-research` (incl. `src/mcpserver/widgets/hooks/useMcpApp.tsx`), `NOTES.md`
- `knowledge/declarative-agents/` — DA manifests incl. `declarative-agent-foodbank-mcp`
- `knowledge/m365-copilot/` — Copilot Camp, API plugin and DA samples
- `.claude/constraints/azure-deployment.md` "BFF Publish-Size Per-Task Verification Rule"
- `.claude/constraints/bff-extensions.md`, `.claude/constraints/auth.md` (A10 summary)

**Canonical code to copy** (paths under `src/server/api/Sprk.Bff.Api/` unless stated):
- Module DI: `Infrastructure/DI/SignalsModule.cs` + `Program.cs` registration
- Kill switch: `Infrastructure/DI/NotificationsModule.cs:20-55` (options + null object when disabled)
- Rate limits: `Infrastructure/DI/RateLimitingModule.cs` (per-user partition ~L301-322)
- Response buffering off: `Api/Workspace/WorkspaceMatterEndpoints.cs:196-200`
- Result-mapper sources: `Api/Signals/DecisionCommitEndpoints.cs:61` (`ToHttp`) and `:93`; `Api/Signals/SignalReadEndpoints.cs:46`, `:85`; `Api/Signals/DecisionPlanEndpoints.cs:251-345`
- Scoped server-side lookup: `Api/ExternalAccess/ExternalModuleDataEndpoints.cs` (guard L897, scope injection L301-356) — copy the pattern, not the plane
- Auth: `Infrastructure/DI/AuthorizationModule.cs` (`ApplyFallbackPolicy` L914; `AuthSchemes.Ciam` / `WorkforceHome`)
- Caller: `Infrastructure/Authentication/CallerResolution.cs`, `Infrastructure/Dataverse/CallerWhoAmIMemo.cs`, `Infrastructure/Dataverse/DataverseUserClient.cs` (`UserContextRequired` = `DATAVERSE_USER_CONTEXT_REQUIRED`)
- Commit owner: `Services/Signals/DecisionCommitService.cs` (`CommitAsync` L439; 1c-billing L974-996; `IsBillingSubject` ~L1684; `sprk_privilegeflagged` select L313)
- Embedded resources: `Sprk.Bff.Api.csproj` L249-268
- Client (`src/client/shared/Spaarke.UI.Components/src/components/`): `Worklist/WorklistWidget.tsx` (props L76-102), `Worklist/WorklistLanePanel.tsx` (presentational, not exported), `Worklist/worklistView.ts` (`buildLaneView`), `Worklist/worklistService.ts` (`createWorklistService({ fetch })` L350 — shim must return a `Response`), `DecisionWizard/DecisionWizardHost.tsx` (props L25-40), `Worklist/openInvoiceReview.ts` (`buildInvoiceReviewLaunch` L33 — reuse the `data` rules only)
- Console mount: `src/solutions/LegalWorkspace/src/sections/worklist.registration.ts:94-114`
- Single-file build: `src/solutions/CreateMatterWizard/vite.config.ts` (singlefile + source aliases + `resolveSharedLibDeps`)
- MCP App hook: `knowledge/mcp-apps/trey-research/src/mcpserver/widgets/hooks/useMcpApp.tsx`
- Arch guard to extend: `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.cs`

**Scripts and CI**:
- `scripts/Validate-TaskPoml.ps1` (Completeness Lint), `scripts/check-task-status-drift.ps1`
- `.github/workflows/client-tests.yml` (jest matrix — add the widget), `.github/workflows/publish-bff-artifact.yml` (widget build must precede BFF publish)
- No bundle-size check exists in CI yet — task 030 adds one

---

## 3. Implementation Approach

### Phase Structure

```
Phase 0: Platform spike (001–003)          — master-based spike branch; gates everything (T5, T0, T1)
Phase A: BFF rework (010–017)              — caller context, contract baseline, capability, mapper, projection, lookup
Phase B: MCP host and tools (020–025)      — module, auth, five tools, conformance + NFR measurements
Phase C: Widget (030–032)                  — scaffold + CI, Decide lane, Decision wizard
Phase D: Registration, deploy, live E2E (040–043) — plugin package, gated dev deploy, connector, live runs, FR-17 if needed
Phase E: Docs and terminology (050–052)    — root CLAUDE.md row, skills, docs; .claude/ edits main-session only
Wrap-up (090)                              — test-diet, lessons learned
```

### Critical Path

**Blocking dependencies:**
- T5 (task 003) gates everything that depends on A10 working through a connector; a T5 failure stops the project and raises an ADR-028 follow-up amendment.
- T0 gates Phase A's MCP-dependent build (020+); 010, 011 may start in parallel with Phase 0.
- 011 (contract baseline) BLOCKS 017, 013, 014 (no extraction before the baseline is recorded).
- 010 → 012 → 017 → 013 → 014 run serially (shared `Services/Signals/**`); 015 and 016 follow 013.
- 020 BLOCKS 021–025; 021 BLOCKS any tool reaching a live caller.
- 030 BLOCKS 031, 032; 023 BLOCKS 032's lookup.
- 041 (dev deploy) is BLOCKED BY ontology's #2580/#2579 pair deploying and the owner's per-SHA go.
- T1 decides 043 (built only if no widget renders in Chat through the connector).

**High-risk items:**
- A10 OAuth through the connector (T5) — Mitigation: first live test; stop on failure.
- Byte-for-byte invariants through B-1 — Mitigation: 011 contract baseline; ontology review; opus/high on 014.
- `HttpContext` reach beyond the spec's list — Mitigation: B-4 split into 010 + 012.

---

## 4. Phase Breakdown

### Phase 0: Platform spike (001–003)

**Objectives:**
1. Prove A10 OAuth through a federated connector and OBO WhoAmI (T5).
2. Prove a stateless MCP endpoint and widget rendering per route (T0, T1, T2, T13), and record entry-point behaviour (E1–E5).

**Deliverables:**
- [ ] 001 Spike server on `spike/spaarkeai-mcp-phase0` (cut from master, own worktree, not merged): minimal stateless MCP endpoint, one echo tool, a test widget resource; measures T0, T13 (two routes), bundle size and init time at 1/3/6 MB
- [ ] 002 A10 connector client registration (dev, "Spaarke Copilot Connector – dev"), Teams Developer Portal OAuth, connector creation — **owner runs admin steps**; client-secret writes need human confirmation (root CLAUDE.md §9). Covers T5
- [ ] 003 Run and record T0–T14 and E1–E5 in `notes/phase0-results.md` with gate decisions (T5 fail → stop + ADR-028 follow-up; T1 → FR-17 yes/no; T13 → route mechanism)

**Critical Tasks:** 002 (T5) MUST precede any decision that depends on connector auth.

**Inputs**: design §13; ADR-028 A10; `knowledge/mcp-apps/`; dev tenant + owner as Global Admin.

**Outputs**: spike branch (throwaway), `notes/phase0-results.md`, recorded client registration facts (no secrets).

**Dependencies**: owner availability for admin-center steps; deploys of the spike branch only with the owner's per-SHA go in uac-r2's session.

### Phase A: BFF rework (010–017)

**Objectives:**
1. Make the decision slice `HttpContext`-free behind a scoped `ICallerContext` (B-4).
2. Extract one transport-free decision capability and one result mapper with REST byte-for-byte unchanged (B-1, B-2).
3. Add the slim list projection and the workforce lookup search (B-5, B-3).

**Deliverables:**
- [ ] 010 B-4a `ICallerContext` (wraps `Spaarke.Core.Auth.CallerIdentity`; Principal, BearerToken, CorrelationId) populated once per request; `DataverseUserClient` and `CallerWhoAmIMemo` read it. Auth tag: FULL rigor, two adversarial passes, uac-r2 review, task 170's per-commit WhoAmI count watched (opus)
- [ ] 011 Contract-test baseline: record current REST responses of signal list, decision plan and decision commit routes (FR-09 guard) before any extraction
- [ ] 012 B-4b thread `ICallerContext` through `DecisionPlanReadStamp`, `Actions/DecisionParameterSources`, `Actions/DecisionActionExecutors`, `Actions/DecisionRouteCores`, `AssignTaskNotifier`, `DecisionCommitFollowUps`, `Actions/BudgetInquiryExecutor` — no `HttpContext` in decision-slice methods, behaviour unchanged
- [ ] 017 B-2 result mapper: `SignalReadResult` / `DecisionCommitResult` → ProblemDetails or MCP `isError` + `reasonCode`, one vocabulary; every REST reason code has an MCP mapping test incl. `ontology.decision.decided_in_invoice_review`
- [ ] 013 B-1 capability: Decide-lane list + decision plan (HttpContext-free; plan-read stamp unchanged)
- [ ] 014 B-1 capability: commit — invariants byte-for-byte and stated in the PR; D-207 order and replay guard unchanged; ontology review (opus)
- [ ] 015 B-5 `WorklistItemSummary` (keeps `invoiceReview`; adds `PrivilegeFlagged` to the list projection — projection only, read rules unchanged) + paging cursor (FR-05, FR-11)
- [ ] 016 B-3 `IDecisionLookupSearch` + REST route (FR-07): plan-driven entity and scope, D-18 matter scoping, as the caller, capped, fail closed; never the external-plane policy

**Critical Tasks:** 011 MUST precede 017/013/014. 010 → 012 → 017 → 013 → 014 serial.

**Inputs**: `Services/Signals/**`, `Api/Signals/**` (post-merge #2580 code), `notes/pre-pipeline-context.md` §3/§5.

**Outputs**: capability layer in `Services/Signals/`, mapper, `ICallerContext`, slim projection, lookup service + route, KEEP-path tests in `tests/integration/{auth,contract,regression,data-mutation}` and `tests/unit/domain`.

**Dependencies**: ontology review on every PR touching `Services/Signals/**`, `Api/Signals/**` or the worklist route; uac-r2 review on 010.

### Phase B: MCP host and tools (020–025)

**Objectives:**
1. Stand up `/mcp/legal-operations` with A10 auth, kill switch, rate and size limits.
2. Ship the five tools as thin adapters over the capability, with conformance in CI.

**Deliverables:**
- [ ] 020 MCP module: `ModelContextProtocol.AspNetCore` 2.3.1, `Microsoft.Extensions.AI` 10.3.0 → 10.11.x (AI suite regression, NFR-08), `AddMcpModule` / `MapMcpEndpoints` for `/mcp/{area}`, `Mcp:Enabled` null object → 503, `mcp` rate-limit policy, request-size limit, buffering off; publish-size delta recorded
- [ ] 021 A10 auth on `/mcp/*` + B-7 per-tool policies + B-8 ArchTest `MapMcp` guard and `ReadOnly` annotation check. Auth tag: FULL, two adversarial passes, uac-r2's four checks (opus)
- [ ] 022 `spaarke_show_decisions` (closed `category` enum incl. `invoiceDecisions`) + FR-04 privilege reduction + FR-14 text path + app-only paging tool (FR-05)
- [ ] 023 `spaarke_get_decision_plan` (writes the plan-read stamp) + `spaarke_search_decision_lookup` (app-only, readOnly)
- [ ] 024 `spaarke_complete_decision` (app-only write via `DecisionCommitService`; idempotency on `reviewId`; one MCP billing-refusal test; no second check)
- [ ] 025 `modelcontextprotocol/conformance` (`--spec-version 2026-07-28`) in CI with a test-token injector + NFR-01/NFR-02 measurements at 200 items

**Critical Tasks:** 020 BLOCKS 021–025; 021 before any tool is exercised by a live caller.

**Inputs**: Phase A capability and mapper; T0/T13 results.

**Outputs**: `Api/Mcp/` (functions organised as `Api/Mcp/LegalOperations/Decisions/…`), module, tests, CI conformance job.

**Dependencies**: Phase A complete for 022–024; T13 decides route mechanism (fallback: one route filtering tools by path).

### Phase C: Widget (030–032)

**Objectives:**
1. Build the single-file widget and embed it as `ui://spaarke/decisions.html`.
2. Render the Decide lane and the Decision wizard through MCP fetch shims, with the Console unchanged.

**Deliverables:**
- [ ] 030 Scaffold `src/client/widgets/spaarke-decisions/` (Vite + `vite-plugin-singlefile`, `@modelcontextprotocol/ext-apps` 2.0.4, MCP fetch shims returning `Response`, host theme), BFF embedded resource + `resources/read` (`text/html;profile=mcp-app`, empty `_meta.ui.csp`), CI build step ahead of BFF publish + bundle-size test (NFR-03)
- [ ] 031 Decide lane: shared-lib single-lane / paging seams (Console unchanged), category cards, rows grouped by matter, Start review across pages; billing rows link out per `openInvoiceReview.ts` rules with "Finding" wording and no decide action
- [ ] 032 Decision wizard: all parameter kinds, lookup type-ahead via 023, `because` (FR-15), in-frame dialog → fullscreen master-detail → `openLink` fallbacks; server refusals shown in the wizard

**Critical Tasks:** 030 BLOCKS 031/032; 023 BLOCKS 032's lookup.

**Inputs**: Phase B tools; shared-lib Worklist and DecisionWizard components; `knowledge/mcp-apps/`.

**Outputs**: widget source, adapter and flow tests (mocked `callServerTool`), bundle-size test, `<ui-tests>` per task.

**Dependencies**: Phase 0 size/init result; WorklistLanePanel standalone mount (fallback: single-lane prop on `WorklistWidget`).

### Phase D: Registration, deploy and live E2E (040–043)

**Objectives:**
1. Package and register Spaarke in the dev tenant with repeatable runbooks.
2. Prove the full flow live in Cowork and Chat with a non-admin user.

**Deliverables:**
- [ ] 040 Plugin package (`src/solutions/CopilotPlugins/spaarke/`, app manifest 1.30, `agentConnectors`) + `spaarke-decisions` skill + skill-name build gate `^spaarke(-[a-z0-9]+)+$` (≤ 64 chars)
- [ ] 041 Dev BFF deploy — **gated**: only after ontology's #2580/#2579 pair has deployed and the owner gives a per-SHA go
- [ ] 042 Connector + runbooks finalised; live E2E in Cowork and Chat with a licensed non-admin user; 20-prompt routing eval; NFR-01 latency over 50 calls each
- [ ] 043 FR-17 Spaarke AI agent fallback (DA v1.9 / plugin v2.4 / manifest 1.30, one MCP action, OpenAPI action removed) — **conditional on T1**; cancelled if T1 passes

**Critical Tasks:** 041 gate; owner runs the admin-center steps (Global Admin).

**Inputs**: Phases B and C; Phase 0 registration facts.

**Outputs**: package, runbooks (`docs/guides/`), live-run evidence (record ids), routing-eval rate.

**Dependencies**: deploy gate; MC1493223 for `/` skills in Chat; dev-tenant seed data (≥ 200 Decide-lane items across ≥ 3 matters incl. privileged and every lookup-bearing action type).

### Phase E: Docs and terminology (050–052)

**Objectives:**
1. Record product names and naming conventions where sessions read them.
2. Leave an architecture doc, a how-to guide and a connector runbook that name real files.

**Deliverables:**
- [ ] 050 Root CLAUDE.md §1.1 row "Spaarke for Microsoft 365 Copilot" / "Spaarke" citing `ontology-component-model.md` §1.1 + `.claude/CHANGELOG.md` entry with byte delta + 200-line / 30 KB budget + `ai-procedure-maintenance` Checklist F — **main session only**
- [ ] 051 Rewrite `mcp-tool-handler` (tool naming, real conventions); update `widget-design` (URI, folder, size) and `declarative-agent` (current schemas) — **main session only**
- [ ] 052 MCP architecture doc (`docs/architecture/`), "how to add a tool" guide and connector runbook (`docs/guides/`); `doc-drift-audit` clean

**Inputs**: naming note §1; implemented code. **Outputs**: updated root CLAUDE.md, skills, docs.

**Dependencies**: Phases B–D substantially complete so docs name real files.

### Wrap-up (090)

- [ ] 090 `/test-diet` project-close reconciliation, lessons learned, `notes/defer-issues.md` reconciled, project CLAUDE.md pruned, durable lessons promoted.

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Microsoft 365 admin center (Copilot › Connectors) + Teams Developer Portal OAuth | GA | Medium | Owner runs steps; runbook each; record whether scriptable |
| `ModelContextProtocol.AspNetCore` 2.3.1 | GA | Low | Pin; measure publish size |
| `@modelcontextprotocol/ext-apps` 2.0.4 | GA | Medium | Samples use 1.x — check API drift in 030 |
| `modelcontextprotocol/conformance` suite | GA | Low | Test-token injector in CI |
| MC1493223 (skills in Chat), MC1476316 (connector writes) | Rolling out / GA | Medium | Cowork first; re-check at Ignite (17–20 Nov 2026) |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| ADR-028 A10 | `.claude/adr/ADR-028-spaarke-auth-architecture.md` | On this branch |
| Ontology #2580 / #2579 | merged into this branch 2026-11-06 | Code present; deploy gate open |
| Decision domain services | `src/server/api/Sprk.Bff.Api/Services/Signals/` | Production |
| Worklist / DecisionWizard components | `src/client/shared/Spaarke.UI.Components/src/components/` | Production |
| Dev BFF + dev tenant | Azure App Service / Entra | Ready (owner per-SHA go) |

---

## 6. Testing Strategy

**Unit tests** (`tests/unit/domain/**`): capability as-caller pass-through, reason-code mapping (every REST code has an MCP mapping), lookup scoping (budget belongs to the matter), slim projection ≤ 600 bytes per row and < 64 KiB per page at 200 items, privilege reduction.

**Integration tests** (`tests/integration/{auth,contract,regression,data-mutation}/**`):
- Contract: signal list / plan / decision REST responses byte-for-byte before and after (011 baseline).
- Auth: anonymous 401 with `WWW-Authenticate`; CIAM 401; A9 home-directory 401 before any tool runs; no-`systemuser` → `UserContextRequired`; no app-only fallback on failed OBO; must-fire route guard on an unauthenticated `/mcp/{area}`.
- Protocol: in-process host — tool list and annotations, `resources/read` MIME type, 406 without the dual `Accept`, 404 for unmapped area, 503 with `Mcp:Enabled=false`, 429 over the limit.
- Data mutation: one completion → one Decision Record + Signal closed; replay returns the first outcome; no plan read → refused; billing Signal → mapped D-207 refusal, nothing written. Existing #2580 tests (Task174_*) stay green, including the one-batched-`BillingEventRead` count.
- Arch: `MapMcp` covered by `RouteAuthorizationGuardTests`; every tool has explicit `ReadOnly`; no AI-internal types injected.
- AI suites for the `Microsoft.Extensions.AI` bump (NFR-08).

**Widget tests**: shim ↔ tool-result adapters, wizard flow with mocked `callServerTool`, loading/empty/error states, bundle-size budget; existing shared-lib Worklist and DecisionWizard tests unchanged.

**E2E / live (dev tenant)**: Phase 0 T0–T14, E1–E5; end-to-end completion per route (Cowork, Chat); 20-prompt routing eval; NFR-01 latency; Console smoke against dev.

---

## 7. Acceptance Criteria

### Technical Acceptance

**Phase 0:**
- [ ] Every T0–T14 / E1–E5 test has a dated result and decision in `notes/phase0-results.md` (FR-19)

**Phase A:**
- [ ] No decision-slice service method takes `HttpContext`; missing caller context fails closed with `UserContextRequired` (FR-10)
- [ ] REST contract tests pass unchanged; every reason code has an MCP mapping test (FR-09)
- [ ] List row ≤ 600 bytes at seeded maximum field lengths; Console list unchanged (FR-11)
- [ ] Lookup negatives: unknown `parameterCode` → `isError`; no other-matter budget; no unreadable record; no external-plane policy (FR-07)

**Phase B:**
- [ ] `tools/list` returns exactly the v1 tools; 404 / 406 / 429 / 503 behaviours hold (FR-01)
- [ ] Auth negatives and uac-r2's four checks pass (FR-02)
- [ ] Privileged marker absent from model-visible output; count still correct (FR-04)
- [ ] Paging returns every item exactly once in Console order; stale cursor safe; every result < 64 KiB at 200 items (FR-05, NFR-02)
- [ ] Conformance suite green in CI (NFR-09)

**Phase C:**
- [ ] Widget renders the lane with Console counts; loading/empty/error states; billing rows link out only (FR-12)
- [ ] Wizard resolves each lookup-bearing action type; refusal shown and item left open; `because` stored (FR-13, FR-15)
- [ ] Bundle-size test enforces < 3 MB (aim < 1 MB) (NFR-03); Fluent tokens only, light/dark, keyboard (NFR-07)

**Phase D:**
- [ ] Non-admin user connects once and completes a Work Item in Cowork and Chat; runbooks reproduce setup (FR-16)
- [ ] FR-17 built only if T1 says so; OpenAPI action removed if built

**Phase E:**
- [ ] `doc-drift-audit` clean on new docs; root CLAUDE.md within budget with CHANGELOG entry (FR-18)

### Business Acceptance

- [ ] A licensed non-admin user resolves a Work Item without leaving Copilot/Cowork, and a Decision Record is written (record id captured)
- [ ] A direct prompt without the skill routes to `spaarke_show_decisions` (routing-eval rate recorded)

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | No widget through the connector in Copilot Chat (documented only for agents) | Med | High | FR-17 Spaarke AI fallback; FR-14 text path; re-check at Ignite (17–20 Nov) |
| R2 | OAuth from a customer-tenant connector to a Spaarke-tenant authority not supported (T5) | Low | High | T5 before any build; stop + ADR-028 follow-up; Entra SSO stays possible for Model 2 |
| R3 | Wizard bundle (Fluent, Lexical `EmailComposer`) exceeds size or init budget | Med | Med | Deep imports, bundle-size test, `decisionDraft.ts` re-render fallback |
| R4 | Hosts ask for write consent inconsistently or not at all | Med | Low | Server never relies on host consent (NFR-06) |
| R5 | `Microsoft.Extensions.AI` bump regresses the AI pipeline | Low | High | Run AI unit + integration suites before merge; pin; measure |
| R6 | First of its kind: no public .NET + Entra + widget + connector sample | Med | Med | Phase 0; adopt PnP `mcp-cs-customers-secure-server` auth pattern (SDK 1.2.0 → 2.3.1) |
| R7 | Contention on `AuthorizationModule.cs` and `Services/Signals/**` | Med | Med | `/conflict-check`, small PRs; ontology and uac-r2 reviews |
| R8 | #2580 held on ebilling's Invoice Review deployment | High | Med | Code merged into this branch 2026-11-06; only the deploy gate remains (R11) |
| R9 | A second MCP server proposed elsewhere (`Sprk.Insights.Mcp`) | Low | Med | Any further MCP area is a new `/mcp/{area}` on this host; raise if that project starts |
| R10 | `HttpContext` reaches further than the spec's list (`DecisionCommitService` helpers, `DecisionPlanReadStamp` L42/L86, `DecisionParameterSources` L109/L404, `DecisionActionExecutors` record field `HttpContext Http` L73, `DecisionRouteCores` ×7, `AssignTaskNotifier` ×5, `DecisionCommitFollowUps` L70, `BudgetInquiryExecutor` via `IHttpContextAccessor`) | High (confirmed) | Med | B-4 split: 010 (context + Dataverse client) then 012 (thread through executors, route cores, notifier); behaviour unchanged; two adversarial passes |
| R11 | Deploy gate: no dev BFF deploy from this branch until ontology's #2580/#2579 pair has deployed | High | Med | Phase 0 on master-based `spike/spaarkeai-mcp-phase0`; Phases A–C build and test locally; 041 waits; if earlier deploy is forced, back the merge out first |
| R12 | Privilege column gap: worklist rows (`SignalReadService`) do not select `sprk_privilegeflagged` | High (confirmed) | High | 015 (B-5) adds the column to the list projection only; read rules unchanged; FR-04 negative test |
| R13 | `ext-apps` 2.0.4 API drift from the 1.x samples | Med | Low | Verify in 001/030 against the package types |

---

## 9. Next Steps

1. Run `task-create` for `projects/spaarkeai-mcp-bff-api-r1` (pipeline Step 3), then the Completeness Lint (`pwsh scripts/Validate-TaskPoml.ps1 projects/spaarkeai-mcp-bff-api-r1/tasks`).
2. Pipeline Step 4: commit, append the `projects/INDEX.md` row, push, register under Epic #421.
3. Wave 1 (parallel-safe): 001 (spike branch, own worktree), 010, 011; owner steps 002/003 alongside.

---

## 10. References

- Spec and design: `spec.md`, `design.md` (§0 O-1–O-26, §8 B-1–B-10, §13 Phase 0, §20 risks)
- Coordination: `notes/coordination/2026-11-06-copilot-naming-and-ownership.md`; `notes/pre-pipeline-context.md` (#2580/#2579 facts and impact)
- Research: `notes/mcp-project-research-starter.md`, `notes/copilot-as-spaarke-surface-2026-11-05.md`; researcher notes `.claude/agent-memory/researcher/mcp-server-dotnet-bff-copilot-widget-2026-11-05.md`, `federated-connector-widgets-writes-2026-11-05.md`, `copilot-as-spaarke-surface-and-frontier-2026-11-05.md`
- Constraints: `.claude/constraints/bff-extensions.md`, `.claude/constraints/auth.md`, `.claude/constraints/azure-deployment.md`, `.claude/constraints/agent-cost.md`
- Architecture: `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md`; terminology `projects/spaarke-ontology-platform-r1/notes/ontology-component-model.md` §1.1, §3
- Samples (MIT): PnP `mcp-cs-customers-secure-server`, `microsoft/DataFactory.MCP`, Copilot Studio Kit `mcp-apps/copilot-agent-kit`, `microsoft/power-platform-skills` `plugins/mcp-apps`, `modelcontextprotocol/conformance`

---

**Status**: Ready for Tasks
**Next Action**: task-create (pipeline Step 3)

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks.*
