# 023 — TRIAGE-EMAIL Binding + Enrichment Trigger + RAG Grounding + Eval Case

> **Task**: 023 (P2, FULL rigor) · **Depends on**: 022 (Action authoring) · **Blocks**: 060

---

## 1. What was wired

### Binding row (`infra/dataverse/sprk_playbookconsumer-rows.json`)

Added the `email-triage` Binding row (mirrors `document-profile`/`email-analysis`/`matter-pre-fill` — a
**Linear AI Consumer**, not a chat/loop capability): `consumerType: "email-triage"`, `actionCode:
"triage-email"`, no `toolDescription`/`surfaces`/`disposition`/`risk`/`captureMode`/`chipTransitions`/
`onEventBindings` (all null — this Binding is invoked directly off the enrichment path, never
agent-selectable or Click-dispatched).

### Mirror-first input schema (`infra/dataverse/inputschemas/`) — **NOT authored, and that is the decision**

022's note flagged this as needing confirmation. Confirmed: **not needed.** The `inputschemas/` mirror is
the *OpenAI function-parameters-subset tool-calling contract* — the Binding-level shape for a
chat/loop-projectable capability (every existing file there corresponds to a mirror row carrying a
non-null `toolDescription`/`surfaces`). Grep-verified: `document-profile`, `email-analysis`,
`matter-pre-fill`, `project-pre-fill`, `summarize-file`, `ai-summary`, `insights-ask`, `insights-search` —
every other Linear Consumer with the SAME null-toolDescription/null-surfaces shape — has **no** entry
under `inputschemas/` either. `email-triage` is invoked directly from `.cs` (never a chat tool call), so
authoring an unused tool-calling schema would be pure scope creep (fails the §11 cost-of-doing-nothing
test). The Action's own JPS `input{}` section (Home A — the prompt-intrinsic input contract) already
exists from task 022; that is a *different* artifact from the `inputschemas/` mirror and was correctly
left alone.

### Enrichment/event trigger (`Services/Communication/CommunicationEnrichmentService.cs`)

New **Step 4.5 "email-triage"**, inserted between `rag-indexing` and `assessment-event` in the fixed FR-08
step order, wrapped in the existing `RunStepAsync` non-fatal guard (same as every other step):

1. Reads `sprk_communication.sprk_associationprovenance` + the regarding-matter lookup
   (`RegardingFieldMap.FieldFor("sprk_matter")`) via the already-injected `IGenericEntityService`
   (read-only — no write, no touch to `AssociationStatusMapper.cs`/`AutoFileGate.cs`).
2. Reconstructs the `CommunicationClassificationResult` `AiClassificationRung` ALREADY PRODUCED from the
   persisted provenance JSON via the new **`PersistedClassificationSignalReader`**
   (`Services/Communication/PersistedClassificationSignalReader.cs`) — a pure parser, no second
   classification call. See §2 below for why a reconstruction (not a live handoff) was the correct design.
3. If no persisted signal exists (outbound today; inbound where rung 5 didn't fire or found nothing useful
   — rung 5 is itself gated to run only when the deterministic rungs did NOT auto-file, per
   `IncomingAssociationResolver`'s cost-control comment), the step no-ops at Debug — this is the expected,
   correct behavior, not a bug.
4. Calls the new **`ICommunicationTriageAi`** facade (`Services/Ai/PublicContracts/`) with the
   reconstructed classification + the normalized subject/body + the resolved matter id (for FR-06
   grounding) + tenant id.
5. Logs the result's closed-set label fields only (category/priority/reviewOutcome/obligation count) —
   summary + obligations text are free-text and are NOT logged (ADR-015).
6. **Does NOT persist** any `sprk_communication` triage field — that is task 025.

---

## 2. Why a reconstruction, not a live handoff

`AiClassificationRung` (rung 5) runs inside the Association Engine at the capture boundary, via
`IncomingAssociationResolver` — a **different call site and scope** than
`CommunicationEnrichmentService.EnrichAsync`, which fires later in the pipeline (FR-08 step 9 of
`IncomingCommunicationProcessor`, after association has already resolved and persisted). There is no
live in-memory handle to the rung's `CommunicationClassificationResult` at the point enrichment runs.

`AssociationStatusMapper` (frozen for this task — task 021 owns it concurrently) already threads the
rung's `RungMatch` into `provenance.signals`, and `IncomingAssociationResolver` (also not touched by this
task) persists that provenance to `sprk_communication.sprk_associationprovenance` BEFORE enrichment runs.
Reading that back is the non-invasive way to reuse the signal without editing either frozen file or
introducing a second live classification path. `PersistedClassificationSignalReaderTests.cs` drives the
REAL `AiClassificationRung.EvaluateAsync` (mocked classifier boundary) to get the actual persisted-string
format and proves the round-trip recovers every field (category/urgency/candidateRecordTypes/obligations/
suggestedActions/privilegeFlagged/rationale) — a regression pin tied to production output, not a
hand-typed mirror of the format string.

**Consequence for 024/025** (see §5 below): triage currently only fires for communications where rung 5
actually ran and found something. Today that is INBOUND only (outbound has no classification pass at all
per the existing `RunAssociationAsync` no-op comment), and even for inbound only when the deterministic
rungs did not already auto-file. This is architecturally correct (triage is meant to review content, and
the existing cost-control gate on rung 5 is out of this task's scope to change) but worth knowing: triage
coverage is currently a strict subset of "all inbound email."

---

## 3. The facade (`Services/Ai/PublicContracts/ICommunicationTriageAi.cs` / `CommunicationTriageAi.cs` / `NullCommunicationTriageAi.cs`)

**Reached via `Services/Ai/PublicContracts/` (ADR-013/NFR-03)** — `CommunicationEnrichmentService` injects
ONLY `ICommunicationTriageAi`, never `IActionResolver`/`IActionRunner`/`IRagService`/`IOpenAiClient`
directly. Internally, `CommunicationTriageAi` resolves + runs the TRIAGE-EMAIL Action via the **Linear AI
Consumer primitives** (`IActionResolver`/`IActionRunner`, `Services/Ai/LinearConsumers/`) — the SAME
mechanism `MatterPreFillService`/`ProjectPreFillService` already use for their own catalog Actions
(precedent-following, not a new invocation mechanism). `ConsumerTypes.EmailTriage = "email-triage"` was
added alongside the existing constants.

**No second full LLM pass (FR-05) — structural, not just documented**: `CommunicationTriageAi`'s
constructor has NO dependency on `ICommunicationClassificationAi`/`IOpenAiClient` — it cannot invoke a
classification even by mistake. `CommunicationTriageRequest.Classification` is a C# `required` member —
every caller MUST supply the already-produced signal at compile time.

**Best-effort (NFR-04)**: every failure mode (Action not routed, grounding failure, completion failure) is
caught inside the facade and returns `null`; `CommunicationEnrichmentService`'s own try/read is wrapped by
the outer `RunStepAsync` guard as defense-in-depth. `EmailTriageSeamTests.cs` proves both layers (a
throwing facade AND a throwing Dataverse read) never propagate into `EnrichAsync`.

**Registration** (`Infrastructure/DI/AnalysisServicesModule.cs`): mirrors `ICommunicationClassificationAi`
exactly — `NullCommunicationTriageAi` (P2 graceful-degradation, returns null) registered in
`AddNullObjectsForCompoundOff`; `CommunicationTriageAi` registered in `AddPublicContractsFacade`. The
Linear AI Consumer primitives (`IActionResolver`/`IActionRunner`, via `AddLinearConsumers()`) and
`IRagService` (real `RagService` or `NullRagService`) are BOTH always registered regardless of the
compound gate, so `CommunicationTriageAi`'s constructor deps always resolve.

---

## 4. FR-06 RAG grounding — wired, with an honest scoping gap

**Wired**: when the communication has an already-resolved regarding matter, `CommunicationTriageAi`
queries `IRagService.SearchAsync` scoped by `ParentEntityType="sprk_matter"` /
`ParentEntityId={matterId}` (the SAME entity-scoping filter `RagSearchOptions` already exposes for
matter-scoped search) and injects the top-5 results as a labeled grounding block via
`BoundInputs.RecordMemoryFragment` — the SAME extension point `ActionRunner.ComposeGroundingContext`
already prepends before the instruction/operand for every Action run (no new plumbing in `ActionRunner`
itself). Failure (including the compound-AI-OFF `NullRagService` throwing `FeatureDisabledException`) is
caught and degrades to a context-free run — grounding is additive, never load-bearing.

**Honest scoping gap (not fixed by this task — flagged for 024/025's awareness)**: BOTH the inbound
(`IncomingCommunicationProcessor.cs`) and outbound (`CommunicationEnrichmentService.RunRagIndexingAsync`)
RAG-indexing call sites currently pass `ParentEntity: null` when enqueuing a communication's `.eml` for
indexing — so **no communication is currently tagged with its regarding matter in the RAG index**, meaning
the FR-06 grounding query will return zero results in practice until that tagging gap is closed. This is a
pre-existing gap in shipped code (`email-communication-solution-r4`), not introduced by this task, and
fixing it touches `IncomingCommunicationProcessor.cs` (a file outside this task's declared scope) plus
possibly a backfill decision for already-indexed prior correspondence — out of scope for 023. The
mechanism is correctly wired end-to-end (proven by `TriageEmailEvalTests.Grounding_...` and the
`CommunicationTriageGrounding` unit-level fact) and will start working the moment ParentEntity tagging is
added to the indexing call sites — flagging this explicitly rather than silently shipping an inert filter.

**Eval demonstration (NFR-07)**: `CommunicationTriageGrounding.BuildFragment` is a small, pure, testable
seam extracted specifically so the "grounded vs context-free" prompt-composition difference is a
mechanical fact (no live model needed) — matching this eval suite's established mechanical-vs-live
convention (see `tests/integration/contract/Eval/README.md`).

---

## 5. Golden-utterance eval case (NFR-07 — blocking merge gate)

New net-new family (same pattern as every sibling family in `tests/integration/contract/Eval/` — joins the
SAME `Category=GoldenUtteranceEval` merge gate via the trait, zero CI-YAML change):

- **Seed**: `tests/integration/contract/Eval/triage-email-eval-cases.json` (`triage-email-eval@v1`, 4 cases:
  `structured-output`, `context-improvement`, `binding-resolution`, `no-second-pass`)
- **Harness**: `tests/integration/contract/Eval/TriageEmailEvalTests.cs`
- Demonstrates: the 5-field structured output shape (cross-checked against the Action's own worked
  example, catching drift in either file); the FR-06 grounded-vs-context-free difference; the Binding
  resolving through the REAL `ConsumerRoutingService`; the FR-05 no-second-pass structural guarantee.

Supporting (non-gate) tests:

- `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/PersistedClassificationSignalReaderTests.cs` — the
  provenance-reconstruction round-trip (driven by the REAL rung output).
- `tests/integration/seam/Communication/EmailTriageSeamTests.cs` — the NFR-04 non-fatal proof (facade
  throws, Dataverse read throws, no signal present) + the "feeds the reconstructed signal, never a second
  classification" positive fact.

---

## 6. Hand-off to task 024 (RI-confidence scorer)

024 edits the SAME `CommunicationEnrichmentService.EnrichAsync` method (per project CLAUDE.md — the
P2-triage tasks serialize, never concurrent). What 023 makes available:

- The new **Step 4.5 "email-triage"** block (private method `RunEmailTriageAsync`) sits between
  `rag-indexing` and `assessment-event`. If 024's RI-confidence scoring wants the triage `priority`/
  `reviewOutcome` as an input signal, either (a) read them back the SAME way 023 reads the classification
  (once 025 persists them to `sprk_communication`), or (b) hoist the `CommunicationTriageResult` out of
  `RunEmailTriageAsync` into a field/local 024's own step can read within the SAME `EnrichAsync` call
  (024's step would need to run AFTER email-triage in the sequence — currently email-triage is step 4.5,
  assessment-event is step 5; inserting an RI-confidence step between them, or folding it into
  assessment-event, are both viable without reordering the FR-08-fixed earlier steps).
- `CommunicationTriageResult` (public record, `Services/Ai/PublicContracts/ICommunicationTriageAi.cs`)
  carries `Category`/`Summary`/`Obligations`/`Priority`/`ReviewOutcome` — `Priority` in particular is the
  likely RI-confidence input.

## 7. Hand-off to task 025 (persist triage output)

- `RunEmailTriageAsync`'s final line is a comment: `// Task 025 persists 'result' to sprk_communication's
  triage fields here.` — the `CommunicationTriageResult result` local is in scope at that point; 025 adds
  the `IGenericEntityService.UpdateAsync("sprk_communication", communicationId, fields, ct)` call (or an
  `IActionSeam.UpdateRecordAsync` call, per the project's PublicContracts discipline) immediately there.
- Field-mapping table (unchanged from 022's note §2, copy-ready):

  | Result field | Target column |
  |---|---|
  | `Category` | `sprk_communication.sprk_triagecategory` (lookup — resolve label → `sprk_triagecategory` record id) |
  | `Summary` | `sprk_communication.sprk_triagesummary` |
  | `Obligations` | `sprk_communication.sprk_triageobligation` (singular — serialize the array to lean JSON per D-06) |
  | `Priority` | `sprk_communication.sprk_triagepriority` (option-set — resolve label → integer) |
  | `ReviewOutcome` | `sprk_communication.sprk_reviewoutcome` (option-set — resolve label → integer) |

- The 5 fields returned by `ICommunicationTriageAi.TriageAsync` are the Action's RAW `$choices` label
  strings (e.g. `"Urgent"`, `"Route"`), not resolved Dataverse values — 025 does the label→record-id /
  label→option-set-integer resolution at persistence time (same responsibility split as every other
  `$choices`-declared Action in this codebase).
- **Known scoping gap to be aware of** (§4 above): FR-06 grounding will return empty results until the
  RAG-indexing `ParentEntity` tagging gap is closed (outside 023's scope) — this does not block 025 (triage
  still produces the 5-field output; it is simply context-free until that gap closes), but 025/060 should
  be aware the FR-06 "improves with matter context" acceptance criterion is currently proven at the
  mechanism level (eval case), not yet observable end-to-end on a live environment.
