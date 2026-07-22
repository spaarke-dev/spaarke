# Project Plan: Spaarke Notification & Action Spine — R1

> **Last Updated**: 2026-07-20
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md)

---

## 1. Executive Summary

**Purpose**: Build the platform's server-initiated typed-signal → grounded-action → delivery spine **once** (Layers A–D), and prove it with two deliberately different consumers (Comms Responsive Intelligence + proactive Daily-Briefing suggestions), so that email-r4, messaging-r3, and the assistant stop each forking their own push/delivery mechanism.

**Scope**:
- Gate-zero SignalR footprint spike (go/no-go gate for Layer-C placement).
- Durable `kind`-typed outbox (Layer B) + typed envelope contract + `kind` taxonomy lock.
- Azure SignalR delivery (Layer C) + host-agnostic shared client subscriber library + poll fallback.
- `communication-arrived` producer at persistence time (unblocks messaging-r3 task 045).
- Session-agnostic Layer-A action seam behind the node executors + `Notification` disposition leg flip.
- Comms-RI proving producer + comms policy layer (re-homed email-r4 050–054).
- Suggestion consumer (absorbed assistant R1.5): Daily-Briefing producer + Assistant renderer branch.
- ADR-047 (concise + full) + R3-P1 contract-lock deliverable.

**Timeline**: 6 phases (Phase 0 is a hard go/no-go gate) | **Estimated Effort**: 4–6 weeks (spine core + two consumers); mid-point pause after Phase 4 is legitimate per spec Assumptions.

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-013** — Layer-A seam consumed via `Services/Ai/PublicContracts/`; no direct injection of AI internals into communication code.
- **ADR-032** — Null-Object kill-switch for every new conditionally-registered service (SignalR delivery, producers); unconditional endpoint ⇒ unconditional/null-object registration (else host startup metadata-gen aborts).
- **ADR-038** — `tests/integration/seam/**` vertical-slice tests are DoD for dispatch-spine changes (FR-07/14/17); no banned test classes (no `Mock<HttpMessageHandler>`, no DI-registration/ctor-null tests).
- **ADR-039 / ADR-041** — grounded actions / ONE confirmation gate (`origin=proactive` for suggestions); no second decider, no second gate.
- **ADR-043** — `DispositionRoutability` is the ONE disposition source of truth; the Notification flip goes THROUGH it (FR-14), never around it.
- **ADR-015 / ADR-045** — privilege flagged, never decided (comms policy + envelopes); enrichment best-effort/non-fatal.
- **ADR-045/046/048** — communication architecture, ACS channel, participant junction (targeting substrate for FR-08); idempotent capture + DLQ precedent for the outbox.
- **ADR-024** — regarding family for `regardingRecordId` in envelopes and RI-created records.
- **ADR-027** — per-customer Azure provisioning via the provisioning orchestrator.
- **ADR-028** — auth v2; client calls via `@spaarke/auth` `authenticatedFetch`; negotiate endpoint authenticated; SignalR/SSE transports are the enumerated raw-fetch exception (comment `// Auth v2 (D-AUTH-7):`).
- **ADR-021** — Fluent v9 + dark mode for renderer/badge surfaces.
- **ADR-047** (NEW, this project) — the spine itself.

**From Spec** (MUST rules):
- MUST keep the chat/playbook dispatch path green while extracting Layer A (characterization first).
- MUST write the outbox BEFORE pinging SignalR (durable truth; push is acceleration).
- MUST derive fan-out targeting from Dataverse record security; MUST test negative access.
- MUST apply grounding + the ADR-041 gate BEFORE any outbox write (suggestion producer).
- MUST run the FR-01 spike before committing Layer-C placement.
- MUST NOT build a second push/delivery/action path; MUST NOT put message bodies/privileged content/pre-authorized action tokens in envelopes; MUST NOT route comms RI through `EventRulesService.FireAsync`; MUST NOT let messaging-r3 wire its own `communication-arrived` producer.

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Wave order = gate-zero → Layer B+C+`communication-arrived` → Layer A+flip → comms-RI → suggestions | Unblocks messaging-r3 (task 045) earliest; spec Assumptions §wave order | R3's `communication-arrived` contract lands in Phase 2, before the heavier Layer-A extraction |
| Outbox is envelope-shaped JSON column + regarding lookup (not a full grounded-payload store) | Producers persist action detail in their own domain records; spine carries IDs + minimal display metadata only | Keeps Layer B thin; NFR-02/03 hold |
| Comms rule-config store = Binding (`sprk_playbookconsumer`) rows + match conditions (assumed) | Design §4 lean; a comms-specific rule table ONLY if Binding provably cannot express tenant/matter match conditions | Decided at the comms-policy task with grep evidence (§11 gate re-checked there) |
| Notification flip preceded by "what lights up" audit | ADR-043 catalog is a shipped behavior surface; flipping routable lights up every chat capability that can emit a notification | Audit doc reviewed BEFORE the flip lands |

### Discovered Resources

**Applicable Skills** (auto-discovered):
- `.claude/skills/task-execute/` — MANDATORY per-task execution protocol (FULL rigor for BFF/`.cs`/`.ts`).
- `.claude/skills/dataverse-create-schema/` — new outbox table + attributes via Web API + PowerShell.
- `.claude/skills/dataverse-deploy/` — solution/schema deploy + `describe_table` verification.
- `.claude/skills/bff-deploy/` + `.claude/skills/azure-deploy/` — BFF deploy (SignalR endpoints) + Azure SignalR Service provisioning.
- `.claude/skills/fluent-v9-component/` — SpaarkeAi renderer/badge surfaces.
- `.claude/skills/code-review/` + `.claude/skills/adr-check/` — Step 9.5 gates (unconditional for BFF-touching + test-modifying tasks).
- `.claude/skills/conflict-check/` — before EVERY BFF PR (heavily-contested hot path).
- `.claude/skills/test-diet/` — project-close reconciliation (ADR-038 §7).

**Knowledge Articles / Patterns**:
- `.claude/constraints/bff-extensions.md` — BINDING pre-merge checklist for any BFF addition (new hub, endpoints, DI, packages); Placement Justification required.
- `.claude/constraints/azure-deployment.md` — publish-size per-task rule (NFR-01), CORS (critical for SignalR negotiate), BackgroundService lazy-resolution rule (outbox pump).
- `.claude/adr/ADR-032-bff-nullobject-kill-switch.md` — the mandated mechanism for feature-gated SignalR/notification services.
- `.claude/agent-memory/researcher/signalr-vs-sse-notification-fabric-2026-07-16.md` — **read first for FR-01**; prior deep-dive on Azure SignalR vs SSE for the notification fabric.
- `.claude/agent-memory/researcher/assistant-push-channel-2026-07-15.md` — prior research on the assistant push channel (R1.5 proactive push).
- `.claude/patterns/api/background-workers.md`, `.claude/patterns/ai/streaming-endpoints.md`, `.claude/patterns/api/service-registration.md`, `.claude/patterns/ai/endpoint-di-symmetry.md` — outbox pump + DI symmetry.
- `docs/adr/ADR-038-testing-strategy.md` + `.claude/patterns/testing/integration-tests.md` — seam-test DoD.

**Reusable Code (verified on branch 2026-07-20)**:
- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/{CreateNotification,CreateTask,UpdateRecord}NodeExecutor.cs` — the executors Layer A is extracted BEHIND (coupled to `NodeExecutionContext` via `ExecuteAsync`/`Validate`).
- `src/server/api/Sprk.Bff.Api/Services/Ai/DispositionRoutability.cs:98-102` — `Notification` entry currently `Routable=false` (the FR-14 flip target).
- `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/` — 39-file facade dir (home for the Layer-A seam exposure).
- `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationEnrichmentService.cs:216-238` — step-5 emit point, method **`RunAssessmentEmissionAsync`** (emit-only `LogInformation`; the FR-11 producer target). *(Spec typo: spec says `RunAsessmentEmissionAsync`.)*
- `src/server/api/Sprk.Bff.Api/Services/NotificationService.cs:55` — `CreateNotificationAsync` → `appnotification` write (the mirror target; centralizes ALL appnotification writes).
- `src/server/api/Sprk.Bff.Api/Services/Ai/EventRules/EventRulesService.cs:113` — `FireAsync` (SSE-shaped; **reuse gate primitives, NOT this seam**): per-user daily cost cap + `ClassifyConfidenceThreshold` gate.
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PendingPlanManager.cs` — THE ONE confirmation gate (`SuspendInvocationAsync`/`ResumeInvocationAsync`). *(Cites ADR-039/040 in-code; spec labels it "ADR-041 gate.")*
- `src/server/api/Sprk.Bff.Api/Api/Ai/DispatchSessionEndpoint.cs:90` — `POST /api/ai/chat/sessions/{id}/dispatch`; `SurfaceLaunch` enum in `PublicContracts/Binding.cs:175`, routed in `OutputRouter.cs:278`.
- `src/server/api/Sprk.Bff.Api/Services/Ai/Narrators/DailyBriefing*` — the suggestion producer's host. **⚠️ The narrator does NOT itself write `appnotification`** (narration-only `ICodedWorkflow`; render endpoint *bypasses* appnotification) — the FR-13/15 producer must write to `NotificationService`/outbox explicitly.
- `src/client/shared/` (14 packages incl. `Spaarke.Auth`) — home for the new host-agnostic subscriber library; `@spaarke/auth` `authenticatedFetch` confirmed at `Spaarke.Auth/src/authenticatedFetch.ts`.

---

## 3. Implementation Approach

### Phase Structure

```
Phase 0: Gate-Zero Spike (BLOCKING GATE)
└─ FR-01 SignalR footprint spike → go/no-go + mode decision + Layer-C placement

Phase 1: Layer B + Contract + ADR-047 (spine foundation)
└─ kind-typed outbox table + service; typed envelope contract; kind taxonomy lock; ADR-047 authored

Phase 2: Layer C + communication-arrived producer  ← unblocks messaging-r3 EARLIEST
└─ SignalR delivery (mode per Phase 0); shared subscriber lib; poll fallback; FR-08 targeting;
   communication-arrived producer; FR-19 R3 contract-lock note

Phase 3: Layer A + Notification flip
└─ session-agnostic action seam behind executors (characterization-first); "what lights up" audit → Notification Routable=true

Phase 4: Comms-RI proving producer + policy (re-homed email-r4 050–054)
└─ RunAssessmentEmissionAsync → fire-and-forget producer; comms policy layer; RI actions via Layer A; appnotification mirror

Phase 5: Suggestion consumer (absorbed assistant R1.5)
└─ Daily-Briefing kind=suggestion producer (grounded+gated BEFORE outbox); Assistant renderer chip source; re-enters SurfaceLaunch/dispatch

Phase 6: Wrap-up
└─ ADR-047 full finalized; test-diet; docs/data-model entry; lessons-learned
```

### Critical Path

**Blocking Dependencies:**
- **Phase 0 BLOCKS all of Phase 2** (Layer-C placement is gated on the spike; by design — that's the gate).
- Phase 1 (outbox + envelope contract) BLOCKS Phase 2 (delivery needs the store + contract) and every producer (Phases 2/4/5 write outbox rows).
- Phase 3 Layer-A extraction BLOCKS Phase 4 (RI actions execute via the Layer-A seam).
- Phase 4 (comms-RI) SHOULD precede Phase 5 (suggestions) — spec sequences suggestions after comms-RI proving.
- `email-r4 W10 merge` BLOCKS Phase 4's enrichment/persist-path touches (external merge-order constraint).

**High-Risk Items:**
- FR-01 outcome (>60 MB) — Mitigation: measured first; hub/negotiate moves out of BFF before Layer-C tasks if it breaches.
- FR-08 fan-out leak — Mitigation: targeting from Dataverse record security; negative-access seam tests; named security sign-off.
- Layer-A extraction regressing chat — Mitigation: characterization tests pin the path BEFORE extraction.
- Cross-project merge collision on `Services/Communication/**` — Mitigation: `/conflict-check` before every BFF PR; email-r4 W10 first; messaging-r3 P1 agreement.

---

## 4. Phase Breakdown

### Phase 0: Gate-Zero Spike (BLOCKING)

**Objectives:**
1. Measure Azure SignalR footprint in BOTH Serverless and Default modes and decide mode + Layer-C placement.

**Deliverables:**
- [ ] Spike note: compressed BFF publish size delta (both modes) vs 55/60 MB bands; cold-start delta; transitive CVE scan; target-env CSP `connect-src` verification.
- [ ] Go/no-go + mode decision doc. If >60 MB, revised Layer-C design placing hub/negotiate OUT of the BFF.

**Critical Tasks:** FR-01 spike — MUST BE FIRST; BLOCKS all Layer-C tasks.

**Inputs**: `signalr-vs-sse-notification-fabric-2026-07-16.md`, `azure-deployment.md` publish-size rule, current BFF baseline (~49.63 MB incl-PDB / 45.87 excl-PDB).

**Outputs**: `notes/spikes/fr-01-signalr-footprint.md` + go/no-go decision.

### Phase 1: Layer B + Envelope Contract + ADR-047

**Objectives:**
1. Stand up the `kind`-typed durable outbox (table + service).
2. Lock the typed envelope contract + `kind` taxonomy.
3. Author ADR-047 (concise) to lock the architecture before consumers wire in.

**Deliverables:**
- [ ] New `sprk_` outbox Dataverse table (name + column set proposed at schema task) + `docs/data-model/` entry.
- [ ] Outbox service: write/read/expire; ADR-032 null-object; registered unconditionally.
- [ ] Typed envelope contract types (`kind` discriminator; communication + suggestion envelopes) + serialization tests.
- [ ] `kind` taxonomy locked (active: `suggestion`|`communication-assessed`|`communication-arrived`; reserved: `job-complete`|`share`|`system-alert`).
- [ ] ADR-047 concise (`.claude/adr/`) authored main-session.

**Inputs**: Phase 0 decision; ADR-046 idempotent-capture precedent; `dataverse-create-schema` skill.

**Outputs**: outbox table + service + contract types; `.claude/adr/ADR-047-*.md`.

### Phase 2: Layer C + `communication-arrived` Producer (unblocks R3)

**Objectives:**
1. Deliver Layer-C SignalR (mode per Phase 0) + host-agnostic subscriber library + poll fallback.
2. Emit `communication-arrived` at persistence time for ALL channels.
3. Deliver the FR-19 contract-lock note to messaging-r3.

**Deliverables:**
- [ ] SignalR delivery service + negotiate endpoint (authenticated) + producer entrypoint (`Clients.User(oid)`/`Group`); ADR-032 null-object.
- [ ] Host-agnostic shared client subscriber library in `src/client/shared/` (negotiate/connect + `kind`-routing); builds standalone.
- [ ] `kind`-generic pending/poll fallback endpoint over the outbox (ADR-032 degrade path).
- [ ] FR-08 fan-out targeting from Dataverse record security + negative-access seam tests.
- [ ] `communication-arrived` producer at persistence time (capture + send) — best-effort/non-fatal.
- [ ] FR-19 R3-P1 contract-lock note (trigger=persistence, spine-emits/R3-consumes, envelope, consumer API, degrade) delivered + acknowledged.

**Inputs**: Phase 0 (mode/placement), Phase 1 (outbox + contract); participant junction + `IThreadResolver`; ADR-028 auth.

**Outputs**: `Services/Notifications/**` (NEW); subscriber lib; R3 note.

### Phase 3: Layer A + Notification Flip

**Objectives:**
1. Extract the session-agnostic action seam behind the node executors, exposed via `PublicContracts`.
2. Flip `DispositionRoutability.Notification` to `Routable=true` after the audit.

**Deliverables:**
- [ ] Characterization tests pinning the chat/playbook dispatch path (BEFORE extraction).
- [ ] Layer-A seam (Create Event/Task/Notification) invokable without a chat session or playbook run; existing executor tests pass unmodified; exposed via `PublicContracts`.
- [ ] "What lights up" audit doc (every shipped chat capability that gains notification emission) — reviewed BEFORE the flip.
- [ ] `Notification` leg `Routable=true` (through the registry); dispatch-spine seam tests green.

**Inputs**: node executors, `DispositionRoutability.cs`, `PublicContracts/`; ADR-013/043.

**Outputs**: Layer-A seam types; audit doc; registry change.

### Phase 4: Comms-RI Proving Producer + Policy (re-homed email-r4 050–054)

**Objectives:**
1. Replace enrichment step 5's emit-only log with a fire-and-forget `communication_assessed` producer.
2. Add the comms policy layer (tenant/matter rules + confidence gate).
3. Execute RI actions via Layer A, write `kind=communication-assessed` rows, mirror to `appnotification`.

**Deliverables:**
- [ ] `RunAssessmentEmissionAsync` → fire-and-forget, non-fatal `communication_assessed` producer (enrichment never fails on producer failure).
- [ ] Comms policy layer: tenant/matter rule config (Binding rows assumed — decide with grep evidence) + confidence gate (reuse primitives, NOT chat scoping); privilege flagged-not-decided.
- [ ] RI actions via Layer-A seam → `kind=communication-assessed` outbox rows → ping Layer C → `appnotification` mirror (Daily Briefing surfaces them).

**Inputs**: **email-r4 W10 merged**, Phases 1–3; `EventRulesService` primitives; `NotificationService` mirror path.

**Outputs**: producer + policy layer; comms-assessed rows visible in Daily Briefing.

### Phase 5: Suggestion Consumer (absorbed assistant R1.5)

**Objectives:**
1. Daily-Briefing `kind=suggestion` producer — grounded + gated BEFORE the outbox write.
2. Assistant renderer branch as a chip source reusing the shipped dispatch + ack-gate.

**Deliverables:**
- [ ] Suggestion producer: grounding (ADR-039) + ADR-041 gate (`origin=proactive`) BEFORE outbox write (ungrounded/ungated → no row, tested). Writes to `NotificationService`/outbox explicitly (narrator does not).
- [ ] Assistant renderer branch: compact card from the envelope; re-fetches/re-grounds via BFF at action time; expired suggestions don't render.
- [ ] Acting on a suggestion re-enters `SurfaceLaunch`/dispatch (P5 ack-gated) — behaviorally identical to reactive; seam test comparing proactive vs reactive dispatch of the same action.

**Inputs**: Phases 1–4; `DailyBriefing*`; `PendingPlanManager` gate; `SurfaceLaunch` dispatch; Fluent v9 (ADR-021).

**Outputs**: suggestion producer + renderer chip source.

### Phase 6: Wrap-Up

**Objectives:** finalize governance + close the project.

**Deliverables:**
- [ ] ADR-047 full (`docs/adr/`) finalized + CHANGELOG entry.
- [ ] `/test-diet` reconciliation (ADR-038 §7).
- [ ] `docs/data-model/` outbox entry finalized.
- [ ] `lessons-learned.md`; README status → Complete.

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Azure SignalR Service | GA | Medium | Mode/tier per FR-01; provisioning via ADR-027 orchestrator |
| Target-env CSP `connect-src` | Verify | Medium | Verified in FR-01; silent-fallback risk if missed |
| New Dataverse outbox table | Not started | Low | Schema per environment; `dataverse-create-schema` patterns |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| email-r4 W10 merge (owns `Services/Communication/**`) | `spaarke-wt-email-communication-solution-r4` | Pending — BLOCKS Phase 4 enrichment touches |
| messaging-r3 Phase-1 coordination (serial persist/read-path edits) | messaging-communication-app-r3 | Coordinate at their P1 (FR-19 lock) |
| Shipped substrate (enrichment emit, thread model, participant junction, create-flow, appnotification) | `Sprk.Bff.Api` | Production (verified 2026-07-20) |
| `Services/Ai/` internals owner (do NOT fork; consume `PublicContracts`) | `spaarke-ai-architecture-redesign-r2` | Coordinate |
| Daily-Briefing producer coordination | `spaarke-daily-update-service-r5` | Coordinate |

---

## 6. Testing Strategy

**Seam tests (DoD per ADR-038 — `tests/integration/seam/**`)**:
- FR-07 Layer-A seam invokable without chat session/playbook run.
- FR-09 `communication-arrived` both channels (email + message) → outbox row + ping.
- FR-14 dispatch-spine green after Notification flip.
- FR-17 proactive vs reactive dispatch of the same action are behaviorally identical.

**Characterization tests**: pin the chat/playbook dispatch path BEFORE Layer-A extraction (FR-07).

**Negative-access tests (R-5 security)**: FR-08 private-thread event reaches only shared participants; internal-only never reaches external users. Named security sign-off on fan-out + envelope.

**Degrade tests**: NFR-04 — SignalR off (null-object) → outbox + FR-06 poll + appnotification mirror still deliver.

**Unit/contract tests**: envelope serialization (FR-03); outbox write/read/expire (FR-02); producer non-fatality (FR-11, NFR-05).

---

## 7. Acceptance Criteria

(Mirrors README graduation criteria + spec §Success Criteria; each item verified per its FR.)

**Phase 0:** [ ] FR-01 spike recorded — mode chosen, size vs 55/60 bands, cold-start + CVE + CSP verified.
**Phase 1:** [ ] outbox write→pending→dismiss/expiry works; [ ] envelope serialization tests pass; [ ] `kind` taxonomy locked; [ ] ADR-047 concise exists.
**Phase 2:** [ ] outbox write + ping delivers to connected client; [ ] disconnected client receives via poll fallback; [ ] subscriber lib builds standalone + workspace consumes it; [ ] `communication-arrived` both channels; [ ] R3 acknowledges contract at their P1.
**Phase 3:** [ ] Layer-A seam invokable without chat session; [ ] existing executor tests pass unmodified; [ ] audit doc reviewed before flip; [ ] `Notification` Routable=true + seam tests green.
**Phase 4:** [ ] producer exception → enrichment completes; [ ] rule match + confidence pass → actions fire, else logged no-action; [ ] assessed inbound auto-creates records + Daily-Briefing notification.
**Phase 5:** [ ] ungrounded/ungated candidate → no outbox row; [ ] card renders from envelope, stale/revoked fails gracefully; [ ] proactive-vs-reactive seam parity.

**Business/Quality Acceptance:**
- [ ] BFF publish ≤60 MB compressed (55 MB review band respected); every BFF task reports size + PDB convention.
- [ ] 0 new HIGH-severity CVE.

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | SignalR SDK pushes BFF publish >60 MB | Med | High | FR-01 measures both modes first; move hub/negotiate out of BFF if breached |
| R2 | Fan-out leaks private thread to external users | Low | High (compliance) | FR-08 targeting from record security; negative-access seam tests; named security sign-off |
| R3 | Layer-A extraction regresses chat/playbook dispatch | Med | High | Characterization-first; seam tests DoD |
| R4 | Merge collision on `Services/Communication/**` (email-r4 / messaging-r3) | High | Med | email-r4 W10 first; `/conflict-check` per BFF PR; messaging-r3 P1 agreement |
| R5 | Accidental second push channel (assistant R1.5 SignalR design) | Med | High | R1.5 absorbed; single contract; renderer is a chip source on shipped dispatch |
| R6 | Binding cannot express tenant/matter match conditions | Med | Med | Decide at comms-policy task with grep evidence; thin comms rule table only if proven necessary (§11) |
| R7 | Notification flip lights up unintended chat capabilities | Med | Med | "What lights up" audit reviewed BEFORE the flip |

---

## 9. Next Steps

1. **Review this plan.md** (wave order, phase gates, cross-project deps).
2. **Run** `/task-create projects/spaarke-notification-spine-r1` to generate POML task files (already invoked by the pipeline).
3. **Begin Phase 0** (FR-01 spike) — the hard go/no-go gate; do NOT start Layer-C tasks until the spike decides.

---

**Status**: Ready for Tasks
**Next Action**: Task decomposition (Step 3), then Phase 0 spike execution after human review of the wave order + cross-project merge constraints.

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks. The FR-01 spike (Phase 0) is a BLOCKING gate — Layer-C placement depends on its outcome.*
