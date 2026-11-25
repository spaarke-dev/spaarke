# Project Plan: Spaarke Intake Pipeline R1

> **Last Updated**: 2026-11-25
> **Status**: Ready for Tasks
> **Spec**: `spec.md` (in the project folder) · Design: `design.md` v0.3 · Reuse: `notes/reuse-scan.md`

---

## 1. Executive Summary

**Purpose**: Build one request pipeline. A door creates a request, and one BFF orchestrator records each pass as a run. A `requestType`-keyed harness does the work, an attorney approves it, and the result returns through the originating door. NDA review is the first harness (the accelerator's engines in a Container Apps Job) and invoices are the second; the design §10.1 defects are fixed along the way.

**Scope**:
- Phase 0: engine-hosting spike, app-only edit plan, cleanup PR, ADR-004 A2.
- Phase 1: records, orchestrator, routing key and harness registry, NDA harness, triage, approval, status emails, Front Door and Console doors, run admin, status API.
- Phase 2: intake mailbox, the Job B "Submit as request" proposal, the #1920 producer, the threaded email return, the MCP tools.
- Phase 3: invoice harness (after eBilling UAT).

**Timeline**: 4 phases, about 36 tasks | **Estimated Effort**: 110–140 hours. Phase 3 waits on an external gate (eBilling UAT).

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply; concise copies in `.claude/adr/`):

*Jobs, runs and placement*
- **ADR-004** (+A1, **A2**): Job Contract envelope, atomic idempotency, ids-only payloads. A2 covers consumers outside the BFF (the engine job).
- **ADR-052**: workload placement. The orchestrator stays in the BFF (written reason, design §4); the Container Apps Job is owner-approved (design §5).
- **ADR-017**: job status reads. **ADR-031**: stage lifecycle semantics for run stages.
- **ADR-036**: scheduled jobs (this project changes only the `sprk_backgroundjob` description).

*Data and write path*
- **ADR-001/008/010/019**: Minimal API, endpoint filters, DI minimalism, ProblemDetails.
- **ADR-002**: no plugins. The request and run invariants have one server-side owner in the BFF write path.
- **ADR-007**: SPE storage of outputs. **ADR-005**: flat storage for staging.
- **ADR-044**: GUID canonicalization for correlation ids and keys.

*AI*
- **ADR-013/014/016**: AI through the PublicContracts facade, AI caching, AI rate limits.
- **ADR-015**: data governance. No content in logs or messages; staging stays private and expires on a TTL.
- **ADR-039**: catalogued Actions only, Event-path budget (the per-sender cap), confirmation before a side effect.
- **ADR-049 I-5/I-7**: Compose-scoped (the tension is Path C; confirm in `/adr-check`).

*Auth and access*
- **ADR-003/034**: authorization seams and record membership for the approve and status endpoints.
- **ADR-028** (A4/A5): app-only identity, and the managed identity for the container.

*Decisions and communication*
- **ADR-040/041**: the Decision Record ledger; attorney approval as judgment confirmation.
- **ADR-043/045/047/048**: communication channels, server send, the notification spine, the participant index.

*Flags, UI and publish*
- **ADR-018/032**: feature flags and kill switches per door and per harness.
- **ADR-012**: shared UI components for the Console card.
- **ADR-029**: BFF publish hygiene.

**From Spec**:
- NFR-01: at-least-once safety; resume never re-runs AI.
- NFR-02: fail-closed authorization.
- NFR-04: per-sender cap.
- NFR-08: kill switches.
- NFR-09: pinned NDA standard version.
- NFR-10: synthetic NDAs only.
- No edits to `Services/Finance/Submission/` before eBilling UAT sign-off.
- Engines are used from `src/accelerators/core/engines/` on master (after #3163), never copied (Python 3.11, stdlib + `lxml`).

### Key Technical Decisions

| Decision | Rationale | Impact |
|---|---|---|
| Orchestrator = BFF `IJobHandler` + `sprk_processingjob` | Reuses the jobs framework and run record; ADR-052 written reason (design §4) | No Durable Task; stages are idempotent and resumable |
| Engines in a Container Apps Job (Python) | Runs the accelerator's tested engines unchanged; owner-approved (design §5, Q3) | First Container Apps resource; ADR-004 A2; CI image build + scan |
| Claim-check staging in blob | ADR-004 payload rule; documents exceed message limits | Stamp-private container, 7-day lifecycle TTL |
| `requestType` routing key on `RoutingContext` | Extends the existing routing contract (reuse-scan row 3) | Harness registry is keyed by it |
| One approve/reject endpoint for the Console and the MDA form | One owner for the decision invariant (ADR-002) | Ribbon script and Console card call the same endpoint |
| Email return as a threaded shared-mailbox reply | `SendMode.User` needs an in-request `HttpContext`; owner Q5 | `createReply` threading added in FR-03 |
| Suggestion = a Job B proposal kind | Owner reuse decision; no third suggest surface | Extends `CommunicationProposalApplyService` |

### Discovered Resources

**Applicable Skills**:

*Dataverse*
- `.claude/skills/dataverse-create-schema/`: columns on `sprk_servicerequest`, `sprk_processingjob` and `sprk_communicationaccount`.
- `.claude/skills/dataverse-deploy/`: solution, form and ribbon deploy.
- `.claude/skills/ribbon-edit/`: Approve/Reject command on the service-request form.

*AI and MCP*
- `.claude/skills/jps-action-create/`, `.claude/skills/jps-validate/`: the agreement-classify / agreement-review Actions.
- `.claude/skills/mcp-tool-handler/`: `spaarke_*` submit and status tools.

*Deploy*
- `.claude/skills/bff-deploy/`: BFF to dev (the only allowed path).
- `.claude/skills/azure-deploy/`: Bicep (Container Apps environment and Job, queues, storage lifecycle).
- `.claude/skills/code-page-deploy/`: Console changes.

*UI and CI*
- `.claude/skills/fluent-v9-component/`: the Console approval card and the submit dialog.
- `.claude/skills/ui-test/`: browser checks of the Console and the form.
- `.claude/skills/ci-cd/`: the engine image build and scan workflow.

*Gates and reuse*
- `.claude/skills/adr-check/`, `.claude/skills/code-review/`: quality gates.
- `.claude/skills/script-aware/`: find existing scripts first.

**Knowledge Articles**:

*Jobs and runs*
- `docs/architecture/jobs-architecture.md`: `IJobHandler`, Service Bus, idempotency, DLQ.
- `docs/architecture/background-workers-architecture.md`: workload placement.
- `docs/guides/BACKGROUND-JOBS-ADMIN-GUIDE.md`: admin and re-drive.

*Data model and write path*
- `docs/data-model/sprk_servicerequest.md`, `docs/data-model/sprk_communicationaccount.md`, `docs/data-model/sprk_backgroundjob.md`, `docs/data-model/sprk_backgroundjobrun.md`.
- `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md`, `docs/guides/DATAVERSE-HOW-TO-CREATE-UPDATE-SCHEMA.md`.

*Communication*
- `docs/architecture/communication-service-architecture.md`: send, shared mailbox.
- `docs/architecture/communication-intelligence-architecture.md`: incoming processing, Job B.
- `docs/guides/COMMUNICATION-ADMIN-GUIDE.md`.

*AI*
- `docs/guides/ai-guide-consumer-wiring.md`, `docs/architecture/ai-architecture-playbook-consumer-routing.md`, `docs/architecture/ai-architecture-actions-nodes-scopes.md`, `docs/guides/ai-guide-playbook-deploy-recipe.md`.

*MCP*
- `docs/architecture/SPAARKE-MCP-SERVER-ARCHITECTURE.md`, `docs/guides/HOW-TO-ADD-AN-MCP-TOOL.md`.

*Doors and UI*
- `docs/architecture/external-access-spa-architecture.md`, `docs/guides/EXTERNAL-ACCESS-ADMIN-SETUP.md`.
- `docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md`.
- `docs/guides/RIBBON-WORKBENCH-HOW-TO-ADD-BUTTON.md`.
- `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md`.

*Infrastructure*
- `docs/architecture/INFRASTRUCTURE-PACKAGING-STRATEGY.md`: Bicep conventions.

*Testing and CI*
- `docs/standards/TEST-ARCHITECTURE.md`, `docs/adr/ADR-038-testing-strategy.md`, `docs/procedures/testing-and-code-quality.md`, `docs/procedures/ci-cd-workflow.md`.

**Patterns** (`.claude/patterns/`):
- *API*: `api/background-workers.md`, `api/send-email-integration.md`, `api/endpoint-definition.md`, `api/endpoint-filters.md`, `api/error-handling.md`, `api/service-registration.md`, `api/resilience.md`.
- *AI*: `ai/public-contracts-facade.md`, `ai/endpoint-di-symmetry.md`.
- *Dataverse*: `dataverse/entity-operations.md`.
- *Auth*: `auth/service-principal.md`, `auth/managed-identity-resource-rbac.md`, `auth/uac-access-control.md`.
- *Provisioning*: `provisioning/handler-registration-completeness.md`.
- *Testing*: `testing/unit-test-structure.md`, `testing/mocking-patterns.md`.
- *Reuse*: `CAPABILITY-INDEX.md`.

**Reusable Code** (reference implementations to copy):

*Jobs*
- `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/InvoiceSubmissionProcessingJobHandler.cs:71`: a staged `IJobHandler` with resume. **Copy the pattern; do not edit it** (frozen until UAT).
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/JobProcessingModule.cs`: handler DI registration.

*Admin and AI facade*
- `src/server/api/Sprk.Bff.Api/Api/Admin/JobsEndpoints.cs:43`: admin endpoint group.
- `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/IInvoiceAi.cs:27`, `InvoiceAi.cs:30`, `NullInvoiceAi.cs`: facade + null object.

*MCP*
- `src/server/api/Sprk.Bff.Api/Api/Mcp/LegalOperations/Decisions/CompleteDecisionTool.cs:37`, `Api/Mcp/McpToolCatalog.cs`: MCP tool + catalog.

*Infrastructure and CI*
- `infrastructure/bicep/modules/service-bus.bicep:37`, `controlplane-sb-queue.bicep:95`: queues.
- `infrastructure/bicep/modules/storage-account.bicep:40`, `temp-blob-lifecycle.bicep`: storage + lifecycle rule.
- `.github/workflows/build-provisioning-sidecar.yml`: the only container build → Trivy scan → ACR push workflow. Copy it for the engine image.

*Ribbon and Console UI*
- `src/client/webresources/js/sprk_registrationribbon.js:28`: ribbon script calling the BFF.
- `src/solutions/SpaarkeAi/src/components/conversation/DocumentPerItemCards.tsx`: the closest Console card. No work-item card component exists yet.

*Tests*
- `tests/unit/Sprk.Bff.Api.Tests/Services/Jobs/Handlers/InvoiceSubmissionProcessingJobHandlerTests.cs`, `InvoiceSubmissionProcessingFlowTests.cs`: handler and flow tests.

*Schema scripts*
- `scripts/Add-BindingContextTypeTagsColumn.ps1`: add columns to an existing entity.
- `scripts/Create-BackgroundJobEntity.ps1`.

*Existing extension points (reuse-scan rows)*
- `IConsumerRoutingService.cs:364` (`RoutingContext`).
- `IActionSeam.CreateTaskAsync`.
- `DecisionRecordWriter`.
- `CommunicationProposalApplyService`.
- `ExternalIntakeEndpoints.cs:143`.
- `DeadLetterQueueService.cs:14`.
- `Infrastructure/Storage/BlobGateway.cs`.
- `IncomingCommunicationProcessor.cs:152`.

**Scripts**:
- *BFF*: `scripts/Deploy-BffApi.ps1` (through `/bff-deploy`), `scripts/Test-SdapBffApi.ps1`, `scripts/Test-BffDevDeployReadback.ps1`.
- *Console and ribbon*: `scripts/Deploy-SpaarkeAi.ps1`, `scripts/Export-EntityRibbon.ps1`, `scripts/Deploy-WebResourceInline.ps1`, `scripts/Check-AllRibbons.ps1`.
- *Dataverse*: `scripts/check-dataverse-columns.ps1`.
- *Infrastructure*: `scripts/Deploy-Platform.ps1`, `.github/workflows/deploy-infrastructure.yml`.

**Schema findings** (from the solution XML; the live Dataverse MCP was unavailable, so the column types need confirming on dev in task 010):

*`sprk_servicerequest`*
- Has: `sprk_requesttype`, `sprk_direction`, `sprk_disposition` (ontology), `sprk_assignedtointernal` (candidate for assigned attorney), `sprk_regardingcommunication`, `statuscode`.
- Needs: channel, stage, current run, send-email-updates, correlation id.

*`sprk_processingjob`*
- Has: stages, status and correlation/idempotency columns.
- Needs: a request lookup, a harness key, an engine execution id or blob prefix, and a re-drive marker.

*`sprk_communicationaccount`*
- Needs: an intake flag and a default request type (or the value in `sprk_processingrules`).

*`sprk_backgroundjob`*
- Exists, so only its description changes (§10.1 item 8).

---

## 3. Implementation Approach

### Phase Structure

```
Phase 0: Foundations and cleanup (001–009)
└─ Prove engine hosting (golden-identical), app-only edit plan, cleanup PR, ADR-004 A2 merged
Phase 1: Pipeline core and first doors (010–039)
└─ Records, orchestrator, NDA harness, triage, approval, status, Front Door + Console, admin, status API
Phase 2: Email and Copilot doors (040–049)
└─ Intake mailbox, Job B suggestion, #1920 producer, threaded return, MCP tools
Phase 3: Invoice harness (050–059) — gated on eBilling UAT sign-off
Wrap-up: 090
```

### Critical Path

**Blocking Dependencies:**
- **Outside this project:**
  - FR-01 image build is BLOCKED BY PR #3163 (engines on master).
  - Phase 3 is BLOCKED BY eBilling UAT sign-off (ebilling-r1 task 163).
  - Task 010 (request columns) is BLOCKED BY ontology coordination on `sprk_servicerequest` (spec Unresolved Q3).
- **Inside Phase 0:** ADR-004 A2 (FR-04) merges before or with the first engine-container code (FR-01).
- **Phase 1 chain:** orchestrator core → NDA harness → approval → status/return. The doors depend on the orchestrator core and the request columns.

**High-Risk Items:**
- Prior-markup preservation and the Clean copy (spec Unresolved Q1). Mitigation: settle it in the Phase 0 spike with the accelerator; FR-09 acceptance waits on it.
- First Container Apps resource (new infra, KEDA, managed identity). Mitigation: a Phase 0 spike on dev, and a provisioning step coordinated with customer-provisioning-orchestration-r1.
- Many hot-path peers on the BFF (see §5). Mitigation: small PRs, rebase often, coordinate shared files.

---

## 4. Phase Breakdown

### Phase 0: Foundations and cleanup (001–009)

**Objectives:**
1. Prove the accelerator engines run unchanged in a Container Apps Job as an ADR-004 A2 consumer.
2. Produce a schema-valid edit plan from an app-only AI Action.
3. Land the §10.1 Phase 0 cleanup as one PR with the real fixes; merge ADR-004 A2.

**Deliverables:**
- [ ] 001 Engine container: Dockerfile + entrypoint (A2 consumer: envelope, blob marker, deterministic outputs, correlationId, outcome) + golden-fixture test (FR-01)
- [ ] 002 Bicep: Container Apps environment + Job (KEDA Service Bus trigger, MI) + staging storage with 7-day lifecycle + queues (FR-01)
- [ ] 003 CI workflow: engine image build + Trivy scan + ACR push, copied from `build-provisioning-sidecar.yml` (FR-01)
- [ ] 004 App-only `agreement-classify` / `agreement-review` Actions + PublicContracts facade → schema-valid edit plan on 3 synthetic NDAs (FR-02)
- [ ] 005 Cleanup PR, BFF fixes: §10.1 items 1b, 2, 4, 5a (+ regression test), 6 (FR-03)
- [ ] 006 Cleanup PR, threaded reply (`createReply`, item 7, proven by a test send) + docs (item 8 docs part) (FR-03)
- [ ] 007 ADR-004 A2 merged (FR-04; `.claude/` edits — main session)
- [ ] 008 Phase 0 dev deploy and spike report (start latency, golden comparison, Clean-copy finding)

**Critical Tasks:** 007 must merge before or with 001. 001 is BLOCKED BY #3163.

### Phase 1: Pipeline core and first doors (010–039)

**Objectives:** a Front Door NDA request returns marked up after attorney approval on dev, with status emails when opted in.

**Deliverables:**

*Records and routing*
- [ ] 010 `sprk_servicerequest` columns (channel, stage, current run, send-email-updates, correlation id; reuse `sprk_requesttype` and `sprk_assignedtointernal` if their types fit) (FR-05)
- [ ] 011 `sprk_processingjob` extension (request lookup, harness key, engine execution / blob prefix, re-drive marker) + `sprk_backgroundjob` description (FR-06, §10.1 item 8)
- [ ] 012 `requestType` on `RoutingContext`/`IRoutingContext` + harness registry (FR-08)

*Orchestrator and NDA harness*
- [ ] 013 Orchestrator core: stage machine, resume without re-running AI, atomic idempotency, terminal-last, kill switches (FR-07, NFR-01, NFR-08)
- [ ] 014 Engine dispatch and completion: blob staging via `BlobGateway`, ids-only dispatch, completion consumer, timeout (FR-07, FR-09, NFR-03, NFR-05)
- [ ] 015 NDA harness: classify → edit plan → engine → outputs + clause table → `check_outputs` → `sprk_document` + `sprk_analysis`; standard version stamp; prior markup preserved (FR-09, NFR-09)
- [ ] 016 Manual-triage path (FR-10)

*Approval and status*
- [ ] 017 Approval: team Work Item via `IActionSeam.CreateTaskAsync`, claim sets the attorney, one approve/reject endpoint, Decision Record for both, approved SPE version (FR-11)
- [ ] 018 MDA service-request form: Approve / Reject ribbon command calling the endpoint (FR-11)
- [ ] 019 Console approval Work Item card (FR-11)
- [ ] 020 Status on every stage + per-transition status email when opted in (FR-12)

*Doors, admin and status API*
- [ ] 021 Front Door door: enqueue on submit; outputs on the request + message so the bell fires (FR-13)
- [ ] 022 Console "Submit as a request" (FR-14)
- [ ] 023 Run admin view + DLQ re-drive (wire `DeadLetterQueueService`; §10.1 item 3) (FR-15)
- [ ] 024 Run-status API (ADR-017; fail-closed authorization) (FR-16)

*Deploy and verify*
- [ ] 029 Phase 1 dev deploy (BFF, Dataverse, Console, ribbon) + end-to-end Front Door NDA run

**Critical Tasks:** 013 BLOCKS 014–017, 020–024. 010/011 BLOCK 013.

### Phase 2: Email and Copilot doors (040–049)

**Deliverables:**
- [ ] 040 Intake mailbox: `sprk_communicationaccount` intake flag; any sender → request; per-sender daily cap; thread reply → new run on the same request (FR-17, NFR-04, §10.1 item 1a)
- [ ] 041 Job B "Submit as request" proposal kind: accept → request + run; dismiss audited; never automatic (FR-18)
- [ ] 042 `AttachmentClassification` producer (#1920) (FR-19)
- [ ] 043 Email return: threaded reply from the shared legal mailbox, attorney named + Cc'd, approved outputs attached (≤ 35 MB) (FR-20)
- [ ] 044 MCP `spaarke_*` submit + status tools (FR-21)
- [ ] 049 Phase 2 dev deploy + end-to-end runs (intake mailbox, accepted suggestion, MCP)

### Phase 3: Invoice harness (050–059) — gated on eBilling UAT

**Deliverables:**
- [ ] 050 Invoice harness on the orchestrator, after UAT sign-off; no `Finance/Submission` edits before it (FR-22)
- [ ] 051 eBilling regression (suite + E2E unchanged) + dev deploy

### Wrap-up (090)

- [ ] 090 Project wrap-up (task-create Step 3.7, including `/test-diet`)

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|---|---|---|---|
| PR #3163 — accelerator engines on master | Open | Medium | 001 waits; coordinate with session "acclerator-r1" |
| eBilling UAT sign-off (ebilling-r1 task 163) | Pending | Medium | Phase 3 only; Phases 0–2 proceed |
| Ontology columns on `sprk_servicerequest` | Coordinate | Medium | Before 010 (spec Unresolved Q3) |
| Container Apps env/Job/registry per stamp | New | Medium | 002 on dev; provisioning step owed by customer-provisioning-orchestration-r1 |
| Shared legal mailbox + Exchange RBAC for Applications | Admin action | Medium | Owner OK; needed by 006 test send, 020, 043 |
| Counsel ratification of the NDA standard | Pending | Low (dev) | Gates customer rollout only |

### Internal Dependencies

| Dependency | Location | Status |
|---|---|---|
| Jobs framework (`IJobHandler`, `ServiceBusJobProcessor`) | `src/server/api/Sprk.Bff.Api/Services/Jobs/` | Production |
| Routing contract | `Services/Ai/PublicContracts/IConsumerRoutingService.cs` | Production |
| Job B proposals | `Services/Communication/CommunicationProposalApplyService.cs` | Production |
| MCP server | `Api/Mcp/` | Production (shared with spaarkeai-mcp-bff-api-r1) |
| ADR-004 A2 | `docs/adr/ADR-004-async-job-contract.md` (this branch) | Drafted |

**Hot-path peers** (projects/INDEX.md, BFF = Y): spaarkeai-mcp-bff-api-r1 (Api/Mcp), spaarke-SPA-external-access-platform-r4 (ExternalIntake), spaarke-ontology-platform-r1 (`sprk_servicerequest`), ebilling-r1 (Finance), customer-provisioning-orchestration-r1, code-quality-and-assurance-r3/r4, dotnet-10-upgrade-r1, email-communication-* (Communication). Open PR #1494 touches `IncomingCommunicationJobHandler.cs`.

---

## 6. Testing Strategy

**Unit tests** (`tests/unit/Sprk.Bff.Api.Tests/`, per ADR-038 and `docs/standards/TEST-ARCHITECTURE.md`):
- **Orchestrator:** stage transitions, resume, duplicate delivery, terminal-last.
- **Routing and endpoints:**
  - registry selection;
  - triage fallback;
  - approve/reject authorization (401/403/404, wrong state);
  - status API authorization.
- **Intake mailbox:** the per-sender cap, and intake vs non-intake mailboxes.
- **Proposals:** Job B accept/dismiss.
- **Return:** email threading.

**Integration tests**:
- **Engine container:** golden-fixture comparison (synthetic NDAs only) and the A2 consumer contract (envelope, marker, outcome, DLQ).
- **BFF:** blob staging round trip.

**E2E tests** (dev, synthetic NDAs):
- Front Door NDA → approve → returned (Phase 1).
- Intake mailbox → threaded reply; accepted suggestion; MCP submit (Phase 2).
- The eBilling suite and E2E are unchanged (Phase 3).

---

## 7. Acceptance Criteria

### Technical Acceptance

**Phase 0:**
- [ ] The engine container's outputs match the accelerator golden fixtures byte for byte.
- [ ] Three synthetic NDAs give schema-valid edit plans.
- [ ] The cleanup PR is merged with its regression tests.
- [ ] ADR-004 A2 is merged.

**Phase 1:**
- [ ] On dev, a Front Door NDA returns marked up after approval.
- [ ] The Decision Record has the approve and the reject.
- [ ] Status emails are sent only when opted in.
- [ ] A duplicate delivery produces no duplicate outputs.

**Phase 2:**
- [ ] Intake mailbox, accepted suggestion and MCP submit all complete end to end.
- [ ] An ordinary mailbox never creates a request without confirmation.

**Phase 3:**
- [ ] The invoice harness runs.
- [ ] The eBilling suite and E2E are unchanged.

### Business Acceptance

- [ ] p95 from submission to AwaitingApproval ≤ 10 min for an NDA ≤ 30 pages / 5 MB (spec Assumptions).
- [ ] No AI output leaves Spaarke without attorney approval.

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|---|---|---|---|---|
| R1 | Clean copy with prior markup unresolved (accept-all conflicts with "preserve") | High | Medium | Settle in the Phase 0 spike with the accelerator; omit Clean if unresolved |
| R2 | #3163 delayed → no engine image | Medium | High | Phase 0 cleanup and edit-plan work proceed; spike waits |
| R3 | First Container Apps resource: KEDA/MI/registry setup friction | Medium | Medium | Spike on dev; copy sidecar workflow; managed-identity RBAC pattern |
| R4 | BFF hot-path collisions with many active projects | High | Medium | Small PRs, rebase often, coordinate shared files (CLAUDE.md §4) |
| R5 | Ontology column ownership on `sprk_servicerequest` | Medium | Medium | Coordinate before 010; reuse existing columns where types fit |
| R6 | Intake mailbox abuse ("anyone" senders) | Medium | Medium | Per-sender cap, kill switch, triage |
| R7 | eBilling UAT slips → Phase 3 waits | Medium | Low | Phase 3 is independent of Phases 0–2 |
| R8 | Shared mailbox RBAC not granted → no email return | Medium | Medium | Owner action requested early (before 006 test send) |

---

## 9. Next Steps

1. Tasks are generated in `tasks/` with `TASK-INDEX.md` (task-create).
2. Settle the spec's Unresolved Questions at their gates: Q1 in the Phase 0 spike, Q2 in `/adr-check`, Q3 before task 010.
3. Begin Phase 0 (001–007), with 001 waiting on #3163.

---

**Status**: Ready for Tasks
**Next Action**: Execute Phase 0 per `tasks/TASK-INDEX.md`.
