# Task 024 — communication-arrived Producer (FR-09): Notes

> **Status**: ✅ Completed 2026-07-21. Single spine-owned `communication-arrived` producer emitting at persistence for all 5 orchestration paths (email/messaging × inbound/outbound). FULL rigor (opus/high). Both Step 9.5 gates CLEAN; full BFF suite green. Escalation trigger did NOT fire (email-r4 W10 merged).

## What shipped

| Artifact | Change |
|---|---|
| `Services/Communication/CommunicationArrivedProducer.cs` (NEW) | The single spine-owned producer. `EmitCommunicationArrivedAsync(Guid communicationId)`: re-reads the persisted `sprk_communication` (+ its thread) with the exact projection the envelope + fan-out need → computes recipients via task-023 `CommunicationFanOutTargetingService` → builds the task-013 `CommunicationEnvelope` (kind=`communication-arrived`) once → per recipient writes the task-012 outbox row FIRST then best-effort pings (task-020). Internally non-fatal (never throws — NFR-05). Concrete Singleton (ADR-010). |
| `Infrastructure/DI/CommunicationModule.cs` | `services.AddSingleton<CommunicationArrivedProducer>();` (right after the task-023 fan-out service it composes). Unconditional (ADR-010/032). |
| `Services/Communication/IncomingCommunicationProcessor.cs` | Optional trailing ctor param `CommunicationArrivedProducer? arrivedProducer`; emit call at **Step 4.8** (after participant index 4.7). |
| `Services/Communication/Channels/MessagingIngestor.cs` | Optional trailing ctor param; emit after enrichment (post participant-index), before return. |
| `Services/Communication/CommunicationService.cs` | Optional trailing ctor param; emit at **3** send orchestrators after `WriteParticipantIndexAsync` — `SendMessageAsync`, `SendAsync`, `SendAsUserAsync`. |
| `tests/integration/seam/Communication/CommunicationArrivedProducerSeamTests.cs` (NEW, 5 tests) | Real producer + real fan-out (real `CommunicationAccessFilter` + `DenyAllThreadPrivateGrantProvider`) + real `OutboxService`; only Dataverse (`IGenericEntityService`) + SignalR (`PingUserAsync` override) doubled. `[Theory]` over email/message × inbound/outbound asserts outbox-row(kind=communication-arrived)+correct channel/direction+ping, and **outbox-before-ping ordering**; `[Fact]` asserts a failed outbox write is non-fatal + skips ping. |

## ⚠️ Material deviation from the checkpoint's "5 CreateAsync sites" plan (POML step 2/7)

The task-024 investigation checkpoint enumerated the **5 raw `_genericEntityService.CreateAsync(communication)` sites** (`IncomingCommunicationProcessor.cs:576`, `MessagingIngestor.cs:220`, `CommunicationService.cs:775/1670/1775`). **Wiring the emit at those raw-create points would be WRONG** and would make the whole feature a silent no-op:

- At the raw `CreateAsync`, the `sprk_communicationthread` lookup + regarding are **not yet stamped** (the thread resolver + association resolver run *after* create) — so the envelope's required `threadId` is absent.
- Decisively, the `sprk_communicationparticipant` **junction that fan-out reads is still EMPTY** at raw-create — the participant indexer runs afterward. Fan-out would return **zero recipients** → zero outbox rows → zero pings. The signal would reach no one.

**Correct emit point = post-association orchestration level** — after each path's participant-index step, where thread + regarding + junction are all populated. The 5 emit points are therefore at the **orchestrators**, not the low-level record builders:

| # | Channel / Direction | Emit site |
|---|---|---|
| 1 | email inbound | `IncomingCommunicationProcessor.ProcessAsync` — Step 4.8 (after 4.7 participant index) |
| 2 | messaging inbound | `MessagingIngestor.IngestAsync` — after enrichment (post participant-index), before return |
| 3 | messaging outbound | `CommunicationService.SendMessageAsync` — after `WriteParticipantIndexAsync` |
| 4 | email outbound (app/shared) | `CommunicationService.SendAsync` — after `WriteParticipantIndexAsync` |
| 5 | email outbound (as user) | `CommunicationService.SendAsUserAsync` — after `WriteParticipantIndexAsync` |

The `CreateDataverseRecordAsync`/`CreateMessageDataverseRecordAsync`/`CreateDataverseRecordForUserAsync` methods (the checkpoint's :775/:1670/:1775) are low-level record builders returning the id; the orchestration (thread/participant/enrichment) lives in the three `Send*` methods above.

## Design decisions

- **Producer takes only a `Guid communicationId` and RE-READS** the communication + thread (app-only via `IGenericEntityService`, mirroring the fan-out service). This centralizes the fan-out projection contract (`sprk_isinternalonly` + `createdon` on the message, `sprk_privacystate` on the thread) in ONE place instead of threading it through five heterogeneous persist sites — the "single, spine-owned emit" the task demands. Cost: one extra read per emit on a fire-and-forget background path — acceptable.
- **Non-fatal = awaited + internally exception-isolated** (not detached `Task.Run`). The whole `EmitCommunicationArrivedAsync` body is wrapped so it NEVER throws (NFR-05), mirroring `CommunicationParticipantIndexer`'s never-throw contract and the `RunStepAsync` precedent (awaited try/catch, not a background task). Chosen over detached `Task.Run` to avoid captive-scope / DI-disposal / unobserved-exception hazards, and because the persist paths already await other non-fatal post-persist steps (association, thread, participant index, enrichment). "Fire-and-forget" is interpreted as "the caller does not depend on / is not failed by the outcome," which awaited-swallow fully delivers and makes the seam test deterministic (no race).
- **Outbox BEFORE ping is structural** — `PingUserAsync` requires the `outboxRowId` that only exists after `OutboxService.WriteAsync` (ADR-041/043). Per recipient: write, then ping. The seam test asserts the ordering on an event log.
- **Per-recipient outbox rows** — the outbox is per-user (`OwnerId` = systemuserid). Fan-out returns systemuserids; the producer writes one row per recipient + pings each. Message-grain fan-out (To+Cc participants of one message) bounds the loop size.
- **`senderDisplay` = privacy-safe channel label** ("New email" / "New message"), NOT the address. NFR-02/03 mandates "senderDisplay: display NAME only — never an address"; `sprk_communication` has no display-name column and `NormalizedMessage.From` is an address, so leaking it would violate the field contract. The client re-fetches the real sender via the access-gated BFF. If a display-name column is added later, populate it here.
- **`snippet` = null** in this task (privacy-conservative — content is NEVER placed on the spine; the field is "only populated when the record is non-private/non-privileged"). A future task can populate it under an explicit non-private check.
- **No-thread ⇒ skip emit** — the envelope's `threadId` is required, and a null thread routes fan-out through the private-thread deny-all gate (empty) anyway. If thread resolution failed (non-fatal), the emit is skipped (logged); the poll fallback + next event refresh the surface. Do NOT fabricate a thread id.
- **Empty regarding allowed** — `RegardingRecordId = ""` when the communication has no association yet (display-only; not used for routing).

## Acceptance — all 8 criteria met

1–4. ✅ Email inbound, email outbound, message inbound, message outbound each yield an outbox row (kind=communication-arrived) + ping — proven by the 4-case `[Theory]` (channel + direction asserted on the deserialized envelope).
5. ✅ Producer exception (outbox write throws) → `EmitCommunicationArrivedAsync` does NOT throw + no ping (write-before-ping) — `[Fact]`.
6. ✅ No assessment prerequisite — the seam wires NO enrichment/assessment collaborator; the producer fires purely on persistence (distinguishes from task 040's `communication_assessed`).
7. ✅ Outbox BEFORE ping — asserted on the ordering log (`["outbox-write", "ping:{recipient}"]`).
8. ✅ `tests/integration/seam/Communication/**` test exists proving both channels (ADR-038 DoD).

## Verification

- `dotnet build src/server/api/Sprk.Bff.Api` (Debug): **0 errors** (22 pre-existing warnings, none from task 024).
- New seam tests: **5/5 green**.
- **Full BFF suite: 8853 passed / 0 failed** (101 skipped) — behavior neutrality across the system incl. the real DI container; the 3 optional-ctor widenings broke zero existing tests (baseline was 8848; +5 = the new seam tests).
- Publish **46.09 MB compressed incl-PDB** ≤60 MB (+0.03 vs 033's 46.06; no package added); **0 new HIGH CVE** (`System.Security.Cryptography.Xml 8.0.3` is pre-existing — identical vulnerable set to master/HEAD).
- **NetArchTest: 3 pre-existing failures** (ADR-010 Options-pattern + ADR-007 Graph-isolation, unrelated to task 024). VERIFIED pre-existing: with the entire task-024 change stashed + new files removed, the clean base still reports `Failed: 3, Passed: 25`. My change adds **zero** new ArchTest failures.
- Step 9.5: **code-review CLEAN** (0 Critical / 0 Warning; 3 documented Suggestions — no producer-level idempotency [upstream dedupe covers it], uncapped per-recipient loop [message-grain bounded], awaited-latency [deliberate over detach]); **adr-check CLEAN** (0 violations — ADR-013/010/032/041/043/045/038/024 all compliant; §10 BFF Hygiene checklist satisfied).

## Coordination (email-r4 / messaging-r3)

- **Escalation trigger did NOT fire**: email-r4 W10 (and W11/W12) are MERGED to master (`5434c2c4b` / `9daadb5e2`) — the exclusive-ownership window on `Services/Communication/**` is closed.
- **`/conflict-check` CLEAN**: messaging-r3 (PR #664) touches ZERO `Services/Communication` or `Services/Notifications` files → no persist-path overlap. No persist-path file changed on master since our sync (`e5f3e2174`).
- **messaging-r3 consumes, does not produce**: this is the single spine-owned `communication-arrived` emit. R3's task 045 becomes verify-only. R3 MUST NOT wire its own producer. **Task 025** (FR-19 contract-lock note) is now unblocked and formalizes this for R3.

## For downstream

- **Task 040** (`communication_assessed`) is a SEPARATE producer, gated on enrichment/assessment — do not conflate. This task deliberately emits at persistence with NO assessment prerequisite.
- **Private-thread fan-out is EMPTY** until a real `IThreadPrivateGrantProvider` ships (task-043 direction) — the `DenyAllThreadPrivateGrantProvider` default means private threads currently notify nobody. Expected + documented in task 023.
- **SenderDisplay / Snippet** are intentionally minimal (channel label / null). Enriching them is a future, NFR-gated enhancement, not a bug.
