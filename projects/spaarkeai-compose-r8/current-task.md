# Current Task State — `spaarkeai-compose-r8`

> **Last Updated**: 2026-08-24 (by `context-handoff`) · **Pushed**: `d6cccc87c` + a formatting commit
> **Recovery**: read "Quick Recovery" first. Everything below is recoverable from files alone.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **051** — FR-C01/C02/C03 anchor supply (`tasks/051-anchor-supply.poml`), Phase 5 Track C |
| **Step** | **4 of 7 done** (0 rigor · 1 trace · 2–3 envelope+thread · 4 rebasing). Remaining: 5 (FR-C02 `CitationResolver`) · 6 (FR-C03 closed set) · 7 (tests) |
| **Status** | **in-progress** — FR-C01 COMPLETE and committed; FR-C02 + FR-C03 not started |
| **Rigor / tier** | FULL · opus @ max (session model must be Opus — do not run this on a lower tier) |
| **Next Action** | Start **FR-C02**: give `src/server/api/Sprk.Bff.Api/Services/Compose/CitationResolver.cs` a real BFF consumer so a reference-driven target ("clause 4.2") resolves through the numbering engine with no text search. Verify with `grep -rn "CitationResolver" src --include=*.cs` — today every hit is inside its own file. |

### Files Modified This Session (all COMMITTED + PUSHED — clean tree)

| File | Purpose |
|---|---|
| `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeEditor.tsx` | Instantiate `useAiGenerateBookmark` + `useAiApplyValidation`; pass both to BOTH `<ComposeAiToolbar>` mounts; anchor + return-handling for the review-note dispatch |
| `src/client/shared/Spaarke.Compose.Components/src/widgets/hooks/useAiGenerateBookmark.ts` | `beginGenerate` accepts an optional explicit `range` (note-clause anchoring) |
| `projects/spaarkeai-compose-r8/current-task.md` | This checkpoint |

### Critical Context

**FR-C01's machinery already existed and was dark.** R4 tasks 040/041 built the bookmark + validate-before-apply
path — tested, exported, and never given a production consumer, so `useBookmark` was permanently false and every
AI edit text-searched. Task 051 part 1 is the wiring, not new machinery. **Nothing has been deleted or retired**
(`target_text` / `match_mode` / `ComposeEditValidator` all still present) — task 052 owns retirement.

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

**Task 052 may now retire the search path only after FR-C02/C03 also land** — a citation-driven or
review-pass edit with no anchor would still depend on it.

## Task 051 — remaining work, with its binding constraints

- **FR-C02** — wire `CitationResolver`; acceptance requires it to have a real BFF consumer outside its own file
  (verified by grep).
- **FR-C03** — enumerate a **closed** paraId set for review passes server-side, supply it to the model, validate
  the returned id against it, **reject loudly**; MUST NOT fall back to searching.
- **Tests** — **C-2 makes a `tests/integration/seam/Compose/**` vertical-slice test the definition of done**
  ("a green contract-shape test is NOT sufficient"). Nearest neighbours: `ComposeParaOffsetAnchorSeamTests.cs`,
  `ComposeReferenceMapSessionLedgerSeamTests.cs`. Plus the acceptance test that a selection-driven edit places
  with **zero text matching invoked**.
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
