# Current Task State — `spaarkeai-compose-r8`

> **Last Updated**: 2026-08-23 (task 044 complete) · **Pushed**: see Quick Recovery
> **Recovery**: read "Quick Recovery" first. Everything below is recoverable from files alone.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Active task** | **none** — 044 closed. Pick 043 or 051 below. |
| **Phases 1–3** | ✅ COMPLETE. Architecture gate **PASSED**. ADR-049 third amendment **APPLIED**. |
| **Phase 4** | 040 ✅ · 041 ✅ · 042 ✅ · **044 ✅** · 043 🔲 not started · 045 🔲 |
| **Next** | **043** (capability gate → read-only + "Edit a copy") or **051** (Track C — the *"wording differs slightly"* banner, independent of all Track A work) |
| **Gate status** | Server **10,920 / 0** · Client (Jest) **1,129 / 0** · NetArchTest **36/36** · publish **44.99 MB incl PDBs / 44.09 excl** (+0.03 vs the 44.96 documented net10 baseline; ceiling 60) · no vulnerable packages |

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
6. **`w:br` soft breaks** (1 doc) and **run-level `rPr` variation** (2 docs) on the edited block — read-side
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
**Phase 3** 030 · 031 — **Phase 4** 040 · 041 · 042 · **044** — **Phase 5** 050

**Blocked**: 074 ⛔ (`ComposeShadowPatchEngine` subsumption NOT-CONFIRMED — gate-decision §5).

## Evidence trail

`projects/spaarkeai-compose-r8/notes/` — `gate-contract.md` · `control-measurement.md` ·
`merge-prototype-results.md` · `gate-decision.md` · `merge-mechanism-results.md` · `edited-block-loss.md` ·
`merge-integrity-results.md` · **`pdf-refresh-baseline.md`** · **`document-creation-paths.md`** ·
`adr-049-third-amendment-draft.md` · `track-s-uat.md` · `honest-failure-set.md` · `document-size-ceilings.md`
