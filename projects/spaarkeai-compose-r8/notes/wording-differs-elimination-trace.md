# FR-C07 — "wording differs slightly": the elimination trace

> **Task**: `053-bounded-confirmable-fallback.poml` · **Rigor**: FULL · opus @ xhigh · **Date**: 2026-08-25
> **Requirement**: *"The string does not exist as a reachable state in the AI edit flow"* (spec FR-C07).
> **Owner's directive**: *"we NEVER should get the 'wording differs slightly' — this doesn't make any
> sense or any possibility if we are controlling the document."*
> **Method constraint (POML step 4)**: *"Proving this is not a grep for the string — it requires tracing
> every path that could render it and showing each is either deleted or unreachable."*

---

## 0. What is being proven, and why a grep is not it

There are two different claims, and only the second one is FR-C07:

| Claim | How you'd check it | Why it is not enough |
|---|---|---|
| **A — the string is not in the source.** | `grep` | A string can be absent while the STATE it named is still produced, now described by a different sentence that is equally untrue. And a grep says nothing about which inputs reach which branch. |
| **B — no reachable input produces that state.** | Enumerate the producers of the state, enumerate the render sites, and pair them. | This is the claim. |

So this document does three passes: **§1** the render sites (where the sentence could ever appear),
**§2** the state producers (what could put the banner into that branch), and **§3** the pairing — for
every producer, what the user now sees, and why that is true rather than merely different.

**Result: 2 production render sites, 1 test-harness duplicate, 6 comments. All three renders are
DELETED. The state behind them still exists — an AI edit sometimes cannot be placed — and it now
renders one of five sentences, each true of the specific way the placement failed.**

---

## 1. Pass 1 — every site that could render the sentence (pre-task-053 inventory)

Repo-wide over `src/`, `tests/`, `infra/`, `projects/`, `docs/`, `.claude/`.

| # | Site | Kind | Reachable by a user? | Disposition |
|---|---|---|---|---|
| **R-1** | `ComposeBannerStack.tsx:921` — batched branch: *"N of M suggested edits couldn't be placed automatically — **their wording differs slightly from this document**. You can still review, edit, and save."* | **Rendered JSX string** | **YES** | **DELETED** (§3) |
| **R-2** | `ComposeBannerStack.tsx:922` — single branch: *"A suggested edit couldn't be placed automatically — **its wording differs slightly from this document**. You can still edit and save."* | **Rendered JSX string** | **YES** | **DELETED** (§3) |
| **R-3** | `usePendingRedline.test.tsx:1184-1185` — `EditorHostHarness`, a test host that duplicated R-1/R-2 verbatim so the editor→host→banner path could be exercised without mounting `ComposeWorkspace`. | Rendered JSX string, **in test code** | No (test-only) | **DELETED** — rewritten to mirror the new copy. Left alone it would have been a live copy of the eliminated sentence sitting one refactor away from production, and would have made a future grep-based check report a false positive. |
| C-1 | `ComposeBannerStack.tsx:911` | Comment | No | Kept + extended — it now records WHY the branch went (§3.1). |
| C-2 | `ComposeEditor.tsx:2731` | Comment | No | Kept, rewritten to say the state is gone, not that it is produced. |
| C-3 | `ConversationPane.tsx:1673` (SpaarkeAi) | Comment | No | Kept — historical explanation of why `documentText` is annotated with paraIds. |
| C-4 | `Services/Ai/Context/ContextBinder.cs:367` | XML doc comment | No | Kept — **server, outside this task's file boundary**; it is documentation of the ADR-043 Amendment 1 rationale, not copy. |
| C-5 | `tests/integration/seam/Ai/ContextBinderActionRunnerSeamTests.cs:263` | Comment | No | Kept — same rationale. |
| C-6 | `.claude/adr/ADR-043`, `docs/adr/ADR-043`, `projects/**` notes, `spec.md`, `design.md`, `plan.md` | Prose | No | Kept — these are the historical record of the defect. Deleting them would erase the reason the fix exists. |

**Adjacent, deliberately NOT touched** — `infra/dataverse/actions/compose-revise-document.action.json`'s
systemPrompt contains *"…so it lands on exactly the paragraph you named even if your prose differs
slightly."* That is **model-facing instruction text asserting the OPPOSITE property** (the anchor makes
prose drift irrelevant). It is not a user-visible failure message and is not the FR-C07 state.

---

## 2. Pass 2 — every producer of the state the sentence described

The banner branch was reached whenever `usePendingRedline` set a `PendingRedlineError`. After tasks
051/052/053 that value has exactly **five** construction sites, all in
`src/client/shared/Spaarke.Compose.Components/src/widgets/hooks/usePendingRedline.ts`, and all five are
fed by exactly **one** function — `planAndApplyTargeted` — which has exactly **five** failure returns:

```
planAndApplyTargeted
├── anchored leg   (resolveAnchoredSpans !== null)
│   ├── P-1  not_found      · source 'anchored'       — the paraId/citation never resolved
│   ├── P-2  ambiguous      · source 'anchored'       — paraId and citation disagree, or a range citation
│   └── P-3  target_deleted · source 'anchored'       — the anchor resolved; the paragraph is gone
└── anchorless leg (resolveAnchoredSpans === null, classifyAnchorlessReplay !== null)
    ├── P-4  not_found      · source 'legacy-replay'  — the quoted prose is not in the document
    └── P-5  ambiguous      · source 'legacy-replay'  — the quoted prose occurs more than once
```

Surfaced by: `materialize`, `materializeMany` (batched, with `failedCount`/`totalCount`),
`applyStaleTargetAnyway`, `applyLegacyProposal`. All four hand the same `{kind, source}` through
unchanged, so the closed set of banner inputs is **3 kinds × 2 sources × {single, batched}** — the exact
matrix `ComposeBannerStack.test.tsx`'s `it.each` now asserts over.

### 2.1 Which producers a user can actually hit

| Producer | Who produces the payload | Live today? |
|---|---|---|
| P-1/P-2/P-3 | Every newly produced compose edit — the four EDIT Actions have emitted `target_para_id` since task 051 | **YES, this is the normal path** |
| P-4/P-5 | Only a `compose` ledger entry written **before** task 052's catalog change and replayed afterwards (§4) | Yes, for the life of those sessions |

**This is why the old copy was not merely vague but false.** Since task 051, the branch a user actually
reaches is P-1/P-2/P-3 — the anchored leg — and **on that leg no document text is ever compared**.
`resolveAnchoredSpans` resolves a `w14:paraId` or a citation through the reference map and returns; it
never calls `resolveTargetSpans`, a fact task 055's module-boundary tripwire asserts structurally (an
armed throwing double, the client twin of the server's `ThrowIfTextSearched`). Telling a user their
*wording* drifted, when nothing about wording was consulted, invented a cause and sent them to re-word a
clause that was never the problem.

---

## 3. Pass 3 — the pairing: what each producer renders now

### 3.1 The deleted branch

The single `else` that rendered R-1/R-2 served **P-1, P-2, P-3, P-4 and P-5 minus the two that had
already been split out** — one sentence for two unrelated causes. It is replaced by a switch on
`(kind, source)`:

| Producer | Rendered now |
|---|---|
| **P-1** `not_found` / `anchored` | *"This suggested edit named a paragraph or section this document doesn't have (**{target}**). Nothing was changed — select the passage you want and try again."* Batched: *"N of M suggested edits named a paragraph or section this document doesn't have…"* |
| **P-2** `ambiguous` / `anchored` | *"This suggested edit matches N places in the document. Select the exact passage and try again."* (pre-existing, already true) |
| **P-3** `target_deleted` / `anchored` | *"The text this suggestion referred to no longer exists."* (task 052, already true) |
| **P-4** `not_found` / `legacy-replay` | *"This suggestion came from an earlier session, before suggestions carried a paragraph reference, and the text it quoted is no longer in this document. Nothing was changed — re-run it to get a suggestion that points at a real paragraph."* |
| **P-5** `ambiguous` / `legacy-replay` | *"This suggestion came from an earlier session and quoted wording that appears in N places, so we won't guess which one it meant. Re-run it on the passage you want."* |

Each names the actual failure and offers a remedy the user can act on. P-1's remedy is a re-run against
a passage the user selects; P-4/P-5's is a re-run that produces an anchored suggestion.

### 3.2 Why the string cannot come back through the same door

`PendingRedlineError.source` is **required, not optional**. A future producer of an unresolved-target
error has to state which channel failed before it compiles, which is what keeps the two stories from
re-merging into one generic sentence. The `it.each` matrix in `ComposeBannerStack.test.tsx` walks the
closed set and asserts `not.toMatch(/wording differs/i)` **and** that the message is specific and
non-empty — so deleting the sentence and replacing it with something vacuous fails too.

### 3.3 Adjacent surfaces checked and cleared (they could have rendered a wording claim; none does)

| Surface | What it says on a placement failure | Verdict |
|---|---|---|
| `ComposeAiToolbar` review banner (`useAiApplyValidation`, R4 FR-07) | *"An AI suggestion needs review — {the target paragraph could no longer be found / the target position moved out of range / the target overlaps non-editable content / …}"* | No wording claim. Reasons are structural. **Clear.** |
| `importedRevisions.ts:341` | *"Imported {insertion|deletion} could not be placed automatically — {author}: …"* | This is an **imported Word revision**, not an AI edit — outside the AI edit flow, and it makes no wording claim. **Clear.** |
| `SAVE_DEGRADATION_COPY` (R7) | save-time fidelity codes | Different flow entirely. **Clear.** |
| `composeResultFormat.ts` (Assistant confirmation line) | renders the rationale + proposed text | Never reports placement outcomes. **Clear.** |
| `ComposeReanchorBanner` / `ComposeReanchorConflictPanel` | return-from-Word re-anchor | **The one place fuzzy matching legitimately speaks to the user.** Rewritten — §5. |
| `POST /api/compose/edit-batch/validate` (server) | — | The validator was **deleted by task 052**, and the endpoint had **zero client callers** (task-052 notes F-1). Nothing server-side can produce this copy. **Clear.** |

---

## 4. The remaining anchorless population — enumerated, since FR-C06 depends on it

FR-C06's fallback is only justified if a genuinely anchorless source exists. It does, it is bounded, and
it is the only producer of P-4/P-5:

| Question | Answer |
|---|---|
| **Which entries?** | `compose`-disposition `SessionOutput` rows whose payload carries `target_text` and **no** `target_para_id` / `target_ref`. Produced by `compose-draft-alternative`, `compose-make-concise`, `compose-rewrite-instruction` and `compose-revise-document`'s `edits[]` under the **pre-task-052** output schemas. |
| **Written when?** | Any compose edit dispatched before task 052's catalog rows reach the environment. R7 shipped and was **deployed to dev on 2026-08-18** with the old schema live (`spaarkeai-compose-r7/notes/uat-issues.md`), and UAT-06/21/24 are user reports of those very edits. Task 051 added `target_para_id`, so entries written between the 051 and 052 deploys carry BOTH and take the anchored leg. |
| **Reachable how?** | `ComposeWorkspace.materializeComposeDraftFromLedger()` re-reads `GET /api/ai/chat/sessions/{id}/compose-outputs` on **every document open, refresh, and Flow-5 `compose_assistant_insert` signal**, picks the head edit output, and calls `materialize` / `materializeMany`. **Every** payload that reaches the redline hook comes from the ledger — there is no direct-from-SSE path (ADR-040 storage-precedes-rendering). A pre-052 head is therefore replayed against the post-052 client. |
| **For how long?** | The session's retention: Cosmos `sessions` container `DefaultTimeToLive = 7776000` (**90 days**) for unfiled sessions, **indefinite** for a filed one (`StoredSession.Ttl == -1`). So the population shrinks but does not vanish on a fixed date. |
| **Can a NEW edit be anchorless?** | Not with prose. The four Actions no longer declare `target_text`, so a post-052 payload cannot carry any. See §4.1 for the one residual shape and why it is out of scope. |

### 4.1 Residual, surfaced not fixed (out of this task's scope)

A post-052 EDIT payload can carry `target_para_id: null` (Structured Outputs requires the KEY; the model
nulls it when the caller supplied no anchor — e.g. a selection in a paragraph with no stamped `paraId`).
That payload has no anchor **and no prose**, so `classifyAnchorlessReplay` returns `null` and
`planAndApplyTargeted` returns `null`; the caller then takes the **insertion-at-cursor** branch and
reports `applied`.

The catalog promises the model something different — *"An EDIT with a null identifier is REFUSED rather
than placed — there is no prose fallback"* — so client and catalog disagree. This is **not** a UAT-21
mis-placement (nothing is struck; a pending insertion appears at the user's own caret and is
accept/rejectable), and the bounded fallback cannot help it (there is nothing to match on). Changing the
insertion-at-cursor branch would also affect `compose-draft-document` and `compose_context_insert`, which
legitimately insert at the caret.

**Surfaced for the owner, not silently changed** (root CLAUDE.md §6 — scope). The distinguishing test, if
it is taken up, is `Object.prototype.hasOwnProperty.call(payload, 'target_para_id')`: key present + null
⇒ an edit that declined to anchor ⇒ refuse; key absent ⇒ not an edit-shaped payload ⇒ insert as today.

---

## 5. FR-C07 step 5 — the return-from-Word message

Where the message **does** still belong, it must be specific. `AnnotationReanchorService` is the
ADR-sanctioned fuzzy case: Word regenerates `w14:paraId`s on save (Open-XML-SDK #925), so anchors from an
externally edited document genuinely have to be re-located by similarity.

| | Before | After |
|---|---|---|
| Attention needed | *"Document updated in Word — 2 re-anchored, 1 need review, 1 orphaned"* | *"**This document was edited in Word** — 2 comments **re-attached to their paragraphs**; 1 need review and 1 couldn't be re-attached"* |
| All clear | *"Document updated in Word — 2 re-anchored, all anchors kept"* | *"This document was edited in Word — 2 comments re-attached to their paragraphs, and nothing needs your attention"* |
| Mixed kinds | *"3 re-anchored"* | *"1 comment and 2 tracked changes re-attached to their paragraphs"* |

Three properties, in order of importance:

1. **It names the CAUSE.** "This document was edited in Word" is why anchors moved at all.
2. **It names WHAT re-attached**, by kind — from `ReanchoredAnnotation.type`, which was **already on the
   wire** and already mirrored in `ComposeReanchor.types.ts`. Nothing new is asked of the server; this is
   project invariant (7) — *deterministic information available at capture time MUST be carried, not
   re-derived* — applied to a sentence.
3. **`AnnotationReanchorService`'s BEHAVIOUR is untouched** (POML negative criterion). Bands, thresholds,
   the Spike-6 ambiguity guard and the never-silently-drop rule are byte-identical; `git diff` on
   `src/server/api/Sprk.Bff.Api/Services/Compose/AnnotationReanchorService.cs` is empty. The composition
   happens client-side in `ComposeReanchorBanner.summarize`.

A summary that arrives without per-annotation detail falls back to the bare count — less specific, still
true, never a fabricated composition.

---

## 6. Verification

| Check | Result |
|---|---|
| `grep -rniE "wording differs\|differs slightly" src/` | 14 hits — **0 are rendered strings**: 5 comments, 7 test assertions/headers proving absence, 2 doc-comment references (`ContextBinder.cs`, `ConversationPane.tsx`). |
| `ComposeBannerStack.test.tsx` closed-set matrix (3 kinds × 2 sources × single/batched = 12 cases) | 12/12 assert the sentence is absent AND the message is specific |
| Mutation M6 — restore the deleted sentence in the `anchored`/`not_found` branch | **2 tests fail** (matrix case + the anchored-specificity test) → the assertion has power |
| Mutation M5 — drop kind detail from the re-anchor banner | **3 tests fail** → the specificity requirement is enforced, not decorative |
| `AnnotationReanchorService.cs` unchanged | `git diff --stat` empty |

---

## 7. Honest residue

- **§4.1** — the `target_para_id: null` post-052 payload lands as an insertion at the caret rather than a
  refusal, contradicting the catalog's own promise to the model. Surfaced, not fixed; scope call is the
  owner's.
- **The state is eliminated, not the failure.** An AI edit can still fail to place — a deleted clause, an
  unknown citation, a replayed entry whose prose has moved on. FR-C07's bar is that the user is never
  told a false reason for it, and never given a remedy that cannot work. That is what §3.1 delivers.
- **No `.claude/` file was modified** (root CLAUDE.md §3). Proposed `.claude/CHANGELOG.md` text is in the
  task's final report for the main session to apply.
