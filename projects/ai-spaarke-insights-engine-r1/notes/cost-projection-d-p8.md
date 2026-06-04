# D-P8 SPE-upload consumer — first-step blocker resolutions

> **Task**: 050 — D-P8 SPE-upload event consumer (`InsightsIngestJobHandler`)
> **Status**: ✅ Resolved 2026-05-28 (user-authoritative decisions documented inline in parent task brief; this file captures the canonical record)
> **Author**: Claude Code via task 050 execution
> **Acceptance criteria coverage**: this file satisfies criteria #1 (event source) and #2 (cost projection sign-off) from the task POML.

---

## Blocker 1 — SPE-upload event source + dispatch mechanism + auth

### Decision

**Plug into the existing `sdap-jobs` Service Bus queue + `IJobHandler` pipeline.** No new BackgroundService, no new Azure Function.

### Mechanism

```
SPE upload (Office add-in / API)
  → UploadFinalizationWorker (existing BackgroundService on office-upload-finalization queue)
  → QueueNextStageAsync()
       ├─ enqueues AppOnlyDocumentAnalysis job (existing — unchanged)
       ├─ enqueues RAG indexing job (existing — unchanged if AiOptions.RagIndex)
       └─ NEW: enqueues InsightsUniversalIngest job (if AiOptions.InsightsIngest = true)
            ↓ sdap-jobs queue
            ↓
       ServiceBusJobProcessor (existing dispatcher)
            ↓ routes by JobType
       InsightsIngestJobHandler (NEW, Zone B per §3.5)
            ↓ IInsightsAi.RunIngestAsync(InsightsIngestRequest{DocumentId, MatterId, TenantId}, ct)
       InsightsOrchestrator (Zone A — task 042 facade impl)
            ↓ delegates to
       IngestOrchestrator.RunAsync (Zone A — task 040 universal-ingest pipeline)
```

### Why not a separate BackgroundService or Azure Function?

| Option | Pros | Cons | Verdict |
|---|---|---|---|
| New `SpeUploadConsumer : BackgroundService` | Self-contained, dedicated queue | Duplicates cross-cutting (auth, retry, DLQ, idempotency, correlation, telemetry — all already provided by `ServiceBusJobProcessor`); violates ADR-001 single-runtime principle for in-pipeline workloads | ❌ |
| New `Azure Function` (Flex Consumption) | Independent scale + Bicep-deployable | Per ADR-001, Functions are "permitted for out-of-band integration workloads"; the Insights ingest is BFF-coupled async work tied to the existing upload pipeline — explicitly in the BackgroundService bucket | ❌ |
| **`IJobHandler` on existing `sdap-jobs` queue** | Zero new infrastructure; inherits all cross-cutting; opt-in queue wires from already-existing `UploadFinalizationWorker.QueueNextStageAsync` | None for Phase 1 | ✅ |

### Auth

App-only via User-Assigned Managed Identity (UAMI) — `ManagedIdentityCredentialFactory` already configured in `Program.cs` (line 23-25). Per ADR-013 the IngestOrchestrator reads `spaarke-files-index` via the existing `SearchIndexClient` which uses the same UAMI. **No new credential infrastructure required**, per D-27 (no new `ClientSecretCredential`).

### Opt-in pattern

- New field `AiProcessingOptions.InsightsIngest` (bool, default `false`).
- Phase 1: default off — existing CRUD/AI pipelines unchanged.
- D-P16 smoke test (task 070): flips `InsightsIngest = true` on fixture uploads.
- Phase 1.5+ production rollout: flip default on after per-tenant monthly cap signoff (see Blocker 2 below).

### MatterId resolution

`UploadFinalizationPayload` does not carry `MatterId` directly. Resolution happens at queue time in `UploadFinalizationWorker.EnqueueInsightsIngestAsync`:

```
var document = await _documentService.GetDocumentAsync(documentId.ToString(), ct);
if (document?.MatterId is null or "")
{
    log + return;   // Skip (Phase 1 requires a Matter subject for Layer 2 Observations)
}
```

Uses `IDocumentDataverseService.GetDocumentAsync` → `DocumentEntity.MatterId` (string, line 261 of `src/server/shared/Spaarke.Dataverse/Models.cs`). Documents without a Matter lookup are skipped (logged at Information level) rather than queued with a null/empty MatterId, because the universal ingest pipeline's Layer 2 Observations require a matter subject (`matter:{MatterId}`). Phase 1.5+ enhancement could relax this for tenant-scoped Observations.

### Backpressure + retry — inherited (not new)

- `ServiceBusJobProcessor` configures `MaxConcurrentCalls = 5` (per `ServiceBusOptions`); the Insights handler shares this with all other handlers on `sdap-jobs`.
- Peek-lock receive + auto-renew (10 min) + delivery-count threshold = 5 for DLQ.
- Transient (HTTP 429, timeouts, RequestFailed) → `JobOutcome.Failure` → abandon → redeliver.
- Permanent (bad payload, ArgumentException, unknown) → `JobOutcome.Poisoned` → DLQ immediately.

### Telemetry events (structured log)

| Event | When | Fields |
|---|---|---|
| `insights_ingest_invoked` | At entry of `ProcessAsync` | `jobId`, `subjectId`, `correlationId`, `attempt/maxAttempts` |
| `insights_ingest_skipped` | Duplicate/lock-held | `jobId`, `documentId`, `reason`, `idempotencyKey` |
| `insights_ingest_succeeded` | After `RunIngestAsync` returns | `jobId`, `documentId`, `matterId`, `tenantId`, `observationsEmitted`, `layer1Classification`, `layer2Triggered`, `elapsedMs` |
| `insights_ingest_failed` | Any exception → poison/retry | `jobId`, `reason` (`invalid-payload`/`argument-invalid`/`transient`/`unexpected`), `error` |
| `insights_ingest_canceled` | Host shutdown | `jobId`, `elapsedMs` |
| `insights_ingest_payload_parse_failed` | JsonException in `ParsePayload` | `error` |

These flow into App Insights via the existing `ILogger` → Application Insights bridge (no new telemetry plumbing).

### Idempotency

Per ADR-004: `IdempotencyKey = $"insights-ingest-{documentId}-{matterId}"` (composed in `UploadFinalizationWorker.EnqueueInsightsIngestAsync` and re-composed defensively in the handler if absent). Substrate writes inside `ObservationIndexUpserter` already use deterministic Observation ids (`MergeOrUpload` overwrites in place), so the pipeline is naturally idempotent at the data layer; the `IIdempotencyService.IsEventProcessedAsync` check short-circuits to avoid duplicate LLM cost.

---

## Blocker 2 — Production live-ingest LLM cost projection

### User-authoritative decision (2026-05-28)

**Per-document hard cap: $0.10/document** (Phase 1 enforcement = observability + alerting; hard-block deferred to Phase 1.5).
**Per-tenant monthly cap: deferred to Phase 1.5** (observability TODO comment in handler; aggregation requires new metric infrastructure).

### Cost projection

Based on Azure OpenAI list pricing (text-embedding-3-large + gpt-4o-mini, eastus2 region, as of 2026-05-28):

| Layer | When fires | Avg prompt tokens | Avg completion tokens | Cost/document |
|---|---|---|---|---|
| Layer 1 classification | Every document (D-59 cheap-gates-expensive) | ~2000 (sanitized doc text) | ~150 (JSON: classification + confidence + reasoning) | ~$0.001 |
| Layer 2 outcome extraction | Outcome-bearing + Layer1 confidence ≥ 0.7 | ~4000 (full doc + structured prompt) | ~600 (structured outcome JSON) | ~$0.05 |
| Embedding (per Observation) | Per emitted Observation (Layer 1: 1; Layer 2: 1-5 typical) | ~50 tokens (predicate + value) | n/a | ~$0.0001 each |

**Typical totals**:
- Non-outcome-bearing document (Layer 2 gated off): ~$0.001 + 1×$0.0001 ≈ **$0.0011**
- Outcome-bearing document (Layer 1 + Layer 2 + 3 embeddings): ~$0.001 + $0.05 + 3×$0.0001 ≈ **$0.0513**
- Worst case (large doc, Layer 2 fires + 5 Observations): ~$0.001 + $0.08 + 5×$0.0001 ≈ **$0.0815**

**$0.10 hard cap rationale**: ~2x headroom over expected worst case. Documents projected > $0.10 (e.g., 100K-char closing letters with 10 outcomes) get a warning + metric emit; they still process in Phase 1 (observability-only). If a tenant consistently hits the cap, Phase 1.5 hard-block ships.

### Phase 1 enforcement (observability)

- Per-document cost projection happens inside `OpenAiClient` (existing, lives in Zone A) — the `InsightsIngestJobHandler` itself does not perform cost math.
- When `OpenAiClient` detects projected cost > $0.10 for the cumulative operation tagged with the ingest correlation id, it emits `insights.ingest.cost.cap_exceeded` (App Insights custom event) with: `documentId`, `matterId`, `tenantId`, `projectedUsd`, `actualTokens`.
- Phase 1: this is **observability only — does NOT block**.
- Phase 1.5: hard cap enforcement (return `JobOutcome.Poisoned` with `"cost-cap-exceeded"` reason) ships after first-month metric review.

### Per-tenant monthly cap (DEFERRED — Phase 1.5)

Aggregation across documents per tenant per month requires:
1. Persistent counter (Redis `INCRBYFLOAT` per `{tenantId}:{yyyyMM}` key with TTL of 35 days)
2. Read-before-process gate inside the handler
3. Admin override + reset mechanism

These are scoped to Phase 1.5 (task index TBD); marked with a TODO in `InsightsIngestJobHandler.ProcessAsync` for visibility.

---

## Sign-off

| Item | Status | Signoff |
|---|---|---|
| Event source decision (`IJobHandler` on `sdap-jobs`) | ✅ Confirmed | User (task 050 brief) |
| Auth mechanism (UAMI, no new credentials) | ✅ Confirmed | ADR-013 + D-27 + Program.cs:23-25 |
| Opt-in default-off (`AiOptions.InsightsIngest = false`) | ✅ Confirmed | User (Phase 1 default-off preserves existing flows) |
| MatterId resolution (`DocumentEntity.MatterId` via `IDocumentDataverseService`) | ✅ Confirmed | Code review (Models.cs:261 + DataverseServiceClientImpl.cs:633) |
| Per-document hard cap $0.10 (observability-only Phase 1) | ✅ Confirmed | User (task 050 brief) |
| Per-tenant monthly cap deferred to Phase 1.5 | ✅ Confirmed | User (task 050 brief) |
| Backpressure inherited from existing `ServiceBusJobProcessor` | ✅ Confirmed | ADR-004 + ServiceBusJobProcessor.cs (no new mechanism) |
| Telemetry events surface in App Insights | ✅ Confirmed | Existing ILogger → AI bridge |

This file is **user-authoritative for task 050** and supersedes any conflicting guidance in the SPEC or POML re: "Function vs BackgroundService" choice.
