# Spaarke AI Architecture Redesign R1 - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-07-05
> **Source**: `design.md` v1.1 (operator-reviewed ×3 rounds) — which summarizes the
> canonical target `docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md` **v0.4**
> and `notes/audit-inputs/SPAARKE-AI-MIGRATION-MAP.md` v1.0. Where this spec and those
> documents disagree, the canonical doc + migration map govern.

## Executive Summary

Deliver a **working Spaarke Assistant**: a legal-ops user drops in a document or types a
plain-language request and reliably gets analysis, cited answers over documents AND Dataverse,
records created with confirmation, and drafts — composing across steps in one conversation.
Simultaneously deliver the **second product**: after this project, a new AI capability is a
catalog row a business analyst authors (prompt + schema + binding + eval case, zero deploys),
not an engineering project. The implementation replaces ten accreted dispatch mechanisms and
four routing surfaces with three entry paths (Event / Click / Text) over two closed catalogs,
adds the session ledger (the composition carrier the estate never had), and deletes the
audited deadwood. All architecture decisions are pre-ratified — this project implements;
it does not re-open design.

## Scope

### In Scope
- Five phases P0-P4 per the migration map (hard cutover per surface; no parallel-run, no compat shims).
- **Track B deadwood sweep, continuous from P0**: ALL dead technical debt in the inventory §9
  register — including code with no relationship to the target design; every entry ends
  grep-verified-deleted or carries a written keep-with-reason.
- Proving capabilities: classify, chat-summarize, matter/project pre-fill, document-profile,
  workspace summarize, email-analysis, Daily Briefing (first coded composite), Insights
  ask/search bindings, **draft-correspondence**, **create-task**.
- Portfolio reconciliation (close/re-scope R7; re-point R4 / Action Engine R1 / insights-r3 triggers).
- ADR-039/040 promotion to Accepted (at P1/P0); ADR A-3 minor refreshes (at P4).

### Out of Scope
- Deep legal capabilities beyond the proving set (contract full-review, NDA clause review,
  redlining, invoice validation) — post-platform catalog rows; redline write-shape tooling is follow-on.
- Runtime Dataverse MCP transport (pending the P0-08 spike; contracts are swap-ready regardless).
- Relocating the agent loop into Azure AI Foundry Agent Service (researched, rejected — wrong
  user-context model for headless multi-tenant BFF).
- Re-migrating the frozen Insights engine pipelines; any maker-facing graph authoring.
- New Dataverse tables for the manifest ("Capability" is vocabulary, not schema).
- Assistant-initiated email SEND (draft-only this project — see Assumptions).
- Admin observability dashboards (audit-trail UI, cost dashboards, refusal-backlog view) —
  named deferral, filed via `/defer` at P4.

### Affected Areas
- `src/server/api/Sprk.Bff.Api/Services/Ai/**` — executor, loop, gate, router, tools, ledger services
- `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/**` — ChatSession ledger model
- `src/server/api/Sprk.Bff.Api/Api/Ai/**` — endpoints (ChatEndpoints slims; SummarizeSessionEndpoint delegates)
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/**` + `Program.cs` — registration hygiene
- `src/client/shared/Spaarke.UI.Components/**` (SprkChat, hooks) + `Spaarke.AI.Widgets/**`
- `src/solutions/SpaarkeAi/src/**` (ConversationPane decomposition, one dispatch helper) +
  LegalWorkspace/Compose summarize call-sites
- `src/client/code-pages/PlaybookBuilder/**` + `src/client/pcf/ScopeConfigEditor/**` (BA authoring surfaces, P4)
- Dataverse: `sprk_analysisaction`, `sprk_playbookconsumer`, `sprk_analysistool` column extensions (spaarkedev1)
- `tests/unit/Sprk.Bff.Api.Tests/**` + `tests/integration/contract/**` (eval suite)
- Track-B delete targets across `src/**`, `scripts/**`, `docs/data-model/**`, `.claude/catalogs/**`

## Requirements

### Functional Requirements

**Phase P0 — Foundations (dark; engineering gate)**

1. **FR-P0-01**: Extend `ChatSession` with typed ledger entries (`Outputs` as `SessionOutput`
   records keyed `{bindingId}@t{n}`, `ToolChains`, `WidgetEvents`, `Gates`) persisted through
   Redis + Cosmos; fix the Cosmos mapping that drops file references. -
   Acceptance: ledger round-trip test (write → Redis → Cosmos-restore) passes incl. file refs; zero readers yet.
2. **FR-P0-02**: Generalize session digest compaction to cover outputs (extend `ChatHistoryManager`). -
   Acceptance: digest includes output summaries; existing compaction tests pass.
3. **FR-P0-03**: Catalog schema extensions — `sprk_analysisaction` (+`sprk_kind`,
   `sprk_workflowclass`, `sprk_inputschema`, `sprk_modeltier`); `sprk_playbookconsumer` (+
   `sprk_ucid`, `sprk_tooldescription`, `sprk_disposition`, `sprk_chiptransitions`, `sprk_risk`,
   `sprk_capturemode`, `sprk_oneventbindings`, `sprk_surfaces`, model override);
   `sprk_analysistool` (+`sprk_toolid`, `sprk_namespace`, `sprk_outputschema`,
   `sprk_sideeffectclass`, `sprk_permissionscope`, `sprk_budgetclass`). -
   Acceptance: columns deployed to spaarkedev1; `ConsumerRoutingService` returns the full Binding contract.
4. **FR-P0-04**: Boot reconciliation — extend `RoutingConsumerTypeHealthCheck` to verify
   `ConsumerTypes` constants ↔ Binding rows and tool row ↔ handler bijection. -
   Acceptance: drift fails startup health check (proven by test).
5. **FR-P0-05**: Registration hygiene — move `PlaybookLookupService` + `OutputOrchestratorService`
   out of FinanceModule into AI modules with Null-Object peers; LinearConsumers under the
   compound AI gate. - Acceptance: services unresolvable when `Analysis:Enabled=false`; Null peers return 503 pattern.
6. **FR-P0-06**: `ICodedWorkflow` registration convention (assembly-scan discovery mirroring
   tool handlers); retrofit `DailyBriefingNarrator`/`Collector` as first instances (no behavior change). -
   Acceptance: workflow resolvable by class ref from an Action row; narrator tests unchanged.
7. **FR-P0-07**: `dataverse.*` typed handlers (`describe`, `read_query`, `search_data`,
   `create_record`, `update_record`, `delete_record`) over BFF-OBO Web API, contracts mirroring
   the GA Dataverse MCP tool surface. - Acceptance: handler tests under a test user's security
   context; names frozen against GA MCP list.
8. **FR-P0-08**: OBO spike — confidential BFF client OBO-exchange for delegated
   `Dynamics CRM/mcp.tools` scope against `/api/mcp`. - Acceptance: documented pass/fail result
   in project notes (decides per-tool transport option; not a blocker either way).
9. **FR-P0-09**: Golden-utterance eval-suite scaffold at `tests/integration/contract/` with ~30
   seed utterances from §3 UC triggers. - Acceptance: suite runs in CI; failure blocks merge (wired from P1).
10. **FR-P0-10**: Verify every flow the tool plane exposes runs user-OBO (no app-only Dataverse
    path reachable from AI). - Acceptance: audit note with per-flow evidence.
11. **FR-P0-11**: Portfolio reconciliation — close/re-scope `spaarke-ai-platform-unification-r7`
    (absorbed waves documented); re-point R4 daily-update graduation gate + Action Engine R1 +
    insights-engine-r3 resumption triggers to this project's phases; file Action Engine re-based
    spec stub. - Acceptance: project files + portfolio issues updated; triggers name this project.

**Phase P1 — First capability end-to-end (gate G-P1, browser)**

12. **FR-P1-01**: `chat-summarize` as Action row (`kind: prompted`, SUM-CHAT@v1 schemas) +
    Binding row; executed via the prompted executor (`ActionRunner` + `PromptSchemaRenderer`);
    `SummarizeSessionEndpoint` delegates; `SessionSummarizeOrchestrator` dual-path dissolves. -
    Acceptance: summarize runs via catalog; orchestrator dual-path code deleted.
13. **FR-P1-02**: Universal ledger write before rendering + `OutputRouter` disposition routing
    (informational path). - Acceptance: every execution produces an addressable `SessionOutput`;
    render follows store (test-proven ordering).
14. **FR-P1-03**: Event Rules service (thin) + `document_uploaded → [classify(1), summarize(2)]`
    binding with bounds (per-user daily cost cap, opt-out, bulk top-1 + "summarize all?" chip,
    explicit-command supersede) and classify-confidence M4 policy. - Acceptance: upload with no
    typed command yields classification + summary + chips in the UI.
15. **FR-P1-04**: Click path — chips carry `binding_id`; ONE client `dispatchConsumer(bindingId,
    args)` helper (SSE→PaneEventBus inside); `executeSummarizeIntent` + `intentMatcher` migrate
    onto it and are deleted. - Acceptance: chip click end-to-end; grep-zero for the two deleted modules.
16. **FR-P1-05**: Engine-output→ledger adapter (E-2) — frozen Insights composite outputs write
    `SessionOutput` entries. - Acceptance: an insights run produces addressable ledger output.
17. **FR-P1-06**: r7 tactical branch closed WITHOUT merging the dispatch patches — keep
    session-id fix, ExtractedText persistence, auto-promote, field_delta synthesis; drop
    `TryDetectExplicitConsumerType` regex + `linear_dispatch` SSE set + `executeLinearDispatch.ts`
    + the diagnostic log; the empty-attachments guard becomes an Event/Click precondition. -
    Acceptance: r7 branch merged/closed with exactly that content; grep-zero for `linear_dispatch`.
18. **FR-P1-07**: Eval suite UC-A-1 utterance family green; **ADR-039 status → Accepted**. -
    Acceptance: CI green; ADR status updated with citation.

**Phase P2 — Text-path hard cutover (gate G-P2, browser)**

19. **FR-P2-01**: Agent-turn loop contract on the `SprkChatAgent` stack — per-turn tool budget
    (default 8), capability-tools projection from the catalog, deterministic session-context
    pre-filter of the tool list, citation enforcement on reads, `ToolChain` ledger persistence. -
    Acceptance: loop tests prove budget/filter/cite/persist; factory line-count shrinks.
20. **FR-P2-02**: Confirmation Gate unification (D12) — `PendingPlanManager` store generalized to
    THE pending store; `/actions/{id}/confirm` second store deleted; FR-48 must-click becomes gate
    presentation; gating driven by `side_effect_class` + Binding `risk` (hardcoded tool-name lists deleted). -
    Acceptance: one store (grep-proven); write tools suspend/resume through it.
21. **FR-P2-03**: Loop-native elicitation — missing required args produce a clarifying turn;
    `capture_mode: modal` routes to the wizard surface; ledger `Gate` markers track in-flight
    invocations; mid-elicitation utterances parse as answers unless hard-slash/restart. -
    Acceptance: walkthrough steps 10-12 semantics test-proven.
22. **FR-P2-04**: Honest refusal — per-tenant `no_match_handler` Binding renders the refusal;
    `dispatch_refused` telemetry emitted. - Acceptance: off-catalog utterance yields the tenant
    template + the event in App Insights.
23. **FR-P2-05**: **Hard cutover of chat NL to the loop**; the four retained soft slashes map to
    deterministic direct invocations; `intentHint` plumbing retired. - Acceptance: no chat
    utterance reaches any legacy dispatcher (telemetry-proven over the UAT window).
24. **FR-P2-06**: Dispatcher-stack deletion in the SAME phase — `PlaybookDispatcher` (+ playbook-
    embeddings index jobs), `IntentRerankerService`, `PlaybookCandidateSelector`,
    `CompoundIntentDetector`, plus their tests. - Acceptance: grep-zero outside git history.
25. **FR-P2-07**: Legacy `Chat/Tools/*` deletion after `AnalysisExecutionTools` +
    `TextRefinementTools` migrate to typed handlers; handler ids re-namespaced per tool contract. -
    Acceptance: directory removed; handlers cover the two migrated capabilities.
26. **FR-P2-08**: Eval suite covers full catalog families + refusal + compound + prompt-injection
    cases. - Acceptance: CI green incl. injection cases (hostile-document scenarios do not
    trigger ungated side effects).

**Phase P3 — Consumer + client consolidation (gate G-P3, browser)**

27. **FR-P3-01**: Remaining consumers as Bindings — `document-profile`, `matter-pre-fill`,
    `project-pre-fill`, workspace `summarize-file`, `email-analysis`, Insights `ask`/`search`;
    the `LinearConsumers`, `Workspace.*PlaybookId`, `Insights.Playbooks.Map` appsettings blocks
    deleted (single-routing-surface rule). - Acceptance: all routes resolve via the Binding
    table; grep-zero for the three config keys.
28. **FR-P3-02**: **`draft-correspondence` capability** — prompted Action; `email.draft`
    write-shape delegates to **Spaarke's Communication (Email) service via Graph API** (owner
    clarification — NOT Outlook drafts): output is a Spaarke communication draft record rendered
    for user review; DRAFT-ONLY (send remains user-initiated through the Communication service);
    gated per `side_effect_class: communicate`. - Acceptance: G-P3 script step "draft the client
    letter" passes in the browser referencing the real summary + matter from the ledger.
29. **FR-P3-03**: **`create-task` capability** — prompted Action → `dataverse.create` of
    `sprk_event (type=task)` under the conversational-confirm gate policy; record carries ledger
    refs (source document + source analysis). - Acceptance: walkthrough steps 10-14 pass in the browser.
30. **FR-P3-04**: Daily Briefing as the first full `coded` composite Action; `/narrate`
    engine-default and `Features:NarrateUseCodeBasedNarrator` flag deleted (Binding decides). -
    Acceptance: briefing renders + emails via the coded path; flag grep-zero.
31. **FR-P3-05**: Engine-shell deletions — `PlaybookExecutionEngine`, `AnalysisOrchestrationService`
    legacy path (R7 FR-11), `SessionSummarizeOrchestrator` remnants; `FileSummarizeService`/
    `DocumentProfileService` wrappers absorbed into the executor path. - Acceptance: grep-zero;
    callers re-pointed; frozen engine (`PlaybookOrchestrationService` + nodes) untouched.
32. **FR-P3-06**: Client consolidation — `ConversationPane` decomposes to thin host + the shared
    helper; LegalWorkspace `summarizeService` + Compose `executeComposeSummarize` migrate to the
    helper (hand-rolled SSE parsers deleted); duplicated chat-hook triples become re-exports;
    wizard/launcher widgets carry binding ids. - Acceptance: one SSE parse path client-wide
    (canonical `useSseStream`); ConversationPane under agreed size budget.
33. **FR-P3-07**: Widget layer — dedupe the two `register-context-widgets.ts`;
    `ExecutionTraceWidget` bridge renders ledger `ToolChain` entries; legacy `FieldDelta`
    dual-render path deleted at the last-playbook cutover (per amended ADR-037). -
    Acceptance: trace widget shows a real chain; FieldDelta grep-zero in the widget.
34. **FR-P3-08**: Work-product record persistence generalized from the widgets-r1 pattern —
    Binding-declared persistence of outputs to host Dataverse records. - Acceptance: a
    work_product capability persists its envelope to the host record (test + UAT).

**Phase P4 — Sweep completion + hardening + graduation (gates G-P4 + G-M)**

35. **FR-P4-01**: Track-B completion audit — every inventory-§9 + overlay-DEL item
    grep-verified deleted or carries a written keep-with-reason. - Acceptance: audit table in
    project notes; zero unexplained survivors.
36. **FR-P4-02**: Catalog governance — single refreshed `scope-model-index.json` (docs twin
    deleted); `Seed-PlaybookConsumers.ps1` regenerated from the table; `sprk_nodetype` option-set
    gap resolved-or-documented for the frozen engine. - Acceptance: one catalog copy; seed
    round-trips.
37. **FR-P4-03**: Documentation — new data-model docs for the three extended tables + frozen
    `sprk_playbooknode`; `docs/data-model/INDEX.md` reconciled; consumer-wiring guide →
    capability-wiring; ADR A-3 minor refreshes (033/034/010/016/018/038). -
    Acceptance: 2026-02 ERD docs deleted + replaced; doc-drift-audit clean.
38. **FR-P4-04**: PlaybookBuilder canvas de-scope → BA scope/prompt/binding editor;
    `ScopeConfigEditor` Binding-editor variant; `AiPlaybookBuilderService` retargeted to
    Action/Binding authoring. - Acceptance: BA can author Action + Binding end-to-end in the UI.
39. **FR-P4-05**: Per-tenant metering — counters (turns, tool calls, tokens, capability
    invocations) per tenant/user to App Insights + documented KQL query pack. -
    Acceptance: KQL pack returns per-tenant rollups in dev.
40. **FR-P4-06**: BFF publish-size + CVE verification; ADR-029 baseline update (net reduction
    expected). - Acceptance: size + diff reported; no new HIGH CVEs.
41. **FR-P4-07**: Wrap-up — G-M maker gate executed (see Success Criteria); `/test-diet`;
    named deferrals filed via `/defer` (admin observability dashboards; assistant-initiated
    send; `/goal` skill promotion if piloted successfully); ADR-040/039 status verified Accepted;
    audit + this project graduate. - Acceptance: wrap-up checklist complete.

**Track B (continuous from P0)**

42. **FR-TB-01**: Sweep-as-you-go — the migration map's dependency-free delete list (~30 items:
    DirectOpenAiAgent cluster, Insights renderer cluster, dead PCF dirs, R1 client
    registries/providers/cross-pane, stale catalogs/seeds/docs, etc.) executed in batches from
    P0 onward. - Acceptance: per-batch grep-zero for deleted symbols + green builds, evidence shown.

### Non-Functional Requirements

- **NFR-01 (publish size)**: per-task verification per ADR-029; ceiling ≤60 MB compressed;
  project expectation is NET REDUCTION; ≥+2 MB single-task delta requires justification.
- **NFR-02 (eval gate)**: golden-utterance suite green is a MERGE GATE from P1 onward; every
  catalog/prompt change adds-or-updates eval cases.
- **NFR-03 (prompt-injection threat model)**: uploaded-document text and tool results are
  UNTRUSTED input to the text path; `AgentContentSafetyMiddleware` coverage verified on the loop
  path; injection scenarios in the eval suite; the confirmation gate is the last line, not the only one.
- **NFR-04 (latency)**: upload→first-summary-token ≤5s p50 / ≤10s p95; text-turn TTFB ≤3s p50
  (owner-set). Prompt caching REQUIRED for the capability-tool projection; targets verified at
  each browser gate on spaarkedev1.
- **NFR-05 (metering)**: per-tenant/per-user usage counters + App Insights telemetry + KQL pack
  (FR-P4-05); billing/pricing surface is a named follow-on.
- **NFR-06 (output quality)**: eval suite includes per-capability schema-conformance +
  citation-integrity assertions — a prompt edit that degrades output fails CI, not UAT.
- **NFR-07 (data governance)**: ledger entries mapped to ADR-015 tiers (ledger = Tier 3
  user-owned/GDPR-erasable; ToolChain = identifiers/filters/counts only); no content in logs.
- **NFR-08 (hard-cutover proof)**: each phase's retirements verified grep-zero (shown output);
  no parallel-run periods; no compat shims retained.
- **NFR-09 (budgets)**: per-turn tool-call cap (default 8) + per-user daily Event-path budget
  enforced + telemetered (ADR-016).
- **NFR-10 (wave /goal conditions)**: every generated wave definition (plan.md section +
  TASK-INDEX header) includes a pre-authored `/goal` condition (shown evidence + scope bind +
  turn cap + "Step 9.5 gates passed"). Project-scoped pilot; no skill changes.
- **NFR-11 (browser rule — BINDING)**: every G-gate UAT script is executed by a user in the
  Spaarke UI with rendered results; curl/tests/logs never satisfy a gate; P0 is the sole
  declared engineering-gated phase.

## Technical Constraints

### Applicable ADRs
- **ADR-039** (Grounded Execution & Closed Catalogs — Proposed→Accepted at P1): one dispatch
  protocol; two closed catalogs; grounded outputs; control-flow-is-code.
- **ADR-040** (Session Ledger — Proposed→Accepted at P0): storage precedes rendering;
  addressable outputs; disposition is the only rendering contract.
- **ADR-013 (amended 2026-07-05)**: capability invocation is the canonical facade verb;
  `IInvokePlaybookAi` legacy shim only; PublicContracts boundary unchanged.
- **ADR-037 (amended 2026-07-05)**: section-name-keyed streaming contract binds any composite
  executor; DeliverComposite frozen; FieldDelta deletable at cutover.
- Standing: ADR-001, 004, 008, 009, 010, 014, 015, 016, 018, 019, 028, 029, 030, 031, 032, 036, 038.

### MUST Rules (distilled)
- ✅ MUST route every AI invocation through Event / Click / Text; ✅ MUST write every output +
  tool chain to the ledger before rendering; ✅ MUST gate side effects via the ONE gate by
  declared `side_effect_class`; ✅ MUST keep both catalogs closed; ✅ MUST run user-OBO for all
  Dataverse tool access; ✅ MUST ship Null-Object peers for gated registrations (ADR-032);
  ✅ MUST keep new composites as `coded` workflows.
- ❌ MUST NOT add a second intent-detection mechanism anywhere; ❌ MUST NOT add routing config
  outside the Binding table; ❌ MUST NOT gate by tool-name lists; ❌ MUST NOT land new capability
  on the frozen engine; ❌ MUST NOT emit ungrounded free-form output; ❌ MUST NOT create new
  manifest tables; ❌ MUST NOT retain compat shims past a surface's cutover.

### Existing Patterns to Follow
- Prompted executor: `src/server/api/Sprk.Bff.Api/Services/Ai/LinearConsumers/` (ActionRunner + PromptSchemaRenderer)
- Coded workflow shape: `Services/Ai/Narrators/DailyBriefingNarrator.cs` (Wave-11 pattern)
- Tool framework: `Services/Ai/Handlers/` + `ToolHandlerToAIFunctionAdapter` + `sprk_analysistool` discovery
- Gate store: `Services/Ai/Chat/PendingPlanManager.cs` (generalize)
- Client: canonical `useSseStream`, `PaneEventBus`, widget registries, `StructuredOutputStreamWidget`
- Record-persisted outputs: widgets-r1 topic-registry pattern (`InsightSummaryCard` + `sprk_aitopicregistry`)
- Per-component verdicts: `notes/audit-inputs/OVERLAY-MATRIX.md` (approved) is the HOW for every slot.

## ADR Tensions (per CLAUDE.md §6.5 — MANDATORY)

> No ADR tensions surfaced at design time. The two conflicts this design would have raised were
> resolved via operator-approved Path-B amendments BEFORE project start (ADR-013 capability-verb
> amendment; ADR-037 engine-steering rescind — see `notes/audit-inputs/ADR-REVIEW-VS-GREENFIELD.md`
> §5, applied 2026-07-05). All listed ADRs apply without exception. This section updates if
> tensions emerge during implementation.

## Success Criteria

1. [ ] **G-P0 (engineering)**: ledger round-trip incl. file refs; health checks green; schema
   deployed; ADR-040 Accepted. - Verify: integration tests + startup logs.
2. [ ] **G-P1 (browser)**: user uploads a file, types nothing, sees classification + summary +
   working chips; any phrasing/typo of "summarize" works; ADR-039 Accepted. - Verify: operator
   UAT script on spaarkedev1.
3. [ ] **G-P2 (browser)**: four-outcome text path (do / clarify-then-do / cited ad-hoc answer over
   docs+Dataverse / honest refusal); session memory ("email that summary to John" resolves the
   ledger ref); writes confirm before executing. - Verify: operator UAT script.
4. [ ] **G-P3 (browser)**: the flagship journey as ONE conversation (upload → auto-summary →
   clause chat → pre-filled matter wizard → confirm → client-letter draft in the Communication
   service referencing real ledger outputs → follow-up task with ledger refs); daily briefing
   email arrives; identical behavior on record form, workspace, SPA. - Verify: operator UAT script.
5. [ ] **G-P4**: everything above reliable + telemetered; Track-B audit zero unexplained
   survivors; publish size reduced. - Verify: NFR checks + audit table.
6. [ ] **G-M (maker gate)**: a business analyst authors a brand-new small capability entirely as
   data (capability chosen by operator at P4; JPS prompt + schema + Binding + chips + eval case;
   ZERO deploys) and a user invokes it in the UI with a rendered result. - Verify: operator
   observes the authoring session + UAT.
7. [ ] Golden-utterance eval suite green at every gate. - Verify: CI.

## Dependencies

### Prerequisites
- Canonical doc v0.4 + migration map v1.0 + overlay matrix (all committed; decisions ratified).
- `spaarkedev1` Dataverse env + `spaarke-bff-dev` App Service access; schema-change permissions.
- FR-P0-11 portfolio reconciliation early (R7 close-out unblocks branch hygiene).

### External Dependencies
- Azure OpenAI deployments (existing); App Insights (existing).
- Dataverse MCP `/api/mcp` reachability for the P0-08 spike (informative, non-blocking).
- Spaarke Communication (Email) service — existing; consumed by FR-P3-02, not modified
  structurally by this project.

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Latency NFRs | Targets for upload→summary + text-turn TTFB? | Standard: ≤5s p50/≤10s p95 upload→first summary token; ≤3s p50 turn TTFB | NFR-04; prompt caching mandatory |
| Email mechanism | Outlook drafts vs send? | **Spaarke's own Communication (Email) service via Graph API — not Outlook** | FR-P3-02 delegates to the Communication service; draft record rendered for review |
| Metering depth | Counter only vs admin endpoint? | Counter + telemetry + KQL pack; endpoint deferred | FR-P4-05 / NFR-05 |
| G-M capability | Name now or at P4? | Choose at P4 | Gate criteria fixed; capability selection is an operator action |
| Cutover doctrine | (prior session) | Customer continuity NOT a constraint — hard cutover per surface | NFR-08; no parallel-run |
| Cleanup scope | (prior session) | Track B covers ALL dead debt incl. unrelated-to-target | FR-TB-01/FR-P4-01 |
| Dispatch model | (ratified) | OQ-1: loop-as-dispatcher; no classifier stack | FR-P2-*; ADR-039 |
| Engine fate | (ratified) | OQ-2: frozen; coded workflows for new composites; no maker graphs | FR-P3-04/05 scope boundary |
| Elicitation | (ratified) | OQ-3: loop-native; modal escape | FR-P2-03 |
| Dataverse tools | (ratified) | OQ-4/D10: native handlers, GA-MCP contracts, OBO spike | FR-P0-07/08 |
| Exceptions | (ratified) | E-1/2/3 accepted; E-4 (text-path confidence dial) + E-5 rejected | contract details in overlay matrix |
| /goal usage | (this session) | Wave-level pilot; pre-authored conditions in wave definitions; never at phase gates | NFR-10 |

## Assumptions

- **draft-correspondence is DRAFT-ONLY this project**: the assistant produces a reviewable draft
  in the Communication service; sending stays user-initiated there. Assistant-initiated gated
  send is a later catalog addition (named in FR-P4-07 deferrals).
- **Eval suite sizing**: ~30 seed utterances (P0) growing to ~100 by P3.
- **G-M capability** will be small and novel (no existing code overlap) — chosen by operator at P4.
- **Loop tool budget default 8** per canonical doc; tunable platform setting.

## Unresolved Questions

*None blocking.* The P0-08 OBO spike result is informative (per-tool transport option), not a
blocker in either outcome.

---

*AI-optimized specification. Original design: `design.md` v1.1. Generated by /design-to-spec 2026-07-05.*
