# Architecture Decision Records (ADRs)

> **Purpose**: Documents architectural decisions for the Spaarke platform
> **Audience**: Developers, architects, AI coding agents
> **Last Updated**: 2026-07-25 (ai-advanced-capabilities-nda-r1 task 001 — reconciled index against `docs/adr/` files: added 11 missing rows 031/032/033/039/040/041/042/043/044/046/047; fixed the mislabeled ADR-030 row + broken link)

## About ADRs

Architecture Decision Records capture important architectural decisions made during the development of Spaarke, including the context, decision, and consequences.

## ADR Index

| ADR | Title | Domain | Status |
|-----|-------|--------|--------|
| [ADR-001](ADR-001-minimal-api-and-workers.md) | Minimal API and BackgroundService Workers | Backend | Accepted |
| [ADR-002](ADR-002-no-heavy-plugins.md) | Thin Dataverse Plugins | Dataverse | Accepted |
| [ADR-003](ADR-003-lean-authorization-seams.md) | Lean Authorization Seams | Security | Accepted |
| [ADR-004](ADR-004-async-job-contract.md) | Async Job Contract | Backend | Accepted |
| [ADR-005](ADR-005-flat-storage-spe.md) | Flat Storage for SPE | Storage | Accepted |
| [ADR-006](ADR-006-prefer-pcf-over-webresources.md) | Anti-Legacy-JS: PCF for Form Controls, React Code Pages for Dialogs | Frontend | Accepted |
| [ADR-007](ADR-007-spe-storage-seam-minimalism.md) | SPE Storage Seam Minimalism | Storage | Accepted |
| [ADR-008](ADR-008-authorization-endpoint-filters.md) | Authorization Endpoint Filters | Security | Accepted |
| [ADR-009](ADR-009-caching-redis-first.md) | Redis-First Caching Strategy | Caching | Accepted |
| [ADR-010](ADR-010-di-minimalism.md) | Dependency Injection Minimalism | Backend | Accepted |
| [ADR-011](ADR-011-dataset-pcf-over-subgrids.md) | Dataset PCF for Form-Embedded List Controls | Frontend | Accepted |
| [ADR-012](ADR-012-shared-component-library.md) | Shared Component Library | Frontend | Accepted |
| [ADR-013](ADR-013-ai-architecture.md) | AI Tool Framework Architecture | AI | Accepted |
| [ADR-014](ADR-014-ai-caching-and-reuse-policy.md) | AI Caching and Reuse Policy | AI | Accepted |
| [ADR-015](ADR-015-ai-data-governance.md) | AI Data Governance | AI | Accepted |
| [ADR-016](ADR-016-ai-cost-rate-limit-and-backpressure.md) | AI Cost, Rate Limits, Backpressure | AI | Accepted |
| [ADR-017](ADR-017-async-job-status-and-persistence.md) | Async Job Status and Persistence | Backend | Accepted |
| [ADR-018](ADR-018-feature-flags-and-kill-switches.md) | Feature Flags and Kill Switches | Operations | Accepted |
| [ADR-019](ADR-019-api-errors-and-problemdetails.md) | API Errors and ProblemDetails | Backend | Accepted |
| [ADR-020](ADR-020-versioning-strategy-apis-jobs-client-packages.md) | Versioning Strategy | Operations | Accepted |
| [ADR-021](ADR-021-fluent-ui-design-system.md) | Fluent UI v9 Design System | **UI/UX** | **Accepted** |
| [ADR-022](ADR-022-pcf-platform-libraries.md) | PCF Platform Libraries — Field-Bound Controls Only | Frontend | Accepted |
| [ADR-023](ADR-023-choice-dialog-pattern.md) | Choice Dialog Pattern | Frontend | Accepted |
| [ADR-026](ADR-026-full-page-custom-page-standard.md) | Full-Page Custom Page Standard | Frontend | Accepted |
| [ADR-027](ADR-027-subscription-isolation-and-dataverse-solution-management.md) | Subscription Isolation & Dataverse Solution Management | Operations | Proposed |
| [ADR-029](ADR-029-bff-publish-hygiene.md) | BFF Publish Hygiene (framework-dependent linux-x64, sourcemap exclusion, transitive CVE overrides, size baseline) | Backend / Operations | Accepted |
| [ADR-030](ADR-030-pane-event-bus.md) | PaneEventBus — Typed Multi-Subscriber Cross-Pane Communication (typed channel for cross-pane/widget messaging, e.g. `widget_load`; the bus advisory/compose events ride) | AI / Frontend | Accepted |
| [ADR-031](ADR-031-stage-lifecycle.md) | Stage Lifecycle Pattern (Pane / Code Page Shell) | Frontend | Accepted |
| [ADR-032](ADR-032-bff-nullobject-kill-switch.md) | BFF Null-Object Kill-Switch Pattern (conditional service consumed by an unconditional endpoint → Null-Object in the else-branch, P1/P2/P3; `FeatureDisabledException` → 503 ProblemDetails; closes RB-T028 cluster) | Backend / API | Accepted |
| [ADR-033](ADR-033-streaming-chat-tool-side-channel.md) | Streaming Chat Tool Side-Channel (doc-stream SSE side-channel for streaming compose edits/redlines to the client) | AI / BFF / FE | Accepted |
| [ADR-034](ADR-034-user-record-membership.md) | User-Record Membership Resolution Pattern (discovery-based `MembershipResolverService` + 6-path identity normalization + Phase 2 junction table `sprk_userentityassociation` + Service Bus topic `sprk-membership-changes`; `LookupUserMembership` playbook node ActionType=52; closes A1/D5 root cause from R2 UAT) | Backend / AI / Dataverse | Accepted (R3 Part 1, 2026-06-21) |
| [ADR-036](ADR-036-background-job-infrastructure.md) | Background-Job Infrastructure (Spaarke.Scheduling — shared lib + `IScheduledJob` contract + `ScheduledJobHost` + Cronos cron parsing + `sprk_backgroundjob*` Dataverse entities + `/api/admin/jobs/*` admin surface; two reference consumers: `MembershipReconciliationJob` + migrated `PlaybookSchedulerJob`) | Backend / Scheduling | Accepted (R3 Part 2, 2026-06-21) |
| [ADR-037](ADR-037-multinode-output-composition.md) | Multi-Node Output Composition (NodeType.DeliverComposite + ActionType.DeliverComposite=42; per-section SSE streaming; FE widget rework to sections-by-name; reduces 5 brittle coordination points to 2) | AI / BFF / FE | Accepted (chat-routing-redesign-r1 Phase 5R Wave 5-C, 2026-06-25) |
| [ADR-038](ADR-038-testing-strategy.md) | Testing Strategy — Integration-heavy pyramid, 6 KEEP path categories as MUST rules (auth/regression/data-mutation/tenant/contract/domain), coverage as observation never gate, ban Mock&lt;HttpMessageHandler&gt; + DI-registration + ctor null-check tests, TimeProvider over Stopwatch. **STANDALONE — does NOT supersede ADR-022 (PCF Platform Libraries — unrelated frontend scope).** | Testing / Backend | Accepted (ci-cd-unit-test-remediation-r1 Phase 1 Stream B, 2026-06-26) |
| [ADR-039](ADR-039-grounded-execution-closed-catalogs.md) | Grounded Execution & Closed Catalogs (ONE dispatch protocol — Event/Click/Text; TWO closed catalogs — Actions+Bindings, Tools; every output grounded; control-flow-is-code, behavior-is-data). **Amended 2026-07-25**: Output Determinism Modes — `fact` (default, deterministic) vs `advisory` (probabilistic, reasoning depth + Reasoning tier, still prompt-controlled + schema-validated + source-cited) refining invariant (a) | AI | Accepted (2026-07-05; amended 2026-07-25) |
| [ADR-040](ADR-040-session-ledger.md) | Session Ledger — append-only addressable typed per-session ledger over the 3-tier store; **storage precedes rendering** (store-before-render); disposition is the only rendering contract; no second session store | AI | Accepted (2026-07-05) |
| [ADR-041](ADR-041-judgment-confirmation-completion-policy.md) | Judgment, Confirmation & Completion Policy — resourcefulness (reads free / writes gated) + confirmation-as-deterministic (risk-tier × origin × completeness; risk is catalog data, never runtime LLM judgment) + completion/OutcomeCard composed after the ledger write | AI | Proposed |
| [ADR-042](ADR-042-memory-architecture-governance.md) | Memory Architecture & Governance — Record + User memory scopes; per-fact docs with deterministic id upsert-by-(Type,Key) supersession; governance envelope (provenance/retention TTL); `memory-items` container partitioned by subject (never tenantId) | AI | Proposed |
| [ADR-043](ADR-043-ai-capability-execution-spine.md) | AI Capability Execution Spine — three execution surfaces converging at one disposition→ledger→OutcomeCard layer; converge the two completion engines onto ONE input model (ContextBinder → ContextEnvelope); single-source DispositionRoutability registry; vertical-slice `tests/integration/seam/**` = definition-of-done | AI / BFF | Proposed |
| [ADR-044](ADR-044-dataverse-guid-canonicalization.md) | Dataverse GUID Canonicalization at System Boundaries — canonicalize every Dataverse GUID to bare-lowercase at every boundary (`@odata.bind`, AI Search key/filter, Xrm ingestion) via the shared `cleanGuid`; codifies FAILURE-MODES AP-3 (case→AI Search) + AP-6 (braces→`@odata.bind`) | Dataverse / Integration | Accepted |
| [ADR-045](ADR-045-communication-architecture.md) | Communication Architecture — canonical `<EmailComposer/>` send (1 engine + 3 wrappers + `sendCommunication()`) + Association Engine over a normalized envelope (8 targets; deterministic-first rungs 0–3 then semantic 4 + AI 5; confidence + provenance) + direction-agnostic `ICommunicationEnrichmentService` (both directions, adds outbound RAG) + channel seams (email impl only); auto-file ON deterministic ≥0.85 (ADR-018 kill-switch; AI rungs never auto-file); org target `sprk_organization`; retires OOB-`email`/SSS. Supersedes the never-written R3 "ADR-033" plan | Communication / BFF / FE | Accepted (email-communication-solution-r4, 2026-07-14) |
| [ADR-046](ADR-046-acs-messaging-channel.md) | ACS Messaging Channel — Transport, First-Class Threads, Inbound Ingestor Seam (ACS Chat as transport + `sprk_communication` type=Message; server-side token minting; `ICommunicationChannelIngestor` seam completing ADR-045's inbound leg; first-class `sprk_communicationthread`; R1 = server plumbing + polling MDA UI, no client ACS SDK) | Communication | Accepted (messaging-communication-app-r1, 2026-07-16) |
| [ADR-047](ADR-047-notification-action-spine.md) | Notification & Action Spine — ONE server-initiated **typed-signal → grounded-action → delivery** spine (Layers A–D) collapsing the email-r4/messaging-r3/assistant-r1 push forks; Azure SignalR Serverless + poll fallback; six commitments (typed signals · shared domain actions · per-source policy · SSE-as-presentation · outbox-before-ping · dumb-transport) | Communication / BFF / FE | Proposed |
| [ADR-048](ADR-048-communication-participant-index.md) | Communication Participant Index — message-grain `sprk_communicationparticipant` junction (parent = `sprk_communication`; thread participation derived by rollup, no thread-grain rows) making participants queryable where `;`-joined `sprk_from/to/cc` TEXT cannot; identity = two typed nullable lookups `sprk_systemuser` XOR `sprk_contact` (**ADR-034 path-C comply-with-intent** — 2 targets not 6, so typed lookups honor the tuple's intent + add FK integrity & DataGrid chip auto-derivation, not an amendment, not polymorphic, no text-name matching); unresolved external rows first-class (`sprk_isresolved=false` + `sprk_addresstext`); `sprk_role` {From/To/Cc/Bcc}; populated by reusing `ParticipantCorrelationRung` (no new resolver/AI/SDK), best-effort + idempotent; powers `participant=` (FR-02) via the R1 impersonation + 2-rule filter. ADR-047 reserved for notification-spine (not claimed) | Communication / BFF / Data | Accepted (messaging-communication-app-r2, 2026-07-19) |

## ADRs by Domain

### Frontend / UI

| ADR | Summary |
|-----|---------|
| **[ADR-021](ADR-021-fluent-ui-design-system.md)** | **Authoritative UI/UX standard: Fluent v9, dark mode, accessibility. React version differs by surface — PCF: 16/17 (platform); Code Pages: 18 (bundled).** |
| [ADR-006](ADR-006-prefer-pcf-over-webresources.md) | Anti-legacy-JS: PCF for form-bound controls; React Code Pages for standalone dialogs/pages. No new legacy JS webresources. |
| [ADR-011](ADR-011-dataset-pcf-over-subgrids.md) | Dataset PCF for list-based UI embedded on forms. Standalone dialogs with list/search UI → React Code Page. |
| [ADR-012](ADR-012-shared-component-library.md) | Shared component library (`@spaarke/ui-components`): peerDependencies `>=16.14.0` to support both PCF (React 16/17) and Code Pages (React 18). Includes WizardDialog and SidePanel. |
| [ADR-022](ADR-022-pcf-platform-libraries.md) | PCF controls (field-bound only): React 16/17 APIs, platform-provided. React Code Pages use React 18 bundled — NOT subject to this ADR. |

### Backend / API

| ADR | Summary |
|-----|---------|
| [ADR-001](ADR-001-minimal-api-and-workers.md) | .NET Minimal API with BackgroundService |
| [ADR-004](ADR-004-async-job-contract.md) | Async job patterns |
| [ADR-010](ADR-010-di-minimalism.md) | Lean DI registrations |
| [ADR-017](ADR-017-async-job-status-and-persistence.md) | Job status persistence |
| [ADR-019](ADR-019-api-errors-and-problemdetails.md) | ProblemDetails for errors |
| [ADR-029](ADR-029-bff-publish-hygiene.md) | BFF publish hygiene: framework-dependent linux-x64, sourcemap exclusion, surgical transitive CVE overrides, size baseline ratchet |
| [ADR-032](ADR-032-bff-nullobject-kill-switch.md) | BFF Null-Object kill-switch pattern: conditional service consumed by unconditional endpoint → Null-Object in else-branch (P1/P2/P3); `FeatureDisabledException` → 503 ProblemDetails per ADR-018/019 |
| [ADR-043](ADR-043-ai-capability-execution-spine.md) | AI capability execution spine: three surfaces → one disposition→ledger→OutcomeCard layer; ContextBinder→ContextEnvelope; seam test = DoD |

### AI Features

| ADR | Summary |
|-----|---------|
| [ADR-013](ADR-013-ai-architecture.md) | AI Tool Framework architecture |
| [ADR-014](ADR-014-ai-caching-and-reuse-policy.md) | AI response caching |
| [ADR-015](ADR-015-ai-data-governance.md) | AI data handling rules |
| [ADR-016](ADR-016-ai-cost-rate-limit-and-backpressure.md) | AI cost controls, rate limits, model-tier selection |
| [ADR-033](ADR-033-streaming-chat-tool-side-channel.md) | Streaming chat tool side-channel (doc-stream SSE) |
| [ADR-037](ADR-037-multinode-output-composition.md) | Multi-node output composition; per-section SSE streaming keyed by section name |
| [ADR-039](ADR-039-grounded-execution-closed-catalogs.md) | Grounded execution & closed catalogs; ONE dispatch protocol; **amended 2026-07-25** — `fact` vs `advisory` output determinism modes |
| [ADR-040](ADR-040-session-ledger.md) | Session ledger; store-before-render; disposition = only rendering contract |
| [ADR-041](ADR-041-judgment-confirmation-completion-policy.md) | Judgment/confirmation/completion; confirmation-as-deterministic; OutcomeCard |
| [ADR-042](ADR-042-memory-architecture-governance.md) | Memory architecture & governance; Record + User scopes; per-fact upsert |
| [ADR-043](ADR-043-ai-capability-execution-spine.md) | AI capability execution spine; ContextBinder→ContextEnvelope; DispositionRoutability registry |
| [ADR-030](ADR-030-pane-event-bus.md) | PaneEventBus — typed cross-pane/widget channel (`widget_load`, advisory/compose events) |

### Security

| ADR | Summary |
|-----|---------|
| [ADR-003](ADR-003-lean-authorization-seams.md) | Authorization patterns |
| [ADR-008](ADR-008-authorization-endpoint-filters.md) | Endpoint filter authorization |

### Storage / Caching

| ADR | Summary |
|-----|---------|
| [ADR-005](ADR-005-flat-storage-spe.md) | SPE flat storage model |
| [ADR-007](ADR-007-spe-storage-seam-minimalism.md) | Storage abstraction |
| [ADR-009](ADR-009-caching-redis-first.md) | Redis-first caching |

### Dataverse

| ADR | Summary |
|-----|---------|
| [ADR-002](ADR-002-no-heavy-plugins.md) | Thin plugins, no HTTP calls |
| [ADR-044](ADR-044-dataverse-guid-canonicalization.md) | Canonicalize Dataverse GUIDs to bare-lowercase at every boundary (`@odata.bind`, AI Search, Xrm) via `cleanGuid` |

### Communication

| ADR | Summary |
|-----|---------|
| [ADR-045](ADR-045-communication-architecture.md) | Canonical `<EmailComposer/>` send + Association Engine (normalized envelope; deterministic-first rungs then semantic + AI); auto-file ON deterministic ≥0.85 |
| [ADR-046](ADR-046-acs-messaging-channel.md) | ACS Messaging Channel — transport + `sprk_communication` (Message) + `ICommunicationChannelIngestor` seam + first-class threads |
| [ADR-047](ADR-047-notification-action-spine.md) | Notification & Action Spine — one typed-signal → grounded-action → delivery spine (SignalR Serverless + poll fallback; outbox-before-ping) |
| [ADR-048](ADR-048-communication-participant-index.md) | Communication Participant Index — message-grain junction; typed `sprk_systemuser` XOR `sprk_contact` lookups; powers `participant=` |

### Operations

| ADR | Summary |
|-----|---------|
| [ADR-018](ADR-018-feature-flags-and-kill-switches.md) | Feature flag patterns |
| [ADR-020](ADR-020-versioning-strategy-apis-jobs-client-packages.md) | Versioning strategy |
| [ADR-027](ADR-027-subscription-isolation-and-dataverse-solution-management.md) | Subscription isolation, managed solutions, Dataverse CI/CD |

---

## For AI Agents

**Concise versions**: AI-optimized summaries (~100-150 lines) are in `.claude/adr/`

**When to load ADRs**:
- Before creating new components
- When making architectural changes
- To understand constraints and patterns

**Key ADRs for UI work**:
- Always load [ADR-021](ADR-021-fluent-ui-design-system.md) for any UI component work (design system, theming, React version by surface).
- Load [ADR-006](ADR-006-prefer-pcf-over-webresources.md) when choosing between PCF and React Code Page.
- Load [ADR-022](ADR-022-pcf-platform-libraries.md) when working on PCF controls (field-bound only).

**Two-tier UI architecture** (ADR-006):
- Field-bound form controls → **PCF** (`src/client/pcf/`) — React 16/17, platform-provided
- Standalone dialogs/pages → **React Code Page** (`src/client/code-pages/`) — React 18, bundled
- Shared components → `src/client/shared/Spaarke.UI.Components/` — both surfaces

---

## Related Resources

- [ADR Validation Process](ADR-VALIDATION-PROCESS.md)
- [README - How to Write ADRs](README-ADRs.md)

