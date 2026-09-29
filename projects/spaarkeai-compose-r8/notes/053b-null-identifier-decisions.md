# Task 053b — the null identifier: the presence-vs-truthiness audit + decisions

> **Task**: `053b-null-identifier-refusal.poml` · **Rigor**: FULL · opus @ xhigh · **Date**: 2026-08-25
> **Owner's bar, verbatim (2026-08-25)**: *"whatever ensures the document updates and saves — so
> whatever that takes."*
> **Predecessors**: task 052 (text-search demotion) · task 053 (bounded confirmable fallback,
> `notes/wording-differs-elimination-trace.md` §4.1, which SURFACED this residual rather than fixing it).

---

## 0. What this task is, and what it deliberately is not

The catalog tells the model, verbatim:

> *"Set target_para_id to null ONLY when you genuinely cannot identify the paragraph. An EDIT with a
> null identifier is REFUSED rather than placed — there is no prose fallback — so a missing identifier
> costs you the edit."*

The client did the opposite. Structured Outputs requires the `target_para_id` KEY, so "I could not
identify it" arrives as an explicit `null`; task 052 removed `target_text` from the edit channel, so
that payload had **no anchor and no prose**; it failed `classifyAnchorlessReplay`, fell out of
`planAndApplyTargeted` as `null`, and the caller ran its **insertion-at-cursor** branch and returned
**`applied`**. A revised indemnity clause could land in the recitals and the status told the user it
succeeded.

**This task is NOT "add the refusal the catalog promised."** A bare refusal satisfies the catalog's
wording and fails the owner: the user asked for a change and would get nothing. The fix routes the case
into task 053's **propose-then-confirm** machinery, so the user places it themselves and it applies and
saves normally. §6 records the one place where the catalog's wording and the shipped behaviour now
differ, and why that is the right way round.

---

## 1. The audit — every site that reads `target_para_id`, with a verdict each

Scope: repo-wide over `src/**` (`.ts`/`.tsx`), production code, both packages in this task's file
boundary plus the two adjacent ones checked and cleared. "Conflates" = treats a falsy value as
equivalent to an absent key.

| # | Site | What it does with a falsy `target_para_id` | Conflates? | Verdict |
|---|---|---|---|---|
| **A-1** | `usePendingRedline.planAndApplyTargeted` → the anchorless branch (`usePendingRedline.ts`) | Fell through to insertion-at-cursor and returned `applied` | **YES — this is the defect** | **CHANGED.** Now asks `classifyUnidentifiedTarget` (key presence) before the insertion branch. |
| **A-2** | `anchorlessReplayFallback.classifyAnchorlessReplay` (`:133`) | `typeof … === 'string' && trim().length > 0` ⇒ a null is "no anchor" ⇒ falls through to the prose check | No — and correctly so | **UNCHANGED.** This function asks *"is there a USABLE anchor?"*, and null and absent give the same honest answer to that question. Distinguishing them here would put the discrimination in the wrong function: this one's job is BOUND 1, not routing. |
| **A-3** | `usePendingRedline.resolveAnchoredSpans` (`:403`) | Passes the value to `resolveAnchorParaIds` | No | **UNCHANGED** (type widened to `string \| null` — see §3). Same reasoning as A-2: it answers "which paragraph does this NAME", and a null names none. |
| **A-4** | `composeAnchorResolution.resolveAnchorParaIds` (`:93`) | `anchor?.paraId?.trim()` ⇒ null becomes `kind: 'none'` | No | **UNCHANGED** (type widened). `'none'` is documented as *"no anchor was supplied at all, which is the caller's signal that a legacy text path is permitted"* — and deciding **what to do** about a null that was *asked for* is a per-consumer policy that deliberately does not live in the shared precedence module. Three consumers, three correct-but-different policies (A-1 proposes, A-5/A-6 fall back to prose). |
| **A-5** | `usePendingRedline.unplaceableLabel` (`:449`) — the site the POML named as the audit's starting point | `if (payload?.target_para_id) return \`paragraph …\`` ⇒ a null yields `''` ⇒ the banner says *"no target given"* | Yes, **benignly** | **UNCHANGED** (type widened + rationale comment added). This function asks *"what did the suggestion NAME?"* — a null named nothing, so there is no string to put in the sentence and `''` is the correct answer for both. Nothing downstream distinguishes them, and nothing should: by the time a label is needed, the placement decision was made elsewhere. **After 053b a null-identifier edit no longer reaches this function at all** (it becomes a proposal, not an error). |
| **A-6** | `ComposeWorkspace.registerAiReviewComments` (`:2917`) — `comments[]`, the `flag-risks` output | `{ paraId: c?.target_para_id }` → resolution fails → the flag keeps its `target_text` prose anchor | Yes, **correctly** | **UNCHANGED** (type widened). The catalog schema itself defines this: a null flag *"hangs on target_text within the document (an ANNOTATION anchor, not an edit placement — the role ADR-049 I-7 leaves intact)."* An annotation is not a placement; nothing lands in the wrong place, so there is nothing to discriminate. |
| **A-7** | `composeResultFormat.formatDraftAlternativeResult` (`:147`, SpaarkeAi) | `asNonEmptyString(record.target_para_id)` ⇒ a null-identifier edit matched **no** formatter, fell through to the ` ```json ` fence | **YES — a real, separate defect** | **CHANGED.** The shape test is now key PRESENCE. Before: the user was shown raw JSON for exactly the edit that most needed explaining. |

### 1.1 Adjacent sites checked and cleared (they read a target field but cannot conflate)

| Site | Why it is clear |
|---|---|
| `ComposeWorkspace.registerAiEditReasonComment` (`:2857`) | Reads `target_text` only, and skips when there is no span to anchor a rationale comment to. It never reads `target_para_id`, so it cannot conflate — and it is an annotation, not a placement. (Separately: post-052 it is dead for NEW edits because no `target_text` is emitted. Pre-existing, unrelated to this task, not touched.) |
| `ComposeEditor.resolveDeterministicAnchorSpan` / `placeAdvisoryComments` | Consumes `AdvisoryCommentInput.paraId`, sourced from review `sectionRef`/`quotedText` — never from an edit's `target_para_id`. |
| `useAiApplyValidation` (`:170`) | `op.targetParaId` is the `mergeParagraph` OPERATION's surviving-paragraph id (`compose-operations.ts`), a different field with a different vocabulary. Not the edit payload. |
| `ConversationPane.dispatchReviseDocument` (SpaarkeAi) | Supplies `documentText` annotated with the closed paraId set. Produces the input, never reads the output's target field. |
| `ComposeAiToolbar` / `useAiGenerateBookmark` (`:792`, `:2999`, `:3092`) | The `targetParaId` **input slot**, sent only when `bookmarkContext.paraId` exists. This is the PRODUCER of the null: an unstamped paragraph means no slot, which means the model has nothing to copy. Correct as-is — and it is why the user's selection IS the intended paragraph in the dominant case (§2). |

### 1.2 Sites outside this task's file boundary — surfaced, not touched

`ProposedEdit.TargetParaId` and the server anchor pass live under `src/server/**`, which this task
must not touch (a parallel agent is deleting files under `Services/Compose/`). Nothing was needed
there: `POST /api/compose/edit-batch/validate` has **zero client callers** (task-052 notes F-1), so the
server has no reachable behaviour on this path.

---

## 2. How the null case now reaches the document

```
payload { target_para_id: null, new_text: "…" }
   │
   ├─ resolveAnchoredSpans → null        (no USABLE anchor — A-3/A-4 unchanged)
   ├─ classifyAnchorlessReplay → null    (no prose — A-2 unchanged)
   ├─ classifyUnidentifiedTarget → MINTED  ◄── the new discriminator: KEY PRESENCE
   │        └─ key ABSENT here would return null ⇒ insertion-at-cursor, exactly as before
   │
   └─ status 'proposed' + PendingRedlineLegacyProposal { reason: 'unidentified-target', … }
            │  NOTHING is in the document. No mark, no text, no `applied`, no error banner.
            │
            ├─ ConfirmModal (task 053's, branched copy — no second modal, no second answer path)
            │     "The assistant couldn't identify which paragraph to change. Nothing has been
            │      changed — you can place it yourself."
            │     confirm: "Replace my selection" | "Insert at my cursor"     cancel: "Skip"
            │
            ├─ confirm → applyLegacyProposal() → planAndApplyTargeted(confirmed:'unidentified-target',
            │            intendedRange) → applyRedlineSpans → a NORMAL pending redline
            │            → accept/reject → SAVE (proved through the save path, §4)
            │            → the host writes the FR-17 supersession (O-2 durability, unchanged)
            └─ skip   → nothing placed; host writes the same durable resolution
```

**Where "the paragraph they intend" comes from.** The placement target is the user's own selection
snapshot — the same `intendedFrom`/`intendedTo` the insertion branch already used, captured before
supersession mutates anything and remapped through the strips. In the dominant real case this is
exact: the AI toolbar acts on a highlighted clause, and `targetParaId` is omitted from the dispatch
precisely when that clause has no stamped `w14:paraId` (`ComposeAiToolbar.tsx:792`) — so the null
identifier and the user's selection are two views of the same paragraph. With only a caret (a ledger
replay at document open), the proposal says so plainly, names the paragraph it would land in, and
offers a skip.

---

## 3. Decisions taken during execution (each surfaced, none silent)

1. **The discriminator is `Object.prototype.hasOwnProperty` + a not-`undefined` check, not truthiness.**
   `undefined` is excluded deliberately: `JSON.parse` can never produce a key whose value is
   `undefined`, so the only source is a TypeScript caller spelling "absent" as
   `{ target_para_id: undefined }`. Treating that as a declined anchor would convert an insertion
   consumer's optional-field idiom into a confirmation prompt — the exact regression this task must not
   cause. Pinned by test and by mutation M-2.

2. **An empty or whitespace-only identifier is treated as DECLINED, identically to null.** The model was
   asked for an address and returned something that cannot address a paragraph; the failure mode is
   identical, so the handling is. No genuine insertion consumer sends an empty target key (Flow-3 sends
   `{ new_text }`; `compose-draft-document`'s schema has no target field at all), so this costs nothing.

3. **`new_text` empty ⇒ NOT a proposal.** This guard is load-bearing, not defensive: an **empty
   superseding compose entry is the FR-17 RETRACTION**, and a retraction that carried a null identifier
   would otherwise have become "where should this go?" with nothing to place. Asserted by test
   (`… is still a RETRACTION, not a question`).

4. **The payload types were widened to `string | null`** on `ComposeDraftPayload`, `ComposeDraftEdit`
   and `ComposeDraftComment`, and on `AnchorRequest`. This is the WIRE shape, not defensiveness — the
   four Action output schemas declare `["string","null"]` and list the key in `required`. Leaving the
   type at `string` would have forced every test to cast, i.e. would have made the type system state
   the opposite of the contract.

5. **No paragraph PICKER was built.** The POML's *"or by picking the paragraph"* was considered and
   rejected on three grounds: (a) a `ConfirmModal` is `dismiss="alert"`, so the user cannot select in
   the document while it is open — a picker would have to be a list INSIDE the modal; (b) a 40-page
   agreement's paragraph list in an `xs` modal is not usable; (c) it is new UI surface for a case whose
   dominant instance already has the answer (the user's selection). The escape for a wrong caret
   placement is stated in the modal itself and costs nothing: skip, select the passage, ask again.

6. **`ComposeBannerStack.tsx` was NOT changed**, though the POML listed it as a primary-edit file. The
   null case now produces a **question**, never a `PendingRedlineError`, so no banner branch fires and
   no third `PendingRedlineError.source` value was introduced. Adding one would have widened task 053's
   closed `3 kinds × 2 sources × {single,batched}` banner matrix for a state that cannot occur. The
   `'no target given'` fallback in the anchored `not_found` copy is now unreachable **from the edit
   path**; it was left in place as the honest rendering of a payload that names nothing in any
   vocabulary, rather than deleted for tidiness.

7. **One confirmation surface, two reasons — not two paths (root §11).** The proposal type gained a
   required `reason` discriminant (the same device `PendingRedlineError.source` uses, and for the same
   reason: the host switches on it, and an absent value would silently pick one of two stories). The
   answer buttons, the deferred queue, the durable FR-17 supersession write and the `ConfirmModal`
   instance are all task 053's, unchanged.

8. **The answer is carried PER DEFERRED EDIT (`DeferredEdit.question`), not inferred from the banner.**
   A batch can in principle hold both anchorless kinds (a mixed-vintage ledger entry). Passing one
   blanket `confirmed` value would replay the other kind un-confirmed. Asserted by the mixed-batch test.
   The *copy* still names the first held-back edit's reason; that is the one place a mixed batch is
   imprecise, and it is imprecise about CAUSE, never about PLACEMENT.

9. **Held positions are rebased on the editor's own transaction `Mapping`.** The unidentified proposal
   is the first thing this hook holds that is addressed by POSITION rather than by paraId or prose. In
   `materializeMany` the pass places other edits between the question and the answer, so an unremapped
   position would confirm into somewhere the user was never shown — a silent mis-placement with a
   dialog in front of it. The primitive is the one already in the editor (`stripRedlineMarks`,
   `RebasedOperationLog`, `useAiGenerateBookmark` all rebase with it); no second position system was
   introduced. Load-bearing, proved by mutations M-3 and M-4.

10. **A-7 (the SpaarkeAi formatter) was fixed rather than only recorded.** It is inside this task's file
    boundary, it is the same presence-vs-truthiness bug, and its user-visible effect — a raw JSON fence
    instead of prose — lands on precisely the payload this task is about.

---

## 4. Proof the confirmed proposal APPLIES **and SAVES**

The owner's bar is the document, not the mark, so the assertion runs the real client save path rather
than inspecting editor marks:

| Claim | How it is asserted |
|---|---|
| It applies as a normal pending redline | `collectMarkedRanges(editor,'deletion'\|'insertion','b1@t1')` non-empty; `pending` has the entry; still accept/rejectable |
| **It reaches the SAVED payload** | `buildImportedContentModel(editor, loadedModel, snapshot, { trackChanges: true })` — the merged `ComposeContentModel` `ComposeWorkspace` POSTs to `/api/compose/documents/{id}/save` — contains the user's text, carried as an `Inserted` **revision run** |
| It survives Accept | after `accept(ledgerRef)`, the model's **accept-state** text (all runs except `Deleted`) still contains it. Read in accept state because R6's render-on-save legitimately INTERLEAVES Deleted+Inserted runs — the raw concatenation reads as both versions at once, which is the tracked change being persisted, not a defect |
| It does not corrupt untouched blocks (project invariant 2) | the untouched clauses are still present verbatim in the same model |
| The caret variant saves too | same assertions on an `insert-at-cursor` confirmation; `hasDeletion === false`, no deletion mark — identical to the pre-053b caret branch, except a human said yes first |
| The born-in-editor save shape carries it | `buildContentModel(editor)` after Accept (reject-state parity means a PENDING insertion is excluded by design, so this asserts the accepted text — which is what that shape persists) |

Both save-path tests were **strengthened after mutation M-1 showed them passing vacuously**: the defect
also put the text at the caret, so a save assertion alone proved nothing. They now assert `'proposed'`
and an empty document *before* confirming, so what they prove is the confirmation, not the insertion.

---

## 5. Bounds and negatives re-asserted

| Bound | How it is held now |
|---|---|
| **BOUND 1** — an anchored edit cannot be minted into the fallback's argument type | Unchanged, and the new mint enforces it identically (a usable `target_para_id` or `target_ref` ⇒ `null`). Proved end-to-end with the module-boundary **tripwire ARMED** — a claim about the ROUTE taken, not about where the text landed. |
| **BOUND 2** — the fallback module has no `applied` outcome | Unchanged. The new leg adds **no outcome type at all**: there is nothing to resolve, so there is nothing to add an `applied` member to. The classifier states a fact; the caller places, behind `confirmed: 'unidentified-target'`, which only a click produces. |
| No auto-apply path added | `NEGATIVE: no sequence of hook calls places it without the confirmation` — re-materialize, then reach for the OTHER question's answer, then accept/reject. Document untouched. |
| No text search reintroduced (ADR-049 I-7) | Tripwire silent across propose AND confirm. The leg reads document text only to DESCRIBE a position the user supplied; it never scans for one. |
| No second confirmation path or modal shell | Same `ConfirmModal`, same `applyLegacyProposal`/`dismissLegacyProposal`, same `resolveRedlineLegacyProposal` durable write. Only the copy branches. |
| Genuine insertion consumers unchanged | Four tests, in the shapes those consumers actually send: absent key; the literal Flow-3 `{ new_text: html }` payload; an explicitly-`undefined` key; and the empty-`new_text` retraction. |

---

## 6. The one honest divergence: the catalog still says "REFUSED"

The catalog prompt (all four EDIT Actions + `compose-revise-document`'s `edits[]`) tells the model that
a null identifier **costs it the edit**. After this task it costs the model nothing automatic — the
edit becomes a question the user answers.

**This is deliberate and the divergence points the right way.** The sentence's job is to make the model
*try harder to supply a real identifier*; that incentive is unchanged and still true from the model's
point of view (a null identifier means the edit does not place itself). Weakening it to "a null
identifier means the user gets asked" would tell the model that nulling the field is a cheap, safe
option — which is precisely the behaviour the sentence exists to suppress. The user-facing behaviour is
the owner's, and the model-facing incentive stays strict.

**Recommended (owner's call, catalog files are outside this task's boundary):** leave the prompt text
as is. If it is ever revised, the honest wording is *"a null identifier costs you the placement — the
user must place it by hand"*, which keeps the incentive while removing the word the client no longer
implements literally. **No `infra/dataverse/**` file was modified by this task.**

---

## 7. Verification

| Check | Before | After |
|---|---|---|
| `Spaarke.Compose.Components` | 101 suites / 1,273 tests | **102 suites / 1,298 tests, all green** (+1 suite `usePendingRedline.nullIdentifier.test.tsx`, +25 tests) |
| `SpaarkeAi` | 121 suites / 1,119 tests | **121 suites / 1,121 tests, all green** (+2 tests in `composeResultFormat.test.ts`) |
| `npx tsc --noEmit` (compose pkg) | 9 pre-existing errors (`@spaarke/ai-widgets` has no `dist`) | **9 — zero new** (5 × TS2307 ai-widgets + 4 × TS7006, the identical baseline set) |
| KEEP files unmodified | — | `docxBridge.ts`, `redlineTextSearch.ts`, `ComposeBannerStack.tsx` — absent from `git status`, verified |
| Files outside the boundary | — | none. `src/server/**` + `tests/**` changes present in the worktree belong to the CONCURRENT task-064 agent (edit-batch retirement), not to this task |

### 7.1 Mutation proofs (every mutation reverted immediately; `grep -c MUTATION` = 0 after each)

The implementation preceded the tests, so test power was established by **mutation**, not by observing
a red run — stated here rather than implied.

| # | Mutation | Tests that failed | What it proves |
|---|---|---|---|
| **M-1** | `classifyUnidentifiedTarget` always returns `null` (exact pre-053b behaviour) | **9** — incl. *"PROPOSES over the passage the user selected"*, *"with only a caret"*, the save-path test, both batched tests | The defect is what the suite is actually about; without the classifier the null case is `applied` again |
| **M-2** | `declaredButEmpty` drops the `hasOwnProperty` + `undefined` checks (truthiness, not presence) | **7** — incl. **all four** insertion-consumer regression tests | The *other* direction: a truthiness discriminator breaks `compose-draft-document` and `compose_context_insert`. This is the constraint the POML called "the thing most likely to break" |
| **M-3** | the transaction rebaser becomes a no-op | **1** — the batched confirm-at-the-promised-passage test | Position rebasing is load-bearing, not decorative |
| **M-4** | `applyLegacyProposal` stops passing `intendedRange` (falls back to the live selection at confirm time) | **2** — both batched tests | The confirm places where the PROPOSAL promised, not where the last placement left the caret |
| **M-5** | the SpaarkeAi detector reverts to `asNonEmptyString` | **1** — the null-identifier rendering test | A-7's fix is real and covered |

### 7.2 What verification caught that the implementation did not

- **Two save-path tests passed vacuously.** M-1 revealed that *"a confirmed caret insertion also reaches
  the saved model"* and *"a born-in-editor save also carries it once accepted"* were green under the
  DEFECT — because the defect also put the text at the caret. Both were strengthened to assert
  `'proposed'` + an empty document before confirming. Without the mutation run they would have shipped
  as decoration. (This is the fourth time in this project a check has been found to pass for the wrong
  reason; running the mutation suite over *new* tests, not only over the ones the POML names, is worth
  keeping as a default.)
- **The accept-state reading of the saved model.** The first save assertion failed because
  `buildImportedContentModel` interleaves Deleted+Inserted runs for an edited paragraph. That is
  correct behaviour, and the naive `modelText().toContain(...)` assertion was measuring the wrong
  thing; `settledText()` (runs minus `Deleted`) is the lens that matches the claim being made.

---

## 8. Files changed

| File | Change |
|---|---|
| `src/client/shared/Spaarke.Compose.Components/src/widgets/hooks/anchorlessReplayFallback.ts` | `classifyUnidentifiedTarget` + its brand, candidate type, `declaredButEmpty` discriminator; header extended to record that two disjoint anchorless legs now live here and that both bounds survive |
| `.../src/widgets/hooks/usePendingRedline.ts` | the third resolution leg in `planAndApplyTargeted`; `intendedRange` argument + `clampToDoc`; `PendingRedlineLegacyProposal.reason`/`proposedText`/`placement`/`contextText`; `ConfirmedQuestion` + `DeferredEdit.question`/`intendedRange`; the transaction rebaser effect; widened anchor param types |
| `.../src/widgets/composeAnchorResolution.ts` | `AnchorRequest.paraId`/`ref` widened to `string \| null` (wire shape) + rationale |
| `.../src/widgets/ComposeEditor.tsx` | `target_para_id` widened to `string \| null` on the three payload interfaces + rationale |
| `.../src/widgets/ComposeWorkspace.tsx` | the FR-C06 `ConfirmModal`'s title / message / confirm-label branch on `reason` (same modal, same answer path) |
| `.../src/widgets/hooks/usePendingRedline.nullIdentifier.test.tsx` | **NEW** — 25 tests |
| `src/solutions/SpaarkeAi/src/components/conversation/composeResultFormat.ts` | A-7: the draft-alternative shape test is key presence |
| `.../__tests__/composeResultFormat.test.ts` | +2 tests |

**Not touched**: `src/server/**`, `tests/**`, `infra/dataverse/**`, `docxBridge.ts`,
`redlineTextSearch.ts`, `ComposeBannerStack.tsx`, anything under `.claude/`,
`projects/**/tasks/TASK-INDEX.md`, `projects/**/current-task.md`.

---

## 9. Proposed `.claude/CHANGELOG.md` entry (main session to apply — root CLAUDE.md §3)

```
### 2026-08-25 — spaarkeai-compose-r8 task 053b (null-identifier placement)

An AI edit whose `target_para_id` arrives explicitly NULL no longer inserts at the caret and reports
`applied`. It is PROPOSED through task 053's existing confirm surface with an honest reason ("the
assistant couldn't identify which paragraph to change") and placed at the passage the user selected
once they confirm — so the change still reaches the document and saves (owner bar, 2026-08-25).
The discriminator is key PRESENCE (`hasOwnProperty` + not-`undefined`), never truthiness: an ABSENT
key is a genuine insertion consumer (`compose-draft-document`, `compose_context_insert`) and is
unchanged. Task 053's two structural bounds are intact — no new `applied` outcome, no route from an
anchored edit into the fallback, no text search.
```
