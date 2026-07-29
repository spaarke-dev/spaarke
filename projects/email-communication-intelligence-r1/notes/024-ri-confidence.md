# 024 — RI-Confidence Scorer (fixes the hardcoded-0 `CommunicationAssessedSignal.Confidence`)

> **Task**: 024 (P2, FULL rigor) · **Date**: 2026-07-29 · **Depends on**: 022 (triage urgency signal) · **Reads**: 020/021's deterministic-rung agreement · **Blocks**: 060

---

## 1. The gap this fixes (FR-04)

`CommunicationAssessedSignal.Confidence` was hardcoded to `0` at emission
(`CommunicationEnrichmentService.RunAssessmentEmissionAsync`), so `CommunicationRuleGate` denied under any
positive threshold and `CommunicationRiActionService` never fired — the shipped notification spine
(Task + appnotification + SignalR ping) was structurally dark (Pillar 1 / success-criterion 5). This task
computes a REAL email-specific RI-confidence and wires it into the signal, replacing the hardcoded value.

---

## 2. The exact formula

```
confidence = clamp01( urgencyWeight × deterministicAgreement )
```

Implemented in the new pure, static, deterministic (ADR-013/NFR-03 — no AI call) helper
**`Services/Communication/RiConfidenceScorer.cs`**:

- `RiConfidenceScorer.UrgencyWeightFromPriority(string? priority)` — maps `CommunicationTriageResult.Priority`
  (the closed option-set label task 022/023's Action produces: `Urgent`/`High`/`Medium`/`Low`).
- `RiConfidenceScorer.UrgencyWeightFromClassification(string? urgency)` — maps
  `CommunicationClassificationResult.Urgency` (the rung-5 classify prompt's open-text fallback, e.g.
  `urgent`/`elevated`/`routine`) onto the SAME four-tier scale.
- `RiConfidenceScorer.Compute(double urgencyWeight, double deterministicAgreement)` — the multiplicative
  blend, each input clamped to `[0,1]` before and after multiplying.

### Urgency → weight mapping (four-tier scale, shared by both vocabularies)

| Tier | `Priority` label (closed set) | `Urgency` label (open text) | Weight |
|---|---|---|---|
| 1 (highest) | `Urgent` | `urgent` | **1.0** |
| 2 | `High` | `elevated` | **0.75** |
| 3 | `Medium` | *(no open-text synonym seeded)* | **0.5** |
| 4 (lowest) | `Low` | `routine` | **0.25** |
| — missing/unrecognized | any other/null/blank | any other/null/blank/`unspecified` | **0.5 (neutral default)** |

**Why 0.5 as the missing-label default (not 0 or 1):** a neutral midpoint neither auto-suppresses nor
auto-boosts the notification path when urgency is genuinely unknown. This is safe by construction because
the OTHER factor (deterministic agreement) defaults to `0` whenever there is no persisted association
provenance at all — so a communication with literally nothing computed yet (no association, no
classification) still scores `0` overall, exactly matching the pre-024 hardcoded value. The 0.5 default only
has visible effect when there IS a resolved deterministic association but the urgency label is missing or
unrecognized — a moderate, proportional score, not a hard deny or an automatic fire.

**Why D-08 does not reuse the Workspace/Portfolio scorer:** `RiConfidenceScorer` lives in
`Services/Communication/`, reads only communication-shaped inputs (triage priority / classification urgency
+ `AssociationDecisionTrace.TopDeterministicConfidence`), and shares no code with the Workspace/Portfolio
priority-scoring path. Verified by grep: no reference to `RiConfidenceScorer` outside `Services/Communication/`
and no reference to the Workspace/Portfolio scorer types from this file.

### Deterministic-rung agreement source

`AssociationDecisionTrace.TopDeterministicConfidence` — the reinforced confidence of the strongest
deterministic-rung (0–3) winner, already computed and persisted by task 021's `AssociationStatusMapper` into
`sprk_communication.sprk_associationprovenance` (JSON). Read back via the SAME reconstruction pattern task
023 established (`PersistedClassificationSignalReader`), extended with a new public method:

```csharp
PersistedClassificationSignalReader.TryDeserializeProvenance(string? provenanceJson) -> AssociationProvenance?
```

This exposes the FULL `AssociationProvenance` document (not just the classification-signal slice
`TryReadFromProvenanceJson`/`TryReconstruct` already reconstructed) — `.Decision.TopDeterministicConfidence`
is the agreement factor. `TryReadFromProvenanceJson` was refactored to call this new method internally (no
duplicated JSON-parse logic — extends the existing reader per §11, rather than forking a second parser).

---

## 3. Where it's wired

`Services/Communication/CommunicationEnrichmentService.cs`:

1. **`RunEmailTriageAsync`** (Step 4.5, task 023) now returns `Task<CommunicationTriageResult?>` instead of
   `Task` (previously void/log-only) — the SAME `CommunicationTriageResult` it already produced is hoisted
   out to the caller instead of being discarded after logging.
2. **`EnrichAsync`** captures that result into a local (`triageResult`) via the `RunStepAsync` closure, then
   passes it into the assessment-emission step — no re-derivation, no second triage call, per FR-05's
   "no second full LLM pass" discipline extended to this task.
3. **`RunAssessmentEmissionAsync`** (Step 5, ~L338) now calls the new private
   `ComputeRiConfidenceAsync(communicationId, triageResult, ct)` BEFORE constructing the signal, and passes
   the computed value as the `Confidence` argument — replacing the previous implicit `0` default:

   ```csharp
   var confidence = await ComputeRiConfidenceAsync(communicationId, triageResult, ct).ConfigureAwait(false);
   var signal = new CommunicationAssessedSignal(
       communicationId, direction, message.Subject, message.From, message.To.Count, confidence);
   ```

4. **`ComputeRiConfidenceAsync`** (new private method):
   - Re-reads `sprk_communication.sprk_associationprovenance` (its own read — a second Dataverse read beyond
     the one `RunEmailTriageAsync` already did; deliberate, keeps this step self-sufficient and matches the
     non-invasive "read back what's persisted" pattern task 023 established, rather than threading additional
     state between steps).
   - Deserializes it via `PersistedClassificationSignalReader.TryDeserializeProvenance` →
     `Decision.TopDeterministicConfidence` (defaults to `0.0` when no provenance yet).
   - Urgency: prefers `triageResult?.Priority` (hoisted, no re-derivation); when null (triage produced no
     result — Action not routed/disabled, or no persisted classification signal yet, e.g. outbound today),
     falls back to `PersistedClassificationSignalReader.TryReconstruct(provenance)?.Urgency` from the SAME
     provenance document.
   - Calls `RiConfidenceScorer.Compute(urgencyWeight, deterministicAgreement)`.
   - **NFR-04**: the entire method is wrapped in try/catch; ANY failure (read, parse) degrades to `0.0` (the
     same conservative value the hardcoded default used) and logs a warning — never throws. `RunStepAsync`'s
     outer guard on the `assessment-event` step is defense-in-depth.

**`CommunicationRuleGate` and `CommunicationRiActionService` were NOT modified** — they already consume
`CommunicationAssessedSignal.Confidence` / `CommunicationRuleDecisionRequest.Confidence` exactly as before;
this task only feeds them a real number instead of the hardcoded `0`. `CommunicationRuleGateSeamTests.cs`
(pre-existing, unmodified, still green) independently proves the gate authorizes when confidence ≥
threshold — that mechanism is unchanged.

---

## 4. Proof the notification path lights up (and stays dark for noise)

`tests/integration/seam/Communication/CommsAssessedProducerSeamTests.cs` (extended) drives the REAL
`EnrichAsync` orchestration end-to-end:

- **`EnrichAsync_HighUrgencyWellAssociatedEmail_EmitsConfidenceThatClearsDefaultGateThreshold`** — a
  provenance with `TopDeterministicConfidence = 0.95` (rung 0) + a triage facade returning `Priority =
  "Urgent"` → emitted `signal.Confidence == 0.95` (`1.0 × 0.95`), which is `≥ CommsPolicyOptions.DefaultConfidenceThreshold`
  (0.8) — this WOULD authorize through the real, unmodified `CommunicationRuleGate`.
- **`EnrichAsync_LowUrgencyWeaklyAssociatedEmail_EmitsConfidenceBelowDefaultGateThreshold`** — a provenance
  with `TopDeterministicConfidence = 0.2`, no persisted classification signal (urgency falls back to the
  neutral 0.5 default) → emitted confidence `0.1`, below the 0.8 default threshold — noise stays denied.
- **`EnrichAsync_OnSuccess_InvokesAssessedProducerWithExpectedSignal`** (pre-existing, extended) — no
  persisted signal at all → confidence `0`, preserving the pre-024 conservative outcome for a genuinely
  unassessed communication.
- **`EnrichAsync_WhenRiConfidenceReadThrows_DegradesToZeroConfidence_WithoutPropagating`** — the Dataverse
  read throws → `EnrichAsync` still completes, the producer still receives a signal, and its `Confidence` is
  `0` (NFR-04 proof, specific to the new scoring logic — distinct from `EmailTriageSeamTests`' existing
  NFR-04 proof for the triage step itself).

`tests/unit/Sprk.Bff.Api.Tests/Services/Communication/RiConfidenceScorerTests.cs` (new, pure domain-logic
unit tests, no mocks/DI/IO): the full urgency × agreement matrix, the four-tier priority/urgency label
mappings (both vocabularies land on the same scale), the missing/unrecognized-label neutral default, and
clamp/boundary behavior (negative inputs floor to 0, inputs `> 1` ceiling to 1).

`tests/unit/Sprk.Bff.Api.Tests/Services/Communication/PersistedClassificationSignalReaderTests.cs`
(pre-existing, unmodified, still green) continues to cover the classification-reconstruction round-trip;
the new `TryDeserializeProvenance` method is exercised transitively through `TryReadFromProvenanceJson`
(refactored to call it) and directly through the new `ComputeRiConfidenceAsync` seam tests above.

---

## 5. Weights are a starting point (C-5 — tune with an eval set)

The four-tier weights (1.0 / 0.75 / 0.5 / 0.25) are the spec's suggested starting scale, chosen so that:
- An `Urgent` + strong deterministic match (rung 0/1, typically ≥ 0.85–0.95 reinforced confidence) clears the
  shipped default gate threshold (`CommsPolicyOptions.DefaultConfidenceThreshold = 0.8`).
- A `Low`/unclassified email with weak agreement (≤ 0.3) stays well below it.

No live eval-set tuning run was performed in this task (no production RI-confidence outcome data exists yet
— the path was dark until this task). Recommended follow-up once the notification path has run in a live
environment: sample a batch of fired vs. suppressed assessments, compare against human "should this have
notified me" judgments, and adjust the four weights (or the `CommsPolicyOptions.DefaultConfidenceThreshold` /
per-rule `sprk_confidencethreshold`) accordingly — the gate's threshold and the scorer's weights are
independently tunable dials.

---

## 6. Hand-off to task 025 (persist triage output, incl. `sprk_riconfidence`)

**Do not re-derive the score.** Task 025 persists the SAME formula's output to `sprk_communication.sprk_riconfidence`.
Two ways to get it, in order of preference:

1. **Call the same scorer** — `RiConfidenceScorer.Compute(urgencyWeight, deterministicAgreement)` with the
   SAME inputs task 024 uses (triage `Priority` preferred, classification `Urgency` fallback, both mapped via
   `RiConfidenceScorer.UrgencyWeightFromPriority`/`UrgencyWeightFromClassification`; deterministic agreement =
   `AssociationDecisionTrace.TopDeterministicConfidence` via
   `PersistedClassificationSignalReader.TryDeserializeProvenance`). This is the "compute once, conceptually"
   contract the brief specified — 025 calls the exact same static formula, not a re-implementation.
2. Alternatively, if 025's persistence step runs in the SAME `EnrichAsync` call as the triage write (per its
   own POML, immediately after `_triageAi.TriageAsync` inside `RunEmailTriageAsync`, or as a later step), it
   could thread the already-computed confidence value forward the same way 024 threads `triageResult` — but
   024's emission runs in a LATER step (`assessment-event`, after `email-triage`), so if 025's write happens
   inside the `email-triage` step itself, the confidence value from `ComputeRiConfidenceAsync` will not yet
   exist at that point in the pipeline. **Recommended**: 025 simply calls `RiConfidenceScorer.Compute(...)`
   independently with the same inputs (option 1) — it's a cheap, pure, deterministic call, so recomputing it
   in a different step is inexpensive and avoids a fragile inter-step data dependency.

**Scorer location + signature (for 025 to import):**

```csharp
namespace Sprk.Bff.Api.Services.Communication;

public static class RiConfidenceScorer
{
    public static double UrgencyWeightFromPriority(string? priority);       // CommunicationTriageResult.Priority
    public static double UrgencyWeightFromClassification(string? urgency); // CommunicationClassificationResult.Urgency fallback
    public static double Compute(double urgencyWeight, double deterministicAgreement); // final [0,1] score
}
```

**Explicitly NOT done by this task (025's scope):** `sprk_riconfidence` (or any other triage field) is NOT
persisted to `sprk_communication` by task 024. `CommunicationEnrichmentService.RunEmailTriageAsync` still
carries the pre-existing comment `// Task 025 persists 'result' to sprk_communication's triage fields here.`
— unchanged by this task.

---

## 7. §11 Component Justification

1. **Existing** — No email-specific RI-confidence scorer existed; `CommunicationAssessedSignal.Confidence`
   was a hardcoded placeholder (`0`). Grep-confirmed no other type in `Services/Communication/` computes this
   blend.
2. **Extension** — `RiConfidenceScorer` is a small new pure static helper (no DI, no interface, no new
   service registration) living beside the code it feeds (`Services/Communication/`); it extends
   `PersistedClassificationSignalReader` (task 023) with one new public method rather than forking a second
   JSON parser. The Workspace/Portfolio scorer is explicitly NOT reused (D-08 — different intent).
3. **Cost-of-doing-nothing** — without it, `CommunicationRuleGate` denies every assessed communication under
   any positive threshold and `CommunicationRiActionService` never fires — the shipped notification path
   (Task + appnotification + SignalR ping) stays permanently dark (spec success-criterion 5 fails).

---

## 8. Build / test / publish-size / CVE results

- `dotnet build src/server/api/Sprk.Bff.Api/` → **0 errors** (23 pre-existing warnings, none introduced by
  this task).
- `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~Communication"` → **749 passed, 5
  failed (all 5 the documented pre-existing sender-identity/DTO failures, `notes/wave2-review-findings.md`
  §"Pre-existing branch test debt" — verified NOT introduced by this task), 8 skipped**. All new tests (43
  targeted: `RiConfidenceScorerTests`, extended `CommsAssessedProducerSeamTests`, plus the unmodified sibling
  suites `EmailTriageSeamTests`, `CommunicationRuleGateSeamTests`, `PersistedClassificationSignalReaderTests`)
  pass.
- Publish-size: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/` → no new package reference added; a
  local tar.gz compression of the publish output measured ≈47 MB (baseline ~49.63 MB incl. PDBs per root
  CLAUDE.md §10) — **delta ≈0**, well under the ceiling.
- CVE: `dotnet list package --vulnerable --include-transitive` → one HIGH finding
  (`System.Security.Cryptography.Xml` 8.0.3, transitive) — this is the SAME pre-existing finding documented in
  `notes/wave2-review-findings.md` #2 ("csproj untouched — NOT introduced by r1"); **no new CVE introduced**
  by this task (no csproj change).

---

## 9. Placement Justification (root CLAUDE.md §10)

Extends the existing enrichment-emission step (`CommunicationEnrichmentService.RunAssessmentEmissionAsync`)
and the existing task-023 reader (`PersistedClassificationSignalReader`) — no new endpoint, no new DI
registration, no new package. The one new file (`RiConfidenceScorer.cs`) is a pure static helper, not a
service, so it needs no DI registration at all. §10 bullet 6 (test update obligation): tests updated in
`tests/integration/seam/Communication/CommsAssessedProducerSeamTests.cs` and added in
`tests/unit/Sprk.Bff.Api.Tests/Services/Communication/RiConfidenceScorerTests.cs`.
