# Current Task State — `spaarkeai-compose-r8`

> **Last Updated**: 2026-08-24 (task 051 IN PROGRESS — discovery complete) · **Pushed**: through `7069717bd`
> **Recovery**: read "Quick Recovery" first. Everything below is recoverable from files alone.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Active task** | **051** — Track C anchor supply. Status: **in-progress**, Step 1 (trace) COMPLETE. |
| **Next action** | Implement the wiring named in "Task 051 findings" below: pass `aiGenerateBookmark` + `aiApplyValidation` to BOTH `<ComposeAiToolbar>` mounts in `ComposeEditor.tsx` (lines ~3486, ~3744), then cover the note-tool sources. |
| **Phases 1–3** | ✅ COMPLETE. Architecture gate **PASSED**. ADR-049 third amendment **APPLIED**. |
| **Phase 4** | 040 ✅ · 041 ✅ · 042 ✅ · 044 ✅ · 043 ⊘ · 045 🔲 · **046 ✅ · 047 ✅ · 048 ✅** (zero-loss follow-ons) |
| **Gate status** | Server **11,060 / 0** · Client (Jest) **1,140 / 0** · NetArchTest **36/36** · publish **44.99 MB incl PDBs** (+0.03 vs the 44.96 net10 baseline; ceiling 60) |

---

## Task 051 findings (Step 1 trace — READ BEFORE IMPLEMENTING)

**F-1 — the FR-C01 machinery EXISTS and is UNWIRED.** `useAiGenerateBookmark` (R4 task 040) +
`useAiApplyValidation` (R4 task 041) already implement exactly what FR-C01 specifies: a request-scoped
bookmark at the live selection, **rebased through concurrent edits with the SAME ProseMirror `Mapping`
primitive the op-log uses** (`RebasedOperationLog.recordTransaction`) — which is project invariant (6)
satisfied by construction — resolving a durable `w14:paraId` via `resolveRunAnchor`, with the model
returning JSON operations referencing paraId rather than free text (I-7).

Both are built, tested (3 test files), and exported from the barrel. **Neither has a production
consumer.** Both `<ComposeAiToolbar>` mounts in `ComposeEditor.tsx` (~3486 BubbleMenu-less, ~3744
BubbleMenu) omit `aiGenerateBookmark` AND `aiApplyValidation`. Therefore:

```ts
const useBookmark = !!aiGenerateBookmark && action.materializesInEditor === true;  // ALWAYS false in prod
...(useBookmark && bookmarkContext?.paraId ? { targetParaId: bookmarkContext.paraId } : {}),  // never sent
```

**F-2 — the task-050 assessment's C-1 is half right.** It states the client "already sends
`selectionAnchorStart`/`selectionAnchorEnd`/`targetParaId` in `args.slots`, so FR-C01's request half is
already on the wire". The first two ARE unconditional; **`targetParaId` is behind `useBookmark` and is
therefore never sent in production.** The durable anchor is NOT on the wire — only raw ProseMirror
positions, which are session-local and drift, which is precisely why 040 built the bookmark.

**F-3 — ESCALATION TRIGGER FIRED (POML trigger 1: "a fourth anchor source exists").** There are FOUR
dispatch sites building edit slots, not one:

| # | Site | Durable identity available | Sent? |
|---|---|---|---|
| 1 | `ComposeAiToolbar.handleActionClick` (selection) | bookmark paraId | ❌ unwired |
| 2 | `ComposeEditor.dispatchNoteToolRequest` (review-note, single) | **the comment `threadId`** via `findCommentAnchorRange` | ❌ flattened to raw PM offsets |
| 3 | `ComposeEditor.runBatchNoteToolAsync` (review-note, batch) | same | ❌ same |
| 4 | Context-pane bridge → `enqueueComposeAction` | inherits from 1–3 | — |

Sites 2/3 already RESOLVE a durable anchor (`findCommentAnchorRange(doc, threadId)`) and then throw it
away, keeping only `selectionAnchorStart/End`. **Task 052 MUST NOT retire the text-search path until
sites 2 and 3 carry an anchor too** — they are the uncovered source the trigger names.

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
