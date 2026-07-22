# Task 040 — comms_assessed Producer (FR-11): Notes

> **Status**: ✅ Completed 2026-07-21. `CommunicationEnrichmentService` step 5 (`RunAssessmentEmissionAsync`) now publishes the `communication_assessed` signal through a real producer seam instead of only logging. FULL rigor (opus/high). Full BFF suite green; both Step 9.5 gates CLEAN.

## What shipped

| Artifact | Change |
|---|---|
| `Services/Communication/ICommunicationAssessedProducer.cs` (NEW) | `CommunicationAssessedSignal` record (CommunicationId, Direction, Subject, From, RecipientCount) + `ICommunicationAssessedProducer` interface (`PublishAsync`) + `LoggingCommunicationAssessedProducer` interim log-only safe default. |
| `Services/Communication/CommunicationEnrichmentService.cs` | (1) ctor gains required `ICommunicationAssessedProducer assessedProducer` (no direct constructions exist → safe). (2) `RunAssessmentEmissionAsync` rewritten `Task`→`async Task`: builds the signal, calls `PublishAsync` inside a try/catch that logs (distinct, id-correlated) + swallows (NFR-05). (3) stale XML doc ("EMIT-ONLY task 010… task 052 W5… ESCALATION E5") replaced with the current producer description; the "not IEventRulesService" reasoning preserved. |
| `Infrastructure/DI/CommunicationModule.cs` | `services.AddSingleton<ICommunicationAssessedProducer, LoggingCommunicationAssessedProducer>();` — unconditional (ADR-032), next to the enrichment registration. |
| `tests/integration/seam/Communication/CommsAssessedProducerSeamTests.cs` (NEW, 2 tests) | Drives real `EnrichAsync` (all 5 steps via real `RunStepAsync`); doubles only the producer seam + the other steps' deps. (a) success → producer invoked with the exact signal shape; (b) producer throws → enrichment completes, no propagation (NFR-05). |

## Acceptance — all 8 criteria met

1. ✅ Escalation gate: email-r4 W10 merged to master (`5434c2c4b`) BEFORE any edit — trigger did not fire.
2. ✅ Normal comm → `RunAssessmentEmissionAsync` invokes the producer (not just a log) with CommunicationId/Direction/Subject/From/RecipientCount (seam test success case).
3. ✅ Producer throws → enrichment step completes, failure logged with CommunicationId, no propagation (seam test throwing case + the inner try/catch).
4. ✅ No Layer-B outbox row written + `IEventRulesService.FireAsync` never called — verified: the only `Outbox`/`FireAsync` mentions in the new code are in comments describing what it deliberately does NOT do; the code path calls only `_assessedProducer.PublishAsync`, and the default impl only logs. (Outbox write of `kind=communication-assessed` is task 042.)
5. ✅ App starts, DI resolves `ICommunicationAssessedProducer` via the unconditional `LoggingCommunicationAssessedProducer` (full suite runs the real `WebApplicationFactory<Program>` container).
6. ✅ Seam test covers success signal shape + producer-throws non-fatal; pre-existing enrichment tests pass unmodified (full suite 8855/0).
7. ✅ Publish **46.09 MB compressed incl-PDB** ≤60 MB (no package added; ~0 delta vs 024); **0 new HIGH CVE** (`System.Security.Cryptography.Xml 8.0.3` pre-existing — identical set to master).
8. ✅ `/conflict-check` clean (#664 does not touch `CommunicationEnrichmentService`); Placement Justification stated.

## Placement Justification (§10 / §11)

New seam `ICommunicationAssessedProducer` lives in `Services/Communication/` beside its sole emit point (enrichment step 5). **Existing**: nothing publishes `communication_assessed` (the prior step-5 body only logged). **Extension**: cannot reuse `IEventRulesService` (chat-session/SSE-shaped — the original ESCALATION-E5 reasoning holds) nor the task-024 `communication-arrived` path (different trigger: enrichment-gated assessment vs persistence). **Cost-of-doing-nothing**: tasks 041 (policy) + 042 (RI actions/outbox) have no real signal to consume — the pipeline stays an inert log line. **Genuine seam (ADR-010 exception)**: ≥2 implementations — this log-only default + task 041's policy-gate consumer, which registers behind the SAME interface with no change to the emit point. Zero AI-internal dependency (ADR-013 clean).

## Design decisions

- **Producer publish is awaited + inner-isolated** (not detached), mirroring the file's `RunStepAsync` non-fatal idiom. The inner try/catch gives a producer-specific, id-correlated failure log; `RunStepAsync`'s outer guard is defense-in-depth. NFR-06 semantics for the other four steps are untouched.
- **Required ctor param, not optional** — no direct constructions of `CommunicationEnrichmentService` exist (grep-verified), so a required param is safe and cleaner than an optional-with-null-branch; production DI + the seam test both supply it.
- **Interim default preserves exact prior behavior** — `LoggingCommunicationAssessedProducer` emits the same structured `communication_assessed` log the old body did, so behavior is identical before/after task 041 swaps in the real consumer.

## For downstream

- **Task 041** (comms policy layer) registers the real policy-gate consumer behind `ICommunicationAssessedProducer` (replacing/superseding `LoggingCommunicationAssessedProducer`) — the emit point does not change.
- **Task 042** (RI actions via seam) is what writes the `kind=communication-assessed` outbox row + `appnotification` mirror, downstream of the 041 gate. This task produces the input signal only.

## Verification
- `dotnet build src/server/api/Sprk.Bff.Api` (Debug): 0 errors.
- New seam tests: 2/2 green. Full BFF suite: **8855 passed / 0 failed** (101 skipped) — behavior neutral (baseline 8853; +2 new).
- Step 9.5: code-review CLEAN (genuine-seam interface justified; NFR-05 swallow not a smell); adr-check CLEAN (ADR-032/013/045/038/010).
