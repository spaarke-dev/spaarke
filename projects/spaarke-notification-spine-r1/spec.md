# Spaarke Notification & Action Spine — R1 — AI Implementation Specification

> **Status**: Ready for Implementation (gate-zero spike is task 0 — see FR-01)
> **Created**: 2026-07-20
> **Source**: `projects/spaarke-notification-spine-r1/design.md` (combined-scope per §6 path (iii), 2026-07-20; consumer specs §5A/§5B contributed by `messaging-communication-app-r1` + `spaarkeai-assistant-enhancements-r1`; R3 consumer verification §5A.7)
> **Follows**: `email-communication-solution-r4` (enrichment + `communication_assessed` emit point; W5 tasks 050–054 re-homed here), `spaarkeai-assistant-enhancements-r1` (reactive create flows shipped; R1.5 proactive scope absorbed here per §4A), `messaging-communication-app-r1/r2` (thread model + participant junction shipped)

## Executive Summary

Build the platform's **server-initiated, typed-signal → grounded-action → delivery spine ONCE**: a session-agnostic domain-action seam (Layer A), a durable per-user `kind`-typed outbox (Layer B), Azure SignalR real-time delivery with a host-agnostic client subscriber (Layer C), and per-source policy (Layer D) — proven in R1 by **two consumers of deliberately different shapes**: (1) **Communication Responsive Intelligence** (assessed communication → auto-created Event/Task/Notification, fire-and-forget, tenant/matter-gated) and (2) **proactive suggestions** (absorbed assistant-R1.5: Daily-Briefing producer → grounded, gated `kind=suggestion` card that re-enters the shipped dispatch path). The spine also emits `communication-arrived` at persistence time for all channels — **messaging-r3's release gates on consuming it** (its task 045 is blocked on this contract). Doctrine: **no second push/delivery mechanism**; the spine is dumb transport — every "should we act/push?" decision stays grounded + gated in per-source producer policy.

## Scope

### In Scope
- **Gate-zero SignalR footprint spike** (go/no-go BEFORE Layer-C placement): Serverless vs Default mode vs the 55/60 MB BFF bands + cold-start + CVE + CSP `connect-src`.
- **Layer A** — promote the existing node executors (`CreateNotification`/`CreateTask`/`UpdateRecord`) to a **session-agnostic action seam** behind the executors (chat/playbook path stays green), exposed via the `PublicContracts` facade; preserves the P5 ack-contract + `SurfaceLaunch`/dispatch entry points.
- **Layer B** — thin `sprk_` **`kind`-typed durable outbox** table (per-user pending row: `kind`, grounded payload ref, delivered/dismissed/expiry) + write/read/expiry service. `appnotification` stays an optional mirror.
- **Layer C** — Azure SignalR delivery (mode per spike) + `IHubContext`/Management producer entrypoint + **host-agnostic shared client subscriber library** (workspace widget, record-form PCF, standalone code-page hosts) routing by `kind` + a **`kind`-generic pending/poll fallback endpoint** (ADR-032 degrade path all hosts share).
- **Typed envelope contract** — `kind` discriminator; IDs + minimal display metadata only (§5A.3 communication envelope incl. `threadId`/`badgeDelta`; §5B.4 suggestion envelope); clients re-fetch/re-ground via BFF.
- **`communication-arrived` producer** — spine emits at **persistence time** for ALL channels (inbound capture and outbound send identically); no assessment prerequisite. **The spine emits; messaging-r3 consumes only** (§5A.7 #1).
- **`kind` taxonomy lock** — `suggestion` | `communication-assessed` | `communication-arrived`; reserve `job-complete` | `share` | `system-alert`.
- **Comms-RI proving producer** (re-homed email-r4 050–054): replace enrichment step 5's emit-only log with a fire-and-forget `communication_assessed` producer + **comms policy layer** (tenant/matter rule config + confidence gate, reusing gate primitives; privilege flagged-not-decided) + domain actions via Layer A + `kind=communication-assessed` outbox rows + Daily-Briefing surfacing via the `appnotification` mirror.
- **`Notification` disposition leg flip** — realize the Layer-A/B legs so `DispositionRoutability.Notification` becomes `Routable=true`, preceded by the "what lights up" audit (deliberate behavior-surface change).
- **Suggestion consumer** (absorbed R1.5, §4A): Daily-Briefing `kind=suggestion` producer (grounded ADR-039 + gated ADR-041 `origin=proactive` BEFORE outbox write); Assistant renderer branch as a **chip source reusing the shipped dispatch + ack-gate** (no new pipeline, no second gate); acting on a suggestion re-enters `SurfaceLaunch`/dispatch — behaviorally identical to reactive.
- **ADR-047** (concise + full): "Notification & action spine — typed signals, shared domain actions, per-source policy, SSE-as-presentation." Authored main-session.
- **R3-P1 contract-lock deliverable**: written contract confirmation (trigger, envelope, consumer API, degrade) to unblock messaging-r3 task 045.

### Out of Scope
- Routing comms RI through `EventRulesService.FireAsync` (chat user/session gate semantics don't apply — reuse gate *primitives* only).
- A comms-only or assistant-only hub, or ANY second push/delivery mechanism (hard MUST NOT).
- Messaging fan-out consumers (messaging-r3 builds its own consume-side badge/toast in its task 045; email badges opt-in later).
- The `Record` disposition leg flip (Notification only in R1; Record is a later wave).
- Presence/idle detection (consumer concern, NOT the spine's — §5B.6).
- Consumers for the reserved kinds (`job-complete`/`share`/`system-alert`) — taxonomy reservation only.
- SSE changes beyond demoting it conceptually to a chat presentation adapter (no SSE code rework).
- Per-customer Azure SignalR provisioning automation beyond wiring into the existing provisioning orchestrator (ADR-027).

### Affected Areas
- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/**` — Layer-A seam behind `CreateNotificationNodeExecutor`/`CreateTaskNodeExecutor`/`UpdateRecordNodeExecutor` (playbook-run-coupled today via `NodeExecutionContext`).
- `src/server/api/Sprk.Bff.Api/Services/Ai/DispositionRoutability.cs` — `Notification` leg flip (currently `Routable=false`, lines ~90–103).
- `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/**` — facade exposure of the Layer-A seam (ADR-013).
- `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationEnrichmentService.cs` — step 5 (`RunAsessmentEmissionAsync`, lines ~216–238) emit-only log → producer; persist-path `communication-arrived` emit (⚠️ shared with messaging-r3 Phase-1 + email-r4 W10 — merge-order constraints).
- `src/server/api/Sprk.Bff.Api/Services/Notifications/**` (NEW) — outbox service, delivery service, negotiate/pending endpoints, ADR-032 null-objects.
- `src/server/api/Sprk.Bff.Api/Services/NotificationService.cs` — `appnotification` mirror integration point.
- `src/server/api/Sprk.Bff.Api/Services/Ai/Narrators/DailyBriefing*` — suggestion producer (⚠️ coordinate with `spaarke-daily-update-service-r5`).
- New Dataverse table: the `sprk_` outbox (name at schema task; docs/data-model entry required).
- `src/client/shared/**` — new shared client subscriber library (host-agnostic).
- `src/solutions/SpaarkeAi/**` — suggestion renderer branch (chip source) + workspace subscriber wiring.
- `tests/unit/Sprk.Bff.Api.Tests/**` + `tests/integration/seam/**` — seam tests are DoD for dispatch-spine changes (ADR-038).

## Requirements

### Functional Requirements

**Phase 0 — gate-zero**
1. **FR-01** — SignalR footprint spike: measure BOTH Serverless and Default modes (server SDK in BFF; compressed publish size vs 55 MB review / 60 MB stop bands; cold-start delta; transitive CVE scan; target-env CSP `connect-src`). Burden of proof on Default (send-only topology favors Serverless). Acceptance: recorded go/no-go + mode decision + measured sizes in notes; if >60 MB, hub/negotiate placement moves out of the BFF and the Layer-C design is revised BEFORE Layer-C tasks start.

**Spine core (Layers A–C + contract)**
2. **FR-02** — `kind`-typed durable outbox table + service: per-user pending row (`kind`, payload/envelope, regarding ref, delivered/dismissed/expiry timestamps); write/read/expire operations. Outbox is the durable source of truth; live push is acceleration. Acceptance: producer writes row → pending endpoint returns it → dismiss/expiry removes it.
3. **FR-03** — Typed envelope contract with `kind` discriminator; **IDs + minimal display metadata ONLY** (communication envelope per §5A.3 incl. `threadId`, `badgeDelta`, privacy-gated `snippet?`; suggestion envelope per §5B.4 incl. `actionHint`, `expiresAt`; **no message bodies, no privileged content, no pre-authorized action tokens**). Acceptance: contract types + serialization tests; envelope review in the R-5 security gate.
4. **FR-04** — Layer-C delivery: SignalR (mode per FR-01) with producer entrypoint targeting `Clients.User(oid)` / `Group(...)`; at-most-once, signal-only ("changed → fetch"); producers remain correct when SignalR is unreachable (best-effort ping after durable write). Acceptance: outbox write + ping delivers to a connected client; disconnected client still receives via FR-06.
5. **FR-05** — Host-agnostic shared client subscriber library: negotiate/connect handling + `kind`-routing, consumable from the SpaarkeAI workspace, record-form PCFs, and standalone code pages (R3's three hosts, §5A.7 #2). Acceptance: library builds standalone; workspace consumes it; contract documented for R3.
6. **FR-06** — `kind`-generic pending/poll fallback endpoint over the outbox (the ADR-032 degrade path every consumer shares; also serves next-load delivery). Acceptance: with SignalR off (null-object), a written outbox row is delivered via the endpoint.
7. **FR-07** — Session-agnostic Layer-A action seam (Create Event / Task / Notification) **behind** the existing node executors, exposed via `PublicContracts`; chat/playbook path behavior unchanged (seam + characterization tests green); preserves the P5 ack-contract and `SurfaceLaunch`/dispatch entry points. Acceptance: seam invokable without a chat session or playbook run; existing executor tests pass unmodified.
8. **FR-08** — Fan-out targeting derives from Dataverse record security (`sprk_communication`/thread + `sprk_communicationparticipant` junction): private-thread events reach only shared participants; internal-only never reaches external users. Acceptance: **test-verified** negative-access cases (R-5; a leak is a compliance incident).
9. **FR-09** — `communication-arrived` producer: emitted by the spine at **persistence time** for ALL channels (capture and send), no assessment prerequisite; envelope per FR-03. Acceptance: persisting an email and a message each yields an outbox row + ping; R3's task-045 consumer contract confirmed in the FR-19 deliverable.
10. **FR-10** — `kind` taxonomy locked: `suggestion` | `communication-assessed` | `communication-arrived` active; `job-complete` | `share` | `system-alert` reserved (discriminator values defined, no consumers).

**Comms-RI proving producer (Layer D-comms; re-homed email-r4 050–054)**
11. **FR-11** — Replace enrichment step 5's emit-only log with a **fire-and-forget, non-fatal** `communication_assessed` producer (enrichment never fails because the producer failed). Acceptance: producer exception → enrichment completes + telemetry logged.
12. **FR-12** — Comms policy layer: tenant/matter-scoped rule config + confidence gate deciding *whether* to act on an assessed communication; reuses gate primitives (cost cap / confidence patterns) but NOT chat user/session scoping; privilege **flagged, never decided** (ADR-015). Acceptance: rule match + confidence pass → actions fire; no match/below threshold → no action, decision logged.
13. **FR-13** — RI actions execute via the Layer-A seam (Event/Task/Notification), write `kind=communication-assessed` outbox rows, ping Layer C, and mirror to `appnotification` (Daily Briefing surfaces them). Acceptance: an assessed inbound communication auto-creates the configured records + notification visible in Daily Briefing.
14. **FR-14** — `DispositionRoutability.Notification` flips to `Routable=true` **only after** the "what lights up" audit (enumerate every shipped chat capability that gains notification emission; sequence deliberately). Acceptance: audit doc exists + reviewed BEFORE the flip lands; dispatch-spine seam tests green after.

**Suggestion consumer (absorbed R1.5, §4A)**
15. **FR-15** — Daily-Briefing `kind=suggestion` producer: grounded (ADR-039) + confirmation-gated (ADR-041 `origin=proactive`) **BEFORE** the outbox write — the spine NEVER carries ungrounded/ungated content; the outbox write is the *output* of the producer's gates. Acceptance: ungrounded/ungated candidate → no outbox row (tested).
16. **FR-16** — Suggestion renderer branch in the Assistant: compact card (chip source) from the envelope; **re-fetches/re-grounds via BFF at action time** (freshness + access re-check); expired suggestions don't render. Acceptance: card renders from envelope; stale/revoked suggestion fails gracefully on action.
17. **FR-17** — Acting on a suggestion **re-enters the shipped dispatch path** (`SurfaceLaunch` disposition → pre-seeded wizard hand-off; P5 ack-gated "done" claims) — behaviorally identical to the equivalent reactive dispatch; NO parallel proactive-action path. Acceptance: seam test comparing proactive vs reactive dispatch of the same action.

**Governance + coordination deliverables**
18. **FR-18** — ADR-047 authored (concise `.claude/adr/` + full `docs/adr/`), main-session (sub-agent write boundary).
19. **FR-19** — R3-P1 contract-lock deliverable: written confirmation to `messaging-communication-app-r3` (trigger=persistence, spine-emits/R3-consumes, envelope, consumer API incl. subscriber lib + FR-06 endpoint, degrade semantics) — unblocks their task 045. Acceptance: note delivered to R3's project notes + acknowledged at their P1.

### Non-Functional Requirements
- **NFR-01** — BFF publish ≤60 MB compressed (ceiling); verify + report per BFF-touching task. Baseline **~46.24 MB**; ≥55 MB → architecture review; SignalR SDK delta governed by FR-01.
- **NFR-02** — The spine is an accelerator, never an authorization bypass: envelopes carry no content a recipient couldn't read via the BFF; clients re-fetch through access-checked BFF endpoints.
- **NFR-03** — The spine never delivers ungrounded or ungated content (grounding + gates live in producers; dumb transport).
- **NFR-04** — ADR-032 Null-Object degradation: SignalR off/unreachable → outbox + FR-06 poll + `appnotification` mirror still deliver; all new services registered unconditionally with null-object fallbacks.
- **NFR-05** — Producer emissions are best-effort/non-fatal to their host flows (enrichment, persist path, Daily-Briefing render never fail on spine errors).
- **NFR-06** — No new HIGH-severity CVE (`dotnet list package --vulnerable --include-transitive`), incl. the SignalR transitive graph (FR-01).
- **NFR-07** — Tests per ADR-038: **seam tests** (`tests/integration/seam/**`) are DoD for dispatch-spine changes (FR-07/14/17); characterization tests pin the chat path before Layer-A extraction; FR-08 targeting has explicit negative-access tests; **named security sign-off** on the fan-out + envelope (R-5).
- **NFR-08** — Merge-order/coordination: enrichment + persist-path touches land AFTER email-r4 W10 merges and coordinate with messaging-r3's serial Phase-1 wave (`/conflict-check` before every BFF PR); Daily-Briefing producer coordinates with daily-update-r5.
- **NFR-09** — SSE remains chat-only presentation; the spine takes no SSE dependency.

## Technical Constraints

### Applicable ADRs
- **ADR-047** (NEW, this project) — the spine itself (FR-18).
- **ADR-013** — AI facade: Layer-A seam consumed via `Services/Ai/PublicContracts/`; no direct injection of AI internals into communication code.
- **ADR-015** — privilege flagged, never decided (comms policy layer + envelopes).
- **ADR-032** — Null-Object kill-switch for every new conditional service (SignalR delivery, producers).
- **ADR-038** — testing strategy; seam tests as DoD for dispatch-spine changes; no banned test classes.
- **ADR-039 / ADR-041** — grounded actions / ONE confirmation gate (`origin=proactive` for suggestions); no second decider, no second gate.
- **ADR-043** — `DispositionRoutability` is the ONE disposition source of truth; the Notification flip goes through it (FR-14), never around it.
- **ADR-045 / ADR-046 / ADR-048** — communication architecture, ACS channel, participant junction (targeting substrate for FR-08).
- **ADR-024** — regarding family for `regardingRecordId` in envelopes and RI-created records.
- **ADR-027** — per-customer Azure provisioning via the provisioning orchestrator.
- **ADR-028** — auth v2; client calls via `@spaarke/auth` `authenticatedFetch`; negotiate endpoint authenticated.
- **ADR-021** — Fluent v9 + dark mode for the renderer/badge surfaces.

### MUST Rules
- ✅ MUST keep the chat/playbook dispatch path green while extracting Layer A (seam behind existing executors; characterization first).
- ✅ MUST write the outbox BEFORE pinging SignalR (durable truth; push is acceleration).
- ✅ MUST derive fan-out targeting from Dataverse record security; MUST test negative access.
- ✅ MUST apply grounding + the ADR-041 gate BEFORE any outbox write (suggestion producer).
- ✅ MUST run the FR-01 spike before committing Layer-C placement.
- ❌ MUST NOT build a second push/delivery/action path (per-consumer hubs, parallel proactive-action path, second gate/decider).
- ❌ MUST NOT put message bodies, privileged content, or pre-authorized action tokens in envelopes.
- ❌ MUST NOT route comms RI through `EventRulesService.FireAsync` or reuse chat user/session gate scoping.
- ❌ MUST NOT let messaging-r3 wire its own `communication-arrived` producer (spine emits; R3 consumes — §5A.7 #1).

### Existing Patterns to Follow
- Node executors: `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/*NodeExecutor.cs` (`NodeExecutionContext` coupling to unwind).
- Disposition registry: `Services/Ai/DispositionRoutability.cs` (ADR-043 §3 discipline).
- Emit point: `Services/Communication/CommunicationEnrichmentService.cs` step 5 (emit-only, task-010 comments describe intended consumer wiring).
- Null-object precedent: the 18-service migration (ADR-032; `bff-extensions.md` §F.1).
- `appnotification` path: `Services/NotificationService.cs` → `useBriefingNotifications` (mirror target).
- Gate primitives: `Services/Ai/EventRules/EventRulesService.cs` (primitives to reuse; scoping NOT to reuse) + `PendingPlanManager` (ADR-041 gate).
- Dispatch re-entry: `POST /api/ai/chat/sessions/{id}/dispatch` + `SurfaceLaunch` hand-off (assistant-r1 `notes/012-assistant-surface-handoff-design.md`).

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration
```xml
<hot-path-declaration>
  <bff>Y</bff>
  <spaarkeai>Y</spaarkeai>
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```
BFF=Y is **broad but platform-justified**: the spine is the shared delivery infrastructure three sibling projects independently designed; the BFF is the sole policy/token point (design §8). Placement Justification per component below; ≤60 MB ceiling verified per task (NFR-01); SignalR SDK delta gated by FR-01. SpaarkeAi=Y: suggestion renderer + workspace subscriber wiring.

### New Components (§11 three-question gate)
| New component | Existing overlap (grep) | Can extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| Layer-A session-agnostic action seam | `*NodeExecutor.cs` (playbook-run-coupled via `NodeExecutionContext`) | Yes → seam extracted BEHIND the existing executors (this IS the extension) | Comms RI + suggestions cannot create records without a chat session/playbook run; FR-11/13/15 impossible |
| `sprk_` outbox table + service | `appnotification` (no `kind`, no typed payload, no delivered/dismissed/expiry semantics) | No — `appnotification` stays as the MDA mirror; it cannot carry the typed contract | No durable per-user pending truth → push becomes fire-and-forget-only; offline users lose signals (FR-02/06 fail) |
| SignalR delivery service + negotiate endpoint | none (zero SignalR in repo — verified 2026-07-20) | No | No live push at all; R1.5 idle-time suggestions and R3 badges wait for next poll |
| Shared client subscriber library | none (no push client anywhere) | No | Each host (workspace, PCF, code page) hand-rolls connection logic → the forked-consumer failure §6 forbids |
| `kind`-generic pending/poll endpoint | `useBriefingNotifications` (appnotification-only read) | No — different store (outbox), `kind`-generic | No degrade path when SignalR off (NFR-04 fails); R3 polling fallback has no target |
| `communication-arrived` + `communication-assessed` producers | `CommunicationEnrichmentService` step 5 (emit-only log) | Yes → extend the emit point / persist path | R3 FR-22 release gate never unblocks; W5 value (auto Event/Task/Notification) never ships |
| Comms policy rule config | Binding (`sprk_playbookconsumer`) + match conditions (assumed store — see Assumptions) | Yes (assumed) → Binding rows; thin comms rule table ONLY if Binding can't express tenant/matter match conditions | Unmediated auto-actions on every assessed communication (no tenant control) or no actions at all |
| Suggestion producer + renderer branch | Daily-Briefing narrator (writes `appnotification` today) + shipped dispatch/ack mechanisms | Yes → producer extends the narrator's output path; renderer is a chip source on the existing dispatch | R1.5's proactive value never ships; briefing stays pull-only |
| ADR-047 | ADR-043 (dispatch), ADR-045 (communication) — adjacent, not overlapping | No — new capability class (delivery spine) | Next consumer project re-litigates spine ownership; §6's fork risk returns |

## ADR Tensions (per CLAUDE.md §6.5)

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| **ADR-043** | Disposition catalog is the ONE source of truth; `Notification` leg shipped as `Routable=false` | Flipping it routable is a behavior-surface change to the shipped catalog (every chat capability that can emit a notification lights up) | **C (comply, sequenced)** | The flip goes THROUGH the registry (never around it), gated on the FR-14 audit produced from the dispatch side before the change lands |

> No other tensions surfaced. The design's central moves (seam behind executors, gate primitives without chat scoping, outbox-before-ping, facade consumption) were shaped BY the ADRs; all listed ADRs apply without exception.

## Success Criteria
1. [ ] Gate-zero spike recorded: mode chosen, publish size measured vs 55/60 bands, cold-start + CVE + CSP verified — Verify: spike notes + go/no-go decision doc (FR-01).
2. [ ] An assessed inbound communication auto-creates configured Event/Task/Notification records and surfaces in Daily Briefing — Verify: E2E with a matched tenant/matter rule (FR-11/12/13).
3. [ ] A persisted communication (email AND message) yields a `communication-arrived` outbox row + live ping — Verify: seam test both channels (FR-09).
4. [ ] Chat/playbook dispatch behavior is unchanged after Layer-A extraction — Verify: pre-extraction characterization + seam tests pass unmodified (FR-07, NFR-08).
5. [ ] With SignalR disabled (null-object), all signals still deliver via the pending endpoint + `appnotification` mirror — Verify: degrade test (NFR-04, FR-06).
6. [ ] A private thread's event reaches ONLY shared participants; internal-only never reaches external users — Verify: negative-access seam tests + named security sign-off (FR-08, R-5).
7. [ ] A grounded, gated suggestion renders as a card; acting on it re-enters `SurfaceLaunch`/dispatch identically to the reactive equivalent; ungrounded candidates never reach the outbox — Verify: proactive-vs-reactive seam comparison + negative test (FR-15/16/17).
8. [ ] `DispositionRoutability.Notification` is routable, preceded by the reviewed audit — Verify: audit doc + registry test (FR-14).
9. [ ] The shared subscriber library is consumed by the workspace AND documented for R3's PCF/code-page hosts; R3's task 045 unblocked via the contract-lock note — Verify: R3 acknowledgment at their P1 (FR-05/19).
10. [ ] ADR-047 authored (concise + full) — Verify: files exist, CHANGELOG entry (FR-18).
11. [ ] Every BFF task reports publish size; final ≤60 MB (55 MB review band respected); 0 new HIGH CVE — Verify: per-task notes (NFR-01/06).

## Dependencies

### Prerequisites
- **email-r4 W10 merged** before this project's enrichment/persist-path touches (`Services/Communication/` is theirs until then) — `/conflict-check` per BFF wave.
- **messaging-r3 Phase-1 coordination**: their tasks 002–005 serially edit the same persist/read path NOW — merge-order agreement at R3's P1 (with the FR-19 contract lock).
- **Assistant-r1 docs note** recording the R1.5 scope move — DONE 2026-07-20 (`notes/r1.5-scope-moved-to-notification-spine.md`); their UAT tail (R4-6/11/12) may touch `Services/Ai` — coordinate.
- Shipped substrate (verified on master 2026-07-20): enrichment + `communication_assessed` emit point, `IThreadResolver`/`threadId` contract, `sprk_communicationparticipant` junction, create-flow vertical (`SurfaceLaunch` + P5), `appnotification` path.

### External Dependencies
- **Azure SignalR Service** instance (mode + tier per FR-01; ~$49/mo/unit Standard) — per-customer provisioning via ADR-027 orchestrator.
- Target-env **CSP `connect-src`** allowing the SignalR endpoint (verified in FR-01; silent-fallback risk if missed).
- New Dataverse table (outbox) schema applied per environment; `docs/data-model/` entry.

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Project structure | Separate assistant-R1.5 project, or combined? | **Combined (path iii)** — this project absorbs R1.5; no r1.5 project created; assistant-r1 closes at reactive milestone | §4A scope; suggestion waves in-project; single contract, no cross-project seam |
| Spine ownership | Who builds Layers A–C? | **This project** (2026-07-20 owner directive; supersedes assistant §14.1a assignment) | R-1 retired; assistant-side note filed |
| Project name | Keep `spaarke-notification-spine-r1`? | **Keep** — siblings + ADR-047 reservation already bind to it | No renames to chase |
| ADR number | Which number? | **ADR-047** (gap held open by messaging-r2) | FR-18 |
| `communication-arrived` producer | Spine emits, or R3 wires it? | **Spine emits at persistence for all channels; R3 consumes only** | FR-09; R3 task 045 step 2 becomes verify-only |
| Producer trigger | On-capture vs on-send? | **Both — trigger is persistence** of `sprk_communication` | Answers R3's unresolved question; in FR-19 lock |
| SignalR mode | Default (assistant assumption) vs Serverless? | **Spike decides (FR-01); Serverless recommended, burden of proof on Default** | Layer-C placement gated on spike |
| Suggestion sequencing | Blocked on create flows? | **Dependency satisfied** — create-flow vertical merged 2026-07-20 | Suggestion waves unblocked; still sequenced after comms-RI proving |

## Assumptions

- **Comms rule-config store**: assuming **Binding (`sprk_playbookconsumer`) rows + match conditions** (design §4's own lean); a comms-specific rule table only if Binding provably cannot express tenant/matter match conditions — decide at the comms-policy task with grep evidence (§11 gate re-checked there).
- **Outbox payload**: assuming envelope-shaped JSON column + regarding lookup on the outbox row (not a full grounded-payload store); producers persist action detail in their own domain records.
- **Wave order** (plan-level, from design §4A/§5A.7): gate-zero → Layers B+C + `communication-arrived` producer (**unblocks R3 earliest**) → Layer A + Notification flip → comms-RI producer/policy → suggestion waves. Mid-point pause point after comms-RI is legitimate.
- **`kind` string values**: assuming kebab-case strings exactly as locked in FR-10.

## Unresolved Questions

- [ ] **FR-01 outcome** (SignalR mode + placement) — Blocks: all Layer-C implementation tasks (by design; that's the gate).
- [ ] **Rule-store final call** (Binding vs thin table) — Blocks: comms-policy schema/config tasks only; resolve at that task with the §11 evidence.
- [ ] **Outbox table naming + column set** — Blocks: schema task; propose at task time per dataverse-create-schema patterns.
- [ ] **email-r4 W10 merge date** — Blocks: enrichment-touch wave scheduling (not spine-core waves).

---
*AI-optimized specification. Original design: `design.md` (preserved verbatim). Consumer contracts: design §5A/§5B/§5A.7. R3 coordination note: `../../projects/messaging-communication-app-r3` via `spaarke-wt-messaging-communication-app-r3/.../notes/notification-spine-contract-alignment.md`.*
