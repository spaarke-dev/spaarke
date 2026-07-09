# Discovery Obligations Confirm — Task 004

> **Purpose**: Confirms, with in-repo file:line evidence, the five Phase-0 discovery obligations flagged in `design.md` §14 ("verify in repo before citing with authority" — spec FR-P0-04) before downstream tasks (020 hoist, 031/033 eval families, gate engine, 014 job-aware completion state) cite them with authority.
> **Method**: Direct read + grep of the current tree (`work/spaarke-ai-architecture-redesign-r2`, 2026-07-08). All five anchors from `plan.md` §3 and this task's `<knowledge><patterns>` were re-verified against the live files, not re-derived from memory.
> **Result**: 4 of 5 confirmed exactly as assumed. 1 correction recorded (§5, Job status surface — component location).

---

## 1. Golden-utterance suite — file location + format

**CONFIRMED**

- **Suite file**: `tests/integration/contract/Eval/golden-utterances.json` — exists, `schemaVersion: "1.0"`, top-level `cases[]` array. Each case: `caseId`, `family`, `ucId`, `channel` (`text|click|event` — the three invocation routes), `utterance`, `context`, `expected` (`outcomeClass: dispatch|clarify|refuse`, `consumerType`, `consumerCode`, `catalogStatus: existing|planned`), `assertions` (`schemaConformance`, `citationIntegrity`), `activation` (`dispatchAssertPhase`, `activatedBy`). Case-schema doc: `tests/integration/contract/Eval/README.md`.
- **CI merge gate**: `.github/workflows/sdap-ci.yml:225` — job `eval-gate` ("Eval Gate (Golden Utterances)"), runs `dotnet test tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj -c Debug --filter "Category=GoldenUtteranceEval"` (NFR-02, merge-blocking; comment at line ~220 notes it's not yet a required branch-protection status check).
- **Test runner files**: `tests/integration/contract/Eval/GoldenUtteranceEvalSuiteTests.cs`, `tests/integration/contract/Eval/P2LoopInjectionEvalSuiteTests.cs` (both present alongside the JSON).
- **Consumers**: FR-A1-02/04 eval families (tasks 031/033) extend the `cases[]` array; no code change required to add a case.

## 2. `OpenAiFunctionSchemaValidator` extension points (row-15 triple-twin hoist, FR-A-01)

**CONFIRMED**

- **File**: `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/OpenAiFunctionSchemaValidator.cs:70` — `public static class OpenAiFunctionSchemaValidator` (class declaration exactly at line 70, matching plan.md's anchor).
- **Shape**: Stateless static class — pragmatic structural validator for the OpenAI function-parameters JSON-Schema subset (originated from the G-P3 UAT H1 incident: a malformed catalog row's `"required": true` inside a property definition 400-failed every text-path turn). Entry point `FindFirstError(string? rawSchemaJson)` at line 87 returns `null` (safe to project) or the first validation error string (keyword path + reason — NFR-07-safe, no user content). Internal recursive worker `ValidateSchemaObject` at line 129.
- **Extension seam**: per the class's own Component-Justification remarks (CLAUDE.md §11), this is the **single-source** schema-parity check reused by the Binding projection (`BindingCapabilityTool`/`RefusalCapabilityTool`) and by a health check — this is the seam the triple-twin hoist (task 020) enforces catalog-row parity through. **ADR-039 note**: confirmed this is the ONLY schema-validation mechanism on the projection path (the adapter's `ToolHandlerToAIFunctionAdapter.ValidateAgainstMetaSchema` is a different, throw-based, generic-JSON-Schema check) — no second intent/validation mechanism is introduced by treating this as the hoist's parity seam.
- **Also touched by plan.md (not part of this task's 5, noted for completeness)**: `PublicContracts/RoutingConsumerTypeHealthCheck.cs:66` — health-check reuse of the same validator.

## 3. Gate-ledger property surface (confirmation-state persistence)

**CONFIRMED**

- **File**: `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/SessionLedgerEntries.cs` — `SessionGate` record at **line 242** (`public sealed record SessionGate`), doc comment at line 235 ("A pending confirmation or in-flight elicitation marker (ADR-040 Gate entry)").
- **Shape** (properties, with line numbers): `GateId` (245, stable id correlating pending+resolution entries), `Kind` (248, `confirmation | elicitation`), **`Status`** (251, `pending | confirmed | rejected | expired | superseded` — this is the confirmation-state property), `Turn` (254), `BindingId` (257, nullable), `SideEffectClass` (260, nullable, `read|write|communicate|pure`), `MissingFields` (267, nullable, elicitation-only, field names only per NFR-07).
- **Structural-impossibility mechanism**: the ledger is append-only (per the type's remarks) — a resolved gate is written as a NEW entry sharing the same `GateId` with an updated `Status`, never an in-place mutation. This is what makes "a second ask" structurally impossible: a consumer checks the latest entry for a `GateId` rather than tracking mutable state.
- **Generalizes**: `PendingPlanManager` store shape (D12 — unification lands in Phase 2 per the doc comment).

## 4. `dispatchUncertain` seam — behavioral, not a literal symbol

**CONFIRMED — matches the plan's explicit "no literal symbol" finding**

- **File**: `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ElicitationTurnRouter.cs:34` — `public static class ElicitationTurnRouter` (class declaration exactly at line 34).
- **Shape**: No method, field, or type named `dispatchUncertain` exists anywhere in the class or file (confirmed by inspection — the class exposes `IsHardSlash(string?)`, `IsExplicitRestart(string?)`, `RestartPhrases` (closed vocabulary: `restart, start over, cancel, never mind, nevermind, forget it, stop`), and `BuildClarifyInstruction(...)`). The "uncertain dispatch" behavior described by design/plan is this router's determination of whether an incoming utterance is an ANSWER to a pending elicitation gate vs. an escape (hard-slash or explicit-restart) — i.e., the router IS the `dispatchUncertain` seam, expressed as deterministic escape-detection rather than a named dispatch function.
- **ADR-039 alignment**: per the class doc comment, this is deliberately classification-free — "the platform's ONLY answer-vs-command disambiguation," and explicitly NOT a second intent-detection mechanism (ADR-039 MUST NOT).
- **No correction needed** — this obligation's assumption (design/plan) already correctly anticipated the behavioral-not-literal shape; this entry exists to make that finding CONFIRMED with a line number rather than asserted from memory.

## 5. Job status surface (`ServiceBusJobProcessor` / Job Contract)

**CONFIRMED with ONE LOCATION CORRECTION**

- **`JobContract.cs:9`** — CONFIRMED. `src/server/api/Sprk.Bff.Api/Services/Jobs/JobContract.cs:9`, `public class JobContract` (class declaration exactly at line 9, matching plan.md's anchor). Shape: job envelope — `JobId`, `JobType`, `SubjectId`, `CorrelationId`, `IdempotencyKey`, `Attempt`/`MaxAttempts` (+ `IsAtMaxAttempts`), `Payload` (`JsonDocument?`), `CreatedAt`, `CreateRetry()`. **Note**: `JobContract` itself carries no `Status` field — status lives on the outcome type (next bullet).
- **Status enum** — CONFIRMED, one level removed from `JobContract`: `src/server/api/Sprk.Bff.Api/Services/Jobs/JobOutcome.cs:67`, `public enum JobStatus { Completed, Failed, Poisoned, ... }` (factory methods `JobOutcome.Success()`/`Failure()`/`Poison()` at lines 25/40/56 set `Status` accordingly). `ServiceBusJobProcessor.cs` deserializes the inbound `JobContract` (line 120) and calls `handler.ProcessAsync(job, ...)` (line 187), which returns a `JobOutcome` carrying this `JobStatus`.
- **`BatchJobStatusStore.cs`** — CONFIRMED at the expected location: `src/server/api/Sprk.Bff.Api/Services/Jobs/BatchJobStatusStore.cs`. Distributed-cache-backed per-job status record store (`CreateJobStatusAsync`, `GetJobStatusAsync`, `BatchJobStatusRecord` → `BatchJobStatusResponse` mapping, `GetStatusMessage`).
- **⚠️ CORRECTION — `JobStatusService.cs` location**: plan.md §3 groups `JobStatusService.cs` alongside `Services\Jobs\ServiceBusJobProcessor.cs` + `JobContract.cs:9` + `BatchJobStatusStore.cs`, implying a `Services/Jobs/` location. **In the current tree it lives at `src/server/api/Sprk.Bff.Api/Services/Office/JobStatusService.cs`** (`namespace Sprk.Bff.Api.Services.Office;`, class `JobStatusService : IJobStatusService, IDisposable`, key members `PublishStatusUpdateAsync`, `SubscribeToJobAsync` (async-enumerable SSE-style subscription), `UpdateJobStatusAsync`). It is a real, working file — just filed under `Services/Office/` (Office Add-ins domain), not `Services/Jobs/`. **Downstream impact**: task 014 (`JobAwareCompletionState`, FR-A0-07) should reference `Services/Office/JobStatusService.cs`, not `Services/Jobs/JobStatusService.cs`, when it wires job-aware completion state. This is a citation-location fix only — no behavioral gap; the escalation trigger (missing/materially-different seam) does NOT fire.

---

## Downstream consumers (cross-reference)

| Obligation | Confirmed anchor | Consuming task(s) |
|---|---|---|
| 1. Golden-utterance suite | `tests/integration/contract/Eval/golden-utterances.json` + `sdap-ci.yml:225` | 031, 033 (eval families) |
| 2. Validator extension seam | `Services/Ai/Chat/OpenAiFunctionSchemaValidator.cs:70` | 020 (triple-twin hoist — BLOCKS all catalog-row tasks) |
| 3. Gate-ledger surface | `Models/Ai/Chat/SessionLedgerEntries.cs` (`SessionGate`, line 242) | Gate engine (task 032), 035/036 (Completion) |
| 4. `dispatchUncertain` behavior | `Services/Ai/Chat/ElicitationTurnRouter.cs:34` | Origin classifier / Policy v2 (D-F1), gate engine (032) |
| 5. Job status surface | `Services/Jobs/JobContract.cs:9` + `Services/Jobs/JobOutcome.cs:67` + `Services/Jobs/ServiceBusJobProcessor.cs` + `Services/Jobs/BatchJobStatusStore.cs` + **`Services/Office/JobStatusService.cs`** (location correction) | 014 (`JobAwareCompletionState`, FR-A0-07) |

---

## Summary

- **4 of 5 obligations CONFIRMED exactly as the plan assumed**: golden-utterance suite, validator extension point, Gate-ledger surface, `dispatchUncertain` behavioral finding.
- **1 correction**: `JobStatusService.cs` is at `Services/Office/JobStatusService.cs`, not `Services/Jobs/`. Non-blocking — file exists and is fully functional; task 014 should cite the corrected path.
- **Escalation trigger did NOT fire** — no seam is missing or materially different in shape from the plan's assumption. Task 020 (triple-twin hoist) and the eval-family tasks (031/033) may proceed on these confirmed anchors.

---

*Task 004 — `spaarke-ai-architecture-redesign-r2`. Verified 2026-07-08 against `work/spaarke-ai-architecture-redesign-r2`.*
