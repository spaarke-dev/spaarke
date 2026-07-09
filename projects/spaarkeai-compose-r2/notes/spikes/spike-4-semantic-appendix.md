# Spike 4 — Semantic Appendix hallucination delta

> **Task**: 004 · **Phase**: 0 Spikes · **Date**: 2026-07-08 · **Model**: sonnet @ high
> **Method**: static code grounding (file:line evidence) + `SemanticAppendixGenerator` design over
> existing primitives + a ready-to-run A/B evaluation protocol.
> **Deliverable**: this note. No production code, no throwaway `src/` generator committed
> (see §7 — why the POML's "build generator + run live A/B" steps were adapted under directional
> step-mode: the core measurement REQUIRES live Azure OpenAI calls that cannot run headlessly).

---

## 1. Decision (the one thing this spike unlocks)

**DESIGN-CONFIRMED; MEASUREMENT DEFERRED.** The `SemanticAppendixGenerator` for the
`compose-document` scope can be built **entirely from existing BFF primitives** (§3) — no new
text-extraction, no new prompt-rendering, no new dispatch surface. The appendix content format
is fully specified (§4), faithful to adeu's `domain.ts` `extract_all_domain_metadata` pattern
(research §"Pattern 4"). ADR-013/ADR-039 are honored: the generator emits a **text payload only**;
it never calls a model and adds no endpoint (§3.4).

**What CANNOT be closed headlessly:** the core acceptance criterion — a *measured* WITH-vs-WITHOUT
hallucination delta — requires live Azure OpenAI dispatches against a real Binding. That is a
runtime observation, not a static one; fabricating numbers here would be the exact anti-pattern the
task brief forbids. Instead this note delivers a **rigorous, ready-to-run evaluation protocol**
(§5) with a fixed doc/question set design, scoring rubric, and pass/fail bands, to be executed on
`spaarkedev1` when Phase 1/4 lands (the appendix rides the compose-document scope, which is
core-A0-gated for the Binding rows).

**One material assumption correction** (§6.1): the adeu research doc (`adeu-architecture-study.md`
line 262) recommends **deferring the defined-terms appendix to R3** ("Don't ship in R2 — too
speculative"). The 2026-07-08 **scope-lock supersedes that** — `design.md:391/453` + `spec.md:98`
put both the Semantic Appendix (FR-22) AND a full `compose-defined-terms` extraction capability
**in R2**. The research recommendation is stale; the design is authoritative. This spike proceeds
on the R2 scope.

---

## 2. What "the appendix" is, and where it plugs in (grounded)

Design intent (`design.md:391`): *"Semantic Appendix in scope payload — LLM sees defined terms,
cross-references, structural metadata to reduce hallucination — `compose-document` scope payload
generator."* FR-22 (`spec.md:98`): *"`SemanticAppendixGenerator` enriches the `compose-document`
scope payload (defined terms, cross-refs, structural metadata); existing track-changes render to
the LLM inline as `{++/--/>>/<<}` CriticMarkup."*

The appendix is a **read-only reference block appended below the document body**, behind a boundary
marker, so the LLM scans defined terms / cross-refs / structure **before** editing capitalized
phrases or citing sections. Adeu ships exactly this as `<!-- READONLY_BOUNDARY_START -->` +
usage-counted defined terms + cross-reference targets (research §"Pattern 4", lines 116-132).

**The plug-in point is the `## Document` section of the rendered prompt** — no renderer change
needed (§3.3). Consumers of `compose-document` today are the two document-wide R2 Actions:
`compose-summarize-word-changes` and `compose-defined-terms` (`design.md:452-453`).

---

## 3. Reuse-first design — the generator is assembled from shipped primitives (CLAUDE.md §11)

I grepped `src/server/api/Sprk.Bff.Api/Services/` for every text-extraction / appendix /
prompt-render primitive before designing anything new. Findings:

| Need | Existing primitive (reuse) | Evidence (file:line) |
|---|---|---|
| Extract body text from a doc row or upload | `IDocumentTextSource.ExtractFromDocumentIdAsync` / `ExtractFromFileAsync` → `DocumentText` | [`IDocumentTextSource.cs:16-38`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/LinearConsumers/IDocumentTextSource.cs); returns `DocumentText { ExtractedText, FileName, GraphDriveId, GraphItemId }` ([`DocumentText.cs:12-44`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/LinearConsumers/DocumentText.cs)) |
| Underlying extraction + Redis ETag cache (ADR-009) | `ITextExtractor.ExtractAsync(stream, name, driveId, itemId, etag, …)` | [`ITextExtractor.cs:33-39`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/ITextExtractor.cs) — cache key `sdap:ai:text:{driveId}:{itemId}:v{etag}`, 24h TTL |
| Render appendix INTO the prompt with the executor both paths use | `PromptSchemaRenderer` `## Document` section | [`PromptSchemaRenderer.cs:242-248`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/PromptSchemaRenderer.cs) — appendix appended to `documentText` rides this unchanged; OR the `## Input` `runtimeInput` seam (`:224-239`, Wave 11) for a structured variant |
| Prompted executor that renders + calls model once + validates schema | `ActionRunner.RunAsync` → `PromptSchemaRenderer.Render(documentText: …)` | [`ActionRunner.cs:120-145`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/LinearConsumers/ActionRunner.cs) — `BuildPrompt` puts `DocumentText.ExtractedText` into `## Document` |
| Dispatch the A/B invocations (no new endpoint) | shipped session-dispatch seam confirmed in Spike 0 | `dispatchConsumer(bindingId, {slots})` → `POST /api/ai/chat/sessions/{id}/dispatch` (`spike-0-dispatch-path.md` §2) |

**No existing appendix/defined-term/cross-ref primitive exists** — grep for
`SemanticAppendix|DefinedTerm|CrossReference` across `Services/` returned only unrelated
`VisualizationService`/`ProjectPreFillService` hits, and `Services/Compose/` contains only
`ComposeService`, `IComposeService`, `StaleCheckoutSweeperHostedService`. So the generator is
genuinely new surface — but it is a **pure text transformer** over `DocumentText`, not a new
extraction/AI stack.

### 3.1 Generator shape (production target: `Services/Compose/SemanticAppendixGenerator`)

```csharp
namespace Sprk.Bff.Api.Services.Compose;

// ADR-013: NO IOpenAiClient / executor / routing injection. Pure payload producer.
// Tier-1 NetArchTest enforces this on the production class (design §7.2).
public sealed class SemanticAppendixGenerator
{
    // Input: already-extracted text (from IDocumentTextSource) + optional OOXML structure.
    // Output: a single read-only appendix string to append below the body.
    public string Build(SemanticAppendixInput input);
}

public sealed record SemanticAppendixInput(
    string BodyText,                              // DocumentText.ExtractedText (Markdown projection)
    IReadOnlyList<CrossReferenceTarget>? CrossRefs // Tier B — from DocxAnnotationReader (Phase 2); null in Tier A
);
```

**Two tiers by input availability** (honest about what each phase can extract):

- **Tier A — text-only (buildable today, drives this spike):** *Defined Terms* (typography regex,
  usage-counted) + *Structure* (heading outline from the Markdown projection). Works purely on
  `DocumentText.ExtractedText`. This is the arm the §5 protocol measures.
- **Tier B — DOCX-structural (Phase 2):** *Cross-Reference Targets* need OOXML `w:bookmarkStart` /
  `w:fldSimple` / `w:instrText REF` walking — that data is **not in flat extracted text**. It
  becomes available from `DocxAnnotationReader` (design §6.2, `design.md:424`). Tier B is
  design-specified here; the generator takes it as an optional input so Tier A ships first.

### 3.2 Defined-terms extraction — copy adeu's typography patterns (not English regex)

Faithful to `domain.ts extract_all_domain_metadata` (research lines 120-124):

- Leading quoted term at paragraph start (optionally after a section number):
  `^(?:[\d.\-()a-zA-Z]+\s*)?["“]([A-Z][A-Za-z0-9\s\-&'’]{1,60})["”]`
- Inline `(the "Agreement")` definition: `\([^)]*?["“]([A-Z][A-Za-z0-9\s\-&'’]{1,60})["”][^)]*?\)`

Then: usage-count each term across the body; **drop zero-use terms**; flag duplicate definitions as
`[Error] Duplicate Definition`. (C# `System.Text.RegularExpressions`; the `compose-defined-terms`
*Action* is the LLM-powered consistency checker — the appendix generator is only the deterministic
typography pre-scan that primes it. Two distinct things, not a duplicate.)

### 3.3 Injection — zero renderer change

`ActionRunner.BuildPrompt` already routes `DocumentText.ExtractedText` into `## Document`
([`ActionRunner.cs:126-127,242-248`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/PromptSchemaRenderer.cs)).
So the compose-document scope-payload assembly step concatenates:

```
{body Markdown, with existing track-changes rendered inline as {++/--/>>/<<} CriticMarkup}
<appendix block from §4>
```

and hands the combined string in as the document text. The appendix rides the existing `##
Document` render path — **no change to `PromptSchemaRenderer`**. (A structured variant could instead
pass the appendix as `runtimeInput` → `## Input` per `PromptSchemaRenderer.cs:224-239`; the inline
form is preferred because it matches adeu's proven "below-body behind a boundary marker" placement
and keeps the LLM's read-order body-then-reference.)

### 3.4 ADR compliance

- **ADR-013** (`.claude/adr/ADR-013-ai-architecture.md:44-45,54`): generator injects no AI-internal
  types; it consumes `DocumentText` and returns `string`. ✅
- **ADR-039** (`ADR-039...md:42,64`): no new dispatch route, no second routing surface; A/B runs
  through the one shipped dispatch seam. ✅
- **CriticMarkup read-direction** (`design.md:384,758`): the LLM *reads* `{++/--/>>/<<}` but never
  *emits* it — the compose Actions produce structured `{target_text,new_text,comment}` payloads.
  The appendix does not change this asymmetry; it sits below the body as read-only reference.

---

## 4. Appendix content format (specified for production reuse — acceptance criterion #2)

Faithful to adeu `<!-- READONLY_BOUNDARY_START -->` (research lines 101, 118, 132). The boundary
marker is load-bearing: the edit validator refuses any edit whose resolved index lands **inside**
the appendix range (adeu `engine.ts:276-287`), so the appendix can never be accidentally edited.

```
<!-- READONLY_BOUNDARY_START -->
## Document Semantics (read-only reference — do NOT edit or cite as body text)

### Defined Terms
- "Agreement" — defined at §1.1 — used 47×
- "Effective Date" — defined at §2.3 — used 8×
- "Confidential Information" — defined at §5.1 — used 23×
[Error] Duplicate Definition: "Party" defined at §1.2 and §3.4

### Cross-Reference Targets            (Tier B — omitted when OOXML structure unavailable)
- _Ref481207 — anchored to "§4.2 Payment Terms" — referenced from §7.1, §9.3
- _Ref481233 — anchored to "Schedule B" — referenced from §6.4

### Structure
- §1 Introduction
- §2 Definitions
- §3 Scope of Services
- §4 Payment Terms
- §5 Confidentiality
<!-- READONLY_BOUNDARY_END -->
```

**Format rules (production contract):**
1. Section order fixed: Defined Terms → Cross-Reference Targets → Structure.
2. Defined Terms: alpha or definition-order; `"<term>" — defined at <loc> — used <n>×`; zero-use
   dropped; duplicates emit an inline `[Error] Duplicate Definition` line.
3. Cross-Reference Targets: `<_RefId> — anchored to "<snippet>" — referenced from <loc-list>`.
   Entire section omitted (not emitted empty) when Tier B input is null.
4. Structure: heading outline only (no page numbers in Tier A — pagination is a DOCX-render
   property, deferred to Tier B / Phase 2).
5. Boundary markers `<!-- READONLY_BOUNDARY_START/END -->` are mandatory and are what the edit
   validator keys its read-only zone on.
6. Token discipline: appendix is bounded (terms/refs/headings are small vs body); no full-text
   duplication. On very large docs, cap defined-terms list and note truncation — mirrors adeu's
   token-budget stance (research line 202).

---

## 5. A/B evaluation protocol (ready-to-run; reproducible — acceptance criterion #3)

> This is the deferred runtime measurement, specified so a later deployed run executes it verbatim
> and fills in §5.5. It is NOT run here (no live Azure OpenAI in a headless session).

### 5.1 Test document set (fixed, ground-truth-labeled)

- **10–12 documents**, legal-operations representative: NDAs, MSAs, SOWs, amendments — the classes
  R2 targets. Mix short (1–2 pp) and long (15+ pp, where hallucination pressure is highest).
- Each doc carries a **hand-authored ground-truth key**: the true set of defined terms (+ definition
  location + usage count), true cross-reference targets, true section outline, and the verbatim text
  of ~5 named clauses. Store under `notes/spikes/spike-4-eval/{doc-id}/ground-truth.json`.
- Include ≥2 **adversarial** docs: near-duplicate defined terms ("Agreement" vs "the Agreements"),
  a duplicate definition, and a term that is capitalized but NOT defined (a hallucination magnet).

### 5.2 Fixed question / edit set (same for both arms)

Per document, a closed set of **8 probes** spanning both grounding and edit generation:

| # | Probe type | Example | What a hallucination looks like |
|---|---|---|---|
| Q1 | Defined-term lookup | "What does 'Confidential Information' mean and where is it defined?" | Cites a §that doesn't exist / invents a definition |
| Q2 | Usage grounding | "Is 'Effective Date' used consistently throughout?" | Asserts usages/§s not present |
| Q3 | Cross-ref grounding | "What does the reference in §7.1 point to?" | Fabricates a target |
| Q4 | Absent-term trap | "Summarize the 'Force Majeure' clause." (doc has none) | Invents a clause instead of refusing |
| Q5 | Structure grounding | "List the top-level sections." | Adds/renames sections |
| E1 | Edit (draft-alt) | "Draft an alternative to the indemnification clause." | New text cites non-existent defined terms/§s |
| E2 | Edit referencing terms | "Tighten the confidentiality clause; keep defined terms exact." | Renames/misquotes a defined term |
| E3 | Edit near a cross-ref | "Revise §4.2 without breaking references." | Breaks/invents the `_Ref` target |

Same probes run against each arm; **only the scope payload differs.**

### 5.3 The two arms (the ONLY difference is the appendix)

- **Arm WITHOUT:** compose-document scope payload = body Markdown (+ inline CriticMarkup) only.
- **Arm WITH:** same body + the §4 appendix appended below the READONLY boundary.
- **Everything else identical:** same Action prompt, same model deployment, same temperature
  (pin `temperature=0` for measurement determinism — overrides the Action's authored temp for the
  eval only), same `maxOutputTokens` ceiling ([`ActionRunner.cs:44`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/LinearConsumers/ActionRunner.cs) `= 4000`), same doc, same probe.
- **Dispatch mechanism:** the shipped seam from Spike 0 — a throwaway harness calls
  `dispatchConsumer(bindingId, {slots})` → `POST /api/ai/chat/sessions/{id}/dispatch`. The harness
  toggles appendix inclusion in the scope-payload assembly step; it does NOT create two Bindings and
  does NOT add an endpoint (ADR-039). Run **3 trials per (doc × probe × arm)** to average LLM
  nondeterminism (even at temp 0, structured decoding has minor variance).

### 5.4 Scoring rubric (hallucination, per response)

Two independent scores per response, both blind to arm:

1. **Fabrication count (objective, ground-truth-checked):** count of asserted entities absent from
   the doc — (a) defined terms not in the key, (b) section/`_Ref` citations not in the key,
   (c) quoted "verbatim" text not found in the body (normalized: smart-quote + Markdown-strip +
   whitespace, per adeu's 4-tier match cascade, research lines 87-92). One fabrication = one point.
2. **Binary grounded/hallucinated label:** a response is "hallucinated" if fabrication count > 0 OR
   (for Q4 absent-term trap) it answered instead of refusing. Rated by (i) a rule-checker against
   the ground-truth key AND (ii) a second human/LLM-judge rater; disagreements adjudicated by a
   human. Report inter-rater agreement (Cohen's κ) as a validity guard.

**Primary metric:** hallucination **rate** = (# hallucinated responses) / (total responses),
computed per arm. **Delta = rate(WITHOUT) − rate(WITH)** (absolute, percentage points). Secondary:
mean fabrication count per arm; per-doc breakdown (no doc may regress). Report a 95% CI on the delta
(paired by doc × probe — same items both arms → paired test, e.g. McNemar on the binary labels).

### 5.5 Pass/fail bands (the FR-22 decision gate)

| Band | Delta (WITHOUT − WITH, absolute pp) | Decision for FR-22 |
|---|---|---|
| **STRONG** | ≥ 20 pp reduction, no per-doc regression | Ship the appendix; production `SemanticAppendixGenerator` justified as designed |
| **MODERATE** | 10–20 pp reduction, ≤1 minor per-doc regression | Ship; note diminishing returns on short docs; consider gating appendix to long docs |
| **WEAK** | < 10 pp, CI includes 0 | Reconsider scope — appendix may not earn its token cost; escalate to owner before building Tier B |
| **NEGATIVE** | WITH ≥ WITHOUT (appendix raises hallucination) | **Do NOT ship**; investigate (likely appendix noise/over-trust) before FR-22 |

Decision is **STRONG or MODERATE → build FR-22 as designed**; WEAK/NEGATIVE → escalate per
CLAUDE.md §6 before committing the production generator. Record the filled table + raw per-doc data
under `notes/spikes/spike-4-eval/results.json` when the run executes.

### 5.6 Runtime prerequisites (why this is deferred, not skippable)

Deployed BFF + Azure OpenAI deployment + one **throwaway** compose-document Binding row (Phase 4
catalog authoring — **core-A0-gated**; do not guess the triple-twin hoist shape until core A0
publishes). Same posture as Spike 0 §6: author throwaway rows on `spaarkedev1`, run, delete.

---

## 6. Assumption corrections (read before authoring task 023 / FR-22)

1. **Research doc "defer defined-terms to R3" is superseded.** `adeu-architecture-study.md:262`
   says don't ship the defined-terms appendix in R2. The 2026-07-08 scope-lock (`design.md:391,453`;
   `spec.md:98`) puts the appendix (FR-22) AND `compose-defined-terms` in R2. Build for R2.
2. **The appendix generator ≠ the `compose-defined-terms` Action.** They both touch defined terms
   but are distinct (CLAUDE.md §11 existing/extension test): the **generator** is a deterministic
   typography pre-scan producing read-only reference text appended to the scope payload; the
   **Action** (`design.md:453`) is an LLM capability that detects terms + flags *inconsistent usage*
   and renders READ-ONLY in the Context pane. The generator primes the Action (and every
   compose-document consumer); it does not replace it. Do not collapse them.
3. **Cross-references are Tier B, not Tier A.** Flat extracted text (`DocumentText.ExtractedText`)
   has no bookmark/REF-field structure. Cross-ref targets require OOXML walking via
   `DocxAnnotationReader` (Phase 2, `design.md:424`). The generator must accept cross-refs as an
   optional input and omit the section when null — don't block Tier A defined-terms + structure on
   Phase 2.
4. **No renderer change is needed** (§3.3). Early instinct was to add a dedicated `## Appendix`
   section to `PromptSchemaRenderer`. Unnecessary — the appendix rides the existing `## Document`
   render path (`PromptSchemaRenderer.cs:242-248`). Extending the renderer would be scope creep
   (CLAUDE.md §11). If a structured form is ever wanted, the `## Input` `runtimeInput` seam
   (`:224-239`) already exists.

---

## 7. Acceptance criteria — disposition

| # | Criterion | Result |
|---|-----------|--------|
| 1 | Measured hallucination delta WITH vs WITHOUT over a fixed question/edit set | ⏸️ **Runtime-measurement-deferred.** Requires live Azure OpenAI dispatches — not runnable headlessly; fabricating numbers is forbidden by the task brief. Ready-to-run protocol with scoring + pass/fail bands delivered (§5); executes at Phase 1/4 on `spaarkedev1` (§5.6). |
| 2 | Appendix content format (defined terms, cross-refs, structural metadata) specified for production | ✅ **Static-design-confirmed** (§4) — exact format + 6 production rules + boundary-marker contract, faithful to adeu Pattern 4. |
| 3 | A/B harness + scoring method documented so measurement is reproducible | ✅ **Static-design-confirmed** (§5) — fixed doc set, closed 8-probe set, two arms differing only in the appendix, fabrication-count + binary-label rubric, paired stats, pass/fail bands. |

Bonus (design confidence): generator design grounded in shipped primitives with file:line evidence
(§3), ADR-013/ADR-039 compliance argued (§3.4), 4 assumption corrections (§6).

## 8. Why the POML steps were adapted (directional step-mode note)

POML steps 1–3 read "build the generator, run each probe WITH/WITHOUT live, measure the delta."
Under `<steps mode="directional">` the binding contract is the goal + acceptance criteria, and the
sequence is adaptable to reality. Two facts forced adaptation:

- **The core measurement cannot run headlessly.** No live BFF + Azure OpenAI + Dataverse Binding in
  this session. A "measured delta" produced here would be fabricated, not evidence — the task brief
  explicitly forbids this. The honest artifacts are the reuse-grounded design + the format + the
  runnable protocol.
- **"Throwaway confined to `notes/spikes/`; no un-runnable throwaway in `src/`."** Committing a
  `SemanticAppendixGenerator.cs` into `Services/Compose/` that can't be exercised end-to-end (its
  measurement gate is deferred) would be repo noise contradicting the spike's own constraint. The
  generator design (§3.1) is the deliverable; task 023/FR-22 builds the production class once the
  §5 run returns STRONG/MODERATE.

The spike's goal — "can the appendix be built from existing primitives, what is its exact format,
and how do we rigorously prove it reduces hallucination?" — is fully met, plus 4 design-invalidating
corrections (§6) a literal build-and-run would have missed.
