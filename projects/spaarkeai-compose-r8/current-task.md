# Current Task State — `spaarkeai-compose-r8`

> **Last Updated**: 2026-08-24 (by `context-handoff`) · **Pushed through**: `5ea76633d`
> **Recovery**: read "Quick Recovery" first. Everything below is recoverable from files alone.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **051** — FR-C01/C02/C03 anchor supply (`tasks/051-anchor-supply.poml`), Phase 5 Track C |
| **Step** | **6 of 7 done** (0 rigor · 1 trace · 2–3 envelope+thread · 4 rebasing · 5 CitationResolver · 7 tests). Remaining: **the SUPPLY half of step 6 only** |
| **Status** | **in-progress — one open decision.** FR-C01 complete (five dispatch sites). FR-C02 complete server + client, live and tested. FR-C03's *validate* half complete; its *supply* half needs an owner decision — see 🔔 ESCALATION. |
| **Rigor / tier** | FULL · opus @ max (session model must be Opus — do not run this on a lower tier) |
| **Next Action** | **Answer the escalation below**, then implement the chosen supply channel and land the `compose-revise-document` catalog-DATA change WITH it — never before it (see the sequencing rule). |

---

## 🔔 ESCALATION — FR-C03 supply channel (the one open decision)

**Situation.** FR-C03 has two halves. The *validate* half is DONE: a model-returned paraId is checked for
membership in the closed set and refused loudly on a miss (`ComposeAnchorResolver` →
`EditErrorKind.UnknownParaId`; client `resolveAnchoredSpans` refuses an id absent from the live document).
The *supply* half — "supply the model an ENUMERATED CLOSED SET of paraIds" — has no home inside this task's
declared boundary.

**Why it is not just more typing.** The whole-document review pass is the `compose-revise-document` Action. Its
dispatch builds `args.slots` in **`src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx`**
(~1669) and sends only `{ revisionIntent, instruction? }` — the document text reaches the model through the
grounding layer, not through a slot. The paraId map lives in the **Compose** pane. So the closed set has to cross
a pane and a package boundary. Every candidate home is fenced:

| Candidate | Blocked by |
|---|---|
| Inject server-side at dispatch admission | **C-7** — "not as a new admission gate in `SessionDispatchOrchestrator`" |
| A fourth operand-vocabulary entry | **C-1** — closed, hardcoded three-name vocabulary; adding one converts Path C → Path B (a spine change) |
| Build the set in `ConversationPane.tsx` | **SpaarkeAi is a hot path** (CLAUDE.md §17) — needs `/conflict-check` and NFR-05 co-deploy, and is outside task 051's declared `<outputs>` (CLAUDE.md §6 "scope expansion beyond task boundaries") |
| Toolbar-only supply — thread `paraIdMap` into `ComposeAiToolbar`, add `paragraphs` to slots when `revisionScope === 'whole-document'` | Feasible and Compose-owned (~1 prop + ~10 lines), but covers only the toolbar entry, NOT the ConversationPane "Revise document" entry that the code comments call the primary one |

**Recommendation — toolbar supply here, ConversationPane supply as a scoped follow-on.** Do the Compose-owned
toolbar half inside task 051 (small, inside the boundary); file the ConversationPane half as its own task with a
`/conflict-check` + co-deploy gate. That keeps 051 inside its outputs and still moves FR-C03 forward.

**⚠️ Sequencing rule that must not be broken.** The catalog-DATA change to
`infra/dataverse/actions/compose-revise-document.action.json` (require `target_para_id` on `edits[]` /
`comments[]`; rewrite the INPUT + OUTPUT CONTRACT sections of the systemPrompt) is **deliberately NOT made yet**.
C-7 sanctions it as catalog DATA, but landing it before the supply exists asks the model for a paraId it was
never given — every edit would then be refused, which is **worse than today**. Catalog change and supply must
land together, covering every entry path that dispatches this Action.

---

### Files Modified This Session (all COMMITTED + PUSHED — clean tree)

| File | Purpose |
|---|---|
| `Services/Compose/ComposeAnchorResolver.cs` | **NEW** — the ONE place an edit's target becomes a paraId. Membership-validates `target_para_id` (FR-C03); resolves `target_ref` through `CitationResolver` (FR-C02). No text-search branch exists to fall into. |
| `Services/Compose/ComposeEditAnchorPass.cs` | **NEW** — anchor-first batch ordering. Anchored edits are kept OUT of the text validator's *input*, because it indexes verdicts by position — ignoring its output would still run `FindAll` over the whole document. |
| `Services/Compose/ComposeEditModels.cs` | `ProposedEdit.target_para_id` + `.target_ref`; `EditVerdict.resolvedParaId`; request `referenceMap`; five anchor `EditErrorKind` values |
| `Api/ComposeEndpoints.cs` | `ValidateEditBatch` now calls the anchor pass |
| `tests/integration/seam/Compose/ComposeEditAnchorPassSeamTests.cs` | **NEW** — 13 seam tests: real corpus → projection → anchor → verdict |
| `.../hooks/usePendingRedline.ts` | `resolveAnchoredSpans` + anchor-first ordering on BOTH the single-edit and change-list legs; banner names the anchor when there is no `target_text` to quote |
| `.../hooks/usePendingRedline.anchor.test.tsx` | **NEW** — 14 client tests |
| `.../widgets/ComposeEditor.tsx` | `ComposeDraftPayload`/`ComposeDraftEdit` anchor fields; `usePendingRedline(editor, paraIdMap)`; caret path (5th dispatch site) wired |

### Commits this session

```
5ea76633d task 051 — close the fifth dispatch site (caret path) on the FR-C01 anchor
6e663be1c task 051 FR-C02 — a citation now resolves an edit's target; CitationResolver gets its first consumer
```

### Critical Context

**The same shape has now appeared four times in a row: 046 (hardBreak), 048 (atoms), 051 FR-C01 (bookmark),
051 FR-C02 (`CitationResolver`).** In each case the machinery existed, was tested, and had no production
consumer. **Look for a dark implementation BEFORE building** — it has been the answer every time.

**`CitationResolver` now has a real consumer** (acceptance criterion, verified):
`grep -rn "CitationResolver" src --include=*.cs` → `ComposeAnchorResolver.cs:60` and `:74` are call sites, not
doc comments.

**The negative is enforced structurally, not by inspecting output.** The seam test hands the anchor pass a
validator that THROWS if called, and a companion test proves that same validator IS still reached for
un-anchored edits — so the tripwire is a real constraint, not an unreachable branch.

**A `BlockInfo`'s `from`/`to` are NODE boundaries, not content.** A redline needs `from+1 .. to-1`, or the
replacement lands *after* the paragraph instead of within it. Cost one debugging cycle; commented at `spanOf`.

**`usePendingRedline.ts` contains a literal NUL byte** (its text-index sentinel), so `grep` treats the file as
binary — use `grep -a`.

**Nothing has been deleted or retired.** `target_text`, `match_mode`, `ComposeEditValidator`, `FindAll` and the
client `resolveTargetSpans` are all still present and still the fallback for un-anchored edits. Task 052 owns
retirement and MUST NOT run until FR-C03's supply half lands — the escalation above is its gate.

**C-4 is still unchecked** (carried forward): ADR-040 caps inline payloads at 128 KB and
`ChatEndpoints.ProjectComposeOutputs` SKIPS truncated entries. Anchors now ride every edit entry, and FR-C03's
closed set would ride every review request. Measure the whole-document worst case or state the degradation.

---

## Task 051 — decisions made (carry these forward)

1. **Wire, don't rebuild.** The two R4 controllers are the FR-C01 implementation; only the mount was missing.
   Invariant (6) holds by construction — the bookmark rebases on the editor's own ProseMirror `Mapping` (the
   op-log's primitive) and applied ops go through a normal `chain()`, so the step interceptor captures them into
   the same log as user keystrokes. **No parallel rebaser was written**, which acceptance criterion 5 requires.
2. **`beginGenerate({ range })` instead of a second mechanism.** The review-note tools anchor on a note's clause
   span, not the caret. One additive parameter reuses all downstream behaviour. The batch path calls
   `dispatchNoteToolRequest`, so both note sources were covered by the single change.
3. **Reanchor options deliberately omitted** from `useAiApplyValidation`. Without them `canReanchor` is false and
   an unvalidatable op surfaces with NO fuzzy hint — the more conservative branch. The hint is presentation only
   and never a placement (the hook's own SCOPE DECISION), so nothing is lost and no service dependency is added.

## Task 051 — findings that bind the REST of Track C

**F-1 / F-2 — the task-050 assessment's C-1 is half right.** It says the client already sends
`selectionAnchorStart` / `selectionAnchorEnd` / `targetParaId`. The first two are unconditional; **`targetParaId`
was behind `useBookmark` and never shipped**. Now fixed. Cite the corrected version, not C-1 as written.

**F-3 — ESCALATION TRIGGER 1 FIRED AND IS NOW CLOSED.** Four dispatch sites build edit slots, not one:

| # | Site | Durable identity | Status |
|---|---|---|---|
| 1 | `ComposeAiToolbar.handleActionClick` (selection) | bookmark paraId | ✅ wired (part 1) |
| 2 | `ComposeEditor.dispatchNoteToolRequest` (review-note, single) | comment `threadId` → `findCommentAnchorRange` | ✅ wired (part 1) |
| 3 | `ComposeEditor.runBatchNoteToolAsync` (batch) | same — reuses site 2 | ✅ covered |
| 4 | Context-pane bridge → `enqueueComposeAction` | inherits from 1–3 | ✅ inherits |
| 5 | `ComposeEditor.runDescribeChangeAtCaret` (Ctrl+Space, UC-5) | caret's enclosing textblock | ✅ wired (`5ea76633d`) — **found AFTER part 1 closed the trigger; there were five sites, not four** |

**Task 052 may now retire the search path only after FR-C02/C03 also land** — a citation-driven or
review-pass edit with no anchor would still depend on it.

## Task 051 — remaining work, with its binding constraints

- ~~**FR-C02**~~ — ✅ DONE (`6e663be1c`). `ComposeAnchorResolver` is the consumer; server + client both resolve a
  citation deterministically, and both refuse rather than degrade to a search.
- **FR-C03** — *validate* half ✅ DONE (membership check + loud refusal, server and client). *Supply* half is the
  open decision — see the 🔔 ESCALATION at the top of this file. It MUST land in the same PR as the
  `compose-revise-document` catalog-DATA change.
- ~~**Tests**~~ — ✅ DONE. `ComposeEditAnchorPassSeamTests.cs` is the C-2 vertical slice (real corpus →
  projection → anchor → verdict, 13 tests); `usePendingRedline.anchor.test.tsx` is its client mirror (14). The
  "zero text matching invoked" criterion is enforced by a throwing validator, not by output inspection.
- **C-4 unchecked** — ADR-040 caps inline payloads at 128 KB and `ChatEndpoints.ProjectComposeOutputs` SKIPS
  truncated entries entirely. Anchors now ride every edit entry; **measure the whole-document-revise worst case
  and confirm headroom, or state the degradation.** Not yet done.
- **C-1** — anchors MUST ride as `args.slots.*`; adding a fourth `OperandVocabulary` entry converts Path C into
  Path B (a spine change). **C-3** — never add `body_html` to an edit payload. **C-6** — do not touch the
  supersession leg. **C-7** — closed-set validation belongs in the Compose-owned path, not `SessionDispatchOrchestrator`.

### The one thing to understand

**Untouched blocks are preserved; the edited block is mostly preserved; a PDF now reopens as the document
it became; nothing is deployed.**

| | Control (master) | Now |
|---|---:|---:|
| Untouched-block preservation, lenient | 18.08% | **100.00%** (18/18 docs) |
| Near tier | 6.67% | **100%** (14/14 measurable) |
| Strict | 12.18% | **100% on 16 of 18** |
| **Edited block intact** | — | **12 of 18 documents** |
| **PDF → 2 documents after a refresh** | yes | **no** (1 item, 1 row, both edits) |

---

## What shipped in 044 (2026-08-23)

**FR-A09 — measured first, and the diagnosis moved.** The POML said a PDF's second save after a refresh
"falls back to a full rebuild". What actually happens is worse: re-opening projects the PDF *again*, so the
user's saved work is **invisible** in a Word document they have no pointer to, and their next save mints a
**duplicate** (the transient key is per-mount and never persisted). Fixed at **load** — resume on the
document that already exists — which makes save two an ordinary imported save that resolves and clones.

The cheap save-side fix (stable transient key → dedup onto the existing doc) was **rejected**: after a
refresh the client's model is the fresh PDF projection, so rendering it into the existing document
overwrites the first save's edit. It trades a visible duplicate for silent data loss.

**Mechanism**: two `IDistributedCache` keys (ADR-009) — `pdf-session:{sessionId}` carries the server's own
bytes-first PDF determination from load to save; `pdf-derived:{driveId}:{speId}` records what that PDF
became. Both best-effort; a miss degrades to the old behavior, never to a failure. **No version id is
stored** — deliberately; the resumed load re-reads the current version, and a creation-time version id
would be read-never and stale. That deviation from the requirement's literal wording is recorded, not
buried.

**FR-A08 was not fully done when it was reported done.** Its first acceptance criterion — enumerate every
creation path and stamp it correctly — was skipped, and PDF-sourced rows were being stamped `Imported`
(measured: `100000001`). The suppression FR-A08 built reads that marker, so **it could not fire for the
class the requirement names first**. Now split: `origin` stays Imported-biased for routing (never forces an
imported doc onto the clean branch — the SEV-1 shape), `originToPersist` is what the document *is*.

---

## What happened to 043 (2026-08-23) — SUPERSEDED

FR-A07 assumes constructs exist the merge cannot safely carry. **There are none.** Owner directed closing
the corpus gap first: four new fixtures now cover OLE objects, chart parts, endnotes and embedded fonts
(five of the six families that had ZERO coverage; macros excluded with reasons). Results:

| | Untouched block | The construct's OWN block edited |
|---|---|---|
| OLE object · chart · endnote · font | **100% strict**, cloned byte-verbatim | dropped, but **NAMED** every time; saved doc schema-valid; package part survives |

Loss is **per-edited-block, never per-document** — so a gate keyed on construct presence would refuse
editing on documents we handle at 100%, a false positive by construction. And **"Edit a copy" has no
trigger to attach to**: every read-only trigger is "we cannot read this at all", and the one genuine
read-but-never-write case (the PDF) already ships.
[`notes/capability-gate-triggers.md`](notes/capability-gate-triggers.md)

---

## Open items carried to task 045

1. **The untested FR-A08 criterion** — *"an Authored document STILL receives save-outcome warnings"* has no
   end-to-end test. Two levers were tried; neither fired through the wire. The property holds structurally
   (every save-outcome warning is constructed after the provenance capture) but that is an argument from the
   code, not evidence from a run.
2. **Browse/local-file PDF door** stamps `Imported` — `/api/compose/project` is contracted stateless and a
   local file has no server identity to key on. First save shows one false warning; the second is correct.
   [`notes/document-creation-paths.md`](notes/document-creation-paths.md) path 4.
3. **Foreign session id on a save** would read another document's PDF marker. A client-contract violation,
   but it is the SEV-1 direction, so it is written down rather than assumed away. Needs a server-side
   binding check at save time if it is to be closed.
4. **A redirected open wastes one PDF download** — detection is bytes-first so the mapping can only be
   consulted after the fetch. The filename pre-check was rejected: it needs a second call site every test
   would leave unexercised.
5. **`ComposeService.cs` is now 4,373 lines** (was 4,031). Track D's file. The PDF-provenance code is one
   self-contained region depending only on `_cache`/`_logger`/`_spe` — extraction is mechanical, same shape
   as `ComposeBlockMerge.cs`.
6. **043 residual — the warning arrives at SAVE, after the edit.** Version history means nothing is
   unrecoverable, but the consent is after the fact. The evidence-supported version of FR-A07 is a warning
   **at the edit**, on the specific block, reusing 044's taxonomy — far smaller than the document gate the
   POML described. Also open: whether Compose should accept `.docm` at all (a product question, not a merge
   one; a `vbaProject.bin` cannot be fixtured as a `.docx`).
7. **`w:br` soft breaks** (1 doc) and **run-level `rPr` variation** (2 docs) on the edited block — read-side
   projection gaps base-carry cannot reach. **`mc:AlternateContent` paraId re-mint** — 2 docs below 100%
   strict (the reverted experiment).

---

## Experiments implemented, measured, and REVERTED (do not repeat)

| Change | Looked right because | Measured |
|---|---|---|
| Exclude opaque regions from `AssignParaIds` | Stops mutating cloned `mc:AlternateContent` subtrees | 2 docs to 100% strict — but breaks task 011's paraId-uniqueness guarantee. **Strict is a ratchet, not a gate**; trading a safety invariant for a non-gating number is what the ADR-049 paired MUST forbids. |
| Emit `xml:space="preserve"` only when needed | Matches what Word "should" do | **Markedly worse**: intact fell 12 → 2. Word emits it far more liberally. |

---

## Traps (all live)

- **`w:pPr` / `w:rPr` are `xsd:sequence`** — child ORDER is schema. Use the order tables in `ComposeBlockMerge`.
- **The I-7 source audit scans source TEXT including comments.** A comment containing the banned membership
  call trips it. Move the code, not the guard.
- **`mergeUnchangedBlocks` is a TEST SEAM, not a feature flag.** Three seam tests are pinned to `false`.
- **A test double that never remembers measures the double, not the behavior** — the promotion's idempotency
  looks rows up by `sprk_graphitemid`; a double answering "no such row" forever reports a false duplicate.
- **Corpus fixtures are held schema-valid.** Never "fix" a real-world fixture — their quirks are the test case.
- **Compose.Components uses Jest, not vitest.**
- **Run the FULL test project before closing a task**, never `--filter`.
- Bash heredocs mangle escapes inside quoted Python — write patch scripts to the scratchpad and run them.
- `git checkout <commit> -- <path>` writes the **INDEX**; the safe A/B form is `git show <commit>:<path> > <path>`.
- `w14:paraId` must be **8 hex digits, non-zero, ≤ `0x7FFFFFFF`**.
- **The pre-commit hook fails on `prettier` not being on PATH** (root install gap) — which is why CI keeps
  landing auto-format commits. `dotnet format <csproj> --include <files>` and
  `npx --no-install prettier --check` from the package dir both work; run them and commit the result.
- **`dotnet format` needs a csproj/sln path** — a bare `--include` throws `MSBuildWorkspaceFinder`.
- SpaarkeAi is Vite and aliases shared-lib SOURCE — clear `dist/ node_modules/.vite/ .vite/` before building.
- Use `pwsh`, not `powershell`, for the deploy hash-verify step.

---

## Owner-visible banners

| Banner | Track | Status |
|---|---|---|
| "Some formatting was simplified when saving" | A | **Should now be gone** for documents that lose nothing, **and for PDF-sourced documents** — but **undeployed** |
| "wording differs slightly from this document" | **C** | Untouched. **051–053**, independent of everything above |

## Not deployed

**Nothing from Phases 3–4 is live.** Task 017's banner fix, the merge, the carry, the taxonomy and 044's
PDF work all ship with the next paired **BFF + `sprk_spaarkeai`** deploy (NFR-05). Never build from a net8 tree.

## Tasks complete

**Phase 0** 001 · 002 — **Phase 1 (Track S)** 010–018 — **Phase 2** 020 · 021 · 022 · 023 —
**Phase 3** 030 · 031 — **Phase 4** 040 · 041 · 042 · **044** (043 superseded) — **Phase 5** 050

**Blocked**: 074 ⛔ (`ComposeShadowPatchEngine` subsumption NOT-CONFIRMED — gate-decision §5).

## Evidence trail

`projects/spaarkeai-compose-r8/notes/` — `gate-contract.md` · `control-measurement.md` ·
`merge-prototype-results.md` · `gate-decision.md` · `merge-mechanism-results.md` · `edited-block-loss.md` ·
`merge-integrity-results.md` · **`pdf-refresh-baseline.md`** · **`document-creation-paths.md`** ·
`adr-049-third-amendment-draft.md` · `track-s-uat.md` · `honest-failure-set.md` · `document-size-ceilings.md`
