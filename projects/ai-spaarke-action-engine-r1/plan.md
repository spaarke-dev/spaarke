# Project Plan: AI Spaarke Action Engine R1

> **Last Updated**: 2026-05-29
> **Status**: Ready for Tasks (gated on task 001 architecture spike)
> **Spec**: [spec.md](./spec.md)
> **Design**: [design.md](./design.md)

---

## 1. Executive Summary

**Purpose**: Build the Action Engine — Spaarke's unified surface for authoring, discovering, and executing Actions (deterministic + probabilistic Tools) across three invocation paths (conversational, explicit UI, system-triggered).

**Scope**:
- BFF surface under `Services/Ai/ActionEngine/` + `PublicContracts/` facade
- Six new Dataverse entities + extension of `sprk_aichatcontextmap`
- Three meta-tools registered with every Assistant session
- IGateResolver primitive (4 implementations, 5 gate types)
- Phase deny-tools at dispatch layer (mechanical enforcement)
- Shared `GateApprovalCard` Fluent v9 component
- SpaarkeAi extensions: `ChatLaunchContext` + ribbon launchers (Matter/Project/Account/Contact)
- Three starter Action Templates (Summarize Matter / Weekly Task Digest / Find Similar Matters)
- Default `Spaarke Assistant — General` playbook seed

**Timeline**: TBD pending task 001 architecture spike. Approximate effort: **6–8 weeks** end-to-end across 7 phases, assuming parallel execution per the parallel-groups plan in `tasks/TASK-INDEX.md`.

**Estimated Effort**: ~140–180 hours (~35 tasks × 4–5h average).

---

## 2. Architecture Context

### Design Constraints

**From ADRs (must comply):**

- **ADR-001** — Minimal API + BackgroundService. No Azure Functions for in-proc Action execution. Scheduler may be Azure-native (Logic Apps timer / Service Bus scheduled / Functions timer) — choice deferred to task 001.
- **ADR-002** — Thin Dataverse plugins. Action triggers via Dataverse webhooks (R2 scope), NOT plugin HTTP/Graph calls.
- **ADR-003** — Authorization seams. Multi-surface OBO chain for user-attributable Actions.
- **ADR-004** — Job Contract pattern. `IJobHandler<T>` for `ScheduledActionDispatchJob`; consumed by `ServiceBusJobProcessor`.
- **ADR-007** — SpeFileStore facade. Graph SDK types stay below facade (relevant for any Tool that touches SPE).
- **ADR-008** — Endpoint-filter authorization. Every Action Engine endpoint applies `.AddEndpointFilter<>()`; NO global middleware.
- **ADR-010** — DI minimalism. New `AddActionEngineModule()` extension; ≤15 non-framework DI registrations.
- **ADR-013 (refined 2026-05-20)** — AI Architecture. BFF placement justified per decision criteria; CRUD-side consumers reach Action Engine via `Services/Ai/PublicContracts/IActionEngineFacade.cs` facade.
- **ADR-015** — Audit middleware. Tier 2 hash-only + Tier 3 Cosmos work history per Tool dispatch (tenant-partitioned, GDPR erasure).
- **ADR-016** — Rate limiting. Soft enforcement MVP; hard caps deferred to R2.
- **ADR-018** — Feature flags + kill switches. Action governance block references kill-switch flag.
- **ADR-019** — ProblemDetails (RFC 7807). All endpoint errors use this format.
- **ADR-021** — Fluent UI v9 + dark mode. `GateApprovalCard` uses semantic tokens; dark-mode tested.
- **ADR-028** — Spaarke Auth v2. `useAuth()`/`authenticatedFetch` for BFF calls; `DefaultAzureCredential` for outbound MI; audit middleware.
- **ADR-029** — BFF Publish Hygiene. ≤5MB delta budget (NFR-10); framework-dependent linux-x64; sourcemap exclusion.

**From spec:**

- **NFR-01**: `FindResources` p95 < 200ms over semantic index
- **NFR-10**: Publish-size delta ≤ 5 MB compressed
- 5-min auto-reject timeout on pending gates
- All 8 hallucination guardrails present and tested

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Action Engine in BFF | ADR-013 decision criteria (latency <500ms, audit/session state lifecycle) | Single deployment unit; bff-extensions.md governance applies |
| `PublicContracts/IActionEngineFacade.cs` facade | ADR-013 refined; CRUD-side consumers never inject AE internals | Phase 1 task 015 creates facade BEFORE any consumer integration |
| Hybrid D runtime (BFF + Azure-native scheduler) | Reuse existing `SprkChatAgent` + `UseFunctionInvocation`; same pattern as Insights Engine Track B | No new runtime; task 001 confirms scheduler choice |
| Mechanical Phase deny-tools | LAVERN Pattern #8; not prompt-coached | Enforced at `IToolHandlerRegistry` dispatch; throws `PhaseToolDeniedException` |
| Tool Registry on existing `spaarke-search-dev` | Reuse infra; ≤5MB budget pressure | New index `spaarke-resource-registry-index` |

### Discovered Resources

**Applicable Skills (auto-discovered via /project-pipeline):**

- `.claude/skills/task-execute/SKILL.md` — Mandatory per-task entry point (root CLAUDE.md §4)
- `.claude/skills/adr-aware/SKILL.md` — Auto-loads ADRs based on task tags
- `.claude/skills/adr-check/SKILL.md` — Step 9.5 quality gate at task end
- `.claude/skills/code-review/SKILL.md` — Step 9.5 quality gate at task end
- `.claude/skills/spaarke-conventions/SKILL.md` — Always-apply naming/structure
- `.claude/skills/script-aware/SKILL.md` — Discover existing scripts before writing new
- `.claude/skills/bff-deploy/SKILL.md` — Phase 6 BFF deploys
- `.claude/skills/dataverse-create-schema/SKILL.md` — Phase 1 entity creation
- `.claude/skills/dataverse-deploy/SKILL.md` — Phase 6 solution deploys
- `.claude/skills/code-page-deploy/SKILL.md` — Phase 6 SpaarkeAi deploys
- `.claude/skills/pcf-deploy/SKILL.md` — (if Assistant PCF on Matter form scoped in)
- `.claude/skills/fluent-v9-component/SKILL.md` — `GateApprovalCard` authoring
- `.claude/skills/jps-action-create/SKILL.md` — Three starter Templates
- `.claude/skills/jps-playbook-design/SKILL.md` — Default `Spaarke Assistant — General` playbook
- `.claude/skills/push-to-github/SKILL.md` — Branch commits
- `.claude/skills/code-review/SKILL.md`, `.claude/skills/ui-test/SKILL.md` — Quality gates

**Knowledge Articles:**

- `docs/architecture/AI-ARCHITECTURE.md` — high-level AI subsystem map
- `docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md` — workspace integration patterns
- `docs/architecture/SPAARKEAI-COMPONENT-MODEL.md` — `@spaarke/*` package boundaries
- `docs/architecture/jobs-architecture.md` — for `ScheduledActionDispatchJobHandler`
- `docs/architecture/background-workers-architecture.md` — `ServiceBusJobProcessor` patterns
- `docs/architecture/shared-ui-components-architecture.md` — `GateApprovalCard` placement
- `docs/architecture/code-pages-architecture.md` — SpaarkeAi launcher extensions
- `docs/assessments/bff-ai-extraction-assessment-2026-05-20.md` — governance evidence base
- `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md` — Pattern D for shared widgets
- `docs/guides/auth-deployment-setup.md` — Spaarke Auth v2 deployment
- `docs/guides/PCF-DEPLOYMENT-GUIDE.md` — if PCF surface added

**Reusable Code (canonical patterns to follow):**

- `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs` — orchestrator pattern → `ActionOrchestrationService`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs` + `UseFunctionInvocation` — multi-step probabilistic agent loops (extension point)
- `src/server/api/Sprk.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs` + existing handlers — `ScheduledActionDispatchJobHandler` slot
- `src/server/api/Sprk.Bff.Api/Program.cs` — module registration sites (`AddJobProcessingModule` line ~95)
- `src/server/api/Sprk.Bff.Api/Endpoints/EndpointMappingExtensions.cs` line ~157 — `MapActionEndpoints()` slot
- `src/server/api/Sprk.Bff.Api/Services/Ai/Audit/AuditEnrichmentMiddleware.cs` — extend for Tool dispatch (ADR-015)
- `src/server/api/Sprk.Bff.Api/builder-scopes/ACT-BUILDER-*.json` — existing builder schemas to extend
- `src/client/shared/Spaarke.UI.Components/src/index.ts` — `GateApprovalCard` placement
- `src/solutions/SpaarkeAi/src/utils/launch-resolver.ts` — `ChatLaunchContext` URL-param parsing

**Binding Constraints:**

- `.claude/constraints/bff-extensions.md` — pre-merge checklist + decision criteria (root CLAUDE.md §10 binding)
- `.claude/constraints/auth.md`, `ai.md`, `api.md`, `jobs.md`, `pcf.md` — domain constraints loaded per task tags
- `.claude/patterns/auth/spaarke-sso-binding.md` — canonical v2 auth pattern
- `.claude/patterns/api/endpoint-filters.md` — endpoint authorization
- `.claude/patterns/api/endpoint-definition.md` — Minimal API patterns

---

## 3. Implementation Approach

### Phase Structure

```
Phase 0: Architecture Spike (Week 1)
└─ Task 001: Validate scheduler, publish-size, Hybrid D runtime
└─ BLOCKS all subsequent phases

Phase 1: Foundation (Week 2-3, parallel)
└─ Dataverse entities (4 tasks)
└─ BFF module + facade + AI Search index (3 tasks)

Phase 2: Tool Registry + Discovery (Week 3-4)
└─ Extended metadata model + endpoints
└─ 3 meta-tools + always-on tools
└─ Phase deny-tools enforcement

Phase 3: IGateResolver + Approval Surface (Week 4-5)
└─ Interface + 4 implementations + 5 gate types
└─ 5-min timeout + GateApprovalCard shared component

Phase 4: Action Execution Runtime (Week 5-6)
└─ Action CRUD + run endpoints
└─ Orchestration service + dispatch job + audit + rate-limit + flags

Phase 5: Surfaces + Templates (Week 6-7)
└─ SpaarkeAi extensions + ribbon launchers
└─ Default playbook seed + 3 starter Templates

Phase 6: Quality Gates + Deployment (Week 7-8)
└─ 4 integration tests + load test + publish-size + CVE scan
└─ BFF deploy + Dataverse deploy + SpaarkeAi deploy + post-deploy validation

Wrap-up (final)
└─ Task 090: README → Complete + lessons-learned + repo-cleanup prep
```

### Critical Path

**Blocking dependencies:**
- Phase 0 (task 001) BLOCKS Phase 1+
- Phase 1 task 015 (PublicContracts facade) BLOCKS Phase 2+ (no AE internals injected anywhere)
- Phase 2 task 020 (Tool Registry metadata model) BLOCKS tasks 021–026
- Phase 3 task 030 (IGateResolver interface) BLOCKS tasks 031–034
- Phase 4 task 040 (Action endpoints) BLOCKS task 042 (dispatch job needs Action Run write target)

**High-risk items:**

- **Publish-size delta exceeds 5 MB cap** — Mitigation: task 001 measures empirically; task 065 validates at end of Phase 6 before deploy.
- **Scheduler choice wrong** — Mitigation: task 001 spike outputs runtime ADR before any Phase 1+ task starts.
- **Tool Registry classification taxonomy not aligned with Insights Engine** — Mitigation: coordination doc in project artifacts; signal envelope joint ownership.

---

## 4. Phase Breakdown

### Phase 0: Architecture Spike

**Objectives:**
1. Resolve 3 open architecture questions from spec.md §15
2. Output runtime ADR draft
3. Provide go/no-go for Phase 1+ start

**Deliverables:**
- [ ] Scheduler choice documented (Logic Apps timer / Service Bus scheduled / Functions timer / Container Apps Jobs) with rationale
- [ ] Publish-size delta measured against ≤5MB budget (NFR-10)
- [ ] Hybrid D runtime confirmed (SprkChatAgent + UseFunctionInvocation serves all multi-step probabilistic Action scenarios)
- [ ] Runtime ADR draft added to `notes/decisions/`
- [ ] `plan.md` Phase 4 updated with confirmed topology

**Critical Tasks:** 001 — MUST be first; blocks everything

**Inputs**: `spec.md`, `design.md`, `coordination-assessment-with-insights-engine.md`, `lavern-pattern-assessment.md`, `.claude/constraints/bff-extensions.md`, ADR-013, ADR-029

**Outputs**: Runtime ADR draft; plan.md update; go/no-go signal

### Phase 1: Foundation

**Objectives:**
1. Stand up Dataverse schema for Action Engine entities
2. Create BFF module skeleton + PublicContracts facade
3. Provision Azure AI Search index for Tool Registry semantic discovery

**Deliverables:**
- [ ] `sprk_action`, `sprk_actiontemplate`, `sprk_actioninstance`, `sprk_actionrun` entities
- [ ] `sprk_toolregistry` entity with extended metadata fields
- [ ] `sprk_gate_approval` entity
- [ ] `sprk_aichatcontextmap` extension fields
- [ ] `Services/Ai/ActionEngine/` folder + `AddActionEngineModule()` extension
- [ ] `Services/Ai/PublicContracts/IActionEngineFacade.cs` facade
- [ ] `spaarke-resource-registry-index` on existing `spaarke-search-dev` service

**Critical Tasks:** 014 (BFF module) + 015 (facade) must precede any consumer integration

**Parallel Group A**: 010, 011, 012, 013 (independent entity creations)
**Parallel Group B**: 014, 015, 016 (independent: module, facade, AI Search index)

### Phase 2: Tool Registry + Discovery

**Objectives:**
1. Build extended Tool Registry metadata model
2. Register 3 meta-tools (`FindResources`, `GetResourceDetail`, `InvokeResource`) + 3 always-on tools
3. Implement Phase deny-tools mechanical enforcement at dispatch layer

**Deliverables:**
- [ ] Tool Registry metadata model (Classification, CostClass, LatencyClass, Idempotency, AuthMode, Discoverability, ModelTier, PhaseRestrictions, EvidenceRequired, IsAlwaysOnInAssistant, PromoteInContexts)
- [ ] `ToolRegistryEndpoints.cs` + queries
- [ ] `FindResources` meta-tool (p95 <200ms via semantic search — NFR-01)
- [ ] `GetResourceDetail` meta-tool (full schema + SourceHints)
- [ ] `InvokeResource` meta-tool (direct exec or gate-pending status)
- [ ] Always-on tools: `SearchDocuments`, `QueryDataverse`, `GetCurrentEntityContext`
- [ ] `IToolHandlerRegistry` Phase deny-tools enforcement (throws `PhaseToolDeniedException`)

**Critical Tasks:** 020 (metadata model) blocks 021–026

**Parallel Group C**: 022, 023, 024 (independent meta-tool implementations)

### Phase 3: IGateResolver + Approval Surface

**Objectives:**
1. Build IGateResolver primitive (4 impls, 5 gate types)
2. Implement 5-min auto-reject timeout
3. Ship shared `GateApprovalCard` Fluent v9 component

**Deliverables:**
- [ ] `IGateResolver` interface + 5 gate types (EthicsCritical, MeaningCritical, FinalDelivery, Manual, Conditional)
- [ ] `DataverseQueueGateResolver` (write to `sprk_gate_approval`; resumes on approval record update)
- [ ] `InteractiveInChatGateResolver` (inline chat card; user approves in conversation)
- [ ] `WebhookGateResolver` (POST to configured URL; resumes on callback)
- [ ] `AutoApproveGateResolver` (no-op approval; for low-risk Actions)
- [ ] 5-min timeout → auto-reject behavior across all resolvers
- [ ] `GateApprovalCard` in `Spaarke.UI.Components` (Fluent v9, ADR-021 dark-mode tested)

**Critical Tasks:** 030 (interface) blocks 031–035

**Parallel Group D**: 031, 032, 033, 034 (independent resolver implementations)

### Phase 4: Action Execution Runtime

**Objectives:**
1. Build Action CRUD + run endpoints
2. Wire orchestration service + dispatch job + audit + rate-limit + feature flags

**Deliverables:**
- [ ] `ActionEndpoints.cs` (CRUD + `/run` endpoint, endpoint-filter auth per ADR-008)
- [ ] `ActionOrchestrationService.cs` (follows `AnalysisOrchestrationService` pattern)
- [ ] `ScheduledActionDispatchJobHandler.cs` (ADR-004 `IJobHandler<T>`)
- [ ] `AuditEnrichmentMiddleware` extension for Tool dispatch (ADR-015 Tier 2 + Tier 3)
- [ ] Rate limiting on `/run` endpoints (ADR-016 soft MVP)
- [ ] Feature flag + kill-switch wiring on Action Definition governance block (ADR-018)

### Phase 5: Surfaces + Templates

**Objectives:**
1. Extend SpaarkeAi with `ChatLaunchContext` URL parsing + ribbon launchers
2. Seed default `Spaarke Assistant — General` playbook
3. Ship 3 starter Action Templates

**Deliverables:**
- [ ] `ChatLaunchContext` parsing in `src/solutions/SpaarkeAi/src/utils/launch-resolver.ts`
- [ ] Ribbon launchers on Matter / Project / Account / Contact entity forms
- [ ] Default playbook seed (system prompt §7.4.3, always-on tools, `InteractiveGateResolver` default)
- [ ] Template 1: **Summarize a matter** (Deterministic Dataverse query + AI narrative summary)
- [ ] Template 2: **Send weekly task digest** (scheduled trigger + template render + SendEmail with gate)
- [ ] Template 3: **Find similar matters** (Hybrid: semantic search + Dataverse hydration + citations)

**Parallel Group E**: 053, 054, 055 (independent Template authoring)

### Phase 6: Quality Gates + Deployment

**Objectives:**
1. Run all integration + load + size + CVE tests
2. Deploy BFF, Dataverse schema/seed, SpaarkeAi
3. Post-deploy validation

**Deliverables:**
- [ ] Integration test: 3 starter Templates author + execute (G1)
- [ ] Integration test: meta-tools discovery flow (G2)
- [ ] Integration test: gate resolution all paths (G3)
- [ ] Integration test: Phase deny-tools enforcement (G4)
- [ ] Load test: `FindResources` p95 < 200ms (G5)
- [ ] Publish-size validation ≤5MB delta (G8)
- [ ] CVE scan: no new HIGH severity (G9)
- [ ] Deploy BFF via `bff-deploy` skill
- [ ] Deploy Dataverse schema + seed playbook + Templates via `dataverse-deploy` + `Deploy-Playbook.ps1` + `Seed-JpsActions.ps1`
- [ ] Deploy SpaarkeAi code page + ribbon resources via `code-page-deploy`
- [ ] Post-deploy validation via `Validate-DeployedEnvironment.ps1`

**Parallel Group F**: 060, 061, 062, 063 (independent integration tests)

### Wrap-up

**Objectives:**
1. Run `code-review` + `adr-check` on all project code
2. Update README → Complete
3. Write lessons-learned

**Deliverables:**
- [ ] `notes/lessons-learned.md` populated
- [ ] README graduation criteria all checked
- [ ] TASK-INDEX all ✅
- [ ] `/repo-cleanup` invoked

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Azure AI Search (`spaarke-search-dev`) | GA | Low | Existing service; new index only |
| Azure scheduler (Logic Apps / SB / Functions) | GA | Low (per option) | Task 001 spike validates choice |
| Insights Engine `InsightArtifact` signal envelope | In progress on `work/ai-spaarke-insights-engine-r1` | Medium | Coordination doc in artifacts; joint ownership |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| `SprkChatAgent` + `UseFunctionInvocation` | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/` | Production |
| `ServiceBusJobProcessor` + `IJobHandler<T>` | `src/server/api/Sprk.Bff.Api/Services/Jobs/` | Production |
| `AnalysisOrchestrationService` (pattern reference) | `src/server/api/Sprk.Bff.Api/Services/Ai/` | Production |
| `Spaarke.UI.Components` (Fluent v9 host) | `src/client/shared/Spaarke.UI.Components/` | Production |
| `SpaarkeAi` code page | `src/solutions/SpaarkeAi/` | Production |
| JPS Playbook (`sprk_playbook`, `sprk_aichatcontextmap`) | Dataverse production | Production |
| ADR-013 (refined 2026-05-20), ADR-028, ADR-029 | `.claude/adr/`, `docs/adr/` | Current |

---

## 6. Testing Strategy

**Unit Tests** (target: 70% coverage on new code):
- `ActionOrchestrationService` orchestrations + branching
- `IToolHandlerRegistry` Phase deny-tools enforcement
- Each `IGateResolver` implementation + timeout behavior
- Tool Registry metadata model + serialization

**Integration Tests** (BFF):
- `ActionEngine.IntegrationTests.StarterTemplates` (G1) — three Templates author + run
- `ActionEngine.IntegrationTests.MetaTools` (G2) — discovery flow end-to-end
- `ActionEngine.IntegrationTests.GateResolution` (G3) — all paths × all timeout outcomes
- `ActionEngine.IntegrationTests.PhaseDeniedTools` (G4) — throws expected exception

**Load Test**:
- `FindResources` against `spaarke-resource-registry-index`: p95 < 200ms (G5/NFR-01) — verified via App Insights metric + dedicated load test harness

**E2E Tests** (browser-based via `ui-test` skill):
- "Summarize Matter X" conversational invocation (G2)
- `GateApprovalCard` renders + dark-mode parity (G3 + ADR-021)
- Ribbon launcher: button visible on Matter form, opens SpaarkeAi as side pane

**Audit + Compliance**:
- ADR-015 Tier 2 hash + Tier 3 work history records present after every Tool dispatch
- Tenant scoping + GDPR erasure path verified

---

## 7. Acceptance Criteria

Mirror of README graduation criteria. Verification methods cited.

### Technical Acceptance

- [ ] G1: Three Templates execute E2E — verified by `StarterTemplates` integration test suite
- [ ] G2: Conversational invocation resolves intent → Tool — verified by `MetaTools` integration test
- [ ] G3: All gate paths route correctly; 5-min timeout auto-rejects — verified by `GateResolution` integration test
- [ ] G4: Phase deny-tools throws `PhaseToolDeniedException` — verified by `PhaseDeniedTools` integration test
- [ ] G5: `FindResources` p95 < 200ms — verified by load test + App Insights metric (NFR-01)
- [ ] G6: Every Tool dispatch writes audit (ADR-015 Tier 2 + Tier 3) — verified by audit middleware integration test
- [ ] G7: All 8 hallucination guardrails present + tested — verified by guardrail test suite
- [ ] G8: Publish-size delta ≤ 5 MB — verified by `dotnet publish --runtime linux-x64` before/after comparison (NFR-10)
- [ ] G9: No new HIGH-sev CVE — verified by `dotnet list package --vulnerable --include-transitive`
- [ ] G10: Runtime ADR present in `docs/adr/` — verified by file existence + ADR registry update
- [ ] G11: Every Action Engine endpoint has endpoint-filter auth — verified by `adr-check` Step 9.5 + grep audit
- [ ] G12: No CRUD-side caller injects AE internals — verified by `code-review` Step 9.5 + grep for `IActionOrchestrationService` outside `Services/Ai/ActionEngine/`

### Business Acceptance

- [ ] Three Templates usable by non-technical legal-ops via SpaarkeAi conversational invocation (acceptance test session with product owner)

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | Publish-size delta exceeds 5 MB cap | Medium | High | Task 001 spike measures empirically; task 065 validates pre-deploy; reuse `SprkChatAgent` (no new runtime) |
| R2 | Wrong scheduler choice; rework Phase 4 | Medium | Medium | Task 001 architecture spike outputs runtime ADR before Phase 1+ start |
| R3 | Tool Registry classification taxonomy diverges from Insights Engine | Medium | Medium | `coordination-assessment-with-insights-engine.md` + joint signal envelope ownership; sync before Phase 5 |
| R4 | `IGateResolver` 5-min timeout misfires in long Actions | Low | Medium | Configurable per-Action; task 035 covers semantics; integration tests validate edge cases |
| R5 | Audit volume from per-Tool-dispatch exceeds Cosmos quota | Low | Medium | Tier 2 (hash only) carries most volume; Tier 3 tenant-partitioned with GDPR erasure |
| R6 | Insights Engine merges first, conflicts with shared `IGateResolver` design | Medium | Medium | Action Engine MVP defines `IGateResolver` canonically per LAVERN Pattern #5; Insights consumes (not defines) |
| R7 | SpaarkeAi PR #306 (assistant new resources) overlaps Phase 5 work | Low | Low | Coordinate timing with PR #306 owner; rebase if needed |

---

## 9. Next Steps

1. **Review this PLAN.md and README.md** ✅ generated by `/project-pipeline`
2. **Execute task 001 — Architecture Spike** — run `execute task 001`
3. **Update plan.md Phase 4** with confirmed scheduler topology after task 001 completes
4. **Proceed through phases per TASK-INDEX.md** — parallel groups dispatched as prerequisites clear

---

## 10. References

- [spec.md](./spec.md) — Functional + non-functional requirements (~6,500 words)
- [design.md](./design.md) — Multi-surface Assistant model + conceptual model (~63KB)
- [coordination-assessment-with-insights-engine.md](./coordination-assessment-with-insights-engine.md) — Joint ownership of signal envelope + gate primitive + Tool Registry stewardship
- [lavern-pattern-assessment.md](./lavern-pattern-assessment.md) — External reference (no separate ADR ratification)
- [.claude/constraints/bff-extensions.md](../../.claude/constraints/bff-extensions.md) — Binding pre-merge governance
- [docs/assessments/bff-ai-extraction-assessment-2026-05-20.md](../../docs/assessments/bff-ai-extraction-assessment-2026-05-20.md) — Governance evidence base
- [.claude/adr/INDEX.md](../../.claude/adr/INDEX.md) — ADR index (15 applicable)
- [tasks/TASK-INDEX.md](./tasks/TASK-INDEX.md) — Task registry + parallel groups + dependency graph

---

**Status**: Ready for Tasks
**Next Action**: Execute task 001 (architecture spike) — `execute task 001`

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks via task-execute.*
