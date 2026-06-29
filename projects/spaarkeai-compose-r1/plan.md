# Project Plan: Spaarke Compose (R1)

> **Last Updated**: 2026-06-29
> **Status**: Ready for Tasks
> **Spec**: [spec.md](./spec.md)
> **Design**: [design.md](./design.md)

---

## 1. Executive Summary

**Purpose**: Deliver the foundation for Spaarke Compose — an AI-native legal drafting workspace that ships as a new SpaarkeAi workspace layout. R1 delivers layout + three-pane wiring + TipTap (OOB) editor shell + SPE plumbing + ChatSession reuse + consumer-routing smoke test. AI actions, return-from-Word round-trip, and DOCX subset enforcement layer in R2+.

**Scope**:
- New `sprk_workspacelayout` system record + `compose-editor` section type
- 7 BFF endpoints under `/api/compose/` + 3 new `Services/Compose/*` services
- New `@spaarke/document-operations` shared library (extracted from SemanticSearch)
- Compose React components (`ComposeWorkspace`, `ComposeToolbar`, `ComposeEmptyState`, `ComposeEditor`)
- Two JPS scopes + one consumer-routing type (`compose-summarize`)
- SPE check-out lock + multi-tab conflict UX + 15-min orphan release
- Three-pane coordination wiring (six flows, locked TypeScript contracts; receivers stubbed)
- Unit tests for every new BFF service per CLAUDE.md §10 #6

**Timeline**: Spike phase ~5 days + main R1 implementation ~3–4 weeks · **Estimated Effort**: 25–35 dev-days (one engineer; parallelizable on Phases 1–3 + 5–6 after spikes complete)

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-001** Minimal API: Compose endpoints use minimal-API + endpoint groups (no Azure Functions, no MVC controllers)
- **ADR-008** Endpoint filters for authorization: every Compose endpoint `RequireAuthorization()`
- **ADR-010** Org-owned Dataverse default: new rows (`sprk_workspacelayout`, `sprk_playbookconsumer`) org-owned
- **ADR-013** AI facade refinement (2026-05-20): inject `IConsumerRoutingService` + `IInvokePlaybookAi` ONLY — NOT `IOpenAiClient` / `IPlaybookService` / other AI internals into Compose CRUD code
- **ADR-015** Multi-tenant isolation Tier 3: inherit from reused `ChatSession` three-tier infrastructure
- **ADR-019** Endpoint conventions: group under `/api/compose/`
- **ADR-028** Spaarke Auth v2: client uses `@spaarke/auth`; BFF validates via existing auth pipeline
- **ADR-032** BFF Null-Object Kill-Switch: applies if any Compose service ends up feature-gated; R1 default = no feature gates
- **ADR-038** Testing strategy: integration-heavy pyramid; 6 KEEP categories; mock-boundary rules; ban `Mock<HttpMessageHandler>`, DI-registration tests, ctor null-check tests

**From Spec** (`spec.md` MUST rules):
- MUST reuse existing `ChatSession` + Redis/Cosmos/Dataverse three-tier; NOT create new session entity
- MUST reuse existing `IConsumerRoutingService` + `IInvokePlaybookAi` for AI dispatch
- MUST reuse existing `GET /api/documents/{id}/open-links` for Word handoff — NOT create new endpoint
- MUST extract `useDocumentActions` to shared lib BEFORE Compose consumes it
- MUST measure publish-size impact on BFF-touching tasks per CLAUDE.md §10 #4
- MUST add unit tests for new BFF services per CLAUDE.md §10 #6
- MUST NOT extend `sprk_analysis` for Compose chat/session storage
- MUST NOT build custom integrations for advanced DOCX features outside TipTap OOB
- MUST NOT support tracked-changes round-trip (out of architecture)
- MUST NOT extend `HostContext` in R1 (transient state → JPS scope inputs)

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| TipTap (ProseMirror) editor | OOB feature richness; React-native; well-maintained | Locks editor choice; OOB-only scope |
| OOB-only DOCX subset | Avoids custom-integration cost | Tracked changes / footnotes / fields / equations → Word handoff |
| ChatSession reuse (no new entity) | Three-tier pattern proven; legacy `sprk_analysis.sprk_chathistory` superseded | DocumentId-bound sessions; existing compaction/archive thresholds unchanged |
| BFF placement (all endpoints in `Sprk.Bff.Api`) | All touch SPE/Graph + Dataverse; no microservice justified | 7 endpoints + 3 services + DI in Program.cs |
| AI via PublicContracts facade only | ADR-013 (refined 2026-05-20) | Compose CRUD never injects AI internals |
| First consumer type: `compose-summarize` → Document Summary playbook | Simplest E2E smoke test; existing playbook | One row in `sprk_playbookconsumer`; no new playbook authoring |
| Per-user single-session SPE check-out lock | Avoids "user collaborating with themselves" | Multi-tab conflict UX + heartbeat orphan release |
| Modal entry (Path A) with full-screen toggle | Reuses existing SpaarkeAi modal pattern | No new shell |
| Empty state: Browse + Search | Two-action default-open per design.md §14 #5 | Two UI affordances |
| First-Save promotion (idempotent) | Ephemeral docs → `sprk_document` records | Indexing follows normal Document pipeline |

### Discovered Resources

**Applicable ADRs** (concise + full):
- [ADR-001](../../.claude/adr/) Minimal API · [full](../../docs/adr/) — BFF endpoint pattern
- [ADR-008](../../.claude/adr/) Endpoint filters — `RequireAuthorization()` on all Compose endpoints
- [ADR-010](../../.claude/adr/) Org-owned Dataverse default — new rows org-owned
- [ADR-013](../../.claude/adr/) BFF AI extraction (refined 2026-05-20) — PublicContracts facade only
- [ADR-015](../../.claude/adr/) Multi-tenant isolation Tier 3 — inherits from ChatSession infra
- [ADR-019](../../.claude/adr/) Endpoint conventions — `/api/compose/` route group
- [ADR-028](../../.claude/adr/) Spaarke Auth v2 — `@spaarke/auth` + BFF auth pipeline
- [ADR-032](../../.claude/adr/) BFF Null-Object Kill-Switch — applies if any Compose service gets feature-gated (R1 default = no gates)
- [ADR-038](../../docs/adr/ADR-038-testing-strategy.md) Testing strategy — standalone; integration-heavy pyramid; 6 KEEP; mock-boundary; ban list

**Applicable Skills**:
- [`task-execute`](../../.claude/skills/task-execute/SKILL.md) — load-bearing for every task in this project
- [`code-review`](../../.claude/skills/code-review/SKILL.md) — judgment-layer quality gate at task Step 9.5
- [`adr-check`](../../.claude/skills/adr-check/SKILL.md) — validates against ADR constraints at task Step 9.5
- [`adr-aware`](../../.claude/skills/adr-aware/SKILL.md) — auto-load ADRs at task start
- [`bff-deploy`](../../.claude/skills/bff-deploy/SKILL.md) — deploy Compose endpoints to Azure App Service
- [`code-page-deploy`](../../.claude/skills/code-page-deploy/SKILL.md) — deploy SpaarkeAi code-page changes
- [`dataverse-create-schema`](../../.claude/skills/dataverse-create-schema/SKILL.md) — create `sprk_workspacelayout` + `sprk_playbookconsumer` rows (no new tables)
- [`dataverse-deploy`](../../.claude/skills/dataverse-deploy/SKILL.md) — deploy any solution-side artifacts
- [`fluent-v9-component`](../../.claude/skills/fluent-v9-component/SKILL.md) — Compose React components use Fluent v9
- [`spe-integration`](../../.claude/skills/spe-integration/SKILL.md) — SPE container, permissions, document open paths
- [`jps-scope-refresh`](../../.claude/skills/jps-scope-refresh/SKILL.md) — refresh scope catalog after adding `compose-selection` / `compose-document`
- [`jps-validate`](../../.claude/skills/jps-validate/SKILL.md) — validate new JPS scopes
- [`script-aware`](../../.claude/skills/script-aware/SKILL.md) — find applicable scripts before writing new code
- [`ui-test`](../../.claude/skills/ui-test/SKILL.md) — browser-based UI testing for Compose components

**Knowledge Articles** (must-read for any Compose task):
- [`docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](../../docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md) — end-to-end pipeline cold-load → widget render
- [`docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`](../../docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md) — two-wrapper architecture (authoritative)
- [`docs/architecture/SPAARKEAI-COMPONENT-MODEL.md`](../../docs/architecture/SPAARKEAI-COMPONENT-MODEL.md) — `@spaarke/*` package inventory
- [`docs/architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md`](../../docs/architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md) — Calendar widget canonical "Pattern D" precedent
- [`docs/architecture/LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md`](../../docs/architecture/LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md) — 21 testable MUSTs for embedded-mode hosts
- [`docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`](../../docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md) — five-archetype decision tree; Calendar Pattern D worked example
- [`docs/guides/HOW-TO-ADD-A-CONSUMER-ROUTING-TYPE.md`](../../docs/guides/HOW-TO-ADD-A-CONSUMER-ROUTING-TYPE.md) — exact steps to add `compose-summarize` consumer
- [`docs/standards/CHAT-ATTACHMENT-POLICY.md`](../../docs/standards/CHAT-ATTACHMENT-POLICY.md) — applies if Assistant uploads documents to Compose
- [`docs/standards/DATA-ACCESS-DECISION-CRITERIA.md`](../../docs/standards/DATA-ACCESS-DECISION-CRITERIA.md) — `Xrm.WebApi` vs BFF decision
- [`docs/adr/ADR-038-testing-strategy.md`](../../docs/adr/ADR-038-testing-strategy.md) — standalone testing ADR
- [`docs/standards/TEST-ARCHITECTURE.md`](../../docs/standards/TEST-ARCHITECTURE.md) — operational test pyramid + 6 KEEP categories
- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — binding pre-merge checklist for BFF additions (§§ F.1–F.3, G)

**Reusable Code (existing patterns to follow)**:
- ChatSession three-tier: [`ChatSessionManager.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ChatSessionManager.cs), [`SessionPersistenceService.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Sessions/SessionPersistenceService.cs), [`ChatHistoryManager.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ChatHistoryManager.cs)
- Consumer-routing facade: [`ConsumerRoutingService.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerRoutingService.cs), [`InvokePlaybookAi.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/InvokePlaybookAi.cs), [`ConsumerTypes.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerTypes.cs)
- Open-in-Word backend: [`FileAccessEndpoints.cs`](../../src/server/api/Sprk.Bff.Api/Api/FileAccessEndpoints.cs) `GetOpenLinks`, [`DesktopUrlBuilder.cs`](../../src/server/shared/Spaarke.Core/Utilities/DesktopUrlBuilder.cs), [`FileOperationModels.cs`](../../src/server/api/Sprk.Bff.Api/Models/FileOperationModels.cs) `OpenLinksResponse`
- Open-in-Word client (source for extraction): [`useDocumentActions.ts`](../../src/client/code-pages/SemanticSearch/src/hooks/useDocumentActions.ts)
- Three-pane shell: [`ThreePaneShell.tsx`](../../src/solutions/SpaarkeAi/src/components/shell/ThreePaneShell.tsx)
- Workspace layout precedent: [`CalendarWorkspaceWidget`](../../src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/) — Pattern D canonical
- Modal launch: [`ConversationPane.tsx`](../../src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx), [`launch-resolver.ts`](../../src/solutions/SpaarkeAi/src/utils/launch-resolver.ts)
- Conversation: [`ConversationPane`](../../src/solutions/SpaarkeAi/src/components/conversation/) — Assistant pane

**Scripts** (consult [`scripts/README.md`](../../scripts/README.md) for full registry):
- `scripts/Deploy-BffApi.ps1` — BFF deployment (used by `/bff-deploy`)
- `scripts/Build-AllClientComponents.ps1` — builds shared libs before code-page rebuild
- `scripts/Test-SdapBffApi.ps1` — smoke tests against deployed BFF
- `scripts/Deploy-CustomPage.ps1` — SpaarkeAi code-page redeployment

**Dataverse schema** (read-only validation via MCP — pending at task time):
- `sprk_workspacelayout` table — verify exists; add Compose row at Phase 1
- `sprk_playbookconsumer` table — verify exists; add `compose-summarize` row at Phase 1
- `sprk_document` table — exists; Compose creates rows on first Save
- No new tables required

---

## 3. Implementation Approach

### Phase Structure

```
Phase 0: Spikes (~5 days, blocking gate before Phase 1+)
  ├─ Spike #1: TipTap OOB + DOCX round-trip prototype (2 days) — locks OOB feature inventory + DOCX bridge choice
  ├─ Spike #2: Three-pane coordination wiring (1 day) — locks six data contracts
  ├─ Spike #3: SPE check-out/check-in + Document promotion-on-Save (1 day) — locks heartbeat + idempotent promotion
  └─ Spike #4: Consumer-routing E2E smoke test + JPS scope registration (1 day) — locks scope schemas + endpoint shape

Phase 1: Dataverse + JPS foundations (1–2 days) — depends on Phase 0
  ├─ Create sprk_workspacelayout "Compose" row (org-owned)
  ├─ Create sprk_playbookconsumer row (compose-summarize → Document Summary playbook)
  └─ Define + register compose-selection / compose-document JPS scopes

Phase 2: BFF endpoints + services (5–7 days) — depends on Phase 0; parallelizable per-service
  ├─ Services/Compose/ComposeService.cs (upload, load, save, promote)
  ├─ Services/Compose/ComposeDocumentService.cs (SPE plumbing)
  ├─ Services/Compose/ComposeSessionService.cs (ChatSession binding)
  ├─ Api/ComposeEndpoints.cs (7 endpoints under /api/compose/)
  ├─ Services/Ai/PublicContracts/ConsumerTypes.cs (add ComposeSummarize constant)
  └─ Unit tests per CLAUDE.md §10 #6

Phase 3: Shared library extraction (2–3 days) — depends on Phase 0; can parallelize with Phase 2
  ├─ Create src/client/shared/Spaarke.DocumentOperations/ package skeleton
  ├─ Move useDocumentActions.ts from SemanticSearch to shared lib
  ├─ Refactor SemanticSearch to consume from @spaarke/document-operations
  └─ Verify SemanticSearch existing tests still green

Phase 4: Frontend — SpaarkeAi Compose surface (5–7 days) — depends on Phase 0–3
  ├─ Register compose-editor section type in SECTION_REGISTRY
  ├─ Implement ComposeWorkspace.tsx (TipTap host + toolbar wrapper)
  ├─ Implement ComposeToolbar.tsx (Open-in-Word buttons via shared lib)
  ├─ Implement ComposeEmptyState.tsx (Browse / Search options)
  ├─ Implement ComposeEditor.tsx (TipTap editor shell + DOCX bridge)
  ├─ Wire three-pane coordination flows (six TypeScript data contracts; stubbed receivers)
  └─ Modal launch (Path A) — command-bar "Open in Compose" wiring

Phase 5: SPE check-out lock mechanism (2–3 days) — depends on Spike #3
  ├─ Acquire SPE check-out on Compose open
  ├─ Multi-tab conflict UX ("[Go to that session] [Force-close other session]")
  ├─ Session heartbeat (default 15 min idle release)
  └─ Release on close / idle

Phase 6: E2E smoke test + integration (2 days) — depends on Phase 1, 2, 4
  ├─ Wire Compose UI button → BFF dispatch-action endpoint → consumer routing → Document Summary playbook
  ├─ Verify result returned + rendered in Compose
  └─ Capture integration test for compose-summarize round-trip

Phase 7: Testing + acceptance (2–3 days) — depends on Phase 2–6
  ├─ Unit tests for all new BFF services (per CLAUDE.md §10 #6)
  ├─ Integration tests for 7 endpoints (auth gating + happy path)
  ├─ Frontend tests for @spaarke/document-operations consumers
  ├─ Cross-browser smoke test (Compose mounts + edits)
  └─ ADR-038 conformance check (no banned test patterns)

Phase 8: Deployment + wrap-up (1–2 days)
  ├─ BFF deploy (measure publish-size delta per CLAUDE.md §10 #4)
  ├─ SpaarkeAi code-page deploy
  ├─ Solution-side artifacts deploy (Dataverse rows)
  ├─ Verify all 22 success criteria
  ├─ /test-diet gate (per CLAUDE.md §7 project-close gate)
  └─ Wrap-up + lessons-learned
```

### Critical Path

**Blocking Dependencies**:
- **Phase 0 (Spikes) BLOCKS all subsequent phases** — outputs unlock locked artifacts (DOCX subset spec, bridge library choice, heartbeat interval, JPS scope schemas, endpoint shape)
- **Phase 3 (shared lib extraction) BLOCKS Phase 4** — Compose consumes `useDocumentActions` from shared lib
- **Phase 1 (Dataverse + JPS) BLOCKS Phase 6** — smoke test requires `sprk_playbookconsumer` row + scopes registered
- **Phase 2 (BFF endpoints) BLOCKS Phase 6** — UI calls BFF dispatch-action endpoint
- **Phase 4 (UI) BLOCKS Phase 5** — check-out lock UX is part of Compose mount
- **Phase 4 (UI) BLOCKS Phase 6** — smoke test fires from Compose UI button

**Parallel opportunities**:
- Phase 2 (BFF) + Phase 3 (shared lib) can run in parallel after Phase 0
- Phase 2 sub-tasks (3 services + endpoints + tests) can parallelize per-file (independent modules)
- Spikes #2, #3, #4 can run in parallel (all independent of Spike #1)
- Spike #1 is the longest single spike (2 days) and the critical path through Phase 0

**High-Risk Items**:
- **DOCX↔TipTap impedance** — Mitigation: Spike #1 validates on 3 real legal DOCXs; publishes locked subset spec
- **SPE check-out + heartbeat reliability** — Mitigation: Spike #3 validates flow + interval
- **Hot-path collision with active BFF projects** (14 active) — Mitigation: bff-extensions.md checklist on every BFF task; PR sequencing via INDEX.md
- **Shared-lib extraction regressing SemanticSearch** — Mitigation: refactor SemanticSearch FIRST; verify tests green before Compose consumes

---

## 4. Phase Breakdown

### Phase 0: Spikes (Foundation, blocking)

**Duration**: ~5 days (Spikes #2–4 parallelizable; Spike #1 is critical path)

**Objectives**:
1. Validate TipTap OOB feature inventory on 3 real legal DOCXs (one letter, one long agreement, one multi-level-numbered contract)
2. Choose specific DOCX bridge library (open-source preferred; no custom integration)
3. Wire three-pane coordination data contracts (Flows 1, 2, 5 minimum)
4. Validate SPE check-out/check-in + Document promotion-on-Save mechanism end-to-end
5. Validate consumer-routing E2E with `compose-summarize` placeholder + JPS scope registration

**Deliverables**:
- [ ] 4-page feasibility memo in `notes/spikes/` capturing OOB feature inventory + bridge choice + heartbeat interval
- [ ] 4 working prototypes (one per spike) — small, throwaway, validation-only
- [ ] Locked DOCX subset spec (published as `notes/spikes/docx-subset.md`)
- [ ] Locked JPS scope schemas (`compose-selection`, `compose-document`)
- [ ] Locked endpoint shape for `POST /api/compose/action/{consumerType}`

**Inputs**: design.md §13, spec.md FRs, existing patterns (ChatSession, consumer-routing, three-pane shell)

**Outputs**: Locked artifacts feeding into Phases 1–4; published in `notes/spikes/`

### Phase 1: Dataverse + JPS Foundations

**Duration**: 1–2 days · **Depends on**: Phase 0 complete

**Objectives**:
1. Add `sprk_workspacelayout` system row for Compose (org-owned, label "Compose", template `single-column`, section `compose-editor`)
2. Add `sprk_playbookconsumer` row linking `compose-summarize` → playbook id `47686eb1-9916-f111-8343-7c1e520aa4df` (Document Summary)
3. Define + register `compose-selection` and `compose-document` JPS scopes via `jps-scope-refresh`

**Deliverables**:
- [ ] Compose workspace layout row in Dataverse (verified via Web API)
- [ ] `compose-summarize` row in `sprk_playbookconsumer` (verified via `IConsumerRoutingService.ResolveAsync`)
- [ ] Two JPS scopes pass `jps-validate` and appear in scope catalog refresh

**Inputs**: Spike #4 outputs (locked JPS scope schemas), spec.md FR-01, FR-08, FR-10

**Outputs**: Dataverse rows + JPS scope catalog updated

### Phase 2: BFF Endpoints + Services

**Duration**: 5–7 days · **Depends on**: Phase 0 complete

**Objectives**:
1. Create 3 new services in `Services/Compose/` injecting only allowed dependencies (no AI internals)
2. Create 7 endpoints under `/api/compose/` with `RequireAuthorization()` on each
3. Add `ConsumerTypes.ComposeSummarize` constant to `PublicContracts/ConsumerTypes.cs`
4. Wire DI in `Program.cs`
5. Add unit tests in `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/` per CLAUDE.md §10 #6

**Deliverables**:
- [ ] `Services/Compose/ComposeService.cs` (upload, load, save, promote)
- [ ] `Services/Compose/ComposeDocumentService.cs` (SPE plumbing via Graph)
- [ ] `Services/Compose/ComposeSessionService.cs` (ChatSession binding with `DocumentId`)
- [ ] `Api/ComposeEndpoints.cs` with 7 minimal-API endpoints
- [ ] `Services/Ai/PublicContracts/ConsumerTypes.cs` — `ComposeSummarize = "compose-summarize"` added
- [ ] `Program.cs` updated with Compose service registrations
- [ ] Unit tests per service (`ComposeServiceTests.cs`, `ComposeDocumentServiceTests.cs`, `ComposeSessionServiceTests.cs`)
- [ ] `ComposeEndpointsTests.cs` — endpoint integration tests
- [ ] Publish-size measured (compressed delta vs ~45.65 MB baseline; expect ≤+2 MB)

**Inputs**: Spec FR-09, FR-21, FR-22; existing patterns (ChatSession, consumer-routing facade); `.claude/constraints/bff-extensions.md`

**Outputs**: BFF builds clean; new endpoints respond per FR-21; unit tests green

### Phase 3: Shared Library Extraction

**Duration**: 2–3 days · **Depends on**: Phase 0 complete; **Can parallelize with**: Phase 2

**Objectives**:
1. Create new shared package `src/client/shared/Spaarke.DocumentOperations/`
2. Move `useDocumentActions.ts` from SemanticSearch to shared lib
3. Refactor SemanticSearch to import from `@spaarke/document-operations`
4. Verify SemanticSearch existing tests + "Open in Word for Web/Desktop" still work

**Deliverables**:
- [ ] Package skeleton with `package.json`, `tsconfig.json`, `index.ts` (export pattern matches other `@spaarke/*` libs)
- [ ] `useDocumentActions.ts` moved (verbatim or minimal refactor)
- [ ] SemanticSearch `SearchCommandBar.tsx` imports from `@spaarke/document-operations`
- [ ] SemanticSearch existing tests pass
- [ ] Frontend tests verify shared lib consumers
- [ ] Package builds standalone (`Build-AllClientComponents.ps1`)

**Inputs**: Spec FR-13, FR-14; existing `useDocumentActions.ts`; `@spaarke/*` package conventions

**Outputs**: `@spaarke/document-operations` consumable; SemanticSearch unchanged behavior; Compose ready to consume

### Phase 4: Frontend — SpaarkeAi Compose Surface

**Duration**: 5–7 days · **Depends on**: Phase 0, Phase 3 (must consume shared lib)

**Objectives**:
1. Register `compose-editor` section type in `SECTION_REGISTRY`
2. Build Compose React components (`ComposeWorkspace`, `ComposeToolbar`, `ComposeEmptyState`, `ComposeEditor`)
3. Mount TipTap editor with DOCX bridge from Spike #1
4. Wire three-pane coordination — six TypeScript data contracts compile; receivers stubbed
5. Wire modal entry (Path A) — command-bar "Open in Compose" launches via `launch-resolver.ts`

**Deliverables**:
- [ ] `src/solutions/SpaarkeAi/src/components/compose/ComposeWorkspace.tsx`
- [ ] `src/solutions/SpaarkeAi/src/components/compose/ComposeToolbar.tsx` (Open-in-Word + dispatch-action buttons)
- [ ] `src/solutions/SpaarkeAi/src/components/compose/ComposeEmptyState.tsx`
- [ ] `src/solutions/SpaarkeAi/src/components/compose/ComposeEditor.tsx` (TipTap host)
- [ ] `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/` — Compose section registered
- [ ] Six TypeScript interface files for three-pane data contracts
- [ ] `ConversationPane.tsx` updated to wire JPS scope inputs
- [ ] `launch-resolver.ts` updated for "Open in Compose" command-bar entry
- [ ] UI tests (per `<ui-tests>` blocks in tasks) — Compose mount + editor + dark-mode compliance (ADR-021)

**Inputs**: Spike #1 (DOCX bridge), Spike #2 (data contracts), shared lib from Phase 3, three-pane shell patterns

**Outputs**: Compose layout appears in workspace picker; selecting it mounts editor; modal entry works

### Phase 5: SPE Check-out Lock Mechanism

**Duration**: 2–3 days · **Depends on**: Spike #3, Phase 4

**Objectives**:
1. Acquire SPE check-out on Compose open
2. Detect existing check-out on same user, same document, different tab → show conflict UX
3. Release on close, on idle (default 15 min)
4. Heartbeat from client to extend lock

**Deliverables**:
- [ ] Check-out acquisition path (BFF endpoint + UI flow)
- [ ] Multi-tab conflict UX: "[Go to that session] [Force-close other session and open here]"
- [ ] Heartbeat client-side timer + BFF heartbeat endpoint
- [ ] Orphan-lock auto-release (verified by integration test or manual test with clock)

**Inputs**: Spike #3 outputs (mechanism + interval), spec.md FR-15, FR-16, FR-17

**Outputs**: Check-out visible in Word for Web as "Checked out to {user}"; multi-tab conflict UX appears; orphan releases verified

### Phase 6: E2E Smoke Test + Integration

**Duration**: 2 days · **Depends on**: Phase 1, Phase 2, Phase 4

**Objectives**:
1. Wire Compose UI button (compose-summarize) → BFF `/api/compose/action/compose-summarize` → consumer routing → playbook → response
2. Render playbook result back in Compose
3. Capture integration test for the round-trip

**Deliverables**:
- [ ] End-to-end smoke test passes (manual + automated)
- [ ] Integration test in `tests/integration/Spe.Integration.Tests/` or equivalent
- [ ] Telemetry / log spans verified

**Inputs**: Phase 1 outputs (consumer row), Phase 2 outputs (endpoint), Phase 4 outputs (UI button), Spike #4

**Outputs**: Smoke test green; demo-ready compose-summarize roundtrip

### Phase 7: Testing + Acceptance

**Duration**: 2–3 days · **Depends on**: Phases 2, 3, 4, 5, 6

**Objectives**:
1. Verify all 22 success criteria
2. Unit + integration test coverage for new code per CLAUDE.md §10 #6
3. ADR-038 conformance — no banned test patterns
4. Cross-browser smoke test
5. CVE scan (`dotnet list package --vulnerable --include-transitive`)

**Deliverables**:
- [ ] Test suite green (`dotnet test`)
- [ ] 22 success criteria checked off in README
- [ ] ADR-038 compliance verified (no `Mock<HttpMessageHandler>`, no DI-registration tests, no ctor null-check tests)
- [ ] No new HIGH-severity CVE

**Inputs**: All prior phase outputs

**Outputs**: Project ready for deployment

### Phase 8: Deployment + Wrap-Up

**Duration**: 1–2 days · **Depends on**: Phase 7 green

**Objectives**:
1. Deploy BFF, SpaarkeAi code-page, Dataverse solution artifacts
2. Verify publish-size delta ≤+2 MB compressed
3. Run `/test-diet` gate per CLAUDE.md §7
4. Generate `notes/lessons-learned.md`
5. Update `projects/INDEX.md` last-touched date
6. Archive via `/devops-project-archive`

**Deliverables**:
- [ ] BFF deployed (smoke test green)
- [ ] SpaarkeAi code-page deployed
- [ ] Dataverse rows deployed
- [ ] Publish-size report in PR description
- [ ] `notes/lessons-learned.md` written
- [ ] GitHub Project Issue closed with Closed Date set
- [ ] `projects/INDEX.md` updated

**Inputs**: Phase 7 acceptance complete

**Outputs**: Compose R1 live; project archived

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| **TipTap** (StarterKit + extensions) | New (Spike #1 chooses version) | Med | Open-source; large active community; alternative is Lexical/Slate |
| **DOCX bridge library** | New (Spike #1 chooses) | Med | `prosemirror-docx` candidate; no custom integration constraint |
| **SharePoint Embedded** | GA (Microsoft) | Low | Already in use; Graph API stable |
| **Document Summary playbook** (id `47686eb1-9916-f111-8343-7c1e520aa4df`) | Production (user-confirmed) | Low | Existing — if regressed, breaks smoke test only |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| `ChatSession` + Redis/Cosmos/Dataverse three-tier | `Services/Ai/Chat/`, `Services/Ai/Sessions/` | Production |
| `IConsumerRoutingService` + `IInvokePlaybookAi` | `Services/Ai/PublicContracts/` | Production |
| `GET /api/documents/{id}/open-links` | `Api/FileAccessEndpoints.cs` | Production |
| `DesktopUrlBuilder` | `Spaarke.Core/Utilities/` | Production |
| SpaarkeAi three-pane shell | `src/solutions/SpaarkeAi/` | Production |
| `ConversationPane` | `src/solutions/SpaarkeAi/src/components/conversation/` | Production |
| `@spaarke/auth`, `@spaarke/ui-components` | `src/client/shared/` | Production |
| ADR-013 (refined 2026-05-20) | `docs/adr/` | Current |
| ADR-038 (testing strategy) | `docs/adr/ADR-038-testing-strategy.md` | Current |

---

## 6. Testing Strategy

**Per [ADR-038](../../docs/adr/ADR-038-testing-strategy.md) — integration-heavy pyramid; 6 KEEP categories. Standalone ADR; does NOT supersede ADR-022 (PCF Platform Libraries).**

**Unit Tests** (per CLAUDE.md §10 #6 obligation):
- `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ComposeServiceTests.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ComposeDocumentServiceTests.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ComposeSessionServiceTests.cs`
- Use `TimeProvider` over `Stopwatch`
- Mock at boundaries only (Graph, Dataverse, Redis) — NOT internal collaborators
- **BAN**: `Mock<HttpMessageHandler>`, DI-registration tests, ctor null-check tests

**Integration Tests**:
- `tests/unit/Sprk.Bff.Api.Tests/Api/ComposeEndpointsTests.cs` — 7 endpoints, auth gating + happy path
- E2E compose-summarize roundtrip (Phase 6 deliverable)
- SemanticSearch consumes-from-shared-lib regression (Phase 3)
- SPE check-out lock + multi-tab conflict UX (Phase 5)

**Frontend Tests**:
- `@spaarke/document-operations` consumer tests (vitest)
- Compose components — mount, edit, save, dispatch-action UI flow
- Three-pane data contract TypeScript interfaces (`tsc --noEmit`)

**UI Tests** (per task `<ui-tests>` blocks, executed by `ui-test` skill at task Step 9.7):
- Compose layout appears in workspace picker
- Selecting Compose mounts TipTap editor
- Empty state shows Browse + Search options
- "Open in Word for Web" + "Open in Word Desktop" buttons function
- Dark mode compliance (ADR-021)

**Test-Diet Gate** (per CLAUDE.md §7 + ADR-038):
- At project close, `/test-diet` reconciles all tests added/modified during the project
- MAINTAIN-class tests stay at KEEP path; SCAFFOLDING-class tests deleted; AMBIGUOUS escalated to reviewer
- Output: `notes/test-diet-report.md`

---

## 7. Acceptance Criteria

### Technical Acceptance (Per Phase)

**Phase 0 (Spikes)**:
- [ ] 4 working prototypes published in `notes/spikes/`
- [ ] DOCX subset spec locked (Spike #1)
- [ ] DOCX bridge library + version locked (Spike #1)
- [ ] JPS scope schemas locked (Spike #4)

**Phase 1 (Dataverse + JPS)**:
- [ ] FR-01: `sprk_workspacelayout` Compose row verifiable via Web API
- [ ] FR-08: `compose-selection` + `compose-document` scopes pass `jps-validate`
- [ ] FR-10: `IConsumerRoutingService.ResolveAsync("compose-summarize", ...)` returns playbook id

**Phase 2 (BFF)**:
- [ ] FR-09: `ConsumerTypes.ComposeSummarize` constant compiles
- [ ] FR-21: 7 endpoints respond 200 happy path + 401 unauthenticated
- [ ] FR-22: unit tests exist for all new services and pass
- [ ] NFR-06: BFF compressed publish-size delta ≤+2 MB

**Phase 3 (Shared Lib)**:
- [ ] FR-13: `@spaarke/document-operations` builds standalone
- [ ] FR-14: SemanticSearch tests pass after refactor

**Phase 4 (UI)**:
- [ ] FR-02: TipTap renders in Compose Workspace pane; basic typing/formatting/lists/tables work
- [ ] FR-04: Path A — opening `sprk_document` → "Open in Compose" mounts file
- [ ] FR-18: Empty state shows Browse + Search
- [ ] FR-19: Modal launches with full-screen toggle
- [ ] FR-20: Six TypeScript data contracts compile

**Phase 5 (SPE Lock)**:
- [ ] FR-15: Check-out visible as "Checked out to {user}" in Word for Web
- [ ] FR-16: Multi-tab conflict UX appears
- [ ] FR-17: Orphan locks released after 15 min idle

**Phase 6 (Smoke Test)**:
- [ ] FR-11: End-to-end compose-summarize → Document Summary playbook → result returned

**Phase 7 (Testing)**:
- [ ] All 22 success criteria from spec.md verified
- [ ] No banned test patterns (ADR-038)
- [ ] `dotnet list package --vulnerable --include-transitive` clean

**Phase 8 (Deployment)**:
- [ ] BFF + code-page + Dataverse artifacts deployed
- [ ] `/test-diet` gate passed
- [ ] `lessons-learned.md` written

### Business Acceptance

- [ ] User can open a `sprk_document` in Compose via command-bar "Open in Compose"
- [ ] User can edit the document with TipTap OOB features (basic formatting, headings, lists, tables)
- [ ] User can dispatch the compose-summarize action and see the playbook result
- [ ] User can hand off the document to Word for Web or Word Desktop and return without data loss
- [ ] User cannot open the same document in two tabs without seeing the conflict UX

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | DOCX↔TipTap impedance loses features | Med | Med | Spike #1 validates OOB inventory on real DOCXs; locked subset spec |
| R2 | SPE check-out + heartbeat unreliable (orphan locks) | Low | High | Spike #3 validates flow; configurable interval; integration test |
| R3 | Hot-path collision with active BFF projects (14 active) | High | Med | INDEX.md hot-path declaration + bff-extensions.md checklist + PR sequencing |
| R4 | SpaarkeAi code-page rebuild conflicts (8 active SpaarkeAi projects) | High | Med | Align with workspace-layout pattern; Calendar precedent reference |
| R5 | Publish-size delta exceeds +2 MB / approaches 60 MB ceiling | Low | Med | Measure per BFF task; TipTap + DOCX bridge are client-side |
| R6 | Consumer-routing smoke test fails (Document Summary playbook regression) | Low | High | Spike #4 validates E2E early; user-confirmed playbook exists |
| R7 | Shared-lib extraction regresses SemanticSearch | Low | Med | Refactor SemanticSearch FIRST; tests verify pre-Compose-consume |
| R8 | New HIGH-severity CVE from TipTap or DOCX bridge | Low | Med | `dotnet list package --vulnerable --include-transitive` at task close |
| R9 | Three-pane data contracts ambiguous → rework needed | Med | Low | Spike #2 locks contracts before main implementation |
| R10 | Stale-branch friction (20 work branches with unmerged commits) | High | Low | Coordinate via INDEX.md; `/merge-to-master` audit before Compose merges |

---

## 9. Next Steps

1. **Review this plan.md** with owner and confirm phase ordering / effort estimates
2. **Run `/task-create projects/spaarkeai-compose-r1`** to generate task files from this WBS (auto-invoked by `/project-pipeline` Step 3)
3. **Begin Phase 0 (Spikes)** as Task 001+ — spike outputs are blocking gates for Phases 1–4

---

**Status**: Ready for Tasks
**Next Action**: `/task-create projects/spaarkeai-compose-r1` (or proceed via `/project-pipeline` Step 3)

---

*For Claude Code: This plan provides implementation context. Load relevant phase sections when executing tasks. Phase 0 (Spikes) is the critical-path gate before Phases 1–4 begin.*
