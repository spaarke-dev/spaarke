# Spike 3 — Atomic edit batch + rollback (ComposeEditBatch / ComposeEditTransaction)

> **Task**: 003 · **Phase**: 0 Spikes · **Date**: 2026-07-08 · **Model**: sonnet @ high
> **Method**: throwaway runnable prototype (pure text-offset logic) + adeu-pattern
> reconciliation, grounded with file:line evidence.
> **Deliverable**: this note + [`edit-batch-prototype.cs`](./edit-batch-prototype.cs).
> The prototype was **actually compiled and run headlessly** (net8.0, `dotnet run`) —
> all four proofs held (exit 0). No BFF/editor runtime was needed because the
> ordering + atomicity invariant is a pure string-offset property.

---

## 1. Decision (the one thing this spike unlocks — design §13)

**VALIDATED — the atomic-transaction model for `ComposeEditBatch` + `ComposeEditTransaction`
is sound and provable.** Tasks 021 (FR-20) and 022 (FR-21) may build against it. The model:

1. **Phase 1 — Resolve (read-only).** Resolve every edit's `target_text` to a physical
   `[start,end)` span per its `match_mode`. Mutates nothing. Unresolvable/ambiguous edits
   become per-index validation errors.
2. **Phase 2 — Sort descending** by resolved start offset (largest first).
3. **Phase 3 — Skip overlap** via `occupied_ranges`; first-resolved wins, later overlap is
   **skip-and-reported (non-fatal)**.
4. **Phase 4 — Apply bottom-up.** Splicing from the highest offset down leaves all lower
   offsets valid — **this is the offset-stability guarantee**.

Wrapped by **`ComposeEditTransaction`**: snapshot the pre-batch document → run Phase 1 for the
**whole batch first** → **if ANY edit fails validation, abort and return the untouched snapshot
(none apply)** → only a cleanly-resolving batch proceeds to Phases 2–4.

**All three acceptance criteria are STATIC-CONFIRMED by real execution** (§4). Nothing here is
runtime-deferred — the logic is headlessly runnable, unlike Spike 0's SSE/ledger legs.

---

## 2. Highest-value finding — an assumption CORRECTION for tasks 021/022

The POML/design §6.1 lists **"skip overlap"** and **"snapshot/rollback"** in one breath, which
reads as if overlap triggers rollback. **It must not.** The prototype makes the two failure classes
explicit and they have **opposite** semantics:

| Failure class | Example | Semantics | Batch outcome |
|---|---|---|---|
| **Validation failure** (Phase 1) | `target_text` not found; ambiguous under `strict`; empty target | **FATAL** → transaction rollback | **NONE apply** |
| **Within-batch overlap** (Phase 3) | two edits claim intersecting spans | **NON-FATAL** → skip later, report | **batch still commits**; overlapped edit dropped |

This matches adeu exactly: overlap is `occupied_ranges` skip-and-report
([`adeu-architecture-study.md:143,239`](../../research/adeu-architecture-study.md) —
"The later-in-list edit is skipped … and reported"), whereas a failed `target_text` resolution or
`strict` ambiguity raises `BatchValidationError` and the sequential wet-run "rolls back the whole
batch" ([`:148`](../../research/adeu-architecture-study.md)).

**Action for task 021/022 authors:** model these as two code paths. `ComposeEditBatch` owns the
non-fatal overlap skip; `ComposeEditTransaction` owns the fatal all-or-nothing rollback. Do **not**
let an overlap collapse the batch, and do **not** let a single invalid edit partially apply. Proof 2
and Proof 4 below are the regression fixtures for this exact distinction.

Second, smaller correction: adeu exposes **no pure insert/delete** — everything is `ModifyText`
with `target_text`+`new_text` (empty `new_text` == delete)
([`:138`](../../research/adeu-architecture-study.md)). R2's `compose-draft-alternative` output
contract (design §4: `{target_text, new_text, match_mode, rationale, sources}`) already mirrors
this; keep it search-and-replace-only so the LLM must supply verifiable anchor context.

---

## 3. Grounding — reuse check (CLAUDE.md §11) and the reference to copy

- **No existing server-side atomic batch-apply primitive exists.** Grep for
  `ApplyEdits|apply_edits|occupied_ranges|OrderByDescending…Offset|TextEdit|IComposeEditApplicator|EditValidationError`
  across `src/server` returned **no files**. The only `Services/Compose/` code today is
  `ComposeService.cs` / `IComposeService.cs` / `StaleCheckoutSweeperHostedService.cs` (DOCX
  load/save/promote + checkout lifecycle) — none does edit application. So this is genuinely new
  surface; the reuse mandated by §11 is **reuse of the adeu *pattern*, not code** — design §6.1
  explicitly says "adopt patterns, not code" and §6.4 "NOT code dependency."
- **The pattern to copy** is adeu `engine.ts apply_edits` (lines 1921–2101) + `takeSnapshot`/
  `restoreSnapshot` (lines 95–128), as documented in
  [`research/adeu-architecture-study.md:140-148`](../../research/adeu-architecture-study.md). The
  four phases and the "bottom-most edits receive lower sequential IDs" side-effect are transcribed
  faithfully into the prototype.
- **Client-side edit surfaces are unrelated** (they stream/insert, they don't do offset-atomic
  batch apply): `AnalysisWorkspace/src/hooks/useDiffReview.ts`, `useDocumentInsert.ts`, and
  `RichTextEditor/hooks/useDocumentStreamConsumer.ts`. The Compose editor
  ([`ComposeEditor.tsx`](../../../../src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeEditor.tsx))
  is a pure TipTap drafting surface with a locked extension set and no edit-application logic — the
  materialization of a resolved batch into ProseMirror marks (`insertion`/`deletion`/`commentAnchor`)
  is design §6.1's separate client work, downstream of this server pipeline and of the ledger
  compose-disposition (ADR-040). This spike is **server-side only**.

### ADR posture
- **ADR-013 (AI facade)**: the pipeline is deterministic text processing — it injects **no**
  `IOpenAiClient`/executor/routing types. Nothing in the prototype touches AI internals; the
  Tier-1 NetArchTest guard in production is satisfied by construction. ✅
- **ADR-038 (testing)**: proofs are pure in-memory assertions at the module boundary — no
  `Mock<HttpMessageHandler>`, no DI-registration/ctor-null tests. The four proofs are MAINTAIN-class
  behavioral fixtures (offset stability, atomicity, ambiguity rollback, overlap non-fatality) and
  are the natural seed for task 021/022's unit tests. ✅

---

## 4. Proofs — real execution output (acceptance criteria disposition)

Ran `dotnet run` on [`edit-batch-prototype.cs`](./edit-batch-prototype.cs); **exit 0, all PASS.**
Document under test (offsets non-trivial — words recur):
`"The Tenant shall pay Rent monthly. The Tenant shall maintain the Premises. The Landlord shall provide access to the Premises."`

| # | Acceptance criterion | Proof | Result |
|---|---|---|---|
| 1 | Note documents the validated model (4-phase + snapshot/rollback) | §1 + §2 | ✅ **static-confirmed** |
| 2 | Intentionally-failing batch applies **NONE** of its edits | **Proof 2** | ✅ **static-confirmed by execution** |
| 3 | Valid batch preserves **offset stability** (bottom-up) | **Proof 1** | ✅ **static-confirmed by execution** |

**Proof 1 (offset stability).** Three non-overlapping edits of *different lengths* (one grows, one
~same, one shrinks) applied bottom-up produced the exact expected document. A **control** —
`BrokenTopDownApply` resolving offsets once then applying lowest-first — corrupted the document
(`"…pay the Rent on the first day of eakeep the Premises in good repairshall maingrant entry…"`),
proving bottom-up ordering is *necessary*, not incidental. Applied order was edit#3 `[94,124)` →
edit#2 `[52,73)` → edit#1 `[17,33)`.

**Proof 2 (atomicity — the core criterion).** The three valid edits **plus** one invalid edit
(`target_text "indemnify the Guarantor"` absent). Result: `committed == false`, document
**byte-identical to the pre-batch snapshot**, zero edits applied, and the error names the exact
failing index (`Edit 4`). One bad edit ⇒ none apply. ✅

**Proof 3 (ambiguity rollback, bonus).** `"The Tenant"` occurs twice under `match_mode=strict` ⇒
`BatchValidationError`-equivalent ⇒ rollback; the *valid sibling* edit in the same batch did **not**
sneak through. Confirms strict ambiguity is a fatal validation failure, and the structured error
carries adeu's copy-pasteable resolution hint (set `match_mode='all'`/`'first'`/add context).

**Proof 4 (overlap non-fatality, the §2 distinction).** Two edits with intersecting spans ⇒ the
first-resolved applied, the later was **skip-and-reported**, and the batch **committed**. This is
the fixture that stops a future implementer from wiring overlap into the rollback path.

---

## 5. Guidance for tasks 021 (ComposeEditBatch) & 022 (ComposeEditTransaction)

1. **`ComposeEditBatch`** (FR-20): implement the four phases as pure functions over the resolved
   offset model. Production `target_text`→offset resolution runs against the **Open XML body
   projection**, not a raw string — but the sort-descending + occupied-ranges + bottom-up-splice
   logic transfers unchanged. Keep Phase 1 strictly read-only.
2. **`ComposeEditTransaction`** (FR-21): snapshot the document part BEFORE Phase 2. In the DOM world
   the cheap-immutable-string snapshot becomes adeu's deep `cloneNode` of all XML parts (`pkg.unzipped`
   + per-part `rels` + `current_id`) — [`adeu-architecture-study.md:148`](../../research/adeu-architecture-study.md).
   Run Phase 1 for the whole batch; abort-and-restore on the first validation error.
3. **Two failure paths, two code paths** (§2): overlap → skip-and-report inside the batch; validation
   failure → transaction rollback. Port Proofs 2 and 4 as the guarding unit tests.
4. **`match_mode` = `strict`(default)/`first`/`all`** with structured, index-keyed, resolution-hint
   errors (adeu `format_ambiguity_error`). `all` fans one edit to N spans in Phase 1. This overlaps
   Spike 2's `ComposeEditValidator` scope — coordinate so validation lives in one place and the batch
   consumes already-validated edits (design §6.1 note: "edits enter the batch pre-validated").
5. **`POST /api/compose/edit-batch/validate`** (design §693) is the client-facing dry-run: run Phase 1
   only, return the structured error list + recovery UX payload, mutate nothing — the adeu
   `dry_run: true` "snapshot → run → unconditionally restore" mode.

## 6. What was NOT proven here (honest scope)
- **Open XML DOM offset resolution** (raw-vs-clean dual mapper, foreign-author `<w:ins>` overlap
  unwrapping, cell-anchor `{#cell:id}` targets) is Phase 2/DOCX-shuttle territory (design §6.2,
  tasks 025+), not this spike. The prototype uses plain `IndexOf` because the atomicity/ordering
  invariant is independent of how offsets are *sourced*.
- **CriticMarkup-in-`new_text` refusal, heading-depth>6, structural-marker mismatch** validators
  (adeu `validate_edit_strings`) belong to Spike 2 / `ComposeEditValidator`; only `not-found`,
  `ambiguous-strict`, and `empty-target` are exercised here — enough to prove the transaction gate.
