# Task 054 — where the whole-document closed set is enumerated, and why

> **Status**: decision recorded, implementation follows in the same change
> **Task**: `tasks/054-whole-document-closed-set-supply.poml` (FR-C03, supply half)
> **Decision**: **client-side, from the Compose editor pane, as an id-annotated `documentText` operand**
> **ADR disposition**: **Path C — comply.** No §6.5 escalation. Rides ADR-043 Amendment 1 unchanged.

---

## 1. The trace, link by link

Task 051 closed the selection-scoped chain. This is the same chain for the WHOLE-DOCUMENT pass
(`compose-revise-document`), traced before writing any code — the 051 lesson.

| # | Link | State before this task |
|---|---|---|
| L1 | A paragraph set is captured | ❌ never — `ConversationPane` holds no paraId map |
| L2 | It is sent | ❌ args are `{revisionIntent, instruction?}` only |
| L3 | Args reach `ContextBinder` | ❌ **the file-operand path passes NO `Args` and NO `InputSchemaJson`** |
| L4 | Declared companions render | ❌ `ResolveOperand` branch (4) returns `OperandChannel.Document`; `CollectDeclaredCompanions` is never called |
| L5 | The Action declares the input | ❌ `.action.json` has no `inputSchema`; the mirror under `infra/dataverse/inputschemas/` is read by **no deploy script** |
| L6 | The model can answer with an id | ❌ `edits[]` / `comments[]` have no `target_para_id` |
| L7 | The server refuses an out-of-set id | ✅ `ComposeAnchorResolver` + `ComposeEditAnchorPass` (051) |
| L8 | The client places by anchor | ✅ `resolveAnchoredSpans` (051) — task 055 verifies it for a whole-document payload |

**Six dark links, not one.** Evidence for the three that matter:

- **L3** — `SessionDispatchOrchestrator.ResolveFileOperandAsync` builds its `ContextBindingRequest` with
  `FileDocument` and the host/session fields only. `Args` and `InputSchemaJson` are absent by
  construction, so nothing the caller sent can reach the prompt on this path.
- **L3 fires because of the branch test.** `HasStructuredOperand` is true only for a top-level
  `ledger_resolution`, or a top-level `selectionText`/`changesText`/`documentText`
  (`TryFindDeclaredOperandField` uses `args.TryGetProperty` — top level, no descent into `slots`).
  The wire args for this dispatch are `{revisionIntent, instruction?}` — `dispatchConsumer.ts:664` sends
  `body: { bindingId, args: args?.slots ?? {} }`, so `slots` is unwrapped and `sessionIdOverride` is
  consumed client-side and never sent. Neither test matches ⇒ the file path is taken.
- **L5** — `Deploy-AnalysisAction.ps1` is the only writer of `sprk_inputschema`, and it reads
  `$action.inputSchema` from `infra/dataverse/actions/*.action.json`. Nothing reads
  `infra/dataverse/inputschemas/*.json`; that directory is CI-validated authoring only.

### Two findings this trace produced that were not in the task

**(1) `revisionIntent` does not reach the model today.** It is dropped at L3 with everything else. The
Action's systemPrompt branches four ways on it ("INSTRUCTIONS BY INTENT"), and none of those branches can
be selected — the model receives the document and the instructions for all four intents, and picks. So
`flag-risks` (contractually comments-only, empty `edits`) is not reliably distinguishable from
`improve-clarity` today. Task 055's entire deliverable is the `comments[]` half of this payload, so this
is a dependency, not an aside.

**(2) The model is shown a different text than the one the edit is placed into.** The file-operand path
resolves text through `ISessionFileTextSource` — the RAG-indexed extraction. Placement happens against the
**editor's** blocks. The Action requires `target_text` to be "an EXACT, VERBATIM substring of
documentText", but the documentText it is quoting from is not the text being searched. This is a
structural cause of whole-document mismatch that is independent of the model's lossiness, and no amount
of prompt tuning fixes it.

---

## 2. The decision

**The closed set is not a separate list. It is the document text the model reads, annotated with its
paraIds — and the Compose editor pane supplies it as the `documentText` operand.**

### Why not a separate enumerated list (the shape the task assumed)

The model has to answer "which paragraph do I mean?" with an id. If the ids arrive in a list *beside* an
unannotated document, the model has to match a paragraph it read to an entry in that list — by number, by
heading, or by quoting its first words. **That is prose matching, moved rather than removed**, and it
re-introduces exactly the lossy generation step Track C exists to delete. Ids must appear where the
content is read, so that naming one is a **copy** rather than a **generation** — the 051 principle.

The size arithmetic agrees. Annotating costs ~12 chars per paragraph (+5–6% on the text). A parallel
`ParaReferenceMapEntry` list costs ~100–150 bytes per entry, which on a large document exceeds
`ContextBinder`'s 32 KB `MaxDeclaredCompanionChars` — and an oversize companion is **skipped and logged**,
which would silently present an incomplete set to the model as complete. The annotated form is ~10×
cheaper and cannot hit that cliff.

### Why client-side, not server-side

The task framed this as server (`ChatSession.ReferenceMap`) vs client (`ConversationPane`), and expected
the server to win on trust. The trace inverts it, on grounds that outrank convenience:

1. **Completeness — the decisive one.** A "closed set" that is missing a paragraph is a contradiction: the
   model gets refused on an id that genuinely exists (the task's own second escalation trigger).
   `ChatSession.ReferenceMap` is a **Load-time snapshot**. Paragraphs the user has typed since Load carry
   new paraIds that are not in it, and the server cannot see unsaved editor state at all. Only the live
   editor holds the complete current set.
2. **`ReferenceMap` cannot produce the annotated form anyway.** `ParaReferenceMapEntry` is
   `(ParaId, ComputedNumber, NumberingLevel, ListPath, HeadingLevel)` — **it carries no text**. Building
   annotated text server-side means re-projecting the document, which is a **re-derivation of information
   already in hand at capture time** — forbidden by project invariant 7, the rule beneath three of R8's
   four root causes.
3. **It collapses finding (2) above.** Supplying the editor's own projection makes the text the model
   quotes from and the text the edit lands in the same text. The server path leaves them different.
4. **The trust objection does not survive contact with the placement path.** Placement is client-side by
   construction: `resolveAnchoredSpans` resolves the returned id against the **live editor blocks**, and an
   id that does not resolve REFUSES rather than falling back to a search (051 / UAT-21). The authoritative
   closed set at placement time is therefore the live editor either way. Supplying the model that same set
   is strictly more correct than supplying a server snapshot that placement will then disagree with.
5. **The hand-off already exists.** The task costed a cross-pane hand-off as a reason to prefer the server.
   It is already built and already carries this exact dispatch's other prerequisite — `ConversationPane`
   waits on the Compose pane for `documentSessionId` (`documentSessionWaiter.ts`, DEF-09). This extends a
   shipped seam rather than opening one.

### What it costs, stated plainly

- A SpaarkeAi touch (hot path). The task authorizes this explicitly — "make them HERE; there is no
  hand-off to another project". `/conflict-check` run; `projects/INDEX.md` already declares SpaarkeAi=Y.
- Document text on the wire, bounded by the Action's declared `maxLength` (60,000).
- The server no longer sees a RAG-extract for this one dispatch. That is the point, not a regression —
  and it is scoped to the whole-document Compose revise, which is the only dispatch that changes shape.

---

## 3. Why this needs no new mechanism (the ADR argument)

Supplying `documentText` in the dispatch slots moves this dispatch from the file-operand path onto the
**structured-operand path that already works**:

- `documentText` is already in the closed `OperandVocabulary` — no fourth entry, no operand displaced
  (the task's NEGATIVE criteria).
- `revisionIntent` and `instruction` become **declared companions** and reach the model through
  ADR-043 Amendment 1 exactly as `targetParaId` does for the selection Actions — the amendment was
  deliberately written generically rather than as a paraId special case, and this is the second consumer
  that proves it.
- **Zero server code changes.** No new dispatch protocol, no new catalog kind, no new `ActionKind`, no new
  admission gate in `SessionDispatchOrchestrator` (C-7 clean). `ContextBinder` is untouched.
- Project invariant 3 holds: the projection is the only coordinate system, and it is now the only thing
  the model sees.

The paragraph set therefore rides the declared-input channel, as the task requires, without a second
supply mechanism existing anywhere.

---

## 4. Catalog change (lands in the same change, never before)

Requiring `target_para_id` before the set is supplied would refuse every whole-document edit — strictly
worse than today. So `compose-revise-document.action.json` gains, in one commit with the supply:

- `inputSchema` declaring `documentText` (operand), `revisionIntent`, `instruction`, so the companions
  are authorized to reach the model. This also closes L5 — the schema now lives where the deploy script
  actually reads it.
- `target_para_id` on `edits[]` **and** `comments[]`.
- The TARGET IDENTITY prompt rule the three selection Actions already carry: echo the id **verbatim**,
  choose only from the ids present in the supplied text, never invent one.

---

## 5. Measured sizes (task step 3) — and what they decided

Measured over the real corpus (`tests/fixtures/compose-corpus/*.docx`), counting `<w:p>` and `<w:t>`:

| Paragraphs | Text chars | Annotated | Overhead | Side-list alternative | Document |
|---:|---:|---:|---:|---:|---|
| 108 | 23,727 | 24,915 | **+5.0%** | 11,340 | `PAT 109270W-1 — CLAIMS` |
| 55 | 3,552 | 4,157 | +17.0% | 5,775 | `AppligentNDA_Signed` |
| 23 | 2,707 | 2,960 | +9.3% | 2,415 | `line-numbered-pleading` |
| 12 | 2,209 | 2,341 | +6.0% | 1,260 | `Engagement Letter` |

The percentage rises on very small documents only because the denominator collapses — the absolute cost is
11 characters per paragraph everywhere. Extrapolated to a realistic worst case, a 40-page contract
(≈800 paragraphs / 120,000 chars): **annotated 128,800 chars, +7.3%**.

**This measurement is what settled the shape**, not just the correspondence argument in §2:

- A parallel `ParaReferenceMapEntry` side list costs **9.5× more per paragraph** at every size measured,
  and at 800 paragraphs reaches **84,000 chars against `ContextBinder`'s 32,768-char
  `MaxDeclaredCompanionChars`**. An oversize companion is **skipped and logged**, so that design would
  have handed the model a document with *no* closed set while the prompt asserted one was present —
  precisely the silent-incompleteness failure the task's second escalation trigger names. The annotated
  form cannot hit that cliff because it is the operand, not a companion.
- **No ceiling is introduced.** The file-operand path this replaces caps nothing (`SessionFileTextSource`
  has no truncation), so capping here would have been a new regression. `buildAnchoredDocumentText` never
  truncates; the Action's declared `maxLength` is advisory metadata, not enforced by the binder.

### C-4 (ADR-040 128 KB inline output cap) — measured, and not the binding constraint

`target_para_id` adds 28 bytes per item (`"target_para_id":"AAAA0001",`).

| Payload | Base | + anchors | Total | Verdict |
|---|---:|---:|---:|---|
| 50 edits at ~800 B (realistic) | 40,000 B | +1,400 B (**3.50%**) | **40.4 KB** | under the cap, ample headroom |
| 50 edits at the schema's 16,000-char maxima | 1,600,000 B | +1,400 B (0.09%) | 1,563.9 KB | over — **pre-existing** |

At realistic sizes the anchor consumes 3.5% of a payload sitting at 32% of the cap. At the schema's
declared maxima the payload breaches 128 KB by more than 10×, but it does so on `target_text`/`new_text`
alone — the exposure predates this task and the anchor contributes 0.09% of it. **Reported, not fixed:**
narrowing those maxima is a change to the whole-document Action's output contract that this task has no
evidence to size, and `ProjectComposeOutputs` skipping truncated entries is a Track-C-wide concern that
task 052's retirement pass is better placed to resolve. It remains genuinely unmeasured against a real
model response.

---

## 6. Known limitations of the supplied text (found by this task's own code review)

Recorded here rather than fixed, because each has a bounded and *safe* failure mode, and the obvious fix
for the first would breach a project invariant. Tasks 052 and 055 should read these.

**L-1 — hard breaks collapse.** `buildAnchoredDocumentText` uses `collectBlocks().text`, which is
ProseMirror's `node.textContent`; leaf nodes contribute nothing, so `<p>line one<br>line two</p>` reaches
the model as `line oneline two`. (`docxBridge.ts:498` explicitly maps `hardBreak → '\n'` in its own walk —
direct evidence the default does not.) The model may therefore quote a `target_text` for such a paragraph
that exists nowhere in the document. **Bounded**: `target_para_id` is the primary anchor now, so
*placement* is unaffected; only the text fallback degrades, and only for hard-break-bearing paragraphs.
**Not fixed** because doing it inside this module requires a second node walk — a second enumeration,
which invariant 3 forbids — and doing it in `collectBlocks` changes the shared fuzzy-anchor path that
`AnnotationReanchorService`'s client mirror depends on. A real fix belongs with whoever next owns
`collectBlocks`.

**L-2 — the provider-registration race is argued, not proven.** The dispatch reads the provider at
dispatch time. The named-intent effect fires when `activeComposeDocSessionId` backfills, which is after an
async byte load, whereas the provider registers during ComposeWorkspace's mount pass — so the provider
should already be there. That is an ordering argument, not a guarantee, and the e2e test registers the
provider first, so it covers the happy path only. **Bounded**: no provider ⇒ no `documentText` ⇒ the
pre-054 dispatch. Silent degradation to prose matching, never a wrong placement.

**L-3 — the `[XXXXXXXX]` prefix is not escaped.** A paragraph whose own text begins `[ABCD1234] ` is
indistinguishable from a real identifier prefix. Contrived for legal prose; noted for completeness.

---

## 7. Deployed-catalog defect found while measuring blast radius

Querying `sprk_analysisaction` in `spaarkedev1`: of 20 active Actions, 16 have `sprk_inputschema = null`
and 4 are populated. Two of the four — **`compose-draft-alternative`** and
**`compose-compare-to-playbook`** — have the **entire mirror file** stored, `$comment`/`actionCode`/
`environment` wrapper and all, rather than the inner schema object.

`GetDeclaredProperties` looks for `properties` at the root of that JSON, does not find it, and returns
`null`. A null declaration means `CollectDeclaredCompanions` yields nothing — so **task 051's
`targetParaId` companion would not have rendered in dev**, even though the code and the Action seed are
both correct.

Not caused by this task, and already fixed by the 051 change to `Deploy-AnalysisAction.ps1` (it writes
`$action.inputSchema`, the inner object). Recorded because it means **the Action seed deploy is a
prerequisite for observing either task's work**, and because it is a second instance of the same failure
mode: an authoring surface that no deploy path reads.
