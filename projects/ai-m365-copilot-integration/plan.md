# Project Plan: M365 Copilot Integration (R1)

> **Last Updated**: 2026-03-26
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md)

---

## 1. Executive Summary

**Purpose**: Integrate Spaarke's AI capabilities into M365 Copilot within Power Apps model-driven apps, delivering a Declarative Agent (Tier 1) and Custom Engine Agent (Tier 2) that expose the full BFF API surface. M365 Copilot becomes the general-purpose Spaarke AI surface; SprkChat is repositioned for Analysis Workspace only.

**Scope**:
- Declarative Agent with API Plugin (manifest files + OpenAPI spec)
- Custom Engine Agent with agent gateway adapter endpoints
- Adaptive Card templates and formatter service
- SSO token flow and Bot Service registration
- Enterprise readiness (telemetry, admin controls, BYOK)

**Timeline**: 10-12 weeks | **Target**: June 2026 launch

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-001**: Minimal API pattern for all new endpoints; no Azure Functions
- **ADR-008**: Endpoint filters for authorization; no global middleware
- **ADR-010**: DI minimalism — ≤15 non-framework registrations; concrete types
- **ADR-013**: Extend BFF (not separate service); use AI Tool Framework; flow ChatHostContext
- **ADR-014**: Redis caching; scope keys by tenant; don't cache streaming tokens
- **ADR-015**: Minimum text to AI; log only identifiers/timings; scope by tenant
- **ADR-016**: Rate limit all AI endpoints; bound upstream concurrency; explicit timeouts
- **ADR-019**: ProblemDetails for all errors; stable errorCode; correlation IDs

**From Spec**:
- Agent endpoints are thin adapters — NO new AI orchestration logic
- SPE containers remain `discoverabilityDisabled = true`
- Full BFF API surface exposed via OpenAPI spec
- Long-running playbooks use async pattern or deep-link to Analysis code page

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Declarative Agent + API Plugin (not Copilot Studio) | Three manifest files vs. full CS project; BFF is the brain | Minimal infrastructure; direct HTTPS to BFF |
| Agent gateway as adapter layer | All AI services already exist in BFF | Small new code surface; reuse proven services |
| Adaptive Card schema 1.5 | Maximum supported in Copilot | Design constraint for all card templates |
| Search-by-name as primary doc discovery | File attachments dropped for Custom Engine Agents | Always-works path; attachment is enhancement |
| Async + deep-link for long playbooks | API plugin timeout limits | Users get immediate feedback via deep-link |

### Discovered Resources

**Applicable ADRs**:
- `.claude/adr/ADR-001-minimal-api-and-workers.md` — Minimal API + BackgroundService
- `.claude/adr/ADR-008-endpoint-filters.md` — Endpoint filters for auth
- `.claude/adr/ADR-010-di-minimalism.md` — DI registration constraints
- `.claude/adr/ADR-013-ai-architecture.md` — AI architecture (extend BFF)
- `.claude/adr/ADR-014-ai-caching.md` — AI caching policy
- `.claude/adr/ADR-015-ai-data-governance.md` — Data governance
- `.claude/adr/ADR-016-ai-cost-rate-limits.md` — Cost & rate limiting
- `.claude/adr/ADR-019-problem-details.md` — Error handling

**Applicable Skills**:
- `.claude/skills/azure-deploy/` — Azure infrastructure deployment
- `.claude/skills/bff-deploy/` — BFF API deployment
- `.claude/skills/code-review/` — Code review checklist
- `.claude/skills/adr-check/` — ADR compliance validation

**Architecture Docs**:
- `docs/architecture/AI-ARCHITECTURE.md` — Four-tier AI architecture
- `docs/enhancements/AI-CHAT-STRATEGY-M365-COPILOT-VS-SPRKCHAT.md` — Copilot vs SprkChat positioning
- `docs/architecture/ai-implementation-reference.md` — Working code examples
- `docs/architecture/sdap-auth-patterns.md` — OBO flow, token management
- `docs/architecture/sdap-bff-api-patterns.md` — API endpoint design
- `docs/architecture/office-outlook-teams-integration-architecture.md` — Office/Teams auth patterns

**API Patterns**:
- `.claude/patterns/api/endpoint-definition.md` — Minimal API endpoint structure
- `.claude/patterns/api/endpoint-filters.md` — Authorization filters
- `.claude/patterns/api/error-handling.md` — ProblemDetails pattern
- `.claude/patterns/api/service-registration.md` — DI configuration
- `.claude/patterns/api/background-workers.md` — Job processing

**Auth Patterns**:
- `.claude/patterns/auth/obo-flow.md` — OBO token exchange for Graph
- `.claude/patterns/auth/dataverse-obo.md` — OBO for Dataverse
- `.claude/patterns/auth/oauth-scopes.md` — OAuth scope inventory
- `.claude/patterns/auth/token-caching.md` — Token caching patterns

**AI Patterns**:
- `.claude/patterns/ai/streaming-endpoints.md` — SSE streaming architecture
- `.claude/patterns/ai/analysis-scopes.md` — Actions, Skills, Knowledge config

**Constraints**:
- `.claude/constraints/ai.md` — AI/ML constraints
- `.claude/constraints/api.md` — API/BFF constraints
- `.claude/constraints/auth.md` — Authentication constraints

**Scripts**:
- `scripts/Test-SdapBffApi.ps1` — BFF API endpoint testing
- `scripts/Register-EntraAppRegistrations.ps1` — Entra ID app registrations
- `scripts/Validate-DeployedEnvironment.ps1` — Environment validation

**Related Projects**:
- `projects/sdap-teams-app/spec.md` — Teams app integration (separate project, complementary)

---

## 3. Implementation Approach

### Phase Structure

```
Phase 1: Foundation — Spikes + Declarative Agent MVP (Weeks 1-3)
├─ Technical spikes (ConversationFileReference, Action.Submit, e2e pipeline)
├─ OpenAPI spec for full BFF API surface
├─ Declarative Agent manifest files
├─ M365 Agents Toolkit setup + sideload to dev tenant
└─ Basic Adaptive Card templates

Phase 2: Agent Gateway + Auth (Weeks 3-5)
├─ Agent gateway adapter endpoints on BFF
├─ SSO token flow (M365 → OBO → BFF → Graph/Dataverse)
├─ Azure Bot Service registration
├─ SpaarkeAgentHandler (M365 Agents SDK)
└─ Multi-turn conversation support

Phase 3: Rich Interactions + Playbook Integration (Weeks 5-8)
├─ Full playbook invocation flow (search → select → execute → results)
├─ Adaptive Card formatter service
├─ All Adaptive Card templates (8+ types)
├─ Deep-link handoff to Analysis Workspace + wizard Code Pages
├─ Email drafting via communications module
└─ Async pattern for long-running playbooks

Phase 4: Enterprise Readiness (Weeks 8-12)
├─ Error handling and graceful degradation
├─ Telemetry and interaction logging
├─ Admin controls (playbook visibility, per-role restrictions)
├─ BYOK Bicep deployment templates
├─ Documentation (admin guide, user guide)
└─ Project wrap-up
```

### Critical Path

**Blocking Dependencies:**
- Phase 1 spikes BLOCK all subsequent Adaptive Card and file attachment work
- OpenAPI spec BLOCKS Declarative Agent testing (Phase 1 internal dependency)
- SSO token flow (Phase 2) BLOCKS all authenticated BFF calls from agent
- Agent gateway endpoints (Phase 2) BLOCK playbook invocation flow (Phase 3)

**High-Risk Items:**
- Spike 1 (ConversationFileReference) — determines entire file attachment UX
- Spike 2 (Action.Submit) — determines playbook selection UX pattern
- SSO/OBO token flow — multi-hop auth is historically fragile

### Parallel Execution Strategy

Tasks are designed for concurrent execution via Claude Code task agents. Each parallel group contains tasks that modify different files and have no dependencies on each other.

```
SERIAL: Tasks 001-003 (foundation — sequential, each builds on prior)

PARALLEL GROUP A: Tasks 004, 005, 006 (Phase 1 spikes — independent investigations)

SERIAL: Task 007 (OpenAPI spec — depends on spike results)

PARALLEL GROUP B: Tasks 008, 009, 010 (Declarative Agent manifests — independent files)

SERIAL: Task 011 (sideload + validate — depends on manifests)

PARALLEL GROUP C: Tasks 012, 013, 014 (Phase 2 — gateway, auth, bot service — different layers)

PARALLEL GROUP D: Tasks 015, 016, 017 (Phase 2 — handler, conversation, integration test)

PARALLEL GROUP E: Tasks 018-024 (Phase 3 — card templates, formatter, handoff, email, async — independent features)

PARALLEL GROUP F: Tasks 025-030 (Phase 4 — error handling, telemetry, admin, BYOK, docs — independent concerns)

SERIAL: Task 031 (project wrap-up)
```

---

## 4. Phase Breakdown

### Phase 1: Foundation — Spikes + Declarative Agent MVP (Weeks 1-3)

**Objectives:**
1. Validate platform capabilities via technical spikes
2. Create OpenAPI spec exposing full BFF API surface
3. Build Declarative Agent manifest files
4. Sideload agent to dev tenant and validate basic functionality

**Deliverables:**
- [ ] Spike 1 results: ConversationFileReference validation
- [ ] Spike 2 results: Action.Submit in API plugin responses
- [ ] Spike 3 results: End-to-end file pipeline validation
- [ ] `spaarke-bff-openapi.yaml` — Full BFF API OpenAPI spec
- [ ] `declarativeAgent.json` — Agent manifest with instructions
- [ ] `spaarke-api-plugin.json` — Function definitions
- [ ] Teams app manifest for deployment
- [ ] Basic Adaptive Card templates (document list, matter summary)
- [ ] Agent sideloaded to dev tenant

**Critical Tasks:**
- Spikes MUST BE FIRST — results inform all subsequent design decisions
- OpenAPI spec BLOCKS Declarative Agent (API plugin references it)

**Inputs**: spec.md, design.md, existing BFF endpoint definitions

**Outputs**: Manifest files, OpenAPI spec, spike results documentation

### Phase 2: Agent Gateway + Auth (Weeks 3-5)

**Objectives:**
1. Build agent gateway adapter endpoints on BFF API
2. Implement SSO token flow for M365 agent authentication
3. Register Azure Bot Service and configure channels
4. Build SpaarkeAgentHandler for M365 Agents SDK

**Deliverables:**
- [ ] `POST /api/agent/message` — Agent gateway endpoint
- [ ] `GET /api/agent/playbooks` — Available playbooks endpoint
- [ ] `POST /api/agent/run-playbook` — Playbook invocation endpoint
- [ ] SSO/OBO token flow working end-to-end
- [ ] Azure Bot Service registered with Teams + Copilot channels
- [ ] `SpaarkeAgentHandler` processing MessageActivity and InvokeActivity
- [ ] Multi-turn conversation with session management
- [ ] Integration tests for agent gateway

**Inputs**: Phase 1 outputs, existing BFF services, auth patterns

**Outputs**: Working agent gateway, authenticated BFF access from M365

### Phase 3: Rich Interactions + Playbook Integration (Weeks 5-8)

**Objectives:**
1. Full playbook invocation flow from Copilot
2. Complete set of Adaptive Card templates
3. Deep-link handoff to Analysis Workspace and wizards
4. Email drafting capability
5. Async pattern for long-running playbooks

**Deliverables:**
- [ ] Document search → playbook menu → execution → results (full flow)
- [ ] AdaptiveCardFormatterService
- [ ] All 8+ Adaptive Card templates (per design mockups)
- [ ] HandoffUrlBuilder for Analysis Workspace and wizard deep-links
- [ ] Email drafting via communications module
- [ ] Async playbook pattern (job ID + status endpoint or deep-link)
- [ ] Playbook library browse
- [ ] End-to-end integration tests

**Inputs**: Phase 2 outputs, existing playbook services, wizard Code Page URLs

**Outputs**: Feature-complete Copilot integration

### Phase 4: Enterprise Readiness (Weeks 8-12)

**Objectives:**
1. Production-grade error handling and graceful degradation
2. Telemetry and interaction logging
3. Admin controls for playbook visibility and role restrictions
4. BYOK deployment templates
5. Documentation

**Deliverables:**
- [ ] Error handling for all failure modes (BFF down, token failure, playbook timeout)
- [ ] Telemetry: interaction types, playbook counts, handoff events, latencies
- [ ] Admin configuration entity in Dataverse for Copilot settings
- [ ] BYOK Bicep templates for customer-hosted deployment
- [ ] Admin guide, user guide, troubleshooting documentation
- [ ] All graduation criteria met

**Inputs**: Phase 3 outputs, BYOK infrastructure patterns

**Outputs**: Production-ready M365 Copilot integration

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| M365 Copilot GA in MDA (Apr 13) | Scheduled | Low | Custom Engine Agent works independently |
| M365 Agents SDK (GA) | Released | Low | Proven SDK |
| M365 Agents Toolkit | Released | Low | VS Code extension for packaging |
| Azure Bot Service | GA | Low | Standard service |
| Adaptive Card schema 1.5 | GA | Low | Design within constraints |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| BFF AI endpoints | `src/server/api/Sprk.Bff.Api/Features/Ai/` | Production |
| PlaybookExecutionEngine | `src/server/api/Sprk.Bff.Api/Features/Ai/` | Production |
| ChatContextMappingService | `src/server/api/Sprk.Bff.Api/Features/Ai/Chat/` | Production |
| OBO token pattern | `src/server/api/Sprk.Bff.Api/Infrastructure/Auth/` | Production |
| Wizard Code Pages | `src/client/code-pages/` | Deployed |
| Azure OpenAI + AI Search | Azure subscription | Provisioned |

---

## 6. Testing Strategy

**Unit Tests** (80% coverage target):
- Agent gateway adapter endpoints
- AdaptiveCardFormatterService
- HandoffUrlBuilder
- SSO/OBO token exchange
- PlaybookMenuService

**Integration Tests**:
- Agent gateway → existing BFF services (search, playbooks, chat)
- SSO token flow end-to-end (M365 → BFF → Graph → SPE)
- Adaptive Card rendering validation
- Multi-turn conversation session management

**E2E Tests**:
- Sideload Declarative Agent → query matters → verify Adaptive Card response
- Document search → playbook selection → execution → results card
- Handoff deep-link → Analysis Workspace opens with correct context
- BYOK deployment → verify agent works against customer infrastructure

**Spike Validation**:
- ConversationFileReference: attach file → inspect reference → attempt Graph retrieval
- Action.Submit: return card with buttons → click → verify invocation
- End-to-end pipeline: attach → retrieve → analyze → card result

---

## 7. Acceptance Criteria

### Technical Acceptance

**Phase 1:**
- [ ] OpenAPI spec covers all BFF API endpoints (count matches inventory)
- [ ] Declarative Agent loads in MDA Copilot side pane
- [ ] Conversation starters appear with Spaarke identity
- [ ] All three spikes documented with findings

**Phase 2:**
- [ ] Agent gateway endpoints respond to authenticated requests
- [ ] SSO/OBO token flow works without user re-authentication
- [ ] SpaarkeAgentHandler processes MessageActivity correctly
- [ ] Multi-turn conversations maintain context

**Phase 3:**
- [ ] All 8 use cases (UC-M1 through UC-M8) work end-to-end
- [ ] Adaptive Cards render correctly in Copilot side pane
- [ ] Handoff deep-links open correct context in Analysis Workspace
- [ ] Long-running playbooks return async status or deep-link

**Phase 4:**
- [ ] Graceful degradation when BFF is unavailable
- [ ] Telemetry captures interaction metrics (no content logged)
- [ ] Admin can toggle playbook visibility
- [ ] BYOK Bicep templates deploy successfully

### Business Acceptance

- [ ] Users can search documents, run playbooks, and query matters from Copilot
- [ ] Authorization model enforced (users see only permitted documents)
- [ ] Handoff to Analysis Workspace works seamlessly
- [ ] BYOK customers can deploy in their Azure subscription

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | OBO token flow complexity | Medium | High | Reuse proven pattern from Outlook add-in |
| R2 | Adaptive Card limitations | High | Medium | "Light interaction + handoff" pattern |
| R3 | Customer confusion (two AI surfaces) | Medium | Medium | Clear UX positioning + handoff |
| R4 | API plugin response timeout | Medium | Medium | Async pattern + deep-link fallback |
| R5 | OpenAPI spec maintenance burden | High | Low | Auto-generate from code annotations |
| R6 | ConversationFileReference doesn't work | Medium | Low | Search-by-name fallback (always works) |
| R7 | Action.Submit broken in Copilot | Low | Medium | Text-based fallback for playbook selection |

---

## 9. Next Steps

1. **Generate task files** — Decompose phases into executable POML tasks
2. **Execute Phase 1 spikes** — Validate platform capabilities first
3. **Build OpenAPI spec** — Foundation for Declarative Agent
4. **Parallel execution** — Independent tasks run concurrently via task agents

---

**Status**: Ready for Tasks
**Next Action**: Generate task files via task-create

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks.*
