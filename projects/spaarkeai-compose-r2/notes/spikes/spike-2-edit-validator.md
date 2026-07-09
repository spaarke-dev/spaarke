# Spike 2 — Edit validator `match_mode` + structured ambiguity errors

> **Task**: 002 · **Phase**: 0 Spikes · **Date**: 2026-07-08 · **Model**: sonnet @ high
> **Method**: throwaway **executable** C# prototype (`dotnet run`, no LLM / no BFF / no Azure)
> grounded in the adeu source study + design §6.1/§13, run against 7 representative edits.
> **Deliverables**: this note + [`edit-validator-prototype.cs`](./edit-validator-prototype.cs)
> (compiles + runs clean on .NET 10; the captured output below is the real run, not hand-authored).
> **Unlocks** (design §13): the FR-19 validator design (`match_mode` semantics) + the structured
> ambiguity-error UX shape, validated before task 020 builds the production
> `Services/Compose/IComposeEditValidator` + `POST /api/compose/edit-batch/validate`.

---

## 1. Decision (the one thing this spike unlocks)

**ADOPT the adeu `match_mode` + structured-ambiguity-error design verbatim, with the concrete
C# contract + error-UX shape prototyped and executed here.** The design is sound, deterministic,
and — unlike Spike 0's live-BFF legs — **fully confirmable headlessly** because the validator is
pure text processing. All 7 sample edits (5 required archetypes + 2 bonus negatives) produced the
expected verdict on a real run (§4). Task 020 should port `edit-validator-prototype.cs` shapes
directly into `ComposeEditModels.cs` / `ComposeEditValidator.cs`.

**Locked design (three parts):**

1. **`match_mode` semantics** — `strict` = exactly-one-or-refuse; `first` = earliest occurrence;
   `all` = every occurrence. Zero-match and empty-target are refusals in every mode. (§2)
2. **Structured ambiguity error** — `{ Kind, Message, MatchCount, Examples[≤5]{Offset, ContextBefore,
   Matched, ContextAfter}, ResolutionHint }` where `ResolutionHint` is the copy-pasteable
   3-option escape hatch adeu proved stops the LLM's refine-forever loop. (§3)
3. **Batch overlap flag** — cross-edit resolved-span collision is surfaced as a batch-level
   `Overlap` error, not two silent conflicting resolutions. (§3, EDGE-6)

**No AI internals.** The prototype (and therefore the FR-19 design) injects nothing — no
`IOpenAiClient`, no executor, no `IConsumerRoutingService`. It takes a `documentText` string +
a list of proposed edits and returns verdicts. This is exactly the ADR-013 facade boundary the
Tier-1 NetArchTest enforces in production (task 025). See §6.

---

## 2. `match_mode` semantics (validated)

The LLM's job is collapsed to find-and-replace; the LLM declares its match precision as a
parameter and the engine owns correctness (adeu `references/criticmarkup.md`: *"target_text must
be unique by default; either add surrounding context, or explicitly set match_mode"*).

| Mode | Rule | Zero matches | One match | N>1 matches |
|---|---|---|---|---|
| **`strict`** (default) | Exactly one match required | ❌ `NoMatch` refuse | ✅ resolve `[off,off+len)` | ❌ `Ambiguous` refuse (the core safety) |
| **`first`** | Earliest occurrence in document order | ❌ `NoMatch` refuse | ✅ resolve | ✅ resolve the earliest only |
| **`all`** | Every occurrence | ❌ `NoMatch` refuse | ✅ resolve (1 span) | ✅ resolve all N spans |

**Design decisions grounded in the run:**
- **Ordinal, case-SENSITIVE matching** (`StringComparison.Ordinal`) so behavior is deterministic
  across hosts/cultures and legal defined-terms ("Party" vs "party") are not silently conflated.
  Verified: S4 `first` on `"Party"` resolved `[132,137)` (the `P`-cap occurrence), not `parties`.
- **Overlap-aware occurrence scan** (advance by `+1`, not by `needle.Length`) so a target like
  `"aa"` in `"aaaa"` reports the true count. Matters for honest `MatchCount` in the error.
- **`strict` is the default and the safe path.** N>1 never applies silently — this is the entire
  point of FR-19 (design §6.1: *"engine refuses ambiguity with an actionable error"*).

Prototype: `ComposeEditValidator.ValidateOne` + `FindAll` in `edit-validator-prototype.cs`
(EDGE-1..EDGE-5 comments mark each branch — mirror the `// EDGE-N:` convention into task 020).

---

## 3. Structured ambiguity-error UX shape (validated)

Adeu source: `markup.ts format_ambiguity_error` (lines 375–441) — *"Without this, agents loop
forever refining target_text/regex because they never learn that match_mode is the built-in
escape hatch."* Ported shape:

```csharp
record EditValidationError(
    EditErrorKind Kind,           // Ambiguous | NoMatch | EmptyTarget | Overlap
    string        Message,        // human/log line, carries the edit index ("Edit 2: …")
    int           MatchCount,     // honest total (may exceed the 5 shown)
    IReadOnlyList<MatchExample> Examples,     // up to 5, first-in-document order
    string        ResolutionHint  // copy-pasteable, multi-line
);
record MatchExample(int Offset, string ContextBefore, string Matched, string ContextAfter);
```

**Window**: ±50 chars pre/post (adeu's value), newlines flattened to spaces for a single-line
example. **Cap**: 5 examples even when `MatchCount` is larger (adeu shows first 5).

**The `ResolutionHint` for an ambiguity refusal is the load-bearing UX** — it is a literal string
the model can act on without re-reasoning:

```
target_text "the Agreement" matched 4 times. Re-send this edit using ONE of:
  1. Set "match_mode":"all"  — apply to every occurrence.
  2. Set "match_mode":"first" — apply to the earliest occurrence only.
  3. Extend target_text with surrounding context so it is unique (see the examples above).
```

**Per-edit index in every message** (adeu: `Edit ${i+1}`) so that in a batch, only the failing
edit is resubmitted. **Batch-level `Overlap`** carries the two colliding spans + a merge/narrow
hint. **`NoMatch`** carries a nearest-candidate hint (see §5 boundary finding).

**Endpoint mapping for task 020**: return **200** with the resolved-spans result when
`BatchValidationResult.IsValid`; **422** with the structured error(s) otherwise (matches task 020
acceptance criterion 7). The error record serializes directly to the 422 body.

---

## 4. The 7 sample edits — expected vs **actual executed** verdicts

Document under test (415 chars, deliberate repetition; full text at top of the run capture):
`"This Master Services Agreement (the \"Agreement\") … Each Party … under the Agreement. Termination
of the Agreement … thirty (30) days notice … the Agreement is signed by the last Party … by a Party
under the Agreement."`

| # | Sample | mode | Expected | **Actual (executed)** | ✓ |
|---|---|---|---|---|---|
| S1 | `"thirty (30) days notice"` (unique) | strict | resolve 1 | **RESOLVE** `[211,234)` | ✅ |
| S2 | `"the Agreement"` (repeated) | strict | refuse `Ambiguous`, count+examples+hint | **REFUSE** `Ambiguous`, count=4, 4 examples w/ context, 3-option hint | ✅ |
| S3 | `"the agreemnt"` (typo) | strict | refuse `NoMatch` + hint | **REFUSE** `NoMatch`, generic hint (see §5) | ✅ |
| S4 | `"Party"` (repeated) | first | resolve earliest | **RESOLVE** `[132,137)` (earliest only) | ✅ |
| S5 | `"the Agreement"` (repeated) | all | resolve every | **RESOLVE** 4 spans `[158,171)[188,201)[276,289)[401,414)` | ✅ |
| S6 | `"   "` (whitespace) | strict | refuse `EmptyTarget` | **REFUSE** `EmptyTarget` | ✅ |
| S7 | two edits, spans collide | strict×2 | batch `Overlap` flag | **batch INVALID**, `Overlap` `[327,373)`×`[352,383)` + merge hint | ✅ |

**S1–S5 are the 5 required archetypes** (unambiguous / ambiguous / no-match / first / all);
S6–S7 are bonus negatives covering the remaining task-020 acceptance rows (empty-target, overlap).
Full verbatim console output is reproducible via `dotnet run` on the prototype; the ambiguity
example block from the real run:

```
--- S2 ambiguous/strict ---   VERDICT: REFUSE   error kind: Ambiguous   matchCount: 4
  examples (4):
    @158: ...Each Party shall perform under [the Agreement]. Termination of the Agreement requires thirty (30...
    @188: ...perform under the Agreement. Termination of [the Agreement] requires thirty (30) days notice. The Effective D...
    @276: ...The Effective Date is the date on which [the Agreement] is signed by the last Party to sign. Confidential...
    @401: ...means any information disclosed by a Party under [the Agreement]....
```

---

## 5. Assumption corrections / boundary findings (read before task 020)

1. **Nearest-candidate hint is intentionally CONSERVATIVE — it does not do fuzzy/typo matching.**
   S3's typo `"the agreemnt"` (missing `e`) fell through to the generic *"No near match found…
   copy an exact span verbatim"* fallback, **not** a "did you mean 'the Agreement'?" suggestion.
   The prototype's `BuildNoMatchHint` only probes **exact variants**: trimmed whitespace,
   case-insensitive, and whitespace-collapsed (newline-flattened). Edit-distance / fuzzy-regex
   ("did you mean" for a genuine misspelling) is adeu's `_make_fuzzy_regex` + `_nearest_match_hint`
   territory (adeu study Pattern 1/3) and is **explicitly out of scope for the FR-19 MVP** — the
   design §6.2 table lists "Anchored-regex traps / `_nearest_match_hint`" as a *Phase 2* adoptable,
   not Phase 1. **Recommendation for task 020**: ship the 3 conservative probes (they catch the
   common whitespace/casing/newline drift cheaply and deterministically); defer fuzzy matching to a
   later task, and say so in a `// EDGE:` comment. A casing-only miss (e.g. `"the agreement"`)
   *would* trip the richer case-insensitive hint — that path is implemented, just not exercised by S3.

2. **The validator is offset-based; the design/FR-20 batch pipeline is where offset drift is
   handled.** This spike resolves spans against the **original** document state only. Design §6.1
   ("4-phase atomic batch pipeline: resolve → sort descending → skip overlap → apply bottom-up")
   and adeu's *"All changes evaluate against the ORIGINAL document state — do not chain dependent
   edits within one batch"* mean **apply-time ordering is FR-20's job (task 021), not the
   validator's**. The validator's only batch duty is the **overlap flag** (S7) — surfacing that two
   edits *would* collide, so FR-20/FR-21 (transaction) can refuse the batch atomically. Keep this
   separation in task 020: the validator validates; it does not apply.

3. **Plaintext-vs-DOCX projection is upstream of this validator (not a spike-2 concern, but flag
   it for task 020's contract).** The validator takes a `documentText` string. In the real pipeline
   that string is a **projection** of the editor/DOCX content (adeu projects to Markdown/CriticMarkup;
   Spaarke's editor is TipTap JSON — design §6.1 "Patterns to Avoid" says keep TipTap JSON canonical,
   project only for track-change rendering). **Task 020's `documentText` input contract must state
   which projection the offsets are relative to** (almost certainly the same plaintext projection the
   `compose-selection` scope payload builds), so downstream apply maps offsets back correctly. This is
   a contract note, not a validator behavior — but getting it wrong makes correct offsets useless.

4. **No existing primitive to reuse (CLAUDE.md §11 default-to-reuse — checked).** Grepped
   `Services/Compose/` (only `ComposeService`/`IComposeService`/`StaleCheckoutSweeperHostedService`
   — load/save/promote lifecycle, no match logic) and `Services/Ai/` (no `find_all_match`-style
   text-resolution primitive; the closest reference, `_nearest_match_hint`, exists only as prose in
   `research/adeu-architecture-study.md`, not as Spaarke code). Design §11 Component Reuse Map (line
   675) independently classifies `ComposeEditValidator` as NEW. Confirmed: FR-19 is genuinely new
   surface; the justification in task 020 §justification stands.

---

## 6. ADR-013 facade boundary — stated explicitly (acceptance criterion 4)

The prototype `ComposeEditValidator`:
- Constructor takes **no dependencies** (pure). No `IOpenAiClient`, no executor/routing types.
- `Validate(string documentText, IReadOnlyList<ProposedEdit>)` — the only public surface; input is
  caller-supplied plaintext + proposed edits. **It never reaches the model.**
- This is the ADR-013 refined boundary (design §Key Technical Constraints: *"`Services/Compose/`
  never injects `IOpenAiClient`/executor/routing types; Tier-1 NetArchTest enforces"*). Task 020
  builds the production service in the same directory under the same enforced constraint; task 025
  is the NetArchTest that fails the build if a future edit injects an AI internal.

The LLM's role is **upstream** (it emits the `{target_text, new_text, match_mode, rationale,
sources}` payload via the `compose-draft-alternative` catalog action — HANDOFF §1). The validator
is the **downstream deterministic gate**. The two never share a type; they meet only at the HTTP
seam (`POST /api/compose/edit-batch/validate`).

---

## 7. Acceptance criteria — disposition

| # | Criterion (POML) | Result |
|---|---|---|
| 1 | Note documents validated `match_mode` semantics + error UX (design §13 decision) | ✅ **Static-confirmed** — §2 (semantics) + §3 (error UX), both executed (§4). |
| 2 | Structured ambiguity error shape (count + ≤5 examples w/ context + recovery path) specified | ✅ **Static-confirmed** — §3 record + real S2 output (count=4, 4 contextual examples, 3-option hint). |
| 3 | All 5 representative sample edits run and outcomes recorded | ✅ **Static-confirmed (genuinely executed)** — §4 S1–S5 + 2 bonus; verbatim `dotnet run` output, not hand-authored. |
| 4 | Prototype respects ADR-013 facade (no AI internals) + note states it | ✅ **Static-confirmed** — §6; zero-dependency ctor, verified in source. |

**Nothing here is runtime-deferred.** Because the validator is pure deterministic text processing
(no LLM call, no BFF, no Azure), the spike is fully confirmable headlessly and all four criteria are
**statically + executably satisfied** — this is the key contrast with Spike 0 (whose SSE-frame /
ledger-row legs required a live runtime and were honestly marked deferred). The only *live* thing
task 020 adds beyond this spike is the HTTP seam + auth + real projection input, none of which
change the validator's verified logic.

### Optional end-to-end check when the deployed BFF exists (post task-020, not required by this spike)
After FR-19 ships, confirm the seam with: `POST /api/compose/edit-batch/validate` (RequireAuthorization)
carrying `{ documentText, edits:[{target_text:"the Agreement", new_text:"this Agreement",
match_mode:"strict"}] }` → expect **422** with the `Ambiguous` body (count=4, 4 examples, hint);
flip `match_mode:"all"` → expect **200** with 4 resolved spans. This is a task-025 integration-test
row, not a spike deliverable.

---

## 8. Hand-off to task 020 (FR-19) — concrete port list

Port from [`edit-validator-prototype.cs`](./edit-validator-prototype.cs):
- **`ComposeEditModels.cs`** ← the records: `MatchMode`, `ProposedEdit` (add `rationale` +
  `sources[]` per HANDOFF §1 to fully mirror the catalog payload), `ResolvedMatch`, `MatchExample`,
  `EditValidationError`, `EditVerdict`, `BatchValidationResult`.
- **`ComposeEditValidator.cs`** ← `ValidateOne` (EDGE-1..5), `FindAll` (ordinal, +1 scan),
  `BuildAmbiguityError`, `ExtractExample` (±50), `BuildNoMatchHint` (3 conservative probes; add
  `// EDGE:` noting fuzzy deferred), `DetectOverlaps` (EDGE-6).
- **`IComposeEditValidator.cs`** ← the one-method interface; register single scoped/singleton per
  ADR-010 (helpers not DI-registered).
- **`ComposeEndpoints.cs`** ← `MapPost("/api/compose/edit-batch/validate", …).RequireAuthorization()`;
  200 on `IsValid`, 422 with structured error otherwise.
- **Tests** ← the 7 samples here are the seed for `ComposeEditValidatorTests.cs`; each maps to a
  task-020 acceptance row. Test through the public contract with real strings (ADR-038; no
  `Mock<HttpMessageHandler>`, no DI-registration tests).
