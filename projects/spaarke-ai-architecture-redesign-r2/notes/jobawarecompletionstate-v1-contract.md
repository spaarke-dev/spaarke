# JobAwareCompletionState v1 — contract note

> Task AIR2-014 · spec FR-A0-07 · design D-F2 / R20 · Phase A0 (seam-first).
> Status: contract shipped with reference producer + consumer + self-contained contract test.

## What it is

A **versioned, tolerant-reader PROJECTION** over the EXISTING Spaarke job pipeline. It
introduces **no new job model**. It projects into **nine per-step states** over a
**consumer-declared ordered step set**, and computes an operation-level **aggregate**
whose whole purpose is to make "record exists" distinguishable from "downstream
analysis/indexing finished" (NFR-12 ingestion parity).

- Contract type: `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/JobAwareCompletionState.cs`
- Test: `tests/integration/contract/Api/Ai/JobAwareCompletionStateContractTests.cs`

## Reused (no new job model)

| Reused surface | Where | Role in the projection |
|---|---|---|
| `JobContract` | `Services/Jobs/JobContract.cs` | Supplies `JobId`, `JobType`, `Attempt`, `MaxAttempts` |
| `JobStatus` (3 values) | `Services/Jobs/JobOutcome.cs` | The stored outcome projected per step |
| `ServiceBusJobProcessor` disposition | `Services/Jobs/ServiceBusJobProcessor.cs` | Complete → Abandon(retry) → DeadLetter(poison) is mirrored by the projector |

## Job-status → JobAwareState mapping (the projection)

The existing pipeline has only 3 stored statuses. The nine states are derived from those
plus stored projection-level signals (started flag, attempt counts, cancellation, gate) —
**none of which require adding fields to the job model**:

| Stored signal | Projected `JobAwareState` |
|---|---|
| `JobStatus.Completed` | `Completed` |
| `JobStatus.Failed` AND `Attempt < MaxAttempts` | `RetryPending` (processor abandons → redelivers) |
| `JobStatus.Failed` AND `Attempt >= MaxAttempts` | `Failed` |
| `JobStatus.Poisoned` (or max-attempts dead-letter) | `Poisoned` |
| no stored outcome, not started | `Queued` |
| no stored outcome, started | `Running` |
| stored cancellation before completion | `Cancelled` |
| stored gate pause (ADR-041), no terminal outcome | `UserActionRequired` |
| aggregate: some steps done + some not-yet-done | `Partial` |

## Terminal / non-terminal map

- **Terminal**: `Completed`, `Failed`, `Poisoned`, `Cancelled`
- **Non-terminal**: `Queued`, `Running`, `Partial`, `RetryPending`, `UserActionRequired`

## Aggregate rule (ingestion parity core)

Most-severe attention-requiring state wins first (`Poisoned` → `Failed` → `Cancelled` →
`UserActionRequired`); otherwise **any step done but not all → `Partial`** (never
`Completed` until EVERY step is `Completed`). This is what stops a bare `sprk_document`
row from being reported as a successful document operation.

## Versioning / tolerant reader

- `Version` field (currently `1`, `CurrentVersion` const).
- States serialize as **stable strings** (`JsonStringEnumConverter`) — additive future
  states don't reorder ordinals.
- Unknown extra JSON fields are ignored on deserialize (default System.Text.Json) —
  asserted by `TolerantReader_UnknownExtraField_IsIgnoredOnDeserialize`.

## Example payload (v1)

```json
{
  "version": 1,
  "jobId": "11111111-1111-1111-1111-111111111111",
  "jobType": "compose-save-back",
  "steps": [
    { "stepName": "container", "state": "Completed" },
    { "stepName": "record", "state": "Completed" },
    { "stepName": "profile-analysis", "state": "Running" },
    { "stepName": "indexing", "state": "Queued" }
  ],
  "aggregate": "Partial",
  "observedAt": "2026-07-08T12:00:00+00:00"
}
```

## Placement justification (ADR-013 / root CLAUDE.md §10)

- **Existing**: overlaps nothing — it is a NEW projection facade over existing job types;
  the sibling `OutcomeCard.OutcomeCompletion` (task 011) already names this type as the
  authoritative full-state contract its 4-status `OutcomeStep` maps into.
- **Extension**: could not live inside `Services/Jobs/` — ADR-013 requires CRUD/AI
  consumers (Compose r2) bind via `Services/Ai/PublicContracts/`, not job-internal types.
- **Cost of doing nothing**: without it, OutcomeCards cannot distinguish "record created"
  from "indexing done", and NFR-12 ingestion parity has no contract to enforce — a bare
  record row would read as success.
- No new endpoint, no DI registration, no package, no Dataverse column, no background work.
  Pure contract + static projector + view. Publish-size impact: one small source file
  (no new dependency) — negligible; no new HIGH CVE (no package change).

## Unblocks

Compose r2 **FR-05** (durable save-back progress) and **FR-28** (job-aware OutcomeCard
consumption) can bind to this seam with zero local variant.
