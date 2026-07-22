# Current Task State — spaarke-notification-spine-r1

> **Last Updated**: 2026-07-22 (task 042 COMPLETE — Phase 4 DONE).
> **Recovery**: Read "Quick Recovery" first. Branch `work/spaarke-notification-spine-r1`. Phases 1-3 (001-033) live on master; **Phase 4 (040/041/042) COMPLETE on branch** (batching master-merge per owner). Next: **050** (suggestion producer, Phase 5 start).

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **051 — Suggestion renderer branch (FR-16), Phase 5 Wave 16 — NOT STARTED** (deps 050✅ 021✅ met). Prior: 024✅ 025✅ 040✅ 041✅ 042✅ **050✅**. Phase 4 COMPLETE; Phase 5 in progress. |
| **Step** | Begin task 051 (`tasks/051-suggestion-renderer-branch.poml`). Tier **sonnet/high**. Frontend: `@spaarke/notifications` client kind-router renders the `kind=suggestion` outbox row (ADR-021 Fluent v9 + dark mode); `actionHint` drives the render + the dispatch it re-enters (052). |
| **Status** | 024/025 on master; **040+041+042+050 committed on branch** (synced to master f8e04ecdc; batching master-merge per owner). Owner-created `sprk_communicationrule` table (Path B, 041). |
| **Next Action** | **"work on task 051"** (renderer). Then 052 (dispatch parity) → 090 wrap-up. Master-merge of 025/040/041/042/050 batched for later per owner. |

### 050 result (Phase 5, Wave 15 — ✅ DONE 2026-07-22)
- **`DailyBriefingSuggestionProducer`** (NEW, `Services/Ai/Narrators/`, sibling of the narrator): per collected high-priority item → **grounding gate (ADR-039)** (real EntityType+parseable EntityId+Name) + **proactive gate (ADR-041, origin=proactive)** (`SuggestionGateOptions.Enabled` AND HighPriority|Monitor) → BOTH pass → one `kind=suggestion` outbox row (013 `SuggestionEnvelope`) via task-012 OutboxService + best-effort task-020 ping; fail either → zero rows + logged. `MaxPerRun` cap. Non-fatal. Scoped.
- **Gate decision (owner-approved 2026-07-22)**: reuse the gate DISCIPLINE (declared-metadata admit, `SuggestionGateOptions`), NOT `PendingPlanManager`'s chat/Redis machinery (§6.5 path A) — the 041 precedent. Outbox row REPRESENTS the pending confirm; real confirm is downstream (051→052).
- **`SuggestionGateOptions`** (`Notifications:Suggestions`): Enabled=**false** deny-by-default (NFR-03), MaxPerRun=3, TtlHours=24. Wired into `DailyBriefingCompositeService.RenderAsync` as an OPTIONAL trailing ctor param (024 pattern → Null peer + existing tests unchanged) + `AddScoped` DI.
- **5 seam tests** (grounded+gated→1 row+envelope+ping; ungrounded→0; disabled→0; not-confirm-worthy→0; cap) against REAL OutboxService. Full suite **8869/0** (narrator/composite tests unmodified — criterion 8); both gates CLEAN; 46.10 MB (≤60); 0 NEW CVE. Notes: `notes/050-suggestion-producer-notes.md`.
- **For 051/052**: 051 renders the `kind=suggestion` row (actionHint drives render); 052 is the confirm/dispatch parity (the confirmation the outbox row represents). Enable per-env via `Notifications:Suggestions:Enabled=true`.

### 050 investigation + LOCKED design (Phase 5, Wave 15 — IN PROGRESS, not yet implemented)
**Rigor**: FULL (opus/high). conflict-check CLEAN (no PR touches BFF `Ai/Narrators/**`; spaarkeai-assistant-enhancements-r1 already merged). **Files read**: `SuggestionEnvelope.cs` (013), `DailyBriefingCompositeService.cs` (run path), `PendingPlanManager.cs` (gate), `DailyBriefingEndpoints.cs` DTOs.
- **Candidate source (grounding, ADR-039)**: `HighPriorityItemDto[]` (from `DailyBriefingCollector.CollectHighPriorityAsync`) — each carries `EntityType` + record `Guid` + `Name` + optional `DueDate`. A candidate is GROUNDED iff it traces to a collected high-priority item with non-empty EntityType+RecordId+Name. `PriorityItemDto` (category/title, NO record id) is NOT a valid grounding source for a regarding-bound suggestion. No re-fetch — read exactly what the composite already collected.
- **Run-path wiring**: new `DailyBriefingSuggestionProducer` (sibling in `Services/Ai/Narrators/`, NEVER inside `DailyBriefingNarrator`). Called from `DailyBriefingCompositeService.RenderAsync` (has `systemUserId` = outbox recipient + `tenantId` + collected `payload`/`highPriorityItems`) as a sibling step to `ExecuteAsync`. Narrator + its tests stay byte-identical.
- **`SuggestionEnvelope` (013, 8 fields)**: Kind=Suggestion, SuggestionId(new Guid), Source="daily-briefing", RegardingRecordId=item.RecordId, Title="Review {Name}", Snippet=null (conservative), ActionHint="review", ExpiresAt=now+window. → `OutboxService.WriteAsync(systemUserId, NotificationKind.Suggestion, envelope, regardingRecordId=item.RecordId, regardingRecordType=item.EntityType, expiresAt, ct)` then best-effort `SignalRDeliveryService.PingUserAsync` (parity with 024/042). Non-fatal outer try/catch (mirror 042).
- **⚠️ OPEN DECISION — the ADR-041 gate (`origin=proactive`)**: `PendingPlanManager` is the ONE confirmation gate but is chat/SSE-shaped (Scoped, Redis suspend/resume, needs a live `ChatSession`) — wiring it whole into a proactive producer is the chat-seam coupling the constraint forbids. **041 precedent**: reuse the gate PRIMITIVE/discipline (`RequiresConfirmation` = declared-metadata→decision), NOT the chat machinery; document the deviation. Proposed for 050: a small metadata-driven admit decision tagged `origin=proactive` (a `SuggestionGateOptions` policy dial — e.g. proactive-suggestions-enabled + a per-item admissibility rule), the outbox row REPRESENTS the pending confirmation (actual user confirm is downstream: 051 renderer → 052 dispatch). Fail grounding OR gate → ZERO rows + logged (POML criteria 2/3). **This is the FR-13/escalation-flagged judgment call — surfaced to owner before implementing.**
- **Tests**: unit (grounded+gated→1 row correct envelope; ungrounded→0; grounded-but-ungated→0) + `tests/integration/seam/**` (candidate→ground→gate→REAL OutboxService write / no-write). Narrator/Collector/Composite existing tests unmodified.
- **Next action**: resolve the gate decision (owner), then implement + wire + test + gates + notes + TASK-INDEX 050→✅.

### 042 result (Phase 4, Wave 14 — ✅ DONE 2026-07-22)
- **`CommunicationRiActionService`** (NEW, `Services/Communication/`): on the 041 gate's AUTHORIZE, converges four seams in order — Layer-A `IActionSeam.CreateTaskAsync` (031, ADR-013 — creates a `task`, never a direct write) → `OutboxService.WriteAsync` kind=communication-assessed (012) FIRST → best-effort `SignalRDeliveryService.PingUserAsync` (020) → explicit `NotificationService.CreateNotificationAsync` appnotification mirror. Whole path non-fatal (NFR-05). Concrete singleton.
- **`RuleGatedAssessedConsumer`**: authorize branch now delegates to the RI service (was log-only). DENY unchanged → structural short-circuit (no seam/outbox/ping/appnotification). DI: `AddSingleton<CommunicationRiActionService>` before the consumer.
- **Design**: seam action = `task` (mirror = appnotification → no double-write); recipient = communication `ownerid` (proving scope; matter-team fan-out is a documented future enhancement — surfaced to owner). Confidence still deny-by-default until real plumbing (authorize path fully seam-tested).
- **2 seam tests** (E2E authorize ordering `seam→outbox→ping→mirror` + deny zero-side-effect). Full suite **8862/0**; both gates CLEAN; 46.10 MB (≤60); 0 new CVE. conflict-check SOFT WARN (r3 #664 disjoint files). Notes: `notes/042-ri-actions-via-seam-notes.md`.
- **For Phase 5**: 050 reuses this exact producer pattern with `SuggestionEnvelope` (kind=suggestion).

### 041 result (Phase 4, Wave 13 — ✅ DONE 2026-07-22)
- **§11 escalation fired** (Binding's shared r2-owned resolver can't see tenant/matter) → **owner chose Path B**: dedicated `sprk_communicationrule` Dataverse table (owner-created live). ADR-039 exception documented (Path A). Evidence + decision: `notes/041-rule-store-decision.md`.
- **`CommunicationRuleGate`** (`Services/Communication/CommunicationRuleGate.cs`): reads the table, matches tenant(blank=all)∧matter(empty=all), lowest priority wins, authorize ⇔ confidence ≥ (rule threshold ?? `CommsPolicyOptions.DefaultConfidenceThreshold` 0.8), privilege FLAGGED never decided (ADR-015), logs every decision, fail-closed DENY on read error. Concrete (ADR-010).
- **`RuleGatedAssessedConsumer`** replaces 040's log-only default behind the `ICommunicationAssessedProducer` seam (emit point unchanged); re-reads `sprk_regardingmatter`, runs the gate; on authorize EXECUTES NOTHING (that's 042). No outbox, no `FireAsync`.
- **Confidence-source boundary (documented)**: signal gained `Confidence` (default 0 → DENY-by-default = safe; no ungoverned action). Real confidence plumbing is 042/downstream.
- Tests 5/5 (4 branches + fallback + priority). Full suite 8860/0; code-review CLEAN; adr-check clean except documented ADR-039 exception; 49.83 MB (≤60); 0 new CVE. Notes: `notes/041-comms-policy-layer-notes.md`.
- **For 042**: execute on the gate's authorize; write `kind=communication-assessed` outbox + appnotification mirror; plumb a real assessment confidence into the signal.

### 040 result (Phase 4, Wave 12 — ✅ DONE 2026-07-21)
- `CommunicationEnrichmentService` step 5 now publishes `communication_assessed` via the NEW `ICommunicationAssessedProducer` seam (`Services/Communication/ICommunicationAssessedProducer.cs`: signal record + interface + `LoggingCommunicationAssessedProducer` interim log-only default). Fire-and-forget non-fatal (NFR-05, inner try/catch + `RunStepAsync` guard).
- DI: unconditional `AddSingleton<ICommunicationAssessedProducer, LoggingCommunicationAssessedProducer>` (ADR-032). Required ctor param (no direct constructions exist). Stale "task 010/052/E5" XML doc refreshed.
- **MUST NOTs honored**: no Layer-B outbox write, no `IEventRulesService.FireAsync` (grep-verified; those are task 042). Genuine seam (2 impls: default + task-041 consumer) → ADR-010 interface justified.
- Seam test 2/2 (success signal shape + producer-throws non-fatal). Full suite 8855/0; both gates CLEAN; 46.09 MB; 0 new CVE. Notes: `notes/040-comms-assessed-producer-notes.md`.
- **For 041**: register the real policy-gate consumer behind `ICommunicationAssessedProducer` (replaces the logging default); emit point unchanged.

### 025 result (Phase 2, Wave 7 — ✅ DONE 2026-07-21)
- Delivered `notification-spine-contract-lock.md` to messaging-r3's worktree notes dir (`C:/code_files/spaarke-wt-messaging-communication-app-r3/.../notes/`) + mirrored to `notes/handoffs/`. Cites the ACTUAL shipped shapes (013 envelope, 020 negotiate, 021 `@spaarke/notifications` client, 022 `/pending`, 024's 5 call sites). MINIMAL rigor (docs-only; no gates).
- **Escalation trigger FIRED + handled in-note (§6)**: R3's alignment note listed the envelope as **8 fields**; shipped is **9** (adds required `regardingRecordId`). Additive/non-breaking (all 8 assumed fields match) — flagged explicitly per the trigger, not silently reconciled. R3 action: add `regardingRecordId: string` to their type mirror.
- Confirmed for R3: trigger=persistence all channels; spine-emits/R3-consumes (MUST NOT wire own producer); envelope not on live wire (poll for detail); degrade = 503→poll fallback, no signal loss.
- ⚠️ R3-worktree copy is DELIVERED ON DISK but NOT committed to R3's branch (cross-project boundary — their team's call to commit).

### 024 result (Phase 2, Wave 6 — ✅ DONE 2026-07-21)
- **Single spine-owned `communication-arrived` producer** (`Services/Communication/CommunicationArrivedProducer.cs`): re-read comm+thread → task-023 fan-out → task-013 envelope → per-recipient task-012 outbox (BEFORE) → task-020 ping. Internally non-fatal (NFR-05). Concrete Singleton.
- **DEVIATION (documented, POML step 2/7)**: emit at **5 orchestration points AFTER participant-index** (email `ProcessAsync` 4.8; msg `IngestAsync`; `Send{Message,,AsUser}Async` after `WriteParticipantIndexAsync`) — NOT the raw CreateAsync sites the checkpoint enumerated (junction empty + thread/regarding unstamped at raw-create → fan-out would be zero). Full rationale: `notes/024-communication-arrived-producer-notes.md`.
- Injected as optional trailing ctor param into 3 singletons (IncomingCommunicationProcessor, MessagingIngestor, CommunicationService) — existing tests compile ZERO-edit.
- Seam test (5 green, `tests/integration/seam/Communication/`): email/msg × in/out each yield outbox(kind=communication-arrived)+ping, outbox-before-ping ordering, non-fatal on failure.
- **senderDisplay** = privacy-safe channel label (NFR-02/03: no address; no display-name column exists); **snippet** = null (conservative). Both documented as intentional.
- Gates: code-review CLEAN (3 Suggestions); adr-check CLEAN. ArchTest 3 reds VERIFIED pre-existing (stash-confirmed). Escalation trigger did NOT fire (email-r4 W10 merged); conflict-check clean (#664 touches 0 persist files).

### 024 investigation (DONE — the de-risking part)
- **5 `sprk_communication` row-create persist sites (merged HEAD, authoritative)**:
  1. `IncomingCommunicationProcessor.cs:576` — inbound capture (email)
  2. `Channels/MessagingIngestor.cs:220` — inbound capture (messaging)
  3. `CommunicationService.cs:775` — outbound send variant A (entity built at :735)
  4. `CommunicationService.cs:1670` — outbound send variant B (entity built at :1603)
  5. `CommunicationService.cs:1775` — outbound send variant C (entity built at :1732)
  - EXCLUDED (not communication rows): threads (DirectThreadAccessService:89, ThreadResolver:156/468, MessagingThreadKeyStrategy:71), participants (CommunicationParticipantIndexer:104), attachment docs (CommunicationService 388/1972/2095/2140, IncomingCommunicationProcessor 790/824/903, MessageAttachmentMaterializer 151/163), and the read/update site (CommunicationThreadReadService:771 — has an id, it's an update).
- **Producer design** (per POML + spec FR-09/NFR-05/08): build a **task-013 `CommunicationEnvelope`** (kind=communication-arrived; IDs + minimal display metadata only — communicationId/threadId/channel/direction/senderDisplay/optional privacy-gated snippet/badgeDelta; NO body) → **`OutboxService.WriteAsync`** (durable truth FIRST) → **`CommunicationFanOutTargetingService`** (task 023) to compute recipients → **`SignalRDeliveryService` ping** (best-effort, requires outboxRowId). Wrap the whole emit **fire-and-forget non-fatal**, mirroring `CommunicationEnrichmentService.RunAssessmentEmissionAsync` (~lines 216-238) — a producer exception MUST NOT fail the persist call (NFR-05).
- **Files to read before implementing**: `Services/Notifications/Envelopes/*` (013 CommunicationEnvelope shape), `OutboxService.cs` (WriteAsync signature), `SignalRDeliveryService.cs` (ping signature — bottom half), `CommunicationFanOutTargetingService.cs` (023 targeting API), `CommunicationEnrichmentService.cs:~216-238` (fire-and-forget precedent), and each of the 5 sites' surrounding method for available locals (communicationId, thread, direction, channel, sender).
- **Placement decision (POML step 3)**: producer needs Communication read (envelope fields) + Notifications (outbox + delivery) → likely `Services/Notifications/CommunicationArrivedProducer.cs` injected into the Communication persist path, OR `Services/Communication/`. Decide per dependency shape; state Placement Justification (§10).
- **DoD**: seam test under `tests/integration/seam/Communication/` proving BOTH channels (email + message) each yield an outbox row (kind=communication-arrived) + a ping, AND a producer exception does NOT fail the persist. Outbox-before-ping ordering asserted.

### 033 Quality Gates (Step 9.5) — both CLEAN
- **code-review**: 0 Critical / 0 Warning / 1 informational (OutputRouter.cs 523 lines >300 — cohesive Email-mirror leg, don't split). ADR-013 facade discipline confirmed; NFR-07 identifiers-only logging.
- **adr-check**: 0 violations. ADR-043 (through-the-registry Path C), ADR-013 (IActionSeam facade), ADR-040 (store-before-render), ADR-010 (no new interface; 5 ctor params <7), ADR-032, ADR-038 all compliant. §10 Placement Justification: existing-leg realization, no package, 46.06 MB, 0 new HIGH CVE.
- **Tests**: full BFF suite 8781 passed / 0 failed / 101 skipped. Targeted: 46 disposition/router + 81 dispatch/ActionSeam.

### Files Modified This Session (033)
- `src/server/api/Sprk.Bff.Api/Services/Ai/DispositionRoutability.cs` — Notification entry `Routable=false→true`, removed NotRoutableReason (+ audit-cite comment).
- `src/server/api/Sprk.Bff.Api/Services/Ai/OutputRouter.cs` — added `IActionSeam? actionSeam=null` ctor param (last, optional → DI auto-injects Singleton); `case BindingDisposition.Notification` → `CreateNotificationViaSeamAsync` (parses `notification` envelope, calls seam, loud on `!Success`/missing envelope, `Skipped`=no-op); updated 2 doc comments.
- `tests/integration/seam/Ai/DispositionRoutabilityNotificationSeamTests.cs` — NEW: 3 tests (admit⇔route⇔store happy path; seam-rejects-content→loud-after-store; missing-envelope→loud).
- `tests/integration/seam/Ai/DispositionRoutabilitySeamTests.cs` — removed Notification from not-routable Theory; added it to `Registry_RoutableSet_IsExactlyTheRealizedLegs`.
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/OutputRouterTests.cs` — removed Notification from `RouteAsync_..._ThrowsLoudNotSupported` Theory (now routes).
- No DI edit needed (single ctor; Singleton IActionSeam auto-resolved). DispositionRoutability/OutputRouter were byte-identical to origin/master (conflict-check pass).

### 032 result (Phase 3, Wave 10 — ✅ DONE 2026-07-21)
- **Outcome: nothing lights up.** Live `spaarkedev1` `sprk_playbookconsumer` catalog has **0** rows with `sprk_disposition=notification` (targeted query `[]`; full census = 28 Bindings: 12 Informational + 7 null + 4 SurfaceLaunch + 3 Compose + 1 Email + 1 WorkProduct + **0 Notification**). Code/seed scan agrees (only enum def + registry entry + option-set def + test fixtures — no Binding).
- **Two notification mechanisms distinguished**: Path 1 = `CreateNotificationNodeExecutor` (ActionType 50, playbook-node, writes appnotification TODAY, NOT gated by registry — e.g. daily-update-service playbooks) is **out of scope**; Path 2 = Binding `disposition=notification` → OutputRouter (the flip target) has **zero rows**.
- **Both escalation triggers cleared** (no risky Binding; no per-Binding-granularity dilemma). No ADR-043 §6.5 tension raised.
- **Recommendation**: immediate flip SAFE; no Binding remediation. 033 MUST (1) land OutputRouter leg in same change as registry flip, (2) add `Notification` admit⇔route⇔store seam test. Forward-looking guard: the FIRST future notification Binding lights up with no further registry gate → re-run NFR-02/03 content check when authoring it (033 Trigger 1 already halts on any unanticipated routed capability).
- Deliverable: `notes/what-lights-up-audit.md` (STANDARD rigor; quality gates skipped — docs-only, no code/tests modified).

### 031 result (Phase 3, Wave 9 — ✅ DONE 2026-07-21)
- Session-agnostic Layer-A seam extracted behind the 3 node executors: 3 cores (`Services/Ai/Nodes/ActionCore/*`) + ADR-013 facade (`Services/Ai/PublicContracts/{IActionSeam,ActionSeam}.cs`) + DI (`AnalysisServicesModule.AddNodeExecutors`, unconditional Singleton). Executors refactored to delegate; **constructors byte-identical**.
- **All 8 criteria met**; 030 characterization + existing unit tests pass ZERO-edit; **full BFF suite 8780/0**; publish 46.05 MB; both Step 9.5 gates clean. Notes: `notes/031-layer-a-seam-notes.md`.
- **`IActionSeam` is now available** for Phase 4/5 producers (024/040/050) — they consume it, never the executors or a synthetic `NodeExecutionContext`.
- 033 will touch `DispositionRoutability.cs` + `OutputRouter.cs` (left untouched here per criterion 6).

### 031 design (locked — from POML prescriptive steps 2–4)
- Three `internal sealed` cores under `Services/Ai/Nodes/ActionCore/`: `NotificationActionCore` (+`BuildNotificationEntity`/idempotency moved; `context.RunId`→`correlationId` param, `"playbook"`→`source` param), `TaskActionCore`, `UpdateRecordActionCore` (+coercion helpers + `FieldCoercionException` moved). Each ctor: boundary service (`IGenericEntityService` / `IFieldMappingDataverseService`+`IServiceScopeFactory`) + `ILogger` (base). Input = typed request records; NO `NodeExecutionContext`.
- Executors keep template rendering + ConfigJson parse + NodeOutput wrapping; delegate build+create to a core built inline from their EXISTING injected fields → **constructors frozen** (criterion 4). `ResolveViaMatterMemberships` stays in the executor (reads `PreviousOutputs`).
- `IActionSeam`+`ActionSeam` in `PublicContracts/`; DI mirror the `IBriefingAi` site (`AnalysisServicesModule.cs`) but **unconditional** (record-create is not feature-gated → no NullActionSeam).
- **DoD / escalation**: 030 seam tests + `CreateTaskNodeExecutorTests`/`UpdateRecordNodeExecutorTests` pass with ZERO edits. If a test needs editing → extraction changed behavior → STOP/escalate (root §6), don't edit the test. If frozen-ctor is structurally impossible without dup → STOP (ADR-013/§11 extend-vs-fork tension).
- Conflict-check: master +57 commits but the 3 executors + PublicContracts + OutputRouter + DispositionRoutability + DispatchSessionEndpoint are byte-identical to our branch → no rebase needed; do NOT touch OutputRouter/DispositionRoutability/DispatchSessionEndpoint (task 033's scope).

### Task 030 result (Phase 3, Wave 8 — ✅ DONE 2026-07-21)
- **14 seam tests** authored under `tests/integration/seam/Ai/Nodes/` (CreateNotification 8 / CreateTask 3 / UpdateRecord 3) — the behavior-neutrality safety net for 031. All green vs pre-031 code; `git diff` on the three executors empty.
- Both Step 9.5 gates CLEAN (code-review + adr-check — mandatory per TEST-MODIFYING override). ADR-038 seam KEEP-path, no banned shapes.
- Notes + escalation-trigger disposition: `notes/030-characterization-notes.md` (trigger correctly did NOT fire — 3 defect-adjacent paths are documented intentional contracts).
- ⚠️ **031 contract**: those 14 tests are 031's DoD — they must pass unmodified after the Layer-A extraction.

### Files Modified This Session
All committed + pushed (6 commits `672ec11f6`→`c759dd4c8`). Nothing uncommitted except this file.

### Critical Context (for the fresh session)
The delivery half of the notification spine is built + committed. **Two things need YOUR action, not code:** (1) a **named human R-5 security sign-off** for the fan-out (`notes/023-fanout-security-signoff.md` — fan-out is now correct/load-bearing); (2) the **read-path leak** (`CommunicationThreadReadService` hardcodes `IsInternalUser:true`) is handed to **messaging-r3 (issue #674)** — you said "that has been communicated." Backfill `systemuser.sprk_isexternal` for the 10 users (in progress, manual).

---

## Full State (Detailed)

### What's DONE (all committed + pushed)

| Task | What | Notes |
|---|---|---|
| **001** | FR-01 SignalR footprint spike | **GO / Serverless** (`Microsoft.Azure.SignalR.Management` 1.33.1, in-BFF; +0.30 MB, 0 new HIGH CVE). `notes/spikes/fr-01-signalr-footprint.md`. |
| **010** | ADR-047 (concise + full) | Notification & Action Spine; Proposed; claims the ADR-046/048-reserved number. |
| **011** | `sprk_notificationoutbox` table | Created **live in spaarkedev1** (Dataverse MCP). kind=text, envelope=Memo4000, ADR-024 minimal regarding, native ownerid. |
| **012** | `OutboxService` | Write/GetPending/Dismiss + read-time expiry; unconditional DI; seam test (interpreting-fake boundary). |
| **013** | Envelope contract | `NotificationKind` closed enum + Communication/Suggestion envelopes; 33 tests. |
| **020** | SignalR delivery + negotiate | Serverless `SignalRDeliveryService` (PingUser/PingGroup, signal-only), `POST /api/notifications/negotiate` (oid from JWT), ADR-032 null-object. Seam 8/8. |
| **021** | `@spaarke/notifications` client lib | negotiate→connect→kind-router→poll fallback; 26/26; SpaarkeAi wire-in. |
| **022** | `GET /api/notifications/pending` | SignalR-agnostic degrade path; oid→systemuserid via resolver. Seam 6/6. |
| **023** | `CommunicationFanOutTargetingService` | Recipients from record security (junction + access filter + fail-closed grant gate); **now uses authoritative `sprk_isexternal` flag**. Seam 7/7. |
| **identity fix** | `ISystemUserIdentityResolver` | systemuserid↔oid (cached, fail-open) + `IsExternalAsync` (sprk_isexternal, fail-closed). `Services/Identity/`. |

### Applicable ADRs / contracts the NEXT tasks must honor
- **Producers ping by Dataverse `systemuserid`** (outbox `OwnerId`); `PingUserAsync` resolves systemuserid→oid internally. Producers never touch oid.
- **Fan-out (023) fail-closes to ZERO** unless the producer projects `sprk_isinternalonly` + `createdon` (message) + `sprk_privacystate` (thread).
- **Outbox BEFORE ping** (store-before-render, ADR-041/043). Spine is dumb transport (IDs + display metadata only; NFR-02/03).
- **Layer A (Phase 3)**: extract the seam BEHIND `*NodeExecutor.cs` (ADR-013 PublicContracts); characterization tests pin the chat path FIRST (task 030). `031` = highest blast radius.
- **033 = ADR-043 Path C**: flip Notification `Routable=false`→true via `DispositionRoutability` + add the matching `OutputRouter` switch leg in the SAME change (both must land together or it throws).

### Runtime / ops still needed (not code)
- Azure SignalR resource + `Notifications:SignalR:ConnectionString` (Key Vault, ADR-027/028) in spaarkedev1 for LIVE push; absent ⇒ null-object/poll only.
- Verify `wss://*.service.signalr.net` in the target Power Platform env CSP (else silent poll fallback).
- Backfill `systemuser.sprk_isexternal` (10 users, manual — in progress).

### Open items / tracked
- **023 R-5 sign-off**: NAMED HUMAN required — `notes/023-fanout-security-signoff.md` (fan-out resolved; read-path pending r3).
- **ISS-001 / #674**: read-path internal-only fix — handed to messaging-r3; `notes/HANDOFF-messaging-r3-internal-only-readpath.md`. CI guard test (ban literal `IsInternalUser:true`) deferred to that hand-off.
- **DEF-001 / #673**: consolidate 6 ad-hoc oid↔systemuserid copies onto `ISystemUserIdentityResolver`.

### Blockers
- **024** (communication-arrived producer) + **040** (comms-assessed) BLOCKED on **email-r4 W10 merge** (owns `Services/Communication/**` persist path). **025** (R3 contract-lock) needs 024.
- Phase 3 (030→033) is **NOT** blocked by email-r4 → the unblocked path forward.

### Cross-project coordination
- **messaging-r3 (PR #664)**: consumes `communication-arrived` (its task 045 needs our FR-19 contract lock via 025); ALSO owns `CommunicationThreadReadService.cs` (read-path hand-off #674). `/conflict-check` before editing any `Services/Communication/**` file.
- **email-r4**: owns `Services/Communication/**` until W10.

### Decisions Made (this session)
- 2026-07-21: GATE 001 = GO/Serverless; Wave 1/Phase 1/Phase 2 Layer C executed via parallel sub-agents; identity keyed systemuserid (producers) ↔ oid (SignalR) via shared resolver; **internal/external now via authoritative `systemuser.sprk_isexternal` (two-option, default No), NOT the systemuser proxy** (owner: external users can be licensed systemusers); read-path fix handed to messaging-r3 (conflict with PR #664).

---

## Recovery Instructions
1. Read Quick Recovery above.
2. To continue: **"work on task 030"** (Phase 3 start) → invokes `task-execute` on `tasks/030-characterization-tests-dispatch.poml`.
3. Reference: `tasks/TASK-INDEX.md` (status), `plan.md` (waves), `CLAUDE.md` (constraints).
4. Before any `Services/Communication/**` edit: `/conflict-check` (messaging-r3 active).
